# MediPOS — Plan UX/IX V4 y cierre de usabilidad del MVP

**Fecha:** 2026-10-26  
**Proyecto:** MediPOS — SaaS para boticas en Perú  
**Baseline público revisado:** rama `ci/postgres-diagnostics-8bc6b4c`, commit `3e7e7e9a2756ad7a544d048d369364a19958eefa`  
**Fuente visual:** capturas entregadas por el propietario de Productos, POS, Inventario y Compras  
**Propósito:** corregir UX/IX, detectar y cerrar defectos de flujo, hacer explícito qué partes del MVP están implementadas y preparar la aceptación manual del propietario.  
**Estado:** PLAN. **No** declara funcionalidades completas, correcciones aplicadas ni aceptación visual.

> **Objetivo de negocio:** el propietario/cajero de una botica debe entender qué puede hacer, completar cada operación y recuperarse de un error sin interpretar un sistema técnico. Reducir ruido, no esconder riesgos financieros, médicos o de inventario.

## 0. Regla fundamental: separar tres tipos de problema

1. **UX/IX:** interfaz mal centrada, ayuda extensa, botones duplicados, pasos poco claros, confirmación errónea de descarte, navegación confusa.
2. **Defecto funcional:** el botón no dispara la acción esperada; guarda, pero la UI no refresca; un rol no puede completar el flujo; ruta rota; una transición impide continuar.
3. **Brecha de producto/backend:** falta endpoint, permisos, persistencia de justificación, invitación de Owner por email, catálogo paginado por categoría/stock para cajeros, series temporales de reportes. **No se soluciona ocultando la ausencia bajo un modal.**

Toda corrección debe identificar la categoría, la evidencia, el responsable y si requiere autorización para backend/migración. Nunca declarar funcional un flujo basándose solo en `npm.cmd run build`.

## 1. Evidencia inicial: observación vs. hipótesis

| ID | Evidencia aportada / confirmación en código | Lectura | Trabajo exigido |
|---|---|---|---|
| UX-001 | El modal «Agregar al ticket» se ve en esquina superior izquierda | Defecto visual observado en Chrome | Corregir posicionamiento del `Modal` real, en top layer, en todas las rutas; **no** centrar drawers intencionalmente laterales. |
| UX-002 | El drawer «Detalle del producto» está anclado a la derecha | **No** es el mismo defecto: es un drawer lateral | Mantener drawer si sirve a consulta lateral, pero reestructurar texto, datos y acciones. |
| UX-003 | «Paracetamol» muestra marca, información medicinal y advertencias como texto corrido | Baja legibilidad, agrupación y jerarquía | Ficha semántica de datos, definiciones alineadas, advertencias contextuales y ayuda bajo demanda. |
| UX-004 | Tras guardar presentaciones, cerrar puede mostrar «Seguir editando / Descartar cambios» | Defecto relatado, **causa probable localizada** en estado `dirty` | Distinguir cambios sin guardar, escritura confirmada y resultado incierto; resetear `dirty` solo tras confirmación verificada. |
| UX-005 | `ProductsPage.tsx` configura `dirty={dirty || task === 'units'}` para editar presentaciones | Confirmado en código: incluso una tarea `units` sin cambios solicita descartar | Corregir a dirty basado en cambios reales de formulario. |
| UX-006 | `frontend/src/app/navigation.ts` marca `empleados` con `availability: 'partial'` | Confirmado en código, **no demuestra qué funciones faltan** | Auditoría de cobertura de Personal: invitación, aceptación, aprobación, sedes, horarios, estados, roles y disponibilidad por perfil. |
| UX-007 | Inventario muestra explicaciones largas, dos métricas en línea y ayuda visible | Ruido y bajo contraste entre consulta y soporte | Reducir contenido primario, mover explicación a ayuda local, estructurar estados vacíos. |
| UX-008 | Compras utiliza cinco etapas de recepción, panel lateral y formulario en página | Secuencia válida pero carga vertical elevada | Asistente focalizado en modal ancho o vista dedicada según complejidad, con pasos, persistencia del borrador y regreso contextual. |
| UX-009 | Producto sin presentaciones activas ni stock no puede agregarse al POS | Bloqueo de dominio **correcto**, no necesariamente defecto | Orientar al Owner: «Configurar presentaciones» → «Registrar recepción»; no permitir ventas ficticias. |
| UX-010 | `WorkspaceShell.tsx` reserva barra superior para botica/sede; sidebar también presenta ambos | Redundancia confirmada | Consolidar contexto en sidebar; mantener únicamente navegación contextual y aviso de estado útil en cabecera, si procede. |
| UX-011 | Algunos botones parecen no hacer nada | Observación del usuario aún **sin reproducción ni ruta identificada** | Inventario exhaustivo de acciones: componente → controlador → llamada API → respuesta → refresco o navegación. |
| UX-012 | POS y dashboards fueron refactorizados V3, pero siguen exigiendo aceptar UX en navegador | Código no equivale a aceptación | Revisar visibilidad, estados vacíos, roles y workflows en 1366×768, 390 px, zoom 200%. |

