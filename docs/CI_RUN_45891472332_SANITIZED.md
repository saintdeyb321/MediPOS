# Evidencia sanitizada CI — MediPOS

Fuente: resumen emitido por los jobs de GitHub Actions, consultado mediante GitHub el 26/11/2026. No contiene logs crudos, datos reales ni credenciales.

- Workflow: Backend validation - PostgreSQL 18
- Run: https://github.com/saintdeyb321/MediPOS/actions/runs/45891472332
- Run ID: 45891472332; attempt: 1; estado: completed; conclusión: failure.
- Rama: ci/postgres-diagnostics-8bc6b4c
- SHA: `5d3889806e11fea23a3ba19402c22c15635a1301`
- Backend job: https://github.com/saintdeyb321/MediPOS/actions/runs/45891472332/job/140154968483 — success.
- Frontend job: https://github.com/saintdeyb321/MediPOS/actions/runs/45891472332/job/140154969224 — failure.

## Backend — resumen del gate

```text
Tested commit: 5d3889806e11fea23a3ba19402c22c15635a1301
sdk: success
restore: success
build: success
diagnostics: success
audit: success
unit: success
architecture: success
format: success
containers: success
postgres: success
unit: total=2694, executed=2694, passed=2694, failed=0, skipped=0
architecture: total=8, executed=8, passed=8, failed=0, skipped=0
postgres: total=905, executed=905, passed=905, failed=0, skipped=0
Actual PostgreSQL server version: 18.7
SUITE MediPOS.IntegrationTests.Modules.IdentityAccess.EmployeeInvitationPersistenceTests: total=6, passed=6, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.IdentityAccess.IdentityPersistenceTests: total=17, passed=17, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.IdentityAccess.InitialOwnerOnboardingPersistenceTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.IdentityAccess.OperationalAccessPersistenceTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.IdentityAccess.OwnerConcurrencyTests: total=6, passed=6, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.IdentityAccess.PlatformAdministrationPersistenceTests: total=5, passed=5, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.IdentityAccess.PlatformBootstrapOwnerPersistenceTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.TenancyLicensing.PlatformDirectoryPersistenceTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.TenancyLicensing.TenancyLicensingTests: total=12, passed=12, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.TenancyLicensing.TenantCreationIdempotencyPersistenceTests: total=15, passed=15, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.TenancyLicensing.TransactionIdentityTests: total=5, passed=5, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Tenancy.CommercialDirectoryPostgreSqlTests: total=10, passed=10, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Tenancy.RuntimeRoleHardeningTests: total=10, passed=10, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Tenancy.SecurityHardeningTests: total=7, passed=7, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Tenancy.TenantConnectionReuseTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Tenancy.TenantQueryProtectionTests: total=4, passed=4, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Tenancy.TenantRowLevelSecurityTests: total=19, passed=19, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.AuditSupport.AuditFoundationTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Branches.BranchesPersistenceTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashCloseConcurrencyTests: total=5, passed=5, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashCloseIsolationTests: total=4, passed=4, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashClosePersistenceTests: total=42, passed=42, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashSessionIsolationTests: total=10, passed=10, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashSessionPersistenceTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashTransferConcurrencyTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashTransferIsolationTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashTransferPersistenceTests: total=9, passed=9, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashTransferRecoverabilityTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.CatalogFoundationTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.InterruptedProductImportMigrationTests: total=1, passed=1, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.InterruptedProductImportTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationConcurrencyTests: total=9, passed=9, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationPersistenceTests: total=15, passed=15, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationUnicodeTests: total=4, passed=4, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.OfficialImport.OfficialCatalogStagingTests: total=14, passed=14, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh.OfficialRefreshCommercialIsolationTests: total=1, passed=1, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh.OfficialRefreshConcurrencyTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh.OfficialRefreshPersistenceTests: total=16, passed=16, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh.OfficialRefreshSourceOrderTests: total=16, passed=16, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.PharmaNormalizationIntegrationTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.ProductImportIntegrationTests: total=9, passed=9, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.ProductImportIsolationTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.ProductSearchIntegrationTests: total=15, passed=15, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.ProductSearchMigrationTests: total=1, passed=1, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.ProductSearchPersistenceTests: total=5, passed=5, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Catalog.ProductUnitIntegrationTests: total=10, passed=10, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Commissions.CommissionAtomicityTests: total=5, passed=5, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Commissions.CommissionConcurrencyTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Commissions.CommissionPostingTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Commissions.CommissionSchemaIsolationTests: total=30, passed=30, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.ElectronicDocuments.ElectronicDocumentPersistenceTests: total=22, passed=22, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Inventory.InventoryMigrationTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Inventory.InventoryPersistenceTests: total=7, passed=7, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Inventory.InventoryQueriesTests: total=1, passed=1, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Notifications.PersistentNotificationsTests: total=24, passed=24, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogConcurrencyTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogImportAndPagingTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogMutationTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogProvisioningTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogSecurityTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSalePostingTests: total=10, passed=10, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSaleSecurityTests: total=5, passed=5, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSyncBatchInterruptionTests: total=7, passed=7, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSyncBatchPersistenceTests: total=6, passed=6, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSyncConcurrencyTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSyncPersistenceTests: total=28, passed=28, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Purchasing.PurchaseWorkflowIntegrationTests: total=7, passed=7, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Purchasing.PurchasingIsolationIntegrationTests: total=20, passed=20, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Purchasing.RecoverableOperationPostgreSqlTests: total=16, passed=16, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Purchasing.Ubl.SupplierUblIntegrationTests: total=36, passed=36, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Purchasing.Ubl.SupplierUblStructureIntegrationTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.FilteredSalesAggregationTests: total=4, passed=4, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.OperationalReportsConsistencyTests: total=9, passed=9, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.OperationalReportsPersistenceTests: total=22, passed=22, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.OperationalReportsPoolingTests: total=1, passed=1, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.OwnerCommissionsReportIntegrationTests: total=4, passed=4, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.OwnerOverviewConsistencyTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.OwnerOverviewIsolationTests: total=6, passed=6, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.OwnerOverviewPersistenceTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.ReplenishmentPersistenceTests: total=16, passed=16, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.ReportExportPersistenceTests: total=22, passed=22, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Reporting.StockThresholdIsolationTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.InternalTicketIsolationTests: total=4, passed=4, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.InternalTicketPersistenceTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.SaleCheckoutConcurrencyTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.SaleCheckoutIsolationTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.SaleCheckoutPersistenceTests: total=17, passed=17, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.SaleDraftIsolationTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.SaleDraftPersistenceTests: total=7, passed=7, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.SaleVoidConcurrencyTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.SaleVoidIsolationTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.SalesPos.SaleVoidPersistenceTests: total=12, passed=12, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Transfers.TransferConcurrencyTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Transfers.TransferIsolationTests: total=17, passed=17, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Transfers.TransferPersistenceTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Observability.RuntimeReadinessPostgreSqlTests: total=2, passed=2, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Performance.PerformanceHarnessPostgreSqlTests: total=3, passed=3, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.PostgreSqlSmokeTests: total=1, passed=1, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Recovery.RecoveryPostgreSqlTests: total=20, passed=20, failed=0, skipped=0
B8.6.3.1: PASSED, cases=8, passed=8
B8.6.3.2: PASSED, cases=7, passed=7
B8.6.4: PASSED, cases=8, passed=8
Overall: CODE_AND_POSTGRESQL_GATE_PASSED (not production acceptance)
```

Reconciliación: 99 clases, 905 pruebas de integración según suma de las líneas SUITE; los conteos B8 son subconjuntos, no pruebas adicionales. Publicación de evidencia y limpieza de recursos efímeros: success.

## Frontend — resumen del gate

```text
checkout: success
node: success
identity: success
install: success
build: success
lint: success
test: failure
Vitest: total=642, PASS=641, FAIL=1, SKIP=0, failed suites=1
Overall: NOT_APPROVED
```

El paso de publicación de evidencia también falló por el gate negativo. El resumen no identifica la prueba fallida; el reporter JSON no expone ese nombre en la salida consultada. No atribuirlo a F-006 ni al producto sin reproducción adicional. No hubo E4 acreditada. No extrapolar a DEV, OAuth real, servicios fiscales o aceptación comercial. Mapear métodos y aserciones al UC y frontera concreta antes de promover E3. Las pruebas no acreditan por sí solas el incidente exacto 100→10 ni resuelven F-002.
