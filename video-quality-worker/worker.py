"""Private batch evaluator called by the .NET video-quality queue consumer."""
from __future__ import annotations

import base64
import hmac
import importlib
import json
import logging
import math
import os
import sys
import tempfile
import threading
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

LOG = logging.getLogger("video-quality")
DIMENSIONS = ("subject_consistency", "background_consistency", "motion_smoothness", "aesthetic_quality", "imaging_quality")
TOKEN = os.environ.get("VIDEO_QA_TOKEN", "")
ALLOWED_HOSTS = {item.strip().lower() for item in os.environ.get("VIDEO_QA_CLIP_HOSTS", "api.cloudinary.com,res.cloudinary.com").split(",") if item.strip()}
MAX_REQUEST = 128 * 1024 * 1024
MAX_CLIP = 100 * 1024 * 1024
LOCK = threading.Lock()
OCR = None

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "video-quality"))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise ValueError("Redirects are not allowed for clip downloads")


def download(url: str, destination: Path) -> None:
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or parsed.hostname not in ALLOWED_HOSTS or parsed.username or parsed.password:
        raise ValueError("Clip URL is not on an allowed HTTPS host")
    request = urllib.request.Request(url, headers={"User-Agent": "APCS-Video-QA/1"})
    with urllib.request.build_opener(NoRedirect).open(request, timeout=120) as response, destination.open("wb") as output:
        total = 0
        while chunk := response.read(1024 * 1024):
            total += len(chunk)
            if total > MAX_CLIP:
                raise ValueError("Clip exceeds size limit")
            output.write(chunk)


def evaluate_ocr(video: Path, source: Path, temporary: Path, reference: Path | None = None) -> dict:
    global OCR
    import run_paddle_ocr as adapter
    if OCR is None:
        try:
            OCR = adapter.PaddleOCR(lang="en", use_doc_orientation_classify=False, use_doc_unwarping=False,
                                    use_textline_orientation=False, enable_mkldnn=False)
        except TypeError:
            OCR = adapter.PaddleOCR(lang="en", use_angle_cls=True, enable_mkldnn=False)
    return adapter.evaluate_ocr(OCR, str(video), str(source), temporary, str(reference) if reference else None)


def evaluate_vbench(scenes: list[dict], directory: Path) -> None:
    import torch
    from vbench.utils import init_submodules
    device = torch.device(os.environ.get("VIDEO_QA_DEVICE", "cuda" if torch.cuda.is_available() else "cpu"))
    for dimension in DIMENSIONS:
        try:
            submodules = init_submodules([dimension], local=False, read_frame=False)
            compute = getattr(importlib.import_module(f"vbench.{dimension}"), f"compute_{dimension}")
            def run_batch(batch: list[dict]) -> None:
                info = directory / "info.json"
                info.write_text(json.dumps([{"prompt_en": f"scene_{s['sceneOrder']}", "dimension": [dimension],
                                             "video_list": [str(s['video'])]} for s in batch]), encoding="utf-8")
                _, per_video = compute(str(info), device, submodules[dimension], imaging_quality_preprocessing_mode="longer")
                by_path = {str(Path(item["video_path"]).resolve()): item["video_results"] for item in per_video}
                for scene in batch:
                    score = by_path.get(str(scene["video"].resolve()))
                    if score is None or not math.isfinite(float(score)):
                        scene["vbench"]["dimensions"][dimension] = {"status": "failed", "detail": "No finite score returned."}
                    else:
                        # VBench's per-video MUSIQ score uses 0..100; the other dimensions use 0..1.
                        score = float(score) / 100 if dimension == "imaging_quality" else float(score)
                        scene["vbench"][f"{dimension}Score"] = score
                        scene["vbench"]["dimensions"][dimension] = {"status": "completed"}
            try:
                run_batch(scenes)
            except Exception:
                LOG.exception("VBench batch %s failed; isolating scenes", dimension)
                for scene in scenes:
                    try:
                        run_batch([scene])
                    except Exception:
                        LOG.exception("VBench %s failed for scene %s", dimension, scene["sceneOrder"])
                        scene["vbench"]["dimensions"][dimension] = {"status": "failed", "detail": "Evaluator failed; see worker logs."}
        except Exception:
            LOG.exception("VBench dimension %s failed", dimension)
            for scene in scenes:
                scene["vbench"]["dimensions"][dimension] = {"status": "failed", "detail": "Evaluator failed; see worker logs."}


