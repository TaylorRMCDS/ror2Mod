param(
    [string]$Type = '*',
    [string]$Method = '*',
    [switch]$Members,
    [switch]$IL,
    [string]$AssemblyPath,
    [string]$GameDirectory,
    [string]$ModProfileDirectory
)

# Read-only assembly inspection; never loads the game assembly for execution.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
if (-not $GameDirectory) {
    $GameDirectory = $props.SelectSingleNode('/Project/PropertyGroup/GameDirectory').InnerText
}
if (-not $ModProfileDirectory) {
    $ModProfileDirectory = $props.SelectSingleNode('/Project/PropertyGroup/ModProfileDirectory').InnerText.Replace('$(APPDATA)', $env:APPDATA)
}
if (-not $AssemblyPath) {
    $AssemblyPath = Join-Path $GameDirectory 'Risk of Rain 2_Data\Managed\RoR2.dll'
}
$cecilPath = Join-Path $ModProfileDirectory 'BepInEx\core\Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecilPath)) { throw "Mono.Cecil not found: $cecilPath" }
if (-not (Test-Path -LiteralPath $AssemblyPath)) { throw "Assembly not found: $AssemblyPath" }
Add-Type -Path $cecilPath

function Get-AllTypes($types) {
    foreach ($entry in $types) {
        $entry
        if ($entry.HasNestedTypes) { Get-AllTypes $entry.NestedTypes }
    }
}

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($AssemblyPath)
try {
    $matches = @(Get-AllTypes $assembly.MainModule.Types | Where-Object { $_.FullName -like $Type })
    if ($matches.Count -eq 0) { throw "No types matched '$Type'." }
    foreach ($entry in $matches) {
        Write-Output "TYPE $($entry.FullName) : $($entry.BaseType)"
        if ($Members) {
            foreach ($field in $entry.Fields) { Write-Output "  FIELD $field" }
        }
        if ($Members -or $IL) {
            foreach ($methodEntry in $entry.Methods | Where-Object { $_.Name -like $Method }) {
                Write-Output "  METHOD $methodEntry"
                if ($IL -and $methodEntry.HasBody) {
                    foreach ($instruction in $methodEntry.Body.Instructions) {
                        Write-Output "    $instruction"
                    }
                }
            }
        }
    }
}
finally { $assembly.Dispose() }
