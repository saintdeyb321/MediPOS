# B-02 · Piloto aislado de MediPOS

Proyecto opt-in fuera de la solución principal. xUnit y Testcontainers reutilizan el fixture PostgreSQL y el seed de estructura de Integración. Playwright .NET 1.64.1 (MIT, NuGet con lockfile) automatiza Chromium y comparte las cookies del contexto con las consultas directas. El código HTTP y comercial es el de MediPOS. La sesión de identidad sintética existe exclusivamente en este ejecutable de pruebas. Usa cookie, keyring, HTTPS y antiforgery reales. [Paquete y licencia](https://www.nuget.org/packages/Microsoft.Playwright/1.64.1)

## Ejecución del operador

En GitHub, ejecutar manualmente **B-02 isolated browser pilot** una vez sobre el SHA del candidato revisado y comprobar el resumen. GitHub exige que el workflow de dispatch exista en la rama por defecto. Este archivo nuevo carece de registro en el baseline. Su disponibilidad en la rama por defecto queda por confirmar después de publicación. El workflow existente de backend y su gate son independientes. [Regla de dispatch](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)

Si el workflow aún no se puede despachar, usar el mismo proyecto desde un checkout limpio del SHA revisado en un runner Linux autorizado. Requiere Docker local operativo por socket Unix, Bash, PowerShell 7 para el instalador, SDK 10.0.402 activo y Node 24.18.1. Usar un perfil de runner sin credenciales privadas, overrides de ASP.NET/PostgreSQL/Docker ni archivo `.testcontainers.properties`. Descarga de herramientas e imagen oficial solo en ese runner.

```bash
set -euo pipefail
test -z "$(git status --porcelain)"
test -S /var/run/docker.sock
test "$(dotnet --version)" = '10.0.402'
test "$(node --version)" = 'v24.18.1'
docker --host unix:///var/run/docker.sock info --format '{{.ServerVersion}}'
export MEDIPOS_E2E_SHA="$(git rev-parse HEAD)"
run_dir="$(mktemp -d)"
export MEDIPOS_E2E_BUILD="$run_dir/frontend-build"
(cd frontend && npm ci && npm run build -- --outDir "$MEDIPOS_E2E_BUILD")
cd backend
dotnet restore tests/MediPOS.E2E/MediPOS.E2E.csproj --locked-mode
dotnet build tests/MediPOS.E2E/MediPOS.E2E.csproj --no-restore
pwsh tests/MediPOS.E2E/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
report="$run_dir/b02.log"
trap 'rm -f -- "$report"' EXIT
code=0
dotnet tests/MediPOS.E2E/bin/Debug/net10.0/MediPOS.E2E.dll -showLiveOutput >"$report" 2>&1 || code=$?
grep -Eo 'B02 (SHA [a-f0-9]{40} BUILD [a-f0-9]{64}|POSTGRES 18[0-9]{4} ORIGIN https://localhost:[0-9]{1,5} ROLE medipos_test_runtime|P[0-5] START|P[0-2] PASS|P3 PASS CASH_HISTORY_AND_RECOVERY|P4 PASS POS_PAY_TICKET_VOID|P5 PASS CATALOG_SEARCH_WRITE_IMPORT)$' "$report" || true
test "$code" -eq 0
for phase in 0 1 2; do grep -q "B02 P$phase PASS$" "$report"; done
grep -q 'B02 P3 PASS CASH_HISTORY_AND_RECOVERY$' "$report"
grep -q 'B02 P4 PASS POS_PAY_TICKET_VOID$' "$report"
grep -q 'B02 P5 START$' "$report"
grep -q 'B02 P5 PASS CATALOG_SEARCH_WRITE_IMPORT$' "$report"
```

Los hitos identifican SHA, manifiesto SHA-256 (rutas ordenadas y hashes), versión PostgreSQL, origen y última fase completada. El workflow solo publica esos hitos. Al investigar localmente, mantener cualquier salida de fallo bajo control del operador. Borrar el build de la carpeta temporal una vez registrada la evidencia.

## Fronteras del piloto

- El fixture selecciona un Docker local explícito, crea BD y contenedor propios con credenciales aleatorias y publica PostgreSQL en un puerto efímero ligado a loopback. Antes de migrar valida contenedor, destino, BD y motor. Formula el runtime desde la misma conexión y comprueba sus privilegios antes del seed. Infraestructura productiva conserva interceptor y RLS.
- API en HTTPS loopback y recursos privados temporales. Chromium usa contextos nuevos, bloqueo de WebSocket/service workers y proxy local sin salida, además de filtro de solicitudes por origen exacto. La confianza del TLS de prueba queda en esos contextos. El host corta el backchannel Google y los clientes HTTP registrados, y parte de configuración privada explícita en un content root nuevo. Plataforma y scheduler apagados. El fixture elimina su contenedor y el host elimina navegador, certificado y keyring propios.
- El seed usa tres Owners y un Cajero sintéticos en dos tenants y tres sedes. P0 comprueba React compilado, sesión, 401, 403 y CSRF sin escrituras. P1 abre caja desde React, comprueba 200/open/null y compara la fila con PostgreSQL y negativas por sede/tenant.
- P2 comprueba falta de fondos, pérdida de acuse, constancia y recepción con otra sesión. P3 exige 100→10 con pérdida de acuse de cierre, arqueo por URL y desde Cierres con entrada limpia, una auditoría y ausencia en Activas. P4 cubre POS móvil con teclado/touch, borrador y versión obsoleta, venta mixta con pérdida de acuse, ticket/impresión, anulación, reversiones y negativas/paginación de Cierres.
- P5 usa otro tenant sintético. Comprueba filtro de ingredientes y comodines literales antes de paginar, negativas por rol y tenant, altas local/global, corrección auditada, precios y presentaciones exactos, 409 por versiones obsoletas, recuperación de alta e XLSX parcial con acuses perdidos, paginación de filas y cero movimientos de inventario.

Para añadir recorridos L-06A–I, reutilizar `BrowserHost` y los escenarios de seed mínimos del fixture, y verificar las filas pertinentes desde otro scope runtime. Probar permisos con contextos e identidades distintos. Los escenarios de service worker de L-06I requieren extender el aislamiento del navegador antes de habilitar workers; el piloto actual cubre conexión real y recuperación con recarga. B-03 conserva la aprobación independiente de integraciones externas.
