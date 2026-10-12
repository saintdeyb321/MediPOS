# MediPOS — Backend Execution Plan

This is the single execution roadmap for the backend. Prompts are given in chat and are **not** stored in the repository.

## Working state
- **Current milestone:** M8 — Backend hardening for real pilot
- **Current sprint:** B8.2 — Performance/load targets
- **Status:** IN_PROGRESS. B8.1's local security hardening is complete; B8.2 harness implementation and local gate are complete, but real PostgreSQL performance is NOT_MEASURED. M8 remains incomplete; B7.3/B7.4 stay voluntarily deferred outside the initial MVP.
- **Next gate:** Measure B8.2 search/confirmation externally on representative PostgreSQL 18 hardware. Real PostgreSQL security/transaction regression, trusted production authentication/session and least-privilege runtime configuration remain pilot prerequisites. B8.3 has not started.

Local policy: this machine has no Docker and will not install it. Do not check/start Docker or run PostgreSQL/Testcontainers here. B0.1 is complete locally. Keep real PostgreSQL integration tests for another environment; they are not part of the local gate and must not be reported as passed without execution.

Allowed statuses: `NOT_STARTED`, `IN_PROGRESS`, `BLOCKED`, `DONE`.

Update the block above only when sprint state materially changes. Do not duplicate implementation notes here.

## Delivery workflow
1. ChatGPT/user define the next tightly scoped prompt from this plan.
2. Codex reads `AGENTS.md` + only relevant doc sections and implements locally.
3. Codex runs the local gate and reports concise results, distinguishing prepared integration tests from executed tests.
4. User reviews the implementation; architecture, security, tests and requirements are audited.
5. Next prompt is either a corrective task or the next planned slice.

One prompt should normally produce one reviewable change-set. Do not combine unrelated sprints to “save time”.

---

# M0 — Foundations
Goal: safe base for all business modules.

### B0.1 — Backend scaffold
**Scope:** solution/projects, dependency direction, formatting, Problem Details, dev OpenAPI, `/health`, PostgreSQL DbContext registration, test projects/Testcontainers foundation, architecture tests.
**Exit:** local `restore/build/unit tests/architecture tests/format` pass; no domain entities/migrations yet. PostgreSQL validation is separate under the local policy above.

### B0.2 — Tenancy & licensing core
**Status:** DONE — audited/completed locally; PostgreSQL tests remain prepared for another environment.
**Requirements:** FR-LIC-001..005, BR-001, BR-017, BR-020.
**Scope:** Tenant, License, lifecycle/state model, license limits/history, application use cases, persistence/configuration, core tests.
**Exit:** tenant/license creation and state enforcement core works; domain invariants unit-tested; local gates pass and real PostgreSQL integration tests are prepared for another environment. FR-LIC-005 covers only the persistent purge request here; physical purge and A18 remain pending.

### B0.3 — Legal entities & branches
**Status:** DONE — audited/completed locally; PostgreSQL tests remain prepared for another environment.
**Requirements:** FR-TEN-001..003, FR-TEN-005.
**Scope:** LegalEntity, Branch, licensed branch count, tenant ownership, main/hub marker.
**Exit:** branch creation cannot cross tenant/license boundaries or exceed licensed limit.

### B0.4 — Identity, membership, roles & schedules
**Status:** DONE — completed locally; PostgreSQL tests remain prepared for another environment.
**Requirements:** FR-AUT-001..005, BR-011, BR-016.
**Scope:** global User identity, Membership, roles, branch assignments, WorkSchedule, Google OIDC integration boundary/session model.
**Exit:** authorization context can resolve user/tenant/branch/role/license/schedule; max two Owners enforced; deactivation preserves history.

### B0.5 — Multi-tenant enforcement & RLS
**Status:** DONE — audited/completed locally; PostgreSQL tests remain prepared for another environment.
**Requirements:** BR-001, FR-AUT-002, A15.
**Scope:** tenant context, query protection, RLS for selected high-risk private tables, integration-test isolation harness.
**Exit:** automated tests prove Tenant A cannot read/write Tenant B by application path and RLS-covered direct DB path.

