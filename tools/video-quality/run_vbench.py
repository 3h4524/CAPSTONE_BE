"""Run VBench custom-input dimensions for one generated scene video.

Clone https://github.com/Vchitect/VBench outside this repository, install its
requirements, and set VBENCH_HOME to that clone. The wrapper uses the official
custom-input command for dimensions supported for arbitrary videos and produces
a compact JSON file that the .NET quality runner can store.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any


DIMENSIONS = ["subject_consistency", "background_consistency", "motion_smoothness", "aesthetic_quality", "imaging_quality"]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--video", required=True)
    parser.add_argument("--source", required=True)  # accepted to keep the quality-adapter contract uniform
    parser.add_argument("--output", required=True)
    return parser.parse_args()


def numeric_values(value: Any) -> list[float]:
    if isinstance(value, bool):
        return []
    if isinstance(value, (int, float)):
        return [float(value)]
    if isinstance(value, dict):
        return [number for nested in value.values() for number in numeric_values(nested)]
    if isinstance(value, list):
        return [number for nested in value for number in numeric_values(nested)]
    return []


def changed_json_files(root: Path, started_at: float) -> list[Path]:
    return [path for path in root.rglob("*.json") if path.stat().st_mtime >= started_at - 1 and "evaluation_results" in path.parts]


def main() -> None:
    args = parse_args()
    home_value = os.environ.get("VBENCH_HOME")
    if not home_value:
        raise RuntimeError("Set VBENCH_HOME to a local clone of Vchitect/VBench before enabling VBench evaluation.")
    home = Path(home_value).expanduser().resolve()
    evaluator = home / "evaluate.py"
    if not evaluator.is_file():
        raise RuntimeError(f"VBench evaluate.py was not found under {home}.")

    started_at = time.time()
    outputs: dict[str, dict[str, Any]] = {}
    for dimension in DIMENSIONS:
        command = [sys.executable, str(evaluator), "--dimension", dimension, "--videos_path", args.video, "--mode=custom_input"]
        completed = subprocess.run(command, cwd=home, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
        if completed.returncode != 0:
            outputs[dimension] = {"status": "failed", "exitCode": completed.returncode,
                                  "detail": completed.stdout[-2000:]}
        else:
            outputs[dimension] = {"status": "completed"}

    values: dict[str, list[float]] = {dimension: [] for dimension in DIMENSIONS}
    for path in changed_json_files(home, started_at):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        for dimension in DIMENSIONS:
            if dimension in path.name or (isinstance(data, dict) and dimension in data):
                values[dimension].extend(numeric_values(data.get(dimension, data) if isinstance(data, dict) else data))

    metrics: dict[str, Any] = {"dimensions": outputs}
    completed_dimensions = 0
    for dimension, scores in values.items():
        if scores:
            metrics[f"{dimension}Score"] = sum(scores) / len(scores)
            completed_dimensions += 1
    metrics["status"] = "completed" if completed_dimensions == len(DIMENSIONS) else "uncertain"
    metrics["completedDimensionCount"] = completed_dimensions
    Path(args.output).write_text(json.dumps(metrics), encoding="utf-8")


if __name__ == "__main__":
    main()
