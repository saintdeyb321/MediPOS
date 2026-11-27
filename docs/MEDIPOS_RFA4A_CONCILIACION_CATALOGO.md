# MediPOS — RFA4A · Conciliación de alta CAT-21 · v1.0

## 1. Mandato y evidencia de entrada
HEAD publicado `55de01125954154f0a67ad61ff5ca812e760f14b`, rama `ci/postgres-diagnostics-8bc6b4c`. El operador autoriza únicamente esta continuación de RFA4A, no una fase nueva. Leer AGENTS, master RFA, CHECKPOINT, F-007/F-008, RUN-23/24 y ficha CAT-09/10/21. No reconstruir la radiografía. Git solo lectura; operador stage/commit/push/CI.

RUN-25: [B-02 46042456650](https://github.com/saintdeyb321/MediPOS/actions/runs/46042456650), job 140653339521, intento 1, 27/11/2026 17:31:04–17:34:29 UTC (205 s). Builds y Chromium PASS; conclusión failure, exit 1. Extracto verificado por GitHub:

```text
B02 SHA 55de01125954154f0a67ad61ff5ca812e760f14b BUILD bf5bf4ff882cc0d02ef6fb973652d5d88f30b86b55e4738dbbad6770f7c4e30d
B02 P0 START
B02 POSTGRES 180007 ORIGIN https://localhost:34711 ROLE medipos_test_runtime
B02 P0 PASS
B02 P1 START
B02 P1 PASS
B02 P2 START
B02 P2 PASS
B02 P3 START
B02 P3 PASS CASH_HISTORY_AND_RECOVERY
B02 P4 START
B02 P4 PASS POS_PAY_TICKET_VOID
B02 P5 START
B02 P5 DIAG SEED START
B02 P5 DIAG SEED PASS
B02 P5 DIAG SEARCH START
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
B02 P5 DIAG CREATE FAIL RECONCILIATION
B-02 failed or was incomplete. Unreported phases are unverified.
```

F-008 (alta involuntaria local) corregido para recorrido real: REVIEW incluyó cero POST/intercepciones y ausencia de intención; CLICK, 201, aborto, guarda y REENTRY alcanzados. Una fila y auditoría del mismo ID/Owner, cero stock. CAT-09 tiene evidencia real de esta corrección. CAT-21 conserva bloqueo/reentrada acreditados, pero conciliación final no aprobada. No acreditar P5 global, CAT-10 asociación ni EDIT/IMPORT/PERSISTENCE final. Conservar limitaciones de sesión, roles, CAT-19 writer drain y E5; no equiparar recuperación de pestaña a idempotencia durable. No cambiar veredictos completos sin cobertura de sus variantes. Registrar RUN-25 primero. El gate frontend histórico 645/646 sigue abierto.

## 2. Alcance único: conciliación final
`backend/tests/MediPOS.E2E/CashPilotTests.Catalog.cs:271–283` contiene una sola frontera que incluye espera de GET, lectura de respuesta, ID/código, escritura del código de revisión, checkbox, confirmación, desbloqueo, borrado del marcador y conteos. La aserción individual no es conocida. No hay evidencia de defecto del backend. Leer `ProductsPage.tsx:73–88`, `useCatalogWrite.ts:37–52,90–105`, `api.ts` y test de recuperación `ProductsPage.test.tsx:256–280` únicamente si es necesario. El detalle y la conciliación pueden emitir GET de la misma URL (useQuery y refresh explícito); comprobar si la espera del arnés toma una respuesta anterior/en vuelo, si el DTO real difiere del doble o si la guarda/UI impide finalizar. Son hipótesis, no hechos. Conservar el contrato y la corrección de CreateProductForm.

## 3. Ejecutar, no otro plan
- Reutilizar ub-mode MIT fijado en RUN-20, sin reinstalar ni auxiliares. Ejecutar solo caracterización focalizada de CAT-21 con dobles fieles a DTO real, incluyendo selección de ficha ajena y original, GET pendiente/error, revisión de atributos, código original, confirmación, limpieza y una sola escritura. Si Chromium local ya está disponible, se permite una reproducción local aislada con transporte sintético sin Docker ni DEV; no instalar ni buscar otra infraestructura. Distinguir E2 de E4.
- RED/GREEN si se demuestra causa de producto o arnés. Si el problema es sincronización, esperar estado observable real (detalle revisado y consulta explícita identificable, formulario habilitado); no sustituir el GET real por doble, ni quitar verificaciones de 200/ID/código. No atribuir causa solo porque una prueba local verde exista. No modificar guardas comerciales ni permitir conciliación de ficha ajena. Mantener almacenamiento del original hasta verificación y nunca reenviar POST. Conservar F-008 y regresiones de asociación/medicamento.
- Si no hay reproducción local, no cambiar producto por hipótesis. Añadir únicamente etiquetas estáticas para las aserciones de RECONCILIATION (consulta iniciada/GET observado/HTTP/ID-código/estado observado/checkbox/confirmación/desbloqueo/storage/conteos) y su allowlist estricta; conservar primera excepción, no publicar payload ni IDs/códigos de usuario. Aprovechar probe y lectura independiente existentes. Si hay corrección demostrada, incorporar las etiquetas en el mismo candidato si son necesarias para no perder evidencia de CI. No repetir instrumentación genérica.
- Gate: P0–P5 intactos, REVIEW cero POST; una creación 201, aborto y bloqueo; reentrada, GET del mismo ID/código, revisión deliberada y desbloqueo solo tras conciliación, storage limpio, una escritura, una auditoría, cero stock. Mantener resto de P5 y aserciones. Builds/lint/tests únicamente afectados, YAML si cambia y diff --check. Docker ausente => NOT_RUN, no insistir. El operador publica una vez para B-02, no auto-retries.

## 4. Cierre y límites
Actualizar solo documentación afectada de `docs/audit/rfa/` (RUN-25/26, MATRIX, FINDINGS, CHECKPOINT, ficha, README/ROADMAP según proceda). Conservar IDs, SHA histórico, 156 UC y evidencia por frontera; F-008 corregido en alta local, F-007 no cerrado globalmente. Si P5 pasa, candidato a cierre RFA4A; si falla, identificar primera frontera precisa y detenerse. No abrir otra ronda de instrumentación sin decisión del operador; las etapas posteriores no alcanzadas no se acreditan. No iniciar RFA4B ni L-06C–I. Producción NO-GO.

Prohibido cambiar backend/dominio/contratos/RLS/esquema/dependencias/AGENTS, backend-validation, componentes compartidos u otros flujos sin causa demostrada y autorización separada. No ampliar timeouts, sleeps, reintentos, relajar pruebas, exponer logs crudos, DEV, main o deploy. No subagentes ni nuevas skills. Entregar `RFA4A_CONCILIACION_CANDIDATA` o bloqueo exacto; resumen compacto del cambio, pruebas y acción única del operador. No pedir GO ya incluido ni repetir evidencias.
