# ADR-0009: Guid v7 identifiers, time passed in as a parameter

- Status: Accepted
- Date: 2026-10-06

## Context
- Entities are created in application code (job offers from search results, history entries) and should have an
  identity before they are saved.
- IDs will appear in URLs of a public web app.
- Business rules depend on time ("seen in the last 24 hours", status history) and must be testable.

## Decision
- **Identifiers are `Guid.CreateVersion7(now)`** (.NET 9+), generated in the domain.
- **Domain methods take `DateTimeOffset now` as a parameter**; the application gets it from `TimeProvider`.
- Times are stored with offset (`DateTimeOffset`), produced in UTC.

## Alternatives considered
- **Database identity (int)** – needs a round-trip to get the ID, leaks counts in URLs (`/jobs/1234`).
- **Random Guid v4** – fragments indexes because inserts land randomly in the B-tree.
- **`DateTime.UtcNow` inside entities** – tests become time-dependent and flaky.

## Consequences
- v7 IDs are time-ordered (timestamp in the first part) → good index locality, and string order = creation order.
- Slightly larger keys than int (16 bytes) – irrelevant at this scale.
- Deterministic tests: "seen yesterday" is always yesterday.
