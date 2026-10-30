# Interactive host setup only; the boot supervisor uses the saved configuration.
. (Join-Path $PSScriptRoot 'PublisherConfiguration.Common.ps1')

function Initialize-HcLanUpdateConfiguration {
    param([string]$Root, [string]$Address, [int]$Port = 0, [string]$PublisherDirectory)
    $hcStateRoot = Join-Path $Root 'artifacts/lan-updates'
    $hcConfigPath = Join-Path $hcStateRoot 'server.json'
    if (Test-Path -LiteralPath $hcConfigPath -PathType Leaf) {
        $hcSaved = Get-Content -LiteralPath $hcConfigPath -Raw | ConvertFrom-Json
        if (($Address -and $Address -ne $hcSaved.Address) -or ($Port -and $Port -ne $hcSaved.Port)) {
            throw 'The saved LAN address differs. Keep its DHCP reservation, or follow docs/lan-updates.md to migrate the feed.'
        }
        # Starting an established server never rewrites its paths or requires
        # access to a signing key. No signing occurs in the server process.
        return $hcSaved
    }

    if (!$Port) { $Port = 17845 }
    $hcIp = $null
    if (![Net.IPAddress]::TryParse($Address, [ref]$hcIp)) { throw 'Choose a private LAN IPv4 address.' }
    $hcBytes = $hcIp.GetAddressBytes()
    if ($hcBytes.Length -ne 4 -or !($hcBytes[0] -eq 10 -or ($hcBytes[0] -eq 172 -and $hcBytes[1] -ge 16 -and $hcBytes[1] -le 31) -or ($hcBytes[0] -eq 192 -and $hcBytes[1] -eq 168))) { throw 'Choose a private LAN IPv4 address.' }
    if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Choose a port between 1024 and 65535.' }

    # Every publishing PC uses the same established signing identity. A newly
    # configured LAN host must never generate an unrelated replacement key.
    $hcPublisher = Get-HcPublisherConfiguration -Root $Root -PublisherDirectory $PublisherDirectory
    $hcConfig = [pscustomobject]@{
        Address=$hcIp.ToString(); Port=$Port; BaseUrl="http://$($hcIp.ToString()):$Port/happycheats"
        PublicDirectory=(Join-Path $hcStateRoot 'public')
        PrivateKeyPath=$hcPublisher.PrivateKeyPath; PublicKeyPath=$hcPublisher.PublicKeyPath
    }
    New-Item -ItemType Directory -Path $hcStateRoot -Force | Out-Null
    $hcStream = [IO.File]::Open($hcConfigPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $hcWriter = [IO.StreamWriter]::new($hcStream, [Text.UTF8Encoding]::new($false))
    try { $hcWriter.Write(($hcConfig | ConvertTo-Json)) } finally { $hcWriter.Dispose() }
    return $hcConfig
}
