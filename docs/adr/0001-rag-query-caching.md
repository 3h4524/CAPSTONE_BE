# ADR 0001: Cache RAG query embeddings and search results

- Status: Accepted (2026-10-29)
- Deciders: project owner
- Context area: `Infrastructure/Services` RAG pipeline, assistant chatbot (`search_faq`)

## Context

Every `IRagService.SearchAsync` call embeds the query through the Gemini embeddings API and then runs a pgvector plus full-text query. FAQ questions repeat constantly (starter chips, the same question from many sellers, several `search_faq` calls in one assistant turn). Each repeat costs Gemini quota and a database round trip. During assistant testing the Gemini key hit its quota (HTTP 429) and the assistant became unavailable.

## Decision

Add two caches on top of the existing `ICacheService` (Redis), both as decorators so `RagService` stays unchanged:

1. `MemoizedEmbeddingProvider` decorates `IRagEmbeddingProvider`. It memoizes the vector of a normalized query text.
   - Key: `rag:embedding:{sha256(model + normalized text)}`. Normalization: trim, collapse whitespace, lower-case.
   - TTL: `Rag:Cache:EmbeddingTtlHours` (default 24). An embedding depends only on model and text, so it is never invalidated.
   - Applies to every RAG consumer (listing, market research, assistant).
2. `CachedRagSearchService` decorates `IRagService`. It caches search results.
   - Only for `ContentType` `faq` or `policy`. Those results do not depend on the signed-in user. Listing, image, video and keyword searches are never cached.
   - Key: `rag:search:{generation}:{sha256(query, topK, contentType, language, sourceType)}`.
   - TTL: `Rag:Cache:SearchTtlMinutes` (default 10).
   - Invalidation: a generation token stored at `rag:search:generation` is replaced whenever a document is processed or activated (`ProcessIngestAsync`, `ActivateDocumentAsync`). Old keys become unreachable and expire by TTL, so no scan or prefix delete is needed.
3. Both caches degrade gracefully: a cache failure is logged and the call falls through to the real implementation. Errors from the embedding provider are never cached.
4. `Rag:Cache:Enabled` (default true) switches both caches off.

## Consequences

- Repeated FAQ questions cost zero Gemini calls and no database query until the TTL ends or a document changes.
- Cached search results are JSON round-tripped, so `RagSearchResultDto.Metadata` values come back as `JsonElement`. The cached content types (`faq`, `policy`) are consumed only for title, content, source URL and score, so this is acceptable. Other content types stay uncached for that reason.
- Up to `SearchTtlMinutes` of staleness is possible on other app instances only if the generation write fails; with a healthy Redis the generation change is immediate.
- A new RAG consumer that needs user-specific results must not use the `faq` or `policy` content type.

## Alternatives considered

- Cache only embeddings: removes the Gemini cost but keeps the database query. Rejected as incomplete.
- Cache inside `RagService`: couples caching to retrieval logic and makes `RagService` harder to test. Rejected in favour of decorators.
- Prefix delete on ingest: needs a Redis scan and races with concurrent writes. Rejected in favour of the generation token.
- Cache user-data tools (`list_workflows`, `get_subscription_overview`) for 30 to 60 seconds: deferred, low quota impact.
