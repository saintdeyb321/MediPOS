# MediPOS — Auditoría y plan de cierre Superadmin V2

**Fecha:** 2026-10-30  
**Fuente:** revisión estática de `saintdeyb321/MediPOS`, rama `ci/postgres-diagnostics-8bc6b4c`, HEAD `d0e0e123219d1f62197ba31a78fe99ebcf0cacde` (`Superadmin`).  
**Alcance:** rutas y experiencia post-Google, separación de plataforma/tenant, boticas, licencias, propietario pendiente por Gmail, accesos/API/estado, código obsoleto, y decisión de limpieza de `medipos_dev`.  
**Limitación:** GitHub no da acceso al árbol local de Windows, la sesión autenticada de Chrome ni al PostgreSQL vivo; las afirmaciones de instalación DEV/39 migraciones son del `backend/README.md`, no verificadas directamente en esta auditoría. **No se modificó la BD, no se ejecutaron simulaciones ni se certificó el MVP.**

## 1. Dictamen ejecutivo

**No rehacer SA1–SA3.** El HEAD ya incorpora el flujo sin UUID visible: `POST /platform/tenants/with-owner`, `initial_owner_invitations`, migración 39 `AddInitialOwnerEmailOnboarding`, reclamo por correo verificado en callback Google, corrección/revocación de invitación pendiente, UI de wizard y CSS de modal centrado. El README informa instalación de migración 39 en DEV con FORCE RLS y permisos mínimos; aceptación con dos cuentas en Chrome todavía pendiente.

El defecto principal que falta es el **enrutamiento y la experiencia diferenciada de Superadmin**: tras Google, `GoogleAuthenticationEvents.TicketReceived` devuelve a `/`, `LoginPage` redirige toda sesión válida a `/seleccionar-botica`; `/plataforma` se renderiza bajo `SelectionLayout` (cabecera/enlace de botica compartidos con owners), y se muestra el enlace «Plataforma» incluso para cuentas ordinarias. No hay una entrada específica de plataforma. Debe ser la vista predeterminada del operador autorizado sin confundir autoridad de plataforma con membresías comerciales.

**Segundo defecto funcional identificado por lectura:** `PlatformPage` deja montado `task='create'` después de que `usePlatformConsole.create()` concluye y `NewTenantForm` mantiene `dirty`; no hay un cierre condicionado al éxito observado (reconciliación por `tenant-requests/{key}`). Esto permite un aviso falso de descarte después de crear. La creación incierta NO se debe cerrar ni repetir.

**Decisión de datos:** para «empezar desde cero» no se necesita crear una nueva migración base ni borrar el historial EF. La opción preferida es purgar únicamente los registros de simulación tenant en `medipos_dev` manteniendo esquema/migraciones 1–39, usuarios/UUID administrativo, allowlist, Google y catálogo global, con respaldo, listado exhaustivo, revisión y autorización separada. La recreación total de la misma BD y un *baseline* nuevo son alternativas más riesgosas y no están autorizadas.

## 2. Evidencia por flujo (confirmado por código vs pendiente)

