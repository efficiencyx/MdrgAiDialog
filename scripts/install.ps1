<#
.SYNOPSIS
  Installer for the MDRG AI Dialog mod on Windows.

.DESCRIPTION
  Locates the game folder, installs MelonLoader when missing, downloads the
  latest MdrgAiDialog.dll release, optionally installs/runs Ollama and pulls the
  default model, then writes UserData\MdrgAiDialog.cfg.

  The script is intentionally idempotent: existing mod DLLs and configs are
  backed up before being replaced, temporary files are cleaned up, and every
  external download is retried before the installer fails.

.EXAMPLE
  .\install.ps1

.EXAMPLE
  .\install.ps1 -GamePath "$env:USERPROFILE\Downloads\My Dystopian Robot Girlfriend" -Yes

.EXAMPLE
  .\install.ps1 -OllamaUrl "https://my-tunnel.trycloudflare.com/v1" -SkipOllamaInstall -SkipModelPull

.EXAMPLE
  .\install.ps1 -Provider OpenRouter -Model "deepseek/deepseek-r1-0528:free" -ApiKey "sk-or-..."
#>
[CmdletBinding()]
param(
  [string]$GamePath = "",

  [ValidateSet("Ollama", "Jun", "OpenAI", "OpenRouter", "Mistral", "Google", "DeepSeek", "Claude")]
  [string]$Provider = "Ollama",

  [string]$Model = "hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF",
  [string]$OllamaUrl = "http://localhost:11434/v1",
  [string]$ApiUrl = "",
  [string]$ApiKey = "",
  [string]$JunEmail = "",
  [string]$JunPassword = "",
  [string]$SystemPersona = "",
  [string]$ModRepo = "StLyn4/MdrgAiDialog",

  [Alias("SkipOllama")]
  [switch]$SkipOllamaInstall,
  [switch]$SkipModelPull,
  [switch]$SkipMelonLoader,
  [switch]$SkipModDownload,
  [switch]$SkipConfig,
  [switch]$LaunchGame,

  [Alias("y")]
  [switch]$Yes,
  [switch]$DryRun,
  [switch]$NoColor
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "Continue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$script:GameExeName = "My Dystopian Robot Girlfriend.exe"
$script:GameFolderNames = @("factorial-omega-win-64")
$script:TempPaths = [System.Collections.Generic.List[string]]::new()
$script:StartedAt = Get-Date

function Write-InstallerLine {
  param(
    [Parameter(Mandatory = $true)][string]$Text,
    [ConsoleColor]$Color = [ConsoleColor]::Gray
  )

  if ($NoColor) {
    Write-Host $Text
  } else {
    Write-Host $Text -ForegroundColor $Color
  }
}

function Write-Banner {
  Write-InstallerLine "============================================================" Magenta
  Write-InstallerLine " MDRG AI Dialog installer" Magenta
  Write-InstallerLine " Windows / MelonLoader / Ollama-ready" Magenta
  Write-InstallerLine "============================================================" Magenta
}

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Text)
  Write-Host ""
  Write-InstallerLine "==> $Text" Cyan
}

function Write-Ok {
  param([Parameter(Mandatory = $true)][string]$Text)
  Write-InstallerLine "    OK: $Text" Green
}

function Write-Note {
  param([Parameter(Mandatory = $true)][string]$Text)
  Write-InstallerLine "    $Text" Yellow
}

function Write-Info {
  param([Parameter(Mandatory = $true)][string]$Text)
  Write-InstallerLine "    $Text" Gray
}

function Write-Fail {
  param([Parameter(Mandatory = $true)][string]$Text)
  Write-InstallerLine "ERROR: $Text" Red
}

function Assert-Windows {
  if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "This installer is for Windows. Use scripts/install.sh on Linux or Steam Deck."
  }
}

