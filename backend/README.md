# MediPOS backend

Requires the .NET 10 SDK. This local machine has no Docker and will not install it. Do not check/start Docker or execute PostgreSQL/Testcontainers locally. B0.1 is complete under the local gate below; PostgreSQL integration tests remain available for another environment.

Configure `ConnectionStrings:MediPosDatabase` outside source control before starting the API. For local development, from `backend/`:

```sh
dotnet user-secrets set "ConnectionStrings:MediPosDatabase" "<connection-string>" --project src/MediPOS.Api
dotnet run --project src/MediPOS.Api
```

Alternatively, set the environment variable `ConnectionStrings__MediPosDatabase`. Startup fails with a configuration error when the connection string is missing or blank. No database is created or migrated automatically.

The local launch profile uses `http://localhost:5080`. `GET /health` reports process health; it does not check database readiness. The OpenAPI document is served at `/openapi/v1.json` only in Development.

```sh
dotnet restore
dotnet build
dotnet test tests/MediPOS.UnitTests
dotnet test tests/MediPOS.ArchitectureTests
dotnet format --verify-no-changes
```

The integration suite is prepared for an environment with a Docker-compatible engine and Linux containers. It shares one isolated `postgres:18-alpine` container with a random password, applies the migration to an empty database, and uses distinct tenant IDs to isolate test data. It covers the smoke connection, persistence/history, tenant-bound foreign keys, unique current licenses, constraints, state changes, append-only history and concurrent renewal. These tests are not part of the local gate and have not been validated here.

Migration tooling (no database connection is needed to scaffold migrations or check the model):

```sh
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/MediPOS.Infrastructure --startup-project src/MediPOS.Infrastructure
```

The first migration is `InitialTenancyLicensing`, in Infrastructure. The design-time factory configures Npgsql without credentials for offline model work; it also accepts `ConnectionStrings__MediPosDatabase` when tooling is used in a database-enabled environment. The API never applies migrations at startup.

TenancyLicensing has five internal application handlers and a tenant/license-scoped persistence port. They are not public HTTP endpoints or an authorization boundary; identity/administrative authorization remains for a later sprint. The operation gate uses the interval `[starts_at, expires_at)`. Reactivation requires Suspended and an explicit Trial/Active/Grace target; renewal preserves status. Identical renewals and repeated suspension/purge requests are no-ops. Cancelled/PurgePending/Purged cannot be renewed or reactivated. Purge requests persist PurgePending and history; physical deletion and FR-LIC-005/A18 completion remain pending.

Tests use `xunit.v3.mtp-off` with VSTest. Unit tests cover domain rules and application behavior; architecture tests continue enforcing the existing project dependency rules.
