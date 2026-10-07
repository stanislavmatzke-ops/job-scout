# ADR-0011: Authentication – BFF with Microsoft/Google OIDC, invite-only

- Status: Proposed (implementation in phase 5)
- Date: 2026-10-01

## Context
- Users sign in with a Microsoft or Google account; access is invite-only (CVs are personal data).
- The frontend is an Angular SPA; storing access tokens in the browser exposes them to XSS.

## Decision
- **Backend-for-Frontend (BFF):** the ASP.NET Core API performs the OIDC sign-in with Microsoft identity platform
  (personal + work accounts) and Google, and keeps the session in an **HttpOnly, Secure, SameSite cookie**.
  The SPA holds no tokens.
- User identity = **provider + subject** claim, never the e-mail.
- Invite check on first sign-in; unknown identities are rejected.

## Alternatives considered
- **Tokens in the SPA (MSAL / oidc-client in the browser)** – common, but tokens are reachable by injected
  scripts; current guidance for browser-based apps recommends BFF.
- **Microsoft Entra External ID** as the single identity broker – one integration for both providers, but more
  Azure setup; may be revisited.
- **Own username/password** – storing credentials is a liability with no benefit here.

## Consequences
- CSRF protection needed for cookie-authenticated state-changing requests (antiforgery / SameSite).
- API and SPA should be served from the same site (or carefully configured CORS).