### B0.6 — Audit foundation & platform hardening
**Status:** DONE — audited/completed locally; PostgreSQL integration tests remain prepared for external validation.
**Requirements:** FR-AUD-001..003, security NFRs.
**Scope:** AuditLog append-only behavior, actor/correlation metadata, stable error codes, rate-limit/security baseline, secret/log review.
**Exit:** critical foundation changes audited; no tenant UI deletion path; architecture/security tests green.

**M0 gate:** create tenant/license/owner/branches/membership without manual DB edits; server-side access context and tenant isolation proven.
**M0 status:** Local implementation completed; full release validation remains pending the external PostgreSQL suite.

---

# M1 — Catalog, purchasing & inventory
Goal: trustworthy product and stock core.

### B1.1 — Global catalog & tenant products
**Status:** DONE — audited/completed locally; PostgreSQL tests remain prepared for external validation.
**Requirements:** FR-CAT-001..005, FR-CAT-007, BR-012, BR-013.
**Scope:** GlobalProduct, MedicineProfile, Category, BusinessProduct; global/private data separation.

### B1.2 — Units and pharmaceutical normalization
**Status:** DONE — audited locally; PostgreSQL tests remain prepared for external validation.
**Requirements:** FR-CAT-004, FR-CAT-006.
**Scope:** ProductUnit, exact base-unit conversion, normalized ingredient/strength/form/route, equivalence key.

### B1.3 — Suppliers & purchases
**Status:** DONE — audited locally; PostgreSQL tests remain prepared for external validation.
**Requirements:** FR-PUR-001..004, FR-INV-001.
**Scope:** Supplier, Purchase/PurchaseLine and atomic confirmation; B1.3 introduces InventoryLot + StockMovement only for purchase receipt; B1.4 completes the ledger, balances, adjustments, FEFO and no-negative-stock.

### B1.4 — Inventory ledger, lots, adjustments & FEFO
**Status:** DONE — audited locally; PostgreSQL tests remain prepared for external validation.
**Requirements:** FR-INV-001..007, BR-002..005.
**Scope:** InventoryLot, StockMovement, balance strategy, adjustment audit, FEFO, no-negative-stock/concurrency.
**Exit:** last-stock race integration tests and FEFO pass.

### B1.5 — Search engine
**Status:** DONE / audited locally; PostgreSQL integration tests prepared for another environment.
**Requirements:** FR-POS-002..003 plus catalog search rules.
**Scope:** `unaccent`, `pg_trgm`, indexes/ranking, branch stock, equivalence fallback, other-branch availability.
**Exit:** typo/accent and structured-equivalence tests pass within representative target dataset.

### B1.6 — Excel product import
**Status:** DONE / audited locally.
**Requirements:** FR-CAT-009.
**Scope:** ImportJob, bounded file validation, staging/result per row, partial validity behavior without importing invalid rows.

**M1 gate:** local implementation complete; PostgreSQL integration validation remains pending in an external environment.

---

# M2 — POS & cash
Goal: complete reliable online sale workflow.

### B2.1 — Cash session
**Status:** DONE / audited locally.
**Requirements:** FR-CASH-001, FR-CASH-005.
**Scope:** CashSession open state, seller/branch ownership, permission checks.
**FR-CASH-005:** B2.3 adds accumulated confirmed sales to the active-session query; PostgreSQL validation remains pending externally.

### B2.2 — Sale draft aggregate & checkout preparation
**Status:** DONE / audited locally.
**Requirements:** FR-POS-001, FR-POS-004, FR-POS-009, BR-002..004.
**Scope:** Sale/SaleLine, seller/cash linkage, ProductUnit conversion, server-side price snapshots and totals. No stock mutation, confirmation or payments. FR-POS-001 remains primarily a frontend/usability requirement.

### B2.3 — Atomic sale confirmation & payments
**Status:** DONE / audited locally.
**Requirements:** FR-POS-005..007, BR-006.
**Scope:** SalePayment, exact payment total, FEFO allocation, lot locking, StockMovement, Sale confirmation and audit in one transaction.

