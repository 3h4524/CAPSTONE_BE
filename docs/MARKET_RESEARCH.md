# Public marketplace research

`POST /api/market-research/retrieve-and-ingest` retrieves public Etsy listings for configured search queries, ranks the returned batch using the metrics supplied by Etsy, and queues selected listings for RAG ingestion. `topN` is applied per query/niche so one popular niche cannot consume the entire result set. The endpoint is authenticated and does not read any seller's private shop data.

The application contract is platform-neutral: each marketplace implements `ICommerceMarketDataConnector`, while viral scoring, normalization, RAG metadata and prompt use remain shared. A later Shopify/Amazon connector does not change the market-research service.

Etsy public API configuration is read from environment variables or `.env`:

```text
Etsy__ApiKey=your_keystring
Etsy__SharedSecret=your_shared_secret
Etsy__BaseUrl=https://api.etsy.com/v3/application/
```

Etsy requires the `x-api-key` header in `keystring:shared_secret` form. The connector uses the marketplace-wide active-listings search endpoint with `sort_on=score`; it deduplicates listing IDs across queries before scoring. Public listing metrics are not treated as sales claims. A score of zero is used when Etsy does not provide the metrics needed for scoring.

Example request:

```json
{
  "platform": 1,
  "queries": ["hiking shirt", "mountain gift"],
  "limitPerQuery": 25,
  "topN": 3,
  "minimumViralScore": 50
}
```

Each selected listing is ingested as `contentType = listing` with a stable source ID such as `etsy:public-listing:123`. Metadata records the platform, public seller scope, listing ID, score and observed metrics. The listing prompt treats these documents as references only; it must not copy their wording or transfer their product facts to the user's product. If five niches are submitted with `topN = 3`, the run can select up to 15 listings, subject to the minimum score and available results for each niche.

The sync can be run on demand by an administrator or scheduled from the admin Market Insights workspace. The admin schedule is disabled by default. The UI saves the shared schedule in Redis so the background worker can pick up changes without restarting the API:

```text
MarketResearch__Scheduled__Enabled=true
MarketResearch__Scheduled__Platform=1
MarketResearch__Scheduled__Queries__0=hiking shirt
MarketResearch__Scheduled__Queries__1=mountain gift
MarketResearch__Scheduled__LimitPerQuery=25
MarketResearch__Scheduled__TopN=3
MarketResearch__Scheduled__MinimumViralScore=50
MarketResearch__Scheduled__IntervalMinutes=1440
```

The admin worker runs once at startup and then checks the schedule every minute. `intervalMinutes` may be configured from 1 to 10080; when the interval has elapsed the worker collects the next `batchSize` queries that have no observation for the current UTC day yet. Each query is therefore collected at most once per UTC day however short the interval is; a one-off admin run can still collect a query again, and the later run of the day replaces that day's observation. OAuth-connected private seller data remains a separate capability and must be scoped to the consenting seller; a personal API credential must not be used to access other sellers' private data.

The admin schedule endpoints are `GET /api/market-research/schedule` and `PUT /api/market-research/schedule`. The admin Market Insights workspace also provides a one-off research action and local run summaries. Results are shown as a ranked snapshot with scanned count, selected count, average trend score, observed favorites/views, source links and RAG queue status; favorites and views remain public signals, not sales claims.

Every run also records one row per platform, query and UTC day in `market_query_observations`: Etsy's `count` of active listings matching the query (`total_results`) together with the sampling method of that run (`sample_size`, `limit_used`, `sort_on`). Queries are stored trimmed, single-spaced and lower-cased. The count describes the whole marketplace, so it is the only supply figure that can be compared across days; the sampled listings are the top `limitPerQuery` results of a search and change from day to day.

## Seller Market Radar

Market Radar is the seller-facing surface. It does not collect Etsy data directly. It reads the shared market opportunities produced by the admin collection pipeline, combines them with the seller's research profile, and ranks only opportunities that meet the seller-fit thresholds.

Seller profile and radar endpoints are:

```text
GET /api/market-opportunities/profile
PUT /api/market-opportunities/profile
POST /api/market-opportunities/refresh
GET /api/market-opportunities
GET /api/market-opportunities/insights
```

The `/insights` response is a niche-level conclusion, not a listing feed. It matches the seller profile to the collected market queries and returns the matched market group, whether the niche is worth following or should be avoided/watched, observed competitor count, a compact list of the top observed competitor seller IDs with representative listing links, competitor growth direction, listing supply direction, demand direction and evidence quality. Each matched keyword also exposes a demand proxy, competition, its active-listing count, the admin-collected market trend score when a selected `MarketOpportunity` exists, and a composite keyword score. Raw public listing snapshots remain an internal calculation source and are not returned to the seller page. These are market proxies derived from public listing snapshots, not Etsy sales or revenue claims. The radar reads the last 90 days of snapshots and observations.

The signals never compare two different samples, because a top-N sample changes with the number of runs, the configured limit and Etsy's ranking:

| Signal | Source |
|---|---|
| Competition | Rank of the keyword's latest `total_results` among all tracked queries (size percentile): bottom third low, middle third medium, top third high. Etsy matches keywords broadly (title, tags and description), so the count is only meaningful relative to other queries. A niche uses its median keyword. With fewer than 5 observed queries, or without an observation, the level is `unknown`. |
| Listing supply trend and competitor trend | `total_results` of the earlier half of the observed days against the later half; more than ±5 % is growing or reducing. A niche averages its keywords' changes. One observed day gives `unknown`. |
| Engagement (demand) trend | Views gained per day by the same listing between its daily observations, earlier half against later half, using only listings seen in both halves; at least 3 such listings and more than ±10 % are needed for up or down. |
| Demand proxy | Median favorites per 30 days of listing age (`original_creation_timestamp` from the raw payload): at least 1 high, at least 0.25 medium, otherwise low; fewer than 5 listings give `insufficient`. |
| Listing signals | From each query's latest collection day only, so listings that dropped out of the top no longer count: median listing age, share of listings under 30 days old, rising new listings (under 90 days and at least 3 favorites per 30 days, up to 5 returned with their public link), share of personalizable listings, price quartiles, sellers at the top, strong sellers (at least one listing with 3 favorites per 30 days) and the share of the sample held by the top 5 sellers. |

Evidence quality is `insufficient` below 5 listings, `high` with at least 20 listings over 7 observed days, `medium` with 20 listings or 3 observed days, and `low` otherwise. A competitor row no longer carries its own listing trend, because a seller's count inside the sample measures the sample rather than the seller's catalogue. The total listing count cannot be turned into a number of competing shops: one shop can own many matching listings, and Etsy shows at most about three listings per shop in the top results, so the sample's seller ratio does not extrapolate.

The niche response also returns `highlights` (plain statements built only from signals that have data), the `recommendation`, `actionPlan` and `risks` of its insight type, and each keyword's `sizePercentile`. Every opportunity in `GET /api/market-opportunities` carries `nicheMarket`: the competition, demand, sellers at the top, strong sellers and rising new listings of the niche it was collected for, from that niche's latest collection within 7 days. An opportunity card therefore shows the same market as the niche view instead of figures from its own one to three evidence listings.

The seller page can refresh its ranking after the admin dataset changes. Seller-specific insight calculation belongs in the radar layer, while public Etsy collection and raw metric inspection belong in the admin Market Insights workspace.

The legacy `/api/market-insights` controller is restricted to administrators for raw metric inspection and compatibility. Seller-facing recommendations should use Market Radar rather than that raw endpoint.
