# MediPOS — Backup, restore y ensayo de migraciones (B8.3)

Estado: implementación local; **ningún backup, restore ni recuperación productiva ha sido validado con PostgreSQL real en esta máquina**. Ejecutar este runbook en un entorno externo aprobado antes de aceptar clientes. B8.2 continúa con ambos objetivos `NOT_MEASURED`; M8 no está completo.

## Política propuesta y responsabilidades

Alternativa portable inicial: backup lógico completo PostgreSQL 18 en formato custom, acompañado de manifiesto y huellas de esquema/datos. Incluye objetos y datos de toda la base operativa; no selecciona tablas ni tenants. Es apropiado para preparar ensayos con el volumen inicial; duración, espacio y carga de la lectura completa deben medirse antes de elegirlo para producción. Un archivo creado o decodificado aún no demuestra restauración funcional.

Objetivos **propuestos, pendientes de aprobación operativa y comercial**: RPO de 24 horas y RTO de 4 horas para incidentes recuperables mediante la copia diaria. No son garantías a clientes. Una copia previa a una migración protege su estado anterior, pero no elimina la pérdida de ventas posteriores al backup. El RTO incluye aprovisionamiento, transferencia, restore, validación, reconciliación y corte controlado; medirlo en el ensayo.

Propuesta: ejecución diaria por el scheduler del hosting/sistema operativo, fuera de ASP.NET; copia adicional antes de migraciones de alto riesgo; 14 diarias, 8 semanales y 6 mensuales. Verificar archivos tras cada ejecución, ensayar restauración al menos mensualmente y antes de cambios de riesgo. Tras cualquier fallo, alertar y mantener la última copia restaurada satisfactoriamente. Aprobar costes, ventanas y plazo de retención antes de habilitar producción.

Responsable: operador de infraestructura designado genera/protege copias y monitoriza fallos; responsable de base de datos revisa migraciones e integridad; responsable de la aplicación verifica flujos y operaciones pendientes; responsable del negocio autoriza RPO/RTO y reapertura. Mantener un suplente y contacto de emergencia fuera de esta documentación. El operador autoriza cada comando, incluso cuando un scheduler externo materializa una política aprobada.

Guardar fuera del servidor y fuera del workspace, en almacenamiento cifrado con acceso restringido, versionado/inmutabilidad o bloqueo de eliminación y una copia en otro dominio de fallo. Claves de cifrado y credenciales se administran separadamente. TLS `verify-full` es obligatorio para conexiones; cifrado del volumen local, carga cifrada al almacén externo, custodia de claves, alertas, scheduler y retención son adaptadores pendientes del hosting. `StorageProtectionApproved` es una atestación operativa: la herramienta no cifra archivos ni prueba la configuración del proveedor. No habilitarla antes de revisar esos controles. Eliminar copias vencidas mediante la política aprobada del almacenamiento y destrucción de claves cuando corresponda; la herramienta no borra backups.

