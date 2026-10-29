# AI Opportunity Radar

## 1. Mục tiêu

AI Opportunity Radar biến dữ liệu listing công khai từ Etsy thành insight có thể hành động cho seller. Hệ thống không cam kết doanh số và không gọi đây là dự đoán doanh thu; hệ thống đánh giá cơ hội dựa trên tín hiệu quan sát được, mùa vụ và mức độ phù hợp với catalog.

Luồng chính:

```text
Etsy public signals
  -> historical snapshots
  -> deterministic opportunity score
  -> AI explanation and recommendations
  -> listing brief and action plan
```

## 2. Giá trị cho seller

Feature trả lời ba câu hỏi:

- Niche hoặc keyword nào nên ưu tiên?
- Vì sao hệ thống đề xuất cơ hội này?
- Seller nên làm gì tiếp theo và khi nào nên triển khai?

Một opportunity có thể đề xuất:

- Product concept.
- Audience và occasion.
- Keyword direction.
- Thời điểm nên publish.
- Listing-content brief.
- Checklist hành động.

## 3. Phạm vi MVP

MVP nên bao gồm:

1. Lưu snapshot listing theo từng lần research.
2. Tính trend growth, engagement, seasonality và relative competition.
3. Tạo Opportunity Score từ 0 đến 100.
4. Gắn confidence và giải thích lý do.
5. Hiển thị opportunity list và detail.
6. AI tạo summary, product concept và launch checklist.
7. Save, dismiss và create listing brief.

Chưa làm trong MVP:

- Dự đoán chính xác sales hoặc revenue.
- Tự động publish listing.
- Copy title, description, tags hoặc artwork của seller khác.
- Machine-learning model phức tạp khi chưa có đủ dữ liệu lịch sử.

## 4. User flow

```text
Admin/Seller
  -> chọn keyword hoặc niche
  -> chạy Etsy research
  -> hệ thống lưu snapshot
  -> generate opportunities
  -> xem score và evidence
  -> AI giải thích cơ hội
  -> tạo listing brief
  -> chuyển sang listing content hoặc mockup workflow
```

## 5. Data model

### 5.1 Market research snapshot

Mỗi listing được lưu lại ở mỗi research run để có dữ liệu theo thời gian.

```text
Id
ResearchRunId
Keyword
ExternalListingId
Title
DescriptionSnapshot
TagsSnapshot
Category
Favorites
Views
TrendScore
SourceUrl
ObservedAtUtc
```

### 5.2 Seasonal event

```text
Id
Name
Region
StartDate
PeakDate
EndDate
RelatedKeywords
Description
IsActive
```

Seed data ban đầu: Christmas, Halloween, Valentine's Day, Mother's Day, Father's Day, Graduation, Back to school, Wedding season, Summer travel và Camping season.

### 5.3 Opportunity

```text
Id
Keyword
Title
Score
Confidence
TrendGrowthScore
EngagementScore
SeasonalityScore
CompetitionGapScore
CatalogFitScore
ScoringVersion
AiSummary
AiRecommendations
Status
CreatedAt
UpdatedAt
```

`ScoringVersion` giúp giải thích đúng cách một opportunity được tính ở thời điểm tạo ra nó.

## 6. Opportunity Score

Backend tính điểm deterministic. AI chỉ giải thích và đề xuất, không tự tính điểm.

```text
Opportunity Score =
    Trend Growth       * 0.30
  + Engagement         * 0.25
  + Seasonality        * 0.20
  + Competition Gap    * 0.15
  + Catalog Fit        * 0.10
```

Tất cả thành phần được chuẩn hóa về 0-100. Nếu thiếu dữ liệu, response phải trả về `unavailable` và điều chỉnh trọng số rõ ràng.

### Thành phần điểm

- Trend Growth: views, favorites, trend score và số listing high-signal tăng theo thời gian.
- Engagement: `favorites / views` khi có đủ dữ liệu.
- Seasonality: khoảng cách đến peak và thời gian seller còn lại để chuẩn bị.
- Competition Gap: số listing, phân phối score, high-signal listings và keyword overlap.
- Catalog Fit: mức phù hợp với product type, audience, niche và khả năng tái sử dụng asset.

Competition là cạnh tranh tương đối, không phải market size tuyệt đối. Nếu chưa có catalog data, Catalog Fit phải là `not evaluated`.

## 7. Confidence

Confidence có thể là `High`, `Medium` hoặc `Low`. Confidence phản ánh chất lượng dữ liệu, không chỉ score.

Inputs gồm số observation periods, số listing, mức đầy đủ của views/favorites, độ ổn định của score và sự đồng thuận giữa các signals.

Ví dụ: Score 84, confidence Medium vì trend tăng mạnh nhưng mới có hai observation periods và không có sales data.

## 8. AI contract

AI chỉ nhận dữ liệu đã được backend tính và evidence có nguồn.

