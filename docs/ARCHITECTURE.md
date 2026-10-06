# MediPOS — Backend Architecture & Engineering Standard

## 1. Architectural baseline
- **Modular monolith**, not microservices for MVP.
- **Pragmatic Clean Architecture**: enforce dependency direction without ceremonial layers.
- **Feature/vertical slices inside modules**.
- Backend: **ASP.NET Core .NET 10 LTS**.
- Persistence: **EF Core + Npgsql**.
- Database: **PostgreSQL 18**, shared database/shared schema with `tenant_id` for private data.
- PostgreSQL is the single operational source of truth.

Initial modules:
IdentityAccess, TenancyLicensing, Branches, Catalog, Inventory, Purchasing, SalesPos, Cash, Transfers, Commissions, Reporting, Notifications, OfflineSync, ElectronicDocuments, AuditSupport.

## 2. Repository/backend shape
Target foundation:

```text
backend/
  MediPOS.sln
  Directory.Build.props
  Directory.Packages.props        # optional; use if useful
  .editorconfig
  src/
    MediPOS.Api/
    MediPOS.Application/
    MediPOS.Domain/
    MediPOS.Infrastructure/
    MediPOS.SharedKernel/
  tests/
    MediPOS.UnitTests/
    MediPOS.IntegrationTests/
    MediPOS.ArchitectureTests/
```

Do not create one .NET project per module. Modules are internal boundaries inside the projects above.

## 3. Dependency rules
```text
Domain          -> SharedKernel
Application     -> Domain, SharedKernel
Infrastructure  -> Application, Domain, SharedKernel
Api             -> Application, Infrastructure
```

Forbidden:
- Domain -> Application/Infrastructure/Api/EF/ASP.NET.
- Application -> Infrastructure/Api/concrete PostgreSQL implementation.
- Infrastructure -> Api.
- Business rules in endpoints/controllers.

Cross-module collaboration uses explicit application contracts/services. Do not bypass a module boundary by directly coupling an endpoint to another module's persistence tables.

## 4. Slice layout
Prefer cohesive feature folders, e.g.:

```text
Application/Modules/Inventory/AdjustStock/
  AdjustStockCommand.cs
  AdjustStockHandler.cs
  AdjustStockValidator.cs          # only if validation abstraction is justified
  AdjustStockResult.cs
```

Names are examples, not mandatory ceremony. Keep each use case easy to locate end-to-end.

Avoid by default: MediatR, AutoMapper, generic repository, generic UnitOfWork, service-locator, `Helpers`, base CRUD frameworks. Add abstractions only for a real boundary/problem.

## 5. Persistence and transactions
- One PostgreSQL operational database initially.
- One primary `MediPosDbContext` is acceptable for MVP; preserve module mapping boundaries through configuration/organization.
- EF Core for general persistence; SQL/Dapper only for measured/reporting cases with a clear reason.
- Explicit transaction boundaries for sale, purchase confirmation, transfer dispatch/receipt, cash close and other atomic workflows.
- Use database constraints/indexes to protect invariants where practical.
- Review migrations before applying. Do not create empty/speculative migrations.
- No production schema changes by manual ad-hoc SQL except documented emergency procedure.

Stock model:
- `StockMovement` is the ledger/audit trail.
- Balance updates and movement creation occur atomically.
- Never “fix” stock by direct silent assignment.
- Protect last-unit races with server-side transactional/concurrency strategy and integration tests.

## 6. Multi-tenancy
Model: shared DB + shared schema + `tenant_id` on all tenant-private tables, even where inferable.

Rules:
- Tenant is derived from authenticated server-side context.
- Never authorize from a client-provided tenant id alone.
- Queries/indexes should commonly start with `tenant_id` and branch keys where query patterns justify it.
- Global catalog is explicitly global; private prices, costs, stock, batches, sales, suppliers, employees and reports never leak into it.
- PostgreSQL Row Level Security is a second defense for high-risk private tables once tenancy is implemented.
- Automated integration tests must prove Tenant A cannot read/write Tenant B data.

## 7. Authentication and authorization
- Google OIDC is the initial identity mechanism.
- Authentication proves identity; authorization is server-side and depends on tenant, membership, role, branch, license and schedule where applicable.
- Maximum two Owner memberships per tenant.
- Deactivation preserves historical references.
- Use secure session/cookie or equivalent OIDC architecture; avoid sensitive tokens in localStorage when possible.
- If cookies are used, apply CSRF protection; restrict CORS to MediPOS origins.