### Pistas de implementación verificadas en GitHub

- `frontend/src/shared/ui/Modal.tsx`: `Modal`, `Drawer`, propiedad `dirty`, `busy`, `recovery`, confirmación de descarte, gestión de foco.
- `frontend/src/shared/ui/Dialog.tsx`: HTML `<dialog>` con manejo de teclado/cancelación.
- `frontend/src/shared/ui/ui.css`: `.task-dialog` (posición fija, `inset: 0`) y `.task-drawer`; verificar **estilos computados en Chrome** para identificar por qué el modal aparece arriba a la izquierda. La causa exacta no debe darse por demostrada sin inspeccionar computed styles.
- `frontend/src/features/catalog/ProductsPage.tsx`: abre/cierra modal de creación y de tarea, drawer de detalle; manejo de `dirty` y `task`.
- `frontend/src/features/catalog/ProductDetailPanel.tsx`: tabs Datos, Precios, Presentaciones, Lotes/stock; editores separados.
- `frontend/src/features/inventory/InventoryPage.tsx`: modales de ajuste y mínimo; callbacks `onSaved` presentes, comprobar que el cierre coincida con escritura confirmada.
- `frontend/src/features/purchasing/PurchasesPage.tsx` y `ReceiptEditor.tsx`: pasos de borrador, producto, lote/costo, revisión y confirmación.
- `frontend/src/features/employees/EmployeesPage.tsx`: ficha en drawer y tareas en modal; navegación marca el módulo como `partial`.
- `frontend/src/app/layouts/WorkspaceShell.tsx` y `WorkspaceHeader.tsx`: estructura global y barra de contexto.

## 2. Principios UX/IX obligatorios

- **Visibilidad del estado:** en cada mutación distinguir `sin cambios`, `editando`, `guardando`, `guardado y reconsultado`, `rechazado`, `resultado incierto/conciliación`.
- **Correspondencia con el trabajo real:** hablar de «tabletas», «cajas», «lotes», «vencimientos», «efectivo esperado»; no mostrar UUID/versiones/UTC como explicación principal.
- **Reconocimiento antes que memoria:** contextualizar acciones en donde se necesitan; no obligar a adivinar que hay que pasar de Productos a Compras para generar stock.
- **Prevención y recuperación:** desactivar envíos inválidos con razón visible; nunca repetir un POST si hubo respuesta incierta. Ofrecer verificación sin resetear la operación original.
- **Divulgación progresiva:** explicación larga en «¿Cómo funciona?» / «¿Cómo interpretar?» y no como varios párrafos permanentes; **las advertencias que impiden pérdidas o errores irreversibles siguen visibles**.
- **Consistencia:** mismos tamaños, posiciones de acciones, labels, rutas, foco, espaciados y estilos en modales de todos los módulos.
- **Accesibilidad:** WCAG 2.2 AA como objetivo; lector de pantalla, teclado, 200% zoom, foco visible, labels explícitos; iconos con texto accesible.
- **Honestidad:** no presentar una funcionalidad como completa si la API o permisos aún la limitan.

