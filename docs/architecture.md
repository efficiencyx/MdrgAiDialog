# Architecture

## Overview

The mod is a single .NET 6 class library loaded by MelonLoader into the game's IL2CPP runtime. It integrates with the game in three ways:

1. **Harmony patches** (`src/Patches/`) hook game methods to add the "Talk (AI)" buttons, track game-state transitions, and tap into the Fungus dialog system.
2. **Il2Cpp-registered MonoBehaviours** (`ChatManager`, `ChatWriter`, `TtsManager`, `SaveStorage`, `MainThreadRunner`) live on a persistent hidden `GameObject` created at startup and are managed through a small singleton framework.
3. **Native game UI reuse** — the chat uses the game's own `InputPopup`, `ProgressPopup`, and Fungus `SayDialog`; only the first-run provider picker is custom uGUI (the IL2CPP build strips IMGUI and has no reusable grid UI).

## Component map

```
                            ┌────────────────────────────┐
     game menus (patched)   │  Core (MelonMod entry)     │  F7 / first run
  InteractState/CuddleState │  - loads ModConfig         │────────────────┐
            │               │  - creates singletons      │                ▼
            ▼               └────────────────────────────┘        Ui/ProviderPicker
   ChatManager.StartChat()                                        Ui/ProviderSettingsPanel
            │                                                     Ui/ModelCatalog
            ▼
 ┌──────────────────────── Chat pipeline ───────────────────────┐
 │  ChatManager ──► AiAdapter ──► AiProvider (streamed chunks)  │
 │      │               │            Ollama/OpenAi/…/Jun/Mock   │
 │      ▼               ▼                                       │
 │  ChatParser ──text──► ChatWriter ──► Fungus SayDialog        │
 │      │                    │                                  │
 │      └──#!commands──► ChatExecutor ──► Live2D expressions,   │
 │                                        arms, flow control    │
 └──────────────────────────────────────────────────────────────┘
            │ sentences                       ▲ history persists via
            ▼                                 │ SaveStorage (game save)
      Tts/TtsManager ──► TtsClient ──► TTS server (Jun or OpenAI format)
            │
            └──► WavAudio → AudioSource playback → TtsLipSync → Live2D mouth
```

## Startup sequence (`src/Core.cs`)

`Core.OnInitializeMelon()`:

1. `ModConfig.Load()` — creates/reads `UserData/MdrgAiDialog.cfg` (MelonPreferences categories for General, each provider, Jun, Tts).
2. Creates a `DontDestroyOnLoad` root `GameObject` and registers the MonoBehaviour singletons: `MainThreadRunner`, `SaveStorage`, `ChatManager`, `ChatWriter`, `TtsManager`.
3. If no provider has been configured yet (`ProviderConfigured = false`), a coroutine waits for the game UI (`UiOverlay`) to exist, lets the scene settle (~120 frames), then shows the **provider picker**.

`Core.OnUpdate()` polls **F7** to reopen the picker; if the legacy `UnityEngine.Input` API throws (game routed input through the new Input System), the hotkey is permanently disabled for the session.

Harmony patches are applied automatically by MelonLoader via the `[HarmonyPatch]` attributes.

## Harmony patches (`src/Patches/`)

| Patch | Game target | Purpose |
|---|---|---|
| `InteractStatePatch` | `InteractState.EnterState` (postfix) | Adds a "Talk (AI)" button to the interact menu when `IsBotSmart` is true. |
| `CuddleStatePatch` | `CuddleState.EnterState` (postfix) | Same button in the cuddle menu. |
| `GameStateWithLive2DPatch` | `GameStateWithLive2D<…>.EnterState` (postfix) | Tracks current/previous game state in `Utils.GameState`. |
| `GameScriptPatch` | `GameScript.LoadGame` / `StartNewGame` (postfix) | Invalidates the `SaveStorage` cache so per-save chat history reloads. |
| `FungusSayDialogPatch` | `SayDialog.DoSay` (prefix) | Fires a `say-dialog-changed` event so `ChatWriter` can bind to the active dialog. |
| `FungusWriterPatch` | `Writer.OnNextLineEvent` (prefix) | Fires `user-input` and suppresses the game's "advance line" handling while the writer is locked by the mod (`Locker<Writer>`), preventing the game from consuming clicks meant for the chat. |

## Persistence

Chat history is stored **inside the game's save file**: `SaveStorage` (`src/Utils/SaveStorage.cs`) JSON-serializes values into `GameVariables.customData` string variables under a `MdrgAiDialog_` key prefix, with an in-memory cache. Keys in use:

- `chat-history` — the non-system chat messages (restored into the provider on load and after provider switches);
- `jun-conversation-id` — the auto-created Jun webapp conversation id.

Loading a save or starting a new game invalidates the cache (via `GameScriptPatch`), and `SaveStorage.EventBus` fires `cache-invalidated`, which makes `ChatManager` reload the history — so each save file keeps its own conversation.

## Utility layer (`src/Utils/`)

| Utility | Role |
|---|---|
| `MonoSingletonManager` / `[MonoSingleton]` | Creation, registration, and lookup of MonoBehaviour singletons on the mod's root object; guards against duplicates and wrong initialization order. |
| `Singleton` | Plain (non-Mono) singleton base. |
| `MainThreadRunner` | Queues actions onto Unity's main thread (many Unity/IL2CPP APIs are main-thread-only); supports "run in next update" and awaiting completion. |
| `EventBus` | Concurrent named-event hub with one-time listeners, async event waiting, and event capture. Used by `ChatWriter`, `SaveStorage`. |
| `Locker<T>` | Keyed lock flags with optional hit-count auto-unlock; used to gate the Fungus `Writer` against the game's own input handling. |
| `GameState` | Tracks the current/previous `GameStateWithLive2D` state (fed by the patch above). |
| `SaveStorage` | Save-file-backed key/value store (see Persistence). |
| `Logger` | Scoped wrapper over `MelonLogger` (`[Scope] message`). |
| `ProgressPopupHelper` | Instantiates the game's native `ProgressPopup` prefab to show progress for long operations (e.g. Ollama model download). |
| `NullableAttributes` | Nullable-annotation attribute shims for the net6 target. |

## In-game settings UI (`src/Ui/`)

- **`ProviderPicker`** — first-run modal: a dark card grid listing all providers (Ollama, OpenAI, OpenRouter, Claude, Gemini, DeepSeek, Mistral, Jun webapp, Mock) with short blurbs. Custom uGUI because the game offers no mod-populatable grid and IMGUI is stripped.
- **`ProviderSettingsPanel`** — per-provider form shown after picking a card: model selection (live-fetched via `ModelCatalog` + custom entry), connection fields (URL/key or Jun email/password), reasoning mode chips, sampling/timeout knobs, and — for Jun — voice/TTS settings. Text fields open the game's native `InputPopup` (no usable inline text input in the IL2CPP build). Saving writes `ModConfig`, marks the provider configured, and calls `ChatManager.RebuildAdapter()` so the change applies without restarting.
- **`UiKit`** — shared uGUI construction helpers and the game-matched purple palette so both panels look identical.
- **`ModelCatalog`** — model list fetching (see [providers.md](providers.md)).

## Threading model

- LLM responses are fetched on a thread-pool task; chunks flow through a `ConcurrentQueue` back into the main-thread chat loop.
- All Unity object access goes through the main thread (`MainThreadRunner` where needed; the chat loop itself is a MelonCoroutine).
- TTS synthesis requests run concurrently (bounded by a `SemaphoreSlim`) while playback stays ordered.
