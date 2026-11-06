# Local video-quality adapters

The application can call these legacy scripts from the automatic QA job or when a seller requests a rerun. They do not call MiniMax or submit a new video-generation request. For the batch Python service, see [`video-quality-worker/README.md`](../../video-quality-worker/README.md).

## PaddleOCR

Install the Python dependencies in the quality environment:

```powershell
python -m pip install paddleocr opencv-python
```

`run_paddle_ocr.py` samples up to five evenly spaced frames and reads both the full image and an enlarged central crop. It compares the video to text reliably detected on the scene source image. For studio frames with an unchanged, explicitly linked product mockup, OCR can use words from that mockup only when also recognized on the source image. Uploaded frames have no assumed original mockup. The adapter ignores single-character tokens and OCR detections below 0.7 confidence, and permits a single character difference for words of at least five characters. If reliable source words cannot be matched, the result is uncertain. Counts and metrics, not recognized text, are saved. OCR is an observational guide, not proof that text is missing or that artwork is intact.

## VBench

Clone the official repository outside the application checkout, follow its PyTorch/VBench installation guide, then set `VBENCH_HOME` to the clone location. The wrapper invokes VBench's `custom_input` mode for `subject_consistency`, `background_consistency`, `motion_smoothness`, `aesthetic_quality`, and `imaging_quality`. GPU and optional model requirements remain owned by the local VBench installation.

Configure the API process after the scripts and Python dependencies are available:

```json
"VideoQuality": {
  "Enabled": true,
  "PythonExecutable": "python",
  "PaddleOcrScriptPath": "D:/FPT/Capstone/source/Backend/CAPSTONE_BE/tools/video-quality/run_paddle_ocr.py",
  "VBenchScriptPath": "D:/FPT/Capstone/source/Backend/CAPSTONE_BE/tools/video-quality/run_vbench.py",
  "TimeoutSeconds": 600
}
```

Keep `Enabled` false until that local environment is ready. A missing script, missing GPU, or a VBench/Paddle failure is saved as non-blocking quality evidence; it never deletes or rejects the generated video.
