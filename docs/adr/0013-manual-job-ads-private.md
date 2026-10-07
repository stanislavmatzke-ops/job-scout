# ADR-0013: Manually added job ads are private to the user

- Status: Proposed (use case in phase 4, UI in phases 5–6)
- Date: 2026-10-07

## Context
- Users want to add a job ad themselves: a URL, pasted text, a PDF, or a forwarded e-mail from a recruiter.
- `JobOffer` was designed as shared by all users (one row per public ad). A recruiter's e-mail or a PDF sent to one
  person must not become visible to others.
- Manual input is unstructured; it needs the same shape as scraped ads (title, company, location, text).

## Decision
- `JobOffer` gets a nullable **`OwnerUserId`**: `null` = public ad from a source (shared), set = private ad visible
  only to its owner. Manual ads use `Source = "manual"` and a generated `ExternalId`.
- Use case **`AddJobManually`**:
  1. URL → if an `IJobSource` can handle it (`CanHandle(url)`), fetch it as a normal public ad;
     otherwise download the page and extract with AI.
  2. Text / PDF / e-mail → **AI extraction** into a structured job ad (documents are sent to the model like the CV).
  3. Then the normal evaluation (+ cover letter) for that user.
- Queries for "my jobs" always filter by `OwnerUserId IS NULL OR OwnerUserId = me` (enforced in one place).

## Alternatives considered
- **Separate table for manual ads** – duplicates the whole evaluation/status pipeline.
- **Treat manual ads as shared** – leaks personal data (recruiter e-mails often contain names and phone numbers).

## Consequences
- One migration (nullable column + index).
- A test must prove that a private ad never appears for another user.
- Extraction prompt must treat the input as untrusted (prompt injection, ADR-0005).
