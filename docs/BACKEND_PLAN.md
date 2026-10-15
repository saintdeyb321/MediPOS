# MediPOS — Backend Execution Plan

This is the single execution roadmap for the backend. Prompts are given in chat and are **not** stored in the repository.

## Working state
- **Current milestone:** M8 — Backend hardening for real pilot
- **Current sprint:** B8.6.5.1 — GitHub Actions context validation corrective
- **Status:** IN_PROGRESS — B8.6.5 attempt 38619817614 failed before creating jobs; invalid job-level runner expression removed. Successful external validation and B8.6.3.x/B8.6.4 PostgreSQL acceptance remain pending. Commercial routes stay unpublished. A18 controlled physical purge is NOT_IMPLEMENTED; A20 is a FRONTEND_DEPENDENCY. B8.2 targets remain NOT_MEASURED; B8.3 recovery and B8.4 deployment/monitor validation remain external. Real Google/TLS/reverse proxy/hosting and patched hosting runtime verification remain pending. M8 is not DONE; SUNAT/SIRE and B7.3/B7.4 remain voluntarily deferred.
- **Next gate:** Audit B8.6.5.1, then the repository owner incorporates the correction and manually runs the existing workflow in GitHub. Inspect the tested SHA, gate outcomes, actual PostgreSQL server version and complete counts; neither a pre-job failure nor a corrected file is integration approval. No later sprint is started.

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
**Status:** IN_PROGRESS — harness implementation and local validation complete. The opt-in executable `backend/tests/MediPOS.Performance` implements synthetic generation, search/confirmation/full-workflow/mixed-load scenarios, separate contention/retry workloads and structured results. Real PostgreSQL execution, latency baseline and before/after optimization evidence are pending; both p95 targets are NOT_MEASURED. No SQL/index/lock/isolation optimization or migration is introduced without measurements. B8.3's bounded correction requires successful confirmations to have persisted Confirmed status; rejected drafts require no partial effects, while a shared retry validates the single winner's complete posting separately. No benchmark was executed.

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

**Status:** IN_PROGRESS — B8.3: implementación local completa; restore rehearsal externo pendiente. See [the operational runbook](BACKUP_RECOVERY_RUNBOOK.md) for proposed policy, hosting prerequisites, commands, adversarial rehearsal and controlled production recovery. Proposed RPO 24 hours/RTO 4 hours are not approved guarantees; WAL/PITR and tenant-selective recovery are not implemented.

`backend/tools/MediPOS.Recovery` is a standalone administrative executable, never registered in the commercial host. Full custom dumps and read-only schema/data fingerprints share an exported PostgreSQL snapshot; manifests distinguish CREATED/ARCHIVE_CHECKED from a real restore result. Explicit environment, approved source/target cluster identities, TLS/passfile, protected paths, unique reservations, operator confirmation, restricted roles, target marker/empty database/no other sessions and disabled workers/effects fail closed. Restores preserve ACL/data/history and require matching encoding/collation; migration rehearsal requires approval of the exact offline SQL plan hash. Runtime-role/RLS, constraints/functions/triggers, stock/financial/audit invariants and pending-operation states are checked without repairing restored data.

**Local validation (2026-10-12):** `dotnet restore`, `dotnet build` (0 warnings/errors), UnitTests (1986 passed, including 49 recovery/benchmark cases), ArchitectureTests (8 passed), `dotnet format --verify-no-changes`, and offline EF `has-pending-model-changes` (none) passed. Seven new PostgreSQL regression cases compiled but were **NOT EXECUTED**. Real pg_dump/pg_restore, two-cluster isolation/TLS/ACL, older-backup migration, fault rehearsals and commercial PostgreSQL regressions remain external. No historical migrations, schema or commercial queries changed; no local PostgreSQL/container or version-control operation was performed. M8 is not DONE; B8.4 remains NOT_STARTED.

### B8.4 — Observability/operability
Structured logs, correlation ids, health/readiness as needed, core resource/job/CPE/sync metrics and minimal alerts.

