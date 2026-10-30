# MediPOS — Plan de corrección de Superadmin, alta de Owner por Gmail, modales y reinicio DEV

**Fecha:** 2026-10-29  
**Repositorio:** `saintdeyb321/MediPOS`  
**Rama de referencia:** `ci/postgres-diagnostics-8bc6b4c`  
**SHA auditado en GitHub:** `613532f6820c026cba60254e8918691b645c0c11`  
**Estado:** PLAN / NO EJECUTADO. Esta auditoría es estática sobre GitHub; no certifica la ejecución Windows, Chrome ni PostgreSQL.

## 1. Objetivo funcional

MediPOS debe permitir que el Superadministrador registrado con su Gmail personal cree una botica y su licencia **introduciendo el correo electrónico del futuro dueño**, aunque ese dueño jamás haya visitado MediPOS. Al primer inicio de sesión con la cuenta Google **que tenga ese correo verificado**, el backend debe vincular de manera segura la identidad registrada (`GoogleSubject` + `UserId`) con la asignación de Owner pendiente, sin UUID visible ni enlace de invitación. El propietario pasa al onboarding de razón social y primera sucursal.

La licencia pertenece a la **botica/tenant**, no al correo. El correo identifica al destinatario pendiente del rol Owner. Un Superadmin no recibe automáticamente membresías comerciales ni una “licencia permanente de botica”; su autoridad es independiente de las licencias de clientes.

## 2. Hallazgos sustentados por el código del SHA auditado

| ID | Hallazgo | Evidencia/ruta | Impacto |
|---|---|---|---|
| SA-01 | `POST /platform/tenants` acepta `TradingName`, fechas, `MaxBranches`, `Status`, **no correo de Owner**. | `backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs` (`CreateRequest`) | El Superadmin no puede designar al propietario antes del login. |
| SA-02 | `/platform/tenants/{tenantId}/bootstrap-owner` recibe `UserId` UUID y exige identidad ya registrada. | `PlatformLicenseEndpoints.cs`, `PlatformDirectoryEndpoints.cs`, `frontend/src/features/platform/PlatformPage.tsx` | UX no apta para un cliente nuevo. |
| SA-03 | El callback Google en `TicketReceived` hace upsert del usuario, pero no reclama asignaciones de Owner por correo. | `backend/src/MediPOS.Api/Authentication/GoogleAuthenticationEvents.cs` | El primer login no concede la membresía pendiente. |
| SA-04 | Se añadió `GoogleEmailVerified`/`google_email_verified` al modelo e INSERT/UPSERT sin migración EF nueva en el commit. La migración 38 no define esa columna. | `UserConfiguration.cs`, `IdentityAccessStore.cs`, migración `20261027043336_*.Designer.cs` | **P0: posible incompatibilidad de esquema al ingresar con Google**. Confirmar estado real antes de arrancar código nuevo. |
| SA-05 | `OwnerRegistrationLinks.cs` contiene tickets/cookies temporales pero no el flujo solicitado de asignación persistente y sin enlace. | `backend/src/MediPOS.Api/Authentication/OwnerRegistrationLinks.cs` | No usar como sustituto de una invitación persistida. Revisar si es código huérfano. |
| SA-06 | El panel divide el alta y la asignación Owner en tareas distintas, con entrada de UUID, notas técnicas y categorías globales en la misma consola. | `frontend/src/features/platform/PlatformPage.tsx`, `usePlatformConsole.ts` | Alta fragmentada y confusa. |
| UX-01 | `<dialog>` nativo en `Modal.tsx` utiliza `.task-dialog {position: fixed; inset: 0; ...}`, sin centrado explícito. La entrada CSS importa Tailwind. | `frontend/src/shared/ui/{Modal,Dialog,ui.css}`, `frontend/src/index.css` | **Hipótesis** de modales anclados arriba/izquierda; diagnosticar estilos computados antes de atribuir causa definitiva. |
| UX-02 | `ProductsPage.tsx` contiene `dirty={dirty || task === 'units'}` y `onChangeCapture` que marca dirty sin comparar el estado contra la versión confirmada. | `frontend/src/features/catalog/ProductsPage.tsx` | Avisos de descarte falsos, inclusive después de guardar. |
| UX-03 | El documento V4 ya describe la misma brecha de Owner por correo y los problemas del sistema modal. | `docs/MEDIPOS_PLAN_UX_IX_V4_Y_CIERRE_MVP.md` | Reutilizar V4, evitar duplicar soluciones anteriores. |

