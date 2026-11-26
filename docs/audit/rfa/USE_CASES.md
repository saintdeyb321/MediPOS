# Inventario transversal

**Corte RFA3.** Los 155 identificadores del inventario siguen siendo la clave estable. [Decisión operativa por familia e ID](TO_BE.md), [frontera acreditada en CI](VALIDATION.md#run-10) y [orden de aceptación](ROADMAP.md).

Los 155 IDs conservan su alcance AS-IS. Para evidencia y veredicto de la revisión conductual, consultar la [matriz acumulativa](MATRIX.csv) y los [escenarios RFA2](VALIDATION.md).

## Router

- `39:      <Route path="/" element={<LoginPage />} />`
- `40:      <Route path="/aceptar-invitacion" element={<AcceptInvitationPage />} />`
- `41:      <Route path="/contingencia" element={<ContingencyPage />} />`
- `43:        <Route path="/entrada" element={<EntryRouter />} />`
- `44:        <Route path="/plataforma" element={<PlatformLayout />}><Route index element={<PlatformPage />} /></Route>`
- `45:        <Route path="/seleccionar-botica" element={<SelectionLayout><TenantSelection /></SelectionLayout>} />`
- `46:        <Route path="/boticas/:tenantId/sucursales" element={<SelectionLayout><BranchSelection /></SelectionLayout>} />`
- `47:        <Route path="/boticas/:tenantId/configuracion" element={<SelectionLayout><TenantOnboardingPage /></SelectionLayout>} />`
- `48:        <Route path="/boticas/:tenantId/sucursales/:branchId" element={<WorkspaceLayout />}>`
- `49:          <Route index element={<DashboardRoute />} />`
- `50:          <Route path="ventas" element={<PosPage />} />`
- `51:          <Route path="caja" element={<CashPage />} />`
- `52:          <Route path="productos" element={<ProductsPage />} />`
- `53:          <Route path="compras" element={<PurchasesPage />} />`
- `54:          <Route path="inventario" element={<InventoryPage />} />`
- `55:          <Route path="empleados" element={<EmployeesPage />} />`
- `56:          <Route path="transferencias" element={<TransfersPage />} />`
- `57:          <Route path="transferencias/sencillo" element={<CashTransfersPage />} />`
- `58:          <Route path="reportes" element={<ReportsPage />} />`
- `59:          <Route path="comisiones" element={<CommissionsPage />} />`
- `60:          <Route path="notificaciones" element={<NotificationsPage />} />`
- `61:          <Route path="configuracion" element={<SettingsPage />} />`
- `62:          <Route path="auditoria" element={<AuditPage />} />`
- `63:          <Route path="importacion" element={<ImportPage />} />`
- `64:          <Route path="sincronizacion" element={<OfflinePage />} />`
- `65:          <Route path=":module" element={<ModulePage />} />`
- `68:      <Route path="*" element={<main className="state-block"><h1>No encontramos esta página</h1><p>Regresa al inicio para continuar.</p><Link className="button primary" to="/">Ir al inicio</Link></main>} />`

## Menú y roles visibles

Fuente [navigation.ts](../../../frontend/src/app/navigation.ts#L7-L19). Visibilidad en la navegación comercial, sujeta a la admisión del servidor.

| Roles visibles | Acciones de entrada |
|---|---|
| Owner, Pharmacist y Cashier | Inicio, POS, Caja, Transferencias, Notificaciones y Contingencia |
| Owner y Pharmacist | Inventario y Compras |
| Owner | Productos, Reportes, Comisiones, Personal y Configuración |
| Superadmin con admisión independiente | Inicio de plataforma, Boticas, Propietarios, Licencias, Catálogo y Cuenta |


## Baseline sin rastrear

- `a96862689450bf5955524637ba753311e24c0a38 docs/MEDIPOS_BLUE_STUDIO_UX_ARCHITECTURE.md`
- `4a356aa285760f0eec939ae4cf6235dcdacfffe0 docs/MEDIPOS_FUNCTIONAL_ARCHITECTURE_AUDIT.md`
- `9db3137c75a016752ffb82d3c983dd379b853183 docs/MEDIPOS_RFA_FASE_01_AS_IS.md`
- `650b1020a95e24dc656dfb5a54fc12dfc36ae3d6 docs/PROMPT_CODEX_RFA.md`
- `7b3315b681e06d13072396277d5bc49910a88f4a docs/PROMPT_CODEX_RFA_FASE_01.md`
- `a4e603cdeb8dd8a9826e531f4cd7c1178a2e59e0 docs/PROMPT_CODEX_UAX.md`
- `7aa3b6ced188a274a261856de7288eda1b8d477a frontend/src/features/catalog/InlineProductLookup.tsx`
- `d64743fb3bfd62e3fb7ed8171e7f0cf7190885f1 frontend/src/features/transfers/TransferTabs.tsx`
- `e4466ba746d4218add06ca6ed5535ac089d44d3a frontend/src/shared/ui/DataSection.tsx`
- `ea15c3571b65d878fc3d8d71e854905468ed0b4e frontend/src/shared/ui/EditorLayout.tsx`
- `9248c2c9f96211998d5c78d87f1755ab04e212f5 frontend/src/shared/ui/HelpPopover.tsx`

## Casos

- [UC-PLA-01](flows/08-plataforma.md#uc-pla-01) · GET `/platform/catalog/categories`
- [UC-PLA-02](flows/08-plataforma.md#uc-pla-02) · GET `/platform/session`
- [UC-PLA-03](flows/08-plataforma.md#uc-pla-03) · GET `/platform/tenants`
- [UC-PLA-04](flows/08-plataforma.md#uc-pla-04) · GET `/platform/tenants/{tenantId:guid}/details`
- [UC-PLA-05](flows/08-plataforma.md#uc-pla-05) · GET `/platform/identities/{userId}`
- [UC-PLA-06](flows/08-plataforma.md#uc-pla-06) · GET `/platform/tenant-requests/{requestKey}`
- [UC-PLA-07](flows/08-plataforma.md#uc-pla-07) · GET `/platform/tenants/{tenantId}/initial-owner`
- [UC-PLA-08](flows/08-plataforma.md#uc-pla-08) · POST `/platform/tenants/{tenantId}/initial-owner/change`
- [UC-PLA-09](flows/08-plataforma.md#uc-pla-09) · POST `/platform/tenants/{tenantId}/initial-owner/revoke`
- [UC-PLA-10](flows/08-plataforma.md#uc-pla-10) · POST `/platform/tenants/{tenantId}/licenses/{licenseId}/renew`
- [UC-PLA-11](flows/08-plataforma.md#uc-pla-11) · POST `/platform/tenants/{tenantId}/licenses/{licenseId}/suspend`
- [UC-PLA-12](flows/08-plataforma.md#uc-pla-12) · POST `/platform/tenants/{tenantId}/licenses/{licenseId}/reactivate`
- [UC-PLA-13](flows/08-plataforma.md#uc-pla-13) · POST `/platform/tenants`
- [UC-PLA-14](flows/08-plataforma.md#uc-pla-14) · POST `/platform/tenants/with-owner`
- [UC-PLA-15](flows/08-plataforma.md#uc-pla-15) · POST `/platform/tenants/{tenantId}/bootstrap-owner`
- [UC-PLA-16](flows/08-plataforma.md#uc-pla-16) · POST `/platform/catalog/categories`
- [UC-AVI-01](flows/06-avisos-auditoria.md#uc-avi-01) · GET `/api/tenants/{tenantId}/audit`
- [UC-REP-01](flows/05-reportes-comisiones.md#uc-rep-01) · GET `/api/tenants/{tenantId}/commissions/settings`
- [UC-REP-02](flows/05-reportes-comisiones.md#uc-rep-02) · PUT `/api/tenants/{tenantId}/commissions/settings`
- [UC-REP-03](flows/05-reportes-comisiones.md#uc-rep-03) · GET `/api/tenants/{tenantId}/commissions/rules`
- [UC-REP-04](flows/05-reportes-comisiones.md#uc-rep-04) · PUT `/api/tenants/{tenantId}/products/{productId}/commission-rule`
- [UC-REP-05](flows/05-reportes-comisiones.md#uc-rep-05) · POST `/api/tenants/{tenantId}/commissions/rules/{ruleId}/deactivate`
- [UC-REP-06](flows/05-reportes-comisiones.md#uc-rep-06) · GET `/api/tenants/{tenantId}/reports/commissions`
- [UC-CAT-01](flows/02-catalogo.md#uc-cat-01) · GET `/api/tenants/{tenantId}/branches/{branchId}/operational-products`
- [UC-CAT-02](flows/02-catalogo.md#uc-cat-02) · GET `/api/tenants/{tenantId}/branches/{branchId}/operational-categories`
- [UC-CAT-03](flows/02-catalogo.md#uc-cat-03) · PUT `/api/tenants/{tenantId}/products/{productId}/local-data`
- [UC-CAT-04](flows/02-catalogo.md#uc-cat-04) · GET `/api/tenants/{tenantId}/categories`
- [UC-CAT-05](flows/02-catalogo.md#uc-cat-05) · GET `/api/tenants/{tenantId}/global-products`
- [UC-CAT-06](flows/02-catalogo.md#uc-cat-06) · GET `/api/tenants/{tenantId}/products`
- [UC-CAT-07](flows/02-catalogo.md#uc-cat-07) · GET `/api/tenants/{tenantId}/branches/{branchId}/products/{productId}`
- [UC-CAT-08](flows/02-catalogo.md#uc-cat-08) · GET `/api/tenants/{tenantId}/branches/{branchId}/products/search`
- [UC-CAT-09](flows/02-catalogo.md#uc-cat-09) · POST `/api/tenants/{tenantId}/products/local`
- [UC-CAT-10](flows/02-catalogo.md#uc-cat-10) · POST `/api/tenants/{tenantId}/products/from-global`
- [UC-CAT-11](flows/02-catalogo.md#uc-cat-11) · PUT `/api/tenants/{tenantId}/products/{productId}/prices`
- [UC-CAT-12](flows/02-catalogo.md#uc-cat-12) · PUT `/api/tenants/{tenantId}/products/{productId}/status`
- [UC-CAT-13](flows/02-catalogo.md#uc-cat-13) · GET `/api/tenants/{tenantId}/products/{productId}/unit-configuration`
- [UC-CAT-14](flows/02-catalogo.md#uc-cat-14) · PUT `/api/tenants/{tenantId}/products/{productId}/unit-configuration`
- [UC-CAT-15](flows/02-catalogo.md#uc-cat-15) · PUT `/api/tenants/{tenantId}/products/{productId}/units`
- [UC-TRA-01](flows/04-transferencias.md#uc-tra-01) · POST `/api/tenants/{tenantId}/branches/{branchId}/cash-transfers`
- [UC-TRA-02](flows/04-transferencias.md#uc-tra-02) · GET `/api/tenants/{tenantId}/cash-transfers/pending`
- [UC-TRA-03](flows/04-transferencias.md#uc-tra-03) · GET `/api/tenants/{tenantId}/cash-transfers/{cashTransferId}`
- [UC-TRA-04](flows/04-transferencias.md#uc-tra-04) · POST `/api/tenants/{tenantId}/cash-transfers/{cashTransferId}/receive`
- [UC-ACC-01](flows/07-acceso-personal.md#uc-acc-01) · GET `/api/tenants/{tenantId}/employees`
- [UC-ACC-02](flows/07-acceso-personal.md#uc-acc-02) · POST `/api/tenants/{tenantId}/employees`
- [UC-ACC-03](flows/07-acceso-personal.md#uc-acc-03) · POST `/api/tenants/{tenantId}/employees/{membershipId}/deactivate`
- [UC-ACC-04](flows/07-acceso-personal.md#uc-acc-04) · PUT `/api/tenants/{tenantId}/employees/{membershipId}/branches`
- [UC-ACC-05](flows/07-acceso-personal.md#uc-acc-05) · PUT `/api/tenants/{tenantId}/employees/{membershipId}/schedule`
- [UC-ACC-06](flows/07-acceso-personal.md#uc-acc-06) · GET `/api/tenants/{tenantId}/employee-invitations/{invitationId}`
- [UC-ACC-07](flows/07-acceso-personal.md#uc-acc-07) · GET `/api/tenants/{tenantId}/employee-invitations`
- [UC-ACC-08](flows/07-acceso-personal.md#uc-acc-08) · POST `/api/tenants/{tenantId}/employee-invitations`
- [UC-ACC-09](flows/07-acceso-personal.md#uc-acc-09) · POST `/api/tenants/{tenantId}/employee-invitations/{invitationId}/approve`
- [UC-ACC-10](flows/07-acceso-personal.md#uc-acc-10) · POST `/api/tenants/{tenantId}/employee-invitations/{invitationId}/revoke`
- [UC-ACC-11](flows/07-acceso-personal.md#uc-acc-11) · POST `/api/employee-invitations/accept`
- [UC-ACC-12](flows/07-acceso-personal.md#uc-acc-12) · GET `/auth/csrf`
- [UC-ACC-13](flows/07-acceso-personal.md#uc-acc-13) · POST `/auth/login`
- [UC-ACC-14](flows/07-acceso-personal.md#uc-acc-14) · GET `/auth/google/callback`
- [UC-ACC-15](flows/07-acceso-personal.md#uc-acc-15) · GET `/auth/me`
- [UC-ACC-16](flows/07-acceso-personal.md#uc-acc-16) · POST `/auth/logout`
- [UC-INT-01](flows/09-contingencia-integraciones.md#uc-int-01) · GET `/health`
- [UC-INT-02](flows/09-contingencia-integraciones.md#uc-int-02) · GET `/health/ready`
- [UC-CAT-16](flows/02-catalogo.md#uc-cat-16) · GET `/api/tenants/{tenantId}/product-imports/template`
- [UC-CAT-17](flows/02-catalogo.md#uc-cat-17) · POST `/api/tenants/{tenantId}/product-imports`
- [UC-CAT-18](flows/02-catalogo.md#uc-cat-18) · GET `/api/tenants/{tenantId}/product-imports/{importJobId}`
- [UC-CAT-19](flows/02-catalogo.md#uc-cat-19) · POST `/api/tenants/{tenantId}/product-imports/{importJobId}/recover-interrupted`
- [UC-COM-01](flows/03-compras-inventario.md#uc-com-01) · GET `/api/tenants/{tenantId}/branches/{branchId}/inventory/expiring-lots`
- [UC-COM-02](flows/03-compras-inventario.md#uc-com-02) · PUT `/api/tenants/{tenantId}/branches/{branchId}/products/{productId}/stock-threshold`
- [UC-COM-03](flows/03-compras-inventario.md#uc-com-03) · GET `/api/tenants/{tenantId}/branches/{branchId}/inventory/stock`
- [UC-COM-04](flows/03-compras-inventario.md#uc-com-04) · GET `/api/tenants/{tenantId}/branches/{branchId}/inventory/lots`
- [UC-COM-05](flows/03-compras-inventario.md#uc-com-05) · POST `/api/tenants/{tenantId}/inventory/lots/{lotId}/adjustments`
- [UC-AVI-02](flows/06-avisos-auditoria.md#uc-avi-02) · GET `/api/tenants/{tenantId}/notifications`
- [UC-AVI-03](flows/06-avisos-auditoria.md#uc-avi-03) · GET `/api/tenants/{tenantId}/notifications/unread-count`
- [UC-AVI-04](flows/06-avisos-auditoria.md#uc-avi-04) · POST `/api/tenants/{tenantId}/notifications/{notificationId}/read`
- [UC-CAJ-01](flows/01-caja-pos.md#uc-caj-01) · GET `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}`
- [UC-CAJ-02](flows/01-caja-pos.md#uc-caj-02) · POST `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions`
- [UC-CAJ-03](flows/01-caja-pos.md#uc-caj-03) · GET `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions/current`
- [UC-CAJ-04](flows/01-caja-pos.md#uc-caj-04) · GET `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions/{cashSessionId}`
- [UC-CAJ-05](flows/01-caja-pos.md#uc-caj-05) · GET `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions/{cashSessionId}/reconciliation`
- [UC-CAJ-06](flows/01-caja-pos.md#uc-caj-06) · POST `/api/tenants/{tenantId}/branches/{branchId}/cash-sessions/{cashSessionId}/close`
- [UC-CAJ-07](flows/01-caja-pos.md#uc-caj-07) · GET `/api/tenants/{tenantId}/cash-sessions/active`
- [UC-CAJ-08](flows/01-caja-pos.md#uc-caj-08) · POST `/api/tenants/{tenantId}/branches/{branchId}/sales`
- [UC-CAJ-09](flows/01-caja-pos.md#uc-caj-09) · GET `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/draft`
- [UC-CAJ-10](flows/01-caja-pos.md#uc-caj-10) · PUT `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/lines`
- [UC-CAJ-11](flows/01-caja-pos.md#uc-caj-11) · POST `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/confirm`
- [UC-CAJ-12](flows/01-caja-pos.md#uc-caj-12) · GET `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/ticket`
- [UC-CAJ-13](flows/01-caja-pos.md#uc-caj-13) · POST `/api/tenants/{tenantId}/branches/{branchId}/sales/{saleId}/void`
- [UC-COM-06](flows/03-compras-inventario.md#uc-com-06) · POST `/api/tenants/{tenantId}/suppliers`
- [UC-COM-07](flows/03-compras-inventario.md#uc-com-07) · GET `/api/tenants/{tenantId}/branches/{branchId}/suppliers`
- [UC-COM-08](flows/03-compras-inventario.md#uc-com-08) · POST `/api/tenants/{tenantId}/branches/{branchId}/purchases`
- [UC-COM-09](flows/03-compras-inventario.md#uc-com-09) · GET `/api/tenants/{tenantId}/branches/{branchId}/purchases/{purchaseId}`
- [UC-COM-10](flows/03-compras-inventario.md#uc-com-10) · PUT `/api/tenants/{tenantId}/purchases/{purchaseId}/lines`
- [UC-COM-11](flows/03-compras-inventario.md#uc-com-11) · POST `/api/tenants/{tenantId}/purchases/{purchaseId}/confirm`
- [UC-COM-12](flows/03-compras-inventario.md#uc-com-12) · GET `/api/tenants/{tenantId}/branches/{branchId}/purchases/search`
- [UC-REP-07](flows/05-reportes-comisiones.md#uc-rep-07) · GET `/api/tenants/{tenantId}/reports/overview`
- [UC-REP-08](flows/05-reportes-comisiones.md#uc-rep-08) · GET `/api/tenants/{tenantId}/reports/dashboard`
- [UC-REP-09](flows/05-reportes-comisiones.md#uc-rep-09) · GET `/api/tenants/{tenantId}/reports/sales`
- [UC-REP-10](flows/05-reportes-comisiones.md#uc-rep-10) · GET `/api/tenants/{tenantId}/reports/stock-risk`
- [UC-REP-11](flows/05-reportes-comisiones.md#uc-rep-11) · GET `/api/tenants/{tenantId}/reports/expirations`
- [UC-REP-12](flows/05-reportes-comisiones.md#uc-rep-12) · GET `/api/tenants/{tenantId}/reports/inventory-capital`
- [UC-REP-13](flows/05-reportes-comisiones.md#uc-rep-13) · GET `/api/tenants/{tenantId}/reports/product-rotation`
- [UC-REP-14](flows/05-reportes-comisiones.md#uc-rep-14) · POST `/api/tenants/{tenantId}/reports/exports`
- [UC-INT-03](flows/09-contingencia-integraciones.md#uc-int-03) · GET `/api/tenants/{tenantId}/branches/{branchId}/sync/devices`
- [UC-INT-04](flows/09-contingencia-integraciones.md#uc-int-04) · POST `/api/tenants/{tenantId}/branches/{branchId}/sync/devices`
- [UC-INT-05](flows/09-contingencia-integraciones.md#uc-int-05) · POST `/api/tenants/{tenantId}/sync/devices/{deviceId}/revoke`
- [UC-INT-06](flows/09-contingencia-integraciones.md#uc-int-06) · POST `/api/tenants/{tenantId}/sync/batches`
- [UC-INT-07](flows/09-contingencia-integraciones.md#uc-int-07) · GET `/api/tenants/{tenantId}/sync/conflicts`
- [UC-INT-08](flows/09-contingencia-integraciones.md#uc-int-08) · POST `/api/tenants/{tenantId}/sync/conflicts/{conflictId}/resolve`
- [UC-INT-09](flows/09-contingencia-integraciones.md#uc-int-09) · GET `/api/tenants/{tenantId}/sync/devices/{deviceId}/catalog/snapshot`
- [UC-INT-10](flows/09-contingencia-integraciones.md#uc-int-10) · GET `/api/tenants/{tenantId}/sync/devices/{deviceId}/catalog/changes`
- [UC-ACC-17](flows/07-acceso-personal.md#uc-acc-17) · GET `/api/me/tenants`
- [UC-ACC-18](flows/07-acceso-personal.md#uc-acc-18) · GET `/api/tenants/{tenantId}/access`
- [UC-ACC-19](flows/07-acceso-personal.md#uc-acc-19) · GET `/api/tenants/{tenantId}/branches`
- [UC-ACC-20](flows/07-acceso-personal.md#uc-acc-20) · GET `/api/tenants/{tenantId}/legal-entities`
- [UC-ACC-21](flows/07-acceso-personal.md#uc-acc-21) · GET `/api/tenants/{tenantId}/license`
- [UC-ACC-22](flows/07-acceso-personal.md#uc-acc-22) · POST `/api/tenants/{tenantId}/legal-entities`
- [UC-ACC-23](flows/07-acceso-personal.md#uc-acc-23) · POST `/api/tenants/{tenantId}/branches`
- [UC-ACC-24](flows/07-acceso-personal.md#uc-acc-24) · GET `/api/tenants/{tenantId}/branches/{branchId}/operations/{requestId}`
- [UC-TRA-05](flows/04-transferencias.md#uc-tra-05) · GET `/api/tenants/{tenantId}/transfers`
- [UC-TRA-06](flows/04-transferencias.md#uc-tra-06) · POST `/api/tenants/{tenantId}/transfers`
- [UC-TRA-07](flows/04-transferencias.md#uc-tra-07) · GET `/api/tenants/{tenantId}/transfers/{transferId}`
- [UC-TRA-08](flows/04-transferencias.md#uc-tra-08) · POST `/api/tenants/{tenantId}/transfers/{transferId}/approve`
- [UC-TRA-09](flows/04-transferencias.md#uc-tra-09) · POST `/api/tenants/{tenantId}/transfers/{transferId}/cancel`
- [UC-TRA-10](flows/04-transferencias.md#uc-tra-10) · POST `/api/tenants/{tenantId}/transfers/{transferId}/dispatch`
- [UC-TRA-11](flows/04-transferencias.md#uc-tra-11) · POST `/api/tenants/{tenantId}/transfers/{transferId}/receive`
- [UC-CAJ-14](flows/01-caja-pos.md#uc-caj-14) · Buscar, elegir presentación y editar carrito
- [UC-CAJ-15](flows/01-caja-pos.md#uc-caj-15) · Imprimir ticket interno
- [UC-CAJ-16](flows/01-caja-pos.md#uc-caj-16) · Recuperar cierre incierto
- [UC-CAJ-17](flows/01-caja-pos.md#uc-caj-17) · Relocalizar caja cerrada tras recarga
- [UC-CAJ-18](flows/01-caja-pos.md#uc-caj-18) · Elegir vendedor y preparar cobro mixto
- [UC-CAT-20](flows/02-catalogo.md#uc-cat-20) · Cambiar vista y filtrar página de catálogo
- [UC-CAT-21](flows/02-catalogo.md#uc-cat-21) · Recuperar escritura de producto
- [UC-CAT-22](flows/02-catalogo.md#uc-cat-22) · Borrado físico de producto
- [UC-COM-13](flows/03-compras-inventario.md#uc-com-13) · Editar recepción local y volver
- [UC-COM-14](flows/03-compras-inventario.md#uc-com-14) · Recuperar recepción o proveedor
- [UC-COM-15](flows/03-compras-inventario.md#uc-com-15) · Asignación FEFO
- [UC-TRA-12](flows/04-transferencias.md#uc-tra-12) · Recuperar solicitud original de mercadería
- [UC-TRA-13](flows/04-transferencias.md#uc-tra-13) · Recuperar despacho original de sencillo
- [UC-REP-15](flows/05-reportes-comisiones.md#uc-rep-15) · Calcular y revertir comisión durante venta
- [UC-REP-16](flows/05-reportes-comisiones.md#uc-rep-16) · Filtrar consulta, paginar y exportar
- [UC-REP-17](flows/05-reportes-comisiones.md#uc-rep-17) · Consultar sugerencias de reposición desde un consumidor interno
- [UC-AVI-05](flows/06-avisos-auditoria.md#uc-avi-05) · Abrir detalle de aviso
- [UC-AVI-06](flows/06-avisos-auditoria.md#uc-avi-06) · Generar alertas periódicas de vencimiento
- [UC-AVI-07](flows/06-avisos-auditoria.md#uc-avi-07) · Recuperar lectura ambigua
- [UC-ACC-25](flows/07-acceso-personal.md#uc-acc-25) · Elegir o cambiar botica y sede
- [UC-ACC-26](flows/07-acceso-personal.md#uc-acc-26) · Primera sede y entrada automática
- [UC-ACC-27](flows/07-acceso-personal.md#uc-acc-27) · Reclamar primer Owner mediante callback
- [UC-PLA-17](flows/08-plataforma.md#uc-pla-17) · Navegar las seis áreas administrativas
- [UC-PLA-18](flows/08-plataforma.md#uc-pla-18) · Solicitar purga e inspeccionar retención
- [UC-INT-11](flows/09-contingencia-integraciones.md#uc-int-11) · Preparar o bloquear dispositivo PWA
- [UC-INT-12](flows/09-contingencia-integraciones.md#uc-int-12) · Encolar venta local, retomar y conciliar
- [UC-INT-13](flows/09-contingencia-integraciones.md#uc-int-13) · Procesar catálogo oficial DIGEMID
- [UC-INT-14](flows/09-contingencia-integraciones.md#uc-int-14) · Importar UBL de proveedor
- [UC-INT-15](flows/09-contingencia-integraciones.md#uc-int-15) · Entrega CPE y consultas de proveedor
- [UC-INT-16](flows/09-contingencia-integraciones.md#uc-int-16) · Comprobar conexión local de plataforma
- [UC-INT-17](flows/09-contingencia-integraciones.md#uc-int-17) · Recuperación y respaldo CLI
- [UC-INT-18](flows/09-contingencia-integraciones.md#uc-int-18) · Servir SPA y caché offline

## Jobs y servicios registrados

- `backend/src\MediPOS.Infrastructure\DependencyInjection.cs:154:            services.AddHostedService<ExpirationAlertsScheduler>();`
- `backend/src\MediPOS.Api\Authentication\LocalDevelopmentPlatform.cs:59:        builder.Services.AddHostedService<LocalDevelopmentPlatformDatabaseCheck>();`


## Cruce router → casos

S representa `/boticas/:tenantId/sucursales/:branchId`. Las declaraciones de índice anidadas se cuentan separadas en inventario por su función. Los rangos abreviados referencian los IDs del índice y la matriz.

| Ruta o superficie | Casos o exclusión |
|---|---|
| `/` | ACC-12/13/14/15 |
| `/aceptar-invitacion` | ACC-11 |
| `/contingencia` | INT-11/12 |
| `/entrada` | ACC-15, PLA-02 |
| `/plataforma e índice` | PLA-01 a PLA-17 con área y tenant por parámetros |
| `/seleccionar-botica` | ACC-17/25 |
| `/boticas/:tenantId/sucursales` | ACC-18/19/25 |
| `/boticas/:tenantId/configuracion` | ACC-20/21/22/23/26 |
| `S (ruta del layout comercial)` | ACC-18/25 |
| `S (índice)` | REP-07/08 y puntos de entrada del menú |
| `S/ventas` | CAJ-01/08–15/18, CAT-01/02/08 |
| `S/caja` | CAJ-02 a CAJ-07 y CAJ-16/17 |
| `S/productos` | CAT-03 a CAT-15 y CAT-20–22 |
| `S/compras` | COM-06 a COM-14 |
| `S/inventario` | COM-01 a COM-05 |
| `S/empleados` | ACC-01 a ACC-10 |
| `S/transferencias` | TRA-05 a TRA-12 |
| `S/transferencias/sencillo` | TRA-01 a TRA-04 y TRA-13 |
| `S/reportes` | REP-06 a REP-14 y REP-16 |
| `S/comisiones` | REP-01 a REP-05 y REP-15 |
| `S/notificaciones` | AVI-02 a AVI-07 |
| `S/configuracion` | ACC-20–23/26, AVI-01 y CAT-16–19 por enlace |
| `S/auditoria` | AVI-01 |
| `S/importacion` | CAT-16 a CAT-19 |
| `S/sincronizacion` | INT-03 a INT-12 |
| `S/:module` | Fallback comercial ModulePage, sin operación propia |
| `*` | Página de ruta desconocida, enlace a entrada y sin operación propia |

### Direcciones de continuidad

- Caja `cash` y `closingCash`. POS `sale` y marcadores de preparación o checkout. Criterio server-side en los UC de Caja/POS.
- Compras `purchase`, `receiptPending`, `receiptRequest`, `supplierPending`, `nueva`, `origen`. Excel `importPending` e `importJob`. Inventario `inventoryPending`. Proveedor y recepción usan intenciones distintas.
- Transferencias `goodsId`, `cashTransferId` y la intención de creación correspondiente. Avisos `notificationRead`. Invitaciones `invitationRequest`, `invitationTarget` y fragmento de ticket. Plataforma `area`, `tenant`, `creationKey`, `platformPending` y `categoryPending`. Son parámetros de navegación/recuperación, sujetos a la autorización original.

### Usos internos o sin consumidor habitual

- `GET /health` y `/health/ready` son operativos (UC-INT-01/02). `MapOpenApi` y fallback SPA son infraestructura en Program.cs:60–63, fuera del conteo de rutas operativas literales, cobertura en UC-INT-18. RequestObservationMiddleware registra observabilidad y no añade una acción.
- `POST /employees` es compatibilidad explícita con rechazo de alta directa (UC-ACC-02). `POST /products/{id}/units` sería un método incorrecto; la ruta registrada usa PUT y es una alternativa histórica cuyo consumidor exacto queda pendiente (UC-CAT-15).
- Alta de botica sin Gmail, bootstrap Owner por UUID y consulta de identidad son superficies históricas/administrativas (UC-PLA-05/13/15); el recorrido normal consume with-owner.
- Reposición (FR-INV-008), UBL, publicación oficial, solicitud de purga y CPE tienen símbolos de aplicación o persistencia; la búsqueda de un consumidor operativo sigue limitada al mapa. Reposición usa `OwnerReplenishmentReader.cs` y queda asociada a reportes de riesgo, sin endpoint público encontrado. Los demás están en UC-PLA-18 y UC-INT-13/14/15.
- El CLI `backend/tools/MediPOS.Recovery/RecoveryWorkflow.cs` se vincula a UC-INT-17, por fuera de React/API comercial. Jobs de vencimiento y arranque en UC-AVI-06 e INT-16. Preparación PWA usa el navegador y flags; no se infiere estado de DEV de appsettings versionado.
