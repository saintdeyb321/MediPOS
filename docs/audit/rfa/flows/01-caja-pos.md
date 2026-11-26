# Caja/POS

**Corte vigente.** Las etiquetas por UC incorporan [RUN-10](../VALIDATION.md#run-10). La columna de aserciones delimita el escenario ejecutado. Las recetas AS-IS describen el diagnóstico anterior. [Decisiones de diseño](../TO_BE.md).

[Validación RFA2](../VALIDATION.md) · SC-01 / SC-03. E2 identifica la frontera con dobles o pruebas locales. BLOCKED indica una frontera esencial sin evidencia aislada.

## Caja y arqueo · patrón

Ruta comercial `/boticas/:tenantId/sucursales/:branchId/caja` o gestión dentro de POS. Scope UI por sesión, membresía, tenant y sede (`CashPage.tsx:25`). Las mutaciones de `PosEndpoints.cs:25–41` atraviesan el filtro de tenant y las escrituras exigen CSRF. `CashReconciliationAccess.cs:10–16` exige la misma sede/tenant y Owner o la membresía de la caja. La política RLS de `20261007024648_AddCashSessions.cs:84–89` fuerza tenant; el filtro de sede/membresía se aplica además en aplicación/consultas. Ver el [recorrido propuesto](../TO_BE.md).

| Origen | Evento | Guarda | Destino y efecto |
|---|---|---|---|
| Sin caja actual | Confirmar apertura | Acceso vigente, importe válido y una sesión abierta por membresía/sede | open, persistencia de apertura y auditoría |
| open | Confirmar cierre | Contexto y actor habilitados, conteo válido, bloqueo de fila y ledger coherente | closed, conteo/esperado/diferencia y auditoría |
| open o resultado incierto | Recuperar por ID | Segunda escritura bloqueada mientras exista intención pendiente | Consulta del estado; solo closed permite retirar el marcador y obtener arqueo |
| closed | Consultar | Acceso a esa misma caja | Arqueo, sin nueva escritura |

### Reporte S/100 → S/10 → lista vacía

Con cero pagos netos en efectivo y cero sencillo, `CashReconciliation.ExpectedCash` suma apertura + efectivo neto + recibido − enviado (`CashReconciliation.cs:104–111`). `CashSession.Close` guarda contado − esperado (`CashSession.cs:54–60`). La expectativa es **100 esperado, 10 contado, −90**. El filtro de activas admite solo open (`CashSessionReader.cs:26–45`); la ausencia tras cerrar es compatible con una fila cerrada preservada. El cierre actualiza la fila bloqueada y registra `CashSessionClosed` en una transacción (`CashCloseTransaction.cs:15–32,59–72`). El indexado por ID y el query string `cash` permiten volver al arqueo mientras se conserva la dirección. Entrar con URL limpia pierde el acceso directo de esta UI; no se identificó un historial general en las rutas de caja inventariadas. La caché se invalida al verificar el cierre; un vacío solo no distingue lectura vigente, cambio de sede, permiso, otra versión del build o resultado no conciliado en el incidente real.

**Receta para RFA2.** En destino autorizado aislado, anotar SHA/build, rol, sede y un ID sintético. Abrir 100 sin movimientos, confirmar GET actual/activas, cerrar 10, guardar estado HTTP, acuse y registro. Consultar por ID, conciliación y auditoría. Comparar navegación con URL retenida y limpia. Repetir con pérdida de respuesta y leer antes de reenviar. Contrastar otra sede y una membresía ajena. La ejecución y el incidente del operador siguen NOT_RUN en esta fase.

 ```mermaid
flowchart LR
  A[Abrir y guardar] --> B[Caja open]
  B --> C[Confirmar conteo]
  C --> D{Evidencia de cierre}
  D -->|Acuse válido| E[Closed y arqueo]
  D -->|Incierto| F[Conservar cash y closingCash]
  F --> G{GET misma caja}
  G -->|Open o error| F
  G -->|Closed| H[GET conciliación]
  H --> E
  E --> I[Activas excluye la cerrada]
  E --> J[Conservar URL cash para reconsulta]
```

## POS · recorrido común

1. En Ventas, buscar por texto/código y elegir presentación. 2. Ajustar carrito y abrir o verificar caja. 3. Persistir borrador, revisar y pagar. 4. Abrir ticket, imprimir o seguir. `PosPage.tsx:21–47`, `useSaleDraft.ts:69–173`, `useSaleCheckout.ts:11–81` y `SaleCheckoutPanel.tsx:31–90`. El navegador usa decimales exactos y el servidor valida el actor y la sede. La confirmación registra pago, movimientos y comisión juntos (`SaleConfirmationPosting.cs:94–134`); el bloqueo de venta/caja y la protección por tenant siguen siendo parte del contrato.



## UC-CAJ-01

GET `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:22`. **E2 · IMPLEMENTADO · BLOCKED**. Consultar resultado desde el ticket o la recuperación. GetSaleStateHandler.cs:10–20 pasa por acceso POS y revisa vendedor/ámbito. SaleDraftStore.cs:13–25 consulta por tenant/sede/ID y devuelve versión y líneas. useSaleCheckout.ts:28–41 consume el estado terminal de la misma venta. Requisito FR-POS-009.

## UC-CAJ-02

POST `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:25`. **E3 · IMPLEMENTADO · PASS**. 1. Abrir caja y escribir monto inicial. 2. Confirmar apertura. 3. Consultar su estado en la misma sede. CashSessionPanel.tsx:16–47 → useCashSession.ts:42–62 → DTO openingAmount en cash/api.ts:57–59 → OpenCashSessionHandler.cs:20–41. OpenCashSessionWriter.cs:15–26 persiste sesión y auditoría en la misma transacción. Índice parcial único de apertura en CashSessionConfiguration.cs:12,48. El acuse se valida para el actor y actualiza current-cash. Si la respuesta es incierta, consultar la actual antes de repetir. Requisito FR-CASH-001.

## UC-CAJ-03

GET `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions/current` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:30`. **E3 · IMPLEMENTADO · PASS**. 1. Entrar a Caja o POS. 2. Leer caja propia vigente. CashSessionStatusQueries.CurrentAsync verifica el contexto antes/después de FindAsync, limitado a open y a la membresía (CashSessionReader.cs:13–24). null describe ausencia de abierta. useCashSession.ts mantiene caché scoped y permite relectura. Requisito FR-CASH-001.

## UC-CAJ-04

GET `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions/{cashSessionId}` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:32`. **E2 · IMPLEMENTADO · BLOCKED**. 1. Seleccionar caja en directorio autorizado o conservar cash en URL. 2. Consultar antes de cerrar o de leer arqueo. CashSessionStatusQueries.cs:18–28 y CashReconciliationAccess.cs:10–16 validan ámbito. expectedCashAmount refleja valor persistido, nulo en una caja abierta. Consulta sin escritura. Requisito FR-CASH-002. Indicio F-002.

## UC-CAJ-05

GET `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions/{cashSessionId}/reconciliation` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:34`. **E3 · IMPLEMENTADO · BLOCKED**. 1. Abrir cierre guardado. 2. Revisar esperado, contado, diferencia y pagos. GetCashSessionReconciliationHandler.cs:14–32 valida closed y ledger; CashSessionReconciliationReader.cs:10–37 consulta sesión/ventas/pagos/reversiones y transferencias. Si el ledger contradice lo guardado, falla. CashPage.tsx:69–82 usa exclusivamente la conciliación decodificada. Requisito FR-CASH-002;FR-CASH-003. Indicio F-001. Prueba existente: `backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashClosePersistenceTests.cs:60`.

## UC-CAJ-06

POST `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions/{cashSessionId}/close` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:37`. **E3 · IMPLEMENTADO · BLOCKED**. 1. Leer estado abierto. 2. Introducir efectivo físico. 3. Activar Confirmar cierre. cash/api.ts:53–55 envía countedCashAmount exacto. CloseCashSessionHandler.cs:19–49 valida acceso, calcula ledger y cierra; CashCloseTransaction.cs:24,59–70 bloquea y confirma junto a auditoría. La UI verifica actor, membresía y conteo antes de aceptar el acuse e invalidar current/active. Cerrada impide repetir. Requisito FR-CASH-002;FR-CASH-003. Indicio F-001. Prueba existente: `backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashClosePersistenceTests.cs:140`.

## UC-CAJ-07

GET `/api/tenants/{tenantId}/cash-sessions/active` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:42`. **E3 · IMPLEMENTADO · BLOCKED**. 1. Entrar como Owner en la sede. 2. Consultar Cajas activas y elegir Revisar. CashPage.tsx:37–41,97–111 query scoped y paginada. GetActiveCashSessionsHandler autoriza explícitamente a Owner. CashSessionReader.cs:26–45 filtra abiertas por tenant/sede y proyecta ventas confirmadas. Al cerrar se invalida la lista. Requisito FR-CASH-005. Indicio F-001. Prueba existente: `backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashClosePersistenceTests.cs:60`.

## UC-CAJ-08

POST `/api/tenants/{tenantId}/branches/{branchId}/sales` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:45`. **E3 · IMPLEMENTADO · BLOCKED**. Crear borrador después de elegir la caja y vendedor. CreateSaleDraftHandler.cs:12 y siguientes valida cajero/sede/caja elegida, crea borrador. SaleDraftStore.cs:26–45 persiste borrador bajo scope, reutiliza transacción del alta recuperable. La respuesta incluye ID original; stock y pagos se reservan para confirmación. Requisito FR-POS-009.

## UC-CAJ-09

GET `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/draft` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:50`. **E3 · IMPLEMENTADO · BLOCKED**. Reabrir borrador del ID original y restaurar líneas. GetSaleDraftHandler.cs:10–21 verifica acceso y estado de borrador. SaleDraftStore.cs:13–25 rehidrata líneas y versión desde scope con tenant/sede/ID. Si el registro ya cambió, el estado general permite continuar conciliación; no crear otro.

## UC-CAJ-10

PUT `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/lines` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:53`. **E3 · IMPLEMENTADO · BLOCKED**. Guardar líneas del borrador con versión desde useSaleDraft.ts:123–151. ReplaceSaleLinesHandler.cs:16–60 verifica contexto, caja, disponibilidad de productos/unidades y versión. SaleDraftStore.cs:48–73 actualiza venta borrador filtrada y líneas dentro de transacción. Releer la revisión del mismo ID antes de cobrar. Requisito FR-POS-004.

## UC-CAJ-11

POST `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/confirm` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:58`. **E3 · IMPLEMENTADO · BLOCKED**. Preparar pago, confirmar y leer ticket. ConfirmSaleHandler.cs:29–58 recibe versión/pagos y llama a SaleConfirmationPosting.cs:43–134. Calcula y valida, confirma la venta, guarda pagos, stock, comisiones y auditoría con CompleteAsync. Si el envío es incierto se mantiene la venta original (useSaleCheckout.ts:28–41,62–80). Los pagos se insertan bajo las políticas de 20261007035126_AddAtomicSaleConfirmationAndPayments.cs:102–107 y el stock se registra bajo 20261006173701_AddPurchasingAndReceiptFoundation.cs:328–338. CommissionHistorySql.cs:17–22 aplica políticas de tenant al ledger. Requisito FR-POS-005;FR-POS-006;FR-POS-009. Prueba existente: `frontend/src/features/pos/PosPage.test.tsx`.

## UC-CAJ-12

GET `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/ticket` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:63`. **E3 · IMPLEMENTADO · BLOCKED**. Abrir el ticket de la venta confirmada. GetInternalTicketHandler.cs:12–33 resuelve acceso y lee ticket de venta verificada; InternalTicketReader.cs:15 y siguientes proyecta venta confirmada y datos de contexto desde EF. El panel puede imprimir u omitir sin escribir. Requisito FR-POS-008.

## UC-CAJ-13

POST `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/void` · `backend/src/MediPOS.Api/Tenant/PosEndpoints.cs:66`. **E3 · IMPLEMENTADO · BLOCKED**. En venta confirmada, abrir anulación y dar motivo. SaleCheckoutPanel.tsx:66–75 y useSaleCheckout.ts:42–61 retienen intención. VoidSaleHandler.cs:58–81 prepara las compensaciones, SaleVoidTransaction.cs:37–43,145–185 bloquea caja, venta y lotes y guarda reversiones auditadas. No se elimina la venta. Si no puede verificarse el resultado se consulta el ID original. Requisito FR-POS-010. Prueba existente: `frontend/src/features/pos/PosPage.test.tsx`.

## UC-CAJ-14

Buscar, elegir presentación y editar carrito. **E2 · IMPLEMENTADO · BLOCKED**. Buscar y elegir coincidencia/presentación en ProductSearchPanel. PosPage.tsx:24–40 actualiza carrito en memoria y evita líneas duplicadas, cotejar búsquedas con UC-CAT-08. Local hasta el guardado del borrador; descartar o cambiar contexto afecta únicamente esta edición local. Requisito FR-POS-002;FR-POS-004.

## UC-CAJ-15

Imprimir ticket interno. **E2 · IMPLEMENTADO · BLOCKED**. Abrir ticket confirmado y elegir impresión. El botón opera en navegador tras query del ticket. Evento de impresión y reglas CSS por referenciar. El botón de InternalTicketPanel.tsx:23 llama window.print con clase no-print, sobre el ticket visible de UC-CAJ-12. Abrir impresión o cancelarla no escribe ni emite CPE. Requisito FR-POS-008.

## UC-CAJ-16

Recuperar cierre incierto. **E2 · IMPLEMENTADO · BLOCKED**. 1. Ante respuesta ambigua, conservar la dirección. 2. Consultar esta misma caja. useCashClose.ts:22–61,65–93 mantiene cash/closingCash; un GET open conserva el bloqueo. GET closed sella estado terminal antes de leer arqueo. El fallo posterior permite reconsultar; una denegación definitiva del POST se trata por separado. Requisito FR-CASH-002. Indicio F-001. Prueba existente: `frontend/src/features/cash/CashPage.test.tsx`.

## UC-CAJ-17

Relocalizar caja cerrada tras recarga. **E2 · NO_EXPUESTO · BLOCKED**. 1. Con la URL que contiene cash, volver a Consultar cierre guardado. 2. Al abrir Caja con URL limpia, solo buscar activas. CashPage.tsx:41–56 y useCashClose.ts:22–28 distinguen dirección y memoria. Búsqueda por fecha/operador de cerradas no queda expuesta en el router comercial. Requisito FR-CASH-002;FR-CASH-005. Indicio F-001.

## UC-CAJ-18

Elegir vendedor y preparar cobro mixto. **E2 · IMPLEMENTADO · BLOCKED**. Seleccionar vendedor en preparación y registrar uno o varios medios en la pantalla Pagos. SaleCheckoutPanel.tsx:83–90 entrega pagos validados al hook, y la regla autoritativa corresponde al POST de UC-CAJ-11. Efectivo esperado solo incluye cash neto. Requisito FR-POS-005;FR-POS-006;FR-POS-009.


## Fuentes

- [CashPage.tsx](../../../../frontend/src/features/cash/CashPage.tsx)
- [PosEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/PosEndpoints.cs)
- [CashReconciliationAccess.cs](../../../../backend/src/MediPOS.Application/Modules/Cash/CashReconciliationAccess.cs)
- [20261007024648_AddCashSessions.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Cash/Persistence/Migrations/20261007024648_AddCashSessions.cs)
- [CashReconciliation.cs](../../../../backend/src/MediPOS.Domain/Modules/Cash/CashReconciliation.cs)
- [CashSession.cs](../../../../backend/src/MediPOS.Domain/Modules/Cash/CashSession.cs)
- [CashSessionReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Cash/Persistence/CashSessionReader.cs)
- [CashCloseTransaction.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Cash/Persistence/CashCloseTransaction.cs)
- [PosPage.tsx](../../../../frontend/src/features/pos/PosPage.tsx)
- [useSaleDraft.ts](../../../../frontend/src/features/pos/useSaleDraft.ts)
- [useSaleCheckout.ts](../../../../frontend/src/features/pos/useSaleCheckout.ts)
- [SaleCheckoutPanel.tsx](../../../../frontend/src/features/pos/SaleCheckoutPanel.tsx)
- [SaleConfirmationPosting.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/ConfirmSale/SaleConfirmationPosting.cs)
- [CashSessionPanel.tsx](../../../../frontend/src/features/cash/CashSessionPanel.tsx)
- [useCashSession.ts](../../../../frontend/src/features/cash/useCashSession.ts)
- [OpenCashSessionHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Cash/OpenCashSession/OpenCashSessionHandler.cs)
- [OpenCashSessionWriter.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Cash/Persistence/OpenCashSessionWriter.cs)
- [CashSessionConfiguration.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Cash/Persistence/CashSessionConfiguration.cs)
- [CashSessionStatusQueries.cs](../../../../backend/src/MediPOS.Application/Modules/Cash/CashSessionStatusQueries.cs)
- [GetCashSessionReconciliationHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Cash/GetCashSessionReconciliation/GetCashSessionReconciliationHandler.cs)
- [CashSessionReconciliationReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Cash/Persistence/CashSessionReconciliationReader.cs)
- [CashClosePersistenceTests.cs](../../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashClosePersistenceTests.cs)
- [CloseCashSessionHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Cash/CloseCashSession/CloseCashSessionHandler.cs)
- [ConfirmSaleHandler.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/ConfirmSale/ConfirmSaleHandler.cs)
- [VoidSaleHandler.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/VoidSale/VoidSaleHandler.cs)
- [SaleVoidTransaction.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/SalesPos/Persistence/SaleVoidTransaction.cs)
- [InternalTicketPanel.tsx](../../../../frontend/src/features/pos/InternalTicketPanel.tsx)
- [useCashClose.ts](../../../../frontend/src/features/cash/useCashClose.ts)
- [Prueba relacionada de la familia](../../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashClosePersistenceTests.cs) · E0 de ejecución, se inventaría por contexto; no atribuir cobertura de todas las variantes.


- [GetSaleStateHandler.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/GetSaleStateHandler.cs)
- [SaleDraftStore.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/SalesPos/Persistence/SaleDraftStore.cs)
- [CreateSaleDraftHandler.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/CreateSaleDraft/CreateSaleDraftHandler.cs)
- [GetSaleDraftHandler.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/GetSaleDraft/GetSaleDraftHandler.cs)
- [ReplaceSaleLinesHandler.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/ReplaceSaleLines/ReplaceSaleLinesHandler.cs)
- [GetInternalTicketHandler.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/GetInternalTicket/GetInternalTicketHandler.cs)
- [InternalTicketReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/SalesPos/Persistence/InternalTicketReader.cs)
