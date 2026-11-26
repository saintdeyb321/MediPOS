# Catálogo

Productos es Owner (`frontend/src/features/catalog/ProductsPage.tsx:32–49`); POS usa contratos de sucursal y la búsqueda devuelve coincidencias y alternativas, no sustitución clínica. `CatalogEndpoints.cs:18–76` separa mantenimiento paginado, consultas operativas y corrección auditada. Lecturas y mutaciones privadas atraviesan `TenantEndpoints.cs:15–19`. En borradores/variantes E0 queda por cerrar el enlace a política SQL/EF concreta.



## UC-CAT-01

GET `/api/tenants/{tenantId}/branches/{branchId}/operational-products` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:18`. **E1 · IMPLEMENTADO · NOT_RUN**. En POS abrir catálogo. Catálogo operativo de sucursal recibe filtros de consulta. OperationalCatalogQueries en CatalogEndpoints.cs:18; verificar proyección activa y alcance SQL. OperationalCatalogQueries.cs:12–17 valida ProductSearch en la sede antes y después de leer; OperationalCatalogReader.cs:9–19 filtra productos activos por tenant, término/categoría y pagina. La respuesta sirve la grilla operativa sin escritura. Requisito FR-CAT-001.

## UC-CAT-02

GET `/api/tenants/{tenantId}/branches/{branchId}/operational-categories` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:21`. **E1 · IMPLEMENTADO · NOT_RUN**. En POS elegir categoría. Consulta de directorio operacional paginada. OperationalCatalogQueries.cs:20–25 verifica ProductSearch y pagina categorías. CatalogDirectoryReader.cs:11–13 lee categorías activas; datos de referencia compartidos. La UI usa el resultado como selector. Requisito FR-CAT-001.

## UC-CAT-03

PUT `/api/tenants/{tenantId}/products/{productId}/local-data` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:24`. **E1 · IMPLEMENTADO · NOT_RUN**. Editar datos locales desde ficha y guardar con revisión observada. ProductDetailPanel/EditLocalProduct → UpdateLocalProductHandler.cs:16–32 verifica revisión bloqueada y registra before/after. BusinessProductStore.cs:62,79 bloquea business_products con xmin y persiste auditoría. Global y snapshots quedan fuera del payload; recuperación UC-CAT-21.

## UC-CAT-04

GET `/api/tenants/{tenantId}/categories` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:29`. **E1 · IMPLEMENTADO · NOT_RUN**. Cargar categorías en selector de alta/filtro. ProductsPage.tsx:50–58 pagina consulta; mapeo EF pendiente. CatalogDirectoryQueries.cs:17–18,32–37 exige Administración antes/después. CatalogDirectoryReader.cs:11–13 proyecta activas paginadas; el selector no modifica datos. Requisito FR-CAT-001.

## UC-CAT-05

GET `/api/tenants/{tenantId}/global-products` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:31`. **E1 · IMPLEMENTADO · NOT_RUN**. Abrir Catálogo global y consultar página. API offset/limit; asociación se escribe por separado. CatalogDirectoryQueries.cs:19–20,32–37 exige Administración. CatalogDirectoryReader.cs:14–16 proyecta referencia global paginada, sin precios ni stock tenant. La asociación es UC-CAT-10. Requisito FR-CAT-001.

## UC-CAT-06

GET `/api/tenants/{tenantId}/products` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:33`. **E1 · IMPLEMENTADO · NOT_RUN**. Abrir Mis productos y filtrar página cargada. Properties de lista privada y offset/limit, consultar `ProductsPage.tsx:48–62`. Verificar EF. CatalogDirectoryQueries.cs:21–22,32–37 autoriza Administración; CatalogDirectoryReader.cs:17–22 limita negocio por tenant y pagina. El filtro local conserva inactivos si están en la página. Requisito FR-CAT-001. Indicio F-003.

## UC-CAT-07

