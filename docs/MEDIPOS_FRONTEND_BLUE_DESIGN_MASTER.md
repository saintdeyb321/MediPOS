# MediPOS · Blue Studio — Prompt maestro de rediseño visual integral

**Propósito:** especificación ejecutable de producto, UX/UI y arquitectura frontend para Codex. **Alcance:** todas las pantallas visibles, incluyendo acceso, onboarding, workspace comercial, Superadmin, estados de error/recuperación, modales, impresión y contingencia. **No es una reimplementación del backend.**

Repositorio `C:\Proyectos\MediPOS`, rama `ci/postgres-diagnostics-8bc6b4c`. Referencia remota auditada `8b9d2194e1afa788ed6d74dd1c54a5b495cb6f2c` (REFACTOR ONBOARDING A MEDIAS). Verificar HEAD local, cambios sin publicar y evidencias más recientes antes de iniciar. Este MD sustituye las decisiones visuales anteriores; preserva las invariantes y los recorridos simplificados de ACCESS-LEAN, FOUNDATION, SUPERADMIN-LEAN, UX-LEAN y RC1. Leer `AGENTS.md`, `frontend/AGENTS.md`, secciones relevantes de `CURRENT_STATE.md` y `MEDIPOS_FRONTEND_DESIGN_SYSTEM_ONBOARDING.md`. No repetir auditorías ni sobrescribir trabajo previo. La evidencia F5 de Foundation estaba incompleta en el SHA remoto; no tomar 615 PASS de ACCESS-LEAN como prueba del nuevo árbol.

## 0. Mandato y ejecución

Actúa como Principal Frontend Engineer + Senior Product Designer responsable del resultado visual real. Ejecuta D1–D5 en orden, en una sesión si los recursos lo permiten. Cada bloque tiene checkpoint resumido en `CURRENT_STATE.md`, con archivos y pruebas; si se interrumpe, continuar desde ese punto. No crear una colección de planes adicionales. Priorizar calidad visible sobre cambios de nombre o color aislados. Prohibido dejar pantallas a medio migrar, introducir módulos ficticios o eliminar capacidades. Si un bloqueo externo impide una prueba, continuar tareas independientes y marcarla pendiente, no fabricar PASS. Finalizar con el build real y las verificaciones, no solamente código.

### Referencias y herramientas (verificadas 2026-11-25)

- **Referencia primaria: TailAdmin React Free**: https://free-react-demo.tailadmin.com/ — https://tailadmin.com/react — https://github.com/TailAdmin/free-react-tailwind-admin-dashboard . Su repo público usa React 19, TS y Tailwind v4, con licencia MIT. Adoptar distribución de shell lateral, tarjetas, formularios, tablas y densidad. No clonar sobre MediPOS ni instalar la plantilla completa. Si se copia código de la edición gratuita, verificar licencia y commit, conservar avisos y registrar procedencia; no copiar material PRO, marcas, assets comerciales ni código no autorizado. Preferir implementar patrones con las primitivas existentes.
- **Referencia secundaria**: https://tabler.io/ para densidad, estados y tablas; no introducir Bootstrap. https://ui.shadcn.com/blocks/dashboard para composición de filtros, menús y superficies; no ejecutar generadores que sobrescriban archivos ni añadir otro sistema de componentes.
- **Skill**: https://developers.openai.com/blog/designing-delightful-frontends-with-gpt-5-4 — usar `frontend-skill` oficial si instalada. Si falta y el operador autoriza instalación, solicitarla mediante `$skill-installer frontend-skill` en Codex; comprobar procedencia, alcance y disponibilidad. No ejecutar scripts desconocidos ni instalar dependencias de aplicación por esa razón. Si no está disponible, continuar con esta especificación. No es un requisito de ejecución de MediPOS.
- **GitNexus**: usar `context`, `trace`, `cypher`, `detect-changes` focalizados si está disponible y autorizado para uso comercial. Comparar índice con HEAD; verificar resultados en código. No reinstalar ni reindexar por costumbre. Alternativa `git ls-files`, `git grep`, `rg`, imports/exportaciones y referencias TS. No inventar relaciones ni subir código privado a servicios externos. No se requiere un turno separado de generación de mapa: generar mapa mínimo dentro de D1.
- **Accesibilidad y red**: https://www.w3.org/TR/WCAG22/ ; https://tanstack.com/query/latest/docs/framework/react/guides/query-keys ; https://tanstack.com/query/latest/docs/framework/react/guides/invalidations-from-mutations . Objetivo WCAG 2.2 AA, no declarar certificación completa sin auditoría. React + TanStack Query existente; no sustituir cliente HTTP ni seguridad.

