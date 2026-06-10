# AnimeStudio CLI Cross-Platform Migration Log

This document records implementation work by migration phase. Each implementation
session must belong to exactly one phase and must document its goal, completed
changes, verification results, unresolved issues, and cautions for later work.
Each session must also record the phase completion status as `Not Started`, `In
Progress`, `Completed`, or `Blocked`.

## Phase 0: Baseline and Tracking

**Phase Completion Status:** Completed

### Completed Work

- Reviewed the Debian 13 compatibility report, project reference graph, native
  dependencies, and the current Windows-only CI workflow.
- Verified that the installed macOS FBX SDK 2020.3.9 contains both `x86_64` and
  `arm64` libraries.
- Verified that AnimeStudio.Ooz can be built on macOS 15 ARM64.
- Confirmed that Texture2DDecoder has current Windows, Linux, and macOS native
  packages, so replacing the decoder is unnecessary.
- Confirmed that a .NET 10 CLI build was blocked by two Span conversion errors
  in `FairGuardUtils`.
- Confirmed that the custom ACL source is absent and that the CLI shader path
  does use `HLSLDecompiler.dll`.

### Cautions for Later Work

- The compatibility report's claims that Newtonsoft.Json 13.0.4 does not exist,
  Texture2DDecoder is Windows-only, and HLSLDecompiler is GUI-only are outdated.
- Do not commit the FBX SDK installer/archive. Commit only permitted build
  outputs and the required Autodesk notices.
- The repository has no suitable test assets; later phases must add synthetic
  fixtures.

## Phase 1: Establish the .NET 10 Managed Baseline

### Session: 2026-06-10

**Phase Completion Status:** Completed

#### Goal

- Pin the repository to the .NET 10.0.301 SDK feature band.
- Move the CLI to `net10.0` while retaining `net8.0` and `net9.0` targets in
  shared projects for the existing GUI.
- Resolve the known .NET 10 Span overload error without changing behavior.

#### Completed Changes

- Added `global.json` with SDK version `10.0.301`, `latestPatch` roll-forward,
  and prerelease SDKs disabled.
- Changed AnimeStudio.CLI from Windows-specific .NET 8/9 targets to the single
  cross-platform target `net10.0`.
- Added `net10.0` to AnimeStudio, AnimeStudio.Utility, AnimeStudio.PInvoke, and
  AnimeStudio.FBXWrapper while retaining their `net9.0` and `net8.0` targets.
- Passed `seedInts.AsSpan()` explicitly to `MemoryMarshal.AsBytes` in
  `FairGuardUtils`.
- Left GUI and Patcher target frameworks, package versions, CLI behavior,
  configuration, and native library layout unchanged.
- Updated `PLAN.md` so every implementation session must record its phase
  completion status, and marked the current status of all migration phases.

#### Verification Commands and Results

- `dotnet --version`: passed; selected SDK `10.0.301`.
- `dotnet restore AnimeStudio.CLI/AnimeStudio.CLI.csproj`: passed.
- `dotnet build AnimeStudio.CLI/AnimeStudio.CLI.csproj -c Release -f net10.0
  --no-restore`: passed with 21 existing warnings and no errors.
- `dotnet build AnimeStudio.GUI/AnimeStudio.GUI.csproj -c Release -f
  net9.0-windows -p:EnableWindowsTargeting=true`: passed with 23 existing
  warnings and no errors.
- `dotnet build AnimeStudio.GUI/AnimeStudio.GUI.csproj -c Release -f
  net8.0-windows -p:EnableWindowsTargeting=true`: passed with 23 existing
  warnings and no errors.
- `dotnet build AnimeStudio.Patcher/AnimeStudio.Patcher.csproj -c Release`:
  passed with no warnings or errors.
- `git diff --check`: passed with no whitespace errors.
- `git status --short`: confirmed that tracked modifications are limited to
  Phase 1 source/project files. New Phase 1 files are `global.json` and this
  migration log; `PLAN.md` was updated for the newly requested status rule.
  Pre-existing untracked instruction/report files and the FBX SDK archive
  remain unmodified and uncommitted.

#### Unresolved Issues

- Existing compiler warnings remain, including nullable annotations outside a
  nullable context, non-exhaustive switches, TODO directives, obsolete OpenTK
  calls, and unused members.
- Direct project builds on macOS emit non-fatal FBXNative copy warnings because
  the existing targets depend on the solution-only `$(SolutionDir)` property.
  Native library layout and copy behavior remain deferred to Phase 3.

#### Cautions for the Next Session

- Do not start Phase 2 until all Phase 1 acceptance commands pass.
- Phase 2 must preserve existing CLI option names, defaults, and filtering
  semantics while migrating System.CommandLine and configuration.
