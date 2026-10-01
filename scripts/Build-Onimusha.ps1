[CmdletBinding()]
param([string]$SourceProject = (Join-Path $PSScriptRoot '..\..\cheats-onimusha'))
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcLua = Join-Path $hcRoot 'modules\onimusha\reframework\autorun'
$hcVersionMatch = [regex]::Match((Get-Content -LiteralPath (Join-Path $hcLua 'onimusha_trainer.lua') -Raw), '(?m)^\s*local\s+VERSION\s*=\s*"(?<version>\d+\.\d+\.\d+)"')
if (!$hcVersionMatch.Success) { throw 'Onimusha trainer source has no supported VERSION declaration.' }
$hcVersion = $hcVersionMatch.Groups['version'].Value
if ($hcVersion -ne '0.4.0') { throw "Unsupported Onimusha trainer source version: $hcVersion" }
$hcFiles = @('dinput8.dll', 'reframework/autorun/onimusha_trainer.lua', 'reframework/autorun/onimusha/modules/parry.lua', 'reframework/autorun/onimusha/modules/stun.lua', 'reframework/autorun/onimusha/modules/player_stun.lua')
foreach ($hcRelative in $hcFiles | Select-Object -Skip 1) {
    if (!(Test-Path -LiteralPath (Join-Path $hcLua $hcRelative.Substring('reframework/autorun/'.Length)) -PathType Leaf)) {
        throw "Onimusha $hcVersion source is incomplete: $hcRelative"
    }
}
$hcOutput = Join-Path $hcRoot 'payload\onimusha'
$hcOriginal = Join-Path $SourceProject ("dist\OnimushaTrainer-" + $hcVersion)
New-Item -ItemType Directory -Force -Path $hcOutput | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $hcOutput 'payload\dinput8.dll'))) {
    if (Test-Path -LiteralPath (Join-Path $hcOriginal 'payload\dinput8.dll')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $hcOutput 'payload') | Out-Null
        Copy-Item -LiteralPath (Join-Path $hcOriginal 'payload\dinput8.dll') -Destination (Join-Path $hcOutput 'payload\dinput8.dll') -Force
    } else {
        $hcProvenance = Get-Content -LiteralPath (Join-Path $hcRoot 'modules\onimusha\dependency\provenance.json') -Raw | ConvertFrom-Json
        $hcCache = Join-Path $hcRoot '.tools\reframework'
        New-Item -ItemType Directory -Force -Path $hcCache | Out-Null
        $hcArchive = Join-Path $hcCache 'REFramework.zip'
        if (!(Test-Path -LiteralPath $hcArchive)) { Invoke-WebRequest -Uri $hcProvenance.assetUrl -OutFile $hcArchive }
        if ((Get-FileHash -LiteralPath $hcArchive -Algorithm SHA256).Hash -ine $hcProvenance.archiveSha256) { throw 'Pinned REFramework checksum mismatch.' }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $hcZip = [IO.Compression.ZipFile]::OpenRead($hcArchive)
        try {
            $hcEntry = $hcZip.GetEntry('dinput8.dll')
            if (!$hcEntry) { throw 'Pinned REFramework archive lacks dinput8.dll.' }
            New-Item -ItemType Directory -Force -Path (Join-Path $hcOutput 'payload') | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($hcEntry, (Join-Path $hcOutput 'payload\dinput8.dll'), $false)
        } finally { $hcZip.Dispose() }
    }
}
if ((Get-FileHash -LiteralPath (Join-Path $hcOutput 'payload\dinput8.dll') -Algorithm SHA256).Hash -ine '8F005288FB1CDED7C36DC834480125A824B3946B075CAF15557E06D524D753BD') { throw 'Pinned REFramework DLL checksum mismatch.' }
New-Item -ItemType Directory -Force -Path (Join-Path $hcOutput 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $hcRoot 'modules\onimusha\dependency\REFramework-MIT.txt') -Destination (Join-Path $hcOutput 'licenses') -Force
Copy-Item -LiteralPath (Join-Path $hcRoot 'modules\onimusha\dependency\provenance.json') -Destination (Join-Path $hcOutput 'licenses') -Force
foreach ($hcRelative in $hcFiles | Select-Object -Skip 1) {
    $hcSource = Join-Path $hcLua $hcRelative.Substring('reframework/autorun/'.Length)
    $hcTarget = Join-Path $hcOutput ('payload/' + $hcRelative)
    New-Item -ItemType Directory -Force -Path (Split-Path $hcTarget -Parent) | Out-Null
    Copy-Item -LiteralPath $hcSource -Destination $hcTarget -Force
}
@{ Version = $hcVersion; AppId = '2638890'; TestedBuild = '24769601'; Files = @($hcFiles | ForEach-Object {
    @{ RelativePath = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $hcOutput ('payload/' + $_)) -Algorithm SHA256).Hash }
}) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $hcOutput 'package.json') -Encoding utf8
Write-Host 'Onimusha payload ready.'
