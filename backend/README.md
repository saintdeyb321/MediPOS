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

The second migration, `AddLegalEntitiesAndBranches`, adds tenant-owned legal entities and branches. Branch creation locks the current license row in a Read Committed transaction before checking its validity and counting branches; the licensing and branch ports share one scoped DbContext. New branches have no hub role; `SetMainHubBranch` moves it atomically using the same lock and a partial unique index. Real PostgreSQL tests for these constraints and concurrent provisioning are prepared but not executed locally.

TenancyLicensing has five internal application handlers and a tenant/license-scoped persistence port. They are not public HTTP endpoints or an authorization boundary; their callers must authorize administrative actions before HTTP exposure. The operation gate uses the interval `[starts_at, expires_at)`. Reactivation requires Suspended and an explicit Trial/Active/Grace target; renewal preserves status. Identical renewals and repeated suspension/purge requests are no-ops. Cancelled/PurgePending/Purged cannot be renewed or reactivated. Purge requests persist PurgePending and history; physical deletion and FR-LIC-005/A18 completion remain pending.

The fifth migration, `AddAuditFoundation`, adds tenant-private `audit_logs` with UUIDv7 IDs, UTC timestamps, bounded action/entity codes, minimal `jsonb` snapshots, tenant/time indexes and forced RLS. Its policies permit tenant-scoped SELECT/INSERT only. Grant the runtime role **SELECT and INSERT only** on this table, without UPDATE, DELETE or TRUNCATE; migration credentials remain separate. Domain has no audit update/delete operations, and DbContext rejects modified/deleted audit entries.

The twelve existing tenant/license, legal entity/branch and membership mutations persist business changes and audit in the same SaveChanges or existing provisioning transaction, including bulk updates/deletes. Failed audited saves clear staged entries; transaction disposal rolls back earlier bulk statements. Idempotent requests produce no new change event. Internal commands require a nonempty actor ID supplied by an authenticated server caller, never a free HTTP body; audit has no mandatory membership FK. Correlation comes from the server Activity trace or a generated server ID. Application errors have stable codes/categories; central API handling emits Problem Details with code/trace and 400/403/404/409, or a sanitized 500. Unexpected-error logs contain only exception type and server trace. There are still no production login/search/sync/integration endpoints requiring rate-limit policies.

Real PostgreSQL audit tests cover migration, restricted role privileges, tenant isolation, cross-tenant insertion, append-only enforcement, successful atomic writes, rollback after audit failure and pooled connection reuse. They are prepared and unexecuted locally.

Tests use `xunit.v3.mtp-off` with VSTest. Unit tests cover domain rules and application behavior; architecture tests continue enforcing the existing project dependency rules.

The third migration, `AddIdentityAccess`, adds global Google-subject users, tenant memberships, branch assignments and local work windows. Roles/day codes are explicit strings; assignments and schedules use composite tenant foreign keys. Membership creation and all membership configuration changes reuse the current-license row lock and transaction. Only active memberships are unique per tenant/user; an inactive membership remains referencable if a new membership is created. Two active Owners is enforced by the serialized application path, not by a database trigger.

The fourth migration, `AddTenantRowLevelSecurity`, enables and forces RLS on licenses, license history, legal entities, branches, memberships, assignments and schedules. `ITenantDataContext` selects one tenant per DI scope; a different tenant needs a new scope. Selection limits data and grants no authorization: `ResolveAccessContext` still checks the authenticated user and B0.4 policy. Normal EF reads fail closed without a tenant, and tracked private writes must match both original and current tenant ownership. Tenant remains a platform root, and User remains global; neither receives RLS or a query filter here. Their administration requires authorized platform/identity callers.

Connection and command interceptors parameterize `medipos.tenant_id`, explicitly writing an empty setting without a scope. They cover pooled checkout, selection after opening and transaction rollback; Npgsql connection pooling remains enabled. Use a production runtime role with **NOSUPERUSER and NOBYPASSRLS**, no table/schema ownership or DDL privileges; use separate migration credentials. FORCE subjects ordinary table owners to policies but cannot constrain superusers/BYPASSRLS ([PostgreSQL documentation](https://www.postgresql.org/docs/18/ddl-rowsecurity.html)). The integration harness migrates using its isolated container administrator and runs application/RLS paths with an ephemeral restricted role. Administrator contexts are reserved for independent constraint/filter checks, never RLS assertions. All PostgreSQL tests remain prepared and unexecuted locally.

Authentication is an explicit server boundary: `IVerifiedGoogleIdentitySource` supplies claims only after OIDC verification, and `IAuthenticatedMediPosUser` supplies the user ID only from a validated MediPOS session. Google verification must check signature, issuer, audience, expiry and applicable protocol protections ([Google documentation](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token)). No production implementations or HTTP login routes are registered. Once callback/session decisions are settled, API composition can register real adapters with `AddIdentityAuthentication`. Internal provisioning handlers require administrative authorization by their caller before HTTP exposure.

Authorization reads current membership, license, branch assignment and schedule in one tenant-bound PostgreSQL statement and returns stable access codes plus context. Requested tenant/branch IDs only select a target; they never establish identity or permission. B0.5 can consume this policy, re-evaluated for each protected operation with server time. Work windows use `TimeOnly`, America/Lima through .NET `TimeZoneInfo`, and the interval `[start, end)`; overnight windows are rejected. Boundaries must fit PostgreSQL microsecond precision. Owners bypass assignment/schedule checks but still need active membership and a valid license; any selected branch must belong to the tenant. Deactivation is idempotent and keeps user, membership, assignments and schedule; inactive configuration cannot be replaced. PostgreSQL constraint, atomic-replacement, authorization-query and concurrent Owner tests are prepared and remain unexecuted locally.
