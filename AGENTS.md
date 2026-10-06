# MediPOS — AGENTS.md

## Scope
MediPOS is a vertical SaaS for independent pharmacies/boticas in Peru, initially 1–5 branches per tenant. Current engineering focus: `backend/`.

This file is the operating contract for coding agents. Detailed product and technical truth lives in `docs/`.

## Context loading — keep it lean
Do not read every document on every task.

For backend work:
1. Read this file.
2. Read the **current sprint** in `docs/BACKEND_PLAN.md`.
3. Read only the relevant requirement/rule IDs in `docs/SPEC.md`.
4. Read only the relevant sections of `docs/ARCHITECTURE.md`.
5. Consult `docs/ADR.md` only when the task touches an architectural decision.

Use search (`rg`, IDE search) to locate IDs/sections instead of rereading entire files. Do not restate documentation in responses unless needed to explain a decision.

## Sources of truth
Precedence:
1. User instruction in the current task.
2. This `AGENTS.md`.
3. `docs/SPEC.md` for product scope, requirements and domain rules.
4. `docs/ARCHITECTURE.md` for technical rules.
5. `docs/ADR.md` for accepted architectural decisions.
6. Existing tests and code, unless they conflict with 1–5.

If a business/security/data rule is missing or ambiguous, do not invent it silently. Stop only if the ambiguity blocks safe implementation; otherwise choose the smallest reversible technical option and report it.

## Non-negotiable product boundaries
- Not a general ERP.
- No diagnosis, symptom-based treatment advice or therapeutic substitution.
- No microservices in the MVP.
- PostgreSQL is the single operational source of truth.
- Offline mode is limited contingency, not indefinite disconnected operation.
- SUNAT/CPE stays decoupled from core POS/inventory.
- No Redis, RabbitMQ, Kafka, Elasticsearch or similar infrastructure without an approved ADR and demonstrated need.
- Do not add out-of-scope modules “for future use”.

## Backend architecture
Style: **modular monolith + pragmatic Clean Architecture + feature/vertical-slice organization**.

Projects and dependency direction:
- `MediPOS.Domain -> MediPOS.SharedKernel`
- `MediPOS.Application -> MediPOS.Domain, MediPOS.SharedKernel`
- `MediPOS.Infrastructure -> MediPOS.Application, MediPOS.Domain, MediPOS.SharedKernel`
- `MediPOS.Api -> MediPOS.Application, MediPOS.Infrastructure`

Rules:
- Domain has no EF Core, ASP.NET, PostgreSQL, HTTP or external-service dependencies.
- Application owns use cases/contracts; no concrete persistence details.
- Infrastructure owns EF Core/Npgsql, persistence and external adapters.
- Api owns HTTP, middleware and composition; no business rules.
- Module boundaries are explicit; do not reach into another module's tables from endpoints.
- Avoid generic repositories, generic UnitOfWork, God services, Helpers dumping grounds and speculative abstractions.

## Required stack and conventions
- .NET 10 LTS, ASP.NET Core, EF Core, Npgsql, PostgreSQL 18.
- Nullable enabled.
- Money: `decimal` / PostgreSQL `numeric`, never float/double.
- Auditable timestamps: UTC. Expiration dates: `DateOnly` / PostgreSQL `date`.
- Synchronizable IDs: UUID/UUIDv7.
- Stable enum persistence; never rely on C# ordinal values.
- Async I/O and `CancellationToken` where applicable.
- HTTP errors: Problem Details + stable business error codes.
- Critical sale/purchase/transfer/cash operations are transactional.
- Stock changes only through `StockMovement`; no silent stock edits.
- Operational records are reversed/voided, not deleted to “fix” history.

## Multi-tenancy and security
- Every tenant-private table contains `tenant_id` even if inferable by relation.
- Resolve tenant from authenticated server-side context; never trust a free client-supplied `tenant_id`.
- Sensitive operations validate tenant + license + membership/role + branch + schedule when applicable.
- PostgreSQL RLS is a second defense for high-risk private tables, added when tenancy model exists.
- No secrets/tokens in source, logs or committed config.

## Quality gates
Tests must match risk:
- Unit: pure domain rules.
- Integration: real PostgreSQL/Testcontainers for persistence, transactions, constraints, RLS and concurrency.
- Architecture tests: forbidden project/module dependencies.

Before finishing a coding task, run the relevant subset of:
- `dotnet format --verify-no-changes`
- `dotnet build`
- `dotnet test`

Never claim a command passed if it was not run successfully.

## Git and delivery workflow
The user owns commits and GitHub publication.

Agent may inspect `git status` / `git diff`, but must **not** commit, push, tag, rebase, create PRs or modify remote history unless explicitly requested.

Keep task output concise:
1. what changed,
2. validation commands/results,
3. blockers or decisions needing review.

Do not paste large diffs or repeat documentation.

## Planning discipline
`docs/BACKEND_PLAN.md` is the execution roadmap. Do not advance to a later sprint unless the current prompt requests it and prerequisites are met.

Update project documentation only when:
- a requirement/domain rule changes,
- an ADR is approved,
- a sprint/milestone status changes materially.

Do not create prompt files, meeting notes, generated plans, task logs or duplicate documentation inside `docs/`.
