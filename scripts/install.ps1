<#
.SYNOPSIS
  One-command installer for the MDRG AI Dialog mod (Windows).

.DESCRIPTION
  Does everything needed to chat with Jun:
    1. Finds your game folder (or asks you for it)
    2. Installs MelonLoader into the game if it is missing
    3. Downloads the newest MdrgAiDialog.dll into the Mods folder
    4. Installs Ollama and downloads the AI model (local setup only)
    5. Writes the mod's config file ([General] + provider block)

  You do NOT need to know anything about the command line.
  Right-click this file -> "Run with PowerShell" and follow the prompts.

.EXAMPLE
  # Fully automatic local setup (Ollama):
  .\install.ps1

.EXAMPLE
  # Point the mod at a remote/Colab/proxy server instead of local Ollama:
  .\install.ps1 -OllamaUrl "https://my-tunnel.trycloudflare.com/v1" -SkipOllamaInstall

.EXAMPLE
  # Use OpenRouter instead of Ollama:
  .\install.ps1 -Provider OpenRouter -Model "deepseek/deepseek-r1-0528:free" -ApiKey "sk-or-..."
#>
[CmdletBinding()]
param(
  # Folder that contains "My Dystopian Robot Girlfriend.exe". Leave empty to auto-detect.
  [string]$GamePath = "",

  # AI provider to write into the config: Ollama, Jun (the Jun webapp stack),
  # OpenAI, OpenRouter, Mistral, Google, DeepSeek, Claude
  [ValidateSet("Ollama", "Jun", "OpenAI", "OpenRouter", "Mistral", "Google", "DeepSeek", "Claude")]
  [string]$Provider = "Ollama",

  # Model name. Default is the model the mod ships with.
  [string]$Model = "hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF",

  # API URL used when -Provider is Ollama (local server, Colab tunnel or reverse proxy)
  [string]$OllamaUrl = "http://localhost:11434/v1",

  # API URL for non-Ollama providers. Leave empty to use the provider's default.
  [string]$ApiUrl = "",

  # API key (needed for cloud providers, optional for Ollama behind a proxy)
  [string]$ApiKey = "",

  # Jun webapp account (only with -Provider Jun). Also enables voice (TTS).
  [string]$JunEmail = "",
  [string]$JunPassword = "",

  # Custom system prompt / persona. Leave empty to keep the mod's default.
  [string]$SystemPersona = "",

  # GitHub repository to download the mod release from
  [string]$ModRepo = "StLyn4/MdrgAiDialog",

  # Skip installing/starting local Ollama (use when your server runs elsewhere)
  [switch]$SkipOllamaInstall,

  # Skip downloading the model (use when the server already has it)
  [switch]$SkipModelPull
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$GameExeName = "My Dystopian Robot Girlfriend.exe"

function Write-Step($text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text) { Write-Host "    OK: $text" -ForegroundColor Green }
function Write-Note($text) { Write-Host "    $text" -ForegroundColor Yellow }

function Find-GameFolder {
  if ($GamePath -and (Test-Path (Join-Path $GamePath $GameExeName))) {
    return (Resolve-Path $GamePath).Path
  }

  if ($GamePath) {
    Write-Note "'$GamePath' does not contain '$GameExeName', trying auto-detect..."
  }

  Write-Step "Looking for the game folder"

  $candidates = @()

  # Steam library folders
  $steamRoots = @(
    "${env:ProgramFiles(x86)}\Steam\steamapps\common",
    "$env:ProgramFiles\Steam\steamapps\common"
  )

  # Common manual-install locations (itch.io users usually unzip somewhere like these)
  $manualRoots = @(
    "$env:USERPROFILE\Downloads",
    "$env:USERPROFILE\Desktop",
    "$env:USERPROFILE\Documents",
    "C:\Games",
    "D:\Games"
  )

  foreach ($root in ($steamRoots + $manualRoots)) {
    if (Test-Path $root) {
      $found = Get-ChildItem -Path $root -Recurse -Depth 2 -Filter $GameExeName -ErrorAction SilentlyContinue |
        Select-Object -First 1
      if ($found) { $candidates += $found.DirectoryName }
    }
  }

  if ($candidates.Count -gt 0) {
    Write-Ok "Found game at: $($candidates[0])"
    return $candidates[0]
  }

  Write-Host ""
  Write-Host "I could not find the game automatically." -ForegroundColor Yellow
  Write-Host "Please open the folder that contains '$GameExeName' in Windows Explorer,"
  Write-Host "then drag that folder onto this window and press Enter."
  Write-Host ""

  while ($true) {
    $answer = (Read-Host "Game folder").Trim('"').Trim()
    if ($answer -and (Test-Path (Join-Path $answer $GameExeName))) {
      return (Resolve-Path $answer).Path
    }
    Write-Host "That folder does not contain '$GameExeName'. Please try again." -ForegroundColor Red
  }
}

function Install-MelonLoader($gameDir) {
  Write-Step "Checking MelonLoader"

  if ((Test-Path (Join-Path $gameDir "MelonLoader")) -and (Test-Path (Join-Path $gameDir "version.dll"))) {
    Write-Ok "MelonLoader is already installed"
    return
  }

  Write-Note "MelonLoader not found, downloading..."
  $zipUrl = "https://github.com/LavaGang/MelonLoader/releases/latest/download/MelonLoader.x64.zip"
  $zipPath = Join-Path $env:TEMP "MelonLoader.x64.zip"

  Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
  Expand-Archive -Path $zipPath -DestinationPath $gameDir -Force
  Remove-Item $zipPath -ErrorAction SilentlyContinue

  Write-Ok "MelonLoader installed into the game folder"
  Write-Note "The mod needs MelonLoader 0.7.2 or newer. If the game fails to load the mod,"
  Write-Note "install the latest Nightly with the official installer:"
  Write-Note "https://github.com/LavaGang/MelonLoader.Installer"
}

function Install-ModDll($gameDir) {
  Write-Step "Downloading the newest MdrgAiDialog.dll"

  $modsDir = Join-Path $gameDir "Mods"
  New-Item -ItemType Directory -Path $modsDir -Force | Out-Null

  $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$ModRepo/releases/latest" -UseBasicParsing
  $asset = $release.assets | Where-Object { $_.name -ieq "MdrgAiDialog.dll" } | Select-Object -First 1

  if (-not $asset) {
    # Fall back to the first zip that contains the dll
    $zipAsset = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
    if (-not $zipAsset) {
      throw "No MdrgAiDialog.dll or zip found in the latest release of $ModRepo"
    }

    $zipPath = Join-Path $env:TEMP $zipAsset.name
    $extractDir = Join-Path $env:TEMP "MdrgAiDialogRelease"
    Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    $dll = Get-ChildItem -Path $extractDir -Recurse -Filter "MdrgAiDialog.dll" | Select-Object -First 1
    if (-not $dll) { throw "The release zip does not contain MdrgAiDialog.dll" }

    Copy-Item $dll.FullName (Join-Path $modsDir "MdrgAiDialog.dll") -Force
    Remove-Item $zipPath, $extractDir -Recurse -Force -ErrorAction SilentlyContinue
  } else {
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile (Join-Path $modsDir "MdrgAiDialog.dll") -UseBasicParsing
  }

  Write-Ok "Mod installed: $modsDir\MdrgAiDialog.dll ($($release.tag_name))"
}

function Get-OllamaExe {
  $cmd = Get-Command "ollama" -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }

  $localExe = "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe"
  if (Test-Path $localExe) { return $localExe }

  return $null
}

