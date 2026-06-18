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

The CLI defaults `--workers` to the process-visible logical CPU count. Object
parsing and AssetMap scanning parallelize across independent serialized files
and, when needed, independent object-bounded views within one serialized file.
They never share a mutable stream position. Non-FBX exports use bounded workers
with output paths reserved in asset order. Shared resource streams synchronize
seek/read operations, while FBX native export remains serialized until the
native wrapper is proven reentrant. Keep Workstation GC: the Debian Server GC
candidate exceeded the Phase 2 AssetMap RSS gate.

A user-level performance profile at `~/.anime/config.json` plus
`--mode default|limit|fast` lets users treat RAM/CPU as an optimization budget
to fill, not a throttle. This file is user/runtime scope, distinct from the
deployment-scoped `appsettings.json`. `default` (no config and no flag)
reproduces the conservative behavior above and must keep the existing RSS
gates. `limit` maximizes speed within the configured `maxMemoryKB`/`cpuCores`
budget; `fast` maximizes use of the whole machine (or budget) and may exceed
the historical Phase 2 AssetMap RSS gate, bounded by machine RAM or
`maxMemoryKB`. Only `default`/`limit` must hold those gates. The worker count
and the parse-worker halving are the levers: `fast`/`limit` disable the halving
to fill the budget while `default` keeps it. No mode changes GC — the 75% heap
hard limit still bounds RSS, RAM is a soft budget that derives worker count and
container threshold, and an explicit `--workers` overrides the mode. The phased
implementation is tracked in `PLAN_PERFORMANCE_PROFILE.md`.

Independent container blocks use one bounded producer/worker pipeline for
VFS, UnityFS/ENCR, Mhy, BLB, and HYG. Input bytes are read in source order,
decode workers own their buffers, and decoded bytes are written to fixed
backing-store offsets under a 256 MiB scratch budget. HNACB1 remains
sequential because later blocks depend on the first block. Concatenated VFS
and ordinary UnityFS block files are layout-probed first, then independent
container ranges run under the same global budget with nested block workers
disabled; parsed results merge by original offset. Unsupported encrypted
headers fall back to the sequential path. Future CPU-heavy streaming or
conversion work must reuse the process-wide worker budget, isolate mutable
state, bound queued memory, preserve deterministic output order, propagate
cancellation, and demonstrate concurrent named workers on Debian before
acceptance.

Every implementation session records its work in `STATUS.md`. `PLAN.md`
contains only the current phase details, while `ROADMAP.md` owns long-term and
cross-phase constraints. Keep these documents synchronized when scope,
acceptance criteria, or architectural decisions change.

The Debian 13 test server at `1.14.226.195` is authorized for real-machine
validation after a phase completes or whenever implementation risk warrants
earlier testing. Record the commands, peak resource metrics, and outcomes in
`STATUS.md`; do not store server credentials in repository files.

Phase 3 must add full systemd deployment integration for Debian service usage.
Use committed templates/scripts rather than ad-hoc server commands: include a
maintained service unit, environment configuration, install/uninstall or
management helpers, and operator documentation for `systemctl` lifecycle
commands and `journalctl` log inspection/following. Service stdout/stderr
should remain journal-visible, writable paths must be explicit, and hardening
must not break configured input, output, or AnimeStudio temporary directories.

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
