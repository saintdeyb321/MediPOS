# MediPOS — Lote 4 | Gates técnicos y operativos para candidato productivo

**Fecha de revisión:** 2026-11-23. **Rama:** `ci/postgres-diagnostics-8bc6b4c`. **SHA Lote 3 publicado en GitHub:** `a6ec96866883d979199b24e42190cabf8184f3bf` (verificar HEAD local antes de usar). No es certificación ni aprobación de release.

## Dictamen de entrada

- L1 Caja: corrección y 12 pruebas focalizadas reportadas PASS; Chrome pendiente.
- L1 Compras: idempotencia del borrador y constancia con transacción compartida en código; **variantes PostgreSQL nuevas compiladas, pero no ejecutadas**. Mercado: no enviar segundo POST con clave diferente ante resultado incierto.
- L1 Solicitud de mercadería: **bloqueada** por transacción propia en `TransferStores.CreateAsync`; no anexar directamente `RecoverableWriteFilter` sin atomicidad.
- L1 Despacho de sencillo: **bloqueado** por transacción propia, candado de caja y ledger en `CashTransferTransaction`; no anexar filtro genérico sin preservar todo el commit.
- L2 Google/Owner, licencias, personal, offline: pruebas locales reportadas 62 Vitest + 201 xUnit PASS; identidad real y concurrencia PostgreSQL pendientes.
- L3: commit GitHub `a6ec968`; 13 archivos afectados, sin cambio de backend. Se publicaron correcciones de conciliación en catálogo/importaciones/notificaciones y regresión offline. **268 pruebas focalizadas PASS reportadas por Codex (86 Vitest y 182 xUnit); no ejecutadas aquí.** No aparecen workflow runs ni checks asociados al SHA consultado.
- **Observación independiente adicional:** en `frontend/src/features/product-import/ImportPage.tsx`, `awaitingReceipt` solo se activa para UUID válido en `importPending`. Una URL heredada `?importPending=1&importJob=<UUID>&importAck=1` puede mostrar un trabajo arbitrario y permitir `Finalizar revisión de importación` sin constancia de identidad. No se ha demostrado doble escritura, pero sí un **cierre de conciliación no sustentado por la intención**. Mantener modo lectura de trabajos conocidos sin declararlos originales, y bloquear la liberación de la intención dudosa hasta prueba suficiente.
- `ImportProductsFromExcelHandler`/`ProductImportStore` confirman inicio y filas en commits independientes; `TryFailAsync` es best-effort. La interrupción abrupta del proceso **podría** dejar un job `processing`. El alcance y tratamiento correctos deben investigarse antes de agregar reconciliación automática.
- **GO para iniciar el Lote 4A técnico; NO-GO para publicar MediPOS.** El Lote 4 no es una orden para ejecutar S4 ni un despliegue.

## L4A — Cinco objetivos de ingeniería y evidencia (una ejecución de Codex)

### L4A.1 — Compras: atomicidad en PostgreSQL 18

**Inspeccionar** `backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/RecoverableOperationPostgreSqlTests.cs`, `RecoverableOperations`, `PurchasingValidation`, `TenantLicenseProvisioning`, RLS, permisos y fixture. Preparar o ejecutar *solamente si existe destino efímero explícitamente autorizado* los cuatro casos parametrizados de `purchase-draft`: pérdida de acuse/replay, rollback del negocio y constancia, competencia por misma intención, autorización por tenant/sede/actor/membresía. Añadir pruebas si falta comprobar identidad de resultado o inmovilización del stock. Acreditar comando, SHA, PostgreSQL `server_version`, conexión aislada sin secretos, conteo y evidencia. Si no hay entorno autorizado, **no ejecutar SQL**; dejar comandos exactos y resultado PENDIENTE.

### L4A.2 — Solicitud de mercadería recuperable

