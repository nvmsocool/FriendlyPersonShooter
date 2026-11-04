# Runs the fast engine-free test suite (Assets/Scripts/Core compiled outside Unity).
# Usage: powershell -ExecutionPolicy Bypass -File Tools/ci/test.ps1
$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path (Join-Path $PSScriptRoot "../.."))
dotnet test Tests/Core.Tests --nologo --verbosity quiet
exit $LASTEXITCODE
