# ADR 0001: Core EF Core reference in the Application layer

Status: Accepted

## Context
Handlers must query through `IAppDbContext` with `AsNoTracking()`, `Select` projections and async terminal operators
(`ToListAsync`, `CountAsync`), as CLAUDE.md requires. Those extensions live in `Microsoft.EntityFrameworkCore`.
`solution-layout.md` §3 allows this single exception only when it is recorded in an ADR.

## Decision
`IncidentHub.Application` may reference the `Microsoft.EntityFrameworkCore` package (core only).
Provider packages (SqlServer, etc.), `DbContext` subclasses and migrations stay in Infrastructure.
Domain still references nothing.

## Consequences
- Handlers depend on `IAppDbContext` (DbSets + `SaveChangesAsync`), which `AppDbContext` implements.
- Handlers can catch `DbUpdateConcurrencyException`; the API maps it to 409.
- Application must not call `Database`, `Migrate` or provider-specific APIs.
