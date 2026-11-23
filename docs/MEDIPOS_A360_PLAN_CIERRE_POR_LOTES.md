# MediPOS — Plan de cierre A360 por lotes (4–5 objetivos)

**Origen y alcance:** `docs/AUDIT_FUNCTIONAL_360.md` (auditoría del 23-11-2026), `docs/CURRENT_STATE.md`, `docs/SPEC.md`, `docs/ARCHITECTURE.md` y código local. Rama: `ci/postgres-diagnostics-8bc6b4c`; SHA de referencia publicado: `ecfb687f4eaaca2e850249e3d94519803c99cd32`. **El árbol de Windows contiene cambios locales posteriores**, incluidos A360-01, documentación y S4: siempre inspeccionarlos antes de editar. Este plan no certifica esos cambios ni autoriza SQL.

## Principio de ejecución

**Una ejecución de Codex por lote, con cuatro o cinco objetivos delimitados.** No es permiso para mezclar cuatro migraciones o inventar cuatro defectos. Para cada objetivo: evidencia → impacto con GitNexus → ajuste mínimo, si está justificado → regresiones focalizadas → estado y bloqueo. Reutilizar contratos existentes, no refactorizar por estética.

GitNexus CLI 1.6.12 tiene índice del SHA de referencia y cubre aproximadamente 1.235 nodos File. En sesiones donde MCP devuelve `unknown MCP server`, usar CLI local (`gitnexus --help` y subcomandos disponibles); FTS puede fallar, usar `cypher`, `context`, `trace`, `impact`/`detect-changes` si existen. **El índice no representa automáticamente cambios locales no confirmados**. Empezar por `git status --short`, `git diff --name-only`, `git diff --check` y búsquedas focalizadas. No reindexar automáticamente, no descargar el grafo completo y no leer README.history ni documentación enorme.

### Contrato común (aplica a todos los lotes)

1. Respetar `AGENTS.md`, `frontend/AGENTS.md` y `CURRENT_STATE.md`; preservar todos los cambios locales existentes.
2. Revisar el código y su prueba **local actual**, no asumir que la rama remota incluye A360-01 o los ajustes S4.
3. Separar **DEFECTO CONFIRMADO**, **RIESGO/CONTRATO INCOMPLETO**, **HIPÓTESIS** y **ACEPTACIÓN PENDIENTE**. No «arreglar» hipótesis sin reproducirlas.
4. Mantener monolito modular, Google/OIDC, CSRF, autoridad de plataforma separada, RLS, aislamiento por botica/sede, exactitud decimal, idempotencia y auditorías históricas.
5. En POST con respuesta incierta, nunca reenviar con clave nueva; distinguir fallo de escritura, lectura posterior y confirmación por constancia. La clave debe enlazar actor/membresía/tenant/sede/tipo/hash de intención y el resultado original.
6. Si una operación y su constancia no pueden confirmarse en **la misma unidad atómica** por las transacciones existentes, **no** agregar superficialmente `RecoverableWriteFilter`; registrar el bloqueo y continuar únicamente objetivos independientes y seguros.
7. Sin SQL real contra DEV, sin S4, sin cambios de secretos, GRANT, credenciales o flags; sin Docker/QA/staging adicional; sin alterar LAB/producción. No aplicar migraciones, aunque puedan analizarse. **Si se requiere crear la migración 40 o un cambio de esquema/grants**, detener esa sub-tarea y presentar primero diseño, impacto en S4 y aprobación solicitada. No modificar las migraciones 1–39.
8. No hacer `git reset`, `git clean`, `pull --rebase`, commit, push ni merge. No instalar dependencias nuevas sin justificar y aprobar.
9. Probar con la menor selección que cubra cada riesgo: Vitest/ESLint/build en archivos afectados; tests .NET focalizados de los handlers tocados y arquitectura cuando corresponda. **PostgreSQL real solo con destino autorizado y aislado conforme al gate existente**; no usar datos comerciales de `medipos_dev` para pruebas automatizadas. No ejecutar suites completas por rutina.
10. Finalizar cada lote con tabla: ID, evidencia, archivos, estado `CORREGIDO_EN_CÓDIGO` / `PROBADO_LOCAL` / `BLOQUEADO` / `NO_DEFECTO` / `PENDIENTE_ACEPTACIÓN`; resultado de pruebas exacto y pasos manuales. Como máximo actualizar la sección pertinente de `CURRENT_STATE.md`, sin crear otros informes Markdown.

