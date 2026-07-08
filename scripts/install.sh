#!/usr/bin/env bash
#
# Installer for the MDRG AI Dialog mod on Linux, Steam Deck, Wine, or Proton.
#
# This script locates the game folder, installs MelonLoader when missing,
# downloads the latest MdrgAiDialog.dll release, optionally installs/runs
# Ollama and pulls the default model, then writes UserData/MdrgAiDialog.cfg.

set -Eeuo pipefail

readonly GAME_EXE_NAME="My Dystopian Robot Girlfriend.exe"
readonly LINUX_GAME_FOLDER_NAME="factorial-omega-linux-64"
readonly WINDOWS_GAME_FOLDER_NAME="factorial-omega-win-64"
readonly INSTALLER_NAME="MDRG AI Dialog installer"

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
SKIP_MELONLOADER=0
SKIP_MOD_DOWNLOAD=0
SKIP_CONFIG=0
YES=0
DRY_RUN=0
NO_COLOR=0

TMP_PATHS=()
STARTED_AT="$(date +%s)"

usage() {
  cat <<'USAGE'
MDRG AI Dialog installer

Usage:
  ./install.sh [options]
  curl -fsSL https://raw.githubusercontent.com/StLyn4/MdrgAiDialog/master/scripts/install.sh | bash
  curl -fsSL https://raw.githubusercontent.com/StLyn4/MdrgAiDialog/master/scripts/install.sh | bash -s -- --yes

Options:
  --game-path PATH        Folder containing "My Dystopian Robot Girlfriend.exe"
  --provider NAME         Ollama, Jun, OpenAI, OpenRouter, Mistral, Google, DeepSeek, Claude
  --model NAME            Model to write into the config and pull from Ollama
  --ollama-url URL        Ollama/OpenAI-compatible endpoint
  --api-url URL           API URL for cloud or Jun providers
  --api-key KEY           API key for cloud providers
  --jun-email EMAIL       Jun webapp account email
  --jun-password PASS     Jun webapp account password
  --system-persona TEXT   Override the default character persona prompt
  --mod-repo OWNER/REPO   GitHub repo to download releases from
  --skip-ollama           Do not install or start local Ollama
  --skip-model-pull       Do not pull the model locally
  --skip-melonloader      Do not install MelonLoader
  --skip-mod              Do not download MdrgAiDialog.dll
  --skip-config           Do not write MdrgAiDialog.cfg
  --yes, -y               Accept installer prompts
  --dry-run               Print actions without changing files
  --no-color              Disable colored output
  --help, -h              Show this help

Examples:
  ./install.sh --game-path "$HOME/Games/My Dystopian Robot Girlfriend" --yes
  ./install.sh --ollama-url "https://xxx.trycloudflare.com/v1" --skip-ollama --skip-model-pull
  ./install.sh --provider OpenRouter --model "deepseek/deepseek-r1-0528:free" --api-key "sk-or-..."
USAGE
}

supports_color() {
  [[ "$NO_COLOR" -eq 0 && -t 1 ]]
}

color() {
  local code="$1"
  shift
  if supports_color; then
    printf '\033[%sm%s\033[0m\n' "$code" "$*"
  else
    printf '%s\n' "$*"
  fi
}

banner() {
  color "35" "============================================================"
  color "35" " $INSTALLER_NAME"
  color "35" " Linux / Steam Deck / Wine or Proton"
  color "35" "============================================================"
}

step() {
  printf '\n'
  color "36" "==> $*"
}

ok() {
  color "32" "    OK: $*"
}

note() {
  color "33" "    $*"
}

info() {
  color "37" "    $*"
}

die() {
  color "31" "ERROR: $*" >&2
  exit 1
}

dry_run() {
  note "[dry-run] $*"
}

on_error() {
  local exit_code=$?
  local line="${1:-unknown}"
  color "31" "ERROR: installer failed near line $line (exit $exit_code)" >&2
  exit "$exit_code"
}