### B2.4 — Sale void & compensating reversals
**Status:** DONE / audited locally.
**Requirements:** FR-POS-010, BR-007.
**Scope:** permissioned sale void, compensating stock movements, payment reversal ledger and audit in one transaction. BR-010 remains pending B4.1, which must extend this same transaction with real commission compensation; no commission hook/service in B2.4.

### B2.5 — Cash close & reconciliation
**Status:** DONE / audited locally.
**Requirements:** FR-CASH-002..003.
**Scope:** expected totals by method, counted cash, difference and close time.

### B2.6 — Internal ticket
**Status:** DONE / audited locally.
**Requirements:** FR-POS-008, BR-014.
**Scope:** InternalTicket representation/data endpoint; explicitly non-CPE.

**M2 gate:** online sale is atomic, auditable, concurrent-safe and cash-accounted.
**M2 status:** local implementation completed; PostgreSQL external validation pending.

---

# M3 — Multi-branch operations
Goal: safe branch-to-branch movement and owner consolidation.

### B3.1 — Product transfers
**Status:** DONE / audited locally.
**Requirements:** FR-TRF-001..006, BR-008..009.
**Scope:** Transfer/Line/Event state machine, approval, batch-preserving dispatch/receipt, quantity validation.

### B3.2 — Cash-change transfers
**Status:** DONE / audited locally.
**Requirements:** FR-CASH-004.
**Scope:** linked origin/receipt flow between branches/cash desks with audit.
**Provisional MVP decision:** reject cash refunds exceeding expected cash after transfer outflows; functional validation pending. This restriction is not part of original BR-007; implementation remains unchanged.

### B3.3 — Consolidated owner queries
**Status:** DONE / audited locally.
**Requirements:** FR-TEN-004.
**Scope:** tenant-wide/branch-filtered read models needed by owner operations.

**M3 gate:** branch stock/cash movement is traceable and consolidated queries respect permissions.
**M3 status:** implementation and code review completed locally; real PostgreSQL integration validation remains pending externally.

---

# M4 — Commissions, reporting & alerts
Goal: owner control and operational visibility.

### B4.1 — Commissions
**Status:** DONE / audited locally — B4.1.1 correction integrated; real PostgreSQL validation remains pending externally.
**Requirements:** FR-COM-001..004, BR-010.
**Scope:** CommissionRule/Entry, fixed/percentage rules, sale posting, compensating reversal, reports.

### B4.2 — Operational dashboards/reports
**Status:** DONE / audited locally — B4.2.1 correction integrated; real PostgreSQL validation remains pending externally.
**Requirements:** FR-RPT-001..004.
**Scope:** period/branch/employee/product/category reporting; critical stock, expiration capital, rotation.

### B4.3 — Exports
**Status:** DONE / audited locally — implemented in commit titled B4.4; real PostgreSQL validation pending externally.
**Requirements:** FR-RPT-005.
**Scope:** Excel/PDF export of selected reports with applied filters.

### B4.4 — Notifications & expiration alerts
**Status:** DONE / audited locally — real PostgreSQL integration validation pending externally.
**Requirements:** FR-INV-003, FR-RPT-007; later FR-RPT-008.
**Scope:** persistent Notification center, 30-day job, read state; Web Push only after internal center is stable.

### B4.5 — Replenishment suggestion
**Status:** DONE / audited locally — real PostgreSQL integration validation pending externally.
**Requirements:** FR-INV-008, FR-RPT-006 P1.
**Scope:** explainable historical-consumption suggestion; never auto-purchase.

**M4 gate:** owner can understand sales, stock risk, commissions and alerts without SQL.
**M4 status:** implementation and static review completed; real PostgreSQL integration validation pending externally.

---

# M5 — Offline synchronization backend
Goal: safe limited contingency sync.

### B5.1 — Devices, event contract & idempotency
**Status:** DONE / audited locally — real PostgreSQL integration validation pending externally.
**Requirements:** FR-OFF-002, BR-018.
**Scope:** SyncDevice/SyncEvent, event versioning, idempotency storage.

