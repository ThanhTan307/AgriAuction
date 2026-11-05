param(
    [string]$AvdName,
    [switch]$Build,
    [string]$Flutter = 'flutter.bat'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$local = Join-Path $root '.local\product'
$sdk = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } elseif ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\sdk' }
$emulator = Join-Path $sdk 'emulator\emulator.exe'
$adb = Join-Path $sdk 'platform-tools\adb.exe'
$apk = Join-Path $local 'apk\agriauction-farmer-emulator-debug.apk'
$stdoutLog = Join-Path $local 'farmer-emulator.stdout.log'
$stderrLog = Join-Path $local 'farmer-emulator.stderr.log'
$apiUrl = 'http://10.0.2.2:5084'

if (!(Test-Path -LiteralPath $emulator)) { throw "Android emulator was not found at $emulator." }
if (!(Test-Path -LiteralPath $adb)) { throw "ADB was not found at $adb." }
if (!(Test-NetConnection -ComputerName '127.0.0.1' -Port 5084 -InformationLevel Quiet -WarningAction SilentlyContinue)) {
    throw 'AgriAuction Backend is not reachable on 127.0.0.1:5084. Start scripts\Start-AgriAuction.ps1 first.'
}
New-Item -ItemType Directory -Force -Path $local | Out-Null

$avds = @(& $emulator -list-avds | Where-Object { $_ -and $_.Trim() })
if (!$avds) { throw 'No Android AVD was found. Create an AVD in Android SDK tools before running Farmer Mobile.' }
if (!$AvdName) {
    $AvdName = if ($avds -contains 'AgriCompletion') { 'AgriCompletion' } elseif ($avds.Count -eq 1) { $avds[0] } elseif ($avds -contains 'Medium_Phone_API_36.1') { 'Medium_Phone_API_36.1' } else { $avds[0] }
}
if (!($avds -contains $AvdName)) { throw "AVD '$AvdName' was not found. Available AVDs: $($avds -join ', ')." }

function Get-Devices {
    @(& $adb devices | Select-String '^(emulator-\d+)\s+(device|offline|unauthorized)$' | ForEach-Object {
        $parts = $_.ToString() -split '\s+'
        [pscustomobject]@{ Serial = $parts[0]; State = $parts[1] }
    })
}
function Wait-Device {
    param([int]$Seconds = 120)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $device = Get-Devices | Where-Object State -eq 'device' | Select-Object -First 1
        if ($device) { return $device }
        if ($process -and $process.HasExited) {
            throw "Android Emulator exited with code $($process.ExitCode). Check $stdoutLog and $stderrLog."
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "No online Android Emulator appeared. Check $stdoutLog and $stderrLog, then run '$adb devices'."
}
function Wait-AndroidReady {
    param([string]$Serial, [int]$Seconds = 120)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $boot = (& $adb -s $Serial shell getprop sys.boot_completed 2>$null).Trim()
        if ($boot -eq '1') { return }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "Android Emulator $Serial is online but has not completed boot. Check the emulator window and retry."
}

$device = Get-Devices | Where-Object State -eq 'device' | Select-Object -First 1
if (!$device) {
    $args = @('-avd', $AvdName, '-gpu', 'auto', '-no-snapshot-load', '-no-boot-anim')
    $process = Start-Process -FilePath $emulator -ArgumentList $args -WindowStyle Normal -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    $device = Wait-Device
    Write-Output "Started AVD $AvdName ($($device.Serial))."
} else {
    Write-Output "Using existing emulator $($device.Serial)."
}
Wait-AndroidReady $device.Serial

if ($Build -or !(Test-Path -LiteralPath $apk)) {
    $buildScript = Join-Path $PSScriptRoot 'Build-FarmerAndroid.ps1'
    & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass -File $buildScript -Profile Emulator -Flutter $Flutter
    if ($LASTEXITCODE -ne 0) { throw 'Farmer APK build failed.' }
}
if (!(Test-Path -LiteralPath $apk)) { throw "Farmer APK was not found at $apk." }

& $adb -s $device.Serial install -r $apk
if ($LASTEXITCODE -ne 0) { throw 'Farmer APK installation failed.' }
& $adb -s $device.Serial shell monkey -p com.agriauction.farmer_mobile 1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not open Farmer Mobile.' }
Write-Output "Farmer Mobile started on $($device.Serial); API: $apiUrl"
