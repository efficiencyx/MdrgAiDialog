# MDRG AI Dialog

**Download:** [Latest Release](https://github.com/StLyn4/MdrgAiDialog/releases)

A MelonLoader mod for **My Dystopian Robot Girlfriend** that lets you chat with Jun through an LLM, local or cloud. It can also speak her replies out loud with lipsync, and keep one conversation going across the game, a browser, and Telegram if you run the [Jun webapp stack](https://github.com/efficiencyx/Jun).

> **Heads up:** by default the mod expects a local Ollama server at `http://localhost:11434`.
> If you don't have [Ollama](https://ollama.com/download) running, the default config will give you a connection error.

## One-command install

You don't need to touch the command line for this. The installer locates your game, installs MelonLoader and the mod, sets up Ollama, pulls the model, and writes the config for you.

**Windows:**

1. Download [`scripts/install.ps1`](scripts/install.ps1) (right-click → Save link as...).
2. Right-click the downloaded file → **Run with PowerShell**.
3. Answer the prompts. Then start the game and press the chat button.

**Linux / Steam Deck:**

```bash
./install.sh
```

Both scripts accept the same options (`-Flag` on Windows, `--flag` on Linux):

| Option | What it does |
|---|---|
| `-GamePath "C:\...\Game"` | Skip auto-detection of the game folder |
| `-OllamaUrl "https://xxx.trycloudflare.com/v1" -SkipOllamaInstall -SkipModelPull` | Use a remote/Colab server instead of local Ollama |
| `-Provider OpenRouter -Model "..." -ApiKey "sk-or-..."` | Use a cloud provider instead of Ollama |
| `-Provider Jun -ApiUrl "https://your-host" -JunEmail ... -JunPassword ...` | Use the Jun webapp stack (chat + voice + shared history) |

If you'd rather set it up by hand, that still works. See [Manual installation](#manual-installation-windows).

## Where does the AI run?

There are four ways to get a model answering, ranging from no setup at all to fully local.

### 1. Existing API providers (no GPU needed)

The mod talks to OpenAI-compatible APIs, so it works with OpenAI, Mistral, Google, DeepSeek, Claude, and most other compatible services. All you add is an API key. See [AI providers](#ai-providers) below.

### 2. OpenRouter

One API key gets you a large model catalog, including a decent selection of **free** models.

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

Keep the Colab tab open while you play. Free sessions time out after a while; re-run the notebook and update the address when that happens.

### 4. Local Ollama (fully local, free, private)

- **Simplest:** install [Ollama](https://ollama.com/download) and let the installer (or the mod itself, on first chat) download the model.
- **Docker:** `docker run -d --gpus=all -v ollama:/root/.ollama -p 11434:11434 --name ollama ollama/ollama`, then `docker exec -it ollama ollama pull <model>`. The default mod config works as is.
- Model quality and speed depend on your PC. A modern GPU with 7+ GB VRAM handles the default 12B model at Q4.

## Voice: TTS with lipsync

The mod can speak Jun's replies out loud while she talks, with her mouth moving in sync. Replies are synthesized sentence by sentence as they stream in, so while one sentence plays the next is already generating and you're not waiting for the whole reply.

The easiest setup is the [Jun webapp stack](https://github.com/efficiencyx/Jun), which bundles a CPU-based TTS engine (Kokoro) behind its PHP proxy and NGINX:

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

Lipsync is on by default (`LipSync = true`). It drives the Live2D mouth parameter from the audio amplitude, the same way the sex-scene moan mod drives the mouth value.

## Jun webapp stack: one conversation everywhere

If you run the [Jun stack](https://github.com/efficiencyx/Jun) (a Docker bundle: NGINX with TLS, PHP proxy, Ollama, and TTS), the mod can use it as a provider:

```ini
[General]
UsedProvider = "Jun"

[Jun]
ApiUrl = "https://your-host"   # NGINX terminates TLS there; LAN or WAN
Email = "you@example.com"      # your webapp account
Password = "..."
ConversationId = 0             # 0 = create automatically; set an id to share one chat everywhere
```

What this gets you over plain Ollama:

- **Same brain everywhere.** The game, the web UI, and the Telegram bridge all hit the same server endpoint, get logged and rate-limited the same way, and share one server-side conversation history. Start a chat in-game, continue it in the browser or on Telegram, and back again.
- **Voice for free.** Set `[Tts] ApiFormat = "Jun"` and the bundled TTS engine runs off the same login.
- **Live2D reactions.** The Jun finetune's `[A:...]` action tags get translated to in-game expressions (happy/sad/angry/shock/blush...) on the fly.
- **TLS stays where it belongs.** The mod is a plain HTTPS client; certificates and TLS termination live in the stack's NGINX, not in the mod.

For the Telegram bridge, see [`server/telegram-bot/`](server/telegram-bot/).

## Requirements

- The game: **My Dystopian Robot Girlfriend**
- **MelonLoader Nightly 0.7.2+** (the latest nightly build at the time of writing)
  - Installer: [LavaGang/MelonLoader.Installer](https://github.com/LavaGang/MelonLoader.Installer)
- This mod: `MdrgAiDialog.dll` (from the Releases page)
- **Ollama**, only if you use the default/local provider
  - Download: [ollama.com/download](https://ollama.com/download)

## Manual installation (Windows)

1. **Install MelonLoader (Nightly).**
   - Download and run the MelonLoader Installer: [LavaGang/MelonLoader.Installer](https://github.com/LavaGang/MelonLoader.Installer)
   - Select your game and install **Nightly** (0.7.2+).
2. **Run the game once** so MelonLoader can create its folders.
3. **Install the mod.**
   - Download `MdrgAiDialog.dll` from the Releases page.
   - Copy it to `<GameFolder>\Mods\MdrgAiDialog.dll`.
4. **Launch the game.**

> **Where is `<GameFolder>`?** The folder that holds `My Dystopian Robot Girlfriend.exe`.

## Configuration

The mod uses a single config file, created on first launch:

- `<GameFolder>\UserData\MdrgAiDialog.cfg`

To edit it:

1. Close the game.
2. Open `<GameFolder>\UserData\MdrgAiDialog.cfg` in Notepad.
3. Change `UsedProvider` and the settings for that provider.
4. Save and launch the game again.

### Minimal config example: Ollama

```ini
[General]
UsedProvider = "Ollama"

[Ollama]
ApiUrl = "http://localhost:11434/v1"
Model = "artifish/llama3.2-uncensored"
```

> **Tip:** the mod checks whether the model exists on your Ollama server. If it's missing, the game offers to download it and shows progress.
>
> **Reverse proxy note:** `ApiUrl` may include a path prefix (e.g. `https://your-host/ollama/v1`). The model check/download requests respect it.

## AI providers

Pick a provider with `UsedProvider` in `[General]`. The names have to match exactly:

- `Ollama` (default)
- `Jun` (the [Jun webapp stack](https://github.com/efficiencyx/Jun): chat + voice + shared history)
- `OpenAI`
- `OpenRouter`
- `Mistral`
- `Google`
- `DeepSeek`
- `Claude`
- `Mock` (for testing)

### Ollama (default, local)

- **Default URL:** `http://localhost:11434/v1`
- **Default model:** `hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF`
- **If you don't install Ollama:** the mod can't connect with the default settings.
- **Pros:** local, free, no usage limits.
- **Cons:** quality and speed depend on your PC, so keep expectations reasonable.

### OpenAI / OpenAI-compatible providers

The mod uses the OpenAI-compatible endpoints, so it works with the many services that expose that kind of API.

To use OpenAI (or a compatible service):

1. Set `[General] UsedProvider = "OpenAI"`.
2. Fill in these fields under `[OpenAI]`:
   - `ApiUrl` (the base URL, usually ending in `/v1`; don't add `/chat/completions`)
   - `ApiKey` (your key/token)
   - `Model` (the provider's model name)

### Mistral / Google / DeepSeek / Claude

These come pre-configured with default API URLs. Usually you only need:

- `ApiKey`
- `Model`

## Troubleshooting

- **A connection error right away.**
  - On `Ollama`: install Ollama and make sure it's running.
  - Or switch to a cloud provider by setting `[General] UsedProvider = "OpenAI"` (or another) and adding an API key.
- **Voice doesn't play.**
  - Check `[Tts] Enabled = true` and that the TTS server is reachable.
  - With `ApiFormat = "Jun"`, the `[Jun]` `Email`/`Password` have to be valid; TTS uses the same login as chat.
  - Look for `TtsClient`/`TtsManager` lines in `<GameFolder>\MelonLoader\Latest.log`.
- **My Colab address stopped working.**
  - Free Colab sessions expire. Re-run the notebook and update `ApiUrl` (the address changes every run).
- **Config file is missing.**
  - Run the game once with MelonLoader installed, then check `<GameFolder>\UserData\`.
- **Where are the logs?**
  - `<GameFolder>\MelonLoader\Latest.log`
- **How do I reset the config?**
  - Close the game and delete `<GameFolder>\UserData\MdrgAiDialog.cfg`. It gets re-created on the next launch.

## For developers

- Install **.NET 6 SDK**.
- Set your game path:
  - `Directory.Build.props` (`<GamePath>...</GamePath>`)
  - (Optional) `scripts\install.bat` (`GAME_DIR_PATH=...`)
- Build with `dotnet build -c Release`.
- The project copies the built DLL to the game's `Mods` folder automatically (see `MdrgAiDialog.csproj`).
