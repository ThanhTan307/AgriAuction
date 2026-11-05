param(
    [string]$PostgresAdminUser = 'postgres',
    [string]$PostgresAdminPassword = $env:AGRIAUCTION_POSTGRES_ADMIN_PASSWORD,
    [int]$PostgresPort = 5432
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$local = Join-Path $root '.local\product'
$databaseSecretFile = Join-Path $local 'database-secret.json'
$pgBin = $null
$dbName = 'agriauction_system'
$dbRole = 'agriauction_system'
$schema = 'agriauction_app'
$apiUrl = 'http://127.0.0.1:5084'

if ([string]::IsNullOrWhiteSpace($PostgresAdminPassword)) {
    throw 'Set AGRIAUCTION_POSTGRES_ADMIN_PASSWORD for the local PostgreSQL administrator.'
}
$psqlCommand = Get-Command 'psql.exe' -ErrorAction SilentlyContinue
$pgBin = if ($psqlCommand) { Split-Path -Parent $psqlCommand.Source } else { $null }
if (!$pgBin) {
    $postgresRoot = Join-Path $env:ProgramFiles 'PostgreSQL'
    if (Test-Path -LiteralPath $postgresRoot) {
        $pgBin = Get-ChildItem -LiteralPath $postgresRoot -Directory |
            Where-Object { $_.Name -match '^\d+(?:\.\d+)?$' } |
            Sort-Object { [version](($_.Name) -replace '^(\d+)$', '$1.0') } -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin' } |
            Where-Object { Test-Path -LiteralPath (Join-Path $_ 'psql.exe') } |
            Select-Object -First 1
    }
}
if (!$pgBin -or !(Test-Path -LiteralPath (Join-Path $pgBin 'psql.exe')) -or !(Test-Path -LiteralPath (Join-Path $pgBin 'pg_isready.exe'))) {
    throw 'PostgreSQL 18 Command Line Tools were not found. Add psql.exe and pg_isready.exe to PATH or install PostgreSQL under Program Files.'
}

New-Item -ItemType Directory -Force -Path $local | Out-Null
& (Join-Path $pgBin 'pg_isready.exe') -h 127.0.0.1 -p $PostgresPort | Out-Null
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL is not accepting connections on 127.0.0.1:$PostgresPort. Start PostgreSQL or pass the correct port with -PostgresPort." }

function New-Secret {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 32
    $rng.GetBytes($bytes)
    $rng.Dispose()
    return [Convert]::ToBase64String($bytes)
}

function Invoke-Psql([string]$database, [string]$sql, [string]$user = $PostgresAdminUser, [string]$password = $PostgresAdminPassword) {
    $old = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $password
        $output = $sql | & (Join-Path $pgBin 'psql.exe') -X -qAt -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $PostgresPort -U $user -d $database -w -f - 2>&1
        if ($LASTEXITCODE -ne 0) { throw (($output -join "`n").Trim()) }
        return ($output -join "`n").Trim()
    }
    finally { $env:PGPASSWORD = $old }
}

$appPassword = New-Secret
$existingSeedPath = Join-Path $local 'seed.json'
if (Test-Path -LiteralPath $existingSeedPath) {
    $existingSeed = Get-Content -LiteralPath $existingSeedPath -Raw | ConvertFrom-Json
    $accountPasswords = @{
        Administrator = $existingSeed.Administrator.password
        Farmer = $existingSeed.Farmer.password
        Enterprise = $existingSeed.Enterprise.password
        Enterprise2 = $existingSeed.Enterprise2.password
    }
} else {
    $accountPasswords = @{
        Administrator = New-Secret
        Farmer = New-Secret
        Enterprise = New-Secret
        Enterprise2 = New-Secret
    }
}

$roleExists = Invoke-Psql 'postgres' "SELECT 1 FROM pg_roles WHERE rolname='$dbRole';"
if (!$roleExists) {
    Invoke-Psql 'postgres' "CREATE ROLE `"$dbRole`" LOGIN PASSWORD '$($appPassword.Replace("'","''"))';"
} else {
    Invoke-Psql 'postgres' "ALTER ROLE `"$dbRole`" WITH LOGIN PASSWORD '$($appPassword.Replace("'","''"))';"
}
$databaseExists = Invoke-Psql 'postgres' "SELECT 1 FROM pg_database WHERE datname='$dbName';"
if (!$databaseExists) {
    Invoke-Psql 'postgres' "CREATE DATABASE `"$dbName`" OWNER `"$dbRole`";"
    Write-Output "Created database $dbName."
} else {
    Write-Output "Database $dbName already exists; existing data is preserved."
}

$connection = "Host=127.0.0.1;Port=55432;Database=$dbName;Username=$dbRole;Password=$appPassword"
$databaseConfig = [ordered]@{
    connectionString = $connection
    database = $dbName
    role = $dbRole
    schema = $schema
    apiUrl = $apiUrl
}
$connection = $connection.Replace('Port=55432', "Port=$PostgresPort")
$databaseConfig.connectionString = $connection
$databaseConfig.host = '127.0.0.1'
$databaseConfig.port = $PostgresPort
$authConfig = @{ signingKey = New-Secret }
$seedConfig = @{
    Administrator = @{ email = 'admin@agriauction.local'; password = $accountPasswords.Administrator }
    Farmer = @{ email = 'farmer@agriauction.local'; password = $accountPasswords.Farmer }
    Enterprise = @{ email = 'enterprise@agriauction.local'; password = $accountPasswords.Enterprise }
    Enterprise2 = @{ email = 'enterprise2@agriauction.local'; password = $accountPasswords.Enterprise2 }
}
[IO.File]::WriteAllText((Join-Path $local 'database.json'), ($databaseConfig | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText($databaseSecretFile, (@{ password = $appPassword } | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $local 'auth.json'), ($authConfig | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $local 'seed.json'), ($seedConfig | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$runtimeConnection = $connection + ';' + ('Password=' + $appPassword)
$env:ConnectionStrings__AgriAuction = $runtimeConnection
$env:Database__Schema = $schema
$env:Auth__SigningKey = $authConfig.signingKey
$env:Product__Enabled = 'true'
$seedConfig.GetEnumerator() | ForEach-Object {
    Set-Item "Env:ProductSeed__$($_.Key)__Email" $_.Value.email
    Set-Item "Env:ProductSeed__$($_.Key)__Password" $_.Value.password
}

Push-Location $root
try {
    & dotnet run --project 'src\AgriAuction.Api' --no-launch-profile -- --urls $apiUrl --migrate --bootstrap-product
    if ($LASTEXITCODE -ne 0) { throw 'Migration or product bootstrap failed.' }
}
finally { Pop-Location }

Write-Output "AgriAuction database initialized: $dbName on 127.0.0.1:$PostgresPort."
Write-Output "Credentials are stored only under $local."
