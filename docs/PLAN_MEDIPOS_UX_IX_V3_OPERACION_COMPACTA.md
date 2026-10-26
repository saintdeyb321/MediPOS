# MediPOS — Plan UX/IX V3: operación compacta y clara

**Fecha:** 26 de octubre de 2026  
**Estado:** frontend V3.1–V3.6 aplicado localmente; aceptación visual y funcional pendiente; extensiones backend sin aprobar  
**Ámbito:** frontend React/TypeScript; extensión de backend **solo con aprobación separada** cuando la capacidad sea imprescindible  
**Fuentes de diagnóstico:** capturas aportadas por el propietario (MediPOS frente a MigaPOS), experiencia de uso relatada y revisión estática del repositorio `saintdeyb321/MediPOS`, rama `ci/postgres-diagnostics-8bc6b4c`, commit observado `d45dabed5ef8fdf50ce4807c03690d27d57364d9`. **Los cambios locales no subidos podrían diferir.**  
**Propósito:** antes de reanudar simulaciones manuales, convertir MediPOS en una interfaz de trabajo **compacta, comprensible, útil desde la primera pantalla**. La inspiración en MigaPOS es de composición e interacción; **no copiar marca, activos visuales ni lógica de negocio de otro producto**.

---

## 0. Decisión de producto

> **Primero la tarea, después la explicación.** La pantalla de venta debe permitir encontrar el catálogo y el carrito inmediatamente; no obligar a atravesar grandes paneles de introducción o un formulario de caja. Una vista principal no es un manual. Lo secundario va a menús, detalles, modales, drawers o rutas dedicadas según su complejidad.

**No confundimos “compacto” con “apretado”:** reducir altura desperdiciada, repetición de títulos, anchos ineficientes y párrafos innecesarios; **conservar** tamaños cómodos de clic, etiquetas, accesibilidad, agrupación y mensajes de riesgo.

### Hallazgos confirmados en el código observado

| Problema | Archivo / situación visible | Cambio de criterio |
| --- | --- | --- |
| POS aparece demasiado abajo | `frontend/src/features/pos/PosPage.tsx`: `PosHeader`, `Notice` y `CashSessionPanel` se renderizan antes de `pos-layout` | POS arriba, estado de caja compacto; sin panel de apertura permanente |
| No hay catálogo antes de escribir | `frontend/src/features/pos/ProductSearchPanel.tsx`: `!text` muestra `EmptyState` | Categorías y productos de inicio **solo si pueden obtenerse de fuentes reales autorizadas**; nunca inventar stock |
| Tarjetas de búsqueda altas y textuales | `ProductResult` muestra campos, stock, selector, cantidad y texto a la vez | Tarjeta compacta; abrir selector de presentación/cantidad bajo demanda; mantener validaciones |
| Caja y POS duplican entrada | `PosPage` usa `CashSessionPanel`; `CashPage.tsx` duplica apertura y añade cierre | Un **punto de entrada** de caja desde POS, sin perder arqueo, listado de cajas de Owner ni rutas profundas |
| Cierre exige conteo pero no justificación persistente | `CashPage.tsx` envía `amount` a `useCashClose`; recibo expone `expectedCashAmount`, `countedCashAmount`, `cashDifference` | Diferencia visible desde cálculo **autorizado**; justificación persistente = brecha funcional/contrato a aprobar |
| Inicio repite opciones e indicadores largos | `DashboardPage.tsx` + `OwnerDashboard.tsx` con métricas `<dl>` lineales | Inicio con 3–4 accesos operativos y pocos KPIs; reportes profundos en Reportes |
| Cambio de sede / cuenta en cabecera | `WorkspaceHeader.tsx` contiene cambio de sede y cuenta/logout | Cambio de sede visible en sidebar; cerrar sesión **una sola vez** en pie del sidebar; móvil equivalente |
| Datos muy crudos en reportes | `ReportsPage.tsx` produce tablas, `OwnerDashboard` lista métricas | Gráficos **solo desde datos agregados reales**; sin series inventadas ni inferencia a partir de páginas incompletas |

### Límites no negociables

- Mantener roles, licencias, Google OAuth, CSRF, RLS, permisos por tenant y sede, autorización del servidor y límites de acceso.
- El stock depende de lote, fecha de vencimiento, sucursal y confirmación de compra. **Crear productos no crea existencias.**
- Preservar `useSaleDraft`, `useSaleCheckout`, `useCashSession`, `useCashClose`, recuperación de resultados inciertos, marcadores URL/identificadores, versiones, idempotencia y prohibición de doble envío.
- El efectivo esperado lo determina el **servidor**, con los movimientos aplicables. **Jamás** calcular saldo contable definitivo como `apertura + total de ventas` ignorando pagos electrónicos, devoluciones y transferencias.
- No implementar “motivo de diferencia” como texto aparente sin persistencia verificable. Si el contrato actual no acepta motivo, documentar requerimiento backend y solicitar aprobación antes de tocarlo.
- No auto-cerrar cajas antiguas, bloquear ventas por antigüedad de caja ni imponer políticas de turnos sin definición de producto y soporte de backend. Solo alertas basadas en tiempos reales.
- No eliminar `/caja` de inmediato: preservar rutas, enlaces, deep links y acceso del Owner a consulta de cajas ajenas cuando esté permitido; cambiar **ubicación en menú**, no borrar la capacidad.
- No añadir fotografías ficticias ni copiar imágenes de MigaPOS. Usar iniciales/iconos funcionales si no hay soporte de imágenes.
- No hacer escrituras a DEV, migraciones, cambios de secretos, sesiones administrativas, push ni despliegues.

