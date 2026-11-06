# Design text check (OCR)

After a design image is saved, the API reads its text with PaddleOCR in the background and compares it with the phrase
the design should print. The result is kept in the image's `generation_metadata` under `textCheck`:

```json
"textCheck": { "ok": false, "expected": "Vintage Fishing Club", "read": "VINTAGE / FISHNG / CLUB",
               "missing": ["FISHING"], "extra": ["FISHNG"], "repeated": [], "cer": 0.0556 }
```

- The review grid and the approval panel show it under each design: "Text matches", or "Text may be wrong: missing …".
  It is only a warning, because OCR can misread stylised lettering. It never blocks or deletes a design.
- **Admin → Image Quality → Live generation stats** shows the share of checked designs with correct text and the mean
  character error rate (CER) per prompt version.
- The rules match the offline scoring (`ocr.py`, kept outside the repo). Case and punctuation are ignored, a word OCR glued
  to its neighbour still counts, and a single stray character is not counted as an extra word. A design that should
  print no words fails only if OCR finds a word.

## How it runs

`ocr_worker.py` stays open while the API runs, so PaddleOCR loads its models only once. Loading takes 1–2 minutes on a
laptop CPU, and each design then takes about 11 seconds. Designs wait in an in-memory queue (at most 200), so the job
that makes them is never slowed down. Designs still waiting when the API stops are not checked.

The worker finds text on a copy at most 960 px long and skips the text-line rotation model. On the 95 lettering designs
checked by eye, these settings agreed with the eye on 82 designs, against 77 for the PaddleOCR defaults, and ran three
times faster. The mobile models were faster again (2 s) but agreed on only 63, often dropping a first letter.

## Turning it on

1. Make a Python 3.11 environment with PaddleOCR. Path names must be short on Windows, so keep it outside a deep folder:
   ```powershell
   py -3.11 -m venv D:\ocr-venv
   D:\ocr-venv\Scripts\python -m pip install paddlepaddle==3.0.0 paddleocr==3.1.1 paddlex==3.1.4
   ```
   Pin `paddlex` 3.1.4: newer versions fail with paddleocr 3.1.1.
2. Configure the API (in `appsettings.Development.json`, or as environment variables such as `DesignTextCheck__Enabled=true`):
   ```json
   "DesignTextCheck": {
     "Enabled": true,
     "PythonExecutable": "D:/ocr-venv/Scripts/python.exe"
   }
   ```
3. Restart the API. The log says "The OCR worker for the design text check is ready" once the models are loaded.

Other settings: `ScriptPath` (default `tools/design-text-check/ocr_worker.py`, looked for from the API folder and from
its parent), `Language` (PaddleOCR language, default `en`), `StartupTimeoutSeconds` (300), `TimeoutSeconds` per design
(90) and `RetryAfterMinutes` (5) before starting a worker again that could not start.
