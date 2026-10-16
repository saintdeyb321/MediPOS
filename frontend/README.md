# MediPOS · Frontend F1 / F1.1 / F2.0 / F2.1

**Bloque 03 — Productos:** pantalla Owner conectada a listado paginado, detalle/unidades/medicina, alta local, asociación global, precios y estado. Guardado de presentaciones bloqueado: el PUT actual recrea sus IDs y no permite inicialización condicional atómica. Formularios de definición disponibles sin enviar cambios inseguros. Estado IN_PROGRESS por aceptación real y ese límite de contrato; ver el plan maestro para el único recorrido de aceptación y el handoff de compras/inventario.

Plan operativo único, estados por bloque y aceptación integral: [Plan maestro de integración](../docs/FRONTEND_INTEGRATION_PLAN.md). El bloque 01 reutiliza el carrito/caja/borrador existente; el bloque 02 conecta pagos mixtos exactos, confirmación explícita con versión, reconciliación sin reenvíos y ticket interno con impresión opcional. Ambos esperan aceptación comercial real conjunta en Chrome LAB; anulación/cierre quedan en 02b.

**Flujo operativo actual:** abrir caja → buscar/agregar → **Cobrar** (crea/guarda internamente) → medios de pago → confirmar explícitamente → **VENTA REALIZADA** verificada → imprimir o seguir vendiendo con la misma caja. La recuperación por identificador es excepcional; no hay botones normales de administración de borradores. El formato quita ceros sobrantes y conserva todos los decimales significativos. 02b conecta anulación desde el ticket y cierre/arqueo desde Caja con revisión y conciliación; pendientes de aceptación real. Las secciones F2.1 inferiores conservan evidencia histórica de los incrementos técnicos anteriores.

Fundación React + TypeScript + Vite existente, con sistema visual y arquitectura modular F1.1. F2.0 LAB tiene Google real y onboarding funcional confirmados por el operador en Chrome: Owner, botica y dos sucursales, con Superadmin revocado. F2.1 conecta búsqueda, carrito, caja, borrador, pagos/confirmación y ticket al backend existente; el operador aún debe aceptar el circuito completo. La evidencia F1/F1.1 de abajo es histórica.

## Ejecutar y validar

Node usado localmente: **24.18.1**, npm **11.16.0**. Las versiones resueltas están en `package-lock.json`.

```powershell
cd frontend
npm ci
npm run build
cd ../backend
dotnet run --project src/MediPOS.Api --launch-profile https-local
```

Abrir **https://localhost:7243/**. Kestrel sirve React compilado y API en un mismo origen con el certificado localhost confiable existente. No hay segundo backend ni proxy Vite. Recompilar y refrescar para ver cambios. `npm run dev` sigue siendo únicamente la herramienta de edición HTTP; no permite el acceso real ni habilita demostración.

```powershell
npm run build
npm run lint
npm run test
```

