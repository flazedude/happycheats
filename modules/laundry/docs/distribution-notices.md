# Laundry Trainer distribution notices

Laundry Trainer is an independent mod and installer for Laundry Store Simulator.
It is not affiliated with the game's developer, publisher, Valve, Microsoft, or
the MelonLoader developers. The game and Steam are not included.

## Components included in the executable

- **LaundryTrainer.dll** is built from the source in this repository.
- **MelonLoader 0.7.3 x64** is redistributed as the unmodified official archive.
  It is licensed under Apache-2.0. Its license, notice, component credits, and
  source links are preserved in `MelonLoader/Documentation` inside that archive
  and in the installed game. The archive's verified SHA256 is
  `5b2b2f3d1cd42b59ec886c5bdc2663edae87a0097a4f4a8f58c0965a99dda416`.
  [Official release and source](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3).
- **Microsoft .NET Desktop Runtime 6.0.36 x64** is the unmodified, Microsoft-signed
  installer. Microsoft publishes its SHA512 in the
  [.NET 6 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/6.0/releases.json).
  The runtime, Windows Desktop, Windows Forms, and WPF source license and
  third-party notices are included in the payload's `licenses` directory.
  The runtime installation retains Microsoft's original installer terms.
- **Microsoft .NET 8 x64** is embedded in the setup executable. The exact patch
  used appears in `manifest.json` as `setupRuntime.version`. Its runtime pack
  license and third-party notices are copied from the exact packages used for
  the build and included in the payload's `licenses` directory.

## MelonLoader components

The official MelonLoader archive contains third-party libraries. The original
`MelonLoader/Documentation/README.md` gives component credits and license/source
links. The archive and its documentation are retained without alteration.

One of these libraries, **Il2CppInterop 1.5.1-ci.845**, uses the LGPL-3.0 license.
Its full license and corresponding source archive for commit
`f03c8f4ae507d47ea814f3d11d1ec6b0391c1576` accompany the installer payload in
`licenses/Il2CppInterop-LICENSE.txt` and
`licenses/Il2CppInterop-source-f03c8f4ae507d47ea814f3d11d1ec6b0391c1576.zip`.
[Corresponding source and build instructions](https://github.com/BepInEx/Il2CppInterop/tree/f03c8f4ae507d47ea814f3d11d1ec6b0391c1576).
The library is distributed as separate DLLs and can be replaced in the installed
MelonLoader directory. Its LGPL rights, including modification and debugging of
those modifications, remain available under that license. The setup may refuse
to overwrite a customized loader; this does not restrict library modification.

On its first launch, MelonLoader may download Cpp2IL and Unity libraries required
to generate assemblies for the installed game. Those files are not bundled in
this installer. Their credits and terms are described by the
[official MelonLoader 0.7.3 documentation](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/README.md#licensing--credits).

## Build provenance

The embedded `manifest.json` records versions, file lengths, SHA256 checksums,
the runtime's official SHA512, and download sources. `dist/payload-manifest.json`
is an inspectable copy produced by the build script. The setup verifies the
bundled installer payload before using it. Build inputs are downloaded over
HTTPS from the original publishers. The .NET 6 installer also must pass a valid
Microsoft Authenticode signature check during packaging.

No Microsoft Visual C++ Redistributable installer is bundled. The pinned
MelonLoader bootstrap imports Windows system DLLs and Universal CRT APIs, which
are present on the supported Windows 10/11 platform, without an MSVCP or
VCRUNTIME DLL import. MelonLoader's pinned documentation lists .NET 6 Desktop
Runtime as its IL2CPP prerequisite.
