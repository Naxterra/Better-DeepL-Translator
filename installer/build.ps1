# Publishes the app and builds its installer into artifacts\installer.
# Inno Setup 7 is taken from its per-user install location unless another ISCC.exe is passed.
param(
    [string]$Iscc = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'DeepLTranslator.csproj'
$publish = Join-Path $root 'artifacts\publish'

if (-not (Test-Path $Iscc)) {
    throw "Inno Setup 7 was not found at $Iscc. Install it with: winget install JRSoftware.InnoSetup.7"
}

$version = (dotnet msbuild $project -getProperty:Version).Trim()
if ($LASTEXITCODE -or -not $version) { throw 'Could not read the app version from the project.' }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --no-self-contained -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed.' }

& $Iscc "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'BetterDeepLTranslator.iss')
if ($LASTEXITCODE) { throw 'The installer build failed.' }