## D1 — Contrato visual y arquitectura compartida

### D1.1 Diagnóstico inicial comprobado y mapa mínimo

Auditoría remota del SHA anterior: `tokens.css` define morado `#5542ad`, fondo `#f2f3f9`, blanco dominante. `InventoryPage.tsx` combina `MetricGrid compact`, `PageToolbar`, badges repetidos de sede/fecha, un `<details className="inline-help">` con enlace largo y un modal para filtrar producto, seguido de pestañas y tabla dentro de panel. Sus indicadores cuentan solo la página, no el inventario total. No debe venderse esa cuenta como KPI global. `WorkspaceNavigation.tsx` construye grupos por roles y mueve caja y contingencia a «MÁS», con menú desplazable. `FormLayout.tsx` ya dispone de FormSection, FormFooter, InlineHelp. Existen Button, IconButton, Modal/Drawer, Dialog, Panel, PageHeader, PageToolbar, PageControls, MetricCard, Badge, Notice, States, TextField, ActionMenu, NavigationSafety, Breadcrumbs, DateText. Antes de crear otra primitiva, revisar consumidores de la existente.

Construir un mapa breve de impacto: `app/router → layouts → feature/page → hooks → api → shared/http` y `tokens/base/ui/operations → primitives → consumidores`. Registrar rutas, roles, queries/mutations, acciones críticas, modales y estados por feature. Buscar `className`, `inline-help`, `task-tabs`, `operation-table`, `PageControls`, `MetricGrid`, `style=`, colores hex/RGB y botones nativos. Identificar estilos de feature que pisan tokens. Localizar pruebas relevantes. Usar grafo para dirigir ediciones, no hacer inventario exhaustivo de backend. Verificar endpoints solo si un cambio de UI depende de su semántica. Preservar las API públicas de features y dirección `app → features → shared`.

### D1.2 Dirección de arte: Blue Studio

Identidad de farmacia moderna: navegación izquierda azul marino, área de trabajo celeste muy suave, superficies de contenido blancas, acentos azul intenso y celeste, naranja cálido para atención y énfasis limitado. Verde para estados positivos, rojo para riesgo/errores. Logo MediPOS y texto en blanco sobre sidebar, marca azul sobre claro. No cambiar nombre ni inventar logotipo distinto; ajustar colores del SVG existente usando tokens. Evitar apariencia de landing page, gamificación, glassmorphism en datos, grandes héroes, degradados en cada tarjeta, exceso de sombras y badges. Las tablas, formularios y datos deben ser protagonistas. Mantener tema claro como única entrega; no agregar selector de tema ni modo oscuro incompleto.

**Tokens obligatorios (valores sRGB):** migrar variables existentes y añadir las que falten; no crear otro archivo de tokens competidor. `@theme inline` debe mapear al mismo origen.

```css
:root {
  --canvas: #EAF3FA;
  --surface: #FFFFFF;
  --surface-muted: #F2F7FC;
  --surface-raised: #FFFFFF;
  --text: #142B43;
  --text-muted: #475B70;
  --primary: #075EA8;
  --primary-hover: #084C86;
  --primary-soft: #DFF2FE;
  --on-primary: #FFFFFF;
  --sky: #38BDF8;
  --nav-bg: #0B2340;
  --nav-bg-end: #12375A;
  --nav-text: #DCEBFA;
  --nav-active: #174A78;
  --nav-active-text: #FFFFFF;
  --nav-accent: #7DD3FC;
  --accent: #F59E42;
  --accent-text: #663300;
  --accent-soft: #FFF0DD;
  --info: #075985;
  --info-soft: #DFF2FE;
  --success: #146C43;
  --success-soft: #E5F6ED;
  --warning: #874600;
  --warning-soft: #FFF1D6;
  --error: #A62632;
  --error-soft: #FDECEF;
  --border: #C8DCEB;
  --control-border: #71879C;
  --focus: #0874BD;
  --overlay: #0B23408C;
  --shadow: 0 2px 10px #0B23400A;
  --shadow-popover: 0 18px 48px #0B234029;
  --radius-sm: 0.375rem;
  --radius-md: 0.625rem;
  --radius-lg: 0.875rem;
  --control-height: 2.75rem;
  --sidebar-width: 15.5rem;
  --sidebar-collapsed-width: 4.5rem;
  --motion-fast: 120ms;
  --motion-normal: 180ms;
  --motion-panel: 220ms;
  --motion-ease: cubic-bezier(.2,.8,.2,1);
}
```

