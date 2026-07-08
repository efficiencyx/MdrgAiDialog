#!/usr/bin/env bash
#
# One-command installer for the MDRG AI Dialog mod (Linux / Steam Deck / Proton).
#
# What it does:
#   1. Finds your game folder (or asks you for it)
#   2. Installs MelonLoader into the game if it is missing
#   3. Downloads the newest MdrgAiDialog.dll into the Mods folder
#   4. Installs Ollama and downloads the AI model (local setup only)
#   5. Writes the mod's config file ([General] + provider block)
#
# Usage:
#   ./install.sh                                  # fully automatic local setup
#   ./install.sh --game-path "/path/to/game"
#   ./install.sh --ollama-url "https://my-tunnel.trycloudflare.com/v1" --skip-ollama
#   ./install.sh --provider OpenRouter --model "deepseek/deepseek-r1-0528:free" --api-key "sk-or-..."
#
set -euo pipefail

GAME_EXE_NAME="My Dystopian Robot Girlfriend.exe"

GAME_PATH=""
PROVIDER="Ollama"
MODEL="hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF"
OLLAMA_URL="http://localhost:11434/v1"
API_URL=""
API_KEY=""
JUN_EMAIL=""
JUN_PASSWORD=""
SYSTEM_PERSONA=""
MOD_REPO="StLyn4/MdrgAiDialog"
SKIP_OLLAMA=0
SKIP_MODEL_PULL=0

usage() {
  sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'
  exit 0
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --game-path) GAME_PATH="$2"; shift 2 ;;
    --provider) PROVIDER="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --ollama-url) OLLAMA_URL="$2"; shift 2 ;;
    --api-url) API_URL="$2"; shift 2 ;;
    --api-key) API_KEY="$2"; shift 2 ;;
    --jun-email) JUN_EMAIL="$2"; shift 2 ;;
    --jun-password) JUN_PASSWORD="$2"; shift 2 ;;
    --system-persona) SYSTEM_PERSONA="$2"; shift 2 ;;
    --mod-repo) MOD_REPO="$2"; shift 2 ;;
    --skip-ollama) SKIP_OLLAMA=1; shift ;;
    --skip-model-pull) SKIP_MODEL_PULL=1; shift ;;
    -h|--help) usage ;;
    *) echo "Unknown option: $1 (use --help)"; exit 1 ;;
  esac
done

step() { printf '\n\033[36m==> %s\033[0m\n' "$1"; }
ok()   { printf '    \033[32mOK: %s\033[0m\n' "$1"; }
note() { printf '    \033[33m%s\033[0m\n' "$1"; }
die()  { printf '\033[31mERROR: %s\033[0m\n' "$1" >&2; exit 1; }

require_tool() {
  command -v "$1" >/dev/null 2>&1 || die "'$1' is required. Install it with your package manager and re-run."
}

require_tool curl
require_tool unzip

find_game_folder() {
  if [[ -n "$GAME_PATH" && -f "$GAME_PATH/$GAME_EXE_NAME" ]]; then
    printf '%s' "$GAME_PATH"
    return
  fi

  step "Looking for the game folder" >&2

  local roots=(
    "$HOME/.steam/steam/steamapps/common"
    "$HOME/.local/share/Steam/steamapps/common"
    "$HOME/Games"
    "$HOME/Downloads"
  )

  local found=""
  for root in "${roots[@]}"; do
    if [[ -d "$root" ]]; then
      found="$(find "$root" -maxdepth 3 -name "$GAME_EXE_NAME" -print -quit 2>/dev/null || true)"
      if [[ -n "$found" ]]; then
        dirname "$found"
        return
      fi
    fi
  done

  echo "" >&2
  echo "I could not find the game automatically." >&2
  echo "Please type (or paste) the full path of the folder that contains '$GAME_EXE_NAME':" >&2

  while true; do
    read -r -p "Game folder: " answer
    answer="${answer%\"}"; answer="${answer#\"}"
    if [[ -n "$answer" && -f "$answer/$GAME_EXE_NAME" ]]; then
      printf '%s' "$answer"
      return
    fi
    echo "That folder does not contain '$GAME_EXE_NAME'. Please try again." >&2
  done
}

install_melonloader() {
  local game_dir="$1"
  step "Checking MelonLoader"

  if [[ -d "$game_dir/MelonLoader" && -f "$game_dir/version.dll" ]]; then
    ok "MelonLoader is already installed"
    return
  fi

  note "MelonLoader not found, downloading..."
  local tmp_zip
  tmp_zip="$(mktemp --suffix=.zip)"
  curl -fL --progress-bar -o "$tmp_zip" \
    "https://github.com/LavaGang/MelonLoader/releases/latest/download/MelonLoader.x64.zip"
  unzip -o -q "$tmp_zip" -d "$game_dir"
  rm -f "$tmp_zip"

  ok "MelonLoader installed into the game folder"
  note "The mod needs MelonLoader 0.7.2 or newer."
  note "Steam Deck / Proton: add this to the game's Steam launch options so MelonLoader loads:"
  note '  WINEDLLOVERRIDES="version=n,b" %command%'
}

