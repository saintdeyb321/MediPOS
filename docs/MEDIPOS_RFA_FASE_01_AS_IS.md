# MediPOS — RFA | Fase 01: inventario y radiografía AS-IS

**Encargo ejecutable · v1.1 · GPT-6.2 Sol / Medium / Standard · 26/11/2026**

## 0. Alcance y precedencia

Ejecutar exclusivamente RFA0–RFA1 del documento existente `docs/MEDIPOS_FUNCTIONAL_ARCHITECTURE_AUDIT.md` (master v1.1). Este archivo delimita la ejecución por fases y prevalece sobre cualquier autorización más amplia del master para este turno. Aplicar el gobierno permanente del master §0.1 (Git manual del operador, skills y revisión entre fases). Usar sus secciones 0–2 y la estructura de entregables de §5; consultar §3 únicamente para la escala de evidencia, §4 solo para clasificar indicios. No ejecutar RFA2–RFA5. Si el master falta, este encargo contiene el mínimo operativo para continuar; registrar la ausencia y no inventar requisitos.

Objetivo: reconstruir el comportamiento implementado de MediPOS desde la perspectiva del usuario y rastrearlo hasta persistencia, para que el operador recupere el control funcional del sistema. Obtener cobertura global antes de invertir en investigaciones de defectos. No confundir inspección del código con ejecución satisfactoria.

Repositorio local `C:\Proyectos\MediPOS`. Referencia histórica remota `7d453285252d2bf9279cb7c42e1ac95f4f8c70c6`, rama `ci/postgres-diagnostics-8bc6b4c`. Verificar realidad local; no cambiar rama ni revertir cambios. Mantener Blue Studio/UAX y decisiones vigentes.

## 1. Herramientas y límites

- Usar `ub-mode` instalada si su procedencia es verificable; aplicar principios de dominio, claridad y pasos pequeños compatibles con auditoría de solo lectura. Las restricciones de este encargo prevalecen sobre instrucciones de implementación, TDD, commit o push de la skill. Si no está disponible, el operador autoriza instalar únicamente `ub-mode` desde https://github.com/unclebob/ub-mode mediante el mecanismo admitido de Codex, conforme al master §0.1; revisar procedencia, licencia y SKILL.md, registrar versión/commit. Si no se puede instalar de forma segura o requiere permisos no disponibles, continuar sin ella. No cargar `frontend-skill`, GitNexus, Crapper, Dryer, Mutator ni otras herramientas adicionales en esta fase.
- Git, rg, referencias TS/C#, router, registros DI, endpoints, handlers, EF, migraciones/SQL/RLS relevantes y nombres/cuerpos de tests cuando aporten evidencia. No indexador nuevo ni subagentes por defecto. No navegación externa salvo la verificación/instalación autorizada de ub-mode o necesidad indispensable autorizada.
- Solo lectura de aplicación y escritura de documentación en `docs/audit/rfa/`. No modificar código, tests existentes, contratos, esquemas, dependencias, configuración, AGENTS, CI ni CURRENT_STATE histórico. No ejecutar suites, build, lint, servidor, migraciones o consultas a bases reales. No realizar transacciones ni escribir DEV/producción, no reiniciar, borrar storage, enviar CPE, purgar, commit/push/merge/deploy. No exponer secretos ni datos reales. Si hay scripts con efectos desconocidos, no ejecutarlos.

## 2. Orden de ejecución (un recorrido por fuentes, varios checkpoints)

### 2.1 RFA0 — Baseline e inventario transversal

