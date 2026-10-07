# ADR-0012: Multiple job sources – source-agnostic search intent, per-source search plans

- Status: Proposed (search intent in phase 3c; second source after MVP)
- Date: 2026-10-07

## Context
- jobs.cz is the first source; prace.cz, StartupJobs and others should follow, plus job ads added manually.
- Today the AI picks categories directly from the jobs.cz catalogue (e.g. `sw-architekt`) – the profile is coupled
  to one source and a second source would need a different choice.
- The same job ad is often published on several portals (jobs.cz and prace.cz belong to the same group) and on the
  company's own site; `(Source, ExternalId)` does not detect that.

## Decision
- The analysed profile carries a **search intent independent of any source**: professions in plain language,
  seniority, locations with commute radius, remote preference.
- For every enabled source the intent is translated once per profile version into a **source search plan**:
  the AI chooses from *that* source's catalogue via a JSON Schema `enum`. Stored with the profile as
  `{ "<source>": { …plan… } }`.
- Sources are registered as multiple `IJobSource` implementations and can be enabled/disabled in configuration.
- Cross-source duplicates (later): a normalised fingerprint (company + title + location) groups duplicates;
  the user sees one job with several sources.

## Alternatives considered
- **One shared canonical taxonomy mapped by hand to every source** – stable, but a lot of manual mapping work for
  hundreds of professions; the AI mapping per profile is cheap and adapts to each catalogue.
- **Free-text search only** – measured low recall (ADR-0007).

## Consequences
- Adding a source = new adapter + its catalogue; profiles get a plan for it on the next run.
- Plans must be regenerated when a source catalogue changes (store catalogue version with the plan).