cleanup() {
  local path
  for path in "${TMP_PATHS[@]:-}"; do
    if [[ -n "$path" && -e "$path" ]]; then
      rm -rf -- "$path"
    fi
  done
}

trap 'on_error $LINENO' ERR
trap cleanup EXIT

need_arg() {
  local flag="$1"
  local value="${2:-}"
  [[ -n "$value" && "$value" != --* ]] || die "$flag requires a value"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --game-path) need_arg "$1" "${2:-}"; GAME_PATH="$2"; shift 2 ;;
    --provider) need_arg "$1" "${2:-}"; PROVIDER="$2"; shift 2 ;;
    --model) need_arg "$1" "${2:-}"; MODEL="$2"; shift 2 ;;
    --ollama-url) need_arg "$1" "${2:-}"; OLLAMA_URL="$2"; shift 2 ;;
    --api-url) need_arg "$1" "${2:-}"; API_URL="$2"; shift 2 ;;
    --api-key) need_arg "$1" "${2:-}"; API_KEY="$2"; shift 2 ;;
    --jun-email) need_arg "$1" "${2:-}"; JUN_EMAIL="$2"; shift 2 ;;
    --jun-password) need_arg "$1" "${2:-}"; JUN_PASSWORD="$2"; shift 2 ;;
    --system-persona) need_arg "$1" "${2:-}"; SYSTEM_PERSONA="$2"; shift 2 ;;
    --mod-repo) need_arg "$1" "${2:-}"; MOD_REPO="$2"; shift 2 ;;
    --skip-ollama) SKIP_OLLAMA=1; shift ;;
    --skip-model-pull) SKIP_MODEL_PULL=1; shift ;;
    --skip-melonloader) SKIP_MELONLOADER=1; shift ;;
    --skip-mod|--skip-mod-download) SKIP_MOD_DOWNLOAD=1; shift ;;
    --skip-config) SKIP_CONFIG=1; shift ;;
    --yes|-y) YES=1; shift ;;
    --dry-run) DRY_RUN=1; shift ;;
    --no-color) NO_COLOR=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) die "Unknown option: $1 (use --help)" ;;
  esac
done

confirm() {
  local message="$1"
  local default_yes="${2:-1}"
  local suffix answer

  if [[ "$YES" -eq 1 ]]; then
    return 0
  fi

  if [[ ! -t 0 ]]; then
    [[ "$default_yes" -eq 1 ]]
    return
  fi

  if [[ "$default_yes" -eq 1 ]]; then
    suffix="[Y/n]"
  else
    suffix="[y/N]"
  fi

  while true; do
    read -r -p "$message $suffix " answer
    case "${answer,,}" in
      "") [[ "$default_yes" -eq 1 ]]; return ;;
      y|yes) return 0 ;;
      n|no) return 1 ;;
      *) note "Please answer yes or no." ;;
    esac
  done
}

require_tool() {
  command -v "$1" >/dev/null 2>&1 || die "'$1' is required. Install it with your package manager and re-run."
}

validate_provider() {
  case "$PROVIDER" in
    Ollama|Jun|OpenAI|OpenRouter|Mistral|Google|DeepSeek|Claude) return 0 ;;
    *) die "Unsupported provider '$PROVIDER'. Use --help to see valid providers." ;;
  esac
}

new_tmp_file() {
  local suffix="$1"
  local path

  if [[ "$DRY_RUN" -eq 1 ]]; then
    printf '%s' "${TMPDIR:-/tmp}/mdrg-ai-dialog.dry-run.$RANDOM.$suffix"
    return 0
  fi

  path="$(mktemp --tmpdir "mdrg-ai-dialog.XXXXXXXXXX.$suffix")"
  TMP_PATHS+=("$path")
  printf '%s' "$path"
}

new_tmp_dir() {
  local path

  if [[ "$DRY_RUN" -eq 1 ]]; then
    printf '%s' "${TMPDIR:-/tmp}/mdrg-ai-dialog.dry-run.$RANDOM"
    return 0
  fi

  path="$(mktemp -d --tmpdir "mdrg-ai-dialog.XXXXXXXXXX")"
  TMP_PATHS+=("$path")
  printf '%s' "$path"
}