Revisar `RequestTransferHandler`, `TransferStores.CreateAsync`, `TransferEndpoints`, `useTrackedTransfer` y tests de transferencia. Diseñar y, solo si es posible preservar transacción única sin migración/GRANT, implementar intención persistente ligada a actor/membresía, tenant y **sede destino**, `kind` y hash del payload. Un solo commit para **transferencia + líneas + evento + auditoría + constancia original**. Bajo replay retornar mismo ID; distinto payload o scope: conflicto; solicitud legítima idéntica pero clave distinta: permitida. Evitar transacción anidada, no aplicar filtro genérico por rutina; pruebas locales de contrato, posteriormente PostgreSQL autorizado. Si es imposible demostrar atomicidad, **BLOQUEADO + diseño preciso**, continuar otros objetivos.

### L4A.3 — Préstamo de sencillo recuperable

Revisar `DispatchCashTransferHandler`, `CashTransferTransaction`, `CashTransferEndpoints`, `useTransferCash`, `useTrackedTransfer`, RLS y tests de concurrencia. Preservar **bloqueo de sesión de caja**, cálculo de ledger, validación de saldo y autorización por **sede origen**. La constancia y el efecto monetario deben confirmarse en el **mismo commit**; doble petición, pérdida del primer acuse y replay no deben registrar doble egreso. No tocar recepción de sencillo salvo dependencia estricta comprobada. Si exige cambios estructurales, migración 40 o privilegios nuevos, **BLOQUEADO + diseño**, no ensayo con datos DEV.

### L4A.4 — Importación: conciliación legada y jobs interrumpidos

**Defecto de UI a comprobar con regresión:** `importPending=1` (no UUID) junto a `importJob=<otro-id>` y `importAck=1` no constituye prueba de intención original. Debe ser imposible atribuir otra carga al POST incierto o desbloquear nueva carga como si hubiera constancia. Preservar consulta segura de jobs antiguos, mensajes de soporte y compatibilidad sin fabricar correlación. Añadir regresión de URL manipulada/recarga. Para `processing` tras caída abrupta: inspeccionar StartAsync/ImportRowAsync/CompleteAsync/FailAsync, idempotencia por fila, locks y auditoría; especificar operador/recuperación segura. **No marcar automáticamente FAILED** por antigüedad sin probar ausencia de escritor activo, ni repetir archivos/filas.

### L4A.5 — Regresiones de concurrencia y evidencia reproducible

Seleccionar filtros mínimos existentes para venta/caja/stock/void, último lote, aislamento RLS, offline `eventId` desde dos conexiones/pestañas y recuperación de respuesta perdida, más los casos de los objetivos anteriores. Consultar `.github/workflows/backend-validation.yml` y fixtures; validar si se puede obtener evidencia en PostgreSQL 18 **efímero y autorizado**, preferiblemente mediante gate CI manual oficial. Si la ejecución de workflow no ha sido autorizada, no iniciarla: presentar nombre de workflow, rama/SHA, paso de disparo y el resultado que debe comprobarse. No usar mocks para acreditar ACID/RLS ni SQLite para garantizar PostgreSQL. No correr la suite completa por defecto.

### Reglas de L4A

