# Configuration reference

All settings live in a single INI file managed through MelonLoader preferences:

```
<GameFolder>/UserData/MdrgAiDialog.cfg
```

It is created with defaults on first launch (`ModConfig.Load()` in `src/ModConfig.cs`). Edit it with the game closed, or use the in-game provider picker (**F7**) which writes the same file. Values shown below are the defaults.

## `[General]`

```ini
[General]
UsedProvider = "Ollama"
SystemPersona = "You are Jun, an advanced companion android..."
ProviderConfigured = false
```

| Key | Default | Meaning |
|---|---|---|
| `UsedProvider` | `Ollama` | Active provider. One of `Ollama`, `OpenAI`, `OpenRouter`, `Mistral`, `Google`, `DeepSeek`, `Claude`, `Jun`, `Mock` (exact spelling). |
| `SystemPersona` | built-in Jun persona | The character/persona part of the system prompt. The mod appends its own behavior and `#!` command instructions around it (see [chat pipeline](chat-pipeline.md)). Not used by the `Jun` provider (the server injects its own prompt). |
| `ProviderConfigured` | `false` | Set automatically once a provider is chosen in the first-run GUI. Set back to `false` to see the picker again on launch. |

## Standard provider sections

Each of `Ollama`, `OpenAI`, `OpenRouter`, `Mistral`, `Google`, `DeepSeek`, `Claude`, `Mock` gets its own section with the same keys:

```ini
[OpenAI]
ApiUrl = "https://api.openai.com/v1"
ApiKey = ""
Model = "gpt-4.1-mini"
Temperature = 0.8
TopK = -1
Reasoning = "Auto"
ReasoningPreFill = false
TimeoutSeconds = 600
```

| Key | Default | Meaning |
|---|---|---|
| `ApiUrl` | per provider (below) | Base URL, usually ending in `/v1`. Do **not** append `/chat/completions`. May include a reverse-proxy path prefix (e.g. `https://host/ollama/v1`) — Ollama model management requests respect it. |
| `ApiKey` | empty | Bearer token. Not needed for local Ollama. |
| `Model` | per provider (below) | Model identifier in the provider's naming scheme. |
| `Temperature` | `0.8` | Sampling temperature, clamped to 0.0–2.0. |
| `TopK` | `-1` | Top-K sampling; `-1` disables (omitted from the request). |
| `Reasoning` | `Auto` | `Auto` (let the provider decide), `Enabled`, or `Disabled`. Translated to each provider's own reasoning/thinking parameter — see [providers](providers.md). |
| `ReasoningPreFill` | `false` | Injects an empty `<think>` tag pre-fill to suppress or guide thinking on models that emit think-tags. |
| `TimeoutSeconds` | `600` | HTTP request timeout. |

### Default URL and model per provider

| Section | Default `ApiUrl` | Default `Model` |
|---|---|---|
| `[Ollama]` | `http://localhost:11434/v1` | `hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF` |
| `[OpenAI]` | `https://api.openai.com/v1` | `gpt-4.1-mini` |
| `[OpenRouter]` | `https://openrouter.ai/api/v1` | `deepseek/deepseek-r1-0528:free` |
| `[Mistral]` | `https://api.mistral.ai/v1` | `mistral-small-2506` |
| `[Google]` | `https://generativelanguage.googleapis.com/v1beta/openai` | `gemini-3-flash` |
| `[DeepSeek]` | `https://api.deepseek.com/v1` | `deepseek-chat` |
| `[Claude]` | `https://api.anthropic.com/v1` | `claude-haiku-4-5` |
| `[Mock]` | (empty) | (empty) |

## `[Jun]` — Jun webapp stack

Used both by the `Jun` chat provider and by TTS when `[Tts] ApiFormat = "Jun"`.

```ini
[Jun]
ApiUrl = "https://localhost"
Email = ""
Password = ""
ConversationId = 0
Model = ""
Reasoning = "Auto"
TimeoutSeconds = 600
```

| Key | Default | Meaning |
|---|---|---|
| `ApiUrl` | `https://localhost` | Base URL of the Jun webapp (NGINX terminates TLS there). Endpoints used: `/api/auth.php`, `/api/chat.php`, `/api/conversations.php`, `/api/tts.php`, `/api/models.php`. |
| `Email` / `Password` | empty | Webapp account credentials — the same login the web UI uses. |
| `ConversationId` | `0` | Server-side conversation to continue. `0` = create one on first chat and remember it in the game save. Set a fixed id to share one conversation across game / browser. |
| `Model` | empty | Model override forwarded to `chat.php`; empty = server default. |
| `Reasoning` | `Auto` | Reasoning effort forwarded to `chat.php`: `Auto`, `Low`, `Medium`, `High`. |
| `TimeoutSeconds` | `600` | HTTP request timeout. |

## `[Tts]` — text-to-speech

```ini
[Tts]
Enabled = false
ApiFormat = "Jun"
ApiUrl = "http://localhost:8880/v1"
ApiKey = ""
Model = "tts-1"
Voice = "af_heart"
Engine = "kokoro"
Speed = 1.0
Volume = 2.5
LipSync = true
LipSyncGain = 3.5
MaxConcurrentRequests = 2
TimeoutSeconds = 60
```

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Speak bot replies out loud via a TTS server. |
| `ApiFormat` | `Jun` | `Jun` = the webapp's `/api/tts.php` (auth via the `[Jun]` section) · `OpenAI` = `POST {ApiUrl}/audio/speech` with an OpenAI-style body (works with OpenAI TTS, Kokoro-FastAPI, openedai-speech, AllTalk, …). Both must return WAV. |
| `ApiUrl` | `http://localhost:8880/v1` | TTS base URL (**OpenAI format only**; the Jun format uses `[Jun] ApiUrl`). |
| `ApiKey` | empty | Bearer key (OpenAI format only, optional). |
| `Model` | `tts-1` | TTS model name (OpenAI format only). |
| `Voice` | `af_heart` | Voice name (Kokoro voice ids for the Jun format). |
| `Engine` | `kokoro` | Jun format only: `kokoro` or `pockettts`. |
| `Speed` | `1.0` | Speech speed multiplier (0.25–4.0). |
| `Volume` | `2.5` | Sample-level gain (0.0–5.0). Unity's `AudioSource.volume` caps at 1.0, so quiet TTS output is boosted at the sample level here. |
| `LipSync` | `true` | Drive Jun's Live2D mouth parameter from the audio amplitude. |
| `LipSyncGain` | `3.5` | Amplitude-to-mouth-openness gain (0.1–20.0). |
| `MaxConcurrentRequests` | `2` | How many sentences may synthesize in parallel while audio plays (1–8). |
| `TimeoutSeconds` | `60` | Per-request timeout. |

## Complete minimal examples

**Local Ollama (default):**

```ini
[General]
UsedProvider = "Ollama"

[Ollama]
ApiUrl = "http://localhost:11434/v1"
Model = "artifish/llama3.2-uncensored"
```

**OpenRouter (free cloud model):**

```ini
[General]
UsedProvider = "OpenRouter"

[OpenRouter]
ApiKey = "sk-or-..."
Model = "deepseek/deepseek-r1-0528:free"
```

**Jun webapp with voice:**

```ini
[General]
UsedProvider = "Jun"

[Jun]
ApiUrl = "https://your-host"
Email = "you@example.com"
Password = "..."
ConversationId = 0

[Tts]
Enabled = true
ApiFormat = "Jun"
Voice = "af_heart"
Engine = "kokoro"
```