function Confirm-InstallerAction {
  param(
    [Parameter(Mandatory = $true)][string]$Message,
    [bool]$DefaultYes = $true
  )

  if ($Yes) {
    return $true
  }

  if (-not [Environment]::UserInteractive) {
    return $DefaultYes
  }

  $suffix = if ($DefaultYes) { "[Y/n]" } else { "[y/N]" }
  while ($true) {
    $answer = (Read-Host "$Message $suffix").Trim()
    if ([string]::IsNullOrWhiteSpace($answer)) {
      return $DefaultYes
    }

    switch -Regex ($answer) {
      "^(y|yes)$" { return $true }
      "^(n|no)$" { return $false }
      default { Write-Note "Please answer yes or no." }
    }
  }
}

function New-InstallerTempPath {
  param([Parameter(Mandatory = $true)][string]$LeafName)
  $safeLeafName = $LeafName -replace '[^\w\.\-]', '-'
  $path = Join-Path ([IO.Path]::GetTempPath()) ("mdrg-ai-dialog-{0}-{1}" -f ([Guid]::NewGuid().ToString("N")), $safeLeafName)
  [void]$script:TempPaths.Add($path)
  return $path
}

function Remove-InstallerTemps {
  foreach ($path in $script:TempPaths) {
    if ($path -and (Test-Path -LiteralPath $path)) {
      Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
}

function Invoke-DryRun {
  param([Parameter(Mandatory = $true)][string]$Text)
  Write-Note "[dry-run] $Text"
}

function Invoke-Download {
  param(
    [Parameter(Mandatory = $true)][string]$Uri,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$Description = "file"
  )

  if ($DryRun) {
    Invoke-DryRun "download $Description from $Uri to $OutFile"
    return
  }

  $headers = @{
    "User-Agent" = "MdrgAiDialogInstaller/1.0"
  }

  for ($attempt = 1; $attempt -le 3; $attempt++) {
    try {
      Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing -Headers $headers
      return
    } catch {
      if ($attempt -eq 3) {
        throw "Failed to download $Description from $Uri. $($_.Exception.Message)"
      }

      Write-Note "Download failed, retrying ($attempt/3)..."
      Start-Sleep -Seconds (2 * $attempt)
    }
  }
}

function Invoke-GitHubApi {
  param([Parameter(Mandatory = $true)][string]$Uri)

  if ($DryRun) {
    Invoke-DryRun "query GitHub API: $Uri"
    return $null
  }

  $headers = @{
    "Accept" = "application/vnd.github+json"
    "User-Agent" = "MdrgAiDialogInstaller/1.0"
  }

  return Invoke-RestMethod -Uri $Uri -UseBasicParsing -Headers $headers
}

function Test-PeFile {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) {
    return $false
  }

  $stream = [IO.File]::OpenRead($Path)
  try {
    if ($stream.Length -lt 2) {
      return $false
    }

    $bytes = New-Object byte[] 2
    [void]$stream.Read($bytes, 0, 2)
    return ($bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A)
  } finally {
    $stream.Dispose()
  }
}

function Backup-File {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  if ($DryRun) {
    Invoke-DryRun "backup $Path"
    return "$Path.backup"
  }

  $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
  $backupPath = "$Path.backup-$stamp"
  Copy-Item -LiteralPath $Path -Destination $backupPath -Force
  return $backupPath
}

function Add-ExistingRoot {
  param(
    [Parameter(Mandatory = $true)]$Roots,
    [string]$Path
  )

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return
  }

  $expanded = [Environment]::ExpandEnvironmentVariables($Path)
  if (Test-Path -LiteralPath $expanded) {
    $resolved = (Resolve-Path -LiteralPath $expanded).Path
    if (-not $Roots.Contains($resolved)) {
      [void]$Roots.Add($resolved)
    }
  }
}

function Get-GameSearchRoots {
  $roots = [System.Collections.Generic.List[string]]::new()

  Add-ExistingRoot $roots "$env:APPDATA\itch\apps"
  Add-ExistingRoot $roots "$env:LOCALAPPDATA\itch\apps"
  Add-ExistingRoot $roots "$env:USERPROFILE\itch\apps"
  Add-ExistingRoot $roots "$env:USERPROFILE\Downloads"
  Add-ExistingRoot $roots "$env:USERPROFILE\Desktop"
  Add-ExistingRoot $roots "$env:USERPROFILE\Documents"
  Add-ExistingRoot $roots "$env:USERPROFILE\Games"
  Add-ExistingRoot $roots "C:\Games"
  Add-ExistingRoot $roots "D:\Games"

  return $roots
}