1. Leer AGENTS aplicables, bloque vigente de `docs/CURRENT_STATE.md`, master RFA en las secciones indicadas y referencias de requisitos/ADR solo cuando afecten una operación. Identificar documentos vigentes: el Documento Maestro de septiembre es intención histórica, no prueba de implementación. Registrar conflictos y fuentes ausentes.
2. Registrar rama, HEAD, git status, diff resumido/huella de árbol local (incluidos archivos relevantes no rastreados), fecha y build identificado por evidencia local disponible sin arrancar servicios. No copiar diffs completos ni secretos. Diferenciar local/remoto/build/entorno: lo desconocido queda NOT_RUN/NO_DETERMINADO.
3. Inventariar una vez las rutas, menús, roles, permisos, acciones significativas, endpoints, trabajos automáticos, feature flags, integraciones y tests relacionados. Identificar endpoints sin consumidor visible y rutas profundas. Registrar evidencia de origen. No tratar nombre de test o endpoint como prueba de comportamiento.
4. Crear de inmediato README, USE_CASES, MATRIX y CHECKPOINT. Dar IDs estables a los casos conocidos y marcar filas provisionales pendientes de rastreo. Completar el inventario transversal antes de profundizar en una familia. Checkpoint RFA0.

### 2.2 RFA1 — Caja y Arqueos como caso patrón

Rastrear apertura, consulta de caja actual, listado de activas, cierre, arqueo/diferencia, recuperación y consulta posterior, según roles y contratos locales. Para el reporte del operador (apertura S/100, cierre contado S/10, listado vacío), distinguir por lectura: persistencia, lista de activas, historial, recuperación por ID, caché, permisos, navegación y build. Con cero movimientos, verificar fórmula del código y anotar como expectativa S/100 esperado, S/10 contado, diferencia -90 si se usa contado menos esperado. NO ejecutar la operación ni afirmar que el caso real se guardó. Registrar hipótesis separadas de defectos demostrables estáticamente y enlazar pruebas existentes sin ejecutarlas. Dejar receta reproducible para RFA2. Validar consistencia entre ficha, diagrama, matriz y hallazgo. Checkpoint Caja.

### 2.3 RFA1 — Cobertura por familias

Continuar automáticamente en orden, sin pedir otro prompt entre familias. Reutilizar patrones y contratos comunes; no releer archivos intactos. El alcance real proviene del inventario; esta tabla indica áreas a investigar, no capacidades que se presuman implementadas.

| Familia | Operaciones a rastrear |
|---|---|
| Caja y POS | Completar POS, búsqueda, carrito, pago, venta, anulación, ticket y relación con caja |
| Productos y catálogo | Crear, asociar, editar, activar/desactivar, eliminar si existe, presentaciones, precios, categorías, búsqueda, Excel y recuperación |
| Compras e inventario | Proveedores, borradores, líneas, lotes, confirmación, consultas, stock, mínimos, ajustes, FEFO, vencimientos |
| Transferencias | Mercadería y efectivo, estados, custodia, solicitud, aprobación, despacho, recepción, cancelación y recuperación |
| Reportes y comisiones | Filtros, alcance, agregaciones, cálculo/reversión y exportación |
| Notificaciones y auditoría | Lectura, contador, acción, historial y trazabilidad |
| Acceso, personal, sedes y configuración | Login, invitaciones, onboarding, roles, horarios, sesión, sedes, licencia y datos fiscales |
| Superadmin | Tenants, licencias, catálogo, soporte, suspensión y purga solo por inspección |
| PWA, contingencia e integraciones | Caché, offline, outbox, sync, conflictos, jobs, CPE/DIGEMID según estado real |

Por cada acción significativa: actor → entrada → UI/estado/hook → HTTP/DTO → endpoint/autorización → handler/reglas → transacción/EF/SQL/RLS → persistencia/auditoría → respuesta/caché → consulta posterior. Leer código suficiente para justificar cada enlace; anotar explícitamente las fronteras no inspeccionadas. Para consultas y trabajos automáticos adaptar el recorrido sin inventar UI o escritura.

Cada familia termina con fichas canónicas, matriz actualizada, enlaces de evidencia, indicios y checkpoint. Si algo está bloqueado, registrar causa y continuar lo independiente. No expandir la investigación de un indicio a toda la arquitectura: pasar a la siguiente familia una vez que su flujo esté documentado.

