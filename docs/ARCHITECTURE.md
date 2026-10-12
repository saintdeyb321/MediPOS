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

## 12. Catalog ingestion, supplier documents and offline backend contracts
### Supplier purchase-document boundary (B7.1)
- Internal Application use cases require trusted server identity, active tenant/license/membership and Owner or Pharmacist. Pharmacist additionally requires assigned branch and valid Lima work schedule. No public HTTP route or production authentication bypass is added.
- The initial documentary profile is Invoice UBL 2.1, Customization 2.0, type `01`, explicit UBL namespaces, electronic series `F[A-Z0-9]{3}` and positive 1–8 digit correlative. Issuer/buyer RUC uses scheme `6` and exactly 11 ASCII digits; this is syntax, not external tax authentication. The buyer must match the selected branch's legal entity RUC. No SUNAT service or signature/CDR verification is involved.
- Boundaries: 5 MiB XML, 1000 lines, depth 64, 200000 nodes, 32 attributes/element and bounded fields. DTD/entities/external resolution and ZIP are rejected. The exact bytes, SHA-256, original document ID, quantities, units and monetary evidence remain immutable. Decimal parsing is invariant; documentary amounts use two decimals with an explicit absolute tolerance of PEN/foreign-currency 0.01 per arithmetic comparison, without rounding intermediate quantities/prices. Unsupported currencies or inconsistent totals remain reviewable evidence but block conversion. Purchases support PEN only; no exchange rate is inferred.
- Supplier RUC, explicit buyer internal code, explicit GTIN/EAN and previously approved supplier-code plus UBL-unit mapping produce conservative suggestions. They never approve products/presentations or create catalog entries. Human review explicitly chooses an existing local ProductUnit, confirms the physical presentation, received quantity and inventory cost per presentation, and supplies receipt allocations. Original invoice prices/taxes never become inventory cost or sale prices. Medicine allocations require batch and expiry; quantities sum exactly to the reviewed receipt, which may differ from the invoice.
- Tenant/document identity and checksum uniqueness, license transactions, document locks, optimistic review versions, tenant-safe FKs, filters and FORCE RLS protect retries and isolation. Original evidence and draft provenance are immutable; review changes and conversion require matching audit in the same transaction. Conversion uses existing Purchasing invariants to create one Draft and traceable receipt lines, without InventoryLot, StockMovement or confirmation. Converted receipt lines and referenced ProductUnit rows cannot be deleted while provenance exists; subsequent commercial confirmation uses the existing ConfirmPurchaseHandler.

### Official catalog staging boundary (B6.1)
- Platform administration requires a trusted server authorization adapter, an approved source registry and a dedicated `medipos_catalog_importer` credential in the same operational database. Tenant roles do not grant this capability. No public endpoint or production identity/source adapter is enabled by B6.1.
- `csv_utf8_v1` is a provisional input adapter, tested with synthetic fixtures, not a DIGEMID schema. Its required headers are `SourceRecordId,Name,Laboratory,Registration,Presentation,Ingredients,Strength,DosageForm,Route`; extra columns are preserved within the 32-column envelope. Row numbers are CSV record ordinals, including header record 1.
- Defaults: 16 MiB/file, 100000 data rows, 200 rows/write transaction, two-minute renewable processing lease; administrative pages cap at 100. Fields cap at 8192 characters and records at 32768; narrower field checks produce row errors without truncating original text. Invalid UTF-8, NUL and malformed CSV are structural failures.
- SHA-256 covers exact input bytes. Source/version selects one immutable checksum; a changed checksum requires administrative review. Same-file retries reuse committed rows, with at most five attempts and retained failure diagnostics. A live lease prevents concurrent processing; expired leases can be reclaimed. `staged` requires all rows; failed-batch counts describe its committed prefix. Duplicate counts overlap valid/invalid counts.
- Source text, raw fields and identity/presentation distinctions remain intact. Text fingerprints only flag candidates; strength shape checks infer no concentrations or equivalences. No source row is merged or published. The temporary spool is removed on close; the original binary is not archived or stored in PostgreSQL. Preserve authorized source files externally if archival is required.
- Before using real data, verify issuer/reference, reuse permission, coverage, publication/version semantics, identifier stability, CSV encoding/delimiter/header mapping and pharmaceutical field semantics against the authorized source. No real DIGEMID integration has been validated.