function Get-DeepSearchRoots {
  $roots = [System.Collections.Generic.List[string]]::new()

  foreach ($drive in [IO.DriveInfo]::GetDrives()) {
    if (-not $drive.IsReady) {
      continue
    }

    if ($drive.DriveType -in @([IO.DriveType]::Fixed, [IO.DriveType]::Removable)) {
      Add-ExistingRoot $roots $drive.RootDirectory.FullName
    }
  }

  return $roots
}

function Resolve-GameFolderInput {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return $null
  }

  $cleanPath = [Environment]::ExpandEnvironmentVariables($Path.Trim('"').Trim())
  if (Test-Path -LiteralPath (Join-Path $cleanPath $script:GameExeName)) {
    return (Resolve-Path -LiteralPath $cleanPath).Path
  }

  return $null
}

function Find-GameUnderRoot {
  param(
    [Parameter(Mandatory = $true)][string]$Root,
    [int]$Depth = -1
  )

  if (-not (Test-Path -LiteralPath $Root)) {
    return $null
  }

  $fileArgs = @{
    LiteralPath = $Root
    Recurse = $true
    File = $true
    Filter = $script:GameExeName
    ErrorAction = "SilentlyContinue"
  }
  if ($Depth -ge 0) {
    $fileArgs.Depth = $Depth
  }

  $found = Get-ChildItem @fileArgs | Select-Object -First 1
  if ($found) {
    return $found.DirectoryName
  }

  foreach ($folderName in $script:GameFolderNames) {
    $directoryArgs = @{
      LiteralPath = $Root
      Recurse = $true
      Directory = $true
      Filter = $folderName
      ErrorAction = "SilentlyContinue"
    }
    if ($Depth -ge 0) {
      $directoryArgs.Depth = $Depth
    }

    $folder = Get-ChildItem @directoryArgs | Select-Object -First 1
    if ($folder) {
      $resolved = Resolve-GameFolderInput $folder.FullName
      if ($resolved) {
        return $resolved
      }
    }
  }

  return $null
}

function Find-GameByDeepScan {
  if (-not (Confirm-InstallerAction "Search local drives for the game? This can take several minutes." $false)) {
    return $null
  }

  Write-Step "Scanning local drives"
  foreach ($root in Get-DeepSearchRoots) {
    Write-Info "Deep searching $root"
    $found = Find-GameUnderRoot -Root $root
    if ($found) {
      Write-Ok "Found game at: $found"
      return $found
    }
  }

  return $null
}

function Find-GameFolder {
  $explicitGameFolder = Resolve-GameFolderInput $GamePath
  if ($explicitGameFolder) {
    Write-Ok "Using game folder: $explicitGameFolder"
    return $explicitGameFolder
  }

  if ($GamePath) {
    Write-Note "'$GamePath' does not contain '$script:GameExeName'; auto-detecting instead."
  }

  Write-Step "Locating the game"

  $roots = [System.Collections.Generic.List[string]]::new()
  foreach ($root in Get-GameSearchRoots) {
    Add-ExistingRoot $roots $root
  }

  foreach ($root in $roots) {
    Write-Info "Searching $root"
    $found = Find-GameUnderRoot -Root $root -Depth 4
    if ($found) {
      Write-Ok "Found game at: $found"
      return $found
    }
  }

  $deepFound = Find-GameByDeepScan
  if ($deepFound) {
    return $deepFound
  }

  Write-Host ""
  Write-Note "I could not find the game automatically."
  Write-Host "The Newgrounds browser version does not expose a folder MelonLoader can patch."
  Write-Host "Open the folder that contains '$script:GameExeName', drag it onto this window, and press Enter."

  while ($true) {
    $answer = Read-Host "Game folder"
    $resolved = Resolve-GameFolderInput $answer
    if ($resolved) {
      return $resolved
    }

    Write-Fail "That folder does not contain '$script:GameExeName'."
  }
}

