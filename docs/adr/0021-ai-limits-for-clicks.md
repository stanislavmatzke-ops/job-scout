# ADR-0021: AI limits for clicks

- Status: Accepted
- Date: 2026-10-13

## Context
The morning run is cheap and bounded: triage with the small model, at most 15 evaluations and 5 cover letters per run
(`EvaluateJobs.MaxPerRun`, `WriteCoverLetters.MaxPerRun`). The buttons are not. After the first real users (12 Oct) it
was clear that an approved user could spend AI tokens without limit: change one letter in the notes and the profile is
analysed again, "Napsat nový návrh" can be clicked forever, and every triaged-out job can be evaluated "anyway" one by one.
Access is approved by hand (ADR-0017), so this is about mistakes and curiosity rather than attacks – but one bored
afternoon could cost more than a month of morning runs.

## Decision
- **Count from what we already record.** Every AI call is an `LlmCall` with user, purpose, cost and time (ADR-0016).
  A *click* is a call for a user, outside a morning run (`ScoutRunId` is null), of a kind a button starts (Profile,
  Evaluation, CoverLetter). No new counters, nothing to keep in sync or reset.
- **A sliding 24-hour window** – no midnight, no time zones, nothing to wait out; the answer says exactly when a slot
  frees up (the oldest click leaves the window).
- **Limits** (configuration `AiLimits`, can be changed without a build):

  | Click | Limit |
  | --- | --- |
  | New profile analysis (new CV or changed notes) | 3 per 24 h – an unchanged CV costs nothing and isn't counted |
  | Cover letter (new or rewritten) | 10 per 24 h, and at most 2 rewrites per job, ever (`JobMatch.LetterRewrites`) |
  | Evaluation on demand | 20 per 24 h |
  | Job ad added by hand (reading text / file / e-mail / page, ADR-0013) | 10 per 24 h – the evaluation counts on its own |
  | All clicks of one user | 0.50 USD per 24 h |
  | All clicks of all users (emergency brake) | 5 USD per 24 h – AI clicks off for everybody, the admins get one e-mail |

- **Admins have no limits**, and their own spending doesn't count towards the emergency brake. The demo account uses no AI.
- **Checked before anything happens**: the use case throws `AiLimitExceededException` before the AI is called and before
  anything is stored (a refused CV upload doesn't replace the stored CV). The API answers **429 Too Many Requests** with
  `Retry-After` and a problem with `reason`, `limit` and `retryAt`; the UI words the message in the user's language.
- **One click per user at a time.** Two fast requests would otherwise both see "2 of 3 used" and both go through.
  A per-user semaphore in memory (`AiQuotaGate`) is enough – the API runs as one replica (ADR-0019).
- **The UI shows what is left** (`GET /api/me/limits`): "zbývá 2 z 3 za 24 hodin" under the buttons, disabled buttons
  with the time they free up. The admin page shows each user's click cost for the last 24 hours.
- **Last line of defence outside the app:** a monthly spending limit in the Anthropic Console.

## Alternatives considered
- **ASP.NET Core rate limiting middleware**: counts requests, not money, and forgets on restart; an unchanged CV would
  count as much as a new one. Useful against floods, not against cost.
- **Counters on the user (resetting at midnight)**: another state to keep right, a time zone to choose, and a user
  could spend twice the limit around midnight.
- **A monthly budget per user**: harder to explain ("you have 1.20 USD left this month") and a user could burn it in a day.
- **Limits on the morning run too**: it is already bounded per run; not needed for now.

## Consequences
- Abuse costs at most about 0.50 USD per user per day and 5 USD for the whole app – predictable.
- The counts include clicks whose result failed to parse (the tokens were paid); calls that failed outright are not counted.
- A profile re-analysis done by the morning run after a prompt change is recorded without a run, so it counts as a
  Profile click – rare (only when `ProfilePrompts.Version` changes) and harmless.
- With a second API replica the per-user semaphore would have to move to the database (or a lease); the limits
  themselves would still work, only two simultaneous clicks could both pass.