GET `/api/tenants/{tenantId}/branches/{branchId}/products/{productId}` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:35`. **E1 · IMPLEMENTADO · NOT_RUN**. Abrir ficha de producto y sus presentaciones. ProductLookup y ProductsPage comparten clave scoped catalog-detail. CatalogDirectoryQueries.cs:23–29 valida ProductSearch por sede y vuelve a autorizar. CatalogDirectoryReader.cs:24 y siguientes proyecta producto y unidades por tenant y producto. UI vuelve al acuse/detalle scoped. Requisito FR-CAT-006.

## UC-CAT-08

GET `/api/tenants/{tenantId}/branches/{branchId}/products/search` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:40`. **E0 · IMPLEMENTADO · NOT_RUN**. En POS/Inventario buscar nombre/código y elegir coincidencia. InlineProductLookup consulta y devuelve ID; SearchProductsHandler se registra en CatalogEndpoints.cs:40–42. Índices y SQL de ranking pendientes. Requisito FR-POS-002;FR-POS-003.

## UC-CAT-09

POST `/api/tenants/{tenantId}/products/local` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:43`. **E0 · IMPLEMENTADO · NOT_RUN**. Completar datos de alta local y confirmar. CreateProductForm/useCatalogWrite, contrato LocalProductRequest. Persistencia/Auditoría de creación por trazar.

## UC-CAT-10

POST `/api/tenants/{tenantId}/products/from-global` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:49`. **E0 · IMPLEMENTADO · NOT_RUN**. Añadir referencia del catálogo global desde la pestaña. GlobalProductRequest crea producto a nivel tenant; persistencia por trazar.

## UC-CAT-11

PUT `/api/tenants/{tenantId}/products/{productId}/prices` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:54`. **E0 · IMPLEMENTADO · NOT_RUN**. Editar precios minorista y opcional mayorista. ProductDetailPanel / PricesRequest. La salida del formulario no confirma escritura incierta, consultar producto original. Revisión transaccional pendiente.

## UC-CAT-12

PUT `/api/tenants/{tenantId}/products/{productId}/status` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:59`. **E0 · IMPLEMENTADO · NOT_RUN**. Cambiar estado activo/inactivo. ProductDetailPanel y ProductStatusRequest. Diferenciar baja lógica de borrado; DB por trazar.

## UC-CAT-13

GET `/api/tenants/{tenantId}/products/{productId}/unit-configuration` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:64`. **E0 · IMPLEMENTADO · NOT_RUN**. Abrir configuración de unidades para obtener versión vigente; el mismo componente lee y escribe. ProductDetailPanel.tsx:64–86; ProductUnitConfigurationHandler. Proyección de unidades por trazar.

## UC-CAT-14

PUT `/api/tenants/{tenantId}/products/{productId}/unit-configuration` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:67`. **E0 · IMPLEMENTADO · NOT_RUN**. Editar presentaciones, confirmar y enviar versión. ProductDetailPanel.tsx:64–86 y ProductUnitConfigurationHandler.cs:38–63 entregan estado deseado y auditoría a SaveAsync bajo expectedVersion. Falta cerrar SQL final para detectar versión y dependencias históricas.

## UC-CAT-15

PUT `/api/tenants/{tenantId}/products/{productId}/units` · `backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs:72`. **E0 · NO_DETERMINADO · NOT_RUN**. Ruta alternativa ReplaceProductUnitsHandler; el consumidor normal observado usa unit-configuration. Revisión de consumidores y compatibilidad pendiente.

## UC-CAT-16

GET `/api/tenants/{tenantId}/product-imports/template` · `backend/src/MediPOS.Api/Tenant/ImportEndpoints.cs:11`. **E0 · IMPLEMENTADO · NOT_RUN**. Descargar plantilla desde ImportPage, generación devuelve XLSX. Sin escritura comercial del cliente. Seguridad del endpoint por cerrar.

## UC-CAT-17

