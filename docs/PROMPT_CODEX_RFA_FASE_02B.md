# MediPOS — RFA | FASE 02B · Evidencia real y cierre

Actúa como Principal Software Engineer y QA Engineer senior. Ejecuta `docs/MEDIPOS_RFA_FASE_02B_EVIDENCIA_REAL.md` bajo el MD de fase 02 y el master RFA vigentes. Solo RFA2. Baseline auditado `5d3889806e11fea23a3ba19402c22c15635a1301`.

Continúa desde `docs/audit/rfa/CHECKPOINT.md`. Conserva 155 UC, RUN-01–08, seis hallazgos y evidencia válida. No repetir la radiografía ni las suites E2 ya ejecutadas.

1. Verifica HEAD, diff y preflight. Usa prioritariamente el nuevo run de `Backend validation - PostgreSQL 18` iniciado por el operador sobre la rama actual. Contrasta SHA, versión del motor, steps, totales y resultados por clase con los métodos/aserciones existentes. Importa evidencia sanitizada en VALIDATION; no asignar PASS ni E3 por un simple CI verde. Si falta evidencia, pedir solo el resumen del run. No disparar Actions ni usar autenticación remota.
2. Prioriza F-001 Caja y F-002 Sencillo: persistencia, cálculo, auditoría, contrato de caja abierta, saldo de ledger y recuperación. Mantén FAIL comercial de Sencillo si la UI permanece bloqueada aunque backend pase. Distingue prueba análoga de reproducción exacta 100→10. Completa huecos críticos y resto de familias usando evidencia CI; solo pruebas nuevas necesarias en copia temporal con PostgreSQL efímero acreditado. Si Docker local no está disponible, no insistir ni instalarlo; documenta límites. No usar DEV.
3. E4 solo con navegador/API/DB y auth sintéticos aislados. Si no hay composición segura, deja B-02 con receta y prerequisitos. CI/Vitest no equivalen a E4. Conserva F-003–006, revisa NOT_RUN y riesgos de repetibilidad sin alterar código ni mocks rastreados.
4. Actualiza incrementalmente VALIDATION, MATRIX, FINDINGS, README, fichas afectadas y CHECKPOINT. Evidencia por UC/aserción, resultados PASS/FAIL/BLOCKED/NOT_RUN/N/A y límites reales. No promover por asociación superficial ni repetir suites verdes. Registra run URL/SHA/attempt y conteos separados de UC.

Respeta gobierno permanente: Git solo lectura; operador hace stage, commit, push y ejecución de CI. No modificar aplicación, tests rastreados, contratos, esquema, AGENTS, dependencias, CI ni DEV; sin skills nuevas, subagentes ni servicios externos. Sin reintentos ciegos. Si faltan condiciones de entorno, documenta una vez y sigue lo independiente.

Reconcilia evidencias y `git diff --check`. Declara FASE_02_COMPLETA/PARCIAL/BLOCKED según gate original, con pendientes específicos y recomendación de paso a RFA3 separada del GO producción. Detente para revisión. No iniciar RFA3 ni corregir código.

Empieza por comprobar el run CI facilitado y el checkpoint.
