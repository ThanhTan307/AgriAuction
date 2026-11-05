$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$local = Join-Path $root '.local\product'
$databaseFile = Join-Path $local 'database.json'
$databaseSecretFile = Join-Path $local 'database-secret.json'
$authFile = Join-Path $local 'auth.json'
$seedFile = Join-Path $local 'seed.json'
$apiUrl = 'http://127.0.0.1:5084'

$database = Get-Content -LiteralPath $databaseFile -Raw | ConvertFrom-Json
$databaseSecret = Get-Content -LiteralPath $databaseSecretFile -Raw | ConvertFrom-Json
$auth = Get-Content -LiteralPath $authFile -Raw | ConvertFrom-Json
$seed = Get-Content -LiteralPath $seedFile -Raw | ConvertFrom-Json

$runtimeConnection = $database.connectionString -replace ';[^;]*$', ''
$runtimeConnection = $runtimeConnection + ';' + ('Password=' + $databaseSecret.password)
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__AgriAuction = $runtimeConnection
$env:Database__Schema = $database.schema
$env:Auth__SigningKey = $auth.signingKey
$env:Auth__LifetimeMinutes = '240'
$env:Auth__Issuer = 'AgriAuction'
$env:Auth__Audience = 'AgriAuction.Clients'
$env:Product__Enabled = 'true'
$seed.psobject.Properties | ForEach-Object {
    Set-Item "Env:ProductSeed__$($_.Name)__Email" $_.Value.email
    Set-Item "Env:ProductSeed__$($_.Name)__Password" $_.Value.password
}

$proc = Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project','src\AgriAuction.Api','--no-launch-profile','--','--urls',$apiUrl) -WorkingDirectory $root -WindowStyle Hidden -PassThru
[IO.File]::WriteAllText((Join-Path $local 'backend.pid'), "$($proc.Id)`n", (New-Object Text.UTF8Encoding($false)))

for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if (Test-NetConnection -ComputerName '127.0.0.1' -Port 5084 -InformationLevel Quiet -WarningAction SilentlyContinue) {
        Write-Host "AgriAuction backend started successfully on $apiUrl with PID $($proc.Id)"
        exit 0
    }
}
Write-Error "Timed out waiting for $apiUrl"
