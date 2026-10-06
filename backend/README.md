# MediPOS backend

Requires the .NET 10 SDK. PostgreSQL integration tests also require a running Docker-compatible engine with Linux containers.

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
dotnet test
dotnet format --verify-no-changes
```

The PostgreSQL smoke test starts an isolated `postgres:18-alpine` container with a randomly generated password, opens a connection through `MediPosDbContext`, executes `SELECT 1`, and disposes the container. These credentials belong only to the disposable test container. A missing Docker engine is a test failure, never a simulated success.

To run the tests that do not require Docker:

```sh
dotnet test --filter "Category!=PostgreSql"
```

Tests use `xunit.v3.mtp-off` with VSTest, the default `dotnet test` runner, without additional runner configuration. Architecture tests inspect the source project references, including empty layers. UnitTests is configured for Domain/Application tests and intentionally has no placeholder tests or business rules in B0.1.
