# Indicios estáticos

## F-001 · Una caja cerrada deja de aparecer en activas

- UC-CAJ-05/06/07/16/17. FR-CASH-002/003/005. Evidencia E1 corroborada por la revisión externa del baseline 939ad83. P2 provisional por descubrimiento del arqueo. Un cierre con 100 inicial y 10 contado, sin movimientos, debe registrar −90. `CashCloseTransaction.cs:59–72` confirma el registro, mientras `CashSessionReader.cs:34` filtra open. La URL `cash` permite recuperar y la entrada limpia carece de historial. No hay evidencia sobre si el caso del operador se guardó. Comparar ambos GET, actor/sede y acuse según [receta Caja](flows/01-caja-pos.md). PENDIENTE_RFA2.

## F-002 · Sencillo exige saldo esperado en una caja abierta

- UC-TRA-01, UC-CAJ-04, FR-CASH-004. E1 de cableado corroborada por la revisión externa del baseline 939ad83. P1 provisional para despacho por UI. `CashTransfersPage.tsx:63,98–101` exige esperado no nulo. `CashSessionStatusQueries.cs:18–28` entrega el valor persistido y `CashSession.cs:54–60` lo establece al cerrar. `CashTransferTransaction.cs:90–96` calcula el saldo vigente al despachar. El doble `cash.test.support.tsx:13` devuelve un esperado en open; su existencia no acredita funcionamiento del circuito real. En RFA2 comparar respuesta de estado y disponibilidad del botón con caja válida y saldo inicial positivo. PENDIENTE_RFA2.

## F-003 · El filtro de mantenimiento es por página

- UC-CAT-06/20. E1. P2 provisional. `ProductsPage.tsx:48–62` filtra filas recibidas. `CatalogDirectoryReader.cs:17–22` aplica offset/límite. La UI expresa esa limitación. Medir producto en página posterior y observar si la necesidad requiere otra consulta. PENDIENTE_RFA3.

## F-004 · Consulta de compras condicionada por referencia o ID

- UC-COM-09/12. E1. P2 provisional. `PurchasingStore.cs:84–90` compara referencia exacta y `PurchaseLookup.tsx:15–42` expone referencia o código de soporte. Una recepción sin referencia puede existir y seguir sin aparecer en la búsqueda. Verificar recuperación tras recarga y salida con el ID original antes de decidir requisitos futuros. PENDIENTE_RFA2.

## F-005 · Existencias y lotes cargan el nombre por detalle

- UC-COM-03/04. E1 del circuito. P2 provisional. `InventoryDirectoryReader.cs:9–22` proyecta IDs. `ProductLookup.tsx:10–23` resuelve nombre con caché scoped por ID. Comparar cantidad de GET con caché fría, caliente y paginación. Hipótesis de coste en operación real, pendiente de medición. PENDIENTE_RFA2.
