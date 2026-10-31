[CmdletBinding()]
param(
    [string]$GameDirectory = 'E:\Games\SteamLibrary\steamapps\common\Project Hospital',
    [string]$Plugin
)
$ErrorActionPreference = 'Stop'
$phRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$Plugin) { $Plugin = Join-Path $phRoot 'payload/project-hospital/BepInEx/plugins/HappyCheats.ProjectHospital/HappyCheats.ProjectHospital.dll' }
Add-Type -Path (Join-Path $phRoot 'payload/project-hospital/BepInEx/core/Mono.Cecil.dll')
$phAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Plugin)
$phFramework = @{}
try {
    $phMissing = @()
    foreach ($phType in $phAssembly.MainModule.GetTypeReferences()) {
        $phScope = $phType.Scope.Name
        if ($phScope -notin @('mscorlib','System','System.Core')) { continue }
        if (!$phFramework.ContainsKey($phScope)) {
            $phFramework[$phScope] = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory "ProjectHospital_Data/Managed/$phScope.dll"))
        }
        if (!$phFramework[$phScope].MainModule.GetType($phType.FullName)) { $phMissing += "$($phType.FullName) in $phScope" }
    }
    if ($phMissing.Count) { throw ('Plugin references framework types missing from the real game runtime: ' + ($phMissing -join '; ')) }
    Write-Host 'PASS: Project Hospital plugin framework types exist in the actual stripped game runtime.'
} finally {
    $phAssembly.Dispose()
    foreach ($phLibrary in $phFramework.Values) { $phLibrary.Dispose() }
}
