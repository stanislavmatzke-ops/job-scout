# ADR-0015: jobs.cz professions resolved by verified URL slugs, not a full catalogue

- Status: Accepted
- Date: 2026-10-07

## Context
- ADR-0007/0012: searching by the source's professions has far better recall than free text, and the
  AI should translate a source-agnostic search intent into a per-source plan.
- Investigation of jobs.cz (7 Oct 2026, public pages only):
  - A profession listing is `/prace/{slug}/` (optionally `/prace/{location}/{slug}/`); the slug is the profession
    label without diacritics: Lakýrník → `lakyrnik`, Zedník → `zednik`, Stavbyvedoucí → `stavbyvedouci`,
    Zdravotní sestra → `zdravotni-sestra`, SW architekt → `sw-architekt`. An unknown slug returns **404**
    (`autolakyrnik`, `softwarovy-architekt`).
  - The full profession list exists only behind the internal autocomplete API (`/api/…`), which robots.txt
    disallows and ADR-0006 forbids. No public page or sitemap lists all professions.
  - Free text is much weaker than a profession: "autolakýrník" ≈ 3 ads nationwide vs. profession Lakýrník ≈ 43.
  - `locality[radius]=N` works with a location + profession (Kolín + Lakýrník: 30 km → 5 ads, 50 km → 13).

## Decision
- No hand-maintained full catalogue. A profession is **resolved on demand**: the AI proposes catalogue-style
  labels for the candidate's professions (e.g. "autolakýrník" → "Autolakýrník", "Lakýrník"), the source turns
  each into a slug and **verifies it with one GET of the public listing page** (200 = exists, 404 = not).
  `IJobSource.ResolveCategoryAsync(label)`.
- Verification runs only when a profile version gets its search plan; the verified plan is stored with the
  profile (`UserProfile.SearchPlanJson`), so the daily run makes no extra requests.
- Professions with no verified category fall back to a free-text query (`q[]`) – nothing is silently dropped.
- Search uses the home location + commute radius (`locality[radius]`) from the search intent.
- The small verified IT list (`JobsCzCatalog`) stays as known categories (no request needed) and as examples
  for the AI.

## Alternatives considered
- **Download the catalogue from the autocomplete API** – complete, but an internal API disallowed by robots.txt.
- **Hand-written catalogue of hundreds of professions** – laborious, goes stale.
- **Free text only** – measured recall is an order of magnitude lower.

## Consequences
- A few extra polite requests per new profile version (≈ 5–15), none per day.
- A label that happens to match a location slug (e.g. a town name) would resolve as a category – professions
  from the AI are job titles, so accepted; can be detected later by the page title if needed.
- If jobs.cz changes its slug scheme, verification starts returning 404 and the plan falls back to free text
  (degrades, does not break) – visible in the run log.
