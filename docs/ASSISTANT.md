# Assistant chatbot

The assistant answers help questions from the RAG knowledge base and operates on the signed-in user's workflows through tools. It lives in `Application/Features/Assistant` and is available to Seller and Admin accounts. Admins only get the FAQ, navigation and choice tools. Architecture and layering rules stay in [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/assistant/chat` | Streams the answer as Server-Sent Events. Rate limited to 20 requests per minute per user (HTTP 429). |
| GET | `/api/assistant/conversations` | The 20 most recent conversations. |
| GET | `/api/assistant/conversations/{id}` | Messages and pending actions of one conversation. |
| DELETE | `/api/assistant/conversations/{id}` | Soft-deletes one conversation and returns 202; the rows are purged by a background worker. |
| DELETE | `/api/assistant/conversations?except={id}` | Same for all of the user's conversations except the given one. |
| PUT | `/api/assistant/messages/{messageId}/client-actions/{actionId}` | Stores `applied` or `dismissed` for a client action chip so it survives reloads. |
| POST | `/api/assistant/actions/{id}/confirm` | Runs a prepared action. |
| POST | `/api/assistant/actions/{id}/reject` | Cancels a prepared action. |

The request body carries `conversationId`, `message` and a `context` (route, open workflow, selected nodes and a bounded snapshot of the open canvas). Failures that happen before the stream starts return `ProblemDetails`; later failures arrive as an `error` event.

### SSE events

`conversation`, `delta`, `tool_started`, `tool_finished`, `citations`, `client_action`, `pending_action`, `choices`, `status`, then exactly one of `done` or `error`. `status` (`{ "state": "retrying", "retryInSeconds": 8 }`) is sent while the assistant waits before retrying a rate limited or busy model. Every event is `event: <type>` followed by one JSON line of camelCase data.

## Tools

Each tool is a class implementing `IAssistantTool` (usually through `AssistantTool<TArgs>`) registered in `Application/DependencyInjection.cs`. `AssistantToolRegistry` offers a caller only the tools whose `IsAvailable` is true.

| Kind | Behaviour | Tools |
|---|---|---|
| Query | Runs immediately and returns data to the model | `search_faq`, `list_workflows`, `get_workflow`, `get_workflow_run`, `get_workflow_capabilities`, `list_batches`, `list_batch_products`, `get_usage_overview`, `get_subscription_overview`, `get_node_catalog`, `list_design_templates`, `list_style_presets`, `list_mockup_templates`, `list_video_templates` |
| ClientAction | Sends an action to the browser; the turn ends unless `moreRequests` is true | `edit_workflow_canvas` (validated by `WorkflowCanvasPatchSimulator` and `WorkflowGraphValidator`), `arrange_workflow_canvas` (only moves nodes; the browser computes the positions from the real node sizes), `navigate` (whitelisted routes only) |
| Confirmation | Stores an `assistant_pending_actions` row; runs only when the user confirms | `start_workflow_run`, `workflow_run_action` |
| Interaction | Shows reply chips | `offer_choices` |

Rules that must hold for every new tool:

- A tool never accepts a user id from the model. Ownership is enforced by the underlying service through `ICurrentUser`.
- Write actions are `ConfirmableAssistantTool<TArgs>`. The model can only prepare them; `AssistantService.ConfirmActionAsync` claims the row atomically (`pending` to `executing`), re-checks expiry (15 minutes) and runs `ExecuteConfirmedAsync`.
- Paid AI video steps are never exposed. `workflow_run_action` refuses `resume_mockups` and `retry` on an `ai_shot` workflow because they need a quote token that only the UI holds.
- Tools run sequentially because they share one `DbContext`. Results are capped at 12 000 characters.
- Successful query tool results (except `search_faq`) are kept in the assistant message metadata as `toolMemory` (800 characters per result, 2 400 per message). The prompt replays them as `<tool_memory>` for the last 3 assistant messages so follow-ups such as "the second one" need no new tool call. The API strips `toolMemory` from the metadata it returns.

To add a tool: create the class, register it, add unit tests next to the existing tool tests, and mention it in the system prompt in `AssistantPromptBuilder` only if the model needs guidance.

## Model and configuration

`IAssistantChatModel` is implemented by `GeminiAssistantChatModel` (Infrastructure, `Google.GenAI` through `Microsoft.Extensions.AI`). The agent loop in `AssistantAgentLoop` allows at most 5 tool rounds, then one final answer without tools.

