# Performance Profile & Modes — Phased Plan

This is a cross-cutting feature work plan. It does **not** replace `PLAN.md`
(which owns the active Phase 2 AssetMap streaming details), `ROADMAP.md`, or
`STATUS.md`. Each phase updates `STATUS.md` with what was done and follow-up
notes, and syncs the relevant local docs.

## Goal

Add a user-level performance profile at `~/.anime/config.json` plus
`--mode default|limit|fast`, so users can let AnimeStudio **optimize resource
use to run faster within a budget** instead of being held below the machine's
capability. Performance metrics are optimization budgets to fill, not throttles.

- **default**: backward-compatible conservative behavior (no config/flag).
  WorkerBudget halving ON; workers = logical CPU count; existing RSS gates hold.
- **limit**: maximize speed within the configured `maxMemoryKB`/`cpuCores`
  budget. The two levers are independent: the parse-worker halving is ALWAYS
  disabled in limit mode, while the RAM/CPU budget caps the worker *count*. A
  tight budget therefore yields fewer workers; it never re-enables the halving.
- **fast**: maximize speed using the whole machine (or config budget). Halving
  OFF; may exceed historical Phase 2 RSS gates, bounded by machine RAM /
  `maxMemoryKB`. Does **not** change GC; the 75% heap hard limit still applies.

Confirmed decisions: RAM = soft budget (no GC hard-limit change); CPU = worker
count only (no affinity); mode via `--mode` (CLI) over config; `--workers`
explicit wins; fast may exceed historical gates.

Open decision (pending owner): whether `default` should also become
optimization-oriented (would touch the Phase 2 RSS gates). Current
recommendation: keep `default` conservative; optimization lives in limit/fast.

## Config schema (`~/.anime/config.json`)

```jsonc
{
  "mode": "limit",                    // limit | fast | default; --mode overrides
  "maxMemoryKB": 14680064,            // soft budget; derives workers/threshold
  "cpuCores": 8,                      // worker cap + ThreadPool min threads
  "workers": 6,                       // config-level worker count (below --workers)
  "containerMemoryThresholdMiB": 192, // overrides appsettings streaming value
  "advanced": {
    "perWorkerMemoryEstimateKB": 1048576,
    "minimumWorkerThreads": 8,
    "halveParseWorkers": null
  }
}
```

`ANIMESTUDIO_CONFIG_PATH` overrides the discovery path (test/advanced use).

## Phases

### Phase 1 — Config model & loader (foundation, no behavior change) — IN PROGRESS
- [x] `AnimeStudio.CLI/PerformanceProfile.cs`: `PerformanceMode` enum,
      `PerformanceConfig`/`AdvancedOverrides` POCO, `Load()` (discovers
      `~/.anime/config.json`; `ANIMESTUDIO_CONFIG_PATH` override; read-only).
- [x] `Settings.SerializerOptions` widened `private` → `internal` for reuse.
- [x] CLI.Smoke `VerifyPerformanceConfigLoad`: parse, case-insensitive mode,
      corrupt-JSON tolerance, missing-file default.
- [ ] Build + smoke green; `STATUS.md` entry. **Not wired → behavior unchanged.**

### Phase 2 — Mode-aware WorkerBudget + resolver (core logic, still not wired) — DONE
- [x] `AnimeStudio/WorkerBudget.cs`: configurable static policy, default
      `halveRetainedWorkers = true` (current behavior verbatim); `ConfigureHalving`.
- [x] `AnimeStudio/AssetsHelper.cs`: `SetParseWorkerHalving(bool)` wrapper.
- [x] `AnimeStudio.CLI/PerformanceResolver.cs`: `ResolvedPerformance` + precedence
      chain & soft-budget formulas.
- [x] `AnimeStudio.CLI/Settings.cs`: `GetContainerStorageOptions(long? overrideMiB)`.
- [x] Core.Smoke: kept existing budget assertion; added `VerifyWorkerBudgetHalvingToggle`
      (finally restores `true`). Build + Core.Smoke green. Resolver behavior is
      exercised end-to-end through the CLI in Phase 3 (cleaner than reflective
      construction; the `ANIMESTUDIO_CONFIG_PATH` route needs Phase 3 wiring).

### Phase 3 — CLI wiring (activate) — DONE
- [x] `CommandLine.cs`: `--mode` option (`Option<PerformanceMode?>`, unset stays
      null to distinguish explicit `default`); `WorkersExplicitlySet` via
      `GetResult(Workers) is { Tokens.Count: > 0 }`.
- [x] `Program.cs`: load + resolve + apply to both AssetsManager instances,
      ThreadPool, ContainerStorageOptions, WorkerBudget policy, export path.
- [x] Observability: kept `Using N workers ...`; added `Performance mode: ...` line.
- [x] CLI.Smoke `VerifyPerformanceModeWiring`: fast (halving off), CLI `--mode`
      over config, explicit `--workers` precedence, backward compat, tolerance,
      worker=0 clamp to 1. Fixed an ambiguous-overload break in
      `VerifyStreamingConfiguration` (now selects the parameterless overload).
- [x] Build + Core.Smoke + osx-arm64 CLI.Smoke green.

### Phase 4 — Docs sync + Debian validation — DOCS DONE; DEBIAN PENDING
- [x] CLAUDE.md / ROADMAP.md / PLAN.md (additive; PLAN.md keeps Phase 2).
- [x] PERF_NEXT_STEP.md (updated stale "no parallelism / cap 2"; "no GC change"
      note; optimize-budget principle; Debian 4 CPU / 15 GiB RAM fact).
- [x] PERF_ANALYZE_REPO.md (**user-authorized exception** to the
      "do not modify" rule; additive "落地状态" note only).
- [ ] Debian `--mode default` vs `--mode fast` comparison; metrics in `STATUS.md`
      (needs server authorization; not run this session).
- [ ] Three-RID package smoke + commit/push (pending user decision on commit).

## Backward compatibility (no config, no --mode = today)

Mode=Default, workers=`ProcessorCount`, WorkerBudget halving ON
(`1→1,2→2,4→2,8→4`), container threshold=256, ThreadPool/LOH/GC unchanged,
RSS gates not regressed, `Using N workers` log preserved.
