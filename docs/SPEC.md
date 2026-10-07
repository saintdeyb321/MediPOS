# MediPOS — Product & Backend Specification

Compact source of truth for product scope, backend requirements and domain rules. Preserve IDs in code/tests/commits when useful.

## 1. Product scope
MediPOS is a multi-tenant SaaS for independent pharmacies/boticas in Peru, initially targeting 1–5 branches per tenant. It prioritizes fast selling, trustworthy stock, batches/expiration, cash control, transfers, commissions, actionable reporting and limited offline contingency.

MVP principle: a feature should clearly help **sell faster**, **avoid money loss**, or **give the owner control**.

Not MVP: full accounting/payroll/advanced AR/AP/CRM, loyalty, patient clinical history, marketplace between unrelated businesses, large chains, diagnosis/symptom recommendations, automatic banking validation for Yape/Plin, generative AI in critical operations.

## 2. Actors
- **Superadmin**: tenant/license lifecycle, audited support, technical platform visibility, controlled purge.
- **Owner**: full tenant access across authorized branches, operations, configuration and reports.
- **Pharmacist (QF)**: operational selling/inventory/lot/transfer actions in assigned branches.
- **Cashier**: selling, product search, own cash session, internal ticket and allowed transfer actions; no managerial costs/reports.
- **System**: FEFO, alerts, commissions, sync, reports, queues and audit automation.

## 3. Core domain rules
- **BR-001** Tenant A never reads/modifies Tenant B private data.
- **BR-002** Stock is changed only in the same transaction that creates `StockMovement`.
- **BR-003** Server rejects online operations that would make stock negative; offline conflicts go to reconciliation.
- **BR-004** Medication outbound allocation proposes FEFO.
- **BR-005** Standard MVP expiration alert: 30 days.
- **BR-006** Mixed-payment sum must equal sale total exactly.
- **BR-007** Voided sale is not deleted; compensate/reverse and audit. Cash refunds must not exceed current expected cash under the original CashSession lock, including cash-transfer inflows/outflows; retry after sufficient cash arrives.
- **BR-008** Transfer flow: requested → approved → dispatched → received/cancelled.
- **BR-009** Batch/expiration traceability is preserved across transfers.
- **BR-010** Commission belongs to seller/line and is compensated on void.
- **BR-011** Operational access requires valid license, active membership, role, assigned branch and allowed schedule.
- **BR-012** Retail/wholesale prices are tenant-private, never global catalog data.
- **BR-013** Non-pharmaceutical retail products do not require medicine composition fields.
- **BR-014** Internal ticket and electronic tax document are separate concepts.
- **BR-015** Owner may see consolidated branches; cashier only operationally necessary data.
- **BR-016** MediPOS does not store Google credentials; use OIDC/session mechanisms.
- **BR-017** License enforcement is server-side; hiding UI is insufficient.
- **BR-018** Offline events carry idempotent UUID and local/server time context.
- **BR-019** Multi-device prolonged offline cannot guarantee global consistency; surface this state and reconcile.
- **BR-020** Full tenant purge requires controlled process, verification and appropriate confirmation/export policy.

Pharmaceutical equivalence is inventory/dispensing assistance only: same normalized active ingredient set + strength + dosage form (+ route when available). Never label it as medical recommendation.

## 4. Functional requirements
Priority: P0 MVP mandatory, P1 immediately after/optional integration, P2 later.

### Tenancy & licensing
- **FR-LIC-001 P0** Create tenant with configuration and associated license.
- **FR-LIC-002 P0** Configure start, expiration, state and branch limit; backend enforces license.
- **FR-LIC-003 P0** Renew/extend license preserving audited history.
- **FR-LIC-004 P0** Suspend/reactivate license and block disallowed operations.
- **FR-LIC-005 P0** Controlled tenant purge without private-data orphans.
- **FR-LIC-006 P1** Superadmin dashboard for tenants/license expiration/state.

### Legal entities & branches
- **FR-TEN-001 P0** One or more legal entities per tenant; branch linked to one.
- **FR-TEN-002 P0** Up to licensed branch limit; standard product max 5.
- **FR-TEN-003 P0** Independent inventory/cash by branch.
- **FR-TEN-004 P0** Owner consolidated/all-branch reporting.
- **FR-TEN-005 P1** Optional main/hub branch marker.

### Identity & access
- **FR-AUT-001 P0** Google OIDC; no Google password storage.
- **FR-AUT-002 P0** Assign role and branches; API rejects unauthorized branch access.
- **FR-AUT-003 P0** Restrict protected operations by employee schedule.
- **FR-AUT-004 P0** Maximum two owners per tenant.
- **FR-AUT-005 P0** Deactivate employee without erasing historical references.

### Catalog
- **FR-CAT-001 P0** Global reference catalog + tenant-specific business products; price/cost/stock never global.
- **FR-CAT-002 P0** Distinguish medicine vs retail product.
- **FR-CAT-003 P0** Name/internal code/category/brand-lab/optional barcode/state.
- **FR-CAT-004 P0** Medicine composition, normalized strength, dosage form and sanitary registration when available.
- **FR-CAT-005 P0** Retail and wholesale price.
- **FR-CAT-006 P0** Product units/packages with exact conversion to base unit.
- **FR-CAT-007 P0** Tenant-local product when absent from global catalog.
- **FR-CAT-008 P1** Curated candidates for global catalog.
- **FR-CAT-009 P0** Excel import with per-row errors and no invalid-row import.

