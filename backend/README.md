# MediPOS backend

La solución `MediPOS.sln` usa .NET 10, ASP.NET Core, EF Core, Npgsql y PostgreSQL 18. Consulta el [estado del MVP](../docs/CURRENT_STATE.md) antes de planificar cambios.

## Arrancar DEV

Desde `backend/`, con `frontend/dist` compilado:

```powershell
dotnet build MediPOS.sln
dotnet run --project src/MediPOS.Api --launch-profile https-local
```

Usa el PostgreSQL DEV existente en loopback 5432, `medipos_dev` y el rol restringido `medipos_runtime`. Mantén la conexión, Google OIDC, Data Protection y la allowlist en configuración privada. El perfil sirve la SPA y la API en [HTTPS localhost:7243](https://localhost:7243/) con el certificado local confiable. Verifica que el puerto esté libre antes de iniciar otra instancia. `/health/ready` comprueba conectividad del runtime. Si Plataforma está habilitada, `/platform/session` anónimo devuelve 401.

## Validar cambios

Para un cambio funcional, ejecuta build y pruebas focalizadas (`dotnet test tests/MediPOS.UnitTests --no-build --filter "FullyQualifiedName~Nombre"`). Ejecuta las pruebas de arquitectura cuando cambien dependencias y EF offline cuando cambie el modelo. Usa PostgreSQL real con datos de pruebas y destino aislado autorizado para persistencia, RLS y concurrencia. Las comprobaciones de DEV son de solo lectura salvo autorización expresa.

Contratos HTTP en `src/MediPOS.Api` y OpenAPI de Development. Para recuperación, consulta el [runbook](../docs/BACKUP_RECOVERY_RUNBOOK.md) y el [procedimiento S4 de DEV](../docs/MEDIPOS_AUDITORIA_SUPERADMIN_V2_PLAN_CIERRE.md#9-correctivos-acotados-y-procedimiento-s4-preparado-base-e61cf352-2026-10-30). La [referencia histórica del backend](README.history.md) conserva matrices de API, procedimientos anteriores y resultados fechados.