| ID | Evidencia estática en HEAD | Estado y acción |
|---|---|---|
| A01 | `frontend/src/features/auth/LoginPage.tsx`: sesión válida → `/seleccionar-botica`; `GoogleAuthenticationEvents.cs`: `ReturnUri="/"`. | **CONFIRMADO P0:** sin redirección por autoridad. Crear punto de entrada post-login. |
| A02 | `frontend/src/app/router/routes.tsx`: `/plataforma` y `/seleccionar-botica` usan `SelectionLayout`. `SelectionLayout.tsx` muestra «Plataforma» para todos. | **CONFIRMADO P0:** separar `PlatformLayout`, navegación y menú; no mezclar consola y selección comercial. |
| A03 | `frontend/src/features/platform/PlatformPage.tsx`: pestañas Boticas/Licencias/Pendientes/Catálogo/Acceso, listados en una tabla y modales. | **P1:** refactorizar como dashboard + áreas orientadas a tareas, no repetir tab sin contexto. |
| A04 | `NewTenantDialog.tsx`: wizard 3 pasos con Gmail sin UUID; `platform/api.ts`: POST `/platform/tenants/with-owner`. | **IMPLEMENTADO, NO ACEPTADO:** no volver a implementar el contrato; afinar validaciones y resultado. |
| A05 | `PlatformLicenseEndpoints.cs`: POST con Owner; `CreateTenantIdempotentlyHandler.cs`: idempotencia con email en hash. | **IMPLEMENTADO:** mantener garantía ante timeout y conservar clave original. |
| A06 | `InitialOwnerInvitations.cs`, store, política RLS, `UpsertGoogleUserHandler.cs`: reclamo tras OIDC/email_verified con bloqueo por tenant. | **IMPLEMENTADO, NO ACEPTADO CON GOOGLE REAL:** verificar en Chrome dos cuentas; evaluar fallo de un tenant pendiente sin bloquear injustificadamente login general. |
| A07 | `backend/README.md`: migración 39 aplicada, 56 tablas FORCE RLS, runtime restringido y GRANT mínimos. | **EVIDENCIA DOCUMENTAL, NO VERIFICADA EN VIVO:** comprobar read-only antes de cualquier cambio. |
| A08 | `PlatformPage.tsx` `Modal(task==='create')` y `usePlatformConsole.create()` no cierran modal con confirmación observada ni limpian dirty. | **CONFIRMADO P0 UX/estado:** retornar resultado tipado (`committed`, `unknown`, `failed`) o equivalente y cerrar solo confirmado/reconciliado. |
| A09 | `ui.css` `.task-dialog` usa `inset:0; margin:auto; height:fit-content`; drawers usan `.task-drawer` aparte. | **CORREGIDO EN CÓDIGO:** no volver a “arreglar” sin inspeccionar CSS computado en Chrome; verificar zoom/móvil. |
| A10 | `TenantSelection.tsx` todavía contiene «Mi identidad para asignar propietario» con UUID, pese al flujo nuevo. | **CONFIRMADO P1:** retirar del recorrido ordinario; conservar diagnóstico restringido solo si hay utilidad y acceso explícito. |
| A11 | `usePlatformConsole.ts` todavía expone `lookup` y `bootstrap`; `platform/api.ts` llama `/platform/identities/{id}` y `/bootstrap-owner` heredados; `PlatformPage.tsx` ya no usa ese flujo visible. | **CANDIDATO OBSOLETO:** buscar referencias en tests, clientes, scripts y documentación; no eliminar endpoints históricos sin política de compatibilidad y evidencia de no uso. |
| A12 | `OwnerRegistrationLinks.cs` ya no existe en HEAD; antes ofrecía ticket/cookie sin flujo de invitación persistente. | **ELIMINADO:** no reintroducirlo. |
| A13 | `backend/README.md` describe dos boticas, dos usuarios, una membresía y datos de ensayo previos. | **DECISIÓN PENDIENTE:** no borrar hasta inventario fresco y aprobación del propietario. |
| A14 | HEAD contiene `frontend/dist.before-owner39/` y `dist.before-platform-rate-20261030/`. | **CANDIDATOS DE LIMPIEZA:** comprobar si son assets versionados por accidente, su referencia en hosting/rollback y respaldo; retirar del repositorio solo si no participan en operación. |
| A15 | `PlatformPage` filtra la página actual de 20 tenants y muestra métricas correctamente rotuladas «en esta página». | **P1:** evitar confundir filtro local con búsqueda global; paginado o búsqueda servidor solo si existe contrato seguro. |

### Dependencias de seguridad que deben permanecer

- Superadmin: allowlist privada de ID interno, sesión Google real y `/platform/session` protegido. **No** crear un campo de rol editable en React ni inferir Superadmin de correo escrito.
- Owner: correo normalizado exacto de ticket OIDC verificado, vinculado al `sub`/`UserId` real al primer ingreso; idempotencia y una membresía Owner inicial. No enlaces ni UUID manuales.
- La licencia pertenece al tenant y el Superadmin **no hereda** permisos sobre stock, ventas, clientes ni sucursales.
- Mantener CSRF, Origin, cookies seguras, rate limit, RLS, audit logs, bloqueos transaccionales, GRANT mínimos, protección ante respuestas inciertas.

