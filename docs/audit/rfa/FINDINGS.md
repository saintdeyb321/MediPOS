# Hallazgos RFA

## Hallazgos y seguimiento

Los IDs F-001–008 se conservan. F-008 registra el alta prematura del producto local. [Validación](VALIDATION.md) · [Aceptación por lote](ROADMAP.md).

| ID · tipo · prioridad | Actor y expectativa | Causa, confianza y alcance | Decisión mínima, prueba y riesgo de regresión |
|---|---|---|---|
| F-001 · descubrimiento de arqueo · P2 | Cajero/Owner cierra y vuelve por entrada limpia. FR-CASH-002/003 exigen arqueo y FR-CASH-005 directorio de abiertas. Un historial para cerradas es una capacidad adicional. | Alta en la separación de lecturas. SC-01 E2 muestra el vacío y la recuperación con URL original. E3 acredita fila, signo, auditoría y relectura por ID en un cierre análogo. Causa del incidente DEV desconocida. | L-02. Decidir roles y periodo de historial. Verificar 100→10 exacto con vacío en activas y relectura de −90 por el actor autorizado. Riesgo: exponer arqueos ajenos o permitir segundo cierre. |
| F-002 · defecto de habilitación · P1 | Cajero con caja abierta y destino válido intenta despachar. FR-CASH-004 exige préstamo vinculado y auditable. | Alta en la guarda incompatible: E2 con valor nulo bloquea el botón; E3 acredita saldo transaccional aun sin arqueo final. Falta ligar GET abierto y UI en el mismo sistema aislado. | L-01. Aprobar envío sujeto al saldo del ledger; decidir aparte si mostrar un disponible informativo. Insuficiente y carrera con reembolso no deben producir sobregiro. Pendiente conserva clave, sin otro POST. |
| F-003 · alcance de búsqueda · P2 | Owner filtra una página que no contiene el producto. FR-CAT-001 define catálogo privado/global; no establece búsqueda administrativa transversal. | Alta en el alcance local por RUN-04; necesidad de buscar sobre todo el directorio pendiente de producto. La búsqueda POS E3 tiene otra finalidad. | L-03. Conservar etiqueta o aprobar consulta paginada global al negocio. Caso de segunda página, rol y exclusión de otro tenant. Riesgo: presentar vacío de página como ausencia total. |
| F-004 · descubrimiento de compra · P2 | Operador intenta reabrir recepción sin referencia. FR-PUR-004 pide buscar el documento/referencia y FR-PUR-002 protege la recepción. | Alta en coincidencia exacta y recuperación por ID. RUN-04 lo observa en DOM. E3 contrasta dos compras por la misma referencia, detalle de la sede y exclusión de ajenas. No se ha demostrado pérdida de la compra. | L-03. Decidir si el operador requiere recientes sin documento. Probar salida, entrada limpia, dos referencias iguales y una ausente. Riesgo: usar referencia duplicada como clave para rehacer compra. |
| F-005 · coste de lectura · P2 provisional | Personal de inventario cambia de existencias a lotes y necesita nombres legibles de su sede. FR-INV-001/FR-TEN-003 son invariantes, sin objetivo de latencia acordado. | Alta en la cuenta DOM 3/0/3 de RUN-09. Coste de red y demora percibida desconocidos. E3 verifica ámbito y cantidades de las lecturas, sin cronometrar el navegador. | L-04. Medir y acordar presupuesto antes de ampliar DTO. Eliminar lecturas solo si se conserva nombre, ámbito y paginación. Riesgo: reutilizar etiquetas de otra sede. |
| F-006 · repetibilidad de pruebas · P2 provisional | QA navega por Transferencias o Plataforma. El control debe esperar el resultado de acceso o la carga del destinatario. Sin requisito FR de sincronización de tests. | Alta para los dos resultados discordantes RUN-03/05; causa de temporización específica y efecto en producto sin demostrar. RUN-10 tiene un FAIL frontend sin nombre. | L-05. Identificar el caso de CI y caracterizar las transiciones observables antes de ajustar esperas. Riesgo: esconder una carrera de autorización o duplicar el diagnóstico de un fallo desconocido. |
| F-007 · recuperación de un alta · P2 | Owner vuelve después de perder el acuse de un alta. La conciliación debe conservar la intención original. | E2. El código interno quedaba solo en memoria y, tras recargar, se podía cerrar la recuperación con otra ficha del mismo tenant. | L-06B conserva el código en el contexto de la sesión del navegador. Otro producto mantiene el bloqueo. Se conserva la revisión humana del original. |
| F-008 · envío prematuro · P2 | El Owner avanza a revisión y el alta local ya se registra. CAT-09/21 y regresión del formulario compartido CAT-10. FR-CAT-001/003/007. | RUN-23 confirma un POST prematuro, 201, producto y auditoría persistidos. RUN-24 demuestra la reutilización del botón y el clic sin cancelar en DOM. | RFA4A_CORRECCION_CANDIDATA. Confirmación separada y CI aislado pendiente. |