No hay WAL archiving/PITR implementado ni validado. Recuperar a un instante requerirá diseño, almacenamiento y monitorización de WAL/base backups y ensayos propios. Evaluar backups físicos, snapshots administrados y PITR cuando se defina el hosting; conservar pruebas de restauración, no inferirlas del servicio contratado. Referencias: [backup PostgreSQL 18](https://www.postgresql.org/docs/18/backup.html), [pg_dump](https://www.postgresql.org/docs/18/app-pgdump.html), [pg_restore](https://www.postgresql.org/docs/18/app-pgrestore.html).

## Qué se conserva

Incremento de fuente con migración 35 (2026-10-17, aún no aplicado en LAB): la copia completa también incluye `employee_invitations`, sus vínculos, hash del ticket, vigencia, estado e historial de auditoría. La comparación de snapshots verifica sus filas íntegramente; proteger el backup como información privada, nunca mostrar hashes de tickets ni identidades. La sonda runtime sin actor debe ver **cero** invitaciones aunque existan filas del tenant: sus políticas exigen Owner o ticket exacto además del tenant. El resultado identifica `ACTORLESS_DENIAL_ONLY` / `ACTOR_CONTEXT_NOT_EXERCISED` y deja `INVITATION_OWNER_AND_TICKET_RLS` pendiente; esa denegación no certifica lectura Owner, aceptación por ticket ni aislamiento completo. Cualquier fila visible sin actor sigue siendo un error fatal. Usar la herramienta de recuperación compatible con esta versión; ensayo PostgreSQL externo y restauración efectiva siguen pendientes.

La copia completa incluye plataforma, catálogo global y perfiles farmacéuticos, catálogo privado/unidades/precios, staging y publicaciones/historial oficial, tenants, licencias/cambios, entidades legales/sucursales, usuarios/membresías/horarios, proveedores/compras/recepción, lotes/vencimientos, StockMovement, ventas/líneas/pagos/reversiones, caja/traslados, comisiones/reglas/ledger, auditoría, notificaciones, jobs de importación, dispositivos/eventos/conflictos offline, versiones/cursor keys del catálogo sincronizado y documentos electrónicos/intentos/series. El XML original UBL `source_xml` es `bytea` dentro de PostgreSQL y está incluido. También constraints/FKs/índices, funciones, triggers, políticas/FORCE RLS, ACL y extensiones `pg_trgm`/`unaccent`.

El inventario de RLS distingue propiedad privada de referencias globales: `tenants.tenant_id` es la identidad del tenant y `platform_tenant_creation_requests.tenant_id` referencia el resultado técnico de plataforma. Se conservan y verifican ambas tablas en el snapshot completo; no se someten a la sonda de filas privadas por tenant ni se certifica allí su autorización de aplicación. Las demás tablas con `tenant_id`, incluso desconocidas por el modelo, deben conservar RLS/políticas/FORCE RLS. La corrección de este inventario y de la consulta de identidad requiere repetir el gate PostgreSQL externo; no implica un restore aprobado.

Los roles y membresías de cluster, autenticación/HBA, configuración del servidor, certificados, secretos y material de cifrado se aprovisionan y respaldan separadamente. No usar `pg_dumpall --globals-only` con contraseñas como sustituto de ese diseño. Conservar un inventario revisado de roles/privilegios, sin claves. Antes del restore crear los nombres originales referenciados por ACL y políticas, incluyendo `medipos_catalog_importer`, `medipos_catalog_reviewer`, `medipos_catalog_publisher` como NOLOGIN/NOSUPERUSER/NOBYPASSRLS/NOCREATEDB/NOCREATEROLE; conservar permisos por columna del publisher. Mantener cuentas administrativas originales como NOLOGIN cuando solo son referencias históricas. No importar credenciales ni membresías privilegiadas de producción al laboratorio.

Los binarios originales CSV de B6.1 y Excel de importación no se archivan en PostgreSQL: su custodia externa, si se requiere, es independiente. El modelo de CPE guarda snapshots y referencias/checksums de evidencia, no constituye un archivo externo de PDF/CDR/XML de proveedor. Si el despliegue añade blobs, certificados de firma o documentos externos, incluirlos con un procedimiento propio y un punto consistente de recuperación. No se respaldan estaciones/offline queues mediante `pg_dump`: conservarlas y reconciliarlas por idempotencia después del incidente.

La versión 1 de verificación cubre el modelo actual en `public`. Rechaza esquemas adicionales, tablas externas/particionadas, secuencias y large objects: no acepta silenciosamente una huella parcial si cambia el almacenamiento. Los `bytea` actuales sí están cubiertos. Backups históricos deben tener manifiesto compatible y un prefijo conocido de migraciones; si su modelo antecede a las invariantes soportadas, usar un procedimiento/versionado compatible y revisado. No modificar el artefacto ni datos para hacerlo pasar.

## Prerrequisitos externos y límites de autoridad

1. PostgreSQL 18 y clientes `pg_dump`/`pg_restore` 18, .NET 10; binarios confiables fuera del workspace. La herramienta no instala ni descubre servidores. Aprobar el origen del dump: restaurarlo ejecuta SQL del emisor, por lo que un hash no autentica un archivo de origen desconocido.
2. Cuenta de backup dedicada: NOSUPERUSER/NOCREATEDB/NOCREATEROLE, BYPASSRLS únicamente para lectura completa, `pg_read_all_data`, sin propiedad de tablas ni CREATE en `public`. No conceder escrituras comerciales. Conceder EXECUTE en `pg_catalog.pg_control_system()` para identificar el cluster; si el hosting no lo permite, detenerse y diseñar un adaptador fiable de aislamiento antes de restaurar. Registrar por un canal independiente el SHA-256 del identificador del origen en `ExpectedSourceClusterSha256` y configurar externamente `medipos.backup_environment` con `Production`, `Staging` o `RecoveryLab` en ese servidor. El guard comprueba ambos antes de leer datos y rechaza un origen productivo etiquetado como laboratorio. No debilita el rol runtime.
3. Autenticación mediante passfile provisionado fuera del workspace, permisos OS restringidos (Unix 0600; ACL equivalente en Windows), sin comodines de host/base/usuario y sin contraseñas en argumentos. Proveer certificado CA válido; no desactivar validación TLS. Rotación y respaldo seguro del material son independientes. No poner secretos en JSON ni usar PGPASSWORD; la herramienta rechaza esa variable ambiental y limpia variables PG heredadas de los procesos clientes. [Passfile PostgreSQL](https://www.postgresql.org/docs/18/libpq-pgpass.html).
4. Almacén protegido con espacio para dump, manifiesto, ensayo y crecimiento, salida fuera del workspace; directorios privados y sin links/reparse points. La herramienta rechaza traversal, UNC, streams alternativos y reutilización de run IDs, no aplica ACL del hosting. La autorización/protección de los directorios es requisito previo.
5. Congelar cambios de esquema/roles/ACL durante el snapshot del backup; las operaciones comerciales pueden continuar. Sincronizar relojes UTC del servidor y del host de operaciones. Un backup con invariantes ya rotas se rechaza y se investiga, sin corregir historia automáticamente.

Para restore/ensayo, el administrador aprovisiona **otro cluster** aislado sin ruta de salida a producción, correo, SUNAT/proveedores o sincronización. Revisar además workers/cron y extensiones/red del servidor. No desplegar el host web comercial allí. Solo la herramienta, operador y pruebas autorizadas pueden conectarse. La atestación de red y workers es un prerrequisito humano; ningún flag JSON demuestra por sí solo un firewall.

Configurar externamente `medipos.recovery_environment=isolated-v1` y `medipos.external_effects=disabled` en el servidor aislado. Obtener SHA-256 de la representación decimal UTF-8 de su `pg_control_system().system_identifier`, diferente del cluster origen, y revisar ese valor por un canal de operaciones independiente. La herramienta verifica el identificador real, además del endpoint, TLS, GUC y comentario; no confía solamente en el hostname.

Crear previamente una base **nueva y vacía** desde template0, nombrada exactamente `medipos_recovery_<RunId>` y comentada `MediPOS isolated recovery v1:<RunId>`. Usar encoding, proveedor de collation, collate/ctype/locale y versión de collation iguales a `Snapshot.DatabaseLocale` del manifiesto; el guard los compara antes de restaurar, sin asumir equivalencia entre locales de hosts diferentes. Propietario: cuenta de restore/migración exclusiva de laboratorio, BYPASSRLS/NOSUPERUSER/NOCREATEDB/NOCREATEROLE y sin membresía capaz de adquirir esos privilegios elevados. Tiene autoridad DDL sobre esa base, no sobre bases comerciales. Precrear roles de catálogo para que migraciones no necesiten CREATEROLE; ambas extensiones confiables deben estar disponibles sin preinstalarlas en la base vacía.

Revocar CONNECT de PUBLIC y concederlo solo al propietario y al runtime de laboratorio. Preservar el **nombre del runtime original** para restaurar sus ACL, con una credencial nueva de laboratorio, sin membresías de administración ni catálogo y sin propiedad/CREATE. El propietario necesita EXECUTE en `pg_control_system()`. No haber usuarios comerciales ni otra conexión al comenzar. No se crea ni elimina ninguna base desde la herramienta; no contiene `DROP DATABASE`, `--clean`, `--create`, `--disable-triggers` ni restauración productiva. La preparación administrativa manual se revisa fuera del gate local.

## Configuración y comandos

Ejecutar exclusivamente en el entorno externo aprobado. Desde `backend/`, generar IDs nuevos de 32 caracteres hexadecimales minúsculos para backup, plan y ensayo; no reutilizarlos. El ejemplo siguiente muestra **solo configuración no secreta** y dominios inválidos: sustituirlos tras aprovisionamiento. Crear JSON fuera del workspace (por ejemplo, en `C:\MediPOSOps\config`); no copiarlo al repositorio.

```json
{
  "RunId": "0123456789abcdef0123456789abcdef",
  "Environment": "RecoveryLab",
  "Workspace": "C:\\Proyectos\\MediPOS",
  "OutputRoot": "C:\\MediPOSOps\\protected",
  "PgBinDirectory": "C:\\MediPOSOps\\postgres18\\bin",
  "Passfile": "C:\\MediPOSOps\\secrets\\pgpass",
  "RootCertificate": "C:\\MediPOSOps\\certs\\root.crt",
  "ProtectedLocationReference": "approved_external_store",
  "TechnicalIdentity": "recovery_operator",
  "StorageProtectionApproved": true,
  "SchemaChangeFreezeApproved": true,
  "NetworkIsolationApproved": true,
  "WorkersDisabled": true,
  "ExternalEffectsDisabled": true,
  "Source": { "Host": "source.example.invalid", "Port": 5432, "Database": "medipos", "Username": "backup_reader" },
  "ExpectedSourceClusterSha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
  "Target": { "Host": "lab.example.invalid", "Port": 5432, "Database": "medipos_recovery_0123456789abcdef0123456789abcdef", "Username": "recovery_owner" },
  "Runtime": { "Host": "lab.example.invalid", "Port": 5432, "Database": "medipos_recovery_0123456789abcdef0123456789abcdef", "Username": "original_runtime_role" },
  "IsolatedClusterSha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
}
```

Para una copia diaria, `Environment` y la variable de proceso deben identificar explícitamente `Production` o `Staging`; la confirmación incluye ese ambiente. `Source` es la única conexión abierta por `backup`. Para restore, `RecoveryLab` es obligatorio y `Source` documenta el endpoint/identidad original; no se conecta al origen. La confirmación de restore incluye el **ID del ensayo**, no el del backup. No convertir automáticamente un config de backup en uno de restore.

```powershell
# Operador externo; los archivos de configuración/autenticación ya están provisionados y protegidos.
$env:MEDIPOS_RECOVERY_OPT_IN='EXTERNAL_OPERATIONS_ONLY'
$env:MEDIPOS_ENVIRONMENT='RecoveryLab'
dotnet run -c Release --project tools/MediPOS.Recovery -- backup --config C:\MediPOSOps\config\backup.json --confirm backup:RecoveryLab:0123456789abcdef0123456789abcdef
dotnet run -c Release --project tools/MediPOS.Recovery -- verify-file --config C:\MediPOSOps\config\backup.json --confirm verify-file:RecoveryLab:0123456789abcdef0123456789abcdef --backup C:\MediPOSOps\protected\0123456789abcdef0123456789abcdef
```

Estados escritos sin sobrescritura en `<OutputRoot>/<RunId>`: `started.json`, luego `database.dump`, `created.json` y finalmente `manifest.json`. La reserva permanente impide repetir un ID incluso tras un corte. El manifiesto registra UTC de inicio del snapshot y creación del manifiesto, procedimiento v1, PostgreSQL major y versión numérica exacta, tipo, bytes/SHA-256, ambiente, identidad técnica, referencia lógica protegida, huella del cluster y del esquema, migraciones y huellas/conteos por tabla. Usar el instante del snapshot para evaluar pérdida potencial, no el final de la copia. No incluye nombres de clientes ni conexión/password. Solo aparece `ARCHIVE_CHECKED` tras exit 0 sin diagnósticos y decodificación completa del archivo con `pg_restore` a un stream descartado. `RestoreRehearsal=NOT_EXECUTED` permanece inmutable: el resultado del ensayo es otro archivo enlazado por ID y SHA-256.

El snapshot exportado desde una transacción repeatable-read/read-only mantiene las huellas y `pg_dump --snapshot` en el mismo estado lógico. Las filas se leen ordenadas, incluyendo texto original, JSON y binarios, y se resumen con SHA-256 de UTF-8 con prefijo de longitud; no se guardan datos de filas en el manifiesto. La lectura/ordenación completa cuesta CPU/IO y puede retener versiones antiguas: medir ventana/espacio y cancelar ante presión. El hash no protege autenticidad frente a alguien que pueda cambiar dump y manifiesto juntos: custodia/versionado/inmutabilidad de ambos y revisión de origen son obligatorios.

Después de crear el archivo, el adaptador de hosting debe cifrar/transferir/verificar su ubicación externa y monitorizarla. Hasta completar ese paso, el manifiesto solo prueba el artefacto local de operaciones y su referencia declarada, no una copia remota protegida.

## Restauración y migraciones

Importar dump/manifiesto aprobados bajo el OutputRoot protegido del laboratorio. Crear config de ensayo con otro RunId y la nueva base vacía, manteniendo origen del manifiesto. `verify-file` comprueba tamaño, hash, formato, versión y decodificación sin abrir una base.

```powershell
dotnet run -c Release --project tools/MediPOS.Recovery -- restore --config C:\MediPOSOps\config\restore.json --confirm restore:RecoveryLab:11111111111111111111111111111111 --backup C:\MediPOSOps\protected\0123456789abcdef0123456789abcdef
```

El guard comprueba cluster real distinto, TLS, ambiente de laboratorio, efectos deshabilitados, base exacta/comentario/propietario, ausencia de otras sesiones, ACL CONNECT restringida y base vacía. Restaura con `--single-transaction --exit-on-error --no-owner`, conserva ACL originales y asigna propiedad al administrador aislado. Las funciones SECURITY DEFINER quedan bajo ese propietario confiable; revisar sus permisos y search paths. Cualquier advertencia falla cerrada. No se desactivan triggers/RLS y no se borran objetos. Un fallo o cancelación queda `FAILED_NOT_VERIFIED`/`CANCELLED_NOT_VERIFIED`; nunca se reintenta automáticamente sobre el destino parcial.

Para ensayar pendientes, generar primero el plan **offline respecto de PostgreSQL** desde el último MigrationId del manifiesto; verificar el archivo usa clientes PostgreSQL sin conexión. Usar un ID distinto para el plan. Revisar su SQL, riesgos de pérdida, backup previo y recuperación, y copiar **`ScriptSha256` de `plan.json` (SHA-256 del SQL UTF-8 exacto de `migrations.sql`, no del JSON)** a la autorización del ensayo. La herramienta no modifica migraciones anteriores.

```powershell
dotnet run -c Release --project tools/MediPOS.Recovery -- plan-migrations --config C:\MediPOSOps\config\plan.json --confirm plan-migrations:RecoveryLab:22222222222222222222222222222222 --backup C:\MediPOSOps\protected\0123456789abcdef0123456789abcdef
# Nueva base/ID/config para el ensayo; SHA-256 revisado del SQL exacto (64 caracteres hexadecimales).
dotnet run -c Release --project tools/MediPOS.Recovery -- rehearse --config C:\MediPOSOps\config\rehearse.json --confirm rehearse:RecoveryLab:33333333333333333333333333333333 --backup C:\MediPOSOps\protected\0123456789abcdef0123456789abcdef --migration-sha256 SHA256_DEL_PLAN_REVISADO
```

`rehearse` restaura, registra esquema inicial, compara todas las huellas, vuelve a comprobar el destino exclusivo, aplica migraciones con el contexto administrativo separado del runtime y exige esquema actual/invariantes. Registra los MigrationId antes/después y hash del plan; comprueba sin recalcular historias append-only existentes. Si una transformación legítima cambia filas de ese historial, este guard exige una estrategia de compatibilidad explícita antes de ampliarse: no lo omitir para obtener verde. El ensayo no afirma que Down revierta transformaciones destructivas. EF no migra al iniciar una instancia comercial.

## Comprobaciones y evidencia

Gate local ejecutado el 2026-10-12: `dotnet restore`, `dotnet build` (0 advertencias/errores), `dotnet test tests/MediPOS.UnitTests` (1986 correctas, 49 casos nuevos de recuperación/correctivo de benchmark), `dotnet test tests/MediPOS.ArchitectureTests` (8 correctas), `dotnet format --verify-no-changes` y EF offline `has-pending-model-changes` sin diferencias. Siete casos PostgreSQL nuevos compilados, **no ejecutados**. El test unitario del plan genera SQL EF determinista desde un prefijo anterior sin abrir PostgreSQL; no demuestra que esas migraciones se apliquen correctamente. No se ejecutaron clientes PostgreSQL, benchmarks, conexiones reales ni containers localmente.

Antes de migrar, igualdad exacta de huellas de todas las tablas y del catálogo de columnas/constraints/FKs/índices/extensiones/funciones/políticas/triggers/ACL. Después, comprobar todos los objetos nombrados por el script de migraciones, tablas y constraints del modelo EF actual, índices válidos, constraints validados y triggers habilitados. Evaluar cada CHECK y FK contra los datos, además de FORCE RLS/políticas en todas las tablas con `tenant_id`.

Invariantes adicionales: saldo por lote igual a suma de movimientos, no negativo; ventas confirmadas/anuladas con líneas y suma de pagos correcta, movimientos completos y auditoría única; borradores sin efectos; reversión exacta de stock/pagos/comisiones contra originales; recepción de compras y auditoría; caja cerrada conciliada con cobros, devoluciones y traslados. Las reversiones de pagos son importes **positivos** que se restan en caja; las reversiones de comisiones son el negativo del earned original. No interpretar ambos ledgers con el mismo signo.

El probe usa el RuntimeDatabaseRoleInterceptor comercial real y conexiones diferenciadas incluso cuando no hay tenants. Con cero tenants abre el runtime con contexto vacío y exige cero filas privadas visibles; con uno exige exactamente sus filas y ninguna ajena. Ambos casos permiten terminar una restauración íntegra, pero **no demuestran aislamiento entre empresas**. Con dos o más examina los dos primeros por UUID y mantiene los conteos propios y ausencia de filas ajenas en cada tabla privada; registra si ambas empresas tienen filas en esa tabla. Tablas vacías o con filas de una sola empresa dejan cobertura pendiente. Este alcance es de lectura para la pareja seleccionada, no una certificación de seguridad ni de aislamiento de escritura/autorización. No insertar tenants artificiales en una copia real; preparar un ensayo sintético separado con dos tenants y datos representativos. Las referencias tenant-safe/identidad de sucursales se verifican por FKs; catálogo global separado por modelo y comparación completa.

El informe enumera conteos de estados offline, conflictos, import jobs, leases de staging/CPE vencidos y entregas `uncertain`/`manual_review`. Conserva exactamente los estados; no libera leases ni convierte incertidumbre en éxito. Recuperar después mediante los casos de uso autorizados y conciliación del resultado externo, con workers todavía detenidos.

`result.json` solo se escribe tras la restauración real y todos los checks aplicables: enlaza backup/ensayo, artefacto, esquema e integridad. B8.3.1 introduce `EvidenceVersion=2`, conserva `ProcedureVersion=1` y los estados históricos `RESTORE_VERIFIED`/`RESTORE_AND_MIGRATION_VERIFIED` como resultados de integridad, y añade `RestoreIntegrity=VERIFIED`. Los manifiestos v1 y las evidencias previas no se reescriben. El antiguo `TWO_TENANTS_CHECKED` significaba lectura de dos contextos, sin detalle de cobertura por tabla; no debe reinterpretarse como certificación completa ni como aprobación productiva. Generar un nuevo ensayo para obtener evidencia v2.

| `TenantIsolation` v2 | `IsolationScope` | Evidencia obtenida y pendiente |
|---|---|---|
| `NOT_DEMONSTRATED_NO_TENANTS` | `EMPTY_TENANT_CONTEXT_READ` | Rol restringido y contexto vacío comprobados; sin identidades examinadas ni evidencia cruzada |
| `NOT_DEMONSTRATED_SINGLE_TENANT` | `SINGLE_TENANT_READ` | Filas propias y ausencia de ajenas comprobadas para un tenant; ensayo cruzado pendiente |
| `SELECTED_PAIR_READ_CHECKED_WITH_GAPS` | `SELECTED_PAIR_READ` | Dos contextos comprobados; alguna tabla carece de filas de ambas empresas |
| `SELECTED_PAIR_READ_CHECKED` | `SELECTED_PAIR_READ` | Lectura de la pareja comprobada con filas de ambas en todas las tablas; regresiones de seguridad siguen pendientes |

`RuntimeRole=CHECKED` exige abrir la conexión con el interceptor real; credenciales privilegiadas, visibilidad incorrecta o errores de integridad rechazan el ensayo, no se reclasifican como pendientes. `TenantsAvailable` cuenta todos los tenants preservados; `TenantsExamined` es 0, 1 o 2. `PrivateTableChecks` registra tabla, conteos esperados/visibles/ajenos y ordinal del contexto examinado (1/2, sin publicar identidades); con cero tenants registra una lectura de contexto vacío con ordinal `null` y conteos cero. `CrossTenantCoverage` es `NO_TENANTS`, `SINGLE_TENANT_ONLY`, `SELECTED_PAIR_ROWS_INCOMPLETE` o `SELECTED_PAIR_ROWS_PRESENT`; presencia de filas no demuestra todos los casos de seguridad. `PendingVerifications` siempre contiene `EXTERNAL_SECURITY_AND_TRANSACTION_REGRESSIONS`; añade `REPRESENTATIVE_TWO_TENANT_REHEARSAL` si hay menos de dos tenants, `PRIVATE_TABLE_ROW_COVERAGE` si faltan filas de la pareja y `ADDITIONAL_TENANTS` si quedaron tenants sin examinar.

La suite comercial pertinente sigue explícitamente `RegressionSuite=PENDING_EXTERNAL_EXECUTION` y `ProductionRecovery=NOT_AUTHORIZED` en todos los casos: ejecutar fuera de esta máquina las regresiones PostgreSQL de ConfirmSale/VoidSale, cash close/transfers, compras, comisiones, RLS/concurrencia, jobs/offline y catálogo/documentos. La suite Testcontainers prepara su propio cluster descartable; no apuntarla a la copia real ni inyectarle credenciales de producción. Un informe exitoso de integridad no reemplaza esas regresiones ni habilita aceptación operativa con evidencia de aislamiento pendiente.

Pruebas preparadas: `RecoveryPostgreSqlTests` usa la infraestructura existente y bases sintéticas nuevas con marcador; no ejecuta benchmarks. Conserva los casos de invariantes después de confirmar/anular con comisiones y RLS/trigger/constraint ausentes, proyección alterada e historial de auditoría incompleto. B8.3.1 añade el probe real con 0/1/2/3 tenants, rol privilegiado con 0/1, visibilidad cruzada incorrecta y fallo de integridad con 0/1; compara huellas antes/después. El caso de cero tenants no genera ni elimina identidades. Las mutaciones se revierten; el fallo RLS entre conexiones se confirma y restaura solo en la base sintética descartable. Estos casos PostgreSQL están **NO EJECUTADOS localmente**. El ensayo de procesos `pg_dump`/`pg_restore`, TLS, ACL y dos clusters es un procedimiento operativo separado, no una restauración simulada en unit tests.

## Fallos adversariales que debe ejecutar el operador

Solo sobre copia sintética y dos clusters descartables. Registrar IDs, UTC, tiempos, versiones, espacio, resultados y hashes en el almacén de operaciones, sin datos de clientes ni claves.

| Caso | Evidencia esperada |
|---|---|
| Dump incompleto, corte, cancelación, disco lleno | Exit distinto de cero/diagnósticos; sin manifiesto válido; reserva impide sobreescritura; alerta operativa |
| Corrupción/hash incorrecto/tamaño cero | `verify-file` falla antes de abrir destino; decodificación detecta daño de bloques, no solo TOC |
| Credenciales/CA ausentes o permiso denegado | Fallo cerrado; error de tipo/código sin password ni stderr bruto |
| Ruta manipulada/workspace/link/archivo existente | Rechazo; ningún archivo ajeno eliminado ni sobreescrito |
| Cluster equivocado, versión distinta, producción o misma instancia | Rechazo por configuración/identificador/GUC/base/versión; cero restore |
| Worker/efecto externo activo u otra sesión | No aprobar atestación; flags o sesiones causan rechazo; comprobar firewall/cron externamente |
| Restore incompleto/fallo de ACL/extensión | Transacción fallida, sin resultado verificado; inspección administrativa, nuevo destino/ID para repetir |
| RLS/trigger/constraint/índice ausente | Diferencia de huella u objeto requerido/invariante; sin reparación automática |
| Stock divergente, auditoría/historial truncado | Huellas y/o invariantes detectan diferencias; no UPDATE correctivo |
| Backup anterior a la última migración | Restaurar prefijo conocido, comparar antes, aprobar plan exacto, migrar y verificar actual/invariantes/historias |
| Migración fallida o proceso interrumpido | `migration-started.json` sin `result.json`, `failure.json` cuando es posible; no Down automático; origen/productivo intacto |

Si el almacenamiento está lleno incluso `failure.json` puede faltar: ausencia de manifiesto/resultado final y exit del scheduler significan **no verificado**. La salida pública registra solo tipo y código de utilidad; obtener diagnósticos detallados en una sesión protegida de operaciones, sin relanzar un restore en una base parcial. Preservar la evidencia fallida en cuarentena hasta revisión, bajo la misma retención/protección.

## Fase D2-A — preparación del ensayo histórico 34→37 (2026-10-17)

**D1 DONE:** [CI 39091989720](https://github.com/saintdeyb321/MediPOS/actions/runs/39091989720), SHA `6c8ffe95fb45bbe8ba2f42c8d3203dad5cf24ff8`: PostgreSQL 18.6 **866/866**, recuperación **19/19**, unitarias **2516/2516**, arquitectura **8/8**. Es evidencia del código anterior a este delta local; no ejecutó el circuito operativo de dump/restore. **D2 IN_PROGRESS; D2-B BLOCKED; Release D NO-GO.** Ninguna operación de esta sección se ha ejecutado contra PostgreSQL. No se han cambiado LAB/DEV, migraciones ni servicios.

### Cobertura y riesgo pendiente

| Área | Cobertura existente/preparada | Evidencia que D2-B debe obtener en la copia restaurada |
|---|---|---|
| Artefacto completo | Snapshot exportado, custom dump, tamaño/SHA-256/decodificación y esquema/ACL completos | Procesos pg_dump/pg_restore 18 reales con TLS verify-full; custodia y transferencia verificadas |
| Ventas/pagos/anulación/comisiones | Invariantes SQL; nueva regresión histórica con ConfirmSale/VoidSale y ledger de ambas boticas | Filas representativas de ambas empresas; snapshot idéntico tras restore y tras upgrade |
| Compras/stock/lotes/costos | Generador sintético existente recibe compras mediante handlers, movimientos y lotes; nueva regresión histórica | Recepciones, costos, lotes/vencimientos/saldos exactos; no modificaciones SQL para reparar |
| Caja/transferencias/licencias | Checks de conciliación e inventario de tablas; no todos esos estados se generan en el nuevo caso | Caja abierta/cerrada, transferencias de mercadería/sencillo y sus eventos, cambios de licencia en ambas empresas |
| Usuarios/membresías/presentaciones | Generador y fingerprints completos; origen 34 conserva esos contratos | Roles, horarios y presentaciones preservados; denegaciones con runtime, no administrador |
| Invitaciones | **No existen en 34**; 35 añade tabla vacía; regresión nueva separa Owner y aceptación por ticket después del upgrade | Verificar primero conservación sin escrituras; luego crear invitaciones sintéticas con handlers en destino 37, Owner propio/ajeno, ticket correcto/incorrecto, aceptación idempotente y ninguna membresía antes de aprobación |
| CPE/UBL | Se incluyen bytes XML, originales, vínculos, estados e intentos; invariantes y huellas disponibles | Crear escenarios sintéticos por contratos de ElectronicDocuments/SupplierUbl; workers sin arrancar, sin proveedor/correo/SUNAT; conservar uncertain/manual_review, XML y todos los intentos |
| Offline/idempotencia/importaciones | Comparación completa y conteos de pendientes; la mera existencia de tablas no cubre sus estados | Eventos aceptados/rechazados, conflictos, reintentos/receipts y jobs; conservar hashes/estados sin reprocesar ni liberar leases |
| Catálogo oficial/notificaciones/auditoría | Fingerprints completos; historiales inmutables ampliados en RecoveryMigrationIntegrity | Publicaciones/revisiones/versiones/lecturas/auditorías sintéticas y datos originales; no fabricar atributos clínicos |
| RLS/runtime | Probe real fail-closed; distingue cobertura incompleta y actor ausente | Filas de ambas empresas en cada tabla aplicable; comprobar lectura/escritura/autorización y Owner/ticket por separado; registrar huecos, nunca borrarlos del resultado |

La regresión nueva `Historical34To37UpgradePreservesTwoTenantCommercialDataAndAddsEmptyInvitationStorage` reutiliza PerformanceDataset y la creación descartable existente, ahora capaz de detener EF en un MigrationId oficial. Usa dos boticas, dos sedes por botica, compras, stock, presentaciones, ventas confirmadas/anuladas, pagos/reversas, comisiones, auditoría y permisos runtime. Compara datos 34→37 y luego prepara comprobaciones independientes Owner/ticket con reloj real de base para las invitaciones. La identidad sintética está limitada al borde de autenticación de pruebas; persistencia/RLS son PostgreSQL real cuando se ejecute externamente. **No es un generador operativo completo D2 ni un ensayo de backup/restore.**

Falta componer y aprobar el generador operativo histórico completo con los escenarios de la matriz (reutilizar handlers y fixtures de transferencias, cierre, UBL/CPE, offline y catálogo). No reutilizar a ciegas `MediPOS.Performance run`: su CLI migra al esquema actual, mide carga y no cubre todos esos estados. No usar su connection string como configuración de Recovery ni aceptar su TLS por inferencia. Si un contrato de refresh no puede producir historia válida en 34 por los defectos corregidos en 36/37, registrar el bloqueo exacto; no parchear la migración 34 ni desactivar el guard para sembrar datos. No certificar historias con conteo cero.

### Prerrequisitos y frontera de autorización D2-B

1. Dos máquinas/VMs/clusters efímeros PostgreSQL 18 independientes, inicializados por separado, con identificadores de sistema diferentes; endpoints e inventario revisados por el operador. Dos bases en una sola instancia, un clon con el mismo system_identifier o dos esquemas no sirven. Ninguna ruta de red/credencial hacia LAB/DEV/producción; no ejecutar containers ni PostgreSQL en esta laptop.
2. Origen nuevo y marcado, exactamente **34** migraciones oficiales y catálogo sin drift. Generación sintética autorizada, determinista, dos tenants/cuatro sedes, reporte privado de conteos por tabla/tenant y cobertura de estados antes de congelar escrituras. Sin usar clientes ni identidades reales. Workers y efectos externos ausentes, no solo flags en JSON.
3. Destino con locales/encoding/proveedor y versión de collation iguales, binarios y extensiones `pg_trgm`/`unaccent` disponibles. Cada restore usa una base **vacía** y RunId nuevos, con nombre/comentario y roles/CONNECT del apartado de prerrequisitos. Precrear nombres de roles originales sin copiar sus credenciales; conceder EXECUTE pg_control_system a las identidades técnicas que lo necesiten. Backup reader BYPASSRLS de solo lectura; restore owner separado; runtime NOSUPERUSER/NOBYPASSRLS/NOCREATEDB/NOCREATEROLE/sin propiedad/CREATE ni membresías privilegiadas. Revisar GRANT por tabla/columna y prohibiciones de historias según el esquema correspondiente.
4. CA privada, certificados de servidor con SAN de cada endpoint y cadena válida, clientes PostgreSQL 18 y .NET 10/proveedor fijados. Passfile privado con entradas exactas por host/puerto/base/usuario, ACL de operador (Unix 0600 o equivalente Windows), sin PGPASSWORD, credenciales en URI/argumentos, trazas SQL ni salida de filas. Verificar TLS desde Npgsql **y** utilidades. Los guards no prueban por sí mismos ACL del passfile, cifrado del almacenamiento ni firewall.
5. Almacén externo cifrado y privado para dump/manifiesto/planes/evidencias, inmutabilidad/custodia y copia en otro dominio de fallo. Aprobación de consumo, retención y posterior eliminación del entorno efímero, sin que Recovery borre bases o artefactos. Configuraciones fuera del workspace; atestaciones solo después de comprobar los controles.
6. Cerrar todas las conexiones de siembra antes del backup. El destino debe quedar sin otras sesiones antes del restore y del recheck previo a migrar; terminar el probe antes de pruebas adicionales. No dejar un monitor/host de aplicación conectado durante esas guardas.

Para este ensayo proponer **`medipos_test_runtime`** en ambos clusters externos, con claves diferentes y privadas, sin reutilizar credenciales de CI/LAB/DEV. La migración 35 concede permisos de invitaciones solo a tres nombres conocidos (`medipos_runtime`, `medipos_lab_runtime`, `medipos_test_runtime`); **un runtime con otro nombre no recibe esos GRANT automáticamente**. Si se escoge otro nombre, bloquear hasta revisar/aprobar su aprovisionamiento explícito de SELECT/INSERT y UPDATE solo de columnas autorizadas, conservando la prohibición de UPDATE global, DELETE y cambios al hash/identidad. No modificar 35 ni ampliar permisos para evitar un fallo del probe.

### Ejecución propuesta y revisión exacta del upgrade

Usar binarios Release **del contenido D2 revisado**, identificados con hashes, SDK/EF/Npgsql y delta frente al SHA D1. El CI verde de 6c8ffe9 no certifica estos cambios locales. Compilar/publicar no instala servicios. Comandos desde `backend/`, únicamente en el host operativo autorizado; ejemplos de rutas corresponden al almacén aprovisionado y no son recursos existentes.

```powershell
# Preparación offline; no abre PostgreSQL. Comparar luego con migrations.sql del manifiesto real.
dotnet build tools/MediPOS.Recovery -c Release
dotnet ef migrations script 20261014193402_AddMembershipSelfDiscovery 20261018023355_FixOfficialRefreshJsonProjectionPrecedence --project src/MediPOS.Infrastructure --startup-project src/MediPOS.Infrastructure --output obj/validation/d2-34-to-37.sql
# EF CLI escribe BOM UTF-8; Recovery escribe UTF-8 sin BOM. Canonicalizar solo este archivo de revisión offline.
$sqlPath=Join-Path (Get-Location) 'obj\validation\d2-34-to-37.sql'
[System.IO.File]::WriteAllText($sqlPath, [System.IO.File]::ReadAllText($sqlPath), [System.Text.UTF8Encoding]::new($false))
Get-FileHash obj/validation/d2-34-to-37.sql -Algorithm SHA256
```

Orden de revisión: **35** `20261017044923_AddTenantEmployeeInvitations` añade tabla vacía/índices/FKs/checks, extiende códigos de auditoría, instala políticas FORCE RLS/guard y GRANT por columna a runtimes conocidos; no recalcula membresías ni datos anteriores. **36** `20261017215309_FixOfficialRefreshApprovalVersionReference` reemplaza `official_refresh_global_guard`, cualificando la columna `version`. **37** `20261018023355_FixOfficialRefreshJsonProjectionPrecedence` reemplaza `official_refresh_guard`, fijando precedencia JSON. Ambos CREATE OR REPLACE preservan dependencias. Son tres entradas EF nuevas, ninguna reescritura de datos existentes ni cambio del snapshot/modelo por D2.

Evidencia local offline: SQL sin BOM **22212 bytes**, SHA-256 `14595BCFF6E619E77D7F377688216E6FB8E9F27F0E457E62919E8EF38291B39C`; tres INSERT de historial EF, una tabla nueva, dos funciones reemplazadas, cero DROP TABLE y cero UPDATE/DELETE de datos a nivel superior. No es autorización ni ejecución del SQL. El BOM del archivo CLI original producía un hash distinto; aprobar siempre el `ScriptSha256` generado por Recovery con el manifiesto real y el ejecutable revisado. No modificar BOM, saltos de línea ni SQL después de aprobarlo; otro SO/proveedor puede producir bytes distintos y requiere revisión de su propio hash.

Con las configuraciones externas del ejemplo anterior, usar backup ID `0123456789abcdef0123456789abcdef`, restore ID `11111111111111111111111111111111`, plan ID `22222222222222222222222222222222` y rehearsal ID `33333333333333333333333333333333` solo si están libres. Source debe apuntar al cluster **sintético 34**, nunca a `medipos_lab`/`medipos_dev`. El administrador crea dos destinos vacíos diferentes: uno para `restore` y otro para `rehearse` (este último incluye de nuevo restore y comparación previa). **No ejecutar rehearse sobre la base que restore ya pobló.**

```powershell
$env:MEDIPOS_RECOVERY_OPT_IN='EXTERNAL_OPERATIONS_ONLY'
$env:MEDIPOS_ENVIRONMENT='RecoveryLab'
$tool='tools/MediPOS.Recovery/bin/Release/net10.0/MediPOS.Recovery.dll'
$backup='C:\MediPOSOps\protected\0123456789abcdef0123456789abcdef'
# Ejecutar secuencialmente; PARAR tras cualquier exit distinto de cero, sin retry automático.
dotnet $tool backup --config C:\MediPOSOps\config\backup.json --confirm backup:RecoveryLab:0123456789abcdef0123456789abcdef
if ($LASTEXITCODE -ne 0) { throw 'Backup NOT VERIFIED' }
$timer=[System.Diagnostics.Stopwatch]::StartNew()
dotnet $tool verify-file --config C:\MediPOSOps\config\backup.json --confirm verify-file:RecoveryLab:0123456789abcdef0123456789abcdef --backup $backup
if ($LASTEXITCODE -ne 0) { throw 'Archive NOT VERIFIED' }
$timer.Stop() # Registrar ElapsedMilliseconds como verificación independiente en evidencia privada.
dotnet $tool restore --config C:\MediPOSOps\config\restore.json --confirm restore:RecoveryLab:11111111111111111111111111111111 --backup $backup
if ($LASTEXITCODE -ne 0) { throw 'Restore NOT VERIFIED' }
dotnet $tool plan-migrations --config C:\MediPOSOps\config\plan.json --confirm plan-migrations:RecoveryLab:22222222222222222222222222222222 --backup $backup
if ($LASTEXITCODE -ne 0) { throw 'Plan NOT VERIFIED' }
# DETENER: revisión humana del SQL y autorización de su ScriptSha256 exacto.
$plan=Get-Content 'C:\MediPOSOps\protected\22222222222222222222222222222222\plan.json' -Raw | ConvertFrom-Json
# Solo DESPUÉS de aprobar ese hash; no automatizar la aprobación leyendo el JSON.
dotnet $tool rehearse --config C:\MediPOSOps\config\rehearse.json --confirm rehearse:RecoveryLab:33333333333333333333333333333333 --backup $backup --migration-sha256 $plan.ScriptSha256
if ($LASTEXITCODE -ne 0) { throw 'Rehearsal NOT VERIFIED' }
```

`restore-compared.json` acredita igualdad de **todas** las tablas, esquema/ACL/locales y 34 migraciones antes de aplicar pendientes. Tras upgrade, RecoveryMigrationIntegrity exige igualdad de todas las tablas originales salvo `__EFMigrationsHistory`, exactamente 37 migraciones conocidas y solo `employee_invitations` nueva/vacía. Incluye estados mutables, datos UBL, idempotencia e historias anteriormente omitidas (licencias, eventos de transferencia e intentos CPE). Para otros prefijos se conserva una política explícitamente limitada de historiales append-only; una transformación legítima exige una estrategia revisada, nunca omitir comparación silenciosamente. `result.json.MigrationDataCoverage` declara el alcance; no reinterpretar evidencias antiguas sin ese campo.

Conservar snapshots/manifiesto, SQL/hash aprobado, UTC, versiones/hashes del ejecutable, identidad técnica lógica y evidencias de ambos clusters bajo custodia. Las lecturas de validación comprueban constraints/FKs contra datos, FORCE RLS/políticas, índices válidos, funciones/triggers esperados y rol runtime. La comprobación de nombres de funciones/triggers no prueba por sí sola su semántica: comparar las definiciones restauradas con el snapshot y los cambios 35–37 revisados; ejecutar las regresiones de comportamiento pertinentes. No iniciar APIs/cron/entregas para probar estados pendientes. Tras cerrar la comparación, registrar por separado las escrituras sintéticas Owner/ticket y sus denegaciones; no mezclarlas con las huellas del restore. Los tests existentes de invitaciones crean su propia base: su PASS no prueba la copia restaurada.

### Negativos y medición

Ejecutar cada fallo sobre artefacto copiado o **nuevo destino efímero** autorizado; nunca alterar el backup de referencia. Casos mínimos: truncamiento de dump y byte cambiado/hash incorrecto; cluster igual/equivocado; destino no vacío; runtime elevado o sin GRANT/ACL incorrecta; extensión no disponible en servidor; interrupción durante restore/migración; corrupción sintética de pago/stock/historia; FORCE RLS ausente; trigger deshabilitado/CHECK NOT VALID; CA errónea, SAN incorrecto, TLS ausente y passfile inaccesible. Esperar exit no cero/sin `result.json`; inspeccionar migraciones efectivamente confirmadas tras interrupción, sin asumir rollback global de EF ni ejecutar Down/retry en destino parcial. La preparación unitaria cubre hashes/prefijos/roles/confirmaciones; estos fallos operativos aún deben ejecutarse con procesos/servidores reales.

El delta D2 escribe `timings.json` con milisegundos monotónicos por snapshot, backup, verificación de archivo, restore, integridad previa, upgrade/recheck, integridad posterior y runtime, más total hasta terminar esas validaciones (excluye escritura final de evidencia y cierre del proceso). Registrar también la duración del `verify-file` independiente con Stopwatch y el tiempo de pared completo desde aprovisionamiento hasta salida del proceso, siembra, copia externa, revisión, reconciliación y aceptación; las fases no solapadas se suman, no sumar dos veces verificación incluida en restore. Un fallo puede no producir timings finales y nunca constituye medición exitosa. No hay tiempos D2 medidos todavía.

Dimensionamiento inicial **estimado, no benchmark**: dos VMs separadas de 2–4 vCPU/4–8 GiB RAM cada una, SSD y host de operaciones .NET10/clientes18; reserva inicial de 10 GiB por cluster y 10 GiB para artefactos protegidos, ajustada antes del ensayo a tamaño real/WAL/temporales. Presupuestar 2–4 horas de operador para primer aprovisionamiento/ensayo/negativos; no es RTO ni duración pronosticada de restore. Medir tamaño de origen `D`, dump `B`, WAL/temporales `W` y número de destinos retenidos `N`: reservar al menos `D + N*D + W` en bases y `2*B` más evidencias/margen en almacén; los negativos requieren espacio adicional. RPO24h/RTO4h siguen **propuestos/NOT_MEASURED**: una copia puntual sintética no prueba periodicidad ni pérdida de una jornada productiva.

**Gate D2-A local PASS:** restore; build (0 advertencias/errores, integración compilada); **102 unitarias focalizadas de Recovery**, incluidas 25 nuevas (subconjunto de las **2541/2541 unitarias completas, 0 FAIL/SKIP**); arquitectura **8/8**; formato verificado; EF offline sin diferencias. TRX bajo `backend/obj/validation/d2-{recovery-focused,unit,architecture}.trx`; SQL offline `d2-34-to-37.sql`. Nueva prueba PostgreSQL histórica/Owner/ticket **NOT EXECUTED**, ningún pg_dump/pg_restore real ni conexión PostgreSQL local. El delta tiene ocho archivos locales; migraciones/snapshot y módulos comerciales sin cambios, HEAD certificado conservado. No atribuirle el CI anterior.

**Checkpoint:** D2-A entrega procedimiento, política de conservación, temporización y regresiones con brechas explícitas de dataset; ejecución/infraestructura siguen bloqueadas. Autorización mínima propuesta para D2-B: aprovisionar exclusivamente dos clusters externos efímeros identificados y almacenamiento protegido, completar el dataset sintético 34 y ejecutar una secuencia backup/verify/restore/plan más negativos revisados; aprobar por separado el SHA exacto del SQL antes de rehearse 34→37 y las pruebas Owner/ticket en la copia. Indicar responsable, endpoints, recursos/retención y eliminación autorizada. Excluir expresamente LAB/DEV, credenciales reales, despliegues, D3, publicación Git/CI y cualquier efecto externo. **No se activó ni creó un workflow D2**: el runner D1 de un solo cluster no demuestra TLS verify-full y aislamiento operativo entre dos clusters.

## Emergencia y recuperación productiva administrada

Detener escrituras y workers del servicio afectado; conservar logs/colas locales y evidencia. Operador y responsable DB seleccionan el último backup de origen confiable, con ensayo asociado, verifican integridad/claves y evalúan pérdida real respecto del RPO. Aprovisionar destino separado; restaurar/validar y reconciliar eventos offline, ventas/caja y entregas inciertas contra evidencias externas. No reenviar entregas tributarias/correos ni aceptar duplicados para “completar” historia.

Esta herramienta **no** autoriza restauración productiva ni cambio de tráfico. El procedimiento definitivo de cutover depende del hosting: requerirá aprobación humana, aislamiento de la antigua base, nuevas credenciales/runtime mínimo, comprobación de compatibilidad de aplicación/esquema, smoke tests autorizados y habilitación gradual de workers con conciliación. Si una migración fue irreversible, recuperar en otro destino desde el backup y evaluar replay de operaciones; no suponer reversibilidad de EF ni reutilizar el origen roto. Solo reabrir tras aceptación del responsable del negocio y verificación del responsable técnico.

Recuperación selectiva de un tenant queda fuera de B8.3: catálogo global, usuarios multi-membresía, publicaciones/historias/auditoría, identificadores y relaciones requieren diseño propio. No copiar tablas entre tenants.

Pendientes para el piloto: hosting/almacenamiento/retención/alertas, operador y suplente, aprobación RPO/RTO, autenticación/TLS y roles técnicos reales, medición de carga/espacio/RTO, ejercicio completo con datos sintéticos incluyendo operaciones pendientes y backup previo a una migración, regresiones PostgreSQL y decisión independiente sobre PITR. Hasta esa evidencia: `B8.3: implementación local completa; restore rehearsal externo pendiente` cuando el gate local esté verde.
