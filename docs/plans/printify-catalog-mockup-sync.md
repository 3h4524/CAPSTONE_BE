# Kế hoạch: đồng bộ Apply Mockup với catalog Printify

Cập nhật: 2026-11-08 · Người thực hiện: _(chưa giao)_

Chọn phôi và màu vải Printify ngay ở bước **Apply mockup**. Mockup, sản phẩm Printify và listing Etsy khi đó
luôn dùng cùng một mẫu áo và cùng màu. Node **Push to Printify** chỉ còn size, mức lãi và các công tắc.

## 1. Hiện trạng và vấn đề

| Chỗ | Hiện tại | Hệ quả |
|---|---|---|
| Apply mockup | Màu áo là **hex tự do**, chọn theo từng template (`templateColors: Record<templateId, hex[]>`, tối đa 5; 8 màu gợi ý trong `CAPSTONE_FE/src/helpers/garment-colors.ts`). Template chỉ có `ProductType` (tshirt, hoodie…), không gắn phôi thật nào | Mockup có thể ra màu mà không xưởng nào có vải |
| Push to Printify | Node tự chọn lại `blueprintId`, `printProviderId`, `colors[]`, `sizes[]` (`PrintifyPublishConfig` trong `Application/Features/Workflows/PublishConfigs.cs`) | Mockup là áo thun trắng nhưng Printify có thể in hoodie đen: khách nhận hàng khác ảnh |
| Ảnh và video gắn lên listing | Lấy **mọi mockup đã duyệt** và **video duyệt mới nhất của sản phẩm** (`WorkflowPublishService`) | Có thể gắn nhầm ảnh của run khác |

Printify chỉ in được những gì có trong catalog của họ. API bắt buộc phải có `blueprint_id`, `print_provider_id` và
`variant_id` (một cặp màu + size có vải thật). Vì vậy việc chọn phải dựa trên catalog thật, và nên làm **một lần,
ở đầu luồng**.

## 2. Mục tiêu và phạm vi

**Làm**
- Apply mockup có mục **Sản phẩm Printify**: chọn phôi (lọc theo `ProductType` của sản phẩm), xưởng, và **màu vải thật** của xưởng đó.
- Mỗi màu đã chọn lưu cả **tên Printify** (để đặt hàng) và **hex** (để tô mockup).
- Node Push to Printify lấy phôi, xưởng và màu từ Apply mockup. Trên node chỉ còn size, mức lãi, thiết kế, Publish to Etsy và Add mockups.
- Bước publish dùng **đúng mockup và video mà run đó đã duyệt**.
- Panel Push to Printify có mục chỉ đọc **"Lấy từ workflow"**: phôi, màu, thiết kế, tiêu đề/tag, số mockup, video.

**Không làm (để sau)**
- Màu khác nhau cho từng template trong cùng một sản phẩm Printify.
- Nhiều phôi Printify trong một workflow.
- Đồng bộ ngược (sửa trên Printify thì cập nhật APCS).

**Vẫn giữ:** workflow không dùng Printify (chưa có key, hoặc chỉ bán digital) vẫn dùng bảng màu hex tự do như hiện nay.

## 3. Luồng mới

1. **Apply mockup**: seller chọn phôi Printify (chỉ hiện phôi cùng loại với `ProductType`), xưởng (xưởng ở Mỹ lên đầu) và 1–5 màu vải của xưởng đó.
2. Config node lưu `printify = { blueprintId, blueprintTitle, printProviderId, printProviderTitle }` và `printifyColors = [{ name, hex }]`.
3. Batch job dựng mockup: mỗi template tô màu được sẽ dựng một mockup cho mỗi màu, bằng `hex`. `MockupImage.GarmentColor` = hex, `MockupImage.VariantKey` = tên màu Printify.
4. **Mockup approval** và **Review video** chạy như cũ. Output của run giữ danh sách mockup đã duyệt và video đã duyệt.
5. **Push to Printify** đọc `printify` và `printifyColors` từ config Apply mockup trong `DefinitionSnapshot` của run, ghép với `sizes` của node, rồi tạo sản phẩm với đúng các variant đó.
6. Printify publish lên Etsy. Etsy có biến thể đúng màu và size đã chọn.
7. App gắn **mockup của chính run đó** lên listing, xếp theo màu nếu có thể, kèm video của run.

## 4. Câu hỏi cần chốt trước khi code

