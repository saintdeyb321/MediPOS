# Contingencia e integraciones

PWA usa caché y cola local scoped; un pendiente local no acredita venta ni pago. Los servicios auxiliares se distinguen de endpoints en `DependencyInjection.cs:144–154,240,257–262,292`. TO-BE PENDIENTE_RFA3.



## UC-INT-01

GET `/health` · `backend/src/MediPOS.Api/Observability/ObservabilityRegistration.cs:66`. **E1 · IMPLEMENTADO · NOT_RUN**. Observabilidad expone liveness. ObservabilityRegistration.cs:66 usa predicado false; no indica PostgreSQL listo. Uso operativo.

## UC-INT-02

GET `/health/ready` · `backend/src/MediPOS.Api/Observability/ObservabilityRegistration.cs:67`. **E0 · IMPLEMENTADO · NOT_RUN**. Readiness en ObservabilityRegistration.cs:67 y siguientes. Confirmar checks particulares según flags; no se midió el servidor.

## UC-INT-03

GET `/api/tenants/{tenantId}/branches/{branchId}/sync/devices` · `backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs:13`. **E0 · IMPLEMENTADO · NOT_RUN**. Al preparar contingencia consulta dispositivos en la sede. workflow.ts:11 y SyncEndpoints.cs:13. Store y filtros pendientes.

## UC-INT-04

POST `/api/tenants/{tenantId}/branches/{branchId}/sync/devices` · `backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs:16`. **E0 · IMPLEMENTADO · NOT_RUN**. Preparar este navegador, registrar dispositivo cuando corresponda y obtener alcance. SyncEndpoints.cs:16. Validación de identidad/versiones persistidas por completar.

## UC-INT-05

POST `/api/tenants/{tenantId}/sync/devices/{deviceId}/revoke` · `backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs:21`. **E0 · IMPLEMENTADO · NOT_RUN**. Revocar dispositivo y conservar pendientes aislados. OfflinePage refleja revocación original ante fallo. Persistencia y auditoría por cerrar.

## UC-INT-06

POST `/api/tenants/{tenantId}/sync/batches` · `backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs:26`. **E0 · IMPLEMENTADO · NOT_RUN**. Conciliar envíos con POST por lote; workflow.ts:97 mantiene evento/identidad. ProcessOfflineSyncBatchHandler.cs:26–55 exige dispositivo y acceso. Savepoints, deduplicación y acuses por rastrear.

## UC-INT-07

GET `/api/tenants/{tenantId}/sync/conflicts` · `backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs:33`. **E0 · IMPLEMENTADO · NOT_RUN**. Abrir conflictos y listar según estado. DTO paginado y usado en OfflinePage. SQL y permisos exactos pendientes.

## UC-INT-08

POST `/api/tenants/{tenantId}/sync/conflicts/{conflictId}/resolve` · `backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs:39`. **E0 · IMPLEMENTADO · NOT_RUN**. Elegir resolución explícita y nota. La UI no ofrece forzar venta o inventario. Handler y persistencia de decisión pendiente.

## UC-INT-09

GET `/api/tenants/{tenantId}/sync/devices/{deviceId}/catalog/snapshot` · `backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs:45`. **E0 · IMPLEMENTADO · NOT_RUN**. Preparar catálogo con snapshot del dispositivo. workflow.ts:11–55 requiere service worker y ámbito, sincronización incremental delegada a endpoint. Transacción del snapshot pendiente.

## UC-INT-10

GET `/api/tenants/{tenantId}/sync/devices/{deviceId}/catalog/changes` · `backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs:51`. **E0 · IMPLEMENTADO · NOT_RUN**. Aplicar cambios a partir de cursor de dispositivo. Validar continuidad y errores; checkout real y contrato de cursor por cerrar.

## UC-INT-11