Conservar escala de espaciado 4/8/12/16/20/24/32/48 px (rem) y `--page-gutter`. Tipografía local `Segoe UI, system-ui, -apple-system, sans-serif` (no CDN, no fuente descargada por defecto). Texto normal 14–16 px; labels 14–15; H1 26–32; H2 18–22; cifras 22–30 con `tabular-nums`. Usar contraste y peso, no solo tamaños. Densidad cómoda: controles 44 px de alto, filas 48–56 px según contenido, separación 16–24 px. En POS densidad configurable por contexto sin controles microscópicos. Evitar duplicar título, subtítulo, botica y sede. Sidebar sin mayúsculas excesivas, tamaño consistente e iconos Lucide 18–20 px. Tokens no literales dispersos; excepciones para impresión/visualizaciones documentadas.

**Contrastes calculados para pares base** (no equivalen a una auditoría completa): blanco/primary 6.61:1; nav-text/nav-bg 13.03; nav-text/nav-active 7.57; texto/canvas 12.84; muted/blanco 7.00; accent-text/accent 4.83; warning/warning-soft 6.47; success/success-soft 5.75; error/error-soft 6.26; info/info-soft 6.58; control-border/blanco 3.72; focus/canvas 4.41. Validar hover, disabled, degradado, foco y opacidades reales en el navegador. No usar blanco sobre naranja o celeste claro. `--border` es separador decorativo, no límite único de input; usar control-border donde se necesita contraste de control. En sidebar usar foco claro `--nav-accent`, validar contra ambos extremos. No comunicar estado solo mediante color.

**Degradados y textura**: `linear-gradient(165deg,var(--nav-bg),var(--nav-bg-end))` en sidebar; un encabezado de bienvenida o acceso `linear-gradient(135deg,#075EA8,#12375A)` con texto blanco y contraste verificado; detalle de acento celeste decorativo. Fondo celeste estable en workspace, alternancia de zonas blancas y paneles tintados para contexto. Naranja solo en alertas útiles, iconos/acento o sección de vencimiento/cupo, no convertir toda acción primaria en naranja. No degradado detrás de texto pequeño ni tablas. Overlay de modal con color semitransparente y opcional `backdrop-filter:blur(3px)` con fallback, verificar rendimiento/contraste; no animar blur.

### D1.3 Layout global y navegación izquierda

Desktop >=1100 px orientativo: sidebar fijo/sticky de ~248 px, topbar contextual 60–64 px, workspace flexible `min-width:0`, contenido con gutters de 16–28 px. Sidebar: marca, selector de botica/sede compacto, grupos de navegación, cuenta y cierre. Menú vertical scroll propio si necesario, cuenta visible cuando haya espacio, no doble scroll inútil. Colapsar a iconos con nombres accesibles y tooltip/label cuando proceda; preferencia no sensible independiente de autorización. Tablet/móvil: drawer lateral izquierdo desde botón menú, foco y Escape, cerrar tras navegar, no reemplazar por navbar inferior. Mantener ruta activa inequívoca también en subrutas (transferencias/sencillo, auditoría, importación); no marcar dos secciones. No recrear shell al cambiar módulo. No poner un buscador global falso ni acciones que no existan. Header: ubicación actual, acción contextual cuando útil, acceso a cuenta/notificaciones existentes sin duplicar GET. Salir/cambiar contexto mantiene protecciones de operaciones pendientes. Evitar reordenar permisos; grupos sugeridos: Operación (Inicio, POS, Caja, Compras, Inventario, Productos según rol, Transferencias), Gestión (Reportes, Comisiones, Personal, Configuración), Avisos/Contingencia según acceso. Revisar menú existente por rol y pantallas profundas, conservar todos los destinos. Si agrupar, no crear subsecciones sin destino. Superadmin usa la misma gramática visual, shell independiente y autoridad distinta.

### D1.4 Componentes compartidos (composición, no herencia de clases)

Modificar `shared/ui` y `shared/styles` primero; feature-specific solo cuando sea semántica particular. No añadir un nuevo framework. Variantes tipadas con defaults seguros; no mega componente con docenas de booleanos. Un origen de estilos por patrón. Mantener handlers, refs, IDs, nombres accesibles y contratos existentes. Antes de sustituir una primitiva, inventariar consumidores; adaptar de forma incremental dentro del mismo encargo.

