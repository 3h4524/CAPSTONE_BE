# Tiêu chí benchmark chất lượng video

| Công cụ | Tiêu chí | Ý nghĩa | Cách đo |
|---|---|---|---|
| VBench | Nhất quán chủ thể | Chủ thể không đổi bất thường giữa các frame. | Phân tích toàn bộ video. |
| VBench | Nhất quán hậu cảnh | Bối cảnh không bị nhảy hoặc biến dạng. | Phân tích toàn bộ video. |
| VBench | Độ mượt chuyển động | Chuyển động không giật, rung hoặc nội suy lỗi. | Phân tích biến đổi giữa các frame. |
| VBench | Chất lượng thẩm mỹ | Mức hấp dẫn thị giác tổng thể. | Mô hình thẩm mỹ của VBench. |
| VBench | Chất lượng hình ảnh | Độ rõ, nhiễu và các lỗi hình ảnh. | Mô hình chất lượng ảnh của VBench. |
| CLIP ViT-B/32 | Khớp nội dung với prompt | Hình ảnh trong video có đúng mô tả mong muốn không. | Lấy đều 8 frame, so embedding với đoạn đầu của prompt, tối đa 77 token. |
| PaddleOCR | Nhận lại token từ ảnh nguồn | Chữ/artwork từ ảnh nguồn có còn xuất hiện trong video không. | OCR frame đầu, giữa, cuối; tính tỷ lệ token nguồn xuất hiện lại. |
| PaddleOCR | Nhất quán chữ giữa các frame | Chữ có bị đổi, mất hoặc nhảy theo thời gian không. | So token OCR giữa các frame lấy mẫu. |
| PaddleOCR | Độ tin cậy OCR | Mức chắc chắn khi công cụ đọc chữ. | Trung bình confidence của các lần nhận dạng. |

VBench và PaddleOCR cho điểm 0–1, cao hơn là tốt hơn. Điểm CLIP dùng thang riêng, chỉ nên so giữa các video được đo cùng cách.

## Repo nguồn

- [VBench](https://github.com/Vchitect/VBench)
- [OpenAI CLIP](https://github.com/openai/CLIP) — model `ViT-B/32`
- [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)

Bằng chứng đo của lần benchmark: [`evaluation-results/latest-vs-old-2026-10-30`](../../../../evaluation-results/latest-vs-old-2026-10-30). Adapter VBench/OCR trong codebase: [`run_vbench.py`](../tools/video-quality/run_vbench.py), [`run_paddle_ocr.py`](../tools/video-quality/run_paddle_ocr.py).

## Chạy tự động trong sản phẩm

QA theo batch bằng Python worker: xem [hướng dẫn cài đặt và chạy](../video-quality-worker/README.md). Worker dùng clip đã lưu trên Cloudinary, chấm các scene theo batch, giữ PaddleOCR trong bộ nhớ và gia hạn lease trong lúc chấm. VBench hiện vẫn nạp model lại cho từng tiêu chí ở mỗi batch.

- Sau khi render thành công, hệ thống luôn lưu kiểm tra kỹ thuật và Etsy cho video tổng. Lỗi kỹ thuật chặn video hoàn tất.
- Với video AI, mỗi scene được đưa vào hàng đợi `video_quality`; worker chấm PaddleOCR/VBench nền và không chặn người dùng duyệt video.
- Bật phép đo thực tế bằng `VideoQuality:Enabled=true` và cấu hình `PaddleOcrScriptPath`, `VBenchScriptPath`. Nếu chưa cấu hình, worker tự ghi kết quả `skipped` thay vì để hàng đợi chờ mãi.
- Video tạo mới phải dài 5–15 giây. Video cũ dài hơn chỉ còn để xem hoặc tải xuống.