**Status:** IN_PROGRESS — IMPLEMENTACIÓN LOCAL COMPLETA; validación externa pendiente. Existing instrumentation is preserved and the operational contract is documented in [backend/README.md](../backend/README.md#observability-and-operability--b84). JSON console projection retains safe operational warnings/event codes without raw framework messages, SQL, exceptions or request scopes; HTTP correlation, guarded read-only readiness/cancellation/concurrency, no-cache responses and bounded native operation metrics are implemented. Queue-depth/state-age gauges and alert delivery require external authorized monitors; no polling worker, collector, commercial endpoint or provider is enabled. B8.2/B8.3 external gates remain pending; M8 is not DONE and B8.5 is NOT_STARTED.

**Local validation (2026-10-13):** `dotnet restore`, `dotnet build` (0 warnings/errors), `dotnet test tests/MediPOS.UnitTests` (2044 passed, 0 failed/skipped), `dotnet test tests/MediPOS.ArchitectureTests` (8 passed), `dotnet format --verify-no-changes` and offline EF `has-pending-model-changes` (none) passed. Two real PostgreSQL readiness cases compiled but were **NOT EXECUTED**. HTTP tests use only a controlled diagnostic probe and certify no database behavior. Runtime-role/read-only verification in PostgreSQL, proxy access/TLS, resource baselines, collectors and alert delivery remain external; no production validation is claimed. No version-control or local PostgreSQL/container operation was performed.

### B8.5 — Full MVP acceptance regression
Execute A1–A20 backend-relevant acceptance coverage plus high-risk concurrency/security cases.

**Status:** IN_PROGRESS — local acceptance review completed; mandatory implementation/integration gaps prevent MVP/pilot approval. No domain algorithm, authorization policy, migration or commercial route changed. B8.2/B8.3/B8.4 external gates remain pending.

#### Acceptance traceability (2026-10-13)

Preserve SPEC section 7's grouped anchors; it does not define separate subcriteria within A1–A2, A3–A7, A8–A11 or A12–A13. States describe the indicated scope, not end-to-end acceptance: **VERIFIED_LOCAL** = executed local tests; **PREPARED_EXTERNAL** = written PostgreSQL tests, unexecuted; **NOT_IMPLEMENTED** = missing required functionality; **BLOCKED_PRODUCTION_INTEGRATION** = real identity/HTTP/infrastructure pending; **FRONTEND_DEPENDENCY** = PWA/UI/device behavior pending; **DEFERRED_APPROVED** = explicitly excluded initial-launch work.

Test references below are class names under `backend/tests/MediPOS.UnitTests` (U) or `backend/tests/MediPOS.IntegrationTests` (PG), with module directories matching the responsible slice. All U references participated in the executed full unit suite. **Every PG reference is PREPARED_EXTERNAL, never PASS.** `PersistenceAuthorizationFixtures` replaces administrative authorization in older persistence suites; those tests prove neither real OIDC nor their stubbed authorization boundary. Current policy must also be checked through `SecurityHardeningTests`, runtime-role/RLS tests and actual resolver-based paths. All commercial HTTP workflows remain **BLOCKED_PRODUCTION_INTEGRATION**: the real host exposes health/readiness and Development OpenAPI only, registers no production Google/session adapters, and fails closed through disabled administrative authority.

| SPEC anchor | Requirements/rules; responsible handlers/modules | U: VERIFIED_LOCAL scope | PG: PREPARED_EXTERNAL coverage | Positive/adversarial scope; remaining acceptance |
|---|---|---|---|---|
| A1–A2 | FR-LIC-001..004, FR-TEN-001..003, FR-AUT-001..005; BR-011/016/017. CreateTenant, license lifecycle, CreateMembership, CreateBranch, assignments/schedules, ResolveAccessContext | LicenseTests, ApplicationTests (TenancyLicensing), BranchApplicationTests, IdentityApplicationTests, OperationalAccessTests, TenantOperationAuthorizationTests, SecurityCompositionTests | TenancyLicensingTests, BranchesPersistenceTests, OwnerConcurrencyTests, OperationalAccessPersistenceTests, SecurityHardeningTests | Create/renew/reactivate and provision via internal handlers; two Owners, branch limit/race, anonymous/foreign branch/expired schedule/suspended license denied; deactivation keeps history. Real OIDC/session, independent platform identity and administrative HTTP are BLOCKED_PRODUCTION_INTEGRATION. Synthetic identities do not satisfy FR-AUT-001. |
| A3–A7 | FR-CAT-001..007/009, FR-PUR-001..004, FR-INV-001..007, FR-POS-002..004; BR-002..005/012/013. Catalog/SearchProducts, Excel import, ConfirmPurchase, AdjustStock/FEFO, expiration alerts | CatalogDomainTests, CatalogApplicationTests, ProductImportTests/ProductImportWorkbookTests, PharmaNormalizationTests, ProductUnitTests, SearchProductsTests, PurchasingWorkflowTests, InventoryDomainTests/AdjustStockTests, ExpirationJobTests | CatalogFoundationTests, ProductImportIntegrationTests/ProductImportIsolationTests, PharmaNormalizationIntegrationTests, ProductUnitIntegrationTests, ProductSearchIntegrationTests, PurchaseWorkflowIntegrationTests/PurchasingIsolationIntegrationTests, InventoryPersistenceTests/InventoryQueriesTests, PersistentNotificationsTests | Global/private and medicine/retail distinction; per-row import errors, exact conversions, atomic receipts, lot/cost/expiry lineage, accent/typo ranking, structural equivalence, FEFO and 30-day alerts. Invalid quantities, ambiguous data, shortage, failed audit/write and concurrent decrements covered. No dose conversion or clinical recommendation. Search SQL/extensions, ledger/RLS and real races require PG; HTTP activation remains blocked. |
| A8–A11 | FR-POS-005..010, FR-CASH-001..005, FR-TRF-001..006, FR-COM-001..004; BR-002/003/006..011. ConfirmSale, VoidSale, CloseCashSession, stock/cash transfers, commission posting | SaleConfirmationDomainTests, ConfirmSaleUseCaseTests, VoidSaleUseCaseTests, CashCloseUseCaseTests, CashTransferUseCaseTests, TransferUseCaseTests, CommissionDomainTests/ExactMoneyRoundingTests | SaleCheckoutPersistenceTests/SaleCheckoutConcurrencyTests, SaleVoidPersistenceTests/SaleVoidConcurrencyTests, CashClosePersistenceTests/CashCloseConcurrencyTests, CashTransferConcurrencyTests, TransferPersistenceTests/TransferConcurrencyTests, CommissionPostingTests/CommissionAtomicityTests/CommissionConcurrencyTests | Multiple lines/presentations/lots, exact mixed payments, seller/cash linkage, one winning retry, nonnegative stock, audited void with exact payment/stock/commission compensation, original commission snapshots, reconciliation and batch-preserving transfers. Last-unit, duplicate dispatch/receipt/void/close and financial/audit rollback remain PG-only proofs; never count them as locally validated. |
| A12–A13 | FR-TEN-004, FR-RPT-001..005, FR-COM-004; BR-010/012/015. Owner overview, operational/commission reports, ExportOwnerReport | GetOwnerBranchOverviewTests, OperationalAuthorizationTests, OperationalPeriodAndMoneyTests, OwnerCommissionsReportTests, ReportExportAuthorizationTests/ReportExportRenderingTests | OwnerOverviewPersistenceTests/OwnerOverviewIsolationTests, OperationalReportsConsistencyTests/OperationalReportsPoolingTests, FilteredSalesAggregationTests, OwnerCommissionsReportIntegrationTests, ReportExportPersistenceTests | Branch/consolidated and half-open Lima periods, exact monetary totals, recognition/void semantics, combined filters/pages and Excel/PDF outputs. Cashier/Pharmacist, foreign scope, overflow and concurrent void denied or handled without inconsistent snapshots. Local rendering is not PostgreSQL aggregate/snapshot validation; production HTTP remains blocked. |
| A14 | FR-OFF-001..005; BR-003/011/018/019. Register/RevokeSyncDevice, ReserveSyncEventReceipt, ProcessOfflineSyncBatch, OfflineSale processor/conflict resolution, branch catalog sync | SyncDomainTests/SyncAuthorizationTests, OfflineSyncBatchTests, OfflineSaleTests, BranchCatalogTests, OfflineSyncBatchHttpContractsTests | OfflineSyncPersistenceTests/OfflineSyncConcurrencyTests, OfflineSyncBatchInterruptionTests/OfflineSyncBatchPersistenceTests, OfflineSalePostingTests/OfflineSaleSecurityTests, BranchCatalogConcurrencyTests/BranchCatalogSecurityTests | UUID identity, altered payload/device rejection, durable replay, interrupted committed prefix, stock conflict after/against online sale, revoked access and explicit reconciliation. Automatic client outbox/sync and cached shell are FRONTEND_DEPENDENCY; HTTP/OIDC is BLOCKED_PRODUCTION_INTEGRATION. Backend tests do not complete the offline PWA. |
| A15 | BR-001/011/012/015; FR-AUT-002/003. TenantDataContext, tenant filters/write guards, RLS/GUC interceptors, runtime-role guard | TenantDataContextTests, AccessDataScopeTests, SecurityCompositionTests and operational authorization tests validate only local scope/guard composition | TenantRowLevelSecurityTests, TenantQueryProtectionTests, TenantConnectionReuseTests, RuntimeRoleHardeningTests plus module isolation/pooling suites | Tenant A/B and unselected scopes, reads/inserts/updates, cross-tenant FKs, pool checkout/reopen/rollback and privileged-role rejection prepared. **Actual tenant isolation, RLS and connection reuse remain PREPARED_EXTERNAL**, not VERIFIED_LOCAL; production least-privilege roles must be separately verified. |
| A16–A18 | FR-INV-006, FR-AUD-001..003, FR-LIC-004/005; BR-002/007/011/017/020. AdjustStock, AuditTrail, Suspend/ReactivateLicense, RequestTenantPurge | InventoryDomainTests/AdjustStockTests, AuditLogTests, LicenseTests/ApplicationTests validate adjustment reason/before-after, immutable local audit and lifecycle gates | InventoryPersistenceTests, AuditFoundationTests, TenancyLicensingTests, SecurityHardeningTests; new checkout-revocation cases in SaleCheckoutConcurrencyTests | Adjustment/audit atomicity, append-only normal-user audit, suspension/reactivation and rechecks during waits covered or prepared. RequestTenantPurge only persists PurgePending/history. **A18 physical purge, retention/export/confirmation workflow and zero-private-orphan verification are NOT_IMPLEMENTED**; no deletion test proves them. A18 remains a mandatory launch blocker. |
| A19–A20 | FR-POS-001/008/009, FR-OFF-001..005, BR-014/016/018/019; FR-CPE-001..005 P1. GetInternalTicket, CPE core, typed offline/catalog HTTP contracts | InternalTicketTests, ElectronicDocumentInvariantTests/ElectronicDeliveryContractTests, OfflineSyncBatchHttpContractsTests/BranchCatalogHttpContractsTests | InternalTicketPersistenceTests/InternalTicketIsolationTests, ElectronicDocumentPersistenceTests; offline/catalog suites above | Internal ticket retains exact sale/payment lineage and explicit non-tax print contract; simulation cannot certify authority acceptance. B7.3/B7.4 real tax-provider/pilot work is DEFERRED_APPROVED. **A20 desktop/mobile UI, installable/cached shell, actual offline outbox and thermal-print/device operation are FRONTEND_DEPENDENCY**, with HTTP/OIDC blocked. No browser/device acceptance was executed. |

Backend contracts needed by the PWA exist for device lifecycle, UUID/versioned event results/replay/conflicts, bounded batch acknowledgments, branch catalog/stock snapshot/delta cursors, internal ticket data and stable Problem Details. They are internal use cases/typed mappings, not activated authenticated routes. PWA storage, network recovery, installation, keyboard/touch usability and printing still need frontend plus end-to-end production-boundary tests.

#### P0 adversarial regression inventory

Reuse the existing tests below; their PG execution remains external. Unit authorization/decimal tests do not substitute for these database cases.

| Priority scenario(s) | PostgreSQL regression entry points |
|---|---|
| Last unit; duplicate/retried confirmation | SaleCheckoutConcurrencyTests: `LastUnitAcrossDifferentSellersAndCashSessionsHasExactlyOneWinner`, `TwoConfirmationsOfOneDraftCommitExactlyOnePaymentMovementAndAudit` |
| Revocation while waiting before posting | SecurityHardeningTests: `AuthorizationChangesDuringLicenseLockWaitRollBackWithoutPurchaseOrStock`; SaleCheckoutConcurrencyTests: **new** `RevocationCommittedDuringRealLotLockWaitPreventsEveryCheckoutEffect` (license/membership/assignment/schedule) |
| Cross-tenant and cross-branch access | TenantRowLevelSecurityTests, SaleDraftIsolationTests, TransferIsolationTests, SecurityHardeningTests reference-query scope and OperationalAccessPersistenceTests |
| Failed audit, partial writes and clean retries | SaleCheckoutPersistenceTests: `InjectedWriteFailureRollsBackPaymentMovementBalanceSaleTimestampAndAudit`; CommissionAtomicityTests; PurchaseWorkflowIntegrationTests; OfflineSyncBatchInterruptionTests |
| Void after cash close | SaleVoidConcurrencyTests: `ClosedOriginalCashIsRecheckedAfterItsLockWaitEvenForOwner`; CashCloseConcurrencyTests verifies both lock orders for confirmation/void versus close |
| Invalid adjustment/receipt quantities | InventoryPersistenceTests ledger/projection/deferred checks; PurchasingIsolationIntegrationTests: `PositiveReceiptConstraintAndUniqueSourceLinePreventInvalidOrDuplicateReceipts`; pure signed-quantity input boundaries remain unit tests |
| Offline conflict against online sale | OfflineSalePostingTests: `OnlineCheckoutAndOfflineSaleUseTheSameStockLockPolicy`; `TwoDevicesContendForFinalMedicineThenOwnerReviewsWithoutDuplicateSalesOrConflicts` |
| Commission reversal | CommissionPostingTests: `MixedPaymentMultiProductMultiLotSaleAndVoidPreserveSellerSnapshotsAndFullyCompensateHistory`; CommissionConcurrencyTests duplicate void; CommissionAtomicityTests failed reversal |
| Repeated transfer dispatch/receipt | TransferConcurrencyTests: `ConcurrentDoubleDispatchOrReceiptHasOneWinnerAndNoDuplicateEffects`; CashTransferConcurrencyTests concurrent overspend/double receipt |
| Reused Npgsql connections | TenantConnectionReuseTests, RuntimeRoleHardeningTests and module `*Isolation*`/`*Pooling*` tests, including unselected context and reopening |
| Unauthorized costs/owner reports | OwnerOverviewIsolationTests: `OperationalRolesCannotExecuteManagerialQueries`; OperationalReportsPersistenceTests and OwnerCommissionsReportIntegrationTests deny operational roles; ReportExportPersistenceTests checks role/tenant/branch |
| Invalid mixed payment/money | SaleCheckoutPersistenceTests: `InvalidPaymentsLeaveDraftStockAndAuditUntouched`; CommissionSchemaIsolationTests checks PostgreSQL/C# rounding and range consistency |

**Coverage correction:** one unit case now checks revoked branch assignment after lot waiting in ConfirmSaleUseCaseTests. Four PostgreSQL cases use an observed real lot-lock wait, commit an audited authorization change through existing stores, then require the stable denial and no sale payment, stock decrement, commission or confirmation audit. They compile but are NOT EXECUTED. These cases cover changes visible at the final access recheck; they do not claim serialization of every later revocation through COMMIT. Existing business, rollback, RLS and concurrency assertions are retained. No demonstrated domain defect was changed merely to obtain green tests.

#### External PostgreSQL execution and acceptance

Use an approved disposable external runner with .NET 10, the existing Testcontainers-compatible Docker environment and PostgreSQL 18 image (`postgres:18-alpine`); never this local workstation, production connections or customer backups. The existing PostgreSqlFixture creates a fresh container/database per test process, applies **all current migrations** with separate setup authority, generates ephemeral passwords and grants restricted runtime NOSUPERUSER/NOBYPASSRLS/NOCREATEDB/NOCREATEROLE credentials without ownership/DDL. Catalog importer/reviewer/publisher roles remain separate. Administrative contexts are only independent constraint/setup/adversarial references, never RLS evidence. Native external-server injection is not implemented in this fixture; do not point it at an existing/customer database.

Keep the PostgreSQL collection's disabled parallelization; cases create independent synthetic tenant pairs, while race cases deliberately use separate scopes/connections and barriers/observed lock waits. Do not replace clocks/sessions or old administrative stubs with a claim of real authentication. Global trigger/DDL fault cases must remain isolated and restore their fixtures; a failed cleanup requires a fresh disposable run, not reuse or manual repair.

Suite order for triage: foundation/security (`Tenancy`, TenancyLicensing, Branches, IdentityAccess, AuditSupport), commercial transaction/stock (Catalog, Purchasing, Inventory, SalesPos, Cash, Transfers, Commissions), Reporting, OfflineSync, then implemented CPE core/Observability and synthetic Performance/Recovery regressions. The latter are correctness regressions, not B8.2 performance measurement or B8.3 backup/restore rehearsal. Full project execution is the required regression evidence; a filtered pass is only diagnostic.

**External runner only**, from `backend/`, after restore/build:

```powershell
dotnet test tests/MediPOS.IntegrationTests --logger "trx;LogFileName=b85-postgresql.trx" --results-directory artifacts/b85/postgresql
```

Retain command/UTC execution window, .NET/PostgreSQL/image versions, environment/resources, assembly/source hashes, restricted-role/guard results and TRX totals/names/failures without credentials. Require a complete run, zero failures and no omitted mandatory cases; reconcile discovered counts with the built suite, inspect race/rollback assertions and keep unit/architecture evidence separate. No external result is available now. On failure stop approval, retain sanitized synthetic evidence, reproduce the specific case on a fresh isolated database, correct within a separately reviewed scope and rerun the affected cases plus the complete gate; never weaken RLS/constraints, suppress a failure or repair customer data to get PASS.

#### Local evidence and launch blockers

**Executed 2026-10-13:** `dotnet restore` passed; `dotnet build` passed (0 warnings/errors, integration tests compiled); `dotnet test tests/MediPOS.UnitTests`: **2045 passed, 0 failed, 0 skipped**; `dotnet test tests/MediPOS.ArchitectureTests`: **8 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` passed; offline `dotnet ef migrations has-pending-model-changes --project src/MediPOS.Infrastructure --startup-project src/MediPOS.Infrastructure --no-build`: no model changes, using provider-only configuration without a connection. The complete local gate passed. No Git, PostgreSQL, container, benchmark or restore-rehearsal operation was performed locally.

Pilot approval remains blocked by:

- **P0 — production access:** FR-AUT-001/002 and A1–A2 need verified Google OIDC, secure sessions, independent platform authority and authenticated HTTP adapters with their actual CORS/CSRF/rate-limit policy. Disabled authority/absent routes are safe closure, not delivered user workflows.
- **P0 — controlled physical purge:** FR-LIC-005/BR-020/A18 lack implementation and orphan/mandatory-history validation. Proposed separate corrective sprint: **A18 — controlled tenant purge**, first approving confirmation/export/retention policy, then implementing isolated administration and synthetic PostgreSQL verification. PurgePending cannot satisfy this gate.
- **P0 — database integrity/security evidence:** all real RLS/runtime-role, transactions, SQL, financial reversal, last-stock/cash/Owner races and pooling proofs are PREPARED_EXTERNAL. Any failure or missing mandatory case blocks acceptance.
- **P0 — operational recovery:** B8.3's actual isolated restore, protected storage/authentication and approved recovery policy remain prerequisites; this regression cannot certify them. B8.4 restricted-role readiness/proxy/TLS and production secret/role controls also need deployment evidence.
- **P0 — user/offline acceptance:** A20/FR-POS-001 and the client parts of A14/FR-OFF-001..005 need the PWA and real browser/device/network/printing workflows. Backend serialization and fake diagnostic probes cannot certify these.
- **P1 — measured operation:** B8.2 search/sale targets are NOT_MEASURED; B8.4 hosting resources/collectors/alert delivery and pending backlog observers remain external. Record actual measurements and operator decisions before pilot approval; no automatic alert sender is claimed. B7.3/B7.4 remain DEFERRED_APPROVED, not silent MVP requirements or validated SUNAT integrations.

### B8.6.1 — Google OIDC and secure production sessions (corrective)
**Requirements:** FR-AUT-001..005, BR-011/016/017, ADR-007; resolves only B8.5's authentication/session implementation gap.

**Status:** IN_PROGRESS — IMPLEMENTACIÓN LOCAL COMPLETA; opt-in real ASP.NET Core OIDC/cookie adapters and local gate complete; real Google, PostgreSQL and deployment remain external. Configuration and operational prerequisites are in [backend/README.md](../backend/README.md#google-oidc-and-secure-server-sessions--b861). A1–A2 and M8 are not complete.

Authorization Code + PKCE and standard issuer/audience/signature/lifetime/state/nonce/correlation validation precede the final TicketReceived event and existing UpsertGoogleUserHandler. Google `sub` establishes global identity; email never merges accounts. The protected cookie references a bounded process-local server ticket containing only the internal UUID. Absolute expiry, explicit server revocation on logout/login, no sliding/token retention, certificate-encrypted durable key ring, strict single HTTPS origin/trusted scheme-only proxy, antiforgery and native rate limits are implemented. Five authentication routes are opt-in; platform administration, CPE provider/authority and commercial routes remain disabled. No new identity table, migration, financial/domain rule or distributed session infrastructure.

Local HTTP tests use actual HTTPS/OIDC/cookie middleware with synthetic keys/backchannel and fake persistence ports. They verify configured protocol rejection and safe composition, not Google's live signatures/service or PostgreSQL. One same-email/different-sub persistence regression is prepared for external PostgreSQL execution. Required external activation evidence: real client/consent/callback/discovery/exchange, runtime-role persistence and revocation, TLS/proxy spoofing, key/PFX/secret ACLs/durability/recovery and real browser cookie/CSRF/logout/restart behavior. Process restart intentionally requires re-login; multi-instance sessions are not implemented. B8.5's historical acceptance matrix remains evidence of that audited snapshot; authentication implementation now exists behind configuration, while its production integration and every other launch blocker remain pending.

**Local validation (2026-10-13):** `dotnet restore`, `dotnet build` (0 warnings/errors), `dotnet test tests/MediPOS.UnitTests` (**2085 passed, 0 failed/skipped**, including 40 new authentication cases), `dotnet test tests/MediPOS.ArchitectureTests` (**8 passed, 0 failed/skipped**), `dotnet format --verify-no-changes` and offline EF `has-pending-model-changes` (none) passed. One new PostgreSQL identity case compiled but was **NOT EXECUTED**. No real Google/PostgreSQL/container or version-control operation was performed. Live OIDC and external hosting/storage controls are unverified; B8.2/B8.3/B8.4/A18/PWA and independent platform/commercial API gates remain pending. Stop here for audit.

### B8.6.1.1 — Authentication rate limiting correction
**Status:** IN_PROGRESS — IMPLEMENTACIÓN LOCAL COMPLETA; independent audit and external hosting acceptance pending. Corrects only the shared auth-flow availability defect; B8.6.1's original scheme-only proxy/rate-limit description above records its historical baseline and is superseded by this correction.

Invalid callbacks previously spent the same 20-request process quota as every client's login/logout. Native chained limits now admit client/operation traffic before global minute admission: login/callback/logout each 20/minute, session query 60, CSRF 30 and other auth paths 10; aggregate 60/minute per client bucket versus unchanged 120 global. Concurrent auth remains 4 globally and 2 per bucket, with callbacks capped at 2 globally and 1 per bucket; every queue is zero. Secret-keyed IP hashing bounds attribution to 1024 buckets (at most 9226 native cache partitions), including under IPv6 churn. NAT/hash collisions conservatively share quotas. Distributed overload can still exhaust the deliberate global safety limit; external traffic and edge controls remain required.

Direct attribution uses only the TLS socket peer. TrustedProxy checks the original peer before one-hop native IP/scheme forwarding, requires unambiguous headers and explicit sanitation acknowledgement, then retains strict Host/HTTPS checks before rate limiting and OIDC. Automatic/earlier forwarding is unsupported; automatic forwarding configuration fails activation. Operators must discard browser forwarding headers, emit trusted connection-derived IP/HTTPS, restrict backend access and validate the topology externally. Final limits/configuration are in [backend/README.md](../backend/README.md#google-oidc-and-secure-server-sessions--b861). No domain, migration, tenant authorization, platform/commercial endpoint or OIDC/CSRF/session protection changes.

**Local validation (2026-10-13):** `dotnet restore`, `dotnet build` (**0 warnings/errors**), `dotnet test tests/MediPOS.UnitTests` (**2105 passed, 0 failed/skipped**, including 20 additional corrective cases), `dotnet test tests/MediPOS.ArchitectureTests` (**8 passed, 0 failed/skipped**), `dotnet format --verify-no-changes` and offline EF `has-pending-model-changes` (**none**) passed. Regression covers per-operation/aggregate/global quotas, rejected callbacks not spending the global minute quota or calling UpsertGoogleUser, trusted client isolation with bounded collisions, slow callback/global concurrency and lease release, spoofed headers, missing/ambiguous/unknown proxy attribution, strict Host, automatic forwarding refusal and valid OIDC/PKCE/state/nonce/correlation/CSRF/session/logout after abuse. Fixtures use controlled loopback HTTPS/proxy headers and synthetic provider/persistence ports; they do not certify production proxies, Google or PostgreSQL. Existing PostgreSQL suites compiled but were **NOT EXECUTED**. No Git/GitHub/container/database operation occurred.

**External gates unchanged:** real Google OIDC/PostgreSQL and hosting/edge limits; independent platform authority; commercial API; A18 controlled purge; A20 PWA; B8.2 performance, B8.3 recovery and external B8.4 observability validation. M8/A1–A2 remain incomplete. No subsequent sprint is started.

### B8.6.2 — Independent platform administration authorization
**Requirements:** FR-LIC-001..005, FR-AUT-001..005, BR-001/011/016/017; independent platform authority only, no HTTP exposure of use cases.

**Status:** IN_PROGRESS — IMPLEMENTACIÓN LOCAL COMPLETA; independent audit and real Google/PostgreSQL/hosting acceptance pending. B8.6.1/B8.6.1.1's earlier statements that platform authority was unimplemented describe their historical snapshots; this sprint adds an opt-in adapter, not production validation or public administration.

The API host registers `SessionPlatformAdministration` only with explicit enabled Production configuration, valid opt-in Google/session composition and a validated protected startup allowlist of 1–32 unique internal UUIDs. Missing/disabled configuration preserves `DisabledPlatformAdministration`; invalid enabled configuration fails startup without identifiers. The adapter requires the successful native MediPOS cookie authentication result, strict internal session identity, matching existing authenticated-user adapter and current issuance/expiry. Email, Google profile/subject supplied by clients, tenant roles and request IDs provide no authority. The immutable snapshot is replaced only by applying configuration and restarting; there is no hot revocation/self-grant endpoint or new administrator table/role. Hosting approval/configuration protection and effective revocation remain external controls, documented in [backend/README.md](../backend/README.md#independent-platform-administration--b862).

Existing consumers remain CreateTenant, RenewLicense, SuspendLicense, ReactivateLicense, RequestTenantPurge, CreateCategory, CreateGlobalProduct and initial-Owner provisioning. Actor mismatch checks and tenant/license/membership audit contracts remain intact; global catalog retains its prior persistence contract. Platform approval and Owner/employee membership remain independent, including deactivation and license enforcement. CreateMembership preserves its historical platform-first precedence for an operator with both grants: the platform path only bootstraps the first Owner under the license lock; any active/inactive Owner history or employee target denies it. Later ordinary Owner administration remains the separate existing tenant path. No business handler, financial rule, domain model, migration, RLS policy, OIDC/session protection or runtime database privilege changed. No new production HTTP route or physical purge was enabled.

**Local validation (2026-10-13):** `dotnet restore`, `dotnet build` (**0 warnings/errors**), `dotnet test tests/MediPOS.UnitTests` (**2157 passed, 0 failed/skipped**, including **52 new platform authorization cases**), `dotnet test tests/MediPOS.ArchitectureTests` (**8 passed, 0 failed/skipped**), `dotnet format --verify-no-changes` and offline EF `has-pending-model-changes` (**none**) passed. Tests cover disabled/invalid activation, native internal-session identity/expiry/cancellation, actor mismatch, configuration snapshot replacement and effective restart revocation, independent platform/Owner access and deactivation/license suspension, same-email/different-sub rejection, preserved audited creation/purge request/bootstrap, no repeated active/inactive historical bootstrap, unchanged public routes and sanitized logs. HTTP transport/provider and persistence ports are synthetic fixtures; adapter/application tests use a synthetic trusted authentication feature, never proof of real Google. Three new PostgreSQL cases compiled but were **NOT EXECUTED**: restricted-runtime concurrent initial Owner/history/audit; mismatched actor/effective allowlist revocation without unintended writes; and audited purge request preserving tenant/User/license history. No Git/GitHub, container or local database operation occurred.

**Pending:** independent audit; external PostgreSQL and genuine Google/hosting approval/revocation/session validation; external B8.2, B8.3, B8.4, B8.6.1 and B8.6.1.1 gates. Commercial/administrative HTTP, controlled physical purge A18 and PWA A20 remain incomplete. M8/A1–A2 are not DONE, production readiness is not declared, and B8.6.3 is not started.

### B8.6.3 — Minimal platform tenant/license HTTP API
**Requirements:** FR-LIC-001..005, FR-AUD-001..003, BR-001/017/020; reuse existing handlers/independent platform authority, no commercial HTTP.

**Status:** IN_PROGRESS — the initial locally validated delivery below exposed renewal, suspension and reactivation only. Its creation blocker is superseded by B8.6.3.1 below, which adds the fourth opt-in route. This subsection retains the original delivery/validation evidence, not the current route inventory. No overall sprint completion or production readiness is claimed.

`PlatformApi:Enabled=true` requires Production, complete valid Google/session activation and enabled valid protected platform administration; absent/false maps no platform route and invalid enabled activation fails startup. Three POSTs under `/platform/tenants/{tenantId}/licenses/{licenseId}` reuse RenewLicense, SuspendLicense and ReactivateLicense handlers. Native session authorization and independent `PlatformAdministrationAccess.RequireAsync` supply the actor before parsing/persistence; client actor/user/role/tenant headers/query fields cannot supply identity and additional JSON fields are rejected. Handler authorization/audit checks remain unchanged. The existing original-peer/HTTPS/Host/single-hop proxy boundary explicitly protects `/platform`, with exact Origin, same-origin fetch restrictions and native CSRF tokens. DTOs are explicit and use stable string status codes. Strict UTF-8 JSON is limited to 4096 bytes/depth 4, including streamed bodies; unknown/duplicate/missing/malformed fields, unsupported encodings and numeric enums fail with sanitized central Problem Details. Privacy headers survive central exception handling.

Administrative rate admission follows independent authorization: 10 requests/operator/minute and 30 globally; concurrency 1/operator and 2 globally, no queues. Only at most 32 configured approved UUIDs can allocate partitions (maximum 66). Authentication limits remain separate. Existing domain semantics are preserved: equal-expiration renewal and repeated suspension are no-ops; repeated reactivation returns the existing conflict; optimistic persistence conflicts return `license.concurrent_change`. No durable request ledger or exactly-once intent semantics are claimed for license mutations. Full activation/routes/body/error/retry/hosting contracts are in [backend/README.md](../backend/README.md#minimal-platform-license-http-api--b863).

**Original blocker, addressed in B8.6.3.1:** the initial delivery deliberately omitted `POST /platform/tenants`: internal CreateTenant alone generates new IDs on retries without a durable operator/request receipt. B8.6.3.1 supplies the additional transaction/orchestration contract and new migration below; its real PostgreSQL proof remains external.

**Local validation (2026-10-13):** `dotnet restore`, `dotnet build` (**0 warnings/errors**), `dotnet test tests/MediPOS.UnitTests` (**2208 passed, 0 failed/skipped**, including **51 new HTTP/admission cases**), `dotnet test tests/MediPOS.ArchitectureTests` (**8 passed, 0 failed/skipped**), `dotnet format --verify-no-changes` and offline EF `has-pending-model-changes` (**none**) passed. Coverage includes missing/false/invalid activation and exact public surface, anonymous/ordinary Owner denial before persistence, real synthetic OIDC sessions reaching the correct handlers with server actor/audit, CSRF/origin/Host/known and unknown proxy controls, bounded JSON/encodings/streaming, foreign/invalid IDs, domain/concurrency errors, domain retries, restart revocation, per-operator/global rate/concurrency and lease release, privacy headers and log/error redaction. Recording persistence proves HTTP composition, not database integration or Google. Two new real PostgreSQL persistence cases compiled but were **NOT EXECUTED**: repeated renewal/suspension and reactivation conflict with authenticated history/audit; concurrent renewal with exactly one commit and rollback of the loser's appended history/audit. Tenant-create durable-idempotency tests remain part of the blocked corrective. No local database/container or Git/GitHub operation occurred.

**Still unpublished/pending after the corrective:** tenant deletion and any purge endpoint, global catalog/employee administration, first Owner HTTP bootstrap, commercial POS/cash/stock/reports and a complete platform dashboard. A18 physical purge and A20 PWA remain incomplete. Real Google/PostgreSQL/hosting, protected configuration/allowlist restart and proxy/capacity controls plus external B8.2, B8.3, B8.4, B8.6.1 and B8.6.1.1 gates remain pending. SUNAT/SIRE remain voluntarily deferred. M8/A1–A2 are not DONE; no later sprint is started.

### B8.6.3.1 — Safe idempotent tenant creation
**Requirements:** FR-LIC-001, FR-AUD-001..003, BR-001/017; resolve only B8.6.3's creation blocker.

**Status:** IN_PROGRESS — code and prepared PostgreSQL tests integrated; independent audit and external persistence/security evidence required. No next sprint or production-readiness declaration.

`POST /platform/tenants` reuses PlatformApi's default-off opt-in, actual server session/Superadmin, HTTPS/Host/Origin/proxy boundary, native CSRF, 4 KiB strict JSON and administrative quota. Business fields only: tradingName, startsAt, expiresAt, maxBranches, status. Mandatory single 36-character nonzero UUID `Idempotency-Key`; 400 `platform.idempotency_key_invalid` otherwise. Actor always comes from the server. First commit: 201 / `Idempotency-Replayed: false`; matching replay: 200 / true and the original LicenseDetails, without Location. Changed payload: 409 `platform.idempotency_conflict`. Existing three license endpoints retain their state-dependent retry semantics.

Application adds a specific creation-request contract/orchestrator around the unchanged CreateTenantHandler. Versioned SHA-256 input is a deterministic UTF-8 JSON array of operation, trimmed Unicode/case-preserved name, invariant UTC round-trip start/end, invariant branch count and explicit stable string status code. Dates truncate submicrosecond ticks before both hashing and creation. Uniqueness is authenticated operator + `platform.tenant.create.v1` + UUID; different keys/operators are independent intents. Recheck authority after a potential unique-index wait and before commit; replay requires current authority. No in-process automatic retry of uncertain commits; callers keep their key and payload to resolve response loss via a new request.

New migration **20261014025458_AddTenantCreationIdempotency**, with updated EF snapshot, creates only global technical receipt storage, composite PK, license/tenant FK and constraints. Infrastructure uses READ COMMITTED INSERT/ON CONFLICT reservation and persisted original-result replay in the same DbContext/transaction as tenant, license/creation history and audit. Deferred completion guard prevents pending commits; completed result/key/hash are immutable and completion checks matching license and actor audit. Forced operator RLS uses a transaction-local server actor GUC, with no SECURITY DEFINER/bypass or PUBLIC access. Separate migration authority must grant runtime SELECT, INSERT only reservation columns and UPDATE only result columns; no DELETE/TRUNCATE/ownership/DDL. Runtime fixture carries exactly these new grants. Custom GUCs assume trusted application/database credentials, as existing tenant RLS does.

Receipts have indefinite retention, no TTL/cleanup, no full payload/trading name. Preserve v1 canonicalization and receipts for historical replay; future A18 purge/retention must approve compatible tombstones/results before deleting referenced histories. Earlier-point restore loses later requests and tenant commits and requires B8.3 recovery-window reconciliation. No historical migration, domain/commercial rule, existing license handler, tenant RLS or business model was changed.

Offline model comparison exposed provider-generated loss of explicit public extension schemas. Preserve the existing pg_trgm/unaccent annotations in the current snapshot and new designer only; no extension DDL or historical migrations change. A permanent offline model/snapshot unit assertion checks this. Global operator receipts map ResultTenantId as a result reference, retaining all existing tenant-private model checks without exceptions. The old recovery-plan unit test now compares the real latest migration script instead of hard-coding ElectronicDocuments; recovery runtime behavior is unchanged.

**Prepared external evidence:** TenantCreationIdempotencyPersistenceTests covers actual unique-index blocking with observed PostgreSQL lock wait, equivalent concurrent calls, fresh-scope lost-response replay, immutable original result after license mutation, conflicts, separate keys/operators, revoked authority, failure before/after audited save with full rollback/retry, incomplete receipt commit denial, completed-receipt immutability, restricted grants and operator RLS/transaction/pool resets. These tests are compiled only, **NOT EXECUTED**; application/HTTP test doubles do not establish PostgreSQL atomicity. Genuine Google, real hosting/proxy/allowlist lifecycle and M8's B8.2/B8.3/B8.4/A18/A20 requirements remain pending.

**Local validation (2026-10-13):** `dotnet restore`, `dotnet build` (**0 warnings/errors**), `dotnet test tests/MediPOS.UnitTests` (**2246 passed, 0 failed/skipped**, **38 new cases**), `dotnet test tests/MediPOS.ArchitectureTests` (**8 passed, 0 failed/skipped**) and `dotnet format --verify-no-changes` passed. Offline EF `has-pending-model-changes` reports **none**. The 81-line incremental SQL script was generated/inspected: only the new receipt table/index/FK/constraints, operator RLS, guard functions/triggers and migration history entry; no extension changes or alterations to existing business/audit tables. Unit coverage includes canonical v1 hash/UTC/Unicode, application rollback/revocation and simulated concurrency, real synthetic HTTPS/OIDC/session/CSRF HTTP creation/replay/conflict/privacy/quota, unchanged three license routes, default-off exact surface and offline schema/model checks. Recording persistence establishes orchestration/HTTP only. **Eight new PostgreSQL cases compiled, NOT EXECUTED**, alongside the prior external suite. No local database/container or Git/GitHub operations occurred. B8.6.3/B8.6.3.1 remain IN_PROGRESS pending independent audit/external evidence.

### B8.6.3.2 — Initial tenant status hardening
**Requirements:** FR-LIC-001/002/004, BR-017/020; tighten only administrative creation admission.

**Status:** IN_PROGRESS — implementation integrated; stop for audit. No production-readiness declaration or B8.6.4.

The shared Application TenantCreationStatusPolicy admits only trial/active/grace/suspended in POST /platform/tenants and CreateTenantIdempotentlyHandler. Suspended represents administrative provisioning with operational access denied, leaving reactivation as a separate authorized lifecycle operation. Cancelled/PurgePending/Purged belong to terminal/deletion lifecycle processes, never initial creation. The API and orchestrator reject them with sanitized 400 `tenant.initial_status_invalid` before any idempotency reservation/lookup or tenant/license/history/audit write. An invalid attempt leaves its UUID available for a valid payload; even an already committed key cannot bypass admission. Undefined enum values are also denied by the orchestrator; unknown HTTP strings retain the existing malformed-input code.

License.Create, original internal CreateTenantHandler, renewal/suspension/reactivation/purge request, domain lifecycle tests, migration/schema/snapshot, RLS and canonical v1 hashing remain unchanged. Historical data/receipts are not rewritten; prohibited creation payloads are now rejected without receipt lookup, while valid original payloads retain replay and conflict behavior regardless of later license transitions. No physical purge, first Owner HTTP or new route is added.

New unit/HTTP cases exercise each admitted/rejected state, zero reservation/store calls and empty business/history/audit effects on rejection, same-key valid recovery, committed-key invalid rejection, original valid replay and changed-payload conflict. Seven additional real PostgreSQL cases are prepared for the state matrix, no database mutations before rejection, valid reuse, persisted status/history and suspended operational denial; they are NOT EXECUTED locally. Existing B8.6.3.1 PostgreSQL 18, real Google OIDC/reverse proxy/hosting, B8.2/B8.3/B8.4 external gates, A18/A20 and first Owner HTTP remain pending. M8 is not DONE.

**Historical local gate blocker at B8.6.3.2 handoff (2026-10-14), remediated by B8.6.3.3 below:** standard restore/build/unit/format commands failed on existing transitive System.Security.Cryptography.Pkcs 8.0.1 NuGet audit errors NU1902/NU1903; format reported restore failure. Architecture tests passed (8/8). No dependency, project-wide warning policy or vulnerability-audit setting was changed in B8.6.3.2. Its supplemental diagnostics retained these warnings with a command-only exception and did not approve the standard gate.

**Executed supplemental validation (2026-10-14), not standard-gate approval:** `dotnet restore -p:WarningsNotAsErrors=NU1902%3BNU1903` and `dotnet build --no-restore -p:WarningsNotAsErrors=NU1902%3BNU1903` complete with **0 build errors and 12 visible NuGet warnings**. Auditing remains enabled; only those two warning codes were demoted for these diagnostic commands. On the resulting current assemblies, `dotnet test tests/MediPOS.UnitTests --no-build --no-restore` passes **2264/2264, 0 failed/skipped**, including **18 new cases** and unchanged lifecycle/license endpoint regressions. Standard architecture command passes **8/8, 0 failed/skipped**. `dotnet format --verify-no-changes --no-restore` passes; existing offline EF `has-pending-model-changes --no-build` reports **none**, with audit warnings still visible. Seven new PostgreSQL cases compile; neither these nor B8.6.3.1's eight cases were executed locally. No migration/domain factory/lifecycle contract, dependency, persistent audit setting, Git/GitHub operation or local database/container is introduced. Stop for audit with the standard dependency gate still blocked.

### B8.6.3.3 — NuGet vulnerability remediation
**Requirements:** restore the standard gate and preserve FR-RPT-001..005 PDF/export contracts; no business, schema, authorization or HTTP changes.

**Status at B8.6.3.3 handoff:** IN_PROGRESS — minimal patch and standard local gate validated; stop for independent audit. At that handoff B8.6.4 was not started; M8 is not DONE.

**Confirmed dependency graph and advisories:** before remediation, PDFsharp 6.2.4 was the sole NuGet package introducing System.Security.Cryptography.Pkcs with minimum >= 8.0.1, resolved to vulnerable 8.0.1. Infrastructure owns PDFsharp; Api, Performance, Recovery, UnitTests and IntegrationTests receive it through their existing project references. Multiple project-reference paths converge on Infrastructure/PDFsharp; no second introducing package was found in any restored net10.0 target. Domain, Application, SharedKernel and ArchitectureTests do not resolve Pkcs. NU1902 identifies GHSA-2fmv-rv2c-59x7 (moderate); NU1903 identifies GHSA-846f-f764-76mx (high), observed in the original NuGet audit.

**Minimal remediation:** add only a direct PackageReference to **System.Security.Cryptography.Pkcs 8.0.2** in Infrastructure, propagating the patched dependency through all six affected graphs. Keep PDFsharp at 6.2.4. The official [PDFsharp dependency metadata](https://www.nuget.org/packages/PDFsharp/6.2.4) accepts this version; the Microsoft-owned [Pkcs 8.0.2 package](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/8.0.2) is stable, compatible with net10.0 through its net8.0 asset, and that asset adds no other package dependencies. Pkcs 10.0.13 is also available with a native net10.0 asset, but no major upgrade is necessary to fix these advisories; a same-branch patch limits compatibility changes to PDFsharp. No bulk PDFsharp/ASP.NET/EF/package upgrade, new project/reference direction, renderer code or artificial test was introduced. TreatWarningsAsErrors=true, NuGetAudit=true, NuGetAuditMode=all and NuGetAuditLevel=low remain effective; WarningsNotAsErrors is empty and no security NoWarn/audit suppression is added.

**Runtime is a separate external gate:** Microsoft's [October 2026 servicing notice](https://devblogs.microsoft.com/dotnet/dotnet-and-dotnet-framework-october-2026-servicing-updates/) and [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) identify **10.0.13, released October 13, 2026**, as a security update including .NET and ASP.NET Core runtimes. Local inspection reports SDK 10.0.401 and installed Microsoft.NETCore.App/Microsoft.AspNetCore.App 10.0.12; this corrective does not install/patch runtimes. Hosting version is not inspected: require inventory, upgrade older .NET/ASP.NET Core 10 installations to 10.0.13 or a later supported security patch and verify the process actually uses it. Framework-dependent hosting must patch its runtime; self-contained delivery must republish with the patched runtime. The Pkcs package patch does not update either shared runtime. Existing Microsoft.AspNetCore/EF Core package references remain 10.0.12: these are distinct NuGet libraries, not proof of installed runtime version, and the current full NuGet audit reports no additional package vulnerabilities requiring a bulk update.

Built-output inspection confirms the distinction: Infrastructure's library deps and Recovery's executable deps select Pkcs 8.0.2, with the patched DLL copied for Recovery. Api, UnitTests, IntegrationTests and Performance do not carry an app-local Pkcs entry/DLL: their ASP.NET shared framework supplies it. The local shared-framework deps include Pkcs under Microsoft.AspNetCore.App.Runtime.win-x64/10.0.12. Thus all six restored NuGet targets are fixed without claiming that the API's shared-runtime Pkcs is patched locally or in hosting. PDFsharp's unchanged metadata minimum remains >= 8.0.1; the selected package key in every affected assets graph is 8.0.2. Runtime patch acceptance and regression on the patched hosting remain external.

**Executed standard local gate (2026-10-14):** exactly `dotnet restore`, `dotnet build`, `dotnet test tests/MediPOS.UnitTests`, `dotnet test tests/MediPOS.ArchitectureTests` and `dotnet format --verify-no-changes` all pass, without security-warning downgrade/exclusion options. Build: **0 warnings, 0 errors**. Unit: **2264 passed, 0 failed/skipped**. Architecture: **8 passed, 0 failed/skipped**. Existing offline EF `has-pending-model-changes --no-build` reports **none**. `dotnet package list --vulnerable --include-transitive --no-restore --format json` reports no vulnerable top-level/transitive packages for all ten projects; all six affected assets graphs select **Pkcs 8.0.2**, no resolved 8.0.1 and no NU1902/NU1903 or new relevant warnings. Effective MSBuild settings confirm auditing enabled/all/low and warnings-as-errors, with no security exception. No artificial test or source test change was needed.

The full unit run includes ReportExportRenderingTests (real PDFsharp PDF generation, embedded font/Unicode, pagination, totals and reading via PdfPig and PDFsharp PdfReader), ReportExportAuthorizationTests and XLSX/PDF regressions. It also includes B8.6.3.1 CreateTenantIdempotencyTests, B8.6.3.2 PlatformTenantCreationHttpTests, PlatformLicenseHttpTests, LicenseTests and existing application lifecycle tests for renewal/suspension/reactivation. Api/Application/Domain/Infrastructure and all remaining solution projects build. This is local functional/package evidence on runtime 10.0.12, not real PostgreSQL, hosting/runtime patch or Google acceptance.

At B8.6.3.3 handoff, all real PostgreSQL 18 evidence (including B8.6.3.1/B8.6.3.2), Google OIDC, TLS/reverse proxy/hosting, B8.2/B8.3/B8.4 external gates, A18/A20 and first Owner HTTP remained pending; B8.6.4 below now implements only the latter, pending audit/external validation. No runtime/hosting patch, real provider test or database acceptance is claimed.

### B8.6.4 — Secure HTTP first Owner provisioning
**Requirements:** FR-AUT-001..005, FR-LIC-001..005; BR-001/011/016/017/020 and historical first Owner bootstrap restriction.

**Status:** IN_PROGRESS — implementation integrated; stop for independent audit. No production-readiness declaration or later sprint.

Only `POST /platform/tenants/{tenantId}/bootstrap-owner` is added. Exact bounded JSON contains only `userId`, a nonempty 36-character D UUID; route supplies tenant, server fixes Owner and the native session supplies the independently approved Superadmin actor. Reuses Production/Google/platform opt-ins (default off), HTTPS/Host/Origin/trusted-proxy boundary, native CSRF and shared administrative quota. Success is 201 with only membershipId/tenantId/userId/role/isActive/createdAt, without Location or replay claims. Central sanitized Problem Details and prior authorization prevent private existence disclosure to unauthorized callers.

The recipient first registers through existing Google login and obtains their own MediPOS UUID via `GET /auth/me`. The Superadmin verifies recipient and target botica through an authorized operational channel; UUID knowledge and email are not authority. No public identity search, invitations or new authentication flow is introduced. The operator gains no commercial membership. README documents the exact procedure and error contract.

The existing CreateMembershipHandler, identity store and license provisioning transaction remain the sole implementation. Its explicit bootstrap entry cannot fall back to ordinary Owner administration after platform revocation. Checks repeat after the license row lock and before commit; expiration is checked again after audited save. Concurrent license suspension serializes on that row; a suspension committed first prevents bootstrap, while an earlier committed bootstrap precedes a later suspension. Suspended licenses must be reactivated through the existing flow first. Membership plus authenticated audit are atomic. Any historical Owner, including inactive, permanently closes bootstrap. Ordinary employees, second Owner, branches and schedules remain outside this HTTP scope.

Concurrent operators are coordinated by PostgreSQL, not the in-memory admission limiter. After a committed first creation, every retry receives 403 `access.bootstrap_denied`, including the same target; it is not a successful replay. After ambiguous response loss, authorized support must reconcile existing membership/history/audit read-only against the verified tenant/recipient/operator and incident evidence. A denial alone does not identify the winning target or prove the caller's prior success. Never delete/deactivate history to reopen bootstrap. No parallel receipt ledger, support endpoint, schema or migration is added; Pkcs 8.0.2 and security audit policies remain unchanged.

**Local validation (2026-10-14):** `dotnet restore`, `dotnet build` (**0 warnings/errors**), `dotnet test tests/MediPOS.UnitTests` (**2314 passed, 0 failed/skipped; 50 new cases**), `dotnet test tests/MediPOS.ArchitectureTests` (**8 passed, 0 failed/skipped**) and `dotnet format --verify-no-changes` passed without security-warning exceptions. Offline `dotnet ef migrations has-pending-model-changes --project src/MediPOS.Infrastructure --startup-project src/MediPOS.Infrastructure --no-build` reports **none**. The complete solution build compiles the integration suite without executing it. The initial targeted HTTP/application run passed 120 tests before adding the final missing-route case; the full run includes all cases and existing regressions.

Eight PostgreSQL cases are prepared for two actual approved operators with observed backend lock wait, exactly one historical Owner/audit and fresh-scope lost-response denial, inactive history, rollback after real audited save/persistence failure/license expiry/session expiry, suspension or expiration during lock wait, missing global user, restricted role and RLS without EF filters. These are **NOT EXECUTED** locally. Existing PostgreSQL commercial/security regressions are retained. No Git/GitHub or local database/container operations occurred.

**Pending:** real PostgreSQL 18 including B8.6.3.1 and initial-status cases; genuine Google OIDC, patched hosting runtime, TLS/reverse proxy/ACL, external B8.2/B8.3/B8.4, A18 physical purge, A20 frontend, ordinary employee administration, commercial HTTP and subsequent work. M8 remains IN_PROGRESS.

### B8.6.5 — Reproducible external PostgreSQL 18 quality gate
**Requirements:** existing real PostgreSQL integration/tenant/security acceptance; no new business capability.

**Status:** IN_PROGRESS — workflow CREATED; the owner reported remote attempt 38619817614 (event push, completed/failure, zero jobs, no step logs), so test execution and PostgreSQL approval remain PENDING. B8.6.5.1 below corrects the invalid expression; successful external execution is still pending. The file continues to declare only workflow_dispatch. Stop for audit; owner controls versioning and manual execution.

One workflow, `.github/workflows/backend-validation.yml`, uses only `workflow_dispatch`, a GitHub-hosted Ubuntu 24.04 runner, read-only contents permission, checkout without persisted credentials, official actions pinned to verified release SHAs and SDK 10.0.402. One run at a time, no automatic push/PR/schedule trigger, 60-minute job limit, 35-minute integration limit. No production secrets, deployment, GitHub settings changes or new services. PostgreSqlFixture retains its disposable `postgres:18-alpine`, generated passwords, migration authority, restricted runtime/catalog roles, Ryuk and serialized collection. Record the pulled image digest because the existing 18-alpine tag can change patch versions. The smoke test additionally asserts the actual server's major version and emits only its safe numeric version.

All existing local code gates run in CI with NuGet Audit/all/low and warnings-as-errors retained. Resolved assets must select Pkcs 8.0.2, and the vulnerable dependency audit must be clean. The complete IntegrationTests project runs once through existing xUnit v3 **VSTest** support (`xunit.v3.mtp-off`, Visual Studio adapter/Test SDK); no filter, parallelism override, assertion relaxation or test exclusion is introduced. Summary prioritizes Tenancy/RLS, TenancyLicensing and IdentityAccess, then every remaining class, with separate B8.6.3.1 receipt/retry/lock/rollback/permission, B8.6.3.2 initial-state and B8.6.4 historical bootstrap/concurrency/expiry reports. Missing results, zero tests, failed/skipped/incomplete counts or missing critical sprint/version evidence fail closed. Test failures, container infrastructure failures and ambiguous fixture setup failures are distinguished without claiming an unverified root cause. Business/RLS/transaction defects require reviewed corrective work, not a softened gate.

Only sanitized counters, method/class names, gate outcomes, SHA and versions reach logs/job summary. Raw test output/TRX stays temporary, is never uploaded and is removed alongside remaining Testcontainers containers/anonymous volumes; no dump, cookie, token or connection string is an evidence artifact. Host teardown remains the final cleanup layer on forced termination. Recovery/performance regressions create extra marked disposable databases solely inside the fixture cluster; these are functional checks, not B8.2 benchmarks or B8.3 backup/restore rehearsals. No additional real external service/secret was found in the reviewed suites.

**Demonstrated fixture correction:** TenantCreationIdempotencyPersistenceTests resolved an unregistered concrete TestGoogleIdentitySource. AddIdentityAuthentication registers only IVerifiedGoogleIdentitySource; the helper now resolves/casts that registered port, matching existing identity fixtures. No production registration or assertion changes.

**Local validation (2026-10-14):** restore/build passed (**0 warnings/errors**); UnitTests **2314 passed**, ArchitectureTests **8 passed**, both **0 failed/skipped**; format verification passed. The two test commands additionally wrote temporary VSTest TRX for offline reporter validation. NuGet vulnerability audit reports no vulnerable packages in all ten projects; six affected assets graphs select Pkcs 8.0.2. PyYAML offline parsing checks manual-only trigger, read-only permissions, one job and pinned official actions; both embedded Python blocks compile. Eight synthetic reporter scenarios cover passing evidence, assertion/container/fixture failure, omission, missing version, zero tests and absent result; actual local TRX is also inspected. These checks are not GitHub execution or PostgreSQL evidence. No Git/gh, remote dispatch, local Docker/PostgreSQL or historical migration operation occurred.

**Owner execution:** after incorporating the workflow into the default branch, open GitHub Actions → Backend validation - PostgreSQL 18 → Run workflow, select the reviewed branch and run manually. See backend README for interpretation, privacy and runner limitations. CREATED does not mean EXECUTED, and EXECUTED does not mean PASSED; associate external acceptance only with a successful complete report for the specific tested SHA.

**Pending:** real PostgreSQL 18 execution of all prepared suites, actual Google OIDC, patched hosting runtime, TLS/reverse proxy/ACL, external B8.2/B8.3/B8.4, A18/A20 and later work. M8 remains IN_PROGRESS; no production-ready declaration.

### B8.6.5.1 — GitHub Actions context validation corrective
**Status:** IN_PROGRESS — minimal YAML correction implemented; stop for audit. External successful execution and PostgreSQL validation remain pending.

Removed only `DOTNET_INSTALL_DIR: ${{ runner.temp }}/medipos-dotnet` from jobs.backend.env. The [official context matrix](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#context-availability) excludes runner there; setup-dotnet uses its standard installation behavior, retaining SDK 10.0.402 and the exact-version guard. No replacement path or other workflow change. All nine remaining expressions were reviewed by location: github.sha in steps.with, steps.*.outcome with always/cancelled in steps.if, and toJSON(steps) in steps.env. Job env/defaults are static; shell RUNNER_TEMP is expanded inside run scripts. Both existing action SHA URLs resolve in the official repositories and remain unchanged. actionlint is unavailable and was not installed/run; targeted offline context checks are not GitHub workflow-engine approval.

The reported run's pre-job failure is consistent with this invalid context; no nonexistent step logs or PostgreSQL results are inferred. The unchanged reporter passes nine synthetic checks for missing/incomplete/failed/skipped evidence, runner/test classification, missing actual-server version, privacy and a failed test step even with passing TRX. Test exit codes remain unmasked, no continue-on-error exists, and always-run cleanup cannot clear prior step failure. The smoke marker originates from the connected Npgsql server version with an explicit major-18 assertion; image metadata alone is insufficient.

**Local validation (2026-10-14):** exact requested commands `dotnet restore`, `dotnet build` (**0 warnings/errors**), `dotnet test tests/MediPOS.UnitTests` (**2314 passed, 0 failed/skipped**), `dotnet test tests/MediPOS.ArchitectureTests` (**8 passed, 0 failed/skipped**) and `dotnet format --verify-no-changes` passed. Existing offline EF `has-pending-model-changes --no-build` reports **none**. Targeted context-location checks reject the original expression and accept the nine remaining expressions against the official matrix; embedded Python syntax and the unchanged reporter's nine synthetic scenarios pass. actionlint was not executed; successful GitHub validation and actual PostgreSQL 18 evidence remain pending. No Git/gh, remote dispatch/settings/secrets changes, local Docker/PostgreSQL, backend/migration changes or additional workflow. M8 and B8.6.5 remain IN_PROGRESS; Google OIDC, hosting runtime/TLS/proxy, B8.2/B8.3/B8.4 and A18/A20 remain pending.

### B8.7 — Commercial HTTP API continuation
**Status:** IN_PROGRESS — scoped commercial P0 surface and continuation blocks A–D implemented; integral audit and external gates remain pending. This continues the partially saved 56-route implementation; no reset/reimplementation or version-control operation.

Recovered the existing TenantApi boundary, DTOs, readers, migration and HTTP tests. The first full local gate found six failures from ListMyTenantsHandler being registered without a server session adapter; registration now belongs to AddIdentityAuthentication. That recovery gate then passed 2384 unit/HTTP and 8 architecture tests, format and offline EF. No assertion or policy was relaxed.

The current surface has **86 commercial method/path endpoints** (30 added), plus the separately authorized Superadmin category POST. Existing handlers serve legal entities/branches, catalog/units/prices/search/XLSX, suppliers/purchases/stock/adjustment, cash/drafts/mixed confirmation/ticket/void/close, ordinary Owner employee administration and Owner reports/export. Continuation adds inventory transfer lifecycle and a bounded branch inbox, cash-change dispatch/pending/read/receipt, commission settings/rules/report, device registration/revocation, versioned batch reception, conflict review/resolution and branch catalog snapshot/delta. It also exposes bounded valued-expiration lots, the existing minimum-stock configuration, notification inbox/unread/read state and Owner audit metadata. No financial rule, reversal formula, table history or transaction boundary was rewritten. New queries are limited to operational discovery/read gaps; GET missing commission settings creates no data.

TenantApi remains default-off, independent of PlatformApi, behind real Google cookies and HTTPS/Host/trusted-peer/proxy controls. Native same-origin GET may omit browser-controlled Origin only with same-origin/none Fetch Metadata; mutations still require exact Origin and CSRF. No global-admin commercial grant. Every operation uses current server identity and existing role/license/branch/schedule policy. Request/body/paging/admission limits are bounded; sync gets a separate 30/min budget and shares the two-operation heavy concurrency ceiling. Native POS writes retain 240/min. Transfer allocation JSON allows 256 KiB for the existing 1000-selection domain bound; default stays 64 KiB and sync stays 4 MiB/50 events. JSON numbers cannot silently round/underflow/overflow into another decimal value. OpenAPI development schemas document UUID/decimal/enums/query/security/error/binary contracts and the offline payload; schema generation depth is separate from strict request depth.

AddMembershipSelfDiscovery is unchanged in meaning: parameterized authenticated UUID is transaction-local, active own membership SELECT only with empty tenant scope, no new write/private-ledger grant or RLS bypass. Its incremental SQL was inspected offline and has only CREATE POLICY/history. No historical migration/extensions were edited; model has no pending changes. CommercialDirectoryPostgreSqlTests prepares real restricted-role discovery, pool reset, rejected private reads/writes, bounded directory scopes, read-only commission settings and transfer inbox; existing sale/transfer/sync/commission/RLS concurrency/rollback suites remain intact. Database assertions are NOT EXECUTED locally.

The backend README contains the HTTP/P0/handler/DTO/role/test matrix and operating protocol. Valued expiring lots reuse the original horizon and valuation with SQL paging and current-role cost visibility. Threshold changes reuse the existing audited Owner handler; notification routes do not activate the opt-in scanner. Audit reads expose only metadata and snapshot-presence flags in a bounded UTC window, never internal JSON. PostgreSQL cases for these reads are prepared, not executed. No additional scoped commercial P0 route gap was identified; integral audit and external evidence remain necessary. Remaining global/UBL/CPE/main-hub administration is outside this continuation; A18 stays unpublished and deferred.

**Closing local gate (2026-10-14):** exact AGENTS commands executed: restore passed; build passed with 0 warnings/errors, including IntegrationTests compilation; UnitTests **2432 passed, 0 failed, 0 skipped** (4 min 46 s); ArchitectureTests **8 passed, 0 failed, 0 skipped**; format verification passed after two test-only whitespace/import fixes. Offline EF reports no pending model changes; the incremental SQL contains only the SELECT policy and migration history. HTTP tests include the native synthetic-cookie commercial flow, all 86 route authentication checks and Development OpenAPI schema/reference generation. Recording ports do not prove real database rollback, locks, RLS or pool cleanup. PostgreSQL assertions are prepared/compiled, NOT EXECUTED here; no TypeScript client generation or real Google/browser acceptance is claimed.

External PostgreSQL 18, B8.6.3.1, Google/browser/hosting/TLS/proxy, B8.2/B8.3/B8.4, A18 physical purge and A20/frontend/PWA remain pending. SUNAT/SIRE stays deferred. No external workflow/secret/repository modification, local Docker/PostgreSQL or subsequent sprint. Stop for integral audit; B8.7 and M8 remain IN_PROGRESS.

**M8 gate:** backend is ready for controlled real-business pilot only after all mandatory implementation and external acceptance gates pass.

**F2.0 local integration (2026-10-15): infrastructure and functional local Google authentication verified; commercial authorization/onboarding acceptance BLOCKED.** Authorization remains restricted to local PostgreSQL 18 / medipos_dev, no Docker/Testcontainers. PostgreSQL 18.4 now has 34/34 official migrations. The original EF-generated refresh history IF reproduced SQLSTATE 42601: an unparenthesized CASE exposed its THEN to the PL/pgSQL IF parser. Parenthesized that sole operand in OfficialCatalogRefreshSql.Up, used only by the then-unapplied refresh migration; no applied migration or existing history entry changed. Six actual read-only PostgreSQL regression comparisons passed; all 49 preexisting application-table row counts/checksums and the 28 original migration IDs were preserved after official EF update. Explicit runtime grants reapplied; medipos_runtime remains private/restricted without ownership, memberships, bypass or DDL, 53 FORCE RLS tables and active RLS on sales/memberships/idempotency receipts. HTTPS SPA/API, real readiness and client/error/cancellation/decimal checks are evaluated independently of authentication. Unit/component test doubles do not establish Google/PostgreSQL integration. Local Google/PFX configuration is now complete: private RSA PFX and key-ring ACLs, encrypted key persistence across restart, actual CSRF, Google redirect/callback and anonymous/Origin rejection verified. The first authenticated directory read exposed SQLSTATE 42703 (JOIN to tenants.id instead of tenants.tenant_id); that JOIN alone is corrected and the read-only query passes under the restricted runtime without RLS, permission or migration changes. The operator repeated real Google login in Chrome after restart and confirmed the empty assigned-botica view; session/directory returned 200 without new database errors. Build, 95 focused unit tests and format passed. The additional empty-directory PostgreSQL regression is prepared/compiled, not run through Testcontainers. The operator additionally verified React /auth/me JSON, Secure/HttpOnly/SameSite=Lax session-cookie attributes and logout followed by 401 auth.session_required. Functional local Google authentication (login/callback/session/cookies/logout) is PASS. Five live anonymous HTTPS probes confirm the intentional foreign-Origin/cross-site 403 boundary and none/same-origin 401 without session; a new tab itself is not denied, and original browser request headers were not captured. Security rules are unchanged. Natural 30-minute expiry, first authorized botica/Owner onboarding, tenant/license/role/branch/schedule authorization, commercial reads and actual two-tenant isolation remain pending; the existing README retains the Production-only bootstrap procedure. No invented records, manual schema workaround, new migration/model, production approval or F2.1.

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

