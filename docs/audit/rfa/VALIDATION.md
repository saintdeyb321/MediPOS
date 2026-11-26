# RFA2 · Validación conductual y persistencia

Baseline `2c54e1f71aa6f66a67190d37eacda6ada71f4b0f`, rama `ci/postgres-diagnostics-8bc6b4c`, 26/11/2026 Lima. `git diff --stat <baseline>` vacío al inicio. Dos documentos del encargo sin rastrear preservados. Windows, Node 24.18.1, npm 11.16.0, Vitest 5.0.4 y SDK .NET 10.0.401. Frontend cargado desde fuentes de ese checkout, con dependencias locales y jsdom en `https://medipos.test/`. Los participantes y datos son sintéticos. E2 de UI mide comportamiento y solicitudes interceptadas por dobles HTTP. Los resultados de runner están separados de los veredictos de caso en [MATRIX](MATRIX.csv).

## Aislamiento y límites

- **B-01 · E3 BLOCKED.** Docker no está disponible en PATH y no existe un destino aislado acreditado. `PostgreSqlFixture.cs:41–62` crea contenedor propio `postgres:18-alpine`, ejecuta las migraciones sobre él y crea runtime efímero `NOSUPERUSER NOBYPASSRLS`. No se inició fixture. ID de contenedor, versión SQL, migraciones aplicadas y rol efectivo quedan sin verificar. DEV, LAB, QA y producción se excluyeron.
- **B-02 · E4 BLOCKED.** Falta composición API + DB sintética aislada y autenticación de pruebas para navegador. jsdom, navegación por MemoryRouter y recarga simulada no son Chrome. Estado de DEV servido y esquema operativo NO_DETERMINADO.
- **B-03 · Servicios externos excluidos.** OIDC real, CPE y tráfico fiscal/proveedores no se ejecutaron. CPE conserva su estado configuracional desactivado; los casos de SDK/parser o transporte local se distinguen de una integración externa.

## Ejecuciones

`$result` representa la carpeta temporal del runner. Los JSON/TRX son artefactos privados no versionados. Las huellas corresponden al reporte emitido, que incluye metadatos del runner.

| RUN | Comando de selección y salida | PASS / FAIL / SKIP | Reporte SHA-256 |
|---|---|---:|---|
| RUN-01 | Desde frontend, `npm.cmd run test -- src/features/cash/CashPage.test.tsx --reporter=json --outputFile="$result/cash.json"` | 12 / 0 / 0 | `D19E552F080EDE9B8A49D949D98D35BE88CF199FF0D8CB07E62114385358187F` |
| RUN-02 | Copia aislada, `vitest run src/features/transfers/CashTransfersPage.test.tsx src/features/transfers/Rfa2CashContract.test.tsx --reporter=json --outputFile="$result/cash-transfer.json"` | 15 / 0 / 0 | `3464DC47003A304A89E54B02E71C0C775CD6D6CDA8E278CC9FEB4C81A00287D1` |
| RUN-03 | Desde frontend, `npm.cmd run test -- @paths --reporter=json --outputFile="$result/front-lotes.json"`. Archivos en tabla siguiente. Duración de comando 71,2 s. | 258 / 2 / 0 | `F9A8B8014F973EE86EEE1C173168CAC4C57F7373E5B6832FB09DE98E26C07EEB` |
| RUN-04 | Copia aislada, `vitest run Rfa2Finding --reporter=json --outputFile="$result/findings.json"` | 3 / 0 / 0 | `B4F9443CFD5A7CD35F3F36E01DC64CEEA87FC1EEA21BEAA6C469DF49AEC46136` |
| RUN-05 | `npm.cmd run test -- src/features/platform/PlatformPage.test.tsx src/features/transfers/TransfersPage.test.tsx --maxWorkers=1 -t 'navigates all six areas\|does not infer source authority' --reporter=json --outputFile="$result/recheck.json"` | 2 / 0 / 0 seleccionados, 42 excluidos por filtro | `D5F36BF350C9313DD7B83C8D9AA131DE638EEEF61D779AA9F894E3C92B643AEB` |
| RUN-06 | `npm.cmd run test -- src/features/employees/invitations.test.tsx src/features/employees/invitations.api.test.ts tests/pwa.test.ts --reporter=json --outputFile="$result/aux.json"` | 15 / 0 / 0 | `63DB0E94E4BD9355FDC8EF07FDFB12630EE39FFF2F2DD1B5B05CB08EE298F3C3` |
| RUN-07 | Copia aislada, `vitest run Rfa2CashReentry --reporter=json --outputFile="$result/cash-reentry-recheck.json"` | 1 / 0 / 0 | `2D0AD8423AD93436CEC9E292CC2765FCEEC53F16D1F9957F525B4BDA31D9E970` |
| RUN-08 | xUnit v3 en proceso, filtro .NET de abajo, desde binario compilado con salida segregada. Tiempo de runner 0,673 s. | 688 / 0 / 0 | `67BFBA0867B323BA1F46DB6800907F36909297D1580C41AF8E64DC8047421D28` |

