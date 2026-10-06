# MediPOS — Architecture Decisions

Accepted baseline decisions. Keep this file compact. Add a new ADR only for a durable decision that changes architecture, security, persistence, module boundaries or external-integration strategy.

| ID | Decision | Status | Rationale |
|---|---|---|---|
| ADR-001 | Modular monolith | Accepted | Lowest operational complexity/cost for initial market; clear internal boundaries. |
| ADR-002 | PostgreSQL as single operational source of truth | Accepted | Avoids distributed consistency for stock/cash/sales. |
| ADR-003 | Static PWA frontend | Accepted | Responsive/offline capability without native apps or permanent SSR server. |
| ADR-004 | ASP.NET Core .NET 10 LTS | Accepted | Performance, LTS support, Visual Studio fit. |
| ADR-005 | PostgreSQL 18 | Accepted | Relational transactions, RLS and search extensions. |
| ADR-006 | `pg_trgm` + `unaccent` before Elasticsearch | Accepted | Adequate initial search with lower RAM/ops cost. |
| ADR-007 | Google OIDC initial authentication | Accepted with review | Avoid password management; owner-account recovery path remains operational concern. |
| ADR-008 | FEFO outbound proposal | Accepted | Reduce expiration loss while preserving traceability. |
| ADR-009 | 30-day expiration alert default | MVP accepted | Simple initial rule; validate with field work. |
| ADR-010 | Equivalent products only by structured composition | Accepted | Inventory/dispensing aid without therapeutic recommendation. |
| ADR-011 | Electronic documents behind adapter | Accepted | PSE/OSE/direct provider can change without coupling core POS. |
| ADR-012 | Offline as contingency | Accepted | Multi-device consistency requires connectivity; conflicts must reconcile. |
| ADR-013 | Stock ledger via movements | Accepted | Auditability and integrity vs direct stock edits. |
| ADR-014 | No permanent free plan initially | Commercial hypothesis | Prioritize paid validation; keep temporary trial. |
| ADR-015 | Standard product limit: 5 branches | Initial accepted | Protect cost/support; larger clients require separate capacity review. |

## Adding a decision
Append only when approved:

```text
ADR-XXX — short title
Status: Proposed | Accepted | Superseded
Context: why a durable decision is required
Decision: chosen option
Consequences: important trade-offs only
```

Do not create ADRs for routine implementation details or reversible local choices.