Referencias: WAI-ARIA Authoring Practices [Modal Dialog Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/), [Modal Dialog Example](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/examples/dialog/), [WCAG 2.2](https://www.w3.org/TR/WCAG22/). Aplicar la norma al patrón real; no traducirla a reglas arbitrarias que perjudiquen usos con teclado o móvil.

## 3. Sistema de superficies: qué se abre y cómo

| Tipo de superficie | Usar para | Ubicación y dimensiones | Evitar |
|---|---|---|---|
| **Modal corto** | Confirmar, abrir caja, elegir presentación, ajustar precio simple, guía puntual | **Centrado horizontal y verticalmente** sobre viewport, ancho según contenido (~420–640 px), `max-height: min(90dvh, …)`, overflow interno, foco visible | Posicionarlo top-left o ocupar todo el ancho sin motivo. |
| **Modal ancho con pasos** | Alta producto, configuración de presentación, recepción manejable, invitación | Centrado, aprox. 720–960 px en escritorio, máximo viewport seguro; encabezado, progreso y acciones consistentes; scroll solo interior | Montar modales dentro de otros modales o perder borradores entre etapas. |
| **Drawer lateral** | Consulta contextual de ficha de producto, empleado, lote, ticket | Anclado a **derecha** (o lado definido por diseño), ancho acotado, scroll interno | Confundir drawer con error de modal sin centrar. |
| **Ruta dedicada** | Operación extensa con muchas líneas/lotes y recuperación de borradores, importación masiva, conciliación crítica | Página focalizada con título, breadcrumb y volver; no héroe gigante | Convertir un formulario de 30 campos en modal imposible a 200% zoom. |
| **Popover/ayuda** | Conceptos de campo, «cómo calcular», «qué significa» | Anclado a disparador, teclado y cierre claros | Solo tooltip hover; icono solo-color sin nombre. |

### Corrección del centrado global

- Diagnosticar CSS **computado** para el `<dialog>` real (incluyendo `margin`, `top`, `left`, `transform`, estilos UA, viewport, stacking context, top layer y especificidad). Revisar si `inset: 0` sin un centrado explícito genera anclaje superior/izquierdo en Chrome.
- Aplicar centrado explícito, por ejemplo una estrategia `margin: auto` sobre dialog nativo con tamaño restringido; la decisión final depende de estilos computados. **No** trasladar la solución al `.task-drawer`, que deliberadamente usa anclaje lateral.
- Probar modal normal y ancho, confirmación de descarte, estado `busy`, estado `recovery`; evitar scroll del fondo. Móvil: márgenes seguros o pantalla completa cuando el contenido lo requiera.
- Páginas con scroll no deben desplazar el modal. Pantallas 1366×768, 1024×768, 390×844, zoom 200%, contenidos largos.
- Evitar modal anidado y `z-index` extremo; preferir `<dialog>.showModal()` ya implementado.

## 4. Máquina de estados de edición obligatoria

No basta con `setDirty(true)` en cualquier `onChange` y esperar a que `Modal` lo resuelva.

```text
CERRADO
  ↓ abrir (cargar versión de servidor)
LIMPIO ── cambiar campo realmente ──> MODIFICADO
  ↑   └─ cancelar sin cambios ───────> CERRADO
  │
MODIFICADO ── guardar ───────────────> GUARDANDO
  └─ salir ──────────────────────────> CONFIRMAR DESCARTE
GUARDANDO ── escritura confirmada + relectura ──> GUARDADO
GUARDANDO ── error definitivo ───────> MODIFICADO + ERROR
GUARDANDO ── respuesta incierta ────> RECUPERACIÓN (sin reenviar)
GUARDADO ── cerrar ──────────────────> CERRADO (sin confirmar descarte)
GUARDADO ── modificar otra vez ──────> MODIFICADO
RECUPERACIÓN ── salir seguro ────────> PENDIENTE EN VISTA (sin borrar intención)
```

**Invariantes:**
- `dirty` = diferencia real frente a versión base, **no** «el usuario entró a la pestaña Presentaciones» ni «el formulario ha recibido algún evento».
- Al confirmar escritura y reconciliar lectura se actualiza base, se limpia `dirty`, se muestra confirmación breve y puede cerrarse automáticamente **solo si no se pierde un contexto que el usuario necesite**.
- Guardar fallido o resultado incierto **no** debe limpiar `dirty` ni permitir repetición insegura.
- Cambiar entre tabs no es cambio de datos.
- Si el usuario vuelve al valor original, el formulario vuelve a estado limpio.
- El mensaje «Descartar cambios» nunca aparece después de guardar satisfactoriamente cuando no hay ediciones posteriores.
- Cuando hay versión optimista, recarga/versionado y conflictos, conservar su control y pedir revisión; no sobrescribir silenciosamente.

### Aplicación por módulo

**Productos:** en `ProductsPage.tsx` eliminar condición equivalente a `dirty || task === 'units'`; `EditPrices`, `UnitEditor` y `CreateProductForm` deben notificar transiciones de estado verificables; reset de dirty cuando `write.notice/observed` confirma y reconciliación finaliza. No inferir éxito de mensaje HTTP sin relectura según contrato.

**Inventario:** examinar `MinimumForm` y `AdjustmentForm`, callback `onSaved`, modal y resultado de `useInventoryWrite`: cerrar únicamente al terminar y validar resultado; si la respuesta es incierta, mantener marcador y salida segura.

**Personal:** la edición de sucursales, horario y estado debe manejar su propio estado base; el modal se cierra tras guardar confirmado, sin continuar bloqueado por flags de una pestaña anterior.

**Compras:** pasos del wizard conservan estado local y servidor, sin borrar líneas ya guardadas. Cerrar un borrador existente permite retomarlo por su identidad original; ante confirmación incierta reconsultar, **no** confirmar otra vez.

## 5. Fichas legibles y ayuda contextual para todas las edades

### Ficha Producto — Información medicinal

**Antes observado:** párrafo denso con marca, código, concentración, forma, registro y advertencia.

**Después exigido (ejemplo de composición, no modificación de datos):**

```text
[Paracetamol] [Medicamento] [Activo]
Código: 1                  Laboratorio: labo1

DATOS DEL MEDICAMENTO                      [ ? Cómo interpretar ]
Principio activo          PARACETAMOL
Concentración registrada  500 GR   [!] Revisar unidad documentada
Forma farmacéutica       nose             [!] Dato a revisar
Vía de administración    No informada
Registro sanitario       No informado      [ ? Qué significa ]

[Configurar presentaciones]  [Registrar recepción]
[MÁS ACCIONES: Precios · Estado]
```

**Advertencia:** mostrar fielmente `500 GR` y `nose` tal como están guardados; no cambiarlos automáticamente a `500 mg` ni inventar registro sanitario. La capacidad de **editar datos medicinales** requiere inspección de contratos reales y aprobación si falta backend.

### Patrón «ayuda bajo demanda»

- Disparador visible en el lugar donde el usuario duda, preferentemente `?` + texto corto: «¿Cómo funciona?» o «¿Qué significa?». No colocar iconos crípticos como única opción para lectores mayores.
- Instrucción breve junto al campo (**máximo 1–2 líneas**); explicación ampliada en popover accesible o disclosure `<details>`, según longitud. Explicar con un ejemplo de botica.
- Ayuda local por módulo: «¿Qué es una unidad base?», «¿Cómo registrar vencimiento?», «¿Cómo interpretar stock vendible?», «¿Qué significa caja por conciliar?», «¿Qué puedo hacer con una invitación?».
- Las advertencias críticas quedan visibles: venta sin stock, lote vencido, falta de autorización, cierre de caja, monto inválido, operación incierta; ayuda no sustituye feedback de error.
- `?` debe tener `aria-label` claro, foco, Escape, control por teclado y objetivo clicable adecuado. No incluir enlaces que lleven fuera del paso y borren formulario.
- Debe haber consistencia de términos: botica / sede / producto / presentación / lote / recepción / caja / arqueo / ticket.

## 6. Barra superior, navegación y retornos

- El sidebar ya muestra `BOTICA DEMO` y la sede seleccionada. La barra superior `BOTICA DEMO / Sede Concepcion` es redundante y en las capturas aporta altura sin acción.
- Eliminar **solo la redundancia**. Conservar un encabezado compacto con menú móvil, accesos accesibles al contexto de sede, notificaciones o estado si resultan útiles; nunca dejar al usuario sin conocer la sede activa.
- Para una **ruta de submódulo**: breadcrumb + `Volver a [padre]` contextual. Evitar dos botones de regreso.
- Para **modal**: botón cerrar, Cancelar y anterior/siguiente en un único pie de acciones; al cerrar, devolver foco al disparador.
- Para **wizard**: barra de pasos visible, anterior/continuar, último paso revisar/confirmar. Tabs son navegación de estados; si son pasos secuenciales no permitir saltarse validaciones sin avisar.
- Rutas profundas, actualización de página, botón Atrás del navegador, cambio de sede y menú móvil deben preservar contextos y marcadores pendientes sin escrituras extra.

## 7. Compras e inventario: reducir desplazamiento sin perder seguridad

### Recepción — flujo propuesto

`Compras [Nueva recepción]` → **modal ancho guiado** si los pasos y lotes caben cómodamente; en recepciones extensas, **ruta dedicada compacta** con equivalente secuencia, NO un modal imposible.

1. **Proveedor y documento:** campos y elección simple, conservar proveedor opcional si el contrato lo permite.
2. **Productos:** buscar y elegir presentación, en bloque acotado.
3. **Lotes, cantidades y costos:** número de lote, vencimiento por línea, cantidad/costo por presentación; controles visibles y validaciones documentadas.
4. **Revisión:** totales exactos, destino y líneas; acciones anteriores accesibles.
5. **Resultado:** consulta real del borrador o confirmación, acceso Inventario.

Reglas de seguridad: creación del borrador no ingresa existencias; solo confirmación genera inventario. No mutar estructura de lotes, FEFO ni ledger. En pérdida de respuesta, reconsultar por ID existente, nunca duplicar recepción.

### Inventario

- Pantalla principal: título único, máximo 2–4 KPIs realmente útiles, filtros, tabs `Existencias | Lotes | Próximos a vencer`, resultados visibles sin atravesar párrafos.
- Los filtros por producto deben mostrar claramente si filtran página actual o datos completos. No llamar «todos» a una página parcial.
- Ayuda «¿Cómo interpretar existencias?» bajo demanda; avisos críticos visibles.
- Desde un producto sin stock: ruta operacional guiada a **configurar presentaciones → registrar recepción → verificar inventario**, diferenciando capacidad (hay endpoint) de recomendación UX.

## 8. Cobertura del MVP: matriz de flujos que la IA auditora debe evaluar

| Área | Caso feliz | Error/alternativa | Identidad mínima |
|---|---|---|---|
| Auth/selección | Google → botica → sede → rol | sin botica/licencia/sede, sesión caducada | Google real y UUID interno solo en backend |
| Plataforma | crear tenant y licencia → Owner inicial | no autorizado, duplicado/idempotencia, trial vencido | superadmin temporal sin acceso global privado |
| Productos | crear → editar precios → presentaciones → ver stock | datos medicinales incorrectos, duplicados, sin presentaciones | Owner |
| Compras | borrador → líneas con lote/vencimiento → confirmar | cantidad/costo inválido, no stock antes de confirmar, respuesta incierta | Owner/farmacéutico autorizado |
| Inventario | existencias → lotes/vencimiento → ajustes/mínimos | permisos, stock cero, conciliación de escritura | Owner/farmacéutico |
| POS | buscar/explorar → carrito → caja → borrador → cobro | lote vencido, stock insuficiente, doble clic/pago incierto | cajero autorizado y caja vigente |
| Caja | abrir → vender → arqueo → cerrar | caja antigua, dos cajas, diferencia, respuesta incierta | rol por contrato |
| Personal | invitar → aceptar con Google → aprobar → asignar sede/horario → desactivar | invitación expirada, actor distinto, roles no autorizados, `partial` | Owner, invitado/empleado |
| Transferencias | preparar → despachar → recibir | lote diferente, pendiente, sucursal indebida | roles y sedes |
| Reportes | filtrar → métricas/grafica → exportar | paginado, sin series, permisos, 0 real vs sin datos | Owner |
| Configuración | entidad legal → sucursal → preferencias | datos faltantes, permisos, respaldo legal | Owner |
| Contingencia | preparar → operar dentro de límites → conciliar | conflicto, duplicidad, permisos vencidos | roles según contrato |
| Notificaciones/auditoría | ver y gestionar según contrato | vacíos, permisos, accesibilidad | actor autorizado |

**Personal `partial`:** identificar por qué el menú lo etiqueta así. El backend actual usa **invitación por enlace**, no asumir envío automático por email ni «Reenviar invitación» si el secreto no es recuperable. Reportar alcance honesto y brecha MVP.

## 9. Inventario de botones / rutas / permisos — procedimiento de auditoría

Para **cada acción visible** en cada perfil:

`Texto exacto del botón → archivo/componente → onClick/onSubmit → hook/controlador → cliente HTTP/función → endpoint → autorización → mutación/lectura → respuesta → cambio de estado/feedback`.

Anotar:
- Acción activa que no tiene handler; handler vacío; evento cancelado; botón sin contexto; `disabled` permanente por condición incorrecta.
- Botón que **sí hace algo**, pero sin feedback visible, fuera de viewport o con un error oculto.
- Duplicidad de acción, ruta/estado inaccesible, botón de atrás que pierde borrador o contenido.
- Navegación que se rompe al refrescar o en otro rol/sede.
- Escribir datos y perderlos al cambiar tab, cierre y recarga.
- API que falla por contrato, versión o permisos; **no** confundir limitación intencional con botón roto.
- Reconciliación de escritura `unknown`: no repetir POST/PUT; persistir marcadores y permitir recuperación real.

Si no se puede ejecutar el código, clasificar **ESTÁTICO / NO REPRODUCIDO**; nunca declarar botón roto solo por intuición.

## 10. Sistema visual y medición de UX

- **Tokens:** espaciado de 4/8/12/16/24/32 px, padding de modal consistente, radio consistente, contraste semántico, sin efecto arcoíris.
- **Tamaño mínimo visual objetivo:** controles primarios cómodos (≈40–44 px), área objetivo WCAG 2.2 de al menos 24 CSS px salvo excepciones; texto normal 16 px; información esencial no en microtexto.
- **Simetría:** grid consistente para formularios de 2 columnas, acciones alineadas, cards altura flexible sin truncar nombres medicinales, sin CTA perdido al fondo.
- **Modal:** centrado y legible a zoom 100/200%, sin overflow horizontal, foco y pie de acciones visibles.
- **Página principal:** una acción dominante, subacciones agrupadas; orientación rápida en ~5 segundos como criterio cualitativo, no garantía de tiempo exacta.
- **Carga:** skeleton solo en consultas reales, estados vacíos con próxima acción, errores legibles y trazables.
- **No inventar gráficos:** reportes y comparaciones solo desde series/agrupaciones autorizadas; `0` no equivale a no disponible.
- **No inventar edición clínica:** conservar el dato original e indicar que requiere corrección documentada.

## 11. Sprints correctivos después de la auditoría independiente

| Sprint | Enfoque | Definición de terminado |
|---|---|---|
| **V4.0 — Bloqueadores inmediatos** | Centrado modal, cierre tras guardar, bandera dirty, foco, ayuda contextual básica, barra superior redundante | Modales cortos centrados; drawer lateral intacto; guardar confirmado sale sin «Descartar»; navegación sin regresos duplicados. |
| **V4.1 — Productos + inventario** | Ficha medicinal legible, tabs, ayuda, ruta de primer stock, filtros y ajuste | Crear/configurar/consultar/editar fluye sin perder estado ni falsos avisos. |
| **V4.2 — Compras + caja + POS** | Wizard apropiado, compra por lotes, cierre y arqueo, operaciones pendientes y ayuda | Los pasos se pueden recorrer y corregir; sin duplicar recepción/cobro/cierre. |
| **V4.3 — Personal + onboarding + permisos** | Completar/etiquetar correctamente módulo parcial; invitación y asignaciones, feedback | Matriz Owner/cajero/farmacéutico resuelta; brechas honestamente documentadas. |
| **V4.4 — Resto + coherencia transversal** | Transferencias, configuración, reportes, plataforma, contingencia, botones inertes y responsive | Mismos patrones y ayuda; navegación coherente; riesgos no escondidos. |
| **V4.5 — Cierre del MVP** | Implementar únicamente hallazgos priorizados P0/P1 y P2 esenciales; pruebas manuales por propietario | Sin P0/P1 abiertos; flujos críticos demostrados; lista clara de pendientes no-MVP. |

**No iniciar sprints por inercia.** Primero la IA auditora entrega hallazgos reproducibles/citables y una matriz de cobertura. Luego Codex corrige por lote de causa raíz. Evitar ciclos de rediseño sin cierre de defectos.

## 12. Severidad, aceptación y límites

- **P0 (bloqueante):** corrupción o duplicidad financiera/stock, exposición de datos, permiso indebido, transacción irreconciliable. No operar con datos reales.
- **P1 (alta):** no se puede completar acción esencial del MVP, modal inaccesible, cierre incorrecto persistente, guardado confirmado que deja al usuario bloqueado.
- **P2 (media):** terminología confusa, navegación con fricción, ayudas extensas, errores de densidad visual que entorpecen el trabajo.
- **P3 (baja):** pulido cosmético sin impacto funcional.

### Entregables de la auditoría externa

1. `AUDITORIA_MEDIPOS_MVP.md`: resumen ejecutivo, metodología, alcance inspeccionado y límites, matriz de funcionalidades (completa/parcial/no implementada/bloqueada/no verificable), mapa de navegación y flujos por rol.
2. `HALLAZGOS_MEDIPOS.csv` o tabla Markdown: ID, severidad, módulo, ruta, pasos, esperado, observado, evidencia **archivo:línea** o screenshot, causa raíz probable, evidencia faltante, dueño (frontend/backend), riesgo, recomendación.
3. `BACKLOG_CORRECTIVO_Codex.md`: agrupación por **causa raíz** y sprint, cambios propuestos, dependencias y criterios de aceptación; no implementar cambios sin aprobación.
4. Lista de pruebas manuales **breve** para el propietario, centrada en acciones reales; no suites masivas.

### Condiciones de aprobación

- No se admite «todo PASS» por build.
- Todo botón importante tiene resultado visible o limitación explícita.
- Reglas de inventario, efectivo, idempotencia, sesiones Google, RLS y multi-tenant siguen intactas.
- Bugs de guardado/cierre y de navegación corregidos al nivel de causa raíz, no mediante temporizadores arbitrarios o force-reset de flags.
- Funciones parciales se distinguen de funcionalidades terminadas; una función faltante se incorpora al backlog con autorización separada para backend.
- Ninguna IA escribe en `medipos_dev`, aplica migraciones, llama a endpoints mutadores o hace push/deploy sin aprobación expresa.

## 13. Fuentes y estado de verificación

- Repositorio auditado: https://github.com/saintdeyb321/MediPOS/tree/ci/postgres-diagnostics-8bc6b4c
- Commit usado como referencia: https://github.com/saintdeyb321/MediPOS/commit/3e7e7e9a2756ad7a544d048d369364a19958eefa
- W3C WAI: https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/
- W3C WCAG 2.2: https://www.w3.org/TR/WCAG22/

**Precisión:** este documento combina capturas del usuario con lectura estática del repositorio. La posición del modal, el mensaje de descarte y los botones aparentemente inertes requieren diagnóstico/reproducción dirigida; no se afirma que todos los bugs estén probados ni que el build verifique UX.
