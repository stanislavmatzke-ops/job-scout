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
- **Learned catalogue (revised 7 Oct, before implementation):** every verification result is stored in a shared
  table `SourceCategories` (Source, Slug, Label, Exists, CheckedAt) – positive *and* negative. Next time any user
  needs the same profession on the same source, the stored answer is used (no request, no guessing). Results are
  re-checked after a while (e.g. 90 days) because portals add and remove professions.
- The AI is given the already **verified categories of that source** and must prefer them – so "Lakýrník" is always
  mapped to `lakyrnik` the same way, instead of a slightly different guess every time. The catalogue grows from
  public pages only, one profession at a time.
- Professions with no verified category fall back to a free-text query (`q[]`) – nothing is silently dropped.
- **Update 22 Oct 2026 (plan v4):** the 3 most important job titles are *also* searched as free text, after all
  categories. Categories still give the best recall per search, but employers file the same job under different
  categories: in one day, "architect" as text found 30+ ads, the category `sw-architekt` 12. The text searches come
  last, so when the cap of 12 searches is reached they are what is left out; the noise they bring (e.g. building
  architects for "architekt") is cheap for the triage to drop.
- **Update 23 Oct 2026 (plan v5): the whole field first.** The AI also names the portal's broad field ("obor") of the
  candidate's main profession; it is verified like a category and searched first – the way a person reads the
  portal: every ad of the field, by its title. IS/IT: Vývoj aplikací a systémů had 90 ads a day, all our narrower
  searches together 58. The triage (titles only, 40 per cheap call) sorts it out for well under a cent a day. The
  professions' categories stay after it, for a field too big for the page limit. Not sure, or a field of mostly other
  work → no field. A category counts as verified only if jobs.cz keeps it on www.jobs.cz (`/prace/prodej-a-obchod/`
  redirects to beta.jobs.cz, whose pages the parser doesn't know).
- Search uses the home location + commute radius (`locality[radius]`) from the search intent.
- The small verified IT list (`JobsCzCatalog`) stays as known categories (no request needed) and as examples
  for the AI.

## Alternatives considered
- **Download the catalogue from the autocomplete API** – complete, but an internal API disallowed by robots.txt.
- **Hand-written catalogue of hundreds of professions** – laborious, goes stale.
- **Free text only** – measured recall is an order of magnitude lower.

## Consequences
- A few extra polite requests per new profile version (≈ 5–15), fewer over time as the learned catalogue grows;
  none per day.
- A label that happens to match a location slug (e.g. a town name) would resolve as a category – professions
  from the AI are job titles, so accepted; can be detected later by the page title if needed.
- If jobs.cz changes its slug scheme, verification starts returning 404 and the plan falls back to free text
  (degrades, does not break) – visible in the run log.
