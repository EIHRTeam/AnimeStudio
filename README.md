# AnimeStudio CLI

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/EIHRTeam/AnimeStudio)<br>
[Ask DeepWiki](https://deepwiki.com/EIHRTeam/AnimeStudio)

This is a fork of the original Anime Studio, focused on improving Linux CI/CD support and compatibility with macOS environments.

The primary goal of this fork is to optimize the CLI for batch processing *Arknights: Endfield* VFS files in low-memory (specifically 16 GB or less) Linux and macOS environments. Maintenance of the GUI version and support for other games are not planned and will not be under consideration.

Please note that much of the work on this fork was assisted by AI. The project is intended to “just work” for its specific use case, rather than to be a polished, elegant, or state-of-the-art solution.

The project targets `.NET 10` exclusively. Install the .NET 10 runtime before using framework-dependent release packages.

## Build

```bash
dotnet restore AnimeStudio.sln
dotnet build AnimeStudio.sln -c Release
```

For a verified Linux x64 release archive:

```bash
./build-linux-release.sh
```

Use `--version <version>`, `--output-dir <path>`, and `--deb` as needed. The
script builds the solution, runs smoke tests, publishes for `linux-x64`, checks
the native-library layout and dependencies, and prints the archive SHA256.

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
- `linux-x64` with Debian GNU/Linux 13 as the baseline
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

## Credits

Special thanks to:

- All contributors of the original Asset Studio
- [Escartem](https://github.com/Escartem) and all contributors of Anime Studio
- [ZengXiaoPi](https://github.com/ZengXiaoPi) and [mengxixiao](https://github.com/mengxixiao)
