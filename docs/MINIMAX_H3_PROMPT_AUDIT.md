# Audit prompt MiniMax H3 — I2VA format và image-grounded preparation

## Hiện tại: Economy và Premium dùng chung pipeline chuẩn bị prompt

Economy `minimax_h3_lite` nay nhận cùng cấu trúc I2VA và bước phân tích ảnh/biên tập Gemini như Premium `minimax_h3`. Những đoạn dưới ghi “Lite bị loại” là lịch sử của các lần triển khai trước; phạm vi hiện tại được mô tả ở phần này.

Profile Tracking có bản nháp nội dung H3/Lite riêng. Theo phản hồi clip quay quá nhanh và lộ mặt lưng, hướng hiện tại là cung phải nhỏ/chậm khoảng 30–40° tổng cho cả shot, tối đa 40° và rút ngắn nếu ảnh đầu đã gần profile. Camera chuyển động ổn định, vào/ra nhẹ, dừng tại ranh giới side-profile, không đi sau vai hoặc reveal mặt lưng. Tên hiển thị được khôi phục từ Right Camera Arc về Profile Tracking. Guardrail được giữ sau Gemini và editor có policy/kiểm tra tương ứng; fallback cũng dùng cung chậm mới. Non-P1 built-in dùng source_view; P1 và custom prompt được bảo toàn. Xem [audit Profile Tracking hiện tại](PROFILE_TRACKING_PROMPT_AUDIT.md) để đọc output thực tế, ma trận test và giới hạn bằng chứng. Gemini preparation vẫn dùng hai bước đã có. Khoảng góc là lựa chọn của ứng dụng, không phải tham số camera được MiniMax bảo đảm.

### Thay đổi và phạm vi

- `AiVideoModel.UsesStructuredPrompt` nhận cả hai model key cụ thể. Compiler áp dụng header ảnh đầu và ba trường I2VA cho mọi template của hai model. Draft không có model key và các model legacy vẫn dùng định dạng cũ.
- Natural Motion và Profile Tracking built-in của cả Lite/H3 dùng bản nội dung riêng với defaults `source_view` / `source_image`, đồng nhất với snapshot khi không P1. Natural Motion giữ push-in nhỏ/chậm; Profile dùng orbit_right nhỏ/chậm, khoảng 30–40° tổng và không vượt side-profile sang phía lưng. Các subtemplate còn lại giữ hướng chuyển động riêng làm đầu vào biên tập. Không ghi đè base prompt tùy chỉnh trong compiler; P1 vẫn giữ kế hoạch hành động riêng.
- `VideoPromptPreparationService` nhận cả H3 và Lite. Hai lượt Gemini, bộ kiểm tra, guardrail, timeout, cấu hình bật/tắt và fallback hoạt động giống nhau. Nếu preparation lỗi, Lite vẫn có prompt compiler I2VA dự phòng.
- Không đổi model generation hoặc đơn giá để đạt định dạng mới. Economy giữ `MiniMax-H3-Lite`, Premium giữ `MiniMax-H3`; provider vẫn dùng ratio tường minh cho Lite, adaptive cho H3. Không có tự động nâng model khi preparation lỗi.
- Cache đã chứa `ModelKey`, nên không dùng nhầm trace Premium cho Economy. Giữ version `h3_grounded_v1` vì thuật toán không thay đổi, chỉ mở thêm model hợp lệ; cache H3 hiện có vẫn tái sử dụng được. Lite cần preview mới để tạo trace mới. Snapshot đã xác nhận giữ nguyên như cũ.

### Nguồn và giới hạn bằng chứng

