# MediPOS Frontend — AGENTS.md

> **Propósito:** reglas permanentes para Codex y cualquier agente que trabaje dentro de `frontend/`.
> **Producto:** MediPOS, SaaS multiempresa y multisede para boticas/farmacias independientes del Perú (segmento inicial: 1–5 sedes).
> **Fase:** construcción progresiva de una PWA web; **no** confundir frontend listo para desarrollar con lanzamiento productivo autorizado.
>
> Este archivo complementa el `AGENTS.md` de la raíz. **No sustituye** los requisitos del producto ni los contratos HTTP reales.

## 1. Jerarquía de decisiones y contexto mínimo

Antes de editar código:

1. Leer `../AGENTS.md` y este archivo.
2. Consultar **solo** los IDs de requisitos afectados en `../docs/SPEC.md`.
3. Consultar únicamente las secciones pertinentes de `../docs/ARCHITECTURE.md` y `../docs/ADR.md` si corresponde.
4. Para integración, inspeccionar los **contratos actuales** de `../backend/src/MediPOS.Api/` y la matriz HTTP de `../backend/README.md`. Si está disponible, usar el OpenAPI de Development como contrato verificable.
5. Revisar componentes, hooks, tipos y estilos existentes antes de crear otros.

**Prioridad en conflicto:** instrucción explícita del usuario > `../AGENTS.md` y este archivo según su ámbito > `../docs/SPEC.md` > `../docs/ARCHITECTURE.md` > ADR aprobados > contratos implementados y pruebas. Si hay ambigüedad de negocio, seguridad o clínica, **no inventar** una regla.

**Disciplina de alcance:** un sprint entrega una funcionalidad vertical comprobable. No reescribir el diseño entero ni incorporar módulos no solicitados para “preparar el futuro”.

## 2. Principios de ingeniería obligatorios

- **Simplicidad antes que abstracción:** Clean Code, KISS, DRY con criterio, YAGNI, alta cohesión y bajo acoplamiento. SOLID donde aporte claridad; no convertirlos en capas ceremoniales.
- **Separación de responsabilidades:** una página compone; un componente muestra/interactúa; un hook orquesta el caso de uso de UI; el cliente API realiza transporte; un adaptador transforma DTOs; el backend decide reglas comerciales.
- **Composición sobre herencia:** preferir props, slots, composición y funciones puras a componentes base gigantes con decenas de flags.
- **Modularización por funcionalidad del negocio (*feature-first*), no por tipo de archivo a escala global.**
- **Una única fuente de verdad:** el backend y PostgreSQL gobiernan ventas, stock, caja, licencia, acceso, lotes, impuestos y comisiones. La UI no vuelve a implementar reglas ni inventa estados.
- **Tipado estricto:** prohibido `any` sin justificación revisada, `@ts-ignore` para esconder errores, `as unknown as` para eludir contratos, enums numéricos inventados o respuestas HTTP sin tipar.
- **Legibilidad:** nombres descriptivos en inglés para código y tipos; textos visibles en español claro del Perú. Evitar funciones enormes, booleanos ambiguos y comentarios que repiten el código.
- **Sin duplicar:** buscar antes de crear. Extraer a `shared` solo cuando sea verdaderamente reutilizable y no dependa de conceptos de un módulo. No abstraer prematuramente una pieza por una sola coincidencia superficial.
- **Dependencias mínimas:** justificar dependencias importantes; no introducir frameworks de estado, UI o CSS alternativos que hagan lo mismo.
- **Cambios pequeños y revisables:** no mezclar rediseño visual con cambios de contratos y refactorizaciones no relacionadas.

## 3. Stack y decisiones de UI

### Stack base

