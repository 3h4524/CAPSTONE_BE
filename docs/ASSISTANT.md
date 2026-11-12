# Assistant chatbot

The assistant, named Podi in the Podivo app, answers help questions from the RAG knowledge base and operates on the signed-in user's workflows through tools. It lives in `Application/Features/Assistant` and is available to Seller and Admin accounts. Admins only get the FAQ, navigation and choice tools. Architecture and layering rules stay in [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/assistant/chat` | Starts a turn and streams it as Server-Sent Events. Rate limited to 20 requests per minute per user (HTTP 429). 409 `assistant.turn_in_progress` while the conversation already has a streaming answer. |
| GET | `/api/assistant/messages/{messageId}/stream` | Re-attaches to an answer: replays the events after `Last-Event-ID` (or `?after=<id>`, or from the start without either), then follows the live turn until its terminal event. Owner only. 404 `assistant.stream_not_found` once the buffer expired (reload the conversation instead). Not under the chat rate limit. |
| POST | `/api/assistant/messages/{messageId}/stop` | Stops a streaming answer from any API instance and returns 202. Owner only. 404 when the message is not the caller's, 409 `assistant.not_streaming` when the turn already ended. |
| GET | `/api/assistant/conversations` | The 20 most recent conversations. |
| GET | `/api/assistant/conversations/{id}` | Messages and pending actions of one conversation. Each message has a `status` (`done`, `streaming`, `error`, `stopped` or `interrupted`; user messages are `done`); a streaming answer returns the text saved so far, which may be empty. |
| DELETE | `/api/assistant/conversations/{id}` | Soft-deletes one conversation and returns 202; the rows are purged by a background worker. |
| DELETE | `/api/assistant/conversations?except={id}` | Same for all of the user's conversations except the given one. |
| PUT | `/api/assistant/messages/{messageId}/client-actions/{actionId}` | Stores `applied` or `dismissed` for a client action chip so it survives reloads. |
| GET | `/api/assistant/workflows/{workflowId}/run-progress` | Where the latest run of a workflow is and what the seller has to do next (Seller only). The frontend polls it to show Podi's run reminders. |
| POST | `/api/assistant/actions/{id}/confirm` | Runs a prepared action. |
| POST | `/api/assistant/actions/{id}/reject` | Cancels a prepared action. |

The request body carries `conversationId`, `message` and a `context` (route, open workflow, selected nodes and a bounded snapshot of the open canvas). Failures that happen before the stream starts return `ProblemDetails`; later failures arrive as an `error` event.

### SSE events

`conversation`, `delta`, `tool_started`, `tool_finished`, `citations`, `client_action`, `pending_action`, `choices`, `status`, then exactly one of `done` or `error`. `conversation` is always first and carries `conversationId`, `userMessageId` and `assistantMessageId`. `status` (`{ "state": "retrying", "retryInSeconds": 8 }`) is sent while the assistant waits before retrying a rate limited or busy model. Every event is `id: <Redis stream entry id>`, `event: <type>` and one `data:` line of camelCase JSON; the id is what a client sends back as `Last-Event-ID`.

Besides the model errors, the terminal `error` can be `assistant.stopped` ("Stopped.", after a stop), `assistant.timeout` (the turn ran longer than `Assistant:TurnTimeoutSeconds`) or `assistant.interrupted` (the API shut down during the turn).

## Resumable answers

A turn is not tied to the HTTP request, so closing the chat or reloading the page does not end it.

1. `POST /chat` validates, saves the user message and an assistant message with empty content and `metadata.status = "streaming"` (created 1 ms after the user message so ordering by `created_at` stays user then assistant), writes the `conversation` event to the turn buffer and queues the turn.
2. `AssistantTurnWorker` runs each queued turn in its own DI scope. `ActingUserContext` makes `ICurrentUser` return the sender, so tools and services see the same user as in the request; the roles travel in `AssistantCaller`. The turn has its own cancellation, ended by a stop, by `Assistant:TurnTimeoutSeconds` or by the API shutting down, never by the client disconnecting.
3. `AssistantTurnRunner` appends every event of `AssistantAgentLoop` to the buffer. When the loop ends, it updates the assistant message with the final content, metadata and status before the terminal event is written, so a client that reloads after the terminal event sees the final row.
4. The response of `POST /chat` and `GET /messages/{id}/stream` read the buffer (`AssistantTurnReader`) and end after the terminal event, when the client disconnects (only the reader stops), when the buffer disappears, or when the turn is older than the timeout plus one minute.

