param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $repo '.build-home'
$env:NUGET_PACKAGES = Join-Path $repo '.nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$localSdk = Join-Path (Split-Path $repo -Parent) '.dotnet/dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }
& $dotnet restore "$repo/Funnies.csproj" --configfile "$repo/NuGet.Config"
if ($LASTEXITCODE) { throw 'Restore failed' }
& $dotnet build "$repo/Funnies.csproj" -c $Configuration --no-restore
if ($LASTEXITCODE) { throw 'Build failed' }
