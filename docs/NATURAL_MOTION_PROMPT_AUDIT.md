# Audit: Product On Model — Natural Motion

> Cập nhật Premium MiniMax H3: xem [audit I2VA format v1](MINIMAX_H3_PROMPT_AUDIT.md) để có prompt trước/sau thực tế, source-based defaults, phạm vi P1/Lite và kết quả kiểm thử mới. Các mục v6–v9 bên dưới giữ lịch sử hành động, không mô tả đầy đủ serializer H3 hiện tại.

## Phạm vi

Thay đổi áp dụng cho subtemplate `lifestyle_in_use / natural_motion`. Tài liệu giữ lịch sử v6 → v7 và bổ sung v9 ở bên dưới (v8 chỉ cập nhật Profile Tracking). Ví dụ dùng sản phẩm `T-shirt`, thời lượng 4 giây, cấu hình mặc định, không bật P1 và không có RAG. Đây là prompt gửi tới provider, không phải kết quả video đã được đánh giá.

## Prompt trước thay đổi

```text
Create one continuous polished product-on-model fashion shot from the approved source image. Keep the model in the established pose and wearing the exact product. The camera performs a slow, smooth cinematic push-in at an eye-level camera angle in a medium composition for the full 4 seconds. The model subtly shifts their weight with restrained, natural body movement while the garment settles naturally across the torso. Warm natural light with soft, realistic shadows. Use soft realistic shadows, shallow depth of field and subtle film grain. Keep the setting coherent and animate only background elements already visible in the source.

Product: T-shirt. Match the approved image for its visible design, colors, fit and proportions.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.
```

## Prompt sau thay đổi v7 (lịch sử)

```text
Create one continuous 4-second live-action product-on-model fashion advertisement from the approved source image.

Product: T-shirt. Keep the product and any visible design as the visual focus. Match the approved image for its visible design, colors, fit and proportions.

The model remains comfortably in the original facing direction and overall stance. Gentle breathing creates small, visible movement in the chest and shoulders, with slight natural adjustments of the relaxed arms. The garment follows the body with small changes in its existing folds. No deliberate posing, torso turns or walking. The camera performs a slow, smooth cinematic push-in at an eye-level camera angle, starting immediately and continuing in a steady, uninterrupted glide throughout the shot. Begin in a medium composition and keep the change in framing small; keep the model's head and product artwork in frame when visible in the opening image. Warm natural light with soft, realistic shadows. Preserve the source setting and natural shadows.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.
```

## Các phần thay đổi

| Phần | Trước | Sau và mục đích |
|---|---|---|
| Ngữ cảnh | Cảnh thời trang; loại sản phẩm xuất hiện sau toàn bộ template | Nêu mục đích quảng cáo, loại sản phẩm và trọng tâm sản phẩm ngay trước chỉ dẫn chuyển động. |
| Người mẫu | Giữ pose đã thiết lập; chuyển trọng tâm rất nhẹ | Giữ hướng và tư thế tổng thể; mô tả hơi thở, vai và cánh tay cử động nhẹ. Không yêu cầu xoay thân, đi lại hay tạo dáng. |
| Camera | Push-in chậm và mượt trong cả clip | Bắt đầu ngay, tiến liên tục và giữ thay đổi bố cục nhỏ; giữ đầu và artwork trong khung khi chúng đã xuất hiện ở ảnh mở đầu. Không yêu cầu phóng lớn theo phần trăm. |
| Hình ảnh | Thêm shallow depth of field và film grain | Giữ ánh sáng, bóng và bối cảnh nguồn, giảm chỉ dẫn trang trí cạnh tranh với chuyển động. |
| Bảo toàn | Quy tắc Product On Model | Giữ nguyên quy tắc; vẫn cho phép thay đổi tự nhiên do chuyển động, nếp vải và che khuất ngắn. |
| P1 | Có thể chèn kế hoạch hành động của người dùng | Khi có P1, bỏ chỉ dẫn chuyển động mặc định và câu cấm xoay thân để tránh xung đột với hành động được chọn. |
| Phiên bản | Product On Model v6 | v7. Khi đọc catalog v6 đã lưu, cập nhật Natural Motion và giữ lại prompt của các subtemplate khác cùng keyframe direction đã sửa. Catalog cũ hơn vẫn theo cơ chế fallback hiện có. |

## Nguồn context và giới hạn

- Loại sản phẩm lấy từ dữ liệu sản phẩm hiện có; ảnh mở đầu đã duyệt vẫn là nguồn tham chiếu thị giác.
- Hệ thống chưa lưu caption mô tả màu/họa tiết được xác minh từ ảnh mở đầu cuối cùng. Vì vậy prompt chưa tự thêm màu, vị trí hoặc nội dung họa tiết cụ thể, cũng không chèn nguyên prompt tạo design. Metadata của mockup gốc có thể khác studio frame đã duyệt.
- Nếu ảnh không có hình in, cụm `any visible design` không khẳng định có họa tiết. Không có bước gọi model phân tích ảnh mới trong thay đổi này.
- Advanced controls đang tắt. Prompt được compile từ default của subtemplate; extra/shared instructions không được áp dụng theo cơ chế hiện tại.
- Prompt là chỉ dẫn cho model, không bảo đảm vận tốc camera, mức chuyển động hay tính chính xác của hình in. Cần tạo video mới và đánh giá thực tế; không suy ra chất lượng video chỉ từ unit test.

## Kiểm tra đề xuất trên video