---

## 1. Arquitectura de información y navegación

### Sidebar de escritorio (una sola fuente de verdad)

**Cabecera:** logo MediPOS + negocio actual.  
**Selector de sede:** debajo del negocio; mostrar sede activa, abrir selector propio; conservar controles de contexto y permisos.  
**Operación:** Inicio · **Punto de venta** · Productos · Inventario · Compras · Transferencias.  
**Gestión:** Personal · Reportes · Comisiones · Configuración · Notificaciones; los accesos sensibles según rol.  
**Más / Operación de caja:** “Arqueos y cajas” accesible desde POS / menú de funciones cuando el rol lo permita; no competir visualmente como una segunda tarea principal si el usuario está vendiendo. Mantener ruta `/caja` accesible.  
**Pie fijo del menú:** avatar compacto, rol real, **Cambiar botica** (distinto de cambiar sede), **Cerrar sesión** con una sola acción clara.  
**Cabecera de contenido:** breadcrumb compacto cuando corresponda, estado actual/alertas relevantes, sin duplicar título ni acciones del sidebar; “Mi cuenta” puede conservar información de perfil, sin un segundo botón de cierre de sesión.

**Móvil:** navegación desplegable con selector de sede, cuenta y cerrar sesión; sin atrapar el foco. Sidebar scrollable, pie de cuenta accesible sin recortar opciones ni duplicar controles en el contenido.

### Navegación contextual

- Usar breadcrumbs solo para niveles reales: `Productos > Paracetamol > Presentaciones`.
- Un único **Volver** por flujo (no mostrar simultáneamente breadcrumb “Inicio” + “Volver al inicio” arriba + otro en tarjeta).
- Drawer de detalle → se cierra y retorna al listado conservando filtros; modal de tarea → vuelve a la ficha/flujo previo.
- Navegación con `react-router`, no `window.history.back()` a ciegas si el origen anterior es externo o pertenece a otra botica.
- Mantener rutas profundas y marcadores de recuperación existentes; no cambiar automáticamente URL ni clave de tenant/sede.

---

## 2. Densidad, composición y sistema visual

### Regla de primera pantalla (criterio de diseño, no garantía universal)

En escritorio **1366×768 a 100% de zoom**, al abrir el POS se debe ver: **buscador + área inicial de productos/categorías + resumen del carrito + acción de caja o cobro** sin hacer scroll de la **página completa**. Si el catálogo o ticket exceden el alto disponible, desplazar sus **paneles internos** en escritorio. Con pantallas pequeñas, zoom o lector de pantalla se permite desplazamiento vertical normal.

**Objetivo de alturas, ajustable con el viewport real:** encabezado superior 56–64 px; subencabezado/estado del POS 52–76 px; barra de búsqueda 48–56 px; sin “hero” de 180 px ni banners permanentes de instrucciones. No fijar alturas rígidas que recorten contenido ampliado.

### Variables visuales (propuesta para codificar como tokens)

- Fondo de app: `#F3F4FA` · Superficie: `#FFFFFF` · Superficie secundaria: `#F8F7FD`.
- Primario: `#5842B6` · hover: `#46349A` · texto principal: `#1D2440`.
- Borde: `#DCE1EF` · texto secundario: `#59647B` · foco visible: `#235FDB`.
- Éxito: `#11724D` · advertencia: `#956200` · error: `#B42335`; verificar contraste sobre cada fondo.
- Escala espacial: **4 / 8 / 12 / 16 / 24 / 32 px**; listas y tarjetas normalmente 12–20 px; módulos 16–24 px.
- Radio base: 10–14 px (tarjetas), modal 16–20 px; sombra sutil y borde de separación sin exceso.
- Texto normal 16 px; secundario mínimo de referencia 14 px, priorizando contraste. Títulos 20–26 px, nunca jerarquía basada solo en tamaño.
- Botones operativos de referencia: altura 40–44 px; icon buttons accesibles con nombre; rejillas responsivas con `minmax` y `gap` uniforme.
- Color semántico, **no estado solo por color**. Sin cajas multicolor por decoración ni sombras pesadas por defecto.

### Sistema de componentes (extender los existentes, no duplicarlos)

`AppShell`, `CompactPageHeader`, `SectionCard`, `StatusChip`, `KpiCard`, `ProductTile`, `CategoryChips`, `CashStatusStrip`, `TicketPanel`, `EmptyState`, `ActionBar`, `Modal`, `Drawer`, `ConfirmDialog`, `Toolbar`, `ErrorSummary`, `Breadcrumb` y `ScopedBackAction`.

**Regla:** una sección = un propósito; una región = máximo una acción primaria; acciones secundarias en menú o drawer. Dividir modales complejos en pasos cuando de verdad sea necesario, sin convertir cada clic en modal. En escritorios, preferir **contenedores con scroll propios** solo para catálogo y carrito, evitando scrolls anidados confusos en el resto de módulos.

### Modales y confirmaciones

