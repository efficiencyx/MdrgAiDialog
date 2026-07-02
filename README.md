# MDRG AI Dialog

**Download:** [Latest Release](https://github.com/StLyn4/MdrgAiDialog/releases)

MDRG AI Dialog is a MelonLoader mod for **My Dystopian Robot Girlfriend** that lets you chat with Jun using an LLM (local or cloud), with optional **voice (TTS + lipsync)** and **cross-device chat** (continue the same conversation in-game, in a browser, or on Telegram via the [Jun webapp stack](https://github.com/efficiencyx/Jun)).

> **Important:** out of the box the mod tries to connect to a local Ollama server at `http://localhost:11434`.
> If you don't have [Ollama](https://ollama.com/download) installed/running, you'll get a connection error with default settings.

## One-command install (recommended)

You don't need to know anything about the command line. The installer finds your game, installs MelonLoader and the mod, sets up Ollama, downloads the AI model and writes the config.

**Windows:**

1. Download [`scripts/install.ps1`](scripts/install.ps1) (right-click → Save link as...).
2. Right-click the downloaded file → **Run with PowerShell**.
3. Follow the prompts. That's it — start the game and press the chat button.

**Linux / Steam Deck:**

```bash
./install.sh
```

Useful options (both scripts take the same ones, `-Flag` on Windows / `--flag` on Linux):

| Option | What it does |
|---|---|
| `-GamePath "C:\...\Game"` | Skip auto-detection of the game folder |
| `-OllamaUrl "https://xxx.trycloudflare.com/v1" -SkipOllamaInstall -SkipModelPull` | Use a remote/Colab server instead of local Ollama |
| `-Provider OpenRouter -Model "..." -ApiKey "sk-or-..."` | Use a cloud provider instead of Ollama |
| `-Provider Jun -ApiUrl "https://your-host" -JunEmail ... -JunPassword ...` | Use the Jun webapp stack (chat + voice + shared history) |

Manual installation still works exactly as before — see [Manual installation](#manual-installation-windows).

## Where does the AI run? (pick one)

There are four ways to get a model answering, from "no setup at all" to "fully local":

### 1. Existing API providers (no GPU needed)

The mod speaks the OpenAI-compatible API, so it works with OpenAI, Mistral, Google, DeepSeek, Claude and any other compatible service — just set an API key. See [AI providers](#ai-providers) below.

### 2. OpenRouter (recommended cloud option)

One API key, a huge model catalog, including a good selection of **free** models.

```ini
[General]
UsedProvider = "OpenRouter"

[OpenRouter]
ApiUrl = "https://openrouter.ai/api/v1"
ApiKey = "PUT_YOUR_OPENROUTER_KEY_HERE"
Model = "deepseek/deepseek-r1-0528:free"
```

### 3. Google Colab (free GPU, no local install)

No decent GPU at home? Run the model on a free Google Colab GPU and point the mod at it over the internet:

1. Open [`colab_ollama_server.ipynb`](colab_ollama_server.ipynb) in [Google Colab](https://colab.research.google.com/).
2. Click **Runtime → Run all** and wait for the `https://….trycloudflare.com` address.
3. Put that address into the mod config (the notebook prints the exact snippet), or run the installer with `-OllamaUrl "https://xxx.trycloudflare.com/v1" -SkipOllamaInstall -SkipModelPull`.

Keep the Colab tab open while you play. Free sessions stop after a while — re-run the notebook and update the address when that happens.

### 4. Local Ollama (fully local, free, private)

- **Simplest:** install [Ollama](https://ollama.com/download) and let the installer (or the mod itself, on first chat) download the model.
- **Docker flavor:** `docker run -d --gpus=all -v ollama:/root/.ollama -p 11434:11434 --name ollama ollama/ollama`, then `docker exec -it ollama ollama pull <model>`. The default mod config works as is.
- Model quality and speed depend on your PC — a modern GPU with 7+ GB VRAM handles the default 12B model at Q4.

## Voice: TTS with lipsync

The mod can speak Jun's replies out loud while she talks, with her mouth moving in sync. Replies are synthesized **sentence by sentence as they stream in** — while one sentence plays, the next is already generating, so there's no long wait for the full reply.

The easiest setup is the [Jun webapp stack](https://github.com/efficiencyx/Jun), which bundles a CPU-based TTS engine (Kokoro) behind its PHP proxy + NGINX:

```ini
[Tts]
Enabled = true
ApiFormat = "Jun"      # uses [Jun] ApiUrl + credentials below
Voice = "af_heart"     # any Kokoro voice
Engine = "kokoro"      # or "pockettts"
```

Any OpenAI-compatible speech endpoint (OpenAI TTS, Kokoro-FastAPI, openedai-speech, AllTalk, ...) works too:

```ini
[Tts]
Enabled = true
ApiFormat = "OpenAI"
ApiUrl = "http://localhost:8880/v1"
Model = "tts-1"
Voice = "af_heart"
```

Lipsync is on by default (`LipSync = true`) and drives the Live2D mouth parameter from the audio amplitude, the same way the sex-scene moan mod drives the mouth value.

## Jun webapp stack: one conversation everywhere

If you run the [Jun stack](https://github.com/efficiencyx/Jun) (Docker bundle: NGINX with TLS + PHP proxy + Ollama + TTS), the mod can use it as a provider:

```ini
[General]
UsedProvider = "Jun"

[Jun]
ApiUrl = "https://your-host"   # NGINX terminates TLS there; LAN or WAN
Email = "you@example.com"      # your webapp account
Password = "..."
ConversationId = 0             # 0 = create automatically; set an id to share one chat everywhere
```

What you get on top of plain Ollama:

- **Same brain everywhere** — the game, the web UI and the Telegram bridge all talk to the same server endpoint, are logged/rate-limited identically, and share one server-side conversation history: start a chat in-game, continue it in the browser or on Telegram, and vice versa.
- **Voice for free** — set `[Tts] ApiFormat = "Jun"` and the bundled TTS engine is used with the same login.
- **Live2D reactions** — the Jun finetune's `[A:...]` action tags are translated to in-game expressions (happy/sad/angry/shock/blush...) on the fly.
- **TLS stays where it belongs** — the mod is a plain HTTPS client; certificates and TLS termination live in the stack's NGINX, not in the mod.

For the Telegram bridge, see [`server/telegram-bot/`](server/telegram-bot/).

## Requirements

- The game: **My Dystopian Robot Girlfriend**
- **MelonLoader Nightly 0.7.2+** (latest nightly build at the time of writing)
  - Installer: [LavaGang/MelonLoader.Installer](https://github.com/LavaGang/MelonLoader.Installer)
- This mod: `MdrgAiDialog.dll` (from the Releases page)
- **Ollama** (only required if you use the default/local provider)
  - Download: [ollama.com/download](https://ollama.com/download)

## Manual installation (Windows)

1. **Install MelonLoader (Nightly)**
   - Download and run the MelonLoader Installer: [LavaGang/MelonLoader.Installer](https://github.com/LavaGang/MelonLoader.Installer)
   - Select your game and install **Nightly** (0.7.2+).
2. **Run the game once** (this lets MelonLoader create its folders).
3. **Install the mod**
   - Download `MdrgAiDialog.dll` from the Releases page.
   - Copy it to:
     - `<GameFolder>\Mods\MdrgAiDialog.dll`
4. **Launch the game**.

> **Where is `<GameFolder>`?** The folder where `My Dystopian Robot Girlfriend.exe` is located.

## Configuration

The mod uses a single config file created on first launch:

- `<GameFolder>\UserData\MdrgAiDialog.cfg`

**How to edit it:**

1. Close the game.
2. Open `<GameFolder>\UserData\MdrgAiDialog.cfg` in Notepad.
3. Change `UsedProvider` and the settings for that provider.
4. Save the file and launch the game again.

### Minimal config example: Ollama

```ini
[General]
UsedProvider = "Ollama"

[Ollama]
ApiUrl = "http://localhost:11434/v1"
Model = "artifish/llama3.2-uncensored"
```

> **Tip:** The mod will check whether the model exists on your Ollama server. If it is missing, the game will ask if you want to download it and show progress.
>
> **Reverse proxy note:** `ApiUrl` may include a path prefix (e.g. `https://your-host/ollama/v1`) — the model check/download requests respect it.

## AI providers

Provider selection is done via `UsedProvider` in `[General]`. Names must match exactly:

- `Ollama` (default)
- `Jun` (the [Jun webapp stack](https://github.com/efficiencyx/Jun): chat + voice + shared history)
- `OpenAI`
- `OpenRouter`
- `Mistral`
- `Google`
- `DeepSeek`
- `Claude`
- `Mock` (just for testing)

### Ollama (default, local)

- **Default URL:** `http://localhost:11434/v1`
- **Default model:** `hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF`
- **If you don't install Ollama:** the mod will fail to connect using default settings.
- **Pros:** local, free, no usage limits, ...
- **Cons:** model quality and speed depend on your PC. Don't expect too much.

### OpenAI / OpenAI-compatible providers

This mod uses the **OpenAI** compatible endpoints so it will just work with
many services that provide such an API.

To use OpenAI (or a compatible service):

1. Set:
   - `[General] UsedProvider = "OpenAI"`
2. Set these fields in `[OpenAI]`:
   - `ApiUrl` (the base URL, usually ends with `/v1` - do not add `/chat/completions`)
   - `ApiKey` (your key/token)
   - `Model` (provider-specific model name)

### Mistral / Google / DeepSeek / Claude

These providers are pre-configured with default API URLs. In most cases you only need to set:

- `ApiKey`
- `Model`

## Troubleshooting

- **I see a connection error right away**
  - If you are using `Ollama`: install Ollama and make sure it is running.
  - Or switch to a cloud provider by setting `[General] UsedProvider = "OpenAI"` (or something else) and adding an API key.
- **Voice doesn't play**
  - Check `[Tts] Enabled = true` and that the TTS server is reachable.
  - With `ApiFormat = "Jun"`, the `[Jun]` `Email`/`Password` must be valid — TTS uses the same login as chat.
  - Look for `TtsClient`/`TtsManager` lines in `<GameFolder>\MelonLoader\Latest.log`.
- **My Colab address stopped working**
  - Free Colab sessions expire. Re-run the notebook and update `ApiUrl` (the address changes every run).
- **Config file is missing**
  - Run the game once with MelonLoader installed, then check `<GameFolder>\UserData\`.
- **Where are logs?**
  - `<GameFolder>\MelonLoader\Latest.log`
- **How to reset configuration?**
  - Close the game and delete `<GameFolder>\UserData\MdrgAiDialog.cfg` (it will be re-created on next launch).

## For developers

- Install **.NET 6 SDK**.
- Set your game path:
  - `Directory.Build.props` (`<GamePath>...</GamePath>`)
  - (Optional) `scripts\install.bat` (`GAME_DIR_PATH=...`)
- Build:
  - `dotnet build -c Release`
- The project copies the built DLL to the game `Mods` folder automatically (see `MdrgAiDialog.csproj`).
