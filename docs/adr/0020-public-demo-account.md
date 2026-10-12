# ADR-0020: Public demo account – "Try it without signing up"

- Status: Accepted
- Date: 2026-10-12

## Context
Access to Job Scout is by request and admin approval (ADR-0017). A recruiter or a contact from LinkedIn who just wants
to see the app would hit the "request access" page and leave. Dev sign-in (ADR-0014) can't help – it is forbidden in
Production, and it lets anyone become anybody. We need a way in that shows real content, costs (almost) nothing and
can't be abused.

## Decision
- **One shared fictional user** – provider `demo`, subject `honza`, "Honza Testovací": a .NET developer with 4 years
  on one product, Brno, hybrid (narrow on purpose – the search finds some jobs, not hundreds). The CV and notes are
  constants in the code (`DemoAccount`), no personal data anywhere.
- **The login page** shows "Vyzkoušet bez registrace" when `Demo:Enabled` is on (Production too); `POST /api/auth/demo`
  signs the visitor in as the demo user. Locally, with dev sign-in on, the editable persona field is shown instead.
- **Real content:** the admin prepares the account once ("Připravit ukázkový účet" – one AI analysis + the first search);
  afterwards the morning run searches for it like for any approved user (a few cents a day).
- **What a visitor can't do** – the `Active` policy (anything that calls the AI) and the new `Editor` policy (letter edits,
  settings) refuse the demo user; CV upload and notes are `Active`. Visitors *can* change statuses and write notes on jobs.
- **The UI says so:** a banner "Prohlížíte si ukázku…", AI buttons and letter editing hidden, a link to sign up.

## Alternatives considered
- **A fresh demo user per visitor**: private sandbox, but every visitor would need their own AI analysis and search – cost.
- **Read-only demo**: safest, but clicking statuses and notes is the point of trying the app.
- **Static screenshots / video**: no cost, but nothing to try.

## Consequences
- Visitors share one account: a status or note one visitor changes, the next one sees. **To do before the LinkedIn post:**
  a nightly reset of the demo's statuses and notes (in the morning job).
- The demo is a normal row in the admin overview (costs visible), can be suspended like anyone.
