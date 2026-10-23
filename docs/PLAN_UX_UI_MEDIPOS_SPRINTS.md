# MediPOS — Plan maestro de refactorización UX/UI del frontend

**Versión:** 1.0 · **Fecha:** 22 de octubre de 2026  
**Estado:** S1–S9 implementados en el árbol local; compilación PASS; aceptación funcional y visual del propietario PENDIENTE  
**Alcance:** frontend React/TypeScript de MediPOS; **no** reescritura del backend ni del modelo de negocio  
**Propietario del producto:** responsable de MediPOS  
**Objetivo principal:** que un propietario, trabajador o tester pueda completar operaciones reales con poca lectura, sin entender UUID, contratos API ni terminología de implementación.

> **Decisión:** antes de continuar las simulaciones manuales del MVP, mejorar de manera integral la presentación y organización del frontend. Se prioriza comprensión, rapidez, claridad y trazabilidad; no se busca copiar MigaPOS ni agregar funciones ficticias.

---

## 1. Diagnóstico: por qué hoy MediPOS resulta abrumador

### Evidencia de uso y referencias

Las capturas proporcionadas de **MigaPOS** sirven como **referencia visual, no como especificación funcional**: menú lateral, encabezados claros, barra de búsqueda, lista de entidades legible, botón de acción principal y formularios independientes en ventanas emergentes. Las capturas actuales y la navegación realizada en MediPOS evidencian:

- **Productos:** lista, catálogo global, creación, detalle, precios, activación y presentaciones conviven en una misma página larga; no queda clara la diferencia entre producto, presentación, lote y stock.
- **Compras:** la recepción tiene datos esenciales de lote y vencimiento, pero el usuario no encuentra el flujo desde la ficha del producto.
- **Superadmin:** mezcla boticas, nuevas licencias, categorías globales, Owner y cambios de licencia; muestra UUID, UTC, idempotencia y textos técnicos a quien intenta operar un SaaS.
- **Empleados:** invitaciones, gestión de personal, asignaciones y horarios compiten visualmente. La invitación existente usa un **enlace temporal**; **NO** hay en el contrato actual envío real dirigido por correo.
- **A nivel transversal:** demasiados botones de igual importancia, textos extensos, confirmaciones visibles todo el tiempo, tablas densas y componentes poco agrupados.

### Lo que realmente existe en el código revisado

En la rama `ci/postgres-diagnostics-8bc6b4c` se observan `frontend/src/shared/ui/Dialog.tsx`, `Button.tsx`, `Panel.tsx`, estilos globales, React Router, TanStack Query, 18+ grupos funcionales y pantallas de **Productos, Compras, Inventario, POS, Caja, Empleados, Invitaciones, Transferencias, Reportes, Comisiones, Configuración, Importación, Contingencia, Auditoría, Notificaciones y Plataforma**.

**Límites que se deben preservar:**

1. El stock se crea al **confirmar recepción** y está separado por sucursal, producto y lote; crear un producto **no** crea existencias.
2. Lote y fecha de vencimiento se introducen en la **línea de compra**, no como propiedades globales del producto.
3. El precio se define por **unidad base**; las presentaciones tienen factores de conversión y operaciones versionadas.
4. Roles y licencias los autoriza el backend. Superadmin **no** implica acceso a ventas, caja o stock privado de todas las boticas.
5. Los flujos de operaciones inciertas, IDs de intento, confirmaciones, recuperación e idempotencia **no deben perderse** por cerrar un modal o navegar.
6. La autenticación es Google, con sesión real, HTTPS, autorización por tenant/sucursal y tokens CSRF.
7. No hacer parecer que funciones no implementadas (por ejemplo, email transaccional, edición de atributos medicinales, imágenes de producto, búsqueda global en todos los registros) existen y funcionan.

**Regla de alcance:** si un rediseño revela una capacidad ausente del backend, registrar un issue/pendiente funcional con contrato propuesto, pero **no** inventar endpoints ni alterar el dominio durante los sprints de UX/UI.

---

## 2. Norte de experiencia y objetivos medibles

### Resultados buscados

- Un usuario primerizo debe entender **dónde crear un producto, dónde registrar una compra y dónde mirar el stock** sin asistencia técnica.
- Una pantalla estándar debe presentar **una finalidad principal**, un listado/resumen entendible y **una acción primaria dominante**.
- Los formularios se abren **bajo demanda**; no se muestra todo el CRUD a la vez.
- La información esencial permanece visible; lo opcional o avanzado se revela cuando se necesita.
- Un proceso largo muestra **progreso, siguiente paso y confirmación**, sin perder borradores, errores ni permisos.
- Un tester puede identificar el resultado de cada acción: qué cambió, dónde verlo y qué hacer si quedó pendiente.

### Indicadores para las simulaciones manuales (objetivos de diseño, no resultados medidos)

| Indicador | Objetivo inicial |
|---|---|
| Alta de producto simple | Encontrar acción principal en la primera pantalla, sin desplazamiento largo |
| Consulta de lote y vencimiento | Llegar desde Inventario en 1–2 interacciones visibles |
| Registrar una compra | Proceso guiado: proveedor/referencia → líneas/lotes → revisión → confirmación |
| Crear personal | Un formulario focalizado; estado posterior claramente explicado |
| Densidad | Evitar más de una acción primaria por región/panel |
| Lectura | Texto normal >= 16 px cuando sea factible; etiquetas >= 14 px; información secundaria legible |
| Responsive | Funcional a 390, 768, 1280 y 1440 px; sin corte de controles |
| Accesibilidad | Objetivo WCAG 2.2 AA; teclado completo y foco visible |
| Integridad funcional | Mismos endpoints, permisos, unidades, cálculos, firmas de operación y estados persistentes |

Los números anteriores son **criterios de diseño** para revisar manualmente, no evidencias de pruebas realizadas.

---

## 3. Reglas UX: qué va en página, modal, drawer o asistente

| Patrón | Cuándo usarlo | Ejemplos MediPOS | Cuándo NO usarlo |
|---|---|---|---|
| **Página base** | Navegar, buscar, filtrar, explorar, consultar resultados | Listado de productos, inventario por lotes, lista de empleados | Insertar cinco formularios completos bajo la tabla |
| **Modal focalizado** | Una tarea breve y aislada, ~2–8 campos | Crear categoría, editar precio, dar de baja, asignar mínimo de stock | Compras con lotes múltiples, POS, reportes complejos |
| **Drawer/panel lateral** | Revisar detalle sin perder el contexto del listado | Ficha rápida de producto, empleado, resumen de licencia | Sustituir una página de flujo crítico o un formulario excesivamente largo |
| **Asistente por pasos** | Proceso secuencial con dependencias y revisión final | Nueva compra, alta inicial de botica, transferencia, invitación de personal | Acción trivial que cabe en un modal breve |
| **Página dedicada** | Operación frecuente, crítica o extensa | POS y carrito, arqueo de caja, conciliación, importación extensa | Convertir cada pequeño filtro en otra ruta |
| **Confirmación crítica** | Acciones no reversibles, destructivas o financieras | Confirmar mercadería, cerrar caja, suspender licencia | Exigir casillas por ver cada dato o cambiar de pestaña |

