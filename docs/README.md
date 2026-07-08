# MDRG AI Dialog — Documentation

MDRG AI Dialog is a [MelonLoader](https://melonwiki.xyz/) mod (version 0.4.0) for the Unity/IL2CPP game **My Dystopian Robot Girlfriend** (by IncontinentCell). It replaces the in-game "Talk" interaction with a live chat against a large language model — local (Ollama) or cloud (OpenAI, OpenRouter, Claude, Gemini, DeepSeek, Mistral, or the self-hosted Jun webapp stack). Replies stream into the game's visual-novel dialog box, drive Jun's Live2D expressions and gestures, and can be spoken out loud with lipsynced text-to-speech.

## Documentation index

| Document | Contents |
|---|---|
| [Installation](installation.md) | One-command installers, manual install, requirements |
| [Configuration reference](configuration.md) | Every setting in `MdrgAiDialog.cfg`, with defaults |
| [AI providers](providers.md) | The nine supported providers and how each differs |
| [Architecture](architecture.md) | High-level design, component map, data flow |
| [Chat pipeline](chat-pipeline.md) | How a message travels from input popup to on-screen text; the `#!` command language; slash commands |
| [Text-to-speech & lipsync](tts.md) | Voice playback, sentence pipelining, Live2D mouth driving |
| [Jun webapp](jun-webapp.md) | The shared-conversation stack: game + browser |
| [Development guide](development.md) | Building from source, project layout, Harmony patches, adding a provider |

## What the mod does, in one paragraph

When Jun is "smart" (the in-game `IsBotSmart` flag), the mod adds a **"Talk (AI)"** button to the interact and cuddle menus. Pressing it switches the game into story (ADV) mode, opens the game's native input popup, and starts a conversation loop: your line is echoed in the dialog box, then the AI's reply streams in character-by-character through the game's Fungus dialog system. The reply text can embed commands like `#!bot.Expression.VeryHappy` which the mod executes as they scroll past, changing Jun's Live2D expression, blush, and arm poses in real time. Chat history is persisted inside the game's save file, so conversations survive save/load. Optionally, each sentence is sent to a TTS server and played back with Jun's mouth animated from the audio amplitude.

## Repository layout

```
MdrgAiDialog/
├── src/                      # C# mod source (net6.0 class library)
│   ├── Core.cs               # MelonMod entry point
│   ├── ModConfig.cs          # MelonPreferences-backed configuration
│   ├── AiProviders/          # LLM backends (OpenAI-compatible + Jun + Mock)
│   ├── Chat/                 # Chat loop, stream parser, dialog writer, command executor
│   ├── Tts/                  # TTS client, audio pipeline, Live2D lipsync
│   ├── JunApi/               # Jun webapp session, config, action-tag translator
│   ├── Ui/                   # First-run provider picker + settings panel (uGUI)
│   ├── Patches/              # Harmony patches into the game
│   └── Utils/                # Singletons, event bus, save storage, main-thread runner…
├── scripts/                  # One-command installers (PowerShell, bash, batch)
├── colab_ollama_server.ipynb # Free-GPU Ollama server on Google Colab
├── MdrgAiDialog.csproj       # Build project (references game + MelonLoader assemblies)
└── Directory.Build.props     # Local game path (edit this to build)
```

## Quick facts

- **Game:** My Dystopian Robot Girlfriend (`IncontinentCell`), IL2CPP Unity build
- **Loader:** MelonLoader **Nightly 0.7.2+**, mod targets .NET 6
- **Config file:** `<GameFolder>/UserData/MdrgAiDialog.cfg` (INI, created on first launch)
- **Logs:** `<GameFolder>/MelonLoader/Latest.log`
- **Default provider:** Ollama at `http://localhost:11434/v1` with model `hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF`
- **First-run UI:** an in-game provider picker appears automatically until a provider is configured; reopen it with **F7** (when the legacy input API is available)
- **License:** see [`LICENSE.txt`](../LICENSE.txt)