function Install-Ollama {
  Write-Step "Checking Ollama"

  $exe = Get-OllamaExe
  if ($exe) {
    Write-Ok "Ollama is already installed ($exe)"
    return $exe
  }

  Write-Note "Ollama not found, downloading the installer (this is a big download)..."
  $setupPath = Join-Path $env:TEMP "OllamaSetup.exe"
  Invoke-WebRequest -Uri "https://ollama.com/download/OllamaSetup.exe" -OutFile $setupPath -UseBasicParsing

  Write-Note "Installing Ollama silently..."
  Start-Process -FilePath $setupPath -ArgumentList "/VERYSILENT", "/NORESTART" -Wait
  Remove-Item $setupPath -ErrorAction SilentlyContinue

  $exe = Get-OllamaExe
  if (-not $exe) {
    throw "Ollama installation finished but ollama.exe was not found. Install it manually from https://ollama.com/download"
  }

  Write-Ok "Ollama installed"
  return $exe
}

function Wait-ForOllama($ollamaExe) {
  # The installer usually starts Ollama automatically; make sure it is up
  for ($i = 0; $i -lt 15; $i++) {
    try {
      Invoke-RestMethod -Uri "http://localhost:11434/api/version" -TimeoutSec 2 | Out-Null
      return
    } catch {
      if ($i -eq 0) {
        Write-Note "Starting the Ollama server..."
        Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WindowStyle Hidden
      }
      Start-Sleep -Seconds 2
    }
  }
  throw "The Ollama server did not start. Start the Ollama app manually and re-run this script."
}