**Regla importante:** el deseo de agrupar en modales **no significa poner todo dentro de un modal**. La comprensión mejora cuando se elige el contenedor adecuado. Formularios largos, acciones con recuperación persistente o alta carga cognitiva requieren pasos o rutas completas; las ventanas emergentes se reservan para tareas acotadas.

### Contrato de cualquier modal/drawer

- Título inequívoco con verbo y entidad (`Crear producto`, `Editar precios — Paracetamol`).
- Descripción breve o ayuda contextual accesible por `Ayuda`; no párrafos técnicos bajo cada campo.
- Botón primario y secundario estables; `Cancelar` no ejecuta efectos.
- Foco inicial intencional, `Tab/Shift+Tab` contenido, `Escape` cuando sea seguro, cierre que devuelve foco al disparador.
- Fondo inerte y scroll interno; máximo `90dvh` y pie con acciones visibles sin cubrir contenido; no modales anidados.
- Si hay cambios sin guardar, confirmar **descartar cambios**, no cerrar silenciosamente. Si hay escritura enviada o resultado incierto, **bloquear salida insegura** y preservar la operación para recuperación.
- Errores junto al campo y resumen de error cuando corresponda; foco al error importante.
- Etiquetas visibles, ayuda breve, campo requerido identificable y ejemplos locales; `aria-labelledby`, estados `aria-live` sin ruido.
- El contexto de botica, sucursal, rol y versión de datos se conserva; al cambiar de contexto se cancela la edición y se invalida la caché de manera controlada.

---

## 4. Sistema de diseño MediPOS (una sola fuente de verdad)

### Identidad visual

Mantener **la marca MediPOS** y su identidad verde petróleo/teal actual salvo decisión explícita del propietario. Inspirarse en MigaPOS para la **estructura visual y la claridad**, no copiar su identidad morada, ilustraciones, nombres ni iconos de forma literal. El resultado debe verse profesional, sobrio y coherente con una botica peruana.

### Tokens propuestos

- **Espaciado:** escala de 4, 8, 12, 16, 24, 32 y 48 px; distribución consistente entre módulos.
- **Tipografía:** cuerpo preferente 16 px; ayuda secundaria 14 px; títulos de sección 20–24 px; título principal 28–32 px en escritorio. No lograr densidad reduciendo texto a 11–12 px.
- **Controles:** objetivo táctil de **44 × 44 px** cuando sea viable; mínimos y excepciones conforme WCAG 2.2; foco contrastado.
- **Superficies:** fondo neutral suave, tarjetas claras con borde ligero, radio consistente 10–16 px; profundidad moderada.
- **Color semántico:** éxito, pendiente, advertencia, error, inactivo; siempre acompañado de texto/ícono.
- **Anchuras:** contenido general `max-width` razonable, formularios estrechos (~560–720 px), drawers más amplios si contienen detalle, POS con layout propio.
- **Iconografía:** reutilizar `lucide-react`, sin emojis decorativos como única señal de significado.
- **Fechas y moneda:** mostrar `S/` y fechas legibles para Perú; conservar valores exactos y zona/UTC de APIs internamente; indicar explícitamente cuando la fecha sea técnica.
- **Navegación:** sidebar en desktop, menú compacto accesible en móvil; contexto de botica/sucursal siempre visible y cambio de contexto evidente.

### Biblioteca compartida a consolidar

`AppShell`, `PageHeader`, `PageToolbar`, `SearchField`, `FilterBar`, `EntityList`/`DataTable`, `StatusBadge`, `EmptyState`, `Skeleton`, `Pagination`, `ActionMenu`, `Modal`, `Drawer`, `ConfirmDialog`, `FormSection`, `FormFooter`, `Stepper`, `InlineHelp`, `FeedbackBanner`, `AsyncOperationRecovery` y `HelpLink`.

**Implementación:** aprovechar componentes existentes (`Dialog`, `Button`, `Panel`, `TextField`, `Notice`, `Steps`) y mejorarlos; no introducir una segunda biblioteca visual innecesaria ni reconstruir todos los componentes con un framework nuevo. Preferir HTML `<dialog>` nativo para modales y controles semánticos. Respetar `prefers-reduced-motion`.

### Reglas de contenido y microcopy

| Evitar en la UI normal | Mostrar al usuario |
|---|---|
| `Bootstrap Owner` | `Asignar propietario` |
| `Tenant`, `UUID` | `Botica`, `Cuenta` (ID solo en detalle técnico o soporte) |
| `Trial` / `Grace` | `Prueba gratuita` / `Periodo de gracia` |
| `ISO 8601 UTC` visible en formulario | Selector de fecha claro; convertir a formato técnico en la capa cliente sin alterar semántica |
| `Idempotency Key` | `No repitas esta operación mientras se confirma` |
| `No response includes stock` | `Consulta existencias en Inventario` |
| Explicación de seguridad de 8 líneas | Resumen de una oración + ayuda técnica desplegable |

Nunca ocultar advertencias operativas fundamentales en un tooltip opcional ni simplificar a un punto que induzca un error de caja, lotes, cantidades o permisos.

---

## 5. Arquitectura de pantallas objetivo

### 5.1 Productos — referencia principal de MigaPOS

**Página:** `Productos` + botón `Nuevo producto` + búsqueda accesible + filtros `Activos / Inactivos` (solo si los datos disponibles pueden cubrirlos correctamente) + lista legible. Cada fila/tarjeta: nombre, presentación/resumen, categoría, marca, precio y acciones `Ver / Editar`.

**Modal `Nuevo producto`:** secciones cortas `Identificación` → `Información farmacéutica` (solo medicamentos) → `Precios` → `Revisar y guardar`. La gestión de fotografías queda **fuera** hasta contar con API de archivos aprobada; no fingir uploads. Al guardar producto, mostrar CTA siguiente: `Configurar presentaciones`.

**Drawer `Detalle del producto`:** encabezado, resumen, tabs `Datos`, `Precios`, `Presentaciones`, `Lotes y stock` (el último es enlace/resumen condicionado a API); acciones focalizadas en modales específicos. Mantener las restricciones de las unidades existentes: factores inmutables donde aplique, configuración versionada, base única.

