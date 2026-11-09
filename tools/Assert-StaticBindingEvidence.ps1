# Read-only PE witnesses. This tool never loads the inspected game image.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BinaryPath,[Parameter(Mandatory)][string]$EvidencePath)
$ErrorActionPreference='Stop'
$auditEvidence=Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
$auditExpected=if($auditEvidence.executable_sha256){$auditEvidence.executable_sha256}else{$auditEvidence.game_assembly_sha256}
$auditBytes=[IO.File]::ReadAllBytes([IO.Path]::GetFullPath($BinaryPath))
$auditHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($auditBytes))
if($auditHash -ine $auditExpected){throw 'Unsupported game binary: reviewed SHA-256 does not match.'}
$auditPe=[BitConverter]::ToInt32($auditBytes,0x3c)
if([BitConverter]::ToUInt32($auditBytes,$auditPe) -ne 0x4550 -or [BitConverter]::ToUInt16($auditBytes,$auditPe+4) -ne 0x8664){throw 'Expected an x64 PE image.'}
$auditCount=[BitConverter]::ToUInt16($auditBytes,$auditPe+6)
$auditOptional=[BitConverter]::ToUInt16($auditBytes,$auditPe+20)
if($auditCount -lt 1 -or $auditCount -gt 96){throw 'Invalid PE section count.'}
foreach($auditBinding in $auditEvidence.bindings){
    $auditPrefix=[Convert]::FromHexString($auditBinding.prefix)
    $auditMatches=@(for($auditIndex=0;$auditIndex -lt $auditCount;$auditIndex++){
        $auditSection=$auditPe+24+$auditOptional+40*$auditIndex
        $auditRva=[BitConverter]::ToUInt32($auditBytes,$auditSection+12)
        $auditSize=[Math]::Min([BitConverter]::ToUInt32($auditBytes,$auditSection+8),[BitConverter]::ToUInt32($auditBytes,$auditSection+16))
        if($auditBinding.rva -ge $auditRva -and $auditBinding.rva+$auditPrefix.Length -le [long]$auditRva+$auditSize){
            [long][BitConverter]::ToUInt32($auditBytes,$auditSection+20)+$auditBinding.rva-$auditRva
        }
    })
    if($auditMatches.Count -ne 1 -or $auditMatches[0]+$auditPrefix.Length -gt $auditBytes.Length){throw "Unmapped binding: $($auditBinding.name)"}
    $auditObserved=[Convert]::ToHexString($auditBytes,[int]$auditMatches[0],$auditPrefix.Length)
    if($auditObserved -ine $auditBinding.prefix){throw "Native binding changed: $($auditBinding.name)"}
}
if((Get-FileHash -LiteralPath $BinaryPath).Hash -cne $auditHash){throw 'Game binary changed during inspection.'}
Write-Host "PASS read-only native evidence: $($auditEvidence.bindings.Count) sites, x64 image and exact SHA-256; no game loaded."
