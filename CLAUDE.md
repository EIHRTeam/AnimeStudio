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

On Linux x64, build and verify a formal release package from the repository
root with:

```bash
./build-linux-release.sh
./build-linux-release.sh --version 1.2.3 --deb
```

Publish packages with `scripts/publish-cli.sh` or
`scripts/publish-cli.ps1`. Supported RIDs are `win-x64`, `linux-x64`, and
`osx-arm64`.

Container decompression uses one shared backing store per container. The live
stores share the aggregate `streaming.containerMemoryThresholdMiB` memory
budget; stores that would exceed it use bounded, reference-counted slices over
a temporary file. Temporary
directory priority is `ANIMESTUDIO_TEMP_DIR`, `appsettings.json`,
`$XDG_CACHE_HOME/animestudio/tmp` or `~/.cache/animestudio/tmp`, then the
Windows local application data directory. Do not change the Debian default to
`/tmp`, because the target host mounts it as tmpfs.

Every implementation session records its work in `STATUS.md`. `PLAN.md`
contains only the current phase details, while `ROADMAP.md` owns long-term and
cross-phase constraints. Keep these documents synchronized when scope,
acceptance criteria, or architectural decisions change.

The Debian 13 test server at `1.14.226.195` is authorized for real-machine
validation after a phase completes or whenever implementation risk warrants
earlier testing. Record the commands, peak resource metrics, and outcomes in
`STATUS.md`; do not store server credentials in repository files.

The normal server delivery path is: commit locally, push the active branch to
GitHub, then fetch and check out the exact commit on the server. Use direct
archive or file transfer only when GitHub is unavailable or diagnostic changes
are not ready to commit. Record that exception and its reason in `STATUS.md`;
it is not release provenance.

Output compatibility checks must report non-determinism explicitly. Long-path
fallback exports currently use `Path.GetRandomFileName()`, and FBX files embed
volatile creation metadata. Do not claim exact cross-run tree equality by
ignoring those differences; use the policy recorded in the active `PLAN.md`.

The only supported target framework is `net10.0`. There is no desktop GUI or
AppHost patcher. Native FBX, Ooz, FMOD, ACL, shader, and texture libraries remain
part of the CLI distribution where supported.

Core parsing is implemented under `AnimeStudio/`; CLI orchestration and export
selection are under `AnimeStudio.CLI/`; conversion and native wrappers are under
`AnimeStudio.Utility/`, `AnimeStudio.PInvoke/`, and
`AnimeStudio.FBXWrapper/`.
