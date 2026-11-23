> **Registro histórico de Superadmin V2.** La sección 9 conserva el procedimiento S4 sujeto a aprobación. El estado vigente se mantiene en [CURRENT_STATE.md](CURRENT_STATE.md).

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

**Estado histórico de esta inspección:** cero borrados; el mecanismo propuesto posteriormente está en §9. Se puede aceptar S1–S3 conservando los negocios actuales, sin esperar la limpieza.

## 9. Correctivos acotados y procedimiento S4 preparado (base e61cf352, 2026-10-30)

**S1–S2 CODE_READY, sin instalar:** EntryRouter conserva el error404 (incluido cuerpo vacío) de `/platform/session` y ofrece «Continuar a mis boticas» únicamente por decisión explícita; no convierte404 en autorización ni oculta fallos429/403/red. Tras una constancia válida, `usePlatformConsole` separa el fallo del refresco del resultado de escritura: cierra/limpia el asistente, informa «Alta confirmada» y permite reconsultar sin otro POST. La pérdida real de respuesta/constancia conserva la recuperación original.31 casos focalizados en EntryRouter/PlatformPage, ESLint de los4 archivos y build TS/Vite PASS; transportes de prueba, no aceptación Chrome/Google/PG. Build aislado en `frontend/node_modules/.cache/superadmin-s4-fix-dist`; no reemplaza el frontend servido ni reinicia DEV.

### Guards auditados y solución de mantenimiento

Los13 guards privados son BEFORE TRUNCATE de sentencia: disparan incluso sin filas. No contienen un modo autorizado de mantenimiento. Sus cuerpos y migraciones quedan intactos:

| Triggers (tabla) | Función / fuente Infrastructure | Rechazo |
|---|---|---|
| `notifications_no_truncate` (notifications), `notification_reads_no_truncate` (notification_read_states) | `protect_notification_history`; Notifications/Persistence/NotificationHistorySql.cs |42501, historial inmutable |
| `sync_devices_no_truncate` (sync_devices), `sync_events_no_truncate` (sync_events) | `protect_sync_device`, `protect_sync_event`; OfflineSync/Persistence/SyncHistorySql.cs |42501, retención de dispositivos/eventos |
| `offline_conflicts_no_truncate` (offline_sale_conflicts) | `protect_offline_conflict`; OfflineSync/Persistence/OfflineSaleReconciliationSql.cs |42501, resolución histórica única |
| `branch_catalog_states_no_truncate` (branch_catalog_sync_states), `branch_catalog_changes_no_truncate` (branch_catalog_changes) | `protect_branch_catalog_sync`; OfflineSync/Persistence/BranchCatalogSyncSql.cs |42501; solo triggers de origen mantienen proyecciones |
| `ubl_document_no_delete`, `ubl_line_no_delete`, `ubl_link_immutable` (supplier_ubl_imports, supplier_ubl_import_lines, supplier_ubl_purchase_line_links) | `supplier_ubl_append_only`; Purchasing/Ubl/SupplierUblSql.cs |23514, documentos/enlaces append-only |
| `cpe_document_no_delete`, `cpe_series_no_delete`, `cpe_attempt_immutable` (electronic_documents, electronic_document_series, electronic_document_attempts) | `cpe_no_delete`; ElectronicDocuments/ElectronicDocumentSql.cs |23514, documentos/series/intentos retenidos |

