# AnimeStudio CLI

AnimeStudio is a CLI-only Unity asset extraction tool focused on Arknights:
Endfield VFS processing in low-memory Linux and macOS environments. It also
publishes a Windows x64 CLI package.

The project targets `.NET 10` exclusively. Install the .NET 10 runtime before
using framework-dependent release packages.

## Build

```bash
dotnet restore AnimeStudio.sln
dotnet build AnimeStudio.sln -c Release
```

## Usage

```bash
AnimeStudio.CLI <input_path> <output_path> --game ArknightsEndfield
```

Run `AnimeStudio.CLI --help` for the complete option list.

## Publish

```bash
./scripts/publish-cli.sh linux-x64 --output-dir artifacts
./scripts/publish-cli.sh osx-arm64 --output-dir artifacts
```

On Windows:

```powershell
./scripts/publish-cli.ps1 -RuntimeIdentifier win-x64 -OutputDirectory artifacts
```

Supported release RIDs:

- `win-x64`
- `linux-x64` with Debian 13 as the baseline
- `osx-arm64` with macOS 15 as the baseline

## Development

Current work is tracked in:

- `ROADMAP.md`
- `PLAN.md`
- `STATUS.md`

Historical migration reports are under `docs/archive/`.

This repository is derived from AnimeStudio/AssetStudio forks maintained by
Escartem, Razmoth, Perfare, and other contributors. See `LICENSE` and
`THIRD_PARTY_NOTICES.md`.
