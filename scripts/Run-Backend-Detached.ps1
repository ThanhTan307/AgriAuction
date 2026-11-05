$ErrorActionPreference = 'Stop'
$root = 'C:\Users\Admin\Desktop\AgriAuction'
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

Set-Location $root
$stdout = Join-Path $local 'backend.stdout.log'
$stderr = Join-Path $local 'backend.stderr.log'

$dll = Join-Path $root 'src\AgriAuction.Api\bin\Debug\net10.0\AgriAuction.Api.dll'
dotnet $dll --contentRoot (Join-Path $root 'src\AgriAuction.Api') --urls $apiUrl > $stdout 2> $stderr

