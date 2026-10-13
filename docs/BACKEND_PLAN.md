# MediPOS — Backend Execution Plan

This is the single execution roadmap for the backend. Prompts are given in chat and are **not** stored in the repository.

## Working state
- **Current milestone:** M8 — Backend hardening for real pilot
- **Current sprint:** B8.6.1.1 — Authentication rate limiting correction
- **Status:** IN_PROGRESS — B8.6.1.1 IMPLEMENTACIÓN LOCAL COMPLETA; audit and live Google/PostgreSQL/hosting validation pending. Opt-in authentication remains disabled by default; platform authority and commercial routes remain disabled. A18 controlled physical purge is NOT_IMPLEMENTED; A20 is a FRONTEND_DEPENDENCY. B8.2 targets remain NOT_MEASURED; B8.3 recovery and B8.4 deployment/monitor validation remain external. M8 is not DONE; B7.3/B7.4 remain voluntarily deferred.
- **Next gate:** Audit B8.6.1.1 and validate real Google consent/callback, restricted runtime persistence and protected keys/HTTPS/proxy externally, including proxy header sanitation and actual traffic/NAT limits. Independent platform administration, commercial HTTP, controlled purge, PWA and the remaining PostgreSQL/pilot gates require separately authorized work. No subsequent sprint is started.

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

**M8 gate:** backend is ready for controlled real-business pilot only after all mandatory implementation and external acceptance gates pass.

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
