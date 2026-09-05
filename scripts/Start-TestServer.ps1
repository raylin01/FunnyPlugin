param([string]$ServerRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.test-server'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$ServerRoot = [IO.Path]::GetFullPath($ServerRoot)
$game = Join-Path $ServerRoot 'game'
$exe = Join-Path $game 'bin/win64/cs2.exe'
if (!(Test-Path $exe)) { throw "CS2 server missing: $exe" }
if (Get-Process cs2 -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
    throw 'The test server is already running. Stop it before deploying.'
}
$plugin = Join-Path $game 'csgo/addons/counterstrikesharp/plugins/Funnies'
New-Item -ItemType Directory -Force $plugin | Out-Null
Copy-Item "$repo/bin/Release/net10.0/Funnies.*" $plugin -Force
$info = Join-Path $game 'csgo/gameinfo.gi'
$contents = Get-Content $info -Raw
if ($contents -notmatch 'Game\s+csgo/addons/metamod') {
    if (!(Test-Path "$info.original")) { Copy-Item $info "$info.original" }
    $contents = $contents -replace '(Game_LowViolence[^\r\n]*)', "`$1`r`n`t`t`tGame`tcsgo/addons/metamod"
    Set-Content $info $contents
}
Copy-Item "$PSScriptRoot/funnies-test.cfg" "$game/csgo/cfg/funnies-test.cfg" -Force
Copy-Item "$PSScriptRoot/funnies-test.cfg" "$game/csgo/cfg/gamemode_competitive_server.cfg" -Force
$process = Start-Process $exe -WorkingDirectory $game -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $ServerRoot 'stdout.log') -RedirectStandardError (Join-Path $ServerRoot 'stderr.log') -ArgumentList '-dedicated -insecure -console -usercon -condebug -ip 127.0.0.1 -port 27016 +sv_lan 1 +sv_hibernate_when_empty 0 +game_type 0 +game_mode 1 +map de_dust2 +exec funnies-test.cfg'
$process.Id | Set-Content (Join-Path $ServerRoot 'server.pid')
Write-Output "Test server PID $($process.Id); client console: connect 127.0.0.1:27016"
