[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GitHubPublishLock.ps1')
$hcCount = 0
$hcRepository = 'example/happycheats'
$hcSource = 'a' * 40
function Assert-HcLockTest([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "GitHub publication lock check failed: $Message" }
    $script:hcCount++
}
function Assert-HcLockRejected([scriptblock]$Action, [string]$Message) {
    $hcRejected = $false
    try { $null = & $Action } catch { $hcRejected = $true }
    Assert-HcLockTest $hcRejected $Message
}
function Reset-HcLockFixture {
    $script:hcRef = $null
    $script:hcTags = @{}
    $script:hcCalls = [Collections.Generic.List[string]]::new()
    $script:hcCreateMode = 'normal'
    $script:hcDeleteFails = $false
    $script:hcReadFails = $false
    $script:hcSerial = 0
}
function Invoke-HcGitHub {
    param($Client, [string]$Method='GET', [string]$Path, $Body, [switch]$AllowNotFound)
    $script:hcCalls.Add("$Method $Path")
    $hcPrefix = "/repos/$hcRepository/git"
    if ($Method -eq 'GET' -and $Path -eq "$hcPrefix/ref/tags/happycheats-publish-lock") {
        if ($script:hcReadFails) { throw 'Fixture read failure.' }
        return $script:hcRef
    }
    if ($Method -eq 'POST' -and $Path -eq "$hcPrefix/tags") {
        $script:hcSerial++
        $hcSha = $script:hcSerial.ToString('x40')
        $hcTag = [pscustomobject]@{ sha=$hcSha; message=$Body.message; object=[pscustomobject]@{ type=$Body.type; sha=$Body.object } }
        $script:hcTags[$hcSha] = $hcTag
        return $hcTag
    }
    if ($Method -eq 'POST' -and $Path -eq "$hcPrefix/refs") {
        if ($script:hcCreateMode -eq 'race') {
            $script:hcRef = [pscustomobject]@{ ref=$Body.ref; object=[pscustomobject]@{ type='tag'; sha=('b'*40) } }
            throw 'Fixture conflict: another PC won atomic creation.'
        }
        if ($script:hcCreateMode -eq 'fail') { throw 'Fixture create failure.' }
        if ($script:hcRef) { throw 'Fixture existing ref cannot be overwritten.' }
        $script:hcRef = [pscustomobject]@{ ref=$Body.ref; object=[pscustomobject]@{ type='tag'; sha=$Body.sha } }
        if ($script:hcCreateMode -eq 'lost-response') { throw 'Fixture connection failed after successful creation.' }
        return $script:hcRef
    }
    if ($Method -eq 'GET' -and $Path.StartsWith("$hcPrefix/tags/")) {
        return $script:hcTags[$Path.Substring(($hcPrefix + '/tags/').Length)]
    }
    if ($Method -eq 'DELETE' -and $Path -eq "$hcPrefix/refs/tags/happycheats-publish-lock") {
        if ($script:hcDeleteFails) { throw 'Fixture delete failure.' }
        $script:hcRef = $null
        return
    }
    throw "Unexpected fixture API call: $Method $Path"
}
function Enter-HcFixtureLock { Enter-HcGitHubPublishLock -Client $null -Repository $hcRepository -Version '0.3.52' -SourceCommit $hcSource }

Reset-HcLockFixture
$hcLock = Enter-HcFixtureLock
Assert-HcLockTest ($hcLock.token -cmatch '^[0-9a-f]{32}$' -and $hcLock.tagSha -ceq $script:hcRef.object.sha) 'atomic acquisition returns unique ownership'
$hcMetadata = $script:hcTags[$hcLock.tagSha].message | ConvertFrom-Json
Assert-HcLockTest ((@($hcMetadata.PSObject.Properties.Name | Sort-Object) -join ',') -ceq 'commit,createdAt,schemaVersion,token,version') 'public lock metadata contains only nonsecret release identity'
Assert-HcGitHubPublishLock -Client $null -Lock $hcLock
Assert-HcLockRejected { Enter-HcFixtureLock } 'second PC cannot acquire a held lock'
Assert-HcLockTest (@($script:hcCalls | Where-Object { $_ -eq "POST /repos/$hcRepository/git/tags" }).Count -eq 1) 'held-lock rejection does not create another ownership tag'
Exit-HcGitHubPublishLock -Client $null -Lock $hcLock
Assert-HcLockTest ($null -eq $script:hcRef) 'owner releases lock'
$hcSecond = Enter-HcFixtureLock
Assert-HcLockTest ($hcSecond.token -cne $hcLock.token -and $hcSecond.tagSha -cne $hcLock.tagSha) 'next publisher gets a different token even for identical source/version'
Exit-HcGitHubPublishLock -Client $null -Lock $hcSecond

Reset-HcLockFixture
$script:hcCreateMode = 'race'
Assert-HcLockRejected { Enter-HcFixtureLock } 'atomic creation race rejects losing PC'
Assert-HcLockTest ($script:hcRef.object.sha -ceq ('b'*40) -and @($script:hcCalls | Where-Object { $_.StartsWith('DELETE ') }).Count -eq 0) 'losing PC preserves winning lock'

Reset-HcLockFixture
$script:hcCreateMode = 'lost-response'
$hcLock = Enter-HcFixtureLock
Assert-HcLockTest ($hcLock.tagSha -ceq $script:hcRef.object.sha) 'lost create response resumes only when fresh read proves ownership'
Exit-HcGitHubPublishLock -Client $null -Lock $hcLock

Reset-HcLockFixture
$script:hcCreateMode = 'fail'
Assert-HcLockRejected { Enter-HcFixtureLock } 'failed create cannot be mistaken for ownership'
Assert-HcLockTest ($null -eq $script:hcRef) 'failed create leaves no lock to remove'
$script:hcReadFails = $true
Assert-HcLockRejected { Enter-HcFixtureLock } 'unavailable lock state aborts before acquisition'

Reset-HcLockFixture
$hcLock = Enter-HcFixtureLock
$script:hcRef.object.sha = 'b'*40
Assert-HcLockRejected { Exit-HcGitHubPublishLock -Client $null -Lock $hcLock } 'changed owner cannot be released'
Assert-HcLockTest (@($script:hcCalls | Where-Object { $_.StartsWith('DELETE ') }).Count -eq 0) 'ownership mismatch never sends deletion'

Reset-HcLockFixture
$hcLock = Enter-HcFixtureLock
$hcLock.token = '0'*32
Assert-HcLockRejected { Exit-HcGitHubPublishLock -Client $null -Lock $hcLock } 'incorrect ownership token cannot release matching tag ref'
Assert-HcLockTest (@($script:hcCalls | Where-Object { $_.StartsWith('DELETE ') }).Count -eq 0) 'incorrect token preserves remote lock'

Reset-HcLockFixture
$hcLock = Enter-HcFixtureLock
$script:hcDeleteFails = $true
Assert-HcLockRejected { Exit-HcGitHubPublishLock -Client $null -Lock $hcLock } 'cleanup API failure is reported'
Assert-HcLockTest ($script:hcRef.object.sha -ceq $hcLock.tagSha) 'failed cleanup retains recoverable ownership'
$script:hcDeleteFails = $false
Exit-HcGitHubPublishLock -Client $null -Lock $hcLock

Reset-HcLockFixture
$hcFailureObserved = $false
try {
    $hcLock = Enter-HcFixtureLock
    try { throw 'Fixture upload failure.' }
    finally { Exit-HcGitHubPublishLock -Client $null -Lock $hcLock }
} catch { $hcFailureObserved = $_.Exception.Message -ceq 'Fixture upload failure.' }
Assert-HcLockTest ($hcFailureObserved -and $null -eq $script:hcRef) 'finally releases owned lock while preserving upload failure'

Reset-HcLockFixture
Assert-HcLockRejected { Enter-HcGitHubPublishLock -Client $null -Repository 'invalid/repository/extra' -Version '0.3.52' -SourceCommit $hcSource } 'invalid identity never reaches API'
Assert-HcLockTest ($script:hcCalls.Count -eq 0) 'invalid identity makes no network calls'
Write-Host "GitHub cross-PC publication lock offline checks passed: $hcCount. No network or credentials used."
