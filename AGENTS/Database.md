# Database Guidance

Read this before adding application data, entities, repositories, scopes, or migrations.

## Implemented foundation

- `LyrionVoiceMcp.Ef.Abstractions` owns EF-facing data-access contracts, entity contracts, and repository contracts. Keep general application abstractions in `LyrionVoiceMcp.Abstractions`.
- `LyrionVoiceMcp.Ef` owns the single application `LyrionVoiceMcpDbContext`, entity configurations, repository implementations, context/scoping infrastructure, and generated migrations.
- The application database uses SQLite, foreign keys, a five-second busy timeout, and WAL mode. Keep contexts short-lived and do not hold them across slow LMS, HTTP, or search-index work.
- The production search index remains a separate, disposable derived artifact. Do not model it as application persistence or put its tables in the EF context.

Search observations and operational jobs, schedules, errors, and MCP tool-call history are authoritative in the EF application database. At startup, retained search-observation rows are copied in bounded batches from the legacy observation database; the importer is idempotent and opens that database read-only. Operational history starts clean in EF and is not imported from the legacy operations database. The handwritten catalogue store remains authoritative until its migration story cuts it over.

## Service and repository responsibilities

- Application services create the read-only or read/write context scope, coordinate repositories, and call `SaveChangesAsync` once for the unit of work.
- Repository implementations derive from `RepositoryBase<TEntity>`, resolve the ambient context, and contain entity queries and persistence operations only.
- Do not inject `LyrionVoiceMcpDbContext` into application services or let services depend on `LyrionVoiceMcp.Ef`.
- Do not open scopes or call `SaveChangesAsync` inside repositories.
- Repository access without an ambient context is a programming error and must fail clearly.

## Context scopes and transactions

- Use `CreateReadOnly()` for queries and `Create()` for normal writes. The read-only contract deliberately exposes no save or explicit-transaction operations. A joined child scope shares its parent's context and only the outer scope commits.
- A read/write scope must not join a read-only scope. Use `ForceCreateNew` only when isolation from an ambient unit of work is intentional.
- Use `SuppressAmbientContext()` before independent or parallel work that must not inherit the caller's context. Restore and dispose scopes in creation order.
- Use `CreateWithTransaction` when the whole scoped unit has a single conventional transaction. Disposal without `SaveChangesAsync` rolls it back.
- Use `TransactionAsync` only on an independent ordinary read/write scope when code needs explicit save/commit control, such as retryable multi-stage work. It is rejected on read-only, joined, and already-transactional scopes; the callback must explicitly commit.
- Never share a `DbContext` across parallel tasks.

## Entities and configuration

- Put persistent entity types and repository interfaces in `LyrionVoiceMcp.Ef.Abstractions`; put `IEntityTypeConfiguration<TEntity>` implementations in `LyrionVoiceMcp.Ef`.
- Use integer primary keys for application-owned entities unless an external identity has a concrete reason to remain the key.
- Entities supporting `ISupportCreatedUpdated` receive UTC audit timestamps during `SaveChanges`.
- Define keys, lengths, requiredness, indexes, relationships, and delete behaviour explicitly in configuration classes. Do not rely on a growing set of conventions hidden in `OnModelCreating`.

## Migrations and startup

- Migrations are generated artifacts. Do not hand-author or edit migration, designer, or model-snapshot files.
- Restore the repository-local tool with `dotnet tool restore`, then generate from the repository root with:

  ```sh
  dotnet tool run dotnet-ef migrations add <MigrationName> \
    --project LyrionVoiceMcp.Ef/LyrionVoiceMcp.Ef.csproj \
    --startup-project LyrionVoiceMcp.Ef/LyrionVoiceMcp.Ef.csproj \
    --context LyrionVoiceMcpDbContext \
    --output-dir Migrations
  ```

- Runtime startup calls `MigrateAsync` through the EF context factory before accepting requests. Do not use `EnsureCreated` for the application database.
- The design-time factory uses `LYRION_VOICE_MCP_DESIGNTIME_DATABASE_PATH` only when tooling needs a specific disposable target.

## Legacy cutover policy

- Search observations have been cut over by copying retained data into EF at startup. Keep the legacy observation file and read-only importer in place until the dedicated cleanup story removes them; never write new observations to that file.
- Operational history has been reset and cut over to EF. Keep the legacy operations file and dormant store code untouched until the dedicated cleanup story; do not initialise, read, write, import, or automatically delete that file.
- Rebuild catalogue data from LMS when the catalogue moves to EF; do not copy the legacy catalogue database.
- Keep each legacy store operating until its own cutover is complete. Do not add a general automatic importer or attempt an all-at-once migration.
