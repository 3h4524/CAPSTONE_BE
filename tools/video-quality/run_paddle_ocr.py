"""Measure visible-text continuity between an approved source image and sampled video frames.

The .NET runner invokes this script with --video, --source and --output. It writes
metrics only: extracted text is deliberately never persisted with a private video.
Install locally with `python -m pip install paddleocr opencv-python` and configure
VideoQuality:PaddleOcrScriptPath to this file.
"""

from __future__ import annotations

import argparse
import json
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
    parser.add_argument("--output", required=True)
    return parser.parse_args()


def normalize_tokens(values: Iterable[str]) -> set[str]:
    return {token.lower() for value in values for token in re.findall(r"[\w]+", value, re.UNICODE)}


def collect_ocr(value: Any, texts: list[str], scores: list[float]) -> None:
    """Handle both PaddleOCR 2.x tuples and 3.x result dictionaries."""
    if isinstance(value, dict):
        result_texts = value.get("rec_texts")
        result_scores = value.get("rec_scores")
        if isinstance(result_texts, (list, tuple)):
            texts.extend(str(text) for text in result_texts if isinstance(text, str))
        if isinstance(result_scores, (list, tuple)):
            scores.extend(float(score) for score in result_scores if isinstance(score, (int, float)))
        for nested in value.values():
            collect_ocr(nested, texts, scores)
        return
    if isinstance(value, (list, tuple)):
        if len(value) == 2 and isinstance(value[0], str) and isinstance(value[1], (int, float)):
            texts.append(value[0])
            scores.append(float(value[1]))
            return
        for nested in value:
            collect_ocr(nested, texts, scores)


def recognize(ocr: PaddleOCR, image: str) -> tuple[set[str], list[float]]:
    texts: list[str] = []
    scores: list[float] = []
    if hasattr(ocr, "predict"):
        results = list(ocr.predict(image))
    else:
        results = ocr.ocr(image, cls=True)
    collect_ocr(results, texts, scores)
    return normalize_tokens(texts), scores


def sample_frames(video: str, directory: Path) -> list[str]:
    capture = cv2.VideoCapture(video)
    if not capture.isOpened():
        raise RuntimeError("OpenCV could not open the video.")
    frame_count = max(1, int(capture.get(cv2.CAP_PROP_FRAME_COUNT)))
    positions = sorted({0, max(0, frame_count // 2), max(0, frame_count - 1)})
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

    source_tokens, source_scores = recognize(ocr, args.source)
    with tempfile.TemporaryDirectory(prefix="apcs-paddle-ocr-") as temporary:
        frame_tokens = [recognize(ocr, frame) for frame in sample_frames(args.video, Path(temporary))]

    video_tokens = set().union(*(tokens for tokens, _ in frame_tokens))
    overlap = source_tokens.intersection(video_tokens)
    recall = len(overlap) / len(source_tokens) if source_tokens else None
    frame_sets = [tokens for tokens, _ in frame_tokens]
    pair_scores = []
    for previous, current in zip(frame_sets, frame_sets[1:]):
        union = previous.union(current)
        pair_scores.append(len(previous.intersection(current)) / len(union) if union else 1.0)
    all_scores = source_scores + [score for _, scores in frame_tokens for score in scores]
    payload = {
        "status": "completed",
        "sourceTokenCount": len(source_tokens),
        "videoTokenCount": len(video_tokens),
        "sourceTokenRecall": recall,
        "frameTextConsistency": sum(pair_scores) / len(pair_scores) if pair_scores else None,
        "meanRecognitionConfidence": sum(all_scores) / len(all_scores) if all_scores else None,
        "sampledFrameCount": len(frame_tokens),
    }
    Path(args.output).write_text(json.dumps(payload), encoding="utf-8")


if __name__ == "__main__":
    main()
