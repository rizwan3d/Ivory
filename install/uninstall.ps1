$ErrorActionPreference = "Stop"

$installDir = Join-Path $env:LOCALAPPDATA "Tusk\bin"
$binary = Join-Path $installDir "tusk.exe"

Write-Host "Removing Tusk binary from $installDir..."
Remove-Item -ErrorAction SilentlyContinue -Force $binary

if (Test-Path $installDir) {
    $entries = Get-ChildItem -Path $installDir
    if ($entries.Count -eq 0) {
        Remove-Item -Force -Recurse $installDir
    }
}

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (-not $userPath) { $userPath = "" }
$parts = $userPath -split ';' | Where-Object { $_ -and ($_ -ne $installDir) }
$updated = ($parts -join ';')
[Environment]::SetEnvironmentVariable("Path", $updated, "User")

Write-Host "Removed $installDir from user PATH."
Write-Host "Tusk uninstalled. Open a new terminal to ensure PATH updates take effect."
