# Hallazgos RFA

## Diagnóstico vigente · RUN-10 / RFA3

Los seis IDs se conservan. Los párrafos de caracterización bajo esta tabla documentan los cortes anteriores. [Pruebas y aserciones en el SHA del run](VALIDATION.md#run-10) · [decisiones, dependencias y aceptación por lote](ROADMAP.md).

| ID · tipo · prioridad | Actor y expectativa | Causa, confianza y alcance | Decisión mínima, prueba y riesgo de regresión |
|---|---|---|---|
| F-001 · descubrimiento de arqueo · P2 | Cajero/Owner cierra y vuelve por entrada limpia. FR-CASH-002/003 exigen arqueo y FR-CASH-005 directorio de abiertas. Un historial para cerradas es una capacidad adicional. | Alta en la separación de lecturas. SC-01 E2 muestra el vacío y la recuperación con URL original. E3 acredita fila, signo, auditoría y relectura por ID en un cierre análogo. Causa del incidente DEV desconocida. | L-02. Decidir roles y periodo de historial. Verificar 100→10 exacto con vacío en activas y relectura de −90 por el actor autorizado. Riesgo: exponer arqueos ajenos o permitir segundo cierre. |
| F-002 · defecto de habilitación · P1 | Cajero con caja abierta y destino válido intenta despachar. FR-CASH-004 exige préstamo vinculado y auditable. | Alta en la guarda incompatible: E2 con valor nulo bloquea el botón; E3 acredita saldo transaccional aun sin arqueo final. Falta ligar GET abierto y UI en el mismo sistema aislado. | L-01. Aprobar envío sujeto al saldo del ledger; decidir aparte si mostrar un disponible informativo. Insuficiente y carrera con reembolso no deben producir sobregiro. Pendiente conserva clave, sin otro POST. |
| F-003 · alcance de búsqueda · P2 | Owner filtra una página que no contiene el producto. FR-CAT-001 define catálogo privado/global; no establece búsqueda administrativa transversal. | Alta en el alcance local por RUN-04; necesidad de buscar sobre todo el directorio pendiente de producto. La búsqueda POS E3 tiene otra finalidad. | L-03. Conservar etiqueta o aprobar consulta paginada global al negocio. Caso de segunda página, rol y exclusión de otro tenant. Riesgo: presentar vacío de página como ausencia total. |
| F-004 · descubrimiento de compra · P2 | Operador intenta reabrir recepción sin referencia. FR-PUR-004 pide buscar el documento/referencia y FR-PUR-002 protege la recepción. | Alta en coincidencia exacta y recuperación por ID. RUN-04 lo observa en DOM. E3 contrasta dos compras por la misma referencia, detalle de la sede y exclusión de ajenas. No se ha demostrado pérdida de la compra. | L-03. Decidir si el operador requiere recientes sin documento. Probar salida, entrada limpia, dos referencias iguales y una ausente. Riesgo: usar referencia duplicada como clave para rehacer compra. |
| F-005 · coste de lectura · P2 provisional | Personal de inventario cambia de existencias a lotes y necesita nombres legibles de su sede. FR-INV-001/FR-TEN-003 son invariantes, sin objetivo de latencia acordado. | Alta en la cuenta DOM 3/0/3 de RUN-09. Coste de red y demora percibida desconocidos. E3 verifica ámbito y cantidades de las lecturas, sin cronometrar el navegador. | L-04. Medir y acordar presupuesto antes de ampliar DTO. Eliminar lecturas solo si se conserva nombre, ámbito y paginación. Riesgo: reutilizar etiquetas de otra sede. |
| F-006 · repetibilidad de pruebas · P2 provisional | QA navega por Transferencias o Plataforma. El control debe esperar el resultado de acceso o la carga del destinatario. Sin requisito FR de sincronización de tests. | Alta para los dos resultados discordantes RUN-03/05; causa de temporización específica y efecto en producto sin demostrar. RUN-10 tiene un FAIL frontend sin nombre. | L-05. Identificar el caso de CI y caracterizar las transiciones observables antes de ajustar esperas. Riesgo: esconder una carrera de autorización o duplicar el diagnóstico de un fallo desconocido. |

El P1 tiene reproducción DOM y bloqueo E4 específico B-02. Ninguno de los seis demuestra pérdida de dinero, fuga entre tenants ni corrupción de stock. Se mantienen las pruebas de esos invariantes como condición de corrección.

La evidencia de ejecución se vincula al [registro RFA2](VALIDATION.md). E2 se limita a las fronteras indicadas. Los diagnósticos originales siguen abajo.

## F-001 · Una caja cerrada deja de aparecer en activas

**RFA2 · CONFIRMADO_EN_EJECUCION E2 para la reentrada, BLOCKED en E3/E4.** SC-01 / RUN-07 envió contado 10 una sola vez, mostró el faltante 90 de la respuesta simulada, vio activas vacías con URL limpia y recuperó el arqueo original por GET al volver con su dirección. RUN-01 verificó lecturas fallidas y respuestas inciertas. Fila, auditoría, importes calculados por el servidor y reentrada en navegador real requieren E3/E4. El incidente DEV permanece sin atribución.

- UC-CAJ-05/06/07/16/17. FR-CASH-002/003/005. Evidencia E1 corroborada por la revisión externa del baseline 939ad83. P2 provisional por descubrimiento del arqueo. Un cierre con 100 inicial y 10 contado, sin movimientos, debe registrar −90. `CashCloseTransaction.cs:59–72` confirma el registro, mientras `CashSessionReader.cs:34` filtra open. La URL `cash` permite recuperar y la entrada limpia carece de historial. No hay evidencia sobre si el caso del operador se guardó. Comparar ambos GET, actor/sede y acuse según [receta Caja](flows/01-caja-pos.md). PENDIENTE_RFA2.

## F-002 · Sencillo exige saldo esperado en una caja abierta

**RFA2 · CONFIRMADO_EN_EJECUCION E2, P1 provisional.** SC-02 / RUN-02 caracteriza que, con estado `open` y `expectedCashAmount: null` según el contrato inspeccionado, el botón queda deshabilitado y se envían cero POST aun con apertura positiva y destino elegido. El doble heredado con esperado no nulo sí permite enviar. Falta probar respuesta real del servidor, cálculo/recepción y la misma UI en navegador aislado.

- UC-TRA-01, UC-CAJ-04, FR-CASH-004. E1 de cableado corroborada por la revisión externa del baseline 939ad83. P1 provisional para despacho por UI. `CashTransfersPage.tsx:63,98–101` exige esperado no nulo. `CashSessionStatusQueries.cs:18–28` entrega el valor persistido y `CashSession.cs:54–60` lo establece al cerrar. `CashTransferTransaction.cs:90–96` calcula el saldo vigente al despachar. El doble `cash.test.support.tsx:13` devuelve un esperado en open; su existencia no acredita funcionamiento del circuito real. En RFA2 comparar respuesta de estado y disponibilidad del botón con caja válida y saldo inicial positivo. PENDIENTE_RFA2.

## F-003 · El filtro de mantenimiento es por página

**RFA2 · CONFIRMADO_EN_EJECUCION E2.** SC-04 / RUN-04 muestra el vacío en la primera página y el producto al pasar a la segunda. El filtro estaba identificado como de página. El comportamiento coincide con el alcance visible, sin violación de contrato demostrada.

- UC-CAT-06/20. E1. P2 provisional. `ProductsPage.tsx:48–62` filtra filas recibidas. `CatalogDirectoryReader.cs:17–22` aplica offset/límite. La UI expresa esa limitación. Medir producto en página posterior y observar si la necesidad requiere otra consulta. PENDIENTE_RFA3.

## F-004 · Consulta de compras condicionada por referencia o ID

**RFA2 · CONFIRMADO_EN_EJECUCION E2.** SC-05 / RUN-04 abre una recepción sintética sin referencia mediante el ID original. En la misma entrada, la búsqueda por referencia no devuelve coincidencias. Hubo cero escrituras. Persistencia y descubrimiento real tras limpiar URL pendientes.

- UC-COM-09/12. E1. P2 provisional. `PurchasingStore.cs:84–90` compara referencia exacta y `PurchaseLookup.tsx:15–42` expone referencia o código de soporte. Una recepción sin referencia puede existir y seguir sin aparecer en la búsqueda. Verificar recuperación tras recarga y salida con el ID original antes de decidir requisitos futuros. PENDIENTE_RFA2.

## F-005 · Existencias y lotes cargan el nombre por detalle

**RFA2 · CONFIRMADO_EN_EJECUCION E2.** SC-06 / RUN-04 cuenta un GET de detalle en frío y cero GET adicionales al abrir Lotes con el mismo ID y ámbito. El posible costo con varios IDs, caché vencida, nuevo ámbito o red real queda pendiente de medición.

**02B · RUN-09.** Con tres IDs distintos, la UI solicitó tres detalles en frío, reutilizó los tres al pasar a Lotes y solicitó tres nuevos al cambiar de sede, sin etiquetas de la sede anterior. Transporte simulado, sin medición de latencia o tráfico real. Se conserva P2 provisional.

- UC-COM-03/04. E1 del circuito. P2 provisional. `InventoryDirectoryReader.cs:9–22` proyecta IDs. `ProductLookup.tsx:10–23` resuelve nombre con caché scoped por ID. Comparar cantidad de GET con caché fría, caliente y paginación. Hipótesis de coste en operación real, pendiente de medición. PENDIENTE_RFA2.

## F-006 · Repetibilidad de dos aserciones DOM bajo carga

**CONFIRMADO_EN_EJECUCION para el runner, P2 provisional.** RUN-03 falló en `TransfersPage.test.tsx:66` al consultar de inmediato si se había emitido el GET de acceso y en `PlatformPage.test.tsx:275` mientras el destinatario seguía cargando. Ambos casos pasaron serializados en RUN-05. El fallo de repetibilidad dificulta interpretar el gate. La espera/aserción de lectura es la causa inmediata observada. La causa de calendario exacta y un fallo equivalente de producto siguen sin demostrar. Para una aceptación posterior, medir llegada del estado y comprobar que las acciones permanecen cerradas durante la espera. UC-TRA-08, UC-PLA-07 y UC-PLA-17.
