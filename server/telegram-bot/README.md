# Telegram bridge for Jun

A small Telegram bot (~250 lines) that forwards messages to the [Jun webapp stack](https://github.com/efficiencyx/Jun)
(`/api/chat.php` behind NGINX + PHP) and replies with the model's answer.

It logs in as a regular webapp user and reads/continues a server-side
conversation, so a chat you started in the game or the browser carries on
from Telegram. All three runtimes share one history, and the server's
SQLite conversation store is the source of truth.

Text-only for now (v1): no TTS, no streaming edits.

## Setup

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy the token.
2. Create (or reuse) a webapp account, the same one the game mod uses.
3. Configure and run:

```bash
cd server/telegram-bot
pip install -r requirements.txt
cp .env.example .env   # edit it
set -a; source .env; set +a
python bot.py
```

## Commands

- `/start` — short intro
- anything else — forwarded to Jun; her reply comes back with the Live2D
  action tags (`[A:...]`) stripped

## Sharing one conversation everywhere

Set the same conversation id in all three places:

- Telegram bot: `JUN_CONVERSATION_ID=42`
- Game mod: `[Jun] ConversationId = 42` in `MdrgAiDialog.cfg`
- Web UI: open conversation #42 in the sidebar

Leave them all at `0` and each runtime creates (and remembers) its own
conversation instead.
