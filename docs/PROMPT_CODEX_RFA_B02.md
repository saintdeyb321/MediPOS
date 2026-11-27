# MediPOS — RFA4 | B-02 · Habilitador E2E común

Ejecuta `docs/MEDIPOS_RFA_B02_HABILITADOR_E2E.md` v1.0 bajo AGENTS y master RFA. Baseline `781277c0032e8d4f364893191137254b3e024c2e`.

Continúa desde CHECKPOINT. L-05B aprobado; aserción histórica de Compras no disponible, no modificar ni repetir investigación. Si recibes resultado de reejecución frontend, concílialo sin esperar ni repetir suites.

1. Implementa un arnés de pruebas reutilizable para las nueve familias, con frontend real, API real, PostgreSQL 18 efímero, identidad sintética y aislamiento fail-closed. Reutiliza composición/fixtures existentes; no duplicar arquitectura. Sigue las autorizaciones delimitadas del MD para navegador y runner manual separado si hacen falta. No tocar backend-validation.
2. Piloto P0→P3: preflight, GET real de caja abierta con JSON null (L-01), despacho/recepción de Sencillo y Caja 100→10 con recuperación. Verifica efectos y negativas, no solo pantallas. Si falta Docker/navegador local, prepara candidato para runner manual y deja NOT_RUN; no insistir ni usar DEV. Si el contrato contradice lo esperado, conserva evidencia y detén aceptación afectada.
3. Ejecuta solo comprobaciones necesarias, registra evidencias por UC, actualiza documentos RFA afectados y checkpoint. `git diff --check`. Entrega estado B-02, gates separados, instrucciones exactas de ejecución y bloqueos. Detente antes de L-06A–I.

Sin nueva auditoría, skills, subagentes ni suites repetidas. Solo infraestructura de pruebas autorizada; sin cambios productivos, contratos, esquema, RLS, AGENTS ni DEV. Git solo lectura; stage/commit/push/Actions exclusivamente operador. Sin secretos, OAuth/CPE reales ni despliegues. Implementa el arnés, no otro plan.
