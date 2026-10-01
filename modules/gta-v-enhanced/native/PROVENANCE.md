# Experimental source runtime

This isolated source runtime is derived from interface layouts, native hashes and
runtime integration concepts in YimMenu/YimMenuV2, enhanced branch, commit
39a0f227782304d7fb3a5f7d3cdaec36a4d348f3, dated 2026-09-16.

https://github.com/YimMenu/YimMenuV2/tree/39a0f227782304d7fb3a5f7d3cdaec36a4d348f3

Distributed under GNU GPL version 2; see COPYING. Original implementation work and
modifications in this directory are also GPL-2.0. The source package must accompany
distribution of a binary containing this runtime, with build instructions and the
corresponding MinHook sources/notices. This directory contains only a 41-native
subset and the necessary script/native layouts; it does not contain upstream
Online, network modification or protection bypass functionality.

Relevant upstream sources: src/game/pointers/Pointers.cpp;
src/game/gta/invoker/{Invoker.cpp,Invoker.hpp,Crossmap.hpp}; src/game/gta/Natives.hpp;
src/types/script/{scrNativeHandler.hpp,scrProgram.hpp,scrThread.hpp};
src/types/rage/{tlsContext.hpp,atArray.hpp}; src/game/gta/Scripts.cpp;
src/game/hooks/Script/RunScriptThreads.cpp.

Local MinHook is BSD-2-Clause with retained HDE notices; it is not MIT. The build
uses modules/crimson-desert/native/vendor/minhook without altering those sources.

The initialized game must expose the exact runtime version string 1158.16, unique
patterns, valid writable globals and unwind function boundaries. No initialized
game image was observed in this task. This is experimental and gameplay-unverified.
