# MediPOS · Frontend F1

Fundación visual sobre el proyecto React + TypeScript + Vite existente. Implementación local preparada para auditoría; la integración real con Google, hosting y backend continúa pendiente. El backend y sus migraciones no se modifican en F1.

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
  app/           proveedores, contextos, rutas y navegación por rol actual
  components/    marca, estados de carga/error y elementos compartidos
  development/   fixtures aislados, importados solo durante desarrollo
  features/
    auth/        ingreso Google, sesión y cierre
    workspace/   selección de botica/sucursal y vistas futuras
    dashboard/   panel inicial sin estadísticas inventadas
    pos/         estructura visual de venta, sin operación comercial
  hooks/         acceso al runtime, sesión y contexto de trabajo
  layouts/       sidebar, cabecera, cuenta y menú modal móvil
  services/      fetch, CSRF, errores y validación de contratos HTTP
  types/         subconjunto F1 de DTOs reales y escenarios de desarrollo
  test/          preparación del entorno de pruebas
  index.css      tokens Tailwind, componentes y adaptación responsive
```

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

Gate local ejecutado el **2026-10-14**:

| Comando / revisión | Resultado |
|---|---|
| `npm run build` | Correcto: TypeScript estricto y Vite 8.3.4; JS 346.24 kB / gzip 107.49 kB |
| `npm run lint` | Correcto, sin errores ni advertencias |
| `npm run test` | **33 pruebas aprobadas, 0 fallidas**, dos archivos; 10.98 s |
| `npm run dev` | Arranque confirmado, puerto 5173; navegación real del navegador con el servicio sintético |
| Compilación de producción | Sin módulo/chunk de fixtures ni marcadores de sus UUIDs, datos o selector de escenarios |
| Revisión visual | Ingreso y panel en escritorio 1440 px, POS en tablet 834 px y móvil 390 px; menú modal móvil y sin desbordamiento horizontal en las vistas comprobadas |

Las pruebas cubren cookies/headers, CSRF antes de mutaciones, bloqueo ante fallo CSRF, Problem Details sanitizado, respuesta HTML inesperada, restricción HTTPS/rutas, cancelación de fetch y lectura de cuerpo, formulario OIDC nativo y su cancelación al desmontar el ingreso, sesión 401, validación de DTOs, recorrido completo de selección/layout/POS inicial/logout, roles, cambio de rol al navegar, licencia, horario, sucursal no asignada, URLs inválidas, estados vacíos y reintentos. La prueba del recorrido de varias pantallas tiene un presupuesto explícito de 15 s; no se eliminan aserciones ni se omiten casos por variaciones de carga local.

Las pruebas del frontend no constituyen evidencia de PostgreSQL, RLS, Google real ni despliegue de producción. El backend conserva sus gates externos pendientes. F1 queda implementado localmente y detenido para auditoría e integración HTTPS real.

F2: conectar búsqueda y presentaciones, caja abierta, borrador y versiones optimistas, cantidades/precios calculados por el servidor, pagos exactos/mixtos, confirmación atómica, reconciliación de respuestas ambiguas y ticket. No implementar esos efectos hasta completar sus validaciones y pruebas reales. PWA/offline y el resto de módulos siguen fuera de F1. Detenerse para auditoría.