**.NET.** El build estándar no pudo copiar las DLL ocupadas bajo `MediPOS.Api/bin` (MSB3021/MSB3027). La compilación con `-p:BaseOutputPath=obj/rfa2-bin/ -m:1 --disable-build-servers --no-restore` produjo `MediPOS.UnitTests.dll` SHA-256 `5AA06912DC563AD3340ADECEA9788FF9ADF62AB22AB3E3F580AED532033CCEC5`. VSTest abortó al no conectar con `testhost` en 90 s, sin resultados de prueba. Se reutilizó ese binario con el runner oficial en proceso, sin repetir la compilación. Se seleccionaron pruebas de dominio/aplicación, sin arranque de PostgreSQL ni servicios. Runtime 10.0.12, xUnit 4.0.1.

```powershell
$f = 'FullyQualifiedName~Modules.Cash|FullyQualifiedName~Modules.SalesPos|FullyQualifiedName~PurchasingWorkflowTests|FullyQualifiedName~PurchasingAuthorizationTests|FullyQualifiedName~AdjustStockTests|FullyQualifiedName~TransferUseCaseTests|FullyQualifiedName~CommissionConfigurationUseCaseTests|FullyQualifiedName~ReplenishmentAuthorizationTests|FullyQualifiedName~ExpirationJobTests|FullyQualifiedName~OperationalAccessTests|FullyQualifiedName~EmployeeInvitationApplicationTests|FullyQualifiedName~InitialOwnerOnboardingTests|FullyQualifiedName~OfflineSaleTests|FullyQualifiedName~SupplierUblStructureTests|FullyQualifiedName~SearchProductsTests'
dotnet tests/MediPOS.UnitTests/obj/rfa2-bin/Debug/net10.0/MediPOS.UnitTests.dll -filterVSTest $f -result-xml "$result/domain-xunit.xml"
```

RUN-07 se compiló tras corregir en la copia temporal el recorte del preámbulo: el primer intento abortó antes de ejecutar el caso. Totales adjudicados de runner: **306 PASS, 2 FAIL, 0 SKIP en Vitest**, incluidos ambos rechecks. **688 PASS, 0 FAIL, 0 SKIP en xUnit**. Los 42 casos excluidos en RUN-05 se ejecutaron en RUN-03 y no se cuentan como skips de la selección.

RUN-03 seleccionó los siguientes nombres exactos mediante `rg --files src`. RUN-04 y RUN-02 reutilizan la misma copia de fuentes, con [recetas reproducibles](evidence/rfa2-dom.md). La primera invocación de RUN-02 quedó antes del runner por redirección de archivos a un temporal inexistente. Se corrigió la ubicación y se creó la carpeta explícitamente.

| Grupo | Archivos de RUN-03 (sufijo `.test.tsx`) | PASS / FAIL |
|---|---|---:|
| POS | PosPage | 42 / 0 |
| Catálogo | ProductsPage, ImportPage | 35 / 0 |
| Compras / inventario | PurchasesPage, InventoryPage | 44 / 0 |
| Transferencias | TransfersPage | 15 / 1 |
| Reportes y comisiones | ReportsPage, CommissionsPage | 21 / 0 |
| Avisos y auditoría | NotificationsPage, AuditPage | 15 / 0 |
| Acceso | EntryRouter, TenantOnboardingPage, EmployeesPage | 54 / 0 |
| Plataforma | PlatformPage | 27 / 1 |
| Contingencia | ContingencyPage, WorkspaceOutboxSync | 5 / 0 |

## Escenarios y fronteras

Los escenarios se agrupan por recorrido. Un PASS parcial E2 permite registrar el nivel alcanzado para la acción ejercitada. La matriz conserva BLOCKED cuando B-01 o B-02 impide acreditar una frontera comercial esencial. Las filas E1 sin prueba dedicada retienen referencias estáticas. Las columnas vacías de pruebas o requisitos significan asociación individual no acreditada, no ausencia demostrada. Rutas auxiliares de hosting y health se evalúan como operación técnica.