function Install-MelonLoader {
  param([Parameter(Mandatory = $true)][string]$GameDir)

  if ($SkipMelonLoader) {
    Write-Note "Skipping MelonLoader install by request."
    return
  }

  Write-Step "Checking MelonLoader"
  $melonDir = Join-Path $GameDir "MelonLoader"
  $versionDll = Join-Path $GameDir "version.dll"

  if ((Test-Path -LiteralPath $melonDir) -and (Test-Path -LiteralPath $versionDll)) {
    Write-Ok "MelonLoader is already installed"
    return
  }

  Write-Note "MelonLoader not found. Installing the latest x64 release zip."
  $zipUrl = "https://github.com/LavaGang/MelonLoader/releases/latest/download/MelonLoader.x64.zip"
  $zipPath = New-InstallerTempPath "MelonLoader.x64.zip"
  Invoke-Download -Uri $zipUrl -OutFile $zipPath -Description "MelonLoader"

  if ($DryRun) {
    Invoke-DryRun "extract MelonLoader into $GameDir"
    return
  }

  Expand-Archive -LiteralPath $zipPath -DestinationPath $GameDir -Force

  if (-not (Test-Path -LiteralPath $versionDll)) {
    throw "MelonLoader extraction completed, but version.dll was not created in $GameDir."
  }

  Write-Ok "MelonLoader installed"
  Write-Note "If the game does not load the mod, install MelonLoader Nightly 0.7.2+ with the official installer."
}

function Install-ModDll {
  param([Parameter(Mandatory = $true)][string]$GameDir)

  if ($SkipModDownload) {
    Write-Note "Skipping mod DLL download by request."
    return
  }

  Write-Step "Installing the latest MdrgAiDialog.dll"

  $modsDir = Join-Path $GameDir "Mods"
  if ($DryRun) {
    Invoke-DryRun "create $modsDir"
  } else {
    New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
  }

  $targetPath = Join-Path $modsDir "MdrgAiDialog.dll"
  $tmpDll = New-InstallerTempPath "MdrgAiDialog.dll"
  $releaseTag = "latest"
  $directUrl = "https://github.com/$ModRepo/releases/latest/download/MdrgAiDialog.dll"

  try {
    Invoke-Download -Uri $directUrl -OutFile $tmpDll -Description "MdrgAiDialog.dll"
  } catch {
    Write-Note "Direct DLL asset was not available; checking release metadata."
    $release = Invoke-GitHubApi -Uri "https://api.github.com/repos/$ModRepo/releases/latest"
    if (-not $release) {
      throw
    }

    $releaseTag = $release.tag_name
    $asset = $release.assets | Where-Object { $_.name -ieq "MdrgAiDialog.dll" } | Select-Object -First 1

    if ($asset) {
      Invoke-Download -Uri $asset.browser_download_url -OutFile $tmpDll -Description "MdrgAiDialog.dll"
    } else {
      $zipAsset = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
      if (-not $zipAsset) {
        throw "No MdrgAiDialog.dll or zip asset found in the latest release of $ModRepo."
      }

      $zipPath = New-InstallerTempPath $zipAsset.name
      $extractDir = New-InstallerTempPath "release"
      Invoke-Download -Uri $zipAsset.browser_download_url -OutFile $zipPath -Description $zipAsset.name

      if (-not $DryRun) {
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force

        $dll = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter "MdrgAiDialog.dll" -File |
          Select-Object -First 1
        if (-not $dll) {
          throw "The release zip does not contain MdrgAiDialog.dll."
        }

        Copy-Item -LiteralPath $dll.FullName -Destination $tmpDll -Force
      }
    }
  }

  if ($DryRun) {
    Invoke-DryRun "install $tmpDll to $targetPath"
    return
  }

  if (-not (Test-PeFile $tmpDll)) {
    throw "Downloaded MdrgAiDialog.dll does not look like a Windows DLL."
  }

  $backupPath = Backup-File $targetPath
  if ($backupPath) {
    Write-Note "Existing mod backed up to $backupPath"
  }

  Move-Item -LiteralPath $tmpDll -Destination $targetPath -Force
  Write-Ok "Installed $targetPath ($releaseTag)"
}

