# Architecture Decision Records

Short records of the decisions that shaped Job Scout: the context, what was decided, which alternatives
were considered and what it costs. One decision per file; a decision is never edited away – when it changes,
a new ADR supersedes the old one (status "Superseded by ADR-00xx").

Format: lightweight version of [Michael Nygard's ADR](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions).

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-modular-monolith.md) | Modular monolith with two deployables, not microservices | Accepted |
| [0003](0003-layering-ports-and-adapters.md) | Layering: Domain / Application / Infrastructure / hosts | Accepted |
| [0004](0004-own-llm-port.md) | Own narrow LLM port over the native Anthropic SDK; model tiers | Accepted |
| [0005](0005-structured-ai-output.md) | Structured AI output; AI results stored as JSON, queried fields as columns | Accepted |
| [0006](0006-job-source-scraping.md) | Job source: HTML parsing + headless browser fallback, never internal APIs | Accepted |
| [0007](0007-search-breadth-then-filter.md) | Search breadth first (categories + age filter), then a filtering funnel | Accepted |
| [0008](0008-persistence-ef-core.md) | EF Core directly – no generic repository, no MediatR; SQLite → PostgreSQL | Accepted |
| [0009](0009-ids-and-time.md) | Guid v7 identifiers, time passed in as a parameter | Accepted |
| [0010](0010-rich-domain-model.md) | Rich domain model; status history without a strict state machine | Accepted |
| [0011](0011-authentication-bff.md) | Authentication: BFF with Microsoft/Google OIDC, invite links, CSRF header | Accepted |
| [0012](0012-multiple-sources-search-intent.md) | Multiple job sources: source-agnostic search intent, per-source search plans | Proposed |
| [0013](0013-manual-job-ads-private.md) | Manually added job ads are private to the user | Proposed |
| [0014](0014-dev-sign-in-test-personas.md) | Dev sign-in and test personas; the app is not IT-only | Accepted |
| [0015](0015-jobscz-profession-resolution.md) | jobs.cz professions resolved by verified URL slugs, not a full catalogue | Accepted |
| [0016](0016-llm-cost-tracking.md) | LLM cost tracking: recording decorator, ambient context, micro-dollars | Accepted |
| [0017](0017-access-requests-admin-approval.md) | Access by request and admin approval; suspending users to cap AI costs | Proposed |

## Template

```markdown
# ADR-00xx: Title

- Status: Proposed | Accepted | Superseded by ADR-00yy
- Date: YYYY-MM-DD

## Context
What forces are at play – requirements, constraints, what we measured.

## Decision
What we do.

## Alternatives considered
What else was on the table and why not.

## Consequences
What gets easier, what gets harder, what we must watch.
```
