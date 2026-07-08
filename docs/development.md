# Development guide

## Prerequisites

- **.NET 6 SDK**
- A copy of the game with **MelonLoader Nightly 0.7.2+** installed and run at least once — the build references the game's IL2CPP interop assemblies from `<GameFolder>/MelonLoader/Il2CppAssemblies/` and MelonLoader's own DLLs from `<GameFolder>/MelonLoader/net6/`.

## Building

1. Point the build at your game folder by editing `Directory.Build.props`:

   ```xml
   <Project>
     <PropertyGroup>
       <GamePath>/path/to/My Dystopian Robot Girlfriend</GamePath>
     </PropertyGroup>
   </Project>
   ```

   (`MdrgAiDialog.csproj` falls back to a hardcoded path if `GamePath` doesn't exist.)

2. Build:

   ```bash
   dotnet build -c Release
   ```

   A post-build target copies `MdrgAiDialog.dll` straight into the game's `Mods/` folder. The `scripts/install.*` files are user-facing installers; for development, run `dotnet build` directly after setting `GamePath`.

Project properties of note: `net6.0`, nullable disabled, unsafe blocks allowed, deterministic/CI build flags on, version `0.4.0` (also in the `[assembly: MelonInfo]` attribute in `Core.cs`).

## Source layout

```
src/
├── Core.cs                 # MelonMod entry: config load, singleton setup, F7 hotkey, first-run picker
├── ModConfig.cs            # All MelonPreferences categories + typed accessors/mutators
├── AiProviders/
│   ├── AiProvider.cs       # Abstract base: history, system message, streaming contract
│   ├── AiProviderConfig.cs # POCO handed to providers (url/key/model/temp/topk/reasoning/timeout)
│   ├── OpenAi.cs           # OpenAI-compatible SSE streaming implementation
│   ├── Ollama.cs           # + model existence check & in-game pull with progress
│   ├── OpenRouter.cs / Google.cs / DeepSeek.cs / Claude.cs / Mistral.cs  # payload tweaks
│   ├── Jun.cs              # Jun webapp provider (server-side history, action-tag translation)
│   └── Mock.cs             # Offline canned-response provider for testing
├── Chat/
│   ├── ChatManager.cs      # Conversation loop, popup rework, history persistence
│   ├── AiAdapter.cs        # System-prompt template + provider factory
│   ├── ChatParser.cs       # Streaming text/command splitter
│   ├── ChatWriter.cs       # Typewriter rendering into Fungus SayDialog
│   └── ChatExecutor.cs     # Command whitelists + execution (expressions, arms, flow)
├── Tts/                    # TtsManager, TtsClient, TtsConfig, TtsLipSync, WavAudio
├── JunApi/                 # JunSession, JunConfig, JunActionTranslator
├── Ui/                     # ProviderPicker, ProviderSettingsPanel, ModelCatalog, UiKit
├── Patches/                # Harmony patches (see architecture.md)
└── Utils/                  # Singletons, EventBus, SaveStorage, MainThreadRunner, Locker, Logger, …
```

See [architecture.md](architecture.md) for how these fit together and [chat-pipeline.md](chat-pipeline.md) for the message flow.

## IL2CPP conventions used in this codebase

- MonoBehaviours added to the game must carry `[RegisterTypeInIl2Cpp]`, and mod-side singletons additionally `[MonoSingleton]`; create them only through `MonoSingletonManager.Add<T>()` and call `this.ValidateSingleton()` in `Awake`.
- Methods with managed-only signatures on Il2Cpp-registered classes are marked `[HideFromIl2Cpp]`.
- Delegates passed to game APIs are wrapped explicitly (`new Action<…>(…)`).
- Avoid LINQ in hot paths (allocation cost under IL2CPP interop) — see `AiProvider.GetNonSystemMessagesSnapshot`.
- Unity APIs are main-thread-only: hop through `MainThreadRunner` from background tasks.
- Interop casts use `.Cast<T>()` (e.g. `buttonList.Cast<IModificationPeriodButtonList>()`).

## Testing without a server

Set `[General] UsedProvider = "Mock"`. The `Mock` provider streams canned responses keyed by the typed message:

| Input | Exercises |
|---|---|
| `1` | Expressions, arms, blush commands inline |
| `2` | `\r`, `\r\n`, `\n` newline normalization |
| `3` | Whitespace collapsing + `#!flow.SplitMessage` |
| `4` | Invalid command handling |

## Adding a provider

Covered step-by-step in [providers.md](providers.md#adding-a-new-provider).

## Release checklist

- Bump the version in `MdrgAiDialog.csproj` (`Version`, `FileVersion`, `AssemblyVersion`) **and** in the `MelonInfo` attribute in `src/Core.cs`.
- Build `Release`, attach `MdrgAiDialog.dll` to a GitHub release — the installers download the latest release asset from the repo configured via `--mod-repo` (default `StLyn4/MdrgAiDialog`).
