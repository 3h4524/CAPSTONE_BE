# Audit: Product On Model — Fabric Detail

## Cấu trúc H3 / H3-Lite hiện tại

Nguồn đối chiếu: [hướng dẫn chính thức MiniMax H3, mục 2, 3.1 và 4.3](https://github.com/MiniMax-AI/MiniMax-H3/blob/main/skills/h3-prompt-writing/references/base-en.txt). H3 mô tả ảnh đầu là trạng thái mở đầu, rồi phát triển hành động đến kết quả; camera được diễn đạt bằng loại chuyển động, biên độ và tốc độ trong câu văn. Áp dụng cùng format cho H3-Lite là chính sách ứng dụng.

- Compiler dùng header I2VA và ba trường hiện có; phần hình ảnh bắt đầu bằng `[Shot 1]`.
- Bản built-in dùng Arc Shot nhỏ/chậm về frame-right, thay push-in: xác lập một vùng chi tiết trong crop đầu → thở nhẹ phù hợp sản phẩm → camera đi theo cung ngắn → dừng nhẹ ở góc hơi nghiêng hơn của cùng chi tiết. Giữ gần như cùng độ cao, khoảng cách, tiêu cự và độ lớn chi tiết; parallax nền nhẹ và phối cảnh tự nhiên thể hiện chuyển động.
- Camera dùng `arcs around the model toward frame-right with small amplitude at slow speed`; mặc định scene là `orbit_right`, `source_view`, `source_image`. Không tiến gần, zoom, pull-back, focus pull hoặc đi sau vai/lộ mặt lưng. Rút ngắn cung nếu crop sát hoặc chi tiết gần mép khung. Không áp số độ của Profile Tracking cho close-up. P1 thay phần chuyển động cơ thể mặc định; sản phẩm cứng không nhận nếp vải.
- Ảnh mẫu người dùng crop sát vai/ngực, hình in bị cắt ở mép phải. Prompt giữ vùng đang thấy và không hoàn thiện chữ/hình ngoài ảnh; không hardcode màu áo, chữ hoặc nền của mẫu vào template chung.
- `fabric_detail_guardrails` được giữ nguyên sau Gemini. Editor có brief arc riêng; nếu mất cụm camera yêu cầu hoặc có một số chỉ dẫn tiến gần/nhanh/lớn thì preparation fallback nguyên draft compiler. Đây là kiểm tra văn bản bảo thủ, có thể từ chối cách diễn đạt tương đương và không chứng minh camera/focus thực tế.
- Catalog vẫn v12: entry lưu có đúng base prompt built-in push-in cũ được nâng sang hướng mới và đổi mặc định push-in thành `orbit_right`. Custom base prompt, custom start frame và camera mặc định khác push-in được giữ. Các subtemplate khác giữ hướng riêng. Nội dung/settings mới thay fingerprint/cache cho preview mới; snapshot đã xác nhận giữ prompt cũ. Cần rebuild/restart backend rồi tạo preview mới.

Test hồi quy: `CompileTrace_FabricDetail_DevelopsSameOpeningDetailWithSlowArc` (H3/Lite, 4/6/8 giây), `Plan_FabricDetail_UsesSupportedSourceAngleAndLighting`, `Resolve_PersistedFabricPush_UpgradesOnlyKnownBuiltInDirection`, `CompileTrace_DetailOrBrandWithP1_PreservesExplicitActionWithoutDefaultBodyMotion`, `CompileTrace_CustomDetailOrBrand_PreservesCatalogDirection`, `CompileTrace_DetailOrBrandRigidProduct_DoesNotInventFabricMotion`, `PrepareAsync_DetailOrBrand_RetainsSpecificGuardrailsAndGroundedShot`, `PrepareAsync_FabricRewriteChangesArc_RetainsExactCompilerDraft`. Các test dùng mock; chưa có A/B video mới để kết luận chất lượng hoặc độ chính xác texture/chữ in.

Trích chỉ dẫn camera H3:

```text
Starting from the viewing direction established by the opening image, the camera arcs around the model toward frame-right with small amplitude at slow speed. Begin gently, glide along one short, shallow curve, then ease softly to a stop at a slightly more side-on view of the same detail. Keep approximately the same camera height, distance, focal length and detail scale, with the lens aimed at the same product region throughout.
```

## Kiểm chứng cấu trúc H3 cho Fabric Detail và Brand Focus

```powershell
dotnet test tests/APCS.Application.UnitTests/APCS.Application.UnitTests.csproj -- --filter "FullyQualifiedName~MiniMaxH3PromptTests|FullyQualifiedName~VideoPromptPreparationServiceTests|FullyQualifiedName~AiVideoPromptCompilerTests|FullyQualifiedName~AiVideoModelRoutingTests|FullyQualifiedName~VideoPlanningServiceTests"
```

Kết quả sau khi đổi Fabric Detail sang arc: **Passed: 184, Failed: 0, Skipped: 0**, exit 0. Test dùng mock, không tạo video trả phí; Brand Focus vẫn giữ push-in.

`dotnet test Capstone.sln`: exit 1. Application **Passed: 1326, Failed: 11, Skipped: 0**; Infrastructure unit **Passed: 424, Failed: 2, Skipped: 0**; Common **Passed: 46**; Domain **Passed: 28**; Infrastructure integration **Skipped: 8**. Tên 13 failure trùng nhóm đã ghi trong audit Profile Tracking trước thay đổi này: duration 3 giây, transition variants/fingerprint, media QA FullDecode, MiniMax duration 5 giây, generated schema table count. First failure: `Plan_FourScenes_DistributesAllFramesWithoutChangingDuration (3)`. API build mặc định bị `APCS.Api` PID 15536 khóa DLL (`MSB3027/MSB3021`), không dừng tiến trình đang chạy.

Build API vào thư mục riêng:

```powershell
dotnet build API/API.csproj --no-restore -p:OutputPath=D:/FPT/Capstone/source/Backend/CAPSTONE_BE/.git/fabric-detail-arc-api-build/
```

Kết quả: **exit 0, 0 errors, 13 warnings** của repository hiện có. API đang chạy chưa nạp mã mới; cần rebuild/restart theo quy trình phát triển trước khi tạo preview mới. Chưa có thử nghiệm chất lượng video thực tế.

Các mục v10 dưới đây là lịch sử prompt provider-neutral, không phải prompt H3 hiện tại.

## Phạm vi và mục tiêu

Subtemplate: `lifestyle_in_use / fabric_detail`. Phiên bản catalog Product On Model tăng từ v9 lên v10. Đây là cải tiến trước khi thử video, chưa có kết quả thực tế để kết luận chất lượng tốt hơn.

Fabric Detail ưu tiên một vùng chi tiết đã có trong ảnh mở đầu, camera tiến gần nhẹ và người mẫu thở nhẹ. Không thêm bước chân hoặc quay đầu như Natural Motion, vì chuyển động đó có thể làm chi tiết rời khung hoặc mất nét. Không giả định áo thun, màu áo, loại nền hoặc đặc điểm vật liệu chưa được xác minh.

Các ví dụ dưới đây dùng sản phẩm `T-shirt`, thời lượng 4 giây, default settings, không có P1 hoặc RAG.

## Prompt trước v10

```text
Create one continuous editorial close-up of the exact garment on the model from the approved source image. Frame a construction or surface detail that is actually visible, such as fabric texture, a seam, stitching, collar, shoulder, or print edge; do not invent missing garment features. The camera performs a slow, smooth cinematic push-in at a three-quarter camera angle in a close-up composition for the full 4 seconds. Keep the model mostly still with subtle breathing and natural fabric settling. Warm natural light with soft, realistic shadows. Let soft light reveal real texture, use shallow depth of field, and keep the selected product detail in precise focus.

Product: T-shirt. Match the approved image for its visible design, colors, fit and proportions.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.
```

## Prompt sau v10

```text
Create one continuous 4-second live-action editorial product-detail advertisement using the approved source image as the opening frame.

Product: T-shirt. Keep the product and any visible design as the visual focus. Match the approved image for its visible design, colors, fit and proportions.

Follow the same visible product detail already emphasized by the opening crop throughout the shot. Preserve that region's existing texture, construction and any visible print edges or lettering. Do not switch to a different detail or add features absent from the source. Gentle breathing causes small movement in the visible torso and existing fabric folds. Keep the original hand placement. No deliberate gestures, steps or body turns. From the first frame, the camera performs a slow, smooth cinematic push-in, continuing in a steady, uninterrupted glide. Keep the approach shallow and the change in framing small. Maintain the viewing direction established by the opening image and keep focus on the same product surface throughout, without a focus pull or sudden reframing. Preserve the background and setting shown in the source image. Preserve the lighting shown in the source image.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.
```

## Các phần thay đổi

| Phần | Trước | v10 |
|---|---|---|
| Context | Loại sản phẩm sau toàn bộ template | Loại sản phẩm và thiết kế nhìn thấy đặt trước chỉ dẫn chuyển động, giống Natural Motion/Right Camera Arc. |
| Vùng chi tiết | Cho model chọn từ danh sách texture, seam, collar, shoulder… | Bám một vùng chi tiết đã được nhấn mạnh trong crop mở đầu; không nhảy sang chi tiết khác. |
| Camera | Push-in chậm trong 4 giây | Bắt đầu từ frame đầu, đi liên tục với biên độ nhỏ. Không thêm phần trăm phóng lớn. |
| Góc nhìn | Ép góc three-quarter | Giữ hướng nhìn của ảnh nguồn (`source_view`). |
| Ánh sáng/nền | Warm natural, shallow depth of field | Giữ ánh sáng và nền nguồn (`source_image`), không bắt model thêm bokeh hoặc nền cụ thể. |
| Người mẫu | Mostly still, breathing/fabric settling chung | Thở nhẹ, giữ tay; không thêm gesture, bước hoặc xoay thân khi không có P1. |
| Vật liệu | Luôn mô tả garment/fabric | Apparel dùng nếp vải; tote dùng quan hệ túi/quai với người mẫu; mug/poster/phone case giữ hình dạng cứng. |
| P1 | Có thể mâu thuẫn giữa mostly still và action plan | Có P1 thì bỏ chỉ dẫn cơ thể mặc định; giữ hành động được chọn. |

## Prompt start frame

Trước:

```text
Create a photorealistic close editorial keyframe of the model wearing the exact product, cropped around the upper torso and a real fabric or artwork detail. Keep texture, stitching, print edges, lettering and colors sharp and unchanged. Use a slight three-quarter angle, soft natural light and no invented details.
```

Sau:

```text
Create a photorealistic close editorial keyframe of one real product detail visible in the source image, retaining its relationship to the model. Keep existing texture, construction, print edges, lettering and colors clear. Preserve the source viewing direction, lighting and setting. Do not invent stitching, artwork or material features.
```

Frame đã duyệt không tự thay đổi. Scene mới lấy start-frame direction mới; scene đã lưu giữ `studioPrompt` riêng. Không tự sửa các scene đó hoặc buộc tạo lại ảnh đang dùng.

## Phiên bản và giới hạn

- Catalog v6–v9 được nâng lên v10 khi resolve. Fabric Detail nhận prompt mới, default push-in, source angle và source lighting. Default start-frame direction cũ được thay; direction do người dùng chỉnh riêng được giữ.
- Nội dung Natural Motion v9 và Right Camera Arc v8 không bị thay đổi khi nâng từ v9. Các bản catalog cũ hơn vẫn nhận các nâng cấp đã có của hai template đó.
- `source_view` có nhãn **Source image angle** trong giao diện. Advanced Controls vẫn tắt theo cấu hình hiện có.
- Nếu ảnh mở đầu là cảnh rộng thay vì một crop chi tiết, prompt không đảm bảo tự tạo được macro sắc nét. Nên chọn hoặc duyệt crop đủ gần của chi tiết muốn quay trước khi tạo video. Không ép model tái tạo sợi vải/chữ không nhìn rõ trong ảnh.
- Cần thử với T-shirt/hoodie, artwork có chữ và ít nhất một nền studio trơn. Kiểm tra vùng focus có giữ nguyên, có nhảy crop/mất nét, có thêm texture hoặc biến đổi chữ và màu hay không.
- Chưa tạo video MiniMax mới trong thay đổi này. Unit test chỉ xác minh việc compile và các quy tắc, không chứng minh chất lượng video.

## Kiểm thử thực hiện

- 42/42 kiểm thử compiler và planner được chọn đã qua, bao gồm context, source angle/lighting, các nhóm sản phẩm, P1 và nâng cấp catalog.
- Frontend `tsc --noEmit` đã qua.
- `dotnet test Capstone.sln -c Release -v:q` không gặp khóa DLL của API Debug nhưng vẫn còn 11 lỗi application tests và 2 lỗi infrastructure tests cùng nhóm đã ghi nhận trước đó: duration/transition, media QA, MiniMax settings và số bảng trong schema. Toàn solution chưa qua.