### Inventory, purchasing, batches
- **FR-INV-001 P0** Batch/expiration/cost/quantity/supplier traceability on pharmaceutical intake.
- **FR-INV-002 P0** FEFO outbound selection.
- **FR-INV-003 P0** 30-day expiration alerts filterable by branch.
- **FR-INV-004 P0** Server prevents negative stock.
- **FR-INV-005 P0** Every stock change creates `StockMovement`.
- **FR-INV-006 P0** Audited manual adjustment with reason and before/after.
- **FR-INV-007 P0** Cost and potential-sale value for expiring stock.
- **FR-INV-008 P1** Simple historical-consumption replenishment suggestion; no automatic purchase.
- **FR-PUR-001 P0** Basic supplier.
- **FR-PUR-002 P0** Multi-line purchase; confirm creates batches/movements atomically.
- **FR-PUR-003 P0** Catalog-assisted product entry during purchase.
- **FR-PUR-004 P0** Purchase document/reference searchable.
- **FR-PUR-005 P1** Import supplier UBL XML into reviewable pre-purchase.
- **FR-PUR-006 P2** Evaluate SIRE/SUNAT purchase prefill only after real technical validation.

### POS & payments
- **FR-POS-001 P0** POS usable keyboard/mouse/touch/mobile with shallow main flow.
- **FR-POS-002 P0** Search name/active ingredient/brand-lab/internal code/barcode.
- **FR-POS-003 P0** Accent/typo tolerant search.
- **FR-POS-004 P0** Sell base unit/blister/package/other configured presentation with exact base deduction.
- **FR-POS-005 P0** Cash/Yape/Plin/card/transfer methods.
- **FR-POS-006 P0** Mixed payments.
- **FR-POS-007 P0** Yape/Plin operation number not mandatory in MVP.
- **FR-POS-008 P0** Internal printable ticket or no-print sale.
- **FR-POS-009 P0** Sale linked to seller and cash session.
- **FR-POS-010 P0** Permissioned void/cancel with stock/payment reversal and audit.

### Cash
- **FR-CASH-001 P0** Open cash with initial amount; required session before sale when policy requires.
- **FR-CASH-002 P0** Close with count vs expected and difference/time.
- **FR-CASH-003 P0** Totals by payment method.
- **FR-CASH-004 P0** Linked/audited cash-change loan between branches/cash desks.
- **FR-CASH-005 P0** Owner sees active cash sessions.

### Transfers
- **FR-TRF-001..006 P0** Request, origin approval, batch-preserving dispatch, destination receipt, full state history, server stock validation at dispatch.

### Commissions
- **FR-COM-001..004 P0** Tenant switch, fixed/percentage product rules, seller attribution with compensation on void, owner report by employee/period.

### Reporting & notifications
- **FR-RPT-001..005 P0** Period dashboard; sales dimensions; critical/expiring stock and capital; top/low rotation; Excel/PDF export respecting filters.
- **FR-RPT-006..008 P1** Replenishment/risk analytics, internal notification center, Web Push.

### Audit
- **FR-AUD-001..003 P0** Append-only normal-user audit for sensitive actions; actor/UTC/tenant/action/entity/before-after; no tenant UI deletion.

### Offline/sync
- **FR-OFF-001..005 P0** Cached PWA shell/branch data, UUID outbox sale, automatic sync, safe-operation restrictions, explicit stock-conflict reconciliation.

### Electronic documents
- **FR-CPE-001..005 P1** Provider adapter, boleta/factura state, persistent retry queue, series configuration, strict separation of internal ticket vs CPE.

## 5. Search rules
PostgreSQL-native initial search: `unaccent` + `pg_trgm` + GIN/indexes and normalized fields. Ranking preference: exact > prefix > similarity > active ingredient > brand/lab. If requested brand has no stock, show same structured equivalence key with branch stock; then other branches of same tenant if useful. No Elasticsearch in initial architecture.

## 6. Data invariants
Private operational entities include `tenant_id`. Important entities include: Tenant, License, LegalEntity, Branch, User, Membership, WorkSchedule, GlobalProduct, MedicineProfile, Category, BusinessProduct, ProductUnit, InventoryLot, StockMovement, Supplier, Purchase/PurchaseLine, CashSession, Sale/SaleLine/SalePayment, CommissionRule/Entry, Transfer/Line/Event, CashTransfer, InternalTicket, ElectronicDocument, Notification, AuditLog, SyncDevice/Event, ImportJob.

Use strict FKs. Cascade only for truly dependent lifecycle. Sales/payments/movements are reversed, not normally deleted.

## 7. MVP acceptance anchors
- **A1–A2** Superadmin can create tenant/license/owner; owner can create branches/employees/schedules without DB intervention.
- **A3–A7** Catalog/purchase/batches work; typo search works; equivalence appears; fractional units stay exact; FEFO is correct.
- **A8–A11** Mixed payment, cash close, transfer traceability and commission/reversal are correct.
- **A12–A13** Owner reporting works by branch/consolidated and exports.
- **A14** Offline sale sync is idempotent.
- **A15** Tenant isolation proven.
- **A16–A18** Adjustment audit, license suspension and controlled purge are proven.
- **A19–A20** Internal thermal ticket is distinct from CPE; app supports desktop/mobile via PWA.