### Restricción de auditoría
La evidencia permite afirmar que faltan contratos y conexiones de flujo. **No** permite afirmar que toda pantalla falla en Chrome, que una migración se aplicó, ni que el estado local coincide con GitHub. Revisar `git status`, migraciones reales y CSS computado en la máquina del propietario.

## 3. Contrato objetivo de negocio y seguridad

1. **Crear botica + licencia + invitación Owner pendiente** de forma atómica y auditable. Campos de UI: nombre comercial, Gmail del Owner, plan/Trial, máximo de sucursales, resumen y confirmación. Mantener clave de idempotencia original ante timeout y GET de recuperación; nunca reenviar con una clave nueva para “solucionar” una respuesta incierta.
2. **Invitación sin enlace ni correo obligatorio.** Persistir `tenant_id`, `normalized_email`, `status` (`pending`, `claimed`, `revoked`), actor, timestamps y referencia de creación; no fabricar un `UserId` ni una membresía activa antes de verificar Google. Una sola invitación inicial vigente por tenant; no permitir dos Owners iniciales por carrera. No basar autoridad en una cookie o texto del frontend.
3. **Reclamo al autenticarse.** Solo tras OIDC finalizado (firma, issuer, audience, nonce, state, expiración) y `email_verified=true`; identificar al usuario por su Google `sub` estable, normalizar el correo con `GoogleEmailAddress`, reclamar bajo transacción y bloqueo, vincular al `UserId` real y crear la membresía Owner **exactamente una vez**. La misma cuenta puede tener distintas invitaciones de boticas; no autoasignar a cuentas no coincidentes. Reintentos concurrentes seguros. Si la licencia no es operable, no conceder acceso efectivo y mostrar estado recuperable. La membresía se sigue autorizando por rol/licencia/sucursal en cada API.
4. **No convertir el Gmail en credencial suficiente.** El email solo sirve como localizador de una invitación. No usar coincidencias imprecisas, quitar puntos, ignorar `+alias`, ni tratar correos no verificados como prueba. Un cambio posterior de correo Google no debe transferir una membresía ya ligada al sujeto Google. Administrar diferencias de email verificado y sujeto explícitamente.
5. **Edición/revocación antes del reclamo.** El Superadmin puede corregir el Gmail pendiente con operación auditada y control de concurrencia; cancelar la invitación vieja y reemplazarla sin acceso residual. Una vez reclamada, no cambiar el Owner mediante el formulario de invitación inicial: implementar procedimiento separado y restringido o marcarlo fuera de MVP.
6. **Cronología de Trial.** Mantener la regla vigente de inicio al crear la botica (30 días) y mostrarla claramente, a menos que el propietario autorice expresamente cambiarla al primer acceso. No alterar silenciosamente las reglas de licencias.
7. **Privilegios.** El Superadmin administra tenants, licencias e invitaciones iniciales; no obtiene acceso global a stock, ventas ni datos comerciales. El Owner no entra a `/plataforma`. Conservar auditoría, CSRF, HTTPS, Origin, controles de sesión, FORCE RLS y runtime limitado. Mantener el modo DEV local persistente ya implementado y no habilitar producción por accidente.
8. **Migraciones.** Resolver primero la columna `google_email_verified` pendiente, con nueva migración incremental (siguiente ID disponible tras comprobar historia), y añadir el esquema de invitaciones con índices, constraints y FORCE RLS, GRANT mínimos. No modificar migraciones 1–38 ni confiar solo en `EnsureCreated` o SQL ad hoc.

## 4. Flujo UX objetivo

### Consola Superadmin (`/plataforma`)

- Resumen compacto: **Boticas**, **Licencias por vencer**, **Propietarios pendientes**; mostrar cantidades reales o estados vacíos (no inventar estadísticas).
- Una sola acción primaria: **Nueva botica**. Tabla/listado compacto y filtrable de boticas, cada fila con nombre, propietario (Gmail si pendiente; identidad si aceptado), licencia, vencimiento, sucursales permitidas, estado y acciones contextuales. Filtros/consultas oficiales, paginación y mensajes claros.
- Mantener módulos especializados: **Boticas**, **Licencias**, **Pendientes** y **Catálogo global** como función secundaria, no interrumpiendo el alta. No duplicar navegación.
- El estado del Owner es explícito: `Pendiente de primer ingreso`, `Activo`, `Revocado` o error recuperable. Nunca mostrar UUID al operador como campo obligatorio.