- Apertura de caja: modal de 2–3 elementos, sin página intermedia; estado de la caja siempre visible en POS.
- Cierre/arqueo: modal o drawer de revisión con cifra esperada, conteo físico, diferencia y acciones; no cerrar automáticamente al confirmar hasta tener respuesta cierta.
- Confirmación con error: conservar campos y estado; foco al error; no duplicar POST al reabrir.
- Modal nativo accesible: título, foco inicial razonable, Escape, retorno al disparador, bloqueo solo durante petición realmente activa y recuperación segura de inciertas.

---

## 3. Flujo POS prioritario (sprint más importante)

### Estructura visual esperada

```text
┌ Barra global: negocio / sede / cuenta breve (sin duplicados) ──────────────────────────┐
│ PUNTO DE VENTA         [● Caja abierta · S/20.00] [Gestionar / Cerrar]                │
├────────────────────────────────────────────────────┬─────────────────────────────────┤
│ [ Buscar medicamento, código, marca...          ] │ TICKET · 0 productos            │
│ [Todos] [Analgésicos] [Vitaminas] [...]           │ ─────────────────────────────── │
│                                                    │ Producto / presentación / cant. │
│ [ Producto ] [ Producto ] [ Producto ]             │                                 │
│ [ Producto ] [ Producto ] [ Producto ]             │                                 │
│                 (catálogo scroll propio)           │ (ticket scroll propio)          │
│                                                    │ TOTAL   S/ 0.00                 │
│                                                    │ [Cobrar] o [Abrir caja]         │
└────────────────────────────────────────────────────┴─────────────────────────────────┘
```

**No usar este esquema como tamaño fijo:** son proporciones objetivo. En escritorio `minmax(0, 1fr)` y panel de ticket de ~340–420 px según viewport; a menor ancho apilar y dar acceso persistente a carrito. Mantener contexto de sede.

#### Caja en POS

- **Cerrada**: chip “Caja cerrada”; CTA principal `Abrir caja` dentro del **ticket**, a una sola interacción del modal. Se puede **consultar catálogo** sin caja si el contrato lo permite, pero no realizar cobros sin la sesión exigida.
- **Abierta**: chip “Caja abierta” + importe inicial/antigüedad resumidos. Acción secundaria `Gestionar caja` abre drawer/menú: consultar, arqueo, cerrar. Solo roles autorizados pueden gestionar otras cajas.
- **Antigua**: señal no bloqueante “Caja abierta desde hace X h; revisa el cierre del turno” basada en `openedAt` real **si está disponible**. Umbrales por decisión del producto (propuesta: aviso a 8 h, advertencia fuerte a 24 h), sin convertirlos en política del backend.
- Si falta hora de apertura confiable, mostrar “Antigüedad no disponible”, no inventar tiempo ni inferirlo de `updatedAt`.

#### Catálogo inicial

- **No mostrar una página vacía por defecto.** Solicitar a Codex que inspeccione endpoints reales de catálogo/búsqueda de su ámbito autorizado.
- Si la API soporta **exploración paginada con stock vendible** y categorías, poblar productos iniciales y chips. Si solo soporta buscar por texto, **no inventar** un listado usando datos incompletos o un GET excesivo; implementar estado útil con foco al buscador, categorías reales si están disponibles, o proponer endpoint *read-only* para aprobación aparte.
- Categorías visibles al inicio **solo si vienen de categorías existentes**, sin chips irrelevantes; indicar alcance real de consulta. Seleccionar categoría debe cambiar resultados con respaldo real.
- `ProductTile`: nombre, precio efectivo de presentación (si se conoce), stock vendible según servidor, símbolo/tipo y acción Agregar. Presentación y cantidad en mini-panel contextual/drawer, sin incrustar 6 campos en cada tarjeta.
- Priorizar código de barras, teclado y rapidez sin permitir productos sin stock o lotes vencidos.

#### Carrito y cobro

- Resumen siempre visible en escritorio: productos, unidades, descuento solo si backend existe, total, estado de caja y CTA único.
- Pago: modal ordenado por método habilitado; Yape/Plin/tarjeta nunca contarse como billetes físicos. Mantener importes exactos y confirmación; estados pendientes visibles.
- Datos del producto, lote, precio base y factor de conversión conservan las reglas del servidor. No simular reservas, imágenes ni descuentos no implementados.

### Criterios de aceptación del POS

1. A 1366×768 con sidebar, catálogo y ticket son visibles sin desplazar la página completa (zoom 100% y datos típicos).
2. No existen dos llamadas a la acción simultáneas para abrir caja ni dos botones de “volver al inicio”.
3. Abrir caja se hace desde el ticket mediante un modal breve; la caja abierta muestra su estado sin bloquear ventas con un panel gigante.
4. Sin búsqueda, no hay una enorme tarjeta de vacío; si existe endpoint de lectura general, se muestran categorías/productos reales.
5. Cantidad y presentación no desbordan `ProductTile`; cuando se expanden, se ve el contexto.
6. Una operación pendiente de pago no queda oculta ni permite repetir el cobro.

---

## 4. Apertura, arqueo y diferencia de caja

### Modelo contable correcto

**Se debe usar `expectedCashAmount` autorizado por el backend o un endpoint de vista previa equivalente**, y no reconstruir el saldo con un cálculo parcial de frontend. El efectivo esperado incorpora **solo** los componentes pertinentes: apertura, cobros en efectivo, devoluciones efectivas, entradas/salidas de sencillo y otros movimientos que el dominio contemple. Los pagos digitales **no suman** a efectivo físico.