So sánh cùng ảnh đầu vào và cùng thời lượng giữa prompt cũ và mới, mỗi phương án ít nhất vài lần nếu ngân sách cho phép. Kiểm tra riêng: nhịp camera từ đầu đến cuối; hơi thở/vai có chuyển động nhưng không xoay người; mức độ giữ đúng hình in, chữ và màu; đầu và sản phẩm có bị cắt; độ tự nhiên của nếp vải. Nếu muốn xác định riêng tác dụng của context sản phẩm, so sánh thêm hai prompt giống nhau ngoài phần context.

## Kiểm thử thực hiện

- `AiVideoPromptCompilerTests`: 18/18 thành công, gồm các kiểm tra prompt Natural Motion, P1 và nâng cấp catalog v6.
- Hai kiểm thử planner cho P1 và cấu hình mặc định: 2/2 thành công.
- `dotnet test Capstone.sln` chưa thành công: API đang chạy khóa DLL trong `API/bin`; đồng thời có 11 lỗi ở application tests liên quan duration/transition và media QA, 2 lỗi infrastructure tests liên quan kiểm tra MiniMax settings và số lượng bảng. Các lỗi này cần được xử lý riêng; không suy ra toàn bộ solution đã qua kiểm thử.
- Chưa gọi MiniMax để tạo video mới, nên chưa đo được cải thiện chất lượng hình ảnh/chuyển động.

## Cập nhật v9: bước ngắn và quay đầu nhẹ

### Prompt trước v9

Prompt trước v9 là bản v7 ở trên; v8 thay đổi Profile Tracking nhưng không đổi nội dung Natural Motion. Chỉ dẫn cơ thể yêu cầu hơi thở nhẹ, giữ tư thế tổng thể và không đi hoặc quay thân. Phản hồi thực tế là người mẫu vẫn trông đứng yên.

### Prompt sau v9 — ví dụ T-shirt, 4 giây

```text
Create one continuous 4-second live-action product-on-model fashion advertisement from the approved source image.

Product: T-shirt. Keep the product and any visible design as the visual focus. Match the approved image for its visible design, colors, fit and proportions.

Starting immediately from the pose in the image, the model takes one short, relaxed step toward the camera with the right foot. As the foot lands, the model transfers their weight onto it. The hips and upper body move forward together, with a small natural balancing movement in the shoulders and any free arm. A hand already resting in a pocket stays there. The garment moves naturally with the body, forming small folds where the fabric bends. Once comfortably balanced in the new stance, the model briefly glances toward the right-hand side of the frame. The head gently follows the gaze with a small, relaxed turn while the shoulders and torso keep their direction. Finish looking slightly off-camera, with a relaxed expression and gentle breathing. Perform one short step followed by this small head turn, with soft, unhurried movement. No continuous walking, sudden head movements or exaggerated posing. The camera performs a slow, smooth cinematic push-in at an eye-level camera angle, starting immediately and continuing in a steady, uninterrupted glide throughout the shot. Begin in a medium composition and keep the change in framing small; keep the model's head and product artwork in frame when visible in the opening image. Warm natural light with soft, realistic shadows. Preserve the source setting and natural shadows.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.
```

### Thay đổi và phạm vi

| Phần | v7/v8 | v9 |
|---|---|---|
| Người mẫu | Hơi thở và điều chỉnh tay rất nhẹ, không bước | Một bước ngắn chân phải, đặt chân và chuyển trọng tâm; sau khi đứng vững liếc rồi quay đầu nhẹ về phía phải khung hình. |
| Tay | Điều chỉnh tay thả lỏng | Tay tự do phản ứng với bước; tay đã ở trong túi tiếp tục ở đó. Không tạo túi hoặc đổi vị trí tay. |
| Sản phẩm | Mô tả chuyển động vải chung | T-shirt/hoodie: nếp vải nhẹ; tote bag: túi và quai theo quan hệ đã có với người mẫu; mug/poster/phone case: giữ hình dạng cứng và vị trí đã có. |
| Camera và bảo toàn | Push-in nhẹ, guardrail chung | Giữ default camera và guardrail. Không yêu cầu thêm bối cảnh, màu hoặc họa tiết chưa xác minh. |
| P1 | Hành động lấy từ action plan | Giữ nguyên: khi có P1, không thêm bước, quay đầu hoặc mô tả chuyển động sản phẩm mặc định để tránh xung đột với hành động đã chọn. |
| Catalog | Product On Model v8 | v9; nâng cấp Natural Motion của các catalog v6–v8, giữ start frame đã chỉnh và Profile Tracking v8. Các video/snapshot đã tạo không tự thay đổi. |

Mô tả phản ứng của sản phẩm được chọn theo loại sản phẩm, không ghi cứng áo thun. Đây là chỉ dẫn, không xác minh vị trí cơ thể hoặc sản phẩm từ ảnh. Nếu chân nằm ngoài ảnh, model phải suy luận chuyển động phía dưới; cần đánh giá bằng video thực tế. Hướng nhìn là **phải của khung hình**, không phải tay phải của người mẫu.

Để tách riêng chất lượng chuyển động người mẫu khỏi push-in, có thể thử cùng prompt với camera cố định trong thử nghiệm riêng. Hiện Advanced Controls vẫn tắt; scene mặc định tiếp tục dùng push-in. Chưa có bằng chứng từ video mới rằng v9 cải thiện kết quả.

### Kiểm thử v9

- 32/32 kiểm thử compiler và planner được chọn đã qua, gồm thứ tự bước/quay đầu, các nhóm sản phẩm, P1 và nâng cấp catalog.
- `dotnet test Capstone.sln` chưa qua: API đang chạy khóa DLL, đồng thời có 11 lỗi application test và 2 lỗi infrastructure test cùng nhóm đã được ghi nhận trong lần kiểm thử trước (duration/transition, media QA, MiniMax settings và schema).
- Chưa tạo video mới hoặc kiểm thử chất lượng chuyển động thực tế với MiniMax.
