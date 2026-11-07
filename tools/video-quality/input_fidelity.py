"""Image-to-video evidence. Similarities are uncalibrated, not pass/fail decisions."""
from __future__ import annotations

import math
from pathlib import Path

import cv2
import numpy as np


def _region(value: dict | None, width: int, height: int) -> tuple[int, int, int, int] | None:
    if value is None:
        return None
    try:
        x, y, w, h = (float(value[key]) for key in ("x", "y", "width", "height"))
        if not all(math.isfinite(n) for n in (x, y, w, h)) or x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > 1.001 or y + h > 1.001:
            raise ValueError("Invalid normalized region")
        left, top = min(width - 1, math.floor(x * width)), min(height - 1, math.floor(y * height))
        return left, top, min(width - left, math.ceil(w * width)), min(height - top, math.ceil(h * height))
    except (KeyError, TypeError):
        raise ValueError("Invalid normalized region") from None


def prepare_start_frame(source: Path, geometry: dict, destination: Path) -> tuple[Path, tuple[int, int, int, int] | None]:
    """Reconstruct MiniMax's normalized crop and aspect ratio; artwork stays in its coordinate system."""
    image = cv2.imread(str(source))
    if image is None:
        raise ValueError("Could not decode source image")
    h, w = image.shape[:2]
    artwork = _region(geometry.get("artwork"), w, h)
    crop = _region(geometry.get("sourceCrop"), w, h) or (0, 0, w, h)
    x, y, cw, ch = crop
    image = image[y:y + ch, x:x + cw]
    ratio = {"9:16": 9 / 16, "16:9": 16 / 9}.get(geometry.get("aspectRatio"))
    if ratio is None:
        raise ValueError("Unsupported input aspect ratio")
    if cw / ch > ratio:
        tw, th = max(1, round(ch * ratio)), ch
    else:
        tw, th = cw, max(1, round(cw / ratio))
    ox, oy = (cw - tw) // 2, (ch - th) // 2
    image = image[oy:oy + th, ox:ox + tw]
    visible = None
    if artwork:
        ax, ay, aw, ah = artwork
        left, top = max(ax, x + ox), max(ay, y + oy)
        right, bottom = min(ax + aw, x + ox + tw), min(ay + ah, y + oy + th)
        if right > left and bottom > top and (right - left) * (bottom - top) >= .9 * aw * ah:
            visible = (left - x - ox, top - y - oy, right - left, bottom - top)
    # The normal case needs no resize; this approximates the provider's size limits.
    scale = min(1.0, 5760 / max(tw, th))
    if min(tw, th) * scale < 256:
        scale = 256 / min(tw, th)
    if abs(scale - 1) > .0001:
        image = cv2.resize(image, (max(1, round(tw * scale)), max(1, round(th * scale))), interpolation=cv2.INTER_CUBIC)
        if visible:
            visible = tuple(round(n * scale) for n in visible)
    if not cv2.imwrite(str(destination), image):
        raise ValueError("Could not prepare input image")
    return destination, visible


def artwork_similarity(model, device, image_path: Path, video_path: Path, artwork: tuple[int, int, int, int] | None) -> dict:
    if artwork is None:
        return {"status": "skipped", "reason": "No fully visible artwork region is recorded on the start image."}
    import torch
    import torchvision.transforms as transforms
    from PIL import Image

    source = cv2.imread(str(image_path))
    x, y, w, h = artwork
    if min(w, h) < 80:
        return {"status": "uncertain", "reason": "The artwork region is too small for a reliable comparison."}
    patch = source[y:y + h, x:x + w]
    orb = cv2.ORB_create(nfeatures=2000)
    keys, descriptors = orb.detectAndCompute(cv2.cvtColor(patch, cv2.COLOR_BGR2GRAY), None)
    if descriptors is None or len(keys) < 12:
        return {"status": "uncertain", "reason": "Not enough visual detail to track the artwork."}
    transform = transforms.Compose([transforms.Resize((224, 224)), transforms.ToTensor(),
                                    transforms.Normalize((.485, .456, .406), (.229, .224, .225))])
    def features(bgr):
        tensor = transform(Image.fromarray(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))).unsqueeze(0).to(device)
        with torch.inference_mode():
            return torch.nn.functional.normalize(model(tensor), dim=-1)
    original = features(patch)
    capture = cv2.VideoCapture(str(video_path))
    if not capture.isOpened():
        raise ValueError("Could not open scene video")
    total = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
    positions = sorted({round(i * (total - 1) / 4) for i in range(5)}) if total > 0 else []
    scores = []
    matcher = cv2.BFMatcher(cv2.NORM_HAMMING)
    corners = np.float32([[0, 0], [w - 1, 0], [w - 1, h - 1], [0, h - 1]]).reshape(-1, 1, 2)
    try:
        for pos in positions:
            capture.set(cv2.CAP_PROP_POS_FRAMES, pos)
            ok, frame = capture.read()
            if not ok:
                continue
            fkeys, fdesc = orb.detectAndCompute(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY), None)
            if fdesc is None or len(fdesc) < 2:
                continue
            pairs = matcher.knnMatch(descriptors, fdesc, k=2)
            good = [pair[0] for pair in pairs if len(pair) == 2 and pair[0].distance < .7 * pair[1].distance]
            if len(good) < 12:
                continue
            src = np.float32([keys[m.queryIdx].pt for m in good]).reshape(-1, 1, 2)
            dst = np.float32([fkeys[m.trainIdx].pt for m in good]).reshape(-1, 1, 2)
            matrix, mask = cv2.findHomography(src, dst, cv2.RANSAC, 4.0)
            if matrix is None or mask is None or int(mask.sum()) < 10 or float(mask.mean()) < .6:
                continue
            polygon = cv2.perspectiveTransform(corners, matrix).reshape(-1, 2)
            fh, fw = frame.shape[:2]
            area = abs(cv2.contourArea(polygon.astype(np.float32)))
            if not np.isfinite(polygon).all() or area < 6400 or area > .8 * fw * fh or not cv2.isContourConvex(polygon.astype(np.float32)):
                continue
            if (polygon[:, 0] < 0).any() or (polygon[:, 0] >= fw).any() or (polygon[:, 1] < 0).any() or (polygon[:, 1] >= fh).any():
                continue
            rectified = cv2.warpPerspective(frame, np.linalg.inv(matrix), (w, h))
            score = float((original * features(rectified)).sum().item())
            if math.isfinite(score):
                scores.append(score)
    finally:
        capture.release()
    if len(scores) < 2:
        return {"status": "uncertain", "reason": "The artwork could not be reliably tracked in enough frames.",
                "sampledFrameCount": len(positions), "trackedFrameCount": len(scores)}
    result = {"status": "completed" if len(scores) >= math.ceil(.6 * len(positions)) else "uncertain",
              "score": sum(scores) / len(scores), "sampledFrameCount": len(positions), "trackedFrameCount": len(scores)}
    if result["status"] == "uncertain":
        result["reason"] = "Artwork was tracked in only a small portion of sampled frames; review the scene."
    return result
