# Hallazgos RFA

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

- UC-COM-03/04. E1 del circuito. P2 provisional. `InventoryDirectoryReader.cs:9–22` proyecta IDs. `ProductLookup.tsx:10–23` resuelve nombre con caché scoped por ID. Comparar cantidad de GET con caché fría, caliente y paginación. Hipótesis de coste en operación real, pendiente de medición. PENDIENTE_RFA2.

## F-006 · Repetibilidad de dos aserciones DOM bajo carga

**CONFIRMADO_EN_EJECUCION para el runner, P2 provisional.** RUN-03 falló en `TransfersPage.test.tsx:66` al consultar de inmediato si se había emitido el GET de acceso y en `PlatformPage.test.tsx:275` mientras el destinatario seguía cargando. Ambos casos pasaron serializados en RUN-05. El fallo de repetibilidad dificulta interpretar el gate. La espera/aserción de lectura es la causa inmediata observada. La causa de calendario exacta y un fallo equivalente de producto siguen sin demostrar. Para una aceptación posterior, medir llegada del estado y comprobar que las acciones permanecen cerradas durante la espera. UC-TRA-08, UC-PLA-07 y UC-PLA-17.