**Página Inventario:** tabs `Existencias`, `Por lotes`, `Por vencer`; filtros visibles y números claros por sucursal. CTA contextual `Registrar ingreso` → **Compras**, no falso stock inicial directo. Acciones `Ajustar lote` y `Configurar mínimo` en modales separados.

**Importante:** no mezclar el concepto de `catálogo global` (referencia farmacéutica) con `inventario global` (suma de existencias). No mostrar stock general de todas las sedes cuando la API solo trae una sucursal.

### 5.2 Compras, proveedores e importación

**Página `Compras`:** listado/consulta de recepciones, botón `Nueva recepción` y acceso a proveedores. Si el backend no ofrece listado global, mostrar la recuperación por referencia **sin afirmar que existe historial exhaustivo**.

**Asistente de compra:** `1. Proveedor y referencia` → `2. Productos y presentaciones` → `3. Lote, vencimiento, cantidad y costo` → `4. Revisar` → `5. Confirmar recepción`. Una línea distinta por lote/vencimiento. El sistema solo suma stock tras **confirmar**. La presentación seleccionada determina la conversión a unidades base. Mantener resumen fijo de total documental, sin cambiar redondeos.

**Importación Excel:** flujo propio `Descargar plantilla` → `Seleccionar archivo` → `Confirmar carga` → `Revisar resultado y errores`. Preservar IDs de job y reconciliación ante respuesta incierta.

### 5.3 POS y Caja

**POS:** conservar una **pantalla dedicada**, no ocultar búsqueda, carrito y total en un modal. Desktop: búsqueda/lista izquierda, carrito y totales derecha; móvil: búsqueda + carrito accesible con botón claro. `Cobrar` abre modal de pago/revisión solo cuando corresponde. Mantener cantidades exactas, lotes/validaciones del servidor y operaciones idempotentes.

**Caja:** resumen de sesión abierta; acción `Abrir caja`; arqueo y `Cerrar caja` en flujo guiado con valores claros, confirmación inequívoca y conservación de estados de cierre pendiente.

### 5.4 Personal e invitaciones

**Página:** personal con búsqueda por nombre/correo **según capacidades reales**, filas con rol, sede, estado y acción `Ver/Editar`. `Nuevo empleado` abre formulario focalizado; horarios pueden ir en un segundo paso si es necesario. En la ficha, tabs `Accesos`, `Sedes`, `Horarios`, `Invitaciones`.

**Importante:** el backend actual implementa una invitación por **enlace temporal con aceptación y aprobación**, no envío dirigido al Gmail; diseñar una UI honesta: `Crear enlace de invitación`, `Copiar enlace`, `Pendiente de aceptación`, `Aprobar incorporación`. El flujo de invitaciones por correo, nombres/apellidos previos y autoasignación se documenta como **evolución funcional aparte**.

### 5.5 Transferencias, sincronización y contingencia

Transferencias: lista simple + botón `Solicitar transferencia` → asistente de origen/destino/productos/cantidades → seguimiento del estado → despacho y recepción por lote en vistas focalizadas. Mantener pasos y validaciones según estados reales, sin permitir atajos.

Contingencia offline: página dedicada con aviso persistente `Operación local pendiente — aún no es venta confirmada`; no esconder recuperación/sincronización en un modal efímero. Preservar IndexedDB, outbox, bloqueo local y advertencias de desactualización.

### 5.6 Reportes, comisiones, notificaciones, auditoría

Reportes: selector de tipo + filtros compactos + resultado en área principal; exportación en botón/menú; no repetir formularios por cada tabla. Comisiones: lista de reglas, `Nueva regla` en modal, habilitación en configuración y desactivación focalizada. Notificaciones: lista con filtros por estado y detalle. Auditoría: filtros de consulta y detalle de evento en drawer; solo información que exponga el backend y rol autorizado.

### 5.7 Configuración, onboarding y Superadmin

**Owner:** configuración como página índice con tarjetas `Negocio`, `Sucursales`, `Licencia`, `Preferencias` (solo capacidades existentes); creación de entidad legal y sucursal en pasos separados. Onboarding inicial = paso visible `Registrar entidad` → `Crear sucursal` → `Seleccionar sucursal` → `Comenzar`.

**Superadmin:** vista propia de administración de plataforma, con **secciones diferenciadas** `Negocios`, `Licencias`, `Alta de Owner`, `Categorías globales` y `Estado de plataforma` según permisos existentes. `Nueva botica` debe ser modal/asistente comercial sencillo; `Asignar primer Owner` conserva la verificación real de la identidad registrada mediante UUID **en la implementación actual**. No simular invitaciones por correo inexistentes; mantener pendiente `Owner por email` como feature futura con backend explícito. La autoridad Superadmin y su ventana DEV de 30 minutos **no** son licencias comerciales.

---

## 6. Protección contra regresiones de negocio durante el refactor

**Invariantes obligatorias para Codex:**

1. **Frontend-only:** no modificar API, DB, migraciones, RLS, Google, permisos, endpoint paths ni modelos C# durante este plan.
2. Conservar payloads, UUID de operaciones, `Idempotency-Key`, secuencia `GET/POST/PUT`, CSRF, manejo de 401/403/404/409/429, abortos y timeouts.
3. Preservar exactitud decimal: `JsonNumber`, helpers `decimal.ts`, precio por unidad base, factor de conversión, lote/vencimiento y costo por presentación. **No** convertir importes a `number` con redondeos implícitos.
4. No transformar una escritura enviada en una acción repetible al desmontar un modal. Mantener URL/identidad de trabajo, estado `pending`, reconciliación y bloqueo en el contenedor correcto.
5. Mantener separación de datos por botica y sucursal; no reutilizar caché React Query entre contextos, no cambiar `scopedKey` inadvertidamente.
6. No ocultar advertencias que afectan seguridad de medicamentos, dinero, stock, permisos o estados de aprobación.
7. No introducir acciones falsamente funcionales: subida de imagen, envío de correo, creación de stock desde producto, edición de ingredientes ya registrados o búsqueda global sin API.
8. Preservar controles de permisos en la navegación y dentro de los componentes; los permisos efectivos siguen viniendo del servidor.
9. Al cambiar ruta, tenant o sucursal: limpiar formularios y selecciones, detener requests cuando corresponda, sin borrar estados de operación incierta persistidos.
10. No hacer push, despliegues, resets, rebases ni cambiar de rama sin autorización del propietario. Los archivos modificados localmente tienen prioridad para revisar conflictos.