## 3. Diseño de interacción objetivo: experiencia independiente

### 3.1 Entrada por sesión

```text
Google OIDC /auth/login → sesión válida → EntryRouter
             |
     comprobación única y controlada de autoridad (GET /platform/session)
       |                                           |
    200 autorizado                              403 ordinario
       |                                           |
  /plataforma                              /seleccionar-botica
  PlatformLayout                           flujo Owner/empleado
```

Un `429`, `5xx`, falta de red o respuesta ambigua **no equivale a 403**: mostrar recuperación y reintento manual sin cambiar de identidad o generar un bucle de consultas. No sondear repetidamente por foco. Hacer una sola detección por login, con caché ligada a sesión y borrado al logout, sin usar el estado de React como autoridad real. Si una misma cuenta es Superadmin y miembro comercial, entrar por defecto a Plataforma y ofrecer un cambio de contexto **explícito**, no una vista mezclada; el backend comprobará cada autorización.

### 3.2 Consola `/plataforma` con `PlatformLayout` exclusivo

- **Sidebar:** Inicio de plataforma; Boticas; Propietarios pendientes; Licencias; Catálogo global (secundario); Cuenta y Cerrar sesión. Solo mostrar rutas de plataforma. Un solo logout.
- **Home en tarjetas:** Boticas registradas (total real o «de esta página»); Owners pendientes; licencias por vencer; incidencias/solicitudes pendientes. No inventar agregados. CTA prominente «Nueva botica».
- **Boticas:** buscador con alcance explícito, tabla compacta legible o tarjetas responsive, estado del Owner y licencia, acción única «Gestionar». Ficha de negocio en drawer/tarea contextual; no redirigir a Workspace comercial.
- **Pendientes:** correo destinatario, negocio, fecha y estado; «Corregir Gmail» o «Revocar invitación» en modal, con confirmación visible; para Owner ya vinculado, mostrar que no es editable mediante alta inicial.
- **Licencias:** tab/área con selección clara de negocio; renovación/suspensión/reactivación en modales enfocados. Mostrar fechas amigables y UTC solo en ayuda/detalle, consecuencias antes de confirmar y respuesta al guardar.
- **Catálogo global:** sección secundaria separada, no competir con la creación de boticas; conservar restricciones y recuperación, señalizar si la conciliación no es idempotente.
- **Diseño:** tokens existentes, jerarquía tipográfica, tarjetas bordeadas con espacios simétricos, modales nativos centrados, confirmaciones diferenciadas, mensajes cortos en español, ayuda «¿Cómo funciona?» bajo demanda, estados vacíos útiles, tabulación y foco accesible, 1366×768, móvil 390 px y zoom 200%.

### 3.3 Wizard «Nueva botica»

1. **Datos de botica:** nombre comercial y explicación mínima.
2. **Owner y licencia:** Gmail (se acepta aún no registrado), 1–5 sucursales y Trial 30 días desde creación.
3. **Resumen:** nombre, correo, caducidad, sucursales, confirmación; un POST con clave idempotente estable.
4. **Resultado confirmado:** cerrar modal sin preguntar «Descartar», notificación breve y mostrar botica/Owner pendiente en la lista o ficha.
5. **Resultado incierto:** mantener intención/clave, bloquear duplicado y ofrecer consulta de resultado original. No borrar formulario ni crear otra botica con otra clave.

### 3.4 Estados de Owner y licencia

