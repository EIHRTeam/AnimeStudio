# Current Phase Plan: Container Streaming Acceptance Closure

## Objective

Close the final Phase 1 output-compatibility gate without misreporting
non-deterministic output as byte-identical. The CLI-only .NET 10 migration,
shared container storage, automated tests, and Debian memory gates are
implemented and passing.

## 1. Preserve Acceptance Evidence

- Keep the Debian manifests for the original baseline and the fresh streaming
  export.
- Record exact counts for common files, path-only differences, and hash-only
  differences.
- Do not use the interrupted/resumed export as compatibility evidence because
  a second run adds another set of random fallback filenames.

Current evidence:

- Both fresh trees contain 77,146 files.
- 77,020 deterministic files have identical relative paths and SHA256 values.
- Each tree has 88 JSON files with different fallback names but an identical
  multiset of content hashes.
- 38 common-path FBX files differ because the exporter embeds a creation time
  and generated file identifier.

## 2. Resolve the Compatibility Policy

The phase cannot be marked complete until one of these policies is explicitly
adopted:

1. Strict byte identity:
   - Replace `Path.GetRandomFileName()` export fallbacks with stable names.
   - Make FBX metadata deterministic.
   - Establish a new deterministic baseline and require exact tree equality.
2. Normalized compatibility:
   - Require exact path and SHA256 equality for deterministic outputs.
   - Compare random fallback outputs by content-hash multiset.
   - Compare FBX payloads with volatile metadata normalized.
   - Track deterministic naming and FBX metadata as a later compatibility
     work package.

The current roadmap keeps non-streaming compatibility work in the final phase,
so normalized compatibility is the scope-consistent option, but it must not be
silently substituted for the original strict criterion.

## 3. Final Phase 1 Checks

After the compatibility policy is resolved:

- Re-run `dotnet build AnimeStudio.sln -c Release`.
- Re-run Core smoke and all three RID package smoke checks.
- Run `git diff --check` and the active-file legacy reference scan.
- Confirm the Debian temporary run directory is empty.
- Confirm `oom_kill` remains unchanged.
- Update `STATUS.md` with the accepted policy and final commit.

## 4. Phase Transition

Only after the output gate closes:

- Mark Phase 1 completed in `STATUS.md` and `ROADMAP.md`.
- Create `feat/asset-map-streaming`.
- Replace this file with the detailed AssetMap streaming plan.

The next phase will remove the complete `List<AssetEntry>` build, add a
disk-backed entry spool, stream XML/JSON/MessagePack generation and filtering,
and preserve or explicitly version each existing map format.
