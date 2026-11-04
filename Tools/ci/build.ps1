# Builds the Windows playtest and zips it.
# Usage: powershell -ExecutionPolicy Bypass -File Tools/ci/build.ps1 [-ZipPath Builds/FriendlyPersonShooter-Playtest.zip]
# Exits non-zero if the build fails. The Unity Editor must NOT be open on this same folder.
param(
    [string]$ZipPath = "Builds/FriendlyPersonShooter-Playtest.zip"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $root

$outDir = "Builds/Playtest"
$exe = Join-Path $outDir "FriendlyPersonShooter.exe"
$dll = Join-Path $outDir "FriendlyPersonShooter_Data/Managed/FriendlyPersonShooter.Arena.dll"
$started = Get-Date

# Start clean, so a stale build can never pass as a new one.
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }

$log = "Logs/ci-build.log"
unity build . --target StandaloneWindows64 `
    --execute-method FriendlyPersonShooter.EditorTools.PlaytestBuild.BuildWindowsCI `
    --log-file $log --non-interactive --no-banner
$code = $LASTEXITCODE

# Undo files the build touches (see the headless-build notes in CLAUDE.md).
git checkout -- ProjectSettings/ProjectSettings.asset 2>$null
Remove-Item -Recurse -Force "Assets/netcode-build-assets-temp*" -ErrorAction SilentlyContinue

if ($code -ne 0) {
    Write-Host "::error::Unity build failed (exit $code). Last log lines:"
    if (Test-Path $log) { Get-Content $log -Tail 60 }
    exit 1
}
if (-not (Test-Path $exe) -or -not (Test-Path $dll) -or (Get-Item $dll).LastWriteTime -lt $started) {
    Write-Host "::error::Build reported success but the output is missing or stale."
    exit 1
}

New-Item -ItemType Directory -Force (Split-Path $ZipPath) | Out-Null
$items = Get-ChildItem $outDir | Where-Object { $_.Name -notlike "*_BackUpThisFolder_ButDontShipItWithYourGame" }
Compress-Archive -Path $items.FullName -DestinationPath $ZipPath
$mb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "Build OK: $ZipPath ($mb MB)"
