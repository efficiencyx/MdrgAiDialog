# Installation

## Requirements

- The game: **My Dystopian Robot Girlfriend**
- **MelonLoader Nightly 0.7.2+** — install with the [MelonLoader Installer](https://github.com/LavaGang/MelonLoader.Installer)
- The mod DLL: `MdrgAiDialog.dll` from the [Releases page](https://github.com/StLyn4/MdrgAiDialog/releases)
- **Ollama** ([ollama.com/download](https://ollama.com/download)) — only if you use the default local provider

## One-command installers

The `scripts/` folder contains installers that locate the game, install MelonLoader if missing, download the latest mod DLL into `Mods/`, optionally install Ollama and pull the model, and write the config file.

The installer needs the downloadable desktop game folder from itch.io or another standalone build. The Newgrounds browser version does not expose a local game folder that MelonLoader can patch.

If the usual itch.io/manual folders do not contain the game, the installer can ask to scan mounted disks for `My Dystopian Robot Girlfriend.exe` before falling back to manual path entry.

### One-line install

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


### Installer options

Both main installers accept the same options (`-Flag value` PowerShell style on Windows, `--flag value` on Linux):

| PowerShell | Bash | Purpose |
|---|---|---|
| `-GamePath` | `--game-path` | Skip auto-detection of the game folder |
| `-Provider` | `--provider` | Provider to configure (default `Ollama`) |
| `-Model` | `--model` | Model name (default `hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF`) |
| `-OllamaUrl` | `--ollama-url` | Ollama endpoint (default `http://localhost:11434/v1`) |
| `-ApiUrl` | `--api-url` | API URL for a cloud/Jun provider |
| `-ApiKey` | `--api-key` | API key for a cloud provider |
| `-JunEmail` | `--jun-email` | Jun webapp account email |
| `-JunPassword` | `--jun-password` | Jun webapp account password |
| `-SystemPersona` | `--system-persona` | Override the default character persona prompt |
| `-SkipOllamaInstall` | `--skip-ollama` | Don't install Ollama (remote server) |
| `-SkipModelPull` | `--skip-model-pull` | Don't pull the model locally |
| — | `--mod-repo` | GitHub repo to download the DLL from (default `StLyn4/MdrgAiDialog`) |

Common recipes:

```bash
# Remote/Colab Ollama over a Cloudflare tunnel
./install.sh --ollama-url "https://xxx.trycloudflare.com/v1" --skip-ollama --skip-model-pull

# Cloud provider via OpenRouter
./install.sh --provider OpenRouter --model "deepseek/deepseek-r1-0528:free" --api-key "sk-or-..."

# Jun webapp stack (chat + voice + shared history)
./install.sh --provider Jun --api-url "https://your-host" --jun-email you@example.com --jun-password ...
```

## Manual installation

1. **Install MelonLoader (Nightly 0.7.2+).** Run the [MelonLoader Installer](https://github.com/LavaGang/MelonLoader.Installer), select the game, choose the **Nightly** channel.
2. **Run the game once** so MelonLoader creates its folders (`Mods/`, `UserData/`, `MelonLoader/`).
3. **Copy the mod.** Put `MdrgAiDialog.dll` into `<GameFolder>/Mods/`.
4. **Launch the game.** On first run the in-game **provider picker** appears — choose a provider, enter its settings, and save. Alternatively edit `<GameFolder>/UserData/MdrgAiDialog.cfg` by hand (see the [configuration reference](configuration.md)).

`<GameFolder>` is the folder containing `My Dystopian Robot Girlfriend.exe`.

## Where does the AI run?

Four options, from zero-setup to fully local:

1. **Cloud API providers** — OpenAI, Mistral, Google, DeepSeek, Claude, or any OpenAI-compatible service. Just add an API key.
2. **OpenRouter** — one key, large catalog, including free models.
3. **Google Colab** — open [`colab_ollama_server.ipynb`](../colab_ollama_server.ipynb) in Colab, *Runtime → Run all*, and point the mod at the printed `https://….trycloudflare.com/v1` address. Free sessions expire; re-run the notebook and update the URL when they do.
4. **Local Ollama** — private and free; a modern GPU with 7+ GB VRAM handles the default 12B Q4 model. Docker works too: `docker run -d --gpus=all -v ollama:/root/.ollama -p 11434:11434 --name ollama ollama/ollama`.

## Using the mod in game

- Jun must be "smart" in the story (the game's `IsBotSmart` variable) — the mod then adds a **"Talk (AI)"** button to the interact menu and cuddle menu.
- Type into the popup and press **Send**; press **Close** (or type `/exit`) to end the chat.
- Press **F7** to reopen the provider picker at any time (falls back silently if the game routes input through the new Input System).

## Troubleshooting

- **Connection error immediately** — with Ollama: make sure Ollama is installed and running; otherwise switch providers or fix the API key.
- **Voice doesn't play** — check `[Tts] Enabled = true`, that the TTS server is reachable, and (for `ApiFormat = "Jun"`) that the `[Jun]` credentials are valid. Look for `TtsClient` / `TtsManager` lines in `MelonLoader/Latest.log`.
- **Colab URL stopped working** — sessions expire; re-run the notebook and update `ApiUrl`.
- **Missing model on Ollama** — the mod checks the server and offers an in-game download with a progress popup.
- **Config file missing** — run the game once with MelonLoader installed; it is created in `UserData/`.
- **Reset the config** — close the game and delete `UserData/MdrgAiDialog.cfg`.
- **Show the first-run picker again** — set `[General] ProviderConfigured = false`.
- **Logs** — `<GameFolder>/MelonLoader/Latest.log`.