| Owner | UI | Acción |
|---|---|---|
| Sin asignación histórica | «Requiere regularización» | No inventar una invitación nueva automáticamente. |
| Pendiente | «Invitación por Gmail pendiente del primer inicio» | Corregir/revocar con confirmación y auditoría. |
| Activo | Nombre/correo verificado y fecha relevante | Consultar; no revocar mediante herramienta inicial. |
| Revocado | Estado histórico, sin acceso | Mostrar explicación; nueva designación solo bajo contrato permitido. |
| Licencia suspendida/vencida | Señalización clara; no prometer acceso inmediato | Resolver licencia y exigir nuevo login si así dicta el contrato. |

## 4. Auditoría de procesos muertos/contratos HTTP

Construir una matriz **comprobable** `ruta/función → llamada UI o cliente → contrato backend → uso en tests/scripts → decisión` para:

- `GET /platform/session` y admisión por login/console; impedir 429 por múltiples consultas o focus.
- `GET /platform/tenants` + paginado, detalle, `GET /initial-owner`, cambio/revocación, licencias y categorías.
- `POST /platform/tenants/with-owner` y `GET /platform/tenant-requests/{key}`: conservar siempre.
- Histórico `POST /platform/tenants`, `POST /platform/tenants/{id}/bootstrap-owner`, `GET /platform/identities/{id}`, y `usePlatformConsole.lookup/bootstrap`, `platformApi.identity/bootstrap`: candidatos a ocultar, encapsular o deprecar tras analizar clientes y compatibilidad. No borrarlos solo por no verse en UI.
- Importaciones antiguas, queries duplicadas, handlers que no se llaman, parámetros/estados `taskHidden`, `initialOwner`, `ownerExpected`, `creationKey` y cachés de consultas; **no suprimir marcadores de recuperación que mantienen la idempotencia**.
- Recursos `frontend/dist.before-*` incluidos en el commit, solo tras confirmar pipeline y posibilidad de restaurar.

Entregar tabla breve `MANTENER / REFRACTORIZAR / DEPRECAR / ELIMINAR`, con evidencia de referencia. Eliminar solo lo demostrado sin consumidores; actualizar tests y documentación de los contratos retirados. No hacer rediseño general de otros módulos.

## 5. Sprints medianos (ejecutar de corrido)

### S1 — Enrutamiento y autoridad independiente (P0)

**Implementar:** `EntryRouter` post-login (no redirección ciega a botica), caché/scoping de admisión, errores 403/429/5xx correctos, `PlatformLayout`, navegación exclusiva y logout único. Owner/empleado conservan selección normal. Mantener backend como fuente de verdad de privilegios.

**Aceptación de código:** login correcto Superadmin redirige a `/plataforma`; Gmail Owner a `/seleccionar-botica`; fallo de red no se interpreta como ausencia de autoridad; ninguna consulta en bucle, ningún cambio de rol por frontend.

### S2 — Consola y flujos de gestión (P0/P1)

**Implementar:** tarjetas y navegación, ficha de botica, estados Owner/licencia, modales operativos, wizard y recuperación. Corregir el falso `dirty`/modal que queda abierto tras `create()` confirmado; cerrar por éxito realmente reconciliado, conservar ante incertidumbre. Mantener Gmail como identificador humano, UUID solo técnico/backend.

**Aceptación de código:** crear botica por Gmail no registrado, ver pending y luego claimed en el mismo contrato; cambiar/revocar pendiente, renovar/suspender/reactivar, errores claros, ninguna creación duplicada al recargar. Verificación manual real por el propietario queda pendiente.

### S3 — Inventario de API/código, reducción y mantenimiento (P1)

**Implementar:** matriz de usos de endpoints, eliminar estados/client calls indiscutiblemente huérfanos, limitar duplicaciones de fetch y resolver transiciones de UI que no reflejan writes. Retirar assets históricos `dist.before-*` solo si demostrado seguro y sin afectar rollback. Comprobar build TS/Vite y .NET y únicamente pruebas focalizadas de auth/claim/idempotencia necesarias para no introducir regresiones de seguridad. Preparar revisión final/guía de 2 Gmail.

**Aceptación:** ninguna ruta visible hace nada; todos los CTAs tienen acción y confirmación/estado/error; cero eliminaciones de endpoints con consumidores conocidos; cambios acotados y trazables.

