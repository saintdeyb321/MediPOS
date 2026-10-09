# MediPOS — Backend Execution Plan

This is the single execution roadmap for the backend. Prompts are given in chat and are **not** stored in the repository.

## Working state
- **Current milestone:** M4 — Commissions, reporting & alerts
- **Current sprint:** B4.1 — Commissions
- **Status:** IN_PROGRESS
- **Next gate:** B4.1 local gate and implementation review; PostgreSQL integration validation pending externally

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
**Status:** IN_PROGRESS.
**Requirements:** FR-COM-001..004, BR-010.
**Scope:** CommissionRule/Entry, fixed/percentage rules, sale posting, compensating reversal, reports.

### B4.2 — Operational dashboards/reports
**Requirements:** FR-RPT-001..004.
**Scope:** period/branch/employee/product/category reporting; critical stock, expiration capital, rotation.

### B4.3 — Exports
**Requirements:** FR-RPT-005.
**Scope:** Excel/PDF export of selected reports with applied filters.

### B4.4 — Notifications & expiration alerts
**Requirements:** FR-INV-003, FR-RPT-007; later FR-RPT-008.
**Scope:** persistent Notification center, 30-day job, read state; Web Push only after internal center is stable.

### B4.5 — Replenishment suggestion
**Requirements:** FR-INV-008, FR-RPT-006 P1.
**Scope:** explainable historical-consumption suggestion; never auto-purchase.

**M4 gate:** owner can understand sales, stock risk, commissions and alerts without SQL.

---

# M5 — Offline synchronization backend
Goal: safe limited contingency sync.

### B5.1 — Devices, event contract & idempotency
**Requirements:** FR-OFF-002, BR-018.
**Scope:** SyncDevice/SyncEvent, event versioning, idempotency storage.

### B5.2 — Batch sync endpoint
**Requirements:** FR-OFF-003.
**Scope:** Accepted/Conflict/Rejected responses, transactional application, cursor/version response.

### B5.3 — Offline sale reconciliation
**Requirements:** FR-OFF-004..005, BR-019.
**Scope:** safe offline-sale subset, explicit stock conflicts/reconciliation queue; no silent overwrite.

### B5.4 — Incremental branch catalog sync
**Requirements:** FR-OFF-001.
**Scope:** version/cursor-based downloads rather than full dataset each reconnect.

**M5 gate:** duplicate event cannot duplicate sale; conflicts are visible and recoverable.

---

# M6 — Official pharmaceutical catalog pipeline
Goal: controlled ingestion without fragile runtime dependency.

### B6.1 — Staging/import pipeline
**Scope:** import official/versioned source into staging; preserve original text.

### B6.2 — Normalize, deduplicate, curate, publish
**Requirements:** FR-CAT-008.
**Scope:** normalization and superadmin review; only validated records become global.

### B6.3 — Versioned refresh/history
**Scope:** upsert by official identifier/registration and track relevant changes.

**M6 gate:** tenant operations never depend on live scraping; global catalog publication is curated.

---

# M7 — Tax & purchase-document integrations
Goal: optional integrations without coupling the core.

### B7.1 — Supplier UBL XML import
**Requirements:** FR-PUR-005.
**Scope:** parse/map into reviewable pre-purchase; no automatic stock increase before human confirmation.

### B7.2 — Electronic document provider abstraction
**Requirements:** FR-CPE-001..005.
**Scope:** ElectronicDocument state model, provider adapter, persistent queue/retry, series config, ticket/CPE separation.

### B7.3 — Provider/sandbox pilot
**Scope:** integrate selected PSE/OSE/direct sandbox only after credentials/contract are known; resilient retries/observability.

### B7.4 — SIRE feasibility spike
**Requirements:** FR-PUR-006 P2.
**Scope:** technical validation with real allowed credentials/data; implement only if useful inventory detail is confirmed.

**M7 gate:** tax failures never corrupt commercial sale/stock state; pending work survives restart.

---

# M8 — Backend hardening for real pilot
Goal: production-quality MVP backend.

### B8.1 — Security review
Tenant isolation, authz matrix, file validation, rate limits, CSRF/CORS/session review, secret/log audit.

### B8.2 — Performance/load targets
Validate catalog search p95 target (<500 ms target from baseline) and online sale confirmation target (<1 s excluding external integrations) on representative data/hardware; optimize measured bottlenecks only.

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
