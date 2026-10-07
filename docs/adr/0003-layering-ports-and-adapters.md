# ADR-0003: Layering – Domain / Application / Infrastructure / hosts

- Status: Accepted (restructuring in phase 3.0)
- Date: 2026-10-07

## Context
Phases 1–3b produced working parts (AI profile extraction and evaluation, jobs.cz source, EF Core persistence),
but the orchestration lived in the Worker host. The web API (phase 5) needs the same use cases – "upload CV and
analyse it" happens both on the web and in the morning run. The use cases also need the database, while the
`DbContext` lives in Infrastructure, which the inner layers must not reference.

## Decision
Four layers, dependencies pointing inwards only (Clean Architecture / ports and adapters):

| Project | Contains | Depends on |
| --- | --- | --- |
| `JobScout.Core` (Domain) | Entities (`User`, `UserProfile`, `JobOffer`, `JobMatch`), enums, job ad value objects (`JobListing`, `JobDetail`) | nothing |
| `JobScout.Application` | Use cases, ports (`ILlmClient`, `IJobSource`, `IAppDbContext`), AI logic (prompts, schemas, profile extraction, evaluation), search planning | Core, `Microsoft.EntityFrameworkCore` (base package only – no database provider) |
| `JobScout.Infrastructure` | Adapters: Claude client, jobs.cz source, Playwright renderer, EF Core `DbContext` + mappings + migrations | Application |
| `JobScout.Worker`, `JobScout.Api` | Thin hosts: configuration, DI, scheduling / HTTP endpoints | Application, Infrastructure |

Use cases access data through `IAppDbContext` (DbSets + `SaveChangesAsync`), implemented by `JobScoutDbContext`.

## Alternatives considered
- **Keep orchestration in hosts** – would duplicate use cases between Worker and API.
- **Use cases in Infrastructure** – mixes "what the app does" with "how it talks to the outside world";
  harder to test and to read.
- **Fully persistence-agnostic Application** (own repository interfaces, no EF reference) – purer, but needs
  repositories for every query and gives up LINQ/`Include`. Referencing only the EF Core base package is a
  widely used, pragmatic middle ground; the domain itself stays free of EF.

## Consequences
- Worker and API stay thin; a use case is written and tested once.
- Application depends on EF Core abstractions – accepted trade-off (see ADR-0008).
- AI prompt engineering is application logic and is unit-testable with a fake `ILlmClient`.