function Get-OllamaExe {
  $cmd = Get-Command "ollama" -ErrorAction SilentlyContinue
  if ($cmd) {
    return $cmd.Source
  }

  $localExe = Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"
  if (Test-Path -LiteralPath $localExe) {
    return $localExe
  }

  return $null
}

function Install-Ollama {
  Write-Step "Checking Ollama"

  $exe = Get-OllamaExe
  if ($exe) {
    Write-Ok "Ollama is installed: $exe"
    return $exe
  }

  if (-not (Confirm-InstallerAction "Ollama is not installed. Download and install it now?" $true)) {
    Write-Note "Ollama install skipped. The mod can still use a remote Ollama endpoint or a cloud provider."
    return $null
  }

  $setupPath = New-InstallerTempPath "OllamaSetup.exe"
  Invoke-Download -Uri "https://ollama.com/download/OllamaSetup.exe" -OutFile $setupPath -Description "Ollama installer"

  if ($DryRun) {
    Invoke-DryRun "run $setupPath /VERYSILENT /NORESTART"
    return "ollama.exe"
  }

  Write-Note "Installing Ollama silently..."
  $process = Start-Process -FilePath $setupPath -ArgumentList "/VERYSILENT", "/NORESTART" -Wait -PassThru
  if ($process.ExitCode -ne 0) {
    throw "The Ollama installer exited with code $($process.ExitCode)."
  }

  $exe = Get-OllamaExe
  if (-not $exe) {
    throw "Ollama installed, but ollama.exe was not found. Install it manually from https://ollama.com/download."
  }

  Write-Ok "Ollama installed"
  return $exe
}

function Test-LocalOllamaUrl {
  if ($Provider -ne "Ollama") {
    return $false
  }

  try {
    $uri = [Uri]$OllamaUrl
    return $uri.Host -in @("localhost", "127.0.0.1", "::1")
  } catch {
    return ($OllamaUrl -match "localhost|127\.0\.0\.1")
  }
}

function Wait-ForOllama {
  param([Parameter(Mandatory = $true)][string]$OllamaExe)

  if ($DryRun) {
    Invoke-DryRun "wait for Ollama at http://localhost:11434"
    return
  }

  for ($i = 0; $i -lt 15; $i++) {
    try {
      Invoke-RestMethod -Uri "http://localhost:11434/api/version" -TimeoutSec 2 | Out-Null
      Write-Ok "Ollama server is running"
      return
    } catch {
      if ($i -eq 0) {
        Write-Note "Starting Ollama server..."
        Start-Process -FilePath $OllamaExe -ArgumentList "serve" -WindowStyle Hidden
      }
      Start-Sleep -Seconds 2
    }
  }

  throw "The Ollama server did not start. Start the Ollama app manually and re-run this script."
}

function Install-Model {
  param([Parameter(Mandatory = $true)][string]$OllamaExe)

  Write-Step "Pulling model: $Model"
  Write-Note "This can take a long time; the default model is several gigabytes."

  if ($DryRun) {
    Invoke-DryRun "$OllamaExe pull $Model"
    return
  }

  & $OllamaExe pull $Model
  if ($LASTEXITCODE -ne 0) {
    throw "Model download failed. You can also let the mod download it on first launch."
  }

  Write-Ok "Model is ready"
}