### S4 — Decisión de limpieza de `medipos_dev` (PLAN, NO EJECUTAR)

**NO borrar nada en S1–S3.** Leer estado real read-only: instancia/DB/puerto/rol/versión/39 migraciones/FORCE RLS/FK/grants/conteos y sesiones; identificar si hay datos reales, otras cuentas o servicios dependientes. Verificar backup fresco recuperable, no confundir TOC legible con restauración comprobada. Proponer dos caminos:

- **A (preferido):** vaciar de forma transaccional y autorizada **solo** las tablas tenant/comerciales de simulación, preservar `users`/UUID Superadmin, configuración, claves DP/OAuth, catálogos globales, funciones, migraciones y RLS; verificar recuentos y rollback antes de COMMIT. No ejecutar sin nueva aprobación de alcance explícito.
- **B (solo si propietario confirma destrucción total DEV):** reconstruir la misma DB DEV desde las 39 migraciones existentes y permisos revisados. Requiere backup/restauración probados, coordinación con proceso HTTPS, recuperación de Superadmin por Google y actualización de allowlist; no afecta otros entornos. **No sustituir arbitrariamente 39 migraciones por una sola**: rompería actualizaciones desde versiones anteriores y no es requisito para «base vacía».

Un baseline limpio para futuras instalaciones comerciales puede estudiarse aparte en el primer release estable con política de compatibilidad, no durante la corrección de Superadmin. En ambos casos, antes de cualquier SQL destructivo, publicar lista exacta de tablas, impacto, pérdida irreversible después del commit, plan de recuperación y solicitar consentimiento específico.

## 6. Recorrido manual de aceptación del propietario (solo después de S1–S3 y, si se autoriza, S4)

1. Perfil Chrome A (Gmail principal Superadmin): login → solo consola Plataforma, sin selector de boticas; `/platform/session` autorizado.
2. «Nueva botica» → nombre, Gmail secundario todavía no registrado, Trial y sucursales → confirmar una vez; modal se cierra, botica aparece «Owner pendiente».
3. Perfil Chrome B (Gmail secundario): primer login Google → acceso a la botica y onboarding; sin UUID, enlace ni segunda aprobación.
4. Perfil A: reconsulta → «Owner activo»; gestionar licencia/propietario; no acceder a stock/ventas de la botica sin membresía.
5. Perfil B: intentar `/plataforma` → 403; Owner solo accede a sus boticas, configura primera sede.
6. Repetir con Gmail C y botica distinta; no compartir tenant, stock, permisos ni licencias.
7. Capturar defectos reales en una tabla `pantalla, acción, esperado, obtenido, HTTP status, traceId` sin secretos.

## 7. Restricciones y entrega de Codex

- Trabajar solo en MediPOS, conservar cambios sin commit, no force push ni reset; no modificar main ni LAB/QA/producción.
- No crear nuevos staging/QA/containers ni bases paralelas. No usar suites masivas ni simulaciones artificiales como sustituto del propietario.
- Para seguridad mínima: build y comprobaciones focalizadas de autorización, Gmail verified, RLS, idempotencia, callbacks concurrentes y contrato de migración si se toca; evitar suites de miles de tests.
- No tocar la BD actual, credenciales, certificados o usuarios para S1–S3. S4 es lectura y propuesta, jamás DROP/TRUNCATE sin aprobación expresa.
- Handoff ≤ 15 líneas: SHA/árbol, cambios, rutas, llamadas retiradas, build, pendientes reales Chrome/PG, comandos DEV habituales y checklist de simulación.
- Criterio de cierre: **CODE_READY** hasta que el propietario valide los dos Gmail en Chrome. El MVP no queda certificado por compilar.

## 8. Ejecución local S1–S3 y preparación S4 (2026-10-30)

**S1–S3 CODE_READY; aceptación Chrome/Google pendiente.** Base local `d0e0e123219d1f62197ba31a78fe99ebcf0cacde`, rama diagnóstica; el plan adjunto se conservó. No cambios de backend, contratos, migraciones, secretos, allowlist, certificados ni datos. Sin push ni servicios nuevos.