## Lote 1 — P0 / Recuperación de operaciones críticas (4 objetivos)

**Objetivo:** eliminar ambigüedad de escrituras financieras y trazabilidad, conservando transacciones existentes.

| ID | Alcance exacto | Puerta de salida |
|---|---|---|
| L1.1 — A360-01 | `frontend/src/features/cash/useCashClose.ts`, `CashPage.test.tsx` y consumidores de Caja/POS. La corrección ya fue reportada: **revisar diff local**, no reescribir. | POST aceptado + GET 401/403/404/429/503 no libera marca ni reenvía POST; lectura `closed` libera cierre aunque el arqueo falle; 12/12 PASS reportados por Codex, no independientes. |
| L1.2 — A360-03 | Alta de compra: `usePurchaseReceipt.ts`, `PurchasingEndpoints.cs`, `CreatePurchaseHandler`, persistencia y `tenant_operation_receipts`. | Identificador original consultable tras perder POST; misma intención misma compra; payload distinto conflicto; referencias documentales duplicadas permitidas; **sin stock** antes de confirmar compra. |
| L1.3 — A360-04a | Solicitud de transferencia de mercadería: `useTrackedTransfer.ts`, `TransferEndpoints.cs`, `RequestTransferHandler` y persistencia. | La pérdida del acuse no crea solicitudes duplicadas; el ID original se puede reconciliar; autorización y reservas/lotes permanecen intactos. |
| L1.4 — A360-04b | Préstamo/traslado de sencillo: `CashTransferEndpoints.cs`, `DispatchCashTransferHandler`, `useTrackedTransfer` y ledger. | Una intención produce **un solo** efecto monetario; recuperación distingue envío, recepción y estado no observado; saldo y permisos conservados. |

**Revisión obligatoria antes de implementar L1.2–L1.4:** `RecoverableWriteFilter`, `IRecoverableOperations`, `RecoverableOperations`, `OperationReceiptRow`, `RecoverableOperationPostgreSqlTests`, `tenant_operation_receipts` y handlers concretos. El mecanismo actual reconoce tipos `supplier`, `sale-draft`, `product-import`, `adjustment`, `minimum`; **compra y transferencias no están habilitadas**. Además `RecoverableOperations.ExecuteAsync` inicia una transacción EF, mientras algunos handlers usan su propio scope/transacción. Verificar que el diseño no incurra en transacciones anidadas incompatibles, permita replay con autorización revalidada y preserve el mismo commit para negocio y constancia. No resolver solo agregando un `kind` al filtro.

**Casos focalizados indispensables:** caída anterior/posterior al commit, respuesta perdida, consulta que falla, solicitud concurrente con igual clave, replay idéntico, payload cambiado, dos intenciones legítimas de contenido igual, actor/membresía/sede/tenant incorrectos, recarga, 401/403/409/429/5xx, un único movimiento financiero/stock pertinente. Usar test de transporte para UI y prueba PostgreSQL controlada para atomicidad cuando esté disponible/aprobada. No atribuir PASS de dobles de HTTP a PostgreSQL.

**Política de decisión:** si se pueden implementar los tres contratos sobre esquema39 y transacciones existentes sin romperlos, realizar cambios focalizados y pruebas. Si alguna ruta requiere rediseñar la unidad transaccional, migración40 o nuevos GRANT, entregar su diseño concreto y seguir las demás rutas independientes; no forzar cierre ficticio.

