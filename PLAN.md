# Current Phase Plan: CLI-only .NET 10 and Container Streaming

## Objective

Remove the retired desktop product and legacy frameworks, then make decompressed
container data single-copy and disk-backed above 256 MiB. Completion requires
the known `68B3...chk` input to process without a memory skip and Debian peak RSS
to remain at or below 10 GiB.

## 1. CLI-only Baseline

- Delete the GUI, Patcher, `build.ps1`, and the GUI-only workflow.
- Keep only CLI, Core, Utility, PInvoke, FBXWrapper, and both smoke projects in
  the solution.
- Change all managed production projects to the single `net10.0` target.
- Remove active GUI/net8/net9/Patcher documentation and CI references.
- Preserve native libraries required by the Windows CLI.

Acceptance:

- `dotnet build AnimeStudio.sln -c Release` succeeds using only .NET 10.
- Active files contain no desktop project or net8/net9 build references.
- All three CLI RID publish smoke tests pass.

## 2. Shared Container Storage

Add:

- `ContainerStorageOptions` with `MemoryThresholdBytes` and
  `TemporaryDirectory`.
- `AssetsManager.ContainerStorageOptions`.
- Internal `SharedBackingStore`.
- Internal `ReadOnlySliceStream`.

Required behavior:

- One backing stream owns each decompressed container.
- Memory is used below the threshold and a temporary file at or above it.
- Slices have independent positions, enforce bounds, and are read-only/seekable.
- Shared reads are synchronized.
- Reference counting deletes the store after the last slice closes.
- Failed handoff, cancellation, parse errors, and OOM close unowned slices.

## 3. Bundle-family Integration

Integrate Bundle, Mhy, Blb, Hyg, and VFS:

- Decompress blocks sequentially into `SharedBackingStore`.
- Replace node MemoryStream copies with `ReadOnlySliceStream`.
- Keep `StreamFile.stream` unchanged.
- Continue using existing FileReader, SerializedFile, and ResourceReader paths.
- Remove avoidable Zstd, UnityWeb, and blocks-info `ToArray()` copies.
- Do not use `OffsetStream` for node slices.

## 4. Temporary Storage

Add:

```json
"streaming": {
  "containerMemoryThresholdMiB": 256,
  "temporaryDirectory": null
}
```

Resolution order:

1. `ANIMESTUDIO_TEMP_DIR`
2. `appsettings.json`
3. `$XDG_CACHE_HOME/animestudio/tmp` or `~/.cache/animestudio/tmp`
4. `%LOCALAPPDATA%/AnimeStudio/Temp`

Use a process run directory and lock file. Require writable storage and
estimated decompressed size plus 1 GiB free space. Never silently fall back to
memory. Remove the current run directory after all stores close, and only clean
stale AnimeStudio directories older than seven days with no active lock.

## 5. Automated Validation

- Test boundaries, independent seeks, interleaved/concurrent reads, reference
  counting, and final deletion.
- Test memory and file backings against identical hashes.
- Test Bundle, Mhy, Blb, Hyg, and VFS node hashes.
- Test cleanup after exceptions and cancellation.
- Run Core and CLI smoke tests.
- Publish and smoke `win-x64`, `linux-x64`, and `osx-arm64`.
- Run `git diff --check`.

## 6. Debian Acceptance

Server: `1.14.226.195`, Debian 13, .NET SDK 10.0.301.

- Process `68B3B9B8EB82E88FBFE6A313E6B18FB6.chk` with default types:
  exit 0, no memory skip, no kernel OOM.
- Process all 31 `.chk` files: exit 0 and no kernel OOM.
- Peak RSS hard limit: 10 GiB; target: 8 GiB.
- Export 36 controllers from SHA256
  `65b72bfe12149339d716919b8379f7e9346b8c7501250bb4a14328d23277df99`.
- Compare original-parameter relative paths and SHA256 output to baseline.
- Record time, peak RSS, temporary disk peak, and total writes.
- Confirm temporary run directories are empty after every run.

## Commit Boundaries

1. `docs: establish plan status and roadmap workflow`
2. `refactor: remove GUI patcher and legacy target frameworks`
3. `feat: add shared container backing store and bounded slices`
4. `feat: stream bundle family entries from shared storage`
5. `feat: configure and clean container temporary storage`
6. `test: verify streaming containers and Debian memory regression`
