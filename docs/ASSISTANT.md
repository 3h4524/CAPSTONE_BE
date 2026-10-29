# Assistant chatbot

The assistant answers help questions from the RAG knowledge base and operates on the signed-in user's workflows through tools. It lives in `Application/Features/Assistant` and is available to Seller and Admin accounts. Admins only get the FAQ, navigation and choice tools. Architecture and layering rules stay in [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/assistant/chat` | Streams the answer as Server-Sent Events. Rate limited to 20 requests per minute per user (HTTP 429). |
| GET | `/api/assistant/conversations` | The 20 most recent conversations. |
| GET | `/api/assistant/conversations/{id}` | Messages and pending actions of one conversation. |
| POST | `/api/assistant/actions/{id}/confirm` | Runs a prepared action. |
| POST | `/api/assistant/actions/{id}/reject` | Cancels a prepared action. |

The request body carries `conversationId`, `message` and a `context` (route, open workflow, selected nodes and a bounded snapshot of the open canvas). Failures that happen before the stream starts return `ProblemDetails`; later failures arrive as an `error` event.

### SSE events

`conversation`, `delta`, `tool_started`, `tool_finished`, `citations`, `client_action`, `pending_action`, `choices`, then exactly one of `done` or `error`. Every event is `event: <type>` followed by one JSON line of camelCase data.

## Tools

Each tool is a class implementing `IAssistantTool` (usually through `AssistantTool<TArgs>`) registered in `Application/DependencyInjection.cs`. `AssistantToolRegistry` offers a caller only the tools whose `IsAvailable` is true.

| Kind | Behaviour | Tools |
|---|---|---|
| Query | Runs immediately and returns data to the model | `search_faq`, `list_workflows`, `get_workflow`, `get_workflow_run`, `get_workflow_capabilities`, `list_batches`, `list_batch_products`, `get_usage_overview`, `get_subscription_overview` |
| ClientAction | Sends an action to the browser; the turn ends | `edit_workflow_canvas` (validated by `WorkflowCanvasPatchSimulator` and `WorkflowGraphValidator`), `navigate` (whitelisted routes only) |
| Confirmation | Stores an `assistant_pending_actions` row; runs only when the user confirms | `start_workflow_run`, `workflow_run_action` |
| Interaction | Shows reply chips | `offer_choices` |

Rules that must hold for every new tool:

- A tool never accepts a user id from the model. Ownership is enforced by the underlying service through `ICurrentUser`.
- Write actions are `ConfirmableAssistantTool<TArgs>`. The model can only prepare them; `AssistantService.ConfirmActionAsync` claims the row atomically (`pending` to `executing`), re-checks expiry (15 minutes) and runs `ExecuteConfirmedAsync`.
- Paid AI video steps are never exposed. `workflow_run_action` refuses `resume_mockups` and `retry` on an `ai_shot` workflow because they need a quote token that only the UI holds.
- Tools run sequentially because they share one `DbContext`. Results are capped at 12 000 characters.

To add a tool: create the class, register it, add unit tests next to the existing tool tests, and mention it in the system prompt in `AssistantPromptBuilder` only if the model needs guidance.

## Model and configuration

`IAssistantChatModel` is implemented by `GeminiAssistantChatModel` (Infrastructure, `Google.GenAI` through `Microsoft.Extensions.AI`). The agent loop in `AssistantAgentLoop` allows at most 5 tool rounds, then one final answer without tools.

| Key | Default | Notes |
|---|---|---|
| `Assistant:Model` | `gemini-3.5-flash` | |
| `Assistant:Temperature` | `0.3` | |
| `Assistant:MaxOutputTokens` | `2048` | |
| `Assistant:ApiKey` | empty | Falls back to `Gemini:TextGeneration:ApiKey`, then `Gemini:Embeddings:ApiKey`. |
| `Rag:Cache:*` | enabled | See [ADR 0001](adr/0001-rag-query-caching.md). |

## Knowledge base

FAQ articles are RAG documents with `contentType = faq`. Ingest them from Admin, RAG ingest, or `POST /api/rag/ingest`. Submitting again with the same `sourceId` supersedes the earlier version. Search results for `faq` are cached; a processed or activated document invalidates them.

## Schema

`scripts/sql/assistant-chatbot.sql` creates `assistant_conversations`, `assistant_messages` and `assistant_pending_actions`. Apply it to Neon first, then re-scaffold as described in `CONTRIBUTING.md`.