- React + TypeScript estricto + Vite: SPA/PWA estática, sin servidor Node permanente.
- React Router: rutas, *layouts* y navegación.
- TanStack Query: estado del servidor, caché, revalidación y mutaciones.
- Tailwind CSS: estilos consistentes basados en tokens.
- Lucide React: iconografía (acompañada de texto/etiquetas accesibles).
- Formularios: React Hook Form + Zod **cuando existan formularios suficientes para justificarlo**; la validación del cliente mejora UX, nunca reemplaza la del backend.
- Accesibilidad de componentes complejos: usar primitivos consolidados como Radix UI, o componentes selectivos de **shadcn/ui** montados sobre ellos, siempre revisando compatibilidad con Vite y versiones instaladas.
- Tablas: tablas HTML semánticas y paginación del servidor; TanStack Table solo si la interacción realmente exige sus capacidades.

**Elección visual inicial recomendada:** Tailwind + componentes accesibles y selectivos de shadcn/ui; **no** combinar a la vez bibliotecas visuales completas (por ejemplo, MUI + shadcn + Ant Design). Esta recomendación es una decisión técnica propuesta para el frontend, **no** un requisito histórico del documento maestro, que admitía MUI o equivalente.

**Prohibido:** plantillas de dashboard completas copiadas sin evaluar licencia, peso, accesibilidad, navegación, seguridad, convenciones y utilidad para una botica. La librería inspira los primitivos; **MediPOS diseña sus propios flujos comerciales**.

### Referencias de patrones, no de marca ni de producto terminado