## 8. HTTP/API
- Thin endpoints/controllers.
- Problem Details for HTTP errors; include stable machine-readable business codes.
- OpenAPI enabled for development/support; protect or disable in production as appropriate.
- Do not version API prematurely; introduce versioning when multiple external/mobile clients require it.
- Rate limit authentication, expensive search, sync and external integrations where relevant.

## 9. Data conventions
- Money: C# `decimal`, PostgreSQL `numeric` with explicit precision where needed.
- Fractionable quantities: `decimal`/`numeric`, precision defined by unit semantics.
- Audit timestamps: UTC (`DateTimeOffset` preferred where useful).
- Expiration: `DateOnly` / PostgreSQL `date`.
- Synchronizable identifiers: UUID/UUIDv7.
- Enums: stable string/code or explicit conversion/reference table; never persist ordinal implicitly.
- Strict foreign keys; cascade only for truly dependent lifecycles.

## 10. Search
Initial pharmaceutical/product search stays inside PostgreSQL:
- `unaccent`
- `pg_trgm`
- GIN/indexes as measured
- normalized lower-case searchable fields

Ranking: exact > prefix > trigram similarity > active ingredient > brand/lab. Structured equivalence key excludes brand, lab, price and stock.

Do not add Elasticsearch until measured evidence shows PostgreSQL search is insufficient.

## 11. Background work and integrations
Initial jobs use ASP.NET `BackgroundService` plus persistent PostgreSQL Outbox/Jobs tables when required. No RabbitMQ/Redis just to schedule work.

External integrations must sit behind explicit adapters/contracts. Electronic tax documents use an `ElectronicDocumentProvider`-style boundary so provider/PSE/OSE/direct integration can change without coupling core sale/inventory.

Persistent queues are required where restart must not lose work (e.g. CPE, sync-related server jobs).

## 12. Offline backend contract
Offline is a later milestone and a contingency mode.
- Each local event has UUID `event_id`, `device_id`, tenant/branch context, local timestamp and versioned payload.
- Server registers idempotency before applying event.
- Response states: Accepted / Conflict / Rejected with reason/version context.
- Never silently overwrite a stock conflict.
- Transfers, permissions and critical configuration are not offline operations in MVP.

## 13. Observability and secrets
- Structured logs with request/correlation id and non-sensitive tenant context where useful.
- Never log tokens, secrets or unnecessary sensitive payloads.
- Environment variables/User Secrets/secret store for credentials.
- Important metrics later: API latency, 5xx, DB connections, resource usage, stuck jobs, pending sync/CPE, failed backups.

## 14. Testing standard
Use xUnit unless a concrete reason requires otherwise.

- **Unit**: FEFO, unit conversions, mixed payments, commissions, license/schedule rules, equivalence key and pure domain logic.
- **Integration with real PostgreSQL/Testcontainers**: atomic sale, no negative stock, concurrency, transfers, RLS, purge, migrations and DB constraints.
- **Architecture**: project/layer dependency restrictions and selected module-boundary rules.
- **E2E later**: login, cash open, sell, ticket, close, purchase/batch, transfer, report.
- **Offline later**: network loss/retry/idempotency/conflict.

High-risk mandatory scenarios before MVP completion:
1. Two concurrent sales compete for last stock; only valid result commits.
2. Void sale with commission + mixed payment fully compensates.
3. Transfer preserves batch/expiration.
4. Stock adjustment records actor/reason/before/after.
5. Same offline `event_id` applied twice creates one sale only.
6. Out-of-schedule user denied.
7. License suspension behavior proven.
8. Test-tenant purge leaves zero private rows/orphans.
9. Tenant A/B isolation proven.

## 15. Definition of done for a backend task
A task is done only when its scoped acceptance criteria are met and relevant validation passes:
- formatting/static checks used by repo,
- build,
- relevant unit/integration/architecture tests,
- no committed secrets or generated `bin/obj`,
- migration reviewed if schema changed,
- docs updated only if requirement/ADR/sprint status materially changed.

Do not chase arbitrary coverage percentages. Protect critical invariants and regressions with meaningful tests.