```json
{
  "keyword": "hiking gift",
  "opportunityScore": 84.2,
  "confidence": "medium",
  "trendGrowth": 27.4,
  "favoritesGrowth": 15.1,
  "listingCount": 42,
  "relativeCompetition": "medium",
  "seasonalEvent": {"name": "Father's Day", "weeksUntilPeak": 7},
  "topKeywords": ["hiking gift for dad", "outdoor dad shirt"],
  "evidence": []
}
```

AI output cần có `summary`, `whyNow`, `productConcepts`, `keywordDirections`, `launchChecklist`, `risks` và `disclaimer`.

AI không được tự tạo sales, revenue, conversion rate, market size, chứng nhận hoặc product fact không có trong input.

## 9. API đề xuất

```http
POST /api/admin/opportunities/generate
GET  /api/admin/opportunities
GET  /api/admin/opportunities/{opportunityId}
POST /api/admin/opportunities/{opportunityId}/save
POST /api/admin/opportunities/{opportunityId}/dismiss
POST /api/admin/opportunities/{opportunityId}/listing-brief
```

List API nên hỗ trợ filter: keyword, eventId, scoreMin, confidence, status, from/to và page/pageSize.

## 10. UI design

### Opportunity dashboard

Summary cards: opportunities found, high-potential opportunities, upcoming seasonal events, average opportunity score và saved opportunities.

Mỗi card hiển thị score, keyword, summary, trend direction, seasonal timing, confidence và primary action.

### Detail page

Hiển thị theo thứ tự:

1. Summary và recommended action.
2. Score breakdown.
3. Historical trend chart.
4. Seasonal timeline.
5. Evidence listings và source links.
6. Product concepts.
7. Keyword direction.
8. Launch checklist.
9. Confidence và limitations.

Observed data, calculated metrics và AI recommendations phải có style khác nhau.

## 11. Backend implementation plan

### Phase 1: Snapshot foundation

- Tạo migration cho market research snapshots.
- Lưu snapshot sau mỗi research run.
- Gắn ResearchRunId và retention policy.
- Viết repository và unit tests.

### Phase 2: Scoring

- Implement pure scoring service.
- Implement trend growth, engagement, seasonality, competition gap và confidence.
- Test missing và partial signals.

### Phase 3: Opportunity API

- Tạo opportunity service.
- Tạo generate/list/detail endpoints.
- Lưu evidence listings.
- Thêm save/dismiss.
- Bảo vệ bằng admin authorization.

### Phase 4: AI

- Tạo provider adapter.
- Dùng structured JSON output.
- Validate schema trước khi lưu.
- Lưu model, prompt và scoring version.
- Có trạng thái AI generation failed.

## 12. Frontend implementation plan

- Thêm route `admin/opportunities`.
- Thêm dashboard và filters.
- Thêm opportunity cards và detail view.
- Thêm score breakdown, chart và source links.
- Thêm save/dismiss.
- Thêm create listing brief.
- Kết nối listing brief với Listing Content Generator.

## 13. Testing

### Backend unit tests

- Complete signals.
- Missing views hoặc favorites.
- Chỉ có một snapshot.
- Trước, trong và sau seasonal event.
- Competition normalization.
- Confidence calculation.
- Scoring version.
- Invalid AI response.

### Integration và acceptance tests

- Generate opportunity từ snapshots.
- Filter, save và dismiss opportunity.
- Create listing brief.
- Reject AI output không hợp lệ.
- Chạy cùng một keyword ít nhất hai lần và kiểm tra timestamps khác nhau.
- Kiểm tra score components có giá trị hoặc trạng thái unavailable.
- Kiểm tra explanation khớp với evidence.

## 14. Metrics

Đo opportunity detail views, save rate, dismiss rate, listing briefs created, thời gian từ insight đến draft, usefulness rating, tỷ lệ insight có đủ evidence và hiệu quả về sau của listing được tạo từ opportunity.

Success criterion đầu tiên nên là productivity: seller đi từ trend signal đến listing brief dùng được trong vài phút.

## 15. Risks and safeguards

- Public Etsy data không bao gồm sales, revenue và conversion rate.
- Sample nhỏ không được gắn High confidence.
- Luôn hiển thị observation period và last refreshed.
- AI output phải có schema validation.
- Mọi insight phải có evidence và source URL.
- Không khuyến khích copy listing content, artwork hoặc branding.
- Hiển thị disclaimer rằng insight chỉ mang tính tham khảo.

## 16. Kết luận

Feature nên được xây theo mô hình:

```text
Evidence -> score -> explanation -> action
```

Đây là hướng phù hợp để biến RAG từ kho lưu trữ listing thành decision-support system, đồng thời tạo giá trị AI rõ ràng mà không phóng đại khả năng dự đoán của dữ liệu Etsy.
