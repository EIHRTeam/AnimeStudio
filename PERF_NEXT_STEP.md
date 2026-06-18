# Next-Step Performance Optimization Reference

## Purpose

This document extracts the performance directions from
`PERF_ANALYZE_REPO.md` that are relevant to the next project work. It is a
planning input, not an authority: `PLAN.md`, `ROADMAP.md`, and `STATUS.md`
remain authoritative.

The source report is based mainly on static analysis. Its speedup estimates
must not be treated as measured results. The target remains Debian 13 with
4 CPU cores, 15 GiB RAM, and a medium-to-low IOPS disk; memory stability has
priority over throughput.

## Current Decision

1. Close the Phase 1 output-compatibility policy before starting another
   optimization phase.
2. Make Phase 2 AssetMap streaming the next implementation work package.
3. Collect stage-level profiling evidence before changing GC, file buffering,
   memory mapping, native compression, or concurrency.
4. Do not increase resident memory to improve speed while the container-only
   run is close to the 10 GiB gate and the full Convert run peaks at
   11,474,792 KiB.
5. Performance metrics are an optimization budget to fill for speed, not a
   throttle. The user-level profile (`~/.anime/config.json`) plus
   `--mode default|limit|fast` is now a separate work package
   (`PLAN_PERFORMANCE_PROFILE.md`): `default` keeps the conservative gates,
   while `limit`/`fast` scale workers to the budget or machine without changing
   GC. The target machine is Debian 13 with 4 CPU cores and 15 GiB RAM.

## Immediate Phase 2 Focus

### 1. Establish a Measured Baseline

Select and record one representative AssetMap build command before changing
the implementation. Keep the input set and all CLI options fixed for every
comparison.

Capture:

- wall-clock time and exit code;
- peak RSS from `/usr/bin/time -v`;
- managed heap size, allocation rate, Gen 0/1/2 counts, and GC pause time;
- temporary disk peak and final map size;
- disk throughput, utilization, queue depth, and await time from `iostat`;
- time spent in loading, object scanning, container resolution, filtering,
  spooling, and each output writer;
- a sampled syscall profile when overhead is acceptable.

Use the existing Debian server only after committing and pushing the exact
test commit to GitHub, then pull that commit on the server. Record commands,
commit, metrics, and cleanup results in `STATUS.md`.

### 2. Replace the Global AssetEntry List

The current AssetMap path retains every matching entry in
`List<AssetEntry>` until all files have been scanned, containers updated, and
all selected formats written. This is the primary Phase 2 memory target.

Introduce an internal, versioned, disk-backed entry spool:

- append one length-delimited record at a time;
- retain only per-input-file relationship state in memory;
- release loaded assets and per-file dictionaries before moving to the next
  input file;
- store enough raw data to resolve containers after all source files have
  contributed to `ResourceIndex`;
- keep a record count so formats requiring an array length can be written
  without rebuilding a complete list;
- enumerate the spool repeatedly with bounded buffers for XML, JSON, and
  MessagePack output;
- create the spool under the existing AnimeStudio process-specific temporary
  directory and reuse its locking, stale cleanup, disk-space checks, and
  explicit failure behavior.

Preserve current behavior order. In particular, current name, type, and
container filters run before `UpdateContainers`; the streamed implementation
must not silently change filtering semantics.

### 3. Stream Public Map Formats

Implement and validate each format independently:

- XML: retain the existing `XmlWriter` structure and stream entries directly
  from the spool.
- JSON: write the wrapper and `AssetEntries` array incrementally instead of
  passing the complete list to `JsonSerializer`.
- MessagePack: preserve the existing `AssetMap` schema, key layout, enum
  representation, and LZ4 block-array compatibility. Prototype the streaming
  writer against fixtures before replacing the current serializer.

The corresponding readers must also remain bounded:

- keep the existing streaming XML reader;
- parse JSON one entry at a time instead of deserializing a complete list;
- parse MessagePack incrementally or through a bounded disk-backed adapter;
- retain only the unique matching `Source` values required by the CLI.

Before implementation, create fixtures from the current writer and verify
that old maps remain readable. If byte-identical MessagePack compression
cannot be retained, document and approve the compatibility policy rather than
quietly changing the format.

### 4. Enforce Bounded Lifetime and Cleanup

Tests must cover:

- a synthetic map large enough to prove entries are not accumulated in a
  process-wide list;
- identical filtered source sets for old and new readers;
- equivalent XML/JSON structure and MessagePack compatibility;
- cancellation, parse failure, disk-full simulation, and simulated OOM;
- no spool or lock-file residue after success or failure;
- repeated spool enumeration without retaining prior entries;
- three RID package smoke checks and Debian peak-RSS regression.

## Low-Risk Supporting Optimizations

These changes touch the same parsing path and may be considered after the
AssetMap baseline identifies them as material:

1. Replace `m_Types.Find(...)` in the object loop with a per-file indexed
   lookup. This removes repeated linear scans without increasing unbounded
   memory.
2. Add capacity hints only where counts are already present in the serialized
   file. This reduces temporary array growth while keeping allocation bounded.
3. Rework `ReadStringToNull` with a rented buffer only if allocation profiling
   confirms it is significant. Rented buffers must have strict maximum lengths
   and must be returned on every exception path.
4. Remove avoidable LINQ allocations in AssetMap filtering only after
   correctness tests define the existing matching behavior.

Each supporting optimization should be a separate commit with its own
before/after metrics. Do not combine them with the initial spool conversion.

## Conditional Experiments

### Shared Backing Store Read Path

The report identifies `SharedBackingStore.ReadAt` as a seek/read/seek path
guarded by one lock. It is not yet proven to dominate the current
single-threaded workload.

Only prototype `MemoryMappedFile` or positioned reads after profiling shows
significant syscall or lock cost. The prototype must account for the number of
simultaneously live file-backed stores: creating a mapping or view per store
can exhaust virtual-memory-area or handle limits even when RSS is low.

Acceptance requires equal bytes, no slice lifetime regressions, bounded mapped
views, and no increase beyond the existing RSS gate.

### GC Configuration

Do not immediately enable Server GC or lower the heap hard limit. First:

- use `git blame` to establish why smoke tests require Workstation GC;
- compare Workstation GC, two-heap Server GC, and DATAS as separate runs;
- measure total RSS, managed heap, GC pause, allocation rate, and page-cache
  pressure;
- keep the current configuration unless a candidate improves wall time without
  reducing OOM margin.

The report's 50-55 percent heap-limit recommendation depends on Phase 4 memory
reductions and is not safe to apply to the current 10-11 GiB baseline.

The user-level performance profile and `--mode` deliberately do **not** change
GC. `fast` only relaxes the application-level worker halving; the 75% heap hard
limit, Workstation GC, concurrent GC, and RetainVM are unchanged, so RSS still
has a hard ceiling. RAM in the profile is a soft budget that derives the worker
count and container threshold, never a GC limit.

### Concurrency

Superseded. The earlier guidance to defer parallelism and cap concurrency at
two has been overridden by the implemented Phase 2 bounded multi-core work
(per-file and object-range parsing, AssetMap scanning, and the container
decode pipeline), which already isolates per-worker manager/stream state,
synchronizes shared resource reads and counters, keeps FBX export serial, and
demonstrated concurrent named workers on the 4-core Debian server.

Building on that, performance is now user-selectable through
`~/.anime/config.json` and `--mode default|limit|fast` (see
`PLAN_PERFORMANCE_PROFILE.md`). Performance metrics are optimization budgets to
fill for speed, not throttles: `fast`/`limit` disable the memory-stable
worker halving and scale workers to the machine or the configured
`maxMemoryKB`/`cpuCores`; `default` keeps the conservative halving and the RSS
gates. The remaining historical caution still applies only to `default`/`limit`:
keep peak retained memory under the established gates.

### CPU and Native Optimizations

LZ4 SIMD, batched FBX P/Invoke, Oodle/FBX compiler flags, native Zstd, and
io_uring remain conditional work. Promote one only when a profile shows that
its exact code path materially contributes to wall-clock time.

## Directions Rejected for the Next Step

- Do not change every file-backed `FileStream` buffer from 1 byte to 64 KiB.
  The 1-byte setting is an intentional response to the prior multi-gigabyte
  RSS increase from many live buffers.
- Do not add a configurable larger default buffer before the read-side
  lifetime problem is solved and Debian memory headroom improves.
- Do not apply global `sysctl`, scheduler, readahead, CPU governor, THP, or
  swap changes as repository defaults. They are host-specific experiments and
  require a captured baseline plus rollback instructions.
- (Superseded) The earlier "do not introduce object-level parallelism" rule
  applied to the initial AssetMap spool conversion only. Bounded object-range
  parallelism has since been implemented under the global worker budget;
  unbounded channels and a wholesale producer-consumer rewrite remain rejected.
- Do not claim the source report's estimated multipliers as achieved gains.

## Suggested Delivery Order

1. Resolve Phase 1 compatibility policy and complete its final checks.
2. Capture the AssetMap baseline and add stage timing instrumentation.
3. Add the internal entry spool with lifecycle and failure tests.
4. Convert AssetMap construction to per-file bounded production.
5. Stream XML output and verify fixtures.
6. Stream JSON output and input.
7. Stream MessagePack output and input after compatibility prototyping.
8. Run local build, smoke, package, cleanup, and output checks.
9. Push the exact commit and run Debian memory and performance regression.
10. Use the profile to select at most one conditional optimization for the
    following work package.