| # | Câu hỏi | Vì sao quan trọng | Đề xuất |
|---|---|---|---|
| Q1 | **Lấy hex của màu vải ở đâu?** Catalog API chỉ trả tên màu (`options.color`), không có hex ([OpenAPI](https://developers.printify.com/openapi.json)). API sản phẩm có thể có `options[].values[].colors`, nhưng chưa xác minh | **Chặn** task B2, F2 | Cần người có quyền chạy thử: `GET /v1/shops/{id}/products.json?limit=1` bằng key Printify, xem `options` có hex không. Nếu không có: dùng bảng tên → hex soạn sẵn cho các brand phổ biến (Gildan, Bella+Canvas, Comfort Colors); màu ngoài bảng thì ước lượng và ghi "màu ước lượng" |
| Q2 | Một bộ màu chung cho mọi template, hay mỗi template một bộ? | Ảnh hưởng schema config | Một bộ chung (một sản phẩm Printify = một bộ màu) |
| Q3 | Template không tô màu được (áo tối, chưa có print map, `AllowRecolor = false`) thì sao? | Mockup có thể không khớp màu vải nào | Bắt chọn màu Printify tương ứng với màu gốc của template; nếu không khớp thì cảnh báo và không cho dùng template đó với Printify |
| Q4 | Workflow cũ đã có Push to Printify kèm phôi/màu trên node | Tương thích ngược | Không tự chuyển; hiện lỗi "Chọn sản phẩm Printify ở Apply mockup" để seller chọn lại |
| Q5 | Bảng `ProductType` → phôi Printify (tshirt → các phôi T-Shirt…) | Lọc phôi đúng loại | Bảng tĩnh trong backend, khớp theo từ khoá tiêu đề blueprint |

## 5. Việc cần làm — Backend

| ID | Việc | File chính |
|---|---|---|
| B1 | Đọc hex màu từ Printify nếu có (theo kết quả Q1); thêm vào `PrintifyCatalogVariant` hoặc tạo kiểu `PrintifyColor(Name, Hex)` | `Application/Abstractions/Commerce/IPrintifyClient.cs`, `Infrastructure/Services/PrintifyClient.cs` |
| B2 | Service bảng màu: màu của (blueprint, provider) kèm hex (từ API hoặc bảng soạn sẵn), cache 1 ngày. Endpoint `GET /api/shop/printify/blueprints/{b}/providers/{p}/colors` | `Application/Features/Shops/PrintifyService.cs`, `API/Controllers/PrintifyController.cs`, file mới `Shops/Common/PrintifyColorPalette.cs` |
| B3 | Lọc phôi theo `ProductType` (Q5): thêm `productType` vào `GET .../blueprints` | `PrintifyService.RankBlueprints` |
| B4 | Apply mockup nhận `printify` + `printifyColors`, lưu vào config batch; khi dựng mockup dùng `hex`, ghi `VariantKey` = tên màu | `Application/Features/BatchMockups/MockupTemplateService.cs` (`ApplyAsync`, `GenerateAllForJobAsync`), `Dtos/Request/ApplyMockupTemplatesRequestDto.cs`, validator; `Application/Features/Workflows` (`MockupConfig`) |
| B5 | Push to Printify đọc phôi/xưởng/màu từ config Apply mockup; bỏ các field đó khỏi `PrintifyPublishConfig` (node chỉ còn `sizes`, `targetProfit`, `designId`, `publishToEtsy`, `addMockups`). Cập nhật `Problem()` và graph validator: có Push to Printify thì Apply mockup phải có sản phẩm Printify và ≥ 1 màu | `Workflows/PublishConfigs.cs`, `Workflows/WorkflowPublishService.cs`, `Workflows/WorkflowGraphValidator.cs` |
| B6 | Ảnh/video theo run: mockup lấy từ output của `approval-gate` (`mockupIds`), video từ `VideoPointer` của `review-video`; truyền vào `SyncPrintifyProductRequestDto` và `PublishEtsyDraftRequestDto` | `Workflows/WorkflowPublishService.cs` |
| B7 | Test: bảng màu, lọc phôi, executor đọc config từ Apply mockup, validator, ảnh theo run | `tests/APCS.Application.UnitTests/Features/Shops`, `.../Features/Workflows/WorkflowPublishServiceTests.cs`, `WorkflowGraphValidatorTests.cs` |

Không cần đổi schema DB: mọi thứ nằm trong config JSON của node/batch và các cột đã có (`mockup_images.garment_color`, `variant_key`).

## 6. Việc cần làm — Frontend

| ID | Việc | File chính |
|---|---|---|
| F1 | Panel Apply mockup: mục **Sản phẩm Printify** (chọn phôi lọc theo loại, chọn xưởng). Dùng lại `BlueprintPicker` | `src/components/workflows/config-panel/fields/mockup-selection-field.tsx`, `src/components/shop/printify-publish-dialog.tsx` (đã export `BlueprintPicker`, `ChipGroup`) |
| F2 | Bảng màu: có sản phẩm Printify thì hiện màu vải thật (ô màu + tên), lưu `{ name, hex }`, tối đa 5; không có thì giữ bảng hex cũ. Cảnh báo template không tô được màu (Q3) | `src/components/mockups/garment-color-picker.tsx`, `src/helpers/garment-colors.ts` |
| F3 | Schema: thêm `printify` + `printifyColors` vào config Apply mockup; bỏ phôi/xưởng/màu khỏi `publishPrintifyConfigSchema` | `src/schemas/workflow.ts`, `src/constants/workflow.ts` |
| F4 | Gửi lựa chọn Printify khi chạy batch mockup | `src/hooks/use-batch-run.ts`, `src/api/mockup-templates.ts` |
| F5 | Panel Push to Printify: mục chỉ đọc **"Lấy từ workflow"**; giữ size (sắp S → 5XL, mặc định S–2XL), lãi, thiết kế, công tắc | `src/components/workflows/config-panel/publish-settings.tsx` |
| F6 | Thẻ node và kiểm tra canvas: tóm tắt lấy từ Apply mockup; báo lỗi "Chọn sản phẩm Printify ở Apply mockup" | `src/components/workflows/nodes/output-card.tsx`, `src/helpers/workflow-graph.ts` |
| F7 | Cập nhật test schema và graph | `src/schemas/workflow.test.ts`, `src/helpers/workflow-graph.test.ts` |

## 7. Thứ tự làm và ước lượng

1. **Chốt Q1–Q5** cùng trưởng nhóm (½ ngày). Q1 cần chạy thử bằng key Printify thật.
2. **B1, B2, B3** — catalog màu và lọc phôi (khoảng 1,5 ngày).
3. **F1, F2, F3, F4** — Apply mockup chọn từ catalog (khoảng 2 ngày). Làm song song với 4 được nếu thống nhất trước API.
4. **B4** — dựng mockup theo màu Printify (khoảng 1 ngày).
5. **B5, B6** + **F5, F6** — node Push to Printify đọc từ Apply mockup, ảnh/video theo run (khoảng 1,5 ngày).
6. **B7, F7** + chạy thử end-to-end (khoảng 1 ngày).

Tổng khoảng **7–8 ngày công** cho một người, hoặc khoảng 4–5 ngày nếu chia backend và frontend cho hai người.

## 8. Tiêu chí nghiệm thu

- Chọn phôi (ví dụ Gildan 64000), xưởng ở Mỹ, màu Black + Navy ở Apply mockup → mockup ra đúng hai màu đó.
- Chạy hết workflow → sản phẩm trên Printify có đúng Black + Navy với các size đã chọn ở node; listing Etsy có đúng các biến thể đó.
- Node Push to Printify **không còn** ô chọn phôi và màu; mục "Lấy từ workflow" hiển thị đúng.
- Ảnh gắn lên listing là mockup của chính run; video là video run đó đã duyệt.
- Chưa có key Printify: Apply mockup vẫn chạy với bảng hex cũ; Push to Printify báo cần sản phẩm Printify.
- Workflow cũ có Push to Printify: canvas báo lỗi rõ, không crash.
- `dotnet test Capstone.sln` qua. Frontend: `tsc` và `eslint` sạch; logic schema/graph kiểm bằng `tsx` (vitest đang lỗi trên Node 22.14).

## Tham khảo trong code hiện tại

- Executor publish: `Application/Features/Workflows/WorkflowPublishService.cs` (chạy nền qua `Infrastructure/BackgroundServices/WorkflowPublishWorker.cs`).
- Printify: `Application/Features/Shops/PrintifyService.cs`, `Infrastructure/Services/PrintifyClient.cs`. Key Printify lấy từ trang API Keys (`api_keys`, provider `printify`), xem `docs/API-KEYS.md`.
- Dựng mockup: `Application/Features/BatchMockups/MockupTemplateService.cs`, `MockupRules.Layers`, compositor Cloudinary.
- Panel node publish: `CAPSTONE_FE/src/components/workflows/config-panel/publish-settings.tsx`.