### Wizard centrado de «Nueva botica»

1. **Negocio:** nombre comercial, validaciones relevantes.
2. **Propietario y licencia:** Gmail verificado por formato (la propiedad se verifica al iniciar Google), Trial y máximo de sucursales, fechas visibles.
3. **Revisión:** resumen breve: botica, Gmail, licencia, vencimiento; confirmación única. Resultado: `Botica creada. Propietario pendiente de iniciar sesión.`

No hay pantalla posterior de “asignar primer propietario” para el flujo ordinario. La lista de pendientes permite corregir el Gmail antes del primer acceso. La ayuda se muestra bajo un botón «¿Cómo funciona?» sin ocultar avisos críticos.

### Primer acceso Owner

- Inicia Google sin enlaces especiales; el servidor identifica invitaciones pendientes coincidentes con el **email verificado**.
- Si se reclama correctamente, la botica aparece en selección; si no hay sedes, mostrar el CTA único **Configurar mi botica** y el onboarding de razón social/primera sede.
- Si el email no corresponde, no hay invitación o está revocada, no otorgar permisos; mostrar un estado comprensible. No exigir UUID ni una segunda confirmación del Superadmin.

## 5. Modales compartidos (todos los módulos)

- Corregir `frontend/src/shared/ui/Modal.tsx`, `Dialog.tsx` y `ui.css` en un solo lugar. Usar `<dialog>.showModal()` y top layer ya existentes; verificar CSS computado (`margin`, `inset`, `display`, `width`, viewport) porque el bug de centrado solo fue observado en el navegador del usuario, no reproducido aquí.
- **Modal normal/ancho:** centro horizontal y vertical visible, ancho acotado, `max-height: 90dvh`, scroll interno y áreas seguras. Confirmación destructiva también centrada. **Drawer:** anclaje lateral deliberado, sin aplicarle centrado global.
- Un único pie de acciones, botones de altura/alineación consistentes, títulos y subtítulos legibles, padding simétrico, espaciado consistente; sin palabras técnicas innecesarias. No anidar modales.
- Mantener Escape, backdrop permitido, foco inicial, trap/foco restaurado, contraste, teclado, zoom 200%, layout móvil y scroll bloqueado del fondo. Verificar 1366×768 y 390×844 en Chrome.
- `dirty` debe significar **diferencia real no guardada contra la versión base**. Al confirmar persistencia y reconciliar estado, limpiar dirty y actualizar baseline. Evitar `dirty || task === 'units'` y flags permanentes por evento. Durante respuesta incierta conservar el indicador de recuperación y no repetir POST. Cerrar después de éxito confirmado debe cerrar sin preguntar “Descartar cambios”.
- Revisar el uso del modal común en Productos, Inventario, Compras, Empleados, POS, Caja, Configuración y Plataforma. No rediseñar cada módulo desde cero ni reemplazar formularios extensos por modales imposibles.

## 6. Sprints medianos (orden obligatorio)

### Sprint SA1 — Backend: contrato Owner por Gmail y esquema (P0)

**Salida:** una creación `botica+licencia+Owner pendiente` atómica con Gmail, idempotencia, consulta de pendientes, cambio/revocación antes del reclamo; migración incremental de `google_email_verified` y tabla de invitaciones; RLS/GRANT mínimos. Reusar `CreateTenantIdempotentlyHandler`, `PlatformLicenseEndpoints`, `CreateMembershipHandler` y modelado existente; no SQL improvisado. Mantener sin uso o eliminar cuidadosamente los tickets de enlace no requeridos, sin introducir una vía alternativa de privilegios.

**Criterios:** un email no registrado puede ser invitado; dos envíos idénticos con misma clave no generan boticas distintas; otro email/una invitación revocada no reciben Owner; la migración se genera y verifica, **no se aplica automáticamente a DEV**.

### Sprint SA2 — Login/claim y experiencia de Superadmin (P0/P1)

**Salida:** reclamo en login de Google verified, protección frente a carreras, estados recuperables; frontend de consola y wizard por Gmail, listado con estado de invitación, propietario y licencia; eliminar UUID del flujo visible. Ajustar selección boticas/onboarding para primer acceso.

