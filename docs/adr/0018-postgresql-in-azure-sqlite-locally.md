# ADR-0018: PostgreSQL in Azure, SQLite locally – one model, two migration sets

- Status: Accepted
- Date: 2026-10-12

## Context
Until now everything ran on one machine with a SQLite file (ADR-0008). In Azure the API (Container App) and the
morning run (Container Apps Job) are two processes, possibly on different hosts, that share one database –
a SQLite file on a network share is not safe for that (file locking over SMB). Containers also lose their file
system on every restart, which matters for one more piece of state: the ASP.NET Core Data Protection keys that
encrypt the session cookie and the OIDC round trip.

At the same time SQLite is perfect locally and in the tests: nothing to install, an in-memory database per test,
and the existing local data (the author's own profile and jobs) keeps working.

## Decision
- **Production: Azure Database for PostgreSQL – Flexible Server** (Burstable B1ms; free for 12 months on the
  Azure free account, then roughly $15/month). Provider Npgsql, transient-error retry on.
- **Local and tests: SQLite** as before.
- Chosen by configuration `Database:Provider` = `Sqlite` (default) | `Postgres`; the connection string stays
  `ConnectionStrings:JobScout`. One place decides: `DatabaseProvider.Configure` (Infrastructure).
- **Two migration sets.** EF Core migrations are provider-specific, so the PostgreSQL ones live in their own
  assembly `JobScout.Migrations.Postgres`; the SQLite ones stay in Infrastructure. The design-time factory
  picks the provider by the argument after `--` (`-- postgres`).
- A test checks *both* sets against the model (`HasPendingModelChanges`) – a forgotten migration for one
  provider fails the build instead of the first production start.
- PostgreSQL `timestamptz` takes only UTC: a converter stores every `DateTimeOffset` as UTC (SQLite keeps the
  sortable binary converter from ADR-0009).
- **Data Protection keys in the database** (`PersistKeysToDbContext`) – a restart or a second replica can still
  read cookies issued before. The keys are not encrypted at rest yet; the database is private to the app
  (Key Vault key wrapping is a later step).
- Production starts with an empty database – no migration of local data (the author signs up like anyone else).

## Alternatives considered
- **PostgreSQL everywhere, including tests** (Testcontainers / local Docker): one migration set, but every test
  run needs Docker and the local data would have to move. Not worth it for one developer today.
- **Azure SQL Database free offer**: free for the lifetime of the subscription, natural for .NET – but capped at
  100 000 vCore-seconds a month; with an auto-pause delay the morning run plus normal use gets close to the cap,
  and then the database either pauses until next month or starts billing. PostgreSQL is always on and predictable.
- **SQLite in production** (with Litestream replication to Blob storage): cheapest, but only works with one process
  writing – the API and the separate daily job would both need the file.

## How to create migrations
```powershell
# SQLite (local, tests)
dotnet ef migrations add <Name> -p src/JobScout.Infrastructure -s src/JobScout.Infrastructure -o Persistence/Migrations
# PostgreSQL (Azure) – note the "-- postgres" at the end
dotnet ef migrations add <Name> -p src/JobScout.Migrations.Postgres -s src/JobScout.Migrations.Postgres -o Migrations -- postgres
```

## Consequences
- Every model change needs two migrations (`dotnet ef migrations add` twice – the test reminds you).
- Provider differences (case-sensitive text comparison in PostgreSQL, UTC only) must be kept in mind; queries
  stay plain LINQ, no raw SQL.
- The Worker and the API reference the migrations assembly so that `Database.Migrate()` finds it at runtime.
