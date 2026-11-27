# Hoja de ruta RFA · fases y lotes subordinados

Reconciliación sobre `1deb4dae9ce5f11c4f9fc0f374837c5d67e1d1d4`, aplicación `52a5ced168dbef2e80145b4e9d115113f0a63755`. La [decisión de los 156 casos](TO_BE.md) incluye los circuitos que deben conservarse.

| Fase | Propósito | Estado |
|---|---|---|
| RFA0–RFA1 | Gobierno, inventario, trazabilidad AS-IS y exclusiones | Cierre documental, 156 UC vigentes incluidos los 155 originales. |
| RFA2 | Acreditar comportamiento, persistencia, recuperación y aislamiento | Cierre de investigación con limitaciones. Cobertura funcional parcial. [RUN-21](VALIDATION.md#run-21) |
| RFA3 | Hallazgos, TO-BE, dependencias y priorización | Aprobada documentalmente en `e1df3ec`. [Hallazgos](FINDINGS.md) · [TO-BE](TO_BE.md) |
| RFA4A · Astra Alto | Correcciones focalizadas de defectos e invariantes | CIERRE_CON_PENDIENTES de F-008 y CAT-21 en el recorrido de alta local. RUN-27 acredita CREATE. P5 y variantes pendientes pasan a RFA5A. |
| RFA4B · Sol Alto | Simplificaciones, refactor y cambios de flujo aprobados | RFA4B_CANDIDATO_CIERRE. Catálogo SIMPLIFY, POS KEEP y cambios opcionales DEFER. [RUN-28](VALIDATION.md#run-28) |
| RFA5A · Sol Medio | Certificación técnica aislada | RFA5A_CANDIDATO_VALIDACION. Una ronda pendiente sobre el SHA final exacto. [Gates RFA5A](#gates-rfa5a) |
| RFA5B · Astra Alto | Aceptación del operador y decisión de puesta en marcha | Pendiente. L-07 exige E5, revisión arquitectónica y operativa, gates externos, backup/restore probado, rendimiento representativo y límites operativos antes de decidir. Producción NO-GO. |

Los lotes L-01–07 y los cortes L-06A–I organizan trabajo dentro de estas fases. Los cambios ejecutados y los escenarios acreditados conservan su propio alcance.

### Cola de decisiones

1. **RFA4A · cierre con pendientes.** Preservar lo acreditado en L-01, L-02/L-06A, L-05 y L-06B. Los escenarios pendientes pasan a los gates RFA5A.
2. **RFA5A · fallo histórico.** El gate frontend 645/646 queda pendiente de certificación del SHA final.
3. **RFA4B · opcionales y KEEP.** Priorizar por riesgo las variantes de L-06 sobre flujo vigente. F-004 requiere decidir si se necesita un directorio de recepciones recientes. F-005 requiere medición representativa y equivalencia antes de optimizar. El saldo informativo de Sencillo y otros refactors requieren aprobación propia.
4. **RFA5A/B · L-07.** Tras cerrar los bloqueos críticos, medir integridad y operación en destino aislado, ejecutar los gates integrales y documentar restauración, rendimiento y externos. Obtener E5 del operador antes de decidir release.

### Primer encargo RFA4A

**Estado del encargo.** RUN-25 acreditó F-008 en el alta local hasta la reentrada. RUN-27 acredita además la conciliación sin reenvío y sus efectos persistidos. CAT-10 y P5 completo se transfieren a RFA5A. [RUN-25](VALIDATION.md#run-25) · [RUN-27](VALIDATION.md#run-27)

- **Alcance.** [F-008](FINDINGS.md#f-008--el-piloto-de-alta-local-falla-en-la-pérdida-inducida-del-acuse), CAT-09/21 y regresión focalizada CAT-10. FR-CAT-001/003/007. El intento B-02 usa el fixture efímero PostgreSQL 18, Chromium aislado, SHA/build fijados y Owner sintético con sesión, categoría y código nuevo. P0–P4 y SEARCH sirven como controles.
- **Prueba discriminante mínima:** justo antes del único clic, comprobar formulario revisado y botón accionable y observar el POST exacto independientemente de `RouteAsync`. Dentro del helper, marcar intercepción, categoría HTTP permitida (201, 400, 401, 403, 409, 429, 5xx u otra), ID válido y conclusión del aborto por separado. Después, distinguir botón y marcador pendiente. Publicar solo marcadores estáticos y estado permitido. Mantener timeout, aserciones y error del runner. Conservar la primera frontera fallida aun si una comprobación independiente también falla.
- **Lectura independiente y aceptación:** en scope runtime del mismo tenant consultar por el código sintético y contar producto, auditoría `BusinessProductCreated` y efectos de stock. Separar ausencia de solicitud, rechazo definitivo, 201 con commit y fallo de descarte o guarda UI. Si llega al caso feliz, exigir un solo POST, una fila, una auditoría del mismo ID y actor, cero stock, bloqueo al perder el acuse y recuperación del mismo ID sin reenviar. Ausencia de fila en una lectura temprana no prueba rechazo.
- **Corrección y reversión:** si la causa es el arnés, limitar el cambio a esa frontera y conservar los demás pilotos. Si se demuestra defecto de producto, RED/GREEN solo sobre el símbolo responsable, con permisos, RLS, exactitud, auditoría y contrato actual. El diagnóstico debe poder retirarse sin cambiar persistencia ni contratos. Cambios de contrato, reglas o constancia durable requieren autorización separada. Sin evidencia suficiente, registrar el bloqueo exacto y detener el intento sin abrir otra ronda automática.

| Lote subordinado | Tipo / prioridad | Dependencia y decisión del operador | Tamaño · riesgo · esfuerzo orientativo |
|---|---|---|---|
| L-01 · Despachar sencillo | Bugfix P1, F-002 | A ejecutada y acreditada para Owner. Variantes de rol pendientes. B opcional si se requiere saldo visible. | S para A, M para B · alto, dinero · Astra High |
| L-02 · Relocalizar arqueos | Brecha de recuperación P2, F-001 | Cierres ejecutado y recorrido acreditado en L-06A. Variantes de actor/periodo pendientes. | M · alto, privacidad/dinero · Astra High |
| L-03 · Localizar compras y productos | Brechas P2, F-003/004 | Catálogo SEARCH acreditado. Recepciones recientes es capacidad opcional y requiere decisión explícita. | M · medio · Sol High |
| L-04 · Coste de existencias/lotes | Medición, F-005 | Requiere página representativa y presupuesto de latencia acordado. Puede terminar sin cambio. | S medir, M proyectar · medio, aislamiento · Sol High |
| L-05 · Repetibilidad | Calidad de tests, F-006 y fallo CI sin atribuir | Sincronización y reporte saneado ejecutados. Aserción de Compras aún ausente. Revalidarla en gate final o antes si aparece evidencia específica. | S si es sincronización del test · medio · Sol High |
| L-06 · Continuidad de operaciones conservadas | Validación y posible simplificación puntual | Tras acordar qué propuestas de L-01–04 entran. Requiere B-02 para aceptación transversal. | M · alto, pérdida de intención/permiso · Astra High |
| L-07 · Cierre de evidencia | Gate, sin capacidad nueva | L-01, L-05, L-06 y los lotes opcionales aceptados. Autorización del operador por ejecución. | M · alto · Astra High |

## L-01 · Sencillo (UC-TRA-01–04/13, UC-CAJ-04)

**Estado vigente.** Alternativa A implementada. [RUN-15](VALIDATION.md#run-15) acredita el GET real de caja abierta y el despacho y la recepción por Owner en dos sedes. Quedan las variantes E4 de los demás roles y el gate frontend.

**Resultado esperado:** el cajero puede presentar un despacho válido aun cuando la caja abierta no tenga arqueo final. `CashTransfersPage.tsx:63,98–101` condiciona la acción a ese campo. `CashSessionStatusQueries.StatusAsync` expone el esperado persistido; `DispatchCashTransferHandler`, `CashTransferAccess.Expected` y `CashTransferTransaction.DispatchScope` recalculan bajo bloqueo y revalidan actor. RUN-02 reproduce el bloqueo DOM y RUN-10 acredita ledger, concurrencia y constancia. La prioridad se justifica por cero envíos válidos con el contrato observado en el código.

1. **Caracterizar primero.** En fixture acreditado, consultar el estado HTTP de una caja abierta, importe y destino válidos. Comprobar el valor de `expectedCashAmount` y contrastar con RUN-02. Si difiere, detener el cambio de guarda y revisar el DTO/build del consumidor.
2. **A recomendada.** En React basar la habilitación en caja abierta autorizada, ámbito, destino seleccionado válido, importe positivo representable y ausencia de envío/conciliación en curso. La disponibilidad definitiva la decide el POST existente. Mostrar `cash_transfer.insufficient_cash` tras un rechazo definitivo manteniendo el importe editable. No calcular disponible con la apertura, ni sustituir null por cero. Ajustar solo el fixture discordante cuando se apruebe el lote.
3. **B opcional.** Si debe mostrarse disponibilidad antes de enviar, definir lectura aditiva de disponible vigente, instante y ámbito de caja. Aprobar visibilidad por rol, coste y consumidores (selector de caja, Sencillo); mantener intacto el esperado de cierre. Desplegar lector compatible antes del consumidor, retirar primero el consumidor para rollback. Un saldo mostrado no reserva fondos. No hay necesidad demostrada de columna ni de GRANT.
4. **Gate.** Un despacho y una recepción con dos sedes; saldo insuficiente y revocación no escriben. Dos solicitudes compiten por el último efectivo. Perder acuse permite GET de la intención original y produce exactamente un efecto. Estado abierto/lectura fallida no desbloquean un nuevo POST pendiente. Contrastar ledger, constancia y las dos auditorías con DOM; E4 requiere B-02. No cambiar confirmación de venta, cálculo contable ni candados.

## L-02 · Caja (UC-CAJ-02–07/16/17)

**L-06A acreditado para el recorrido de RUN-17.** P3 verifica reentrada por Cierres y recuperación tras perder el acuse. P4 añade negativas por sede, cierres propios de Cajero y paginación Owner. [RUN-17](VALIDATION.md#run-17) delimita las variantes restantes. [RUN-15](VALIDATION.md#run-15) preserva la brecha del baseline.

**Primero evidencia.** Con apertura 100 y contado 10 sin movimientos, verificar acuse, fila cerrada (100, 10, −90), auditoría única, salida de activas, lectura de arqueo por ID y reentrada con y sin URL. Revocar acceso y cambiar de sede para comprobar denegación. Tras POST confirmado y GET 404/503 no debe enviarse otro cierre; GET closed seguido de error de detalle permite reconsultar solo el arqueo.

**Decisión.** Conservar enlace original cuesta poco y no resuelve perderlo. Si se necesita historial autorizar consulta acotada por fecha y sede con orden estable, roles y periodo definidos. Proponer solo filas permitidas por `CashReconciliationAccess`; cajero propio, alcance Owner por validar. `CashPage`, `useCashClose`, `CashSessionReader`, endpoints de caja y tests de aislamiento son los consumidores/fronteras. Mantener Activas open-only. Agregar consulta de lectura antes de la UI, probar página vacía y separación tenant/sede/membresía; para revertir retirar la entrada UI. La alternativa de mostrar cerradas en Activas cambia FR-CASH-005 y se descarta. El incidente DEV sigue sin causa acreditada.

## L-03 · Descubrimiento (UC-COM-09/12/13/14, UC-CAT-06/20)

- **Compras.** Si una compra sin referencia debe encontrarse desde una entrada limpia, aprobar listado acotado por tenant, sede, permiso, estado y periodo. `PurchaseLookup`, `PurchasingStore`, lectores de compra y contrato de directorio. Contrastar dos compras con igual referencia, una sin referencia, paginación, permisos revocados y otro tenant. Elegir una fila abre el ID existente. Recuperar escritura sigue usando la clave original; la búsqueda nunca identifica una intención por nombre/documento.
- **Catálogo.** El operador autorizó la búsqueda en todo Mis productos. L-06B implementa filtros previos a paginar con respuesta compatible y conserva la búsqueda POS. SEARCH acreditado en [RUN-21](VALIDATION.md#run-21). La recuperación del alta queda bajo F-008 y F-007.
- **Entrega reversible.** Lecturas optativas y UI después. Volver al consumidor anterior sin modificar filas. Cualquier índice/migración exigiría medición, plan SQL y aprobación propios.

## L-04 · Inventario (UC-COM-01/03/04)

Con `InventoryDirectoryReader` y `ProductLookup` registrar tamaño de página, IDs distintos, GET, latencia hasta nombres visibles, frío/caliente y cambio de sede. RUN-09 midió 3/0/3 **solicitudes en DOM**, sin tiempos de red. Si se cumple el objetivo operativo, conservar caché por ámbito. Si no, comparar nombre proyectado en la lectura paginada frente a consulta por IDs limitada y autorizada. Para proyección, campo aditivo, compatibilidad con consumidor anterior y retirada inicial de UI; preservar coste privado y paginación. Gate: cantidades, lotes y permisos idénticos, nombres correctos en sede B, sin lecturas cruzadas ni mutación. No precargar el catálogo completo.

## L-05 · Repetibilidad y evidencia CI (UC-TRA-08, UC-PLA-07/17)

**Diagnóstico CI verificado, gate frontend abierto.** El resumen identificó Compras como único FAIL de RUN-13. Los casos F-006 pasaron en ese SHA. El caso de Compras pasó focalizado y dentro de su archivo, y queda NOT_REPRODUCED localmente. [RUN-13](VALIDATION.md#run-13).

Contrastar `TransfersPage.test.tsx:66` y `PlatformPage.test.tsx:275` con el estado de carga que RUN-03 observó y RUN-05 superó en serie. Obtener **solo nombre de archivo, caso y aserción saneada** del fallo frontend en el informe disponible del operador. Si no existe, solicitar posteriormente una ejecución que produzca ese dato. No asignarlo a uno de esos dos tests por similitud. Caracterizar el caso identificado de modo focalizado; si es una espera, observar transición visible y comprobar que acciones siguen protegidas durante la carga. Si es fallo funcional, registrar causa propia antes de tocar producto. Gate: regresión focalizada y suite del candidato sin omisiones, sin ampliar timeouts para silenciar un fallo.

## L-06 · Aceptación funcional por familia

La [matriz](MATRIX.csv) y el [TO-BE](TO_BE.md) definen el inventario y sus decisiones iniciales. KEEP conserva la operación y aún requiere aceptación. A tiene evidencia ejecutada, B queda como candidato con gate fallido y C–I están suspendidos. Cada corte registra actor, tarea, pasos y estados AS-IS, requisito, evidencia, fricción o defecto, decisión KEEP/FIX/SIMPLIFY/BRECHA/VALIDATE y prueba de aceptación. Justificar KEEP, los bloqueos y N/A. Un escenario puede cubrir varios UC con aserciones explícitas. Incluir errores, espera, permisos, reentrada y pérdida de respuesta cuando apliquen. Medir clics o solicitudes cuando informen una decisión y conservar Blue Studio/UAX e invariantes con cambios y regresiones focalizados.

| Corte | Casos | Recorrido y criterio de aceptación | Dependencia o decisión |
|---|---|---|---|
| **L-06A · Caja/POS** | UC-CAJ-01–19 (19) | Apertura, borrador, pagos mixtos, ticket, anulación y arqueo. Pérdida de acuse, reentrada y efectos persistidos. | B-02 P0–P4 PASS en RUN-17. Variantes y E5 pendientes. |
| **L-06B · Catálogo** | UC-CAT-01–22 (22) | Filtrar, dar de alta/asociar, editar precio, estado y unidades versionadas. Importar parcialmente y recuperar la intención original. | SEARCH, alta local y CAT-21 del original acreditados. EDIT/UNITS bloquea P5. [RUN-27](VALIDATION.md#run-27) |
| **L-06C · Compras e inventario** | UC-COM-01–15 (15) | Owner/Pharmacist autorizado elige proveedor, prepara recepción, revisa, confirma y vuelve a ella. Si otro editor cambia líneas, un POST con versión revisada retorna conflicto y preserva borrador y stock. Confirmación válida genera lotes, saldos, caducidad y movimientos exactos. | Incorporar descubrimiento L-03 si se aprueba. Medir coste de nombres según L-04 antes de proponer cambios. Incluir el caso CI identificado en RUN-13. |
| **L-06D · Transferencias** | UC-TRA-01–13 (13) | Owner/roles habilitados prueban Sencillo y mercadería por estado. Comprobar solicitud, aprobación, despacho, recepción completa/parcial según contrato, cancelación permitida, concurrencia, origen/destino y constancia original. | RUN-15 acredita Sencillo con Owner. Variantes pendientes según matriz. |
| **L-06E · Reportes y comisiones** | UC-REP-01–17 (17) | Owner filtra y contrasta pantalla/exportación con ventas, anulaciones y ledger. Cambiar regla y comprobar que la reversión compensa el importe original. Reposición según contrato interno. | Reutilizar datos conciliados de A/C/D. |
| **L-06F · Avisos y auditoría** | UC-AVI-01–07 (7) | Operador abre aviso y navega a detalle permitido. Lectura personal y repetida conserva el estado del otro miembro y del riesgo. Owner verifica metadatos y visibilidad. | Reutilizar lote con riesgo de C y efectos auditados del resto. |
| **L-06G · Acceso y estructura** | UC-ACC-01–27 (27) | Owner y personal ejercitan onboarding, invitación, roles, sedes y horarios. Cambiar contexto, verificar expiración y revocación, proteger el último Owner y los pendientes del ámbito original. | Identidades sintéticas separadas y variantes de licencia/membresía antes de los flujos que las usan. |
| **L-06H · Plataforma** | UC-PLA-01–18 (18) | Operador administra tenants, invitación inicial, licencias, catálogo y consultas de soporte. Verificar recuperación y separación de autoridad comercial. Purga solo con datos sintéticos y autorización específica. | Vincular con G para comprobar que la designación y el rol comercial no otorgan acceso de plataforma. |
| **L-06I · Contingencia e integraciones** | UC-INT-01–18 (18) | Operador prepara dispositivo, trabaja offline, reentra y sincroniza el mismo lote/UUID. Conciliar conflictos, caja cerrada, cambio de identidad y caché PWA. Usar integraciones simuladas dentro de su contrato. | Componer con A/B/G. Decisiones B-03 separadas para servicios externos y fiscales. |

### Habilitador común B-02

**Habilitador validado para P0–P4 de L-06A.** [RUN-17](VALIDATION.md#run-17) · [Guía del arnés](../../../backend/tests/MediPOS.E2E/README.md). RUN-19 localizó P5. [RUN-21](VALIDATION.md#run-21) localizó CREATE/POST_LOST_ACK. Cada lote conserva su aprobación individual.

Solicitar una sola aprobación para preparar y reutilizar navegador, API y PostgreSQL efímero con autenticación sintética. El preflight debe acreditar SHA/build, identidad del contenedor/BD, roles mínimos con RLS, sesiones de prueba y dos tenants/sedes. Usar puertos y perfil exclusivos, impedir conexión a DEV o producción y tráfico hacia OAuth, CPE u otros servicios reales. Limitar la purga a una prueba sintética específicamente autorizada. Registrar alcance y evidencias saneadas y limpiar solo recursos del fixture. Reutilizar la misma composición para los nueve cortes; su diseño e implementación requieren un encargo propio.

## L-07 · Cierre de evidencia y decisión de release

Conciliar los 156 UC vigentes y cualquier caso nuevo justificado con decisión, implementación aprobada cuando aplique, prueba acorde al riesgo y pendientes explícitos. Exigir E4 para recorridos críticos y E5 para aceptación del operador. Nombrar el SHA y build verificados. Producción permanece NO-GO mientras falle un gate requerido, exista un P0/P1 de release abierto o haya datos sin conciliar.

### Gates RFA5A

Ejecutar sobre un SHA final publicado por el operador y un build identificado, con el aislamiento acreditado de B-02 y PostgreSQL 18 de pruebas. Prioridad por riesgo:

1. **Unidades CAT-13/14, F-009.** Identificar aserción y causa de UNITS antes de corregir. Verificar GET, PUT exacto, versión obsoleta y 409, y leer de forma independiente versión, unidades y auditoría. Contrastar ausencia de cambios en stock e historial.
2. **P5 completo.** Conservar controles P0–P4, producto y conciliación del original tras RUN-28. Acreditar las etapas posteriores, incluida compatibilidad de unidades, estado, asociación global, importación, persistencia final de precios, permisos y auditoría.
3. **Integridad y aislamiento.** Gates backend, arquitectura y PostgreSQL 18 real. Priorizar dinero, stock, concurrencia, roles, revocación y RLS multiempresa, offline y recuperación según las variantes de la matriz. Reutilizar evidencia por aserción. E4 se exige en los recorridos críticos.
4. **Frontend integral.** Builds, lint y suite completa. Mantener el antecedente 645/646 de otro SHA y aislar el test de Compras si vuelve a fallar, preservando la revisión obsoleta, el borrador y el bloqueo de escrituras.

**Ejecución coordinada.** Publicar una vez en `ci/postgres-diagnostics-8bc6b4c` y verificar el SHA final. El push activa B-02 por los cambios del arnés y workflow. Usar dispatch de B-02 solo si el push no lo activó. En paralelo, seleccionar esa rama en Backend validation - PostgreSQL 18 y hacer un único dispatch. Ambos runs deben indicar el mismo SHA. El gate integral exige PostgreSQL 18 real, builds, formato, suites backend y arquitectura y el job frontend completo. Registrar resultados PASS/FAIL/SKIP y los marcadores de aprobación solo si los emiten los workflows.

Reservar RUN-30 para SHA, build, runs, intentos, resultados y alcance por aserción. Si falla algún gate, registrar RFA5A_CIERRE_CON_BLOQUEOS y transferirlo a RFA5B. RFA5B conserva E5, restauración, rendimiento y gates externos para decidir producción. [Checkpoint](CHECKPOINT.md)