```text
Efectivo esperado (dato de servidor):  S/ 50.00
Efectivo contado por el cajero:       S/ 55.00
Diferencia = contado − esperado:       +S/ 5.00  → SOBRANTE

Efectivo esperado (dato de servidor):  S/ 50.00
Efectivo contado por el cajero:       S/ 40.00
Diferencia = contado − esperado:       −S/ 10.00 → FALTANTE
```

**Ejemplo válido:** apertura S/ 20 y S/ 30 de ventas efectivamente **cobradas en efectivo**, sin devoluciones, transferencias ni otros movimientos ⇒ esperado S/ 50. Si esos S/ 30 fueron vía Yape, **no** serían S/ 50 en efectivo.

### UX propuesto para cierre

1. Abrir drawer/modal `Cerrar y cuadrar caja` desde el POS o la gestión de caja.
2. Mostrar resumen documentado y **monto esperado solo si API lo expone con vigencia y permisos**.
3. Campo `Efectivo físico contado`, editable y validado con precisión decimal exacta.
4. Comparar contra dato **autoritativo**: `Cuadre exacto`, `Sobran S/ 5.00` o `Faltan S/ 10.00`; icono + texto y contraste, no solo color.
5. Si diferencia distinta de cero, mostrar `Motivo / justificación` (requerido por regla de negocio **pendiente de aprobación**). Ejemplo: “Sobraron S/ 5.00. Explica la diferencia antes de confirmar”. No permitir que un campo **no persistido** se presente como justificación auditada.
6. Pantalla de confirmación con datos finales y un solo envío. Tras la respuesta cierta, mostrar recibo persistido y diferencia final **tal como la calculó el servidor**.
7. Ante operación incierta, bloquear nuevo cierre y ofrecer `Consultar este cierre` con el mismo identificador.

**Brecha de contrato identificada:** el `CashPage.tsx` observado envía solamente `amount` al cierre y la lectura del recibo trae `expectedCashAmount`, `countedCashAmount` y `cashDifference`. No consta en esa ruta un campo persistente `reason` para faltantes/sobrantes ni un preview garantizado antes del POST. **Codex debe inspeccionar contrato real**: (a) si existe preview/reason, usarlo; (b) si no, implementar solo lo que pueda garantizar el frontend y entregar propuesta acotada de backend separada para aprobación; nunca guardar la justificación solo en estado React como si fuera auditoría real.

### Cajas de varios días

- Mostrar antigüedad real si disponible, con confirmación del servidor; no declarar “sesión vencida” sin contrato.
- Advertencia no bloqueante + acceso directo a arqueo/cierre.
- El cierre sigue requiriendo credenciales/rol, caja vigente, lectura actual y confirmación servidor.

---

## 5. Otras pantallas: reducir carga cognitiva sin amputar funcionalidades

| Módulo | Primera pantalla | Secundario bajo demanda |
| --- | --- | --- |
| **Inicio** | 3–4 accesos: Vender, Productos, Recibir, Inventario; 3–4 indicadores indispensables, alerta relevante | Detalle gerencial, gráficos, reportes y explicación de cálculo → **Reportes**. No duplicar toda la navegación en 25 tarjetas |
| **Productos** | búsqueda, filtros/categoría, listado compacto/grid, CTA `Nuevo producto` | detalle drawer; precios/presentaciones en tareas focalizadas; existencias en Inventario, con link |
| **Inventario** | stock por sede, alertas FEFO/vencimiento visibles y tabla clara | lotes/movimientos/filtros avanzados y ajuste en modal justificado; no desambiguar mal stock físico vs disponible |
| **Compras** | consultas recientes reales y CTA `Nueva recepción` | proveedor → líneas/lote/costo → revisión → confirmación por pasos con resumen lateral; recuperación preservada |
| **Personal** | directorio legible con rol/sede/estado, `Invitar integrante` | ficha, horario, sucursales, bloqueo; dejar claro “invitación por enlace” hasta tener servicio de email real |
| **Transferencias** | listado y estados por sucursal, CTA único | flujo solicitud/despacho/recepción por lotes, sin esconder reconciliación de estados inciertos |
| **Reportes** | KPIs relevantes + series y comparaciones reales del periodo, filtros compactos | desglose tabular, exportación, definiciones y datos técnicos en panel avanzado. No falsificar gráficos con una sola cifra agregada |
| **Comisiones** | estado actual, resumen de reglas y CTA claro | edición, historial/versiones, activación/desactivación protegidas |
| **Notificaciones** | alertas accionables, pendientes y estado | detalle y auditoría |
| **Configuración** | tarjetas por función: negocio, sedes, licencia, permisos | formularios focalizados, sin mostrar CRUD completo al cargar |
| **Plataforma** | negocios/licencias/resumen claro para Superadmin | invitar Owner por email es **función pendiente de backend**; no simularla con UUID ni prometer correo automático |
| **Contingencia** | estado online/offline y acción pertinente | cola y recuperación con toda su integridad; nunca mostrar envío local como venta final |

**Gráficos:** solo series por fecha/categoría que estén disponibles en endpoints o derivados correctamente de resultados completos y autorizados. Si el backend no provee datos suficientes: estado vacío ilustrativo de tipo de gráfica sin valores artificiales y propuesta de contrato para aprobación. Separar `0 real` de `sin datos` y de `sin permisos`.