- [shadcn/ui: Sidebar](https://ui.shadcn.com/docs/components/base/sidebar): referencia para el *app shell* administrativo.
- [shadcn/ui: Data Table](https://ui.shadcn.com/docs/components/base/data-table): patrón de listas con paginación y acciones, sin construir una tabla genérica monstruosa.
- [Radix Primitives: Accessibility](https://www.radix-ui.com/primitives/docs/overview/accessibility): teclado, foco, dialogs, combobox, select y popovers.
- [W3C: WCAG 2.2](https://www.w3.org/TR/WCAG22/): criterio de accesibilidad verificable.

## 4. Estructura modular recomendada

Adaptar la plantilla existente sin reorganizarla por deporte.

```text
frontend/
├── public/
├── src/
│   ├── app/
│   │   ├── providers/          # Router, QueryClient y providers mínimos
│   │   ├── router/             # Configuración central de rutas
│   │   ├── layouts/            # AuthLayout, AppLayout, PosLayout
│   │   └── App.tsx
│   ├── features/
│   │   ├── auth/
│   │   ├── tenants/
│   │   ├── branches/
│   │   ├── dashboard/
│   │   ├── catalog/
│   │   ├── purchasing/
│   │   ├── inventory/
│   │   ├── pos/
│   │   ├── cash/
│   │   ├── transfers/
│   │   ├── commissions/
│   │   ├── reporting/
│   │   ├── employees/
│   │   ├── notifications/
│   │   └── offline-sync/     # Implementar solo cuando se autorice su fase
│   ├── shared/
│   │   ├── api/              # HTTP base, Problem Details, CSRF, tipos comunes
│   │   ├── ui/               # Button, Input, Dialog, DataTableShell, StatusBadge...
│   │   ├── hooks/            # Solo hooks independientes del negocio
│   │   ├── lib/              # Fechas, dinero exacto, utilidades pequeñas y puras
│   │   ├── config/           # Navegación, entornos y constantes de UI
│   │   ├── styles/           # Tokens globales y estilos base
│   │   └── assets/
│   └── main.tsx
├── package.json
└── AGENTS.md
```

**Regla de dependencia:**

```text
app ──> features ──> shared
  └────────────────> shared

shared ─X─> features
feature A ─X─> archivos internos de feature B
```

Si dos módulos comparten una pieza de presentación realmente genérica, llevarla a `shared/ui`; si comparten una operación de negocio, definir un contrato público pequeño y componerla desde `app`, en vez de importar carpetas internas de otra feature.

Cada feature crea **solo las subcarpetas que necesita**, por ejemplo:

```text
features/pos/
├── api/sales.api.ts               # llamadas concretas HTTP; sin JSX
├── components/ProductSearch.tsx   # vista e interacción local
├── components/SaleCart.tsx
├── components/PaymentDialog.tsx
├── hooks/useSaleDraft.ts          # orquestación y queries
├── hooks/useConfirmSale.ts
├── pages/PosPage.tsx              # composición de la pantalla
├── model/sale.view-model.ts       # mapeos y estado exclusivamente UI
└── __tests__/...
```

No crear archivos `helpers.ts`, `utils.ts`, `services.ts`, `components.tsx` de miles de líneas, `GlobalStore` que lo controla todo ni barrels `export *` que generen dependencias circulares. Mantener los tests cerca del módulo o en su directorio de pruebas, con una convención uniforme.

## 5. Límites entre capas

| Capa | Sí hace | No hace |
|---|---|---|
| `app` | routers, providers, layouts, composición de features | cálculos de caja, stock o venta |
| `features/*/pages` | organiza pantalla y estados globales de esa vista | requests directos dispersos y reglas backend |
| `features/*/components` | dibuja y controla interacción accesible | gestionar sesión global y persistir ventas |
| `features/*/hooks` | coordina queries, mutaciones, estados UI | SQL, interpretar roles como permiso definitivo |
| `features/*/api` | endpoints y DTOs tipados del módulo | componentes React y reglas contables |
| `features/*/model` | adaptadores DTO→ViewModel y funciones puras | inventar política comercial/clinica |
| `shared/api` | cliente HTTP común, cookies, CSRF, errores y transporte | endpoints mezclados de todos los módulos |
| `shared/ui` | primitivos visuales neutros y accesibles | `SaleCart`, `InventoryLotPicker` u otras reglas de botica |

**Patrones preferidos:** composición, componentes controlados cuando convenga, adaptación DTO→ViewModel, custom hooks, estados explícitos y *error boundaries* en límites adecuados. Repository pattern en frontend, event bus global, CQRS client-side y factory/DI generalizados **no son requisitos**.

## 6. Sistema de diseño MediPOS (base propuesta)

**Identidad:** profesional, confiable y rápida; evita pantallas sobrecargadas, degradados llamativos, animación decorativa, tarjetas excesivas y estética de videojuego. Optimizar para trabajo repetitivo en mostrador.

### Tokens semánticos

Usar tokens CSS / variables de tema conectadas a Tailwind. Evitar colores hex directos y tamaños arbitrarios dentro de cada componente.

- Fondo general: neutro muy claro.
- Superficies: blanco / neutro; bordes discretos.
- Acción primaria: verde azulado (salud + tecnología), validado con contraste accesible.
- Acción secundaria / enlaces: azul sobrio.
- Éxito, advertencia, error e información: cuatro colores **semánticos**, siempre con texto e icono cuando importa.
- Tipografía: sans-serif legible, preferentemente *system stack* inicialmente. Tamaño base aproximado de 16 px, texto secundario no menor de 14 px cuando afecte la operación.
- Escala de separación uniforme: 4, 8, 12, 16, 24 y 32 px. Radios pequeños/medianos consistentes.
- Área interactiva táctil deseable de al menos 44 × 44 px para acciones frecuentes.
- Sombras discretas; contraste mínimo de 4.5:1 para texto normal, excepto los casos permitidos por WCAG.
- Espaciado, tipografía, bordes y estados de foco centralizados en tokens; no crear cinco estilos diferentes de botón.

**Componentes compartidos mínimos, creados a demanda:** `Button`, `IconButton`, `TextField`, `NumberField`, `Select/Combobox`, `Dialog`, `ConfirmDialog`, `Toast`, `EmptyState`, `ErrorState`, `LoadingState`, `StatusBadge`, `PageHeader`, `FormField`, `Pagination`, `ResponsiveTable`, `MoneyText`, `DateText`.

**Componentes específicos:** `MedicineSearchResult`, `ProductUnitSelector`, `SaleCart`, `PaymentBreakdown`, `CashSessionStatus`, `StockLotRow`, `TransferStatusTimeline`, etc. Mantenerlos dentro de su feature.

**Regla de reutilización:** reutilizar para mantener comportamiento y lenguaje visual consistentes, **no** unificar vistas solo porque ambas tengan un botón y una tabla.

## 7. Modelos de pantallas (referencia UX obligatoria)

### A. Patrón de aplicación administrativa — “App Shell”

```text
┌────────────────────────────────────────────────────────────────────┐
│ MediPOS   [Botica ▼] [Sucursal ▼]    [Estado conexión]  [Usuario] │
├────────────────┬───────────────────────────────────────────────────┤
│ Inicio         │ Título de página   [Acción principal]           │
│ Punto de venta │ Descripción corta y filtros útiles              │
│ Productos      │ ┌───────────────────────────────────────────────┐ │
│ Inventario     │ │ Tabla / formulario / indicadores reales      │ │
│ Compras        │ │ vacío · cargando · error · datos             │ │
│ Caja           │ └───────────────────────────────────────────────┘ │
│ Transferencias │ [Paginación / acciones secundarias]             │
│ Reportes       │                                                   │
│ Personal       │                                                   │
│ Configuración  │                                                   │
└────────────────┴───────────────────────────────────────────────────┘
```

- Navegación corta, agrupada por trabajo, no por entidades del backend.
- Mostrar claramente botica y sucursal actuales en todo momento.
- El propietario ve visión global y administración. Cajero/farmacéutico ven solo navegación operativa autorizada; **el backend siempre reautoriza**.
- Tablas con búsqueda/filtros y paginación reales; no cargar indefinidamente ni ocultar datos cuando no hay permisos.
- Los dashboards solo muestran datos obtenidos del backend; no inventar métricas, gráficos o porcentajes.

### B. Patrón de POS — “Search + Cart + Checkout”

```text
┌────────────────────────────────────────────────────────────────────┐
│ MediPOS POS · Sucursal Centro · Caja abierta · [Conexión]       │
├────────────────────────────────────┬───────────────────────────────┤
│ [Buscar nombre/código/barra...]    │ Venta actual                  │
│                                    │ Producto · unidad · cantidad  │
│ Resultados útiles y rápidos        │ Precio · subtotal            │
│ - nombre + presentación           │                               │
│ - stock de la sede                │ Total: S/ ...                │
│ - lote/vencimiento cuando aplique │ [Cobrar]                      │
│ - disponibilidad / restricciones  │ [Vaciar con confirmación]     │
└────────────────────────────────────┴───────────────────────────────┘
```

- En escritorio el buscador y carrito permanecen visibles; evitar cambiar de página para agregar productos.
- Buscar por nombre, marca/laboratorio, principio activo, código interno y código de barras según el endpoint real.
- Las equivalencias son **coincidencias estructuradas de catálogo**, no consejo médico ni sustitución terapéutica automática.
- Seleccionar unidad/presentación y cantidad con información comprensible; no recalcular FEFO en el navegador.
- Caja y estado de conexión visibles antes de cobrar; el botón de cobro bloqueado con explicación si no corresponde.
- El cobro es un diálogo o paso focalizado con efectivo/Yape/Plin/tarjeta/transferencia y pagos mixtos.
- Confirmar **una sola vez**; mostrar progreso y reconciliar una respuesta perdida consultando venta/ticket persistidos.
- Venta confirmada → ticket interno (impresión opcional). **No llamarlo boleta/factura SUNAT**.
- Priorizar teclado, mouse, lector de códigos como entrada compatible y táctil. Los atajos se documentan y prueban, sin sobrescribir atajos esenciales del navegador.
- En móvil: búsqueda a pantalla completa; carrito como panel accesible; cobro en una vista/paso separado; no comprimir tres columnas hasta volverlas ilegibles.

### C. Patrón de catálogo/inventario

- Lista con acciones claras: agregar, importar, filtrar, abrir detalle.
- Formularios organizados por secciones: identidad del producto, datos farmacéuticos **si es medicamento**, precios, presentaciones, estado.
- Stock por sucursal, lotes, fechas de vencimiento y alertas explícitas, sin mezclar unidades base y presentaciones en una columna ambigua.
- Acciones críticas (ajuste de stock, desactivar producto, anular) requieren contexto, motivo si el backend lo exige y confirmación proporcional al riesgo.

### D. Patrón de compras y transferencias

- Borrador → revisión → confirmación → resultado trazable.
- Líneas editables con producto, unidad, cantidad, costo y lote/vencimiento cuando aplique.
- Diferenciar estados solicitada/aprobada/despachada/recibida/anulada conforme al backend.
- No incrementar stock solo porque se cargó un XML/Excel; exige confirmación real de la compra.

### E. Patrón de propietario y reportes

- Filtros por sede y periodo persistentes durante la navegación de reportes, sin exponer datos entre tenants.
- Resumen con indicadores reales, interpretación sencilla y detalle consultable.
- Exportación con formato y estado claro; no dibujar valores ficticios ni “proyectados” como contabilizados.

### F. Patrón de offline (fase posterior)

- Estado explícito `En línea` / `Sin conexión` / `Pendiente` / `Sincronizando` / `Conflicto` / `Confirmado`.
- “Guardado en este dispositivo” no equivale a “venta registrada en servidor”.
- Mostrar número de pendientes, posibilidad de revisión y pasos de conciliación.
- No permitir cambios de roles, configuración, transferencias ni otras operaciones de riesgo cuando el protocolo offline no las admite.

## 8. UX e interacción: reglas verificables

1. **Tarea primero:** minimizar pasos de venta, búsqueda y cobro; evitar confirmaciones sin valor, excepto ante riesgo real.
2. **Feedback inmediato:** cada acción tiene estado pendiente, éxito confirmado o fallo explicable.
3. **Estados completos:** inicial / cargando / vacío / error / sin acceso / deshabilitado / éxito / conflicto. No renderizar un `[]` como éxito cuando falló la red.
4. **Jerarquía visual:** una acción primaria por contexto; acciones destructivas separadas y etiquetadas.
5. **Lenguaje humano:** errores del backend (`ProblemDetails.code`) se traducen en mensajes comprensibles sin perder la posibilidad de soporte con `traceId`.
6. **Confirmación segura:** deshabilitar doble clic durante mutaciones críticas, pero **no asumir** que el bloqueo visual evita reintentos de red.
7. **Accesibilidad:** HTML semántico, labels visibles, gestión de foco tras modales/errores, navegación por teclado, estados `aria-*`, mensajes `aria-live` para cambios relevantes, no depender solo del color y respetar `prefers-reduced-motion`.
8. **Responsive real:** diseñar y comprobar al menos ancho móvil estrecho (~360 px), tablet (~768 px) y escritorio (~1280 px). Estos son puntos de prueba, **no** breakpoints rígidos de negocio.
9. **Rendimiento percibido:** evitar bloquear la búsqueda al teclear; debounce/cancelación justificados, skeletons donde ayuden y paginación del backend. No optimización prematura ni virtualización sin evidencia.
10. **Localización:** moneda PEN y textos `es-PE`; separar fechas UTC del servidor y presentación local `America/Lima`; el vencimiento `DateOnly` no se convierte ingenuamente mediante zona horaria.
11. **No diseño engañoso:** acciones sin implementación real se ocultan, deshabilitan con contexto o etiquetan como demostración en modo desarrollo. No hay botones decorativos que prometan funciones inexistentes.
12. **Interacción comprobable:** diseñar un flujo extremo a extremo antes de decorar páginas secundarias.

## 9. Integración backend: contrato y aislamiento

- Una sola función/base HTTP compartida. Cada feature tiene métodos de API concretos; sus componentes no llaman `fetch` directamente.
- No inventar rutas ni cambiar DTOs para que “encajen”. OpenAPI de Development + código backend actual son el contrato.
- **Autenticación:** sesión cookie HttpOnly gestionada por el navegador; `credentials: 'include'` cuando corresponda; obtener y renovar CSRF usando `/auth/csrf`; mutaciones con `X-CSRF-TOKEN`.
- **Origen:** frontend y backend se integran bajo el origen HTTPS permitido por el servidor. **No** desactivar protecciones de HTTPS/Host/Origin/CSRF o confiar en un proxy dev HTTP para evadirlas.
- **Identidad:** actor y permisos vienen del servidor. Tenant/sucursal en ruta son selección de recurso, nunca autorización. No crear headers propios `ActorId`, `Role` o `UserId`.
- **Query keys:** incluir `tenantId`, `branchId` y filtros pertinentes; al cambiar cuenta/tenant/sucursal o cerrar sesión, **cancelar/invalidad/limpiar** la caché que podría mostrar datos del contexto anterior, y limpiar borradores dependientes del contexto.
- **Permisos UI:** las rutas y menú orientan la experiencia según el acceso actual, pero **no** equivalen a autorización. Manejar 401/403/409 y estados de licencia/sesión de forma visible.
- **Errores:** procesar `ProblemDetails` centralmente; no hacer `catch { return [] }`, no filtrar trazas/secrets, no ocultar resultados inciertos.
- **Concurrencia:** conservar `expectedVersion` de edición/confirmación/anulación; en resultado ambiguo consultar estado real por UUID de venta antes de reintentar; nunca crear una venta nueva a ciegas.
- **Tipos:** tipos del transporte separados de view models. Preferir generación tipada desde OpenAPI cuando sea viable y comprobada; no importar tipos C# manualmente convertidos por intuición.
- **Moneda y precisión:** `decimal` en backend viaja en JSON como número. JavaScript `number` usa punto flotante y **no garantiza** precisión monetaria exacta. No usar sumas en `number` para pagos/cantidad/precio. Para operaciones locales provisionales usar representación decimal exacta (por ejemplo, `decimal.js` si se justifica); si se serializan números JSON, validar una estrategia que preserve el literal decimal sin pasarlo por `Number`. El backend confirma los totales definitivos. El formato `S/` es solo presentación.
- **Fechas:** timestamps con zona/UTC del contrato; representar el tiempo visible en Perú sin cambiar el instante. Fechas de vencimiento son fechas de calendario, no instantes UTC convertibles.
- **Datos sensibles:** no guardar sesiones, tokens, permisos, costos de otras sedes, respuestas privadas o datos clínicos innecesarios en localStorage, logs o herramientas de analítica.

### Pruebas contractuales clave

- Un cajero del tenant A jamás ve el caché del tenant B después de cambiar de sesión.
- Un cajero no recibe vistas gerenciales ni costos fuera de la política vigente.
- Stock insuficiente, 409 de versión y caída de red no generan estados UI falsamente confirmados.
- Dos clics sobre cobrar no crean dos intents en la interfaz, y una respuesta perdida se reconcilia con la venta existente.
- La impresión es opcional; el resultado siempre se etiqueta como ticket interno.

## 10. Estado del cliente y datos

- TanStack Query para el **estado del servidor**: productos, permisos, caja, ventas, stock, reportes, caché y reintentos controlados.
- `useState`/`useReducer` para el **estado transitorio local**: diálogo abierto, filtros editables, campos, vista del carrito antes de persistencia.
- React Context solo para contexto transversal pequeño: sesión validada, tenant/sucursal seleccionados y tema, sin recrear una segunda caché del servidor.
- No introducir Redux/Zustand o un event bus global si no existe una necesidad demostrada.
- No persistir carrito/sesión sin una estrategia explícita de integridad e identidad de dispositivo.
- Las mutaciones invalidan las consultas relevantes de la **misma** botica/sucursal, no toda la aplicación sin razón.

## 11. Calidad de implementación y pruebas

**Por cada feature o pantalla:**

- [ ] Revisé componentes/hooks existentes y no dupliqué capacidades.
- [ ] La feature tiene límite claro y depende solo de contratos públicos.
- [ ] La pantalla usa un patrón visual de la sección 7 y tokens de la sección 6.
- [ ] Se prueban al menos los estados relevantes: carga, vacío, error, permiso y éxito.
- [ ] Las operaciones peligrosas contemplan conflicto, doble envío y resultado incierto.
- [ ] La autorización sigue siendo del backend; no se falsea usuario, tenant o sede.
- [ ] Desktop, móvil y teclado son usables en el flujo principal.
- [ ] Componentes, hooks y mapeos puros relevantes tienen tests.
- [ ] No hay código muerto, placeholders engañosos, `any` injustificados ni secretos.
- [ ] Se actualizan docs solo si cambió materialmente una convención o decisión.

**Gates frontend**, configurados en `package.json` antes de declararlos obligatorios:

```sh
npm run lint
npm run build
npm run test -- --run
```

Añadir formateo comprobable cuando se incorpore Prettier u otra herramienta. Tests preferidos: Vitest + React Testing Library para comportamiento/UX básico; Playwright en flujos E2E importantes cuando exista un entorno reproducible autorizado. Tests de captura visual cuando estabilicemos diseño, no como sustituto de probar acciones. No reportar comandos que no se ejecutaron.

**Revisión de UX con evidencia:** un cajero completa búsqueda → unidad → cantidad → cobro mixto → ticket sin perder contexto; el dueño consulta cifras por sucursal; cambiar de tenant no deja datos previos; móvil no oculta los botones de operación. Estas pruebas se validarán con usuarios piloto, no se consideran aprobadas por una captura bonita.

## 12. Fases de entrega (no adelantar alcance)

- **F1:** configuración, sistema visual base, auth, selección de botica/sucursal, app shell, dashboard inicial y maqueta de POS claramente marcada como no funcional si aún no se conectó.
- **F2:** POS conectado de extremo a extremo: caja, búsqueda, carrito, unidades, pago mixto, confirmación persistida y ticket.
- **F3:** catálogo, compras, inventario, lotes y ajustes controlados.
- **F4:** propietario: reportes, empleados, transferencias, comisiones y configuración.
- **F5:** PWA offline limitada, IndexedDB/outbox, sincronización idempotente y conflictos, solo sobre los contratos aprobados.
- **Piloto:** seguridad y UX reales con Google, TLS y PostgreSQL validados; no vender ni tratar como listo para producción antes de los gates externos y de la purga controlada A18 que requiere el MVP.

Las fases son un **orden de trabajo frontend sugerido**, no un reemplazo del roadmap oficial ni licencia para saltar aceptación. Repriorizar solo ante petición explícita.

## 13. Cómo debe trabajar y reportar Codex

**Antes:** indicar pantalla/feature objetivo, componentes existentes a reutilizar, endpoints reales y riesgos. Si hay un bloqueo que altera reglas de negocio, consultar en vez de inventarlo.

**Durante:** implementar el menor cambio vertical con UI, integración y tests; evitar rediseños transversales sin autorización; no tocar backend ni migraciones para conveniencia del frontend.

**Al entregar, resumir:**

1. Pantallas y flujos implementados; qué es funcional, qué es solo maqueta y qué usa fixtures de desarrollo.
2. Componentes compartidos **reutilizados** y los que se añadieron, con motivo.
3. Endpoints realmente conectados y permisos esperados.
4. Validaciones ejecutadas, resultado exacto y pruebas faltantes.
5. Evidencia/observaciones UX de escritorio y móvil cuando se hayan comprobado.
6. Riesgos/bloqueadores concretos y siguiente incremento pequeño.

**Restricciones de proyecto vigentes:** no ejecutar `git` ni `gh`; no crear commits/PR/ramas; no cambiar GitHub; no instalar/iniciar Docker ni PostgreSQL local; no desactivar verificaciones del backend; no copiar secretos; no crear un backend Node adicional; no hacer llamadas reales a proveedores por cuenta del usuario. Los cambios en el árbol de trabajo local son para revisión y commit posterior por el usuario.

---

**Regla principal MediPOS:** cada decisión debe mejorar una de estas cuatro cosas: **venta rápida, stock confiable, vencimientos controlados o visibilidad útil para el propietario**. La arquitectura y el diseño sirven al trabajo diario de la botica, no al revés.
