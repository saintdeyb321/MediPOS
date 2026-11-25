# MediPOS — Prompt maestro de ejecución E0–E5

**Versión:** 24 de noviembre de 2026 UTC (23 de noviembre, Perú).  
**Destino:** Codex, repositorio `C:\Proyectos\MediPOS`.  
**Rama de trabajo:** `ci/postgres-diagnostics-8bc6b4c`.  
**Base validada:** `4523902c848a53f7f1d9eb89de3e2d8a7a03ae11`.  
**Modo:** un único encargo, ejecución secuencial con checkpoints internos, sin publicación ni despliegue automático.

## 1. Misión y autoridad del encargo

Actúa como Principal Software Engineer y arquitecto de SaaS multi-tenant especializado en .NET 10, EF Core, PostgreSQL 18, React/TypeScript, PWA, integridad financiera y sistemas de farmacia. Implementa las mejoras seguras de E1–E5 en este mismo encargo, partiendo de E0 aprobado. No te limites a producir otra auditoría o una lista de recomendaciones: corrige el código cuando exista una mejora demostrable, añade regresiones necesarias y conserva el comportamiento comercial.

Objetivo final: MediPOS debe ser simple de operar, eficiente en el servidor, mantenible y preparado para un piloto comercial. Menos consultas y pasos innecesarios, no menos garantías. SUNAT/CPE debe quedar preparado para que la integración futura se limite al adaptador específico, su configuración y validación; mantenerlo apagado.

Este documento actualiza la secuencia de `docs/MEDIPOS_A360_LEAN_PREPRODUCCION.md`: sustituye la orden de ejecutar un lote por conversación por una ejecución coordinada de todas las fases. No sustituye AGENTS, SPEC, ADR ni autorizaciones de seguridad. No concede permiso para modificar bases existentes, publicar cambios, activar integraciones ni desplegar producción.

## 2. Baseline acreditado: E0 cerrado

