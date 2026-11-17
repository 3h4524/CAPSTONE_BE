# Luồng tạo video trước khi cải tiến (worker tự tạo scene AI)

Tài liệu này mô tả luồng tạo video **trước khi có pha tạo clip trong API** (`AiVideo:ClipPhase:Enabled = false`, hoặc code trước khi thêm tính năng này). Media-worker tự gửi từng scene AI lên MiniMax, chờ kết quả rồi mới render bằng Remotion.

Luồng sau cải tiến được mô tả ở mục *AI scenes made by the API* trong [VIDEO_WORKFLOWS.md](VIDEO_WORKFLOWS.md).

## Cấu hình mặc định của luồng cũ

| Cấu hình | Giá trị | Nguồn |
|---|---|---|
| Số container media-worker | 1 | [deploy/compose.yml](../deploy/compose.yml) |
| Job xử lý cùng lúc trong một worker | 1 | [media-worker/src/worker.ts](../media-worker/src/worker.ts) (vòng claim → `await execute(...)`) |
| Scene AI chạy cùng lúc trong một job | 1 (`AI_SCENE_CONCURRENCY=1`) | [deploy/env/media-worker.env.tpl](../deploy/env/media-worker.env.tpl) |
| Nhịp poll trạng thái scene AI | mỗi 5 giây, tối đa 240 lần (khoảng 20 phút) | `worker.ts`, trước khi có `ai-shot-polling.ts` |
| Luồng render Remotion | `min(2, số CPU)` | [media-worker/src/renderer.ts](../media-worker/src/renderer.ts) |
| Heartbeat / lease | mỗi 15 giây / lease 2 phút | `worker.ts`, [MediaJobService.cs](../Application/Features/Workflows/MediaJobService.cs) |
| Hạn chót một job | 30 phút | `worker.ts` |
| Số lần thử lại một job | 3 (`MaximumAttempts`) | [WorkflowRuntime.cs](../Application/Features/Workflows/WorkflowRuntime.cs) |

## Ví dụ dùng trong tài liệu

Batch có 2 product, mỗi product có cả scene AI và scene standard:

- **Product 1:** S1 standard, S2 AI, S3 standard
- **Product 2:** S1 AI, S2 AI, S3 standard

## 1. Toàn cảnh: 2 product chạy nối tiếp

```
API (lúc bấm Run)
  ├─ Run P1 ─ node generate-video ─▶ tạo media_job P1 (status = queued)
  └─ Run P2 ─ node generate-video ─▶ tạo media_job P2 (status = queued)
        │  (trigger DB ghi outbox → MediaStreamDispatcher đẩy vào Redis stream)
        ▼
Redis stream:  [ P1 ][ P2 ]
        │
        ▼  worker đọc từng message một (XREADGROUP COUNT 1)
media-worker (1 container, 1 job tại một thời điểm)

Thời gian ───────────────────────────────────────────────────────────────▶
Worker: [──────────── Product 1 ────────────][──────────── Product 2 ────────────]
                                             ▲ chỉ bắt đầu khi P1 đã complete hoặc fail
```

Mỗi product là **một media job riêng**. Worker chỉ lấy job tiếp theo khi job hiện tại đã xong. Ở chế độ polling, worker gọi `POST /internal/media-jobs/claim` thay cho Redis stream, nhưng vẫn xử lý từng job một.

## 2. Chi tiết một product (Product 1: S1 standard, S2 AI, S3 standard)

```
 media-worker                API (.NET)                  MiniMax            Cloudinary
     │                            │                          │                   │
 ①  │ POST /claim ──────────────▶│ lease job, ký URL tải     │                   │
     │◀── job: payload, assets,   │ (mockup S1–S3, nhạc),     │                   │
     │    upload grants           │ grant upload: ai-shot-1,  │                   │
     │                            │ video, thumbnail          │                   │
     │                            │                           │                   │
     │ ┌─ heartbeat mỗi 15 giây (chạy nền suốt job) ─▶ /heartbeat (gia hạn lease 2 phút)
     │                            │                           │                   │
 ②  │ tải mockup S1, S2, S3 (+ nhạc) ◀────────────────────────────────────────── │
     │ (cả scene standard và AI đều cần ảnh gốc)                                  │
     │                            │                           │                   │
 ③  │ ── chỉ các scene AI, lần lượt từng cái (ở đây chỉ có S2) ──               │
     │ POST /ai-shots/1/start ───▶│ khoá job, đánh dấu submitting                 │
     │   (chờ tối đa 4 phút)      │ lấy key, kiểm duyệt prompt                    │
     │                            │ POST /v2/video_generation ─▶│                 │
     │                            │◀──────────── task_id ───────│                 │
     │◀──────── "polling" ────────│ lưu task_id, status polling │                 │
     │                            │                             │                 │
     │ ⟳ chờ 5 giây               │                             │                 │
     │ POST /ai-shots/1/poll ────▶│ GET /v2/query/.../{task_id}▶│                 │
     │◀──────── "polling" ────────│◀──────── running ───────────│                 │
     │   ... lặp tối đa 240 lần (khoảng 20 phút) ...            │                 │
     │ POST /ai-shots/1/poll ────▶│ GET /v2/query/... ─────────▶│                 │
     │◀──────── "ready" ──────────│◀──── succeeded + url ───────│                 │
     │                            │                             │                 │
     │ POST /ai-shots/1/download ▶│ tải clip từ url MiniMax ───▶│                 │
     │◀──────── bytes mp4 ────────│◀────────────────────────────│                 │
     │ ghi file ai-shot-1.mp4     │                             │                 │
     │ upload clip bằng grant ──────────────────────────────────────────────────▶│
     │ POST /ai-shots/1/stored ──▶│ kiểm tra file trên Cloudinary ──────────────▶│
     │                            │ status = stored             │                 │
     │                            │                             │                 │
 ④  │ RENDER (trong worker, không gọi API)                                       │
     │   Remotion renderMedia (concurrency 2, Chromium):                          │
     │     S1 = ảnh mockup + chuyển động/zoom (standard)                          │
     │     S2 = clip AI vừa tải                                                   │
     │     S3 = ảnh mockup + chuyển động (standard)                               │
     │     + chuyển cảnh + nhạc  → render.mp4                                     │
     │   FFmpeg mã hoá lại (H.264, AAC, faststart) → video.mp4                    │
     │   renderStill khung 10 → thumbnail.png                                     │
     │   ffprobe kiểm tra kỹ thuật (QA)                                           │
     │                            │                             │                 │
 ⑤  │ upload video.mp4 + thumbnail bằng grant ────────────────────────────────▶ │
     │ POST /complete ───────────▶│ kiểm tra file, job = succeeded,               │
     │                            │ lưu video, chuyển sang bước review            │
     │ ack message Redis          │                             │                 │
     ▼
   lấy message tiếp theo → Product 2
```