| Primitiva | Especificación de producto |
|---|---|
| PageHeader / PageToolbar | Título, subtítulo breve opcional, acción principal a la derecha y debajo en móvil. Breadcrumb solo si navegación profunda. Toolbar de filtros dentro de la superficie de datos, alineada; evitar filas con espacios gigantes. |
| Button / IconButton | Primary azul, secondary borde, tertiary/quiet discreto, danger rojo con texto. Estados hover/active/focus/loading/disabled. Spinner sin salto de ancho. Icon-only solo con nombre accesible y tooltip opcional. No duplicar acción ni usar enlaces sin estilo como CTA. |
| Panel / Section / MetricCard | Panel blanco con borde suave, título claro; zonas tintadas para contexto. Indicador con icono, número y alcance real. No tarjetas de KPI ficticias ni envolver cada texto en tarjeta. |
| TextField / SearchField / select / checkbox | Label encima, placeholder ejemplo no sustituto, helper/error asociado, ancho adaptable, `inputMode` cuando procede. Checkbox representa dato/consentimiento o verificación independiente, no ritual. Filtros locales sin red; filtros de servidor declarativos. |
| FormSection / FormFooter | Grupos por intención, 1 columna móvil, 2 solo si campos cortos relacionados. CTA final; espacio simétrico, pie alineado a la derecha en escritorio, centrado o ancho completo en móvil; no todos los botones centrados sin jerarquía. |
| Modal / Drawer / Dialog / ConfirmDialog | Un título y una descripción útil, cuerpo scroll interno, footer consistente con Cancelar / acción primaria. Modal centrado en pantalla con ancho max 480–640 px; ancho amplio solo para tareas complejas; móvil pantalla casi completa si necesario. No modales anidados. Mantener cierre seguro, protección dirty/busy/recovery, foco inicial/retorno, Tab/Escape y bloqueo de doble envío. IDs de formulario para submit del footer si están fuera del form. |
| Tabs / task-tabs | Pestañas de navegación semántica con estado activo claro, sin panel de pestañas gigantes. `aria-pressed` para filtros segmentados o patrón ARIA tabs completo si es navegación de tabs; teclado coherente. No cambiar estado que borre operaciones. |
| Table / operation-table | Tabla semántica con caption accesible, encabezado tonal, números a derecha, filas con hover, separación, columna acciones compacta. Sticky header solo en contenedor con scroll propio si ayuda. No usar `display:block` en td destruyendo semántica; en móvil scroll localizado o lista de tarjetas accesible derivada del mismo modelo, no duplicar controles. |
| PageControls | Solo paginación útil con conteo de página/cursor; ocultar si no hay navegación real (sin ocultar salida de página vacía). No refrescos rutinarios. No fingir total global. |
| Badge / Notice / States | Badge solo estado real, texto+icono; aviso contextual y breve; éxito localizado en sección, no persistir en otra. Skeleton/estado de carga sin cambiar tamaño. Vacío orientado a acción y permisos. Error específico + reintento. Recuperación incierta visible y distinta de error normal. |
| InlineHelp | Dejar de renderizar como enlace improvisado. Reutilizar `details/summary` estilizado como botón discreto con icono CircleHelp, «Ayuda» o «Sobre el stock», chevron y texto breve expandible. Alternativa panel de ayuda en drawer para contenido largo; nunca depender de hover ni ocultar información crítica. No forzar popover para una frase simple. |
| ActionMenu / Breadcrumbs / DateText | Menús accesibles, acciones destructivas separadas, no esconder acción frecuente. Breadcrumb solo contexto útil. Fecha es-PE/Lima, no UTC en UI normal; mantener valor exacto para soporte. |

Formularios: no limpiar inputs cuando una consulta de fondo cambia; no remontar una tarea al animar. Estados técnicos (UUID, trace, scope, idempotency) solo en ayuda/soporte pertinente. Texto de recuperación conserva significado y acción segura; no esconder restricciones del producto.

### D1.5 Movimiento y microinteracciones

CSS nativo y React existentes. Transiciones selectivas `background-color,color,border-color,box-shadow,opacity,transform` 120–180ms; no `transition: all`. Hover de tarjeta accionable hasta 1px y sombra suave, press breve `scale(.99)` solo si no afecta foco/lectura. Entrada de modal/drawer 180–220ms opacity/translate 4–8px, sin retrasar showModal ni interacción. Expansión de menú estable; carga skeleton suave, sin shimmer agresivo. Evitar animar listas completas al refetch, importes de caja, totales POS, texto de error o cada cambio de búsqueda. No animar layout principal continuamente ni usar remount para animación. Desactivar movimientos no esenciales con `prefers-reduced-motion: reduce`, mantener indicación de carga textual. Sin nueva dependencia de animación, sin CDN. Probar teclado y rendimiento en equipo modesto. No bloquear submit por animación.