Nguồn cấu trúc là [guide H3 do MiniMax công bố](https://github.com/MiniMax-AI/MiniMax-H3/blob/35491cdba2adfe62a510f725e8619f8e58783ea2/skills/h3-prompt-writing/references/base-en.txt). [Tài liệu Video Generation đã đối chiếu](https://platform.minimax.io/docs/guides/video-generation) mô tả H3; lần kiểm tra này không xác minh được một guide riêng cho H3-Lite hoặc thử nghiệm chứng minh cấu trúc này tối ưu Lite. Áp dụng sang Lite là quyết định ở application layer theo mục tiêu của dự án, không phải xác nhận chính thức về Lite hay bằng chứng ngang chất lượng H3. Unit test dùng provider giả lập chỉ xác nhận request/route/string, không chứng minh API thật chấp nhận hoặc sinh clip đúng yêu cầu.

Mục tiêu kiểm chứng tiếp theo là chất lượng và **tổng chi phí trên mỗi clip được chấp nhận**, gồm Gemini chuẩn bị prompt, lần sinh Lite, retry và render. Đơn giá đang cấu hình trong code là Lite 0,04 USD/giây, H3 0,08 USD/giây; đây là cấu hình ứng dụng, không phải hóa đơn đo được. Một scene 6 giây tương ứng phần video 0,24 USD hoặc 0,48 USD trước phí chuẩn bị/retry. Quote hiện chưa cộng phí Gemini. Không suy ra tiết kiệm tổng 50% hoặc chất lượng ngang H3 chỉ từ chênh lệch đơn giá.

### Kiểm chứng mở rộng Economy

| Yêu cầu | Test |
|---|---|
| Cả bốn subtemplate Product On Model có header/ba trường, trace và shot khớp | `Plan_H3ProductOnModel_SendsFirstFrameAndThreeOrderedSections` — chạy H3 và Lite |
| Defaults và chuỗi hành động Natural Motion nhất quán giữa hai model | `Plan_H3NaturalMotion_SnapshotsTheEffectiveSourceControls`, `CompileTrace_H3NaturalMotion_PreservesActionOrderAndUsesSourceLightingAndMeasuredCamera` |
| Lite kế thừa lựa chọn Economy toàn video hoặc override Premium ở scene vẫn giữ đúng model/giá | `Plan_LiteScene_UsesStructuredFormatAndKeepsEconomyRouting` |
| P1, custom direction và trimmer giữ ưu tiên trên cả hai model | `CompileTrace_H3WithP1_UsesOnlyTheRequestedActionAndKeepsDisabledDirectionsOut`, `CompileTrace_H3CustomNaturalMotion_PreservesThePersistedCreativeDirection`, `CompileTrace_H3FormatOverhead_TrimsOptionalExperienceBeforeExceedingTheLimit` |
| Lite thực sự qua analysis/rewrite, giữ model/giá và đồng bộ snapshot/fingerprint | `PrepareAsync_ValidObservationAndRewrite_AlignsPromptTraceShotAndFingerprints` — hai model |
| Cache không lẫn Premium/Economy; Lite dùng lại kết quả của chính mình | `PrepareAsync_ChangesFromPremiumToEconomy_IsolatesCacheWithoutPromotingLite` |
| Lỗi preparation giữ nguyên draft, không nâng model | `PrepareAsync_ProviderUnavailable_RetainsDraftAndCachesShortFallback` — hai model |
| Request MiniMax vẫn chọn model và ratio đúng | `StartAsync_ApprovedFirstFrame_SendsH3At768PAndReturnsTaskId` — cả `lite=false` và `lite=true` |

Application focused: `dotnet test tests/APCS.Application.UnitTests/APCS.Application.UnitTests.csproj -- --filter "FullyQualifiedName~MiniMaxH3PromptTests|FullyQualifiedName~VideoPromptPreparationServiceTests|FullyQualifiedName~AiVideoPromptCompilerTests|FullyQualifiedName~AiVideoModelRoutingTests|FullyQualifiedName~VideoPlanningServiceTests"` — **Passed: 126, Failed: 0, Skipped: 0**, exit 0.

Infrastructure focused: `dotnet test tests/APCS.Infrastructure.UnitTests/APCS.Infrastructure.UnitTests.csproj --no-restore -- --filter "FullyQualifiedName~GeminiMultimodalPromptTests|FullyQualifiedName~GeminiTextGenerationProviderTests|FullyQualifiedName~MiniMaxFirstFramePreparerTests|(FullyQualifiedName~MiniMaxVideoProviderTests&FullyQualifiedName!~StartAsync_UnsupportedSettings_AreRejectedBeforeAnyRequest)"` — **Passed: 27, Failed: 0, Skipped: 0**, exit 0. Method duration cũ vẫn được chạy và báo ở toàn solution; không sửa hoặc skip test đó.

`dotnet test Capstone.sln` — exit 1: Application **Passed: 1.268, Failed: 11, Skipped: 0**; Infrastructure unit **Passed: 424, Failed: 2, Skipped: 0**; Common **Passed: 46**; Domain **Passed: 28**; Infrastructure integration **Skipped: 8**. Tên các failure khớp audit trước (duration/transition/fingerprint, FullDecode, MiniMax duration 5 giây, generated table count). API build trong solution vẫn bị process `APCS.Api` PID 31224 khóa DLL. Không sửa các failure ngoài phạm vi hoặc dừng process đó.

Build API với output riêng để kiểm tra compilation: `dotnet build API/API.csproj --no-restore -p:OutputPath=D:/FPT/Capstone/source/Backend/CAPSTONE_BE/.git/h3-lite-api-build/` — **exit 0, 0 errors, 13 warnings** từ package/repository hiện có. Output riêng không cập nhật process đang chạy.

Không đọc/sửa `.env`, không đổi database, không gọi API Gemini/MiniMax thực hoặc sinh video trả phí trong lần kiểm tra này. Cần rebuild/restart backend rồi mở lại preview Economy để sử dụng code mới.

---

## Lịch sử: bổ sung phân tích ảnh và biên tập shot cho Premium

Đã bổ sung pipeline `h3_grounded_v1` sau compiler/planner đồng bộ. Các phần audit I2VA v1 bên dưới là lịch sử triển khai và đánh giá trước bước này; nhận định “chưa có phân tích ảnh/rewrite” mô tả v1, không còn mô tả toàn bộ pipeline hiện tại. Compiler tự thân vẫn là hàm đồng bộ, có kết quả xác định từ đầu vào; tác vụ AI nằm ở service bất đồng bộ.

### Luồng đã triển khai

1. Planner kiểm tra cấu hình và chọn đúng source thực tế của shot, bao gồm studio frame hoặc source crop.
2. `MiniMaxFirstFramePreparer` chuẩn bị PNG theo cùng logic mà MiniMax adapter dùng: source crop → center crop theo aspect → giới hạn kích thước. Không phân tích mockup gốc nếu shot dùng studio frame. Test gradient ảnh xác nhận pixel đầu, kích thước và bytes phân tích khớp bytes gửi provider cho cả 9:16/16:9.
3. Gemini nhận ảnh inline và chỉ mô tả những gì nhìn thấy: style, bố cục, chủ thể, tư thế, sản phẩm, bối cảnh, ánh sáng. JSON có `openingDescription` và `uncertainties`. Không suy diễn chuyển động/âm thanh từ ảnh; không yêu cầu đoán hay sửa chữ/logo.
4. Lượt Gemini thứ hai nhận quan sát và các phần draft đã compile, gồm P1/RAG nếu có; biên tập continuation thành văn xuôi theo diễn tiến cảnh. System instruction yêu cầu giữ thứ tự/số lượng/hướng hành động, camera, trạng thái kết thúc và ưu tiên P1. Bỏ nhãn điều phối khỏi phần mô tả được gửi đi.
5. Ghép lại header I2VA, `[Shot 1]`, quan sát mở đầu, continuation, các guardrail bảo toàn sản phẩm/source/template giữ nguyên, soundscape/music theo chính sách cũ. Chưa có bước biên tập âm thanh dựa trên ngữ cảnh.
6. Đồng bộ `ScenePlan.CompiledPrompt`, `AiPromptTrace.FinalPrompt`, `AiShotPlan.Prompt`; cập nhật fingerprint shot/board khi chuỗi gửi provider thay đổi. Bản draft và các part gốc vẫn nằm trong trace nhưng không gửi lặp lại.

Cả `VideoPlanningService.PreviewAsync` và `WorkflowRuntime.PlanAsync` đều chạy preparation sau khi plan hợp lệ. Snapshot đã xác nhận được trả về trước bước này, không bị rewrite lại. Bước này áp dụng H3, không tự áp dụng Lite hoặc Standard.

### Cấu hình, chi phí và phương án dự phòng

- Dùng cấu hình/credential `Gemini:TextGeneration` hiện có. `VideoPromptPreparation:Enabled` mặc định `true`; `TimeoutSeconds` mặc định 45, giới hạn 5–120 giây cho tổng hai lượt của mỗi scene. Không sửa `.env` hoặc database trong lần triển khai này.
- Mỗi scene chưa có cache có tối đa hai lời gọi logic analysis/rewrite; adapter Gemini vẫn có retry/fallback riêng nên số HTTP attempt có thể nhiều hơn hai. Khi chạy thực tế có chi phí Gemini và độ trễ. Không gọi API tạo video để biên tập prompt.
- Cache trace 24 giờ, tách theo owner và nội dung đầu vào: source ID/hash/revision/storage key/version, crop/aspect, duration, model key, draft trace và preparation version. Kết quả fallback cache hai phút. Cache không lưu ảnh, signed URL hoặc API key. Chưa có cache analysis riêng giữa các scene khác draft.
- Khi thiếu cấu hình, lỗi provider, timeout, JSON không hợp lệ, marker shot/field lạ, mô tả quá dài hoặc prompt tổng vượt 7.000 ký tự: giữ nguyên toàn bộ prompt xác định từ compiler và thêm cảnh báo. Không cắt bỏ guardrail để ép bản rewrite vào giới hạn.
- Ảnh chuẩn bị lớn hơn 12 MB dùng fallback để giới hạn inline request, không âm thầm resize khác pixels của H3. Cancellation từ caller được truyền tiếp; không coi là fallback thành công.
- Cache lỗi không làm mất bản preparation thành công. Eviction/TTL hết hạn hoặc request đồng thời có thể sinh bản rewrite khác; chưa có distributed lock. Quote/fingerprint hiện có vẫn chặn việc tiếp tục với prompt đã thay đổi mà chưa review lại.

### Mức độ bằng chứng

Đây là cải tiến do ứng dụng triển khai dựa trên guide; không phải MiniMax Context-IR, cũng không phải pipeline được MiniMax chứng nhận. Phân tích ảnh và biên tập là output của mô hình: bộ kiểm tra xác nhận cấu trúc, độ dài, marker và việc giữ nguyên guardrail; **chưa chứng minh quan sát đúng mọi chi tiết hoặc bản rewrite giữ nguyên mọi ý nghĩa của P1/custom/RAG**. Cần review prompt và kiểm tra video thực tế. Không coi observation là OCR đã xác minh; chưa thêm text/logo cụ thể khi không có nguồn chính xác. Chưa có generation/A/B để chứng minh chất lượng video tăng.

`AiPromptTrace.Preparation` lưu status, version, model phân tích/biên tập, hash PNG đã chuẩn bị, quan sát mở đầu và uncertainties. Metadata này giúp audit nhưng không tự xác nhận fidelity.

### Nguồn cho phần cập nhật

- [MiniMax H3 Video Prompt Writing Guide](https://github.com/MiniMax-AI/MiniMax-H3/blob/35491cdba2adfe62a510f725e8619f8e58783ea2/skills/h3-prompt-writing/references/base-en.txt): căn ảnh đầu, viết shot theo diễn tiến và ba trường nội dung. Không suy ra hiệu quả thực nghiệm từ việc công bố guide.
- [Google Gemini image understanding](https://ai.google.dev/gemini-api/docs/image-understanding): khả năng nhận ảnh inline để mô tả ảnh và ngân sách request inline.
- [Gemini generateContent API](https://ai.google.dev/api/generate-content): `systemInstruction`, content parts và `generationConfig`. Gemini adapter nay gửi system instruction trong trường riêng, đồng thời hỗ trợ ảnh inline; lời gọi chỉ có text vẫn dùng temperature cấu hình nếu không override.

### Kiểm chứng phần cập nhật

| Hành vi | Test trực tiếp |
|---|---|
| Phân tích đúng ảnh chuẩn bị, rewrite thành shot, giữ guardrail và đồng bộ prompt/fingerprint | `PrepareAsync_ValidObservationAndRewrite_AlignsPromptTraceShotAndFingerprints` |
| Cache theo owner, cùng input giữ fingerprint; crop/draft mới làm cache miss | `PrepareAsync_SameInput_UsesOwnerScopedCacheAndKeepsFingerprintsStable`, `PrepareAsync_ChangedCropOrDraft_InvalidatesCacheAndPassesActualCrop` |
| Không rewrite lại board đã chuẩn bị | `PrepareAsync_AlreadyPreparedBoard_IsIdempotent` |
| JSON/marker sai, độ dài đoạn hoặc tổng prompt quá giới hạn | `PrepareAsync_InvalidAnalysis_RetainsExactDraftAndSkipsRewrite`, `PrepareAsync_InvalidRewrite_RetainsExactDraft`, `PrepareAsync_TooLongRewrite_RetainsDraftWithoutTruncatingConstraints`, `PrepareAsync_FinalPromptOverProviderLimit_RetainsDraftInsteadOfDroppingGuardrails` |
| Provider không sẵn sàng, cache lỗi, caller cancellation | `PrepareAsync_ProviderUnavailable_RetainsDraftAndCachesShortFallback`, `PrepareAsync_CacheUnavailable_StillPreparesWithoutDiscardingResult`, `PrepareAsync_CallerCancelsDuringAnalysis_PropagatesCancellationWithoutFallbackCache` |
| P1 input đến editor không chứa bước chân mặc định | `PrepareAsync_P1Rules_SendsExplicitPlanWithoutDefaultStepToEditor` — xác nhận input, không chứng minh mọi output LLM tuân thủ |
| Disabled/Lite/Standard/source stale không gọi AI | `PrepareAsync_IneligibleScene_DoesNotReadImageOrCallGenerator` |
| Preview và runtime được nối; snapshot xác nhận được tái sử dụng | `PreviewAsync_ValidPlan_PreparesPromptBeforeReturningReview`, `PlanAsync_NewPlan_PreparesPromptButReusesConfirmedSnapshotWithoutAnotherCall` |
| API Gemini có bytes ảnh và system instruction riêng; text-only giữ cấu hình | `GenerateAsync_ImageAndInstruction_SendsInlinePixelsAndSeparateSystemInstruction`, `GenerateAsync_TextOnly_UsesConfiguredTemperatureWithoutImageParts`, `GenerateAsync_InvalidImage_FailsBeforeHttp` |
| Preparation/provider dùng chung pixels cho hai aspect | `PrepareAsync_CroppedSource_MatchesPixelsSentByMiniMax` |

Lệnh Application focused: `dotnet test tests/APCS.Application.UnitTests/APCS.Application.UnitTests.csproj -- --filter "FullyQualifiedName~VideoPromptPreparationServiceTests|FullyQualifiedName~MiniMaxH3PromptTests|FullyQualifiedName~AiVideoPromptCompilerTests|FullyQualifiedName~VideoPlanningServiceTests"` — **Passed: 111, Failed: 0, Skipped: 0**, exit 0.

Lệnh Infrastructure focused ban đầu gồm toàn bộ `MiniMaxVideoProviderTests`: **Passed: 28, Failed: 1, Skipped: 0**. Lỗi `StartAsync_UnsupportedSettings_AreRejectedBeforeAnyRequest(5,...)` cũng được ghi ở phần audit v1 bên dưới: test kỳ vọng 5 giây không hợp lệ nhưng adapter chấp nhận 4–15 giây. Không sửa duration policy trong nhiệm vụ này.

Lệnh focused thành công (loại method duration cũ khỏi lần chạy này, không skip/sửa test): `dotnet test tests/APCS.Infrastructure.UnitTests/APCS.Infrastructure.UnitTests.csproj --no-restore -- --filter "FullyQualifiedName~GeminiMultimodalPromptTests|FullyQualifiedName~GeminiTextGenerationProviderTests|FullyQualifiedName~MiniMaxFirstFramePreparerTests|(FullyQualifiedName~MiniMaxVideoProviderTests&FullyQualifiedName!~StartAsync_UnsupportedSettings_AreRejectedBeforeAnyRequest)"` — **Passed: 26, Failed: 0, Skipped: 0**, exit 0. Lỗi cũ vẫn thuộc toàn solution và được báo riêng.

Các test dùng mock Gemini và ảnh gradient cục bộ; không gọi Gemini/MiniMax thực, không sinh video, không xác nhận semantic adherence của LLM.

Đã chạy `dotnet test Capstone.sln`, exit 1:

| Project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Application unit | 1.255 | 11 | 0 |
| Infrastructure unit | 423 | 2 | 0 |
| Common unit | 46 | 0 | 0 |
| Domain unit | 28 | 0 | 0 |
| Infrastructure integration | 0 | 0 | 8 |

Tên 13 failure khớp các failure đã ghi ở audit v1: planner duration (1), planner transitions (8), transition fingerprint (1), MediaJob FullDecode (1), MiniMax duration 5 giây (1), generated table count (1). Không sửa các hành vi đó trong nhiệm vụ này. API build trong solution bị `MSB3027/MSB3021` do `APCS.Api` PID 23616 đang khóa DLL; không dừng process.

Để kiểm tra API compile mà không đụng DLL đang chạy, đã chạy `dotnet build API/API.csproj --no-restore -p:OutputPath=D:/FPT/Capstone/source/Backend/CAPSTONE_BE/.git/h3-preparation-api-build/`: exit 0, **0 errors**, 13 warnings từ package ImageSharp/repository hiện có. Output tạm nằm trong Git metadata, không stage. Build này không cập nhật process API đang chạy. Cần rebuild/restart theo quy trình phát triển để sử dụng code mới.

---

## Lịch sử: audit I2VA format v1 trước preparation

## Kết quả và phạm vi

Các AI scene mới chọn Premium `minimax_h3` được compile thành chỉ dẫn ảnh đầu và ba trường nội dung I2VA. Prompt review, storyboard snapshot và AI shot dùng cùng chuỗi cuối. Đây là thay đổi chuẩn bị prompt, chưa phải bằng chứng video tốt hơn.

Envelope áp dụng cho mọi template chạy H3. Chỉ Natural Motion mặc định của Product On Model được rút gọn nội dung hành động/camera. Fabric Detail, Brand Focus, Profile Tracking và các template khác giữ hướng chuyển động hiện có bên trong trường visual. H3-Lite chưa nhận định dạng này vì chưa xác minh guide áp dụng tương đương cho Lite.

Không gọi API tạo ảnh/video, không đọc hoặc sửa `.env`, không cập nhật database hay provider adapter. Snapshot đã lưu không bị viết lại. Khi lập kế hoạch mới, prompt thay đổi làm fingerprint thay đổi; không kỳ vọng tái sử dụng clip cũ khi prompt khác. Catalog Product On Model vẫn ở version 12; phiên bản định dạng riêng nằm ở trace part `h3_i2va_v1`.

## Nguồn chính thức

- [Video Prompt Writing Guide, commit cố định 35491cd](https://github.com/MiniMax-AI/MiniMax-H3/blob/35491cdba2adfe62a510f725e8619f8e58783ea2/skills/h3-prompt-writing/references/base-en.txt): các mục 2, 3.1, 4.1–4.3, 4.6–4.7. Dùng I2VA, không dùng cấu trúc sáu phần full-reference.
- [MiniMax Video Generation API](https://platform.minimax.io/docs/guides/video-generation): text và ảnh là content item riêng; ảnh mở đầu có role `first_frame`; giới hạn prompt đối chiếu là 7.000 ký tự.
- [H3 repository](https://github.com/MiniMax-AI/MiniMax-H3): Context-IR và H3-Base là thành phần khác nhau. Tuân theo guide không đồng nghĩa triển khai đầy đủ Context-IR.

Guide là tài liệu chính thức. Audit này kiểm tra implementation bằng unit test; không kiểm chứng thực nghiệm lợi ích độc lập của schema và không bảo đảm camera, anatomy hay thiết kế giữ đúng từng pixel.

## Phát hiện và thay đổi

### Đối chiếu lại với guide: đúng khung, nội dung mới đáp ứng một phần

Nguồn được xác minh thêm qua [README chính repository, mục Prompt Writing Skill](https://github.com/MiniMax-AI/MiniMax-H3#prompt-writing-skill) và [skill entry chính thức](https://github.com/MiniMax-AI/MiniMax-H3/blob/main/skills/h3-prompt-writing/SKILL.md). README gọi tên `base-en.txt`; skill yêu cầu dùng guide này cho text/keyframe modes và giữ tên trường/thứ tự. Đây là hướng dẫn và định dạng do MiniMax phát hành trong repository của họ. Prompt POD cụ thể trong ứng dụng là bản ứng dụng tự biên soạn, không phải một template POD nguyên bản được MiniMax xác nhận.

| Hạng mục đối chiếu | Đánh giá prompt hiện tại | Căn cứ/giới hạn |
|---|---|---|
| Mode I2VA và ảnh first frame | Đạt cho luồng đang kiểm tra | Adapter gửi một ảnh với role `first_frame`, không nhầm full-reference |
| Chỉ dẫn căn ảnh đầu tiên, blank line | Đạt | Mục 2.1; chỉ dẫn nằm đầu prompt, trước ba trường |
| Ba trường đúng tên và thứ tự | Đạt | Mục 2.2; không phải JSON request schema |
| Một `[Shot 1]`, không timestamp cho shot đầu | Đạt | Mục 4.2; thời điểm 0.00 ở chỉ dẫn ảnh không phải cut timestamp |
| Anchor → hành động → diễn tiến → kết quả | Đạt về thứ tự với Natural Motion mặc định | Mục 3.1; bước, chuyển trọng tâm, quay đầu, kết thúc có thứ tự |
| Mô tả cụ thể style và composition ngay đầu shot | Một phần | Mục 3.1/4.1 yêu cầu thiết lập cảnh mở đầu. Hiện chỉ nói các thuộc tính khớp ảnh, chưa mô tả cụ thể medium/close shot, hướng người, vị trí hay bối cảnh từ ảnh. Compiler chưa có bước phân tích ảnh |
| Body mô tả điều nhìn/nghe thấy theo timeline | Một phần | Mục 4.1. Vẫn có câu yêu cầu tạo video, nhãn `Product:`, câu chỉ dẫn điều khiển chung. Đây không phải lỗi cú pháp nhưng chưa thành caption liên tục, cụ thể như guide |
| Camera type + amplitude + speed | Đạt về ngữ nghĩa cho Natural Motion mặc định | Mục 4.3. Push-in nhỏ/chậm nằm trong câu; cách viết động từ trực tiếp có thể tự nhiên hơn. Không suy ra mọi custom template đều đạt |
| Soundscape | Đúng field, còn chung chung | Mục 4.6. Hai câu tiếng Anh trong một đoạn, không dùng N/A để giả định im lặng; chưa có âm thanh môi trường/hành động cụ thể. Không tự thêm tiếng vải hoặc bước chân nếu chưa phù hợp nguồn |
| Không có background music | Đạt | Mục 4.7 cho phép N/A khi không có nhạc nền |
| Chữ nhìn thấy giữ nguyên trong dấu ngoặc kép | Chưa xác minh | Mục 4.5. Prompt chỉ yêu cầu giữ lettering, chưa nhận dạng và đưa chuỗi chữ đã xác minh vào mô tả. Không được tự bịa chữ để đủ cấu trúc |
| P1 và RAG sau serialization | Cần cải thiện nội dung | P1 vẫn chứa nhãn action plan; RAG có lời hướng dẫn áp dụng kinh nghiệm được gửi thẳng. Nên chuyển thành hành vi nhìn/nghe thấy trong shot, giữ nguồn gốc ở trace; guide không có trường RAG riêng |

Kết luận kiểm tra lại: **đạt cấu trúc I2VA và một số quy tắc diễn đạt; chưa thể gọi là tuân thủ đầy đủ mọi hướng dẫn nội dung**. Lần triển khai này chủ yếu chuẩn hóa envelope, rút gọn Natural Motion và giữ các ràng buộc hiện có; chưa có bước visual grounding hoặc rewrite caption đầy đủ. 72 unit test được ghi bên dưới xác nhận các hành vi phần mềm đã nêu, không phải chứng nhận tuân thủ toàn guide hay chất lượng generation.

Phần đối chiếu này chỉ cập nhật tài liệu, không sửa prompt/code hoặc chạy lại generation. Hướng cải thiện kế tiếp là mô tả shot liên tục và cụ thể từ metadata/ảnh được xác minh, biên dịch P1/RAG thành diễn tiến quan sát được, và xử lý chữ thật khi có nguồn chính xác. Việc thêm visual analysis là thay đổi pipeline riêng, không được coi là đã có trong implementation hiện tại.

| Trước | Sau |
|---|---|
| Nhiều đoạn văn ghép lại, chưa có trường nội dung H3 | Envelope I2VA tại compiler trước snapshot/fingerprint |
| API có first frame, text chưa căn thời điểm | Dòng đầu căn `<Picture 1>` với `0.00 seconds`, `[Shot 1]` |
| Chưa có marker shot | Một `[Shot 1]`, không timestamp/cut bổ sung |
| Hành động Natural Motion được nhắc lại | Giữ bước → chuyển trọng tâm → liếc/quay đầu nhẹ → kết thúc; bỏ câu tóm tắt lặp |
| Camera dùng mô tả cinematic chung | Natural Motion: push-in, biên độ nhỏ, tốc độ chậm |
| Default eye-level/ánh sáng ấm có thể khiến ảnh bị diễn giải lại | Natural Motion H3 không P1 dùng `source_view`/`source_image`; snapshot effective settings cũng ghi đúng |
| Audio trong product guardrail | Tách audio built-in sang soundscape, background music `N/A`; giữ visual constraints |
| Envelope tăng chiều dài prompt | Tính cả overhead trước khi bỏ RAG ít liên quan; không cắt guardrail để nhường RAG |
| Có thể ghi đè base prompt đã chỉnh | Chỉ viết lại Natural Motion nếu base prompt khớp chính xác bản built-in; giữ base prompt tùy chỉnh |

P1 vẫn dùng `StartingState`, `PrimaryAction`, `EndingState`; không chèn bước chân, quay đầu hoặc chuyển động sản phẩm mặc định. P1 giữ normalized controls hiện có (`eye_level`, `warm_natural`). Vì vậy không suy diễn rằng mọi prompt H3 đều chuyển sang ánh sáng nguồn.

Advanced Controls vẫn tắt. Shared/custom directions bị loại theo luồng cũ. RAG là phần riêng trong trace và chịu guardrail cuối. Implementation chỉ tách câu audio built-in đã biết, không parse/sửa mọi câu audio tự do trong prompt catalog.

## Prompt trước/sau thực tế

Fixture: Product On Model / Natural Motion, `tshirt`, source role `Lifestyle`, 4 giây, không P1/RAG hoặc metadata màu. Đây là fixture biên dịch một scene, không tuyên bố storyboard chỉ 4 giây thỏa mọi quy tắc thời lượng cấp video.

Prompt trước được tái lập bằng nhánh chưa chọn provider, giữ hành vi cũ; prompt sau dùng `modelKey = minimax_h3`. Cả hai được trích từ assembly Application đã build bằng probe cục bộ, không gọi dịch vụ ngoài. Tổng ký tự có tính newline: trước **2.102**, sau **2.330**, tăng **228**. Prompt tổng dài hơn vì thêm cấu trúc; không có kết luận rằng prompt ngắn hơn, rẻ hơn hoặc tạo video tốt hơn.

### Trước

```text
Create one continuous 4-second live-action product-on-model fashion advertisement from the approved source image.

Product: T-shirt. Keep the product and any visible design as the visual focus. Match the approved image for its visible design, colors, fit and proportions.

Starting immediately from the pose in the image, the model takes one short, relaxed step toward the camera with the right foot. As the foot lands, the model transfers their weight onto it. The hips and upper body move forward together, with a small natural balancing movement in the shoulders and any free arm. A hand already resting in a pocket stays there. The garment moves naturally with the body, forming small folds where the fabric bends. Once comfortably balanced in the new stance, the model briefly glances toward the right-hand side of the frame. The head gently follows the gaze with a small, relaxed turn while the shoulders and torso keep their direction. Finish looking slightly off-camera, with a relaxed expression and gentle breathing. Perform one short step followed by this small head turn, with soft, unhurried movement. No continuous walking, sudden head movements or exaggerated posing. The camera performs a slow, smooth cinematic push-in at an eye-level camera angle, starting immediately and continuing in a steady, uninterrupted glide throughout the shot. Begin in a medium composition and keep the change in framing small; keep the model's head and product artwork in frame when visible in the opening image. Warm natural light with soft, realistic shadows. Preserve the source setting and natural shadows.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.
```

### Sau

```text
For the target video, at 0.00 seconds into the target video, <Picture 1> (from [Shot 1]) is fully referenced.

integrated_multimodal_description: [Shot 1] The opening visual style, composition, subject appearance, product, setting and lighting match <Picture 1>. Continue from that opening state with the following movement and preservation requirements.

Create one continuous 4-second product-on-model advertisement from the approved source image.

Product: T-shirt. Keep the product and any visible design as the visual focus. Match the approved image for its visible design, colors, fit and proportions.

Starting from the original pose, the model takes one short, relaxed step toward the camera with the right foot and gently transfers their weight onto it. The torso and shoulders follow the step naturally. Keep the original hand placement; a hand already resting in a pocket stays there. The garment moves naturally with the body, forming small folds where the fabric bends. After settling into the new stance, the model glances toward the right-hand side of the frame. The head follows with a small, relaxed turn while the torso maintains its direction. Finish comfortably balanced, looking slightly off-camera, with gentle breathing. No additional steps or exaggerated posing. The camera performs a push-in with small amplitude at slow speed, starting immediately and continuing smoothly throughout the shot. Retain the viewing direction established by the opening image and the opening composition, allowing only a small change in framing. Keep the model's head and product artwork in frame when visible in the opening image. Preserve the lighting shown in the source image. Preserve the source setting and natural shadows.

Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks.

overall_soundscape: Quiet ambient sound only. No dialogue, speech, singing or diegetic music.

non_diegetic_music: N/A
```

Không thêm tiếng vải mặc định vào soundscape vì template có thể dùng mug, poster hoặc phone case. Không tự tạo màu áo, nội dung chữ, logo, speaker ID hoặc nhạc. Native audio vẫn là chỉ dẫn generation; renderer cuối theo chính sách âm thanh của ứng dụng.

## Kiểm chứng implementation

Tests mới: [`MiniMaxH3PromptTests.cs`](../tests/APCS.Application.UnitTests/Features/Workflows/MiniMaxH3PromptTests.cs). Assertion về cuối studio prompt được chuyển sang cuối phần visual guardrail vì nay có hai trường audio theo sau.

| Yêu cầu | Bằng chứng test |
|---|---|
| First frame, ba trường đúng thứ tự, review/snapshot/AI shot khớp | `Plan_H3ProductOnModel_SendsFirstFrameAndThreeOrderedSections` — bốn subtemplate |
| Effective settings khớp prompt | `Plan_H3NaturalMotion_SnapshotsTheEffectiveSourceControls` |
| Hành động đúng thứ tự; giữ tay/camera/design | `CompileTrace_H3NaturalMotion_PreservesActionOrderAndUsesSourceLightingAndMeasuredCamera` |
| Không áp chuyển động vải cho vật cứng | `CompileTrace_H3NaturalMotionWithRigidProduct_DoesNotAddFabricMotion` — ba loại sản phẩm |
| P1 không bị chèn hành động; shared/custom đang tắt không lọt vào | `CompileTrace_H3WithP1_UsesOnlyTheRequestedActionAndKeepsDisabledDirectionsOut` |
| Audio tách khỏi visual, giữ artwork constraints | `CompileTrace_H3Audio_IsSeparatedFromProductPreservation` — hai template |
| Lite/legacy giữ định dạng cũ | `CompileTrace_OtherModel_RetainsTheUnformattedDraft`, `Plan_LiteScene_DoesNotOptIntoTheH3Format` |
| Giữ base prompt tùy chỉnh | `CompileTrace_H3CustomNaturalMotion_PreservesThePersistedCreativeDirection` |
| Prompt cũ đúng 7.000 ký tự: thêm envelope bỏ RAG, giữ guardrail/trace | `CompileTrace_H3FormatOverhead_TrimsOptionalExperienceBeforeExceedingTheLimit` |
| P1, source defaults và studio guardrail trong planner | Ba test planner của lệnh dưới |

Lệnh kiểm tra cuối, từ thư mục backend:

```powershell
dotnet test tests/APCS.Application.UnitTests/APCS.Application.UnitTests.csproj -- --filter "FullyQualifiedName~MiniMaxH3PromptTests|FullyQualifiedName~AiVideoPromptCompilerTests|FullyQualifiedName~VideoStoryboardPlannerTests.Plan_P1Rules_UsesTemplateDefaultsAndExposesExactPromptTrace|FullyQualifiedName~VideoStoryboardPlannerTests.Plan_LegacyLifestyleScene_UsesTemplateDefaultsWhileAdvancedControlsAreDisabled|FullyQualifiedName~VideoStoryboardPlannerTests.Plan_ApprovedStudioFrame_IsUsedForEveryShowcaseSceneWithSafeMotion"
```

**Passed: 72, Failed: 0, Skipped: 0**, exit code 0: 17 trường hợp H3 mới, 52 trường hợp compiler hiện có và ba test planner.

Baseline trước chỉnh sửa, lọc toàn bộ `AiVideoPromptCompilerTests|VideoStoryboardPlannerTests`: **Passed: 138, Failed: 10, Skipped: 0**. Mười lỗi xác nhận trước thay đổi: `Plan_FourScenes_DistributesAllFramesWithoutChangingDuration(3)`, tám trường hợp `Plan_MultipleAiScenes_UsesSelectedTransitionAtEveryBoundary`, và `Plan_ChangingTransition_ReusesAiClipsAndChangesStoryboardFingerprint`.

Đã chạy `dotnet test Capstone.sln` sau thay đổi production code, trước khi bổ sung bốn trường hợp kiểm thử cuối. Solution **không qua**:

- Application: Passed 1.228, Failed 11. Gồm mười lỗi planner trên và `MediaJobServiceTests.CompleteAsync_ValidVideoAndThumbnail_PublishesCandidateOnlyToReviewCheckpoint` ở `MediaQa.FullDecode`.
- Infrastructure unit: Passed 417, Failed 2: `StartAsync_UnsupportedSettings_AreRejectedBeforeAnyRequest(5,...)` và `Model_ReverseEngineeredPublicSchema_ContainsMappedTablesIncludingWorkflowRuntimeAndRag`.
- Common: Passed 46; Domain: Passed 28.
- Infrastructure integration: Passed 0, Skipped 8; không chứng minh integration đã thực thi thành công.
- API build lỗi `MSB3027/MSB3021`: DLL bị process `APCS.Api` PID 23616 khóa. Không dừng process đang chạy.

Chỉ mười lỗi planner được đo trực tiếp trong baseline lần này. Không dùng baseline hẹp để khẳng định nguyên nhân mọi lỗi toàn solution. Các kiểm thử khác và build blocker chưa được sửa trong phạm vi prompt này.

## Giới hạn và đánh giá tiếp theo

1. Chưa sinh video mới hoặc chạy A/B. Test chứng minh string/routing, không chứng minh chất lượng video hoặc giữ artwork.
2. Chân ngoài khung hình khiến model vẫn phải suy diễn bước chân. Prompt bảo toàn không loại bỏ rủi ro này.
3. Thay đổi gồm cả format, câu hành động, camera và defaults. Muốn đo riêng schema cần thêm nhánh giữ nguyên nội dung, chỉ đổi format.
4. Chữ/logo cần được kiểm tra trên vùng thiết kế qua thời gian; không tự động pass khi OCR không đọc được hoặc bị che khuất.
5. Chưa thêm serializer cho Lite, first/last-frame hoặc full-reference; không giả lập bằng cách đổi role tài sản.
6. Base prompt tùy chỉnh quá dài có thể vẫn vượt giới hạn khi không còn RAG. Adapter hiện có tiếp tục kiểm tra trước submit; không âm thầm cắt phần bắt buộc.
7. API đang chạy cần rebuild/restart theo quy trình phát triển để nạp binary mới; audit không có nghĩa process hiện tại đã dùng code mới.

Đánh giá video tiếp theo nên dùng cùng ảnh duyệt, thời lượng, model và output settings; sinh nhiều lần cho mỗi nhánh, chấm riêng hành động người mẫu, camera, artwork/chữ/logo, consistency và tỷ lệ clip được nhận. Báo cả lỗi và chi phí trên mỗi clip đạt chuẩn.
