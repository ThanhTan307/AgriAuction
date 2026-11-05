$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pidFile = Join-Path $root '.local\product\backend.pid'
if (!(Test-Path -LiteralPath $pidFile)) {
    Write-Output 'No AgriAuction backend PID file was found.'
    exit 0
}
$backendPid = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
$process = Get-Process -Id $backendPid -ErrorAction SilentlyContinue
if ($process) {
    Stop-Process -Id $backendPid
    Write-Output "Stopped AgriAuction backend process $backendPid."
} else {
    Write-Output "AgriAuction backend process $backendPid is not running."
}
Remove-Item -LiteralPath $pidFile -Force
Write-Output 'PostgreSQL was not stopped.'
