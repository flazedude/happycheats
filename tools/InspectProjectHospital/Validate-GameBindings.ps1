[CmdletBinding()]
param(
    [string]$GameDirectory = 'E:\Games\SteamLibrary\steamapps\common\Project Hospital',
    [string]$GameAssembly,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$phRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$GameAssembly) { $GameAssembly = Join-Path $GameDirectory 'ProjectHospital_Data/Managed/Assembly-CSharp.dll' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $phRepo 'artifacts/project-hospital-bindings' }
$phCecilCandidates = @(
    (Join-Path $phRepo 'payload/project-hospital/BepInEx/core/Mono.Cecil.dll'),
    (Join-Path $phRepo 'tools/InspectChineseParents/bin/Release/net8.0/Mono.Cecil.dll'),
    (Join-Path $phRepo 'tools/InspectChineseParents/bin/Debug/net8.0/Mono.Cecil.dll'),
    (Join-Path $env:USERPROFILE '.nuget/packages/mono.cecil/0.11.6/lib/netstandard2.0/Mono.Cecil.dll')
)
$phCecil = $phCecilCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$phCecil) { throw 'Mono.Cecil is missing. Build tools/InspectChineseParents/InspectChineseParents.csproj, then rerun this read-only check.' }
Add-Type -Path $phCecil
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($GameAssembly)
function Get-TypeDef([string]$name) {
    $type = $assembly.MainModule.GetType($name)
    if (!$type) { throw "Missing type: $name" }
    $type
}
function Get-MethodDef([string]$type, [string]$name, [int]$parameterCount) {
    $methods = @((Get-TypeDef $type).Methods | Where-Object { $_.Name -eq $name -and $_.Parameters.Count -eq $parameterCount })
    if ($methods.Count -ne 1) { throw "Expected one method $type.$name/$parameterCount" }
    $methods[0]
}
function Assert-Field([string]$type, [string]$name, [string]$fieldType, [bool]$isStatic = $false) {
    $field = (Get-TypeDef $type).Fields | Where-Object { $_.Name -eq $name }
    if (!$field -or $field.FieldType.FullName -ne $fieldType -or $field.IsStatic -ne $isStatic) { throw "Incorrect field $type.$name" }
}
function Assert-Reference($method, [string]$text) {
    if (!@($method.Body.Instructions | Where-Object { [string]$_.Operand -like "*$text*" }).Count) { throw "Missing IL reference $text in $($method.FullName)" }
}
try {
    $hash = (Get-FileHash -LiteralPath $GameAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne '1120d899fb74d16f31115c5c27325edbd1154b68e9419e6b7ea03cc3d43b6f24') { throw "Unexpected game assembly SHA256: $hash" }
    $unlock = Get-MethodDef PlayerProfile IsCampaignLevelUnlocked 1
    if ($unlock.IsStatic -or $unlock.ReturnType.FullName -ne 'System.Boolean' -or $unlock.Parameters[0].ParameterType.FullName -ne 'GameDBCampaignLevel') { throw 'Campaign hook signature changed' }
    Assert-Reference $unlock 'm_campaignLevelsUnlocked'
    Assert-Reference (Get-MethodDef PlayerProfile UnlockCampaignLevel 1) 'PlayerProfile::Save()'
    Assert-Reference (Get-MethodDef LevelButtonController '<Start>m__0' 0) 'PlayerProfile::IsCampaignLevelUnlocked'
    Assert-Reference (Get-MethodDef MapList OnButtonAction 1) 'PlayerProfile::IsCampaignLevelUnlocked'
    $category = Get-MethodDef GameDBCampaignLevel get_LevelCategory 0
    if ($category.IsStatic -or $category.ReturnType.FullName -ne 'LevelCategory') { throw 'Campaign category signature changed' }
    $categories = Get-TypeDef LevelCategory
    if (($categories.Fields | Where-Object Name -eq CAMPAIGN).Constant -ne 1 -or ($categories.Fields | Where-Object Name -eq DLC_CAMPAIGN).Constant -ne 5) { throw 'Campaign category values changed' }
    $cap = Get-MethodDef Lopital.Hospital ReachedCharacterLimit 0
    if ($cap.IsStatic -or $cap.ReturnType.FullName -ne 'System.Boolean') { throw 'Staff hook signature changed' }
    Assert-Reference $cap 'm_disabledEmployeeLimit'
    Assert-Reference $cap 'TWEAKABLE_EMPLOYEE_LIMIT'
    Assert-Reference $cap 'Lopital.Hospital::CountEmployees()'
    $count = Get-MethodDef Lopital.Hospital CountEmployees 0
    if ($count.IsStatic -or $count.ReturnType.FullName -ne 'System.Int32') { throw 'Staff count signature changed' }
    $update = Get-MethodDef Lopital.Hospital UpdateCurrentStatistics 0
    if ($update.IsStatic -or $update.ReturnType.FullName -ne 'System.Void') { throw 'Statistics hook signature changed' }
    Assert-Reference $update 'Lopital.DailyStatistics::Calculate'
    Assert-Reference $update 'Lopital.DailyStatistics::m_reputation'
    Assert-Field Lopital.Hospital sm_instance Lopital.Hospital $true
    Assert-Field Lopital.Hospital m_state Lopital.HospitalPersistentData
    Assert-Field Lopital.Hospital m_departments 'System.Collections.Generic.List`1<Lopital.Department>'
    Assert-Field Lopital.HospitalPersistentData m_budget Lopital.Budget
    Assert-Field Lopital.Budget m_currentBalance System.Int32
    Assert-Reference (Get-MethodDef Lopital.Hospital Pay 2) 'Lopital.Budget::m_currentBalance'
    Assert-Reference (Get-MethodDef Lopital.Hospital Spend 2) 'Lopital.Budget::m_currentBalance'
    Assert-Reference (Get-MethodDef Lopital.Budget AllowsBuilding 0) 'Lopital.Budget::m_currentBalance'
    Assert-Field Lopital.HospitalPersistentData m_dailyStatistics Lopital.DailyStatistics
    Assert-Field Lopital.Department m_departmentPersistentData Lopital.DepartmentPersistentData
    Assert-Field Lopital.DepartmentPersistentData m_todaysStatistics Lopital.DailyStatistics
    Assert-Field Lopital.DailyStatistics m_reputation System.Single
    $calculate = Get-MethodDef Lopital.DailyStatistics Calculate 2
    Assert-Reference $calculate 'UnityEngine.Mathf::Clamp(System.Single,System.Single,System.Single)'
    Assert-Reference (Get-MethodDef MapEditorController Update 0) 'Lopital.Hospital::UpdateCurrentStatistics()'
    Assert-Field MapEditorController sm_instance MapEditorController $true
    Assert-Field MapEditorController m_initialized System.Boolean
    Assert-Field MapEditorController m_levelLoaded System.Boolean
    Assert-Field MapEditorController m_unloading System.Boolean
    Assert-Field PlayerProfile sm_instance PlayerProfile $true
    Assert-Field Lopital.DayTime sm_instance Lopital.DayTime $true
    Assert-Field Lopital.DayTime m_persistentData Lopital.DayTimePersistentData
    Assert-Field Lopital.DayTimePersistentData m_currentTimeHours System.Double
    Assert-Field Lopital.DayTimePersistentData m_day System.Int32
    $dayUpdate = Get-MethodDef Lopital.DayTime MultiUpdate 2
    if ($dayUpdate.IsStatic -or $dayUpdate.ReturnType.FullName -ne 'System.Void' -or $dayUpdate.Parameters[0].ParameterType.FullName -ne 'System.Int32' -or $dayUpdate.Parameters[1].ParameterType.FullName -ne 'System.Single') { throw 'Clock hook signature changed' }
    Assert-Reference $dayUpdate 'Lopital.DayTimePersistentData::m_currentTimeHours'
    Assert-Reference $dayUpdate 'Lopital.DayTimePersistentData::m_day'
    Assert-Reference $dayUpdate 'Lopital.DayTime::OnNewHour'
    Assert-Reference (Get-MethodDef MapEditorController MainUpdate 1) 'Lopital.DayTime::MultiUpdate'
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    @($unlock, $cap, $update, $calculate, $dayUpdate, (Get-MethodDef Lopital.Hospital Pay 2), (Get-MethodDef Lopital.Hospital Spend 2), (Get-MethodDef MapEditorController Update 0)) | ForEach-Object {
        $_.FullName
        $_.Body.Instructions | ForEach-Object { $_.ToString() }
        ''
    } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'verified-il.txt')
    [ordered]@{
        assembly = $GameAssembly
        sha256 = $hash
        validation = 'Passed read-only Cecil signatures, field types and real game call paths. Does not load/execute the game or touch saves.'
        checks = @('Campaign menu and click gates and categories', 'Persistent unlock saves profile; runtime hook avoids that path', 'Native employee limit, count and unlimited-setting path', 'Current prestige field types, clamp and aggregate recalculation', 'Money integer field and clock update signature', 'Initialized/loaded/unloading readiness fields')
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'validation.json')
    Write-Host 'PASS: Project Hospital read-only assembly binding validation'
}
finally { $assembly.Dispose() }
