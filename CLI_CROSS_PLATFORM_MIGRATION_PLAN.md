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
