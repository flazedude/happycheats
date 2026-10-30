# Repository-wide serialization for publishers on different PCs. The annotated
# tag is an ownership token, not a software release and never carries secrets.
function Assert-HcPublishLockIdentity {
    param([string]$Repository, [string]$Version, [string]$SourceCommit)
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
        $Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $SourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid GitHub publication lock identity.' }
}

function Get-HcPublishLockRecoveryMessage {
    return 'GitHub publication lock refs/tags/happycheats-publish-lock is held or could not be confirmed. Wait for the other PC to finish, then retry. If a publisher was interrupted, first verify that neither PC is publishing, inspect that lock tag, and explicitly remove only that lock ref before retrying the preserved release stage. The lock is never stolen automatically.'
}

function Enter-HcGitHubPublishLock {
    param([Net.Http.HttpClient]$Client, [string]$Repository, [string]$Version, [string]$SourceCommit)
    Assert-HcPublishLockIdentity -Repository $Repository -Version $Version -SourceCommit $SourceCommit
    $hcReference = 'refs/tags/happycheats-publish-lock'
    $hcReadPath = "/repos/$Repository/git/ref/tags/happycheats-publish-lock"
    if (Invoke-HcGitHub -Client $Client -Path $hcReadPath -AllowNotFound) { throw (Get-HcPublishLockRecoveryMessage) }
    $hcToken = [Guid]::NewGuid().ToString('N')
    $hcTime = [DateTimeOffset]::UtcNow.ToString('o')
    $hcMessage = [ordered]@{ schemaVersion=1; version=$Version; commit=$SourceCommit; token=$hcToken; createdAt=$hcTime } | ConvertTo-Json -Compress
    $hcTag = Invoke-HcGitHub -Client $Client -Method POST -Path "/repos/$Repository/git/tags" -Body @{
        tag='happycheats-publish-lock'; message=$hcMessage; object=$SourceCommit; type='commit'
        tagger=@{ name='HappyCheats Publisher'; email='publisher@happycheats.invalid'; date=$hcTime }
    }
    if ($hcTag.sha -cnotmatch '^[0-9a-f]{40}$') { throw 'GitHub did not return an ownership tag for the publication lock.' }
    $hcLock = [pscustomobject]@{ repository=$Repository; reference=$hcReference; tagSha=$hcTag.sha; token=$hcToken; version=$Version; commit=$SourceCommit }
    try {
        # Creating an existing ref fails atomically. Never PATCH/force a lock ref.
        $hcCreated = Invoke-HcGitHub -Client $Client -Method POST -Path "/repos/$Repository/git/refs" -Body @{ ref=$hcReference; sha=$hcTag.sha }
        if ($hcCreated.ref -cne $hcReference -or $hcCreated.object.type -cne 'tag' -or $hcCreated.object.sha -cne $hcTag.sha) { throw 'GitHub returned a different publication lock owner.' }
    } catch {
        # A lost response may follow a successful ref creation. Proceed only if
        # a fresh read proves that this exact unique ownership tag holds the ref.
        try { $hcCurrent = Invoke-HcGitHub -Client $Client -Path $hcReadPath -AllowNotFound }
        catch { throw (Get-HcPublishLockRecoveryMessage) }
        if (!$hcCurrent -or $hcCurrent.object.type -cne 'tag' -or $hcCurrent.object.sha -cne $hcTag.sha) { throw (Get-HcPublishLockRecoveryMessage) }
    }
    return $hcLock
}

function Assert-HcGitHubPublishLock {
    param([Net.Http.HttpClient]$Client, $Lock)
    Assert-HcPublishLockIdentity -Repository $Lock.repository -Version $Lock.version -SourceCommit $Lock.commit
    if ($Lock.reference -cne 'refs/tags/happycheats-publish-lock' -or $Lock.tagSha -cnotmatch '^[0-9a-f]{40}$' -or $Lock.token -cnotmatch '^[0-9a-f]{32}$') { throw 'Invalid GitHub publication lock token.' }
    $hcCurrent = Invoke-HcGitHub -Client $Client -Path "/repos/$($Lock.repository)/git/ref/tags/happycheats-publish-lock" -AllowNotFound
    if (!$hcCurrent -or $hcCurrent.object.type -cne 'tag' -or $hcCurrent.object.sha -cne $Lock.tagSha) {
        throw 'GitHub publication lock ownership changed or disappeared. No foreign lock will be removed; inspect both PCs before retrying.'
    }
    # The immutable tag SHA binds the unique token and complete source identity.
    $hcTag = Invoke-HcGitHub -Client $Client -Path "/repos/$($Lock.repository)/git/tags/$($Lock.tagSha)"
    $hcOwner = $hcTag.message | ConvertFrom-Json
    if ($hcTag.object.type -cne 'commit' -or $hcTag.object.sha -cne $Lock.commit -or
        $hcOwner.schemaVersion -ne 1 -or $hcOwner.token -cne $Lock.token -or
        $hcOwner.commit -cne $Lock.commit -or $hcOwner.version -cne $Lock.version) { throw 'GitHub publication lock ownership token does not match; nothing was removed.' }
}

function Exit-HcGitHubPublishLock {
    param([Net.Http.HttpClient]$Client, $Lock)
    Assert-HcGitHubPublishLock -Client $Client -Lock $Lock
    try {
        $null = Invoke-HcGitHub -Client $Client -Method DELETE -Path "/repos/$($Lock.repository)/git/refs/tags/happycheats-publish-lock"
    } catch {
        throw ('Release work may have completed, but removing this publisher''s GitHub lock failed. ' + (Get-HcPublishLockRecoveryMessage))
    }
}