- Do not treat current nullable, non-exhaustive switch, or unused-variable
  warnings as part of Phase 1.

## Phase 2: Modernize the CLI and Configuration

### Session: 2026-06-10

**Phase Completion Status:** Completed

#### Goal

- Upgrade System.CommandLine to stable 2.0.9 without changing option names,
  defaults, filter behavior, or help intent.
- Return meaningful process exit codes and keep batch processing alive when an
  individual asset fails.
- Replace App.config and ConfigurationManager with an appsettings.json file
  loaded through System.Text.Json from `AppContext.BaseDirectory`.
- Convert `types`, `uvs`, and `texs` from JSON-encoded strings into structured
  JSON objects while retaining existing defaults.
- Replace CLI-owned hard-coded path separators with `Path.Combine`.

#### Completed Changes

- Upgraded System.CommandLine from beta4 to stable 2.0.9.
- Replaced `BinderBase`, `SetHandler`, and legacy value accessors with
  `SetAction(ParseResult)`, `CustomParser`, `Validators`, `DefaultValueFactory`,
  and `GetValue`.
- Preserved all existing option names, defaults, enum choices, regex-file
  behavior, hexadecimal/decimal key parsing, and help intent.
- Changed `Program.Main` and `Program.Run` to return process exit codes. Parse
  and startup failures return non-zero; individual file failures are logged and
  batch processing continues.
- Completed `AssetsManager.Clear()` so failed loads cannot leak queued files,
  duplicate tracking, or offset state into the next batch item.
- Removed App.config and System.Configuration.ConfigurationManager.
- Added a strongly typed `appsettings.json` loaded from
  `AppContext.BaseDirectory` with System.Text.Json. Missing or invalid files
  fall back to defaults with a diagnostic.
- Converted `types`, `uvs`, and `texs` to structured JSON objects. Partial
  nested entries inherit the remaining built-in defaults.
- Copied `appsettings.json` to build and publish output.
- Replaced CLI-owned path concatenation and `./Maps` literals with
  `Path.Combine` and `Path.GetRelativePath`. Relative input and output paths
  remain based on the current working directory.

#### Verification Commands and Results

- `dotnet build AnimeStudio.CLI/AnimeStudio.CLI.csproj -c Release -f net10.0`:
  passed with no errors.
- `dotnet publish AnimeStudio.CLI/AnimeStudio.CLI.csproj -c Release -f net10.0
  --self-contained false`: passed; `appsettings.json` was present and the old
  `.dll.config` and ConfigurationManager assembly were absent.
- CLI smoke tests: `--help` returned 0; missing arguments, invalid game/key/
  regex, missing input, and missing optional files returned 1.
- A normal invocation with decimal/hex keys, multiple regexes, and a regex file
  returned 0. Invalid regex lines inside a filter file were skipped as before.
- Configuration tests from a copied application directory passed for complete,
  missing, invalid, and partial JSON. Relative outputs were created under the
  invoking working directory.
- A two-file batch containing an unreadable first file logged that error,
  processed the second file, and returned 0.
- The old and new defaults contain the same 30 type entries and 8 UV entries.
- `dotnet build AnimeStudio.GUI/AnimeStudio.GUI.csproj -c Release -f
  net9.0-windows -p:EnableWindowsTargeting=true`: passed with no errors.
- Scoped `git diff --check` for Phase 2 files passed.

#### Unresolved Issues

- Existing compiler warnings remain outside this phase.
- Direct macOS builds and publishes still warn when the old targets cannot copy
  Windows FBXNative DLLs through `$(SolutionDir)`; Phase 3 owns native layout.
- Repository-wide `git diff --check` is currently blocked by trailing
  whitespace in the separately modified `LICENSE` file. Phase 2 files pass.
- Automated parser and configuration tests remain scheduled for Phase 6.

#### Cautions for the Next Session

- Phase 3 must preserve the new framework-dependent publish behavior and
  `appsettings.json` copy rules while replacing native asset layout.
- Do not restore App.config or ConfigurationManager for native configuration.
- Keep `AssetsManager.Clear()` clearing transient load state; otherwise one
  failed file can poison subsequent batch entries.
- `LICENSE` and `README.md` contain concurrent user changes and must not be
  reverted or overwritten.

## Phase 3: Native Dependencies and RID Layout

### Session: 2026-06-10 to 2026-06-11

**Phase Completion Status:** Completed

#### Goal

- Move native assets to `AnimeStudio.Libraries/runtimes/<rid>/native` and make
  RID-specific publishes contain only the selected platform's files.
- Replace manual Win32/libdl preloading with `NativeLibrary` and logical
  P/Invoke names.
- Upgrade Texture2DDecoder and add its Windows, Linux, and macOS native
  packages.
