# Estado actual de MediPOS

Revisión del 23 de noviembre de 2026. Rama `ci/postgres-diagnostics-8bc6b4c`, HEAD `db1af8f27b89f9f7ee52f6e7badc98d4da8d56d2`. Los correctivos E0 están en el árbol local, sin commit. [SPEC](SPEC.md), [ARCHITECTURE](ARCHITECTURE.md) y [ADR](ADR.md) definen el producto y sus límites.

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
| E0.5 | Se corrigieron los 45 `WHITESPACE` ajenos a migraciones y los siete `IMPORTS` del CI. La verificación completa local salió con código 2: 120 `WHITESPACE` en la migración 39, dos `CHARSET` en 38/39 y 4 754 `ENDOFLINE` en 43 archivos del checkout Windows. **NO-GO** hasta resolver el formato histórico con autorización y repetir CI. |

Build backend PASS, 0 advertencias y 0 errores. Pruebas focalizadas: 189 de lógica y composición más 60 de contrato HTTP con Kestrel y dobles, todas PASS, 0 FAIL, 0 SKIP en la ejecución final. `git diff --check` PASS. Descubrimiento de integración: 77 casos en las nueve clases afectadas, 0 ejecutados. Repetir en el CI PostgreSQL completo los 38 fallos del run de referencia, con especial atención a 34→37, permisos del runtime, sencillo, recuperación XLSX repetida y revocación, y aislamiento de anulación. DEV, S4, RLS, configuración fiscal y migraciones 1–40 quedan intactos.

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
