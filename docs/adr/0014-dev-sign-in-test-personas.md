# ADR-0014: Development sign-in and test personas; the app is not IT-only

- Status: Accepted (personas in the Worker since phase 3c, dev sign-in in the API since phase 5a)
- Date: 2026-10-07

## Context
- Testing needs several users with very different profiles (e.g. a car body painter, a bricklayer, a software
  architect) – creating Microsoft/Google accounts for each is painful.
- Username/password accounts were rejected in ADR-0011 (storing credentials is a liability).
- The first prompts and the category catalogue were IT-specific ("you are an IT recruiter", technology examples,
  11 IT professions) – a car body painter would get meaningless searches and evaluations.

## Decision
- **Development sign-in:** a separate authentication scheme, registered only when explicitly enabled outside
  Production, lets you sign in as a test persona without a password. Identity `Provider = "dev"`,
  `Subject = <persona>`; everything else (profile, matches, statuses) runs exactly as for a real user.
  Start-up fails if it is enabled in Production; a test guards the registration.
- **Personas** live in `samples/personas/<name>/` (fictional `cv.md` + `preferences.md`). The Worker can run for a
  persona (`--Scout:Persona=<name>`), creating the `dev:<name>` user if needed.
- **Profession-neutral AI:** prompts and schema descriptions are written for any trade or profession
  ("skills", not "technologies"); the category catalogue covers all professions of the source, not only IT;
  the search intent includes a **commute radius** (trades search locally, not remotely).
- Personas double as **evaluation cases**: each has expectations (e.g. the painter gets painter jobs scored high,
  IT jobs scored low).

## Alternatives considered
- **ASP.NET Core Identity with passwords** – password storage, reset flows, brute-force protection; no benefit for testing.
- **Real Microsoft/Google test accounts** – work, but slow to create and manage.

## Consequences
- The dev scheme is a security-sensitive switch: it must be off by default and impossible in Production.
- Prompts need re-validation on IT and non-IT personas whenever they change (eval suite in phase 4+).