**Propuesta elegida: reconstrucción lógica transaccional de objetos en la misma `medipos_dev`, desde un respaldo fresco39, omitiendo solo los49 TABLE DATA privados.** `pg_restore --clean --if-exists --file` genera SQL sin conectarse; una lista TOC conserva íntegros esquema, funciones, índices, restricciones, triggers, políticas/ACL y los13 TABLE DATA preservados. PostgreSQL documenta `--clean` y la selección `--use-list` en [pg_restore18](https://www.postgresql.org/docs/18/app-pgrestore.html). No es TRUNCATE ni una excepción permanente a los guards. Tampoco es A18/purga de producción.

**Impacto destructivo explícito:** se eliminarían/recrearían objetos del esquema durante la transacción, incluidos los que contienen datos preservados; al commit sus definiciones/datos verificados vuelven desde el mismo archivo. No se elimina/recrea la base, no se ejecuta EF ni se cambia el historial de39 IDs. Los49 conjuntos privados de §8/`tools/dev-reset/policy.json` quedan vacíos: boticas, licencias/cambios, Owners/membresías/invitaciones, entidades/sedes, productos locales/presentaciones, compras/proveedores, stock/lotes/ledger, ventas/pagos/tickets derivados, cajas/transferencias, comisiones, auditoría privada, UBL/CPE, constancias/idempotencia, importaciones privadas, notificaciones y offline. Se perderían también sus historiales y posibilidades de conciliación: confirmar expresamente que TODO es simulación; vaciar colas/carritos pendientes de los navegadores antes de reabrir, sin reproducir intenciones antiguas.

Los13 conjuntos preservados son `users` completos (subject/correo verificado/UUID), `__EFMigrationsHistory`, `categories`, `global_products`, `medicine_profiles` y los8 `official_catalog_*` enumerados en policy. Sus hashes incluyen todas las columnas, sin excluir `google_email_verified`. La allowlist/UUID administrativo, User Secrets/OAuth, credenciales, TLS y keyring/DP permanecen fuera de BD y no se cambian. Se cotejan39 IDs,56 FORCE RLS, guards habilitados, esquema/ACL/propietarios/roles y cada tabla preservada; runtime nunca recibe autoridad de mantenimiento. Los guards adicionales del catálogo global también se recrean con su definición exacta.

### Gates y recuperación, antes de aprobar pérdida

- Herramienta preparada: `backend/tools/New-DevS4RestorePlan.ps1`, plantillas `tools/dev-reset/{policy.json,preflight.sql,capture.sql,verify.sql}`. No contiene llamada a psql ni ejecución SQL: genera archivos privados desde pg_restore y exige SHA de archivo/raíz física no redirigida/ACL usuario+SYSTEM/ruta nueva. `-SelfTest`:8 casos offline TOC/plantillas PASS en PowerShell5.1, sin conexiones, herramientas nativas ni escrituras. Parser PS5.1 PASS. No se generó un plan privado ni se ejecutó restauración.
- Antes del SQL: detener solo DEV desde su consola, cerrar todos los clientes de esa BD, congelar altas/mutaciones/login; verificar loopback5432, `medipos_dev`, postgres temporal por pgpass, versión18,39 IDs exactos,62 tablas exactas,13 guards privados,56 FORCE RLS/runtime restringido,0 dependencias entrantes desde preservadas. El script toma advisory lock y ACCESS EXCLUSIVE sobre las62 tablas. Detiene ante cualquier discrepancia, sin CASCADE improvisado.
- Ensayo obligatorio **en la misma base y una sola transacción**: fotografía completa → restauración de TODO el archivo → comparar todas las filas/esquema/permisos → ROLLBACK → volver a comparar la base original. Solo entonces exporta `approved-baseline.csv` privado. No basta listar/decodificar el archivo. El SQL incluye DROP de objetos aunque termine en rollback: requiere autorización específica incluso para el ensayo. No existe prueba de recuperación ejecutada todavía.
- Se rechazan secuencias/setval, LOB, objetos foráneos/materializados, replicación/publicaciones/suscripciones, event triggers, esquemas/extensiones no revisados o drift. Las secuencias no se revierten transaccionalmente según [PostgreSQL18](https://www.postgresql.org/docs/18/functions-sequence.html); por eso no se presupone que cualquier dump admita este ensayo. Ante estos casos, NO-GO, no cambiar el método silenciosamente.
- Aplicación: exige baseline del ensayo, mismo cluster/base/OID/versión/roles y estado original exacto, además de autorización `RESET_MEDIPOS_DEV_39`; inserta únicamente los13 conjuntos preservados y confirma después de comparar. Un fallo anterior a COMMIT revierte por transacción/cierre de conexión. Una respuesta incierta de COMMIT exige `inspect.psql`: distingue estado original, comercial vacío o drift; no repetir reset a ciegas.
- Recuperación después de commit: con DEV aún detenido y **otra autorización expresa**, `rollback.psql` restaura TODO el archivo39 en esa misma base y solo confirma si coincide con el baseline. El gate conserva cluster/esquema/roles y exige archivo/manifiesto intactos; drift de esquema/configuración = detener y revisar. Si se reanudaron ventas, restaurar el archivo perdería esas nuevas escrituras: no usarlo sin un nuevo alcance aprobado. Conservar archivo/plan/logs/secretos originales, nunca ejecutar Down ni resetear credenciales.
- El `RecoveryWorkflow` existente conserva su guard de cluster aislado para restauraciones operativas; no se le pasa DEV disfrazada de destino ni se debilita. Este procedimiento DEV se verifica por separado. No crea QA/staging/otra BD ni una función persistente de bypass.

### Comandos futuros exactos — NO autorizados ni ejecutados

Ejecutar desde **Windows PowerShell5.1 normal**, después de aprobar respaldo privado fresco, ventana DEV y ensayo con DROP/ROLLBACK sobre la misma base. No desde AppData virtualizado de Codex. Nunca mostrar SQL generado, logs, datos, contraseñas o hashes de identidad por chat. El cierre de DEV es Ctrl+C en su consola; si7243 está ocupado por una instancia desconocida, detener este procedimiento, no matar procesos por nombre.

```powershell
Set-Location C:\Proyectos\MediPOS
$ErrorActionPreference='Stop'
if(Get-NetTCPConnection -LocalPort 7243 -State Listen -ErrorAction SilentlyContinue){throw 'DEV sigue activo'}
$pg='C:\Program Files\PostgreSQL\18\bin'
$root=Join-Path $env:APPDATA ('MediPOS\DevAdministration\maintenance-s4-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
if(Test-Path -LiteralPath $root){throw 'Destino existente: no sobrescribir'}
$null=New-Item -ItemType Directory -Path $root
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
$acl.SetOwner($sid)
foreach($identity in @($sid,(New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))){
  $rule=New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
  $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $root -AclObject $acl
# pgpass externo ya configurado; -w nunca pide/imprime contraseña.
$backup=Join-Path $root 'medipos_dev-schema39.dump'
& "$pg\pg_dump.exe" -w -h localhost -p 5432 -U postgres -d medipos_dev --format=custom --no-statistics --lock-wait-timeout=5s --file $backup 2> "$root\dump.private.log"
if($LASTEXITCODE -ne 0){throw 'Backup fallido: conservar y detener'}
$sha=(Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$root\archive.sha256",$sha)
$secretId=(Select-Xml -Path backend/src/MediPOS.Api/MediPOS.Api.csproj -XPath '//UserSecretsId').Node.InnerText
$secret=Join-Path $env:APPDATA ('Microsoft\UserSecrets\'+$secretId+'\secrets.json')
Copy-Item -LiteralPath $secret -Destination "$root\secrets.original.private.json"
$settingsSha=(Get-FileHash -LiteralPath $secret -Algorithm SHA256).Hash
if($settingsSha -cne (Get-FileHash "$root\secrets.original.private.json").Hash){throw 'Configuración no coincide'}
$plan=Join-Path $root 'plan'
& .\backend\tools\New-DevS4RestorePlan.ps1 -Archive $backup -ExpectedArchiveSha256 $sha -PrivateDirectory $plan
if(-not $? -or -not(Test-Path "$plan\manifest.json")){throw 'Plan incompleto: no ejecutar SQL'}
$manifestSha=(Get-FileHash "$plan\manifest.json" -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$root\plan.sha256",$manifestSha)
```

El generador lista y decodifica completamente el archivo a SQL privado (`all.toc`, `full-restore.sql`); verifica62 entradas TABLE DATA y omite49 en `reset.toc`. No basta que pg_dump termine: la restauración ensayada debe pasar. La carpeta privada contiene datos personales/comerciales, no subirla al repositorio. Si se interrumpe el aprovisionamiento del plan, conservarla para diagnóstico y detener; no ejecutar archivos parciales.

En la **misma consola** (o recuperar `$root` como la ruta privada exacta ya registrada, nunca seleccionar automáticamente «la más reciente»), comprobar integridad **antes de cada invocación**:

```powershell
function Test-S4Files {
  if((Get-FileHash -LiteralPath $secret).Hash -cne $settingsSha -or (Get-FileHash "$root\secrets.original.private.json").Hash -cne $settingsSha){throw 'Configuración o respaldo privado cambió'}
  if((Get-FileHash "$plan\manifest.json" -Algorithm SHA256).Hash -cne $manifestSha){throw 'Manifiesto modificado'}
  $m=Get-Content "$plan\manifest.json" -Raw|ConvertFrom-Json
  if($m.State -cne 'PREPARED_NOT_EXECUTED' -or $m.Database -cne 'medipos_dev' -or $m.Port -ne 5432 -or $m.Migrations -ne 39 -or $m.ArchiveSha256 -cne $sha){throw 'Manifiesto distinto'}
  if((Get-FileHash $backup -Algorithm SHA256).Hash -cne $sha){throw 'Backup distinto'}
  foreach($file in $m.Files){
    if($file.Name -notmatch '^[A-Za-z0-9_.-]+$'){throw 'Ruta inesperada'}
    $path=Join-Path $plan $file.Name
    if((Get-Item -LiteralPath $path).Length -ne $file.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $file.Sha256){throw 'Artefacto distinto'}
  }
}
$connection=@('-X','-w','-h','localhost','-p','5432','-U','postgres','-d','medipos_dev','-v','ON_ERROR_STOP=1')
Test-S4Files
& "$pg\psql.exe" @connection -f "$plan\rehearse.psql" *> "$root\rehearse.private.log"
if($LASTEXITCODE -ne 0 -or -not(Test-Path "$plan\approved-baseline.csv") -or -not(Select-String -LiteralPath "$root\rehearse.private.log" -SimpleMatch 'S4_REHEARSAL_PASS_ROLLBACK_VERIFIED' -Quiet)){throw 'Ensayo/rollback NO-GO; no limpiar'}
$baselineSha=(Get-FileHash "$plan\approved-baseline.csv" -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$root\baseline.sha256",$baselineSha)
```

**Punto de consentimiento separado:** revisar resultado real del ensayo y aprobar pérdida de las49 tablas de simulación. Mantener DEV detenido, preservar hash de `approved-baseline.csv` y cotejarlo antes de las invocaciones posteriores. Solo tras esa autorización:

```powershell
Test-S4Files
if((Get-FileHash "$plan\approved-baseline.csv").Hash -cne $baselineSha){throw 'Baseline distinto'}
& "$pg\psql.exe" @connection -v approved_action=RESET_MEDIPOS_DEV_39 -f "$plan\reset.psql" *> "$root\reset.private.log"
# Incluso ante respuesta incierta: inspeccionar primero, no reenviar reset.
Test-S4Files
if((Get-FileHash "$plan\approved-baseline.csv").Hash -cne $baselineSha){throw 'Baseline distinto'}
& "$pg\psql.exe" @connection -f "$plan\inspect.psql" *> "$root\inspect.private.log"
if($LASTEXITCODE -ne 0 -or -not(Select-String -LiteralPath "$root\inspect.private.log" -SimpleMatch 'S4_STATE_EMPTY_COMMERCIAL' -Quiet)){throw 'Estado distinto: no arrancar; revisar logs privados'}
```

El reset debe terminar con `S4_RESET_PASS` y la inspección con `S4_STATE_EMPTY_COMMERCIAL`; `FULL_SOURCE` significa que la limpieza no quedó aplicada. Los mensajes se consultan localmente sin publicar logs. Después de cotejar configuración privada original,39/RLS/permisos/usuarios/globales y sin errores, arrancar con `Set-Location C:\Proyectos\MediPOS\backend; dotnet run --project src/MediPOS.Api --launch-profile https-local`, verificar readiness y nuevo login en perfiles separados. No cambiar flags/allowlist ni usar administrador SQL como runtime. Rehabilitar simulaciones únicamente tras lectura real del estado esperado.

**Rollback completo, únicamente si se autoriza posteriormente y aún no hay nuevas operaciones:**

```powershell
Test-S4Files
if((Get-FileHash "$plan\approved-baseline.csv").Hash -cne $baselineSha){throw 'Baseline distinto'}
& "$pg\psql.exe" @connection -v approved_action=RESTORE_MEDIPOS_DEV_39 -f "$plan\rollback.psql" *> "$root\rollback.private.log"
if($LASTEXITCODE -ne 0){throw 'Restauración no confirmada: mantener DEV detenido'}
# Debe observarse S4_ROLLBACK_PASS y luego S4_STATE_FULL_SOURCE.
Test-S4Files
& "$pg\psql.exe" @connection -f "$plan\inspect.psql" *> "$root\inspect-after-rollback.private.log"
if($LASTEXITCODE -ne 0 -or -not(Select-String -LiteralPath "$root\inspect-after-rollback.private.log" -SimpleMatch 'S4_STATE_FULL_SOURCE' -Quiet)){throw 'Recuperación NO-GO'}
```

**Bloqueos reales actuales:** no autorización de mantenimiento destructivo, backup39 fresco ni recuperación ensayada; compatibilidad del SQL nativo con ese respaldo aún no ejecutada. Plan preparado/static checks no equivalen a restauración PASS. S4 **PREPARED / NO-GO para ejecución**, sin datos/configuración/permisos cambiados, sin servicios reiniciados. Los dos Gmail/Chrome y pruebas externas SA1–SA3 conservan sus pendientes anteriores.

**Revisión operativa del 2 de noviembre de 2026.** DEV 18.4 tiene 39 migraciones, 62 tablas, 56 FORCE RLS, 13 guards privados y runtime restringido. Una consulta de solo lectura confirmó tres usuarios, el UUID aprobado con correo Google verificado, una categoría y tres boticas. Las vistas, tablas particionadas, secuencias y objetos de replicación inventariados están ausentes. La decodificación offline del respaldo histórico 38 mostró 61 TABLE DATA, DROP/CREATE de `unaccent` y `pg_trgm` sin CASCADE y triggers creados después de COPY. El archivo 39 y su recuperación siguen pendientes.

El generador admite únicamente los tipos de entrada TOC revisados y conserva los límites de espera locales. La comprobación con SQL nativo del respaldo histórico verificó que solo cambiaron ocho bytes de su preámbulo. El preflight exige que la versión predeterminada de cada extensión coincida con la instalada. La captura incluye privilegios efectivos predeterminados, propietarios y otorgantes. Si se pierde la respuesta de COMMIT, ejecutar únicamente `inspect.psql`. El rollback exige que los 49 conjuntos sigan vacíos y que los 13 preservados coincidan con el ensayo. Las entradas operativas son los archivos `.psql` de §9. Los dos `*-restore.sql` contienen DDL para inclusión dentro de sus transacciones.

- **A. Pendiente de autorización.** Detener únicamente DEV 7243, congelar operaciones, respaldar privadamente `medipos_dev` y su configuración según §9, comprobar SHA/TOC, ensayar la restauración completa en la misma base y exigir igualdad de filas, esquema y permisos antes y después del ROLLBACK. Conservar evidencia privada. Al finalizar, reabrir DEV con los datos originales. Cualquier escritura posterior al ensayo exige un nuevo respaldo y validación antes de B.
- **B. Pendiente de una segunda autorización y del PASS real de A.** Confirmar expresamente la pérdida de los datos de simulación de las 49 tablas, incluidas las tres boticas y su historial. Verificar de nuevo el baseline bajo bloqueo, preservar las 13 tablas, la identidad aprobada, RLS y permisos. Reanudar simulaciones solo con `S4_STATE_EMPTY_COMMERCIAL` verificado.
