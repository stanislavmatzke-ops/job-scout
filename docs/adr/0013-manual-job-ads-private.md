# ADR-0013: Manually added job ads are private to the user

- Status: Accepted (implemented 2026-10-15)
- Date: 2026-10-07, updated 2026-10-15

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

## Implementation (2026-10-15)
- UI: "Přidat nabídku" on the job list – pasted text, a file (PDF, .docx, .txt/.md, a saved e-mail **.eml**) or a link,
  the status the job already has (e.g. Applied), and "evaluate now" (on by default).
- **Link**: `IJobSource.ExternalIdFromUrl` recognises the source's own ads (jobs.cz `/rpd/{id}/`) – those become
  the normal *public* offer (shared, no AI to read them). Any other page is downloaded by `WebPageReader` and read by
  the AI into a private ad. The address comes from a user, so the HttpClient connects **only to public IP addresses
  on ports 80/443**, checked in `SocketsHttpHandler.ConnectCallback` – also after redirects and whatever DNS answers
  (SSRF: no loopback, private ranges, link-local/cloud metadata). No proxy, 20 s timeout, 5 MB, no browser: a page
  rendered only by JavaScript gives too little text and the user is asked to paste the text instead.
- **E-mail (.eml)** is taken apart by **MimeKit** (Infrastructure, behind `IEmailReader`): encodings and charsets,
  an e-mail forwarded as an attachment, PDF/Word/text attachments. Outlook `.msg` is not supported (save as .eml).
- **Extraction** (`JobAdExtractor`, cheap model, `LlmPurpose.JobImport`) returns title, company, location and
  `isJobAd`; the ad's text is asked back only for a PDF – pasted text, e-mails and web pages are stored as we have them.
- `JobMatch.AddedManually`: the job waits in "Nové" without triage; **the morning run never evaluates it**
  on its own – the user decides when to spend the AI (the evaluate button in the detail works as for any job).
- Limits (ADR-0021): a new click kind `JobImport` (10 per 24 h); with "evaluate now" the evaluation limit is checked
  too, both before anything is read, called or saved.
- Privacy: offers are only ever read through the owner's matches; tests prove another user sees neither the list
  item nor the detail.

## Alternatives considered
- **Separate table for manual ads** – duplicates the whole evaluation/status pipeline.
- **Treat manual ads as shared** – leaks personal data (recruiter e-mails often contain names and phone numbers).

## Consequences
- One migration (nullable column + index).
- A test must prove that a private ad never appears for another user.
- Extraction prompt must treat the input as untrusted (prompt injection, ADR-0005).