### Entrega obligatoria de cada sprint

- Código listo en el **working tree actual**; cambios pequeños y cohesivos, no reescritura simultánea de todos los módulos.
- `npm run build` desde `frontend/` como comprobación básica de compilación (no equivale a suite funcional).
- **No ejecutar suites de pruebas automatizadas ni auditorías extensas por defecto**, conforme a la instrucción del propietario. Si un cambio requiere una prueba focalizada para conservar una operación crítica, pedir autorización antes.
- Resumen breve: archivos tocados; antes/después; rutas afectadas; riesgos que se preservaron; pendiente funcional detectado.
- **Checklist manual de máximo 3 a 5 acciones** para que el propietario pruebe después; Codex no deberá crear negocios, ventas, pagos, usuarios ni inventario ficticio por su cuenta.
- No declarar “funciona en mi navegador” sin validación real del propietario. No pedir pausas largas entre sprints: continuar con el siguiente tras terminar el anterior, salvo bloqueador real.

---

## 7. Plan de implementación: 9 sprints medianos

**Progreso local (22/10/2026):** S1 implementado; `npm run build` PASS (baseline y S1). Se conservaron dependencias/rutas/permisos y se reutilizaron los componentes existentes. Modal/drawer nativos con cierre protegido, descarte dentro del mismo diálogo, foco y pie visible; navegación y estilos compartidos consolidados. No se ejecutaron suites ni operaciones comerciales. El aviso previo de bundle >500 kB permanece, sin elevar el umbral. Revisión S1 del propietario: abrir menú móvil; recorrerlo con Tab/Escape; comprobar contexto botica/sucursal; abrir una tarea de los módulos siguientes y probar descarte/cierre. Accesibilidad visual y responsive aún no certificadas por navegador.

**S2 implementado, build PASS:** Productos separa lista, referencias globales, alta por pasos y detalle en drawer; precio/estado/presentaciones en tareas independientes sin anidar diálogos. Filtros etiquetados como filtro de la página cargada, sin afirmar búsqueda global. La recuperación conserva el hook/URL y muestra atributos consultados dentro de la ventana bloqueada. Inventario mantiene tres consultas reales; filtro, mínimo y ajuste focalizados; recepción enlaza a Compras. Sin cambios en APIs, decimales, versiones ni permisos. Pendientes de backend conservados en §9 (fotos, identidad medicinal, búsqueda global); no se simulan. Revisión manual: abrir/filtrar producto; recorrer alta sin guardar si no corresponde; consultar presentaciones/precios; abrir Inventario/Lotes; cancelar ajuste sin enviarlo.

**S3 implementado, build PASS:** recepción en página con proveedor/referencia, selección de producto, lote/cantidad/costo, revisión y resultado confirmado. Se mantiene URL de compra, líneas por lote, total exacto, confirmaciones y recuperación. Proveedores: directorio bajo demanda y alta en modal; XLSX muestra pasos y resultados por filas sin ocultar importaciones parciales. No hay historial general inventado. Revisión manual: recuperar por referencia; abrir nueva recepción; preparar líneas/lotes; revisar total antes de confirmar; consultar un trabajo XLSX original. No se ejecutó ninguna de estas operaciones desde herramientas.

**S4 implementado, build PASS:** POS dedicado con carrito visible/fijo en escritorio y acceso móvil; cobro en modal con pagos exactos y conciliación dentro del modal. Apertura, arqueo/cierre y anulación son tareas focalizadas; sus hooks y marcadores permanecen fuera del estado de las ventanas. Confirmación persistida/ticket no se sustituyen por estados locales. Revisión manual: buscar/agregar; revisar presentación/cantidad; abrir/cancelar cobro; revisar caja; completar venta y ticket solo por interacción del propietario. Pendiente validación funcional en Chrome; no se ejecutaron ventas/pagos desde herramientas.

**S5 implementado, build PASS:** Personal separa listado, ficha por tabs y edición de sucursales/horarios/estado. Invitaciones explican enlace temporal, Google y aprobación; no hay envío por correo. Enlace de un solo retorno permanece en memoria del contenedor, con copia explícita. Recuperación accesible dentro de tareas bloqueadas; desactivación Owner continúa bloqueada. Revisión manual: filtrar la página; abrir ficha; cancelar edición; crear/copiar enlace solo si corresponde; revisar aceptación/identidad y aprobación.

**S6 implementado, build PASS:** solicitud de mercadería por pasos, seguimiento con eventos reales y acciones por estado. Nuevos envíos se abren bajo demanda; campos de creación se conservan montados durante pendientes. Sencillo conserva caja/saldo/revisión y conciliación. Contingencia sigue en página completa, con advertencia persistente y badges de outbox; IndexedDB, workflow y protocolo no se modificaron. Revisión manual: filtrar transferencias; preparar solicitud; revisar lotes/estado; consultar una operación pendiente; revisar cola/conflictos sin borrar datos.

**S7 implementado, build PASS:** filtros principales de reportes agrupados y avanzados desplegables; métricas reales en resumen y exportación de la consulta aplicada en menú. Comisiones separa habilitación, regla nueva y desactivación, con recuperación de la revisión original en el modal. Alertas/auditoría muestran listas y detalle lateral, solo con datos devueltos. Fechas de lectura en Lima; filtros técnicos UTC conservan su significado explícito. Revisión manual: aplicar filtros; descargar; abrir regla sin guardar; consultar alerta; abrir metadatos de auditoría.

**S8 implementado, build PASS:** Configuración por secciones, entidades y sucursales en modales y pasos iniciales según los directorios consultados. Plataforma separa negocios/licencias/propietario/categorías/estado y conserva admisión independiente, identidad verificada, constancia de creación y conciliación. Calendarios UTC conservan el instante textual; entrada avanzada permite la precisión original sin redondeo. Dashboard y selección usan mensajes operativos, sin KPIs inventados. No se activó autoridad administrativa ni se tocó configuración del host. Revisión manual: seleccionar botica/sucursal; abrir Configuración; cancelar alta de entidad/sede; revisar licencia; revisar Plataforma únicamente cuando exista autorización independiente.

**S9 implementado, build PASS final:** foco al título/retorno al disparador, Tab contenido y exclusión de campos inertes durante el descarte; Escape cancela el descarte antes de cerrar la tarea. Los estados pendientes impiden cierre/envío inseguro y conservan controladores/URL; el alta de producto ya reconocida no vuelve a ofrecer el envío original. Se revisaron estilos compartidos, navegación, pies de formulario, fechas, textos, móvil y superficies intervenidas, sin una auditoría general. No se ejecutaron tests, lint, navegador automatizado ni simulaciones comerciales. Responsive390/768/1280/1440, teclado real y operaciones con PostgreSQL quedan pendientes del propietario. Bundle final726.18kB (gzip205.53kB): persiste el aviso >500kB de la base; no se cambió el umbral.

