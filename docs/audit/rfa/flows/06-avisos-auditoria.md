# Avisos/auditoría

La bandeja muestra lectura y riesgo como estados distintos. La auditoría expone metadatos y separa UI comercial de datos sensibles. TO-BE PENDIENTE_RFA3.



## UC-AVI-01

GET `/api/tenants/{tenantId}/audit` · `backend/src/MediPOS.Api/Tenant/AuditEndpoints.cs:7`. **E0 · IMPLEMENTADO · NOT_RUN**. Filtrar periodo y abrir detalle. AuditPage.tsx renderiza acción/entidad/fecha y metadatos; GetAuditHistoryHandler.cs:7–25 valida autorización, rango y proyección sin before/after en el DTO. EF/RLS de consulta queda pendiente.

## UC-AVI-02

GET `/api/tenants/{tenantId}/notifications` · `backend/src/MediPOS.Api/Tenant/NotificationEndpoints.cs:9`. **E1 · IMPLEMENTADO · NOT_RUN**. Abrir avisos y filtrar Todas/No leídas y sede permitida. NotificationsPage.tsx:33–35 consulta lista separada del contador. NotificationHandlers.cs:9–33 reevalúa acceso; NotificationStore.cs:11–21 une notas, lecturas, lotes y productos bajo scope.

## UC-AVI-03

GET `/api/tenants/{tenantId}/notifications/unread-count` · `backend/src/MediPOS.Api/Tenant/NotificationEndpoints.cs:12`. **E1 · IMPLEMENTADO · NOT_RUN**. Observar contador del contexto. Hooks comparten notification-count con misma sesión, membresía y sede; NotificationHandlers.cs:40–43 y NotificationStore.cs:24–28 cuentan no leídas y devuelven entero exacto.

## UC-AVI-04

POST `/api/tenants/{tenantId}/notifications/{notificationId}/read` · `backend/src/MediPOS.Api/Tenant/NotificationEndpoints.cs:15`. **E1 · IMPLEMENTADO · NOT_RUN**. Marcar una alerta individual. NotificationsPage.tsx:35–52 guarda marcador de intención, invalida tras acuse y mantiene pendiente ante pérdida. NotificationHandlers.cs:53–66 valida sede original; NotificationStore.cs:35–47 escribe lectura de membresía y devuelve fila deduplicada.

## UC-AVI-05

Abrir detalle de aviso. **E1 · IMPLEMENTADO · NOT_RUN**. Abrir drawer desde una fila y leer lote, mensaje y riesgo. NotificationsPage.tsx presenta los campos de la respuesta previa; no hay escritura por abrir y el estado accionable no se deduce de leído.

## UC-AVI-06

Generar alertas periódicas de vencimiento. **E0 · IMPLEMENTADO · NOT_RUN**. Scheduler habilitado por composición de DependencyInjection.cs:154 ejecuta job de vencimiento; ExpirationAlertsScheduler.cs:21 despacha al handler. Configuración, detección/deduplicación y SQL por completar.

## UC-AVI-07

Recuperar lectura ambigua. **E1 · IMPLEMENTADO · NOT_RUN**. Volver a Todas, reconsultar y conciliar lectura observada. NotificationsPage.tsx guarda notificationRead en URL; ausencia de fila no acredita resultado y solicitud pendiente bloquea reenvío.


## Fuentes

- [AuditEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/AuditEndpoints.cs)
- [GetAuditHistoryHandler.cs](../../../../backend/src/MediPOS.Application/Modules/AuditSupport/GetAuditHistoryHandler.cs)
- [NotificationEndpoints.cs](../../../../backend/src/MediPOS.Api/Tenant/NotificationEndpoints.cs)
- [NotificationsPage.tsx](../../../../frontend/src/features/notifications/NotificationsPage.tsx)
- [NotificationHandlers.cs](../../../../backend/src/MediPOS.Application/Modules/Notifications/NotificationHandlers.cs)
- [NotificationStore.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Notifications/Persistence/NotificationStore.cs)
- [DependencyInjection.cs](../../../../backend/src/MediPOS.Infrastructure/DependencyInjection.cs)
- [ExpirationAlertsScheduler.cs](../../../../backend/src/MediPOS.Infrastructure/Modules/Notifications/ExpirationAlertsScheduler.cs)
- [Prueba relacionada de la familia](../../../../backend/tests/MediPOS.UnitTests/Api/TenantNotificationHttpTests.cs) · E0 de ejecución, se inventaría por contexto; no atribuir cobertura de todas las variantes.