| Escenario y casos | Esperado y observado | Veredicto / límite |
|---|---|---|
| SC-01 · Caja · F-001, CAJ-02–07/16–17 | FR-CASH-002/003/005. Abrir 100 sin movimientos, cerrar 10, esperado 100 y diferencia −90. RUN-07 envió un POST con 10, mostró faltante 90 del acuse simulado y distinguió URL limpia (activas vacías) de dirección original (recuperación por GET, cero reenvíos). RUN-01 cubrió 401/403/404/429/503 y rechazo de otro ámbito. | E2 parcial. Secuencia real, fila cerrada, auditoría, activas y reentrada real BLOCKED por B-01/B-02. Incidente DEV sin reproducción. |
| SC-02 · Sencillo · F-002, TRA-01–04/13, CAJ-04 | FR-CASH-004. Con saldo inicial 10, destino permitido y envío 1,0001, el shape de caja abierta que proyecta el código devuelve esperado null. RUN-02 caracteriza botón deshabilitado y cero POST. El doble heredado devuelve 10 y permite un único despacho; también verifica recepción simulada y recuperación de la intención. | **FAIL comercial en despacho E2**. E3 del GET, custodia/ledger y E4 BLOCKED. El test de caracterización pasa al observar el bloqueo. |
| SC-03 · POS · CAJ-01/08–15/18, CAT-01/02/08 | FR-POS. RUN-03 ejercita borrador, cantidades/presentaciones, recuperación, checkout, pago mixto y ticket o anulación con respuestas simuladas. | E2 parcial. Stock FEFO, pagos, comisiones, reversión, duplicación concurrente y cierre real BLOCKED. |
| SC-04 · Catálogo · F-003, CAT-03–07/09–14/16–21 | FR-CAT-001/006. RUN-04: producto en fila 51 ausente con filtro de página 1, visible tras ir a página 2, con uno y luego dos GET de lista. RUN-03 incluye revisión/versionado y recuperación de altas e importación. | E2 PASS para filtro de página (UC-CAT-20); alcance declarado. Mutaciones, Excel parcial y auditoría real BLOCKED. |
| SC-05 · Compras · F-004, COM-06–14 | Una recepción sin referencia queda fuera de búsqueda por referencia y es accesible con ID original. RUN-04 verifica ambas lecturas y cero escrituras. RUN-03 cubre recepción, revisión, respuesta perdida y referencia repetida sin tomarla como constancia. | E2 PASS parcial. Confirmación, rollback, recuperación persistida e inventario real BLOCKED. |
| SC-06 · Inventario · F-005, COM-01–05/15 | RUN-04 observa un GET de detalle en caché fría y el mismo total tras abrir Lotes para el mismo producto dentro de la ventana. RUN-03 cubre exactitud de ajuste, costo visible por rol y caché/scope. | E2 PASS parcial. Sin medir página multi-ID en red real, latencia ni reentrada de navegador. Efecto de movimientos y FEFO real BLOCKED. |
| SC-07 · Mercadería · TRA-05–12 | RUN-03 caracteriza solicitud, aprobación, lotes, recepción parcial y recuperación por clave. La aserción inmediata del GET de acceso de origen falló en el lote y pasó en RUN-05. | E2 parcial e intermitencia del test. Custodia, idempotencia persistida, concurrencia y aislamiento BLOCKED. |
| SC-08 · Reportes y comisiones · REP-01–17 | RUN-03 cubre métricas decimales, alcance de filtros, exportación interceptada, versión/estado de reglas, negativas y lecturas. | E2 parcial. Agregados contra ledger, reversión y reposición SQL BLOCKED. |
| SC-09 · Avisos/auditoría · AVI-01–07 | RUN-03 verifica contador, lectura ambigua y cambio de membresía; auditoría niega roles y oculta contenido de snapshots. | E2 parcial. Job, deduplicación e inmutabilidad real BLOCKED. |
| SC-10 · Ingreso, estructura y personal · ACC-01–27 | RUN-03 verifica admisión, denegaciones, 429/503, entidad/sede y recuperación, edición de personal y permiso por rol. RUN-06 cubre la invitación por clave, la aceptación desde sesión con fragmento removido, aprobación versionada sin userId del cliente, negativas y pérdida de respuesta. El primer Owner por Gmail exige vinculación autenticada y persistida. | E2 parcial. Google externo B-03; último Owner concurrente, reclamo, consentimiento y aislamiento real BLOCKED. |
| SC-11 · Plataforma · PLA-01–18 | RUN-03 verifica negativa de admisión, alta por correo/claves, respuesta confirmada seguida de un fallo de refresco, cambios auditables simulados y navegación. Faltó la proyección del destinatario dentro del tiempo del test de seis áreas, con lectura en curso; RUN-05 pasó. | E2 parcial e intermitencia del test. Sin persistencia ni autoridad demostradas en entorno real. |
| SC-12 · Contingencia e integraciones · INT-01–18 | RUN-03 verifica cola provisional con IndexedDB de prueba y conservación de ámbito al navegar y cambiar sucursal. RUN-06 verifica que el service worker generado excluye APIs privadas, auth, POST y otros orígenes de su caché. UBL, DIGEMID, Recovery, health y CPE conservan sus límites de exposición en las fichas. | E2 parcial. Posting/acuse PostgreSQL, PWA real y restauración BLOCKED. Sin llamadas externas. |

