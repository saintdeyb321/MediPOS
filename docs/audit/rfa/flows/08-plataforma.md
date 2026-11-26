# Plataforma

Seis áreas en `PlatformPage.tsx:24–32`. Allowlist privada de UUID y sesión independientes de Owner. `Program.cs:28–31,55–57` compone flags. `PlatformDirectoryEndpoints.cs:52–63` exige autoridad, tipo de rate lease y validación de consulta; UI distingue 429, denegación y conflicto. TO-BE PENDIENTE_RFA3.



## UC-PLA-01

GET `/platform/catalog/categories` · `backend/src/MediPOS.Api/Platform/PlatformDirectoryEndpoints.cs:13`. **E0 · IMPLEMENTADO · NOT_RUN**. Entrar a Catálogo global y listar categorías; límite validado en PlatformDirectoryEndpoints.cs:13–21. Store por revisar.

## UC-PLA-02

GET `/platform/session` · `backend/src/MediPOS.Api/Platform/PlatformDirectoryEndpoints.cs:22`. **E1 · IMPLEMENTADO · NOT_RUN**. Entrar a plataforma o resolver destino al login. /platform/session exige autoridad (PlatformDirectoryEndpoints.cs:22,52–63), 404 si no se mapea, distinto de denegación. Consulta de admisión sin escritura.

## UC-PLA-03

GET `/platform/tenants` · `backend/src/MediPOS.Api/Platform/PlatformDirectoryEndpoints.cs:24`. **E0 · IMPLEMENTADO · NOT_RUN**. Entrar a Boticas/Propietarios/Licencias y paginar. usePlatformConsole.ts:42 y PlatformDirectoryEndpoints.cs:24–32. Scope SQL por completar.

## UC-PLA-04

GET `/platform/tenants/{tenantId:guid}/details` · `backend/src/MediPOS.Api/Platform/PlatformDirectoryEndpoints.cs:33`. **E0 · IMPLEMENTADO · NOT_RUN**. Abrir ficha de la botica seleccionada. usePlatformConsole.ts:43–44 encadena detalle e invitación solo por contexto. Join de licencias/tenants pendiente.

## UC-PLA-05

GET `/platform/identities/{userId}` · `backend/src/MediPOS.Api/Platform/PlatformDirectoryEndpoints.cs:39`. **E0 · NO_EXPUESTO · NOT_RUN**. Consulta de identidad interna para soporte heredado; mirar consumidores y no asumir requisito del alta moderna. PlatformDirectoryEndpoints.cs:39–43. Proyección privada pendiente.

## UC-PLA-06

GET `/platform/tenant-requests/{requestKey}` · `backend/src/MediPOS.Api/Platform/PlatformDirectoryEndpoints.cs:43`. **E0 · IMPLEMENTADO · NOT_RUN**. Consultar constancia cuando se conserva creación pendiente. usePlatformConsole.ts:152–173 verifica IDs y confirma antes de refrescar lista. PlatformDirectoryQueries.cs:53–56 revalida autoridad; store por completar.

## UC-PLA-07

GET `/platform/tenants/{tenantId}/initial-owner` · `backend/src/MediPOS.Api/Platform/PlatformInitialOwnerEndpoints.cs:12`. **E0 · IMPLEMENTADO · NOT_RUN**. Abrir ficha y leer asignación. PlatformInitialOwnerEndpoints.cs:12 y usePlatformConsole.ts:44. Completar store y estado terminal.

## UC-PLA-08

POST `/platform/tenants/{tenantId}/initial-owner/change` · `backend/src/MediPOS.Api/Platform/PlatformInitialOwnerEndpoints.cs:15`. **E1 · IMPLEMENTADO · NOT_RUN**. Corregir Gmail pendiente usando expectedInvitationId. InitialOwnerInvitations.cs:50–74 valida actor y expectedInvitationId antes de revocar y crear la nueva designación en el mismo scope. InitialOwnerInvitationStore.cs:25–40 audita ambas acciones; la UI refresca la asignación y conserva la intención si no hay respuesta.

## UC-PLA-09

POST `/platform/tenants/{tenantId}/initial-owner/revoke` · `backend/src/MediPOS.Api/Platform/PlatformInitialOwnerEndpoints.cs:17`. **E1 · IMPLEMENTADO · NOT_RUN**. Revocar Gmail pendiente desde modal. InitialOwnerInvitations.cs:50–74 ejecuta la rama sin reemplazo con el mismo identificador/version vigente, terminal revocado. InitialOwnerInvitationStore.cs:31–40 guarda historial auditado; UI vuelve a consultar la asignación.

## UC-PLA-10

POST `/platform/tenants/{tenantId}/licenses/{licenseId}/renew` · `backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs:35`. **E0 · IMPLEMENTADO · NOT_RUN**. Renovar con vencimiento seleccionado; usePlatformConsole.ts:184–200 actualiza licencia/acuses y directorio. Falta revisar invariantes de dominio y store.

