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

The sync can be run on demand by an administrator or scheduled from the admin RAG workspace. The schedule is disabled by default. The UI saves the schedule in Redis so the background worker can pick up changes without restarting the API:

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

The worker runs once at startup and then at the configured interval. OAuth-connected private seller data remains a separate capability and must be scoped to the consenting seller; a personal API credential must not be used to access other sellers' private data.

The admin schedule endpoints are `GET /api/market-research/schedule` and `PUT /api/market-research/schedule`. The UI also keeps a `Run now` action for one-off research. Results are shown as a ranked snapshot with scanned count, selected count, average trend score, observed favorites/views, source links and RAG queue status; favorites and views remain public signals, not sales claims.
