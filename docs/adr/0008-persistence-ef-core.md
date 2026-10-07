# ADR-0008: EF Core directly – no generic repository, no MediatR; SQLite → PostgreSQL

- Status: Accepted
- Date: 2026-10-06

## Context
The app needs users, versioned profiles, shared job offers, per-user matches with status history, and later
run/cost records. Development should need zero setup; production runs in Azure.

## Decision
- **EF Core 10** with Fluent API mappings (`IEntityTypeConfiguration<T>` per entity) in Infrastructure; the domain
  has no EF attributes.
- Use cases work with `IAppDbContext` (DbSets + `SaveChangesAsync`) – **no generic `IRepository<T>`**.
- **No MediatR / CQRS framework** – use cases are plain classes with methods.
- **SQLite** for development and integration tests; **PostgreSQL** in Azure. Migrations from day one, applied by
  the worker at start-up (single instance); in production via a migration bundle.
- Pitfalls handled explicitly: own Guid keys → `ValueGeneratedNever()` (otherwise new history rows are sent as
  UPDATE); SQLite can't order by `DateTimeOffset` → stored as a sortable number on SQLite only; history mapped
  through the private `_events` backing field.

## Alternatives considered
- **Generic repository over EF** – EF's `DbContext` already is a repository and unit of work; a generic layer
  hides LINQ/`Include`, adds code and usually leaks anyway.
- **MediatR** – indirection without benefit for a handful of use cases; commercial licence since 2025.
- **Dapper / raw SQL** – more control, but manual mapping and migrations.
- **Cosmos DB / document store** – the data is relational (users × offers × matches) with unique constraints.

## Consequences
- Migrations are provider-specific: PostgreSQL will need its own migration set (decided in phase 7).
- Integration tests run against real SQLite in memory created by the same migrations, including a check that
  the model has no pending changes.