POST `/api/tenants/{tenantId}/product-imports` · `backend/src/MediPOS.Api/Tenant/ImportEndpoints.cs:16`. **E0 · IMPLEMENTADO · NOT_RUN**. Seleccionar XLSX y enviar. ImportPage rota intención original y ImportEndpoints.cs:16–28 aplica RecoverableWriteFilter product-import; resultados por fila y trabajo. Atomicidad por fila por trazar.

## UC-CAT-18

GET `/api/tenants/{tenantId}/product-imports/{importJobId}` · `backend/src/MediPOS.Api/Tenant/ImportEndpoints.cs:29`. **E0 · IMPLEMENTADO · NOT_RUN**. Consultar por trabajo y paginar resultados, incluidos errores. UC-CAT-17 deja job original, UI no lo reenvía. Store pendiente.

## UC-CAT-19

POST `/api/tenants/{tenantId}/product-imports/{importJobId}/recover-interrupted` · `backend/src/MediPOS.Api/Tenant/ImportEndpoints.cs:35`. **E0 · IMPLEMENTADO · NOT_RUN**. Si aparece recuperación de interrumpido, cerrar el trabajo original. RecoverInterruptedImportHandler.cs:11–24 y feature flag; falta inspección de candado y conciliación del job.

## UC-CAT-20

Cambiar vista y filtrar página de catálogo. **E1 · IMPLEMENTADO · NOT_RUN**. 1. Abrir Mis productos. 2. Cambiar lista/tarjetas, buscar y filtrar categoría/estado. 3. Al paginar, los filtros se aplican a la nueva página. ProductsPage.tsx:48–62,96–140 deriva resultados en memoria, sin búsqueda administrativa global. Indicio F-003.

## UC-CAT-21

Recuperar escritura de producto. **E1 · IMPLEMENTADO · NOT_RUN**. Tras ambigüedad conservar marcador y código originales. useCatalogWrite.ts:46–92 diferencia escritura y consulta, vuelve a verificar ficha antes de habilitar acción; no asumir ausencia como rechazo. Prueba ProductsPage.test.tsx.

## UC-CAT-22

Borrado físico de producto. **E1 · NO_EXPUESTO · NOT_RUN**. La superficie encontrada ofrece status inactivo y no hay ruta DELETE de productos en CatalogEndpoints. Pendiente verificar posibles operadores internos fuera de TenantApi.


## Fuentes

- [ProductsPage.tsx](../../../../frontend/src/features/catalog/ProductsPage.tsx)
- [CatalogEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs)
- [TenantEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/TenantEndpoints.cs)
- [OperationalCatalogQueries.cs](../../../../backend/src/MediPOS.Application/Modules/Catalog/OperationalCatalogQueries.cs)
- [OperationalCatalogReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Catalog/Persistence/OperationalCatalogReader.cs)
- [CatalogDirectoryReader.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Catalog/Persistence/CatalogDirectoryReader.cs)
- [UpdateLocalProductHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Catalog/UpdateLocalProductHandler.cs)
- [BusinessProductStore.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Catalog/Persistence/BusinessProductStore.cs)
- [CatalogDirectoryQueries.cs](../../../../backend/src/MediPOS.Application/Modules/Catalog/CatalogDirectoryQueries.cs)
- [ProductDetailPanel.tsx](../../../../frontend/src/features/catalog/ProductDetailPanel.tsx)
- [ProductUnitConfigurationHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Catalog/ReplaceProductUnits/ProductUnitConfigurationHandler.cs)
- [ImportEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/ImportEndpoints.cs)
- [RecoverInterruptedImportHandler.cs](../../../../backend/src/MediPOS.Application/Modules/Catalog/ProductImport/RecoverInterruptedImportHandler.cs)
- [useCatalogWrite.ts](../../../../frontend/src/features/catalog/useCatalogWrite.ts)
- [Prueba relacionada de la familia](../../../../backend/tests/MediPOS.UnitTests/Modules/Catalog/ProductUnitConfigurationTests.cs) · E0 de ejecución, se inventaría por contexto; no atribuir cobertura de todas las variantes.

