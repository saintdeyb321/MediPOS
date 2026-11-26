# MediPOS — RFA | FASE 02 · Validación conductual y persistencia

Actúa como Principal Software Engineer y QA Engineer senior especializado en sistemas transaccionales .NET, PostgreSQL, React y PWA.

Ejecuta `docs/MEDIPOS_RFA_FASE_02_VALIDACION.md` v1.0 bajo `docs/MEDIPOS_FUNCTIONAL_ARCHITECTURE_AUDIT.md` v1.1. Solo RFA2.

Baseline auditado: `2c54e1f71aa6f66a67190d37eacda6ada71f4b0f`, rama `ci/postgres-diagnostics-8bc6b4c`. Fase 1 aprobada: 155 UC E1/NOT_RUN, nueve familias y cinco hallazgos. Continúa desde `docs/audit/rfa/CHECKPOINT.md`. No repetir la radiografía.

## MISIÓN

Demostrar qué operaciones funcionan, fallan o no pueden verificarse mediante evidencia de comportamiento, no solo lectura de código ni tests verdes con mocks. Validar persistencia, recuperación, permisos, dinero, stock e integridad. Actualizar la radiografía acumulativa, sin corregir aún el producto.

## EJECUCIÓN

1. Verifica HEAD/diff, AGENTS, baseline y preflight de aislamiento del MD. Aprovecha Testcontainers PostgreSQL 18 y suites .NET/Vitest existentes. No usar DEV ni producción. Si no hay destino aislado acreditado, no ejecutar escrituras; registra bloqueo y sigue lo independiente.
2. Primero F-001: apertura 100, cierre 10 sin movimientos, diferencia −90; contrasta acuse, fila persistida, auditoría, activas, consulta por ID y recuperación tras reentrada. Separa historial/UX de pérdida real; no atribuyas el incidente DEV a una causa sin evidencia.
3. Después F-002: caja abierta válida, expectedCashAmount real, bloqueo UI y mock discordante; contrasta backend de despacho/recepción con respuesta real y escenario UI fiel. No modificar guardas ni mocks existentes. Continúa POS y resto de familias según §4 del MD.
4. Agrupa escenarios por dependencias/riesgo. Reutiliza tests y aserciones existentes; nuevas reproducciones solo en espacio temporal aislado si hay hueco esencial. Comprueba caso feliz y negativas pertinentes, persistencia, recarga, idempotencia, alcance y concurrencia según contrato. Distingue E2, E3 y E4, runner vs veredicto comercial, fallo del producto vs bloqueo del entorno. No inventar PASS.
5. Completa F-003–005 y cualquier hallazgo nuevo, referencias de pruebas/requisitos pertinentes y clasificación de los 155 UC; no asignar evidencia a casos no ejercitados. Registra evidencia y pendientes por escenario en `VALIDATION.md` y actualiza matriz, hallazgos, fichas y checkpoint existentes.

## EFICIENCIA Y CONTINUIDAD

Usa el inventario de RFA1. Ejecuta piloto, después agrupa pruebas no repetidas por proyecto/fixture; no una suite por caso. Consulta solo fuentes necesarias. No instalar skills/herramientas redundantes, no subagentes por defecto, no volcar logs completos ni rehacer documentación. No reintentar sin hipótesis. Guarda checkpoint tras preflight, Caja, Sencillo y cada lote, antes de compactar. Si hay bloqueo común, vincula casos afectados y continúa lo independiente. No asumir acceso a mi cuota. Prioriza integridad y evidencia real sobre cantidad de pruebas.

## LÍMITES Y GATE

Gobierno permanente del master y aislamiento del MD. No modificar aplicación, tests rastreados, API, esquema, dependencias, AGENTS, CI, DEV ni configuración. No operaciones reales, reinicios, purga real, CPE ni servicios externos. Git solo lectura: stage/commit/push/publicación exclusivamente operador; no intentar autenticación ni alternativas. Solo documentos RFA y reproducciones temporales aisladas autorizadas.

Entrega conteos reales UC por evidencia/veredicto, escenarios y tests (PASS/FAIL/SKIP), F-001–005, nuevos riesgos, entorno, bloqueos, archivos y diff. Actualiza CHECKPOINT, clasifica FASE_02_COMPLETA/PARCIAL/BLOCKED según el MD. No declarar producción lista ni iniciar RFA3 o correcciones. Detente para revisión del operador.

Empieza por el preflight y la receta de Caja.
