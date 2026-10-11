# Testing AI Shot video generation

AI Shot uses the Gemini API key already connected in Provider Settings to call `veo-3.1-lite-generate-preview`. The key must have paid Veo access; successful image generation alone does not prove Veo access. The app asks for confirmation before each new paid clip. At the current Google 720p rate, one 6-second clip is estimated at US$0.30, a full 8-second video at US$0.40, and a full 15-second video (two 8-second clips, overlapped by one second) at US$0.80. Existing stored clips are reused when their source and prompt fingerprint matches. No AI Background option is part of Generate Video.

## One-time setup

1. Apply [`scripts/sql/ai-shot-mode.sql`](../scripts/sql/ai-shot-mode.sql) to the development database. This replaces the old `promo_videos.mode = 'standard'` constraint. Do not apply it to production before reviewing the deployment.
2. Make sure Redis and the database are running and the existing Gemini key shows Connected. Veo needs billing enabled in the Google project linked to that key.
3. Rebuild and restart the backend API from this branch. A previously running API process still serves the old code. If using `Start-VideoDev.ps1`, first build the Release API and ensure port 5191 is free; the script checks ports and will not stop existing processes.
4. Start the media worker with `./scripts/Start-MediaWorker.ps1` in a separate PowerShell terminal. It uses the worker key from `.env`. Restart the frontend from this branch too.

## UI test

1. In Generate Video, select **AI Shot**. For a low-cost first test, choose **One AI scene + Standard scenes**, **Product cinematic**, and a 12-second output. An approved Hero mockup is enough. The AI clip is 6 seconds; remaining scenes are Standard.
2. Preview the storyboard. AI scenes show a source-image placeholder, not fabricated motion. The actual clip appears only after generation.
3. At Mockup Approval, approve at least one current mockup and click **Continue with approved mockups**. Review the Google cost prompt before confirming. This is the point after which a paid Veo request may be submitted.
4. Wait for the Generate Video job. The worker downloads the generated clip, stores it privately, composites the final silent MP4, and runs technical QA. Review the actual video and product details before approving or exporting.
5. Try **Full AI video** at 8 seconds (one clip), then 15 seconds (two clips with a one-second overlap). For a person-motion preset, use a Lifestyle mockup that already visibly contains an adult and confirm image rights. AI Shot does not synthesize a person into a product-only image.

If a job fails, inspect `artifacts/video-dev/worker-error.log` when using `Start-VideoDev.ps1`, or the terminal running `Start-MediaWorker.ps1`. **Retry checkpoint** reuses already stored clips and resumes known Veo operations. A submission with an uncertain outcome is never automatically sent again. **Regenerate AI clips** explicitly requests fresh clips and shows a new cost confirmation; check Google usage first if a previous submission may have succeeded. Do not delete or recreate the batch merely to retry the video checkpoint.

## Local no-charge verification

From `media-worker`, run `npm.cmd run check`, `npm.cmd test`, and `npm.cmd run smoke:ai`. The AI smoke command generates a synthetic clip locally and verifies the real Remotion/FFmpeg path without calling Google. The paid Veo request itself requires a manual test with a billed key and cannot be validated by these checks.
