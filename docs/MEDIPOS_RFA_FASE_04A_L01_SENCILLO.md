# MediPOS — RFA4A / L-01 · Despacho de sencillo

Versión 1.0 | Baseline publicado: `50b97a2c451a15ac59a21d06c11d266b81a77a68` | Rama `ci/postgres-diagnostics-8bc6b4c`.
Modelo sugerido: GPT-6.2 Sol, Medium, Standard. Alcance exclusivo L-01 alternativa A. El operador autoriza el trabajo local al enviar el prompt; no autoriza publicación, DEV ni otros lotes.

## 1. Gobierno y evidencia de entrada

Aplicar AGENTS vigentes, `docs/MEDIPOS_FUNCTIONAL_ARCHITECTURE_AUDIT.md`, `docs/audit/rfa/ROADMAP.md` (L-01), `FINDINGS.md` (F-002), `VALIDATION.md` (SC-02, RUN-02 y RUN-10), `CHECKPOINT.md` y las filas UC-TRA-01–04/13 y UC-CAJ-04. Leer solo esas secciones y los consumidores afectados. RFA3 está revisada para iniciar este lote; RFA2 continúa parcial. No reabrir la radiografía ni leer las nueve familias.

RUN-10, Actions 45891472332 intento 1, SHA `5d3889806e11fea23a3ba19402c22c15635a1301`: PostgreSQL 18.7, 905/905 integraciones, 2694/2694 unitarias, 8/8 arquitectura; frontend 641/642, fallo no identificado. El commit posterior solo cambia documentación. No atribuir el fallo CI a F-006. UC-TRA-01 tiene E3 parcial para backend y FAIL comercial E2 para UI; UC-CAJ-04 no tiene GET real de caja abierta acreditado. No inferir E4.

## 2. Causa y alcance exacto

- `frontend/src/features/transfers/CashTransfersPage.tsx`: `canDispatch` exige `expectedCashAmount !== null` y compara el importe contra ese valor; el hint y el mensaje de bloqueo también presuponen saldo conocido.
- `frontend/src/features/transfers/CashSelection.tsx`: el mensaje de caja abierta afirma que no se puede despachar con esperado nulo. Corregir mensaje, incluido su estado de consulta, sin afectar recepción.
- `useTransferCash.ts`: ya filtra selección consultada, estado open, carga/error y pertenencia para cajero; `cash/api.ts` valida ID/sede y el contrato acepta nulo. Preservar estas guardas y la remonta scoped.
- `cash.test.support.tsx`: el doble devuelve esperado no nulo para caja abierta. Separar saldo disponible sintético del esperado de cierre; el mock de POST debe poder rechazar saldo insuficiente explícitamente, sin simular autoridad de base de datos.
- `CashTransfersPage.test.tsx`: la prueba de importes incluye `10.0001` como inválido por el saldo del mock. Cambiar esa expectativa a rechazo del servidor, NO eliminar la prueba ni las guardas de positividad, rango y precisión. `decimalInput(..., maximumMoney, 4)` preserva validación exacta (verificar implementación).
- `CashSessionStatusQueries.StatusAsync`, `CashSession`, `DispatchCashTransferHandler`, `CashTransferTransaction.DispatchScope`: el esperado persistido es de cierre; saldo vigente se calcula en servidor con ledger, bloqueo y autorización. No cambiar lógica, DTO, migraciones ni semántica de dinero. El código de backend de producción queda fuera de alcance.

## 3. Caracterización y gate previo

Verificar HEAD/diff y estado de Docker con un preflight único, sin instalar ni iniciar servicios DEV. Reutilizar evidencia válida; si el código cambió, comprobar solo diferencias relevantes. Primero escribir prueba focalizada RED en Vitest que exige permitir un despacho válido con `status: open`, `expectedCashAmount: null`, importe positivo y destino correcto; comprobar fallo por guarda original, no por setup. La caracterización histórica que esperaba bloqueo se conserva en documentación, no como aceptación futura.

Acreditar mediante prueba de contrato HTTP real con PostgreSQL efímero: abrir caja sintética, GET de estado por ID, HTTP 200, identidad/sede correctas, `status=open`, `expectedCashAmount` presente y JSON null. Usar fixture de PostgreSQL 18 y mecanismo de autenticación sintética existentes si permiten componer ese recorrido de forma segura; no inventar que existen. Preferir agregar una regresión rastreada pequeña y ejecutable a la infraestructura de tests existente (tests backend autorizados, código de producción no). Si no existe composición HTTP segura, cubrir StatusAsync con fixture real como evidencia parcial y dejar el HTTP pendiente explícito; no crear un harness nuevo ni ampliar dependencias. Un test HTTP con doble puede complementar pero no sustituye PostgreSQL.

**Orden normal:** ejecutar la caracterización real primero y corregir solo si confirma el contrato. Si contradice el contrato, parar L-01 y registrar causa/diff; no cambiar guarda ni backend a ciegas.

**Vía de candidato provisional (autorizada solo al enviar este encargo):** si falta Docker/runner aislado local, no quedar en un bucle ni volver a instalarlo. Dejar la prueba de integración/contrato rastreada preparada para CI cuando la infraestructura existente lo permita. Con el contrato estático verificado y el RED reproducido, se permite preparar la corrección frontend A en el mismo árbol, como `L01_CANDIDATO_PENDIENTE_CONTRATO_REAL`; NO se declara aprobada, no se publica ni usa en DEV. Se exige confirmación independiente del GET real antes de aceptación, además del gate de CI del operador. Si la prueba no puede prepararse sin un harness nuevo, entregar candidato y receta explícita, con bloqueo sin resolver. Si el GET real ejecutado contradice la hipótesis, no seguir por esta vía. El operador revisa el candidato antes de stage/commit y ejecuta manualmente Actions; CI verde no reemplaza una aserción ausente. No se autoriza un segundo lote.