## 3. Formato de salida funcional

Una ficha `UC-{FAMILIA}-{NN}` por operación significativa, variantes junto a la ficha. Escribir primero lenguaje de botica y después detalle técnico. Enlazar IDs FR/BR/UC de requisitos cuando se puedan verificar. Ejemplo de estructura (no inventar los pasos):

- Objetivo, actor/rol, sede, permisos y precondiciones.
- AS-IS usuario: pasos numerados desde entrada al módulo hasta resultado; acciones siguientes realmente disponibles (consultar, editar, inhabilitar, anular, etc.). Indicar si una acción es inaccesible o inexistente según evidencia. No llamar eliminar a desactivar.
- AS-IS técnico: ruta, componentes/hook, método/endpoint, DTO, autorización, handler, entidad/tabla, transacción, efectos y caché. Referencias `ruta:línea` + símbolo + baseline; compartir referencias comunes sin repetir bloques de código.
- Validaciones, estados, transiciones, alternativas, cancelación, error, reintento y resultado incierto; postcondición, recarga y recuperación. Para procesos con estados, tabla evento/origen/guarda/destino/actor/efecto.
- Requisito relacionado y diferencia observada por inspección. TO-BE: PENDIENTE_RFA3, salvo indicar pregunta a resolver. No diseñar solución definitiva ni alterar reglas ahora.
- Implementación: IMPLEMENTADO / NO_EXPUESTO / DESACTIVADO / NO_IMPLEMENTADO / NO_DETERMINADO, con restricciones de acceso por rol separadas. Evidencia E1 solo cuando el trayecto indicado fue inspeccionado; E0 cuando no. Resultado de ejecución NOT_RUN (o BLOCKED con motivo, N/A justificado). No otorgar PASS por inspección. Tests existentes: referencia y qué intentan demostrar, no resultado.

Mermaid `flowchart` o `stateDiagram-v2` solo para recorridos con decisiones/estados relevantes; un diagrama por flujo o familia coherente, no uno por función ni duplicado de cada ficha. Usar IDs estables, sintaxis estándar y diferenciar hipótesis de comportamiento. Diagramar ramas de recuperación y consulta posterior cuando existan. No instalar renderizador.

## 4. Entregables incrementales

Usar la misma estructura del master, no crear un segundo sistema documental:

- `docs/audit/rfa/README.md`: baseline, mapa de módulos y dependencias funcionales, cobertura, enlaces y limitaciones.
- `docs/audit/rfa/USE_CASES.md`: índice por módulo/actor, acciones, ID y enlace a ficha; inventario de rutas/endpoints sin caso con justificación.
- `docs/audit/rfa/flows/*.md`: fichas y Mermaid por las nueve familias del master. Enlazar, no repetir fichas.
- `docs/audit/rfa/MATRIX.csv`: `uc_id,module,actor,entry,action,implementation,evidence_level,verdict,requirement_ids,finding_ids,flow_ref,test_ref`. Una fila por caso/variante relevante, CSV válido, IDs únicos para casos y variantes identificables, denominadores reales. Mantener inventario de rutas/acciones/endpoints y sus referencias o razón de exclusión en el índice; no declarar cobertura global solo por número de módulos.
- `docs/audit/rfa/FINDINGS.md`: indicios `F-{NNN}`, síntoma, UC, evidencia, impacto/severidad provisional, causa estática demostrada o hipótesis, prueba propuesta y estado PENDIENTE_RFA2/RFA3. No afirmar pérdida de datos por listado vacío. No inventar severidad definitiva.
- `docs/audit/rfa/CHECKPOINT.md`: única continuidad; fase/paso, baseline, modelo, familias y UC completados/pendientes, fuentes/contratos ya inspeccionados, artefactos, indicios, bloqueos, siguiente familia/UC exacto y próxima fase. Actualizar después de inventario, Caja y cada familia; antes de compactar o finalizar. No duplicar fichas. Si ya existe documentación RFA, preservar e integrar evidencia vigente, invalidar solo lo afectado por el nuevo baseline.
- `ROADMAP.md` queda reservado para RFA3; si existe, no sobrescribirlo ni adelantar propuestas. Sin HTML/PDF ni nuevos paquetes en esta fase.