---

## 6. Accesibilidad, legibilidad y reglas de interacción

- Objetivo WCAG 2.2 AA: reflow a 320 CSS px (con excepciones justificadas para datos tabulares), foco visible, etiquetas y descripciones programáticas, contraste AA, teclado completo, nombres accesibles y navegación coherente.
- Botones preferentemente ≥40 px alto en interfaz operativa; cumplimiento mínimo WCAG 2.2 sobre tamaño/espaciado de objetivo interactivo.
- Modal con foco gestionado, Escape, devolución de foco; no cerrar durante POST en vuelo. Si el servidor da respuesta incierta, permitir salida **segura** manteniendo pendiente persistido e impidiendo reenvíos.
- Evitar scroll vertical de página principal del POS en escritorio; no imponer prohibición global de scroll en pantallas pequeñas o cuando usuario aumente zoom.
- Mensajes de error cerca del campo y resumen accesible si hay varios; no borrar entradas después de fallos de validación.
- Campos monetarios: utilizar utilidades decimales exactas existentes; no usar `parseFloat` o `Number` para resultado contable, no redondeos silenciosos.
- Estados descriptivos (“Caja cerrada: ábrela para cobrar”) en lugar de textos largos y redundantes. Consejos avanzados detrás de “¿Cómo funciona?”.
- Respeto por reduced motion, zoom y tamaño de fuente; no ocultar información indispensable detrás de hover.

**Referencias externas de UX** (principios, no copia literal):

- NN/g, Progressive Disclosure: https://www.nngroup.com/articles/progressive-disclosure/
- NN/g, Recognition and Recall: https://www.nngroup.com/articles/recognition-and-recall/
- USWDS, Design Tokens: https://designsystem.digital.gov/design-tokens/
- W3C, WCAG 2.2: https://www.w3.org/TR/WCAG22/
- GOV.UK Design System, Recover from validation errors: https://design-system.service.gov.uk/patterns/validation/
- WAI ARIA APG modal dialog: https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/

---

## 7. Implementación en seis sprints medianos

**Modo de trabajo:** Codex avanza sprint a sprint conservando el working tree. Cambios de diseño ordinarios no requieren interrumpir; **sí se detiene** si necesita cambiar contrato de backend, migración, regla contable o autorización. El usuario hará aceptación funcional manual. `npm.cmd run build` al final de cada sprint; evitar suites grandes y auditorías paralelas. No convertir el build en prueba de comportamiento.

| Sprint | Alcance | Resultado esperado | Preservar especialmente |
| --- | --- | --- | --- |
| **V3.1 — Shell compacto** | Sidebar: sede/cuenta/salir, nav, breadcrumbs, título único, tokens compactos, modales; quitar duplicaciones | Base sin redundancias ni espacios gigantes | rutas, roles, selección de sede, Google |
| **V3.2 — POS primero** | `PosPage`, `PosHeader`, `ProductSearchPanel`, tarjetas/ticket, categoría real o fallback honesto; CSS responsive | Buscar, explorar si lo permite API, agregar, cobrar sin scroll de página inicial | stock por lote, carrito, permisos y cobro único |
| **V3.3 — Caja integrada** | entrada caja en ticket, modal apertura, gestión/cierre, antigüedad no bloqueante, arqueo/diferencias; legibilidad de caja Owner | Cero panel gigante; caja comprensible desde POS | IDs y estados, cobro en efectivo vs digital, conciliación; sin razón ficticia |
| **V3.4 — Inicio y reportes** | home muy breve, KPIs, gráficos con datos reales, Reportes visuales y detalle bajo demanda | Menos repetición en Inicio, analítica profunda en Reportes | scopes, exactitud, paginación, exportación |
| **V3.5 — Operación restante** | Productos, Compras, Inventario, Personal, Transferencias y Configuración, coherencia de cards/actions | Todas las pantallas muestran acción importante sin ruido | lotes/vencimiento, versiones, invitaciones, rutas |
| **V3.6 — Pulido final** | Auditoría de consistencia **visual focalizada**, responsive, densidad, teclado, vacíos, modales, pequeñas correcciones | Lista de aceptación para propietario; issues backend explícitos | No rediseñar lógica ni crear datos |

### Entrega exigida al final de cada sprint

- Archivos cambiados; qué redundancias eliminó; qué acciones quedaron visibles.
- Confirmación de `npm.cmd run build` con error o PASS (sin afirmar tests que no se ejecutaron).
- Captura textual (estructura visual) o instrucciones de revisión visual por el usuario; si no tiene navegador disponible, **no afirmar** que vio UI correctamente.
- Bloqueos funcionales separando: “frontend resuelto”, “backend necesario”, “diseño pendiente de aceptación”.

---

## 8. Criterios de aceptación manual del propietario

### POS (prioridad 0)

- [ ] En 1366×768, con una sede elegida, se ve buscador, zona de catálogo y ticket sin scroll de **la página completa** a zoom 100%.
- [ ] Una sola ubicación para `Abrir caja` cuando está cerrada; se abre modal breve.
- [ ] Caja abierta: chip compacto; se puede iniciar búsqueda y venta sin pasar por panel de caja.
- [ ] No hay doble “Volver al inicio”; cambio de sede en sidebar; logout visible en pie del sidebar.
- [ ] El catálogo vacío inicial presenta una opción útil y **no falsifica** productos ni disponibilidad.
- [ ] Tarjetas simétricas sin campos extensos; añadir producto abre cantidad/presentación cuando corresponde.
- [ ] Ticket legible, total destacado, cobro seguro; pendiente de confirmación impide duplicar.