| Status | Meaning |
|---|---|
| `streaming` | The turn is running. Left out of the model history; a new message in the conversation gets 409. |
| `done` | Finished normally. |
| `error` | The model failed, gave no answer, the turn timed out, or the turn failed unexpectedly (`metadata.error` holds the code). |
| `stopped` | Stopped by the user; the partial text is kept. |
| `interrupted` | The API stopped during the turn. At startup `AssistantTurnWorker` marks every `streaming` row older than the timeout plus one minute as `interrupted`, so a turn still running on another instance is never touched; a `streaming` row older than that is also reported as `interrupted` by the conversation endpoint and does not block new messages. |

Redis (main instance, shared `IConnectionMultiplexer`, keys prefixed with `Redis:InstanceName`):

| Key or channel | Use |
|---|---|
| `{prefix}assistant:turn:{assistantMessageId}` | Stream of the turn's events (`type`, `data` fields), `XADD MAXLEN ~ 5000`. Expires 15 minutes after the last event, 10 minutes after the terminal one. |
| `{prefix}assistant:turn:{id}:notify` | Pub/sub channel published after each append. StackExchange.Redis cannot block on `XREAD` on a shared multiplexer, so readers `XRANGE` from their last id when notified and also poll every 500 ms so a lost notification never stalls them. |
| `{prefix}assistant:turn:{id}:stop` | Key (15 minutes) and pub/sub channel of a stop request. The runner subscribes to the channel, checks the key when it starts and every second. |

## Tools

Each tool is a class implementing `IAssistantTool` (usually through `AssistantTool<TArgs>`) registered in `Application/DependencyInjection.cs`. `AssistantToolRegistry` offers a caller only the tools whose `IsAvailable` is true.

| Kind | Behaviour | Tools |
|---|---|---|
| Query | Runs immediately and returns data to the model | `search_faq`, `list_workflows`, `get_workflow`, `get_workflow_run`, `get_run_progress`, `estimate_run_cost`, `get_workflow_capabilities`, `list_batches`, `list_batch_products`, `get_usage_overview`, `get_subscription_overview`, `get_node_catalog` (not offered when the node catalog is already in the prompt), `list_design_templates`, `list_style_presets`, `list_mockup_templates`, `list_video_templates` |
| ClientAction | Sends an action to the browser; the turn ends unless `moreRequests` is true | `edit_workflow_canvas` (validated by `WorkflowCanvasPatchSimulator` and `WorkflowGraphValidator`; it returns only the graph warning the patch introduces, not one the canvas already had), `arrange_workflow_canvas` (only moves nodes; the browser computes the positions from the real node sizes), `navigate` (whitelisted routes only), `run_workflow`, `save_workflow` and `continue_run` (see below) |
| Confirmation | Stores an `assistant_pending_actions` row; runs only when the user confirms | `start_workflow_run`, `workflow_run_action`, `review_designs`, `review_mockups` |
| Interaction | Shows reply chips | `offer_choices` |

### Running a workflow from the chat

The Run button of the workflow screen is orchestrated by the frontend (`useWorkflowExecution.start`): the cost dialog, the design batch job, design approval, mock-up generation and import, then a batch video run (`run-groups`) or a single video run. No single backend call does all of it, so Podi does not start runs itself:

- `run_workflow` and `save_workflow` emit a client action. The card's button puts a request in the frontend `workflow-command` store, and the toolbar of that workflow carries it out with its own Save and Run (a run with unsaved changes saves first). Paid steps are confirmed in the same cost dialog as a manual run; Podi never shows or confirms a cost itself. `run_workflow` reports a graph problem of the open canvas as a warning rather than an error, since an `edit_workflow_canvas` proposed earlier in the same turn may fix it.
- `start_workflow_run` (no canvas open) refuses workflows with design steps (`needs_workflow_screen`), because `POST /api/workflows/{id}/runs` skips the design and mock-up part; the prompt then sends the user to the workflow.
- `estimate_run_cost` reads `IRunCostEstimateService` for the saved workflow and is read only.
- `get_run_progress` and the run-progress endpoint use `AssistantRunProgressReader`. It takes the newest of the workflow's latest batch job (from the batch in Product Input, filtered by `workflowId` in the job config, drafts ignored), latest run group and latest run that is not a group child, and maps it to a `stage` (`not_started`, `designs`, `design_approval`, `designs_ready`, `mockup_approval`, `video`, `video_review`, `listing`, `export`, `publish`, `done`) and a `state` (`idle`, `working`, `waiting_for_user`, `failed`, `cancelled`, `done`) with a `nextStep` sentence for the model.
- Reviews during a run: `review_designs` approves (the first design of each product) or rejects the designs that wait in the latest design job, for all waiting products or the named ones, through `IDesignGenerationService.SetImageApprovalAsync`. `review_mockups` approves or rejects the pending mock-ups of the run group's designs (matched by product and artwork group) or of the single run's product through `IMockupAssetService.ReviewAsync`, with the revisions read at preparation; a mock-up changed in between is left for review and counted. Both refuse when the run is not at that stage.
- `continue_run` (canvas open, run at design approval, designs ready or mock-up approval) offers Continue. The frontend's `continueRun` puts the design job (and the video-follows note) in the URL and calls `applySelectionAndGenerateMockups`, or opens the run group or video run, selects Mockup Approval and asks its panel to press its own Continue (`panelContinue`, honoured for 30 seconds), so the AI quote dialog still appears there.
- Video review stays with `workflow_run_action` (`approve_video`, `reject_video`); `get_run_progress` lists each design's `runId` in `targets` so a batch run's videos can be reviewed one by one.
- Run reminders: while a workflow is open, or after the user pressed Run on a Podi card, the frontend polls the run-progress endpoint every 20 seconds (only while the run is not settled or is being watched). When the run waits for the user, or failed or finished in the last 24 hours, the launcher shows a dot and the chat shows a bar with a "What now?" chip that asks Podi. No model call is made until the user clicks it.