### B5.2 — Batch sync & durable acknowledgments
**Status:** DONE / audited locally — real PostgreSQL integration validation pending externally. HTTP mapping prepared; public activation blocked until production OIDC/session authentication exists.
**Requirements:** FR-OFF-003.
**Scope:** sequential reception with one transaction per event; durable pending acknowledgments and persisted terminal replays. HTTP activation requires production server authentication; business application belongs to B5.3. Protocol version 1, no incremental receipt cursor.

### B5.3 — Offline sale reconciliation
**Status:** DONE / audited locally — real PostgreSQL integration validation pending externally. HTTP activation requires production server authentication. Manual resolution is Owner-only and never posts a sale, payment or stock movement.
**Requirements:** FR-OFF-004..005, BR-019.
**Scope:** safe offline-sale subset, explicit stock conflicts/reconciliation queue; no silent overwrite.

### B5.4 — Incremental branch catalog sync
**Status:** DONE / audited locally — real PostgreSQL integration validation pending externally. Public HTTP activation requires production server authentication.
**Requirements:** FR-OFF-001.
**Scope:** version/cursor-based downloads rather than full dataset each reconnect.

**M5 gate:** implementation complete / audited locally; real PostgreSQL integration validation pending externally. Duplicate event cannot duplicate sale; conflicts are visible and recoverable.

---

# M6 — Official pharmaceutical catalog pipeline
**Status:** Implementation complete; PostgreSQL validation and production activation pending.
Goal: controlled ingestion without fragile runtime dependency.

### B6.1 — Staging/import pipeline
**Status:** DONE / audited locally — approved by static review; real PostgreSQL integration validation pending externally. Only synthetic CSV fixtures have been processed; production platform authentication and an authorized source adapter are required before activation.
**Scope:** import official/versioned source into staging; preserve original text.

### B6.2 — Normalize, deduplicate, curate, publish
**Status:** DONE / audited locally — B6.2.1 Unicode/unit validation correction integrated; real PostgreSQL integration validation remains pending externally (28 prepared cases). Staging is immutable, administrative proposals/revisions and platform audit are preserved, and individual publication uses a separate restricted capability. Real-source semantics and production platform authorization remain prerequisites for activation.
**Requirements:** FR-CAT-008.
**Scope:** normalization and superadmin review; only validated records become global.

### B6.3 — Versioned refresh/history
**Status:** DONE locally / statically audited — B6.3.1 chronology correction integrated. Preparation, active review and application compare official publication dates against the last applied history for the exact source and official identity, falling back to the original publication only before any refresh is applied. Earlier dates are stale, equal dates or reused versions conflict, and missing dates leave order unverified; opaque version labels are never sorted. Applied retries are read-only. The corrective migration replaces guards without changing historical data or the EF model. Local gate and offline model/script checks passed; 38 prepared PostgreSQL cases remain pending externally. No production authorization/source adapter, implicit administrative override or M7 work is enabled.
**Scope:** refresh by verified source and official identity; compare changes, preserve the global product identity and apply only approved administrative metadata with complete history.

**M6 gate:** tenant operations never depend on live scraping; global catalog publication is curated.

---

# M7 — Tax & purchase-document integrations
Goal: optional integrations without coupling the core.

### B7.1 — Supplier UBL XML import
**Status:** DONE — implementation and local gate complete; real PostgreSQL validation and production HTTP authentication remain pending.
**Requirements:** FR-PUR-005.
**Scope:** parse/map into reviewable pre-purchase; no automatic stock increase before human confirmation.
**Implemented:** bounded, namespace-aware Invoice UBL 2.1/Customization 2.0/type 01 parser; immutable XML/checksum and documentary evidence; legal receiver RUC validation; conservative suggestions and explicit product/presentation/quantity/cost/lot review; Owner/Pharmacist server authorization; tenant-safe idempotency, audit, FORCE RLS and draft provenance. Conversion uses existing Purchasing invariants and never receives stock. `AddSupplierUblImports` adds three private tables, composite FKs and guarded transitions without changing historical migrations or stock schema. Local restore/build, 1604 unit tests (62 B7.1), 8 architecture tests, format verification, EF pending-model check and offline migration SQL inspection passed. 36 real PostgreSQL cases are prepared and compiled, including concurrent retries, rollback, pooling, tenant isolation and the three-medicine workflow; none ran locally. The provisional profile and protected referenced presentation/receipt rows are documented in ARCHITECTURE.md. At that delivery, B7.2 was untouched.

