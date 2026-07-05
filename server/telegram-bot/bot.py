#!/usr/bin/env python3
"""Minimal Telegram bridge for the Jun webapp.

Forwards Telegram messages to the Jun stack's PHP API - the same
/api/chat.php endpoint the web UI and the game mod use - so all three
runtimes share one server-side conversation and are indistinguishable
(and identically logged/rate-limited) upstream. Text-only: no TTS,
no streaming edits - deliberately v1.

Flow per message:
  1. login (cookie session, once)          POST /api/auth.php?action=login
  2. pick/create the conversation (once)   /api/conversations.php
  3. pull shared history                   GET  /api/conversations.php?action=messages
  4. ask the model (SSE parsed to text)    POST /api/chat.php
     chat.php persists both turns server-side; nothing to push back.

Configuration comes from environment variables (see .env.example).
Only python-requests is needed: pip install -r requirements.txt
"""

import json
import logging
import os
import re
import sys
import time
from datetime import datetime

import requests

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("jun-telegram")

# --- Configuration -----------------------------------------------------------

BOT_TOKEN = os.environ.get("TELEGRAM_BOT_TOKEN", "")
JUN_URL = os.environ.get("JUN_URL", "https://localhost").rstrip("/")
JUN_EMAIL = os.environ.get("JUN_EMAIL", "")
JUN_PASSWORD = os.environ.get("JUN_PASSWORD", "")
# 0 = create a conversation on first run and remember it in this file
CONVERSATION_ID = int(os.environ.get("JUN_CONVERSATION_ID", "0"))
CONVERSATION_ID_FILE = os.environ.get("JUN_CONVERSATION_ID_FILE", ".conversation_id")
MODEL = os.environ.get("JUN_MODEL", "")           # empty = server default
REASONING = os.environ.get("JUN_REASONING", "auto")
# Set to "false" only for local/self-signed HTTPS testing
VERIFY_TLS = os.environ.get("JUN_VERIFY_TLS", "true").lower() != "false"
# Comma-separated Telegram user ids allowed to talk to the bot. Empty = everyone
# (do NOT leave empty on a public bot: it is one shared conversation).
ALLOWED_USER_IDS = {
    int(x) for x in os.environ.get("ALLOWED_USER_IDS", "").split(",") if x.strip().isdigit()
}

TG_API = f"https://api.telegram.org/bot{BOT_TOKEN}"
MAX_HISTORY = 78  # chat.php rejects requests with more than 80 messages

# Jun's Live2D action tags ([A:...] / legacy [ACTION:...]) drive the web rig;
# they are meaningless in Telegram, so hide them from the output.
ACTION_TAG = re.compile(r"\[\s*A(?:CTIONS?)?\s*:[^\]]*\]", re.IGNORECASE)

jun = requests.Session()
jun.verify = VERIFY_TLS


# --- Jun webapp API ------------------------------------------------------------

def jun_login():
    response = jun.post(
        f"{JUN_URL}/api/auth.php?action=login",
        json={"email": JUN_EMAIL, "password": JUN_PASSWORD},
        timeout=15,
    )
    if response.status_code != 200:
        raise RuntimeError(f"Jun login failed: {response.status_code} {response.text[:200]}")
    log.info("Logged in to the Jun webapp as %s", JUN_EMAIL)


def jun_authed(request_fn):
    """Runs a request, retrying once with a fresh login on 401."""
    response = request_fn()
    if response.status_code == 401:
        jun_login()
        response = request_fn()
    return response


def ensure_conversation():
    global CONVERSATION_ID

    if CONVERSATION_ID <= 0 and os.path.exists(CONVERSATION_ID_FILE):
        with open(CONVERSATION_ID_FILE, encoding="utf-8") as f:
            saved = f.read().strip()
            if saved.isdigit():
                CONVERSATION_ID = int(saved)

    if CONVERSATION_ID > 0:
        probe = jun_authed(lambda: jun.get(
            f"{JUN_URL}/api/conversations.php?action=messages&id={CONVERSATION_ID}",
            timeout=15,
        ))
        if probe.status_code == 200:
            log.info("Continuing conversation #%s", CONVERSATION_ID)
            return

    response = jun_authed(lambda: jun.post(
        f"{JUN_URL}/api/conversations.php?action=create", timeout=15,
    ))
    response.raise_for_status()
    CONVERSATION_ID = int(response.json()["id"])

    with open(CONVERSATION_ID_FILE, "w", encoding="utf-8") as f:
        f.write(str(CONVERSATION_ID))
    log.info("Created conversation #%s", CONVERSATION_ID)


