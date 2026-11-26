# MediPOS — RFA | Cierre 02B + FASE 03 · Diagnóstico y TO-BE

Actúa como Principal Software Engineer y arquitecto de dominio/producto para sistemas transaccionales .NET, PostgreSQL, React y PWA.

Ejecuta `docs/MEDIPOS_RFA_FASE_03_DIAGNOSTICO_TO_BE.md` bajo el gobierno del master RFA y AGENTS. Autoriza únicamente reconciliación pendiente RFA2 y diagnóstico RFA3 en la misma sesión, no RFA4/RFA5.

Baseline `5d3889806e11fea23a3ba19402c22c15635a1301`, rama `ci/postgres-diagnostics-8bc6b4c`.

## MISIÓN
Recuperar el máximo valor de la evidencia ya obtenida y entregar una radiografía AS-IS → TO-BE comprensible y un backlog ejecutable para corregir MediPOS sin reescribirlo ni comprometer dinero, stock o aislamiento.

## EJECUCIÓN
1. Continúa desde `docs/audit/rfa/CHECKPOINT.md`. Verifica HEAD/diff, preserva 155 UC y RUN-01–08. Lee la evidencia suministrada `docs/audit/rfa/evidence/CI_RUN_45891472332_SANITIZED.md` (attempt 1, SHA exacto). Backend PASS: 2694 unitarias, 8 arquitectura, 905 integraciones/99 clases, PostgreSQL 18.7. Frontend FAIL: 641/642; no está identificado el test fallido. No volver a ejecutar suites verdes ni consultar logs crudos.
2. Reconcilia 02B por UC y aserciones, no por asociación de suite. Promueve E3 solo donde corresponda. Conserva FAIL de Sencillo E2 y límites de Caja, historial y recuperación. Distingue caso exacto 100→10 de pruebas análogas. No inventar E4 ni atribuir el fallo CI a F-006. Registra pendientes y gate RFA2 independiente; guarda checkpoint.
3. Continúa RFA3 sin otro prompt. Revisa los seis hallazgos, identifica causas y riesgos demostrables, y diseña los recorridos TO-BE de las nueve familias según el MD. Pasos del usuario primero, decisiones, estados, recuperación y efectos después; separar corrección, refactor, nuevas capacidades y decisiones comerciales. Conserva lo correcto. No imponer cambios de contrato ni arquitectura ceremonial.
4. Genera `TO_BE.md` y `ROADMAP.md` dentro de `docs/audit/rfa/` con lotes priorizados, dependencias, alcance, pruebas de aceptación, impactos, decisiones del operador y primer lote recomendado. Asocia todos los UC a conservar/mejorar/brecha/pendiente mediante agrupación y referencias, sin duplicar 155 fichas. Actualiza matriz, hallazgos, validación, índice y checkpoint incrementalmente.

## EFICIENCIA Y LÍMITES
Reutiliza evidencia e inventarios; lectura focalizada por causa, sin subagentes ni skills nuevas, sin explorar todo el repositorio otra vez. No repetir tests, build/lint ni intentar desbloquear Docker o crear harness E2E. Solo reproducciones focalizadas en aislamiento acreditado si aportan evidencia esencial y son viables. Si falta entorno, documenta y avanza en propuestas independientes. No asumir cuota visible. Mantén salidas breves.

Solo diagnóstico/documentos. No modificar código, tests rastreados, contratos, esquema, CI, dependencias, AGENTS, configuración o DEV. Sin transacciones reales ni servicios externos. Git de solo lectura; el operador hace stage, commit, push y publicación. No ejecutar Actions ni eludir autenticación.

## ENTREGA
Gates RFA2 y RFA3 separados, conteos reales E/veredicto, riesgos/bloqueos, decisiones que requieren aprobación, backlog y próximo lote exacto. `git diff --check`. No declarar producción lista ni iniciar RFA4. Detente para revisión del operador.

Empieza por reconciliar la evidencia CI y continúa con el diagnóstico.