## 4. GREEN: corrección mínima A

Habilitar solicitud de despacho solo con API disponible, selección autorizada de caja abierta y vigente, destino consultable distinto al origen, lista de sucursales válida/completa según guardas actuales, importe exacto positivo dentro de máximo/escala y sin operación pendiente/bloqueada ni registro incompatible. Quitar la dependencia del esperado de cierre para decidir saldo disponible; no usar apertura, cero, número JS flotante ni saldo cacheado como sustituto. El POST existente y el ledger del servidor deciden fondos. No tocar `canReceive` ni las transiciones salvo un defecto nuevo directamente necesario y demostrado, que se documentará antes de ampliar alcance.

Ajustar únicamente mensajes relacionados: «Caja abierta» y «El saldo se verifica al confirmar el envío» (no afirmar disponibilidad); indicar datos que faltan, evitar texto que prometa saldo previo. Para `cash_transfer.insufficient_cash` usar el mecanismo de error existente, mensaje comprensible y preservar importe/destino editables, si el rechazo es definitivo. No convertir 403/409/503 o respuesta ambigua en autorización de un segundo envío. Respetar semántica de `outcomeUnknown` y las reglas de `useTrackedTransfer`: intención/requestId original, recuperación por GET y sin reenvío ciego. No alterar hooks compartidos salvo necesidad demostrada; preferir cambio local. Mantener Blue Studio/UAX.

## 5. Regresiones y aceptación

Ejecutar RED/GREEN focalizado y al final suite de Sencillo (incluye recepción y recuperación); ampliar solo a consumidores afectados si se toca un helper compartido. Build/lint focalizado o un único chequeo del frontend si necesario, sin regenerar assets servidos por DEV. Reutilizar resultados anteriores como históricos, no como gate del candidato.

| Caso | Aserción mínima |
|---|---|
| Caja abierta, esperado null, destino válido, 1.0001 PEN | Habilita despacho, un POST con cuerpo decimal exacto y clave de intención, doble clic no duplica; estado observado por lectura. |
| Importe inválido | Cero, negativo, fuera de rango o escala excesiva no hacen POST. |
| Importe representable pero supera fondos | UI puede solicitar, servidor/mocked API rechaza con código exacto; sin transferencia exitosa, datos editables, sin reenvío automático. No afirmar no escritura real desde mock. |
| Caja cerrada, no seleccionada, lectura fallida/en curso, identidad ajena o ámbito inválido | Acción bloqueada; cero POST. Destino igual a origen tampoco. |
| Owner/Cashier/Pharmacist | Preservar acceso y selección autorizada; Pharmacist no adquiere permiso. |
| Acuse perdido o lectura incierta | Recuperar la intención por GET, un solo POST, no liberar bloqueo por lectura insuficiente. |
| Recepción | Caja destino abierta autorizada y distinta, recepción completa única, estado terminal; sin regresión. |
| Contrato real | GET open 200 y esperado JSON null con caja sintética y scope verificado; prueba aislada, sin DEV. |
| Integridad real | En CI/fixture, contrastar pruebas existentes de saldo insuficiente, concurrencia, revocación, auditorías, ledger, recuperación e aislamiento; añadir caso si frontera requerida no está cubierta. No repetir 905 pruebas localmente. |

Si la suite focalizada falla, resolver causa dentro de L-01. No relajar aserciones de seguridad, tiempos ni precisión para conseguir verde. No alterar pruebas ajenas (incluidas las dos de F-006 y el fallo CI aún sin identidad). E4 permanece BLOCKED si no hay entorno completo acreditado; no crear uno en este lote. No ejecutar suites completas repetidamente ni instalar skills/herramientas. Si el preflight bloquea algo, registrar una vez.

## 6. Entrega y continuidad

Mantener `docs/audit/rfa/` como fuente única. Actualizar solo F-002, UC afectados, RUN nuevo con selección/aserciones/entorno y resultados, estado de L-01 en ROADMAP, TO_BE solo si la decisión aprobada difiere, CHECKPOINT y resumen/índice si cambia estado. Conservar resultados históricos y distinguir E2, E3 y E4 y evidencia del candidato frente al baseline. No elevar otros UC a PASS por asociación; RFA2 global sigue parcial. No copiar el MD de CI a otra carpeta. Registrar diff resumido y `git diff --check`. Entregar lista corta de archivos, tests RED/GREEN y límites, próximo paso exacto, sin volcar logs completos.

Estado del lote: `L01_LISTO_PARA_REVISION_CI` si contrato real y pruebas focalizadas acreditados localmente, `L01_CANDIDATO_PENDIENTE_CONTRATO_REAL` si no se ha ejecutado esa frontera, `L01_BLOCKED` si no hay candidato seguro, `L01_FAIL` si contradicción o fallo esencial. Ninguno equivale a producción lista. Aceptación definitiva requiere revisión externa, evidencia del GET real, CI del candidato y decisión del operador; E4 queda pendiente cuando no se acredita. Detenerse al terminar L-01, no iniciar L-02/L-05 ni refactor transversal.

Git solo lectura; preservar trabajo local. Operador realiza stage/commit/push, CI, reinicios y despliegue. No acceder a credenciales, no modificar CI/AGENTS/dependencias/configuración, ni tocar DEV, datos reales, OAuth, SUNAT, esquema, RLS o migraciones. Solo frontend afectado, sus tests, prueba de contrato backend aislada y documentación RFA autorizados. No usar scripts destructivos, no eludir autenticación. Checkpoint al cerrar RED, candidato y gate o antes de compactación.