**Checkpoint D1**: mapa breve, tokens y primitivas aprobados por build/test focalizado, inventario representativo montado como primera pantalla patrón con datos fixture. Comparar visualmente con captura previa; no esperar aprobación intermedia para continuar con esta dirección ya definida.

## D2 — Aplicar patrón a todos los recorridos de operación

### D2.1 Inventario (pantalla de referencia obligatoria)

La captura del operador muestra inventario con cifras de página en franja vacía, filtro separado, badges repetidos, texto de ayuda subrayado y doble panel. Rediseñar así:

```
[Sidebar izquierda] | Topbar: botica / sucursal
                    | Inventario                           [Registrar recepción]
                    | Controla las existencias y vencimientos de tu sucursal.
                    | [Productos en esta página: n] [Sin stock en esta página: n]
                    |  (ocultar métricas si vacías/no aportan; no inventar globales)
                    | ┌────────────────────────────────────────────────────────┐
                    | │ Existencias   Lotes   Por vencer (30 días)       (?) Ayuda │
                    | │ [Buscar/elegir producto...]   [Filtro actual ×]             │
                    | │ Producto                             Disponible            │
                    | │ ...                                                      │
                    | │ vacío: Aún no hay existencias / No hay resultados         │
                    | │       [Registrar recepción] si permiso y contexto         │
                    | │ paginación solo si corresponde                            │
                    | └────────────────────────────────────────────────────────┘
```

Un solo panel de datos, no repetir ubicación si topbar ya la muestra. «Disponible» con unidad real (unidad base cuando corresponde), no confundir presentación/paquete. Cifras de página pueden pasar a resumen compacto dentro de toolbar o tarjetas pequeñas tintadas si aportan, etiquetadas sin ambigüedad. No crear métricas de stock bajo o valor total sin contrato. «Por vencer» en ámbar con semántica de fecha (30 días, Lima), vencido separado de próximo. Filtro inline mediante ProductLookup existente en popover/superficie o sección compacta; si requiere búsqueda avanzada, mantener modal con título y acción clara, no añadir GET por tecla sin control. Conservar selección por ID, `offset`, scope y `write.pending`; no escribir al filtrar. Ayuda `(?) Sobre el stock` integrada junto al título/tab, expandible: «Las cantidades se muestran en la unidad base del producto. Los indicadores corresponden a esta página.» Detalles de mínimos/ausencia de historial en ayuda secundaria; no simular historial. Reportes solo para Owner. Vacío sin filtro: «Aún no hay existencias» y orientar a recepción cuando permiso; filtrado: «No encontramos existencias para este producto» y quitar filtro. No «No es un error de consulta» como texto habitual; error es estado distinto. Mantener acciones ajuste/mínimo, costo solo Owner, expiración, exactitud y recuperación de recibo. Revisar `ProductLabel` consumidores y N+1 si aparece en DOM, no añadir endpoint ni eliminar validaciones. No duplicar CTA principal con vacío si ya visible, puede ser enlace contextual.

### D2.2 POS y caja

POS: prioridad a búsqueda/lector y carrito, no hero ni KPI decorativo. Desktop dos columnas (catálogo/búsqueda flexible + carrito/cobro ~340–400 px según ancho); tablet adaptable; móvil resumen y CTA «Cobrar S/ …» visibles sin tapar líneas ni perder teclado. Carrito con producto/presentación/cantidad/precio/subtotal; total prominente y exacto, foco de lector estable. Conservar comprobante interno, impresión, motivos de anulación, pagos múltiples, cambio y estados inciertos. No reintroducir confirmaciones rutinarias. Caja: estado de caja y saldo según permiso; flujo abrir/cerrar en modal uniforme, resumen de arqueo, transferencias y diferencias; CTA coherente y bloqueado en curso. No mezclar estimados con saldo confirmado. Mantener protección de respuesta perdida y no enviar otro POST al cerrar modal.

### D2.3 Productos, compras, proveedores y transferencias

