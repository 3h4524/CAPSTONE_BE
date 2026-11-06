# Video quality worker

The .NET service owns the durable job queue and sends the scenes of a video to this private Python HTTP worker. The worker downloads the already stored Cloudinary clips into a temporary directory, runs VBench in a batch for each dimension, and returns per-scene metrics. It removes the temporary directory after the request. OCR is initialized once per worker process; when the source has no reliable detected text, OCR reports insufficient evidence.

Input fidelity uses the official VBench-I2V `i2v_subject` evaluator with the reconstructed MiniMax input crop. This score also includes video frame-to-frame consistency, and is not a measure of exact artwork or text preservation. When an artwork region is recorded and remains substantially visible in the input crop, the worker attempts to track it using OpenCV feature matches and compares tracked crops with DINOv2 ViT-S/14. It skips or reports uncertainty when the region is absent, too small, occluded, or cannot be tracked; the reported similarity only covers tracked frames. Both metrics are uncalibrated guides, not pass/fail decisions. Input crop metadata is available for newly queued MiniMax QA jobs; other providers and older queued payloads are skipped.

VBench's current upstream `compute_*` functions load their models on each dimension call. This worker batches scenes so each model is loaded once per dimension per video; it does **not** promise to keep VBench models resident between jobs. Inference runs one batch at a time to limit GPU memory use. A failed batch is retried scene by scene so one bad clip does not hide the other results.

## Setup

Install Python, PyTorch, VBench and its model dependencies in a dedicated environment following the [official VBench instructions](https://github.com/Vchitect/VBench). Install PaddlePaddle, PaddleOCR and OpenCV in the same environment. Checkpoint downloads and GPU drivers must be available to the worker. These dependencies and weights are not bundled with this repository.

Install the VBench checkout with its `vbench2_beta_i2v` package. The first fidelity evaluation loads the official DINO ViT-B/16 weights; artwork evaluation also downloads DINOv2 ViT-S/14 through PyTorch Hub when needed. A GPU is recommended, and the worker needs network access for the first download or prepopulated PyTorch Hub caches. The two models run sequentially with the existing VBench dimensions.

Example on PowerShell, after activating that environment and installing VBench:

```powershell
$env:VIDEO_QA_TOKEN = '<long-random-shared-secret>'
$env:VIDEO_QA_CLIP_HOSTS = 'api.cloudinary.com,res.cloudinary.com'
python video-quality-worker/worker.py
```

In this workspace, the previous benchmark environment was found at `D:\tools\video-quality-venv\Scripts\python.exe`. It can be used in place of `python` after confirming its dependencies and GPU configuration.

The service listens on `127.0.0.1:8099` by default. `VIDEO_QA_HOST` and `VIDEO_QA_PORT` can change the binding; only expose it on a trusted private network. `VIDEO_QA_DEVICE` can select `cuda` or `cpu`; GPU is strongly recommended. Store the token in a secret store or ignored environment file, not source control. By default, the HTTPS allowlist permits `api.cloudinary.com` (used by Cloudinary's private download URLs) and `res.cloudinary.com`. If `VIDEO_QA_CLIP_HOSTS` is set, it replaces this default and must include every required host. Redirects are rejected.

Set the API process configuration, using the same secret:

```text
VideoQuality__Enabled=true
VideoQuality__AutoEvaluate=true
VideoQuality__WorkerUrl=http://127.0.0.1:8099
VideoQuality__WorkerToken=<same-secret>
VideoQuality__WorkerTimeoutSeconds=3600
```

Restart the API after configuring it. Start the worker before enabling QA: connection failures cause the .NET job to retry and eventually fail visibly. With `Enabled=false`, the existing runner records evaluations as skipped. When `WorkerUrl` is blank, the existing script runner remains available. The worker authenticates both `GET /health` and `POST /evaluate` using `X-Worker-Token`.

The .NET worker maintains the existing database queue and extends the job lease during inference. This change needs no SQL migration. Video QA remains asynchronous and does not block video preview. GPU memory, inference duration and first-time model downloads depend on the environment; benchmark with representative videos before selecting production limits.