- **S1:** `/` → `/entrada` tras sesión Google válida; una admisión compartida por UserId/expiración de sesión, borrada al logout/401. Solo403 `access.platform_denied` elige selección comercial; Origin403,429,5xx y red muestran recuperación. Sin sondeo por foco/reconexión; la consola revalida cada60s y bloquea ante error. `PlatformLayout` exclusivo, sidebar/móvil, cuenta y un logout; cambio comercial explícito. El selector normal solo muestra el retorno a Plataforma si ya hay admisión aprobada en caché; no consulta autoridad por su cuenta. Se retiró el UUID de «Mi identidad».
- **S2:** inicio con indicadores rotulados por página, directorio/filtro/paginación reales, áreas Pendientes/Licencias/Catálogo/Cuenta y ficha lateral. Creación y recuperación retornan éxito únicamente tras constancia original; se cierra el wizard, limpia dirty y conserva la notificación sin desmontar el hook al seleccionar tenant. Resultado incierto conserva clave/campos y recuperación GET; error definitivo permite corregir. Modales de Owner/licencia/categoría limpian al confirmar o conciliar; categorías sin idempotencia siguen exigiendo verificación manual. Acciones de licencia reflejan las transiciones existentes de `License`, sin sustituir autorización del servidor. Se conservó el CSS compartido ya centrado, foco y bloqueo de cierre durante petición.
- **S3:** eliminados estado `identity`, `lookup/bootstrap`, clientes `identity/bootstrap` y tipo `VerifiedIdentity` frontend sin consumidores ordinarios; endpoints históricos conservados. Recuperación de direcciones `/plataforma?...` queda ligada a identidad en el mecanismo compartido de navegación, sin guardar cuerpos/tokens. Los12 archivos de dos `dist.before-*`, fuera del hosting/pipeline actual, se trasladaron byte a byte a `backend/obj/superadmin-v2-validation/legacy-frontend-rollback`; hashes y mapeos en `relocation-manifest.json`. No se borró contenido de rollback; `.gitignore` evita nuevos archivos de ese patrón. No reejecutar helpers históricos de instalación con sus ubicaciones antiguas.

### Matriz comprobada de consumidores

| Función o endpoint | Consumidores / uso real en código | Decisión |
|---|---|---|
| GET `/platform/session` | `usePlatformAdmission`, EntryRouter/PlatformLayout; HTTP tests de directorio | MANTENER; unificar caché por sesión y eliminar consulta por foco |
| GET `/platform/tenants` | directorio/indicadores de página en `usePlatformConsole`; `PlatformDirectoryHttpTests` | MANTENER, filtro local explícito y paginado existente |
| GET `/platform/tenants/{id}/details` | ficha/licencias y conciliación de mutaciones, también bootstrap histórico | MANTENER |
| GET `/platform/tenants/{id}/initial-owner` | ficha, cambio/revocación y conciliación; pruebas Owner por correo | MANTENER |
| POST `/platform/tenants/with-owner` | `platformApi.create` → wizard; contrato CSRF/idempotencia y HTTP Owner | MANTENER como alta ordinaria |
| GET `/platform/tenant-requests/{key}` | `create/reconcileCreation`, incluida recarga con clave previa | MANTENER siempre; no sustituir por repetir POST |
| POST `/initial-owner/change` y `/revoke` | formulario pendiente, identidad optimista, lectura posterior | MANTENER |
| POST `.../licenses/{id}/renew,suspend,reactivate` | modal de licencia y conciliación; pruebas de ciclo de vida | MANTENER |
| GET/POST `/platform/catalog/categories` | área secundaria con lectura previa; alta y conciliación manual | MANTENER sin afirmar idempotencia inexistente |
| POST histórico `/platform/tenants` | `PlatformTenantCreationHttpTests`, compatibilidad/idempotencia histórica; sin cliente ordinario nuevo | DEPRECAR para nuevas altas UI, conservar endpoint y hashes históricos |
| POST `.../bootstrap-owner` | `PlatformBootstrapOwnerHttpTests` y procedimiento técnico histórico; recuperación/compatibilidad | MANTENER protegido, fuera de UI; no retirar por desconocer consumidores externos |
| GET `/platform/identities/{id}` | `PlatformDirectoryHttpTests`, verificación histórica del bootstrap | MANTENER protegido, sin listado masivo ni consulta automática |
| `usePlatformConsole.lookup/bootstrap`, `platformApi.identity/bootstrap`, `VerifiedIdentity` UI | no consumidores actuales salvo expectativas de UI antigua | ELIMINAR del frontend; reemplazadas pruebas de UUID por flujo Gmail, manteniendo aserciones de idempotencia/recuperación |
| `creationKey,platformPending,categoryPending,ownerExpected,taskHidden` | transporte, URL y recuperación modal/página | MANTENER; no son código muerto |
| `dist.before-owner39`, `dist.before-platform-rate-20261030` | no consumidores en hosting `frontend/dist` ni Vite; rollback de instalación anterior | RETIRAR del árbol versionable, conservar copia exacta/mapeo local ignorado |