Preparar o bloquear dispositivo PWA. **E0 · IMPLEMENTADO · NOT_RUN**. Abrir contingencia, preparar tras validar sesión/propia caja o bloquear el dispositivo local. workflow.ts:11–55 y ContingencyPage.tsx mantienen separada la preparación de la cola. Pendiente revisar IndexedDB claim y expiración.

## UC-INT-12

Encolar venta local, retomar y conciliar. **E0 · IMPLEMENTADO · NOT_RUN**. Registrar venta provisional, encolar misma identidad, y consultar acuses desde cola. workflow.ts:83–97 no la considera venta confirmada; backoff/efecto de persistencia por cerrar.

## UC-INT-13

Procesar catálogo oficial DIGEMID. **E0 · NO_EXPUESTO · NOT_RUN**. StageOfficialCatalogImportHandler y OfficialCatalogServices.cs:39 exponen servicios internos. No se encontró router HTTP en inventario para staging/curación/publicación. Diferenciar de catálogo global de lectura. Falta trazar entradas operativas y tablas por etapa.

## UC-INT-14

Importar UBL de proveedor. **E0 · NO_EXPUESTO · NOT_RUN**. ImportSupplierUblInvoiceHandler registrado en DependencyInjection.cs:292. Parser, store/revisión/conversión en Modules/Purchasing/Ubl. No se encontró ruta HTTP UBL en mapa API; revisar entrypoint interno y no confundir staging con recepción.

## UC-INT-15

Entrega CPE y consultas de proveedor. **E1 · DESACTIVADO · NOT_RUN**. RunElectronicDocumentDelivery y work store existen (DependencyInjection.cs:257–262). Identidad de entrega registrada como DisabledElectronicDeliveryIdentity. No se encontró endpoint CPE o job alojado en el inventario. Fuente efectiva de configuración del entorno no inspeccionada.

## UC-INT-16

Comprobar conexión local de plataforma. **E0 · IMPLEMENTADO · NOT_RUN**. Comprobación especial de startup para Platform DEV, registrada en LocalDevelopmentPlatform.cs:59. Condición y actores en Program.cs:28–29; SQL/rol efectivo no inspeccionados.

## UC-INT-17

Recuperación y respaldo CLI. **E0 · IMPLEMENTADO · NOT_RUN**. Backup y recuperación operativos fuera del router. Ver BACKUP_RECOVERY_RUNBOOK, sin ejecutar scripts. Falta relacionar subcomandos y entrypoints concretos.

## UC-INT-18

Servir SPA y caché offline. **E0 · IMPLEMENTADO · NOT_RUN**. SPA seleccionada en Program.cs:60–63. PublishedFrontend.cs:10–16 exige Production con LocalFrontend=false; PWA empaqueta app. Caché de actualización y fallback offline por cerrar.


## Fuentes

- [DependencyInjection.cs](../../../../backend/src/MediPOS.Infrastructure/DependencyInjection.cs)
- [ObservabilityRegistration.cs](../../../../backend/src/MediPOS.Api/Observability/ObservabilityRegistration.cs)
- [SyncEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/SyncEndpoints.cs)
- [workflow.ts](../../../../frontend/src/features/offline/workflow.ts)
- [ProcessOfflineSyncBatchHandler.cs](../../../../backend/src/MediPOS.Application/Modules/OfflineSync/ProcessOfflineSyncBatchHandler.cs)
- [OfficialCatalogServices.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Catalog/OfficialImport/OfficialCatalogServices.cs)
- [LocalDevelopmentPlatform.cs](../../../../backend/src/MediPOS.Api/Authentication/LocalDevelopmentPlatform.cs)
- [PublishedFrontend.cs](../../../../backend/src/MediPOS.Api/Hosting/PublishedFrontend.cs)
- [Prueba relacionada de la familia](../../../../backend/tests/MediPOS.UnitTests/Api/OfflineSyncBatchHttpContractsTests.cs) · E0 de ejecución, se inventaría por contexto; no atribuir cobertura de todas las variantes.
