# MediPOS · RFA4 · L-05B y continuidad global

Versión 1.0. Baseline publicado `e2ffd12f577234c943237e71361a8ee0716a8eb8`, rama `ci/postgres-diagnostics-8bc6b4c`. Modelo sugerido: GPT-6.2 Sol, Medio, Standard. Gobierno: AGENTS aplicables, master RFA, ROADMAP y CHECKPOINT vigentes. Este encargo autoriza exclusivamente diagnóstico focalizado del gate, ajustes de pruebas si se demuestra su defecto, y actualización documental. No autoriza las futuras capacidades de producto.

## 1. Evidencia CI suministrada por la auditoría externa

Run https://github.com/saintdeyb321/MediPOS/actions/runs/45915477001, intento 1, SHA indicado arriba. Duración 18 min 58 s (00:13:30–00:32:28 UTC del 27/11/2026; 19:13–19:32 del 26/11 en Perú). Estado completed/failure. Job backend 140233528093 success; job frontend 140233527873 failure. Backend: checkout, SDK, restore/audit, build, safe diagnostics, unitarias, arquitectura, formato, infraestructura de contenedores, PostgreSQL, publicación y limpieza PASS. Frontend: checkout/identidad, Node, install, build, lint PASS; tests y publicación del gate FAIL. Sin logs crudos ni credenciales.

```text
unit: total=2694, executed=2694, passed=2694, failed=0, skipped=0
architecture: total=8, executed=8, passed=8, failed=0, skipped=0
postgres: total=907, executed=907, passed=907, failed=0, skipped=0
Actual PostgreSQL server version: 18.7
SUITE MediPOS.IntegrationTests.Modules.Cash.CashCloseConcurrencyTests: total=5, passed=5, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashCloseIsolationTests: total=4, passed=4, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashClosePersistenceTests: total=43, passed=43, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashSessionIsolationTests: total=10, passed=10, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashSessionPersistenceTests: total=9, passed=9, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashTransferConcurrencyTests: total=8, passed=8, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashTransferIsolationTests: total=11, passed=11, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashTransferPersistenceTests: total=9, passed=9, failed=0, skipped=0
SUITE MediPOS.IntegrationTests.Modules.Cash.CashTransferRecoverabilityTests: total=3, passed=3, failed=0, skipped=0
Overall: CODE_AND_POSTGRESQL_GATE_PASSED (not production acceptance)
Vitest: total=646, PASS=645, FAIL=1, SKIP=0, failed suites=1
Vitest failure: `frontend/src/features/purchasing/PurchasesPage.test.tsx` — `rejects confirmation under the server precondition if lines changed after review, without retry`
Overall: NOT_APPROVED
```

Extracto de suites relevantes, no listado completo. Contrastar con el código y el alcance de la evidencia. Ambas clases de Caja pasaron íntegramente, incluyendo las nuevas pruebas de este SHA si se confirma inclusión en el filtro ejecutado. No certificar HTTP+PostgreSQL en un solo recorrido, E4 o incidente DEV con esta evidencia. No confundir FAIL actual de Compras con FAIL histórico sin nombre ni con F-006.

## 2. Encargo inmediato — L-05B

1. Verificar HEAD, diff, AGENTS y checkpoint. Reconciliar el CI como nuevo RUN (ID siguiente libre), sin reejecutar suites backend verdes. Actualizar VALIDATION, MATRIX, FINDINGS, ROADMAP, README y fichas afectadas solo cuando cambie evidencia o estado. Mantener histórico, SHA, fronteras y veredictos; no asignar PASS comercial por E3 parcial. Para F-001 documentar caso exacto sintético, diferencia -90, auditoría, segundo cierre rechazado y recuperación por ID. Para L-01 documentar E3 de StatusAsync y E2 HTTP con dobles; el GET HTTP con PostgreSQL y E4 siguen pendientes. No cerrar L-01 definitivamente si falta su gate.
2. Caracterizar el único FAIL identificado: `frontend/src/features/purchasing/PurchasesPage.test.tsx`, alrededor de línea 82. Leer prueba, doble, transición de revisión/revisión obsoleta, manejo de 409/precondición según contrato real y hook afectado. Reproducir prueba focalizada y archivo sin compilaciones simultáneas. Comprobar un único POST, borrador intacto, ausencia de reintento y acciones protegidas durante recuperación/revisión. No asumir que es una carrera ni cambiar un requisito. Si se demuestra defecto de sincronización de la prueba, corregir exclusivamente la prueba con estados observables y respuesta controlada, conservando todas las negativas. Si se demuestra defecto de producto, registrar evidencia/UC/causa y parche propuesto para aprobación, no modificar aplicación en este encargo. Si no reproduce, registrar NOT_REPRODUCED sin cambios especulativos. No ocultar fallos con sleeps, retries, skips, timeouts ampliados ni reducción de aserciones. Preservar los cinco fallos previos de RUN-12 como evidencia histórica.
3. Si se modifica una prueba, ejecutar prueba focalizada, archivo de pruebas afectado, lint focalizado y tipos según impacto. No ejecutar suites completas, repetir CI ni modificar workflow en este encargo. `git diff --check` y resumen de diff. El operador decidirá si combina una ejecución de CI posterior con el siguiente lote autorizado. No usar DEV ni instalar Docker, skills o dependencias.

