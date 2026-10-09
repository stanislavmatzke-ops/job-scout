# ADR-0016: LLM cost tracking – recording decorator, ambient context, micro-dollars

- Status: Accepted
- Date: 2026-10-09

## Context
- First real runs cost $0.30–0.35 (IT profile, 3-day window); the cloud phase needs per-user and per-run costs,
  later quotas and a "what does a user cost per month" figure for the README.
- Five components call the model (profile, search plan, triage, evaluation, cover letter); adding cost code to each
  would duplicate it and be easy to forget in the next one.
- A paid call must be recorded even when the use case then fails (e.g. a truncated letter, an exception later).

## Decision
- **Decorator** `UsageRecordingLlmClient` around the real `ILlmClient` (Infrastructure). Every call – successful or
  failed – becomes one `LlmCall` row: purpose, model, tokens, cost, duration, user, run, error.
- **Purpose** is part of the request (`LlmRequest.Purpose`), set by each component.
- **Who / which run** comes from an ambient `LlmCallContext` (`AsyncLocal`) set by the host (Worker, later API) and by
  `AnalyzeProfile`; use cases and the domain stay unaware of cost tracking.
- Rows are written through **`IDbContextFactory`** (own short-lived context), independent of the use case's unit of
  work → recorded even if the use case rolls back. A recording failure is logged and never breaks the AI call.
- **Cost in micro-dollars (`long`)** computed at call time from prices in configuration (`Claude:Pricing`, per model
  family, dated snapshots like `claude-haiku-4-5-20251001` match `claude-haiku-4-5`). Stored, because prices change.
  Integers are exact and summable in SQL (SQLite can't `SUM` decimals server-side).
- `LlmCall` has **no foreign keys** (UserId, ScoutRunId): it is an audit log written before a new user is saved
  (profile analysis) and must not block work; account deletion removes it explicitly.

## Alternatives considered
- **Explicit logging in every use case** – duplication, easy to forget, mixes concerns.
- **Passing userId/runId through every call** – touches every signature for a cross-cutting concern.
- **Cost from the provider's billing API** – delayed, not per user/run.

## Consequences
- Ambient context is implicit: a call made outside `LlmCallContext.Begin` is recorded without user (visible as null).
- Prices must be kept up to date in configuration; unknown model → cost 0 + warning.
- Basis for quotas (phase 5+) and the cost section of the README (phase 7).
