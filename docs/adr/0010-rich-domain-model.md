# ADR-0010: Rich domain model; status history without a strict state machine

- Status: Accepted
- Date: 2026-10-06

## Context
- A job match has two independent dimensions: where the **pipeline** got (found / triaged out / evaluated) and
  what the **user** did (new, saved, applied, call, interview rounds, offer, rejected, dismissed, hidden).
- The user wants a history of status changes and notes ("call with HR on Friday").
- People mis-click; a wrong status must be correctable.

## Decision
- **Rich domain model:** entities have private setters; changes go through methods that enforce rules
  (`ChangeStatus` always writes a `JobMatchEvent`, `ApplyEvaluation` rejects scores outside 1–10,
  `Refresh` moves `LastSeenAt` but never `FirstSeenAt`).
- Two enums: `MatchStage` (pipeline) and `MatchStatus` (user).
- **No strict state machine** for user statuses – any transition is allowed, every transition is recorded.
- One shared `JobOffer` per ad; one `JobMatch` per user × offer (unique) – a "refreshed" ad is never offered twice.

## Alternatives considered
- **Anemic model** (public setters, rules in services) – rules get forgotten in some code path.
- **Strict state machine** (only Applied → Call → Round1 …) – real processes skip steps and need corrections.
- **Event sourcing** – we need history, not state reconstruction from events; a history table next to the current
  state is simpler and queryable.

## Consequences
- Invariants are unit-tested on the entities themselves, no database needed.
- EF Core needs private parameterless constructors and backing-field mapping for collections.
- Reporting on transitions (e.g. "time from Applied to Call") is possible from the history table.