### Caja

- [ ] Abrir S/ 20, simular S/ 30 de **efectivo** (cuando haya ventas reales), revisar lo esperado en backend.
- [ ] Con esperado real S/ 50: contar S/ 55 → mensaje `Sobran S/ 5`; contar S/ 40 → `Faltan S/ 10` **solo con vista previa autorizada**.
- [ ] Si no existe API previa de arqueo, mostrar la diferencia **después** del cierre confirmado, y registrar la necesidad de preview/motivo en backend; no fingir persistencia.
- [ ] Caja de varios días: aviso de antigüedad, no autocierre ni bloqueo oculto.

### Global

- [ ] Inicio muestra pocas métricas, navegación sencilla, nada de muro de indicadores.
- [ ] Cada ruta tiene título único; subflujo tiene un regreso contextual, no tres botones repetidos.
- [ ] Layout y modales se ven a 390/768/1280/1366/1440 px; a 200% zoom no corta controles ni impide enviar formularios.
- [ ] Reportes muestran gráficos **solo con datos reales suficientes** y una ruta al detalle o a la tabla.
- [ ] Ninguna operación crítica pierde reconciliación o repite acciones tras recarga.

---

## 9. Brechas funcionales detectadas: NO fingir solución visual

1. **Justificación de sobrante/faltante persistente y vista previa del cierre**: pedir a Codex inspeccionar contratos. Si faltan, proponer mínimo endpoint/DTO/almacenamiento/auditoría y solicitar aprobación independiente; no codificar backend sin autorización.
2. **Exploración inicial de productos en POS por categorías**: si no hay endpoint con disponibilidad autorizada y lectura acotada, proponer `GET` read-only paginado con soporte de categoría y stock vendible; no reutilizar APIs privadas de Owner en cajeros.
3. **Gráficos analíticos**: si el servidor solo entrega agregados sin series, requerir contrato read-only con series temporal/categoría y filtros autorizados; sin inventar cifras.
4. **Alertas de caja por duración**: requieren fecha/hora de apertura confiable y política de horas configurable; indicador no equivale a autorización o cierre.

---

## 10. PROMPT MAESTRO PARA CODEX — ejecutar V3.1 a V3.6

> Copiar este bloque a Codex después de adjuntar este MD o guardarlo en `docs/PLAN_MEDIPOS_UX_IX_V3_OPERACION_COMPACTA.md`.

```text
MEDIPOS — UX/IX V3: SIMPLIFICACIÓN OPERATIVA PROFUNDA, NO OTRA CAPA DE PANELES

Lee íntegramente docs/PLAN_MEDIPOS_UX_IX_V3_OPERACION_COMPACTA.md y ejecuta V3.1 a V3.6 en orden sobre C:\Proyectos\MediPOS, conservando todos mis cambios locales.

La última captura demuestra que el POS muestra casi toda la primera pantalla como títulos duplicados, instrucciones y panel gigante de caja. Eso es un fallo de jerarquía. Quiero ver BUSCADOR + PRODUCTOS/CATEGORÍAS REALES + TICKET + ABRIR CAJA/COBRAR desde el primer vistazo. Usa MigaPOS solo como referencia de composición, no como código o marca.

PRIORIDADES:
1) Sidebar: selector de sede visible, cuenta y único logout al pie; sin cambio de sede/logout duplicados en cabecera. Mantener selección de botica y permisos.
2) POS: eliminar hero alto, avisos permanentes, botones volver duplicados y panel enorme de caja. En escritorio a 1366x768, el catálogo y ticket deben estar en la primera pantalla sin scroll de página completa; que el catálogo y carrito tengan scroll interno si crecen. Responsive accesible.
3) Caja cerrada: CTA “Abrir caja” dentro del ticket; modal corto. Caja abierta: chip compacto con estado y acceso a gestionar/cerrar. El módulo /caja y la consulta de cajas de Owner deben seguir disponibles a través de gestión contextual/ruta existente; no borrar lógica ni enlaces profundos.
4) Productos POS: tiles uniformes y compactos; categorías visibles y catálogo inicial SOLO si los endpoints reales autorizados pueden proveerlos. Sin inventar stock/precios/imágenes ni forzar APIs de Owner a cajeros. Si falta endpoint, deja fallback honesto y anota bloqueo funcional sin implementar backend.
5) Arqueo: usar expectedCashAmount del servidor; efectivo físico excluye Yape/Plin. Ejemplo: apertura S/20 + cobros efectivo S/30 (sin otros movimientos) = esperado S/50. Si se cuentan S/55, sobrante S/5; con S/40, faltante S/10. Mostrar diferencias antes del cierre SOLO si un contrato autorizado las permite; si no, después de la confirmación. Motivo de diferencia persistente requiere backend y aprobación: NO fingir guardar un texto solo en React.
6) Inicio: pocas acciones e indicadores; detalles a Reportes. Reportes: KPIs y gráficos derivados únicamente de series agregadas reales; no dibujar estadísticas con datos falsos.
7) Todos los demás módulos: reducir redundancia y texto continuo, no ocultar errores críticos, recuperar operaciones inciertas, no permitir doble POST. Modales y drawers proporcionados, UI simétrica, colores y tokens semánticos, breadcrumbs/back únicos, accesibilidad real.

REGLAS DE EJECUCIÓN:
- Sprints medianos V3.1 a V3.6 del MD, progresivos y sin pedir permiso por detalles visuales ordinarios.
- No cambiar backend, migraciones, contratos, auth, permisos, RLS, cálculos monetarios, idempotencia o base PostgreSQL. Si una capacidad requiere backend, escribe propuesta separada y pide aprobación; no la implementes a escondidas.
- No crear datos de demo, ejecutar pruebas funcionales ni suites automatizadas masivas, hacer push o desplegar.
- Al cerrar cada sprint, ejecutar `npm.cmd run build` y reportar solo archivos, decisiones, PASS/error y limitaciones verificables. El usuario hará la aceptación manual en navegador.
- Preservar URLs actuales, contextos botica/sede, deep links, estados de conciliación, IDs de operación y autorizar cada acción en backend.
- No usar `parseFloat`/redondeos JS para dinero; reutilizar utilidades decimales exactas.
- No considerar terminado porque “se ve más violeta”; cumplir los criterios de primera pantalla, menos duplicación y tareas rápidas del documento.

Comienza con V3.1 y continúa secuencialmente hasta V3.6, deteniéndote solo ante un bloqueo funcional real o necesidad de backend. Entrega al final una tabla con los criterios de aceptación que sí dependen de mis simulaciones.
```

