# Local video-quality adapters

The application calls these scripts only after a seller presses **Evaluate scene**. They do not call MiniMax or submit a new video-generation request.

## PaddleOCR

Install the Python dependencies in the quality environment:

```powershell
python -m pip install paddleocr opencv-python
```

`run_paddle_ocr.py` samples the first, middle, and last frames. It stores only token counts, source-token recall, frame-to-frame text consistency, and confidence; it never writes OCR text from private videos to the database.

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