1. Inspeccionar `git status --short`, `git diff --check`, HEAD, `AGENTS.md`, `frontend/AGENTS.md`, `docs/CURRENT_STATE.md`, `docs/AUDIT_FUNCTIONAL_360.md` y el plan existente `docs/MEDIPOS_A360_PLAN_CIERRE_POR_LOTES.md`. Revisar las diferencias locales y preservarlas.
2. GitNexus CLI graph-first solo en símbolos críticos, y contrastar con código local cuando el índice vaya detrás de HEAD. No reindexar automáticamente ni leer los ~1.235 archivos.
3. Trabajar de a cinco objetivos pero **separar commits lógicos solo si el usuario autoriza hacer commits**; por defecto no commit/push/merge.
4. Aplicar solo parches acotados, con pruebas focalizadas antes/después, build, lint y `git diff --check`. No instalar herramientas, modificar OAuth, secretos, RLS, datos comerciales, S4, migraciones 1–39 ni ejecutar SQL en `medipos_dev`.
5. **Stop-gates:** migración 40, cambios de GRANT/políticas, SQL en datos existentes, nuevo entorno con datos reales, modificación transaccional que no demuestra atomicidad, lanzamiento de CI remoto y S4 exigen aprobación específica. Ante bloqueo, avanzar a objetivos independientes seguros.
6. Registrar resultado de cada objetivo como `CONFIRMADO`, `CORREGIDO_EN_CÓDIGO`, `PROBADO_LOCAL`, `PROBADO_POSTGRESQL_18`, `BLOQUEADO` o `PENDIENTE_ACEPTACIÓN`, sin inflar evidencias. No declarar release aprobado.
7. Actualizar solo `docs/CURRENT_STATE.md` con **SHA/entorno/resultado real**. Si el usuario guardó este plan en `docs/`, no generar informes adicionales.

**Salida L4A:** tabla de cinco filas (ID, evidencia, código, pruebas, bloqueos), pruebas y counts PASS/FAIL/SKIP, comandos exactos, SHA, riesgos remanentes, decisiones de autorización solicitadas, máximo 30 líneas. Detenerse; no empezar L4B automáticamente.

## L4B — Aceptación Chrome, dos boticas y datos controlados (después de L4A)

Guion operativo con perfiles aislados **A Superadmin, B Owner de botica uno y C Owner de botica dos**: alta Trial con correo verificado, reclamo real, aislamiento, suspensión/renovación con sesión abierta, trabajadores/sedes/horarios, catálogo → compra → lote → stock → venta (pagos mixtos) → caja → anulación → transferencias, corte de red, sincronización de una misma venta desde dos pestañas y recuperación de cierre. Verificar con herramientas de red y observabilidad saneadas. Registrar SHA, fecha/operador, IDs de prueba no privados, esperado/observado, capturas sin datos sensibles y fallo/solución. **No iniciar sesión con Gmail ni crear operaciones comerciales automáticamente desde Codex.** S4 solo si se requiere empezar con datos vacíos y se autorizan por separado el ensayo de restauración y la limpieza DEV.

## L4C — GO/NO-GO release (después de L4A y L4B)

Exigir CI reproducible en SHA exacto y PR revisado a `main`, suites focalizadas y gate PostgreSQL 18 ejecutado, respaldo+restauración probados, rollback, monitoreo, dominio/HTTPS, secreto/rotación, RLS/roles, observabilidad, manual operativo y límites regulatorios (ticket interno ≠ comprobante SUNAT). Evaluar alcance de purga física A18 y restricciones de piloto. **Sin PASS y aceptación por operador → NO-GO.** No publicar versión, hacer deployment o merge sin autorización.

## Prompt corto para Codex

> Ejecuta solamente **L4A** de `docs/MEDIPOS_A360_LOTE4_GATES_CIERRE.md` (cinco objetivos). Lee instrucciones locales y estado antes de cambios. Toma GitHub SHA `a6ec96866883d979199b24e42190cabf8184f3bf` como referencia publicada y verifica HEAD/working tree. GitNexus primero; fragmentos de código después. No reabrir Lotes 1–3 salvo defectos explícitos del gate. Para transferencias, solo implementar si se demuestra atomicidad de constancia y efecto con el mismo commit y permisos correctos; en otro caso registrar bloqueo técnico y avanzar. Reproducir y corregir el bypass de conciliación `importPending=1` si se confirma. Preparar las regresiones PostgreSQL y ejecutar **únicamente** cuando haya destino efímero explícitamente autorizado, jamás sobre `medipos_dev`. No nuevos GRANT, migraciones, S4, CI remoto, commits/push sin aprobación. Test focalizado, lint/build y diff-check. Actualizar CURRENT_STATE con evidencia; entregar tabla 5 objetivos y bloqueos; detenerse sin entrar a Chrome/release.
