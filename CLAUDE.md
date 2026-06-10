# Agent Documentation

This file provides guidance to agents when working with code in this repository.

## Build Commands

```bash
# Restore NuGet packages
dotnet restore

# Build CLI only (current Windows TFM)
dotnet build AnimeStudio.CLI -c Release -f net9.0-windows

# Build GUI only
dotnet build AnimeStudio.GUI -c Release -f net9.0-windows

# Build everything via PowerShell script (Windows only)
.\build.ps1

# Build native Oodle/Ooz library (Linux/macOS)
cmake -S AnimeStudio.Oodle -B build/oodle -DCMAKE_BUILD_TYPE=Release
cmake --build build/oodle
```

No test suite exists in this repository. CI runs via `.github/workflows/build.yml` on `windows-latest` only.

## Project Architecture

AnimeStudio is a **Unity game asset extraction tool** targeting Genshin Impact, Honkai: Star Rail, and Zenless Zone Zero. It is a .NET 8/9 desktop app in CLI (console) and GUI (WinForms) variants. It is a maintained fork of Razmoth's AssetStudio, itself forked from Perfare's original.

### Solution Layers (top to bottom)

```
AnimeStudio.CLI / AnimeStudio.GUI          ← Entry points (Exe)
├── AnimeStudio.Utility                     ← Converters, shader tools, ACL wrappers
│   ├── AnimeStudio.PInvoke (DllLoader)     ← Cross-platform native DLL loading
│   └── AnimeStudio.FBXWrapper              ← C# wrapper around C++ FBX exporter
└── AnimeStudio (Core)                      ← Unity asset parsing engine
```

### Core Library (`AnimeStudio/`) — The Parsing Engine

The core is a **Unity serialized-file reader**. It parses Unity's binary asset format:

- `AssetsManager` — Central orchestrator. Loads files, resolves dependencies between assets, manages the asset list.
- `FileReader` + `FileIdentifier` — Identifies file type (Bundle, Web, Blk, Block, MhyFile) and dispatches to the appropriate reader.
- `BundleFile` / `WebFile` / `BlkFile` / `MhyFile` — Decompress and parse each container format into `StreamFile[]`.
- `Classes/` — One C# class per Unity `ClassIDType` (GameObject, Texture2D, Mesh, MonoBehaviour, etc.). Each class knows how to deserialize itself from a Unity binary stream using the `ObjectReader`.
- `ClassIDType.cs` — Enum of all supported Unity asset types, with extension methods `CanParse()` / `CanExport()` driven by `TypeFlags`.
- `TypeFlags` — Runtime matrix controlling which class IDs are parsed vs. exported, configured from App.config or CLI `--types` flags.
- `BuildTarget.cs` — Platform-specific reading quirks (switch endianness, etc.).

### Game Abstraction

`Game` is a polymorphic base class. Concrete types encode per-game differences:
- `Mr0k` (Genshin Impact), `Mhy` (Star Rail / sub-group), `Blk` (ZZZ), and others
- `GameManager.GetGame(name)` resolves a string to a `Game` instance
- `UnityCNGame` extends `Game` with decryption key support (`UnityCN.SetKey()`)
- `Game.Type` (enum) drives branching in many places — asset filtering logic frequently checks `game.Type.IsGISubGroup()`, `IsSRGroup()`, `IsZZZ()`, etc.

### CLI Flow (`AnimeStudio.CLI/`)

The entry point is `Program.Main → CommandLine.Init → Program.Run(Options)`.

1. **Argument parsing** via `System.CommandLine` (2.0.0-beta4). `OptionsBinder` defines 16+ options and binds them to `Options`.
2. **Game resolution**: `GameManager.GetGame(o.GameName)` resolves the `--game` string.
3. **File discovery**: Scan `input_path` recursively for asset files.
4. **Asset map** (optional): Build or load CAB maps / JSON asset maps for dependency resolution.
5. **Per-file loop**: For each file, `assetsManager.LoadFiles()` → `BuildAssetData()` builds `exportableAssets` → `ExportAssets()` writes files.
6. **Export pipeline**: `Exporter.ExportConvertFile()` dispatches by `ClassIDType` to type-specific exporters.

### Export Pipeline

`Studio.cs` orchestrates; `Exporter.cs` implements. The three export modes:

