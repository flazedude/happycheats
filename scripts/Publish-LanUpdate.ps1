# Legacy opt-in dual-host publishing. Normal releases use Publish-Update.ps1.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$NotesEnglish,
    [Parameter(Mandatory)][string]$NotesChinese,
    [string[]]$NewGame = @(),
    [string]$GitHubRepository = 'flazedude/happycheats',
    [switch]$SkipPayloads,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Publish-Update.ps1') @PSBoundParameters -PublishLan