**Handoff de cierre:** usar §12 para simulaciones manuales sobre el entorno ya disponible. No hay otro sprint UX por iniciar. Corregir después solo defectos concretos reportados con pantalla/acción/estado HTTP saneado. Backend, contratos API, autenticación, runtime/servicios, RLS/CSRF, migraciones, licencias y bases no se modificaron durante este plan; sin datos creados, dependencias nuevas, push o despliegue. Los gates y bloqueos funcionales previos del Plan Maestro permanecen vigentes: este cierre es de implementación UI/compilación, no certificación del MVP ni producción.

| Sprint | Ámbito y foco | Resultado visible | Dependencia | Estado técnico |
|---|---|---|---|---|
| **S1** | Fundaciones, tokens, layout, modal/drawer y navegación | Sistema visual coherente; patrones de trabajo listos | Ninguna | Implementado · build PASS |
| **S2** | Productos + Presentaciones + Inventario | Catálogo claro; edición en ventanas; lotes localizables | S1 | Implementado · build PASS |
| **S3** | Compras + Proveedores + Importación Excel | Recepción guiada con lote y vencimiento | S2 | Implementado · build PASS |
| **S4** | POS + Caja | Operación de venta priorizada; cobro y arqueo focalizados | S1–S3 | Implementado · build PASS |
| **S5** | Empleados + Invitaciones + accesos | Lista limpia; fichas, asignaciones e invitación entendible | S1 | Implementado · build PASS |
| **S6** | Transferencias + Contingencia + sincronización | Estados y pasos explícitos, recuperación intacta | S1–S4 | Implementado · build PASS |
| **S7** | Reportes + Comisiones + Notificaciones + Auditoría | Filtros y acciones agrupados; lectura sin ruido | S1 | Implementado · build PASS |
| **S8** | Settings + onboarding Owner + Plataforma + dashboard/selección | Inicio de negocio y administración SaaS entendibles | S1–S7 | Implementado · build PASS |
| **S9** | Consistencia transversal, móvil, accesibilidad y cierre | Experiencia unificada y guía para simulaciones | S1–S8 | Implementado · build PASS |

### Matriz de revisión manual propuesta por sprint

| Sprint | Recorrido breve que hará el propietario |
|---|---|
| S1 | Abrir/cerrar modal con teclado y ratón; comprobar menú en móvil |
| S2 | Abrir producto, revisar precios/presentaciones, localizar Inventario por lote |
| S3 | Crear borrador de compra, ingresar lote/vencimiento, confirmar y reconsultar |
| S4 | Agregar al carrito, revisar cobro, observar sesión de caja |
| S5 | Crear enlace de invitación, consultar estado, editar sede/horario sin confundir permisos |
| S6 | Preparar/consultar transferencia; identificar pendientes sin repetir escrituras |
| S7 | Filtrar reporte, exportar; revisar una regla de comisión |
| S8 | Registrar sucursal y localizar gestión de botica/licencia |
| S9 | Recorrer rutas principales a 390/768/1280 px; teclado; errores y estados vacíos |

---

## 8. Prompts operativos listos para Codex

### Instrucciones de uso

Ejecutar los prompts **en orden S1 → S9** dentro de Codex, sobre el mismo working tree de `C:\Proyectos\MediPOS`. Cada prompt es un sprint autocontenido; no mezclar todos en un solo cambio gigante. Codex debe leer este archivo si lo encuentra dentro del repositorio; si no, copiar el prompt correspondiente. **No asumir que el GitHub remoto contiene todos los cambios locales.**

### PROMPT MAESTRO — ejecución ordenada sin pausas innecesarias

Usar cuando el propietario quiera que Codex implemente **varios sprints consecutivos** tras adjuntar este `.md` al chat de Codex o guardarlo en `docs/` del repositorio. No sustituye los contratos particulares S1–S9.

```text
MEDIPOS | EJECUCIÓN DE PLAN UX/UI

Lee íntegramente el documento PLAN_UX_UI_MEDIPOS_SPRINTS.md adjunto o ubicado en docs/ dentro del repositorio. Respeta los sprints S1–S9 y sus instrucciones específicas. Trabaja sobre el working tree local de C:\Proyectos\MediPOS, sin resets, push, backend, SQL, migraciones, auth ni cambios de permisos.

Implementa UN sprint mediano por vez, pero no solicites confirmación entre sprints por decisiones visuales ordinarias: informa al cerrar cada sprint en un resumen muy corto y pasa al siguiente en la misma ejecución mientras haya capacidad y no exista un bloqueo real. Si el contexto de Codex se agota, detente después de completar un sprint y deja un resumen de continuidad con exactamente qué se hizo y cuál sigue. No mezcles cambios a medio hacer de sprints distintos.

No ejecutes suites de tests automatizados, auditorías generales ni simulaciones comerciales. Usa npm run build al terminar cada sprint para detectar errores de compilación; corregir solo los que provengan de estos cambios. Preserva estados 'pending', reconciliación, idempotencia, permisos, exactitud decimal y separación tenant/sucursal. Ante una funcionalidad que no existe en el backend, documenta el faltante; no la simules.

Quiero una UI de operaciones real: páginas limpias, listas y búsqueda honestas, una acción principal por contexto, modales para tareas cortas, drawers para detalles, asistentes/páginas para procesos extensos, accesibilidad y diseño adaptable. No copies la marca MigaPOS.

Al finalizar cada sprint deja: módulos modificados, decisión de diseño, build y hasta 5 pasos para mi revisión manual. Yo haré las simulaciones del MVP; tú NO debes crear datos ficticios en mi DEV. Continúa de manera ordenada hasta completar los sprints que quepan sin comprometer calidad ni coherencia.
```

### PROMPT S1 — Base visual, modales y navegación