## UC-PLA-11

POST `/platform/tenants/{tenantId}/licenses/{licenseId}/suspend` · `backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs:41`. **E0 · IMPLEMENTADO · NOT_RUN**. Confirmar suspensión en modal y volver a ficha/lista. Cambio no equivale a purga. Persistencia y efecto de admisión pendiente.

## UC-PLA-12

POST `/platform/tenants/{tenantId}/licenses/{licenseId}/reactivate` · `backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs:44`. **E0 · IMPLEMENTADO · NOT_RUN**. Reactivar con estado permitido y reconsultar. Controles de dominio y store pendientes.

## UC-PLA-13

POST `/platform/tenants` · `backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs:47`. **E0 · NO_EXPUESTO · NOT_RUN**. Alta histórica sin correo desde API. PlatformLicenseEndpoints.cs:81–99 comparte orquestador; formulario vigente busca with-owner. Compatibilidad a revisar.

## UC-PLA-14

POST `/platform/tenants/with-owner` · `backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs:48`. **E1 · IMPLEMENTADO · NOT_RUN**. Nueva botica → nombre/Gmail/cupo → confirma trial. NewTenantDialog.tsx fija 30 días; API exige clave (PlatformLicenseEndpoints.cs:82–99). usePlatformConsole.ts:152–173 conserva clave y consulta constancia. CreateTenantIdempotentlyHandler.cs:22,47–54 valida plataforma, reserva por actor/clave/hash, crea tenant/licencia y añade Gmail a la misma operación antes de completar reserva. Acuse comparado por usePlatformConsole.ts:152–173; un fallo al refrescar lista no es rechazo del alta.

## UC-PLA-15

POST `/platform/tenants/{tenantId}/bootstrap-owner` · `backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs:51`. **E0 · NO_EXPUESTO · NOT_RUN**. Bootstrap histórico por UUID en PlatformLicenseEndpoints.cs:51. UI actual delega primer Owner a with-owner/reclamo y no llama a este endpoint. Flujo histórico exacto por inspeccionar.

## UC-PLA-16

POST `/platform/catalog/categories` · `backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs:52`. **E0 · IMPLEMENTADO · NOT_RUN**. Nueva categoría, validar directorio completo y no duplicar. usePlatformConsole.ts:131–145 cambia caché tras respuesta; CreateCategoryHandler enlazado en PlatformLicenseEndpoints.cs:52. Store por cerrar.

## UC-PLA-17

Navegar las seis áreas administrativas. **E1 · IMPLEMENTADO · NOT_RUN**. Navegar Inicio/Boticas/Propietarios/Licencias/Catálogo/Mi cuenta. PlatformPage.tsx:24–32,50–105 conserva coordinador; usePlatformConsole.ts:42–53 cancela consultas fuera de área. Pendientes mantienen URL.

## UC-PLA-18

Solicitar purga e inspeccionar retención. **E0 · NO_EXPUESTO · NOT_RUN**. RequestTenantPurgeHandler está registrado (DependencyInjection.cs:164), pero no se encontró ruta pública de purga en el inventario Platform. Cronograma y persistencia pendientes; sin atribuir borrado físico al estado.


## Fuentes

- [PlatformPage.tsx](../../../../frontend/src/features/platform/PlatformPage.tsx)
- [PlatformDirectoryEndpoints.cs](../../../../backend/src/MediPOS.Api/Platform/PlatformDirectoryEndpoints.cs)
- [usePlatformConsole.ts](../../../../frontend/src/features/platform/usePlatformConsole.ts)
- [PlatformDirectoryQueries.cs](../../../../backend/src/MediPOS.Application/Modules/TenancyLicensing/PlatformDirectoryQueries.cs)
- [PlatformInitialOwnerEndpoints.cs](../../../../backend/src/MediPOS.Api/Platform/PlatformInitialOwnerEndpoints.cs)
- [PlatformLicenseEndpoints.cs](../../../../backend/src/MediPOS.Api/Platform/PlatformLicenseEndpoints.cs)
- [CreateTenantIdempotentlyHandler.cs](../../../../backend/src/MediPOS.Application/Modules/TenancyLicensing/CreateTenant/CreateTenantIdempotentlyHandler.cs)
- [DependencyInjection.cs](../../../../backend/src/MediPOS.Infrastructure/DependencyInjection.cs)
- [Prueba relacionada de la familia](../../../../frontend/src/features/platform/PlatformPage.test.tsx) · E0 de ejecución, se inventaría por contexto; no atribuir cobertura de todas las variantes.


- [InitialOwnerInvitations.cs](../../../../backend/src/MediPOS.Application/Modules/IdentityAccess/Invitations/InitialOwnerInvitations.cs)
- [InitialOwnerInvitationStore.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/IdentityAccess/Persistence/InitialOwnerInvitationStore.cs)

