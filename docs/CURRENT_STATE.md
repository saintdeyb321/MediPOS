# Estado actual de MediPOS

Revisión del 25 de noviembre de 2026. Rama `ci/postgres-diagnostics-8bc6b4c`, base y HEAD local `47e4335b2c46f0de874797724ba30e123705ea79` (RC1 publicado). Los cambios UX-LEAN y el ajuste previo del workflow están en el árbol local. [SPEC](SPEC.md), [ARCHITECTURE](ARCHITECTURE.md) y [ADR](ADR.md) definen el producto y sus límites.

- **CODE_READY** indica que el flujo existe en código y puede pasar a verificación.
- **Probado** exige evidencia de una comprobación concreta con SHA, alcance y entorno registrados.
- **Aceptado** exige que el recorrido real cumpla los criterios del MVP con evidencia del operador o de la prueba de aceptación correspondiente.

## Implementación y aceptación

| Área | Implementado | Aceptación pendiente |
|---|---|---|
| Plataforma e identidad | Google OIDC, admisión y consola independientes, alta idempotente de botica Trial, designación y reclamo del primer Owner por correo Google verificado. | Recorrido actual en Chrome con A como Superadmin y B/C como Owners separados. Verificar denegación cruzada y recuperación de alta incierta. |
| Operación comercial | Catálogo global y local, presentaciones versionadas, compras/lotes/inventario, POS/caja/pagos/ticket, anulación y arqueo. | Recorrido integral desde compra hasta cierre, precisión de importes y cantidades, auditoría y constancias. |
| Gestión | Sucursales, personal e invitaciones, transferencias, reportes, comisiones, notificaciones, importación y contingencia PWA. | Aceptación por rol/sede, pruebas entre dos boticas y simulación de recuperación/offline dentro del alcance. |