| Mode | CLI flag | Description |
|------|----------|-------------|
| `Raw` | `--export_type Raw` | Write asset bytes as `.dat` |
| `Dump` | `--export_type Dump` | Text dump via `asset.Dump()` |
| `Convert` | `--export_type Convert` | Convert to standard format (PNG/WAV/FBX/JSON) |

FBX export is the most complex path: `ModelConverter` transforms Unity GameObject hierarchies into an intermediate representation, then `ModelExporter.ExportFbx()` calls into the native `AnimeStudio.FBXNative.dll` via P/Invoke.

### Native DLL Loading (`AnimeStudio.PInvoke/DllLoader.cs`)

`DllLoader.PreloadDll(name)` is called in static constructors of P/Invoke wrapper classes (e.g., `ACL.cs`). It:

1. Determines the architecture subdirectory (`x64` or `x86` on Windows, detected via `Environment.Is64BitProcess`)
2. Optionally prepends `bin/` if a `bin/` subdirectory exists (for the AppHost-patched layout)
3. Loads the DLL via `LoadLibraryEx` (Windows) or `dlopen` (Linux/macOS)

On Linux, it expects `lib{name}.so`; on macOS, `lib{name}.dylib`. The Posix path exists but uses the deprecated `libdl` DllImport — glibc ≥2.34 has dlopen in libc directly.

### Configuration

`Settings.cs` wraps `System.Configuration.ConfigurationManager` reading from `App.config` (XML). Settings control export behavior: image format, FBX version, bone size, euler filtering, etc. Defaults are hardcoded as fallbacks in the property getters.

### Key NuGet Dependencies and Their Roles

| Package | Used For |
|---------|----------|
| `Newtonsoft.Json` 13.0.4* | JSON serialization everywhere (MiHoYoBinData dump, asset maps, material export) |
| `System.CommandLine` 2.0.0-beta4 | CLI argument parsing |
| `MessagePack` 3.1.4 | Binary asset map (de)serialization |
| `Kyaru.Texture2DDecoder` + `.Windows` | BC7/ASTC texture decoding → RGBA |
| `Vortice.D3DCompiler` | HLSL shader compilation (Windows-only, wraps d3dcompiler_47.dll) |
| `Mono.Cecil` 0.11.6 | IL analysis for MonoBehaviour type reconstruction from dummy DLLs |
| `SixLabors.ImageSharp.Drawing` | Cross-platform image encoding (PNG output) |
| `ZstdSharp.Port` | Zstandard decompression (pure C#) |
| `K4os.Hash.xxHash` | xxHash for asset integrity |

*\* Version 13.0.4 does not exist on NuGet — latest is 13.0.3.*

### Native DLLs in `AnimeStudio.Libraries/`

| DLL | Source | Exports |
|-----|--------|---------|
| `AnimeStudio.Ooz.dll` | `AnimeStudio.Oodle/` (in repo, has CMakeLists.txt) | Oodle/Kraken decompression |
| `AnimeStudio.FBXNative.dll` | `AnimeStudio.FBXNative/` (in repo, vcxproj only) | FBX SDK wrapper |
| `acl.dll` / `sracl.dll` | `nfrechette/acl` (MIT open source) | `DecompressAll`, `Dispose` |
| `acldb.dll` / `acldb_zzz.dll` | Same ACL fork | `DecompressTracks`, `Dispose` |

All P/Invoke signatures are in `AnimeStudio.Utility/ACL/ACL.cs`. `fmod.dll` and `HLSLDecompiler.dll` are GUI-only, not used by CLI.

### Platform Limitations

The CLI targets `net9.0-windows;net8.0-windows` — the `-windows` TFM suffix prevents building or running on Linux/macOS without modification. Core library projects target plain `net9.0;net8.0`. CI runs on `windows-latest` only. See `DEBIAN13_CLI_COMPATIBILITY_REPORT.md` for the full cross-platform migration analysis.

### Build Output Layout (after `build.ps1`)

The patcher (`AnimeStudio.Patcher/`) modifies the .NET AppHost executable so it looks for DLLs in a `bin/` subdirectory. Final output:

```
dist/net9.0-windows/
├── AnimeStudio.CLI.exe       ← AppHost-patched
├── AnimeStudio.GUI.exe       ← AppHost-patched
└── bin/                      ← All managed + native DLLs
    ├── x64/                  ← 64-bit native DLLs
    └── x86/                  ← 32-bit native DLLs
```