#### B7.1.1 — Structural XML hardening and auditable rejection
**Status:** DONE locally; real PostgreSQL validation remains pending externally.
**Corrective:** required elements use explicit non-null presence, namespace and cardinality checks; optional containers retain their existing semantics. Missing/duplicated nodes, ambiguous structures, wrong namespaces and malformed/unsafe XML produce sanitized application codes through the existing file-rejection audit path. XML security limits, exact source bytes/checksum, cancellation, authorization, deduplication and commercial posting remain unchanged. Local restore/build, 1759 unit tests (155 new structural cases), 8 architecture tests, format verification and offline EF model check passed. Two additional PostgreSQL tests are prepared and compiled for rejection audit, tenant isolation, no partial/commercial effects and valid-file review/conversion after rejection; they have not run locally. No schema changes or new migration. At that delivery, B7.2 had not started.

### B7.2 — Electronic documents core, provider abstraction & durable delivery
**Status:** DONE locally; real PostgreSQL validation and production activation pending.
**Requirements:** FR-CPE-001..005, BR-014.
**Scope:** ElectronicDocument state model, provider adapter, persistent queue/retry, series config, ticket/CPE separation.

**Implemented:** immutable issuer/customer/sale/monetary preparation snapshots; Owner-authorized series and administration; Pharmacist branch/schedule access and Cashier own-sale access. One ordinary document per sale; equivalent requests recover the same identity and conflicting requests fail. Row-locked series allocation, unique numbers, three private tables with composite tenant FKs, FORCE RLS, append-only attempt events and transactional audit are added by `AddElectronicDocuments`; earlier migrations and commercial modules are unchanged. Fiscal input is explicit under the provisional `preparation_v1` profile (PEN, exact original four-decimal amounts); no tax rates are inferred and this profile does not certify SUNAT/UBL validity.

**Delivery:** bounded explicit processing with SKIP LOCKED, leases and claim tokens; a committed dispatch intent precedes the provider call outside all database transactions. Expired unsent claims can retry with bounded backoff; expired dispatched claims and transport uncertainty require manual reconciliation without automatic resend. Reception, authenticated authority evidence, rejection and simulation are distinct. A commercial void before dispatch blocks delivery; later voids preserve provider facts and surface a correction-review flag. Production provider, technical principal and evidence verification default to disabled; the deterministic fake exists only in test assemblies. No public endpoints or automatic external delivery. Financial history cannot be removed through automatic migration rollback.

**Validation:** local restore/build (zero warnings), 1831 unit tests (72 B7.2), 8 architecture tests, full format verification, offline EF model check and SQL inspection passed. 21 real PostgreSQL cases are prepared and compiled for migration/RLS/FKs, concurrent requests/numbers/claims, recovery, fencing, rollback/audit, pooling, voids and commercial integrity; NO EJECUTADAS locally. Self-audit corrected mutation-before-validation, time overflow, stable snapshot enum codes and authorization/lease expiry during lock waits. B7.3 was NOT_STARTED at B7.2 delivery.

### B7.3 — Provider/sandbox pilot
**Scope:** integrate selected PSE/OSE/direct sandbox only after credentials/contract are known; resilient retries/observability.

**Status:** BLOCKED — voluntarily deferred by product decision, excluded from the initial MVP, per `SUNAT_INTEGRATION_PLAN.md`. Only the mandatory simulation correction is integrated: every simulated result is terminal `simulated`, preserves a bounded sanitized diagnostic, and discards authority evidence. No historical records are rewritten; external delivery stays disabled. Provider selection and contract verification are future prerequisites, not MVP launch requirements.