---

**Fin del documento.**

## 11. Resultado de implementación local — 2026-10-26

Los seis sprints se implementaron en orden sobre el frontend existente. Cada uno tuvo `npm.cmd run build` PASS (TypeScript + Vite). Este resultado certifica compilación, no distribución observada en Chrome ni integración funcional. No se ejecutaron suites, simulaciones, escrituras comerciales, PostgreSQL ni despliegues. La advertencia Vite de chunk JS >500 kB permanece visible.

| Sprint | Redundancias retiradas y estructura resultante | Estado verificable |
|---|---|---|
| V3.1 | Sede/cuenta/logout en sidebar; cabecera solo contexto, sin título duplicado. Breadcrumb únicamente en subflujos reales, sin segundo Volver. `/caja` conservada en Más y Comisiones según rol | Código integrado + build PASS; aceptación manual pendiente |
| V3.2 | POS: fila compacta de título/estado → buscador y catálogo a la izquierda → ticket/total/CTA a la derecha. Sin hero, instrucciones iniciales ni panel permanente de caja. Presentación/cantidad se abren en modal; líneas del ticket compactas | Código integrado + build PASS; primera pantalla a 1366×768 pendiente de comprobar |
| V3.3 | Una apertura desde el ticket; gestionar/cerrar en drawer/modal con el mismo controlador. Arqueos/otras cajas Owner siguen en `/caja`. Sobrante/faltante desde `cashDifference` del recibo; esperado siempre del servidor. Recordatorios 8/24 h no bloqueantes | Código integrado + build PASS; flujos reales de caja pendientes |
| V3.4 | Inicio: hasta cuatro accesos autorizados y cuatro KPIs de hoy, sin reportes completos ni dos consultas gerenciales adicionales. Reportes conserva filtros, paginación/exportación y gráficos reales; una observación no dibuja comparación | Código integrado + build PASS; aceptación manual pendiente |
| V3.5 | Compras prioriza recuperar una recepción y muestra el editor al iniciar el flujo, sin desmontar controles pendientes. Personal: lista inicial y una invitación principal. Configuración muestra una sección a la vez. Productos/inventario/tablas tienen menor densidad de paneles y detalle bajo demanda | Código integrado + build PASS; aceptación manual pendiente |
| V3.6 | Reflow vertical cuando no cabe el escritorio, menú con footer accesible en alturas cortas, foco inicial al buscador y al error tras petición, estado de venta pendiente visible junto a caja; controles críticos conservan su bloqueo | Revisión de composición/CSS focalizada + build PASS; teclado/zoom/navegador pendientes |

Estructura textual del POS preparado (sin afirmar captura):

```text
Sidebar: negocio / sede / módulos / cuenta / cambiar botica / salir
Cabecera: contexto de sucursal, sin acciones duplicadas
POS + estado/antigüedad de caja + pendiente de venta si existe
┌ Buscador y categorías reales ─────────┬ Ticket en preparación ┐
│ Referencias Owner o búsqueda por rol │ Líneas editables      │
│ Tarjetas → presentación/cantidad     │ Total estimado        │
│ Scroll de catálogo en escritorio    │ Abrir caja o Cobrar   │
└─────────────────────────────────────┴ Gestionar/recuperar ───┘
```

No se borraron ni reconstruyeron `useSaleDraft`, `useSaleCheckout`, `useCashSession` o `useCashClose`. La composición mantiene los controladores montados al ocultar superficies. Los cierres pendientes conservan `cash`/`closingCash`, y las ventas sus marcadores/versiones originales. Cobrar no queda habilitado mientras el cierre de caja está pendiente. El estado de Google, RLS, permisos, migraciones y bases permanece intacto.

### Archivos modificados en V3

