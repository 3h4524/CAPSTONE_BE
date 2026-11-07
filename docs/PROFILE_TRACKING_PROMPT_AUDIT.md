# Audit: Product On Model — Profile Tracking

## Hành vi hiện tại

Tên hiển thị được khôi phục về **Profile Tracking**; mã `profile_tracking` giữ nguyên để các scene đang lưu vẫn resolve được. Catalog chuẩn hóa tên/mô tả của entry built-in ngay cả khi dữ liệu lưu còn tên Right Camera Arc. Base prompt tùy chỉnh vẫn giữ hướng riêng.

Phản hồi mới cho biết clip quay quá nhanh và đi ra phía lưng. Bản trước yêu cầu `large amplitude at fast speed`, đi qua profile giữa shot và tới góc sau ba phần tư ở cuối; đó là chỉ dẫn quá mạnh cho mục tiêu hiện tại. Chưa có clip cục bộ để đo góc/tốc độ thực tế; không kết luận prompt là nguyên nhân duy nhất của mọi lỗi chuyển động.

| Thành phần | Quy định hiện tại |
|---|---|
| Loại chuyển động | Arc shot sang phía phải của khung mở đầu; camera thực sự di chuyển với parallax nền nhẹ |
| Biên độ | Khoảng **30–40° tổng cho cả shot**, không quá 40° so với hướng nhìn mở đầu; không phải độ/giây |
| Tốc độ | `with small amplitude at slow speed`, nhịp thong thả, ổn định, vào/ra chuyển động nhẹ |
| Điểm kết thúc | Góc nhìn hơi nghiêng hơn trong phạm vi front three-quarter tới side-profile; không đi qua vai ra phía lưng |
| Ảnh đầu gần profile | Rút ngắn cung; giới hạn side-profile được ưu tiên hơn mục tiêu 30–40° |
| Người mẫu | Giữ tư thế, hướng đầu/gaze và vị trí tay; thở nhẹ; không đi bộ hoặc xoay theo camera mặc định |
| Framing | Gần như giữ độ cao, khoảng cách và kích thước chủ thể; không rung như đang chạy hoặc whip pan |
| Sản phẩm POD | Giữ thiết kế/màu/fit/vị trí in; cho phép foreshortening tự nhiên; không sao chép graphic ra lưng hoặc dựng thêm chi tiết |
| Model | Cùng policy cho H3 và H3-Lite; giữ model và đơn giá đã chọn |

Đây là lựa chọn tại application layer. Không có bằng chứng MiniMax thực hiện chính xác số độ hoặc bản này đã đạt chất lượng video mong muốn. Các con số là mục tiêu diễn đạt trong prompt, không phải camera API parameter.

## Đối chiếu tài liệu chính thức

