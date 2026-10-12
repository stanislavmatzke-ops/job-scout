# ADR-0019: Hosting – one container image, Azure Container Apps (API) + Container Apps Job (morning run)

- Status: Accepted
- Date: 2026-10-12

## Context
Job Scout has two processes: the API (web app, sign-in, on-demand AI, first search after the first CV) and the
Worker (the morning run for every approved user). Both need the same code, the same database (ADR-0018) and a
headless Chromium for reading job postings (Playwright). Budget: a personal project – a few dollars a month,
no idle VMs, nothing to patch by hand.

## Decision
- **One Docker image** built from the repository root (`Dockerfile`, three stages): Angular build → `dotnet publish`
  of the API and the Worker → runtime on `mcr.microsoft.com/dotnet/aspnet:10.0` with headless Chromium installed by the
  Playwright driver that ships with the app (same version as the NuGet package, no extra Node.js/PowerShell).
  - default entry point: the API (port 8080, non-root user);
  - the job overrides the command: `dotnet /app/worker/JobScout.Worker.dll --contentRoot /app/worker --Scout:Mode=daily`.
- **Registry: GitHub Container Registry** – built and pushed by GitHub Actions (`docker.yml`) with the workflow's own token,
  tags `latest` and `sha-<commit>` (deploy or roll back an exact build). Free for a public repository.
- **API: Azure Container Apps**, consumption plan, **1 replica minimum** – the first-search queue lives in memory
  (6h) and a cold start of .NET + Chromium would greet the first visitor with a long wait. HTTPS ends at the ingress;
  the app trusts `X-Forwarded-Proto` so OIDC redirect URIs are `https://…`.
- **Morning run: Azure Container Apps Job** on a cron schedule (UTC) – billed only while it runs.
- Infrastructure as code (Bicep) and configuration/secrets: next step (7d).

## Alternatives considered
- **App Service** (Linux, container): fine, but a Basic plan is a fixed monthly price even when idle, and scheduled
  jobs would need WebJobs or a second service.
- **Azure Functions** for the morning run: a different hosting model and programming model for the same code;
  a Container Apps Job runs the existing Worker unchanged.
- **Two images** (API, Worker): cleaner separation, but twice the build time and Chromium twice; the code is shared anyway.
- **Azure Container Registry**: ~$5/month for the Basic tier; GHCR is free and lives next to the code.

## Consequences
- The image is large (~700 MB with Chromium); the build is cached per stage, a code-only change rebuilds in minutes.
- One replica = no high availability; acceptable for a personal tool, scale-out would need a shared queue for 6h.
- The image tag deployed is explicit (`sha-…`) – what runs in Azure is always traceable to a commit.
