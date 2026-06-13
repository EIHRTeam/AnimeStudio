# AnimeStudio Repository Guide

The authoritative agent workflow is in `AGENTS.md`. Read `ROADMAP.md`,
`PLAN.md`, and `STATUS.md` before development.

AnimeStudio is a CLI-only .NET 10 Unity asset extraction tool. The managed
dependency chain is:

```text
AnimeStudio.CLI
├── AnimeStudio.Utility
│   ├── AnimeStudio.PInvoke
│   └── AnimeStudio.FBXWrapper
└── AnimeStudio
```

Build and test:

```bash
dotnet restore AnimeStudio.sln
dotnet build AnimeStudio.sln -c Release
dotnet run --project scripts/AnimeStudio.Core.Smoke -c Release
```

Publish packages with `scripts/publish-cli.sh` or
`scripts/publish-cli.ps1`. Supported RIDs are `win-x64`, `linux-x64`, and
`osx-arm64`.

The only supported target framework is `net10.0`. There is no desktop GUI or
AppHost patcher. Native FBX, Ooz, FMOD, ACL, shader, and texture libraries remain
part of the CLI distribution where supported.

Core parsing is implemented under `AnimeStudio/`; CLI orchestration and export
selection are under `AnimeStudio.CLI/`; conversion and native wrappers are under
`AnimeStudio.Utility/`, `AnimeStudio.PInvoke/`, and
`AnimeStudio.FBXWrapper/`.
