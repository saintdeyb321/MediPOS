# RFA2 · Validación conductual y persistencia

<a id="run-24"></a>
## RUN-24 · Corrección candidata F-008

**RFA4A_CORRECCION_CANDIDATA** sobre `2248381ffcd808533f2f8204ff8aeece4ce028cd`. En `CreateProductForm`, Continuar cancela su acción por defecto antes de cambiar el paso. Conserva `type="button"` y una identidad DOM distinta de Guardar/Asociar, que siguen siendo controles submit nativos. La validación de los datos, revisión documental de medicamentos y recuperación de la intención se conservan.

| UC | Caso verificado |
|---|---|
| CAT-09 | RED en DOM previo al correctivo. El mismo elemento de Continuar cambia a submit durante el clic y la acción por defecto queda sin cancelar. GREEN local. Nodos distintos, clic cancelado, avance y retroceso sin escritura ni marcador, un POST deliberado, CSRF y decimal exacto. Medicamento mantiene Guardar inactivo hasta su revisión documental y después despacha un POST. |
| CAT-10 | Mismo RED/GREEN de identidad y cancelación en asociación global. Enter sobre Continuar permite revisar sin crear intención y Enter sobre Asociar despacha una sola escritura en DOM con transporte doble. P5 comprueba el POST real de ruta exacta, desde antes de abrir el formulario, cero en revisión, un 201 tras la confirmación con Enter y el vínculo y la restricción de editar la referencia. |
| CAT-21 | El doble de transporte pierde el acuse tras registrar el alta. La recarga conserva el bloqueo. Otra ficha no permite conciliar. La ficha original habilita el cierre con un solo POST total. P5 conserva el probe, la lectura independiente por código y las aserciones de reentrada y conciliación. |

