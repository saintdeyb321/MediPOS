# RFA3 · Backlog para aprobación por lote

Baseline `5d38898`. Los lotes son propuestas. La columna de decisión de los [155 casos](TO_BE.md) cubre también los circuitos que deben conservarse. [RUN-10](VALIDATION.md#run-10) aporta PostgreSQL 18.7 y un fallo de frontend aún sin identificar. Ningún lote habilita datos de DEV ni cambios fiscales.

| Orden | Tipo / prioridad | Dependencia y decisión del operador | Tamaño · riesgo · esfuerzo orientativo |
|---|---|---|---|
| L-01 · Despachar sencillo | Bugfix P1, F-002 | Primero. Aprobar alternativa A y destino aislado para comprobar contrato. B solo si el operador necesita saldo visible. | S para A, M para B · alto, dinero · Astra High |
| L-02 · Relocalizar arqueos | Brecha de recuperación P2, F-001 | Independiente de L-01. Elegir enlace actual o historial con alcance explícito. | M · alto, privacidad/dinero · Astra High |
| L-03 · Localizar compras y productos | Capacidades opcionales P2, F-003/004 | Dos decisiones independientes de búsqueda. Reutilizar criterio de paginación/scope de L-02 cuando coincida. | M · medio · Sol High |
| L-04 · Coste de existencias/lotes | Medición, F-005 | Requiere página representativa y presupuesto de latencia acordado. Puede terminar sin cambio. | S medir, M proyectar · medio, aislamiento · Sol High |
| L-05 · Repetibilidad | Calidad de tests, F-006 y fallo CI sin atribuir | Puede investigarse en paralelo a L-01. Necesita identificación saneada del test de CI. Requisito para L-07. | S si es sincronización del test · medio · Sol High |
| L-06 · Continuidad de operaciones conservadas | Validación y posible simplificación puntual | Tras acordar qué propuestas de L-01–04 entran. Requiere B-02 para aceptación transversal. | M · alto, pérdida de intención/permiso · Astra High |
| L-07 · Cierre de evidencia | Gate, sin capacidad nueva | L-01, L-05, L-06 y los lotes opcionales aceptados. Autorización del operador por ejecución. | M · alto · Astra High |

## L-01 · Sencillo (UC-TRA-01–04/13, UC-CAJ-04)

**Estado vigente: `L01_CANDIDATO_PENDIENTE_CONTRATO_REAL`.** Alternativa A autorizada e implementada localmente. [RUN-11](VALIDATION.md#run-11) acredita RED/GREEN con dobles HTTP. Se preparó la prueba de `StatusAsync` para PostgreSQL 18 efímero. El operador debe revisar el diff, comprobar el GET real según la receta y ejecutar CI sobre el SHA del candidato antes de aceptar.

**Resultado esperado:** el cajero puede presentar un despacho válido aun cuando la caja abierta no tenga arqueo final. `CashTransfersPage.tsx:63,98–101` condiciona la acción a ese campo. `CashSessionStatusQueries.StatusAsync` expone el esperado persistido; `DispatchCashTransferHandler`, `CashTransferAccess.Expected` y `CashTransferTransaction.DispatchScope` recalculan bajo bloqueo y revalidan actor. RUN-02 reproduce el bloqueo DOM y RUN-10 acredita ledger, concurrencia y constancia. La prioridad se justifica por cero envíos válidos con el contrato observado en el código.

1. **Caracterizar primero.** En fixture acreditado, consultar el estado HTTP de una caja abierta, importe y destino válidos. Comprobar el valor de `expectedCashAmount` y contrastar con RUN-02. Si difiere, detener el cambio de guarda y revisar el DTO/build del consumidor.
2. **A recomendada.** En React basar la habilitación en caja abierta autorizada, ámbito, destino seleccionado válido, importe positivo representable y ausencia de envío/conciliación en curso. La disponibilidad definitiva la decide el POST existente. Mostrar `cash_transfer.insufficient_cash` tras un rechazo definitivo manteniendo el importe editable. No calcular disponible con la apertura, ni sustituir null por cero. Ajustar solo el fixture discordante cuando se apruebe el lote.
3. **B opcional.** Si debe mostrarse disponibilidad antes de enviar, definir lectura aditiva de disponible vigente, instante y ámbito de caja. Aprobar visibilidad por rol, coste y consumidores (selector de caja, Sencillo); mantener intacto el esperado de cierre. Desplegar lector compatible antes del consumidor, retirar primero el consumidor para rollback. Un saldo mostrado no reserva fondos. No hay necesidad demostrada de columna ni de GRANT.
4. **Gate.** Un despacho y una recepción con dos sedes; saldo insuficiente y revocación no escriben. Dos solicitudes compiten por el último efectivo. Perder acuse permite GET de la intención original y produce exactamente un efecto. Estado abierto/lectura fallida no desbloquean un nuevo POST pendiente. Contrastar ledger, constancia y las dos auditorías con DOM; E4 requiere B-02. No cambiar confirmación de venta, cálculo contable ni candados.

## L-02 · Caja (UC-CAJ-02–07/16/17)

**Primero evidencia.** Con apertura 100 y contado 10 sin movimientos, verificar acuse, fila cerrada (100, 10, −90), auditoría única, salida de activas, lectura de arqueo por ID y reentrada con y sin URL. Revocar acceso y cambiar de sede para comprobar denegación. Tras POST confirmado y GET 404/503 no debe enviarse otro cierre; GET closed seguido de error de detalle permite reconsultar solo el arqueo.

**Decisión.** Conservar enlace original cuesta poco y no resuelve perderlo. Si se necesita historial autorizar consulta acotada por fecha y sede con orden estable, roles y periodo definidos. Proponer solo filas permitidas por `CashReconciliationAccess`; cajero propio, alcance Owner por validar. `CashPage`, `useCashClose`, `CashSessionReader`, endpoints de caja y tests de aislamiento son los consumidores/fronteras. Mantener Activas open-only. Agregar consulta de lectura antes de la UI, probar página vacía y separación tenant/sede/membresía; para revertir retirar la entrada UI. La alternativa de mostrar cerradas en Activas cambia FR-CASH-005 y se descarta. El incidente DEV sigue sin causa acreditada.

## L-03 · Descubrimiento (UC-COM-09/12/13/14, UC-CAT-06/20)

- **Compras.** Si una compra sin referencia debe encontrarse desde una entrada limpia, aprobar listado acotado por tenant, sede, permiso, estado y periodo. `PurchaseLookup`, `PurchasingStore`, lectores de compra y contrato de directorio. Contrastar dos compras con igual referencia, una sin referencia, paginación, permisos revocados y otro tenant. Elegir una fila abre el ID existente. Recuperar escritura sigue usando la clave original; la búsqueda nunca identifica una intención por nombre/documento.
- **Catálogo.** Si se aprueba buscar en todo mantenimiento, ampliar el lector paginado de `CatalogDirectoryReader` y consumidores en `ProductsPage` con filtro opcional aplicado antes de paginar. Verificar el producto que está en la segunda página, conteos coherentes, inactivos según rol y exclusión de productos ajenos. Mantener búsqueda operativa de POS y contrato anterior sin filtro. Si se prefiere la página actual, conservar su etiqueta y cerrar con RUN-04. No trasladar precios, stock ni datos globales.
- **Entrega reversible.** Lecturas optativas y UI después. Volver al consumidor anterior sin modificar filas. Cualquier índice/migración exigiría medición, plan SQL y aprobación propios.

## L-04 · Inventario (UC-COM-01/03/04)

Con `InventoryDirectoryReader` y `ProductLookup` registrar tamaño de página, IDs distintos, GET, latencia hasta nombres visibles, frío/caliente y cambio de sede. RUN-09 midió 3/0/3 **solicitudes en DOM**, sin tiempos de red. Si se cumple el objetivo operativo, conservar caché por ámbito. Si no, comparar nombre proyectado en la lectura paginada frente a consulta por IDs limitada y autorizada. Para proyección, campo aditivo, compatibilidad con consumidor anterior y retirada inicial de UI; preservar coste privado y paginación. Gate: cantidades, lotes y permisos idénticos, nombres correctos en sede B, sin lecturas cruzadas ni mutación. No precargar el catálogo completo.

## L-05 · Repetibilidad y evidencia CI (UC-TRA-08, UC-PLA-07/17)

Contrastar `TransfersPage.test.tsx:66` y `PlatformPage.test.tsx:275` con el estado de carga que RUN-03 observó y RUN-05 superó en serie. Obtener **solo nombre de archivo, caso y aserción saneada** del fallo frontend en el informe disponible del operador. Si no existe, solicitar posteriormente una ejecución que produzca ese dato. No asignarlo a uno de esos dos tests por similitud. Caracterizar el caso identificado de modo focalizado; si es una espera, observar transición visible y comprobar que acciones siguen protegidas durante la carga. Si es fallo funcional, registrar causa propia antes de tocar producto. Gate: regresión focalizada y suite del candidato sin omisiones, sin ampliar timeouts para silenciar un fallo.

## L-06 · Validar lo conservado en nueve familias

| Circuito conservado | Operación y criterio de aceptación | Frontera |
|---|---|---|
| Caja/POS, compras e inventario | Crear recepción y contrastar lote → venta con presentación y pago mixto → ticket → anulación → arqueo. Verificar stock/movimientos y efectivo exactos. Salir del borrador intacto, modificado y vaciado; contar advertencias y POST. | `useSaleDraft`, `useCashClose`; si existe una advertencia redundante, simplificar solo después de demostrar pérdida o fricción. |
| Transferencias | Aprobar, despachar, recibir parcialmente o cancelar en estado válido. Volver tras perder respuesta: una solicitud, linaje conservado, diferencia visible. | `useTrackedTransfer`, lectores de bandeja y transacciones de origen/destino. |
| Catálogo | Alta local y asociación global, precio, estado y configuración versionada; Excel parcial y reapertura del trabajo original. Productos visibles únicamente en alcance correcto. | `useCatalogWrite`, importador; no usar PUT heredado para editar. |
| Reportes y comisiones | Mismos filtros en pantalla/exportación. Reconciliar con cobro y reversión tras cambiar regla. Reposición solo interna. | `ReportsPage`, lectores SQL/ledger de comisiones. |
| Avisos y auditoría | Abrir lote del aviso y marcar leído. Otro miembro conserva su contador. Reiniciar consulta y verificar que riesgo e historia persisten, sin revelar snapshots privados. | `NotificationsPage`, lector de auditoría. |
| Acceso y plataforma | Invitación/aceptación/aprobación, sesión expirada, cambio de rol/sede/horario, último Owner y pérdida de respuesta en alta. Mantener separado al operador de plataforma y al Owner. | Resolución de acceso, `TenantStructure`, `PlatformPage`. |
| Contingencia | Desconectar/reconectar con la misma venta local; recuperar un acuse y probar conflicto por última unidad y caja cerrada, también tras cambio de identidad. | `WorkspaceOutboxSync`, dispositivos/cursor; verificar caché PWA sin credenciales reales. |

Usar referencia y saldo controlados en fixture, dos tenants, dos sedes y roles sintéticos. B-02 requiere navegador, API y PostgreSQL efímeros con autenticación de pruebas y build identificado. B-03 excluye OIDC y emisión fiscal externos. Mantener bloqueo para casos no reproducibles. No diseñar UI adicional para reposición, Recovery o CPE por el hecho de tener un servicio interno.

## L-07 · Decisión posterior

Solo tras cierre del P1 y atribución del FAIL frontend, conciliar evidencias por UC, casos exactos, SHA servido y pendientes B-02/B-03. El operador decide cualquier ejecución futura y aceptación. No se propone migrar, desplegar ni liberar. El primer encargo autorizable es **L-01, paso 1 y alternativa A condicionada a que se confirme el contrato abierto**. El historial de cajas, búsqueda ampliada y saldo informativo tienen decisiones independientes.