**Criterios:** Superadmin crea invitación antes del primer login; Owner correcto ingresa y ve su botica sin acción adicional; el login repetido no duplica membresía; el Superadmin no entra automáticamente al espacio comercial; el Owner no accede a plataforma.

### Sprint SA3 — Sistema modal y aceptación manual (P1)

**Salida:** modales centrados globalmente salvo drawers, formularios compactos, estilos coherentes, corrección de dirty/guardado; sin alteración de contratos POS/compras/inventario. Revisión visual rápida en Chrome con el propietario, no batería masiva.

**Criterios:** modal corto/ancho centrado y usable, 1366×768/390×844 y zoom 200%; guardar presentaciones y cerrar no solicita descarte falso; foco, Escape y recuperación correctos.

### Sprint SA4 — Reinicio de los datos de simulación de DEV (DESTRUCTIVO, aprobación aparte)

**No ejecutar en SA1–SA3.** Preparar inventario y plan de restauración sin escribir datos. Verificar `medipos_dev` en `localhost:5432`, proceso localhost:7243, backups y secreto/allowlist. Identificar conteo de boticas, ventas, movimientos, licencias y usuarios; comprobar que no hay datos reales ni otros propietarios que dependan de ese DEV. Obtener autorización final indicando objetivo exacto, pérdida de datos y respaldo privado restaurable.

**Preferencia:** dejar **vacías las entidades comerciales/de simulación** conservando migraciones, permisos, extensión RLS, identidad interna de Superadmin y configuración Google. No `TRUNCATE ... CASCADE` sin diseño de FK/auditoría, ni `dropdb` genérico, ni borrar LAB/QA/main/producción. Si se elige recrear por completo la base DEV, documentar cómo se conservará o reconstruirá la identidad de Superadmin y su UUID allowlisted: al borrar `users`, el UUID anterior deja de existir y habrá que registrar Google de nuevo y reautorizar por configuración privada.

**Criterios:** respaldo/rollback verificados; consentimiento expreso para ejecutar el reset; esquema al día, RLS, GRANT y login Google; cero boticas comerciales anteriores; creación desde cero con los dos Gmail en Chrome.

## 7. Recorrido de aceptación manual (lo realiza el propietario)

1. Perfil Chrome A: login Gmail principal → `/plataforma` (200 autorizado); panel sin UUID obligatorio.
2. Crear `Botica A` con Gmail secundario **aún no registrado**, licencia Trial de 30 días y límite de sucursales; ver `Owner pendiente`.
3. Perfil Chrome B: login Gmail secundario → aparece la Botica A; seleccionar → onboarding sin sede → crear sede; sin URL de invitación y sin UUID manual.
4. Perfil A: comprobar `Owner activo`, licencia vigente y navegación; no tener acceso comercial a la Botica A solo por ser Superadmin.
5. Perfil C con otro Gmail: no debe poder reclamar Botica A ni acceder a plataforma.
6. Crear Botica B con otro Gmail; comprobar que cada cuenta ve exclusivamente sus boticas/sucursales y que ningún Owner hereda plataforma.
7. Productos → presentación → proveedor → compra con lote/vencimiento → inventario → apertura/cierre de caja y venta; verificar fallos y corregirlos en iteraciones breves.
8. Confirmar edición de presentaciones y cierre modal sin falso descarte; probar modales en escritorio/móvil y zoom 200%.

## 8. Reglas de ejecución y reporte

- **Sin** otros clusters, QA o staging; trabajar sobre repositorio existente. No modificar main, LAB/QA/producción ni hacer push/despliegues no autorizados.
- Evitar suites enormes; usar build frontend/backend y **validaciones muy focalizadas** únicamente en contratos delicados: claims/OIDC verificado, idempotencia, concurrencia Owner, RLS y migración. La aceptación comercial es manual del propietario.
- No modificar secretos, Data Protection ni privilegios persistentes sin necesidad. No confundir texto de UI con autorización backend.
- Al terminar cada sprint, comunicar **máximo 10 líneas**: archivos, contrato, migración, bloqueo, build, cómo reproducir manualmente. Mantener actualizado `docs/FRONTEND_INTEGRATION_PLAN.md` en pocas líneas.
- No declarar cerrado el MVP hasta realizar el recorrido manual anterior.
