[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameExe, [string]$Output)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7.' }
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
public static class EldenCompatibilityAudit {
    public sealed record Section(string Name, uint Rva, uint VirtualSize, uint RawOffset, uint RawSize, uint Flags);
    public sealed record Match(string Name, int Count, long[] InstructionRvas, long[] TargetRvas, long ExpectedTarget, bool MatchesProfile);
    public sealed record Result(bool X64, ushort OptionalMagic, uint ImageSize, Section[] Sections, Match[] Signatures, bool GlobalSectionsValid, bool PlayerVtableSectionValid);
    public static Result Inspect(string file) {
        byte[] b = File.ReadAllBytes(file);
        if (BitConverter.ToUInt16(b,0) != 0x5a4d) throw new Exception("Missing MZ header.");
        int pe = BitConverter.ToInt32(b,0x3c);
        if (BitConverter.ToUInt32(b,pe) != 0x4550) throw new Exception("Missing PE header.");
        int count = BitConverter.ToUInt16(b,pe+6), optionalSize = BitConverter.ToUInt16(b,pe+20), optional = pe+24;
        bool x64 = BitConverter.ToUInt16(b,pe+4) == 0x8664;
        ushort magic = BitConverter.ToUInt16(b,optional);
        uint imageSize = BitConverter.ToUInt32(b,optional+56);
        var sections = new List<Section>();
        for (int i=0;i<count;i++) {
            int t = optional+optionalSize+i*40;
            var s = new Section(Encoding.ASCII.GetString(b,t,8).TrimEnd('\0'),BitConverter.ToUInt32(b,t+12),BitConverter.ToUInt32(b,t+8),BitConverter.ToUInt32(b,t+20),BitConverter.ToUInt32(b,t+16),BitConverter.ToUInt32(b,t+36));
            if ((long)s.RawOffset+s.RawSize>b.Length) throw new Exception("Section exceeds file.");
            sections.Add(s);
        }
        Match Scan(string name,string pattern,long expected) {
            string[] tokens=pattern.Split(' '); var hits=new List<long>(); var targets=new List<long>();
            foreach (var s in sections) {
                if (s.Name!=".text" || (s.Flags&0x20000000)==0) continue;
                for (long i=s.RawOffset;i+(long)tokens.Length<=s.RawOffset+(long)s.RawSize;i++) {
                    if (b[i]!=0x48 || b[i+1]!=0x8b || b[i+2]!=0x05) continue;
                    bool same=true;
                    for (int j=3;j<tokens.Length;j++) if(tokens[j]!="??" && b[i+j]!=Convert.ToByte(tokens[j],16)){same=false;break;}
                    if(!same) continue;
                    long rva=s.Rva+i-s.RawOffset;
                    hits.Add(rva); targets.Add(rva+7+BitConverter.ToInt32(b,(int)i+3));
                }
            }
            return new Match(name,hits.Count,hits.ToArray(),targets.ToArray(),expected,hits.Count==1 && targets[0]==expected);
        }
        bool globalsValid=true;
        foreach(long rva in new long[]{0x3D6D018,0x3D709A8,0x3D81560,0x3D64FB8})
            globalsValid &= sections.Exists(s=>(s.Flags&0x80000000)!=0 && (s.Flags&0x20000000)==0 && rva>=s.Rva && rva+8<=s.Rva+(long)s.VirtualSize);
        bool vtableValid=sections.Exists(s=>(s.Flags&0x40000000)!=0 && (s.Flags&0xa0000000)==0 && 0x2A81CB0>=s.Rva && 0x2A81CB0+8<=s.Rva+(long)s.VirtualSize);
        return new Result(x64,magic,imageSize,sections.ToArray(),new[]{
            Scan("GameMan","48 8B 05 ?? ?? ?? ?? 80 B8 ?? ?? ?? ?? 0D 0F 94 C0 C3",0x3D709A8),
            Scan("GameDataMan","48 8B 05 ?? ?? ?? ?? 48 85 C0 74 05 48 8B 40 58 C3 C3",0x3D64FB8)
        },globalsValid,vtableValid);
    }
}
'@
$hcExe = Get-Item -LiteralPath $GameExe
$hcHashBefore = (Get-FileHash -LiteralPath $GameExe -Algorithm SHA256).Hash
$hcAudit = [EldenCompatibilityAudit]::Inspect($hcExe.FullName)
$hcResult = [ordered]@{
    executable = $hcExe.FullName
    fileVersion = $hcExe.VersionInfo.FileVersion
    bytes = $hcExe.Length
    sha256 = $hcHashBefore
    readOnly = $true
    gameLaunched = $false
    gameplayVerified = $false
    activeEacBypassImplemented = $false
    pe = $hcAudit
}
if ((Get-FileHash -LiteralPath $GameExe -Algorithm SHA256).Hash -cne $hcHashBefore) { throw 'Game executable changed during inspection.' }
if ($Output) { $hcResult | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $Output -Encoding utf8NoBOM }
$hcResult | ConvertTo-Json -Depth 7

if ($hcExe.VersionInfo.FileVersion -cne '2.7.2.0' -or !$hcAudit.X64 -or !$hcAudit.GlobalSectionsValid -or !$hcAudit.PlayerVtableSectionValid -or @($hcAudit.Signatures | Where-Object { !$_.MatchesProfile }).Count) { throw 'Elden Ring installed image does not match the reviewed worldwide 2.7.2.0 profile.' }