Rules that must hold for every new tool:

- A tool never accepts a user id from the model. Ownership is enforced by the underlying service through `ICurrentUser`.
- Write actions are `ConfirmableAssistantTool<TArgs>`. The model can only prepare them; `AssistantService.ConfirmActionAsync` claims the row atomically (`pending` to `executing`), re-checks expiry (15 minutes) and runs `ExecuteConfirmedAsync`.
- Paid AI video steps are never exposed. `workflow_run_action` refuses `resume_mockups` and `retry` on an `ai_shot` workflow because they need a quote token that only the UI holds.
- Tools run sequentially because they share one `DbContext`. Results are capped at 12 000 characters.
- Successful query tool results (except `search_faq`) are kept in the assistant message metadata as `toolMemory` (800 characters per result, 2 400 per message). The prompt replays them as `<tool_memory>` for the last 3 assistant messages so follow-ups such as "the second one" need no new tool call. The API strips `toolMemory` from the metadata it returns.
- The client actions a message proposed (kind, settings cut to 800 characters) and the user's choice for each (`applied`, `dismissed` or `undecided`, from `clientActionStates`) are replayed the same way as `<action_memory>`, so the model knows what it already proposed when the user refers to it or says it did not work.
- When the client sends node positions, `app_context` gets `canvas.layout` (`left to right in one row`, `top to bottom in one column` or `spread over several rows and columns`) and lists the nodes in reading order. Raw coordinates are not sent to the model, so it describes the layout in words.

To add a tool: create the class, register it, add unit tests next to the existing tool tests, and mention it in the system prompt in `AssistantPromptBuilder` only if the model needs guidance.

## Model and configuration

`IAssistantChatModel` is implemented by `GeminiAssistantChatModel` (Infrastructure, `Google.GenAI` through `Microsoft.Extensions.AI`). The agent loop in `AssistantAgentLoop` allows at most 5 tool rounds, then one final answer without tools. Before that final round it adds a `<final_answer>` note telling the model it can no longer call tools and must say what it found, what blocked it and the next step. If the turn still ends with nothing visible and a tool failed, the loop streams a fallback built from the most recent failed tool's `message` (for example "I couldn't start the run: ...") instead of `assistant.empty_response`; failures that only describe internal mistakes (`unknown_tool`, `invalid_arguments`, `invalid_operations`, `tool_failed`, `missing_run`) are not used. Such a turn is not saved to the answer cache.

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
| `Assistant:TurnTimeoutSeconds` | `180` | 10 to 1800. A turn still running after this ends with `assistant.timeout`. |
| `Assistant:ApiKeys` | empty | Extra Gemini keys tried in order. In `.env` write one line separated by `;`, like `Cors__AllowedOrigins`: `Assistant__ApiKeys=key1;key2` (indexed `Assistant__ApiKeys__0` and an appsettings array also work). An HTTP 429 switches to the next key at once without waiting; the delayed retries start only when every key is rate limited. The working key is kept for later requests. |
| `Rag:Cache:*` | enabled | See [ADR 0001](adr/0001-rag-query-caching.md). |

## Knowledge base

FAQ articles are RAG documents with `contentType = faq`. Ingest them from Admin, RAG ingest, or `POST /api/rag/ingest`. Submitting again with the same `sourceId` supersedes the earlier version. Search results for `faq` are cached; a processed or activated document invalidates them.

## Schema

`scripts/sql/assistant-chatbot.sql` creates `assistant_conversations`, `assistant_messages` and `assistant_pending_actions`. Apply it to Neon first, then re-scaffold as described in `CONTRIBUTING.md`.

## Node catalog and connection rules

