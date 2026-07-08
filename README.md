<div align="center">

# MDRG AI Dialog

**A *My Dystopian Robot Girlfriend* fan mod - give Jun a live AI chat, synced expressions, and an optional voice inside the game.**

[![License](https://img.shields.io/badge/license-see%20LICENSE.txt-blue.svg)](LICENSE.txt)
[![Release](https://img.shields.io/github/v/release/StLyn4/MdrgAiDialog?label=release)](https://github.com/StLyn4/MdrgAiDialog/releases)
![C#](https://img.shields.io/badge/C%23-.NET%206-512BD4?logo=dotnet&logoColor=white)
![MelonLoader](https://img.shields.io/badge/MelonLoader-Nightly%200.7.2%2B-f15a24)
![Ollama](https://img.shields.io/badge/LLM-Ollama-black)
![Providers](https://img.shields.io/badge/providers-Ollama%20%7C%20OpenAI%20%7C%20OpenRouter%20%7C%20Jun-2f6f4e)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20Steam%20Deck-informational)

[What it does](#what-it-does) &middot;
[Get it running](#get-it-running) &middot;
[Colab](#try-a-free-gpu-on-google-colab) &middot;
[Providers](#pick-where-the-ai-runs) &middot;
[Voice](#voice-and-lipsync) &middot;
[Docs](#documentation)

</div>

---

## So what is this?

MDRG AI Dialog is a MelonLoader mod for **My Dystopian Robot Girlfriend**. It adds a **Talk (AI)** flow to the game so you can chat with Jun through a local model, a cloud API, or the self-hosted [Jun webapp stack](https://github.com/efficiencyx/Jun).

The reply is not just dumped into a console. It streams into the game's visual-novel dialog box, can trigger Jun's Live2D expressions as the words arrive, and can be spoken through a TTS server with lipsync.

## ! Warning ! Adult Content
> This is an unofficial fan mod for an adult game. It is not affiliated with Incontinent Cell or the game team. Use it responsibly and only where the underlying game is legal and appropriate for you.

## What it does

- **She answers inside the game.** The mod uses the game's own input popup and dialog box, so the chat feels like part of the scene instead of a side window.
- **She reacts while the message streams.** Action commands can change expressions, blush, gestures, and Live2D state at the moment the relevant text appears.
- **She can talk out loud.** TTS is generated sentence by sentence, so one line can play while the next is still being prepared.
- **Her mouth follows the audio.** Lipsync drives Jun's Live2D mouth from the speech amplitude.
- **Your model can live anywhere.** Run Ollama locally, use a free Colab GPU, point at OpenRouter, use OpenAI-compatible APIs, or connect to the Jun webapp stack.
- **The game and browser can share a conversation.** With the Jun provider, the mod talks to the same server-side conversation system as the web UI.
- **Setup is not a scavenger hunt.** The installer can find the game, install MelonLoader, download the DLL, configure Ollama or a remote provider, and write the config.

## Get it running

You need the game, MelonLoader Nightly 0.7.2 or newer, and `MdrgAiDialog.dll`. The installers handle most of that.

### The lazy way (one line)

These commands download and execute the installer script. That script then downloads the mod DLL and may install MelonLoader/Ollama depending on your answers. This is convenient, but you should inspect the script first if you have any doubt. It is recommended.

**Windows PowerShell**

```powershell
irm https://raw.githubusercontent.com/StLyn4/MdrgAiDialog/master/scripts/install.ps1 | iex
```

Read it first:

[install.ps1](https://raw.githubusercontent.com/StLyn4/MdrgAiDialog/master/scripts/install.ps1)


**Linux / Steam Deck / WSL**

```bash
curl -fsSL https://raw.githubusercontent.com/StLyn4/MdrgAiDialog/master/scripts/install.sh | bash
```

Read it first:

[install.sh](https://raw.githubusercontent.com/StLyn4/MdrgAiDialog/master/scripts/install.sh)


### Manual install

1. Install [MelonLoader Nightly 0.7.2+](https://github.com/LavaGang/MelonLoader.Installer) for **My Dystopian Robot Girlfriend**.
2. Run the game once so MelonLoader creates `Mods/`, `UserData/`, and `MelonLoader/`.
3. Download `MdrgAiDialog.dll` from the [latest release](https://github.com/StLyn4/MdrgAiDialog/releases).
4. Copy it to `<GameFolder>/Mods/MdrgAiDialog.dll`.
5. Launch the game and choose a provider in the first-run popup.

`<GameFolder>` is the folder that contains `My Dystopian Robot Girlfriend.exe`.

## Try a free GPU on Google Colab

No local GPU? Use Colab as an Ollama-compatible server and point the mod at its tunnel URL.

[![Open In Colab](https://colab.research.google.com/assets/colab-badge.svg)](https://colab.research.google.com/github/StLyn4/MdrgAiDialog/blob/master/colab_ollama_server.ipynb)

1. Open [`colab_ollama_server.ipynb`](colab_ollama_server.ipynb) in Google Colab.
2. Choose a GPU runtime.
3. Run all cells and wait for the `https://...trycloudflare.com/v1` URL.
4. Put that URL in `[Ollama] ApiUrl`, or run the installer with `-OllamaUrl` / `--ollama-url`.

Colab sessions are disposable. If the URL stops working, rerun the notebook and update the config.

## Pick where the AI runs

| Mode | Good for | Notes |
|---|---|---|
| **Local Ollama** | Private, free, no API key | Best if you have enough CPU/GPU for the model |
| **Colab Ollama** | Free GPU test runs | Uses a temporary Cloudflare tunnel |
| **OpenRouter** | Big model catalog, simple key management | Includes free model options |
| **OpenAI-compatible APIs** | Cloud quality without local hardware | Works with OpenAI, Mistral, Google, DeepSeek, Claude, and compatible endpoints |
| **Jun webapp** | Shared game/browser history plus bundled TTS | Uses the same login and conversation stack as [Jun OS](https://github.com/efficiencyx/Jun) |

## Configuration

The config file is created on first launch:

```text
<GameFolder>/UserData/MdrgAiDialog.cfg
```

<details>
<summary><b>Local Ollama</b></summary>

```ini
[General]
UsedProvider = "Ollama"

[Ollama]
ApiUrl = "http://localhost:11434/v1"
Model = "hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF"
```

</details>

<details>
<summary><b>OpenRouter</b></summary>

```ini
[General]
UsedProvider = "OpenRouter"

[OpenRouter]
ApiUrl = "https://openrouter.ai/api/v1"
ApiKey = "sk-or-..."
Model = "deepseek/deepseek-r1-0528:free"
```

</details>

<details>
<summary><b>Jun webapp with voice</b></summary>

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
LipSync = true
```

</details>

See [docs/configuration.md](docs/configuration.md) for every setting and default.

## Voice and lipsync

TTS is optional. When enabled, the mod sends clean sentence chunks to a speech endpoint, queues the WAV replies, plays them in order, and drives Jun's mouth from the audio amplitude.

| TTS mode | What it uses |
|---|---|
| `ApiFormat = "Jun"` | `[Jun] ApiUrl`, `Email`, and `Password`; hits the webapp `/api/tts.php` endpoint |
| `ApiFormat = "OpenAI"` | `{ApiUrl}/audio/speech`; works with OpenAI TTS and compatible local servers |

See [docs/tts.md](docs/tts.md) for the full voice pipeline.

## Under the hood

```text
Game UI
  |-- Talk (AI) button and input popup
  |-- Fungus dialog writer
  |-- Live2D expression / mouth control

MdrgAiDialog
  |-- ChatManager streams replies
  |-- AiAdapter routes to Ollama, Jun, OpenRouter, OpenAI, Mistral, Google, DeepSeek, Claude, or Mock
  |-- ChatExecutor applies action commands as text arrives
  |-- TtsManager queues speech and lipsync

Optional services
  |-- Ollama local, Colab, or reverse proxy
  |-- Jun webapp for shared browser/game conversations
  |-- OpenAI-style TTS server
```

## Documentation

| Document | Contents |
|---|---|
| [Installation](docs/installation.md) | Installers, manual setup, Steam Deck/Wine notes |
| [Configuration](docs/configuration.md) | Every `MdrgAiDialog.cfg` option and default |
| [AI providers](docs/providers.md) | Provider differences and reasoning settings |
| [Jun webapp](docs/jun-webapp.md) | Shared game/browser conversation setup |
| [TTS](docs/tts.md) | Voice, sentence pipelining, lipsync |
| [Architecture](docs/architecture.md) | Component map and data flow |
| [Chat pipeline](docs/chat-pipeline.md) | Input, streaming, action commands, slash commands |
| [Development](docs/development.md) | Build from source and add providers |

## When things go sideways

<details>
<summary><b>Connection error immediately</b></summary>

If you use the default provider, make sure Ollama is installed and running at `http://localhost:11434/v1`. If you use a cloud provider, check `UsedProvider`, `ApiUrl`, `ApiKey`, and `Model`.
</details>

<details>
<summary><b>The first-run provider popup is gone</b></summary>

Close the game, open `<GameFolder>/UserData/MdrgAiDialog.cfg`, set `ProviderConfigured = false`, and relaunch. You can also press `F7` in game where the legacy input API is available.
</details>

<details>
<summary><b>Voice does not play</b></summary>

Check `[Tts] Enabled = true`, confirm the TTS endpoint is reachable, and look for `TtsClient` / `TtsManager` lines in `<GameFolder>/MelonLoader/Latest.log`.
</details>

<details>
<summary><b>The Colab URL stopped working</b></summary>

Free Colab sessions expire and Cloudflare tunnel URLs change. Rerun the notebook and update `[Ollama] ApiUrl`.
</details>

<details>
<summary><b>The config is broken or you want to start over</b></summary>

Close the game and delete `<GameFolder>/UserData/MdrgAiDialog.cfg`. It will be recreated on the next launch.
</details>

## Where everything lives

```text
.
|-- src/
|   |-- Core.cs                 MelonMod entry point
|   |-- ModConfig.cs            MelonPreferences config
|   |-- AiProviders/            Ollama, Jun, OpenAI, OpenRouter, Mistral, Google, DeepSeek, Claude, Mock
|   |-- Chat/                   Input, streaming, parser, writer, action execution
|   |-- Tts/                    Speech requests, WAV playback, lipsync
|   |-- JunApi/                 Jun webapp auth, chat, TTS, action translation
|   |-- Ui/                     First-run provider picker and settings panels
|   |-- Patches/                Harmony patches into the game
|   `-- Utils/                  Event bus, save storage, main-thread runner, logging
|-- docs/                       Full user and developer docs
|-- scripts/                    Windows, Linux, and developer installers
|-- colab_ollama_server.ipynb   Free-GPU Ollama endpoint for the mod
|-- MdrgAiDialog.csproj         .NET 6 project
`-- Directory.Build.props       Local game path for builds
```

## Build from source

Install the .NET 6 SDK, install MelonLoader into the game, and point `Directory.Build.props` at your game folder.

```powershell
dotnet build -c Release
```

After a successful build, the project copies `MdrgAiDialog.dll` into the game's `Mods` folder.

## Standing on the shoulders of

- [MelonLoader](https://github.com/LavaGang/MelonLoader) for Unity mod loading
- [Harmony](https://github.com/pardeike/Harmony) for runtime patching
- [Ollama](https://ollama.com/) for local LLM inference
- [OpenRouter](https://openrouter.ai/) and OpenAI-compatible APIs for cloud models
- [Jun OS](https://github.com/efficiencyx/Jun) for the shared webapp/TTS stack

## License

See [LICENSE.txt](LICENSE.txt). This is an unofficial fan project; all rights to the game and its characters belong to their respective owners.