La evidencia de ejecución se vincula al [registro RFA2](VALIDATION.md). E2 se limita a las fronteras indicadas. Los diagnósticos originales siguen abajo.

**Correcciones demostradas fuera de los siete IDs.** UC-CAJ-10 documenta que quitar la última línea de un borrador guardado dejaba la navegación sin protección. El RED/GREEN y la corrección local de ese estado están en [RUN-16](VALIDATION.md#run-16). P4 acredita el guardado versionado, pero la variante de vaciado conserva evidencia DOM. Los resultados de P5 y del test frontend de Compras siguen siendo incidencias de ejecución sin causa de producto establecida. [RUN-19](VALIDATION.md#run-19) · [RUN-13](VALIDATION.md#run-13).

## F-001 · Una caja cerrada deja de aparecer en activas

**RUN-17 · E4 del historial.** La caja 100→10 conserva −90 y una auditoría. Tras perder el acuse, la UI recupera el mismo arqueo por URL y desde Cierres con entrada limpia. Activas excluye el cierre. Incidente DEV sin atribución. [Aserciones y variantes](VALIDATION.md#run-17).

**RUN-15 · E4 anterior al historial.** El cierre 100→10 persistió −90 con una auditoría. El navegador recuperó el arqueo por URL y la entrada limpia no ofrecía cómo encontrarlo. El último hecho confirma la brecha de descubrimiento. L-06A añade Cierres para la sede autorizada. Su aceptación en el piloto nuevo y la causa del incidente DEV siguen pendientes. [Evidencia](VALIDATION.md#run-15).

**RUN-13 · E3 del caso exacto.** PostgreSQL confirma apertura 100, contado 10, −90, auditoría única, rechazo del segundo cierre, exclusión de activas y arqueo por ID desde otro scope. Reentrada en navegador aislado e incidente DEV permanecen abiertos. [Aserciones](VALIDATION.md#run-13).

**RFA4 · Caso exacto preparado.** La prueba PostgreSQL de [RUN-12](VALIDATION.md#run-12) fija apertura 100 y conteo 10 sin actividad, comprueba un único cierre con diferencia −90 y relee el arqueo por ID desde otro scope. Compilada, NOT_RUN. El incidente DEV sigue sin atribuir.

**RFA2 · CONFIRMADO_EN_EJECUCION E2 para la reentrada, BLOCKED en E3/E4.** SC-01 / RUN-07 envió contado 10 una sola vez, mostró el faltante 90 de la respuesta simulada, vio activas vacías con URL limpia y recuperó el arqueo original por GET al volver con su dirección. RUN-01 verificó lecturas fallidas y respuestas inciertas. Fila, auditoría, importes calculados por el servidor y reentrada en navegador real requieren E3/E4. El incidente DEV permanece sin atribución.

- UC-CAJ-05/06/07/16/17. FR-CASH-002/003/005. Evidencia E1 corroborada por la revisión externa del baseline 939ad83. P2 provisional por descubrimiento del arqueo. Un cierre con 100 inicial y 10 contado, sin movimientos, debe registrar −90. `CashCloseTransaction.cs:59–72` confirma el registro, mientras `CashSessionReader.cs:34` filtra open. La URL `cash` permite recuperar y la entrada limpia carece de historial. No hay evidencia sobre si el caso del operador se guardó. Comparar ambos GET, actor/sede y acuse según [receta Caja](flows/01-caja-pos.md). PENDIENTE_RFA2.

## F-002 · Sencillo exige saldo esperado en una caja abierta

**RUN-15 · E4 Owner.** GET de estado abierto con esperado null en HTTP+PostgreSQL. Despacho y recepción entre sedes, pérdida de acuse y consulta de constancia con un único efecto acreditados por navegador. El rechazo por fondos insuficientes no escribió. Aceptación por demás roles y gate general pendientes. [Evidencia](VALIDATION.md#run-15).

**RUN-13 · E3 de aplicación.** `StatusAsync` leyó en PostgreSQL la caja abierta con esperado nulo. RUN-12 ya verificó HTTP con dobles. Falta el GET respaldado por PostgreSQL y el despacho desde navegador aislado. [Aserciones](VALIDATION.md#run-13).

**RFA4 · Evidencia HTTP E2.** El host efímero con dobles devolvió 200 y JSON null presente para el esperado de cierre de la caja abierta correspondiente al actor y sede. Quedan la prueba de aplicación con PostgreSQL y la comprobación del GET contra esa persistencia. [RUN-12](VALIDATION.md#run-12).

**RFA4A · Corrección local provisional.** [RUN-11](VALIDATION.md#run-11) reproduce el bloqueo en el baseline y verifica la alternativa A con dobles HTTP. El importe se valida localmente por formato, rango y positividad. El saldo viene del ledger bajo bloqueo al ejecutar el despacho. El rechazo definitivo por fondos insuficientes mantiene importe y destino editables. Una respuesta incierta conserva la intención original. Pendientes el GET real de caja abierta y CI del candidato.

**RFA2 · CONFIRMADO_EN_EJECUCION E2, P1 provisional.** SC-02 / RUN-02 caracteriza que, con estado `open` y `expectedCashAmount: null` según el contrato inspeccionado, el botón queda deshabilitado y se envían cero POST aun con apertura positiva y destino elegido. El doble heredado con esperado no nulo sí permite enviar. Falta probar respuesta real del servidor, cálculo/recepción y la misma UI en navegador aislado.

- UC-TRA-01, UC-CAJ-04, FR-CASH-004. E1 de cableado corroborada por la revisión externa del baseline 939ad83. P1 provisional para despacho por UI. `CashTransfersPage.tsx:63,98–101` exige esperado no nulo. `CashSessionStatusQueries.cs:18–28` entrega el valor persistido y `CashSession.cs:54–60` lo establece al cerrar. `CashTransferTransaction.cs:90–96` calcula el saldo vigente al despachar. El doble `cash.test.support.tsx:13` devuelve un esperado en open; su existencia no acredita funcionamiento del circuito real. En RFA2 comparar respuesta de estado y disponibilidad del botón con caja válida y saldo inicial positivo. PENDIENTE_RFA2.

## F-003 · El filtro de mantenimiento es por página

**RUN-21 · SEARCH acreditado.** El filtro administrativo alcanza el producto de la segunda página usando ingrediente, categoría y estado en el navegador, con API respaldada por PostgreSQL. Los negativos de acceso y el escape literal se verifican por HTTP. El fallo posterior ocurre en CREATE. [Aserciones](VALIDATION.md#run-21)

**Estado al cierre.** La búsqueda ampliada está en el baseline publicado y conserva la verificación local. P5 falló sin identificar qué aserción llegó a ejercitarse. El hallazgo histórico y la evidencia local siguen vigentes. [RUN-19](VALIDATION.md#run-19).

**RUN-18 · Solución aprobada, candidato.** Mis productos combina filtros de texto, categoría y estado en SQL antes de paginar. La prueba con dobles encuentra la fila que antes quedaba en página 2 y verifica un nuevo GET desde offset cero. La comprobación real con PostgreSQL y navegador está preparada en P5. [Aserciones](VALIDATION.md#run-18).

**RFA2 · CONFIRMADO_EN_EJECUCION E2.** SC-04 / RUN-04 muestra el vacío en la primera página y el producto al pasar a la segunda. El filtro estaba identificado como de página. El comportamiento coincide con el alcance visible, sin violación de contrato demostrada.

- UC-CAT-06/20. E1. P2 provisional. `ProductsPage.tsx:48–62` filtra filas recibidas. `CatalogDirectoryReader.cs:17–22` aplica offset/límite. La UI expresa esa limitación. Medir producto en página posterior y observar si la necesidad requiere otra consulta. PENDIENTE_RFA3.

## F-004 · Consulta de compras condicionada por referencia o ID

**RFA2 · CONFIRMADO_EN_EJECUCION E2.** SC-05 / RUN-04 abre una recepción sintética sin referencia mediante el ID original. En la misma entrada, la búsqueda por referencia no devuelve coincidencias. Hubo cero escrituras. Persistencia y descubrimiento real tras limpiar URL pendientes.

- UC-COM-09/12. E1. P2 provisional. `PurchasingStore.cs:84–90` compara referencia exacta y `PurchaseLookup.tsx:15–42` expone referencia o código de soporte. Una recepción sin referencia puede existir y seguir sin aparecer en la búsqueda. Verificar recuperación tras recarga y salida con el ID original antes de decidir requisitos futuros. PENDIENTE_RFA2.

## F-005 · Existencias y lotes cargan el nombre por detalle

**RFA2 · CONFIRMADO_EN_EJECUCION E2.** SC-06 / RUN-04 cuenta un GET de detalle en frío y cero GET adicionales al abrir Lotes con el mismo ID y ámbito. El posible costo con varios IDs, caché vencida, nuevo ámbito o red real queda pendiente de medición.

**02B · RUN-09.** Con tres IDs distintos, la UI solicitó tres detalles en frío, reutilizó los tres al pasar a Lotes y solicitó tres nuevos al cambiar de sede, sin etiquetas de la sede anterior. Transporte simulado, sin medición de latencia o tráfico real. Se conserva P2 provisional.

- UC-COM-03/04. E1 del circuito. P2 provisional. `InventoryDirectoryReader.cs:9–22` proyecta IDs. `ProductLookup.tsx:10–23` resuelve nombre con caché scoped por ID. Comparar cantidad de GET con caché fría, caliente y paginación. Hipótesis de coste en operación real, pendiente de medición. PENDIENTE_RFA2.

## F-006 · Repetibilidad de dos aserciones DOM bajo carga

**RUN-13.** Los casos de Transferencias y Plataforma pasaron. El FAIL identificado corresponde al caso de revisión obsoleta en Compras, que pasó focalizado y en su archivo local. Queda NOT_REPRODUCED y sin causa atribuida. El FAIL innominado de RUN-10 sigue separado. [Resultados](VALIDATION.md#run-13).

**RFA4 · Sincronización ajustada.** Los dos casos esperan la transición que verifican. Transferencias controla el GET de origen y verifica el bloqueo antes y después de la denegación. Plataforma mantiene pendiente la lectura del destinatario y verifica la ficha antes y después de recibirlo. Los dos casos pasaron antes y después en esta sesión. El fallo histórico de CI sigue sin identificar. [RUN-12](VALIDATION.md#run-12).

**CONFIRMADO_EN_EJECUCION para el runner, P2 provisional.** RUN-03 falló en `TransfersPage.test.tsx:66` al consultar de inmediato si se había emitido el GET de acceso y en `PlatformPage.test.tsx:275` mientras el destinatario seguía cargando. Ambos casos pasaron serializados en RUN-05. El fallo de repetibilidad dificulta interpretar el gate. La espera/aserción de lectura es la causa inmediata observada. La causa de calendario exacta y un fallo equivalente de producto siguen sin demostrar. Para una aceptación posterior, medir llegada del estado y comprobar que las acciones permanecen cerradas durante la espera. UC-TRA-08, UC-PLA-07 y UC-PLA-17.

## F-007 · Conciliación de alta con un producto distinto tras recarga

**Estado vigente.** RUN-25 acredita guarda y reentrada con la intención original. El bloque de conciliación final falla sin aserción individual conocida. RUN-26 verifica con dobles que la ficha ajena y una lectura de detalle en vuelo mantienen el bloqueo, y que revisar el original permite conciliar sin otro POST. La aceptación real de la conciliación sigue pendiente. [RUN-25](VALIDATION.md#run-25) · [RUN-26](VALIDATION.md#run-26)

RUN-21 delimita el fallo a `CREATE/POST_LOST_ACK`, antes de la reentrada. F-007 conserva su RED/GREEN local. La evidencia del piloto no permite atribuir este fallo a F-007. [RUN-21](VALIDATION.md#run-21)

**RUN-18 · E2 RED/GREEN.** El código original vivía en el estado del hook. El marcador URL seguía presente después de recargar y quedaba vacía la comparación del código. La regresión observó el botón de conciliación habilitado con otra ficha. El candidato conserva el código original en `sessionStorage` por identidad, membresía, tenant y sede. Exige coincidencia y revisión explícita, conserva un único POST y deja bloqueadas las solicitudes anteriores sin un identificador verificable. Los endpoints de alta carecen de constancia transaccional por solicitud. [Aserciones y límites](VALIDATION.md#run-18).

## F-008 · El piloto de alta local falla en la pérdida inducida del acuse

**Corrección acreditada en alta local.** RUN-25 comprueba cero POST en revisión, un 201 deliberado, aborto del acuse, una auditoría Owner y cero stock. El fallo restante pertenece al bloque final de CAT-21. Asociación global y P5 completo siguen pendientes. [RUN-25](VALIDATION.md#run-25)

**Candidato anterior.** RFA4A_CORRECCION_CANDIDATA. RUN-23 constató que el POST local se ejecutó antes de Guardar. La escritura y su auditoría concuerdan con el único 201 observado y el inventario no registró lotes ni movimientos. El RED en DOM de RUN-24 mostró que React reutiliza el botón Continuar como submit durante el clic al entrar en revisión. El correctivo cancela esa activación y monta un botón distinto para la confirmación. Queda pendiente acreditar el resultado en Chromium y PostgreSQL. [RUN-23](VALIDATION.md#run-23) · [RUN-24](VALIDATION.md#run-24)

**Historial.** RUN-21 delimitó el fallo a `POST_LOST_ACK`. El probe de RUN-22 separó las fronteras y localizó en RUN-23 el envío durante REVIEW, antes de la confirmación deliberada.

**Contrato.** [CreateProductForm](../../../frontend/src/features/catalog/CreateProductForm.tsx#L32) requiere revisión, validez y acceso para despachar. [useCatalogWrite](../../../frontend/src/features/catalog/useCatalogWrite.ts#L54) conserva la intención por sesión de pestaña y detiene el envío si no logra armarla. [catalogApi](../../../frontend/src/features/catalog/api.ts#L60) valida DTO/ámbito y [HttpClient](../../../frontend/src/shared/api/http.ts#L78) prepara CSRF, envía con cookies same-origin y trata la pérdida de respuesta de una escritura como incertidumbre. [CatalogEndpoints](../../../backend/src/MediPOS.Api/Tenant/CatalogEndpoints.cs#L48) devuelve 201 tras completar [el alta autorizada](../../../backend/src/MediPOS.Application/Modules/Catalog/CreateLocalBusinessProduct/CreateLocalBusinessProductHandler.cs#L19). Producto y auditoría se guardan en ámbito transaccional y el código interno es único por tenant. La unicidad rechaza un duplicado, mientras que la recuperación de F-007 depende de la intención de esa pestaña y carece de constancia durable de idempotencia.

| Orden diagnóstico | Hipótesis por comprobar | Señal que la distingue |
|---|---|---|
| 1 | El clic no se ejecuta, la guarda/CSRF impide salir o la URL observada queda fuera de la ruta interceptada. | Separar localizador accionable, clic y evento de solicitud de navegador. Ausencia de POST y presencia sin intercepción son casos distintos. |
| 2 | El POST llega con una respuesta distinta de 201 o el 201 no tiene ID válido para el helper. Un rechazo de autorización, validación, duplicado o fixture son posibilidades. | Registrar intercepción, categoría HTTP saneada y validación booleana del ID. Comparar con fixture y contrato sin publicar cuerpo ni código de producto. |
| 3 | Fetch/reenvío o aborto falla. La espera puede resolverse antes de terminar el aborto y un fallo en el callback puede parecer timeout. | Distinguir entrada de callback, resultado upstream, finalización de aborto y timeout. Usar clasificación estática, preservando la excepción y la aserción originales. |
| 4 | Con 201 y acuse descartado, el botón esperado cambia de nombre o estado, o el bloqueo no persiste. | Distinguir localizador del botón, marcador pendiente observado y estado bloqueado después de confirmar el descarte. Una consulta fixture separada determina si el alta persistió. |

El probe confirmó respuesta, ID, aborto y persistencia. En el candidato se conserva para acreditar cada frontera, incluida la conciliación sin reenviar. [Aceptación RFA4A](ROADMAP.md#primer-encargo-rfa4a)