```text
MEDIPOS | SPRINT UX S1 — FUNDACIONES

Trabaja en C:\Proyectos\MediPOS y sobre mi rama/working tree actual; conserva cambios locales. Mi objetivo es refactorizar todo el frontend para que sea claro y fácil de probar manualmente. Implementa ÚNICAMENTE este sprint. NO hagas backend/SQL/migraciones ni suites automáticas, push o despliegues.

Inspecciona frontend/src/shared/ui, shared/styles, app/layouts y app/navigation. Construye/refina un design system consistente con la identidad MediPOS actual: tokens de espacio, tipografía, estados, contenedores, navegación, tablas y formularios. Reutiliza componentes existentes; no introduzcas una biblioteca nueva sin necesidad.

Componentes objetivo: Modal/Dialog accesible usando <dialog> nativo, Drawer (si se justifica), ConfirmDialog, FormSection/FormFooter, PageToolbar, SearchField, FilterBar, StatusBadge, EmptyState, ActionMenu y un Stepper reutilizable. Crea ejemplos de uso internos solo si ayudan a integrar componentes reales, no una página de demostración productiva.

Modal: foco al abrir, Tab contenido, Escape y cierre controlado, retorno de foco, fondo inerte, scroll interno y botones fijos; si hay cambios sin guardar, confirmar descarte; ante operación enviada/resultado incierto NO permitir cierre inseguro. Compatibilidad móvil y teclado. Sin modales anidados. Labels legibles, idioma español Perú, contraste y diseño responsive.

No cambies endpoints, auth, CSP, rutas ni flujos comerciales. Para el estilo, usa las capturas de MigaPOS únicamente como inspiración de agrupación, no copies su identidad.

Entrega: archivos cambiados + decisiones técnicas + npm run build + recorrido manual de máximo 5 pasos. Si hay un bloqueo concreto, descríbelo; no abras una auditoría general. Continúa solo con S1.
```

### PROMPT S2 — Productos e Inventario

```text
MEDIPOS | SPRINT UX S2 — PRODUCTOS, PRESENTACIONES, INVENTARIO

Sobre mi working tree actual, con S1 ya integrado. Refactoriza frontend/src/features/catalog/* y frontend/src/features/inventory/* sin cambiar backend ni contratos API. No ejecutar suites de pruebas, push ni desplegar. Usa los componentes compartidos de S1.

Quiero Productos como página de listado limpia: título, CTA principal 'Nuevo producto', búsqueda solo si está soportada por la API (o etiquetada honestamente como filtro de página), categorías/estados realmente consultables, filas legibles. Oculta formularios hasta pulsar la acción. Nuevo producto en modal/asistente breve con identificación, información farmacéutica condicional, precios y revisión. Detalle de producto en drawer/panel organizado por Datos, Precios, Presentaciones, Stock/Lotes; precios y edición de presentaciones en acciones separadas. Mantén errores y bloqueos de guardado.

Inventario: vistas separadas Existencias, Lotes y Próximos a vencer; filtros y tarjetas/tabla claras, ajuste de lote y stock mínimo en modales focalizados. La pantalla debe explicar dónde ingresar vencimiento: 'Registrar recepción en Compras'. NO inventes stock inicial ni permitas fechas de vencimiento en el producto. Preserva conversiones base, JsonNumber, factor inmutable, versiones de presentación, permisos Owner/Pharmacist y scope tenant/sucursal.

No añadir fotos de producto sin API, ni edición de identidad medicinal si el backend no la soporta. Documenta esas carencias como backlog funcional sin fingir interfaz activa.

Entrega con npm run build, archivos, cambios de UX y checklist manual breve (producto → presentaciones → inventario). No pruebas automatizadas.
```

### PROMPT S3 — Compras, proveedores e Importación Excel

```text
MEDIPOS | SPRINT UX S3 — COMPRAS E IMPORTACIÓN

Trabaja solo frontend/src/features/purchasing/* y product-import/* más componentes S1 reutilizables. Conserva el código local, contratos HTTP y la API sin alteraciones. No ejecutar pruebas automatizadas ni operaciones de negocio reales.

Convierte Compras en una vista clara de consulta/recuperación + CTA 'Nueva recepción'. Diseña un asistente de recepción: proveedor y referencia; productos y presentación; líneas con cantidad, costo por presentación, lote y fecha de vencimiento; revisión; confirmación. Mantén una línea por lote y muestra conversiones exactas en unidades base. Medicamento: lote + vencimiento obligatorios. Stock SOLO al confirmar, jamás al crear borrador o producto.

La recepción puede contener múltiples líneas; no uses un modal pequeño que pierda estado. Usa ruta dedicada o asistente amplio y persistente cuando corresponda. Preserva recovery de transacciones y NO reintentes automáticamente POST/PUT o confirmaciones. En errores de resultado incierto, conservar identidad del intento y guiar a consultar el registro original.

Separa gestión de proveedores en tabla + modal. Importación XLSX: flujo claro de descarga de plantilla → selección → confirmar envío → resultado por filas, manteniendo job ID, conciliación y límites originales. Nada de 'importar stock' si solo importa productos.

Entrega build, resumen técnico y hasta 5 pasos de verificación manual. No suites, sin backend, sin push.
```

### PROMPT S4 — POS y Caja

```text
MEDIPOS | SPRINT UX S4 — POS Y CAJA

Refactoriza SOLO frontend/src/features/pos/* y frontend/src/features/cash/*, manteniendo frontend-only, working tree, identidad visual S1 y funcionalidades existentes. Prohibido tocar endpoint, modelos, reglas de precio, stock, autorización, idempotencia, anulaciones o dinero. No pruebas automatizadas, push ni despliegues.

POS debe ser una página operativa rápida y visual: búsqueda de producto y selección clara, carrito persistente en panel visible, totales legibles, CTA 'Cobrar' destacado. En móvil, panel de carrito invocable sin perder datos. Modal de cobro/revisión conciso, no mover el POS entero a un modal. Mostrar estados de caja (abierta, cerrada, pendiente) sin sobrecargar.

Caja: dashboard de sesión y caja activa; apertura/arqueo/cierre por acciones independientes. El cierre es operación crítica: revisión de montos, confirmación clara y soporte completo de respuesta incierta. No permitir cerrar un modal y perder identidad de una operación en curso. No reintentos silenciosos.

Respeta roles, dinero decimal exacto, selección de presentación, ventas por lote, controles del backend y atajos actuales. Entrega npm run build, detalles y checklist manual de 3–5 pasos, sin suites.
```

### PROMPT S5 — Empleados e Invitaciones

```text
MEDIPOS | SPRINT UX S5 — PERSONAL E INVITACIONES

Refactoriza frontend/src/features/employees/* y sus componentes compartidos únicamente. No cambies backend ni contrato de invitaciones. Conservar working tree y permisos efectivos. Nada de pruebas automatizadas, push o despliegues.

Página Personal limpia: listado/búsqueda solo donde la API la soporte, estados, rol y sede; CTA 'Nuevo integrante'. Detalle de empleado en drawer organizado por Perfil, Sedes, Horarios y Estado. Formularios y edición de horarios en modales/asistentes focalizados. El empleado no obtiene acceso hasta completar el procedimiento real del backend.

La invitación existente NO se envía a un email: actualmente genera un enlace temporal que debe aceptar una cuenta Google y aprobar después el Owner. Diseña el flujo real de manera intuitiva: 'Crear enlace', 'Copiar enlace', 'Pendiente', 'Cuenta que aceptó', 'Aprobar incorporación' y 'Revocar'. No crear campo 'Enviar por Gmail' que finja una capacidad ausente; anotar el feature futuro Owner/empleado por correo en backlog.

Preserva validación de identidad, estado y versión, expiración, aprobación independiente, permisos de sede, concurrencia y recuperación de acciones pendientes. Entrega build, cambios concretos y 3–5 pasos manuales.
```

