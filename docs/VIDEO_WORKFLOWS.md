# Video from Mockup — Standard MVP

## Scope

The executable pipeline is Product Input → Apply Mockup → Mockup Approval → Generate Video → Review Video → Export ZIP. A run snapshots one owned product and workflow revision. Standard is the only enabled media mode. AI Background and AI Shot are declared in the contract/capability response and displayed as Coming soon; no AI provider jobs, generated-media tables or OpenMontage dependency are installed.

Template catalog version 2 contains Product Showcase, Design Detail and Variant Showcase. Auto chooses Variant Showcase for two or more named variants, then Design Detail when an artwork-detail role or detail region exists, and otherwise Product Showcase. The planner chooses up to four approved images deterministically by role, variant, resolution and ID. A single image becomes two scenes with distinct motion. Version 1 definitions and videos keep their original planning behavior.

Upload mockups, mark protected product/artwork/detail regions directly on the image and approve the current metadata revision. The upload service assigns the product artwork group and Hero role by default. Role and variant remain under Advanced information, and the group selector is hidden when the product has only one group. Editing metadata increments the asset revision and removes its approval. Images are normalized to immutable PNG sources. The renderer produces silent 30 FPS H.264/yuv420p MP4 in Square 1080 × 1080, Portrait 1080 × 1920, Tall 1080 × 2160 or Landscape 1920 × 1080, 3–15 seconds (12 by default), under 100 MB. Existing workflows without an output format continue to use Tall. Review always addresses a video ID, review revision and run revision. Re-render preserves old versions; retry reuses the same logical candidate at its checkpoint.

Standard motion presets include `contain_gentle`, zoom in/out, horizontal and vertical pans, and four diagonal pan directions. `contain_gentle` applies a small scale animation while retaining the complete image and is the version 2 default when no protected region exists. Crop motion is accepted only when its complete path contains the protected product or artwork region; otherwise the planner returns contain/static framing and a warning.

New workflows default to Auto template version 2, 12 seconds, fade, Varied motion, no caption and automatic image selection. The browser debounces storyboard requests and displays the exact backend `ScenePlan` through the same composition geometry package used by the worker. A stale preview response cannot overwrite newer settings. Scene order and motion move together; Reset to template clears selected IDs, order and motion overrides. Existing workflows retain version 1 when `templateVersion` is absent.

`GET /api/workflows/video-templates` returns the active version 2 catalog, preview asset URL, defaults and requirements. A rerender request should include `expectedStoryboardFingerprint` from a current preview. The API replans from current approved asset revisions and returns conflict instead of enqueuing when the fingerprint differs.

## Combined with design generation

The same workflow can generate the designs and mock-ups that the video is made from. Prompt Synthesis, Design Image and Design Approval (`design-approval`) may sit between Product Input and Apply Mockup, each at most once, so the full line is Product Input → Prompt Synthesis → Design Image → Design Approval → Apply Mockup → Mockup Approval → Generate Video → Review Video → Export ZIP. `WorkflowCapabilityRegistry.RunOrder` holds that order and `WorkflowGraphValidator` requires one edge between each pair of neighbouring steps that are present. The six video steps are still required exactly once.

These design steps have no executor in a run. The canvas runs them first as a batch job for the whole batch (`api/batch-jobs`), and a run leaves their checkpoints `pending`. Design Approval reviews designs before mock-ups are composited; Mockup Approval (`approval-gate`) still reviews mock-ups before rendering. Product Input therefore carries a batch for the designs and one product for the video.

Apply Mockup composites mock-ups as Cloudinary transformation URLs. Such a row (`source_type = 'generated'`) has no stored file or content hash, so it is not listed by `GET /api/products/{id}/mockup-assets` and cannot be planned or rendered. `POST /api/products/{id}/mockup-assets/import-generated` stores a PNG snapshot of each one (Cloudinary fetches the composite URL itself) and records its key, version, hash and size on the same row, at most 24 per call, newest first. Imported mock-ups stay `pending` and are approved at Mockup Approval like uploads. Their artwork group is the design image ID and their variant is the garment color, so a product with several approved designs has several groups and a video uses one of them. The canvas calls the import when the video part starts and from the Mockup Approval panel. A mock-up regenerated after its template changed becomes a new row; the older row keeps any import and approval it had.