Productos: tabla/lista compacta con búsqueda y filtros claros; detalle organizado en datos, presentaciones, precios y configuración según contratos. CTA «Nuevo producto» si permiso; asociación global visible sin duplicación. Mantener versiones, unidades, reglas clínicas y fuentes obligatorias. No fotos ficticias ni precios inventados; icono neutral si no hay imagen.
Compras: directorio/recepción en un panel, estado claro, proveedor, líneas editables con subtotal y total. Acciones de recepción y confirmación distintas solo cuando negocio lo exige; no colapsar guardado no confirmado. Proveedores cargados al necesitarse. Formularios coherentes, no modales encadenados. Mantener recuperación de creación, FEFO/stock, versiones y exactitud decimal.
Transferencias: pestañas Mercadería/Efectivo o rutas existentes con navegación clara; origen/destino/custodia destacados; estados y acción contextual por rol. No fusionar solicitud/aprobación/despacho/recepción ni eliminar verificación de custodia. Identificar importe/cantidad y sede en acciones de compromiso. Conservar reintentos de lectura, constancias y no repetición.

**Checkpoint D2**: inventario, POS, caja, productos, compras, proveedores, transferencias y sus subrutas/diálogos migrados al mismo patrón. Pruebas focalizadas por consumidores cambiados, captura escritorio/móvil de inventario, POS y un modal representativo. No declarar aceptación comercial por fixtures.

## D3 — Aplicar patrón a administración y acceso (todas las demás superficies)

| Superficie | Diseño objetivo y controles |
|---|---|
| Inicio comercial | Bienvenida compacta con acento degradado, accesos rápidos según rol y métricas reales disponibles (etiqueta periodo/sede). Evitar panel de bienvenida enorme, gráficas sin datos, KPIs inventados. |
| Reportes | Filtros de periodo y sede agrupados, botón aplicar solo si consulta explícita necesaria, tablas de resultados y exportar según contrato. Gráficas solo con datos existentes, leyendas y alternativa textual. No recomputar totales exactos con float. |
| Comisiones | Listado, vigencias y reglas claras, modal corto con efecto de reemplazo; preservar fecha y autorización. |
| Personal e invitaciones | Directorio/tarjetas con nombre, rol, estado, sedes y horarios; acciones contextuales, verificación de identidad independiente, invitación/revocación real. No mostrar datos fuera de alcance. |
| Notificaciones | Lista por relevancia/estado, lectura y enlaces existentes; no notificaciones falsas, contador existente y caché controlada. |
| Configuración y onboarding | Preservar un formulario inicial y una acción principal, entrada automática autorizada y CTA Crear/Gestionar sucursales. Reutilizar TenantStructure; secciones Mi negocio/datos fiscales, Sucursales, Plan y licencia, historial. Tarjetas y modales coherentes, no volver a cuatro etapas, no duplicar alta ni inventar editar/eliminar si no hay contrato. |
| Importación Excel | Carga/revisión/resultados parciales con progreso y resumen, tabla de errores legible, recuperación sin reenvío; no ocultar fallos ni cambiar proceso transaccional. |
| Auditoría | Filtros y tabla, actor/acción/fecha y detalle de soporte bajo demanda. Conservar ámbito y límites. No presentar auditoría como editable. |
| Contingencia/PWA | Estado offline, cola y última sincronización real, acción de sincronizar cuando proceda, errores y pendientes visibles. Conservar IndexedDB, service worker, marcadores, locks y separación por cuenta. No hacer caching adicional de datos sensibles. |
| Login, invitación, entrada, selectores, acceso denegado, 404 | Marca azul, fondo celeste/degradado sobrio, formularios y CTA claros, tarjetas de opciones, sin pasos si no hay decisión. Conservar Google legítimo, permisos y rutas. No usar credenciales ficticias en DEV real. |
| Superadmin: Inicio, Boticas, Propietarios pendientes, Licencias, Catálogo global, Mi cuenta | Shell azul común y contexto administrativo diferenciado. Directorios compactos, filtros alineados, cards de categorías, vigencia/cupo y acciones propias. No modificar límites de S1–S5 ni consultar datos comerciales privados. No repetir «Nueva botica» fuera de contexto. Mantener cooldown 429, recuperación y separación lectura/escritura. No simular filtro global si consulta paginada. |
| Ticket/impresión y estados transversales | Ticket interno identificado, CSS print sin fondos/elementos de navegación, montos legibles. Errores, carga, vacío, permisos y recuperación coherentes en todas las rutas. No afirmar emisión SUNAT. |

Para cada módulo inventariar subtareas, vistas vacías/llenas, modales y estados críticos. Una pantalla no se considera migrada solo por cambiar el fondo. Verificar composición, jerarquía, texto, acciones y responsive. No cambiar flujo comercial sin motivo probado. Eliminar textos burocráticos en vista normal, mover explicaciones técnicas a ayuda contextual; no eliminar significado. Actualización declarativa de datos: conservar carga por contexto, acuses verificados, invalidación selectiva, cache por identidad/tenant/sede/membresía, cancelación de respuestas obsoletas y ausencia de botones rutinarios de refresco. No polling agresivo, no refetch de tab oculta, no prometer actualización instantánea externa sin canal. Reintentar errores y consultar resultado incierto son acciones válidas. No duplicar hooks de datos para desktop/móvil.

