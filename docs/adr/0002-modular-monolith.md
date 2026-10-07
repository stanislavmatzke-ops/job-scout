# ADR-0002: Modular monolith with two deployables, not microservices

- Status: Accepted
- Date: 2026-10-07

## Context
- One developer, one bounded domain (finding and tracking job offers), one database.
- Load is tiny: tens of new job ads per day, a handful of users (invite-only).
- Two clearly different run modes: an interactive web app and a once-a-day batch (search → triage → evaluate).
- One part has a distinct operational profile: scraping company career pages needs a headless browser
  (heavy image, different failure modes – the external site can change at any time).

## Decision
A **modular monolith** – one codebase with explicit module boundaries (interfaces such as `IJobSource`,
`ILlmClient`, `IPageRenderer`) – deployed as **two units sharing one database**:
- **Web API** (+ Angular UI) – interactive, always on;
- **Worker** – the morning run, executed as a scheduled job (Azure Container Apps Job).

## Alternatives considered
- **Microservices** (e.g. scraper, evaluator, user service). They solve organisational scaling (many teams
  deploying independently) and independent scaling of parts with very different load. Neither applies here.
  Costs would be real: distributed transactions, network failures between services, data consistency across
  boundaries, more pipelines, more observability, higher hosting cost.
- **Single deployable** (API with a hosted background service). Simpler, but a long scraping/AI batch would
  share resources with the web app, and the scheduled job model maps better to "run once, exit, report".

## Consequences
- Simple local development, one transaction scope, one migration path.
- The boundaries are where a split would happen if needed. **First candidate: the scraper** (headless browser,
  different scaling and failure profile) – it would become a service behind a new `IJobSource` implementation
  without changes to the rest of the system.
- Discipline needed: modules must talk through their interfaces, not reach into each other's internals.