**Validation:** local restore/build (zero warnings), 1841 unit tests, 8 architecture tests, full format verification and offline EF model check passed. The 22 PostgreSQL cases are prepared and compiled, NO EJECUTADAS; sandbox validation is NO EJECUTADA. Provider selection, official sandbox/authentication and secure credential references, request/response/evidence/idempotency contracts and an approved fiscal issuance profile are required if this future work is reopened.

### B7.4 — SIRE feasibility spike
**Requirements:** FR-PUR-006 P2.
**Scope:** technical validation with real allowed credentials/data; implement only if useful inventory detail is confirmed.
**Status:** NOT_STARTED — voluntarily deferred and excluded from the initial MVP; not a launch dependency.

**M7 gate:** tax failures never corrupt commercial sale/stock state; pending work survives restart.

---

# M8 — Backend hardening for real pilot
Goal: production-quality MVP backend.

### B8.1 — Security review
Tenant isolation, authz matrix, file validation, rate limits, CSRF/CORS/session review, secret/log audit.
**Status:** DONE for the local review and hardening; no known unresolved P0/P1 within the reviewed scope. Foundational handlers fail closed without trusted identity, apply the Owner/Pharmacist/Cashier policy and revalidate after waits/before commit. Initial Owner provisioning requires independent platform authority and serialized history checks; shared supplier creation is Owner-only. Restricted purchase queries filter and paginate in SQL, record lookups expose only the branch before authorization, and Excel row/completion transactions reauthorize before commit. Commercial connections reject privileged database roles and multiplexing; migrations and restricted catalog hosts retain separate composition.

**Validation:** local restore/build (zero warnings/errors), 1914 unit tests, 8 architecture tests, full format verification and offline EF model check passed. 17 new real PostgreSQL security cases are prepared and compiled, NO EJECUTADAS: restricted/privileged roles, FORCE RLS, pooling/reopening, tenant/branch query scope, revocation during waits, concurrent/historical Owner bootstrap and import rollback. Existing commercial regression suites remain prepared for external PostgreSQL execution; no migration or schema change was introduced.

**Pilot activation conditions:** execute real PostgreSQL security/transaction/concurrency regression; integrate trusted production OIDC/session and independent platform administration; provision least-privilege runtime credentials and secret management; configure hosting/origins, session-dependent CORS/CSRF and rate limits for the endpoints actually activated. File-input, error/log and current HTTP surface reviews are complete; the API still exposes only health and Development-only OpenAPI. No operational endpoint, production authentication or external tax delivery was enabled. These conditions are pending, not validated production readiness.

### B8.2 — Performance/load targets
Validate catalog search p95 target (<500 ms target from baseline) and online sale confirmation target (<1 s excluding external integrations) on representative data/hardware; optimize measured bottlenecks only.
**Status:** IN_PROGRESS — harness implementation and local validation complete. The opt-in executable `backend/tests/MediPOS.Performance` implements synthetic generation, search/confirmation/full-workflow/mixed-load scenarios, separate contention/retry workloads and structured results. Real PostgreSQL execution, latency baseline and before/after optimization evidence are pending; both p95 targets are NOT_MEASURED. No SQL/index/lock/isolation optimization or migration is introduced without measurements. B8.3 remains NOT_STARTED.

**Local validation:** restore/build passed with zero warnings/errors; 1937 unit tests (23 new deterministic harness cases) and 8 architecture tests passed, as did full format verification and the offline EF model check (no pending model changes). Three new real PostgreSQL regressions compile for guarded disposable generation/non-reuse, tenant/branch/ranking/equivalence/EXPLAIN and mixed-payment/FEFO/commission/last-lot integrity; NO EJECUTADAS. Benchmarks and both latency targets are NOT_MEASURED. Self-audit preserved core business algorithms/security, corrected expected retry classification and incomplete-run approval/validation reporting, confined generation to verified new `template0` databases and separated actual runtime role-check timing/effective session settings from administrative setup.

