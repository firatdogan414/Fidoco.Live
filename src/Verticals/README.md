# Verticals

Industry-specific modules live here, one project per vertical, for example
`Fidoco.Verticals.CombatSports` (fight card, bouts, fighters, results).

Rules (enforced by `tests/Fidoco.ArchitectureTests`):

- A vertical references only `Fidoco.SharedKernel` and `Fidoco.Contracts`.
- It owns its own `DbContext` and schema (e.g. `combat`) and links to the core through `EventId`.
- It reacts to core domain events (`StreamWentLive`, `EventEnded`, ...) and exposes its own
  endpoints under `/api/v1/...`.

No verticals are built in phase 1. See `ARCHITECTURE.md` for how to add one.