download() {
  local url="$1"
  local out="$2"
  local label="${3:-file}"

  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "download $label from $url to $out"
    return 0
  fi

  curl --fail --location --retry 3 --connect-timeout 15 --progress-bar \
    --user-agent "MdrgAiDialogInstaller/1.0" \
    --output "$out" \
    "$url"
}

backup_file() {
  local path="$1"
  local backup

  [[ -f "$path" ]] || return 0

  backup="$path.backup-$(date +%Y%m%d-%H%M%S)"
  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "backup $path to $backup"
  else
    cp -p -- "$path" "$backup"
  fi
  note "Existing file backed up to $backup"
}

is_pe_file() {
  local path="$1"
  [[ -f "$path" ]] || return 1
  [[ "$(LC_ALL=C head -c 2 "$path")" == "MZ" ]]
}

trim_quotes() {
  local value="$1"
  value="${value#\"}"
  value="${value%\"}"
  printf '%s' "$value"
}

game_search_roots() {
  local roots=(
    "$HOME/.config/itch/apps"
    "$HOME/.local/share/itch/apps"
    "$HOME/.var/app/io.itch.itch/config/itch/apps"
    "$HOME/Games"
    "$HOME/Downloads"
    "$HOME/Desktop"
    "$HOME/Documents"
  )

  printf '%s\n' "${roots[@]}"
}

deep_search_roots() {
  {
    game_search_roots
    printf '%s\n' "/" "/mnt" "/media" "/run/media/${USER:-}" "/opt" "/games"
    if command -v df >/dev/null 2>&1; then
      df -P -l 2>/dev/null | awk 'NR > 1 { print $6 }'
    fi
  } | awk 'NF && !seen[$0]++'
}

resolve_game_folder() {
  local candidate
  candidate="$(trim_quotes "${1:-}")"
  [[ -n "$candidate" && -f "$candidate/$GAME_EXE_NAME" ]] || return 1
  (cd "$candidate" >/dev/null 2>&1 && pwd -P)
}

find_game_in_root() {
  local root="$1"
  local max_depth="${2:-}"
  local found resolved

  [[ -d "$root" ]] || return 1

  if [[ -n "$max_depth" ]]; then
    found="$(find "$root" -maxdepth "$max_depth" -type f -name "$GAME_EXE_NAME" -print -quit 2>/dev/null || true)"
  else
    found="$(find "$root" -type f -name "$GAME_EXE_NAME" -print -quit 2>/dev/null || true)"
  fi

  if [[ -n "$found" ]]; then
    dirname "$found"
    return 0
  fi

  if [[ -n "$max_depth" ]]; then
    found="$(find "$root" -maxdepth "$max_depth" -type d \( -name "$LINUX_GAME_FOLDER_NAME" -o -name "$WINDOWS_GAME_FOLDER_NAME" \) -print -quit 2>/dev/null || true)"
  else
    found="$(find "$root" -type d \( -name "$LINUX_GAME_FOLDER_NAME" -o -name "$WINDOWS_GAME_FOLDER_NAME" \) -print -quit 2>/dev/null || true)"
  fi

  if [[ -n "$found" ]] && resolved="$(resolve_game_folder "$found")"; then
    printf '%s\n' "$resolved"
    return 0
  fi

  return 1
}

deep_scan_game_folder() {
  local root resolved

  if ! confirm "Search mounted disks for the game? This can take several minutes." 0; then
    return 1
  fi

  step "Scanning mounted disks" >&2
  while IFS= read -r root; do
    [[ -d "$root" ]] || continue
    info "Deep searching $root" >&2
    if resolved="$(find_game_in_root "$root")"; then
      ok "Found game at: $resolved" >&2
      printf '%s' "$resolved"
      return 0
    fi
  done < <(deep_search_roots)

  return 1
}

