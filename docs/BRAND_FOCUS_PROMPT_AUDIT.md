# Audit: Product On Model — Brand Focus v12

## Cấu trúc H3 / H3-Lite hiện tại

Nguồn đối chiếu: [hướng dẫn chính thức MiniMax H3, mục 2, 3.1 và 4.3](https://github.com/MiniMax-AI/MiniMax-H3/blob/main/skills/h3-prompt-writing/references/base-en.txt). Format I2VA dùng ảnh đầu làm trạng thái mở đầu, tiếp diễn hành động và camera đến kết quả trong một shot. Áp dụng cùng format cho Lite là chính sách ứng dụng.

- Compiler dùng header I2VA và ba trường hiện có; phần hình ảnh bắt đầu bằng `[Shot 1]`. Context sản phẩm được viết thành câu văn thay nhãn `Product:`.
- Vùng artwork/lettering/logo đang nhìn thấy làm điểm neo trong bố cục gồm cả người mẫu và sản phẩm. Người mẫu nhẹ nhàng hướng mắt lên, cằm nâng theo một chút, rồi ổn định nhìn hơi cao hơn ống kính và thở tự nhiên. Đây là một hành động ngước nhìn liên tục; thân giữ hướng và tay giữ vị trí ban đầu, không thêm bước chân, xoay thân hoặc đưa tay qua hình in. Nếu đã nhìn lên ở ảnh đầu thì chỉ điều chỉnh rất nhỏ, không ngửa cổ quá mức.
- Camera đồng thời `pushes in with small amplitude at slow speed`. Giữ mặt/đầu trong khung khi có trong ảnh đầu, cùng vùng thiết kế đang thấy; rút ngắn đường tiến để giữ cả hai thay vì crop sát logo. Không rack focus qua lại giữa mặt và hình in hoặc dựng thêm mặt bị crop khỏi ảnh đầu. Giữ hướng nhìn, ánh sáng và nền nguồn. P1 thay hành động ngước nhìn mặc định. Custom base prompt hoặc camera mặc định khác push-in giữ hướng riêng; sản phẩm cứng giữ hình dạng.
- `brand_focus_guardrails` được giữ nguyên sau Gemini; phần performance mặc định chỉ được thêm khi không có P1. Editor nhận brief riêng cho động tác mắt/cằm và camera. Khi thiếu cụm push-in chậm hoặc thiếu phần ngước nhìn/cằm mặc định, preparation giữ nguyên draft compiler. P1 không bị ép thêm động tác này. Kiểm tra cụm từ bảo thủ có thể từ chối cách diễn đạt tương đương; không chứng minh hành động, logo/chữ hoặc chất lượng video thực tế.
- Catalog vẫn v12; nội dung mới đổi fingerprint/cache cho preview mới. Snapshot đã xác nhận giữ prompt cũ. Cần rebuild/restart backend rồi tạo preview mới.

Test hồi quy: `CompileTrace_BrandFocus_CombinesNaturalUpwardGazeWithSlowPushIn` (H3/Lite, 4/6/8 giây), `CompileTrace_DetailOrBrandWithP1_PreservesExplicitActionWithoutDefaultBodyMotion`, `CompileTrace_CustomDetailOrBrand_PreservesCatalogDirection`, `CompileTrace_DetailOrBrandRigidProduct_DoesNotInventFabricMotion`, `PrepareAsync_DetailOrBrand_RetainsSpecificGuardrailsAndGroundedShot`, `PrepareAsync_BrandRewriteDropsUpwardPerformance_RetainsExactCompilerDraft`, `PrepareAsync_BrandWithP1_PreservesExplicitPerformanceWithoutDefaultGaze`. Test dùng mock; chưa gọi Gemini/MiniMax thật hoặc đánh giá A/B clip mới.

## Kiểm chứng hành động ngước nhìn và push-in chậm

```powershell
dotnet test tests/APCS.Application.UnitTests/APCS.Application.UnitTests.csproj -- --filter "FullyQualifiedName~MiniMaxH3PromptTests|FullyQualifiedName~VideoPromptPreparationServiceTests|FullyQualifiedName~AiVideoPromptCompilerTests|FullyQualifiedName~AiVideoModelRoutingTests|FullyQualifiedName~VideoPlanningServiceTests"
```

Kết quả: **Passed: 190, Failed: 0, Skipped: 0**, exit 0. Fabric Detail vẫn giữ Arc Shot. Snapshot đã xác nhận không được viết lại; cần restart/rebuild backend và tạo preview mới.

`dotnet test Capstone.sln`: exit 1. Application **Passed: 1332, Failed: 11, Skipped: 0**; Infrastructure unit **Passed: 424, Failed: 2, Skipped: 0**; Common **Passed: 46**; Domain **Passed: 28**; integration **Skipped: 8**. Tên 13 failure trùng nhóm đã ghi nhận trước: duration 3 giây, transition variants/fingerprint, media QA FullDecode, MiniMax duration 5 giây và generated schema table count. First failure: `Plan_FourScenes_DistributesAllFramesWithoutChangingDuration (3)`. API đang chạy PID 15536 khóa DLL ở output mặc định (`MSB3027/MSB3021`).

Build vào thư mục riêng để giữ tiến trình API đang chạy:

```powershell
dotnet build API/API.csproj --no-restore -p:OutputPath=D:/FPT/Capstone/source/Backend/CAPSTONE_BE/.git/brand-upward-gaze-api-build/
```

Kết quả: **exit 0, 0 errors, 13 warnings** của repository hiện có. Chưa đánh giá video thực tế; ảnh đầu nên có mặt để động tác ngước nhìn hiện rõ, không dùng prompt để dựng thêm mặt đã bị crop.

Các mục dưới đây lưu lịch sử thay đổi provider-neutral v12, không phải prompt H3 hiện tại.

## Phạm vi và mục tiêu

Subtemplate: `lifestyle_in_use / brand_focus`. Phiên bản catalog Product On Model tăng từ v11 lên **v12**. Đây là cải tiến prompt trước khi đánh giá video mới, chưa phải bằng chứng chất lượng render đã tốt hơn.

Brand Focus ưu tiên thiết kế tổng thể đang nhìn thấy: artwork, lettering, logo; nếu không thấy branding, ưu tiên dáng, màu và cấu trúc hiện có của sản phẩm. Khác với Fabric Detail, scene này không chuyển sang soi một mép in, đường may hay chất liệu. Khác với Natural Motion, scene này không yêu cầu bước chân hoặc quay đầu. Khác với Right Camera Arc, camera không quay quanh người mẫu.

Ví dụ dưới đây dùng T-shirt, 4 giây, cấu hình mặc định, không có P1/RAG. T-shirt chỉ là giá trị context của ví dụ, không được ghi cố định vào template.

## Prompt trước v12

```text
Create one continuous medium product-on-model editorial shot from the approved source image. Use torso framing and keep the garment's visible artwork, lettering, or logo as the visual focus; if the garment has no branding, emphasize its real silhouette, color blocking, and construction instead. The camera performs a slow, smooth cinematic push-in at an eye-level camera angle in a medium composition for the full 4 seconds. The model makes a small confident posture adjustment while keeping the product naturally visible. Soft golden-hour light with gentle warm highlights. Preserve accessories only when they already exist in the source. Use natural fabric folds, warm commercial color, shallow depth of field, and restrained film grain.

Product: T-shirt. Match the approved image for its visible design, colors, fit and proportions.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.
```

## Prompt sau v12

```text
Create one continuous 4-second live-action brand-focused product-on-model fashion advertisement using the approved source image as the opening frame.

Product: T-shirt. Keep the product and any visible design as the visual focus. Match the approved image for its visible design, colors, fit and proportions.

Keep the product's existing artwork, lettering or logo as the visual focus. If no branding is visible, emphasize the existing silhouette, colors and construction instead. Keep the design area visible in the opening image inside the frame and in focus throughout. Do not invent branding or reveal design areas absent from the source. Starting from the pose in the opening image, the model makes one small, relaxed posture adjustment: the shoulders gently lower and settle while the torso keeps its original direction. Keep the original hand placement. The garment moves naturally with the body, forming small folds where the fabric bends. After this small adjustment, continue with gentle breathing. No steps, deliberate body turns or gestures across the product. From the first frame, the camera performs a slow, smooth cinematic push-in, continuing in a steady, uninterrupted glide. Keep the approach shallow so the visible design stays within the frame. Maintain the viewing direction established by the opening image, without orbiting around the model or shifting focus away from the product. Preserve the background and setting shown in the source image. Preserve the lighting shown in the source image.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.
```

## Các phần thay đổi

| Phần | Trước | v12 |
|---|---|---|
| Context | Loại sản phẩm đứng sau toàn bộ chỉ dẫn scene | Đưa loại sản phẩm và yêu cầu bám thiết kế nhìn thấy lên ngay sau câu mở đầu, giống ba template đã cải tiến. Không lấy raw design prompt để suy đoán nội dung ảnh. |
| Trọng tâm | Artwork/logo hoặc silhouette, chưa mô tả rõ giới hạn crop | Giữ vùng thiết kế đã thấy trong ảnh mở đầu ở trong khung và rõ nét. Không hoàn thiện phần thiết kế đang bị che/cắt khỏi ảnh. Nếu không thấy branding, dùng dáng, màu và cấu trúc hiện có. |
| Người mẫu | `small confident posture adjustment` khá chung | Một động tác nhỏ cụ thể: vai nhẹ nhàng hạ xuống rồi ổn định, thân giữ hướng ban đầu; sau đó thở nhẹ. Giữ vị trí tay, không bước hoặc cố ý xoay thân, không đưa tay ngang qua sản phẩm. |
| Phản ứng sản phẩm | `natural fabric folds` cho mọi loại sản phẩm | Dùng helper hiện có `NaturalProductMotion`: áo/hoodie có nếp vải nhỏ khi vải uốn; túi/quai chuyển động theo quan hệ hiện có; mug/poster/phone case giữ hình dạng cứng; loại khác dùng mô tả tổng quát. |
| Camera | Push-in nhưng chưa yêu cầu bắt đầu ngay, giữ nhịp và giới hạn crop | Push-in nông từ khung đầu, trượt liên tục với nhịp đều; giữ hướng quan sát nguồn và không crop thêm vùng thiết kế đang nhìn thấy. Không đặt phần trăm phóng đại hoặc vận tốc giả định. |
| Góc và ánh sáng | Eye-level, golden hour, warm commercial color, shallow DOF, film grain | Mặc định `source_view` và `source_image`; giữ nền và ánh sáng nguồn. Bỏ yêu cầu thêm màu ấm, grain hoặc DOF có thể làm thiết kế kém rõ hay thay đổi phong cách nguồn. |
| P1 | Động tác mặc định vẫn tồn tại khi action plan chọn hành động khác | `{{BRAND_MOTION}}` dùng chỉ dẫn theo action plan khi có PrimaryAction, thay cho động tác vai và các lệnh cấm hành động mặc định. |
| Bảo toàn sản phẩm | Guardrail chung Product On Model | Giữ nguyên; vẫn cho phép thay đổi tự nhiên do nếp vải, tư thế, phối cảnh và che khuất ngắn. |

Động tác vai chỉ tạo chuyển động nhẹ ở thân trên, phù hợp mục tiêu giữ thiết kế làm trọng tâm. Không yêu cầu người mẫu kéo áo, vuốt hình in, chỉnh cổ áo hay giữ artwork phẳng bất chấp chuyển động cơ thể. Nếu video vẫn quá tĩnh, cần đánh giá clip thực tế trước khi tăng mức chuyển động.

## Prompt tạo start frame

Trước:

```text
Create a photorealistic medium-close, eye-level fashion keyframe that is nearly front-facing. Center the garment artwork, logo or silhouette and keep it flat, readable and fully unobstructed. The model stands naturally with hands outside the product area, clean commercial light and a simple coherent background.
```

Sau:

```text
Create a photorealistic medium-close product-on-model keyframe using the source image as the reference. Frame the existing visible artwork, lettering or logo clearly; if none is visible, emphasize the product's real silhouette and colors. Keep hands from covering the design where the source pose allows. Preserve the source viewing direction, lighting, setting and natural product shape. Do not add branding or complete hidden design areas.
```

Bỏ yêu cầu ép góc gần chính diện, làm sản phẩm phẳng và đổi sang nền/ánh sáng thương mại. Prompt mới ưu tiên vùng thiết kế nhìn thấy, giữ hướng nhìn, bối cảnh, ánh sáng và hình dạng tự nhiên của ảnh nguồn. Chỉ tránh tay che thiết kế khi tư thế nguồn cho phép, không tự bịa phần bị khuất.

Ảnh start frame đã duyệt không bị thay bằng thay đổi này. Prompt studio đã lưu riêng trong scene không tự được sửa.

## Catalog đã lưu và tương thích

- Catalog từ v6 đến v11 tiếp tục theo các bước nâng cấp lịch sử tương ứng. Khi `brand_focus.BasePrompt` khớp nguyên văn prompt mặc định cũ, đổi sang prompt v12, dùng push-in, source-view, source-lighting. Giữ các cấu hình khác.
- Với Brand Focus mặc định cũ, StartFramePrompt trống hoặc khớp nguyên văn bản mặc định cũ sẽ nhận prompt frame mới. Prompt frame do người dùng chỉnh riêng được giữ lại.
- BasePrompt Brand Focus do người dùng chỉnh riêng được giữ cùng defaults của nó; không áp nâng cấp Brand Focus mặc định lên prompt đó. Cơ chế điền StartFramePrompt còn trống từ catalog vẫn hoạt động như trước.
- Khi nâng catalog v11 lên v12, giữ các subtemplate Natural Motion, Fabric Detail, Right Camera Arc và BasePrompt cha. Không thay đổi guardrail chung, rule P1, cơ chế RAG hoặc API của provider.
- Phiên bản chung Product On Model đổi nên fingerprint có thể thay đổi cả ở scene khác dù nội dung prompt scene đó không đổi. Kiểm tra preview và chi phí trước khi xác nhận render.
- Không cập nhật database hay tự tạo video có phí trong lần triển khai này.

## Cách kiểm tra trên video

1. Khởi động lại backend, mở preview Brand Focus và kiểm tra `brand-focused`, `the shoulders gently lower and settle`, `Maintain the viewing direction established by the opening image` và `Preserve the lighting shown in the source image`.
2. Dùng start frame có vùng thiết kế đủ rõ. Nếu ảnh hiện tại cắt mất artwork, đổi prompt video không thể bảo đảm khôi phục chính xác phần thiếu; chọn hoặc tạo frame thích hợp trước.
3. Đối chiếu đầu/giữa/cuối clip: camera bắt đầu ngay, tiến gần nhẹ và đều; vai chuyển động nhỏ rồi ổn định; thân không tự xoay hoặc bước đi.
4. Kiểm tra hình in, logo, chữ giữ nội dung và vị trí; vùng thiết kế nguồn không bị crop thêm; tay không phát sinh động tác mới qua sản phẩm.
5. Với studio hoặc nền trơn, không xuất hiện cảnh vật/nguồn sáng mới. Với hoodie hoặc túi, không xuất hiện đặc điểm áo thun hay nếp vải áp lên sản phẩm cứng.

Trong planner hiện tại, bài kiểm tra một video chỉ có một scene dùng tổng thời lượng 5 giây để phù hợp kiểm tra thời lượng chung của video. Ví dụ compile 4 giây ở trên mô tả prompt scene, không thay đổi chính sách thời lượng video cuối.

## Kiểm thử v12

55/55 kiểm thử liên quan đã qua:

```powershell
dotnet test tests/APCS.Application.UnitTests/APCS.Application.UnitTests.csproj -c Release -- --filter 'FullyQualifiedName~AiVideoPromptCompilerTests|FullyQualifiedName~Plan_ProductOnModel|FullyQualifiedName~Plan_P1Rules_UsesTemplateDefaultsAndExposesExactPromptTrace'
```

| Yêu cầu | Bằng chứng |
|---|---|
| Thiết kế là trọng tâm, động tác vai rõ, giữ crop/góc/ánh sáng nguồn | `Compile_BrandFocus_KeepsVisibleDesignInFrameAndUsesSourceViewAndLighting` |
| Mô tả theo loại sản phẩm, không cố định T-shirt | `Compile_BrandFocus_UsesProductAppropriateMotion` — T-shirt, hoodie, tote bag, mug |
| Không chồng động tác mặc định lên P1 | `Compile_BrandFocusWithActionPlan_DoesNotAddConflictingDefaultMovement` |
| Nâng catalog v11 và giữ các tùy chỉnh, frame rỗng/cũ/tùy chỉnh | `Resolve_VersionElevenBrand_UpgradesDefaultAndPreservesCustomDirections` — 4 DataRow |
| Planner dùng push-in, source-view và source-lighting | `Plan_BrandFocus_UsesSourceViewLightingAndPushIn` |

Ví dụ compile mới dài 2.011 ký tự. Kiểm thử kiểm tra câu chữ và cấu hình; chưa chứng minh mức chuyển động, nét chữ hay độ ổn định thiết kế trong video thực tế.

`dotnet test Capstone.sln -c Release -v:q` đã chạy và vẫn chưa qua với cùng 13 lỗi đã ghi nhận ở lần kiểm tra v11: 11 application test (duration/transition, media completion) và 2 infrastructure test (MiniMax unsupported settings, số bảng schema). Application: 1.215 qua / 11 lỗi; Infrastructure: 417 qua / 2 lỗi. Không có kiểm thử Brand Focus mới lỗi.

`git diff --check` trên hai file code/test không báo lỗi. Ví dụ trước/sau và hai prompt start frame trong audit đã được đối chiếu với template, defaults và phép thay placeholder tương ứng.

\n