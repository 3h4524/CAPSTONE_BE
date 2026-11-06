"""Measure visible-text continuity between an approved source image and sampled video frames.

The .NET runner invokes this script with --video, --source and --output. It writes
metrics only: extracted text is deliberately never persisted with a private video.
Install locally with `python -m pip install paddleocr opencv-python` and configure
VideoQuality:PaddleOcrScriptPath to this file.
"""

from __future__ import annotations

import argparse
import json
import math
import re
import tempfile
from pathlib import Path
from typing import Any, Iterable

import cv2
from paddleocr import PaddleOCR


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--video", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--reference")
    parser.add_argument("--output", required=True)
    return parser.parse_args()


def normalize_tokens(values: Iterable[str]) -> set[str]:
    return {token.lower() for value in values for token in re.findall(r"[\w]+", value, re.UNICODE)}


MIN_CONFIDENCE = 0.7
MIN_TOKEN_LENGTH = 2
SAMPLED_FRAMES = 5


def collect_ocr(value: Any, observations: list[tuple[str, float]]) -> None:
    """Handle both PaddleOCR 2.x tuples and 3.x result dictionaries."""
    if isinstance(value, dict):
        result_texts = value.get("rec_texts")
        result_scores = value.get("rec_scores")
        if isinstance(result_texts, (list, tuple)) and isinstance(result_scores, (list, tuple)):
            observations.extend((text, float(score)) for text, score in zip(result_texts, result_scores)
                                if isinstance(text, str) and isinstance(score, (int, float)) and math.isfinite(score))
            return
        for nested in value.values():
            collect_ocr(nested, observations)
        return
    if isinstance(value, (list, tuple)):
        if len(value) == 2 and isinstance(value[0], str) and isinstance(value[1], (int, float)):
            if math.isfinite(value[1]):
                observations.append((value[0], float(value[1])))
            return
        for nested in value:
            collect_ocr(nested, observations)


def recognize(ocr: PaddleOCR, image: str) -> tuple[set[str], list[float]]:
    observations: list[tuple[str, float]] = []
    if hasattr(ocr, "predict"):
        results = list(ocr.predict(image))
    else:
        results = ocr.ocr(image, cls=True)
    collect_ocr(results, observations)
    tokens = {token for text, score in observations if score >= MIN_CONFIDENCE
              for token in normalize_tokens([text]) if len(token) >= MIN_TOKEN_LENGTH}
    return tokens, [score for _, score in observations]


def matches(left: str, right: str) -> bool:
    if left == right:
        return True
    if min(len(left), len(right)) < 5 or abs(len(left) - len(right)) > 1:
        return False
    if len(left) == len(right):
        return sum(a != b for a, b in zip(left, right)) <= 1
    shorter, longer = sorted((left, right), key=len)
    return any(shorter == longer[:i] + longer[i + 1:] for i in range(len(longer)))


def matched_count(source: set[str], target: set[str]) -> int:
    # One target token cannot satisfy multiple source tokens.
    available = set(target)
    count = 0
    for token in sorted(source):
        candidate = token if token in available else next((item for item in sorted(available) if matches(token, item)), None)
        if candidate is not None:
            available.remove(candidate)
            count += 1
    return count