find_game_folder() {
  local root found answer resolved

  if [[ -n "$GAME_PATH" ]]; then
    if resolved="$(resolve_game_folder "$GAME_PATH")"; then
      ok "Using game folder: $resolved" >&2
      printf '%s' "$resolved"
      return 0
    fi
    note "'$GAME_PATH' does not contain '$GAME_EXE_NAME'; auto-detecting instead." >&2
  fi

  step "Locating the game" >&2

  while IFS= read -r root; do
    [[ -d "$root" ]] || continue
    info "Searching $root" >&2
    if found="$(find_game_in_root "$root" 4)"; then
      printf '%s' "$found"
      return 0
    fi
  done < <(
    {
      game_search_roots
    } | awk 'NF && !seen[$0]++'
  )

  if resolved="$(deep_scan_game_folder)"; then
    printf '%s' "$resolved"
    return 0
  fi

  printf '\n' >&2
  note "I could not find the game automatically." >&2
  printf 'The Newgrounds browser version does not expose a folder MelonLoader can patch.\n' >&2
  printf 'Paste the folder containing "%s" and press Enter.\n' "$GAME_EXE_NAME" >&2

  while true; do
    read -r -p "Game folder: " answer
    if resolved="$(resolve_game_folder "$answer")"; then
      printf '%s' "$resolved"
      return 0
    fi
    color "31" "That folder does not contain '$GAME_EXE_NAME'." >&2
  done
}

install_melonloader() {
  local game_dir="$1"
  local zip_path

  if [[ "$SKIP_MELONLOADER" -eq 1 ]]; then
    note "Skipping MelonLoader install by request."
    return 0
  fi

  step "Checking MelonLoader"

  if [[ -d "$game_dir/MelonLoader" && -f "$game_dir/version.dll" ]]; then
    ok "MelonLoader is already installed"
    return 0
  fi

  note "MelonLoader not found. Installing the latest x64 release zip."
  zip_path="$(new_tmp_file "MelonLoader.x64.zip")"
  download "https://github.com/LavaGang/MelonLoader/releases/latest/download/MelonLoader.x64.zip" "$zip_path" "MelonLoader"

  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "extract MelonLoader into $game_dir"
    return 0
  fi

  unzip -o -q "$zip_path" -d "$game_dir"
  [[ -f "$game_dir/version.dll" ]] || die "MelonLoader extraction completed, but version.dll was not created."

  ok "MelonLoader installed"
  note "Wine/Proton launch option, if version.dll is not loaded automatically:"
  note 'WINEDLLOVERRIDES="version=n,b" %command%'
}

