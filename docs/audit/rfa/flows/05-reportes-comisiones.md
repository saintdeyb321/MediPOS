# Reportes y comisiones

**Corte vigente.** Las etiquetas por UC incorporan [RUN-10](../VALIDATION.md#run-10). La columna de aserciones delimita el escenario ejecutado. Las recetas AS-IS describen el diagnóstico anterior. [Decisiones de diseño](../TO_BE.md).

[Validación RFA2](../VALIDATION.md) · SC-08. E2 identifica la frontera con dobles o pruebas locales. BLOCKED indica una frontera esencial sin evidencia aislada.

Reportes y Comisiones son visibles para Owner. `ReportsPage.tsx:34–54` separa borrador de consulta aplicada y revalida por cache scoped/foco. Export es POST por contrato, de generación de archivo, con filtros capturados; ni el gráfico ni la tabla crean datos contables. Ver el [recorrido propuesto](../TO_BE.md).



## UC-REP-01

GET `/api/tenants/{tenantId}/commissions/settings` · `backend/src/MediPOS.Api/Tenant/CommissionEndpoints.cs:16`. **E3 · IMPLEMENTADO · BLOCKED**. Como Owner abre Comisiones y consulta si está habilitado. CommissionEndpoints.cs:16–17 llama GetTenantCommissionSettingsHandler.cs:13–20; verifica Owner antes y después de leer la configuración scoped en CommissionConfigurationStore.cs:16–24,43–44. Sin registro devuelve deshabilitado y no configurado, sin crear fila.

## UC-REP-02

PUT `/api/tenants/{tenantId}/commissions/settings` · `backend/src/MediPOS.Api/Tenant/CommissionEndpoints.cs:18`. **E3 · IMPLEMENTADO · BLOCKED**. En Comisiones cambia habilitación y guarda. SetTenantCommissionsEnabledHandler.cs:15–33 comprueba Owner, usa alcance transaccional, registra cambio y auditoría y rearma autorización antes del commit. CommissionConfigurationStore.cs:52–60,99–102 persiste settings. Respuesta contiene estado; si es incierta consulta UC-REP-01, conservando historial.

## UC-REP-03

GET `/api/tenants/{tenantId}/commissions/rules` · `backend/src/MediPOS.Api/Tenant/CommissionEndpoints.cs:23`. **E2 · IMPLEMENTADO · BLOCKED**. En Comisiones filtra revisiones por producto o inactivas. GetCommissionRulesHandler.cs:14–19 exige Owner antes/después; CommissionConfigurationStore.cs:26–35 filtra tenant/producto/estado, ordena por creación e ID y pagina. La salida sirve de lista y contexto para editar o desactivar.

## UC-REP-04

PUT `/api/tenants/{tenantId}/products/{productId}/commission-rule` · `backend/src/MediPOS.Api/Tenant/CommissionEndpoints.cs:26`. **E3 · IMPLEMENTADO · BLOCKED**. Elige producto, importe fijo o porcentaje y vigencia; guarda revisión. SetCommissionRuleHandler.cs:17–45 verifica Owner, producto y regla activa y construye reemplazo. CommissionConfigurationStore.cs:61–79 guarda cierre/revisión y auditoría en transacción. Se puede contrastar resultado en UC-REP-03 tras ambigüedad; no recalcula devengos previos.

## UC-REP-05

POST `/api/tenants/{tenantId}/commissions/rules/{ruleId}/deactivate` · `backend/src/MediPOS.Api/Tenant/CommissionEndpoints.cs:31`. **E3 · IMPLEMENTADO · BLOCKED**. Sobre una revisión, confirma desactivación. DeactivateCommissionRuleHandler.cs:15–31 verifica Owner antes y después, carga regla y desactiva con auditoría. CommissionConfigurationStore.cs:81–102 guarda y confirma. La consulta con inactivas permite verificar el estado; asiento previo permanece en su ledger.

## UC-REP-06

GET `/api/tenants/{tenantId}/reports/commissions` · `backend/src/MediPOS.Api/Tenant/CommissionEndpoints.cs:36`. **E3 · IMPLEMENTADO · PASS**. Comisiones devengadas y reversiones. OwnerCommissionsReportReader.cs:10–24 selecciona ámbito/ventana y pagina por confirmación, venta y línea. GetOwnerCommissionsReportHandler.cs:21–38 revalida Owner y devuelve el total/filas del reader. Consumo por filtro aplicado. Requisito FR-TEN-004.

## UC-REP-07

GET `/api/tenants/{tenantId}/reports/overview` · `backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs:14`. **E3 · IMPLEMENTADO · BLOCKED**. Resumen por sede. GetOwnerBranchOverviewHandler.cs:28–47 arma periodo y exige Owner. OwnerBranchOverviewReader.cs:23 y siguientes limita tenant/sede; DTO agrupa métricas por sede. Lectura sin escritura para Inicio o Reportes. Requisito FR-TEN-004;FR-RPT-001.

## UC-REP-08

GET `/api/tenants/{tenantId}/reports/dashboard` · `backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs:17`. **E3 · IMPLEMENTADO · BLOCKED**. Indicadores del periodo. GetOwnerOperationalReportsHandlers.cs:6–18 y OperationalReportReaders.cs:112 y siguientes usan periodo, ámbito y métricas agregadas de ventas/stock/caja. Owner mediante ReadOwnerAsync. Cambiar filtro cambia clave de consulta. Requisito FR-RPT-001.

## UC-REP-09

GET `/api/tenants/{tenantId}/reports/sales` · `backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs:20`. **E3 · IMPLEMENTADO · BLOCKED**. Ventas por dimensión con orden y filtros. GetOwnerOperationalReportsHandlers.cs:20–39 valida roles, criterio y paginación. OperationalReportReaders.cs:33–50 realiza consulta filtrada y dimensión, devuelve totales/página. La UI presenta el filtro aplicado. Requisito FR-RPT-002.

## UC-REP-10

GET `/api/tenants/{tenantId}/reports/stock-risk` · `backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs:24`. **E3 · IMPLEMENTADO · BLOCKED**. Existencias críticas. GetOwnerOperationalReportsHandlers.cs:41–55 usa ReadOwnerAsync; OperationalReportReaders.cs:52–62 consulta ámbito/stock y opción de mínimo crítico, devuelve página. No crea ajustes. Requisito FR-RPT-003. Owner selecciona Stock crítico en ReportsPage, aplica sede y filtros de producto/mínimo. Cambiar página conserva la consulta aplicada. Si falla, reconsultar con esos filtros. SC-08/RUN-10 no acreditan aceptación en navegador real.

## UC-REP-11

GET `/api/tenants/{tenantId}/reports/expirations` · `backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs:27`. **E3 · IMPLEMENTADO · BLOCKED**. Lotes próximos a vencer. GetOwnerOperationalReportsHandlers.cs:57–71 valida rango y Owner; OperationalReportReaders.cs:64–78 proyecta vencimientos por sede. UI muestra vigencia del filtro aplicado. Requisito FR-RPT-003. Owner elige Vencimientos, aplica rango y sede y navega páginas del resultado. Puede volver a ejecutar el mismo GET tras un error sin cambiar el lote. Falta verificación E4.

## UC-REP-12

GET `/api/tenants/{tenantId}/reports/inventory-capital` · `backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs:30`. **E3 · IMPLEMENTADO · BLOCKED**. Capital de inventario. GetOwnerOperationalReportsHandlers.cs:73–84 valida Owner y ámbito. OperationalReportReaders.cs:80–95 computa capital de lotes bajo ámbito, no desde una página parcial. Requisito FR-RPT-003. Owner elige Capital en inventario y aplica el ámbito. La pantalla usa los importes del servidor y permite repetir la lectura. Sin escrituras ni cálculo del total a partir de filas de otra página. Falta verificación E4.

## UC-REP-13

GET `/api/tenants/{tenantId}/reports/product-rotation` · `backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs:33`. **E3 · IMPLEMENTADO · BLOCKED**. Rotación por producto. GetOwnerOperationalReportsHandlers.cs:86–100 valida Owner, periodo y modo. OperationalReportReaders.cs:97–110 consulta rotación y pagina. No infiere consumo desde un solo lote. Requisito FR-RPT-004.

## UC-REP-14

POST `/api/tenants/{tenantId}/reports/exports` · `backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs:36`. **E3 · IMPLEMENTADO · BLOCKED**. Exportar XLSX o PDF con filtros aplicados. ExportOwnerReportHandler.cs:13–43 prepara política, lee snapshot y construye archivo; ReportExportReader.cs:13,51–83 acota consultas y filas por tipo. Descarga aplicando filtros; fallo conserva resultado previo y no crea operaciones comerciales. Requisito FR-RPT-005.

## UC-REP-15

Calcular y revertir comisión durante venta. **E3 · IMPLEMENTADO · PASS**. Sin acción separada del usuario. Confirmar venta elige regla vigente, agrega earned si corresponde y guarda en la misma transacción. Al anular, VoidSaleHandler.cs:58–81 crea reversión desde original, y SaleVoidTransaction.cs:158–179 confirma compensaciones junto a auditoría. SaleConfirmationPosting.cs:94–134 y SaleCheckoutTransaction.cs:197–205 en el alta.

## UC-REP-16

Filtrar consulta, paginar y exportar. **E3 · IMPLEMENTADO · BLOCKED**. 1. Elegir reporte/periodo/sede. 2. Aplicar filtros. 3. Paginar o descargar cuando se admite. ReportsPage.tsx:34–54 separa borrador y filtros aplicados, scoping y exportación del conjunto aplicado, no de la sola página. Errores de consulta/exportación tienen estados separados.


## UC-REP-17

Consultar sugerencias de reposición en un consumidor interno. **E3 · NO_EXPUESTO · PASS**. El Owner solicitaría el cálculo por sede y periodo histórico. No hay una acción React ni ruta HTTP registrada para obtenerlo en este árbol. `DependencyInjection.cs:330` registra `GetOwnerReplenishmentSuggestionsHandler.cs:15–28`, que valida filtros y usa `OperationalReportPolicy.ReadOwnerAsync`. `OwnerReplenishmentReader.cs:9–19` toma una lectura scoped de ventas, stock vendible, mínimos y transferencias entrantes. El handler calcula cantidades explicables mediante `ReplenishmentEstimate` y devuelve una página. Una consulta repetida vuelve a calcular sobre una lectura nueva y no genera una orden. FR-INV-008. [Prueba de persistencia](../../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/ReplenishmentPersistenceTests.cs), con ejecución NOT_RUN.

## Fuentes

- [ReportsPage.tsx](../../../../frontend/src/features/reporting/ReportsPage.tsx)
- [CommissionEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/CommissionEndpoints.cs)
- [ReportEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/ReportEndpoints.cs)
- [VoidSaleHandler.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/VoidSale/VoidSaleHandler.cs)
- [SaleVoidTransaction.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/SalesPos/Persistence/SaleVoidTransaction.cs)
- [SaleConfirmationPosting.cs](../../../../backend/src/MediPOS.Application/Modules/SalesPos/ConfirmSale/SaleConfirmationPosting.cs)
- [SaleCheckoutTransaction.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/SalesPos/Persistence/SaleCheckoutTransaction.cs)
- [Prueba relacionada de la familia](../../../../frontend/src/features/reporting/ReportsPage.test.tsx) · E0 de ejecución, se inventaría por contexto; no atribuir cobertura de todas las variantes.


- [OwnerCommissionsReportReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Reporting/Persistence/OwnerCommissionsReportReader.cs)
- [GetOwnerCommissionsReportHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Reporting/GetOwnerCommissionsReport/GetOwnerCommissionsReportHandler.cs)
- [GetOwnerBranchOverviewHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Reporting/GetOwnerBranchOverview/GetOwnerBranchOverviewHandler.cs)
- [OwnerBranchOverviewReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Reporting/Persistence/OwnerBranchOverviewReader.cs)
- [GetOwnerOperationalReportsHandlers.cs](../../../../backend/src/MediPOS.Application/Modules/Reporting/Operational/GetOwnerOperationalReportsHandlers.cs)
- [OperationalReportReaders.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Reporting/Persistence/OperationalReportReaders.cs)
- [ExportOwnerReportHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Reporting/ExportOwnerReport/ExportOwnerReportHandler.cs)
- [ReportExportReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Reporting/Persistence/ReportExportReader.cs)
