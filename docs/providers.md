# AI providers

Providers live in `src/AiProviders/`. All of them derive from the abstract `AiProvider` base class, which owns the chat history (`List<ChatMessage>` of `system` / `user` / `assistant` roles), exposes history snapshot/restore for save-game persistence, and defines the streaming contract:

```csharp
public abstract class AiProvider(AiProviderConfig config) {
  public abstract Task WarmUp();                                   // called when the chat button is pressed
  public virtual  Task<bool> EnsureReadyForChat();                 // pre-chat checks (login, model download)
  public abstract IAsyncEnumerable<string> SendMessage(string msg); // streamed response chunks
  ...
}
```

The active provider is selected by `[General] UsedProvider` and instantiated in `AiAdapter.CreateProvider()` (`src/Chat/AiAdapter.cs`). Provider name → class:

| Name | Class | Base | Notes |
|---|---|---|---|
| `Ollama` | `Ollama` | `OpenAi` | Default. Adds model management on top of the OpenAI-compatible endpoint. |
| `OpenAI` | `OpenAi` | `AiProvider` | The generic OpenAI-compatible implementation; works with any compatible service. |
| `OpenRouter` | `OpenRouter` | `OpenAi` | Maps `Reasoning` to OpenRouter's `reasoning = { enabled, exclude: true }` payload field. |
| `Mistral` | `Mistral` | `OpenAi` | Plain OpenAI-compatible; no extra payload fields. |
| `Google` | `Google` | `OpenAi` | Uses Google's OpenAI-compatibility endpoint; `Reasoning = Disabled` sends `reasoning_effort = "none"`. |
| `DeepSeek` | `DeepSeek` | `OpenAi` | Maps `Reasoning` to `thinking = { type: enabled/disabled }`. |
| `Claude` | `Claude` | `OpenAi` | Talks to Anthropic's OpenAI-compatibility layer; maps `Reasoning` to `thinking = { type: enabled/disabled }`. |
| `Jun` | `Jun` | `AiProvider` | The self-hosted Jun webapp stack — see [jun-webapp.md](jun-webapp.md). |
| `Mock` | `Mock` | `AiProvider` | Offline test provider; canned streamed responses keyed by the input (`"1"`, `"2"`, …) exercising commands, newline handling, whitespace, and invalid-command paths. |

## `OpenAi` — the workhorse

`src/AiProviders/OpenAi.cs` implements streaming chat completions against `POST {ApiUrl}/chat/completions`:

- **Headers:** `Authorization: Bearer <ApiKey>`, plus `HTTP-Referer`/`X-Title` identification headers (used by OpenRouter for app attribution).
- **Streaming:** server-sent events are read line-by-line; `delta.content` fragments are yielded as they arrive. The full reply is accumulated and appended to the history as the `assistant` message.
- **Payload knobs from config:** `model`, `temperature`, `top_k` (only when `TopK != -1`), plus subclass-specific reasoning fields (see table above).
- **Reasoning pre-fill:** with `ReasoningPreFill = true`, an empty `<think></think>` assistant prefix is injected to suppress/steer chain-of-thought on models that emit think tags.
- **Errors** are caught and surfaced into the dialog text rather than crashing the chat loop.

## `Ollama` — local models with in-game downloads

Extends `OpenAi` with a second `HttpClient` against the native Ollama API (the URL minus the `/v1` suffix, so reverse-proxy path prefixes are preserved):

- On construction it checks `api/tags` in the background to see whether the configured model exists.
- `EnsureReadyForChat()`: if the model is missing, the game shows a native confirmation and then a **progress popup** while the mod pulls the model through `api/pull`, streaming progress.
- `WarmUp()` issues a request when the chat opens so model loading latency is hidden while the player types.

## `Jun` — the webapp stack provider

`src/AiProviders/Jun.cs` speaks to the [Jun webapp](https://github.com/efficiencyx/Jun) (NGINX + PHP proxy + Ollama + TTS) through the same `/api/chat.php` endpoint the web UI uses:

- **Auth:** shared `JunSession` singleton (cookie-based login via `/api/auth.php`, auto re-login on 401). The same session is reused by the TTS client.
- **Conversations:** `EnsureReadyForChat()` logs in, picks or creates a server-side conversation (`ConversationId = 0` → create and remember the id in the game save under `jun-conversation-id`), then pulls the shared history from `/api/conversations.php` — so a chat started in the browser or Telegram continues in-game.
- **History cap:** at most 78 messages are sent per request (`chat.php` rejects > 80).
- **System prompt:** the *server* injects its own system prompt and strips client-sent `system` roles, so the mod's `#!` command instructions do not apply. Instead, the `[A:...]` action tags emitted by the Jun finetune are translated on the fly into the game's `#!bot.*` commands by `JunActionTranslator` (`src/JunApi/JunActionTranslator.cs`) — a streaming filter that holds back text from `[` to the matching `]` (up to 300 chars) so tags split across stream chunks still parse, maps emotes (`happy`, `sad`, `angry`, `embarrassed`, …) to expression commands, and drops tags with no in-game equivalent.

## Model discovery for the settings UI

`src/Ui/ModelCatalog.cs` populates the in-game model list: `GET {ApiUrl}/models` for OpenAI-compatible providers, `/api/models.php` for Jun (a proxy over Ollama's `/api/tags`). Failures never throw — the UI falls back to a curated per-provider list plus a free-text "custom" entry.

## Adding a new provider

1. Create a class in `src/AiProviders/` deriving from `OpenAi` (if the service is OpenAI-compatible) or `AiProvider` (if not). Override `CreateRequestPayload` for provider-specific fields.
2. Register a config section with defaults in `ModConfig.Load()` via `SetupProvider("Name", defaultUrl, defaultModel)`.
3. Add the name to `ModConfig.ProviderNames` and to the `switch` in `AiAdapter.CreateProvider()` (the names must match exactly).
4. Optionally add a card to `ProviderPicker.Providers` and a curated model list in `ModelCatalog` so it shows up nicely in the first-run UI.
