# Jun webapp stack

The [Jun webapp stack](https://github.com/efficiencyx/Jun) is a self-hosted Docker bundle — NGINX (TLS termination), a PHP API proxy, Ollama, and a CPU TTS engine (Kokoro / pocket-tts) — that this mod can use as its provider. Its selling point is **one conversation across game and browser**: the game and web UI talk to the same server endpoints, share one server-side conversation history (SQLite on the server is the source of truth), and are logged and rate-limited identically.

## What the mod uses

All client code is in `src/JunApi/` and `src/AiProviders/Jun.cs`. The mod is a plain HTTPS client; certificates live in the stack's NGINX.

| Endpoint | Used for |
|---|---|
| `POST /api/auth.php?action=login` | Cookie-session login (`omega_session`, httponly). Handled by `JunSession`, a shared singleton reused by chat **and** TTS; retries once with a fresh login on 401; `JunSession.Reset()` rebuilds it after credentials change in the settings UI. |
| `/api/conversations.php` | List/create conversations and pull the shared message history at chat start. |
| `POST /api/chat.php` | Send a message; the reply streams back as SSE. The server persists both turns, injects its own system prompt, and rejects requests with more than 80 history messages (the mod sends at most 78). |
| `/api/models.php` | Model list for the settings UI (thin proxy over Ollama's `/api/tags`). |
| `/api/tts.php?action=tts` | Speech synthesis (see [tts.md](tts.md)). |

### Configuration (game side)

```ini
[General]
UsedProvider = "Jun"

[Jun]
ApiUrl = "https://your-host"   # NGINX terminates TLS; LAN or WAN
Email = "you@example.com"      # your webapp account
Password = "..."
ConversationId = 0             # 0 = create automatically; set an id to share one chat everywhere
Model = ""                     # empty = server default
Reasoning = "Auto"             # Auto, Low, Medium, High
```

With `ConversationId = 0` the mod creates a conversation on first chat and remembers its id **in the game save** (`jun-conversation-id`), so each save file gets its own thread by default.

### Action tags instead of `#!` commands

Because the server owns the system prompt, the mod's `#!` command instructions never reach the model. The Jun finetune instead emits `[A:emote|happy]`-style tags (legacy `[ACTION:brow|emotion=sad]` also supported), which `JunActionTranslator` converts to the game's `#!bot.Expression.*` commands mid-stream (happy/excited/laughing → `VeryHappy`, sad/crying → `VerySad`, angry → `VeryAngry`, surprised/shocked → `VeryShock`, embarrassed → `VeryBlush`, neutral → `Clear`, …). Tags with no in-game equivalent are dropped; text is held back from `[` until the matching `]` (max 300 chars) so tags split across stream chunks still parse.

## Sharing one conversation across game / browser

Set the same conversation id in both places:

- game mod: `[Jun] ConversationId = 42`
- web UI: open conversation #42 in the sidebar

Leave both at `0` and each client creates (and remembers) its own conversation instead.