- Add reproducible CMake builds for AnimeStudio.Ooz and FBXNative, produce the
  available target binaries, and record their provenance and hashes.

#### Completed Changes

- Moved runtime assets to `AnimeStudio.Libraries/runtimes/<rid>/native` for
  `win-x64`, `linux-x64`, and `osx-arm64`. Removed the unsupported Windows x86
  runtime set and moved `BinaryDecompiler.lib` to `link/win-x64` so it is not
  published.
- Replaced the Win32 `LoadLibraryEx` and POSIX `libdl` loader with a
  per-assembly `NativeLibrary.SetDllImportResolver`. P/Invoke declarations now
  use logical names, including `AnimeStudio.Ooz` without a `.dll` suffix.
- Made CLI publishes copy only the repository native assets for the selected
  `RuntimeIdentifier`. GUI net8/net9 builds now consume the same win-x64 root
  layout and explicitly copy the Windows x64 Texture2DDecoder native binary.
- Upgraded Kyaru.Texture2DDecoder to wrapper `0.17.1` and Windows, Linux, and
  macOS native packages `0.2.0`. Removed the duplicate Utility package
  reference.
- Renamed the Ooz CMake target output to `AnimeStudio.Ooz`, disabled unrelated
  Bun/executable targets by default, and added a POSIX native build script.
- Added a C++17 FBXNative CMake project using `FBX_SDK_ROOT` and the static
  Autodesk FBX SDK library.
- Built `osx-arm64` Ooz and FBXNative on macOS 15.7.7 with Apple Clang 17 and
  FBX SDK 2020.3.9.
- Built `linux-x64` Ooz and FBXNative on the remote Debian 13.5 x86_64 host
  using APT CMake 3.31.6, GCC 14.2.0, and the supplied FBX SDK 2020.3.9.
- Added `NATIVE_ASSETS.md` with RID, compiler, SDK, and SHA256 provenance.
  Added the Autodesk-required copyright notice to `THIRD_PARTY_NOTICES.md` and
  included it in CLI/GUI outputs.
- Added the FBX SDK archive to `.gitignore` and removed the archive from version
  control while preserving the local file.
- Installed and then removed local Colima/Docker/Lima after switching Linux
  validation to the provided Debian server. No container runtime remains.

#### Verification Commands and Results

- `scripts/build-native.sh osx-arm64`: passed; produced ARM64 Mach-O Ooz and
  FBXNative libraries with the expected exported symbols.
- `FBX_SDK_ROOT=... scripts/build-native.sh linux-x64`: passed on Debian 13.5;
  produced x86-64 ELF Ooz and FBXNative libraries. FBXNative has no dynamic
  dependency on `libfbxsdk`.
- `dotnet publish ... -r win-x64|linux-x64|osx-arm64 --self-contained false`:
  passed for all three RIDs.
- Native publish inspection passed: each publish contains only its platform's
  Ooz, FBXNative, and Texture2DDecoder native format. Windows additionally
  contains its Windows-only ACL, FMOD, and HLSLDecompiler DLLs.
- macOS native smoke passed: FBX quaternion conversion, DXT1 decode, and Ooz
  invocation all entered the ARM64 native libraries.
- Debian 13 native smoke passed with the same FBX, DXT1, and Ooz checks. CLI
  `--help` returned 0 on both macOS and Debian.
- GUI net9.0-windows and net8.0-windows builds passed and both outputs contain
  the win-x64 FBXNative, Ooz, Texture2DDecoder, ACL, FMOD, and HLSLDecompiler
  runtime files.
- Patcher Release build passed.
- Scoped `git diff --check` passed. The only repository-wide whitespace issue
  remains the concurrent trailing space in `LICENSE`.

#### Unresolved Issues

- Existing win-x64 Ooz and FBXNative binaries were relocated without
  modification. Their original compiler metadata is unavailable; FBXNative can
  be rebuilt later on a Windows host with the new CMake project.
- Windows native execution was not available in this session. The win-x64
  publish structure and PE architecture were verified, but runtime smoke remains
  for the Windows CI work.
- ACL and DirectX shader capability/degradation behavior is intentionally
  deferred to Phase 4.

#### Cautions for the Next Session

- Phase 4 must use the root-level RID publish layout and must not restore
  x86/x64 directory probing or direct `libdl` calls.
- Keep `BinaryDecompiler.lib` out of runtime outputs.
- The FBX SDK archive and extracted SDK directories must remain uncommitted.
- Use APT first for dependencies on Debian. The remote build host supports
  password-authenticated sudo and already has the Phase 3 build prerequisites.
- Future remote work should clone the GitHub repository on the server and sync
  only uncommitted patches and external SDK inputs.
- Do not modify or revert the concurrent `LICENSE` and `README.md` changes.

## Phase 4: Platform Capabilities and Degraded Behavior

