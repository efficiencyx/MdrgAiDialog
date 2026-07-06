# Chat pipeline

This page follows one message through the system, then documents the `#!` command language the AI uses and the slash commands the player can type. All classes are in `src/Chat/`.

## The players' path

1. **Entry.** The patched interact/cuddle menus call `ChatManager.StartChat()`, which starts the `ChatLoop` coroutine.
2. **Story mode.** `EnterStoryState()` switches the game into ADV (visual-novel) mode: the Live2D controller is prepared for dialogue, the game state changes to story state, and Jun's "brain" enters `StoryBrainState`.
3. **Preflight & warm-up.** Before the first prompt, `AiAdapter.EnsureReadyForChat()` runs provider prerequisites (Ollama: model-exists check and optional in-game download with progress popup; Jun: login, conversation selection, server history pull). Then `WarmUp()` fires so model load latency overlaps with the player typing.
4. **Input.** The game's native `InputPopup` opens, reworked by `ConfigureChatPopup`: the paste button and commands text are removed and the single OK button is replaced with **Send** and **Close** (re-opened through the game's own popup-choice path so close-on-click behavior is preserved; a duplicate popup-stack entry is dropped to avoid an unclickable modal).
5. **Send.** `ProcessUserInput` starts fetching the AI response on a background task immediately — chunks pile into a `ConcurrentQueue` — while the player's own line is echoed into the dialog box first, hiding network/VRAM latency.
6. **Streaming.** As chunks arrive, `ChatParser.Parse()` normalizes them and splits text from commands. Text goes to `ChatWriter` (typewriter rendering in the Fungus `SayDialog`); commands go to `ChatExecutor` **at the exact position they appear in the text**, so an expression change lands right before the words it colors.
7. **Voice.** In parallel, `TtsManager` accumulates the same text into sentences and synthesizes/plays them pipelined (see [tts.md](tts.md)).
8. **Finish.** After the stream ends, the trailing partial sentence is spoken, `parser.Flush()` writes any remaining text and unlocks the writer (also done on errors — otherwise the game soft-locks), and the history is persisted to the save. The loop then reopens the input popup until the chat ends.
9. **Exit.** `/exit`, the Close button, or an AI-issued `#!flow.ExitChat` calls `StopChat()`: the writer stops, TTS stops, expressions reset, and the game returns from story state.

## The components

### `ChatManager` (MonoBehaviour singleton)

Owns the conversation loop and the game-state transitions; wires `AiAdapter`, `ChatParser`, `ChatWriter`, `ChatExecutor` together; persists/restores history via `SaveStorage` (`chat-history` key, reloaded on `cache-invalidated` — i.e. on save load/new game); adds both sides of the conversation to the game's narrative log; exposes `RebuildAdapter()` so a provider switch from the settings UI takes effect immediately.

### `AiAdapter`

Facade between the chat system and the providers. Builds the full **system prompt** from a template: `{0}` is the configurable `[General] SystemPersona`, followed by hard-coded behavior rules (proactive, match the user's language, no emoji, medium-length messages…) and the complete `#!` command documentation with the valid command lists (`{1}`–`{5}`) injected from `ChatExecutor`'s whitelists — so prompt and validator can never drift apart. Instantiates the provider named by `UsedProvider` (exact-match switch).

### `ChatParser`

Character-by-character streaming parser. Responsibilities:

- whitespace normalization (tabs→spaces, collapse runs, `\r`/`\r\n`→`\n`);
- command detection: `#!` starts a potential command, terminated by whitespace/end of chunk; the surrounding text is emitted clean;
- ordering: each command is registered as a callback at its exact text position, so `ChatWriter` executes it precisely when the typewriter reaches that point;
- `Flush()` finalizes trailing text/commands at end of stream.

### `ChatWriter` (MonoBehaviour singleton)

Renders text into the game's Fungus `SayDialog` with typewriter animation. It binds to the active dialog via the `say-dialog-changed` event (from `FungusSayDialogPatch`), holds a text buffer with pending positional callbacks, advances visible characters over time, and pauses/waits for user clicks at message boundaries (`user-input` event from `FungusWriterPatch`; `Locker<Writer>` keeps the game's own line-advance logic from stealing clicks). `Stop(waitForInput)` ends output cleanly.

### `ChatExecutor`

Validates and runs commands. Invalid commands are logged and ignored (the system prompt threatens the model with "deactivation" for making them up). Expression/arm commands call the game's bot API for the Live2D model; flow commands call back into `ChatManager`. `ResetBotEmotes()` returns Jun to a neutral state on chat end.

## The `#!` command language

Commands are embedded inline in the AI text, start with `#!`, and end at a space. They execute at their position in the text stream.

### Expressions — `#!bot.Expression.*`

| Main emotions | Blush |
|---|---|
| `VerySad`, `Sad`, `Happy`, `VeryHappy`, `Shock`, `VeryShock`, `Angry`, `VeryAngry` | `Blush`, `VeryBlush` |

One main expression can combine with one blush state. Regular variants are subtle; `Very` variants are obvious (the prompt nudges the model toward `Very`). `Clear`/`NoBlush` reset (used by the Jun action translator and mock).

### Arms — `#!bot.ArmL.*`, `#!bot.ArmR.*`, `#!bot.ArmBoth.*`

Positions: `UpPoint`, `UpHi`, `UpLecture`, `DownNormal`, `DownClenched`. Use either `ArmBoth` for synchronized movement or an `ArmL` + `ArmR` combination.

### Flow — `#!flow.*`

| Command | Effect |
|---|---|
| `#!flow.ExitChat` | Ends the chat session (the model is instructed to use it on goodbyes, or if "offended"). |
| `#!flow.ResetChat` | Erases the whole chat history — framed to the model as its "death button", to be used only on explicit user request. |
| `#!flow.SplitMessage` | Message break: waits for a click, clears the dialog, continues with a fresh message. For long replies. |

## Player slash commands

Typed into the chat popup instead of a message:

| Command | Effect |
|---|---|
| `/exit` | Close the chat. |
| `/reset` | Wipe chat history (in memory and in the save). |
| `/clear` | Reset Jun's expression/pose to neutral. |
| `/pack` | Reserved — history packing is a TODO, currently a no-op. |

## Notes on the Jun provider

When `UsedProvider = "Jun"`, the server injects its own system prompt and strips the client's, so the model never sees the `#!` instructions. Instead its `[A:emote|happy]`-style tags are converted to the same `#!bot.*` commands by `JunActionTranslator` before parsing — the rest of the pipeline is identical.