### Các bước tương ứng trong code

| Bước | Worker | API |
|---|---|---|
| ① Claim | `runMediaStream` → `api(".../claim")` trong [worker.ts](../media-worker/src/worker.ts) | `MediaJobService.ClaimStreamAsync` / `ClaimCoreAsync` |
| ② Tải tài nguyên | `downloadAssets` trong [media-files.ts](../media-worker/src/media-files.ts) | URL ký sẵn từ `IMediaStorage.SignRead` |
| ③ Scene AI | `generateShot` trong `worker.ts` | `AiShotJobService.StartAsync`, `PollAsync`, `DownloadAsync`, `StoredAsync`; gọi MiniMax qua `MiniMaxVideoProvider` |
| ④ Render | `RemotionVideoRenderer.render` trong [renderer.ts](../media-worker/src/renderer.ts) | (không có) |
| ⑤ Hoàn tất | `uploadFile`, `api(".../complete")` | `MediaJobService.CompleteAsync` |
| Lỗi | `api(".../fail")` với mã từ [failures.ts](../media-worker/src/failures.ts) | `MediaJobService.FailAsync` |

## 3. Product 2 (S1 AI, S2 AI, S3 standard)

Giống hệt bước ① đến ⑤. Riêng bước ③ có **2 scene AI chạy nối tiếp**, vì `AI_SCENE_CONCURRENCY=1`:

```
③  S1: start → poll 5s … ready → download → upload → stored
    S2: start → poll 5s … ready → download → upload → stored   (chỉ bắt đầu khi S1 xong)
④  render 1 lần cho cả S1 (AI), S2 (AI), S3 (standard)
```

## 4. Những điểm quan trọng

- **Scene standard không gọi API.** Chúng chỉ được vẽ bằng Remotion từ ảnh mockup ở bước ④.
- **Không render từng scene riêng.** Mọi scene (AI và standard) được ghép trong **một lần render**, sau khi tất cả clip AI đã có.
- **Clip đã lưu được dùng lại.** Khi product chạy lại mà clip AI đã `stored`, API đưa clip đó vào assets (`ai-shots/{n}.mp4`) và bước ③ bỏ qua scene đó.
- **Xử lý lỗi.** Worker gọi `POST /fail` với mã lỗi. API quyết định:
  - **thử lại** (job về `queued`): khi MiniMax quá tải hoặc hết quota (`AiVideoBusy`, chờ 70 giây × số lần), hoặc lỗi tạm thời. Tối đa 3 lần.
  - **fail luôn:** khi MiniMax từ chối scene, nguồn ảnh đã thay đổi, hoặc request tạo video có kết quả không rõ (`AiVideoSubmissionUncertain`, không gửi lại để tránh tính tiền hai lần).
- **Giới hạn mỗi user:** tối đa 2 job đang chạy (`MaximumLeasedPerUser`). Với 1 worker, giới hạn này chưa bao giờ chạm tới.

## 5. Điểm nghẽn của luồng này

```
Product 1:  [ S2 AI: chờ MiniMax ......... ][render]
Product 2:                                          [ S1 AI: chờ ...... ][ S2 AI: chờ ...... ][render]
                ▲ CPU của worker gần như rảnh trong lúc chờ, nhưng worker không nhận việc khác
```

- Thời gian batch xấp xỉ **tổng của (số scene AI × thời gian một scene + thời gian render)** của từng product.
- Phần lớn thời gian worker chỉ **chờ MiniMax**, trong khi worker được cấp 3 CPU và 4 GB RAM để render.
- User gửi sau phải chờ hết các product của user gửi trước (hàng đợi vào trước làm trước).

Đây là lý do cải tiến chuyển bước ③ sang API, để worker chỉ còn làm bước ④ và ⑤.
