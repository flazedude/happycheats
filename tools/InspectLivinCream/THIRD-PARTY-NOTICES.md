# Livin' the Cream native trainer notices

Package 1.0.1 uses the repository's pinned MinHook v1.3.4 (including reviewed x64 allocation fixes) and JSON for Modern C++ v3.12.0. Full license texts accompany the payload in ThirdPartyNotices.txt. No BepInEx loader or game-provided Newtonsoft.Json assembly is shipped.

Read-only inspection uses official Cpp2IL 2022.1.0-pre-release.21, Windows asset SHA-256 `663fb432433b4371fd1ee0ebc321a8fff2a9aac5ac4230c843f9e03ddee4e04c`. It is not a shipped dependency. Mono.Cecil reads generated metadata in record mode. Generated assemblies, extracted game code, game binaries, art and saves are never distributed.

Historical package 1.0.0 used BepInEx 5.4.23.5 x64 and its bundled licensed dependencies. That immutable release retains its original notices and is not rebuilt.
