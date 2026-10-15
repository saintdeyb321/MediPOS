# MediPOS · Frontend F1 / F1.1

Fundación React + TypeScript + Vite existente, con sistema visual y arquitectura modular F1.1. Implementación local preparada para auditoría; la integración real con Google, hosting y backend continúa pendiente. El backend y sus migraciones no se modifican en F1.

## Ejecutar y validar

Node usado localmente: **24.18.1**, npm **11.16.0**. Las versiones resueltas están en `package-lock.json`.

```powershell
cd frontend
npm ci
npm run dev
```

Abrir `http://127.0.0.1:5173/`. Vite escucha únicamente en loopback y requiere el puerto 5173 libre. No hay proxy local hacia la API ni un segundo backend. En HTTP local la conexión real muestra el requisito de HTTPS; no se debilitan los controles del servidor.

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
    App.tsx        composición del runtime y entrada de desarrollo
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
  development/     fixtures y selector explícitos; excluidos de producción
  types/           identificadores de escenarios de desarrollo
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

## Vista de desarrollo

En el ingreso local, usar **Explorar vista de desarrollo**. El aviso amarillo identifica siempre **datos sintéticos y ausencia de sesión real**. El selector permite Owner, QF, Cashier, boticas vacías, error de listado, sucursales vacías, horario restringido, licencia restringida y sesión expirada.

El servicio de ejemplo es independiente del cliente HTTP y no intercepta solicitudes reales. No hay fallback automático ante errores. Se reinicia todo el proveedor/caché al cambiar de escenario. Todas las pantallas del recorrido pueden revisarse con este servicio sintético; no hay productos, importes de ventas, movimientos, datos personales ni estadísticas comerciales simulados. `import.meta.env.DEV` controla el import dinámico y la entrada al modo; `npm run build` excluye el módulo de fixtures y el selector. `npm run preview` sirve únicamente la compilación de producción, sin modo de ejemplo.

## Integración HTTPS de mismo origen pendiente

1. Servir `dist/` desde la raíz del origen HTTPS público autorizado para MediPOS, con certificado confiable. Puede hacerlo el hosting/reverse proxy aprobado; F1 no configura ese entorno.
2. Enrutar `/auth/*`, `/api/*`, `/platform/*`, `/health*` y el OpenAPI de desarrollo al backend, sin reescritura SPA. El resto de rutas de navegación del frontend puede resolver a `index.html`. No devolver HTML del frontend cuando una API falla.
3. Conservar Host público, TLS, controles de proxy confiable, antiforgery y cookies `__Host-*`. No ampliar CORS, confiar en cualquier proxy, omitir Origin ni desactivar HTTPS. El login regresa a `/`, donde el frontend verifica de nuevo `/auth/me` y obtiene un token CSRF nuevo cuando se necesite.
4. Configurar mediante el procedimiento existente Google, su callback exacto `/auth/google/callback`, `TenantApi:Enabled`, Data Protection y sesión persistente. El backend decide acceso, licencia, membresía, sucursal y horario. F1 no incorpora secretos en variables `VITE_*`.
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
