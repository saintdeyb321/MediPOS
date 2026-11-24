# MediPOS — A360-LEAN | Refinamiento y preparación comercial

**Plan de ejecución por lotes · 23 de noviembre de 2026 (Perú)**  
**Rama:** `ci/postgres-diagnostics-8bc6b4c` · **Base observada:** `db1af8f27b89f9f7ee52f6e7badc98d4da8d56d2`  
**Decisión:** NO-GO para producción. GO para corregir el gate actual. Mantener monolito modular, .NET 10, PostgreSQL 18, React, RLS y el modelo de operaciones recuperables. El objetivo es hacer menos trabajo innecesario por operación y reducir la fricción de usuario, sin sacrificar integridad.

## 1. Evidencia de partida y alcance

- [CI run 45312440841](https://github.com/saintdeyb321/MediPOS/actions/runs/45312440841): SDK fijado, restore, build, NuGet Audit, arquitectura y contenedores PASS. Formato .NET FAIL. Unitarias: 2655/2664 PASS, 9 FAIL. PostgreSQL 18.7: 866/904 PASS, 38 FAIL. Arquitectura: 8/8 PASS. Total: 3529 PASS, 47 FAIL, 0 SKIP. Gate NOT_APPROVED. No es una estimación.
- `RecoverableOperationPostgreSqlTests` 16/16; `CashTransferRecoverabilityTests` 2/3; `InterruptedProductImportMigrationTests` 1/1; `InterruptedProductImportTests` 7/8; `ElectronicDocumentPersistenceTests` 22/22. Las suites de concurrencia de venta, caja y transferencias listadas en CI pasaron, pero existen otros fallos de aislamiento y seguridad. No generalizar PASS a un módulo completo.
- El código ya contiene `backend/tests/MediPOS.Performance`, `PerformanceHarnessPostgreSqlTests`, instrumentación `MediPOS.Performance.Sale` y un lector de acceso de una sola sentencia SQL. Reutilizarlos. No crear otro framework de benchmarks.
- El módulo `ElectronicDocuments` ya existe (B7.2), con snapshot, series, correlativos, cola persistente, intentos, proveedor abstracto, identidad técnica y verificador de evidencia. `DisabledElectronicDocumentProvider`, `DisabledElectronicDeliveryIdentity` y `DisabledElectronicEvidenceVerifier` son barreras deliberadas. B7.3 sigue diferido. `docs/SUNAT_INTEGRATION_PLAN.md` es la referencia de alcance; el usuario solicita preparar la integración futura, no activar emisión fiscal ahora.

Fuentes del repositorio: `docs/CURRENT_STATE.md`, `docs/SPEC.md`, `docs/ARCHITECTURE.md`, `docs/ADR.md`, `docs/BACKEND_PLAN.md`, `docs/AUDIT_FUNCTIONAL_360.md`, `docs/MEDIPOS_A360_LOTE4_GATES_CIERRE.md`, `docs/SUNAT_INTEGRATION_PLAN.md`, código en el SHA indicado y log saneado de CI. Si HEAD cambia, verificar nuevamente. Este plan no sustituye SPEC ni las reglas locales.

## 2. Orden de trabajo

| Lote | Resultado esperado | Dependencia |
|---|---|---|
| E0 | Gate de estabilidad verde: corregir 47 fallos y formato | Ahora |
| E1 | Baseline medido y eficiencia SQL en caminos críticos | E0 verde |
| E2 | Lógica y contratos más simples, con garantías equivalentes | E1 con mediciones |
| E3 | Flujos comerciales simples en Chrome y PWA | E0 verde; candidato E1/E2 |
| E4 | CPE/SUNAT preparado, desacoplado y apagado | Revisión independiente; puede analizarse en paralelo, implementar tras E0 |
| E5 | Gate del candidato, recuperación y piloto comercial | E0–E4 aceptados en su alcance |

No ejecutar todos los lotes de una vez. Trabajar en grupos de 4–5 objetivos, con revisión y un resultado verificable al terminar cada grupo. No perseguir refactors cosméticos ni optimizaciones sin impacto.

## 3. E0 — Estabilización del candidato real (cinco grupos)

Usar el run 45312440841 y sus logs saneados como lista de trabajo, no volver a buscar defectos generales. Las agrupaciones siguientes suman todos los fallos observados; **las causas aún son hipótesis** hasta contrastar código, stack y reproducción.

| Grupo | Suite/cantidad | Trabajo |
|---|---|---|
| E0.1 | Unitarias: 4 ExpirationScheduler, 1 SecurityComposition, 1 ElectronicDeliveryContract, 1 TenantCatalogReportingHttp | Revisar configuración del host/DI, entorno de CI y dobles de importación. Determinar causa exacta; no habilitar CPE ni schedulers para hacer pasar tests. |
| E0.2 | Unitarias: 1 RecoveryMigrationIntegrity, 1 TenantCommercialContract. PostgreSQL: 10 BranchesPersistence, 4 TenancyLicensing, 2 RuntimeReadiness | Contrastar contratos, inventario de rutas, esquema 40, fixtures y admisión actual. Actualizar expectativas de pruebas solo cuando SPEC y cambios aprobados justifiquen el nuevo contrato. Conservar el prefijo histórico 1–39 y verificar seguridad. |
| E0.3 | PostgreSQL: 15 RecoveryPostgreSql, 3 PerformanceHarness | El log muestra SQLSTATE 42501 en casos que pasan por `PerformanceDataset` / `CreateMembershipHandler`. Determinar SQL y permiso exactos; revisar la preparación sintética y rol efectivo. No ampliar privilegios runtime para acomodar un seed de pruebas. Mantener aislamiento y pruebas negativas. |
| E0.4 | PostgreSQL: 1 CashTransferRecoverability, 1 InterruptedProductImport, 1 SaleVoidIsolation, 1 SecurityHardening | Revisar la prueba `FOR UPDATE NOWAIT` y SQLSTATE 55P03 (el bloqueo puede ser esperado, pero la aserción falla); recuperación repetida y ámbito del candado/fixture; reutilización física y RLS; `ImportedRowRollsBackWhenAuthorityIsRevokedBeforeItsCommit` ahora debe ejercer el protocolo de candado sin saltar la comprobación de revocación. No asumir que son solo defectos de test. |
| E0.5 | Formato .NET y gate final | Revisar errores WHITESPACE y demás diagnósticos de `dotnet format --verify-no-changes`; aplicar formato focalizado, separar cambios mecánicos de semánticos. No editar migraciones históricas. Reejecutar pruebas afectadas y validar el gate completo mediante CI para el SHA nuevo. |

**Método:** comprobar HEAD y diff, consultar GitNexus solo para rutas complejas (índice puede estar atrasado), usar los logs completos saneados del run para identificar todos los métodos fallidos. Separar defecto de producto, test obsoleto y entorno. Si se repite un fallo, registrar evidencia original y posterior. Si falta una excepción concreta, ampliar diagnósticos seguros sin exponer secretos. No cambiar el pin del SDK ni saltar `dotnet format`, auditoría NuGet, pruebas de arquitectura o integración. Mantener la selección estricta del SDK 10.0.402 en CI salvo nueva decisión explícita.

**Aceptación E0:** todos los fallos tienen causa demostrada y corrección verificada; nuevo workflow integral con SHA exacto, PostgreSQL 18.x real, todas las suites PASS, 0 FAIL y 0 SKIP, `CODE_AND_POSTGRESQL_GATE_PASSED (not production acceptance)`. No exigir 904 como número fijo si se añaden regresiones legítimas: comparar inventario de pruebas y justificar la variación. No instalar Docker local; usar GitHub Actions existente previa publicación y autorización del operador. Conservar 40 como esquema candidato; prohibido alterar DEV, S4, RLS, GRANT o migraciones 1–40. Si se necesita una decisión de esquema/seguridad, bloquear solo el subgrupo y continuar los demás.

## 4. E1 — Medir y reducir costo real del backend

### E1.1 Baseline reproducible

Usar `MediPOS.Performance` y B8.2 en BACKEND_PLAN. Primero leer `DisposablePerformanceDatabase`, `PerformanceOptions`, `PerformanceDataset`, `PerformanceRunner`, `PerformanceObservation` y sus tests. La fixture de integración y el banco de rendimiento no son intercambiables: comprobar el destino exacto, aislamiento, autorización y ciclo de vida antes de ejecutar. No usar medipos_dev ni datos reales. Si requiere un runner dedicado, presentar comando y destino; no crear entornos automáticamente. Medir con el mismo SHA, SDK, versión/digest PG, recursos, semilla, warmup, dataset y concurrencia para comparación A/B. Conservar los límites existentes y un escenario de 2 tenants, 3 sedes, 2000 productos y concurrencias 1/2/4/8 como punto de partida si el harness lo permite; variar catálogo y líneas conforme al uso esperado.

Registrar p50/p95/p99, throughput, errores previstos e imprevistos, consultas SQL por operación, tiempo SQL/lock/transacción, conexiones, CPU y memoria; declarar qué métricas pueden observarse y cuáles faltan. Separar tiempo del handler del tiempo HTTP y navegador. No usar una corrida en CI compartido como SLA de producción. `EXPLAIN (ANALYZE, BUFFERS)` exclusivamente sobre datos sintéticos en consultas seguras; escrituras solo en fixture autorizada. No registrar SQL con datos/secretos. Sin medición real, etiquetar las oportunidades como HIPÓTESIS, no como mejoras verificadas.

### E1.2 Compra

`ConfirmPurchaseHandler` contiene lecturas de acceso en varias fases y una consulta por producto distinto. Evaluar lectura batch de productos (mismo tenant, validación de todos los IDs, activos/tipos), reduciendo viajes SQL sin alterar revisiones de compra, locks, comprobaciones de sede/proveedor, stock, lotes o auditoría. Revalidar después de esperas cuando corresponda; no cachear autorización para toda una transacción.

### E1.3 Venta

`SaleConfirmationPosting` agrupa líneas por producto, pero consulta productos antes y después de adquirir los locks de lotes. Evaluar lectura agrupada por fase, manteniendo revalidación posterior a espera, fecha local y orden de bloqueo FEFO/FIFO vigente. `SaleCheckoutTransaction` consulta existencia de pagos, comisiones, movimientos y auditoría por separado; evaluar consolidación en SQL manteniendo la misma detección de historial inconsistente. No suprimir validación porque la operación use idempotencia. Preservar exactitud decimal, versión de borrador, snapshots, reserva concurrente y posting compartido online/offline.

### E1.4 Caja, reportes y búsquedas

`CashSessionReconciliationReader` materializa ventas, pagos, reversos y traslados. Medir crecimiento con jornadas grandes; evaluar agregación SQL y proyecciones mínimas conservando reconciliación exacta y detección de inconsistencias. Revisar consultas de stock y búsqueda, índices y paginación con planes reales. No introducir saldos cacheados ni nueva fuente de verdad. No crear índices/migración 41 sin evidencia y autorización.

**Aceptación E1:** tabla antes/después por escenario, reducción demostrada de consultas o latencia sin empeorar concurrencia, resultados, memoria ni seguridad. Usar objetivos orientativos p95 de búsqueda ≤200 ms, confirmación de venta ≤500 ms y cierre ≤2 s solo después de definir el entorno; prevalecen criterios B8.2 aprobados y medición reproducible. Si no se logra mejora medible, conservar el diseño anterior. Pruebas focalizadas de equivalencia y gate real en SHA final.

## 5. E2 — Normalización de reglas y contratos

1. **Autorización:** `OperationalAccessReader` ya ofrece snapshot de una sentencia; mantener. Mapear validaciones antes/después de locks; consolidar solo llamadas cuya equivalencia temporal y autorización esté demostrada. No ocultar acceso denegado en frontend ni eliminar revalidaciones finales.
2. **Operaciones recuperables:** comparar compras, mercadería, sencillo, caja, POS y Excel; reutilizar semántica común de intención, acuse y lectura sin crear otro coordinador. Mantener particularidades de cada transacción, scope, locks y permisos. Una intención no equivale a igualdad de payload entre dos operaciones legítimas. GET 404 no prueba rollback.
3. **Reglas de dominio:** identificar cálculos o validaciones duplicados de unidades, precio, impuestos provisionales, lotes, comisiones y ledger. Unificar solo cuando significado e invariantes coincidan. No fusionar entidades con responsabilidades distintas. Preservar snapshots históricos y precisión decimal; no usar floats monetarios.
4. **API/contratos:** medir secuencias HTTP; eliminar reconsultas solo si el acuse existente contiene evidencia suficiente y fresca. Mantener rutas históricas con consumidores activos, compatibilidad API/React y GET de recuperación cuando haya incertidumbre. No agregar un endpoint por cada clic ni retirar contratos por ausencia en el grafo. Revisar helpers de frontend antes de crear otros.
5. **Arquitectura:** detectar abstracciones sin valor, adapters duplicados y dependencias cruzadas. Mantener módulo y frontera transaccional explícitos; evitar repositorio genérico, mediator o CQRS extra, microservicios, Redis y broker sin necesidad demostrada. Reducir complejidad por caso de uso, no perseguir cantidad de líneas como métrica aislada.

**Aceptación E2:** mapa antes/después de operaciones (HTTP, SQL, commits, responsabilidades), equivalencia demostrada, consumidores preservados y ningún cambio de reglas comerciales sin aprobación. Mantener los tests de A360 y la evidencia E1.

## 6. E3 — Simplificación de flujos comerciales

En Chrome autorizado con perfiles A/B/C y datos controlados, medir tiempo, clics, campos obligatorios, esperas, GET/POST y errores del recorrido exitoso y recuperación. No usar clientes reales. No capturar PII ni secretos. No ejecutar S4 para limpiar datos. Confirmar instalación conjunta API/React y migración 40 con autorización separada antes de probar si el destino la requiere.

| Flujo | Experiencia objetivo | Invariantes |
|---|---|---|
| POS | Buscar/escaneo → cobrar → ticket interno | Un solo cobro, stock exacto, caja y auditoría coherentes; teclado y lector |
| Compras | Proveedor/documento → líneas → confirmar | Borrador recuperable; stock solo al confirmar |
| Caja | Apertura con monto; cierre con contado y resultado | Ledger, permisos, no repetir cierre; arqueo separado |
| Transferencias | Solicitud → aprobación/despacho → recepción según rol | No colapsar pasos que separan custodia y responsabilidad |
| Alta, licencias, personal | Acciones claras por rol y estado | Reclamo Gmail, aislamiento, revocación y licencia efectiva |
| Excel / PWA | Cargar/confirmar/revisar; recuperación explícita cuando sea necesaria | No reenviar a ciegas; preservar resultados; dos pestañas y cambio de identidad |

Quitar confirmaciones redundantes de operaciones reversibles; mantener las de dinero, stock, importación irreversible o cambio de acceso cuando aporten control. Evitar pasos obligatorios para datos opcionales. No convertir la recuperación excepcional en pasos de la ruta normal. Evaluar accesibilidad, teclado, desktop y móvil según A20. Validar errores y estados vacíos/ocupados; no mostrar éxito sin evidencia. No rediseño visual masivo. Registrar B y C aislados, A separado de negocio y pruebas de licencia/rol con sesión abierta.

## 7. E4 — SUNAT/CPE: preparado para integrar, no habilitado

**Resultado requerido: `FISCAL_ADAPTER_READY_DISABLED`**. El core comercial permanece libre de llamadas fiscales. No prometer que cualquier API se conecta únicamente con URL/token: una API REST de tercero y SUNAT directo pueden tener protocolos, credenciales, mapeos, firma, respuestas y reglas distintos. La meta real es que, una vez implementado y validado el adaptador del proveedor elegido, la activación sea por configuración y no por reescritura de POS, caja, inventario ni ventas.

### E4.1 Reutilizar B7.2

Inventariar `ElectronicDocumentPreparation`, `RequestElectronicDocumentHandler`, `ElectronicDocumentDelivery`, `RunElectronicDocumentDelivery`, `ElectronicDeliveryWorkStore`, `ElectronicDocumentStore`, `ElectronicDocument`/`Series`/`Attempt`, `ElectronicDocumentAccess`, DI y tests. Ya existen `IElectronicDocumentProvider.SubmitAsync/QueryAsync`, capabilities, identidad técnica, verificación de evidencia y cola persistente. No duplicar tablas, outbox, motor de reintentos, auditoría o módulos. Confirmar si `QueryAsync` tiene consumidor de conciliación y si la identidad enviada al proveedor distingue suficientemente tenant, entidad emisora, documento y entorno; no afirmar cobertura por la sola existencia de una interfaz.

### E4.2 Contrato de integración y configuración

Definir o completar una frontera estable que permita elegir adaptador por entidad legal emisora (tenant + legalEntity/RUC), no una clave fiscal global. Revisar el snapshot `preparation_v1` existente: identificar datos de emisor, receptor, serie/correlativo, moneda, fecha, ítems, unidades, descuentos, tributos y totales; separar contrato interno de un perfil tributario validado. No cambiar el histórico ni inventar datos de identificación, tasas o redondeos. Identificar huecos que dependan del proveedor y del tipo de comprobante. Mantener la preparación interna no fiscal claramente etiquetada.

Preparar configuración tipada y validación fail-closed para selección de provider, ambiente, endpoint aprobado, límites/timeout y referencias a secretos/certificados por emisor; sin credenciales reales, hosts de clientes arbitrarios, secretos en JSON de negocio/logs ni exposición a React. Reusar DI y `HttpClient` seguro donde proceda; no desarrollar una API genérica que acepte cualquier URL ni un adaptador REST imaginario. Si la selección multi-emisor requiere nuevo almacenamiento o worker, presentar diseño y detener solo ese punto antes de introducir migraciones o infraestructura. Sin proveedor seleccionado, entregar contrato, guía de conexión y test contract, no una falsa integración operativa.

### E4.3 Entrega, evidencia y conciliación

La venta se confirma localmente. La solicitud fiscal (cuando esté habilitada) se gestiona separada, con snapshot inmutable, identidad de documento y correlativo controlados. La llamada externa debe ocurrir fuera de la transacción comercial y de la transacción SQL de claim. Conservar leases, fencing, idempotencia local, auditoría e intentos existentes. Distinguir recibido por intermediario, aceptación oficial verificada, rechazo, no enviado e incierto; HTTP 200 no basta. Timeout posterior al envío nunca habilita reintento ciego; usar consulta/conciliación compatible con las capacidades reales del proveedor y escalamiento cuando no exista. No deducir idempotencia externa de la local. Diseñar contrato para resguardo seguro de XML firmado, respuestas/CDR aplicables y referencias verificables; no afirmar que B7.2 ya conserva todos esos artefactos. No traducir anulación comercial a nota de crédito/baja automática.

### E4.4 Pruebas y estado apagado

Mantener proveedor, identidad y verificador deshabilitados por defecto; ninguna emisión, numeración fiscal consumida por una venta normal, llamada de red, scheduler fiscal activo ni secreto requerido para usar el POS. Probar contrato con fake exclusivamente en tests: envío, clasificación, timeout, duplicados, resultado incierto, evidencia inválida, tenant/emisor cruzado, credenciales ausentes, proveedor deshabilitado y caída del proveedor sin alterar venta/stock/caja. La simulación nunca se vuelve aceptación oficial. No registrar fake como proveedor operativo en DI de producción. Reutilizar las 22 pruebas PostgreSQL de CPE que pasaron en el run base y las pruebas de contrato, corregir el fallo unitario observado en E0. Mantener importación UBL de proveedores y SIRE como alcances separados.

### E4.5 Paquete de activación futura

Actualizar `docs/SUNAT_INTEGRATION_PLAN.md` con esta decisión de producto y una lista de implementación pendiente por proveedor: contrato técnico vigente, mapeo fiscal/validaciones, autenticación y firma (si aplica), envío, consulta, resguardo de artefactos, notas/bajas/resúmenes según alcance, pruebas sandbox y homologación/habilitación aplicable, verificación legal por emisor y piloto. Incluir guía de alta de un adaptador, configuración de ejemplo sin secretos, pruebas reutilizables, procedimiento de apagado y compatibilidad. No desarrollar protocolos SUNAT ni conectores a terceros sin sus especificaciones y aprobación; nunca simular un CDR válido. No activar la emisión fiscal real. No afirmar que el ticket interno es boleta/factura. La venta comercial del SaaS no exime al cliente de sus obligaciones tributarias; validar el uso permitido para cada botica antes del piloto.

**Aceptación E4:** core sin dependencia fiscal activa, contrato/adaptador documentado y comprobado con dobles, estado disabled por defecto, capacidades pendientes declaradas por proveedor; sin modificaciones comerciales para conectar el futuro adaptador. Diferenciar `FISCAL_ADAPTER_READY_DISABLED` (preparación interna) de `FISCAL_SANDBOX_VERIFIED` y `FISCAL_LIVE_APPROVED` (ambos fuera de este alcance). Si B7.2 ya cumple un requisito, no reimplementarlo.

## 8. E5 — Gate de candidato y piloto

- Validar SHA exacto con backend integral PostgreSQL 18, frontend build/lint/tests y aceptación Chrome A/B/C real. Reejecutar benchmarks críticos con la versión final. Identificar resultados y fallos intermitentes. No confundir CI backend con E2E.
- Verificar backup y restauración en destino aislado, RPO/RTO definidos y medidos, rollback de aplicación compatible con esquema 40 y evento nuevo; nunca bajar a 39 borrando historial. Mantener `ProductImportRecovery__EnabledAfterWriterDrain=false` hasta retirar escritores y lectores incompatibles según procedimiento aprobado. S4 para esquema 39 queda fuera: no ejecutar ni actualizar sin autorización propia.
- Preparar HTTPS, secretos, roles mínimos, alertas, health/readiness, latencia, capacidad, auditoría, incidentes, soporte, retención y privacidad. Revisar alcance comercial del ticket no fiscal y obligaciones aplicables con responsable tributario; no activar SUNAT por defecto.
- Piloto controlado en una botica elegible con usuarios autorizados, datos y despliegue aprobados, soporte y criterio de detener/retroceder; ampliar solo con evidencia.

## 9. Reglas generales de ejecución Codex

- Leer AGENTS.md, frontend/AGENTS.md y secciones pertinentes de CURRENT_STATE/SPEC/ADR. Usar GitNexus focalizado y comprobar vigencia. No leer repositorio completo ni reindexar automáticamente. Un lote por sesión; no reabrir trabajo aceptado sin evidencia.
- Preservar cambios locales y contratos. No editar migraciones 1–40, RLS, GRANT, triggers de inmutabilidad, credenciales/OAuth ni datos existentes. No aplicar SQL, S4, restauración real o carga sobre DEV, LAB o producción. Cualquier prueba con escritura requiere destino efímero aprobado. No instalar herramientas ni dependencias sin necesidad y autorización. No commit/push/merge/deploy/workflow remoto automático; operador publica y lanza el gate.
- No debilitar aserciones, omitir pruebas, sustituir PostgreSQL por mocks o fabricar métricas. Una corrección estructural, migración 41, cambio de permisos o contrato incompatible requiere decisión específica; continuar subtareas independientes.
- Actualizar `docs/CURRENT_STATE.md` con estado por objetivo; E4 puede actualizar además el plan SUNAT existente. No crear informes redundantes. Entregar tabla compacta de objetivo, estado, causa/evidencia, archivos, pruebas, métricas antes/después, riesgos y siguiente GO/NO-GO. Distinguir código preparado, probado local, PostgreSQL validado, aceptado en Chrome y listo para producción.

**Próximo encargo: E0, exclusivamente.** El gate fallido es la evidencia prioritaria; no modificar todavía la lógica de eficiencia ni CPE. Analizar SUNAT en E4 sin conexión externa. Concluir E0 con un candidato revisable y una nueva ejecución CI aprobada antes de refactorizar los caminos críticos.