| Key | Default | Notes |
|---|---|---|
| `Assistant:Model` | `gemini-3.5-flash` | |
| `Assistant:Temperature` | `0.3` | |
| `Assistant:MaxOutputTokens` | `2048` | |
| `Assistant:ThinkingLevel` | `Low` | `Minimal`, `Low`, `Medium` or `High`. |
| `Assistant:RateLimitRetries` | `2` | Retries after an HTTP 429, 500, 503 or 504 from Gemini (read from `ApiException.StatusCode`), only before any text was streamed. |
| `Assistant:OverloadRetryDelaySeconds` | `2` | Pause before retrying an overload error. |
| `Assistant:RateLimitRetryDelaySeconds` | `8` | Pause between those retries. |
| `Assistant:ApiKey` | empty | Falls back to `Gemini:TextGeneration:ApiKey`, then `Gemini:Embeddings:ApiKey`. |
| `Assistant:ApiKeys` | empty | Extra Gemini keys tried in order. In `.env` write one line separated by `;`, like `Cors__AllowedOrigins`: `Assistant__ApiKeys=key1;key2` (indexed `Assistant__ApiKeys__0` and an appsettings array also work). An HTTP 429 switches to the next key at once without waiting; the delayed retries start only when every key is rate limited. The working key is kept for later requests. |
| `Rag:Cache:*` | enabled | See [ADR 0001](adr/0001-rag-query-caching.md). |

## Knowledge base

FAQ articles are RAG documents with `contentType = faq`. Ingest them from Admin, RAG ingest, or `POST /api/rag/ingest`. Submitting again with the same `sourceId` supersedes the earlier version. Search results for `faq` are cached; a processed or activated document invalidates them.

## Schema

`scripts/sql/assistant-chatbot.sql` creates `assistant_conversations`, `assistant_messages` and `assistant_pending_actions`. Apply it to Neon first, then re-scaffold as described in `CONTRIBUTING.md`.

## Node catalog and connection rules

`Application/Features/Assistant/Catalog/WorkflowNodeCatalog.cs` describes every step type, the settings the assistant may change and their valid values. `WorkflowCanvasPatchSimulator` validates `update_config` against it (unknown keys, values outside the options, ranges, ids) and applies it with a deep merge; dotted keys such as `standardOptions.transition` are expanded. It also enforces the same connection rules as the frontend (`getConnectionIssue`): a step connects only to the next step present in run order, `generate-listing` is outside the run order, no duplicates or loops. Rejected operations come back to the model as `invalid_operations` so it can correct itself.

The catalog and the connection rule mirror the frontend (`src/constants/workflow.ts`, `src/helpers/workflow-graph.ts`). Update both sides together; unit tests compare the run order with `WorkflowCapabilityRegistry`.

## Deleting conversations

Deleting sets `deleted_at` immediately and signals `AssistantConversationPurgeWorker` (an in-memory coalescing queue) to remove the rows. The worker also sweeps at startup, so a restart never leaves soft-deleted data behind.

## Latency

Each model call costs several seconds, so the design minimises calls:

- **Help articles are retrieved before the model runs** (`AssistantKnowledgeBase.PrefetchAsync`, `faq` content type, the user's own message as the query, 1.2 s timeout, skipped when a workflow canvas is open). The articles go into the prompt as `<help_articles>`, so a help question needs one model call instead of two, and the repeated query hits the RAG caches.
- **Pure help answers are cached** for 30 minutes (first message of a chat, no canvas, no tool other than `search_faq`), keyed by the normalised message, role and page path (route without its query string) and by the RAG generation token, so re-indexing invalidates them. A cache hit replays the answer without calling the model (`model: "cache"`).
- **Tools that end the turn carry the user-facing sentence in a `reply` argument** (`edit_workflow_canvas`, `arrange_workflow_canvas`, `navigate`, `start_workflow_run`, `workflow_run_action`; `offer_choices` uses `question`). The loop streams it and skips the closing model call. Without a reply the model writes the closing sentence as before.
- **Messages with several requests.** Every ClientAction, Confirmation and Interaction tool gets an optional boolean `moreRequests` argument, added to its schema by `AssistantToolSchemas.Build`. The model sets it when the message still has an unanswered question or another action; the loop then calls the model again with tools allowed instead of ending the turn (still bounded by the 5 tool rounds). The replies of every action are shown, and text from different rounds is separated by a blank line.
- The node catalog is added to the system prompt when a canvas is open, so the model does not call `get_node_catalog` first.
- Thinking is set to `Assistant:ThinkingLevel` (default `Low`).