`Application/Features/Assistant/Catalog/WorkflowNodeCatalog.cs` describes every step type, the settings the assistant may change and their valid values. `WorkflowCanvasPatchSimulator` validates `update_config` against it (unknown keys, values outside the options, ranges, ids) and applies it with a deep merge; dotted keys such as `standardOptions.transition` are expanded. `add_node` accepts an optional `configJson` checked with the same rules, and its normalized operation carries the result as `config`. It also enforces the same connection rules as the frontend (`getConnectionIssue`): a step connects only to the next step present in run order, `generate-listing` is outside the run order, no duplicates or loops. The run order ends with `export-zip`, `publish-printify`, `publish-etsy`; the `type` enum of `edit_workflow_canvas` and the run order in the system prompt are built from the catalog, so a new step type only has to be added there. Rejected operations come back to the model as `invalid_operations` so it can correct itself.

Step names are unique: the step type name, numbered when it repeats (`Design image`, `Design image 1`, `Design image 2`). `add_node` ignores any label from the model and takes the next free name with the same rule as the frontend (`WorkflowCanvasPatchSimulator.UniqueLabel`, `uniqueNodeLabel` in `src/helpers/workflow-graph.ts`). The model is not offered a rename operation; an unexpected `rename_node` is rejected as an unknown operation. When a step name in the user's message matches several steps, the prompt tells the model to ask with `offer_choices` instead of guessing.

The catalog and the connection rule mirror the frontend (`src/constants/workflow.ts`, `src/helpers/workflow-graph.ts`). Update both sides together; unit tests compare the run order with `WorkflowCapabilityRegistry`.

## Deleting conversations

Deleting sets `deleted_at` immediately and signals `AssistantConversationPurgeWorker` (an in-memory coalescing queue) to remove the rows. The worker also sweeps at startup, so a restart never leaves soft-deleted data behind.

## Latency

Each model call costs several seconds, so the design minimises calls:

- **Help articles are retrieved before the model runs** (`AssistantKnowledgeBase.PrefetchAsync`, `faq` content type, the user's own message as the query, 1.2 s timeout, skipped when a workflow canvas is open). The articles go into the prompt as `<help_articles>`, so a help question needs one model call instead of two, and the repeated query hits the RAG caches.
- **Pure help answers are cached** for 30 minutes (first message of a chat, no canvas, no tool other than `search_faq`), keyed by the normalised message, role and page path (route without its query string) and by the RAG generation token, so re-indexing invalidates them. A cache hit replays the answer without calling the model (`model: "cache"`).
- **Tools that end the turn carry the user-facing sentence in a `reply` argument** (`edit_workflow_canvas`, `arrange_workflow_canvas`, `navigate`, `start_workflow_run`, `workflow_run_action`; `offer_choices` uses `question`). The loop streams it and skips the closing model call. Without a reply the model writes the closing sentence as before.
- **Messages with several requests.** Every ClientAction, Confirmation and Interaction tool gets a required boolean `moreRequests` argument, added to its schema by `AssistantToolSchemas.Build`. When the model sets it, the loop calls the model again with tools allowed instead of ending the turn. Small models often leave it false, so after a round that ended with an action and a reply the loop runs one **completion check**: it adds a `<completion_check>` note and calls the model once more with tools allowed. The model answers a question it skipped, calls an action it skipped, or replies `DONE`. The text of the check is held until the call ends and dropped when it is `DONE` or when at least 60% of its words already appear in the answer (a repeated summary). The check runs at most once per turn, not after `offer_choices` and not when `moreRequests` was already used; it costs one extra model call per action turn. The replies of every action are shown, and text from different rounds is separated by a blank line.
- The node catalog is added to the system prompt when a canvas is open (`AssistantPromptBuilder.IncludesNodeCatalog`), and `get_node_catalog` is then not offered at all.
- Without an open canvas, the prompt tells the model not to explore when a request needs canvas edits: it says what is missing and offers a `navigate` button to `/workflows?id=<id>`. When `start_workflow_run` fails it explains the returned reason instead of investigating.
- Thinking is set to `Assistant:ThinkingLevel` (default `Low`).
- **Prompt caching.** Gemini caches a repeated prompt prefix implicitly. The system text keeps the stable part first (instructions, then the node catalog) and the date and role last, and `app_context` rides on the latest user message only. Each model call logs its input, cached and output token counts (`Assistant model usage`) so the hit rate can be measured. Gemini's server-side conversation state (Interactions API, `previous_interaction_id`) is not used: the .NET SDK does not expose it, tools and the system instruction would still be sent each turn, an interaction is bound to one API key while the assistant rotates keys on 429, and the loop rewrites history (`<final_answer>`, `<completion_check>`, memories, retries).