### PROMPT S6 — Transferencias, Offline y Contingencia

```text
MEDIPOS | SPRINT UX S6 — TRANSFERENCIAS Y CONTINGENCIA

Refactoriza frontend/src/features/transfers/* y frontend/src/features/offline/* sin cambiar lógica de servidor, IndexedDB, outbox ni eventos. Conserva cambios locales, sistema de diseño S1, roles y estado. Sin suites automáticas, push ni despliegues.

Transferencias: listado + filtro de estado y origen/destino; un CTA 'Nueva transferencia'. Solicitud guiada con productos y cantidades; panel de detalle con timeline de estado y acciones disponibles según backend: aprobar, cancelar, despachar por lotes, recibir por lotes. Cada acción crítica tiene revisión y confirmación; recuperar una operación pendiente es un flujo visible, no un botón perdido.

Contingencia: conservar página completa para ventas provisionales, aviso permanente 'no es venta confirmada'; acceso claro a registros pendientes y sincronización. No comprimir en modal el outbox, los errores de sincronización, conflictos o bloqueo. No permitir duplicar solicitudes al cerrar diálogos.

Preserva autorización por sucursal, identidad de transacciones, estados de transferencia, asignaciones por lote y flujo de reconexión. Entrega build y checklist manual corto.
```

### PROMPT S7 — Reportes, Comisiones, Notificaciones y Auditoría

```text
MEDIPOS | SPRINT UX S7 — LECTURA Y GESTIÓN

Refactoriza solo frontend/src/features/reporting/*, commissions/*, notifications/* y audit/*, sin alterar consultas, roles o formatos/exportaciones. Sin backend, suites automatizadas, push ni despliegues.

Reportes: encabezado, tipo, filtros compactos, botón 'Aplicar filtros', resultados principales y descarga Excel/PDF cuando el contrato la soporte; filtros avanzados secundarios. Conserva exactamente las consultas y exportaciones del backend, fechas/permisos y paginación.

Comisiones: habilitación separada, lista de reglas y revisiones; 'Nueva regla' en modal; detalles o desactivación mediante confirmación focalizada. Estados e historia visibles, conciliación de escritura incierta no debe perderse.

Notificaciones: lista de lectura con filtros y detalle; Auditoría: tabla de eventos y drawer de detalle, sin mostrar datos que el rol/servidor no entregue. Unificar badges y estados vacíos. No crear reportes o KPIs inventados.

Entrega npm run build, archivos cambiados y lista breve de comprobación manual.
```

### PROMPT S8 — Configuración, Onboarding, Superadmin y Dashboard

```text
MEDIPOS | SPRINT UX S8 — ONBOARDING Y PLATAFORMA

Refactoriza frontend/src/features/settings/*, platform/*, dashboard/*, tenants/*, branches/* y selection/layout correspondiente. No modificar backend, PostgreSQL, migraciones, autenticación, allowlist de Superadmin, ni renovar ventana administrativa. Conservar cambios locales. Nada de suites automáticas, push ni despliegues.

Owner: página de Configuración con grupos claros 'Datos del negocio', 'Entidades legales', 'Sucursales', 'Licencia'; crear entidad y sede por pasos o modales independientes. Primer acceso guiado con los pasos reales: registrar entidad → sucursal → seleccionar espacio. Sin solicitar UUID al Owner si su flujo no lo requiere.

Superadmin: página administrativa distinta del entorno de botica, con lista/consulta de negocios, crear botica de prueba, gestionar licencia, asignar primer Owner y categorías globales en secciones/tabs y modales de tarea. OCULTAR detalles técnicos detrás de 'Detalles para soporte' pero NO ocultar permisos, confirmaciones ni estados pendientes. La asignación Owner actual requiere una cuenta Google ya registrada y UUID verificado por el servidor: mantener funcionalidad con etiquetado claro; NO fingir que ya existe invitación por email. Documentar ese flujo como mejora funcional futura que necesita endpoints y diseño de seguridad separados.

Dashboard y selección: indicar próxima acción apropiada según acceso real y estado del negocio, sin inventar KPIs. Preservar tenants/licencias, query keys, roles, dependencias de backend y la restricción Production-only de Superadmin.

Entrega npm run build, resumen y hasta 5 pasos manuales sobre rutas existentes. No crear boticas, licencias ni Owners por tu cuenta.
```

### PROMPT S9 — Pulido transversal y cierre para pruebas manuales

```text
MEDIPOS | SPRINT UX S9 — CONSOLIDACIÓN FINAL

Todos los sprints S1–S8 ya están integrados. No inventar nuevas funciones ni hacer reescritura adicional. Revisa únicamente inconsistencias visuales y de accesibilidad entre las pantallas intervenidas: espaciados, tamaños de letra, CTA, tab order, focus visible, teclado, responsive 390/768/1280/1440, estados vacíos/error/carga/pendiente, scroll y cierre de modales, etiquetas S/ y fechas legibles, denominaciones de roles/estados y cambios de sucursal.

Corrige incoherencias puntuales sin tocar endpoints, seguridad ni lógica de negocio. Todas las operaciones con resultado incierto deben conservar IDs, bloqueo y recuperación al salir de una ventana. No crear nuevas rutas falsas ni hacer búsquedas globales simuladas.

Ejecuta solo npm run build como chequeo básico de compilación. No ejecutar suites automatizadas ni auditorías, no hacer push o despliegues. Entrega un inventario de módulos terminados, funcionalidades pendientes que requieren backend y una guía del propietario de máximo 1 página: ruta para crear producto, configurar presentación, recibir lote, consultar stock, abrir caja, vender, invitar empleado y administrar licencia. DETENTE AL FINALIZAR.
```

---

## 9. Contratos que NO cubre este rediseño (backlog funcional separado)