**Límite del RED/GREEN.** Las pruebas DOM caracterizan la reutilización y cancelación del evento y usan dobles HTTP. [RUN-23](#run-23) acredita el alta prematura real. El orden nativo de activación en Chromium queda como explicación respaldada por esas dos observaciones, pendiente de confirmar el comportamiento corregido en el siguiente B-02.

**Comprobaciones locales.** `npm.cmd run test -- src/features/catalog/ProductsPage.test.tsx src/features/catalog/CatalogDirtyState.test.tsx` obtuvo 21 PASS, 0 FAIL, 0 SKIP en GREEN. El filtro `-t 'creates a local general product|associates a selected reference'` obtuvo 2 FAIL esperados en RED y 2 PASS finales, con 16 excluidos por filtro en cada ejecución. ESLint focalizado sobre formulario y prueba PASS. `npm.cmd run build -- --outDir node_modules/.cache/rfa4a-f008-build` PASS, con advertencia de tamaño de chunk. El primer build detectó un error de tipos en la prueba y se corrigió. `dotnet build tests/MediPOS.E2E/MediPOS.E2E.csproj --no-restore -p:BaseOutputPath=obj/b02-bin/ -m:1 --disable-build-servers` desde `backend/` PASS con 0 errores y 0 advertencias.

**P0–P5 del candidato NOT_RUN local** por ausencia de Docker. El operador publica una vez en la rama diagnóstica. B-02 debe acreditar primero la frontera de F-008 y después las restantes etapas de P5. El gate integral frontend 645/646, E5 y producción NO-GO siguen independientes.

<a id="run-23"></a>
## RUN-23 · Alta prematura antes de Guardar

[CI B-02 46019645382](https://github.com/saintdeyb321/MediPOS/actions/runs/46019645382), job 140575642581, intento 1, push del 27/11/2026 14:51:17–14:54:33 UTC (196 s). SHA `2248381ffcd808533f2f8204ff8aeece4ce028cd`, build `e40191caae1b9b7295a27b34b2b3c9489d4a61cc1484178ae3d73c17d0985203`. Chromium/API en HTTPS aislado, PostgreSQL 18.7 (180007) y runtime `medipos_test_runtime`. Preparación PASS, workflow **FAIL**, salida 1.

| Frontera | Evidencia real |
|---|---|
| P0–P4 / P5 SEED y SEARCH | PASS en las fronteras ya descritas en RUN-21. |
| P5 CREATE / REVIEW · CAT-09 E4 histórico | Lectura previa con cero producto, auditoría, lotes y movimientos. Antes de `CLICK` se observó un POST y una intercepción, 201 upstream, ID válido y aborto completado. La lectura runtime posterior confirma una fila con ese ID, una auditoría de creación por el Owner y cero lotes y movimientos. El formulario había escrito antes del clic Guardar. La aserción individual de REVIEW queda sin identificar. |
| CAT-21 y CAT-10 | REENTRY y RECONCILIATION no alcanzadas. EDIT, ASSOCIATION, IMPORT y PERSISTENCE final se mantienen NOT_RUN. El resultado real acredita el defecto del alta local. La aceptación de su corrección y de la recuperación aún está pendiente. |

<a id="run-22"></a>
## RUN-22 · F-008 · candidato diagnóstico local

RFA3 aprobada documentalmente sobre `e1df3ec9e9fb1b346118a1b168450cf832862801`. [RUN-21](#run-21) sigue siendo la última ejecución del piloto en Chromium/API/PostgreSQL. **RFA4A_CANDIDATO**. CAT-09 E3/BLOCKED y CAT-21 E2/BLOCKED. Causa de F-008 indeterminada.

| Frontera en [el piloto de Catálogo](../../../backend/tests/MediPOS.E2E/CashPilotTests.Catalog.cs) | Qué permitirá concluir la ejecución |
|---|---|
| Antes de crear | Un contexto nuevo con el runtime del tenant verifica que el código está libre y que el tenant sembrado carece de lotes y movimientos. Los observadores de solicitud del navegador y de intercepción de la ruta exacta se instalan antes de abrir el formulario. En revisión se exige formulario válido, un botón visible y habilitado, intención libre y ambos contadores a cero. |
| Clic y red | Un único clic normal. Se distinguen el retorno del clic, los POST observados y los interceptados. Ante timeout tras el clic, NO_POST se refiere a la URL exacta; NO_INTERCEPTION significa que el navegador sí emitió esa solicitud. Si el callback sigue en curso, la clasificación es INDETERMINADA. |
| Acuse y guarda | Se distingue categoría de estado upstream, validez del ID, aborto y descarte del recurso. La espera de 20 s termina con el resultado del callback tras su limpieza. Se conserva el primer error y se informa por separado un fallo de limpieza. Un 201 upstream no fue recibido por React si el aborto terminó. Se exige recuperación visible y habilitada con Guardar deshabilitado, y el código original antes y después de reentrar. |
| Conciliación y persistencia | Se contrasta el GET de detalle del mismo ID y código, el desbloqueo y la retirada del marcador. Se vuelve a contar POST e intercepciones. Una lectura runtime nueva, en snapshot repetible, contrasta producto, auditoría de creación del mismo ID y usuario, lotes y movimientos. Ante error de UI se obtiene la lectura por separado conservando la primera excepción. INCONCLUSIVE advierte que aún puede haber una operación en vuelo. Los conteos se publican como 0, 1 o MANY. Una fila por sí sola no acredita recuperación. |

**Comprobaciones locales.** SDK 10.0.401, desde `backend/`: `dotnet restore tests/MediPOS.E2E/MediPOS.E2E.csproj --locked-mode` y `dotnet build tests/MediPOS.E2E/MediPOS.E2E.csproj --no-restore -p:BaseOutputPath=obj/b02-bin/ -m:1 --disable-build-servers`. Build PASS, cero errores y advertencias. YAML analizado y AWK real contra entradas sintéticas. RED del filtro anterior: faltaban 64 de 71 líneas esperadas. GREEN: 71/71 reproducidas sin prefijo y 10/10 entradas inválidas o con información adicional omitidas. La única modificación del workflow amplía su filtro de salida.

**Piloto local NOT_RUN** por ausencia de Docker. Sin RED/GREEN funcional atribuible al producto. El operador publica el candidato en la rama actual. El push activa B-02 una sola vez. Revisar SHA/build, P0–P5, primera frontera fallida y lectura independiente en el resumen saneado. Si falla una etapa posterior, detener la aceptación en esa etapa. Frontend histórico 645/646, E5 y DEV conservan sus límites. Producción NO-GO.

<a id="run-21"></a>
## RUN-21 · Primera frontera fallida de P5

[CI B-02 46015549281](https://github.com/saintdeyb321/MediPOS/actions/runs/46015549281), job 140561621805, intento 1, push del 27/11/2026, 14:23:50–14:27:39 UTC (229 s entre creación y actualización). SHA `1deb4dae9ce5f11c4f9fc0f374837c5d67e1d1d4`, build `e40191caae1b9b7295a27b34b2b3c9489d4a61cc1484178ae3d73c17d0985203`, PostgreSQL 18.7 (180007), origen HTTPS efímero y runtime `medipos_test_runtime`. Checkout, herramientas, React, restore/build E2E y Chromium completados. **Failure**, exit 1 en `Run preflight and P0–P5`. El build coincide con RUN-19. El SHA y las fronteras se registran por ejecución.

| Frontera | Evidencia y aplicación por UC |
|---|---|
| P0–P4 PASS | Reiteran las aserciones delimitadas en [RUN-15](#run-15) y [RUN-17](#run-17), incluidos Caja/POS y Sencillo Owner. |
| P5 SEED PASS | Completó el seed sintético y las sesiones Owner, Cajero y Owner de otro tenant. El alta del seed por handler no acredita el alta del formulario. |
| P5 SEARCH PASS · CAT-06/20 E4 | Primera página de 50 sin coincidencia y segunda página con la única fila restante. En navegador, ingrediente, categoría y estado inactivo encuentran el producto y limpiar restituye la lista inicial. HTTP real acredita comodines literales, negativos por categoría/estado/offset, status inválido 400, Cajero y otro tenant 403 y lectura del producto propio en el segundo tenant. Lista/tarjetas, otros términos y revocación dinámica conservan sus fronteras previas. |
| P5 SEARCH PASS · CAT-04 E4, CAT-01 E3 | Selector de categoría administrativa utilizable en navegador. Exclusión de inactivo para Cajero comprobada por GET operativo real, sin recorrido de grilla ni verificación de stock. |
| P5 CREATE · CAT-09/21 | FORM alcanzado. `CREATE FAIL POST_LOST_ACK` delimita un bloque que incluye clic, solicitud interceptada, comprobación de respuesta, descarte del acuse y guarda posterior. El resumen no acredita envío, HTTP 201, commit, número de escrituras ni estado final de la guarda. Se mantienen E3 histórica del alta y E2 local de F-007. |
| Resto de P5 | REENTRY y RECONCILIATION no alcanzadas. EDIT, ASSOCIATION, IMPORT y PERSISTENCE **NOT_RUN** en este run, incluida la lectura directa de RLS que omite filtros EF y el cotejo final de auditoría/stock. **P5 global FAIL**. |

**RFA2_CIERRE_CON_LIMITACIONES.** Cierre de la investigación acordada, con cobertura funcional parcial. 156 UC en nueve familias: 2 E1, 25 E2, 103 E3, 26 E4 y 0 E5. Se conservan 19 PASS, 135 BLOCKED, 1 NOT_RUN y 1 N/A. RUN-20 mantiene las 6 Vitest y 17 xUnit con dobles y el límite de AVI-05. CAT-22 E1/N/A e INT-16 E1/NOT_RUN. Frontend integral 645/646 de RUN-13, DEV sin atribución y E5 pendiente. El siguiente diagnóstico pertenece a RFA4A. [Primera tarea propuesta](ROADMAP.md#primer-encargo-rfa4a)

<a id="run-20"></a>
## RUN-20 · RFA2_CANDIDATO_CIERRE

HEAD documental `c521e9c5019b25371d45b9a661bb90588554e389`, aplicación `52a5ced168dbef2e80145b4e9d115113f0a63755`. El diff entre ambos solo contiene documentos. Se contrastaron los símbolos y tests pendientes frente a sus cortes de evidencia, sin transferir el resultado de una suite a otro SHA. El encargo local preexistente se conservó. Windows, .NET SDK 10.0.401 y Node 24.18.1. Docker no disponible. PostgreSQL, navegador real y DEV no se iniciaron.

Se leyeron directamente [SKILL.md](https://raw.githubusercontent.com/unclebob/ub-mode/3da62bf3fcfd2034de41f30d920d2739e566171f/SKILL.md) y [LICENSE MIT](https://raw.githubusercontent.com/unclebob/ub-mode/3da62bf3fcfd2034de41f30d920d2739e566171f/LICENSE) de ub-mode fijados en `3da62bf3fcfd2034de41f30d920d2739e566171f`. Se aplicaron sus principios autorizados de reproducir antes de atribuir, observar conducta, conservar invariantes y limitar el alcance. Uso por lectura directa, sin instalación persistente ni herramientas auxiliares.

| Familia | Frontera acreditada | Límite prioritario |
|---|---|---|
| Caja/POS | RUN-17/19, P1/P3/P4. Cierre 100→10, arqueo y reentrada, pago mixto, reversión de stock y auditoría. | Vaciado de borrador queda E2. Impresión física, otros actores y E5 pendientes. |
| Catálogo | RUN-18, filtros/escapes, recuperación F-007, versiones e importación por fronteras locales. CAT-22 E1/N/A, borrado fuera de exposición. | P5 FAIL sin aserción (RUN-19). Falta E4 de búsqueda/alta/XLSX, recuperación sin sesión y CAT-19 con writer drain. |
| Compras/Inventario | RUN-10/13, precondición concurrente, FEFO, constancia y aislamiento persistidos. RUN-20, el lector que devuelve una fila fuera de sede falla cerrado con dobles. | COM-11 sigue NOT_REPRODUCED en el histórico local. Código y prueba de Compras sin diff relevante entre RUN-13 y HEAD. Faltan aserción saneada y E4 financiero. F-004/005 siguen como brecha de consulta y medición, respectivamente. |
| Transferencias | RUN-15/19 acreditan Sencillo Owner y una sola recuperación. RUN-10 acredita mercadería. RUN-20 valida cancelación con motivo/evento y separación origen/destino con revalidación bajo espera, en dobles. | Recepción parcial y competencia de mercadería en navegador, roles operativos de Sencillo y reentrada cruzada pendientes. |
| Reportes/Comisiones | RUN-10, cálculo y reversión originales, alcance persistido. RUN-20, cancelar lecturas al cambiar contexto y descartar respuesta antigua en DOM, y revalidar membresía antes de soltar el reporte con dobles. | Falta conciliación E4 de pantalla/exportación y efecto neto entre sedes. |
| Avisos/Auditoría | RUN-10, lectura por membresía y restricción de metadatos. RUN-20, AVI-05 abre lote/producto de la respuesta asignada, queda no leído, cierra sin GET adicional ni POST. | AVI-05 E2, aislado con doble. Pendiente ver el mismo límite con navegador/API/PostgreSQL y revocación efectiva. |
| Acceso/estructura | RUN-10/13, último Owner y membresía persistida. RUN-19, sesión sintética/negativas. RUN-20, revalidación de identidad, licencia, membresía, horario y sucursal con dobles. | Reclamo, expiración, cambios de membresía y reentrada reales combinados con operación pendiente. |
| Plataforma | RUN-10, autoridad separada y constancia del operador. RUN-20, un 503 de lista no oculta la revocación 403 en DOM. | Falta E4 de acciones de plataforma con identidades sintéticas separadas. Purga real fuera de alcance. |
| Contingencia | RUN-10, constancias/conflictos persistidos. RUN-20, outbox aborta al desmontarse y conserva ámbito, sin mezclar otra sede. Pérdida de acceso al esperar lote bloquea escritura en aplicación con dobles. | INT-16 E1/NOT_RUN. Arranque aislado con PostgreSQL/credenciales sintéticas, PWA real, reconciliación offline y servicios externos pendientes. |

**Ejecuciones focalizadas.**

- Vitest en `frontend/`. `npm.cmd run test -- <caracterización AVI-05> src/features/transfers/TransfersPage.test.tsx src/features/reporting/ReportsPage.test.tsx src/features/platform/PlatformPage.test.tsx src/features/offline/WorkspaceOutboxSync.test.tsx -t 'opens and closes the assigned alert|persistent event|allows only actual endpoints|cancels old report reads|503 list hide|keeps original scope|different branch with the prior scope'`. **6 PASS, 0 FAIL, 50 excluidos por filtro**. La caracterización temporal añadió un solo caso en `src/features/notifications/Rfa2Avi05.temporary.test.tsx`. Reutilizó fixtures y añadió un aviso de otra sede, clic en detalle, lote/producto, estado no leído, cierre y conteo de GET/POST. Primer intento diagnóstico **1 FAIL** del selector exacto, que no encontró texto compartido con la fecha en el mismo párrafo. El selector por fragmento conserva la misma expectativa observable. Se retiró solo ese archivo temporal.
- .NET con dobles. Build de UnitTests y ejecución del runner en proceso con `-filterVSTest` por `BranchLookupCannotAuthorize`, `RevalidatesIdentityLicense`, `SourceAndDestinationAssignments`, `AccessLostDuringLotWait`, `OwnerMembershipIsRevalidatedAfterTheReportRead` y `AStoreReturningRowsOutsideTheAuthorizedBranch`. **17 PASS, 0 FAIL, 0 SKIP**. Sin conexión a BD.
- Arnés. El intento `dotnet restore tests/MediPOS.E2E/MediPOS.E2E.csproj --locked-mode` devolvió 1 localmente sin diagnóstico útil en la salida capturada. Se usaron los assets ya disponibles para `dotnet build tests/MediPOS.E2E/MediPOS.E2E.csproj --no-restore -p:BaseOutputPath=obj/b02-bin/ -m:1 --disable-build-servers`. Build PASS con 0 errores/advertencias. El workflow conserva restore bloqueado, preflight y gates. YAML cotejado con HEAD. El awk real aceptó **52** marcadores sintéticos y descartó **8** entradas no permitidas. P5 instrumentado **NOT_RUN** localmente.

**Diagnóstico de un único run posterior.** La salida admite `B02 P5 DIAG <ESCENARIO> START|PASS` y `B02 P5 DIAG <ESCENARIO> FAIL <PUNTO>` para pares enumerados. Los escenarios son SEED, SEARCH, CREATE, EDIT, ASSOCIATION, IMPORT y PERSISTENCE. El último START sin PASS y el par FAIL identifican la primera frontera y el bloque del test. El arnés relanza la excepción original. El resumen publica exclusivamente las líneas saneadas. El gate previo P0–P5 decide el resultado.

**Siguiente acción.** El operador publica el candidato en `ci/postgres-diagnostics-8bc6b4c`. El push activa **B-02 isolated browser pilot** una sola vez. Entregar el extracto de `Run preflight and P0–P5` en el resumen del run con URL/attempt, SHA/build, versión de PostgreSQL, marcadores P0–P5 y DIAG. Si falta un FAIL diagnóstico, registrar la última frontera alcanzada. Conciliar el extracto antes de dar cierre a RFA2. Frontend integral 645/646, E5 pendiente y producción NO-GO.

<a id="run-19"></a>
## RUN-19 · B-02 del candidato L-06B

[CI 45938999412](https://github.com/saintdeyb321/MediPOS/actions/runs/45938999412), job 140308706593, intento 1, 183 segundos. Extracto sanitizado suministrado por el operador. SHA `52a5ced168dbef2e80145b4e9d115113f0a63755`, build `e40191caae1b9b7295a27b34b2b3c9489d4a61cc1484178ae3d73c17d0985203`, PostgreSQL 18.7 y rol `medipos_test_runtime`. Conclusión **failure**, código de salida **1**.

| Frontera | Resultado e impacto en la matriz |
|---|---|
| P0–P4 | PASS. Corrobora los recorridos ya vinculados en [RUN-15](#run-15) y [RUN-17](#run-17). Se conservan los límites por aserción y rol. |
| P5 `CATALOG_SEARCH_WRITE_IMPORT` | START sin PASS. Aserción fallida desconocida y sin artefacto recuperable. **FAIL del piloto**. Sus aserciones individuales son **no acreditadas**. |
| L-06B | Mantiene la evidencia local de [RUN-18](#run-18). No se promueve ningún UC de Catálogo por P5 ni se atribuye un fallo al producto, a F-003 o a F-007. |

La matriz conserva 156 UC. Las variantes de CAT-19 con writer drain, recuperación de altas sin la sesión original y E5 siguen pendientes. El gate frontend integral de RUN-13 (645/646) es independiente. Fase RFA2 parcial, correcciones suspendidas y producción NO-GO.

<a id="run-18"></a>
## RUN-18 · L-06B candidato

Base `5a94de91a17592d5d3739426db8ec08223976758`. RUN-04 caracteriza la falsa ausencia de un producto que estaba en la página 2. El primer RED del candidato falla porque la vista solo ofrecía filtro por página. El GREEN exige un GET filtrado de offset cero, coincidencia, estado/categoría, vacío, limpieza y cero mutaciones. F-007 se reproduce por separado. El RED permite confirmar tras recarga con otra ficha. El GREEN conserva el código original y mantiene bloqueada la escritura hasta revisar el producto correcto.

| Control | Comando / selección | Resultado |
|---|---|---|
| Vitest focalizado | En frontend, `npm.cmd run test -- src/features/catalog/ProductsPage.test.tsx src/features/catalog/api.test.ts src/features/catalog/CatalogDirtyState.test.tsx src/features/product-import/ImportPage.test.tsx` | 47 PASS, 0 FAIL, 0 SKIP. E2 con dobles. Reentrada limpia verificada después con un PASS adicional del mismo caso de recuperación. |
| .NET traducción | Runner xUnit en proceso, `-filterVSTest 'FullyQualifiedName~CatalogDirectorySqlTests'` | 1 PASS. Npgsql traduce el ingrediente, escape literal, ámbito, predicados, orden y página. SQL generado offline. |
| .NET HTTP | Mismo runner, `-filterVSTest 'FullyQualifiedName~CatalogDirectoryBindsOptionalFiltersAndDiscardsResultsWhenAccessChanges'` | 1 PASS. Loopback temporal con dobles, arreglo compatible, filtros normalizados, seis 400 y 403 al revocar durante la lectura. |
| Builds y lint | `dotnet build <proyecto UnitTests o E2E> --no-restore -p:BaseOutputPath=obj/b02-bin/ -m:1 --disable-build-servers`. `npm.cmd run build -- --outDir node_modules/.cache/medipos-l06b-build` y ESLint de los cinco archivos TS/TSX modificados. | PASS. Vite mantiene el aviso de chunk mayor de 500 kB. |
| Reconciliación estática | YAML con PyYAML, comparación contra el workflow del baseline, matriz/fichas y `git diff --check`. | PASS. Controles P0–P4 preservados, START/PASS de P5 obligatorios y 41/41 fichas CAJ/CAT alineadas. |
| P5 real | [Guía B-02](../../../backend/tests/MediPOS.E2E/README.md) y workflow `b02-browser-pilot.yml` | NOT_RUN, Docker ausente. Exige START y PASS `CATALOG_SEARCH_WRITE_IMPORT` además de P0–P4. |

| UC | Decisión | Evidencia y límite |
|---|---|---|
| CAT-01 | KEEP | Lectura operativa por sucursal. P5 exige que Cajero no vea el inactivo. Stock de sede conserva evidencia previa. |
| CAT-02 | KEEP | Categorías operativas conservan admisión ProductSearch. Sin variante nueva de navegador. |
| CAT-03 | KEEP | Corrección local revisada y auditada. P5 prepara 409 por versión y referencia global, mismo ID y auditoría. Snapshots históricos mantienen evidencia anterior. |
| CAT-04 | KEEP | DOM distingue error de categorías y bloquea alta inválida. P5 carga y elige una categoría real. |
| CAT-05 | KEEP | DOM verifica directorio global vacío y asociación. P5 carga la referencia sintética existente. |
| CAT-06 | FIX | FR-CAT-010, HTTP E2 y traducción Npgsql offline; P5 prepara ingrediente, literal, inactivos, páginas, roles, dos tenants y RLS efectivo. E3 anterior corresponde al listado sin filtros. |
| CAT-07 | KEEP | DOM protege ante una respuesta tardía de otra ficha. P5 exige relectura del mismo ID tras edición. |
| CAT-08 | KEEP | RUN-17 P4 E4 busca por código y elige presentación con stock real. Variantes de ingrediente y disponibilidad conservan sus pruebas anteriores. |
| CAT-09 | FIX | Alta conserva código y bloqueo tras recarga. DOM exige exactitud y un solo POST. P5 pierde el acuse tras el commit y coteja la única fila. |
| CAT-10 | KEEP | DOM verifica asociación y vínculo. P5 coteja 201, ID global, auditoría y ausencia de stock. |
| CAT-11 | KEEP | E2 conserva el literal minorista 1.2345 y el mayorista nulo. P5 coteja 2.5678/1.2345 y auditoría vía HTTP/PostgreSQL. |
| CAT-12 | KEEP | DOM conserva comportamiento ante 403 de estado. P5 comprueba la desactivación vía HTTP y fila persistida. |
| CAT-13 | KEEP | DOM usa la versión que abrió el editor. P5 requiere GET autoritativo de configuración. |
| CAT-14 | KEEP | DOM protege edición y versión tras 409/pérdida. P5 compara persistencia, factor 10.125 y 409 de la revisión vieja. |
| CAT-15 | KEEP | Ruta legacy fuera del editor. P5 prepara no-op y 409 al intentar un cambio efectivo mediante HTTP. |
| CAT-16 | KEEP | DOM descarga el archivo sin POST. P5 descarga la plantilla oficial de la API aislada. |
| CAT-17 | KEEP | DOM conserva constancia y un envío. P5 prepara XLSX real, 22 filas, 1 importada y 21 inválidas. |
| CAT-18 | KEEP | DOM conserva job y cursor. P5 recupera tras perder el acuse, pagina y verifica 403 cruzado. |
| CAT-19 | VALIDATE | DOM y fixtures previos preservados. Variante real de recuperación interrumpida BLOCKED por gate de writer drain. |
| CAT-20 | FIX | RED/GREEN verifica filtro aplicado a todo el catálogo, reset, vacío y compatibilidad de paginación. P5 pendiente. |
| CAT-21 | FIX | RED/GREEN tras reentrada: otra ficha no habilita la conciliación. El código guardado habilita verificar el original sin repetir el POST. Un alta antigua sin referencia verificable requiere soporte. |
| CAT-22 | N/A | Desactivar conserva la historia y no se expuso borrado físico. |

La traducción offline y los dobles acotan el candidato local. P5 aún debe demostrar las aserciones contra PostgreSQL real. La aceptación E5 y el gate frontend completo siguen pendientes. Conservar RUN-17 de L-06A por SHA y la falta de atribución en DEV.

<a id="run-17"></a>
## RUN-17 · Gate de L-06A y entrada de L-06B

[CI B-02 45935935713](https://github.com/saintdeyb321/MediPOS/actions/runs/45935935713), job 140299036413, intento 1, 199 s. El operador confirmó GO y suministró el extracto sanitizado de todos los pasos SUCCESS. SHA `5a94de91a17592d5d3739426db8ec08223976758`, manifiesto `d8375f0dc06b550c21627bf0f60c215e1c43d86088b2ce2d8bb3de8331f86dd0`, PostgreSQL 18.7 (180007), origen HTTPS efímero y `medipos_test_runtime`. P0–P2 PASS, P3 `CASH_HISTORY_AND_RECOVERY` PASS y P4 `POS_PAY_TICKET_VOID` PASS.

| UC | Aserciones acreditadas en el SHA del run |
|---|---|
| CAJ-01/08/09 | P4 crea un borrador desde Cobrar, verifica vendedor, caja y versión y reentra a la venta por su ID. Confirma que el mismo ID se vuelve a consultar tras perder el acuse de confirmación. La pérdida del acuse al crear el borrador conserva evidencia anterior. |
| CAJ-02/03/04 | P1 mantiene apertura por UI y GET 200/open/null, con negativas. P4 abre 10.0001 desde móvil, lee el mismo importe inicial vía GET de caja propia y acredita rechazo de apertura duplicada. |
| CAJ-05/06/07/16/17 | P3 verifica el arqueo real 100→10 y −90, fila y auditoría únicas, ausencia en Activas, acuse perdido después del commit, un solo cierre desde UI y rechazo de una segunda escritura explícita. Recupera en la URL original tras recarga y por Cierres desde entrada limpia, con el mismo ID. |
| CAJ-10/14 | P4 busca por código, elige la presentación con teclado y agrega con touch. Comprueba base 10, total 21.25 y 409 con revisión anterior. El vaciado y la protección al salir tienen evidencia DOM de RUN-16. |
| CAJ-11/18 | P4 cobra 5.0001 en efectivo y 16.2499 en Yape, verifica ambos pagos persistidos y stock 25→15, pierde el acuse después del commit y recupera la venta sin reenviarla. La elección de otro vendedor sigue pendiente. |
| CAJ-12/15 | P4 recupera el ticket confirmado y luego anulado. La acción de impresión llama una vez a la función del navegador sin escribir y también permite volver sin imprimir. La impresión física queda pendiente. |
| CAJ-13 | P4 anula y verifica dos reversiones de pagos, retorno de 10 al mismo lote, reversión ligada al movimiento original, caja neta 10.0001 y auditorías por actor. La lectura de la venta desde otro tenant devuelve 403. |
| CAJ-19 | P3 valida vacío histórico, rango 400, tenant ajeno 403 y acceso al arqueo desde Cierres. P4 acredita cierres propios del Cajero, exclusión de sede no asignada y páginas diferentes para Owner. Pharmacist y demás variantes del calendario siguen pendientes. |

P0–P2 refuerzan las aserciones de RUN-15 de sesión/aislamiento y Sencillo Owner. Los resultados de P3/P4 acreditan L-06A para el alcance ejecutado y habilitan L-06B. Gate integral frontend 645/646, incidente DEV sin atribución, variantes restantes y aceptación del operador preservados.

<a id="run-16"></a>
## RUN-16 · L-06A candidato

Base `c26ee5090ebbc2d48a27bb25b8817554f6ec7adf`. RED focalizado en `PosPage.test.tsx` confirmó que vaciar un borrador dejaba el registro dirty en false. GREEN con la protección local corregida. Las dos pruebas de descarte de Caja verifican una confirmación tras editar, cierre ordinario sin cambios y apertura confirmada. El cierre del diálogo de apertura se verifica después de limpiar su marca, pues la caja puede aparecer abierta durante la transición.

| Verificación | Comando / selección | Resultado |
|---|---|---|
| Vitest RED / GREEN | Desde frontend, `npm.cmd run test -- src/features/pos/PosPage.test.tsx -t 'protects removal of the last persisted draft line'`, después `npm.cmd run test -- src/features/cash/CashPage.test.tsx src/features/pos/PosPage.test.tsx`. | RED 1 FAIL esperado, 42 excluidos. GREEN 57 PASS, 0 FAIL, 0 SKIP. Dos caracterizaciones de diálogos agregadas después, 1/1 cada una por filtro. Tras pasar el detalle histórico a solo lectura, 2/2 comprobaciones focalizadas. |
| HTTP con dobles | xUnit en proceso sobre `MediPOS.UnitTests.dll` con `-filterVSTest` para `RealCookieOnboardingPurchaseMixedSaleTicketCashCloseAndOwnerReportUseExistingHandlers` y `EveryPublishedTenantRouteRequiresNativeAuthenticationAndNoOtherDomainIsMapped`. | 2 PASS. Historial, rango 400, auditoría inalterada y 401 anónimo. |
| Aplicación con puertos dobles | `dotnet tests/MediPOS.UnitTests/obj/b02-bin/Debug/net10.0/MediPOS.UnitTests.dll -filterVSTest "FullyQualifiedName~ClosedDirectory"`. | 4 PASS. Lima semiabierto, alcance por rol y denegación tras revocar la asignación durante lectura. |
| Compilación y análisis | `dotnet build tests/MediPOS.E2E/MediPOS.E2E.csproj --no-restore -p:BaseOutputPath=obj/b02-bin/ -m:1 --disable-build-servers` y mismo comando para `tests/MediPOS.UnitTests`. ESLint de archivos tocados y `npm.cmd run build -- --outDir node_modules/.cache/medipos-l06a-build`. | PASS. Vite advierte un chunk mayor de 500 kB. |
| Formato y workflow | `dotnet format whitespace --verify-no-changes --no-restore --include <archivos C# modificados>`, lectura YAML y cotejo de alcance con el baseline, `git diff --check`. | PASS. Los cuatro archivos C# editados que quedaron con CRLF se guardaron en LF, como sus blobs del baseline. |
| PostgreSQL y navegador del candidato | [Guía](../../../backend/tests/MediPOS.E2E/README.md), workflow B-02 con P3 `CASH_HISTORY_AND_RECOVERY` y P4 `POS_PAY_TICKET_VOID`. | NOT_RUN. Docker ausente. P0–P2 conservados. |

| UC | Decisión | Evidencia o aserción |
|---|---|---|
| CAJ-01 | KEEP | DOM lee la venta original tras acuse perdido. P4 exige el mismo ID confirmado. |
| CAJ-02 | KEEP | P1 histórico abre desde UI; P4 añade apertura decimal y rechazo duplicado. |
| CAJ-03 | KEEP | Lectura actual existente; P4 exige importe inicial exacto por sesión. |
| CAJ-04 | KEEP | P1 histórico 200/open/null con negativas por sede y tenant. |
| CAJ-05 | KEEP | P3 histórico arqueo autoritativo por ID. P3 candidato lo abre desde historial. |
| CAJ-06 | KEEP | P3 histórico 100→10 y conflicto al repetir. DOM verifica edición, cierre y protección. |
| CAJ-07 | KEEP | P3 histórico excluye cerradas de Activas y P3 candidato conserva esta aserción. |
| CAJ-08 | KEEP | Vitest recupera la constancia del borrador original. P4 cuenta una sola venta. |
| CAJ-09 | KEEP | DOM recupera líneas/versiones tras recarga. P4 exige lectura del borrador real antes del cobro. |
| CAJ-10 | FIX | RED/GREEN para vaciar borrador sin perder la edición local al salir; P4 exige 409 con versión anterior. |
| CAJ-11 | KEEP | Vitest cubre cobro incierto y una sola escritura. P4 induce pérdida tras commit real y controla efectivo/Yape y stock. |
| CAJ-12 | KEEP | DOM y E3 existentes. P4 identifica venta, presentación y estado en ticket. |
| CAJ-13 | KEEP | Reversión existente y negativa DOM. P4 exige dos reversiones de pago, restitución de lote y auditoría. |
| CAJ-14 | KEEP | Vitest cubre búsqueda, selección y edición. P4 prepara teclado y touch en viewport de 390 px. |
| CAJ-15 | KEEP | DOM y P4 verifican imprimir opcional y volver sin imprimir. P4 intercepta solo la llamada local a impresión y comprueba cero escrituras al usarla. |
| CAJ-16 | VALIDATE | Vitest verifica GET fallido/open/closed tras pérdida. P3 candidato pierde acuse de un cierre confirmado y verifica arqueo sin segundo POST UI. |
| CAJ-17 | FIX | Brecha del baseline en RUN-15. Vitest verifica entrada limpia a Cierres, arqueo por ID y cero POST. P3 real pendiente. |
| CAJ-18 | KEEP | Vitest distingue vendedor de la membresía, montos y pagos. P4 verifica vendedor autorizado y el efectivo neto tras anulación. |
| CAJ-19 | FIX · capacidad aditiva | GET Cierres por sede, membresía, días de Lima, orden y página. HTTP con dobles y DOM PASS. P3/P4 deben verificar la consulta real. |

Variantes físicas de impresión, vistas adicionales y aceptación del operador siguen pendientes. Sin cambios en lógica financiera. Estado `L-06A_CANDIDATO`, gate frontend integral pendiente y RFA2 parcial.

<a id="run-15"></a>
## RUN-15 · B-02 acreditado del baseline

[CI 45933239638](https://github.com/saintdeyb321/MediPOS/actions/runs/45933239638), intento 1, push, job browser 140290452309, todos los pasos PASS (180 s). SHA `c26ee5090ebbc2d48a27bb25b8817554f6ec7adf`, manifiesto `fd2acf2f5191c7b7d25baae58e8c4c006db56209e5ca4c1135d3cf636cb83c23`, PostgreSQL 18.7 (180007), runtime `medipos_test_runtime`, puerto efímero 43297. Evidencia saneada del MD del operador.

| Fase | Aserción del SHA ejecutado |
|---|---|
| P0 PASS | React compilado, ACC-15/17 identidad sintética y directorio, INT-02 readiness, 401 sin sesión, 403 de otro tenant, CSRF 400 y cero cajas en ambos tenants. |
| P1 PASS | CAJ-02 apertura por UI y CAJ-04 HTTP 200 con ID/sede/membresía, open y JSON null, fila real y negativas. |
| P2 PASS | TRA-01/03/04/13 rechazo de fondos sin efecto, despacho de 1.0001, acuse perdido después del commit, GET de la constancia, sesión destino distinta, una recepción, detalle y dos auditorías. LEDGER verificado por cierre API. |
| P3 PASS | CAJ-05/06/07/17 cierre 100→10 y diferencia −90, fila y auditoría única, ausencia en activas, reconsulta por URL, segundo cierre rechazado. `F001_CLEAN_ENTRY_GAP_OBSERVED` acredita la brecha de descubrimiento en la entrada limpia del baseline. P3 en este SHA recibió el acuse normal. |

B-02 validado para estos escenarios y SHA. L-01 acredita corrección E4 para Owner, con variantes restantes por rol y gate general pendientes. F-001 demuestra persistencia y la brecha en navegador aislado; incidente DEV sin atribuir. La matriz alcanza 156 casos con el nuevo UC-CAJ-19. 3 E1, 33 E2, 111 E3 y 9 E4. 19 PASS, 135 BLOCKED, 1 NOT_RUN y 1 N/A. El FAIL histórico de Sencillo conserva su evidencia E2 y su variante Owner ya verificada pasa a BLOCKED por alcance residual. Frontend conserva 645/646 de RUN-13.

<a id="run-14"></a>
## RUN-14 · B-02 · Candidato local

Baseline `781277c0032e8d4f364893191137254b3e024c2e`. Se implementó [un proyecto E2E opt-in](../../../backend/tests/MediPOS.E2E/README.md) fuera de la solución principal. Reutiliza la API con sus guardas reales y el fixture PostgreSQL 18. El workflow manual separado prepara Chromium y registra SHA, manifiesto, motor, origen, rol y fases.

| Control | Resultado local |
|---|---|
| NuGet | `dotnet restore tests/MediPOS.E2E/MediPOS.E2E.csproj --locked-mode`, PASS con lockfile. |
| Compilación | `dotnet build tests/MediPOS.E2E/MediPOS.E2E.csproj --no-restore -p:BaseOutputPath=obj/b02-bin/ -m:1 --disable-build-servers`, PASS, 0 errores y 0 advertencias. |
| Preflight local | Runner xUnit en proceso, filtro `FullyQualifiedName~MediPOS.E2E.BrowserPreflightTests`. 9 PASS / 0 FAIL / 0 SKIP. Rechazo de overrides ambientales y admisión de entradas propias. |
| React | `npm.cmd run build -- --outDir node_modules/.cache/medipos-b02-build`, PASS. Vite avisó que un chunk superó 500 kB después de minificar. |
| Runner manual | YAML, límites y permisos revisados localmente. Ejecución GitHub pendiente. Se requiere registro en rama por defecto para dispatch, o la alternativa de runner aislado de la guía. |

| Piloto implementado | UC y frontera | Estado |
|---|---|---|
| P0 | ACC-15/17, INT-02, sesión real con identidad sintética, React compilado, readiness, 401/403 y CSRF, cero cajas creadas en ambos tenants | NOT_RUN |
| P1 | CAJ-02/04, apertura desde UI, 200/open/null de la misma caja y fila real, rechazo por sede/tenant | NOT_RUN |
| P2 | TRA-01/03/04/13, fondos insuficientes sin efecto, acuse perdido inducido, constancia original, única recepción y saldos auditados | NOT_RUN |
| P3 | CAJ-05/06/07/17, cierre 100→10, −90, rechazo de segundo cierre, una auditoría, reentrada con/sin URL | NOT_RUN |

Docker no está instalado en el host de esta sesión. El piloto necesita el entorno conjunto para iniciar y no aporta una ejecución E3/E4 nueva. Se conservan los niveles/veredictos de la matriz y los resultados de RUN-13. El FAIL de Compras permanece separado, sin resultado de reejecución suministrado. Estado `B02_CANDIDATO_PENDIENTE_RUNNER`.

<a id="run-13"></a>
## RUN-13 · CI y diagnóstico local de Compras

[Run 45915477001](https://github.com/saintdeyb321/MediPOS/actions/runs/45915477001), intento 1, `e2ffd12f577234c943237e71361a8ee0716a8eb8`, rama `ci/postgres-diagnostics-8bc6b4c`. Importado del extracto saneado del encargo. Duración 18 min 58 s. El run terminó en failure.

| Job | Evidencia suministrada |
|---|---|
| Backend 140233528093 | PASS. Restore/audit, build, diagnóstico, formato, contenedores, publicación y limpieza completados. 2694 unitarias, 8 arquitectura y 907 integraciones PASS, 0 FAIL y 0 SKIP. PostgreSQL 18.7. |
| Frontend 140233527873 | Build y lint PASS. 645 PASS, 1 FAIL, 0 SKIP. Test y publicación del gate fallidos. Identificado `PurchasesPage.test.tsx:82`, caso de confirmación con revisión obsoleta. La aserción fallida no figura en el resumen. |

El paso PostgreSQL ejecuta el proyecto de integración completo sin filtro. Las clases `CashClosePersistenceTests` (43/43) y `CashSessionPersistenceTests` (9/9) incluyen los siguientes métodos en ese SHA.

- **F-001, UC-CAJ-05/06/07, E3.** `ClosingEmptyCashFromOneHundredToTenPersistsShortageAndIsRecoverableById` verifica esperado 100, contado 10, diferencia −90, cero ventas/pagos/préstamos, fila cerrada y exactamente una auditoría de cierre con actor e ID. Un segundo cierre recibe `AlreadyClosed`. Desde otro scope autorizado, el cierre desaparece de activas y el arqueo por ID coincide con el acuse. El incidente DEV y la reentrada en navegador sin URL siguen pendientes.
- **L-01, UC-CAJ-04, E3 de aplicación.** `OpenStatusReturnsPersistedIdentityAndNullClosingExpectedAmount` abre una caja sintética, cambia de scope y observa el mismo ID/sede/membresía, estado open, monto inicial, cierre nulo y esperado nulo vía `StatusAsync`. RUN-12 acredita HTTP 200 y JSON null con dobles. El GET respaldado por PostgreSQL dentro de un mismo recorrido y la aceptación E4 siguen pendientes. `L01_CANDIDATO_PENDIENTE_CONTRATO_REAL`.
- **UC-COM-11, E3 de aplicación.** El paso integral incluye `ConcurrentLineEditorsCannotConfirmAReviewWhoseLinesWereReplaced`. Una edición gana, la confirmación con revisión vieja recibe `purchasing.receipt_changed` y no se crean lotes ni movimientos. Esta aserción cubre la precondición del backend, mientras el FAIL CI ocurre en una prueba DOM.
- **F-006.** Los dos casos modificados en L-05 pasaron en este run. El único FAIL identificado en RUN-13 es de Compras. El nombre del fallo de RUN-10 sigue desconocido y los cinco fallos de RUN-12 permanecen en el histórico.

### Reproducción local de Compras · NOT_REPRODUCED

En frontend, `npm.cmd run test -- src/features/purchasing/PurchasesPage.test.tsx -t 'rejects confirmation under the server precondition'` obtuvo 1 PASS, 0 FAIL, 26 excluidos por filtro. Inmediatamente después, el mismo comando sin `-t` obtuvo 27 PASS, 0 FAIL, 0 SKIP. Sin compilaciones simultáneas. Ambas ejecuciones usan dobles HTTP.

El test carga el borrador, espera productos válidos y cambia las líneas en el doble tras la revisión. `operations.tsx` compara `expectedRevision` con la revisión nueva y responde 409. La prueba exige un único POST, borrador intacto y botón deshabilitado al observar el error. `usePurchaseReceipt.run` conserva busy durante el GET de conciliación, bloquea otro envío y lee la misma compra. Tras cargar la revisión vigente, el botón puede habilitarse para una nueva acción explícita. Existe la hipótesis de que el error y el estado del botón se observaron en renders distintos. El resumen suministrado no distingue esa hipótesis de un fallo de preparación o producto. Se conserva el test sin cambios y se requiere la aserción fallida saneada para atribuir la causa.

**Gate.** RFA2 sigue parcial. 155 UC: 3 E1, 33 E2, 119 E3, 0 E4. Se conservan los veredictos acumulados (19 PASS, 1 FAIL histórico del baseline de Sencillo, 133 BLOCKED, 1 NOT_RUN, 1 N/A). Los PASS de CI y de la prueba local de Compras describen fronteras distintas del gate comercial. [Cobertura futura y habilitador B-02](ROADMAP.md#l-06--aceptación-funcional-por-familia).

<a id="run-12"></a>
## RUN-12 · RFA4 / L-05, L-01 y F-001

Baseline `bc36ecc5b7b9e2e0cd4c52d6c8b9f00ab7790b76`. Resultados locales del candidato.

| Verificación | Selección y resultado |
|---|---|
| Vitest E2 · F-006 | Desde frontend, `npm.cmd run test -- src/features/transfers/TransfersPage.test.tsx src/features/platform/PlatformPage.test.tsx`. Antes: **44 PASS / 0 FAIL / 0 SKIP**. Primera corrida posterior, con compilación y comprobaciones simultáneas: **39 PASS / 5 FAIL / 0 SKIP**. Ambos casos modificados pasaron. Fallaron otros casos de mercadería multilote, revocación 403 con lista 503, alta con Gmail y dos de recuperación de alta. Repetición acotada sin compilación simultánea, mismo comando: **44 PASS / 0 FAIL / 0 SKIP**. La causa de los cinco fallos no queda atribuida y el fallo histórico de CI sigue sin nombre. |
| ESLint y tipos | ESLint de ambos archivos de pruebas y `tsc.cmd -b`: PASS. |
| HTTP E2 · L-01 | `dotnet tests/MediPOS.UnitTests/obj/rfa4a-bin/Debug/net10.0/MediPOS.UnitTests.dll -filterVSTest "FullyQualifiedName~RealCookieOnboardingPurchaseMixedSaleTicketCashCloseAndOwnerReportUseExistingHandlers"`. Primer intento: socket loopback bloqueado antes de iniciar sesión. Ejecución autorizada del mismo test: **1 PASS / 0 FAIL / 0 SKIP**. HTTP 200, ID caja/sede/membresía correctos, open y propiedad JSON null presente. Persistencia con dobles. |
| Compilación .NET | `dotnet build tests/MediPOS.UnitTests --no-restore -p:BaseOutputPath=obj/rfa4a-bin/ -m:1 --disable-build-servers` y mismo comando con `tests/MediPOS.IntegrationTests`. Ambos PASS, cero errores y advertencias. |
| Diagnóstico CI | YAML válido. Comparación estructural igual al retirar el cuerpo del único paso editado. Python ejecutó ambos cuerpos sobre 11 entradas sintéticas con igual código de salida y veredicto: aprobado, caso y suite fallidos, informe inválido, nombre dinámico y hostil, ruta externa, ruta con controles, archivo sin rastrear, truncamiento y lint fallido con pruebas aprobadas. La salida excluyó detalles dinámicos, mensajes de fallo y rutas absolutas. |

**F-006.** Transferencias mantiene pendiente el GET de origen hasta comprobar el bloqueo, espera el fin de la verificación y comprueba nuevamente el botón deshabilitado. Plataforma mantiene pendiente la ficha del destinatario hasta observar carga, conserva los conteos exactos y espera el correo y su acción habilitada antes de salir del panel. Ambos casos conservan cero escrituras donde corresponde.

**PostgreSQL NOT_RUN.** Docker ausente. En CI del candidato deben ejecutarse `OpenStatusReturnsPersistedIdentityAndNullClosingExpectedAmount` y `ClosingEmptyCashFromOneHundredToTenPersistsShortageAndIsRecoverableById`. Este último caracteriza acuse, fila, diferencia −90, auditoría única, rechazo de segundo cierre, ausencia en activas y consulta del arqueo por ID en scope independiente. Sin atribución al incidente DEV. La comprobación HTTP contra PostgreSQL y E4 siguen pendientes según [RUN-11](#run-11).

<a id="run-11"></a>
## RUN-11 · RFA4A / L-01 · Candidato Sencillo

Baseline `50b97a2c451a15ac59a21d06c11d266b81a77a68`. Vitest 5.0.4 en jsdom con dobles HTTP. La verificación del candidato se registra aparte de los veredictos del baseline y de RUN-10. Docker no estaba en PATH ni en su ubicación estándar.

| Verificación | Comando o selección | Resultado |
|---|---|---|
| RED | Desde frontend, `npm.cmd run test -- src/features/transfers/CashTransfersPage.test.tsx -t 'dispatches once from a selected open cash'` | 1 FAIL esperado en `toBeEnabled()`, 13 excluidas por filtro. Caja abierta, esperado null, importe 1.0001 y destino válido. |
| GREEN | Desde frontend, `npm.cmd run test -- src/features/transfers/CashTransfersPage.test.tsx src/features/transfers/cashApi.test.ts src/features/transfers/recovery.test.tsx` | 24 PASS, 0 FAIL, 0 SKIP. La primera pasada GREEN detectó dos problemas en la preparación de las pruebas: un límite del doble afectaba el caso de máximo decimal y la aserción de URL se adelantaba a la navegación. El límite quedó particularizado al caso de falta de fondos y la aserción espera la transición sin reducir cobertura. |
| Lint y tipos | ESLint local sobre `CashTransfersPage.tsx`, `CashSelection.tsx`, `CashTransfersPage.test.tsx`, `cash.test.support.tsx` y `access/errors.ts`. `tsc.cmd -b` desde frontend. | PASS. |
| Compilación de integración | Desde backend, `dotnet build tests/MediPOS.IntegrationTests --no-restore -p:BaseOutputPath=obj/rfa4a-bin/ -m:1 --disable-build-servers` | PASS, 0 advertencias, 0 errores. |

**Verificado en E2.** Un POST con importe exacto y clave pese al doble clic, seguido de lectura. Cero POST para caja en consulta, cerrada, ajena o con error, ausencia de caja, importe inválido y falta de destino válido. Se conservan las negativas por rol y membresía. El rechazo 409 `cash_transfer.insufficient_cash` conserva los campos editables y no reenvía. La pérdida de acuse conserva la clave original ante GET insuficiente y concilia sin otro POST. La recepción completa verifica un único envío y estado terminal, también tras perder el acuse. El rechazo de fondos es simulado en esta verificación.

**Contrato pendiente.** Para el fixture PostgreSQL 18 aislado, desde backend: `dotnet test tests/MediPOS.IntegrationTests --filter FullyQualifiedName~OpenStatusReturnsPersistedIdentityAndNullClosingExpectedAmount`. Esta prueba abre una caja sintética y lee por `StatusAsync` en un nuevo scope con autorización del runtime. Afirma ID, sede, membresía, monto inicial, estado open y nulos de cierre. Se compiló y queda NOT_RUN sin Docker. El fixture disponible compone servicios de aplicación con PostgreSQL, y el encargo deja pendiente la frontera HTTP completa.

En un entorno aislado autorizado con API, PostgreSQL y sesión sintética, abrir caja y consultar como su operador `GET /api/tenants/{tenantId}/branches/{branchId}/cash-sessions/{cashSessionId}`. Verificar HTTP 200, IDs de fixture, `status: open` y presencia de `expectedCashAmount: null` en JSON. Si difiere, detener la aceptación de la guarda y comprobar versión servida y serialización. E3 para la nueva prueba, HTTP real, E4 y CI del candidato siguen pendientes.

<a id="run-10"></a>
## RUN-10 · Conciliación de 02B

26/11/2026. [Resumen entregado por el operador](evidence/CI_RUN_45891472332_SANITIZED.md), [ejecución 45891472332](https://github.com/saintdeyb321/MediPOS/actions/runs/45891472332), intento 1. Rama `ci/postgres-diagnostics-8bc6b4c`, SHA `5d3889806e11fea23a3ba19402c22c15635a1301` idéntico al checkout. Copia byte a byte de `docs/CI_RUN_45891472332_SANITIZED.md`, SHA-256 `67486107dcbc29406f6fbad27a41afac8448b03dac7f03f3500c2426f77865e2`. Se importó el resumen disponible sin consultar logs ni lanzar otra ejecución.

| Job / frontera | PASS | FAIL | SKIP | Comprobación |
|---|---:|---:|---:|---|
| Backend · unitarias | 2694 | 0 | 0 | Ejecución completa. Las pruebas HTTP con dobles aportan E2. |
| Arquitectura | 8 | 0 | 0 | Dependencias de código. |
| Integración | 905 | 0 | 0 | 99 clases únicas, suma independiente de sus filas SUITE. Servidor real 18.7 en CI. Las tres agrupaciones B8 son subconjuntos. |
| Frontend | 641 | 1 | 0 | Build y lint correctos, Vitest y publicación de evidencia fallidos. Nombre del test desconocido. |

Backend aprobado, workflow global fallido. RUN-01–09 siguen vigentes a sus respectivos alcances. Los contadores de tests de RUN-10 no se suman a ejecuciones anteriores como pruebas únicas. B-01 se levanta **solo** para aserciones PostgreSQL efectivamente cubiertas. B-02 sigue abierto para navegador–API–PostgreSQL; B-03 para servicios externos excluidos.

### Cruce por UC y aserción

Cada referencia identifica archivo y línea en el SHA del run. Las filas PostgreSQL entran por servicio, handler o lector con PostgreSQL aislado. Los tests que comparten datos preparatorios solo se atribuyen a las acciones efectivamente observadas. El nivel de la matriz es el máximo por frontera. Un PASS de handler no certifica el consumidor React; un BLOCKED con E3 retiene el resultado de persistencia.

| UC (prefijo UC- implícito) | Aserciones ejecutadas | Resultado y límites |
|---|---|---|
| CAJ 02,03 | [CashSessionPersistenceTests:48](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashSessionPersistenceTests.cs#L48) | Apertura 0.1234, un asiento de auditoría a nombre del usuario y lectura por membresía/sede. Otra membresía o sede obtiene vacío. |
| CAJ 05,06,07 | [CashClosePersistenceTests:60](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashClosePersistenceTests.cs#L60) | Activas antes y después, esperado = 100 + efectivo neto, contado 100.1234, diferencia, fila cerrada y actor. El arqueo por ID coincide con el acuse del handler. |
| CAJ 06 | [CashClosePersistenceTests:140](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashClosePersistenceTests.cs#L140), [CashCloseConcurrencyTests:27](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashCloseConcurrencyTests.cs#L27), [CashCloseIsolationTests:28](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashCloseIsolationTests.cs#L28) | Fallo de persistencia revierte el cierre. En carrera hay un ganador y una auditoría. Se ejercita la frontera de actor/sede. |
| CAJ 08,09,10 | [SaleDraftPersistenceTests:44](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/SaleDraftPersistenceTests.cs#L44), [SaleDraftPersistenceTests:156](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/SaleDraftPersistenceTests.cs#L156) | Creación y reemplazo calculan líneas con precio del servidor, se relee el borrador y dos editores sobre una versión producen un conflicto. |
| CAJ 11 | [SaleCheckoutPersistenceTests:43](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/SaleCheckoutPersistenceTests.cs#L43), [SaleCheckoutPersistenceTests:200](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/SaleCheckoutPersistenceTests.cs#L200) | Pago mixto, movimiento, saldo y auditoría se confirman juntos. Un fallo de escritura los revierte. |
| CAJ 12 | [InternalTicketPersistenceTests:25](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/InternalTicketPersistenceTests.cs#L25), [InternalTicketPersistenceTests:115](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/InternalTicketPersistenceTests.cs#L115) | Lectura del ticket mixto conserva sus líneas, pagos y posterior anulación incluso con caja cerrada. |
| CAJ 13 | [SaleVoidPersistenceTests:41](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/SaleVoidPersistenceTests.cs#L41), [SaleVoidPersistenceTests:111](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/SaleVoidPersistenceTests.cs#L111) | Anular venta multilote restaura consumo e historia mediante compensaciones. Fallo inyectado revierte la anulación. |
| TRA 01,02,03,04 | [CashTransferPersistenceTests:41](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashTransferPersistenceTests.cs#L41) | Despacho 50.1234 sin caja destino aún, lectura de pendientes por sede, dos auditorías, detalle recibido y desaparición de pendientes. Arqueos 50.8766 y 70.1234. TRA-01 conserva FAIL del botón en E2. |
| TRA 01,04 | [CashTransferConcurrencyTests:27](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashTransferConcurrencyTests.cs#L27), [CashTransferConcurrencyTests:82](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashTransferConcurrencyTests.cs#L82) | Reembolso y despacho compiten por el mismo efectivo. Dos despachos no lo sobregiran y la doble recepción tiene un ganador. |
| TRA 01,13 | [CashTransferRecoverabilityTests:28](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashTransferRecoverabilityTests.cs#L28), [CashTransferRecoverabilityTests:97](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashTransferRecoverabilityTests.cs#L97), [CashTransferRecoverabilityTests:123](../../../backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashTransferRecoverabilityTests.cs#L123) | Misma clave y origen concilian un solo débito. Un fallo revierte constancia, ledger y auditoría. Se verifica propietario de caja y privacidad. |
| TRA 05 | [CommercialDirectoryPostgreSqlTests:199](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L199) | Bandeja acotada a origen o destino, sin filas del otro tenant. |
| TRA 06,12 | [RecoverableOperationPostgreSqlTests:92](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/RecoverableOperationPostgreSqlTests.cs#L92), [RecoverableOperationPostgreSqlTests:144](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/RecoverableOperationPostgreSqlTests.cs#L144) | La intención se vincula al destino y al original. Fallar antes de guardar constancia revierte la solicitud. |
| TRA 07,08,10,11 | [TransferPersistenceTests:46](../../../backend/tests/MediPOS.IntegrationTests/Modules/Transfers/TransferPersistenceTests.cs#L46), [TransferIsolationTests:28](../../../backend/tests/MediPOS.IntegrationTests/Modules/Transfers/TransferIsolationTests.cs#L28) | Detalle de transferencias multilote, aprobación y traspaso autorizado, recepción completa/parcial, linaje, saldos y auditoría. Acciones de origen/destino según rol. |
| TRA 09 | [TransferPersistenceTests:132](../../../backend/tests/MediPOS.IntegrationTests/Modules/Transfers/TransferPersistenceTests.cs#L132) | Cancelar solicitada/aprobada conserva motivo y evento único sin mover stock. |
| CAT 03 | [RecoverableOperationPostgreSqlTests:312](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/RecoverableOperationPostgreSqlTests.cs#L312) | La corrección de medicamento local usa versión bloqueada y conserva auditoría original y existencias. |
| CAT 06,07 | [CommercialDirectoryPostgreSqlTests:120](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L120) | Directorio del tenant y unidades del detalle. Producto ajeno devuelve vacío. Filtro de página se observó aparte en DOM. |
| CAT 08 | [ProductSearchIntegrationTests:19](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/ProductSearchIntegrationTests.cs#L19), [ProductSearchIntegrationTests:228](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/ProductSearchIntegrationTests.cs#L228) | Prioridad de coincidencias y bloques por disponibilidad, con lecturas de otras sedes limitadas al tenant. |
| CAT 09,10,11,12 | [CatalogFoundationTests:69](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/CatalogFoundationTests.cs#L69), [CatalogFoundationTests:211](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/CatalogFoundationTests.cs#L211) | Copia global y alta local, precio y estado persisten con auditoría tenant. Un fallo de auditoría revierte la mutación. |
| CAT 13,14 | [ProductUnitIntegrationTests:28](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/ProductUnitIntegrationTests.cs#L28), [ProductUnitIntegrationTests:158](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/ProductUnitIntegrationTests.cs#L158) | Cambio versionado de unidades, fracción decimal exacta, identidad retenida y una edición ganadora. |
| CAT 17,18 | [ProductImportIntegrationTests:24](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/ProductImportIntegrationTests.cs#L24), [RecoverableOperationPostgreSqlTests:281](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/RecoverableOperationPostgreSqlTests.cs#L281) | Importación mixta conserva resultados por fila, unidades válidas y resumen. Se conserva la referencia original al trabajo tras respuesta perdida. |
| CAT 19 | [InterruptedProductImportTests:32](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/InterruptedProductImportTests.cs#L32), [InterruptedProductImportTests:100](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/InterruptedProductImportTests.cs#L100), [InterruptedProductImportTests:135](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/InterruptedProductImportTests.cs#L135) | Trabajo activo bloquea recuperación. Repetición y concurrencia generan una auditoría, con guardas de rol, tenant y drenaje. |
| COM 01 | [CommercialDirectoryPostgreSqlTests:220](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L220) | Caducidad paginada y valoración por costo histórico limitadas a sede y horizonte. |
| COM 02 | [StockThresholdIsolationTests:20](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/StockThresholdIsolationTests.cs#L20), [StockThresholdIsolationTests:83](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/StockThresholdIsolationTests.cs#L83) | Actualizar respeta versión y audita antes/después. Fallo de auditoría revierte el mínimo. |
| COM 03,04,07,09 | [CommercialDirectoryPostgreSqlTests:120](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L120) | Stock 56.25, lotes acotados al producto, proveedor local y recepción por ID con dos líneas. Sede/tenant ajenos se excluyen. Sin acreditar descubrimiento sin referencia. |
| COM 05 | [InventoryPersistenceTests:27](../../../backend/tests/MediPOS.IntegrationTests/Modules/Inventory/InventoryPersistenceTests.cs#L27), [InventoryPersistenceTests:61](../../../backend/tests/MediPOS.IntegrationTests/Modules/Inventory/InventoryPersistenceTests.cs#L61), [InventoryPersistenceTests:85](../../../backend/tests/MediPOS.IntegrationTests/Modules/Inventory/InventoryPersistenceTests.cs#L85) | Ajuste firmado, saldo y auditoría atómicos. Dos decrementos de la última unidad producen un éxito. |
| COM 06,08,14 | [RecoverableOperationPostgreSqlTests:52](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/RecoverableOperationPostgreSqlTests.cs#L52), [RecoverableOperationPostgreSqlTests:247](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/RecoverableOperationPostgreSqlTests.cs#L247) | Acuse perdido conserva compra original, clave por instancia y controles de actor/sede. Las variantes supplier y purchase-draft conservan el acuse en un scope nuevo. |
| COM 10,11 | [PurchaseWorkflowIntegrationTests:34](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/PurchaseWorkflowIntegrationTests.cs#L34), [PurchaseWorkflowIntegrationTests:136](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/PurchaseWorkflowIntegrationTests.cs#L136), [PurchaseWorkflowIntegrationTests:170](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/PurchaseWorkflowIntegrationTests.cs#L170) | Reemplazo de líneas todo-o-nada y confirmación concurrente con dos lotes, dos movimientos y una auditoría. |
| COM 12 | [PurchaseWorkflowIntegrationTests:194](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/PurchaseWorkflowIntegrationTests.cs#L194) | Referencia exacta duplicada devuelve las dos compras del tenant y excluye la ajena. No cubre directorio sin referencia. |
| COM 15 | [InventoryQueriesTests:28](../../../backend/tests/MediPOS.IntegrationTests/Modules/Inventory/InventoryQueriesTests.cs#L28), [SaleCheckoutPersistenceTests:76](../../../backend/tests/MediPOS.IntegrationTests/Modules/SalesPos/SaleCheckoutPersistenceTests.cs#L76) | Plan FEFO en dos lotes, respeta vencimiento y sede, rechaza faltante. En confirmación descuenta en orden estable. |
| REP 01 | [CommercialDirectoryPostgreSqlTests:184](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L184) | Consulta de ajustes ausentes como Owner sin insertar configuración implícita. |
| REP 06 | [OwnerCommissionsReportIntegrationTests:22](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/OwnerCommissionsReportIntegrationTests.cs#L22), [OwnerCommissionsReportIntegrationTests:76](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/OwnerCommissionsReportIntegrationTests.cs#L76) | Importes y filtros se mantienen entre páginas. Personal operativo no lee las comisiones del equipo. |
| REP 07 | [OwnerOverviewPersistenceTests:27](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/OwnerOverviewPersistenceTests.cs#L27) | Periodos de confirmación/anulación y totales por medio de pago incluyen sedes vacías. |
| REP 08,10,11,12,13 | [OperationalReportsPersistenceTests:53](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/OperationalReportsPersistenceTests.cs#L53) | Dashboard neto 9, riesgo físico 8/vendible 5 y capital 21.1333. Se comprueban totales de caducidad y rotación contra datos comunes. |
| REP 09 | [OperationalReportsConsistencyTests:22](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/OperationalReportsConsistencyTests.cs#L22) | Consulta de ventas filtradas usa una instantánea mientras otra conexión anula la venta. |
| REP 14,16 | [ReportExportPersistenceTests:49](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/ReportExportPersistenceTests.cs#L49), [ReportExportPersistenceTests:142](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/ReportExportPersistenceTests.cs#L142) | Ambos formatos concuerdan con lecturas de PostgreSQL y se rechaza acceso de rol/sede ajenos. En REP-16 cubre exportación, sin descarga de navegador. |
| REP 15 | [CommissionPostingTests:29](../../../backend/tests/MediPOS.IntegrationTests/Modules/Commissions/CommissionPostingTests.cs#L29) | Comisión de venta multilínea conserva vendedor y se compensa en su totalidad al anular. |
| REP 17 | [ReplenishmentPersistenceTests:30](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/ReplenishmentPersistenceTests.cs#L30), [ReplenishmentPersistenceTests:83](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/ReplenishmentPersistenceTests.cs#L83), [ReplenishmentPersistenceTests:140](../../../backend/tests/MediPOS.IntegrationTests/Modules/Reporting/ReplenishmentPersistenceTests.cs#L140) | Consulta interna con mínimos por sede y unidades base, sin escrituras. Denegación a personal y consistencia al anular simultáneamente. |
| AVI 01 | [CommercialDirectoryPostgreSqlTests:248](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L248) | Consulta de metadatos excluye otra empresa y preserva snapshots. |
| AVI 02,03,04,06 | [PersistentNotificationsTests:30](../../../backend/tests/MediPOS.IntegrationTests/Modules/Notifications/PersistentNotificationsTests.cs#L30), [PersistentNotificationsTests:174](../../../backend/tests/MediPOS.IntegrationTests/Modules/Notifications/PersistentNotificationsTests.cs#L174), [PersistentNotificationsTests:257](../../../backend/tests/MediPOS.IntegrationTests/Modules/Notifications/PersistentNotificationsTests.cs#L257) | Dos ejecuciones de generación dejan un aviso por lote, con lecturas por membresía. Doble lectura registra una fila. Paginación y contador respetan sede. No acredita disparo del reloj del host. |
| ACC 01,19,20 | [CommercialDirectoryPostgreSqlTests:120](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L120) | Directorio de empleados contiene asignación/horario; sedes según membresía y entidades del tenant. |
| ACC 03 | [OwnerConcurrencyTests:21](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/OwnerConcurrencyTests.cs#L21), [OwnerConcurrencyTests:38](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/OwnerConcurrencyTests.cs#L38) | Desactivación serializa la decisión sobre el último Owner y conserva historial. |
| ACC 04,05 | [IdentityPersistenceTests:212](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/IdentityPersistenceTests.cs#L212), [IdentityPersistenceTests:241](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/IdentityPersistenceTests.cs#L241) | Reemplazar asignaciones y horarios conserva las relaciones. Un fallo revierte el reemplazo ejercitado. |
| ACC 08,09,11 | [EmployeeInvitationPersistenceTests:25](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/EmployeeInvitationPersistenceTests.cs#L25), [EmployeeInvitationPersistenceTests:89](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/EmployeeInvitationPersistenceTests.cs#L89) | Aceptación sin membresía, aprobación concurrente con una membresía y auditoría, rollback ante fallo. |
| ACC 10 | [EmployeeInvitationPersistenceTests:71](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/EmployeeInvitationPersistenceTests.cs#L71) | Tras revocar, el ticket falla y no crea membresía ni auditoría de aceptación. |
| ACC 17 | [CommercialDirectoryPostgreSqlTests:31](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L31), [CommercialDirectoryPostgreSqlTests:58](../../../backend/tests/MediPOS.IntegrationTests/Tenancy/CommercialDirectoryPostgreSqlTests.cs#L58) | Vacío sin membresía y directorio paginado de membresías activas propias. |
| ACC 18 | [OperationalAccessPersistenceTests:22](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/OperationalAccessPersistenceTests.cs#L22), [OperationalAccessPersistenceTests:69](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/OperationalAccessPersistenceTests.cs#L69) | Resolución por usuario, sede y horario. Licencia y membresía releídas tras cambios. |
| ACC 22,23 | [BranchesPersistenceTests:39](../../../backend/tests/MediPOS.IntegrationTests/Modules/Branches/BranchesPersistenceTests.cs#L39), [BranchesPersistenceTests:145](../../../backend/tests/MediPOS.IntegrationTests/Modules/Branches/BranchesPersistenceTests.cs#L145), [BranchesPersistenceTests:174](../../../backend/tests/MediPOS.IntegrationTests/Modules/Branches/BranchesPersistenceTests.cs#L174) | Entidad/sede conservan tenant. Crear sedes concurrentemente no supera el cupo de licencia. |
| ACC 24 | [RecoverableOperationPostgreSqlTests:247](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/RecoverableOperationPostgreSqlTests.cs#L247) | Recuperar la constancia requiere membresía, tenant, sede y autorización vigente. |
| ACC 27 | [InitialOwnerOnboardingPersistenceTests:44](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/InitialOwnerOnboardingPersistenceTests.cs#L44), [InitialOwnerOnboardingPersistenceTests:62](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/InitialOwnerOnboardingPersistenceTests.cs#L62) | Reclamo mediante identidad suministrada por fixture. Dos ingresos generan una membresía y el correo ajeno/no verificado no reclama. Pendiente callback OIDC real. |
| PLA 01,03,04,05,06 | [PlatformDirectoryPersistenceTests:28](../../../backend/tests/MediPOS.IntegrationTests/Modules/TenancyLicensing/PlatformDirectoryPersistenceTests.cs#L28) | Consultas con rol restringido devuelven categorías, página acotada, detalle y usuario registrado. Solo el operador original puede consultar la constancia. |
| PLA 07,08,09,14 | [InitialOwnerOnboardingPersistenceTests:26](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/InitialOwnerOnboardingPersistenceTests.cs#L26), [InitialOwnerOnboardingPersistenceTests:62](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/InitialOwnerOnboardingPersistenceTests.cs#L62), [InitialOwnerOnboardingPersistenceTests:76](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/InitialOwnerOnboardingPersistenceTests.cs#L76) | Alta con email deja destinatario pendiente sin membresía. Recuperación conserva licencia, cambio de destinatario o revocación condicionan el reclamo. |
| PLA 10,11,12,18 | [TenancyLicensingTests:67](../../../backend/tests/MediPOS.IntegrationTests/Modules/TenancyLicensing/TenancyLicensingTests.cs#L67), [PlatformAdministrationPersistenceTests:118](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/PlatformAdministrationPersistenceTests.cs#L118) | Renovar, suspender, reactivar y solicitar purga conservan historia por transición. La solicitud no elimina datos físicos. |
| PLA 13 | [TenantCreationIdempotencyPersistenceTests:39](../../../backend/tests/MediPOS.IntegrationTests/Modules/TenancyLicensing/TenantCreationIdempotencyPersistenceTests.cs#L39), [TenantCreationIdempotencyPersistenceTests:72](../../../backend/tests/MediPOS.IntegrationTests/Modules/TenancyLicensing/TenantCreationIdempotencyPersistenceTests.cs#L72), [TenantCreationIdempotencyPersistenceTests:138](../../../backend/tests/MediPOS.IntegrationTests/Modules/TenancyLicensing/TenantCreationIdempotencyPersistenceTests.cs#L138) | Admisión inicial, respuesta perdida y rollback: una creación y acuse original, rechazo de otra intención. |
| PLA 15 | [PlatformBootstrapOwnerPersistenceTests:33](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/PlatformBootstrapOwnerPersistenceTests.cs#L33), [PlatformBootstrapOwnerPersistenceTests:139](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/PlatformBootstrapOwnerPersistenceTests.cs#L139) | Dos autoridades compiten por un único primer Owner. Usuario inexistente no genera identidad. |
| INT 02 | [RuntimeReadinessPostgreSqlTests:18](../../../backend/tests/MediPOS.IntegrationTests/Observability/RuntimeReadinessPostgreSqlTests.cs#L18), [RuntimeReadinessPostgreSqlTests:34](../../../backend/tests/MediPOS.IntegrationTests/Observability/RuntimeReadinessPostgreSqlTests.cs#L34) | Sonda con runtime restringido y sin mutación; conexión privilegiada rechazada. Es la sonda, no todo el despliegue HTTP. |
| INT 04,05 | [OfflineSyncPersistenceTests:183](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/OfflineSyncPersistenceTests.cs#L183), [OfflineSyncPersistenceTests:268](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/OfflineSyncPersistenceTests.cs#L268) | Revocación conserva historia e impide reutilizar sesión. Una inscripción sin auditoría no puede confirmar. |
| INT 06 | [OfflineSyncBatchPersistenceTests:20](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/OfflineSyncBatchPersistenceTests.cs#L20), [OfflineSyncBatchPersistenceTests:56](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/OfflineSyncBatchPersistenceTests.cs#L56) | Repetir lote conserva una identidad por UUID, mismo stock y resultado terminal tras nueva instancia. |
| INT 07,08 | [OfflineSalePostingTests:61](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/OfflineSalePostingTests.cs#L61), [OfflineSaleSecurityTests:120](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/OfflineSaleSecurityTests.cs#L120) | Contención por la última unidad deja un conflicto reconocible. Resolver revalida Owner y la repetición distinta no reescribe el caso. |
| INT 09,10 | [BranchCatalogProvisioningTests:17](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/BranchCatalogProvisioningTests.cs#L17), [BranchCatalogMutationTests:68](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/BranchCatalogMutationTests.cs#L68) | Snapshot y cursor resisten altas concurrentes. Un rollback omite deltas fantasma. |
| INT 12 | [OfflineSalePostingTests:30](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/OfflineSalePostingTests.cs#L30), [OfflineSalePostingTests:173](../../../backend/tests/MediPOS.IntegrationTests/Modules/OfflineSync/OfflineSalePostingTests.cs#L173) | Venta offline mixta y FEFO con acuse perdido. Un reenvío tras commit conserva los mismos registros financieros. Cola y reentrada PWA real pendientes. |
| INT 13 | [OfficialRefreshPersistenceTests:91](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/OfficialRefresh/OfficialRefreshPersistenceTests.cs#L91), [OfficialRefreshPersistenceTests:137](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/OfficialRefresh/OfficialRefreshPersistenceTests.cs#L137) | Refresco compatible conserva ID, checksum e historia; cambio crítico no publica. Fixture de catálogo, sin carga externa. |
| INT 14 | [SupplierUblIntegrationTests:55](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/Ubl/SupplierUblIntegrationTests.cs#L55), [SupplierUblIntegrationTests:78](../../../backend/tests/MediPOS.IntegrationTests/Modules/Purchasing/Ubl/SupplierUblIntegrationTests.cs#L78) | XML sintético preservado y revisión humana produce un borrador, sin entrada de stock. |
| INT 15 | [ElectronicDocumentPersistenceTests:105](../../../backend/tests/MediPOS.IntegrationTests/Modules/ElectronicDocuments/ElectronicDocumentPersistenceTests.cs#L105), [ElectronicDocumentPersistenceTests:143](../../../backend/tests/MediPOS.IntegrationTests/Modules/ElectronicDocuments/ElectronicDocumentPersistenceTests.cs#L143) | Trabajo recuperable antes de enviar, timeout posterior incierto y proveedor deshabilitado conserva pendiente. Sin resultado fiscal real. |
| INT 17 | [RecoveryPostgreSqlTests:124](../../../backend/tests/MediPOS.IntegrationTests/Recovery/RecoveryPostgreSqlTests.cs#L124), [RecoveryPostgreSqlTests:159](../../../backend/tests/MediPOS.IntegrationTests/Recovery/RecoveryPostgreSqlTests.cs#L159), [RecoveryPostgreSqlTests:211](../../../backend/tests/MediPOS.IntegrationTests/Recovery/RecoveryPostgreSqlTests.cs#L211) | Comprobaciones sobre BD íntegra y dañada, sonda runtime y datos preservados. No acredita ensayo operativo completo de backup/restauración. |
| ACC 02 | [TenantCommercialContractTests:142](../../../backend/tests/MediPOS.UnitTests/Api/TenantCommercialContractTests.cs#L142) | HTTP en host de prueba: alta arbitraria responde 409 membership.invitation_required y la colección sigue vacía. |
| ACC 12 | [AuthenticationRateLimitHttpTests:24](../../../backend/tests/MediPOS.UnitTests/Api/AuthenticationRateLimitHttpTests.cs#L24) | Host de prueba: GET de CSRF 200 dentro de cuota y 429 al excederla. El límite no prueba cookies en Chrome. |
| INT 01 | [AuthenticationRateLimitHttpTests:117](../../../backend/tests/MediPOS.UnitTests/Api/AuthenticationRateLimitHttpTests.cs#L117) | GET /health obtiene 200 aun con cuota de autenticación agotada. Liveness del host de prueba. |
| ACC-06 | [EmployeeInvitationPersistenceTests:89](../../../backend/tests/MediPOS.IntegrationTests/Modules/IdentityAccess/EmployeeInvitationPersistenceTests.cs#L89) | E3 · Tras fallo de aprobación, la lectura por ID devuelve accepted_pending_approval y la misma versión. |
| PLA-16 | [CatalogFoundationTests:34](../../../backend/tests/MediPOS.IntegrationTests/Modules/Catalog/CatalogFoundationTests.cs#L34) | E3 · El fixture usa CreateCategoryHandler y verifica la fila global por ID. Quedan guardas HTTP de alta por verificar en recorrido integrado. |
| REP-02 REP-04 REP-05 | [CommissionPostingTests:29](../../../backend/tests/MediPOS.IntegrationTests/Modules/Commissions/CommissionPostingTests.cs#L29) | E3 · Cambio de regla, desactivación y apagado desde el handler conservan los earned históricos y sus importes al revertir. No certifica el formulario de reglas. |
| ACC-13 ACC-14 | [AuthenticationRateLimitHttpTests:38](../../../backend/tests/MediPOS.UnitTests/Api/AuthenticationRateLimitHttpTests.cs#L38) | E2 · Proveedor de prueba: desafío con code y S256, callback inválido rechazado y retorno válido con PKCE verificado. OAuth externo excluido. |
| CAT-01 CAT-02 | [TenantCommercialContractTests:34](../../../backend/tests/MediPOS.UnitTests/Api/TenantCommercialContractTests.cs#L34) | E2 · Ambas rutas operativas registradas y 401 sin sesión. Quedan los resultados positivos por categoría en una sede real aislada. |
| INT-03 | [TenantSyncHttpTests:19](../../../backend/tests/MediPOS.UnitTests/Api/TenantSyncHttpTests.cs#L19) | E2 · GET del directorio por sede devuelve el dispositivo registrado en el host con dobles. |
| CAT-15 | [TenantApiHttpTests:68](../../../backend/tests/MediPOS.UnitTests/Api/TenantApiHttpTests.cs#L68) | E2 · PUT heredado con cambio devuelve 409 catalog.units_legacy_write_blocked. El no-op compatible se distingue del PUT versionado. |

**F-001.** En la teoría de cierre se comprueban fila closed, esperado, contado, diferencia, actor/fecha, una auditoría, igualdad con arqueo por ID y ausencia en activas. La teoría usa contado 100.1234; no contiene el recorrido exacto 100 → 10 sin movimientos. RUN-07 sí ejercitó el recorrido en DOM con respuesta simulada. Faltan la fila y auditoría del caso exacto aislado, y Chrome tras perder la URL. La ausencia en activas coincide con su contrato. La causa del incidente DEV sigue indeterminada.

**F-002.** El préstamo y su recepción tienen E3 en ambas sedes. El saldo de cierre de origen incorpora el efectivo neto y el préstamo; el destino recibe el importe una vez. Las carreras compiten por el saldo bajo candado. No se ha identificado una aserción HTTP real sobre el JSON de estado abierto con este fixture. RUN-02 muestra que la UI con esperado nulo emite cero POST. Por eso UC-TRA-01 alcanza E3 en el backend y conserva FAIL comercial reproducido a E2. El GET de UC-CAJ-04 sigue con evidencia previa.

**Resultados acotados.** COM-15, REP-15 y REP-17 alcanzan su objetivo de dominio/consulta interna en datos persistidos. PLA-13 acredita el contrato heredado sin pantalla propia; ACC-02 acredita el rechazo HTTP de la ruta heredada e INT-01 liveness. CAT-20 conserva su PASS local. ACC-12 tiene HTTP ejercitado; falta verificar emisión/uso en el navegador aislado. INT-16 continúa NOT_RUN en su frontera de arranque; registrar el servicio en DI no ejercita la conexión del host. CAT-22 conserva N/A, borrado físico fuera del producto.

**Cobertura vigente, 155/155 UC, nueve familias, doce escenarios acumulativos.** 3 E1, 34 E2, 118 E3. 133 BLOCKED, 1 FAIL, 1 N/A, 1 NOT_RUN, 19 PASS. PASS adicionales por objetivo acotado: CAJ-02/03 (apertura y actual por membresía), TRA-02/03 (pendientes y detalle recibido), COM-02/05 (versionado y ajuste atómico), ACC-03 (último Owner), AVI-03/04 (conteo y lectura personal), REP-06 (reporte, filtros y negativa), PLA-06/15 (constancia del operador y bootstrap histórico). Cada PASS se limita al escenario de las filas anteriores junto con la caracterización previa aplicable. Los pasos UI que solo tienen runner agregado con un fallo innominado no ganan evidencia por este run. Para los UC bloqueados, el límite específico se conserva en SC-01–12, en las filas anteriores y en los lotes de [aceptación](ROADMAP.md). El gate RFA2 sigue parcial por las dos recetas exactas y por B-02. Los cortes que siguen son históricos.

### Qué falta por familia

| Familia | UC | E1 / E2 / E3 / E4 | PASS / FAIL / BLOCKED / NOT_RUN / N/A | Frontera aún abierta |
|---|---:|---|---|---|
| CAJ | 18 | 0 / 7 / 11 / 0 | 2 / 0 / 16 / 0 / 0 | Acuse HTTP y consulta de la misma caja tras reentrada, cierre exacto sin movimientos y observación del estado abierto. En POS faltan recarga de la intención, impresión y sesión real de navegador. |
| CAT | 22 | 1 / 8 / 13 / 0 | 1 / 0 / 20 / 0 / 1 | Quedan surtido/categorías de sede autorizada, edición después de un conflicto, recuperación del importador en la pantalla y visibilidad por rol. CAT-22 carece de conducta ejecutable. |
| COM | 15 | 0 / 1 / 14 / 0 | 3 / 0 / 12 / 0 / 0 | Ver recepción sin referencia tras perder la URL y conservar líneas locales. En stock/lotes medir coste de nombres con transporte real. La persistencia de ajustes y de lotes sí fue observada. |
| TRA | 13 | 0 / 0 / 13 / 0 | 2 / 1 / 10 / 0 / 0 | La recepción y el saldo se ejercitaron por aplicación. Falta el GET abierto y el recorrido UI de despacho, reconsulta del original al perder el acuse y recepción desde la otra sede sin duplicar. |
| REP | 17 | 0 / 1 / 16 / 0 | 3 / 0 / 14 / 0 / 0 | Falta contrastar los datos que presenta React y el archivo descargado con una misma selección de sede/rango real; configuración y lista de reglas requieren alcance y versión en ese flujo. |
| AVI | 7 | 1 / 1 / 5 / 0 | 2 / 0 / 5 / 0 / 0 | Comprobar abrir el drawer, recuperación tras acuse perdido y actualización del contador en una sesión real. AVI-06 tiene generador y deduplicación con BD, sin prueba del disparo en el host. |
| ACC | 27 | 0 / 10 / 17 / 0 | 2 / 0 / 25 / 0 / 0 | Pendientes reentrada con identidad cambiada, selección de sede, expiración y revocación durante la sesión, más el salto desde el callback. La membresía del reclamo y el límite de sede sí tienen efectos E3. |
| PLA | 18 | 0 / 2 / 16 / 0 | 3 / 0 / 15 / 0 / 0 | Pendientes admisión y roles en la consola real, regreso tras refresco fallido, cambio/revocación de destinatario en UI y navegación bajo 429. Solicitud interna de purga no acredita borrado. |
| INT | 18 | 1 / 4 / 13 / 0 | 1 / 0 / 16 / 1 / 0 | Cola y caché PWA en navegador real, aceptación del acuse tras reconexión y ejercicio operativo de restore. INT-16 no se arrancó. No usar una respuesta de fixture para certificar CPE ni un servicio externo. |

## Corte anterior · continuación 02B · `5d38898`

HEAD coincide con `5d3889806e11fea23a3ba19402c22c15635a1301`. La comparación con `2c54e1f7` solo contiene documentos, por lo que se conserva RUN-01–08. El run de PostgreSQL iniciado por el operador queda **PENDIENTE DE EVIDENCIA**. El encargo no incluye enlace ni resumen. Se solicitó URL, attempt, SHA, resultados de pasos, versión SQL real, totales y líneas `SUITE`. Se verificó una vez la falta de Docker en PATH y en la ubicación estándar. B-01 y B-02 siguen vigentes.

**RUN-09 · SC-06 / F-005.** `vitest run Rfa2bScope --reporter=json --outputFile="$result/f005.json"` desde copia temporal con dependencias locales, Node 24.18.1 y jsdom con doble HTTP. **1 PASS, 0 FAIL, 0 SKIP**. Reporte SHA-256 `C28EF4E14542CA904C442ACBB8A84BDCD7AA5538D95E436E127F2B9D1258C2CE`. Para tres productos distintos: **3 GET de detalle en frío, 0 adicionales** al pasar de Existencias a Lotes con caché vigente; **3 nuevos** al cambiar de sede dentro del mismo tenant. Sin etiquetas de la sede anterior ni mutaciones. Permanece E2 y no mide latencia o tráfico real. [Reproducción](evidence/rfa2-dom.md#sc-06--tres-productos-y-cambio-de-sede-02b).

### Aserciones localizadas para cotejar con el run

La columna de alcance describe el código de la prueba. Queda pendiente asociarlo a ejecución real. Un `SUITE` completo en PASS puede respaldar los métodos cotejados si coincide SHA, se verifica PostgreSQL 18 real y el paso de integración no se abortó. Para clases mixtas hace falta evidencia del método relevante y su fila o variante.

| Caso | Candidato de integración | Alcance y hueco |
|---|---|---|
| F-001 / UC-CAJ-05/06/07 | `CashClosePersistenceTests.cs:53–102`, `:137–174` | Confirma cierre por handler, fila, actor/fecha y auditoría, exclusión de activas, arqueo por ID y rollback ante fallo. Usa contado `100.1234` en la prueba parametrizada, incluso en la fila `null`. Caso análogo, no reproducción exacta de 100→10 sin movimientos. |
| F-001 / UC-CAJ-02/03/06 | `CashSessionPersistenceTests.cs:48–99,134–148`; `CashCloseConcurrencyTests.cs:27–58`; `CashCloseIsolationTests.cs:28–55,59–78` | Apertura/membresía/auditoría, consulta de abierta y ausencia de históricas, doble cierre con un ganador y una auditoría, negativa y RLS. Lecturas por aplicación/EF; el descubrimiento con URL limpia y el acuse HTTP real siguen pendientes. |
| F-002 / UC-TRA-01–04 | `CashTransferPersistenceTests.cs:39–84,105–127`; `CashTransferRecoverabilityTests`, `CashTransferConcurrencyTests`, `CashTransferIsolationTests` | El método de alto riesgo prueba importe `50.1234`, esperado origen `50.8766` y destino `70.1234`, trámite en tránsito, recepción y auditorías. Cotejar las demás clases por constancia, bloqueo y alcance. La prueba análoga no observa el JSON del GET de estado `open`. El FAIL E2 del botón se conserva. |

**NOT_RUN revisados.** `ACC-02` tiene aserción HTTP con doble en `TenantCommercialContractTests.cs:142–146`, `ACC-12` e `INT-01` en `AuthenticationRateLimitHttpTests.cs:23,116–117`, e `INT-16` en `LocalDevelopmentPlatformTests.cs:17–31`. Esta última verifica el registro del chequeo sin iniciar DEV, por lo que no acredita su conexión. Conservar estado hasta acreditar ejecución de esas pruebas. F-006 conserva fallos y rechecks de RUN-03/05.

**B-02, receta.** Con PostgreSQL efímero acreditado, usar build identificado de SPA/API en loopback separado, autenticación sintética por un mecanismo de tests existente y perfil de navegador limpio. Confirmar que API, workers y navegador no alcanzan DEV, OAuth real ni servicios externos. Crear datos sintéticos mediante fixture, conservar los ID de Caja/Sencillo y comparar acuse, fila, auditoría, activas y reentrada por URL. Para F-002 capturar el GET `open` real, observar el estado del botón y contar POST. La inspección focalizada no acreditó esa composición completa con navegador en este runner.

## Evidencia acumulada de RUN-01–08

Baseline `2c54e1f71aa6f66a67190d37eacda6ada71f4b0f`, rama `ci/postgres-diagnostics-8bc6b4c`, 26/11/2026 Lima. `git diff --stat <baseline>` vacío al inicio. Dos documentos del encargo sin rastrear preservados. Windows, Node 24.18.1, npm 11.16.0, Vitest 5.0.4 y SDK .NET 10.0.401. Frontend cargado desde fuentes de ese checkout, con dependencias locales y jsdom en `https://medipos.test/`. Los participantes y datos son sintéticos. E2 de UI mide comportamiento y solicitudes interceptadas por dobles HTTP. Los resultados de runner están separados de los veredictos de caso en [MATRIX](MATRIX.csv).

## Aislamiento y límites

- **B-01 · E3 BLOCKED.** Docker no está disponible en PATH y no existe un destino aislado acreditado. `PostgreSqlFixture.cs:41–62` crea contenedor propio `postgres:18-alpine`, ejecuta las migraciones sobre él y crea runtime efímero `NOSUPERUSER NOBYPASSRLS`. No se inició fixture. ID de contenedor, versión SQL, migraciones aplicadas y rol efectivo quedan sin verificar. DEV, LAB, QA y producción se excluyeron.
- **B-02 · E4 BLOCKED.** Falta composición API + DB sintética aislada y autenticación de pruebas para navegador. jsdom, navegación por MemoryRouter y recarga simulada no son Chrome. Estado de DEV servido y esquema operativo NO_DETERMINADO.
- **B-03 · Servicios externos excluidos.** OIDC real, CPE y tráfico fiscal/proveedores no se ejecutaron. CPE conserva su estado configuracional desactivado; los casos de SDK/parser o transporte local se distinguen de una integración externa.

## Ejecuciones

`$result` representa la carpeta temporal del runner. Los JSON/TRX son artefactos privados no versionados. Las huellas corresponden al reporte emitido, que incluye metadatos del runner.

| RUN | Comando de selección y salida | PASS / FAIL / SKIP | Reporte SHA-256 |
|---|---|---:|---|
| RUN-01 | Desde frontend, `npm.cmd run test -- src/features/cash/CashPage.test.tsx --reporter=json --outputFile="$result/cash.json"` | 12 / 0 / 0 | `D19E552F080EDE9B8A49D949D98D35BE88CF199FF0D8CB07E62114385358187F` |
| RUN-02 | Copia aislada, `vitest run src/features/transfers/CashTransfersPage.test.tsx src/features/transfers/Rfa2CashContract.test.tsx --reporter=json --outputFile="$result/cash-transfer.json"` | 15 / 0 / 0 | `3464DC47003A304A89E54B02E71C0C775CD6D6CDA8E278CC9FEB4C81A00287D1` |
| RUN-03 | Desde frontend, `npm.cmd run test -- @paths --reporter=json --outputFile="$result/front-lotes.json"`. Archivos en tabla siguiente. Duración de comando 71,2 s. | 258 / 2 / 0 | `F9A8B8014F973EE86EEE1C173168CAC4C57F7373E5B6832FB09DE98E26C07EEB` |
| RUN-04 | Copia aislada, `vitest run Rfa2Finding --reporter=json --outputFile="$result/findings.json"` | 3 / 0 / 0 | `B4F9443CFD5A7CD35F3F36E01DC64CEEA87FC1EEA21BEAA6C469DF49AEC46136` |
| RUN-05 | `npm.cmd run test -- src/features/platform/PlatformPage.test.tsx src/features/transfers/TransfersPage.test.tsx --maxWorkers=1 -t 'navigates all six areas\|does not infer source authority' --reporter=json --outputFile="$result/recheck.json"` | 2 / 0 / 0 seleccionados, 42 excluidos por filtro | `D5F36BF350C9313DD7B83C8D9AA131DE638EEEF61D779AA9F894E3C92B643AEB` |
| RUN-06 | `npm.cmd run test -- src/features/employees/invitations.test.tsx src/features/employees/invitations.api.test.ts tests/pwa.test.ts --reporter=json --outputFile="$result/aux.json"` | 15 / 0 / 0 | `63DB0E94E4BD9355FDC8EF07FDFB12630EE39FFF2F2DD1B5B05CB08EE298F3C3` |
| RUN-07 | Copia aislada, `vitest run Rfa2CashReentry --reporter=json --outputFile="$result/cash-reentry-recheck.json"` | 1 / 0 / 0 | `2D0AD8423AD93436CEC9E292CC2765FCEEC53F16D1F9957F525B4BDA31D9E970` |
| RUN-08 | xUnit v3 en proceso, filtro .NET de abajo, desde binario compilado con salida segregada. Tiempo de runner 0,673 s. | 688 / 0 / 0 | `67BFBA0867B323BA1F46DB6800907F36909297D1580C41AF8E64DC8047421D28` |

**.NET.** El build estándar no pudo copiar las DLL ocupadas bajo `MediPOS.Api/bin` (MSB3021/MSB3027). La compilación con `-p:BaseOutputPath=obj/rfa2-bin/ -m:1 --disable-build-servers --no-restore` produjo `MediPOS.UnitTests.dll` SHA-256 `5AA06912DC563AD3340ADECEA9788FF9ADF62AB22AB3E3F580AED532033CCEC5`. VSTest abortó al no conectar con `testhost` en 90 s, sin resultados de prueba. Se reutilizó ese binario con el runner oficial en proceso, sin repetir la compilación. Se seleccionaron pruebas de dominio/aplicación, sin arranque de PostgreSQL ni servicios. Runtime 10.0.12, xUnit 4.0.1.

```powershell
$f = 'FullyQualifiedName~Modules.Cash|FullyQualifiedName~Modules.SalesPos|FullyQualifiedName~PurchasingWorkflowTests|FullyQualifiedName~PurchasingAuthorizationTests|FullyQualifiedName~AdjustStockTests|FullyQualifiedName~TransferUseCaseTests|FullyQualifiedName~CommissionConfigurationUseCaseTests|FullyQualifiedName~ReplenishmentAuthorizationTests|FullyQualifiedName~ExpirationJobTests|FullyQualifiedName~OperationalAccessTests|FullyQualifiedName~EmployeeInvitationApplicationTests|FullyQualifiedName~InitialOwnerOnboardingTests|FullyQualifiedName~OfflineSaleTests|FullyQualifiedName~SupplierUblStructureTests|FullyQualifiedName~SearchProductsTests'
dotnet tests/MediPOS.UnitTests/obj/rfa2-bin/Debug/net10.0/MediPOS.UnitTests.dll -filterVSTest $f -result-xml "$result/domain-xunit.xml"
```

RUN-07 se compiló tras corregir en la copia temporal el recorte del preámbulo: el primer intento abortó antes de ejecutar el caso. Totales adjudicados de runner: **306 PASS, 2 FAIL, 0 SKIP en Vitest**, incluidos ambos rechecks. **688 PASS, 0 FAIL, 0 SKIP en xUnit**. Los 42 casos excluidos en RUN-05 se ejecutaron en RUN-03 y no se cuentan como skips de la selección.

RUN-03 seleccionó los siguientes nombres exactos mediante `rg --files src`. RUN-04 y RUN-02 reutilizan la misma copia de fuentes, con [recetas reproducibles](evidence/rfa2-dom.md). La primera invocación de RUN-02 quedó antes del runner por redirección de archivos a un temporal inexistente. Se corrigió la ubicación y se creó la carpeta explícitamente.

| Grupo | Archivos de RUN-03 (sufijo `.test.tsx`) | PASS / FAIL |
|---|---|---:|
| POS | PosPage | 42 / 0 |
| Catálogo | ProductsPage, ImportPage | 35 / 0 |
| Compras / inventario | PurchasesPage, InventoryPage | 44 / 0 |
| Transferencias | TransfersPage | 15 / 1 |
| Reportes y comisiones | ReportsPage, CommissionsPage | 21 / 0 |
| Avisos y auditoría | NotificationsPage, AuditPage | 15 / 0 |
| Acceso | EntryRouter, TenantOnboardingPage, EmployeesPage | 54 / 0 |
| Plataforma | PlatformPage | 27 / 1 |
| Contingencia | ContingencyPage, WorkspaceOutboxSync | 5 / 0 |

## Escenarios y fronteras

Los escenarios se agrupan por recorrido. Un PASS parcial E2 permite registrar el nivel alcanzado para la acción ejercitada. La matriz conserva BLOCKED cuando B-01 o B-02 impide acreditar una frontera comercial esencial. Las filas E1 sin prueba dedicada retienen referencias estáticas. Las columnas vacías de pruebas o requisitos significan asociación individual no acreditada, no ausencia demostrada. Rutas auxiliares de hosting y health se evalúan como operación técnica.

| Escenario y casos | Esperado y observado | Veredicto / límite |
|---|---|---|
| SC-01 · Caja · F-001, CAJ-02–07/16–17 | FR-CASH-002/003/005. Abrir 100 sin movimientos, cerrar 10, esperado 100 y diferencia −90. RUN-07 envió un POST con 10, mostró faltante 90 del acuse simulado y distinguió URL limpia (activas vacías) de dirección original (recuperación por GET, cero reenvíos). RUN-01 cubrió 401/403/404/429/503 y rechazo de otro ámbito. | E2 parcial. Secuencia real, fila cerrada, auditoría, activas y reentrada real BLOCKED por B-01/B-02. Incidente DEV sin reproducción. |
| SC-02 · Sencillo · F-002, TRA-01–04/13, CAJ-04 | FR-CASH-004. Con saldo inicial 10, destino permitido y envío 1,0001, el shape de caja abierta que proyecta el código devuelve esperado null. RUN-02 caracteriza botón deshabilitado y cero POST. El doble heredado devuelve 10 y permite un único despacho; también verifica recepción simulada y recuperación de la intención. | **FAIL comercial en despacho E2**. E3 del GET, custodia/ledger y E4 BLOCKED. El test de caracterización pasa al observar el bloqueo. |
| SC-03 · POS · CAJ-01/08–15/18, CAT-01/02/08 | FR-POS. RUN-03 ejercita borrador, cantidades/presentaciones, recuperación, checkout, pago mixto y ticket o anulación con respuestas simuladas. | E2 parcial. Stock FEFO, pagos, comisiones, reversión, duplicación concurrente y cierre real BLOCKED. |
| SC-04 · Catálogo · F-003, CAT-03–07/09–14/16–21 | FR-CAT-001/006. RUN-04: producto en fila 51 ausente con filtro de página 1, visible tras ir a página 2, con uno y luego dos GET de lista. RUN-03 incluye revisión/versionado y recuperación de altas e importación. | E2 PASS para filtro de página (UC-CAT-20); alcance declarado. Mutaciones, Excel parcial y auditoría real BLOCKED. |
| SC-05 · Compras · F-004, COM-06–14 | Una recepción sin referencia queda fuera de búsqueda por referencia y es accesible con ID original. RUN-04 verifica ambas lecturas y cero escrituras. RUN-03 cubre recepción, revisión, respuesta perdida y referencia repetida sin tomarla como constancia. | E2 PASS parcial. Confirmación, rollback, recuperación persistida e inventario real BLOCKED. |
| SC-06 · Inventario · F-005, COM-01–05/15 | RUN-04 observa un GET de detalle en caché fría y el mismo total tras abrir Lotes para el mismo producto dentro de la ventana. RUN-03 cubre exactitud de ajuste, costo visible por rol y caché/scope. | E2 PASS parcial. Sin medir página multi-ID en red real, latencia ni reentrada de navegador. Efecto de movimientos y FEFO real BLOCKED. |
| SC-07 · Mercadería · TRA-05–12 | RUN-03 caracteriza solicitud, aprobación, lotes, recepción parcial y recuperación por clave. La aserción inmediata del GET de acceso de origen falló en el lote y pasó en RUN-05. | E2 parcial e intermitencia del test. Custodia, idempotencia persistida, concurrencia y aislamiento BLOCKED. |
| SC-08 · Reportes y comisiones · REP-01–17 | RUN-03 cubre métricas decimales, alcance de filtros, exportación interceptada, versión/estado de reglas, negativas y lecturas. | E2 parcial. Agregados contra ledger, reversión y reposición SQL BLOCKED. |
| SC-09 · Avisos/auditoría · AVI-01–07 | RUN-03 verifica contador, lectura ambigua y cambio de membresía; auditoría niega roles y oculta contenido de snapshots. | E2 parcial. Job, deduplicación e inmutabilidad real BLOCKED. |
| SC-10 · Ingreso, estructura y personal · ACC-01–27 | RUN-03 verifica admisión, denegaciones, 429/503, entidad/sede y recuperación, edición de personal y permiso por rol. RUN-06 cubre la invitación por clave, la aceptación desde sesión con fragmento removido, aprobación versionada sin userId del cliente, negativas y pérdida de respuesta. El primer Owner por Gmail exige vinculación autenticada y persistida. | E2 parcial. Google externo B-03; último Owner concurrente, reclamo, consentimiento y aislamiento real BLOCKED. |
| SC-11 · Plataforma · PLA-01–18 | RUN-03 verifica negativa de admisión, alta por correo/claves, respuesta confirmada seguida de un fallo de refresco, cambios auditables simulados y navegación. Faltó la proyección del destinatario dentro del tiempo del test de seis áreas, con lectura en curso; RUN-05 pasó. | E2 parcial e intermitencia del test. Sin persistencia ni autoridad demostradas en entorno real. |
| SC-12 · Contingencia e integraciones · INT-01–18 | RUN-03 verifica cola provisional con IndexedDB de prueba y conservación de ámbito al navegar y cambiar sucursal. RUN-06 verifica que el service worker generado excluye APIs privadas, auth, POST y otros orígenes de su caché. UBL, DIGEMID, Recovery, health y CPE conservan sus límites de exposición en las fichas. | E2 parcial. Posting/acuse PostgreSQL, PWA real y restauración BLOCKED. Sin llamadas externas. |

**Clasificación sin ejecución dedicada.** `CAT-22` es N/A porque el borrado físico no está expuesto como acción operativa. `ACC-02`, `ACC-12`, `INT-01` e `INT-16` conservan NOT_RUN para sus respuestas o composición específica. Las operaciones internas restantes y las integraciones deshabilitadas conservan su clasificación de alcance. Una prueba de bloqueo fail-closed o integración requiere el entorno correspondiente y no autoriza activar CPE.

## Reanudación E3 y E4

### Aserciones de aplicación y dominio en RUN-08

- SC-01/02/03: `CashCloseUseCaseTests`, `CashReconciliationDomainTests`, `CashTransferUseCaseTests`, `ConfirmSaleUseCaseTests`, `VoidSaleUseCaseTests` e `InternalTicketTests` ejercitan diferencia física, exclusión de medios digitales, autorización revalidada, despacho según pagos/reversiones/transferencias, negativos, FEFO, compensaciones y errores de posting. Los stores y transacciones son dobles y no prueban atomicidad SQL.
- SC-04/05/06/07/08: `SearchProductsTests`, `PurchasingWorkflowTests`, `PurchasingAuthorizationTests`, `AdjustStockTests`, `TransferUseCaseTests`, `CommissionConfigurationUseCaseTests` y `ReplenishmentAuthorizationTests` ejercitan validaciones, ausencia de efectos ante rechazo, historia, concurrencia reportada por el store y ámbito Owner de reposición. No hay persistencia real en este grupo.
- SC-09/10/11/12: `ExpirationJobTests`, `OperationalAccessTests`, `EmployeeInvitationApplicationTests`, `InitialOwnerOnboardingTests`, `OfflineSaleTests` y `SupplierUblStructureTests` ejercitan job, negativas por contexto, invitación y reclamo, reconciliación contable offline y análisis de XML sintético. El backoffice, RLS e integración real con proveedor permanecen fuera de esa evidencia.

### Secuencia prevista antes de recibir RUN-10 (histórica)

1. En runner autorizado con Docker local, confirmar el contenedor propio y el rol/versión por consulta de prueba, sin mostrar connection strings. Ejecutar desde `backend/` el piloto del fixture: `dotnet test tests/MediPOS.IntegrationTests --filter "FullyQualifiedName~CashClosePersistenceTests|FullyQualifiedName~CashSessionPersistenceTests|FullyQualifiedName~CashCloseIsolationTests|FullyQualifiedName~CashTransferPersistenceTests|FullyQualifiedName~CashTransferConcurrencyTests|FullyQualifiedName~CashTransferRecoverabilityTests" --logger "trx;LogFileName=rfa2-postgres-cash.trx" --results-directory "$result"`.
2. En el mismo destino efímero, caracterizar el hueco 100→10 exacto en copia temporal. Conservar ID sintético y contrastar acuse, caja y auditoría en DB, lista activa, GET por ID, alcance y ausencia de duplicados. Probar aparte que GET open informa esperado null y que despacho válido calcula saldo desde el ledger.
3. En E4, levantar exclusivamente app y perfil sintético aislados. Completar reentrada con URL original y limpia, probar Sencillo con la respuesta de la API real y contar GET de F-005 en página con varios IDs y cambio de scope. Después ejecutar los lotes PostgreSQL restantes de §4. La aceptación de pruebas externas requiere evidencia nueva.