**Clasificación sin ejecución dedicada.** `CAT-22` es N/A porque el borrado físico no está expuesto como acción operativa. `ACC-02`, `ACC-12`, `INT-01` e `INT-16` conservan NOT_RUN para sus respuestas o composición específica. Las operaciones internas restantes y las integraciones deshabilitadas conservan su clasificación de alcance. Una prueba de bloqueo fail-closed o integración requiere el entorno correspondiente y no autoriza activar CPE.

## Reanudación E3 y E4

### Aserciones de aplicación y dominio en RUN-08

- SC-01/02/03: `CashCloseUseCaseTests`, `CashReconciliationDomainTests`, `CashTransferUseCaseTests`, `ConfirmSaleUseCaseTests`, `VoidSaleUseCaseTests` e `InternalTicketTests` ejercitan diferencia física, exclusión de medios digitales, autorización revalidada, despacho según pagos/reversiones/transferencias, negativos, FEFO, compensaciones y errores de posting. Los stores y transacciones son dobles y no prueban atomicidad SQL.
- SC-04/05/06/07/08: `SearchProductsTests`, `PurchasingWorkflowTests`, `PurchasingAuthorizationTests`, `AdjustStockTests`, `TransferUseCaseTests`, `CommissionConfigurationUseCaseTests` y `ReplenishmentAuthorizationTests` ejercitan validaciones, ausencia de efectos ante rechazo, historia, concurrencia reportada por el store y ámbito Owner de reposición. No hay persistencia real en este grupo.
- SC-09/10/11/12: `ExpirationJobTests`, `OperationalAccessTests`, `EmployeeInvitationApplicationTests`, `InitialOwnerOnboardingTests`, `OfflineSaleTests` y `SupplierUblStructureTests` ejercitan job, negativas por contexto, invitación y reclamo, reconciliación contable offline y análisis de XML sintético. El backoffice, RLS e integración real con proveedor permanecen fuera de esa evidencia.

### Secuencia pendiente

1. En runner autorizado con Docker local, confirmar el contenedor propio y el rol/versión por consulta de prueba, sin mostrar connection strings. Ejecutar desde `backend/` el piloto del fixture: `dotnet test tests/MediPOS.IntegrationTests --filter "FullyQualifiedName~CashClosePersistenceTests|FullyQualifiedName~CashSessionPersistenceTests|FullyQualifiedName~CashCloseIsolationTests|FullyQualifiedName~CashTransferPersistenceTests|FullyQualifiedName~CashTransferConcurrencyTests|FullyQualifiedName~CashTransferRecoverabilityTests" --logger "trx;LogFileName=rfa2-postgres-cash.trx" --results-directory "$result"`.
2. En el mismo destino efímero, caracterizar el hueco 100→10 exacto en copia temporal. Conservar ID sintético y contrastar acuse, caja y auditoría en DB, lista activa, GET por ID, alcance y ausencia de duplicados. Probar aparte que GET open informa esperado null y que despacho válido calcula saldo desde el ledger.
3. En E4, levantar exclusivamente app y perfil sintético aislados. Completar reentrada con URL original y limpia, probar Sencillo con la respuesta de la API real y contar GET de F-005 en página con varios IDs y cambio de scope. Después ejecutar los lotes PostgreSQL restantes de §4. La aceptación de pruebas externas requiere evidencia nueva.