### Controlled catalog curation (B6.2)
- Immutable staging, normalized proposals, append-only administrative decisions and published global references are separate records. B6.1 is audited locally; real PostgreSQL validation remains pending externally.
- The provisional source schema does not establish ingredient/concentration associations or a verified ingredient dictionary. Drafts trim reference text but infer no clinical components, unit conversions, salts or package/base-unit relationships. A reviewer explicitly supplies structured components, verifies identity and clinical data, chooses an active category and records a reason. Retail proposals follow the existing non-medicine invariants.
- `medipos_catalog_reviewer` and `medipos_catalog_publisher` are independent NOLOGIN capabilities, granted to separate restricted credentials in the same operational database. They read staging without changing it; only the publisher can insert global products/profiles. Neither may access tenant-private tables. Explicit server authorization adapters are required; the commercial API registers no curation routes or production authority.
- Each staging row has at most one curation. Stable official identity is unique within its source; the original identity cannot be silently replaced. A missing source identity requires a documented, verified administrative alternative. B6.2 new-product publication still blocks an already published identity; B6.3 refresh references that immutable provenance separately.
- Optimistic candidate versions bind reviews to snapshots. Correction revokes approval; rejection and publication are terminal in B6.2. Duplicate screening uses bounded PostgreSQL queries and records evidence: official identity, verifiable exact attributes including presentation, potential name/registration match, or structured pharmaceutical similarity. Similarity never merges products. Exact provenance may be linked to an existing active global product only by an explicit reviewer decision. A new blocking match requires renewed review.
- Queue match/error filters describe the last saved screening; the dedicated duplicate query screens the current catalog. Approval/publication recheck matches. Queries and review history paginate at 100 results; screening caps at 101 and blocks approval/publication if evidence exceeds the review envelope.
- Individual publication locks the candidate, then transactionally coordinates official identity and attribute fingerprint. Product/profile creation uses domain factories. Source binding, the published state, revision and platform audit commit together; deferred database guards reject an untraceable publisher insert or missing audit. Uniqueness and locks prevent duplicate publication; a retry with the original approval returns the published product. Failure diagnostics are append-only after rollback and never imply a successful publication.
- No artificial tenant ID, business-product association, price/stock mutation or financial-history update is part of curation. Real-source mapping, ingredient semantics, identifier stability and production platform authentication remain unvalidated; synthetic fixtures prove no clinical equivalence or DIGEMID compatibility.

### Controlled versioned refresh (B6.3)
- A refresh references the original publication, its stable global product and a separately staged source version. `(SourceCode, SourceVersion, OfficialIdentity)` is unique; exact checksums permit idempotent preparation, while incompatible content conflicts. Source versions are opaque labels, not lexical/numeric chronology; available official publication timestamps must advance beyond the original source. Application is always an explicit administrative decision against the current product version.
- Existing curation drafts provide source references without inferred ingredient associations. Refresh correction supplies verified structured data. Deterministic comparisons explain unchanged, metadata, clinical, identity-conflict, absence and incomplete cases. Only name, laboratory and active category may be approved for direct update. Clinical/type/registration/presentation changes, including unit spelling changes, remain blocked; rejection, keeping the previous product, further review and documented referral to another identity are decisions, never automatic publication or substitution.
- Absence is recorded only against a completed staged batch after verifying the identity is absent. Its staging/curation references are null rather than fabricated. It implies neither withdrawal nor deactivation. No verified source-retirement policy is assumed.
- Refreshes use optimistic review versions plus a fingerprint of the actual global reference/profile and a monotonic application version shared by all origins of that global product. Explicit correction rebases stale proposals, retaining earlier baselines in immutable history. Publication identity/global advisory locks, row locks and persistent uniqueness serialize application; unchanged versions also require approval and advance the application version without rewriting the product.
- Append-only version events retain distinct previous, proposed and approved snapshots, changed fields, decisions, reason, reviewer, applier and trace. Approval is nullable before approval and after rejection/correction. Existing platform audit accompanies every transition; deferred guards require history/audit for every intermediate version, not only the final state. Application retries with the original approval return the same result.
- The existing publisher receives column UPDATE only for `global_products.name`, `brand_or_laboratory` and `category_id`. Guards require immutable approval, current baseline and matching application/history/audit in the same transaction; clinical profiles and original publication/revisions remain immutable. A restricted `SECURITY DEFINER` active-category lock function, owned by the trusted migration identity with a fixed search path, supplies SELECT row locking without granting category UPDATE. Reviewer/importer/commercial credentials cannot apply metadata or access each other's private capabilities.
- Global metadata, refresh state, immutable history and audit commit atomically. Business products, units, prices, stock, sales, financial history and tenant catalog synchronization remain independent. No public HTTP host, production platform authorization or automatic source refresh is enabled; real PostgreSQL validation remains external.

### Offline synchronization
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
