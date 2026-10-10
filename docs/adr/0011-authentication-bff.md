# ADR-0011: Authentication – BFF with Microsoft/Google OIDC, invite-only

- Status: Accepted (implemented in phase 5e, 2026-10-10)
- Date: 2026-10-01, refined 2026-10-10

## Context
- Users sign in with a Microsoft or Google account; access is invite-only (CVs are personal data).
- The frontend is an Angular SPA; storing access tokens in the browser exposes them to XSS.
- The app never calls Microsoft or Google APIs on the user's behalf – it only needs to know *who* signed in.

## Decision
- **Backend-for-Frontend (BFF):** the ASP.NET Core API performs the OpenID Connect sign-in (authorization code flow
  with PKCE) with the Microsoft identity platform (`/common` – work and personal accounts) and Google, and keeps the
  session in its own **HttpOnly, Secure, SameSite=Lax cookie** (`__Host-jobscout`). The SPA holds no tokens; provider
  tokens are not saved (`SaveTokens = false`) – there is nothing to call with them.
- After the provider's callback, the external principal is **replaced by our own**: the cookie carries our user id,
  not the provider's claims. Dev sign-in (ADR-0014) produces exactly the same cookie.
- User identity = **provider + subject** (`sub` claim), never the e-mail. For Microsoft `/common` the issuer differs
  per tenant and is validated with `AadIssuerValidator`.
- **Invitations are one-time links**, not e-mail allow-lists: a random 128-bit code, only its SHA-256 hash stored,
  valid 14 days, used up by the first *new* identity that signs in with it. Known users never need one.
  (E-mail claims are not reliably verified across providers – binding to an e-mail would let someone with a
  look-alike address in their own Microsoft tenant in.)
- **CSRF:** state-changing `/api` requests must carry the header `X-CSRF: 1`. Browsers can't add a custom header to a
  cross-site request without a CORS preflight, which the API never allows. Together with SameSite=Lax this replaces
  antiforgery tokens (also for the multipart CV upload).
- Unauthenticated API calls get **401**, never a redirect; the login is started by `GET /api/auth/login/{provider}`.
  Return URLs are local paths only (no open redirect).

## Alternatives considered
- **Tokens in the SPA (MSAL / oidc-client in the browser)** – common, but tokens are reachable by injected
  scripts; current guidance for browser-based apps recommends BFF.
- **Microsoft Entra External ID** as the single identity broker – one integration for both providers, but more
  Azure setup; may be revisited.
- **Own username/password** – storing credentials is a liability with no benefit here.
- **E-mail allow-list instead of invitation links** – simpler, but trusts the e-mail claim (see above).
- **Antiforgery tokens** – work, but need a token endpoint and plumbing in the SPA; the custom-header pattern is
  what BFF frameworks (e.g. Duende BFF) use for APIs.

## Consequences
- The Angular app must send `X-CSRF: 1` on every call (one HTTP interceptor) and be served from the same site as the API.
- Each provider needs an app registration with the redirect URI `https://<host>/signin-microsoft` / `/signin-google`;
  client secrets live in user secrets locally and Key Vault in Azure. A provider without configuration is simply
  not offered (`GET /api/auth/providers`).
- One identity per user for now – the same person signing in with Microsoft and later with Google is two users.
  Linking identities is backlog.
