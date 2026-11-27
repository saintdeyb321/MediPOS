# MediPOS — RFA4A · F-008: envío prematuro de alta de producto · v1.0

## 1. GO, baseline y evidencia real
Continuación de RFA4A, no fase nueva. HEAD `2248381ffcd808533f2f8204ff8aeece4ce028cd`, rama `ci/postgres-diagnostics-8bc6b4c`. No repetir RFA2/RFA3 ni instrumentar otra ronda genérica. Leer AGENTS aplicables, master RFA, RUN-21/22, F-007/008, CAT-09/10/21 y código afectado. Aplicar ub-mode MIT ya leído en RUN-20 (SHA `3da62bf3fcfd2034de41f30d920d2739e566171f`), sin reinstalar.

CI [46019645382](https://github.com/saintdeyb321/MediPOS/actions/runs/46019645382), job 140575642581, intento 1, push 27/11/2026 14:51:17–14:54:33 UTC (196 s), conclusión failure, exit 1. Checkout, SDK/Node, build React, restore/build E2E y Chromium PASS. Extracto real (repeticiones de presentación del log omitidas):

```text
B02 SHA 2248381ffcd808533f2f8204ff8aeece4ce028cd BUILD e40191caae1b9b7295a27b34b2b3c9489d4a61cc1484178ae3d73c17d0985203
B02 P0 START
B02 POSTGRES 180007 ORIGIN https://localhost:36723 ROLE medipos_test_runtime
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
B02 P5 CREATE UPSTREAM 201
B02 P5 CREATE ID VALID
B02 P5 CREATE ABORT DONE
B02 P5 CREATE READ AFTER_RESPONSE PRODUCTS 1 AUDIT 1 OWNER 1 LOTS 0 MOVES 0 MATCH YES
B02 P5 CREATE POST 1 INTERCEPT 1
B02 P5 DIAG CREATE FAIL REVIEW
B-02 failed or was incomplete. Unreported phases are unverified.
```

## 2. Diagnóstico y decisión
La traza REVIEW se fija después del último Continuar y antes de `trace.Step("CLICK")` / `save.ClickAsync()` en `CashPilotTests.Catalog.cs:227–255`. No se alcanzó el clic explícito de guardado. La única solicitud observada recibió 201, ID válido, aborto correcto; lectura runtime posterior confirma fila e ID, auditoría del Owner y cero stock. Es evidencia de alta prematura en el recorrido real, no de duplicación ni de fallo de transacción. No se conoce la aserción individual de REVIEW. REENTRY, RECONCILIATION y etapas posteriores no acreditadas. No afirmar que F-007 está resuelto E4.

Hipótesis principal, comprobable: `CreateProductForm.tsx:32–38,74–77` renderiza alternativamente dos `Button` en la misma posición, sin identidad distinta; `Button.tsx:9–13` aplica `type="button"` por defecto y permite override `type="submit"`. Al avanzar a revisión React puede reutilizar el nodo y cambiar su tipo durante el evento de activación, provocando submit nativo del mismo clic; `onSubmit` ya ve `step === reviewStep`. Los tests DOM usan `fireEvent` y pueden no reproducir el orden real del navegador. No dar por demostrada esta mecánica hasta caracterizarla. Asociación global comparte el formulario: CAT-10 es regresión acotada, no nueva feature.

## 3. Implementación autorizada (en una sesión)
- Reproducir con interacción real o caracterización mínima del contrato DOM/activación y mostrar RED por causa esperada. Aprovechar RED real de RUN-23 para el comportamiento extremo a extremo; si Chromium local no está disponible, no instalar ni insistir. Usar tests existentes y una comprobación estructural de identidad del botón y cancelación de acción por defecto; distinguir E2 de E4. No reclamar reproducción nativa con jsdom.
- Corregir localmente la transición de `CreateProductForm`: botón de avance explícito `type="button"`, cancelar acción por defecto antes del cambio de paso y dar identidad distinta y estable a avance vs submit (keys distintos o separación equivalente). Mantener navegación anterior, revisión de medicamento y validación del formulario. Ajustar solo lo mínimo demostrado por la caracterización. Guardar/Asociar deliberados por clic o teclado accesible deben seguir funcionando. No impedir globalmente Enter ni convertir toda confirmación en evento de ratón. No cambiar `Button` compartido si la causa es el consumo local.
- Conservar `useCatalogWrite`, CSRF, contrato POST, transacción, RLS, precios exactos, unicidad, código original de sesión y conciliación sin reenvío. Solo si nueva evidencia demuestra otra causa dentro de esta frontera, corregir el símbolo responsable con RED/GREEN. Nada de modificar reglas financieras, stock, esquema o endpoints.
- Pruebas focalizadas `ProductsPage.test.tsx`, `CatalogDirtyState.test.tsx` y solo otras directamente afectadas. Local y global: avanzar y retroceder no escriben ni crean intención; revisión visible/accionable y ningún POST antes de la confirmación; confirmación explícita una escritura; medicamento exige revisión documental; pérdida del acuse conserva original y otra ficha no desbloquea. Si se puede, cubrir activación por teclado con herramientas ya disponibles. No sustituir aserciones por mocks que oculten el defecto.
- Preservar `CreatePostProbe`, la prueba independiente y P0–P5. En P5 mantener cero POST preconfirmación y añadir comprobación equivalente en asociación global (observación desde antes del formulario, cero POST hasta confirmación, una después), sin interceptar/modificar el contrato de asociación. Usar el fixture actual. No relajar aserciones ni timeouts, no retries ni sleeps. Mantener trazas saneadas existentes; no otra ronda de instrumentación salvo aserción focalizada necesaria para la regresión. Compilar E2E y frontend, tipos/lint focalizados, `git diff --check`.

## 4. Gate de salida y continuidad
Registrar RUN-23 con evidencia CI anterior, RUN-24 local con pruebas reales ejecutadas, actualizar F-008 y fichas CAT-09/10/21, matriz/README/roadmap/checkpoint sin borrar historial. 156 UC; baseline 2 E1/25 E2/103 E3/26 E4, 19 PASS/135 BLOCKED/1 NOT_RUN/1 N/A. No subir E4 recuperación sin verificarla. Entregar RFA4A_CORRECCION_CANDIDATA con causa demostrada o hipótesis delimitada y cambios mínimos; si no hay Docker, P5 real NOT_RUN local. Operador publica una vez; B-02 del nuevo SHA debe acreditar P0–P4 y P5 completo, incluida revisión sin escritura, recuperación, asociación, importación y persistencia. Si una etapa nueva falla, no inferir que F-008 sigue abierto si su propia frontera se acreditó; no abrir otra ronda automática. Conciliar por aserción/UC y distinguir cierre de F-008 del gate global. Tras aceptación, RFA4B; no comenzar ahora. Gate integral frontend 645/646 histórico, E5 pendiente y producción NO-GO.

Git solo lectura. No commit/push/merge, main, DEV, servicios reales, dependencias, migraciones, esquema, cambios de RLS, backend-validation, nuevas skills/subagentes, ni L-06C–I. No repetir suites verdes; no pedir de nuevo GO/evidencia incorporados. Preservar trabajo ajeno.