function Install-Model($ollamaExe) {
  Write-Step "Downloading the AI model: $Model"
  Write-Note "This can take a long time (several gigabytes). Please be patient."

  & $ollamaExe pull $Model
  if ($LASTEXITCODE -ne 0) {
    throw "Model download failed. You can also let the mod download it on first launch."
  }

  Write-Ok "Model is ready"
}

function Escape-Toml($value) {
  return $value.Replace('\', '\\').Replace('"', '\"').Replace("`r", '').Replace("`n", '\n')
}

function Write-ModConfig($gameDir) {
  Write-Step "Writing the mod configuration"

  $userDataDir = Join-Path $gameDir "UserData"
  New-Item -ItemType Directory -Path $userDataDir -Force | Out-Null
  $configPath = Join-Path $userDataDir "MdrgAiDialog.cfg"

  if (Test-Path $configPath) {
    $backupPath = "$configPath.backup"
    Copy-Item $configPath $backupPath -Force
    Write-Note "Existing config backed up to $backupPath"
  }

  $lines = @()
  $lines += "[General]"
  $lines += "UsedProvider = `"$Provider`""
  if ($SystemPersona) {
    $lines += "SystemPersona = `"$(Escape-Toml $SystemPersona)`""
  }
  $lines += ""

  if ($Provider -eq "Jun") {
    # The Jun webapp stack: chat, shared history and voice all come from one server
    $lines += "[Jun]"
    if ($ApiUrl) { $lines += "ApiUrl = `"$ApiUrl`"" }
    $lines += "Email = `"$JunEmail`""
    $lines += "Password = `"$(Escape-Toml $JunPassword)`""
    $lines += ""
    $lines += "[Tts]"
    $lines += "Enabled = true"
    $lines += "ApiFormat = `"Jun`""
  } else {
    $lines += "[$Provider]"

    if ($Provider -eq "Ollama") {
      $lines += "ApiUrl = `"$OllamaUrl`""
    } elseif ($ApiUrl) {
      $lines += "ApiUrl = `"$ApiUrl`""
    }

    $lines += "Model = `"$Model`""
    if ($ApiKey) {
      $lines += "ApiKey = `"$ApiKey`""
    }
  }
  $lines += ""

  # The mod fills in every other setting with defaults on first launch
  Set-Content -Path $configPath -Value ($lines -join "`r`n") -Encoding UTF8
  Write-Ok "Config written: $configPath"
}

# --- Main -------------------------------------------------------------------

Write-Host "=============================================" -ForegroundColor Magenta
Write-Host " MDRG AI Dialog - one-command installer" -ForegroundColor Magenta
Write-Host "=============================================" -ForegroundColor Magenta

$gameDir = Find-GameFolder
Install-MelonLoader $gameDir
Install-ModDll $gameDir

$isLocalOllama = ($Provider -eq "Ollama") -and ($OllamaUrl -match "localhost|127\.0\.0\.1")

if ($isLocalOllama -and -not $SkipOllamaInstall) {
  $ollamaExe = Install-Ollama
  Wait-ForOllama $ollamaExe

  if (-not $SkipModelPull) {
    Install-Model $ollamaExe
  }
} elseif ($Provider -eq "Ollama") {
  Write-Note "Using a remote Ollama server ($OllamaUrl), skipping local Ollama install."
  Write-Note "Make sure the server is running and the model '$Model' is pulled there."
}

Write-ModConfig $gameDir

Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host " All done! Start the game and press the chat" -ForegroundColor Green
Write-Host " button to talk to Jun." -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