**Checkpoint D3**: todas las rutas visibles inventariadas y migradas; lista de excepciones por módulo (si existe) con causa. No crear nueva fase de diseño. Mantener una sola fuente de UI.

## D4 — Consolidación responsive, accesibilidad, calidad React y rendimiento

- CSS Grid/Flex responsive con `minmax(0,1fr)`, `min-width:0`, `max-inline-size`, `flex-wrap`, `clamp`, `overflow-wrap:anywhere`, `100dvh` y safe areas. Reflow desde 320px. Breakpoints por contenido: móvil <640, tablet 640–1099, desktop >=1100 como orientación; unificar con estilos reales, no cambiar por número si rompe POS. Tabla ancha tiene scroll local, no overflow horizontal global; no esconder desbordes con `overflow:hidden` en body. Menús/diálogos no se recortan. No controles duplicados en DOM accesible. No modal con pie fuera de pantalla. Formularios no requieren scroll horizontal. Zoom 200% y reflow equivalente 320 CSS px. Landscape 844x390 y teclado móvil. Impresión separada.
- Accesibilidad: WCAG 2.2 AA como objetivo; 4.5:1 texto normal, 3:1 grande y componentes pertinentes; foco visible/no cubierto, objetivos de 44px deseados (mínimo AA 24px con excepciones), labels, roles, aria-current, descripciones de error y estados live sin ruido. Tab, Shift+Tab, Enter, Escape; lector y atajo Alt+V conservados. No `tabIndex` positivo ni div clicable; no interacción solo hover; aria semántica antes que decoración.
- React: componentes funcionales, TS estricto, props pequeñas tipadas, separación de presentación y lógica, keys estables, derivar estado sin effects innecesarios, mantener estado de operación fuera de animaciones y superficies. Memoización solo con necesidad medida. Usar CSS para responsive, no listeners de resize en cada componente. No carga de iconos mediante assets remotos. No agregar librería UI/animación/chart/router. Preservar bundle y PWA; si cambia bundle, medir tamaño y explicar. No reescribir test suites masivamente para cambios visuales.
- Red y seguridad: preservar autenticación, CSRF, contratos y roles, scopes, autorización servidor, RLS, versiones, exactitud de cantidades/importes, idempotencia existente, recuperación, PWA y rate limiter. No ocultar operaciones inciertas al cambiar sección. No introducir auto submit, replay, simulación de acuse, ni autorización por caché. Verificar alta inicial parcial y marcadores de recuperación; corregir únicamente regresiones reproducibles del rediseño, no reconstruir lógica. Si hay defecto backend independiente, documentar ruta/código/repro, no editarlo sin autorización.
- Inventario de CSS: reemplazar colores hardcoded de UI por tokens/variantes; eliminar reglas viejas solo después de verificar consumidores. Mantener estilos particulares cuando tienen sentido operativo (POS/print). Buscar inconsistencias de alineación, tamaños, focus, sombra, borde, spacing, iconos y mensajes. No dejar dos sistemas visuales coexistiendo. Documentar reglas en `frontend/AGENTS.md` apuntando a este MD (sin borrar invariantes existentes).

**Checkpoint D4**: build y lint, regresiones de primitives/consumidores, tabla de cobertura por ruta y riesgos. Resolver `FOUNDATION_EVIDENCE_PLACEHOLDER` si sigue presente con evidencia auténtica, no confundir prueba anterior con nueva.

## D5 — Gate final: visual real, funcional y build servido

### D5.1 Validación de producto

Ejecutar build/lint/test completo frontend tras cambios, más `git diff --check`. Reutilizar resultados focalizados vigentes; no suites backend si no cambió backend. No disminuir cobertura, borrar tests ni subir timeouts indiscriminadamente. Cambiar assertions de copy/semántica solo cuando mejora real; mantener pruebas de integridad. Registrar conteo PASS/FAIL/SKIP, archivos y SHA/estado exacto. Si flakiness, registrar primera falla y raíz; no declarar PASS ignorándola. No considerar 615 tests como objetivo fijo ni inventar nuevo total.

### D5.2 Inspección visual (no opcional si navegador disponible)