### Validación y estado operativo

Build API .NET10 aislado:0 errores/advertencias. Build TS/Vite PASS, warning de bundle grande conservado. ESLint y tsc focalizados PASS.77 casos distintos en8 archivos PASS: ejecuciones complementarias de49 y35 casos,7 comunes; entrada/401/403/429/red, admisión, Gmail/CSRF/idempotencia, transiciones, modal/dirty, recuperación, navegación y límites comerciales. Las expectativas antiguas del Router se alinearon a los títulos/navegación ya presentes en d0e0e123, sin modificar UI comercial ni eliminar verificaciones de permisos/logout. Su fixture comercial parte de selección, mientras el flujo desde `/` se prueba con transporte de admisión explícito en EntryRouter. Transportes/sesiones sintéticos, no evidencia de Google ni PostgreSQL. Ocho casos PostgreSQL SA1–SA3 externos continúan NOT EXECUTED. No se repitieron suites generales ni se realizaron operaciones comerciales. Reclamo OIDC existente conserva fail-closed ante errores de persistencia: no se añadió un catch que oculte un rollback o un fallo de identidad; una licencia no operable deja el pendiente según el contrato actual.

En la inspección nativa Windows el puerto7243 estaba **sin listener** y había0 clientes de DEV; no se detuvo/reinició/inició ningún host. Configuración privada sin cambios. El React nuevo se prepara en `frontend/dist`; arranque habitual desde PowerShell normal: `Set-Location C:\Proyectos\MediPOS\backend; dotnet run --project src/MediPOS.Api --launch-profile https-local`. No usar overrides históricos que deshabilitan plataforma ni arrancar otra instancia con7243 ocupado. Ctrl+C detiene la instancia de esa consola. Falta HTTP/Google/Chrome de esta versión, no se declara PASS operativo por build.

### S4 — inventario real y NO-GO de limpieza

Lectura real únicamente: PostgreSQL18.4/localhost5432/`medipos_dev`;39 IDs EF coinciden exactamente con fuente;56 FORCE RLS; `medipos_runtime` sin elevación, membresías, propiedad o DDL.62 tablas públicas:49 con `tenant_id`,13 preservadas;130 FK privadas y0 FK entrantes desde las preservadas.3 usuarios,3 boticas/licencias,2 membresías/Owners históricos,1 entidad,2 sedes,1 producto,2 cajas y1 designación inicial;0 compras/ventas/movimientos. No se presume que los datos sean desechables: el propietario debe confirmar los3 negocios y sus historiales como simulación antes de aprobar pérdida.