install_mod_dll() {
  local game_dir="$1"
  step "Downloading the newest MdrgAiDialog.dll"

  local mods_dir="$game_dir/Mods"
  mkdir -p "$mods_dir"

  local api="https://api.github.com/repos/$MOD_REPO/releases/latest"
  local dll_url
  dll_url="$(curl -fsL "$api" | grep -o '"browser_download_url": *"[^"]*MdrgAiDialog.dll"' | head -n1 | sed 's/.*"\(https[^"]*\)"/\1/')"

  if [[ -n "$dll_url" ]]; then
    curl -fL --progress-bar -o "$mods_dir/MdrgAiDialog.dll" "$dll_url"
  else
    # Fall back to the first zip asset that contains the dll
    local zip_url tmp_zip tmp_dir
    zip_url="$(curl -fsL "$api" | grep -o '"browser_download_url": *"[^"]*\.zip"' | head -n1 | sed 's/.*"\(https[^"]*\)"/\1/')"
    [[ -n "$zip_url" ]] || die "No MdrgAiDialog.dll or zip found in the latest release of $MOD_REPO"

    tmp_zip="$(mktemp --suffix=.zip)"
    tmp_dir="$(mktemp -d)"
    curl -fL --progress-bar -o "$tmp_zip" "$zip_url"
    unzip -o -q "$tmp_zip" -d "$tmp_dir"

    local dll
    dll="$(find "$tmp_dir" -name 'MdrgAiDialog.dll' -print -quit)"
    [[ -n "$dll" ]] || die "The release zip does not contain MdrgAiDialog.dll"
    cp "$dll" "$mods_dir/MdrgAiDialog.dll"
    rm -rf "$tmp_zip" "$tmp_dir"
  fi

  ok "Mod installed: $mods_dir/MdrgAiDialog.dll"
}

install_ollama() {
  step "Checking Ollama"

  if command -v ollama >/dev/null 2>&1; then
    ok "Ollama is already installed"
  else
    note "Ollama not found, installing (asks for sudo)..."
    curl -fsSL https://ollama.com/install.sh | sh
    command -v ollama >/dev/null 2>&1 || die "Ollama installation failed. Install it manually from https://ollama.com/download"
    ok "Ollama installed"
  fi

  # Make sure the server is running
  if ! curl -fsS --max-time 2 http://localhost:11434/api/version >/dev/null 2>&1; then
    note "Starting the Ollama server in the background..."
    nohup ollama serve >/dev/null 2>&1 &
    for _ in $(seq 1 15); do
      sleep 2
      curl -fsS --max-time 2 http://localhost:11434/api/version >/dev/null 2>&1 && break
    done
    curl -fsS --max-time 2 http://localhost:11434/api/version >/dev/null 2>&1 \
      || die "The Ollama server did not start. Run 'ollama serve' manually and re-run this script."
  fi
}

install_model() {
  step "Downloading the AI model: $MODEL"
  note "This can take a long time (several gigabytes). Please be patient."
  ollama pull "$MODEL" || die "Model download failed. You can also let the mod download it on first launch."
  ok "Model is ready"
}

escape_toml() {
  printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '\r' | sed ':a;N;$!ba;s/\n/\\n/g'
}

write_mod_config() {
  local game_dir="$1"
  step "Writing the mod configuration"

  local user_data_dir="$game_dir/UserData"
  mkdir -p "$user_data_dir"
  local config_path="$user_data_dir/MdrgAiDialog.cfg"

  if [[ -f "$config_path" ]]; then
    cp "$config_path" "$config_path.backup"
    note "Existing config backed up to $config_path.backup; advanced settings from it stay only in the backup."
  fi

  {
    echo "[General]"
    echo "UsedProvider = \"$PROVIDER\""
    echo "ProviderConfigured = true"
    if [[ -n "$SYSTEM_PERSONA" ]]; then
      echo "SystemPersona = \"$(escape_toml "$SYSTEM_PERSONA")\""
    fi
    echo ""
    if [[ "$PROVIDER" == "Jun" ]]; then
      # The Jun webapp stack: chat, shared history and voice all come from one server
      echo "[Jun]"
      if [[ -n "$API_URL" ]]; then
        echo "ApiUrl = \"$API_URL\""
      fi
      echo "Email = \"$JUN_EMAIL\""
      echo "Password = \"$(escape_toml "$JUN_PASSWORD")\""
      echo ""
      echo "[Tts]"
      echo "Enabled = true"
      echo "ApiFormat = \"Jun\""
    else
      echo "[$PROVIDER]"
      if [[ "$PROVIDER" == "Ollama" ]]; then
        echo "ApiUrl = \"$OLLAMA_URL\""
      elif [[ -n "$API_URL" ]]; then
        echo "ApiUrl = \"$API_URL\""
      fi
      echo "Model = \"$MODEL\""
      if [[ -n "$API_KEY" ]]; then
        echo "ApiKey = \"$API_KEY\""
      fi
    fi
    echo ""
  } > "$config_path"

  # The mod fills in every other setting with defaults on first launch
  ok "Config written: $config_path"
}

# --- Main ---------------------------------------------------------------------

printf '\033[35m=============================================\n'
printf ' MDRG AI Dialog - one-command installer\n'
printf '=============================================\033[0m\n'

game_dir="$(find_game_folder)"
ok "Game folder: $game_dir"

install_melonloader "$game_dir"
install_mod_dll "$game_dir"

if [[ "$PROVIDER" == "Ollama" && "$OLLAMA_URL" =~ localhost|127\.0\.0\.1 && $SKIP_OLLAMA -eq 0 ]]; then
  install_ollama
  if [[ $SKIP_MODEL_PULL -eq 0 ]]; then
    install_model
  fi
elif [[ "$PROVIDER" == "Ollama" ]]; then
  note "Using a remote Ollama server ($OLLAMA_URL), skipping local Ollama install."
  note "Make sure the server is running and the model '$MODEL' is pulled there."
fi

write_mod_config "$game_dir"

printf '\n\033[32m=============================================\n'
printf ' All done! Start the game and press the chat\n'
printf ' button to talk to Jun.\n'
printf '=============================================\033[0m\n'
