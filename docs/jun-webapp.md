# Jun webapp stack & Telegram bridge

The [Jun webapp stack](https://github.com/efficiencyx/Jun) is a self-hosted Docker bundle — NGINX (TLS termination), a PHP API proxy, Ollama, and a CPU TTS engine (Kokoro / pocket-tts) — that this mod can use as its provider. Its selling point is **one conversation everywhere**: the game, the web UI, and a Telegram bot all talk to the same server endpoints, share one server-side conversation history (SQLite on the server is the source of truth), and are logged and rate-limited identically.

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

## Telegram bridge (`server/telegram-bot/`)

A ~250-line Python bot (`bot.py`, dependency: `requests` only) that forwards Telegram messages to the same `/api/chat.php` and replies with the model's answer, with the `[A:...]` tags stripped. Text-only by design (v1): no TTS, no streaming edits.

### Setup

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy the token.
2. Use the **same webapp account** as the game mod and web UI.
3. Configure and run:

```bash
cd server/telegram-bot
pip install -r requirements.txt
cp .env.example .env   # edit it
set -a; source .env; set +a
python bot.py
```

### Environment variables (`.env.example`)

| Variable | Default | Meaning |
|---|---|---|
| `TELEGRAM_BOT_TOKEN` | — | Bot token from @BotFather. |
| `JUN_URL` | `https://localhost` | Base URL of the Jun webapp. |
| `JUN_EMAIL` / `JUN_PASSWORD` | — | Webapp login (same account as the mod for a shared chat). |
| `JUN_CONVERSATION_ID` | `0` | Conversation to continue; `0` = create on first run and remember in `.conversation_id` (`JUN_CONVERSATION_ID_FILE`). |
| `JUN_MODEL` | empty | Optional model override (empty = server default). |
| `JUN_REASONING` | `auto` | Reasoning effort: `auto`, `low`, `medium`, `high`. |
| `ALLOWED_USER_IDS` | empty | Comma-separated Telegram user ids allowed to use the bot. **Do not leave empty on a public bot** — it is one shared conversation. Find your id via @userinfobot. |
| `JUN_VERIFY_TLS` | `true` | Set `false` only when testing against self-signed HTTPS. |

### Behavior

- `/start` — short intro; anything else is forwarded to Jun.
- Per message: login once (cookie session, re-login on 401) → pick/create conversation once → pull shared history (capped at 78 messages) → `POST /api/chat.php`, parsing the SSE stream to text. The server persists both turns, so there is nothing to push back.

## Sharing one conversation across game / browser / Telegram

Set the same conversation id in all three places:

- game mod: `[Jun] ConversationId = 42`
- Telegram bot: `JUN_CONVERSATION_ID=42`
- web UI: open conversation #42 in the sidebar

Leave all at `0` and each runtime creates (and remembers) its own conversation instead.