def fetch_history():
    response = jun_authed(lambda: jun.get(
        f"{JUN_URL}/api/conversations.php?action=messages&id={CONVERSATION_ID}",
        timeout=15,
    ))
    response.raise_for_status()
    return [
        {"role": row["role"], "content": row["content"]}
        for row in response.json()
        if row.get("role") in ("user", "assistant") and isinstance(row.get("content"), str)
    ]


def ask_model(history, user_text):
    """POSTs to chat.php and assembles the reply from the SSE token stream."""
    messages = history[-MAX_HISTORY:] + [{"role": "user", "content": user_text}]

    payload = {
        "conversation_id": CONVERSATION_ID,
        "messages": messages,
        "reasoning": REASONING,
        "client_time": datetime.now().strftime("%A, %B %d, %Y at %I:%M %p"),
    }
    if MODEL:
        payload["model"] = MODEL

    response = jun_authed(lambda: jun.post(
        f"{JUN_URL}/api/chat.php", json=payload, stream=True, timeout=600,
    ))
    response.raise_for_status()

    tokens = []
    for line in response.iter_lines(decode_unicode=True):
        if not line or not line.startswith("data: "):
            continue
        data = line[6:]
        if data == "[DONE]":
            break
        try:
            event = json.loads(data)
        except json.JSONDecodeError:
            continue
        if event.get("error"):
            raise RuntimeError(f"chat.php error: {event['error']}")
        token = event.get("token")
        if token:
            tokens.append(token)

    return "".join(tokens)


# --- Telegram -----------------------------------------------------------------

def tg_request(method, **params):
    response = requests.post(f"{TG_API}/{method}", json=params, timeout=65)
    response.raise_for_status()
    return response.json()["result"]


def send_reply(chat_id, text):
    # Telegram caps messages at 4096 chars
    for start in range(0, len(text), 4000):
        tg_request("sendMessage", chat_id=chat_id, text=text[start:start + 4000])


def handle_message(message):
    chat_id = message["chat"]["id"]
    user_id = message.get("from", {}).get("id")
    text = (message.get("text") or "").strip()

    if not text:
        return

    if ALLOWED_USER_IDS and user_id not in ALLOWED_USER_IDS:
        log.info("Ignoring message from unauthorized user %s", user_id)
        return

    if text == "/start":
        send_reply(chat_id, "Hi! I'm bridged to Jun. Whatever you say here continues "
                            "the same conversation as in the game and on the web.")
        return

    tg_request("sendChatAction", chat_id=chat_id, action="typing")

    try:
        history = fetch_history()
        raw_reply = ask_model(history, text)
    except Exception as error:  # noqa: BLE001 - keep the bot alive on server hiccups
        log.error("Jun request failed: %s", error)
        send_reply(chat_id, "Sorry, I couldn't reach Jun's server. Try again later.")
        return

    reply = ACTION_TAG.sub("", raw_reply).strip()
    send_reply(chat_id, reply or "...")


def main():
    if not BOT_TOKEN:
        sys.exit("TELEGRAM_BOT_TOKEN is not set (create a bot with @BotFather)")
    if not JUN_EMAIL or not JUN_PASSWORD:
        sys.exit("JUN_EMAIL / JUN_PASSWORD are not set (webapp account credentials)")

    jun_login()
    ensure_conversation()
    log.info("Bot up. Webapp: %s, conversation: #%s", JUN_URL, CONVERSATION_ID)

    offset = 0
    while True:
        try:
            updates = tg_request("getUpdates", offset=offset, timeout=60,
                                 allowed_updates=["message"])
        except Exception as error:  # noqa: BLE001
            log.warning("getUpdates failed: %s", error)
            time.sleep(5)
            continue

        for update in updates:
            offset = update["update_id"] + 1
            if "message" in update:
                try:
                    handle_message(update["message"])
                except Exception as error:  # noqa: BLE001
                    log.error("Failed to handle message: %s", error)


if __name__ == "__main__":
    main()