**Entrega L1:** 4 estados + diffs + pruebas seleccionadas + nuevos bloqueos y su autorización necesaria. No avanzar a lote 2 automáticamente.

## Lote 2 — Identidad, permisos y continuidad (4 objetivos)

| ID | Alcance | Criterio |
|---|---|---|
| L2.1 — A360-05 | Gmail A/B/C, callback Google, reclamo del primer Owner e invitaciones múltiples/transitorias. | Correo y subject Google verificados; claim atómico y exactamente una membresía; un fallo transitorio no se interpreta como reclamo concedido; no introducir endpoint público para reclamar. |
| L2.2 — F03 | Suspender/renovar/reactivar licencias con sesión abierta y actualización concurrente. | Backend conserva autoridad; UI no confunde intención, rechazo ni transición ajena; expiración/suspensión bloquean lo que corresponde. |
| L2.3 — F04 | Roles, último Owner, miembros, sedes y horarios. | No acceso cruzado entre boticas/sedes ni promoción no autorizada; comprobar comportamiento al revocar permisos. |
| L2.4 — A360-07 | Offline/PWA, dos pestañas, mismo evento, pérdida de respuesta y scope de usuario/sede. | Mismo evento produce efecto único; no fuga de cola al cambiar de cuenta; conflictos trazables. |

**Regla:** estos ítems son principalmente **riesgos pendientes de aceptación**, no defectos confirmados. Primero reproducir con tests focalizados, mocks controlados o inspección de contratos. Solo codificar correcciones ante evidencia; no hacer login real ni POST en datos privados desde Codex. Preparar un guion de Chrome A/B/C para el operador.

**Entrega L2:** cuatro dictámenes con evidencia y escenarios manuales; detener ante decisiones de seguridad, permisos o persistencia.

## Lote 3 — Integridad comercial y deuda técnica controlada (4 objetivos)

| ID | Alcance | Criterio |
|---|---|---|
| L3.1 — F07/F08 | Venta con pagos mixtos, stock/lotes, anulación y cierre concurrentes, comisiones. | No doble cobro, stock negativo ni compensación duplicada; auditoría completa; sin inventar resultados desde mocks. |
| L3.2 — F05 | Presentaciones versionadas, precio, catálogo local/global y contexto de sede. | IDs y snapshots estables, rechazo de revisión obsoleta y precios de transacción decididos por servidor. |
| L3.3 — F11 | Importaciones, notificaciones y auditoría; límites y reconsulta. | Procesos reanudables, errores parciales honestos y visibilidad limitada a permisos; no importar datos reales sin autorización. |
| L3.4 — A360-08 | Rutas antiguas de catálogo/plataforma, consultas duplicadas, posibles piezas sin consumidor. | Inventario de consumidores internos/externos, medidas de latencia; **no eliminar** contratos por ausencia de aristas de grafo. |

**Regla:** no iniciar refactorización amplia. Cada riesgo confirmado genera un parche mínimo; no intentar resolver módulos sanos por completitud.

**Entrega L3:** cuatro dictámenes y conjunto de defectos reales corregidos, con pruebas proporcionales y regresiones pendientes.

## Lote 4 — Gate productivo (5 decisiones; no implica despliegue)