def evaluate_input_fidelity(scenes: list[dict], directory: Path) -> None:
    import torch
    import input_fidelity as adapter
    device = torch.device(os.environ.get("VIDEO_QA_DEVICE", "cuda" if torch.cuda.is_available() else "cpu"))
    prepared = []
    for scene in scenes:
        metrics = scene["fidelity"] = {"i2vSubject": {"status": "skipped", "reason": "Input crop information is unavailable."},
                                        "artwork": {"status": "skipped", "reason": "Input crop information is unavailable."}}
        if not scene.get("geometry"):
            continue
        try:
            source, region = adapter.prepare_start_frame(scene["source"], scene["geometry"], directory / f"input_{scene['sceneOrder']}.png")
            prepared.append((scene, source, region))
            metrics["artwork"] = {"status": "skipped", "reason": "No fully visible artwork region is recorded on the start image."}
        except Exception:
            LOG.exception("Could not reconstruct input frame for scene %s", scene["sceneOrder"])
            metrics["i2vSubject"] = {"status": "failed", "reason": "Could not prepare the input frame."}
            metrics["artwork"] = {"status": "skipped", "reason": "Input frame is unavailable."}
    if not prepared:
        return
    try:
        from vbench2_beta_i2v.i2v_subject import i2v_subject
        model = torch.hub.load("facebookresearch/dino:main", "dino_vitb16", trust_repo=True).to(device).eval()
        try:
            for scene, source, _ in prepared:
                try:
                    with torch.inference_mode():
                        _, result = i2v_subject(model, [(str(source), str(scene["video"]))], device)
                    score = float(result[0]["video_results"])
                    if not math.isfinite(score):
                        raise ValueError("Non-finite score")
                    scene["fidelity"]["i2vSubject"] = {"status": "completed", "score": score}
                except Exception:
                    LOG.exception("I2V subject failed for scene %s", scene["sceneOrder"])
                    scene["fidelity"]["i2vSubject"] = {"status": "failed", "reason": "Image-to-video evaluation failed; see worker logs."}
        finally:
            del model
            if device.type == "cuda": torch.cuda.empty_cache()
    except Exception:
        LOG.exception("VBench-I2V unavailable")
        for scene, _, _ in prepared:
            scene["fidelity"]["i2vSubject"] = {"status": "failed", "reason": "VBench-I2V is unavailable; see worker logs."}
    artwork_scenes = [(scene, source, region) for scene, source, region in prepared if region is not None]
    if artwork_scenes:
        try:
            model = torch.hub.load("facebookresearch/dinov2", "dinov2_vits14", trust_repo=True).to(device).eval()
            try:
                for scene, source, region in artwork_scenes:
                    try:
                        scene["fidelity"]["artwork"] = adapter.artwork_similarity(model, device, source, scene["video"], region)
                    except Exception:
                        LOG.exception("Artwork evaluation failed for scene %s", scene["sceneOrder"])
                        scene["fidelity"]["artwork"] = {"status": "failed", "reason": "Artwork evaluation failed; see worker logs."}
            finally:
                del model
                if device.type == "cuda": torch.cuda.empty_cache()
        except Exception:
            LOG.exception("DINOv2 unavailable")
            for scene, _, _ in artwork_scenes:
                scene["fidelity"]["artwork"] = {"status": "failed", "reason": "DINOv2 is unavailable; see worker logs."}


