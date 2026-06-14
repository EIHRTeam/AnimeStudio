# Current Phase Plan: Asset Map Streaming

## Objective

Remove the process-wide `List<AssetEntry>` lifetime from AssetMap generation
and consumption. Use a versioned disk-backed entry spool so XML, JSON, and
MessagePack maps can be written and filtered with bounded memory while
preserving existing schemas, filter order, and output compatibility.

## 1. Baseline and Compatibility Fixtures

- Add stage timing for loading, object scanning, container resolution,
  filtering/spooling, and each map writer without changing default output
  semantics.
- Generate committed small XML, JSON, and MessagePack fixtures with the current
  writer before replacing it.
- Record the exact fixture hashes and verify all old fixtures remain readable.
- Establish one fixed Debian AssetMap command and capture wall time, peak RSS,
  managed GC metrics, temporary disk peak, final map size, and `iostat`.

## 2. Disk-Backed Entry Spool

- Introduce an internal versioned, length-delimited spool record.
- Create spools under the existing AnimeStudio process temporary directory and
  reuse its locking, stale cleanup, free-space checks, and explicit failures.
- Append entries incrementally and keep an exact record count.
- Support repeated bounded enumeration for multiple output formats.
- Clean up spool data and lock files on success, cancellation, parse failure,
  simulated OOM, and disk failure.

## 3. Bounded AssetMap Construction

- Preserve current type, name, and container filter order.
- Retain only per-input-file object relationships while scanning that file.
- Spool enough unresolved data to apply `ResourceIndex` container resolution
  after all source files have contributed.
- Release each file's loaded assets, object dictionaries, and relationship
  lists before processing the next file.
- Do not introduce object-level parallelism or unbounded channels.

## 4. Streaming Writers

- XML: keep the existing document shape and `XmlWriter` representation while
  writing one entry at a time.
- JSON: write the existing wrapper and `AssetEntries` array incrementally.
- MessagePack: preserve key layout, enum representation, array lengths, and
  LZ4 block-array compatibility. Prototype against the legacy fixture before
  replacing `MessagePackSerializer.Serialize`.
- If MessagePack cannot remain byte-compatible, stop and record an explicit
  format policy rather than silently changing it.

## 5. Streaming Readers

- Keep the XML reader incremental.
- Parse JSON entries one at a time and retain only unique matching `Source`
  values.
- Parse MessagePack incrementally or through a bounded disk-backed adapter.
- Preserve current type, name, and container matching behavior and source
  ordering.

## 6. Automated Acceptance

- Synthetic large-map tests prove entries are not retained in a global list.
- XML and JSON structures remain equivalent to legacy fixtures.
- MessagePack legacy fixtures remain readable and new output satisfies the
  approved compatibility policy.
- Old and new readers return identical filtered source sets.
- Repeated spool enumeration returns identical entries without retention.
- Cancellation, parse failure, disk-full simulation, and simulated OOM leave
  no spool or lock residue.
- `dotnet build AnimeStudio.sln -c Release`, Core smoke, and all three RID
  package smoke checks pass.
- `git diff --check` and active-file legacy reference scans pass.

## 7. Debian Acceptance

- Push the exact validation commit and check it out on the authorized Debian
  13 server.
- Re-run the fixed AssetMap command and record wall time, peak RSS, GC metrics,
  temporary disk peak, final map size, disk metrics, cleanup, and `oom_kill`.
- Require lower peak retained memory than the baseline without increasing the
  existing container-only or full Convert memory regressions.
- Preserve output compatibility for every enabled map format.

Phase 2 is complete only when every automated and Debian criterion passes.