| ID | Gate | Evidencia de salida |
|---|---|---|
| L4.1 — A360-02 | Selección del SHA candidato, PR a `main`, checks frontend/backend y PostgreSQL real autorizado. | PASS verificable en **el SHA exacto**, sin confundir CI anterior `9f543454` con el candidato nuevo. |
| L4.2 — A360-06 PROD | Backup completo, restauración comprobada, RPO/RTO aprobados, rollback operacional y alertas. | Ensayo real y seguro, en destino autorizado, sin depender de S4. |
| L4.3 — S4 DEV | Respaldo39 fresco + ensayo DROP/ROLLBACK autorizado en `medipos_dev`, luego eventual limpieza comercial con **segunda autorización**. | No ejecutar por defecto; si no es imprescindible para probar Superadmin, validar con datos existentes. S4 **no** es plan de recuperación de producción. |
| L4.4 — ACEPTACIÓN | Chrome Gmail A/B/C, aislamiento, compras, POS, caja, transferencias, offline A20 y respuesta perdida. | Guiones con resultados, capturas/red sanear, casos ACEPTADO solo tras ejecución real. |
| L4.5 — RELEASE | Dominio/HTTPS, secretos, despliegue, monitoreo, seguridad, migraciones, rollback, soporte, obligaciones tributarias y alcance A18. | Lista **GO/NO-GO** sin publicarlo automáticamente. Resolver alcance/obligaciones de purga A18 y emisión según piloto; no afirmar factura electrónica válida por el ticket interno. |

**Política:** no llamar «listo para producción» a `CODE_READY`, builds, pruebas unitarias o índice GitNexus. No publicar release, activar producción, ejecutar purgas, ni exponer clientes sin aceptación y autorización independiente.

## Prompt reutilizable: ejecutar un lote

Pega este texto en Codex para iniciar **solo el lote indicado**. Mantén este plan en un único archivo dentro de `docs/` si decides versionarlo; nunca dupliques los resultados históricos.

> **MediPOS — Ejecutar LOTE N del plan A360.** Lee `AGENTS.md`, `frontend/AGENTS.md`, `docs/CURRENT_STATE.md`, `docs/AUDIT_FUNCTIONAL_360.md` y la sección **Lote N** de `docs/MEDIPOS_A360_PLAN_CIERRE_POR_LOTES.md`. Conserva todos los cambios locales; inspecciona `git status` y diff, especialmente caja A360-01 y S4. Usa GitNexus CLI graph-first (MCP solo si está disponible en esta sesión) y lectura de fragmentos. Trata los 4–5 objetivos del lote como un único paquete de trabajo con checkpoints separados. Reproduce y corrige **solo defectos demostrados**, reutilizando contratos existentes y preservando idempotencia, RLS, permisos y transacciones. Ejecuta únicamente pruebas focalizadas y builds de archivos afectados. Si cualquier sub-tarea requiere migración40, SQL en DEV, nuevos GRANT, cambio de autoridad o cambio transaccional inseguro, **detén solo esa sub-tarea**, explica el diseño y sigue las independientes seguras. No instales nada, no hagas SQL real, S4, nuevos entornos, suites masivas, commit, push ni merge. Responde con tabla ID/estado/evidencia/archivos/tests, riesgos y **próximo lote recomendado**, en máximo 25 líneas; no avances automáticamente.

### Primer comando de trabajo

1. Guardar este archivo en `C:\Proyectos\MediPOS\docs\MEDIPOS_A360_PLAN_CIERRE_POR_LOTES.md`.
2. Abrir Codex local sobre la rama actual.
3. Ejecutar el **prompt reutilizable sustituyendo `LOTE N` por `LOTE 1`**.
4. Compartir el resumen, diffs relevantes y bloqueos técnicos antes de autorizar migraciones o siguiente lote.

### Notas de gobernanza

- A360-01 reportado como 12/12 PASS en tests focalizados y build aislado, **no** aceptado con Chrome; no repetir trabajo salvo regresión nueva.
- `docs/AUDIT_FUNCTIONAL_360.md` es el diagnóstico; este archivo es el plan de corrección; `docs/CURRENT_STATE.md` es el estado operativo. No crear otro documento de salida por cada lote.
- Si un cambio del lote afecta el contenido indexado, `gitnexus detect-changes` puede señalar cobertura, pero la evidencia final procede del código modificado, tests y criterios de aceptación.
- No etiquetar `v0.1.0-rc.1` sin cerrar gates del lote4 sobre el SHA final.