The frontend resolves `@apcs/video-composition` from `../CAPSTONE_BE/shared/video-composition`, and `Start-VideoDev.ps1` looks for the frontend in `../CAPSTONE_FE` first and `../../Frontend/CAPSTONE_FE` second. With npm the package must be installed as a copy (`install-links=true` in the frontend's `.npmrc`): Turbopack does not resolve a link that points outside the project.

## Setup

For this Windows workspace, `./scripts/Start-VideoDev.ps1` starts the Release API at localhost:5191, frontend at localhost:3000 and local media worker using installed smoke-test binaries. It creates a shared ephemeral worker secret in child-process environments, not a file, and refuses occupied ports instead of stopping existing processes. Logs are in ignored `artifacts/video-dev`. Stop the returned processes before rerunning; the secret changes each launch. This convenience launcher does not replace production secret/container configuration.

Keep existing PostgreSQL, Cloudinary, Redis and authentication configuration. Add `MediaWorker__ApiKey` to the API's ignored environment file or secret manager: a random 32–256-character secret. Do not put it in workflow JSON or frontend environment variables. Configure the identical value as `MEDIA_WORKER_API_KEY` for the worker; copy `media-worker/.env.example` to its ignored `.env` and replace the placeholder. Never use the placeholder as a real credential.

Apply the additive database-first schema to the intended database after taking the normal backup. The script reads `ConnectionStrings__DefaultConnection` without printing it. It includes nullable product custom fields and mockup-template ownership already expected by the source but absent from the inspected database. It does not delete existing business rows or require EF migrations.

```powershell
dotnet build API/API.csproj -c Release
./scripts/Apply-VideoSchema.ps1 -Configuration Release
./scripts/Scaffold-Database.ps1 -Configuration Release -NoBuild
dotnet build Capstone.sln -c Release
```

Restart the API to load the new endpoints and worker key. The worker Dockerfile uses the backend repository root as its build context so it can copy `shared/video-composition` before installing and building the worker. From `media-worker`, with Docker Engine running:

```powershell
docker compose up --build -d
```

The container has Node, Remotion, Chromium, FFmpeg and ffprobe. It runs as a non-root user, one job at a time, with a 2-minute renewable lease, 10-minute processing deadline and up to 3 queue attempts. `APCS_API_URL` must be reachable from the container. The default host port is 5191; change it if the API listens elsewhere. Use HTTPS outside the local/container network and restrict `/internal/media-jobs` at the ingress; it is separately authenticated with the worker key.

Local worker development is also possible with Node 22+, FFmpeg/ffprobe on PATH (or `FFMPEG_PATH`/`FFPROBE_PATH`), `npm ci`, `npm run browser`, and `npm start`. Set process environment variables explicitly; the Node worker does not automatically load `.env` (Docker Compose does).

The frontend uses its existing `NEXT_PUBLIC_API_BASE_URL`, cookie session and query providers. No AI key is required. Install with `pnpm install --frozen-lockfile`, then `pnpm dev`. The old simulated workflow server is removed. Existing browser-only demo workflows are not silently imported into the real database.

## Seller flow

1. Open Workflows, select a batch and one product in Product Input.
2. Upload/select up to 8 mockups in Apply Mockup. Draw Product, Artwork or Detail regions on the image; drag or use the keyboard/buttons to move and resize them. Role and variant are optional advanced fields. Replacing an image means uploading a new immutable asset and selecting it; changing metadata invalidates approval.
3. Save the definition and Run. Open Mockup Approval, approve/reject current image revisions, then Continue with approved mockups. Runs without valid sources remain waiting for input.
4. Leave Generate Video on Auto for a complete storyboard, or choose an available template card. Choose Square for square product images, Portrait for short-form social video, Tall for the legacy 1:2 canvas, or Landscape for horizontal playback. `contain_gentle` keeps the entire source visible, so choosing a canvas whose ratio is close to the source reduces background bands. Template previews play only on focus, hover or explicit selection. The storyboard and review players use the selected ratio and cap their visual height inside the settings panel. Open Edit scenes only when image choice, order or per-scene motion needs adjustment. Rendering progress is polled from durable state; the URL retains the run ID for refresh.
5. Review the completed video without autoplay. Inspect QA/warnings and source scenes. Approve the exact candidate, reject, edit/re-render, or download the MP4. Previous versions are viewable but cannot approve a newer candidate.
6. Export runs only after video approval. Download approved ZIP when export succeeds. ZIP contains video.mp4, thumbnail.png, approved source PNGs and a provenance manifest, not prompts, credentials, signed URLs or internal provider metadata.

Manual asset selection is frozen when starting a run. For uploads during a waiting run, use automatic selection or cancel/start a newly saved run to change its frozen source IDs. Retry is for failed checkpoints; explicit re-render is offered while waiting for video review. Editing a completed run starts a new run rather than rewriting its approved package.

## Storage and security

Cloudinary media use `authenticated` delivery, unique non-overwriting keys and 10-minute signed access issued only after ownership checks. Lease-scoped worker upload grants are not exposed through public workflow APIs. Source hashes/revisions are checked at claim and completion. Cancel or lease loss fences late completion; source approval revoked during render/export also rejects completion. Workflow config refuses credential/provider-routing fields.

The API trusts the authenticated worker's QA report after verifying Cloudinary output identity/version/size. Restrict worker credentials accordingly; this is not independent server-side re-decoding. Cloudinary free-plan quotas and permission to serve authenticated raw ZIP assets must be verified on the actual account. Failed/stale attempts can leave private orphan media; scheduled garbage collection is not included in this MVP.

## Verification

```powershell
./scripts/Test-WithCoverage.ps1
```

The behavioral coverage gate includes `APCS.Application.Features.Workflows.*` and requires 80% line / 70% branch. PostgreSQL lease tests are opt-in in the separate integration project, require `APCS_TEST_CONNECTION_STRING`, and use temporary tables plus rollback, never public business rows.

In `media-worker`: `npm run check`, `npm test`, `npm run browser`, `npm run smoke -- --all`. The smoke command writes six synthetic product-profile fixtures, actual MP4s, thumbnails and full-decode QA under ignored artifacts. These geometry fixtures are not a substitute for seller photography review.

In the frontend: `pnpm lint`, `pnpm test`, `pnpm exec playwright install chromium`, `pnpm test:workflow-ui`. Browser tests exercise the real UI with intercepted API fixture responses; they are not a real Cloudinary end-to-end run.

Before production, verify the complete signed-in upload → render → review → ZIP path on the actual Cloudinary account, video seeking, source fidelity/crop on real photos for all six types, API/worker restart recovery, ingress/auth limits, failure/cancel races and Docker deployment. The Windows workstation may need Developer Mode or Linux/CI for Next standalone symlink packaging; do not disable production standalone output merely to mask that host limitation.

## Extension and licensing

APCS owns `IVideoRenderer` and the workflow contracts. Add AI Background first via mask confirmation, protected pixel compositing, generated-asset provenance, cost/budget policy and QA; enable capability only after an executor exists. Add one AI Shot per video later with at most two generation attempts and explicit Standard fallback. Do not enable a mode by changing only the capability flag.

Standard incurs no AI model charges, but render compute, storage and bandwidth still have costs. Remotion's [official license](https://www.remotion.dev/license) currently allows free use for eligible individuals, organizations with up to three employees, non-profits, or non-commercial evaluation. Confirm the legal entity's eligibility before production and recheck when upgrading Remotion; this implementation does not establish license eligibility automatically. [Cloudinary upload/delivery reference](https://cloudinary.com/documentation/image_upload_api_reference) governs authenticated asset behavior.
