# MediPOS — RFA5B · Cierre definitivo de la radiografía

**v1.0 · 27/11/2026 · GO de revisión documental final.** Rama `ci/postgres-diagnostics-8bc6b4c`, HEAD verificado `f9ea19ebd09e46c1e141000049796fe3ab429448`. RFA4B aprobada en alcance; RFA5A termina con bloqueos. Última fase del plan original. Producción NO-GO. Modelo GPT-6.2 Astra, High, Standard; sin subagentes. El operador publica los cambios; Git solo lectura. No ejecutar nuevos CI ni suites, no implementar correctivos, no instalar herramientas, no tocar DEV, main, datos, configuración, contratos o esquema. Respetar AGENTS, SPEC, ARCHITECTURE, ADR y master RFA. Reutilizar ub-mode MIT fijado en RUN-20, sin reinstalar; no usar GitNexus sin licencia comercial. Este encargo autoriza la consolidación documental, no una puesta en producción.

## 1. Evidencia remota independiente para RUN-30

Ambos runs fueron comprobados mediante GitHub, intento 1, rama indicada y SHA idéntico al HEAD. No solicitar al operador los mismos logs. No atribuir a un commit posterior de documentación un nuevo resultado de ejecución.

- [B-02 46070129846](https://github.com/saintdeyb321/MediPOS/actions/runs/46070129846), job 140744804457, 21:24:43–21:28:31 UTC (228 s), conclusión failure; checkout, herramientas, build React, build E2E y Chromium PASS; P0–P5 exit 1. Build `ccfa07ff17ca7a4ea4de7d3739027d6c8ca025eab806c92fddd0d0f63ed79cca`. PostgreSQL 18.7 (180007), HTTPS aislado, runtime restringido `medipos_test_runtime`.
- [Backend validation 46071442479](https://github.com/saintdeyb321/MediPOS/actions/runs/46071442479), 21:36:49–21:51:50 UTC (901 s), conclusión failure. Job frontend 140749067007 success; job backend 140749067322 failure. PostgreSQL 18.7. Restore, build, comprobación de diagnósticos, auditoría NuGet, arquitectura, formato y preparación de contenedores PASS. Unitarias y PostgreSQL FAIL; resumen NOT_APPROVED. Artefactos: ninguno en ese run.

```text
B02 P0 PASS
B02 P1 PASS
B02 P2 PASS
B02 P3 PASS CASH_HISTORY_AND_RECOVERY
B02 P4 PASS POS_PAY_TICKET_VOID
B02 P5 DIAG SEED PASS
B02 P5 DIAG SEARCH PASS
B02 P5 DIAG CREATE START
B02 P5 CREATE READ BEFORE PRODUCTS 0 AUDIT 0 OWNER 0 LOTS 0 MOVES 0 MATCH UNKNOWN
B02 P5 CREATE CLICK DONE
B02 P5 CREATE UPSTREAM 201
B02 P5 CREATE ID VALID
B02 P5 CREATE ABORT DONE
B02 P5 CREATE GUARD PENDING
B02 P5 CREATE READ AFTER_RESPONSE PRODUCTS 1 AUDIT 1 OWNER 1 LOTS 0 MOVES 0 MATCH YES
B02 P5 CREATE POST 1 INTERCEPT 1
B02 P5 DIAG CREATE PASS
B02 P5 DIAG EDIT START
B02 P5 UNITS READ BEFORE VERSION SAME ROWS 0 AUDIT 0 EFFECTS SAME
B02 P5 UNITS GET HTTP 200
B02 P5 UNITS PUT HTTP 200
B02 P5 UNITS READ AFTER_PUT VERSION CHANGED ROWS MANY AUDIT 1 EFFECTS SAME
B02 P5 UNITS READ INCONCLUSIVE VERSION CHANGED ROWS MANY AUDIT 1 EFFECTS SAME
B02 P5 DIAG EDIT FAIL UNITS_STALE
B-02 failed or was incomplete. Unreported phases are unverified.
```

Interpretación por orden de aserciones del arnés: CREATE acredita el alta local y conciliación del original tras RFA4B (un POST, un producto y auditoría Owner, cero stock). No acredita CAT-10, pérdida completa de storage ni variantes no ejecutadas. EDIT supera metadata, 409 de revisión obsoleta y PUT de precios; GET de unidades y primer PUT versionado 200, versión DB cambiada, dos unidades activas (base 1 y caja 10.125), preservación de identidades anteriores cuando existan, una auditoría nueva del Owner y recuentos de lotes/movimientos/líneas de ventas/compras/transferencias iguales. No confundir igualdad de recuentos con igualdad de contenido histórico. UNITS_STALE falla antes de publicar cualquier estado HTTP STALE: en `PutCatalogAsync` incluye obtención CSRF, APIRequest y evaluación de estado. No afirmar que hubo PUT recibido, 409 incorrecto, timeout, duplicación o defecto de concurrencia demostrado. La lectura INCONCLUSIVE no sustituye la comparación posterior al conflicto. LEGACY_UNITS, STATUS, ASSOCIATION, IMPORT y PERSISTENCE final NOT_RUN. P5 global FAIL. ProductUnitIntegrationTests 10/10 PASS; no sustituye el recorrido HTTP ni el 409 faltante.

| Gate | Total | PASS | FAIL | SKIP | Resultado |
|---|---:|---:|---:|---:|---|
| Vitest | 652 | 652 | 0 | 0 | FRONTEND_GATE_PASSED; build/lint PASS |
| Unit .NET | 2700 | 2694 | 6 | 0 | FAIL |
| Architecture | 8 | 8 | 0 | 0 | PASS |
| PostgreSQL integration | 907 | 890 | 17 | 0 | FAIL |

Frontend histórico 645/646 superado en este SHA; no inferir causa del fallo histórico. Suites PostgreSQL fallidas: TenancyLicensingTests 8/12, BranchesPersistenceTests 1/11, SaleVoidIsolationTests 10/11, RuntimeReadinessPostgreSqlTests 0/2. RLS 19/19, RuntimeRoleHardening 10/10, SecurityHardening 7/7, CashClosePersistence 43/43, CashSessionPersistence 9/9, CashTransferConcurrency 8/8, CashTransferIsolation 11/11, CashTransferPersistence 9/9, CashTransferRecoverability 3/3, ProductUnitIntegration 10/10, PurchaseWorkflowIntegration 7/7, PurchasingIsolationIntegration 20/20, RecoverableOperationPostgreSql 16/16, SaleCheckoutConcurrency 8/8, SaleCheckoutIsolation 11/11, SaleCheckoutPersistence 17/17, SaleVoidPersistence 12/12, SaleVoidConcurrency 3/3, RecoveryPostgreSql 20/20. B8.6.3.1 8/8, B8.6.3.2 7/7, B8.6.4 8/8. No convertir suites completas en PASS de UC sin cotejar sus aserciones.

## 2. Bloqueo transversal de composición (nuevo hallazgo, sugerido F-010)

Los identificadores saneados de los 6 fallos unitarios corresponden a cuatro métodos (uno parametrizado):
- `ExpirationSchedulerTests.DailySchedulerRequiresExplicitEnablement` (line 18); `StartupRunsInBackgroundAndStoppingCancelsActiveScanWithoutOverlap` (26).
- `SecurityCompositionTests.ActualApiPublishesOnlyHealthReadinessAndDevelopmentOpenApi` (Program.cs:43, test:25).
- `ElectronicDeliveryContractTests.NormalDiHasNoSimulatedProviderOrHostedDeliveryAndRequiresTechnicalIdentity` (181).

Los 17 fallos de integración corresponden a las cuatro suites anteriores. Identidades saneadas (algunas parametrizadas, no contar métodos como casos):
- TenancyLicensingTests: `StoreRejectsMismatchedTenantAndLicenseWithoutChanges`, `ConcurrentRenewalCannotOverwriteExpirationOrAppendPartialHistory`, `CreationPersistsTenantLicenseHistoryAndStableCodes`, `MutationSlicesPersistStateHistoryAndPurgeGate` (helper:277).
- BranchesPersistenceTests: `CompositeForeignKeyRejectsLegalEntityFromAnotherTenant`, `SuspendedOrExpiredLicenseRejectsBranchCreation`, `PartialUniqueIndexRejectsTwoMainHubsForSameTenant`, `LegalEntityAndBranchPersistTenantBoundaryAndUtcDates`, `MovingHubPreservesOneHubPerTenantAndRejectsForeignTarget`, `ForeignKeysRejectLegalEntityAndBranchWithoutTenant`, `UncommittedProvisioningRollsBackAndReleasesLicenseLock`, `ConcurrentCreationsSerializeAndCannotExceedLicenseLimit`, `LicensedLimitRejectsCreationAfterLastAvailableSlot` (helper:253).
- SaleVoidIsolationTests: `ReusedPhysicalConnectionClearsReversalsBeforeUnscopedAndNextTenantReads` (128).
- RuntimeReadinessPostgreSqlTests: `RuntimeReadinessUsesRestrictedRoleAndDoesNotMutateTenantData`, `PrivilegedCredentialIsRejectedBeforeReadinessQuery` (helper:49).

Todos los diagnósticos publicados clasifican UNCLASSIFIED_TEST_FAILURE y System.InvalidOperationException. Las líneas contrastadas en el SHA apuntan a `BuildServiceProvider` con ValidateOnBuild/ValidateScopes o `builder.Build` (Program). Hipótesis prioritaria: composición/registro/configuración de servicios compartida; dependencia concreta no identificada. No afirmar defecto de RLS, pérdida de dinero, falta de licencia o 23 defectos independientes. Comparar solo símbolos afectados con baseline L-05 si aporta valor; fixture PostgreSqlFixture cambió para el piloto aislado, sin causalidad acreditada. No investigar ni modificar producto en RFA5B. Registrar una futura prueba discriminante focalizada que identifique servicio/dependencia faltante sin publicar secretos, con regresión de las suites afectadas y gate final. No rebajar seguridad ni validar mediante desactivar ValidateOnBuild/ValidateScopes.

## 3. Trabajo de cierre (solo docs/audit/rfa/)

1. Incorporar RUN-30 y un resumen de evidencia reproducible, con links, SHA, build, job IDs, intentos, resultados, fronteras y límites anteriores. Registrar RFA5A_CIERRE_CON_BLOQUEOS. Conservar históricos; reconciliar textos de estado vigente obsoletos.
2. Actualizar MATRIX, fichas afectadas, FINDINGS, TO_BE, ROADMAP, CHECKPOINT y README de manera diferencial. Matriz anterior 156 UC únicos: E1=2 E2=24 E3=102 E4=28 E5=0; 19 PASS, 135 BLOCKED, 1 NOT_RUN, 1 N/A. No promover globalmente. CAT-13/14: GET/primer PUT son HTTP+DB en escenario aislado, no editor browser completo. Acreditar únicamente aserciones y niveles justificados. Los tests fallidos no demuestran automáticamente defecto funcional. Mapear F-009 y nuevo hallazgo de composición a UC reales, mantener evidencias históricas distinguiendo vigencia y no hacer PASS de lo no alcanzado. Recontar niveles/veredictos e IDs; validar coherencia y enlaces locales. No releer o regenerar 156 circuitos completos.
3. Dejar README como entrada única con mapa de nueve familias, cómo ubicar actor/ruta/endpoint/UC, dependencias, persistencia, invariantes, tests, hallazgos, TO-BE y decisión. Confirmar navegación de las fichas existentes e índice; no duplicar documentación. Añadir resumen final de las siete etapas y decisión en README/CHECKPOINT/ROADMAP o un único informe de cierre enlazado. Distinguir cierre del proyecto de auditoría de certificación del sistema.
4. Priorizar backlog de producción independiente del plan RFA (no crear fase RFA6): A) composición DI y gates de licencias/sucursales/anulación/readiness; B) F-009 y P5 restante incluida asociación/importación y validación del historial; C) variantes críticas no acreditadas, recuperación offline/roles/revocación, writer drain CAT-19 e INT-16; D) aceptación E5, backup/restore probado, rendimiento representativo, OIDC real/TLS/hosting/proxy y externos según SPEC/ADR. Opcionales F-004/F-005 y saldo Sencillo siguen DEFER salvo aprobación; no forzar cambios ni inventar prioridad P0/P1 sin prueba de impacto. Cada bloqueo: evidencia, causa conocida/hipótesis, alcance, dependencia, aceptación medible, siguiente responsable/acción y gate exigido.
5. Emitir **RFA5B_CANDIDATO_CIERRE_DEFINITIVO**, **RADIOGRAFIA_CERRADA_CON_LIMITACIONES**, **PRODUCCION_NO_GO**. E5 no realizado, no simular aceptación del operador, restauración, pruebas externas ni rendimiento. La revisión final independiente del operador del commit documental será el cierre del plan, aunque la liberación del producto quede bloqueada. Sin nuevas rondas de Codex ni CI dentro de esta fase. No merge, deploy ni push por agente.

## 4. Entrega

Resumen compacto: estado 7 etapas, cambios y archivos, conteos definitivos, evidencia actual frente a histórica, lista única priorizada de bloqueos, criterios de release, comprobaciones documentales (IDs, enlaces, estados, git diff --check) y limitaciones. HEAD de entrada fijo, conservar pruebas y workflows; no declarar resultados de pruebas no ejecutadas. Git solo `status`, `diff`, `diff --check`, `log`, `show`, `ls-files`, `rev-parse`, `grep` y otras consultas sin mutación. El operador publica una vez; revisión documental posterior sin disparar nuevos gates. Fin del plan.
