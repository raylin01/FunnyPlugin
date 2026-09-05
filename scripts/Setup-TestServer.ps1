param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive',
    [string]$ServerRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.test-server')
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$destination = [IO.Path]::GetFullPath($ServerRoot).TrimEnd('\')
if ($destination -eq $source -or $destination.StartsWith($source+'\', [StringComparison]::OrdinalIgnoreCase) -or $source.StartsWith($destination+'\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The test server must be separate from the Steam game installation.'
}
if (!(Test-Path "$source/game/bin/win64/cs2.exe")) { throw 'CS2 installation not found.' }
if (Get-Process cs2 -ErrorAction SilentlyContinue | Where-Object Path -eq "$destination\game\bin\win64\cs2.exe") {
    throw 'Stop the test server before updating its frameworks.'
}
if (!(Test-Path "$destination/game/bin/win64/cs2.exe")) {
    # The independent copy keeps Steam updates and game settings out of the test server.
    robocopy "$source/game" "$destination/game" /E /R:1 /W:1 /XF *.dem *.mdmp *.log /NFL /NDL /NP
    if ($LASTEXITCODE -ge 8) { throw "Server copy failed: $LASTEXITCODE" }
}
$downloads = Join-Path $destination 'downloads'
New-Item -ItemType Directory -Force $downloads | Out-Null
$packages = @(
    @{ Name='metamod'; Url='https://github.com/alliedmodders/metamod-source/releases/download/2.0.0.1411/mmsource-2.0.0-git1411-windows.zip'; Sha='D29A4AA23144B2A073A09F05EFD1B6B1613F4A284EF76F7BE7E28FEAE0F1C07E' },
    @{ Name='counterstrikesharp'; Url='https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v1.0.373/counterstrikesharp-with-runtime-windows-1.0.373.zip'; Sha='16D1DB8E48CCEA5EF3F030A1841575D6C006345C6920E4F6691135EF8907E695' }
)
foreach ($package in $packages) {
    $archive = Join-Path $downloads ($package.Name+'.zip')
    if (!(Test-Path $archive)) { Invoke-WebRequest $package.Url -OutFile $archive }
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $package.Sha) { throw "Hash mismatch: $archive" }
    Expand-Archive $archive "$destination/game/csgo" -Force
}
Write-Output 'Server frameworks installed. Run Build.ps1, then Start-TestServer.ps1.'