`build` ejecuta TypeScript estricto y genera `dist/`. Las pruebas usan Vitest, Testing Library y jsdom; no necesitan PostgreSQL. La implementación de Tailwind usa su [plugin oficial para Vite](https://tailwindcss.com/docs/installation/using-vite), junto a React Router declarativo, TanStack Query y Lucide React. No se incorporan frameworks de servidor ni un generador de cliente.

## Organización

```text
src/
  app/
    App.tsx        composición del runtime HTTP real
    providers/     QueryClient y expiración de sesión
    router/        rutas, composición del panel y secciones pendientes
    layouts/       selección, navegación, cabecera y menú móvil
  features/
    access/        contrato público de sesión/acceso, DTOs, adaptador y hooks F1
    auth/          ingreso Google, sesión y cierre
    tenants/       selección de botica y coordinación de consultas
    branches/      selección de sucursal y coordinación de consultas
    dashboard/     panel contextual sin estadísticas inventadas
    pos/           encabezado, búsqueda/resultados, carrito y resumen visual
  shared/
    api/           transporte HTTP, CSRF y ApiError
    ui/            primitivos accesibles y sus estilos
    styles/        tokens semánticos y base
  development/     fixtures históricos usados únicamente por tests existentes
  types/           identificadores de esos tests
  test/            preparación de Testing Library
  index.css        únicamente imports de Tailwind y estilos comunes
  main.tsx         montaje de React
 tests/            verificaciones Node de arquitectura y contraste
```

`app` compone las pantallas; las features consumen el contrato público `features/access/index.ts` para el contexto transversal de sesión/botica/sucursal. Los DTOs, mensajes comerciales y el adaptador F1 permanecen en esa feature, no en `shared`. Ninguna feature importa `app` ni archivos internos de otra; un test de arquitectura comprueba estas fronteras. Los hooks coordinan consultas/mutaciones; los componentes no realizan HTTP directo. Las claves, revalidación y cancelación de F1 se conservan.

## Sistema visual F1.1

- `shared/styles/tokens.css`: superficies, texto principal/secundario, verde azulado primario, azul informativo, éxito/advertencia/error, foco y bordes. Integra tokens semánticos con Tailwind mediante `@theme inline`.
- Tipografía de sistema: base 16 px, secundario operativo 14 px, títulos con escala responsive; números tabulares en el resumen. Separación de 4/8/12/16/24/32 px, radios y sombras discretas, controles frecuentes de al menos 44 px de alto.
- `shared/ui/ui.css`: estados hover, focus, disabled, loading y error; las superficies, layouts y features consumen variables. El CSS de pantallas está junto a su feature/layout; `index.css` deja de contener el monolito.
- Se evolucionan Brand, BackLink, PageHeader, LoadingState y ErrorState. Se añaden y utilizan Button/IconButton, Badge, Panel, Notice, EmptyState, FormField, TextField/SearchField, Steps y Dialog. ApiErrorState adapta errores sanitizados a presentación; `shared/ui` no conoce licencias ni tenants.
- Dialog conserva el elemento nativo, fondo inerte, Escape y retorno al disparador, y cierra el ciclo de Tab dentro del menú. No se añade una librería visual. No se crean ConfirmDialog, Skeleton ni tabla genérica sin un caso real: el POS aún presenta un estado sin resultados, no filas sintéticas.
- Login equilibrado; selección con progreso y acciones explícitas; panel centrado en abrir POS, sucursal y acceso; encabezado con botica/sucursal; módulos pendientes identificados. El POS separa ProductSearchPanel, SaleCartPanel y CheckoutSummary, apila en móvil y ofrece un enlace de teclado al carrito. Búsqueda y cobro siguen deshabilitados.

El contraste de los pares de texto usados se comprueba con la fórmula de luminancia de [WCAG 2.2, contraste mínimo](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html): mínimo medido **5.47:1**, por encima del umbral 4.5:1. Foco/bordes de controles se prueban a 3:1. Esto comprueba esos pares, no certifica toda la aplicación: lector de pantalla, dispositivos físicos y aceptación con usuarios piloto continúan pendientes.


## Pantallas y alcance

- Ingreso: sin sesión, verificación, inicio de Google, error de conexión y sesión expirada.
- Boticas: listado paginado por cursor, rol descriptivo, selección, ausencia de membresías y reintento ante error.
- Sucursales: solo las devueltas por el servidor; ausencia de asignaciones, restricciones y validación operacional antes de entrar.
- Layout: botica/sucursal, cuenta sin nombre/correo inventados, cambio de espacio, cierre de sesión, sidebar de escritorio y menú móvil con `dialog` nativo, Escape y foco modal. Atajo `Alt+V` abre la vista inicial del POS fuera de campos editables.
- Panel: contexto actual y accesos a ventas, productos, inventario, compras, caja, reportes, empleados y configuración según el rol operacional vigente. No consulta ni fabrica indicadores comerciales. Las secciones futuras están identificadas como próximas; configuración solo muestra el contexto actual en lectura.
- POS: buscador y resultados sin conectar, carrito vacío, total de carrito vacío y cobro deshabilitado. No crea borradores ni confirma ventas, pagos o cambios de stock.

## Contratos conectados en código

Todos los paths son relativos al mismo origen. Las referencias son `backend/src/MediPOS.Api/Authentication/GoogleAuthenticationRegistration.cs`, `Tenant/TenantEndpoints.cs`, `MyTenantDirectory.cs`, `BranchDetails.cs` y `EvaluateOperationalAccess.cs` del backend B8.7.

| Método y ruta | Uso F1 |
|---|---|
| `GET /auth/me` | UUID de sesión; 401 significa ausencia o expiración |
| `GET /auth/csrf` | Token antiforgery transitorio, nuevo antes de cada mutación |
| `POST /auth/login` | Navegación completa mediante formulario nativo con `__RequestVerificationToken`, nombre predeterminado de ASP.NET; no fetch del challenge Google |
| `POST /auth/logout` | Cierre real con `X-CSRF-TOKEN`; sin reintento automático |
| `GET /api/me/tenants?limit=100&afterTenantId=…` | Boticas propias; el cursor se usa solo al cargar más |
| `GET /api/tenants/{tenantId}/branches?limit=100&offset=0` | Sucursales autorizadas; el límite supera el máximo de cinco sucursales del producto |
| `GET /api/tenants/{tenantId}/access?branchId=…` | Acceso operacional actual antes de mostrar el espacio |

El cableado está implementado y probado con transporte simulado; **no se ha ejecutado una autenticación Google real ni una conexión con el backend desplegado**. Los demás endpoints comerciales quedan para sus módulos posteriores. No hay llamadas a PlatformApi desde F1.

## Seguridad y estado remoto

El cliente usa `fetch`, `credentials: same-origin`, `cache: no-store`, rechazo de redirecciones API, `AbortSignal` y errores Problem Details. Solo admite rutas de API relativas y HTTPS. No establece `Origin` ni `Sec-Fetch-Site`; los administra el navegador. Nunca muestra `detail`, payloads, títulos arbitrarios del servidor o errores técnicos sin sanitizar; conserva códigos estables y referencias de soporte válidas.

Cookies, tokens, credenciales, roles y selecciones no se guardan en localStorage, sessionStorage ni IndexedDB. El token CSRF solo vive en memoria durante la solicitud/formulario. La caché Query es de memoria. Un 401 con sesión activa cancela solicitudes, retira consultas protegidas, invalida la sesión y lleva al ingreso; el cierre confirmado vacía la caché. Las claves privadas incluyen botica y sucursal. Cambiar botica cancela y retira las consultas del espacio anterior.

El rol del listado de boticas no autoriza la navegación. Se usa el contexto devuelto por `/access`, contrastado con el usuario y la selección. Se vuelve a consultar por ruta, montaje, foco y cada 60 segundos; mientras se comprueba, no se presenta contenido operacional anterior. Una denegación o error bloquea la vista. Las opciones reflejan las reglas actuales: Owner administra; QF ve ventas/inventario/compras/caja; Cashier ve ventas/caja. Esto solo controla presentación; cada operación futura debe seguir sometida a la autorización del servidor. No se agregan permisos permanentes en el cliente ni se interpreta Superadmin como acceso comercial.

## Demostración retirada en F2.0

La entrada real ya no importa fixtures ni admite el selector de escenarios, tampoco en Development. Se retiraron banner y estilos de demostración. Los fixtures históricos se conservan únicamente para las pruebas existentes de componentes; no se usaron para validar la integración. No hay fallback ante un error de red ni identidad simulada.

## Despliegue HTTPS externo pendiente (referencia F1)

1. Servir `dist/` desde la raíz del origen HTTPS público autorizado para MediPOS, con certificado confiable. Puede hacerlo el hosting/reverse proxy aprobado; F1 no configura ese entorno.
2. Enrutar `/auth/*`, `/api/*`, `/platform/*`, `/health*` y el OpenAPI de desarrollo al backend, sin reescritura SPA. El resto de rutas de navegación del frontend puede resolver a `index.html`. No devolver HTML del frontend cuando una API falla.
3. Conservar Host público, TLS, controles de proxy confiable, antiforgery y cookies `__Host-*`. No ampliar CORS, confiar en cualquier proxy, omitir Origin ni desactivar HTTPS. El login regresa a `/`, donde el frontend verifica de nuevo `/auth/me` y obtiene un token CSRF nuevo cuando se necesite.
4. Configurar mediante el procedimiento existente Google, su callback exacto `/auth/google/callback`, `TenantApi:Enabled`, Data Protection y sesión segura del servidor (el store actual es de memoria; reiniciar requiere login). El backend decide acceso, licencia, membresía, sucursal y horario. F1 no incorpora secretos en variables `VITE_*`.
5. Validar externamente Google → sesión → botica → sucursal → acceso, renovación/expiración de sesión, rechazo de CSRF/orígenes y logout. En localhost HTTP solo se verifica la interfaz; un proxy de Vite no sustituye estas comprobaciones.

## Evidencia local y pendientes

Gate local F1.1 ejecutado el **2026-10-15**:

| Comando / revisión | Resultado |
|---|---|
| `npm run build` | Correcto: TypeScript estricto y Vite 8.3.4; JS 344.25 kB / gzip 107.31 kB; CSS 27.70 kB / gzip 5.89 kB |
| `npm run lint` | Correcto, sin errores ni advertencias |
| `npm run test` | **68 pruebas aprobadas, 0 fallidas**, cinco archivos; 12.42 s en la verificación final |
| `npm run dev` | Arranque confirmado en loopback, puerto 5173 |
| Compilación de producción | Sin marcadores de UUID/datos/selector/factory de fixtures; sin chunk de fixtures |
| Navegador | Chrome instalado en modo headless, Playwright del runtime disponible; **46 comprobaciones** a 360, 768, 1280 y 1440 px; ninguna dependencia nueva del proyecto |
| UI medida | Login, boticas, sucursales, panel, POS, menú móvil y ocho escenarios adicionales: sin desbordamiento horizontal, texto visible mínimo 14 px y sin errores JavaScript de página |
| Teclado en navegador | Apertura modal con foco en cerrar, ciclo Tab/Shift+Tab, Escape y retorno al disparador, navegación POS desde menú, Alt+V en escritorio y salto al carrito móvil |

La verificación final revisó 56 módulos sin imports relativos rotos, ciclos ni componentes exportados duplicados. Se retiraron únicamente cuatro recursos de plantilla sin referencias: `src/assets/react.svg`, `src/assets/vite.svg`, `src/assets/hero.png` y `public/icons.svg`. El favicon MediPOS se conserva. No se cambiaron componentes ni pruebas durante este cierre.

El primer run completo agotó la espera de la pantalla de ingreso tras logout. El caso aislado y la repetición completa pasaron sin modificar aserciones ni tiempos de espera; el logout también pasó en Chrome en las cuatro anchuras. No se reprodujo un defecto funcional; se conserva esta incidencia para seguimiento si vuelve a ocurrir.

Se conservan las 33 pruebas F1 de transporte, CSRF, cookies, cancelación, sesión, DTOs, roles, horarios, licencias y navegación. Solo se actualizan las consultas de UI afectadas por el título compacto del POS y el tipo semántico `search` del input; las aserciones de bloqueo siguen presentes. Se añaden 35 casos: primitivas accesibles/estados, selección con teclado, carga antes de vacío, descripción de módulos pendientes, fronteras de imports y contraste. Las pruebas Node de arquitectura/colores se tipan con `tsconfig.node.json`; el código del navegador conserva sus tipos DOM separados.

Las capturas locales de login, selección, panel, POS, menú y escenarios se obtuvieron durante la revisión; no forman parte del bundle. El navegador integrado no pudo inicializar su runtime, por lo que se usó Chrome temporal sin sesión ni proveedores reales. Las comprobaciones de navegador emplean exclusivamente los fixtures explícitos de desarrollo.

Google/OIDC real, backend desplegado, HTTPS del hosting, RLS y PostgreSQL no fueron ejecutados. No se modificaron backend, migraciones, contratos HTTP ni configuración de seguridad; tampoco se usó Git/GitHub. **F1.1 está terminado dentro de su alcance local y listo para cierre en auditoría.** La base permite comenzar F2 tras esa revisión y autorización; F2 no se inició. No se declara producción lista.

F2: conectar búsqueda y presentaciones, caja abierta, borrador y versiones optimistas, cantidades/precios calculados por el servidor, pagos exactos/mixtos, confirmación atómica, reconciliación de respuestas ambiguas y ticket. No implementar esos efectos hasta completar sus validaciones y pruebas reales. PWA/offline y el resto de módulos siguen fuera de F1/F1.1. Detenerse para auditoría.

## F2.0 — integración local real (2026-10-15)

**Registro histórico de DEV; el cierre funcional LAB se documenta al final.** Kestrel sirve el build en https://localhost:7243 con TLS confiable y mismo origen, sin CORS/proxy alternativo. Sin demostración ni datos comerciales inventados. PostgreSQL 18.4 tiene **34/34 migraciones** oficiales y la API usa `medipos_runtime`. Se reprodujo el 42601 del IF/CASE de `official_refresh_history_guard` y se corrigió únicamente con paréntesis en el SQL de la migración entonces pendiente; no se modificó ninguna ya aplicada. Las huellas de las 49 tablas previas se conservaron; GRANT mínimos reaplicados, 53 tablas FORCE RLS y RLS activo con el runtime sin privilegios elevados. Readiness no sustituye una prueba entre dos tenants. Ver las regresiones SQL y los pasos privados exactos de Google/PFX/primer Owner en `../backend/README.md`, sección F2.0.

El mismo `shared/api/http.ts` conserva cookies same-origin, CSRF nuevo por mutación, redirecciones API rechazadas, Problem Details sanitizado y cancelación. Añade límite de 15 segundos (incluye CSRF y consumo del body), validación del path normalizado y serialización estricta. No hay reintentos automáticos. Red/timeout/cancelación tras enviar una mutación, 5xx o respuesta de éxito ilegible se distinguen como resultado incierto: nunca significan rollback ni habilitan otro envío a ciegas. El estado se debe consultar por el mismo UUID persistido; si ni esa consulta responde, conservar el resultado pendiente de reconciliación. Ninguna confirmación/cobro está implementada en este incremento.

Clientes **de lectura** preparados y exportados por feature:

| Feature | Contrato backend inspeccionado | Rutas reales bajo `/api/tenants/{tenantId}/branches/{branchId}` |
|---|---|---|
| `catalog/api.ts` | `SearchProductsResult`, `PosProductMatch`, `ProductUnitDetails` | `GET /products/search?q=…&limit=…` |
| `inventory/api.ts` | `ProductStockDetails` | `GET /inventory/stock?productId=…&offset=…&limit=…` |
| `cash/api.ts` | `CurrentCashSession`, `OpenCashSessionDetails`, `CashSessionStatusDetails` | `GET /cash-sessions/current`, `GET /cash-sessions/{id}` |
| `pos/sales.api.ts` | `SaleDraftDetails` y sus líneas | `GET /sales/{saleId}` para leer/reconciliar estado persistido |
| `pos/tickets.api.ts` | `InternalTicket` y display actual | `GET /sales/{saleId}/ticket` |

Son DTOs TypeScript derivados del código actual citado en cada archivo, con validadores de respuesta, UUIDs, enums y parámetros acotados; no un cliente generado ni un contrato OpenAPI descargado de una sesión real. Caja/venta/ticket contrastan la selección y el identificador de respuesta. No se añaden escrituras ni pantallas. `scopedKey` usa `['tenant', tenantId, branchId, resource, …]`; entrar en selección de sucursal cancela/retira el espacio anterior, y un cambio directo de botica/sucursal retira las consultas de otros espacios. Las features deben pasar el AbortSignal de Query al cliente compartido.

Los clientes comerciales activan lectura numérica exacta: `JsonNumber.literal` conserva el token JSON sin convertir decimales en `Number`, y bloquea coerción aritmética implícita. Los enteros de versión se convierten solo tras comprobar el rango uint. La escritura preparada usa tokens numéricos `JSON.rawJSON`, nunca strings de dinero ni floats; rechaza valores indefinidos/no finitos/cíclicos y números JS no enteros seguros. Requiere [JSON.parse con contexto del token original](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/JSON/parse) y `JSON.rawJSON` nativos; si faltan, rechaza la operación sin pérdida silenciosa. No hace cálculos de pagos ni conversiones de dosis/unidades.

Validado: build, lint y **77 tests** (68 previos intactos, ocho de JSON exacto y una frontera contra fixtures en App). Chrome real a 360/768/1280/1440 abrió el login sin desbordamiento, demo ni errores JS y llamó al backend por HTTPS sin omitir certificados. `/auth/me` y `/auth/csrf` devolvieron 404 Problem Details porque Google sigue deshabilitado; `/health/ready` devolvió 200 Healthy. La primera ejecución encontró una regresión en el orden de validación de HTTPS; se corrigió el cliente sin cambiar la aserción. Los tests unitarios de transporte existentes no cuentan como evidencia de integración real.

Comprobación final sobre el esquema completo: Chrome ejecutó el cliente HTTP actual (transpilado desde su fuente, sin fetch simulado) contra el backend HTTPS. Pasaron Problem Details 404 real, cancelación nativa sin enviar la solicitud abortada, rechazo previo de la mutación cuando falla el CSRF real deshabilitado (ningún POST enviado), precisión decimal pura en Chrome y readiness 200. Esto no certifica una sesión o validación CSRF positivas: Google sigue deshabilitado. Las rutas SPA devuelven HTML; API/platform/POST desconocido/asset ausente conservan 404 sin HTML; Host ajeno devuelve 400. Build/lint y 77 tests frontend, restore/build sin advertencias, 2432 unitarios, 8 de arquitectura, format y modelo EF offline pasan. Las seis regresiones SQL reales no sustituyen la suite Testcontainers, que no fue ejecutada.

Actualización de autenticación local: Google/TenantApi habilitados tras configurar PFX RSA externo, ACL y claves cifradas persistentes. CSRF real **200**, rechazo de CSRF inválido **400**, Origin ajeno **403** y acceso anónimo **401** comprobados. El usuario completó el callback real; el directorio falló por el JOIN a `tenants.id` (42703), corregido a `tenants.tenant_id` sin tocar RLS, permisos ni migraciones. La consulta corregida pasó con el runtime real. Las comprobaciones 404 anteriores corresponden a la fase con Google deshabilitado. El usuario repitió el acceso Google tras el reinicio y confirmó «Aún no tienes boticas asignadas»; los logs muestran 200 sin nuevos errores PostgreSQL. **PASS funcional de autenticación local:** el usuario verificó en Network el JSON real de React GET /auth/me, cookie Secure/HttpOnly/SameSite=Lax y logout seguido de 401 auth.session_required. El 403 de navegación externa es compatible con el rechazo intencional de Origin ajeno o Fetch Metadata cross-site; abrir otra pestaña no se prohíbe por sí mismo, y no se capturaron los headers originales para atribuirles un valor exacto. Cinco sondas HTTPS reales confirmaron el contrato sin sesión. **Pendiente:** expiración natural de 30 minutos; onboarding autorizado, permisos por licencia/rol/sucursal/horario, lecturas comerciales y aislamiento entre tenants. Callback: **https://localhost:7243/auth/google/callback**, PublicOrigin idéntico y HttpsMode Direct. No se inició F2.1 ni se declara producción lista.


## F2.0 LAB — onboarding funcional confirmado

El operador confirmó login Google nuevo como Owner en https://localhost:7343/, MediPOS LAB en el directorio, LAB Centro/LAB Norte y acceso al espacio de LAB Centro sin errores. El cierre funcional de ese onboarding queda PASS. Superadmin sigue revocado, la aprobación cerrada y la ruta `/platform/tenants` y los archivos de alta temporal devuelven 404. `backend/obj` no es la raíz estática ni parte del producto publicado. Continúan pendientes la suite PostgreSQL externa, aislamiento entre dos tenants y las demás aceptaciones; no se declara producción lista.

## F2.1 — carrito, caja y borrador persistido (estado actual)

El piloto autorizado quedó persistido una sola vez: producto local PANADOL Antigripal NF, Tableta/caja de 10, precio LAB S/ 1.00 y compra ficticia de 20 tabletas a S/ 0.50 en LAB Centro. PostgreSQL confirma lote/fecha y stock 20; el operador confirmó la búsqueda real por paracetamol. La categoría se creó con autoridad temporal ya revocada; ambas páginas temporales fueron retiradas y `/platform/tenants` devuelve 404.

El POS normal permite agregar varias líneas, elegir presentación/cantidad, editar y quitar sin borrar el carrito al buscar. BigInt conserva conversiones e importes exactos; los subtotales locales se etiquetan como estimados (redondeo ToEven a cuatro decimales). Se controla stock acumulado por producto y límites del contrato; la validación definitiva es del servidor. El carrito no se persiste en el navegador y se separa por usuario/membresía/botica/sucursal. La revalidación periódica de acceso conserva el componente mientras consulta; una denegación sí retira el espacio operacional.

`cashApi` consulta la caja propia y permite abrirla solo al enviar el formulario con un monto explícito; siempre vuelve a consultar el estado real. `salesApi` crea un borrador solo por botón con caja válida, guarda mediante PUT con `expectedVersion` y recupera el mismo identificador de la URL usando GET estado + GET draft. El panel distingue carrito local/cambios pendientes/total persistido del servidor. Una respuesta perdida de PUT se reconcilia por lecturas; diferencias requieren revisión explícita. No hay reintentos automáticos de escrituras ni endpoints de confirmación/pagos/tickets en este recorrido.

Limitación del contrato existente: POST de creación no admite identificador anticipado ni clave idempotente. Si se pierde esa respuesta, la URL conserva un marcador de resultado incierto y bloquea nuevas creaciones, incluso al recargar; se necesita reconciliación del operador y recuperar el identificador antes de continuar. No se inventa otra venta. Los marcadores no contienen cookies, tokens, permisos ni respuestas privadas. Al recargar se recuperan las líneas persistidas, no cambios locales pendientes. Las presentaciones/precios recuperados son snapshots; el buscador consulta su disponibilidad actual.

Validación: build aislado LAB, lint y **132 pruebas frontend** PASS (incluye 26 nuevas regresiones de carrito/caja/borrador, contexto, conflictos, pérdidas de respuesta y revalidación de acceso). Los dobles de transporte son evidencia de componentes, no de PostgreSQL/OIDC. Publicado solo al hosting privado LAB sin reiniciar el host; SPA/JS/CSS/readiness HTTPS 200, plataforma 404 y `dist/` de DEV intacto. Comprobación PostgreSQL real read-only: piloto íntegro, 20 unidades disponibles, cero cajas/borradores/ventas confirmadas/pagos al verificar, 34 migraciones, 53 FORCE RLS; runtime restringido y consulta sin tenant sin filas privadas. El nuevo recorrido autenticado de abrir caja/guardar/recargar requiere los clics del operador en Chrome; no se ejecutó ningún POST comercial de prueba desde herramientas. Suite externa PostgreSQL y aceptación visual responsive siguen pendientes. F2.1 permanece en curso; siguiente incremento: pagos, confirmación y ticket interno.

**Corrección durante la prueba Chrome:** el operador observó 429 de búsqueda al recuperar foco. El cliente podía lanzar cinco lecturas TenantApi a la vez, frente al límite existente de cuatro por usuario (y dos para autenticación). El único `HttpClient` ahora admite como máximo dos solicitudes HTTP simultáneas, con cola cancelable dentro del timeout y liberación en finally. Obtiene CSRF antes de admitir la mutación para evitar bloqueo por petición anidada. No sube límites del servidor ni reintenta 429/escrituras. Cuatro regresiones de admisión/cancelación/CSRF y el resto del gate frontend pasan; nuevo JS publicado solo en LAB, sin reinicio. La repetición autenticada en Chrome sigue pendiente; el 429 anterior no se registra como PASS.

## F2.1 — primer incremento: búsqueda real del POS (histórico; reemplazado por el carrito anterior)

`ProductSearchPanel` consume `catalogApi` y el único `HttpClient` existente: `GET /api/tenants/{tenantId}/branches/{branchId}/products/search?q=…&limit=20`. Nombre, principio activo, laboratorio, códigos y tolerancia a errores los resuelve el backend PostgreSQL. La UI presenta las coincidencias directas del negocio, precio por unidad base, stock vendible y presentaciones activas del contrato; no consulta el catálogo global ni fabrica medicamentos, precios o existencias. Los bloques de equivalencias y otras sedes no se mezclan con los resultados directos de este incremento.

Debounce de 300 ms, límite de 128 caracteres, AbortSignal de Query hasta HTTP y claves por botica/sucursal/texto. Una consulta nueva, borrar, desmontar o cambiar de sucursal cancela la anterior; no se presentan respuestas tardías ni resultados anteriores mientras carga. Los estados inicial, carga, vacío y error son explícitos; una denegación no se convierte en un vacío y el reintento de lectura es manual. Precio/stock conservan `JsonNumber`; la disponibilidad de una presentación se compara con BigInt, sin redondeo ni conversiones de dosis. La selección solo vive en memoria y se limpia al cambiar búsqueda, contexto o revalidar; el backend conserva la autorización. Carrito, borradores y cobro siguen sin escrituras.

Validación local: **25 regresiones nuevas**, **102 pruebas frontend totales** en ocho archivos, lint y build PASS. Los dobles de transporte de las pruebas de componentes no son evidencia de integración real. Se ejecutó `npm run build -- --outDir node_modules/.cache/medipos-lab-dist` para preservar `dist/`, que sirve DEV. Solo ese build se copió al hosting privado LAB, sin reiniciar el host ni invalidar sesiones; SPA/JS/CSS/readiness HTTPS 200, plataforma 404 y ACL privadas verificadas. No se modificó lógica backend ni se repitieron sus suites. El operador confirmó en Chrome LAB Centro la respuesta real con los tres bloques vacíos al buscar «paracetamol» y la vista «Sin coincidencias en esta sucursal». La respuesta/render del catálogo vacío queda comprobada por esa observación; el estado HTTP numérico no se capturó independientemente. Una segunda petición roja es compatible con la cancelación prevista, pero no se puede atribuir sin su estado de Network: los logs ordinarios no contienen registros 200/499/5xx. Cuatro regresiones focalizadas de abort/cambio de contexto/respuesta tardía se repitieron y pasaron (seis casos no seleccionados). Los casos positivos con medicamentos y stock auténticamente persistidos siguen pendientes de un conjunto autorizado; no se importó ni creó ningún producto.