def evaluate(payload: dict) -> dict:
    items = payload.get("scenes")
    if not isinstance(items, list) or not 1 <= len(items) <= 20:
        raise ValueError("scenes must contain 1 to 20 entries")
    if len({item.get("sceneOrder") for item in items}) != len(items):
        raise ValueError("scene orders must be unique")
    results = []
    with tempfile.TemporaryDirectory(prefix="apcs-video-qa-") as temp:
        directory = Path(temp)
        scenes = []
        for index, item in enumerate(items):
            result = {"sceneOrder": item["sceneOrder"], "status": "failed", "evaluatorVersion": "video-quality-batch-v1", "metrics": {}}
            results.append(result)
            try:
                video, source = directory / f"scene_{index}.mp4", directory / f"source_{index}.png"
                data = base64.b64decode(item["sourceImage"], validate=True)
                if not data or len(data) > 20 * 1024 * 1024:
                    raise ValueError("Invalid source image size")
                source.write_bytes(data)
                reference = None
                if item.get("referenceMockup"):
                    reference_data = base64.b64decode(item["referenceMockup"], validate=True)
                    if not reference_data or len(reference_data) > 20 * 1024 * 1024:
                        raise ValueError("Invalid reference image size")
                    reference = directory / f"reference_{index}.png"
                    reference.write_bytes(reference_data)
                download(item["clipUrl"], video)
                scenes.append({"sceneOrder": item["sceneOrder"], "video": video, "source": source, "reference": reference, "geometry": item.get("inputGeometry"),
                               "vbench": {"dimensions": {}}, "result": result})
            except Exception:
                LOG.exception("Could not prepare scene %s", item.get("sceneOrder"))
                result["detail"] = "Could not prepare scene; see worker logs."
        if scenes:
            try:
                evaluate_vbench(scenes, directory)
            except Exception:
                LOG.exception("Could not initialize VBench")
                for scene in scenes:
                    scene["vbench"]["dimensions"] = {name: {"status": "failed", "detail": "VBench unavailable."} for name in DIMENSIONS}
            try:
                evaluate_input_fidelity(scenes, directory)
            except Exception:
                LOG.exception("Input fidelity evaluator unavailable")
                for scene in scenes:
                    scene["fidelity"] = {"i2vSubject": {"status": "failed", "reason": "Input fidelity evaluator failed; see worker logs."},
                                         "artwork": {"status": "skipped", "reason": "Input fidelity evaluator unavailable."}}
            for scene in scenes:
                result, vbench = scene["result"], scene["vbench"]
                count = sum(entry["status"] == "completed" for entry in vbench["dimensions"].values())
                vbench["completedDimensionCount"] = count
                vbench["status"] = "completed" if count == len(DIMENSIONS) else "uncertain" if count else "failed"
                try:
                    ocr_dir = directory / f"ocr_{scene['sceneOrder']}"
                    ocr_dir.mkdir()
                    ocr = evaluate_ocr(scene["video"], scene["source"], ocr_dir, scene["reference"])
                except Exception:
                    LOG.exception("OCR failed for scene %s", scene["sceneOrder"])
                    ocr = {"status": "failed", "reason": "OCR failed; see worker logs."}
                result["metrics"] = {"vbench": vbench, "paddleOcr": ocr, "inputFidelity": scene["fidelity"]}
                fidelity = scene["fidelity"]
                fidelity_complete = all(fidelity[key]["status"] in ("completed", "skipped") for key in ("i2vSubject", "artwork"))
                result["status"] = ("completed" if vbench["status"] == "completed" and ocr["status"] in ("completed", "skipped") and fidelity_complete
                                    else "uncertain" if count or ocr["status"] == "completed" else "failed")
    return {"results": results}


class Handler(BaseHTTPRequestHandler):
    def send_json(self, code: int, data: dict) -> None:
        body = json.dumps(data, allow_nan=False).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def authorized(self) -> bool:
        return bool(TOKEN) and hmac.compare_digest(self.headers.get("X-Worker-Token", ""), TOKEN)

    def do_GET(self) -> None:
        if not self.authorized():
            self.send_json(401, {"error": "Unauthorized"})
        elif self.path == "/health":
            self.send_json(200, {"status": "ok"})
        else:
            self.send_json(404, {"error": "Not found"})

    def do_POST(self) -> None:
        if not self.authorized():
            self.send_json(401, {"error": "Unauthorized"})
            return
        if self.path != "/evaluate":
            self.send_json(404, {"error": "Not found"})
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if length <= 0 or length > MAX_REQUEST:
                self.send_json(413, {"error": "Request too large"})
                return
            payload = json.loads(self.rfile.read(length))
            with LOCK:
                result = evaluate(payload)
            self.send_json(200, result)
        except (ValueError, KeyError, TypeError, json.JSONDecodeError):
            self.send_json(400, {"error": "Invalid request"})
        except Exception:
            LOG.exception("Batch evaluation failed")
            self.send_json(500, {"error": "Evaluation failed"})


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    if not TOKEN:
        raise SystemExit("VIDEO_QA_TOKEN is required")
    server = ThreadingHTTPServer((os.environ.get("VIDEO_QA_HOST", "127.0.0.1"), int(os.environ.get("VIDEO_QA_PORT", "8099"))), Handler)
    LOG.info("Video QA worker listening on %s:%s", *server.server_address)
    server.serve_forever()