| Funcionalidad solicitada o detectada | Situación actual y decisión |
|---|---|
| **Invitar primer Owner por correo** | Hoy se asigna UUID de usuario Google ya registrado; requiere diseño backend de invitación dirigida a email verificado, caducidad, un solo uso y seguridad. **Fuera del refactor UX**. |
| **Invitar empleados enviando email real** | Existe enlace temporal y aprobación Owner, no envío automático al correo; futura integración transaccional. |
| **Cargar fotografías de productos** | No se ha verificado API de archivos/fotos; no simular upload. |
| **Editar identidad farmacéutica existente** | El panel actual edita precios, estado y presentaciones, no los datos medicinales centrales; requiere API y reglas de trazabilidad. |
| **Stock inicial en creación de producto** | No debe introducirse como atajo oculto. Ingresos mediante recepción o ajuste autorizado con trazabilidad según capacidades reales. |
| **Búsqueda global entre todas las páginas** | Solo cuando exista contrato paginado/filtrado real; no presentar filtro de página como búsqueda completa. |
| **Superadmin permanente de producción** | Diferente de licencia comercial y de ventana DEV; requiere procedimiento de producción/operación seguro independiente. |
| **Cambio de reglas contables, fiscales o farmacéuticas** | No incluido. Este sprint cambia presentación, no reglas. |
| **Historial completo de recepciones** | El contrato actual permite referencia documental o identificador, no un listado general. Se mantiene consulta honesta. |
| **Consulta inequívoca de ajuste, mínimo o alta de proveedor inciertos** | Los contratos actuales tienen límites de consulta; un saldo o un nombre coincidente no acreditan resultado. Se conserva bloqueo y conciliación operacional. |
| **Desactivar Owner** | El bloqueo frontend previo continúa vigente; el rediseño no certifica ni desbloquea esa operación crítica. |

---

## 10. Bases profesionales que sustentan el plan

1. **W3C/WAI-ARIA — Dialog (Modal) Pattern:** foco dentro del modal, contención de tabulación, `Escape` y retorno de foco. https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/
2. **W3C/WCAG — HTML `<dialog>` (H102):** aprovechar comportamiento modal nativo, contenido de fondo inerte y gestión de foco. https://www.w3.org/WAI/WCAG22/Techniques/html/H102
3. **WCAG 2.2:** contraste, teclado, foco no oculto, tamaño mínimo de objetivos e interfaces operables. https://www.w3.org/TR/WCAG22/
4. **Nielsen Norman Group — Progressive Disclosure:** mostrar primero las opciones frecuentes y trasladar detalles avanzados a niveles secundarios. https://www.nngroup.com/articles/progressive-disclosure/
5. **GOV.UK Design System — Interruption Pages:** usar interrupciones y confirmaciones con moderación, únicamente cuando una decisión lo exige. https://design-system.service.gov.uk/patterns/interruption-pages/

**Aplicación:** estos estándares orientan decisiones, no sustituyen la validación con operaciones manuales y el contexto de una botica peruana.

## 11. Definición de terminado (DoD)

Una sección se considera lista para simulación manual cuando:

- Se comprende en primera lectura qué puedo hacer y qué ocurrirá al pulsar el botón principal.
- No hay varios CRUD abiertos a la vez ni controles amontonados; modales/drawers están reservados a tareas adecuadas.
- Los datos críticos para decidir (producto, presentación, lote, vencimiento, stock, moneda, rol, sucursal, estado) están visibles donde corresponden.
- El usuario puede navegar, escribir y cerrar con teclado y sin perder información de una operación enviada.
- Un fallo de red, respuesta incierta, expiración de sesión o permiso insuficiente no produce reintentos ni falsas confirmaciones.
- El proyecto compila; el propietario realiza personalmente las simulaciones funcionales y comunica defectos concretos.

## 12. Guía breve del propietario para las simulaciones

La implementación y compilación están terminadas; estos recorridos **no fueron ejecutados por el agente**. Abre el entorno habitual y actualiza Chrome con Ctrl+F5. Inicia Google si corresponde; selecciona botica y sucursal. No actives Superadmin para recorrer módulos ordinarios.

1. **Producto:** Productos → Nuevo producto → Identificación → Datos del producto → Precios → Revisión → Guardar. Transcribe únicamente datos documentados. Crear no ingresa stock. Referencias del catálogo global es una vista distinta, sin existencias privadas.
2. **Presentaciones:** abre Ver producto → Presentaciones → Configurar. Conserva una base y los factores existentes; añade otra presentación si necesita otro factor. Precios se edita por separado, siempre por unidad base.
3. **Recepción:** Compras → Nueva recepción. Elige proveedor documentado o continúa sin proveedor; añade referencia, productos y presentaciones, cantidad/costo por presentación y lote/vencimiento. Guarda todas las líneas, revisa y confirma solo cuando corresponda. El stock ingresa únicamente al confirmar.
4. **Stock:** Inventario → Existencias / Lotes / Próximos a vencer. Filtra el producto. Ajustar lote exige motivo y confirmación; el mínimo es una tarea distinta, solo para propietario. No uses ajustes para ocultar errores de recepción.
5. **Caja y venta:** Caja → Abrir mi caja → Confirmar apertura. Punto de venta → buscar → seleccionar presentación/cantidad → carrito → Cobrar. Este paso prepara el borrador; revisa los pagos y confirma la venta una sola vez. Consulta el ticket interno (no SUNAT). Para cerrar: Caja → Preparar cierre de mi caja → contar efectivo → revisar y confirmar.
6. **Personal:** Personal → Nuevo integrante → elegir rol → crear/copiar enlace temporal. El trabajador accede con Google y acepta; el propietario verifica la cuenta, aprueba y configura sucursales/horarios desde la ficha. No se envía correo automáticamente. La desactivación de propietario sigue bloqueada.
7. **Consultas y seguimiento:** Transferencias muestra solicitudes/estados y tareas por lote. Reportes → aplicar filtros → consultar/descargar; Comisiones → reglas e historial. Revisa alertas y metadatos de auditoría desde sus detalles. Contingencia conserva su página y cola: un evento local aún no es una venta confirmada.
8. **Negocio y licencia:** Configuración → Entidades legales / Sucursales / Licencia. Los cambios administrativos de licencia y el primer propietario pertenecen a Plataforma y requieren autoridad independiente vigente; DEV continúa ordinario y este plan no la habilita.

**Comprobación visual:** repetir pantallas principales a390,768,1280 y1440px; Tab/Shift+Tab, Escape, retorno de foco, cerrar/cancelar cambios y lectura de tablas. Ante un resultado incierto, conservar la dirección y consultar la operación original; no repetir escrituras ni borrar datos del navegador. Informar únicamente pantalla, acción y error/estado HTTP saneados, sin cookies, secretos o identificadores privados.

**Fin del plan.**