### Session: 2026-06-11

**Phase Completion Status:** Completed

#### Goal

- Add unified capability checks for ACL animation decompression and DirectX
  shader decompilation.
- Prevent missing or unsupported native dependencies from surfacing as
  `DllNotFoundException`.
- Skip ACL animation resources on Linux and macOS while logging the resource
  and game type and continuing the remaining export.
- Emit an explicit unsupported comment and hash for DirectX shader programs on
  non-Windows platforms without changing Metal or SPIR-V conversion.
- Verify that the Windows CLI publish contains `HLSLDecompiler.dll` and does
  not contain the link-only `BinaryDecompiler.lib`.

#### Completed Changes

- Added `PlatformCapabilities` as the shared source of truth for ACL animation
  and DirectX shader support. Native capability checks use the same logical
  names and assembly search paths as the `DllImport` resolver.
- Added cached `NativeLibrary` availability probes that release temporary
  handles after probing.
- Protected ACL, SRACL, DBACL, and HLSLDecompiler entry points so an unavailable
  native dependency becomes `PlatformNotSupportedException` rather than a
  top-level `DllNotFoundException`.
- Made model conversion skip only unsupported ACL animation clips while
  continuing the model and remaining animations. The warning records animation
  name, PathID, source file, game name, and game type.
- Made direct CLI `.anim` export skip unsupported ACL resources with the same
  identifying information. YAML conversion has an equivalent shared guard for
  non-CLI callers.
- Added non-Windows degradation for DX9 and DXBC shader subprograms before any
  Windows native API is called. The exported shader contains the bytecode hash
  and an explicit unsupported-platform comment.
- Left Metal and SPIR-V shader conversion paths unchanged.
- Retained `HLSLDecompiler.dll` and all four ACL DLLs in the `win-x64` publish.
  `BinaryDecompiler.lib` remains link-only and is absent from runtime output.

#### Verification Commands and Results

- Local SDK resolved to .NET `10.0.301`.
- CLI `net10.0`, GUI `net9.0-windows`, GUI `net8.0-windows`, and Patcher Release
  builds passed with no errors. Existing warnings remain.
- `osx-arm64` publish passed and `--help` returned 0. Its native files were only
  the macOS FBXNative, Ooz, and Texture2DDecoder dylibs.
- A macOS behavior smoke verified that DXBC output contains a hash and explicit
  macOS unsupported comment, Metal and SPIR-V do not use that degradation, and
  ACL returns `PlatformNotSupportedException`.
- The Debian 13 server was shallow-cloned directly from GitHub at the same base
  commit, then synchronized with uncommitted migration changes. APT upgraded
  `dotnet-sdk-10.0` from `10.0.300` to `10.0.301`.
- `linux-x64` publish and CLI `--help` passed on Debian 13. The Linux behavior
  smoke produced the Linux DXBC comment, preserved Metal/SPIR-V paths, and
  rejected ACL without exposing `DllNotFoundException`.
- The Linux publish contained only the Linux FBXNative, Ooz, and
  Texture2DDecoder shared objects as native runtime files.
- `win-x64` publish passed. `HLSLDecompiler.dll`, `acl.dll`, `sracl.dll`,
  `acldb.dll`, and `acldb_zzz.dll` were present as x86-64 PE files, while
  `BinaryDecompiler.lib` was absent.
- Scoped `git diff --check` and staged diff checks passed. The unrelated
  trailing whitespace in the concurrently modified `LICENSE` remains untouched.

#### Unresolved Issues

- Windows native execution was not available in this session. Runtime ACL and
  HLSLDecompiler smoke coverage remains for the Windows CI runner in Phase 5.
- ACL animation decompression intentionally remains disabled on Linux and
  macOS for the first cross-platform release.
- Automated capability and shader-output tests remain scheduled for Phase 6.
- Existing nullable, non-exhaustive switch, TODO, obsolete API, and unused
  variable warnings remain outside this phase.

#### Cautions for the Next Session

- Phase 5 publish scripts and CI must preserve the root-level RID native layout
  and the `PlatformCapabilities` guards.
- Windows CI must execute ACL and DirectX shader smoke cases; checking that DLLs
  exist is not sufficient to prove their dependencies and entry points load.
- Linux and macOS jobs should assert the explicit ACL and DirectX degradation
  behavior as well as `--help`.
- Keep `BinaryDecompiler.lib` out of all runtime archives.
- Continue using APT first on Debian. The remote host now has .NET SDK
  `10.0.301`, and the reusable shallow clone is at `~/AnimeStudio-phase4`.
- Do not commit the FBX SDK archive, extracted SDK directories, build outputs,
  or temporary smoke projects.
- Do not modify or revert the concurrent `LICENSE` and `README.md` changes.
