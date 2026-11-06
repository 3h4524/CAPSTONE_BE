"""Long-running PaddleOCR worker for the design text check.

The API starts it once and keeps it open, so PaddleOCR loads its models a single time. It reads one JSON request per
line on stdin, {"id": "...", "path": "<image file>"}, and answers each on stdout with a line starting with "@@OCR ":
{"id": "...", "lines": [{"text": "...", "score": 0.98}]} or {"id": "...", "error": "..."}. It first sends
{"ready": true} once the models are loaded. Everything else PaddleOCR prints goes to stderr.

The image is read as the offline scoring (ocr.py) reads it: transparent pixels on gray, at most
1536 px on its longest side, with the same PaddleOCR settings (OCR_SETTINGS), so the app and the report agree.
"""
import argparse
import json
import os
import sys

PROTOCOL = sys.stdout
sys.stdout = sys.stderr  # PaddleOCR prints progress; only answers go to the real stdout

os.environ.setdefault("PADDLE_PDX_MODEL_SOURCE", "BOS")
import numpy as np  # noqa: E402
from PIL import Image  # noqa: E402
from paddleocr import PaddleOCR  # noqa: E402

PREFIX = "@@OCR "
GRAY = (128, 128, 128)
MAX_SIDE = 1536

# Text found on a copy at most 960 px on its longest side, with no text-line rotation model: on the 95 lettering designs
# checked by eye (2026-11-03) this agreed with the eye 82 times, against 77 for the PaddleOCR defaults, and took about
# 11 s a design on a laptop CPU instead of 35 s. The same settings as the offline scoring (ocr.py).
OCR_SETTINGS = dict(use_doc_orientation_classify=False, use_doc_unwarping=False, use_textline_orientation=False,
                    text_det_limit_type="max", text_det_limit_side_len=960)


def send(message):
    PROTOCOL.write(PREFIX + json.dumps(message, ensure_ascii=False) + "\n")
    PROTOCOL.flush()


def load(path):
    """Transparent pixels become gray, so light and dark lettering both stay readable. Returns BGR, as PaddleOCR expects."""
    image = Image.open(path).convert("RGBA")
    canvas = Image.new("RGBA", image.size, GRAY + (255,))
    canvas.alpha_composite(image)
    flat = canvas.convert("RGB")
    if max(flat.size) > MAX_SIDE:
        scale = MAX_SIDE / max(flat.size)
        flat = flat.resize((round(flat.width * scale), round(flat.height * scale)), Image.LANCZOS)
    return np.array(flat)[:, :, ::-1].copy()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--lang", default="en")
    args = parser.parse_args()

    ocr = PaddleOCR(lang=args.lang, **OCR_SETTINGS)
    send({"ready": True})

    for raw in sys.stdin:
        raw = raw.strip()
        if not raw:
            continue
        try:
            request = json.loads(raw)
        except ValueError:
            continue
        request_id = request.get("id")
        try:
            result = ocr.predict(load(request["path"]))[0]
            lines = [{"text": str(text), "score": round(float(score), 4)}
                     for text, score in zip(result["rec_texts"], result["rec_scores"])]
            send({"id": request_id, "lines": lines})
        except Exception as error:  # one bad image must not stop the worker
            send({"id": request_id, "error": f"{type(error).__name__}: {error}"})


if __name__ == "__main__":
    sys.exit(main())