Nguồn: [MiniMax H3 — base-en.txt, mục 4.3](https://github.com/MiniMax-AI/MiniMax-H3/blob/main/skills/h3-prompt-writing/references/base-en.txt).

Hướng dẫn mô tả chuyển động theo loại, biên độ và tốc độ, viết thành hành động tự nhiên trong cảnh. Arc Shot là camera đi theo cung quanh chủ thể; small amplitude và slow speed thể hiện phạm vi nhỏ, nhịp chậm. Mục 3.1 yêu cầu I2VA phát triển từ trạng thái của ảnh đầu. Cách triển khai giữ các nguyên tắc đó, đồng thời thêm giới hạn góc/điểm dừng của dự án. Tài liệu không đưa ra benchmark kiểm chứng policy 30–40° hoặc sự tương đương chất lượng H3-Lite/H3.

## Compiler, Gemini và fallback

- Compiler tạo bản I2VA có first-frame header và ba trường chuẩn; Profile có direction riêng, không mượn bước đi/head turn của Natural Motion.
- `profile_orbit_guardrails` giữ giới hạn cung chậm, không đi sau lưng và quy tắc bảo toàn sản phẩm. Guardrail được giữ nguyên cả khi Gemini biên tập.
- Gemini vẫn có hai bước hiện tại: phân tích pixels first frame, rồi biên tập continuation. Editor nhận policy cung nhỏ/chậm, tổng 30–40°, rút ngắn nếu gần profile và giữ ranh giới phía trước.
- Bộ kiểm tra văn bản yêu cầu continuation giữ `with small amplitude at slow speed` và `30-40 degrees`, loại các cụm rear three-quarter, large amplitude, at fast speed hoặc degrees per second. Nếu không đạt, giữ **bản compiler chậm mới**, không quay lại hướng lớn/nhanh cũ.
- Đây là kiểm tra bảo thủ theo cụm từ, có thể loại cả cách viết tương đương hoặc câu phủ định chứa cụm cấm. Nó không hiểu đầy đủ ngữ nghĩa, không đo tốc độ/góc quay, và không chứng minh video bảo toàn thiết kế.
- Không thêm lượt Gemini, đổi timeout, credentials, model/giá hoặc logic tạo video. Việc fallback do Gemini HTTP 503 vẫn thuộc đường xử lý hiện có, không được khẳng định đã giải quyết ở thay đổi camera này.
- Mã template/catalog version vẫn là `profile_tracking` / 12. Draft/guardrail mới làm đổi fingerprint/cache key cho preview mới. Snapshot đã xác nhận vẫn giữ prompt cũ; cần mở preview mới sau khi rebuild/restart backend. FE đọc tên từ API catalog; không cần đổi mã scene hoặc sửa database để đổi label built-in.
- Explicit P1 giữ kế hoạch hành động riêng; custom base prompt không bị compiler thay bằng bản built-in.

## Prompt compiler thực tế

Fixture: T-shirt / Lifestyle / Profile Tracking / 6 giây / không P1 hoặc RAG. Trích từ compiler đã build bằng probe cục bộ, không gọi provider. **3745 ký tự**; prompt H3 và Lite giống nhau, model/đơn giá vẫn riêng. Đây là bản chưa qua Gemini, cũng là nội dung fallback khi preparation lỗi.

```text
For the target video, at 0.00 seconds into the target video, <Picture 1> (from [Shot 1]) is fully referenced.

integrated_multimodal_description: [Shot 1] The opening visual style, composition, subject appearance, product, setting and lighting match <Picture 1>. Continue from that opening state with the following movement and preservation requirements.

One continuous 6-second product-on-model shot develops from the approved opening image.

The shot features the same T-shirt from the opening image. Its design, colors, fit and proportions remain consistent during the shallow change in viewpoint. Preserve artwork where visible, allowing natural perspective and foreshortening.

The model remains in the opening position with the same body orientation, head direction and gaze. Gentle breathing causes small movement in the visible torso and existing fabric folds. The hands keep their original placement. The model does not walk, step or turn to follow the camera. The camera begins at the viewing direction established by the opening image and arcs around the model toward the right-hand side of the opening image with small amplitude at slow speed. Spread a short, shallow arc of about 30-40 degrees in total across the entire shot, never more than 40 degrees from the opening viewing direction. The movement starts gently, glides at an unhurried, even pace, then eases softly to a stop at the end, with stable gimbal-like motion and no shake, abrupt acceleration or whip movement. Visible but restrained background parallax establishes that the camera is actually moving. The final view is slightly more side-on, remaining within the front three-quarter to side-profile range; stop at the side-profile boundary without passing behind the shoulder or revealing the back. If the opening image is already close to profile, shorten the arc to stay on the front-facing side of that boundary. Camera height and distance remain approximately constant, with the model centered at approximately the same size and the product in focus. The front artwork becomes naturally foreshortened as the angle changes; its content and placement remain unchanged. Preserve the lighting shown in the source image. Preserve the source setting and natural shadows.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and natural occlusion as surfaces turn out of view. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks.

Camera-path and design-visibility constraint: use a slow, smooth, shallow arc toward frame-right, about 30-40 degrees across the whole shot and never more than 40 degrees from the opening view. Shorten the path when needed to stop at the side-profile boundary. Do not travel behind the model or reveal the back. Keep the movement stabilized and unhurried, with gentle starts and stops rather than a fast sweep, running-camera bounce or whip pan. Do not turn the model merely to keep facing the camera; an explicitly selected action plan still takes precedence for the person's actions. Allow natural foreshortening of the front design as the angle changes. Do not twist or redraw the product to keep that design facing the lens. Never copy the front graphic onto the back or add artwork, lettering, logos or unsupported construction details to newly revealed surfaces.

overall_soundscape: Quiet ambient sound only. No dialogue, speech, singing or diegetic music.

non_diegetic_music: N/A
```

## Kiểm chứng

| Requirement | Evidence |
|---|---|
| Cung nhỏ/chậm, tổng góc có giới hạn, gần profile phải rút ngắn; giữ pose, thiết kế và format | `CompileTrace_ProfileTracking_UsesSlowBoundedArcAndKeepsOpeningPose` — H3/Lite, 4 và 6 giây |
| Planner/preview dùng cùng prompt và controls orbit_right/source_view/source_image | `Plan_ProfileTracking_UsesSupportedRightOrbitAndSourceLighting` — H3/Lite |
| Biên tập Gemini giữ ảnh phân tích và guardrail chậm/không đi sau lưng | `PrepareAsync_ProfileTracking_SendsArcBriefWithoutNaturalMotionAndRetainsGrounding` — mock H3/Lite |
| Editor trả hướng thiếu thông tin, nhanh, rear view, góc/giây hoặc large amplitude thì fallback bản chậm | `PrepareAsync_ProfileRewriteViolatesSlowArc_RetainsBoundedCompilerPrompt` |
| Catalog lưu tên Right Camera Arc được đổi lại, các shot khác giữ nguyên | `Catalog_PersistedRightCameraArc_RestoresProfileNameWithoutChangingOtherShots` |
| P1/custom direction/rigid products không nhận chuyển động sai | Các case ProfileTrackingWithP1, CustomProfileTracking, ProfileTrackingRigidProduct trong `MiniMaxH3PromptTests` |

Focused command:

```powershell
dotnet test tests/APCS.Application.UnitTests/APCS.Application.UnitTests.csproj -- --filter "FullyQualifiedName~MiniMaxH3PromptTests|FullyQualifiedName~VideoPromptPreparationServiceTests|FullyQualifiedName~AiVideoPromptCompilerTests|FullyQualifiedName~AiVideoModelRoutingTests|FullyQualifiedName~VideoPlanningServiceTests"
```

Kết quả sau khi tăng cung lên 30–40°: **Passed: 147, Failed: 0, Skipped: 0**, exit 0. Tests dùng mock; không gọi Gemini/MiniMax thật, không phát sinh video trả phí và không thay `.env` hoặc database.

Toàn solution `dotnet test Capstone.sln` sau thay đổi 30–40°: exit 1. Application **Passed: 1.289, Failed: 11, Skipped: 0**; Infrastructure unit **Passed: 424, Failed: 2, Skipped: 0**; Common **Passed: 46**; Domain **Passed: 28**; Infrastructure integration **Skipped: 8**. Tên 13 failure trùng nhóm đã ghi nhận ở lần kiểm tra trước: planner duration 3 giây (1), transition variants (8), transition fingerprint (1), media QA FullDecode (1), MiniMax duration 5 giây (1), generated schema table count (1). First failure: `Plan_FourScenes_DistributesAllFramesWithoutChangingDuration (3)` truy cập failed Result. Không sửa các failure ngoài phạm vi camera.

API build mặc định trong solution bị process `APCS.Api` PID 34008 khóa các DLL (`MSB3027/MSB3021`). Không dừng process đang chạy. Ở lần kiểm tra trước thay đổi 30–40°, đã kiểm chứng build vào thư mục riêng:

```powershell
dotnet build API/API.csproj --no-restore -p:OutputPath=D:/FPT/Capstone/source/Backend/CAPSTONE_BE/.git/profile-slow-arc-api-build/
```

Kết quả lần trước: **exit 0, 0 errors, 13 warnings** từ package/repository hiện có. Build này không nạp thay đổi vào API đang chạy. Chuỗi prompt 30–40° trong audit được so sánh nguyên văn với output probe hiện tại và khớp; H3/Lite đều 3.745 ký tự.

## Giới hạn và bước thử video

Cần rebuild/restart backend, reload catalog rồi tạo preview Profile Tracking mới. Kiểm tra prompt có cung nhỏ/chậm và giới hạn tổng góc; tên phải là Profile Tracking. Không dùng lại snapshot đã xác nhận theo hướng lớn/nhanh cũ.

Chưa có A/B video mới. Cần kiểm tra cung quay thực tế, gia tốc/rung, điểm dừng phía trước profile và tính ổn định của hình in trên clip trả về. Một ảnh đầu không xác minh được mặt lưng, vì vậy bản hiện tại chủ động tránh reveal mặt lưng. Không hứa chính xác góc, chất lượng ngang H3 hoặc bảo toàn chữ/logo chỉ từ unit test.