## 5. Economía de contexto y cierre

- Leer una vez instrucciones y fuentes compartidas; buscar con rg/git y luego abrir rangos relevantes. Leer cuerpo completo de un caso cuando haga falta para entender sus reglas. No volcar árboles, logs, lockfiles, código de terceros ni resultados masivos al contexto.
- Priorizar amplitud antes que profundidad: inventario global primero, Caja patrón, familias sucesivas. Reutilizar hechos vigentes en archivos; escribir documentación incrementalmente, no duplicarla en el chat. No hacer auditoría visual general, medir bundle, complejidad, mutación ni ejecutar tests. No perseguir un defecto más allá de lo necesario para documentar su recorrido y evidencia estática.
- No asumir acceso a cuota restante. Si el operador interrumpe o la sesión termina, los checkpoints ya guardados son el punto de reanudación. Ante reanudación verificar solo cambios relevantes y continuar pendientes. No compactar a costa de perder IDs o referencias. No imponer límite arbitrario de tokens ni garantizar que el cupo alcance.
- Gate fase 1: reconciliar inventario, fichas, matriz y referencias; cada ruta/acción significativa y endpoint operativo debe estar mapeado a un caso/variante, uso interno o exclusión justificada. Distinguir inventario completo de casos trazados completos; si faltan familias, declarar FASE_01_PARCIAL y sus pendientes. Verificar archivos/links locales, IDs y CSV sin ejecutar aplicación. `git diff --check` y status final solo para revisar cambios documentales; no revertir cambios ajenos. Aplicar el gobierno Git del master §0.1; no stagear ni publicar cambios, ni intentar eludir autenticación.
- Entrega final compacta: baseline, número de rutas/acciones/UC inventariados y trazados (con denominadores), familias completas/pendientes, principales indicios con referencias, rutas de artefactos y estado FASE_01_COMPLETA/PARCIAL/BLOCKED. No declarar funcionamiento comprobado ni GO producción. Detenerse en RFA1, incluso si hay cupo. RFA2 requerirá revisión de estos artefactos por el operador y un nuevo encargo explícito. Dejar lista la carpeta canónica para esa revisión; no iniciar la fase siguiente aunque quede cupo.

## 6. Calendario lógico de modelos (orientativo, no ejecutar aquí)

| Fase | Modelo / esfuerzo | Gate |
|---|---|---|
| 1 RFA0–RFA1 | GPT-6.2 Sol / Medium | Radiografía AS-IS trazable y checkpoint |
| 2 RFA2 | GPT-6.2 Sol / High | Evidencia conductual aislada; defectos y bloqueos reproducibles |
| 3 RFA3 | GPT-6.2 Astra / High | Diagnóstico y TO-BE, backlog y aprobación de cambios |
| 4A RFA4 | GPT-6.2 Astra / High | Correcciones críticas autorizadas y pruebas |
| 4B RFA4 | GPT-6.2 Sol / High | Refactor y flujos aprobados, sin regresiones |
| 5A RFA5 | GPT-6.2 Sol / Medium | Gates técnicos y evidencias de ejecución |
| 5B RFA5 | GPT-6.2 Astra / High | Revisión de riesgos y GO/NO-GO; aceptación externa independiente |

Los modelos se seleccionan manualmente según disponibilidad y cuota. Extra alto solo para bloqueos de razonamiento justificados. No cambiar modelo automáticamente ni iniciar siguiente fase.