El 2 de noviembre se observaron HTTP 200 en React y readiness, 401 en sesión de plataforma anónima y seis hashes de assets coincidentes en DEV. PostgreSQL 18.4 tenía 39 migraciones y 56 tablas FORCE RLS. Son evidencias fechadas, con la disponibilidad de hoy por verificar. El [gate PostgreSQL anterior](https://github.com/saintdeyb321/MediPOS/actions/runs/39147784981) está registrado como PASS para `9f5434549a9c06f61705523069fc13673aad3226`. Falta evidencia integral del SHA candidato actual y aceptación completa de los cambios posteriores.

**Próxima simulación.** Confirmar readiness, abrir DEV con Gmail A en un perfil Chrome y verificar la entrada automática a Plataforma. Si se requieren cero boticas antes de las altas, aprobar primero A de S4 (respaldo privado de esquema 39 y ensayo real con rollback). B (limpieza comercial) requiere el PASS de A y otra aprobación. Después, A designa a B al crear una botica Trial y B reclama acceso iniciando Google en otro perfil. Repetir con C para verificar aislamiento. Registrar resultados por cuenta, operación y SHA, sin datos privados. El [procedimiento S4](MEDIPOS_AUDITORIA_SUPERADMIN_V2_PLAN_CIERRE.md#9-correctivos-acotados-y-procedimiento-s4-preparado-base-e61cf352-2026-10-30) contiene el alcance y los gates.

**Bloqueos del MVP.** Quedan pendientes la evidencia integral del candidato, el cierre de aceptación Chrome/PostgreSQL por roles y boticas y la verificación de recuperación operativa. S4 solo condiciona empezar con datos comerciales vacíos. La preparación de un piloto público exige sus propios gates de despliegue y operación. SUNAT/SIRE y la purga física A18 conservan su alcance diferido.

### A360 · Identidad, permisos y continuidad

- **Lote 1 parcialmente abierto.** Se conserva la evidencia previa de Caja (12/12) y Compras (30/30 frontend y 31/31 .NET). La evidencia pendiente de compras y de las dos transferencias se detalla en L4A.
- **Lote 2 probado localmente.** Licencias distingue el acuse del POST de la lectura posterior y permite recuperar la licencia aunque falle la consulta de Owner. Offline conserva el acuse terminal frente a un `pending` tardío de la misma constancia. En Google, una regresión comprueba que un fallo en la segunda botica interrumpe el callback antes de emitir la identidad local y que el reintento reclama la pendiente una sola vez. Las pruebas de Personal siguen la ficha y los diálogos actuales. Vitest 62/62 y xUnit 201/201, con ESLint focalizado y builds PASS.
- **Pendiente de aceptación.** En perfiles Chrome separados, A designa a B y C en boticas distintas. Cada cuenta verifica su reclamo y la denegación cruzada. Con una sesión comercial abierta, A comprueba suspensión y reactivación. B incorpora a C como trabajador de su botica y comprueba sede, horario y revocación. En un entorno de pruebas autorizado, verificar el mismo evento offline en dos pestañas, pérdida de respuesta y revocación de dispositivo con PostgreSQL 18 real.

### A360 · Lote 3 · Integridad comercial

| ID | Evidencia y estado |
|---|---|
| L3.1 | POS conserva un único POST cuando el servidor confirma y la relectura falla. Verificación de UI y de lógica de dominio. Pendientes las carreras PostgreSQL de doble confirmación, última unidad, cierre, anulación y reversión de comisiones, además del recorrido Chrome. |
| L3.2 | Catálogo retira la evidencia anterior de conciliación y exige configuración versionada vigente para resolver un cambio de presentaciones. Pendientes la concurrencia PostgreSQL y el recorrido Chrome de precio, carrito y snapshots históricos. |
| L3.3 | Importación con clave exige conciliar la constancia original tras recarga. Notificaciones conserva el acuse aunque fallen las lecturas posteriores. Auditoría mantiene autorización Owner, paginación y metadatos; se actualizaron sus pruebas al diálogo actual. Pendientes RLS con dos membresías y recuperación real de cargas parciales. Una interrupción abrupta antes de registrar el fallo puede dejar el trabajo en `processing`. |
| L3.4 | El contador de navegación y la página reutilizan la consulta de sucursal fresca. Prueba de transporte observó un GET en vez de dos al abrir la página; la actualización explícita conserva su GET. Productos Owner consume `GET /products`, el POS usa `/operational-products` y el editor usa `unit-configuration`. El `PUT /units` anterior conserva el no-op de compatibilidad. React crea boticas por `with-owner`; el alta anterior y el bootstrap siguen cubiertos por contratos HTTP. Las lecturas de plataforma separan constancia y directorio. |

Offline rechaza acuses terminales del mismo evento con `receiptId` distinto, aunque coincidan estado y referencia de negocio. Gate focalizado con Vitest 86/86 (componentes, transporte e IndexedDB simulado), xUnit 182/182 (lógica y puertos de aplicación), ESLint y builds PASS. La repetición en serie completó la prueba de anulación que agotó el tiempo durante el build paralelo. PostgreSQL real y aceptación Chrome siguen pendientes.

### A360 · Migración 40 y gate técnico

| Objetivo | Estado y evidencia |
|---|---|
| Compras | **PENDIENTE PostgreSQL.** Cuatro casos descubiertos de `purchase-draft` para replay, rollback, competencia y aislamiento. Ahora contrastan el ID y la sucursal de la constancia con el borrador almacenado, junto con las comprobaciones de no mover stock. |
| Mercadería | **CODE_READY.** La solicitud con clave usa el coordinador existente como único dueño del commit de transferencia, líneas, evento, auditoría y constancia. Autoriza en destino y vincula la clave a actor, membresía, tenant y destino. React consulta la constancia original tras recarga y bloquea nuevas solicitudes mientras no pueda conciliar. Dos regresiones PostgreSQL compiladas para commit, replay, conflicto y rollback. |
| Sencillo | **CODE_READY.** El despacho con clave entra al mismo `MediPosDbContext` y transacción `ReadCommitted` del coordinador. El orden es candado de intención, candado de sede y `FOR UPDATE` en caja origen. `cash_transfers` es la fuente del egreso del ledger; traslado, auditoría y constancia comparten commit. El actor, membresía, tenant, sede y hash de caja/destino/importe resuelven replay o conflicto. Owner/Cashier se revalidan para replay y recuperación. React conserva el UUID hasta consultar la constancia de la sede origen. El despacho sin clave conserva su transacción propia. |
| Importación | **CODE_READY**, PostgreSQL pendiente. Ambos caminos de importación usan exclusión por trabajo. Owner puede cerrar un `processing` mediante una acción explícita habilitada por el servidor. La URL `importPending=1` conserva su consulta de solo lectura. |
| Concurrencia y RLS | **PENDIENTE PostgreSQL.** Set de 70 casos, incluido el smoke que acredita `server_version`, para POS, última unidad, stock, caja, anulación/comisiones, sencillo, offline desde conexiones distintas, interrupción y aislamiento. |

**Cierre de Excel.** `POST /api/tenants/{tenantId}/product-imports/{importJobId}/recover-interrupted` usa la sesión y protecciones Origin/CSRF existentes y requiere Owner vigente. El candado de sesión se obtiene antes de `StartAsync` en ambos caminos. Con clave, se toma primero el candado de intención. La conexión física se mantiene abierta entre transacciones y cada escritura comprueba que su sesión posee el candado. El cierre devuelve 409 ante un escritor activo, relee `processing` con `FOR UPDATE NOWAIT`, recuenta resultados y confirma `Failed` y `catalog.product_import_interrupted` en un solo commit, conservando productos, unidades, filas, actor original y constancia. La auditoría identifica al Owner autenticado. Un estado terminal existente se devuelve intacto. React exige confirmación y reconsulta el mismo trabajo, incluso ante pérdida de acuse.

**Migración 40.** `20261124011936_AddInterruptedProductImportAudit` añade el código de interrupción en `ck_audit_logs_codes`, para `import_job`. Mantiene el evento de importación completada y sus guardas. El descenso comprueba la visibilidad global bajo RLS y rechaza la operación antes de cambiar la restricción si existen eventos nuevos. Los scripts de ambos sentidos se generaron y revisaron offline. El procedimiento S4 de esquema 39 requiere revisión, validación y autorización separadas para esquema 40.

**Orden de despliegue pendiente.** Mantener `ProductImportRecovery__EnabledAfterWriterDrain` ausente o `false`. Aplicar esquema 40 con autorización, desplegar API y React compatibles y comprobar que han finalizado todas las solicitudes y procesos que podían escribir con una versión anterior. Solo entonces habilitar la recuperación. Con la opción apagada, el nuevo API puede leer y escribir en esquema 39 usando los códigos existentes. El React nuevo entiende un GET antiguo sin indicador y oculta la acción. Un backend antiguo puede fallar al leer la nueva auditoría. Tras el primer evento de interrupción, conservar esquema 40 y lectores compatibles al corregir o revertir el resto del despliegue.

GitNexus CLI consultó el índice `ecfb687`, seis commits detrás del HEAD publicado. Se contrastaron los escritores, lectores de auditoría y límites de transacción con el código local.

Evidencia del L4A publicado: 44 Vitest con dobles HTTP, 246 xUnit de lógica y 14 xUnit de contrato HTTP con Kestrel en loopback y servicios de prueba. En cada grupo, 0 FAIL y 0 SKIP. ESLint focalizado, builds frontend/backend y `git diff --check` PASS.

Evidencia de L4A-R publicado: 44 Vitest con dobles HTTP, 161 xUnit de lógica y 20 xUnit de contrato HTTP con Kestrel en loopback y dobles de identidad/persistencia. Cada grupo tuvo 0 FAIL y 0 SKIP. ESLint, builds frontend/backend y `git diff --check` PASS. El despacho recuperable requiere instalar frontend y backend compatibles.

**Preparación local del gate PostgreSQL de migración 40.** El filtro previo al CI descubrió 92 casos únicos y conservó los 83 de L4A-FINAL. Los 9 nuevos cubren exclusión con y sin clave, pérdida de sesión, filas y constancia, concurrencia, repetición, acceso, estado previo, cancelación, pooling, rollback y migración. Desde `backend/`, compilar con `dotnet build tests/MediPOS.IntegrationTests --no-restore -m:1 -nr:false`.

```powershell
$pg = 'tests/MediPOS.IntegrationTests/bin/Debug/net10.0/MediPOS.IntegrationTests.dll'
$names = @(
    '*purchase-draft*',
    '*TransferRequestCommitsOneOriginalAndBindsTheKeyToItsDestination',
    '*TransferRequestFailureBetweenBusinessSaveAndReceiptRollsBackEverything',
    '*CashTransferRecoverabilityTests.*',
    '*SaleCheckoutConcurrencyTests.*', '*SaleVoidConcurrencyTests.*', '*CashCloseConcurrencyTests.*',
    '*CashTransferConcurrencyTests.*', '*CashTransferIsolationTests.*',
    '*SaleCheckoutIsolationTests.*', '*SaleVoidIsolationTests.*', '*CashCloseIsolationTests.*',
    '*CommissionAtomicityTests.*',
    '*SameEventSimultaneousReplayCommitsOneSaleAndRejectedPayloadNeverBecomesASale',
    '*MigrationRlsAndPoolingIsolateConflictsWithoutQueryFiltersOrClientAuthority',
    '*ActualConnectionTerminationDuringSecondReservationKeepsOnlyCommittedPrefixAndRetryRecovers',
    '*UnitPersistenceFailureRollsBackOnlyThatProductAndPreservesEarlierCommittedRows',
    '*SummaryAuditFailureCannotLeaveJobCompletedWithoutItsAudit',
    '*RuntimeRlsIsForcedForJobsAndRowsAndNeverExposesAnotherTenantsProductsUnitsOrImportHistory',
    '*PooledConnectionClearsImportJobsAndRowsBeforeReuseByAnotherTenant',
    '*InterruptedProductImportTests.*', '*InterruptedProductImportMigrationTests.*',
    '*ContainerAcceptsConnectionThroughDbContext'
)
$filters = foreach ($name in $names) { '-displayName'; $name }
dotnet $pg -preEnumerateTheories -list full/json @filters
# Solo en un runner aislado autorizado:
dotnet $pg -preEnumerateTheories -showLiveOutput -failSkips -result-trx 'tests/MediPOS.IntegrationTests/bin/Debug/net10.0/a360-migration40-postgres.trx' @filters
```

Publicar el candidato según el procedimiento de revisión y ejecutar en GitHub `Actions → Backend validation - PostgreSQL 18 → Run workflow → ci/postgres-diagnostics-8bc6b4c`, comprobando que esa rama apunte al nuevo SHA. Cotejar `Tested commit`, la versión `MEDIPOS_POSTGRES_VERSION=18.x`, los conteos PASS/FAIL/SKIP y las suites `InterruptedProductImportTests` e `InterruptedProductImportMigrationTests`. El workflow hace checkout del `github.sha` y ejecuta el proyecto de integración completo. La fixture usa datos sintéticos, credenciales generadas y un runtime sin `SUPERUSER` ni `BYPASSRLS` en PostgreSQL efímero.

**Antecedente L4A-FINAL.** xUnit de lógica y autorización: 161 PASS, 0 FAIL, 0 SKIP. Vitest con dobles HTTP: primera pasada 41 PASS, 1 FAIL, 0 SKIP en la prueba del flujo completo de mercadería. La repetición en serie sin cambios dio 42 PASS, 0 FAIL, 0 SKIP.

**Candidato 40, evidencia local.** xUnit de lógica, autorización y conversión de auditoría 110 PASS, Kestrel con dobles 2 PASS y Vitest con dobles HTTP 17 PASS, todos con 0 FAIL y 0 SKIP en la ejecución final. ESLint, builds frontend/backend, EF sin cambios pendientes y revisión offline del SQL PASS. La primera compilación frontend detectó un fixture desactualizado y una inferencia de tipo en pruebas; ambos se corrigieron. El build conserva la advertencia de tamaño de bundle de Vite.

**Guion L4B, pendiente de GO.** Con PostgreSQL aprobado, verificar instalación conjunta de API y React. Usar tres perfiles Chrome y datos controlados: A administra plataforma, B y C son Owners de boticas distintas. B valida alta y confirmación de compra con stock único, solicitud de mercadería y despacho de sencillo. Simular pérdida del acuse, recuperar cada intención original y comprobar una sola escritura y su saldo. C valida la denegación en la botica de B y A la separación de plataforma y negocio. Para Excel, verificar primero los resultados parciales y ensayar el cierre solo con la acción auditada habilitada y comprobada.

### A360-LEAN · E0

[CI de referencia](https://github.com/saintdeyb321/MediPOS/actions/runs/45312440841), sobre `db1af8f`: SDK 10.0.402, build, NuGet Audit y 8 pruebas de arquitectura PASS. Unitarias 2 655 PASS y 9 FAIL. PostgreSQL 18.7, 866 PASS y 38 FAIL. Cero SKIP. Formato FAIL y gate `NOT_APPROVED`.

| Grupo | Causa y corrección local |
|---|---|
| E0.1 | El coordinador de constancias exigía un servicio de identidad que deliberadamente no se registra con Google deshabilitado. El host compone sin esa dependencia y las constancias exigen una identidad real antes de autorizar o acceder a datos. El doble XLSX implementa el contrato de adquisición del candado. Proveedor fiscal y schedulers conservan sus controles. |
| E0.2 | El inventario HTTP conservaba 95 rutas y omitía cinco contratos ya aprobados: catálogo operacional, edición local, consulta de constancias y recuperación XLSX. La prueba comprueba las 100 rutas. El plan offline apunta a esquema 40, con seis pasos desde 34 y el prefijo 34→37 intacto. Sucursales, licencias y readiness comparten el fallo de composición observado antes de sus verificaciones PostgreSQL. |
| E0.3 | El bootstrap de plataforma del seed consultaba `initial_owner_invitations` con el rol comercial del benchmark (`42501`). La importación de usuarios también referenciaba `google_email_verified` en esquema 34 (`42703`). El seed sintético usa la conexión de preparación de la base desechable y columnas comunes. La carga y el acceso comercial conservan el runtime restringido. La regresión histórica reutiliza el permiso de fixture de invitaciones solo después de verificar el upgrade 34→37 y la sonda de lectura. |
| E0.4 | La prueba de sencillo comprueba `55P03` directamente en Npgsql porque EF envuelve el error esperado. Excel distingue un trabajo terminal autorizado del bloqueo de escritura por RLS y conserva el candado al verificar revocación. El caso de anulación tropezaba con la misma composición de E0.1. |
| E0.5 | E0 corrigió los 45 `WHITESPACE` ajenos a migraciones y los siete `IMPORTS` del CI. E0-FINAL resuelve los 120 `WHITESPACE` de la migración 39 y los dos `CHARSET` de 38/39. La verificación focalizada da PASS en LF, con los mismos blobs que produciría Git. El checkout CRLF solo reporta 242 `ENDOFLINE` en esos dos archivos. |

**Evidencia previa de E0.** Build backend y `git diff --check` PASS. Pruebas focalizadas con 189 de lógica y composición y 60 de contrato HTTP con Kestrel y dobles, todas PASS, 0 FAIL, 0 SKIP. Descubrimiento de integración con 77 casos en nueve clases afectadas y 0 ejecutados.

**Evidencia de E0-FINAL.** Con SDK 10.0.401 y las mismas opciones offline, los 22 scripts EF/Recovery y los cinco literales SQL de 38/39 coincidieron byte por byte y por SHA-256. Se verificaron seis planes de `RecoveryWorkflow.MigrationPlan` y sus `PlanHash`. En 34→40 ambos producen `8EC172A6173E63EAC745C10FBF5D0A9795940902BE14C593B554593D7411284A`. Los tres descensos que cruzan 37 siguen rechazados por la misma guarda. La evidencia anterior/posterior y el manifiesto de hashes se conservaron fuera del repositorio. Builds focalizados PASS con 0 advertencias y 0 errores. `RecoveryMigrationIntegrityTests` 27 PASS, 0 FAIL, 0 SKIP. `git diff --check` PASS.

**E0 PASS_PG.** En el SHA `4523902c848a53f7f1d9eb89de3e2d8a7a03ae11`, el [run 45325845075](https://github.com/saintdeyb321/MediPOS/actions/runs/45325845075) aprobó restore, build, NuGet Audit, formato, 2 664 unitarias, 8 de arquitectura y 904 PostgreSQL 18.7. Total 3 576 PASS, 0 FAIL, 0 SKIP. Los 9 y 38 fallos arriba pertenecen al run anterior. Esta evidencia abarca E0 y ese SHA.

### A360-LEAN · E1–E5

| Fase | Checkpoint del candidato local |
|---|---|
| E1 | **IMPLEMENTADO · PENDIENTE_BENCHMARK.** Compras pasa de N consultas de producto a una lectura por IDs distintos. POS usa dos lotes frente a 2N consultas de producto, uno antes de los candados y otro después. El repositorio usa una consulta acotada al tenant, sin tracking. El orden de candados, FEFO, revisión, autorización, constancias y commits se conservan. 116 pruebas focalizadas de compra/POS/offline PASS. El caso PG del harness comprueba una sentencia y exclusión de IDs ajenos/ausentes. Caja, reportes, búsqueda y las validaciones de efectos POS conservan sus consultas hasta medir planes y snapshots. |
| E2 | **IMPLEMENTADO.** Confirmar compra con acuse terminal verificado elimina un GET de detalle, conservando líneas de la revisión aceptada. Un GET temprano en borrador sigue sin liberar incertidumbre. Recuperar constancias y reconciliar escrituras realmente inciertas siguen siendo pasos separados. Se conservaron los rechecks de acceso y la lógica decimal y de ledger donde verifican fronteras distintas. |
| E3 | **IMPLEMENTADO · PENDIENTE_ACEPTACION.** Apertura de caja usa el acuse de la membresía autenticada y evita un GET. POS muestra recuperación cuando el ticket discrepa o existe una operación pendiente. Compras evita un aviso duplicado. El editor de presentaciones conserva su versión frente a una lectura posterior sin sincronizar estado por efecto. Excel mantiene carga/revisión y solo invalida el catálogo al observar otro trabajo o progreso en sus filas. Transferencias, licencias, personal y PWA mantienen sus pasos de custodia y acuses de identidad. |
| E4 | **FISCAL_PREPARATION_PENDING.** El proveedor y el verificador reciben un documento con emisor, tenant e identidad documental validados. El lease queda en persistencia. DI mantiene proveedor, identidad y evidencia deshabilitados. Query carece de consumidor operativo. El [plan SUNAT](SUNAT_INTEGRATION_PLAN.md#61-frontera-de-conexión-e4) detalla perfil, ambiente, evidencia, conciliación, registro y apagado. Quedan pendientes el adaptador elegido y su certificación. |
| E5 | **BLOQUEADO para publicar candidato.** Build en ambos stacks, ESLint, EF offline y verificación focalizada PASS. La suite completa de frontend sigue en FAIL con 54 casos que también fallan en la base. Quedan pendientes CI exacto, rendimiento medido, aceptación Chrome, recuperación operativa y aprobación comercial/fiscal. S4 conserva su decisión independiente. |

**Gate local backend.** SDK 10.0.401. Build de la solución con 0 errores y 0 advertencias. 233 unitarias focalizadas, 60 HTTP con Kestrel y dobles, y 8 arquitectura PASS, 0 FAIL, 0 SKIP. EF offline sin cambios de modelo. Formato de las rutas editadas y `git diff --check` PASS. Formato completo local devolvió código 2 con 4 400 `ENDOFLINE` del checkout Windows y cero diagnósticos de reglas de código. Inventario descubierto 2 672 unitarias, 8 arquitectura y 904 PostgreSQL (3 584 total). La diferencia con E0 son ocho regresiones unitarias de compra, POS y CPE. PostgreSQL del candidato sigue sin ejecutarse.

**Gate local frontend.** Node 24.18.1. Build y ESLint PASS, con advertencia Vite de tamaño del bundle. Primera ejecución paralela: 521 PASS, 68 FAIL, 0 SKIP. Ejecución final en serie: **535 PASS, 54 FAIL, 0 SKIP**, con **138 PASS** en compra/POS/búsqueda/caja/Excel/editor de presentaciones/offline. Los 54 fallos finales se reprodujeron por nombre y archivo en el SHA base (65 FAIL de 107 casos en 11 archivos examinados con las fuentes de Git). Las diez pruebas recuperadas de búsqueda usan el selector y diálogo actuales. La prueba de sucursales de Personal pasó en la corrida final y falló en las otras ejecuciones. Quedan bloqueos de navegación, catálogo, comisiones, invitaciones, Inicio/reportes y configuración/alta inicial. Conservar los reportes JSON externos. Alinear esas regresiones a los flujos vigentes y repetir la suite antes de solicitar publicación. Cero emisiones fiscales reales y cero modificaciones a bases existentes.

**Comparación B8.2, pendiente de infraestructura autorizada.** Usar el mismo runner, SDK 10.0.402, digest PostgreSQL 18, hardware, parámetros y semilla para la base `4523902` y el SHA del candidato. Ejecutar desde `backend/` en cada checkout revisado. El operador inyecta por separado las credenciales protegidas de [B8.2](BACKEND_PLAN.md#b82--performanceload-targets), con un run ID único por variante y una base sintética nueva asociada. Conservar JSON y EXPLAIN fuera del repositorio.

```powershell
$env:MEDIPOS_ENVIRONMENT = 'Performance'
$env:MEDIPOS_PERF_OPT_IN = 'CREATE_DISPOSABLE_DATABASE'
$runId = Read-Host 'Run ID aprobado de 32 hexadecimales que coincide con la base runtime'
$evidence = Read-Host 'Directorio protegido absoluto de la variante, fuera del repositorio'
$hardware = Read-Host 'CPU, RAM, almacenamiento y red del host de referencia'
dotnet run -c Release --project tests/MediPOS.Performance -- --run-id $runId --environment isolated-lab --server-hardware $hardware --products 2000 --stocked-products 128 --lots 3 --branches 3 --operators 2 --tenants 2 --concurrency 1,2,4,8 --samples 1000 --warmup 100 --seed 6282 --output $evidence --explain true
if ($LASTEXITCODE -ne 0) { throw 'Benchmark incompleto o fallido' }
$report = Get-Content -Raw -LiteralPath (Join-Path $evidence ($runId + '.json')) | ConvertFrom-Json
if (-not $report.Completed -or ($report.Results | Where-Object Errors -gt 0)) { throw 'Resultados sin gate de integridad' }
$report.Results | Select-Object Name,Concurrency,Successful,ExpectedConflicts,Errors,SuccessfulPerSecond,SqlCommands,PeakObservedConnections,SuccessLatency,Phases,TargetResult
```

Comparar p50/p95/p99, consultas por intento, fases de espera/posting y throughput bajo datos equivalentes. Registrar CPU, memoria y pool con la telemetría del runner y servidor en la misma ventana. El harness mide handlers y no incluye benchmark de compras/cierre ni HTTP/navegador. Medir esos recorridos en un entorno aprobado antes de cambiar agregación de caja, locks o índices. Metas B8.2: búsqueda p95 <500 ms, confirmación p95 <1 s. Los objetivos 200 ms/500 ms/2 s de E1 siguen orientativos.

**Aceptación y recuperación pendientes.** En una instalación aislada de API/React del mismo SHA con esquema 40, A crea Trial/designa, B y C reclaman por Google en perfiles separados y verifican denegación cruzada y revocación. B recibe compra con lote y vencimiento, abre caja, cobra con pago mixto, imprime ticket interno y cierra/arquea. Verificar última unidad, transferencias de ambas clases, personal y Excel. En dos pestañas probar pérdida de acuse y cambio de identidad en POS/PWA, sin duplicar ventas, stock, constancias o eventos. Medir pedidos, clics, tiempo y errores en desktop y móvil, incluyendo teclado y recuperación.

**Gate del piloto.** Ejecutar el workflow manual existente sobre el SHA exacto y `npm run build`, `npm run lint`, `npm run test` para frontend. Ensayar backup, restauración aislada y rollback con el [runbook B8.3](BACKUP_RECOVERY_RUNBOOK.md#restauración-y-migraciones), comparar huellas y medir pérdida/tiempo frente a RPO/RTO aprobados. Instalar API y React compatibles juntos. Mantener `ProductImportRecovery__EnabledAfterWriterDrain=false` hasta drenar versiones anteriores. Tras el primer evento `catalog.product_import_interrupted`, conservar esquema 40 y lectores compatibles. Antes del piloto, el operador debe verificar HTTPS/proxy, secretos externos, runtime restringido y RLS, readiness, latencia/errores/pool/colas, copias/retención, alertas, soporte y responsables de incidente. El responsable tributario valida elegibilidad del cliente y alcance del ticket interno antes de autorizar el piloto.

### RC1 · Evidencia local y gates externos

| Control | Resultado |
|---|---|
| C1 · Frontend | La base en serie tenía 535 PASS, 54 FAIL y 0 SKIP. Los 54 se agruparon en navegación (6), catálogo (14), comisiones (10), invitaciones (4), inicio y reportes (8) y configuración/alta inicial (12). La primera corrida paralela anterior reportó 68 fallos. Se actualizaron rutas, formularios y DTO de prueba al contrato vigente, se eliminó la presentación simultánea del enlace de invitación en la página y el modal, y se estabilizó la reapertura en Personal esperando que termine la conciliación y consultando el botón vigente. Vitest usa dos workers. Dos corridas habituales de `npm run test` dieron 592 PASS, 0 FAIL y 0 SKIP cada una, con +3 regresiones de compra, caja y presentaciones. Repetición focalizada: 39 PASS. Build y lint PASS con Node 24.18.1. |
| C2 · Integridad | El acuse de compra exige el actor de la sesión, una respuesta inconsistente conserva la recuperación, y POS oculta el estado previo cuando el ticket discrepa. Se fija el tipo usado al bloquear lotes y se comprueba otra vez antes de registrar stock. Excel ignora retrocesos de progreso al invalidar catálogo. Regresiones de apertura/cierre de caja entre lecturas, revisión de presentaciones y un solo POST. Compras y ventas conservan respectivamente N→1 y 2N→2 lecturas de productos, sin medición de latencia. |
| C3 · CPE | 18 casos nuevos validan perfiles de preparación y composición desactivada. Queda `FISCAL_PREPARATION_PENDING` hasta aprobar el vínculo persistente de perfil, reconciliación/evidencia y el proveedor para sandbox. [Plan SUNAT](SUNAT_INTEGRATION_PLAN.md#61-frontera-de-conexión-e4) |
| C4 · Backend | SDK local 10.0.401. Solución completa compilada, 285 unitarias focalizadas, 60 HTTP con dobles (6 reejecutadas tras el ajuste final de POS) y 8 arquitectura PASS, 0 FAIL/SKIP. EF offline sin cambios, formato verificado en copia LF y `git diff --check` PASS. Descubiertos 2 691 unitarias, 8 arquitectura y 904 PostgreSQL, frente a 2 672/8/904 en E1–E5 (+18 perfiles y +1 cambio de tipo tras el lock). PostgreSQL RC1 ejecutado localmente: 0. Pendientes CI 10.0.402, NuGet Audit remoto y benchmark A/B. |
| C5 · Operación | Preparados los pasos de aceptación, recuperación y piloto. Pendientes aceptación en Chrome, prueba PWA real, restauración/rollback, RPO/RTO medidos y aprobación comercial. |

**Decisión RC1.** GO para publicar el candidato de pruebas. NO-GO para aceptación comercial hasta superar el CI del nuevo SHA y autorizar el entorno aislado. NO-GO para piloto hasta completar aceptación, recuperación, validación operativa y aprobación comercial.

#### Cómo validar y aceptar el candidato

1. Publicar la revisión RC1 en la rama de pruebas tras revisar el diff y fijar el SHA. En GitHub Actions, lanzar manualmente `Backend validation - PostgreSQL 18` sobre ese SHA con .NET SDK 10.0.402. Exigir PostgreSQL 18.x verificado, NuGet Audit, formato, 0 fallos/omitidos y `CODE_AND_POSTGRESQL_GATE_PASSED (not production acceptance)`. Propuesta para el mismo SHA en runner limpio de solo lectura y sin secretos: fijar Node 24.18.1 y ejecutar desde `frontend/` `npm ci`, `npm run build`, `npm run lint` y `npm run test`. Conservar JSON de resultados fuera del repo.
2. Autorizar el destino desechable `medipos_perf_<run-id>` y ejecutar el comando B8.2 anterior para `4523902` y RC1 con el mismo hardware, SDK, digest y configuración. Comparar p50/p95/p99, SQL por intento, throughput, errores, esperas y consumo de recursos. Medir compra y cierre por separado. Aplicar los objetivos p95 de 500 ms para búsqueda y 1 s para confirmación.
3. Autorizar un entorno de aceptación aislado en esquema 40 con API y React del mismo SHA. En perfiles Chrome distintos, A designa Owners B y C en dos boticas Trial. Verificar reclamo real por Google, aislamiento cruzado, licencia, horario, cambio de permisos y revocación con sesión activa. Ejecutar compra/stock, POS/pago mixto/ticket/anulación, arqueo, ambas transferencias, personal, reportes e importación. Contrastar saldos y constancias. Probar PWA en dos pestañas ante reconexión, pérdida de respuesta y cambio/revocación de identidad. Registrar tareas, solicitudes, clics y tiempos con teclado, lector y móvil. Usar datos sintéticos autorizados sin ejecutar S4.
4. El operador ensaya respaldo y restauración en el destino aislado según [B8.3](BACKUP_RECOVERY_RUNBOOK.md#restauración-y-migraciones). Compara huellas, invariantes y operaciones inciertas, mide pérdida y tiempo frente a RPO/RTO aprobados y prueba rollback de la aplicación sobre esquema 40 con lectores compatibles. Verifica TLS/proxy, secretos externos, runtime/RLS, readiness, métricas, alertas, retención y responsables de soporte e incidentes. Drena versiones antiguas antes de aprobar la activación de `ProductImportRecovery__EnabledAfterWriterDrain`. Mantener la opción en `false` hasta entonces.
5. El responsable del negocio aprueba por separado botica, alcance tributario, datos, entorno y criterios de éxito/detención del piloto. CPE permanece desactivada. El piloto no fiscal solo es elegible si el alcance tributario del cliente permite operar con ticket interno.

### UX-LEAN · U1–U5

Base local `47e4335b2c46f0de874797724ba30e123705ea79`, rama `ci/postgres-diagnostics-8bc6b4c`. El workflow de certificación ya tenía cambios locales. La consulta de estado del SHA no devolvió checks y el listado de ejecuciones manuales no estuvo disponible. El candidato UX requiere su propio SHA y CI.

| Fase | Resultado | Antes → después |
|---|---|---|
| U1 · POS, caja y compras | Cobro, anulación y conteo cierran con acción explícita. Enter en el buscador acelera la consulta; un código exacto único mueve el foco a la selección local. Compras integra producto/lote, exige guardar las líneas y reserva el ingreso de stock para la recepción física. | Compromiso de cobro/anulación/cierre 2→1 interacciones cada uno. Compras 5→4 etapas indicadas y 4→2 interacciones de guardado/confirmación. **MEDIDO_EN_TEST_DOM:** cierre normal 3→1 GET de estado/arqueo incluyendo la preparación, con 1 POST y refrescos de caja activa conservados. Compras conserva 2 GET del detalle en el caso verificado y 1 POST de confirmación. |
| U2 · Catálogo, inventario y transferencias | Precios, estado, presentaciones, mínimos y ajuste usan guardado directo o un compromiso contextual. Cantidades, lotes, motivo y revisiones se conservan. Sencillo muestra caja, sede e importe vigentes en el momento del clic. | Catálogo local 4→3 etapas, referencia global 4→2. Solicitud de mercadería 3→1 vista y 4→1 interacciones de avance/compromiso. Aprobación, cancelación y custodia tienen una interacción obligatoria menos cada una. |
| U3 · Administración y Excel | Alta de botica compacta con Gmail exacto y resumen Trial, cambios de licencia rotulados por efecto, asignaciones/horarios y comisiones con guardado directo. Excel mantiene plantilla opcional, cierre explícito y revisión de fallos parciales. | Alta 3→1 etapa y 4→1 interacciones de avance/compromiso. Excel 4→2 etapas indicadas y 2→1 tanto para importar como para terminar una revisión exitosa. |
| U4 · Red, accesibilidad y PWA | Se retiró la reconsulta al aplicar filtros idénticos. Se conserva «Actualizar» y la aplicación agrupada de filtros de servidor. La caja solo usa el acuse si concuerdan ámbito, membresía, actor e importe. | Aplicar filtros ya vigentes en primera página 1→0 GET adicional, verificado en el DOM para el candidato. Un escaneo en prueba DOM realizó 1 GET y 0 escrituras. POS conserva sus lecturas autoritativas de estado y ticket. Permisos, recuperación y sincronización PWA conservan el circuito de RC1. |
| U5 · Validación | **PROBADO_LOCAL.** Node 24.18.1, npm 11.16.0 y Vitest con dos workers. Focalizadas U1 137 PASS, U2–U4 302 PASS. Suite final 593 PASS, 0 FAIL, 0 SKIP, 66 archivos (+1 regresión del lector). Build, lint y `git diff --check` PASS. Se corrigió la carrera entre el foco inicial y el resultado del escaneo detectada en la primera corrida integral. | Chrome 1366×768/móvil, teclado, lector físico y PWA de dos pestañas quedan **PENDIENTE_ACEPTACION** en el entorno aislado del guion RC1. |

Las interacciones de las filas anteriores son **ESTIMACIÓN_DE_INTERACCIÓN**. Se cuentan los controles obligatorios de avance/confirmación, con los campos válidos ya ingresados. La línea base HTTP salió del código RC1 y los conteos posteriores se verificaron con dobles HTTP (**MEDIDO_EN_TEST_DOM**). Se retiraron 32 casillas JSX. La revisión de fuente clínica permanece para medicamento local y corrección auditada. Se conservan estado activo, sucursales, filtros, consentimiento y verificación independiente de identidad, revisiones de recuperación, conciliación de Excel parcial y declaraciones offline. Ninguna reducción de pasos agrega escrituras.

**Decisión UX-LEAN.** GO para publicar el candidato de pruebas. NO-GO para aceptación comercial y piloto hasta certificar el nuevo SHA y completar el guion de aceptación y los gates externos de RC1.

## CI y primer candidato

El [workflow actual](../.github/workflows/backend-validation.yml) es manual y solo cubre backend. Comprueba SHA, restore, build, auditoría NuGet, diagnóstico saneado, pruebas unitarias y de arquitectura, formato y PostgreSQL 18 real en recursos efímeros. Usa acciones fijadas por SHA, permisos de lectura y resultados saneados. El resumen falla ante pruebas omitidas, incompletas o sin evidencia del servidor PostgreSQL 18. El build y los tests vuelven a resolver dependencias después del restore explícito. La imagen `postgres:18-alpine` se obtiene por etiqueta y se registra su digest. Para un candidato reproducible, revisar el pin de la imagen en el workflow y la fixture en un PR posterior.

Propuesta, pendiente de aprobación. Añadir un workflow `pull_request` a `main` con dos jobs paralelos, sin secretos, permisos `contents: read`, acciones fijadas por SHA, cancelación de corridas superadas y herramientas fijadas (.NET 10.0.402 para reproducir CI y Node 24 en una versión aprobada).

- **Backend rápido.** Desde `backend/`, restore con NuGet Audit, build sin restore adicional, arquitectura y un filtro de regresiones del área cambiada. Exigir que el filtro ejecute casos. Añadir EF offline si cambia el modelo y formato para cambios .NET.
- **Frontend rápido.** Desde `frontend/`, `npm ci`, `npm run build`, `npm run lint` y Vitest focalizado en las rutas afectadas. La admisión y la consola tienen regresiones en `EntryRouter.test.tsx` y `PlatformPage.test.tsx`.

Para integrar, separar documentación, ajustes S4 y cambios funcionales en commits revisables. Revisar la comparación de la rama con `main`, abrir un PR borrador con alcance, migraciones, riesgos y evidencia por SHA, y exigir los dos checks rápidos y revisión. Mantener el gate PostgreSQL manual. Identificar el primer candidato como `v0.1.0-rc.1` en el registro del PR cuando el SHA exacto que se propone liberar tenga checks integrales, recuperación y aceptación verificadas. Si el SHA cambia al integrar, verificar la revisión resultante antes de etiquetar. La propuesta no activa el workflow ni publica el candidato.

## GitNexus

Compatible en principio con el repositorio Git y con C# y TypeScript/JavaScript. Node 24.18.1 en Windows x64 satisface el mínimo publicado (22.18+ en 22.x o 24.11+). El equipo tiene el redistribuible Visual C++ x64 registrado y ambos DLL de OpenSSL 3 en PATH. Su carga en GitNexus sigue sin probarse. La indexación y cobertura de TSX, EF, DI, SQL/RLS y dependencias dinámicas requieren comprobación contra código y pruebas. [Proyecto oficial](https://github.com/abhigyanpatwari/GitNexus) · [Requisitos Windows](https://github.com/abhigyanpatwari/GitNexus/blob/main/gitnexus/README.md)

La licencia publicada es PolyForm Noncommercial 1.0.0. El uso en el desarrollo comercial de MediPOS requiere aclarar el permiso con el titular antes de instalar. Tras esa autorización, revisar exclusiones de secretos y artefactos, fijar versión y ejecutar la primera indexación con `gitnexus analyze --index-only`. Revisar el árbol y las huellas de ambos AGENTS antes y después. Omitir la generación de instrucciones, hooks, integración y wikis. [Licencia de GitNexus](https://github.com/abhigyanpatwari/GitNexus/blob/main/LICENSE)

## Referencias históricas

- [Plan backend](BACKEND_PLAN.md), [plan frontend](FRONTEND_INTEGRATION_PLAN.md) y [plan maestro original](MediPOS_Plan_Maestro_Integracion_Funcional.md).
- UX [V1](PLAN_UX_UI_MEDIPOS_SPRINTS.md), [V2](PLAN_UX_UI_MEDIPOS_V2.md), [V3](PLAN_MEDIPOS_UX_IX_V3_OPERACION_COMPACTA.md), [V4](MEDIPOS_PLAN_UX_IX_V4_Y_CIERRE_MVP.md) y [protocolo de auditoría](MEDIPOS_PROMPT_AUDITORIA_INDEPENDIENTE_MVP.md).
- [Plan inicial de alta por correo](MEDIPOS_PLAN_SUPERADMIN_OWNER_EMAIL_UX_RESET_DEV.md), [referencia de cierre y S4](MEDIPOS_AUDITORIA_SUPERADMIN_V2_PLAN_CIERRE.md) y guías previas de [backend](../backend/README.history.md) y [frontend](../frontend/README.history.md).