## 3. Continuidad de cobertura global — actualizar ROADMAP, no reauditar todo

El alcance RFA sigue siendo los 155 UC (o los adicionales descubiertos justificadamente), las nueve familias, todas las rutas, roles y contratos. El TO_BE vigente clasifica cada caso, pero KEEP no significa aprobado ni impide una mejora de flujo basada en evidencia. El baseline de MATRIX conserva 19 PASS, 1 FAIL, 133 BLOCKED, 1 NOT_RUN y 1 N/A; son resultados históricos que se actualizarán con evidencia, no objetivos artificiales.

Desglosar L-06 en los siguientes cortes futuros. No implementarlos todavía. Asociar IDs exactos del inventario existente mediante referencias o rangos verificados, sin copiar fichas. L-02/03/04 se mantienen como capacidades o mejoras específicas que requieren aprobación; incorporar su validación al corte correspondiente, sin duplicarla.

| Corte futuro | Familia / casos del baseline | Recorrido funcional mínimo y criterios |
|---|---|---|
| L-06A | Caja/POS, 18 | Apertura, venta, presentación, pago mixto, ticket, anulación, cierre, historial si aprobado; borradores, salida segura, respuesta incierta, saldos y auditoría. |
| L-06B | Catálogo, 22 | Alta local/asociación global, edición/estado/precios/unidades, búsqueda, importación y recuperación; permisos y no generar stock al crear. |
| L-06C | Compras e inventario, 15 | Proveedor, recepción, revisión, revisión obsoleta, confirmación, reentrada, existencias/lotes/vencimientos y rendimiento. Una sola escritura, stock exacto. |
| L-06D | Transferencias, 13 | Sencillo, mercadería, solicitud/aprobación/despacho/recepción/cancelación conforme contrato; concurrencia, ámbito y recuperación. |
| L-06E | Reportes y comisiones, 17 | Filtros, cálculos, exportación, reversión, cambios de reglas y reposición interna según contrato; reconciliación financiera. |
| L-06F | Avisos y auditoría, 7 | Lectura personal, riesgo accionable, navegación a detalle, permisos y visibilidad de auditoría. |
| L-06G | Acceso y estructura, 27 | Onboarding, roles, membresías, sedes, horarios, cambio de contexto, revocación, último Owner y expiración. |
| L-06H | Plataforma, 18 | Tenants, invitaciones, licencias, catálogo y soporte; separación plataforma/tenant y recuperación. Purga solo sintética aislada si autorizada. |
| L-06I | Contingencia e integraciones, 18 | Offline, outbox, conflictos, reentrada, cambio de identidad, sincronización, integraciones simuladas y límites fiscales/externos. |

Para cada corte futuro: actor → tarea → pasos y estados AS-IS → expectativa/requisito → evidencia → fricción/defecto → decisión KEEP/FIX/SIMPLIFY/BRECHA/VALIDATE → prueba de aceptación. Observar tareas normales y variantes de riesgo: error, espera, pérdida de respuesta, regreso, recuperación y permisos; medir clics o solicitudes solo cuando sirva a una decisión. No inventar CRUD ni funcionalidad. Aplicar Blue Studio/UAX, DRY/KISS/SOLID y TDD; preservar invariantes y evitar abstracciones ceremoniales. Revisar también KEEP y registrar por qué se conserva. No exigir 155 pruebas independientes: un escenario transversal puede cubrir varios UC con aserciones explícitas. Si un caso no aplica o está bloqueado, justificarlo; no dar por cerrado el módulo por un test verde o por haber cambiado de apariencia.

## 4. Infraestructura y gates futuros

B-02 requiere un entorno común aislado con navegador, API, PostgreSQL y autenticación sintética. Proponer en ROADMAP un único encargo habilitador reutilizable, con preflight, aislamiento verificable, permisos mínimos, sin OAuth real, sin DEV, sin CPE, sin purga real, cleanup y SHA/build identificado. No construirlo en L-05B ni cambiar CI sin nueva aprobación. Reutilizarlo por familias, no diseñar uno por módulo. B-03 requiere decisiones separadas para integraciones externas. En L-07/RFA5 reconciliar 155/155 UC y casos nuevos con decisión, implementación si aprobada, evidencia acorde a riesgo y pendientes explícitos. E4 para recorridos críticos reales; E5 para aceptación del operador. Producción NO-GO con gates requeridos fallidos, P0/P1 de release abiertos o datos no reconciliados.

## 5. Entrega y límites

Checkpoint único incremental; no repetir RFA1–3 ni reescribir TO_BE. Resumen breve: resultados CI y nuevas fronteras, fallo Compras (causa demostrada/hipótesis/no reproducido), diff y pruebas, cobertura histórica vs nueva, plan L-06A–I y próximo paso. Mantener todos los casos y los hallazgos; asignar nuevo ID solo si corresponde. No modificar código de aplicación, contratos, esquema, RLS, CI, AGENTS, dependencias, configuración o DEV. Solo pruebas autorizadas y documentación. Git solo lectura; operador hace stage, commit, push, Actions y despliegue. Sin subagentes ni nuevas skills. Detenerse al terminar L-05B; no iniciar L-02, L-06 ni RFA5.
