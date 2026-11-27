# MediPOS — RFA4B · Simplificación y refactorización delimitada

Versión 1.0 · GO del operador para este encargo · 27/11/2026.
Rama `ci/postgres-diagnostics-8bc6b4c`, HEAD publicado `f5dbea241e8e73d9b2e4cf9356c5db412b096bf2`. Agente GPT-6.2 Sol, High, Standard. Una sesión; sin subagentes ni instalaciones. Aplicar ub-mode MIT ya leído en RUN-20 (`3da62bf3fcfd2034de41f30d920d2739e566171f`), TDD, DRY/KISS/SOLID. AGENTS y SPEC/ARCHITECTURE/ADR mandan. No repetir auditoría ni reabrir RFA1–3.

## 1. Evidencia verificada y cierre de alcance RFA4A

Importar RUN-27 en `docs/audit/rfa/VALIDATION.md` y conciliar fichas, matriz, hallazgos, roadmap y checkpoint sin reescribir historia. CI [46061727689](https://github.com/saintdeyb321/MediPOS/actions/runs/46061727689), intento 1, job 140717347226, 20:09:58–20:13:26 UTC (208 s), SHA exacto arriba, build `bf5bf4ff882cc0d02ef6fb973652d5d88f30b86b55e4738dbbad6770f7c4e30d`, PostgreSQL 18.7 (180007), runtime `medipos_test_runtime`. Preparación/compilaciones PASS; workflow FAIL, exit 1. Mismo build productivo que RUN-25; candidato solo modificó tests, filtro y docs.

```text
B02 P0 PASS
B02 P1 PASS
B02 P2 PASS
B02 P3 PASS CASH_HISTORY_AND_RECOVERY
B02 P4 PASS POS_PAY_TICKET_VOID
B02 P5 DIAG SEED PASS
B02 P5 DIAG SEARCH PASS
B02 P5 DIAG CREATE START
B02 P5 CREATE READ BEFORE PRODUCTS 0 AUDIT 0 OWNER 0 LOTS 0 MOVES 0 MATCH UNKNOWN
B02 P5 CREATE CLICK DONE
B02 P5 CREATE UPSTREAM 201
B02 P5 CREATE ID VALID
B02 P5 CREATE ABORT DONE
B02 P5 CREATE GUARD PENDING
B02 P5 CREATE READ AFTER_RESPONSE PRODUCTS 1 AUDIT 1 OWNER 1 LOTS 0 MOVES 0 MATCH YES
B02 P5 CREATE POST 1 INTERCEPT 1
B02 P5 DIAG CREATE PASS
B02 P5 DIAG EDIT START
B02 P5 DIAG EDIT FAIL UNITS
```

- F-008 alta local y CAT-21 recuperación original: acreditados E4 para el recorrido ejecutado. F-007 conserva negativa de ficha ajena E2, sin atribuir E4 a variantes no ejercitadas ni a pérdida de sessionStorage. No afirmar causa exacta del FAIL de conciliación anterior. F-008 asociación global sigue NOT_RUN en RUN-27.
- EDIT alcanzó UNITS: pasos anteriores de corrección de datos, revisión obsoleta 409 y PUT de precios superados según el orden del arnés; no acreditar persistencia final de precios ni E4 global. `CashPilotTests.Catalog.cs:323–336` comprende GET unit-configuration, lectura de versión, PUT con base 1 y presentación 10.125 y PUT obsoleto esperado 409. No hay aserción/status exacto ni evidencia de corrupción. Registrar frontera CAT-13/14 (nuevo hallazgo si procede, sin inventar causa). LEGACY_UNITS, STATUS, ASSOCIATION, IMPORT y PERSISTENCE final NOT_RUN en este run. No repetir CI por este único marcador ahora; trasladarlo a RFA5A y mantenerlo como bloqueo de release.
- RFA4A: `CIERRE_CON_PENDIENTES` del encargo prioritario, no certificación de todos los invariantes. P5 FAIL y frontend integral histórico 645/646, E5 pendiente, producción NO-GO. 156 UC, nueve familias; baseline 2 E1 / 24 E2 / 102 E3 / 28 E4, 19 PASS / 135 BLOCKED / 1 NOT_RUN / 1 N/A. Actualizar solo lo que acreditan las aserciones; BLOCKED no equivale a defecto. Nunca trasladar un PASS entre SHA sin indicar su alcance histórico.

## 2. RFA4B — trabajo concreto, límites y decisiones

Objetivo: reducir complejidad de interacción y duplicación demostrada, sin cambiar contratos ni las reglas comerciales. Leer `docs/audit/rfa/README.md`, TO_BE, ROADMAP, fichas pertinentes y tests referenciados; no escanear los 156 casos de nuevo. Resolver decisiones pequeñas autónomamente, sin preguntar al operador por el flujo. Limitar trabajo productivo a los siguientes candidatos, en orden; no forzar cambios si KEEP es más seguro.

A. Catálogo (CAT-09/10/21): `ProductsPage.tsx` reutiliza el panel `recovery` en página, modal de alta y modal de edición; RUN-26 demuestra copias ocultas montadas y ambigüedad del selector. Caracterizar y, si es seguro, simplificar exclusivamente el render del panel de conciliación para que exista una sola instancia en la superficie activa, manteniendo estado/identidad en el propietario actual. Mantener montaje de formularios, foco, accesibilidad, error, bloqueo, salida conservando intención y reentrada. No cambiar `useCatalogWrite`, `CreateProductForm` corregido ni `Modal`/`Dialog` compartidos salvo causa demostrada imprescindible y dentro del alcance; preferir ajuste local. Adaptar prueba RUN-26 al comportamiento deseado sin debilitar guardas ni perder la caracterización histórica de duplicación. Si el cambio crea complejidad/riesgo o no aporta valor, KEEP documentado, sin reabrir F-008.

B. POS (fichas de edición y persistencia de borrador): contrastar `useSaleDraft` y sus tests existentes con TO_BE. Confirmar que salir de borrador persistido sin edición no dispara aviso; edición real (incluido vaciar todas las líneas) conserva aviso; petición incierta conserva identidad y bloqueo. Si existe duplicación o fricción reproducible, cambio local mínimo solo en la decisión dirty/navegación de POS; no tocar confirmación, pago, ledger, permisos, API, ni el proveedor compartido de navegación. Si ya funciona, KEEP; no rehacer L-06A. No convertir una falta de E4 en defecto.

C. Mantener las otras familias y decisiones aprobadas. F-004 (historial de compras), F-005 (optimización de nombres previa medición representativa), saldo informativo de Sencillo y otras funcionalidades nuevas: DEFER por no tener decisión comercial/medición requerida; conservar sus propuestas y criterios. No ejecutar L-06C–I como nueve encargos. No introducir orquestador genérico, cambiar estilos Blue Studio/UAX, añadir rutas, índices, migraciones, dependencias ni refactorizar todo el repo. No resolver UNITS por conjetura en esta fase. Si aparece un defecto crítico probado, registrarlo como bloqueante de release y proponer corrección focalizada con evidencia para RFA5A, sin ocultarlo ni mezclarlo con refactor.

## 3. Pruebas y cierre

- RED/GREEN o caracterización equivalente por cada cambio de comportamiento, tests focalizados existentes de catálogo y/o POS solo si se toca ese flujo; si KEEP, usar evidencia previa y documentar revisión. Catálogo: cero POST al avanzar, uno al confirmar, ficha ajena/503/código erróneo no desbloquean, original revisado permite conciliar sin reenvío, un solo panel accesible, cierre/reapertura, modalidad de página y edición/alta. POS: dirty real vs guardado, vaciado, pendiente. Conservar precisión decimal, ámbito y guards. Builds/lint afectados y `git diff --check`; no repetir suite integral en RFA4B. No sleeps, retries, timeout ampliado, saltos ni relajación de asserts.
- No modificar workflow/arnés B-02 para provocar otra ejecución; no lanzar CI, no Docker/DEV ni llamadas a servicios reales. El workflow actual dispara por cambios de arnés E2E/workflow, no por el refactor frontend; reservar B-02 para RFA5A sobre SHA publicado exacto. Git exclusivamente lectura (status/diff/log/show/ls-files/rev-parse/grep); operador publica y ejecuta CI. No main, merge, deploy ni cambios de credenciales.
- Actualizar únicamente `docs/audit/rfa/` y archivos/test estrictamente necesarios del cambio local. RUN-27 importa evidencia real; RUN-28 registra decisiones KEEP/SIMPLIFY/DEFER, RED/GREEN y límites. Reconciliar IDs y estados sin duplicar fichas. Entregar `RFA4B_CANDIDATO_CIERRE` aun si no se justifica refactor productivo; listar cambios concretos o KEEP razonado y riesgos pendientes.
- Preparar una lista única de gates RFA5A por riesgo, incluyendo: UNITS (GET/PUT/stale 409, lectura independiente de versión, unidades y auditoría, sin cambio stock/histórico; distinguir aserción y causa antes de corregir); restantes P5; frontend integral 645/646 de otro SHA y test de Compras; suites backend/arquitectura/PG18, builds/lint, escenarios de dinero, stock, permisos/aislamiento, concurrencia, offline y recuperación según matriz. Reusar evidencia y ejecutar gates sobre el candidato final; no exigir E4 universal. No ejecutar RFA5A ahora. RFA5B decide GO/NO-GO con evidencias, E5, restauración, rendimiento y externos, sin aprobar producción por cierre documental.

Condición de parada: una sesión, un resultado verificable, sin nueva ronda diagnóstica automática. Si falta entorno, registrar NOT_RUN y transferir a RFA5A; no solicitar interacción adicional para este alcance. No dar por resuelto un fallo crítico ni promover un gate a PASS sin evidencia. Producción NO-GO.
