$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$toolsRoot=Join-Path $projectRoot '.local/tools'
$archive=Join-Path $toolsRoot 'MinGit-2.56.0.4-64-bit.zip'
$destination=Join-Path $toolsRoot 'mingit-2.56.0.4'
New-Item -ItemType Directory -Force -Path $toolsRoot|Out-Null
if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.4/MinGit-2.56.0.4-64-bit.zip' -OutFile $archive -TimeoutSec 120}
if((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ne 'E3BF78351FE777FBA47DDEE5950082F8A98BB0F02604FDC7BA3EE2CB68FFA877'){throw 'Portable Git checksum mismatch'}
if(!(Test-Path -LiteralPath $destination)){Expand-Archive -LiteralPath $archive -DestinationPath $destination}
& (Join-Path $destination 'cmd/git.exe') --version
if($LASTEXITCODE -ne 0){throw 'Portable Git validation failed'}
Write-Output 'Portable Git ready in .local/tools. No system PATH or installed software changed.'
