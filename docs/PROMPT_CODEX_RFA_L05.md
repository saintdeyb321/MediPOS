# MediPOS — RFA4 | L-05 + evidencia L-01 y F-001

Ejecuta `docs/MEDIPOS_RFA_L05_GATE_Y_CONTRATOS.md` v1.0 bajo AGENTS y master RFA. Baseline `bc36ecc5b7b9e2e0cd4c52d6c8b9f00ab7790b76`. Continúa desde CHECKPOINT; no repetir auditoría ni L-01.

1. Corrige únicamente la sincronización demostrada de los dos casos F-006 (Transferencias y Plataforma), preservando autorización, estados observables, conteos y negativas. No sleeps, retries, skips ni timeouts para ocultar fallos. No atribuir el fallo CI histórico sin evidencia.
2. Autorizo la excepción mínima de CI descrita en §4: publicar identificación segura y acotada de pruebas Vitest fallidas a partir del JSON existente, sin cambiar criterios del gate ni exponer logs/secretos. Verifica con entradas sintéticas positivas, negativas y hostiles.
3. Completa la aserción HTTP de caja abierta con `expectedCashAmount: null` en el host existente; conserva prueba PostgreSQL L-01. Prepara la caracterización real 100→10 de F-001 en el fixture existente, sin implementar historial. Si falta Docker, compila y deja NOT_RUN para CI; no instalar ni insistir. No confundir HTTP con dobles, PostgreSQL y E4.
4. Ejecuta solo pruebas afectadas, lint/tipos y compilación necesaria. Actualiza evidencia y checkpoint sin duplicar documentos. `git diff --check`. Detente para revisión; no ejecutar CI ni declarar cerrados los gates pendientes.

Sin modificaciones de producto, contratos, esquema, AGENTS, dependencias o DEV; solo tests, diagnóstico CI delimitado y docs autorizados. Git de solo lectura; yo hago stage, commit, push y Actions. Sin skills nuevas ni subagentes. Entrega resultado compacto y próximo paso exacto. Implementa, no generes otro plan.