function Escape-Toml {
  param([AllowNull()][string]$Value)

  if ($null -eq $Value) {
    return ""
  }

  return $Value.Replace('\', '\\').Replace('"', '\"').Replace("`r", '').Replace("`n", '\n')
}

function Add-TomlString {
  param(
    [Parameter(Mandatory = $true)]$Lines,
    [Parameter(Mandatory = $true)][string]$Key,
    [AllowNull()][string]$Value
  )

  $Lines.Add('{0} = "{1}"' -f $Key, (Escape-Toml $Value)) | Out-Null
}

function Write-ModConfig {
  param([Parameter(Mandatory = $true)][string]$GameDir)

  if ($SkipConfig) {
    Write-Note "Skipping config write by request."
    return
  }

  Write-Step "Writing mod configuration"

  $userDataDir = Join-Path $GameDir "UserData"
  $configPath = Join-Path $userDataDir "MdrgAiDialog.cfg"

  if ($DryRun) {
    Invoke-DryRun "create $userDataDir"
  } else {
    New-Item -ItemType Directory -Path $userDataDir -Force | Out-Null
  }

  $backupPath = Backup-File $configPath
  if ($backupPath) {
    Write-Note "Existing config backed up to $backupPath"
  }

  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add("[General]") | Out-Null
  Add-TomlString $lines "UsedProvider" $Provider
  $lines.Add("ProviderConfigured = true") | Out-Null
  if ($SystemPersona) {
    Add-TomlString $lines "SystemPersona" $SystemPersona
  }
  $lines.Add("") | Out-Null

  if ($Provider -eq "Jun") {
    $lines.Add("[Jun]") | Out-Null
    if ($ApiUrl) {
      Add-TomlString $lines "ApiUrl" $ApiUrl
    }
    Add-TomlString $lines "Email" $JunEmail
    Add-TomlString $lines "Password" $JunPassword
    $lines.Add("") | Out-Null
    $lines.Add("[Tts]") | Out-Null
    $lines.Add("Enabled = true") | Out-Null
    Add-TomlString $lines "ApiFormat" "Jun"
  } else {
    $lines.Add("[$Provider]") | Out-Null

    if ($Provider -eq "Ollama") {
      Add-TomlString $lines "ApiUrl" $OllamaUrl
    } elseif ($ApiUrl) {
      Add-TomlString $lines "ApiUrl" $ApiUrl
    }

    Add-TomlString $lines "Model" $Model
    if ($ApiKey) {
      Add-TomlString $lines "ApiKey" $ApiKey
    }
  }

  $lines.Add("") | Out-Null

  if ($DryRun) {
    Invoke-DryRun "write $configPath"
    return
  }

  Set-Content -LiteralPath $configPath -Value ($lines -join "`r`n") -Encoding UTF8
  Write-Ok "Config written: $configPath"
}

function Start-Game {
  param([Parameter(Mandatory = $true)][string]$GameDir)

  if (-not $LaunchGame) {
    return
  }

  $gameExe = Join-Path $GameDir $script:GameExeName
  Write-Step "Launching the game"

  if ($DryRun) {
    Invoke-DryRun "start $gameExe"
    return
  }

  Start-Process -FilePath $gameExe -WorkingDirectory $GameDir
  Write-Ok "Launched $script:GameExeName"
}

function Main {
  Assert-Windows
  Write-Banner

  if ($DryRun) {
    Write-Note "Dry run enabled. No files will be written and no installers will be started."
  }

  Write-Info "Provider: $Provider"
  if ($Provider -eq "Ollama") {
    Write-Info "Ollama URL: $OllamaUrl"
  }

  $gameDir = Find-GameFolder
  Install-MelonLoader $gameDir
  Install-ModDll $gameDir

  if (Test-LocalOllamaUrl) {
    if ($SkipOllamaInstall) {
      Write-Note "Skipping local Ollama install by request."
    } else {
      $ollamaExe = Install-Ollama
      if ($ollamaExe) {
        Wait-ForOllama $ollamaExe
        if (-not $SkipModelPull) {
          Install-Model $ollamaExe
        }
      } elseif (-not $SkipModelPull) {
        Write-Note "Skipping model pull because Ollama is not installed."
      }
    }
  } elseif ($Provider -eq "Ollama") {
    Write-Note "Using remote Ollama endpoint: $OllamaUrl"
    Write-Note "Make sure that server has the model '$Model' pulled."
  }

  Write-ModConfig $gameDir
  Start-Game $gameDir

  $elapsed = [int]((Get-Date) - $script:StartedAt).TotalSeconds
  Write-Host ""
  Write-InstallerLine "============================================================" Green
  Write-InstallerLine " Install complete in ${elapsed}s" Green
  Write-InstallerLine " Start the game and use the Talk (AI) button to chat with Jun." Green
  Write-InstallerLine "============================================================" Green
}

try {
  Main
} catch {
  Write-Host ""
  Write-Fail $_.Exception.Message
  exit 1
} finally {
  Remove-InstallerTemps
}
