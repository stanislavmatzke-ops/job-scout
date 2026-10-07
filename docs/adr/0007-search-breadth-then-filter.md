# ADR-0007: Search breadth first (categories + age filter), then a filtering funnel

- Status: Accepted
- Date: 2026-10-06

## Context
- First approach: the AI generated narrow free-text phrases ("tech lead .NET") → only a few results; whole
  relevant groups (e.g. Java roles) were missing.
- jobs.cz has professions/fields usable in the URL (verified: `/prace/praha/sw-architekt/` = 88 ads), an age
  filter (`?date=24h`, `?date=3d`) and paging (30 per page; past the end the last page repeats).
- Measured: 4 professions × Prague × 3 days = 32 unique ads, including relevant ones the phrase search missed –
  and also noise (SAP consultant, electrical team lead).

## Decision
Optimise **recall first, precision later**:
1. **Breadth:** search by professions from a closed, verified catalogue (the AI chooses from it via a JSON Schema
   `enum`, so it cannot invent a category), locations, only new ads (`24h`; first run `3d`), all pages.
2. **Funnel:** deduplicate → drop already seen → cheap triage on titles/tags (Fast model) → fetch details only for
   promising ads → full evaluation (Smart model) → cover letter for score ≥ 7.

## Alternatives considered
- **Precise phrases only** – cheap but misses good ads; recall can't be added afterwards.
- **Evaluate everything with the Smart model** – expensive and slow, details for all ads would also mean
  many more requests to the site.

## Consequences
- Daily volume is predictable (tens of ads per profile), cost dominated by a few Smart evaluations.
- The category catalogue must be maintained; unknown slugs return 404 and are skipped with a warning.