**Bloqueo demostrado:**13 triggers BEFORE TRUNCATE, incluso con tablas vacías: `notifications`, `notification_read_states`, `sync_devices`, `sync_events`, `offline_sale_conflicts`, `branch_catalog_sync_states`, `branch_catalog_changes`, `supplier_ubl_imports`, `supplier_ubl_import_lines`, `supplier_ubl_purchase_line_links`, `electronic_documents`, `electronic_document_series`, `electronic_document_attempts`. Sus funciones rechazan borrado/truncado; la lista TRUNCATE propuesta anteriormente **no es ejecutable**. No se desactivaron ni se propone eludirlas con `session_replication_role`.

**Tablas candidatas exclusivamente, no autorización SQL:** `audit_logs`, `branch_catalog_changes`, `branch_catalog_sync_states`, `branch_product_stock_thresholds`, `branches`, `business_products`, `cash_sessions`, `cash_transfers`, `commission_entries`, `commission_rules`, `electronic_document_attempts`, `electronic_document_series`, `electronic_documents`, `employee_invitations`, `import_jobs`, `import_row_results`, `initial_owner_invitations`, `inventory_lots`, `legal_entities`, `license_changes`, `licenses`, `membership_branches`, `memberships`, `notification_read_states`, `notifications`, `offline_sale_conflicts`, `platform_tenant_creation_requests`, `product_units`, `purchase_lines`, `purchases`, `sale_lines`, `sale_payment_reversals`, `sale_payments`, `sales`, `stock_movements`, `supplier_ubl_import_lines`, `supplier_ubl_imports`, `supplier_ubl_purchase_line_links`, `suppliers`, `sync_devices`, `sync_events`, `tenant_commission_settings`, `tenant_operation_receipts`, `tenants`, `transfer_events`, `transfer_lines`, `transfer_lot_allocations`, `transfers`, `work_schedules`.

**Preservar:** `users` completos/UUID aprobados, `__EFMigrationsHistory`, `categories`, `global_products`, `medicine_profiles` y las8 tablas oficiales `official_catalog_*`; fuera de BD, allowlist/credenciales/OAuth/DP/TLS. Las39 migraciones no se comprimen ni editan. Evidencia saneada ignorada: `backend/obj/superadmin-v2-validation/dev-inventory.json`. El respaldo existente antes de39 coincide en SHA, se lista y decodifica, pero contiene esquema38 y es anterior a los datos nuevos; **no es un backup fresco de S4 ni un restore ensayado**. No se creó otro respaldo sin aprobación.

| Alternativa | Alcance/condición antes de aprobar | Recuperación y límites |
|---|---|---|
| A — preferida, limpieza selectiva | Conservar esquema/39 migraciones/usuarios/globales; requiere un procedimiento de mantenimiento/purga expresamente revisado que trate los13 guards inmutables. Hoy no existe un reset ordinario seguro; A18 no se implementa aquí. No ejecutar TRUNCATE/DELETE improvisados ni deshabilitar protecciones. | Respaldo fresco de esquema39 y datos, roles/GRANT y exportes privados preservados; validación de recuperación real externa o mediante mantenimiento separado autorizado, sin crear una base local paralela. Transacción revisada, verificaciones y ROLLBACK antes de COMMIT; después, restauración completa específicamente autorizada. |
| B — reconstruir la misma DEV, solo con consentimiento destructivo separado | Reaplicar las39 migraciones oficiales únicamente a la misma BD después de respaldo/restauración probados. Reimportar de forma controlada usuarios con UUID originales y catálogos preservados; verificar allowlist y runtime sin cambiar credenciales. Pérdida de datos explícita, sin nuevos entornos. | Ventana de indisponibilidad y plan completo de restauración de la misma DEV, con hashes/verificación de identidad/esquema/GRANT. No es un baseline nuevo ni una aprobación para DROP actual. |

**Autorización siguiente, todavía NO solicitada como ejecutable:** elegir A o B solo después de revisar/probar su mecanismo, confirmar datos a perder y aprobar respaldo fresco/recuperación/destino exactos. Hasta entonces: S4 BLOCKED, cero borrados. Se puede aceptar S1–S3 conservando los negocios actuales, sin esperar la limpieza.
