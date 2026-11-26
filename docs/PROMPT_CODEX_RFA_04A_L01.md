# MediPOS — RFA4A | L-01 · Corregir Sencillo

Ejecuta `docs/MEDIPOS_RFA_FASE_04A_L01_SENCILLO.md` v1.0 bajo AGENTS y master RFA. Baseline `50b97a2c451a15ac59a21d06c11d266b81a77a68`. RFA3 revisada. Autorizo exclusivamente L-01 alternativa A, incluyendo la vía de candidato provisional condicionada al contrato real del MD.

Continúa desde CHECKPOINT. Verifica HEAD/diff. Lee solo L-01, F-002, SC-02/RUN-10, guardas, consumidores y pruebas afectados. No repitas auditoría.

1. RED: caracteriza despacho válido con caja open y expectedCashAmount null. Comprueba GET real con fixture PostgreSQL aislado si viable. Si Docker/HTTP aislado falta, prepara prueba de contrato rastreada cuando sea posible y sigue la vía provisional del MD; no uses DEV ni inventes E3. Si GET real contradice hipótesis, detente.
2. GREEN: quita dependencia del esperado de cierre en la guarda de despacho, conserva validación exacta, autorización, scope, estado, destino, bloqueos e intención original. El servidor decide saldo bajo bloqueo. Ajusta mensajes en CashTransfersPage/CashSelection y mock discordante. Importe superior al saldo se rechaza por servidor, no por apertura ni esperado de cierre. Conserva recuperación y recepción.
3. Ejecuta RED/GREEN y regresiones focalizadas: válido, inválido, fondos insuficientes, scope/rol/caja cerrada, doble clic, pérdida de acuse y recepción. No relajes aserciones. Sin suites repetidas ni otra infraestructura; reutiliza CI histórico y deja gate del candidato para el operador. No tocar F-006/L-05 ni otros lotes.
4. Actualiza solo evidencias/UC/F-002/ROADMAP/CHECKPOINT afectados. Entrega cambios, comandos, resultados, bloqueos y estado L-01 según MD; `git diff --check`. No declarar PASS comercial/E4/producción sin evidencia. Detente para revisión.

Sin nuevas skills, subagentes ni exploraciones masivas. Backend productivo, contratos, esquema, CI, AGENTS, dependencias y DEV intactos. Git de solo lectura; yo hago stage/commit/push y CI. No reiniciar ni publicar. Implementa el candidato local, no otro informe de recomendaciones.
