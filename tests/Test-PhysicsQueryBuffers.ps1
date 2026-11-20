[CmdletBinding()]
param(
    [string]$GameDir = $env:ER2_GAME_DIR,
    [string]$PluginDll = (Join-Path $PSScriptRoot '..\bin\Release\net6.0\ER2RealismOverhaul.dll')
)

$ErrorActionPreference = 'Stop'
if (-not $GameDir) { $GameDir = 'E:\SteamLibrary\steamapps\common\Easy Red 2' }
Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path -LiteralPath $PluginDll).Path)
try {
    $queryCount = 0
    $invalid = @()
    foreach ($type in $module.GetTypes()) {
        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            $hasQuery = $false
            $copiesArray = $false
            foreach ($instruction in $method.Body.Instructions) {
                $call = $instruction.Operand
                if ($call -isnot [Mono.Cecil.MethodReference]) { continue }
                if ($call.DeclaringType.FullName -eq 'UnityEngine.Physics' -and $call.Name.EndsWith('NonAlloc')) {
                    $queryCount++
                    $hasQuery = $true
                }
                # A CLR result buffer passed to this API produces a new IL2CPP array.
                # The caller's array remains empty even when the native hit count is positive.
                if ($call.Name -eq 'op_Implicit' -and
                    $call.DeclaringType.Namespace -eq 'Il2CppInterop.Runtime.InteropTypes.Arrays' -and
                    $call.Parameters[0].ParameterType.IsArray) {
                    $copiesArray = $true
                }
            }
            if ($hasQuery -and $copiesArray) { $invalid += $method.FullName }
        }
    }
    if ($queryCount -eq 0) { throw 'No Unity non-allocating physics queries found in this assembly.' }
    if ($invalid) { throw ("Physics output buffer copied in " + ($invalid -join [Environment]::NewLine)) }
    Write-Output "$queryCount native physics query sites checked. No implicit result-buffer copies."
}
finally {
    $module.Dispose()
}
