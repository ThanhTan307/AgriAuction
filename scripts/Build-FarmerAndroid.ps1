param([Alias('Profile')][ValidateSet('Emulator','Usb','Lan')][string]$DeviceProfile='Usb',[string]$ApiUrl,[string]$Flutter='flutter.bat')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$portable=Join-Path $root '.local/tools/mingit-2.56.0.4/cmd'
if(Test-Path -LiteralPath $portable){$env:PATH=$portable+';'+$env:PATH}
$output=Join-Path $root '.local/product/apk'
New-Item -ItemType Directory -Force -Path $output|Out-Null
$target=$DeviceProfile.ToLowerInvariant()
if($DeviceProfile -eq 'Emulator'){$expected='http://10.0.2.2:5084'}
elseif($DeviceProfile -eq 'Usb'){$expected='http://127.0.0.1:5084'}
else {
    $uri=$null
    if(![Uri]::TryCreate($ApiUrl,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -notin @('http','https') -or $uri.Host -in @('10.0.2.2','127.0.0.1','localhost','::1') -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/'){throw 'LAN requires an explicit reachable HTTP(S) origin without credentials/path/query.'}
    $expected=$ApiUrl.TrimEnd('/')
}
if($DeviceProfile -ne 'Lan' -and $ApiUrl -and $ApiUrl.TrimEnd('/') -ne $expected){throw 'Use the matching profile or Lan for a custom server.'}
Push-Location (Join-Path $root 'farmer_mobile')
try {
    $flutterCommand=(Get-Command $Flutter -ErrorAction Stop).Source
    $started=[DateTime]::UtcNow
    $process=Start-Process -FilePath $flutterCommand -ArgumentList @('build','apk','--debug','--no-pub',"--dart-define=ANDROID_TARGET=$target","--dart-define=API_BASE_URL=$expected") -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -RedirectStandardOutput (Join-Path $output "$target-build.stdout.log") -RedirectStandardError (Join-Path $output "$target-build.stderr.log") -PassThru
    $processHandle=$process.Handle
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw 'Flutter debug build failed; see per-profile build logs.'}
    if((Get-Item -LiteralPath 'build/app/outputs/flutter-apk/app-debug.apk').LastWriteTimeUtc -lt $started){throw 'Stale APK rejected'}
    $apk=Join-Path $output "agriauction-farmer-$target-debug.apk"
    Copy-Item -LiteralPath 'build/app/outputs/flutter-apk/app-debug.apk' -Destination $apk
    $proof=[ordered]@{profile=$target;apiBaseUrl=$expected;debug=$true;apk=$apk;sha256=(Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $apk).Length;builtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
    [IO.File]::WriteAllText(($apk+'.json'),($proof|ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
    Write-Output "Debug APK: $apk; API: $expected"
} finally {Pop-Location}
