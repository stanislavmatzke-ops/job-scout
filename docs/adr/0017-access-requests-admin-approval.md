# ADR-0017: Access by request and admin approval; suspending users to cap AI costs

- Status: Proposed (implementation in phase 5f, e-mail notification in phase 7)
- Date: 2026-10-10
- Amends: ADR-0011 (invite-only access)

## Context
- ADR-0011 made the app invite-only with one-time links. In practice the owner doesn't want to hand out links:
  anyone interested should be able to sign in with Microsoft/Google and **ask** for access.
- Every active user costs real money for AI (≈ $0.1–0.4 per day). Someone who only tried the app must not keep
  burning tokens every morning.
- The owner needs an overview: who uses the app, what each user costs, and a way to stop a user.

## Decision
- **Any Microsoft/Google identity may sign in.** A new user is created with access status **Pending**; the API
  serves only the sign-in status and the "request access" endpoint, everything else answers 403 with the status.
- The user **requests access** (optional message). The **admin approves** → status **Approved**; the user is told by
  e-mail (phase 7) and gets in on the next sign-in. Rejecting = staying Pending with a note.
- The admin can **suspend** an approved user with a reason (e.g. capacity): the user can still sign in and read
  their data, but the morning run skips them and nothing that calls the AI is allowed; the UI shows the reason and
  a "request again" button. Resume = Approved again.
- **Admin** = identities listed in configuration (`Auth:Admins`, provider + subject of the owner's Microsoft account),
  not a database flag – a compromised database can't make anyone an admin.
- **Admin panel** (API in 5f, UI in phase 6): users with status, sign-in dates, profile yes/no, AI cost for the last
  7 / 30 days (from `LlmCall`, ADR-0016); approve / suspend / resume.
- Invitation links (ADR-0011) stay as a shortcut: signing in with a valid invitation = approved immediately.

## Alternatives considered
- **Invite links only** – the owner has to know people in advance; no self-service.
- **Open registration** – unbounded AI costs.
- **Hard monthly token quota per user** – may come later; manual suspension is enough for a handful of users.

## Consequences
- `User` gets an access status (+ requested/approved/suspended timestamps and reasons) – a migration.
- Every user-facing endpoint needs an "approved" check (one authorization policy) – Pending/Suspended see only
  what the policy allows.
- The Worker runs only for Approved users with a profile.
- Approval e-mail needs an e-mail service (Azure Communication Services or similar) – phase 7.
