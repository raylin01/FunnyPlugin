$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $repo '.build-home'
$env:NUGET_PACKAGES = Join-Path $repo '.nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$localSdk = Join-Path (Split-Path $repo -Parent) '.dotnet/dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }
& $dotnet restore "$repo/tests/Regression/Regression.csproj" --configfile "$repo/NuGet.Config"
if ($LASTEXITCODE) { throw 'Test restore failed' }
& $dotnet run --project "$repo/tests/Regression/Regression.csproj" --no-restore
if ($LASTEXITCODE) { throw 'Regression checks failed' }