Fuente: [GitHub Actions run 45325845075](https://github.com/saintdeyb321/MediPOS/actions/runs/45325845075), job 138215130869. SHA probado y HEAD remoto verificados: `4523902c848a53f7f1d9eb89de3e2d8a7a03ae11`.

| Gate | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Unitarias | 2664 | 0 | 0 |
| Arquitectura | 8 | 0 | 0 |
| PostgreSQL 18.7 real | 904 | 0 | 0 |
| Total | 3576 | 0 | 0 |

Restore, build, SDK 10.0.402, NuGet Audit, formato y diagnóstico saneado PASS. Resumen literal: `CODE_AND_POSTGRESQL_GATE_PASSED (not production acceptance)`.

Suites relevantes: RecoverableOperationPostgreSqlTests 16/16; CashTransferRecoverabilityTests 3/3; InterruptedProductImportMigrationTests 1/1; InterruptedProductImportTests 8/8; ElectronicDocumentPersistenceTests 22/22; PerformanceHarnessPostgreSqlTests 3/3; RecoveryPostgreSqlTests 20/20. No repetir E0 salvo fallo nuevo reproducible. Este PASS pertenece exclusivamente al SHA indicado; no se hereda por refactorización. El harness PASS tampoco demuestra capacidad, latencia objetivo ni RPO/RTO productivos.

## 3. Preparación y reglas de ejecución

1. Leer AGENTS.md, frontend/AGENTS.md y apartados pertinentes de CURRENT_STATE, SPEC, ARCHITECTURE, ADR, BACKEND_PLAN, A360-LEAN y SUNAT_INTEGRATION_PLAN. No leer todos los archivos del repositorio ni generar otro diagnóstico global. Comprobar HEAD, rama, `git status --short`, diff y último CI disponible. Si HEAD difiere, identificar cambios y evidencia; no asumir E0 válido para ellos.
2. Preservar cambios ajenos. Usar git/rg y navegación de símbolos. GitNexus solo si su licencia permite este uso comercial y su índice es útil; si no, herramientas nativas. No reinstalar ni reindexar por rutina.
3. Ejecutar E1, E2, E3, E4 y E5 en ese orden, con 4–5 objetivos por fase y checkpoint breve. Continuar automáticamente cuando la tarea siguiente no dependa de una aprobación o medición ausente. No detener la sesión únicamente por terminar una fase. Si un bloqueo afecta un camino, aislarlo, no modificar a ciegas y continuar los objetivos independientes. E5 se limita a lo verificable y preparable sin accesos o autorizaciones faltantes.
4. Para cada cambio: evidencia inicial → decisión mínima → parche → prueba focalizada → comparación de resultados. No cambiar simultáneamente múltiples invariantes de dinero/stock para dificultar el diagnóstico. No crear infraestructura ni abstracciones nuevas por anticipación.
5. No ejecutar S4, reset, truncado, purga, migración, restore o carga sobre DEV, LAB o producción. No modificar las migraciones 1–40, sus SQL/hashes, RLS, GRANT, triggers inmutables, OAuth, secretos, roles ni datos existentes. Migración 41, índice nuevo, cambios de privilegios o contratos incompatibles requieren decisión explícita. No instalar herramientas, cambiar dependencias ni credenciales sin autorización. No commit, push, merge, tag, deploy ni inicio de workflow remoto. Solo preparar candidato revisable.
6. Host conocido sin Docker: comprobar disponibilidad una vez, no instalarlo. Pruebas PostgreSQL y benchmarks exclusivamente sobre infraestructura efímera aislada ya autorizada, con datos sintéticos, roles runtime restringidos y destino comprobado. Si no existe, preparar comandos y un procedimiento manual de CI, sin ejecutarlo remotamente. No autorizar implícitamente una nueva base persistente. Se puede preparar un job manual de benchmark separado y deshabilitado por defecto si el harness no tiene vía existente: justificar necesidad, permisos mínimos, presupuesto, límites, limpieza solo de recursos propios y no iniciarlo. No cambiar el gate integral para omitir comprobaciones.
7. Mantener un solo registro de avance en `docs/CURRENT_STATE.md`, sin crear informes repetidos. Guardar artefactos de benchmark/TRX/logs saneados fuera del repo o en artefactos CI autorizados. Actualizar también el plan SUNAT existente en E4. Registrar SHA base, diff/estado y SHA probado; no decir que el árbol local es un commit publicado. Checkpoints con estado `IMPLEMENTADO`, `PASS_LOCAL`, `PASS_PG`, `MEDIDO`, `PENDIENTE_ACEPTACION`, `BLOQUEADO` o `SIN_CAMBIO_JUSTIFICADO`. No confundir descubrimiento, simulación y ejecución real.

## 4. E1 — Eficiencia SQL y transacciones

### E1.1 Baseline y medición reutilizable

Usar el proyecto `backend/tests/MediPOS.Performance`, sus pruebas, B8.2 y la instrumentación existente `MediPOS.Performance.Sale`. Revisar `DisposablePerformanceDatabase`, `PerformanceDataset`, `PerformanceOptions`, `PerformanceRunner`, `PerformanceObservation` y el contrato de conexión del harness. No construir otro framework. Ejecutar baseline y comparación sobre mismo SDK, digest PG, hardware, semilla, datos, concurrencia, warmup y número de muestras. Configuración inicial orientativa: 2 tenants, 3 sedes, 2000 productos, concurrencia 1/2/4/8, más escenarios de líneas/lotes y jornadas grandes que el harness soporte. Registrar p50/p95/p99, throughput, consultas SQL por operación, tiempo de transacción/locks, memoria/CPU/pool y errores (esperados e inesperados). Diferenciar latencia de handler, HTTP y navegador. No afirmar SLA de producción a partir de CI compartido. Usar EXPLAIN seguro en destino sintético; EXPLAIN ANALYZE de escritura puede ejecutar la operación.

Si no hay PostgreSQL autorizado, conservar el plan y comandos exactos de comparación base/candidato (incluyendo preparación, ejecución y extracción del resultado), no fabricar latencias. Se permiten mejoras pequeñas con equivalencia comprobable y reducción de llamadas demostrada por código/pruebas, identificándolas como hipótesis de rendimiento pendientes de benchmark. Cambios de SQL o locks de alto riesgo sin evidencia real quedan diferidos; no frenar E2–E4 independientes.

### E1.2 Compras: evitar lecturas por producto

En `ConfirmPurchaseHandler`, revisar `IBusinessProductStore` y consumidores. Evaluar lectura batch por IDs distintos, limitada a tenant, con todos los productos necesarios y validación idéntica de ausencia, estado y tipos. Conservar orden determinista, autorización inicial y revalidación tras espera, revisiones, sede/proveedor, candados, lotes, movimientos y auditoría. No hacer consultas paralelas sobre un DbContext ni cargar catálogos completos. Probar 1, 10 y múltiples líneas, duplicados, producto ausente/inactivo/ajeno y rollback. Medir consultas por fase antes/después.

### E1.3 POS: reducir rondas de acceso y productos sin alterar FEFO

En `SaleConfirmationPosting` y `SaleCheckoutTransaction`, evaluar lectura de productos agrupada en cada una de las fases preflight/recheck; no convertirlas en una sola lectura anterior a los bloqueos. Revisar si las consultas de existencia de efectos (pagos, comisiones, stock, auditoría) pueden consolidarse sin perder detección de inconsistencias. Mantener locks en orden, fecha local, versión del borrador, caja abierta, autorización final, snapshot de comisión y cantidades/decimales exactos. Conservar el posting compartido online/offline. Probar concurrencia última unidad, venta/cierre, reintento, revocación, cambio de precio/producto y anulación; una única contabilización.

### E1.4 Caja, stock, búsqueda y reportes

En `CashSessionReconciliationReader` y lectores críticos, medir materialización de ventas/pagos/reversos/traslados y crecimiento de la jornada. Evaluar proyecciones acotadas, agregaciones SQL y paginación real preservando errores de integridad, totales, signos, redondeos, scopes y snapshot coherente. Reutilizar índices; no añadir caché de saldo ni segunda fuente de verdad. Revisar planes de búsqueda y límites, sin cambiar equivalencias de medicamentos ni disponibilidad por sede. Si se necesita índice nuevo, registrar plan y propuesta exacta para autorización, no crear migración.

### E1.5 Gate de eficiencia

Comparar escenario, consultas, p95/p99, errores, resultado e invariantes. Aplicar las metas B8.2; búsqueda p95 200 ms, venta 500 ms y cierre 2 s son metas orientativas sujetas al entorno, no cifras actuales ni requisitos sustitutos. Conservar cambios útiles demostrados, retirar únicamente los cambios propios que empeoran corrección o rendimiento. Sin medición real, estado `PENDIENTE_BENCHMARK`, nunca E1 aprobado integralmente.

## 5. E2 — Normalización de lógica y contratos

### E2.1 Autorización y acceso

`OperationalAccessReader` ya resuelve acceso con una sentencia. Mantenerlo. Clasificar verificaciones por frontera de confianza, tiempo, lock y cambio de contexto. Consolidar solo duplicados del mismo significado y snapshot demostrable. No cachear permisos/licencia/horario a través de esperas. No ampliar roles ni sustituir RLS por filtros de aplicación.

### E2.2 Operaciones recuperables

Revisar `RecoverableOperations`, `RecoverableWriteFilter`, hooks de compras, caja, POS, transferencias y Excel. Reutilizar únicamente mecanismos comunes comprobados de intención, acuse, error incierto y recuperación. Una operación = una identidad durable; payload igual no significa misma intención. Conservar claves, actor, membresía, tenant, sede y hash, autorización en replay, compatibilidad con clientes legados, commit único para dinero/stock/auditoría/constancia, y transacciones por fila de Excel. GET 404 no demuestra rollback. No generalizar los candados monetarios ni trasladar la complejidad a un framework universal. Verificar recarga, dos pestañas, sesión cambiada, respuesta perdida y reintento.

### E2.3 Dominio y persistencia

Buscar duplicaciones reales de unidades, precio, stock, lotes, comisión, cálculos de ledger, validaciones y snapshots. Unificar código solo si las reglas son equivalentes y se reduce mantenimiento. Mantener aritmética decimal, invariantes, historial y separación de módulos. No eliminar guardas redundantes por apariencia cuando protejan una frontera distinta. No sustituir código explícito por abstracciones genéricas innecesarias. No introducir microservicios, Redis, brokers, mediator/CQRS adicionales o repositorio genérico.

### E2.4 Contratos HTTP y frontend

Medir GET/POST, invalidaciones, lectura posterior y bytes por flujo. Usar acuse existente cuando pruebe el estado terminal; diferenciar confirmación y fallo de refresco. Consolidar lecturas solo con equivalencia y consumidores revisados; conservar rutas legacy activas y comportamiento compatible. No prometer un ticket fiscal ni omitir recuperación ante incertidumbre. Simplificar helpers existentes sin duplicar estados ni guardar credenciales en el cliente.

### E2.5 Gate de normalización

Por caso de uso registrar antes/después de HTTP, SQL, commits, responsabilidades y estados; qué redundancia se retiró y qué protección permanece. Añadir tests de equivalencia y autorización; no perseguir porcentaje de reducción de líneas. Marcar `SIN_CAMBIO_JUSTIFICADO` donde el diseño actual ya sea adecuado. No retirar código solo por ausencia de referencias en un grafo.

## 6. E3 — Flujos comerciales y UX sencilla

### E3.1 POS y caja

Recorrido feliz: buscar/escanear → cobrar → ticket interno; apertura: monto inicial → abrir; cierre: contado → confirmar → resultado. Evitar modales, campos, relecturas y acciones duplicadas; teclado/lector y foco correcto. El sistema calcula stock, total, comisiones y auditoría, no el operario. Mantener confirmación de dinero cuando aporta control, pagos mixtos y cierre seguro. Las recuperaciones excepcionales no deben ser pasos obligatorios del recorrido feliz.

### E3.2 Compras y catálogo

Proveedor/documento → líneas/presentaciones → confirmar. Reducir formularios repetidos y campos opcionales obligatorios; permitir consulta clara de unidades/precio/stock. Mantener borrador recuperable, revision y stock solo tras confirmación. No confundir importar catálogo Excel con recibir compras.

### E3.3 Transferencias, licencias y personal

Simplificar navegación, estados, acciones por rol y formularios; conservar solicitud, aprobación, despacho y recepción cuando separen custodia/responsabilidad; saldo y sede origen en sencillo. Alta Trial y reclamo Google verificable, suspensión/reactivación, sedes, turnos y revocación. No inventar permisos ni fusionar pasos con distinta responsabilidad.

### E3.4 Excel y PWA

Cargar/confirmar/revisar resultados; nunca reenviar automáticamente por timeout. Mantener recuperación auditada y `ProductImportRecovery__EnabledAfterWriterDrain=false` hasta retiro comprobado de escritores/lectores antiguos. Probar en dos pestañas, evento único, reintento y cambio de identidad; no aceptar un acuse terminal distinto. Reducir fricción sin ocultar conflictos. Revisar desktop/móvil, navegación por teclado, errores, accesibilidad y A20.

### E3.5 Aceptación operativa

Si existe navegador y entorno aislado autorizado con API/React compatibles y esquema 40, ejecutar flujos con perfiles A Superadmin, B Owner botica 1, C Owner botica 2 y personal autorizado, sin solicitar ni exponer credenciales. Verificar B/C aislamiento, roles, sesión revocada, alta/reclamo real, venta/compra/caja/stock/transferencias y offline; respuesta perdida sin duplicados. Registrar clics, campos, requests, duración y resultados antes/después en condiciones comparables. Si no hay entorno o participación del operador, ejecutar Vitest/contratos disponibles, preparar guion exacto y dejar Chrome real pendiente. No conectar DEV ni aplicar migraciones para simular aceptación. No rediseñar UI completa.

## 7. E4 — SUNAT/CPE preparado y desactivado

**Meta:** `FISCAL_ADAPTER_READY_DISABLED`. Reutilizar B7.2; no emitir comprobantes reales, no contratar proveedor. El objetivo de configurar y activar solo es realista DESPUÉS de implementar y certificar el adaptador específico. Una API REST comercial y SUNAT directo pueden exigir contratos distintos, firma, formatos, consultas y habilitación. No inventar un conector universal.

### E4.1 Inventario y brechas reales

Revisar `ElectronicDocuments`, `ElectronicDocumentPreparation`, `RequestElectronicDocumentHandler`, `ElectronicDocumentDelivery`, `RunElectronicDocumentDelivery`, `ElectronicDeliveryWorkStore`, `ElectronicDocumentStore`, entidades Document/Series/Attempt, `ElectronicDocumentAccess`, DI, tests y `docs/SUNAT_INTEGRATION_PLAN.md`. Ya existen snapshots, correlativos, cola persistente, intentos, `IElectronicDocumentProvider.SubmitAsync/QueryAsync`, capabilities, identidad técnica y verificador de evidencia. No duplicar módulos/tablas/outbox. Verificar si QueryAsync tiene consumidor de conciliación real; existencia de la interfaz no demuestra implementación completa.

### E4.2 Contrato interno estable y selección por emisor

Validar que un adaptador futuro reciba contexto no ambiguo tenant + entidad legal/RUC + documento + ambiente y un snapshot versionado inmutable. Revisar `preparation_v1`: emisor, receptor, fecha, moneda, tipo/serie/correlativo, ítems, unidades, descuentos, tributos y totales; no inventar datos ni reglas fiscales. No modificar snapshots históricos. Identificar brechas concretas por tipo de comprobante y proveedor. Preparar configuración tipada y fail-closed, selección de adaptador por emisor, ambiente, endpoints aprobados, timeout/límites y referencias a secretos/certificados; secretos fuera de React, logs y datos comerciales. Reutilizar DI/HttpClient si aplica. No permitir URLs arbitrarias de clientes (SSRF). Si hace falta persistencia nueva, infraestructura o migración, entregar diseño exacto y dejar bloqueado ese punto; no inventar un proveedor operativo.

### E4.3 Entrega y conciliación desacopladas

Venta, caja, stock y comisiones se confirman independientemente de CPE. Solicitud fiscal explícita cuando se habilite, fuera de transacción comercial; envío externo también fuera del claim SQL. Conservar leases, fencing, intentos, idempotencia local y evidencia. Clasificar recibido por tercero, aceptado por autoridad con evidencia verificada, rechazado, no enviado e incierto. HTTP 200 no es aceptación SUNAT. Timeout tras envío: consultar/reconciliar si el proveedor lo permite; nunca reenvío ciego. Preparar frontera de resguardo XML firmado, respuestas/CDR aplicables y referencias comprobables, sin prometer que ya existen. Anulación comercial no genera automáticamente baja o nota fiscal. Si alguna capacidad requiere un proveedor concreto, declararla pendiente de adaptador.

### E4.4 Pruebas de contrato y apagado

Conservar `DisabledElectronicDocumentProvider`, `DisabledElectronicDeliveryIdentity` y `DisabledElectronicEvidenceVerifier` en composición normal. Sin credenciales, llamadas de red, worker fiscal activo ni consumo de numeración por uso normal del POS. Tests con fake solo en pruebas: deshabilitado, ausencia de credenciales, emisor/tenant incorrecto, timeout, duplicado, estado incierto, evidencia inválida, resultado simulado nunca oficial, caída externa sin impacto comercial. Reutilizar 22 regresiones PG y tests de contrato existentes. No habilitar proveedor simulado en runtime. SIRE e importación XML UBL de compras son alcances distintos.

### E4.5 Kit de conexión futuro

Actualizar `docs/SUNAT_INTEGRATION_PLAN.md` con: diagrama de flujo, contrato exacto de extensión, punto de registro DI, plantilla de configuración sin secretos, selección por emisor, guía de implementación del adaptador, pruebas de conformidad, apagado seguro y operación. Checklist pendiente por proveedor: especificación vigente, identificación y tributación, XML/UBL y firma cuando corresponda, envío/consulta, resguardo XML/CDR, notas/bajas/resúmenes según necesidad, homologación/sandbox y habilitación aplicable, revisión tributaria y piloto. No hacer llamadas a SUNAT ni aprobar emisión. El ticket interno sigue no fiscal. Solo declarar `FISCAL_ADAPTER_READY_DISABLED` si la frontera, configuración y pruebas internas cumplen; si hay brechas, usar `FISCAL_PREPARATION_PENDING` con faltantes. `FISCAL_SANDBOX_VERIFIED` y `FISCAL_LIVE_APPROVED` quedan fuera de alcance. No afirmar que cualquier URL/token basta para integrar un proveedor no elegido.

## 8. E5 — Candidato comercial, operación y validación final

### E5.1 Gate técnico final

Verificar tests focalizados durante fases; al final ejecutar builds backend/frontend, ESLint, tests unitarios y de arquitectura pertinentes, formato .NET y `git diff --check`. Mantener gate integral `.github/workflows/backend-validation.yml` con SDK 10.0.402, auditoría NuGet, SHA exacto, PostgreSQL 18.x, todas las suites sin FAIL ni SKIP. Preparar gate frontend completo usando scripts existentes, sin omitir pruebas. Si no hay runner autorizado, dejar comandos y pasos de publicación/ejecución manual. Comparar inventario de casos con los 3576 base y justificar cambios, no fijar un conteo artificial. No heredar PASS del SHA base al código nuevo.

### E5.2 Rendimiento y aceptación final

Repetir benchmarks comparables del candidato, verificar ausencia de regresiones y aplicar metas acordadas B8.2. Completar Chrome A/B/C, PWA y recorridos comerciales sobre el artefacto exacto cuando el operador autorice. Si faltan métricas, cuentas, infraestructura o participación humana, preparar checklist y marcar pendiente. No confundir CI backend con pruebas de carga o E2E.

### E5.3 Recuperación y rollback

Reutilizar herramientas de Recovery existentes. Revisar backup/restore y pruebas de integridad, preparar ensayo aislado autorizado con datos sintéticos, RPO/RTO medidos y alcance/retención definidos. Las 20 pruebas de Recovery PASS de E0 son evidencia de integración, no restauración operativa en destino real. Verificar compatibilidad API/React y esquema 40; después de registrar `catalog.product_import_interrupted`, no bajar a 39 ni usar lectores incompatibles. Preparar rollback de aplicación compatible o procedimiento de recuperación aprobado, sin borrar auditoría. S4 permanece NO-GO independiente; no es requisito para aceptación con datos controlados.

### E5.4 Seguridad y despliegue

Checklist verificable de HTTPS, secretos por entorno, roles mínimos, readiness/health, alertas, saturación del pool, errores/latencia, copias, privacidad, auditoría, soporte e incidentes. Comprobar dependencias y configuración fiscal apagada. Confirmar el alcance del ticket interno y elegibilidad tributaria del cliente con responsable competente antes del piloto; no afirmar cumplimiento fiscal universal. Registrar requisitos de infraestructura y valores medidos, no inventar SLA o hardware definitivo. Preparar orden de instalación conjunta API/React y drenaje de versiones antiguas. No tocar producción.

### E5.5 Entrega y criterios de decisión

Preparar candidato local revisable, relación de cambios y checklist de publicación. Proponer PR y validación sobre el SHA resultante, sin crearlos ni desplegar. `GO_PARA_PUBLICAR_CANDIDATO_DE_PRUEBAS` significa que el código supera gates locales y no tiene defecto crítico conocido; no significa producción. `GO_PARA_PILOTO` requiere CI final exacto, aceptación real, rendimiento suficiente, recuperación/rollback y operación verificados, alcance legal aprobado y autorización del operador. Si falta cualquiera, `NO_GO_PARA_PRODUCCION` con causa concreta, responsable y siguiente acción. No ampliar el piloto ni etiquetar v0.1.0-rc.1 sin aprobación.

## 9. Formato de entrega y disciplina de recursos

- Mantener un checkpoint por fase en CURRENT_STATE, con resultados y pendientes. Continuar fases independientes sin volver a pedir el prompt. Si el contexto se agota, dejar el último checkpoint y comandos exactos de continuación; no afirmar que se ejecutará trabajo en segundo plano.
- No repetir las 3576 pruebas tras cada parche; usar filtros que ejecuten casos reales, verificar alcance, ejecutar una vez el gate integral del candidato publicado. Si una fase no necesita cambio, demostrarlo y pasar a la siguiente.
- No producir refactors cosméticos ni cambiar más archivos que los necesarios. Separar en el diff los cambios de eficiencia, reglas, UX y preparación fiscal; conservar las regresiones existentes y explicar cualquier actualización de contrato/test. No eliminar/omitir tests para conseguir PASS. Nunca ocultar fallos intermitentes.
- Entrega final compacta (máximo 45 líneas, más referencias a evidencia): tabla E0–E5 con estado, cambios principales, métricas antes/después, PASS/FAIL/SKIP por tipo, bloqueos y próximos gates. Incluir archivos modificados, compatibilidad, riesgos, SHA base y estado real del árbol. Mostrar explícitamente si hubo cero emisiones fiscales reales y cero modificaciones de bases existentes. Resumir qué falta para conectar un proveedor fiscal sin cambiar el núcleo comercial.
- En caso de bloqueos externos, terminar todo lo independiente; no abrir otra ronda de auditoría. La ejecución concluye cuando las mejoras autorizadas están implementadas o justificadamente diferidas y el candidato/checklist está preparado. La validación externa y el piloto requieren intervención del operador.

**Instrucción final a Codex:** comienza ahora. E0 ya pasó para la base acreditada. Recorre E1–E5 dentro de este encargo, con checkpoints internos, aplica las mejoras seguras verificables, conserva todos los controles y entrega una sola conclusión. No publiques ni despliegues automáticamente.