- Shell: `frontend/src/app/navigation.ts`, `layouts/WorkspaceNavigation.tsx`, `WorkspaceHeader.tsx`, `WorkspaceShell.tsx`, `workspace.css`, `router/routes.tsx`.
- POS: `frontend/src/features/pos/PosPage.tsx`, `PosHeader.tsx`, `ProductSearchPanel.tsx`, `PosCatalogBrowser.tsx`, `SaleCartPanel.tsx`, `CheckoutSummary.tsx`, `SaleCheckoutPanel.tsx`, `pos.css`.
- Caja: `frontend/src/features/cash/CashPage.tsx`, `CashSessionPanel.tsx`, `CashAge.tsx`, `CashDifference.tsx` (nuevo), `index.ts`.
- Inicio/reportes: `frontend/src/features/dashboard/DashboardPage.tsx`, `dashboard.css`; `frontend/src/features/reporting/OwnerDashboard.tsx`, `ReportFilters.tsx`.
- Otros módulos: `frontend/src/features/catalog/ProductsPage.tsx`, `catalog.css`; `features/inventory/InventoryPage.tsx`; `features/purchasing/PurchasesPage.tsx`; `features/employees/EmployeesPage.tsx`, `EmployeeInvitationsPanel.tsx`; `features/settings/SettingsPage.tsx`; `features/transfers/CashTransfersPage.tsx`.
- Compartidos: `frontend/src/shared/ui/Modal.tsx`, `MetricCard.tsx`, `BarChart.tsx`, `ui.css`; `shared/styles/operations.css`; este plan.

### Extensiones propuestas de backend — NO implementadas; requieren aprobación separada

1. **Vista previa y motivo auditado de diferencia de caja.** Evidencia: `CashSessionStatusQueries.StatusAsync` devuelve `session.ExpectedCashAmount`; `CashSession.Close` asigna ese valor al cerrar. `Tenant/PosEndpoints.cs` define `CloseCashRequest(decimal CountedCashAmount)` sin motivo. Propuesta: lectura autorizada por tenant/sede/caja de `expectedCashAmount`, instante y revisión del ledger; cierre transaccional con la revisión esperada y motivo persistido cuando el resultado final tenga diferencia. Rechazar una revisión obsoleta con conflicto y exigir nueva lectura; registrar actor/fecha/motivo en historial auditable. El esperado debe seguir calculándose desde efectivo y sus reversos/transferencias; nunca desde todas las ventas. Esto requiere diseño de contrato/persistencia y aprobación antes de modificarlo. Hoy la UI muestra la diferencia solo desde el arqueo confirmado, sin campo de motivo ficticio.
2. **Exploración inicial operativa paginada del POS.** Evidencia: `CatalogDirectoryQueries.CategoriesAsync` y `ProductsAsync` exigen `Administration`; `ProductMatch` de búsqueda no contiene categoría. Propuesta de GET read-only de sucursal con categoría y paginación acotada, reutilizando autorización `ProductSearch` y stock vendible/FEFO del servidor; devolver identidad, categoría, precios exactos, presentaciones y disponibilidad real. Sin exponer APIs Owner ni costos a trabajadores. Hoy Owner explora referencias de una página y consulta stock al elegir; otros roles disponen de búsqueda útil, sin catálogo inicial simulado.
3. **Series analíticas reales.** `reporting/contracts.ts` ofrece agregados y dimensiones sucursal/empleado/producto/categoría, no una serie diaria ni reparto general por método de pago. Propuesta read-only Owner con periodo/alcance validados, zona Lima, puntos diarios completos y totales coherentes; el desglose por medio debe proceder de pagos/reversiones reales y distinguir efectivo de digitales. La UI actual conserva gráficos agregados disponibles y tablas paginadas, sin derivar series de páginas incompletas. Los pagos por medio se muestran únicamente para el arqueo real consultado.

No se solicitó ni implementó una política de cierre automático. Los umbrales visuales 8/24 h no son vencimiento de sesión ni regla de autorización; hacerlos configurables por tenant sería otra decisión separada.

### Aceptación pendiente del propietario

| Comprobación manual | Evidencia que aún falta |
|---|---|
| 1366×768, zoom 100%, POS inicial | Buscador, categorías/referencias autorizadas y ticket/CTA visibles sin scroll global con datos habituales |
| Caja cerrada/abierta | Un único Abrir caja desde ticket, modal breve, estado compacto y drawer de gestión |
| Sobrante/faltante | Cerrar personalmente y comprobar el mensaje contra esperado/contado/diferencia del servidor; sin preview ni motivo persistido hasta aprobación |
| Pago y respuesta incierta | Confirmación única, mismo ID al consultar, aviso de pendiente visible y sin otra escritura habilitada |
| Navegación | Sede y único logout en menú visible, `/caja` y cajas Owner accesibles, un regreso por subflujo |
| 390/768/1280/1366/1440 y zoom 200% | Reflow, scroll y footer de menú accesibles; ningún campo/CTA recortado |
| Teclado y modales | Foco en búsqueda, selector contextual, errores, Escape y regreso sin perder marcadores |
| Inicio/reportes/otros módulos | Pocos KPIs, gráficos con alcance/página explícitos, filtros y recuperación preservados |

Siguiente paso: aceptación manual del frontend preparado. No se declara ningún sprint DONE por el build ni se autorizan cambios backend con este registro.