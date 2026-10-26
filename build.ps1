param(
    [string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\Risk of Rain 2',
    [string]$ModProfileDirectory = "$env:APPDATA\r2modmanPlus-local\RiskOfRain2\profiles\ModDev",
    [switch]$SkipPackage
)
$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Install the .NET SDK through Visual Studio (.NET desktop development).' }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '..\.dotnet'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '..\.nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'

& $dotnet build (Join-Path $PSScriptRoot 'src\TaylorsBalancePatch.csproj') -c Release --nologo "-p:GameDirectory=$GameDirectory" "-p:ModProfileDirectory=$ModProfileDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
& $dotnet run --project (Join-Path $PSScriptRoot 'validation\Validation.csproj') -c Release "-p:GameDirectory=$GameDirectory" "-p:ModProfileDirectory=$ModProfileDirectory" -- (Join-Path $GameDirectory 'Risk of Rain 2_Data\Managed\RoR2.dll') (Join-Path $PSScriptRoot 'src\bin\Release\netstandard2.1\TaylorsBalancePatch.dll')
if ($LASTEXITCODE -ne 0) { throw 'Offline validation failed.' }

if ($SkipPackage) {
    Write-Host 'Release build and offline validation passed; packaging skipped.'
    return
}

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$packageDirectory = Join-Path $PSScriptRoot "dist\package-$($manifest.version_number)"
$packageRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist'))
$packageFullPath = [System.IO.Path]::GetFullPath($packageDirectory)
if (-not $packageFullPath.StartsWith($packageRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package directory must remain inside dist.'
}
if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
$pluginDirectory = Join-Path $packageDirectory 'plugins'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
foreach ($file in @('TaylorsBalancePatch.dll', 'TaylorsBalancePatch.pdb')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "src\bin\Release\netstandard2.1\$file") -Destination $pluginDirectory -Force
}
foreach ($file in @('README.md','manifest.json','icon.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $packageDirectory -Force
}
$archive = Join-Path $PSScriptRoot "dist\T4ylor-T4ylorSurvivors-$($manifest.version_number).zip"
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archive -Force
Write-Host "Built and checked: $archive"
Write-Host 'This script packages the mod; it does not install it or launch the game.'