**Measurement contract:** handler latency uses monotonic `Stopwatch`; nearest-rank percentiles are `sorted[ceil(p*n)-1]`, computed separately for success, expected conflict and error. Default measured attempts are 1000 per scenario/concurrency, after 100 warmup attempts; configurable even counts are 100–10000 and 10–1000. Fewer than 100 successful observations, unexpected errors or an incomplete run cannot yield PASS. Throughput uses the sum of released batch windows, excluding draft preparation, correctness checks and disposal; this is closed-loop batch load, not open-loop HTTP traffic. Confirmation drafts are created outside timing; the full draft/lines/confirm scenario is measured separately. A repeated confirmation is counted as a conflict, never a second independent sale. Warmup mutations remain legitimate synthetic history.

Handler/persistence activities measure access, license/branch, normalization, main search, equivalence, other-branch stock, presentations, composition, draft/cash reads, lock waits, product rechecks, commission snapshot and posting/commit. Inclusive nested phases overlap and must not be summed. EF command timing excludes full reader materialization and manual tenant GUC/SET/commit commands; observed logical connection checkout/opening includes the mandatory runtime role check and pool/network waits, with a separate runtime-role-guard activity; this is not total physical pool/server utilization. Optional EXPLAIN replays the slowest observed read per category with captured typed parameters, selected tenant, deterministic trigram thresholds and a read-only transaction after the workload; plans do not affect measured latency and describe a post-workload diagnostic replay, not the original sample's execution plan/snapshot. HTTP latency is NOT_MEASURED.

**Synthetic profiles:** configure 1/3/5 branches, 1–4 employees per branch, 1–4 tenants, concurrency steps 1/2/4/8 and 2000/10000/30000 products per tenant. These are reference scenarios, not typical pharmacy size claims. Concurrency cannot exceed the available independent operators. Catalog/price/Unicode/medicine/retail content is deterministic by seed; domain UUIDv7 IDs are generated normally. Catalog persistence tracks at most 200 products at once; stocked subset (default 128, max 5000) and lots (default 3, max 10) are configurable. Purchases create receipts/movements, including expired and sellable lots; multi-lot/last-lot preparation uses audited adjustments outside timing. No historical/customer data or dose conversion is involved. The external test identities only replace authentication; the real authorization reader, license/schedule checks, RLS and runtime role guard remain active. Every worker has its own DI scope/DbContext and cash session; only the explicitly named retry scenario uses two scopes for the same seller/draft.

Search covers exact barcode/code/name, prefix, accents, typo, ingredient, brand/laboratory, local/no-local stock, structured equivalence, other-branch retail stock, no results, short/literal wildcard/frequent/broad queries and limits 1/10/25/50 at configured concurrency. Sales cover one/multiple lines, one/mixed payment, two FEFO lots, retail, fractional/package presentations, enabled/disabled commissions, independent sellers/sessions/branches/tenants, full workflow, shortage, two independent confirmations, last-lot competition and same-confirmation retry. Pair workloads require two operators in a branch; otherwise they explicitly report NOT_MEASURED. An 80% search/20% confirmation scenario measures mixed load separately. Payment, movement, audit, commission and nonnegative ledger invariants are checked outside measured windows.

**External execution only:** use a dedicated PostgreSQL 18 performance server with no customer workloads. Supply two credentials through a secure environment mechanism: `MEDIPOS_PERF_CONTROL_CONNECTION` targets its `postgres` maintenance database and can create/drop owned databases, migrate and grant scoped permissions; `MEDIPOS_PERF_RUNTIME_CONNECTION` targets `medipos_perf_<run-id>` on the same host/port with a distinct pre-provisioned NOSUPERUSER/NOBYPASSRLS/NOCREATEDB/NOCREATEROLE runtime role, no table/schema ownership, schema CREATE, inherited bypass or catalog capability. Neither credential belongs in command arguments or results. Control setup is untimed; all measured use cases use the restricted runtime. The executable creates a NEW database from pristine `template0` (existing names are refused), applies current migrations and analyzes seeded tables. It never starts containers or accepts an existing database for generation.

