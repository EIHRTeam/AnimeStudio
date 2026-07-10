# AnimeStudio Development Roadmap

## Product Direction

AnimeStudio is a CLI-only Unity asset extraction tool targeting .NET 10. The
primary deployment is Debian 13 on a 15 GiB server, with Windows x64 and macOS
ARM64 CLI packages retained.

Memory stability takes priority over throughput. Streaming changes must keep
export paths and file bytes stable unless a future phase explicitly versions an
output format.

## Phase 0 - Parser and Memory Safety Baseline

Status: completed.

- Fixed Endfield AnimatorController TOSData parsing.
- Added object-bounded allocation validation.
- Added GC limits, periodic LOH compaction, and per-file OOM recovery.
- Fixed native FBX/ACL cleanup and large-container size overflow.
- Added Core and CLI smoke coverage.

## Phase 1 - CLI-only .NET 10 and Container Streaming

Status: implementation complete; acceptance closure in progress.

Active branch: `feat/container-streaming`.

The detailed implementation and acceptance criteria are in `PLAN.md`.

## Phase 2 - Asset Map Streaming

- Generate and consume XML, JSON, and MessagePack maps incrementally.
- Use disk-backed staging for cross-file container-reference resolution.
- Preserve existing formats and deterministic output bytes.

## Phase 3 - Text and Resource Export Streaming

- Stream OBJ, YAML, Shader, header, Raw, audio, and video output directly.
- Add bounded resource-copy APIs instead of complete byte arrays.

## Phase 4 - Texture, Sprite, FBX, TypeTree, and Mesh Memory Reduction

- Reduce the full-directory Convert export peak observed at 11,474,792 KiB
  while writing 1,161,481 files; keep the container-only memory gate separate.
- Release converted textures incrementally.
- Add a bounded SpriteAtlas decode cache.
- Batch FBX intermediate data and texture transfer.
- Write TypeTree output without a complete boxed DOM.
- Decode Mesh data in bounded segments.
- Cache MiHoYoBinData decryption and parsing lazily.

## Phase 5 - Remaining Inputs and Release Stabilization

- Stream Web, Zip, ImportHelper, and legacy UnityWeb paths.
- Rebuild the Windows FBXNative binary from fixed source.
- Complete performance baselines, CI gates, and release documentation.
- Address optimized Animator hierarchies, long output paths, and unsupported
  Shader variants.
- Make fallback export names and FBX metadata deterministic if strict
  cross-run tree hashes remain a release requirement.

## Cross-phase Constraints

- Only `net10.0` is supported.
- Do not reintroduce the desktop GUI or Patcher.
- Default aggregate live-container memory threshold is 256 MiB.
- Temporary storage must not default to `/tmp` on Debian.
- Disk failures are explicit errors; they never silently fall back to memory.
- Output compatibility defaults to relative-path and SHA256 equality.
- The authorized Debian 13 server at `1.14.226.195` may be used after each
  phase or whenever real-machine validation is needed. Validation results and
  metrics belong in `STATUS.md`; credentials must never be committed.
- Remote validation code should be committed and pushed to GitHub first, then
  fetched and checked out by exact branch or commit on the server. Direct file
  transfer is an exception for GitHub outages or uncommittable diagnostic work
  and its reason must be recorded in `STATUS.md`.
- `Unknown ClassIDType 1186182244` is outside the streaming roadmap.
- Every session must update `STATUS.md`.
