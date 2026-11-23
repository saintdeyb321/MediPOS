# Estado actual de MediPOS

Revisión del 23 de noviembre de 2026. Rama local `ci/postgres-diagnostics-8bc6b4c`, HEAD `a6ec96866883d979199b24e42190cabf8184f3bf` (Lote 3). El árbol conserva el plan L4 recibido y contiene los cambios locales de L4A sin commit. [SPEC](SPEC.md), [ARCHITECTURE](ARCHITECTURE.md) y [ADR](ADR.md) definen el producto y sus límites.

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

### A360 · L4A · Gates técnicos

| Objetivo | Estado y evidencia |
|---|---|
| Compras | **PENDIENTE PostgreSQL.** Cuatro casos descubiertos de `purchase-draft` para replay, rollback, competencia y aislamiento. Ahora contrastan el ID y la sucursal de la constancia con el borrador almacenado, junto con las comprobaciones de no mover stock. |
| Mercadería | **CODE_READY.** La solicitud con clave usa el coordinador existente como único dueño del commit de transferencia, líneas, evento, auditoría y constancia. Autoriza en destino y vincula la clave a actor, membresía, tenant y destino. React consulta la constancia original tras recarga y bloquea nuevas solicitudes mientras no pueda conciliar. Dos regresiones PostgreSQL compiladas para commit, replay, conflicto y rollback. |
| Sencillo | **BLOQUEADO.** `CashTransferTransaction` adquiere el candado de caja origen y confirma directamente. La constancia debe incorporarse al commit de ese alcance, conservando validación de saldo/ledger bajo el candado. La clave debe ligar origen, caja, destino, importe, actor y membresía. Requiere aprobar el rediseño acotado del despacho y comprobar replay, pérdida de acuse, carreras con cierre/venta/anulación y saldo insuficiente en PostgreSQL. |
| Importación | **PROBADO LOCALMENTE para el marcador heredado.** `importPending=1` permite consultar trabajos autorizados, pero solo un acuse o constancia verificados de una clave propia permite finalizar la revisión. Para `processing`, preservar la clave y el trabajo, consultar su constancia y las filas confirmadas, y escalar a soporte sin reenviar el archivo. Las filas se confirman por separado y el candado de `CompleteAsync`/`FailAsync` no bloquea `ImportRowAsync`. Cerrar un trabajo interrumpido requiere comprobar que no quedan escritores, mediante un candado por trabajo que cubra también el camino legado, antes de registrar un estado terminal, recuentos y auditoría. |
| Concurrencia y RLS | **PENDIENTE PostgreSQL.** Set de 70 casos, incluido el smoke que acredita `server_version`, para POS, última unidad, stock, caja, anulación/comisiones, sencillo, offline desde conexiones distintas, interrupción y aislamiento. |

En Excel, `StartAsync` confirma el trabajo y la constancia de la solicitud con clave antes de importar filas. Cada fila confirma producto, unidad base y resultado en su propia transacción. La restricción única `(tenant, trabajo, fila)` protege ese trabajo y una nueva carga crea otro identificador. Un fallo abrupto puede impedir el registro terminal aun cuando ya existan filas confirmadas.

GitNexus CLI consultó el índice `ecfb687`, tres commits detrás de HEAD. Se contrastaron los símbolos con el código local, incluidas las relaciones por interfaces y DI.

Gate local final: 44 Vitest con dobles HTTP, 246 xUnit de lógica y 14 xUnit del contrato HTTP con Kestrel en loopback y servicios de prueba. En cada grupo, 0 FAIL y 0 SKIP. ESLint focalizado, builds frontend/backend y `git diff --check` PASS. El build frontend aisló sus assets de DEV. La aplicación de mercadería con clave requiere publicar API y React compatibles. PostgreSQL y Chrome siguen sin ejecutar para este candidato.

Desde `backend/`, compilar con `dotnet build tests/MediPOS.IntegrationTests --no-restore -m:1 -nr:false`. El siguiente filtro se comprobó solo mediante descubrimiento y seleccionó 76 casos: 4 de compras, 2 de mercadería y 70 del gate.

```powershell
$pg = 'tests/MediPOS.IntegrationTests/bin/Debug/net10.0/MediPOS.IntegrationTests.dll'
$names = @(
    '*purchase-draft*',
    '*TransferRequestCommitsOneOriginalAndBindsTheKeyToItsDestination',
    '*TransferRequestFailureBetweenBusinessSaveAndReceiptRollsBackEverything',
    '*SaleCheckoutConcurrencyTests.*', '*SaleVoidConcurrencyTests.*', '*CashCloseConcurrencyTests.*',
    '*CashTransferConcurrencyTests.*', '*CashTransferIsolationTests.*',
    '*SaleCheckoutIsolationTests.*', '*SaleVoidIsolationTests.*', '*CashCloseIsolationTests.*',
    '*CommissionAtomicityTests.*',
    '*SameEventSimultaneousReplayCommitsOneSaleAndRejectedPayloadNeverBecomesASale',
    '*MigrationRlsAndPoolingIsolateConflictsWithoutQueryFiltersOrClientAuthority',
    '*ActualConnectionTerminationDuringSecondReservationKeepsOnlyCommittedPrefixAndRetryRecovers',
    '*ContainerAcceptsConnectionThroughDbContext'
)
$filters = foreach ($name in $names) { '-displayName'; $name }
dotnet $pg -preEnumerateTheories -list full/json @filters
# Solo en un runner aislado autorizado:
dotnet $pg -preEnumerateTheories -showLiveOutput @filters
```

Registrar SHA exacto, conteos PASS/FAIL/SKIP y `MEDIPOS_POSTGRES_VERSION=18.x` del smoke real. La fixture usa PostgreSQL 18 efímero y el rol runtime de pruebas sin `SUPERUSER` ni `BYPASSRLS`; el destino DEV queda excluido. Como alternativa sujeta a publicar y autorizar el SHA candidato, el workflow manual `Backend validation - PostgreSQL 18` ejecuta el gate completo. Elegir la rama del candidato en `Run workflow` y verificar el SHA del run. L4B, L4C y la liberación siguen pendientes de evidencia y revisión.

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