def recognize_with_center(ocr: PaddleOCR, image: str, directory: Path, name: str) -> tuple[set[str], list[float]]:
    tokens, scores = recognize(ocr, image)
    frame = cv2.imread(image)
    if frame is None:
        raise RuntimeError("Could not read image for text evaluation.")
    height, width = frame.shape[:2]
    center = frame[int(height * .2):max(int(height * .8), 1), int(width * .1):max(int(width * .9), 1)]
    if center.size:
        scale = min(2.0, 2000 / max(center.shape[:2]))
        if scale > 1:
            center = cv2.resize(center, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
        path = directory / f"{name}-center.png"
        if not cv2.imwrite(str(path), center):
            raise RuntimeError("Could not write text evaluation crop.")
        extra_tokens, extra_scores = recognize(ocr, str(path))
        tokens |= extra_tokens
        scores.extend(extra_scores)
    return tokens, scores


def evaluate_ocr(ocr: PaddleOCR, video: str, source: str, directory: Path, reference: str | None = None) -> dict:
    source_tokens, source_scores = recognize_with_center(ocr, source, directory, "source")
    reference_tokens: set[str] = set()
    if reference:
        reference_tokens, _ = recognize_with_center(ocr, reference, directory, "reference")
    # A word from the original mockup is usable only if OCR also finds it on the start frame.
    visible_reference = {token for token in reference_tokens if any(matches(token, visible) for visible in source_tokens)}
    if visible_reference:
        source_tokens = visible_reference
    if not source_tokens:
        return {"status": "uncertain", "reason": "Could not reliably read text visible on the start frame. Review the scene manually.",
                "sourceTokenCount": 0, "referenceAvailable": bool(reference), "referenceVisibleTokenCount": 0}
    frames = [recognize_with_center(ocr, path, directory, f"sample-{index}")
              for index, path in enumerate(sample_frames(video, directory))]
    frame_sets = [tokens for tokens, _ in frames]
    video_tokens = set().union(*frame_sets)
    matched = matched_count(source_tokens, video_tokens)
    pairs = []
    for previous, current in zip(frame_sets, frame_sets[1:]):
        if previous and current:
            overlap = matched_count(previous, current)
            pairs.append(overlap / (len(previous) + len(current) - overlap))
    scores = source_scores + [score for _, values in frames for score in values]
    result = {"status": "completed" if matched == len(source_tokens) else "uncertain", "sourceTokenCount": len(source_tokens),
              "videoTokenCount": len(video_tokens), "sampledFrameCount": len(frames),
              "referenceAvailable": bool(reference), "referenceVisibleTokenCount": len(visible_reference),
              "framesWithTextMatch": sum(bool(matched_count(source_tokens, frame)) for frame in frame_sets),
              "sourceTokenRecall": matched / len(source_tokens) if matched else None,
              "frameTextConsistency": sum(pairs) / len(pairs) if pairs else None,
              "meanRecognitionConfidence": sum(scores) / len(scores) if scores else None}
    if matched < len(source_tokens):
        result["reason"] = "Some source text could not be reliably matched in sampled frames; review the scene manually."
    return result


def sample_frames(video: str, directory: Path) -> list[str]:
    capture = cv2.VideoCapture(video)
    if not capture.isOpened():
        raise RuntimeError("OpenCV could not open the video.")
    frame_count = max(1, int(capture.get(cv2.CAP_PROP_FRAME_COUNT)))
    positions = sorted({round(i * (frame_count - 1) / (SAMPLED_FRAMES - 1)) for i in range(SAMPLED_FRAMES)})
    paths: list[str] = []
    for index, position in enumerate(positions):
        capture.set(cv2.CAP_PROP_POS_FRAMES, position)
        ok, frame = capture.read()
        if not ok:
            continue
        path = directory / f"frame-{index}.png"
        if cv2.imwrite(str(path), frame):
            paths.append(str(path))
    capture.release()
    if not paths:
        raise RuntimeError("No video frame could be sampled for OCR.")
    return paths


def main() -> None:
    args = parse_args()
    try:
        ocr = PaddleOCR(
            lang="en",
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
            enable_mkldnn=False,
        )
    except TypeError:
        ocr = PaddleOCR(
            lang="en",
            use_angle_cls=True,
            enable_mkldnn=False,
        )

    with tempfile.TemporaryDirectory(prefix="apcs-paddle-ocr-") as temporary:
        payload = evaluate_ocr(ocr, args.video, args.source, Path(temporary), args.reference)
    Path(args.output).write_text(json.dumps(payload), encoding="utf-8")


if __name__ == "__main__":
    main()
