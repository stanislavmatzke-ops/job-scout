# ADR-0006: Job source – HTML parsing + headless browser fallback, never internal APIs

- Status: Accepted
- Date: 2026-10-06

## Context
- jobs.cz has no public API. Search results (`/prace/…`) and most job details (`/rpd/{id}/`) are server-rendered
  HTML; robots.txt allows these paths.
- Measured: **~45 % of job details (9 of 20)** redirect to company career pages (`company.jobs.cz`) whose content
  is loaded by JavaScript from an internal GraphQL API. Plain HTTP gets only a meta-tag snippet (~300 characters).
- Losing half of the ads would defeat the purpose of the app.

## Decision
A fallback chain inside `JobsCzSource.GetDetailAsync`:
1. **HTTP + HTML parsing** (AngleSharp, stable `data-test` attributes) – fast path for ~55 %.
2. When redirected: **headless Chromium (Playwright)** renders the career page, waits for `.cp-detail__content`,
   blocks images/fonts/styles/trackers; HTML is then parsed by the same pure parser.
3. If that fails: meta-tag snippet with `IsComplete = false`. **A job ad is never silently dropped.**

Politeness: one request at a time, ≥ 2 s between requests (`PoliteDelayHandler`), honest User-Agent,
the browser is started lazily and only for redirected details.

## Alternatives considered
- **Calling the internal GraphQL API** – unofficial, can change without notice, bypasses how the site is meant to
  be used; not acceptable in a public repository.
- **Headless browser for everything** – slow and heavy where plain HTML works.
- **Accepting snippets only** – too little information for a meaningful evaluation.

## Consequences
- The worker image must contain Chromium (official `mcr.microsoft.com/playwright/dotnet` base image).
- Parsers depend on the site's markup → covered by unit tests on fictional fixtures with the same structure;
  unknown career page templates are logged as warnings with the host name.
- Natural first candidate for extraction into a separate service (ADR-0002).
