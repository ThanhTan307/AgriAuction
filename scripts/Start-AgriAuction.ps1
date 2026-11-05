param([switch]$FarmerMobile)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$local = Join-Path $root '.local\product'
$databaseFile = Join-Path $local 'database.json'
$databaseSecretFile = Join-Path $local 'database-secret.json'
$authFile = Join-Path $local 'auth.json'
$seedFile = Join-Path $local 'seed.json'
$pidFile = Join-Path $local 'backend.pid'
$stdoutFile = Join-Path $local 'backend.stdout.log'
$stderrFile = Join-Path $local 'backend.stderr.log'
$apiUrl = 'http://127.0.0.1:5084'

if (!(Test-Path -LiteralPath $databaseFile) -or !(Test-Path -LiteralPath $authFile) -or !(Test-Path -LiteralPath $seedFile)) {
    throw 'AgriAuction is not initialized. Run scripts\Initialize-AgriAuction.ps1 once.'
}
$database = Get-Content -LiteralPath $databaseFile -Raw | ConvertFrom-Json
$databaseSecret = if (Test-Path -LiteralPath $databaseSecretFile) {
    Get-Content -LiteralPath $databaseSecretFile -Raw | ConvertFrom-Json
} else {
    throw 'Database secret is missing. Run scripts\Initialize-AgriAuction.ps1 again to repair the local database configuration.'
}
$auth = Get-Content -LiteralPath $authFile -Raw | ConvertFrom-Json
$seed = Get-Content -LiteralPath $seedFile -Raw | ConvertFrom-Json
if ($database.apiUrl -ne $apiUrl -or $database.database -ne 'agriauction_system' -or $database.schema -ne 'agriauction_app') {
    throw 'Local product configuration does not match agriauction_system / 5084.'
}

function Test-Port([int]$port) {
    return (Test-NetConnection -ComputerName '127.0.0.1' -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue)
}
function Wait-Port([int]$port) {
    for ($i = 0; $i -lt 45; $i++) {
        if (Test-Port $port) { return }
        Start-Sleep -Seconds 1
    }
    throw "Timed out waiting for 127.0.0.1:$port."
}

$configuredPostgresPort = if ($database.port) {
    [int]$database.port
} elseif ($database.connectionString -match 'Port=(\d+)') {
    [int]$Matches[1]
} else {
    5432
}
$postgresPort = $configuredPostgresPort
if (!(Test-Port $postgresPort) -and $configuredPostgresPort -eq 55432 -and (Test-Port 5432)) {
    $postgresPort = 5432
    $database.connectionString = $database.connectionString -replace 'Port=\d+', "Port=$postgresPort"
    $database | Add-Member -NotePropertyName port -NotePropertyValue $postgresPort -Force
    [IO.File]::WriteAllText($databaseFile, ($database | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Write-Host 'Using PostgreSQL on 127.0.0.1:5432 (updated legacy 55432 configuration).' -ForegroundColor Yellow
}
if (!(Test-Port $postgresPort)) { throw "PostgreSQL must already be running on 127.0.0.1:$postgresPort." }
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$runtimeConnection = $database.connectionString -replace ';[^;]*$', ''
$runtimeConnection = $runtimeConnection + ';' + ('Password=' + $databaseSecret.password)
$env:ConnectionStrings__AgriAuction = $runtimeConnection
$env:Database__Schema = $database.schema
$env:Auth__SigningKey = $auth.signingKey
$env:Product__Enabled = 'true'
$seed.psobject.Properties | ForEach-Object {
    Set-Item "Env:ProductSeed__$($_.Name)__Email" $_.Value.email
    Set-Item "Env:ProductSeed__$($_.Name)__Password" $_.Value.password
}

$status = [ordered]@{ PostgreSQL = 'OK'; Backend = ''; Web = ''; Android = 'SKIPPED' }
if (Test-Port 5084) {
    $status.Backend = 'OK (already running)'
} else {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project','src\AgriAuction.Api','--no-launch-profile','--','--urls',$apiUrl) `
        -WorkingDirectory $root -WindowStyle Minimized -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile -PassThru
    [IO.File]::WriteAllText($pidFile, "$($process.Id)`n", (New-Object Text.UTF8Encoding($false)))
    Wait-Port 5084
    $status.Backend = 'OK (started)'
}
try {
    Start-Process -FilePath 'explorer.exe' -ArgumentList $apiUrl -ErrorAction Stop
    $status.Web = 'OK (browser opened)'
}
catch {
    $status.Web = "SKIPPED (open $apiUrl manually)"
    Write-Warning "Could not open the browser automatically: $($_.Exception.Message)"
}

if ($FarmerMobile) {
    & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'Start-FarmerMobile.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Farmer Mobile startup failed.' }
    $status.Android = 'OK (emulator and APK started)'
} else {
    $sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { Join-Path $env:LOCALAPPDATA 'Android\sdk' }
    $adb = Join-Path $sdk 'platform-tools\adb.exe'
    $apk = Join-Path $root 'farmer_mobile\build\app\outputs\flutter-apk\app-debug.apk'
    if ((Test-Path -LiteralPath $adb) -and (Test-Path -LiteralPath $apk)) {
        $device = (& $adb devices | Select-String '^emulator-\d+\s+device$' | Select-Object -First 1)
        if ($device) {
            $serial = $device.ToString().Split("`t")[0]
            & $adb -s $serial install -r $apk | Out-Null
            & $adb -s $serial shell monkey -p com.agriauction.farmer_mobile 1 | Out-Null
            $status.Android = 'OK (APK opened)'
        } else { $status.Android = 'SKIPPED (no online emulator)' }
    } else { $status.Android = 'SKIPPED (SDK or APK unavailable)' }
}

Write-Host ''
Write-Host 'AgriAuction startup status' -ForegroundColor Cyan
$status.GetEnumerator() | ForEach-Object { Write-Host ('{0}: {1}' -f $_.Key, $_.Value) }
Write-Host 'PostgreSQL is shared and is not stopped by this script.'