Utilizar navegador/automatización existente y fixtures aislados. No añadir dependencias de runtime. No iniciar Google falso contra DEV. Para cada ruta y estado importante de D2–D3, revisar escritorio 1366x768 / 1920x1080, tablet 768x1024, móvil 390x844 y mínimo 320x640; landscape 844x390 y zoom 200% en pantallas densas. Comprobar `document.documentElement.scrollWidth <= document.documentElement.clientWidth`, overflow localizado, acciones accesibles, foco, diálogos con contenido largo, tablas vacías y llenas, etiquetas/nombres largos, estado pendiente y errores, reduced-motion. Pruebas DOM no prueban layout. Capturas reales del bundle, no mockup. Comparar antes/después mínimo: Inventario (prioridad), POS, Caja, Productos, Compras, Transferencias, Inicio, Personal, Configuración, onboarding, Superadmin (directorio y categorías), reporte, Excel/contingencia. Para los demás módulos registrar evidencia por ruta y viewport. No inventar datos de producción; usar fixtures sintéticos identificados. Guardar evidencia fuera de repo, sin tokens/cookies/correos/datos privados. No exportar screenshots sensibles a terceros. No prometer pixel-perfect si falta entorno. Si una superficie no se pudo validar, indicar `PENDIENTE_VISUAL` concreta. Aceptación real Chrome/lector/impresión/PWA sigue siendo gate separado si solo fixture.

### D5.3 Entregar localhost actualizado (evitar la repetición del problema de versiones)

Mantener la instancia DEV y sus datos intactos salvo reinicio explícitamente autorizado. Identificar PID del puerto 7243, ejecutable, versión y posibles operaciones activas. No asumir que HEAD = binario servido. Compilar el frontend actual (`npm run build`), verificar que no se desplegarán cambios no revisados. Solo tras autorización de reinicio, detener exclusivamente el PID identificado y arrancar con configuración privada existente desde `backend/`: `$env:ProductImportRecovery__EnabledAfterWriterDrain = 'false'`; `dotnet run --project src/MediPOS.Api --launch-profile https-local`. No aplicar migraciones, resetear DEV ni ejecutar S4. Verificar readiness HTTP 200 y `index.html`/hashes de JS y CSS servidos contra `frontend/dist`, registrar PID/URL. No borrar storage PWA, cookies, marcadores ni cache privada indiscriminadamente. Si requiere sesión Google, operador inicia. Si no hay autorización, entregar comandos y estado `BUILD_LISTO_PENDIENTE_REINICIO`; no fingir que localhost está actualizado. Probar navegación en las pantallas nuevas con operador, no crear registros reales ni ejecutar transacciones sin autorización. Los cambios de diseño no autorizan deploy remoto, commit, push, merge ni CI automático.

### D5.4 Aceptación final y entrega

- Diseño: azul marino izquierdo, fondo celeste, paneles blancos y acentos semánticos, sin restos morados accidentales ni zonas blancas sin jerarquía; misma gramática en cada módulo. Inventario debe coincidir con estructura D2.1. Ayuda integrada y comprensible. Modal/tabla/formulario/botón/tarjeta/pestaña consistentes. No acciones ficticias. No regresiones de onboarding, permisos y recuperación.
- UX: ninguna confirmación rutinaria o refresco manual reintroducido; una CTA por tarea, estados claros, redacciones naturales. Verificar navegación y densidad comercial real, no sustituir funcionalidad por estética. Distinción entre fixture, datos reales y aproximación por código.
- Evidencia: tabla por módulo con estado, capturas y viewport; lista de primitivas cambiadas, mapa de impacto, métricas de red/acciones si medidas, tamaño de bundle antes/después si disponible, pruebas exactas; hashes del build y estado DEV. No afirmar métricas ni latencia que no se midieron.
- Actualizar CURRENT_STATE.md y frontend/AGENTS.md con decisiones y link a este documento. Una conclusión consolidada D1–D5: qué se cambió, riesgos/regresiones, PASS/FAIL/SKIP, cobertura real, GO/NO-GO publicar, pendientes externos. No abrir más fases por estética. El piloto exige CI sobre SHA definitivo y gates externos previamente definidos; no quedan aprobados por diseño visual.

**Restricciones absolutas:** no backend, contratos API, DB, migraciones, S4 de DEV, OAuth, SUNAT, RLS, GRANT, secrets, CI, ni cambios de permisos; no nuevas dependencias sin justificación y aprobación separada. No repositorio plantilla importado masivamente, no duplicar sistema de diseño, no destruir datos. No commit/push/despliegue automático. Si un componente compartido afecta seguridad o navegación, preservar semántica y probar. La decisión de diseño no autoriza otra implementación de negocio.
