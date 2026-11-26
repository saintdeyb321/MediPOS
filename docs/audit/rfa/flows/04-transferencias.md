# Transferencias

**Corte vigente.** Las etiquetas por UC incorporan [RUN-10](../VALIDATION.md#run-10). La columna de aserciones delimita el escenario ejecutado. Las recetas AS-IS describen el diagnóstico anterior. [Decisiones de diseño](../TO_BE.md).

[Validación RFA2](../VALIDATION.md) · SC-02 / SC-07. E2 identifica la frontera con dobles o pruebas locales. BLOCKED indica una frontera esencial sin evidencia aislada.

Mercadería conserva aprobación, despacho y recepción separados. El cajero no adquiere autoridad de origen por tener la sede destino. Sencillo requiere Owner/Cashier y caja autorizada de la sede. Ambas familias guardan ID/intención mediante `useTrackedTransfer.ts:58–113`. Ver el [recorrido propuesto](../TO_BE.md).

```mermaid
stateDiagram-v2
  [*] --> requested
  requested --> approved
  requested --> cancelled
  approved --> cancelled
  approved --> in_transit
  in_transit --> received
```

## UC-TRA-01

POST `/api/tenants/{tenantId}/branches/{branchId}/cash-transfers` · `backend/src/MediPOS.Api/Tenant/CashTransferEndpoints.cs:19`. **E3 · IMPLEMENTADO · FAIL**. Elegir caja/sede/importe y confirmar. CashTransfersPage.tsx:56–65,98–101 requiere saldo esperado no nulo. CashSessionStatusQueries.cs:18–28 retorna valor persistido, nulo para open (CashSession.cs:54–60). La UI bloquea el flujo normal. En API el candado y cálculo vigente los realiza CashTransferTransaction.cs:57–73,90–96; la constancia se asocia a la intención original. Requisito FR-CASH-004. Indicio F-002.

## UC-TRA-02

GET `/api/tenants/{tenantId}/cash-transfers/pending` · `backend/src/MediPOS.Api/Tenant/CashTransferEndpoints.cs:21`. **E3 · IMPLEMENTADO · PASS**. En Sencillo abre envíos pendientes y elige sede destino si corresponde. GetPendingCashTransfersHandler.cs:15–22 exige acceso al destino; el directorio sin destino exige Owner. CashTransferReader.cs:15–24 filtra InTransit por tenant/destino y pagina. La lista permite abrir el mismo envío después de navegar.

## UC-TRA-03

GET `/api/tenants/{tenantId}/cash-transfers/{cashTransferId}` · `backend/src/MediPOS.Api/Tenant/CashTransferEndpoints.cs:24`. **E3 · IMPLEMENTADO · PASS**. Pulsa Ver o consulta el ID original de un préstamo. GetCashTransferHandler.cs:14–22 aplica admisión y alcance en sede origen o destino; CashTransferReader.cs:10–14 lee por tenant/ID. Recibido puede aparecer en detalle aunque ya no esté en pendientes. No inferir pérdida por lista vacía.

## UC-TRA-04

POST `/api/tenants/{tenantId}/cash-transfers/{cashTransferId}/receive` · `backend/src/MediPOS.Api/Tenant/CashTransferEndpoints.cs:27`. **E3 · IMPLEMENTADO · BLOCKED**. Desde destino, elige caja abierta y confirma la recepción completa. ReceiveCashTransferHandler.cs:13–29 resuelve acceso antes y durante el bloqueo, valida caja destino y rechaza ya recibido. CashTransferTransaction.cs:37–52,107–125 guarda transición y auditoría. 200 devuelve estado; si no llega, consultar original (UC-TRA-03) y verificar caja destino antes de habilitar otra acción.

## UC-TRA-05

GET `/api/tenants/{tenantId}/transfers` · `backend/src/MediPOS.Api/Tenant/TransferEndpoints.cs:21`. **E3 · IMPLEMENTADO · BLOCKED**. Abrir Mercadería y filtrar sede/estado. GetTransfersHandler.cs:16–32 valida página, estado y sede, reservando consulta sin sede para Owner. TransferDirectoryReader.cs:9 y siguientes filtra tenant y ambos lados del traslado y proyecta paginado. Cambiar filtro causa una nueva lectura.

## UC-TRA-06

POST `/api/tenants/{tenantId}/transfers` · `backend/src/MediPOS.Api/Tenant/TransferEndpoints.cs:24`. **E3 · IMPLEMENTADO · BLOCKED**. Crear solicitud desde sede destino, con sede origen y líneas por presentación. RequestTransferHandler.cs:17–38 valida sede destino, datos de catálogo y cantidades. TransferStores.cs:13–36 abre ámbito, guarda solicitud/líneas/evento/auditoría. RecoverableOperations.cs:39–101 completa acuse; reconsulta no reenvía.

## UC-TRA-07

GET `/api/tenants/{tenantId}/transfers/{transferId}` · `backend/src/MediPOS.Api/Tenant/TransferEndpoints.cs:26`. **E3 · IMPLEMENTADO · BLOCKED**. Abrir detalle por ID y ver líneas/eventos/asignaciones. TransferOperation.cs:13–20 valida tenant/ID e integridad y GetTransferHandler confirma visibilidad. TransferStores.cs:41–62 proyecta eventos, lotes/nombres y estado. Lectura no mueve stock.

## UC-TRA-08

POST `/api/tenants/{tenantId}/transfers/{transferId}/approve` · `backend/src/MediPOS.Api/Tenant/TransferEndpoints.cs:29`. **E3 · IMPLEMENTADO · BLOCKED**. Abrir solicitud y aprobar desde contexto habilitado. TransferOperation.cs:22–35 reautoriza acción tras bloqueo de fila, y ApproveTransferHandler.cs:16–21 agrega evento. TransferTransaction.cs:26,66–79 commitea estado y auditoría; origen autoriza antes de despachar.

## UC-TRA-09

POST `/api/tenants/{tenantId}/transfers/{transferId}/cancel` · `backend/src/MediPOS.Api/Tenant/TransferEndpoints.cs:34`. **E3 · IMPLEMENTADO · BLOCKED**. Elegir cancelar y motivo antes de despacho. CancelTransferHandler.cs:17–23 admite requested/approved; TransferOperation.cs:22–35 verifica acceso de transición dentro del bloqueo. TransferTransaction.cs:66–79 guarda motivo, evento y auditoría. Tras despachar falta transición cancelar y la API lo niega.

## UC-TRA-10

POST `/api/tenants/{tenantId}/transfers/{transferId}/dispatch` · `backend/src/MediPOS.Api/Tenant/TransferEndpoints.cs:39`. **E3 · IMPLEMENTADO · BLOCKED**. En origen con aprobación, elegir lotes y confirmar despacho. TransferOperation.cs:22–35 revalida actor de despacho tras bloquear. TransferTransaction.cs:46–67 bloquea lotes y conserva custodias y movimientos. DispatchTransferHandler.cs:18–33 exige approved; acuse in_transit.

## UC-TRA-11

POST `/api/tenants/{tenantId}/transfers/{transferId}/receive` · `backend/src/MediPOS.Api/Tenant/TransferEndpoints.cs:44`. **E3 · IMPLEMENTADO · BLOCKED**. En destino, confirmar recepción y diferencias por asignación. TransferOperation.cs:22–35 revalida actor receptor tras el bloqueo; ReceiveTransferHandler.cs:19–32 exige in_transit y procesa diferencias. TransferTransaction.cs:66–79 registra lotes/movimientos/estado y auditoría. Recibir de nuevo no repite efectos.

## UC-TRA-12

Recuperar solicitud original de mercadería. **E3 · IMPLEMENTADO · BLOCKED**. Consultar constancia antes de otra solicitud; si cambió a un estado posterior conciliable se privilegia registro original. useTrackedTransfer.ts:58–113 mantiene requestId, bloquea nuevas escrituras y consulta GET por ID o constancia. Constancia ejercitada en PostgreSQL en RUN-10; reentrada en navegador aislado pendiente.

## UC-TRA-13

Recuperar despacho original de sencillo. **E3 · IMPLEMENTADO · BLOCKED**. Consultar original aun si ya no figura entre pendientes. useTrackedTransfer.ts:91–113 y CashTransfersPage.tsx sostienen misma intención. GET 404 temprano no prueba cancelación. Constancia ejercitada en PostgreSQL en RUN-10; reentrada en navegador aislado pendiente. Requisito FR-CASH-004.


## Fuentes

- [useTrackedTransfer.ts](../../../../frontend/src/features/transfers/useTrackedTransfer.ts)
- [CashTransferEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/CashTransferEndpoints.cs)
- [CashTransfersPage.tsx](../../../../frontend/src/features/transfers/CashTransfersPage.tsx)
- [CashSessionStatusQueries.cs](../../../../backend/src/MediPOS.Application/Modules/Cash/CashSessionStatusQueries.cs)
- [CashSession.cs](../../../../backend/src/MediPOS.Domain/Modules/Cash/CashSession.cs)
- [CashTransferTransaction.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Cash/Persistence/CashTransferTransaction.cs)
- [CashTransferReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Cash/Persistence/CashTransferReader.cs)
- [TransferEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/TransferEndpoints.cs)
- [ApproveTransferHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Transfers/ApproveTransfer/ApproveTransferHandler.cs)
- [TransferTransaction.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Transfers/Persistence/TransferTransaction.cs)
- [CancelTransferHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Transfers/CancelTransfer/CancelTransferHandler.cs)
- [DispatchTransferHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Transfers/DispatchTransfer/DispatchTransferHandler.cs)
- [ReceiveTransferHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Transfers/ReceiveTransfer/ReceiveTransferHandler.cs)
- [Prueba relacionada de la familia](../../../../backend/tests/MediPOS.IntegrationTests/Modules/Transfers/TransferPersistenceTests.cs) · E0 de ejecución, se inventaría por contexto; no atribuir cobertura de todas las variantes.


- [GetTransfersHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Transfers/GetTransfersHandler.cs)
- [TransferDirectoryReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Transfers/Persistence/TransferDirectoryReader.cs)
- [RequestTransferHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Transfers/RequestTransfer/RequestTransferHandler.cs)
- [TransferStores.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Transfers/Persistence/TransferStores.cs)
- [RecoverableOperations.cs](../../../../backend/src/MediPOS.Infrastructure/Persistence/RecoverableOperations.cs)
- [TransferOperation.cs](../../../../backend/src/MediPOS.Application/Modules/Transfers/TransferOperation.cs)
