# Repository Agent Instructions

AnimeStudio is a CLI-only .NET 10 project.

Before changing code:

1. Read `ROADMAP.md`.
2. Read `PLAN.md`.
3. Read `STATUS.md`.
4. Update the current session entry in `STATUS.md` before editing code.

Development rules:

- Functional branches use the `feat/...` prefix.
- A session belongs to exactly one phase from `ROADMAP.md`.
- `PLAN.md` contains only the detailed plan for the active phase.
- Preserve prior session records in `STATUS.md`.
- Finish every development session by recording completed work, validation,
  unresolved work, and follow-up cautions in `STATUS.md`.
- Do not advance phases until every acceptance criterion in `PLAN.md` passes.
- The only supported managed target framework is `net10.0`.
- Do not reintroduce the removed desktop GUI or AppHost patcher.
- Unity serialized type names containing `GUI`, such as `GUIStyle`, are data
  format concepts and must not be removed as desktop GUI remnants.

Documents under `docs/archive/` describe earlier repository states. They are
references only; `ROADMAP.md`, `PLAN.md`, and `STATUS.md` are authoritative.