Run from `backend/` on that external host, after injecting those credentials; the 32-character lowercase hex run ID must match the runtime connection database:

```powershell
$env:MEDIPOS_ENVIRONMENT = 'Performance'
$env:MEDIPOS_PERF_OPT_IN = 'CREATE_DISPOSABLE_DATABASE'
# Example run ID; use a new one for every baseline/repeat and match the runtime credential's Database.
dotnet run -c Release --project tests/MediPOS.Performance -- --run-id 0123456789abcdef0123456789abcdef --environment isolated-lab --server-hardware 'CPU, RAM, storage and network description' --products 10000 --branches 3 --operators 2 --tenants 2 --concurrency 1,2,4,8 --samples 1000 --warmup 100 --seed 6282 --output C:\MediPOS-PerformanceResults --explain true
```

Repeat with new databases/run IDs for branch and catalog sizes; a one-branch 8-concurrency profile needs two tenants and four operators/branch. Keep PostgreSQL settings, hardware, network and background workloads comparable; JSON records client/runtime/PG version, relevant PG settings, pool limits, memory observations, all scenario parameters, outcome counts, percentiles, throughput and PASS/FAIL/NOT_MEASURED. Output is outside the workspace (default OS temp), never credentials. Do not use regression fixture hardware to approve a representative baseline. Run the external PostgreSQL security/commercial regression before accepting performance results.

Databases are removed by default on normal completion/failure, after verifying exact run marker and ownership. `--keep-database true` retains the marked run for investigation; explicit cleanup uses the same credentials/opt-in and `dotnet run -c Release --project tests/MediPOS.Performance -- cleanup --run-id <same-id> --environment isolated-lab --server-hardware retained-run`. Cleanup cannot target another database name or marker. Abrupt process/connection loss can leave a disposable database; use guarded cleanup, or administrator review if creation was interrupted before its marker. It never deletes financial rows or disables constraints/RLS to reset scenarios.

**Prioritized hypotheses — NOT evidence of bottlenecks:** (1) compare repeated authorization/license/branch statements and connection-open/role-check overhead; no caching or removal of revalidation without consistency proof; (2) inspect main-search OR/trigram/prefix plans, estimates and stock aggregation against existing tenant B-tree/GIN/FEFO indexes; no speculative index; (3) separate cash/sale/lot contention and per-product preflight/recheck queries; keep lock order and FEFO; (4) inspect equivalence/presentation materialization and bounded composition only if CPU/allocation measurements justify it. Baseline and EXPLAIN must establish the affected query, before/after plan, read/write/maintenance cost and concurrency effects before any optimization.

### B8.3 — Backup/recovery & migration rehearsal
Daily external backup process, destructive-migration backup policy, isolated restore rehearsal and documented recovery checks.

### B8.4 — Observability/operability
Structured logs, correlation ids, health/readiness as needed, core resource/job/CPE/sync metrics and minimal alerts.

### B8.5 — Full MVP acceptance regression
Execute A1–A20 backend-relevant acceptance coverage plus high-risk concurrency/security cases.

**M8 gate:** backend is ready for controlled real-business pilot.

---

# M9 — Post-pilot scale/readiness review
Not an automatic rewrite. Use measured evidence from real customers.

Possible actions only when justified:
- tune queries/indexes/pooling,
- scale VM vertically,
- move PostgreSQL to separate/managed service,
- split heavy background worker while retaining DB queue,
- object storage for growing files,
- only then consider extracting a module/service if real load/ownership requires it.

No microservice migration simply because M9 is reached.

---

## Global quality gates
Before a milestone is marked DONE:
- requirements/sprint exit criteria proven,
- build + relevant tests green,
- critical integration tests use real PostgreSQL,
- tenant/security regression green when affected,
- schema migrations reviewed,
- no unnecessary infrastructure/dependencies,
- no unresolved P0 defect for that milestone.

## Completion definition
“Backend v1 complete” means M0–M5 and M8 are DONE, plus the M6/M7 portions explicitly selected for the commercial release. M9 is evidence-driven scaling, not a prerequisite for initial sale.