github_release_asset_url() {
  local pattern="$1"
  local json url

  json="$(curl --fail --silent --location --retry 3 \
    --user-agent "MdrgAiDialogInstaller/1.0" \
    "https://api.github.com/repos/$MOD_REPO/releases/latest")"

  if command -v python3 >/dev/null 2>&1; then
    url="$(printf '%s' "$json" | python3 -c '
import json
import sys

needle = sys.argv[1].lower()
data = json.load(sys.stdin)
for asset in data.get("assets", []):
    name = asset.get("name", "").lower()
    if name == needle or (needle == "*.zip" and name.endswith(".zip")):
        print(asset.get("browser_download_url", ""))
        break
' "$pattern")"
  else
    if [[ "$pattern" == "*.zip" ]]; then
      url="$(printf '%s' "$json" | tr '\n' ' ' | sed -n 's/.*"browser_download_url"[[:space:]]*:[[:space:]]*"\([^"]*\.zip\)".*/\1/p' | head -n 1)"
    else
      url="$(printf '%s' "$json" | tr '\n' ' ' | sed -n 's/.*"browser_download_url"[[:space:]]*:[[:space:]]*"\([^"]*MdrgAiDialog\.dll\)".*/\1/p' | head -n 1)"
    fi
  fi

  printf '%s' "$url"
}

install_mod_dll() {
  local game_dir="$1"
  local mods_dir="$game_dir/Mods"
  local target="$mods_dir/MdrgAiDialog.dll"
  local tmp_dll direct_url dll_url zip_url tmp_zip tmp_dir dll

  if [[ "$SKIP_MOD_DOWNLOAD" -eq 1 ]]; then
    note "Skipping mod DLL download by request."
    return 0
  fi

  step "Installing the latest MdrgAiDialog.dll"

  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "create $mods_dir"
  else
    mkdir -p "$mods_dir"
  fi

  tmp_dll="$(new_tmp_file "MdrgAiDialog.dll")"
  direct_url="https://github.com/$MOD_REPO/releases/latest/download/MdrgAiDialog.dll"

  if ! download "$direct_url" "$tmp_dll" "MdrgAiDialog.dll"; then
    note "Direct DLL asset was not available; checking release metadata."
    dll_url="$(github_release_asset_url "MdrgAiDialog.dll")"

    if [[ -n "$dll_url" ]]; then
      download "$dll_url" "$tmp_dll" "MdrgAiDialog.dll"
    else
      zip_url="$(github_release_asset_url "*.zip")"
      [[ -n "$zip_url" ]] || die "No MdrgAiDialog.dll or zip asset found in the latest release of $MOD_REPO."

      tmp_zip="$(new_tmp_file "release.zip")"
      tmp_dir="$(new_tmp_dir)"
      download "$zip_url" "$tmp_zip" "release zip"

      if [[ "$DRY_RUN" -eq 0 ]]; then
        unzip -o -q "$tmp_zip" -d "$tmp_dir"
        dll="$(find "$tmp_dir" -type f -name 'MdrgAiDialog.dll' -print -quit)"
        [[ -n "$dll" ]] || die "The release zip does not contain MdrgAiDialog.dll."
        cp -- "$dll" "$tmp_dll"
      fi
    fi
  fi

  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "install $tmp_dll to $target"
    return 0
  fi

  is_pe_file "$tmp_dll" || die "Downloaded MdrgAiDialog.dll does not look like a Windows DLL."
  backup_file "$target"
  cp -- "$tmp_dll" "$target"
  ok "Installed $target"
}

install_ollama() {
  local installer

  step "Checking Ollama"

  if command -v ollama >/dev/null 2>&1; then
    ok "Ollama is already installed"
  else
    if ! confirm "Ollama is not installed. Download and run the official installer now?" 1; then
      note "Ollama install skipped. Use a remote Ollama endpoint or a cloud provider."
      return 1
    fi

    installer="$(new_tmp_file "ollama-install.sh")"
    download "https://ollama.com/install.sh" "$installer" "Ollama installer"

    if [[ "$DRY_RUN" -eq 1 ]]; then
      dry_run "run sh $installer"
      return 0
    fi

    sh "$installer"
    command -v ollama >/dev/null 2>&1 || die "Ollama installation failed. Install it manually from https://ollama.com/download."
    ok "Ollama installed"
  fi
}

wait_for_ollama() {
  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "wait for Ollama at http://localhost:11434"
    return 0
  fi

  if curl --fail --silent --max-time 2 http://localhost:11434/api/version >/dev/null 2>&1; then
    ok "Ollama server is running"
    return 0
  fi

  note "Starting Ollama server in the background..."
  nohup ollama serve >/dev/null 2>&1 &

  for _ in $(seq 1 15); do
    sleep 2
    if curl --fail --silent --max-time 2 http://localhost:11434/api/version >/dev/null 2>&1; then
      ok "Ollama server is running"
      return 0
    fi
  done

  die "The Ollama server did not start. Run 'ollama serve' manually and re-run this script."
}

install_model() {
  step "Pulling model: $MODEL"
  note "This can take a long time; the default model is several gigabytes."

  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "ollama pull $MODEL"
    return 0
  fi

  ollama pull "$MODEL" || die "Model download failed. You can also let the mod download it on first launch."
  ok "Model is ready"
}

is_local_ollama() {
  [[ "$PROVIDER" == "Ollama" ]] || return 1
  [[ "$OLLAMA_URL" =~ ^https?://(localhost|127\.0\.0\.1|\[::1\]|::1)(:|/) ]]
}

escape_toml() {
  printf '%s' "$1" |
    sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' |
    tr -d '\r' |
    sed ':a;N;$!ba;s/\n/\\n/g'
}

toml_string() {
  local key="$1"
  local value="$2"
  printf '%s = "%s"\n' "$key" "$(escape_toml "$value")"
}

write_mod_config() {
  local game_dir="$1"
  local user_data_dir="$game_dir/UserData"
  local config_path="$user_data_dir/MdrgAiDialog.cfg"

  if [[ "$SKIP_CONFIG" -eq 1 ]]; then
    note "Skipping config write by request."
    return 0
  fi

  step "Writing mod configuration"

  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "create $user_data_dir"
  else
    mkdir -p "$user_data_dir"
  fi

  backup_file "$config_path"

  if [[ "$DRY_RUN" -eq 1 ]]; then
    dry_run "write $config_path"
    return 0
  fi

  {
    echo "[General]"
    toml_string "UsedProvider" "$PROVIDER"
    echo "ProviderConfigured = true"
    if [[ -n "$SYSTEM_PERSONA" ]]; then
      toml_string "SystemPersona" "$SYSTEM_PERSONA"
    fi
    echo ""

    if [[ "$PROVIDER" == "Jun" ]]; then
      echo "[Jun]"
      if [[ -n "$API_URL" ]]; then
        toml_string "ApiUrl" "$API_URL"
      fi
      toml_string "Email" "$JUN_EMAIL"
      toml_string "Password" "$JUN_PASSWORD"
      echo ""
      echo "[Tts]"
      echo "Enabled = true"
      toml_string "ApiFormat" "Jun"
    else
      echo "[$PROVIDER]"
      if [[ "$PROVIDER" == "Ollama" ]]; then
        toml_string "ApiUrl" "$OLLAMA_URL"
      elif [[ -n "$API_URL" ]]; then
        toml_string "ApiUrl" "$API_URL"
      fi
      toml_string "Model" "$MODEL"
      if [[ -n "$API_KEY" ]]; then
        toml_string "ApiKey" "$API_KEY"
      fi
    fi
    echo ""
  } > "$config_path"

  ok "Config written: $config_path"
}

main() {
  local game_dir elapsed

  validate_provider
  require_tool curl
  require_tool unzip
  require_tool find
  require_tool sed
  require_tool awk
  require_tool head

  banner
  if [[ "$DRY_RUN" -eq 1 ]]; then
    note "Dry run enabled. No files will be written and no installers will be started."
  fi
  info "Provider: $PROVIDER"
  if [[ "$PROVIDER" == "Ollama" ]]; then
    info "Ollama URL: $OLLAMA_URL"
  fi

  game_dir="$(find_game_folder)"
  ok "Game folder: $game_dir"

  install_melonloader "$game_dir"
  install_mod_dll "$game_dir"

  if is_local_ollama; then
    if [[ "$SKIP_OLLAMA" -eq 1 ]]; then
      note "Skipping local Ollama install by request."
    else
      if install_ollama; then
        wait_for_ollama
        if [[ "$SKIP_MODEL_PULL" -eq 0 ]]; then
          install_model
        fi
      elif [[ "$SKIP_MODEL_PULL" -eq 0 ]]; then
        note "Skipping model pull because Ollama is not installed."
      fi
    fi
  elif [[ "$PROVIDER" == "Ollama" ]]; then
    note "Using remote Ollama endpoint: $OLLAMA_URL"
    note "Make sure that server has the model '$MODEL' pulled."
  fi

  write_mod_config "$game_dir"

  elapsed="$(( $(date +%s) - STARTED_AT ))"
  printf '\n'
  color "32" "============================================================"
  color "32" " Install complete in ${elapsed}s"
  color "32" " Start the game and use the Talk (AI) button to chat with Jun."
  color "32" "============================================================"
}

main "$@"
