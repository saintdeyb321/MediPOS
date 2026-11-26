# MediPOS — Radiografía funcional y arquitectura operativa (RFA)

**Especificación ejecutable para Codex · v1.1 · 26/11/2026**

## 0. Mandato y límites

Reconstruir el sistema real de extremo a extremo antes de modificarlo: qué puede hacer cada actor, por qué ruta, en qué orden, con qué reglas, qué cambia en PostgreSQL y qué puede recuperar después. Comparar comportamiento observado (AS-IS), intención aprobada (requisitos) y propuesta (TO-BE). Entregar una radiografía navegable para el operador y evidencia técnica para corregir causas raíz. No confundir rediseño visual con corrección funcional, ni cobertura de tests con éxito del negocio.

Repositorio local `C:\Proyectos\MediPOS`. Referencia remota histórica: `7d453285252d2bf9279cb7c42e1ac95f4f8c70c6`, rama `ci/postgres-diagnostics-8bc6b4c`; NO asumir que es HEAD local ni cambiar de rama. Preservar UAX/Blue Studio, trabajo no confirmado y operaciones pendientes. La auditoría incluye frontend, API, aplicación, dominio, persistencia, integración y PWA. El monolito modular y PostgreSQL se mantienen salvo decisión explícita posterior.

**Alcance general de auditoría: RFA0–RFA3, solo diagnóstico y artefactos de auditoría.** La fase activa se autoriza exclusivamente por su MD y prompt; el master no autoriza anticipar fases ni pruebas que la fase activa excluya. Se permiten pruebas de lectura, suites existentes y reproducciones con datos sintéticos en entornos de prueba aislados que no tengan acceso a DEV real/producción. Se pueden crear pruebas de caracterización/reproducción en espacio temporal aislado, guardando ubicación/diff; no modificar código de aplicación, esquemas, contratos, AGENTS, CI, dependencias o configuración sin aprobación. No reiniciar DEV, resetear datos, borrar storage, cambiar OAuth, enviar CPE, purgar tenants reales ni hacer commit/push/merge/deploy. No usar credenciales privadas en herramientas externas. Ante duda sobre destino de conexión, no ejecutar escrituras. Mantener los gates operativos previos, incluido S4/writer drain. RFA4–RFA5 son fases propuestas, **no autorizadas por el prompt de auditoría**.

### 0.1 Gobierno permanente de RFA y entrega al operador

- El operador es el único responsable de preparar/stagear cambios, hacer commits, push, merge, publicación y despliegue. Codex usa Git local de solo lectura (`status`, `diff`, `diff --check`, `log`, `show`, `ls-files`, `rev-parse`, `grep`); no ejecutar `git add`, `commit`, `push`, `pull`, `fetch`, `merge`, `rebase`, `reset`, `restore`, `clean`, `checkout`, `switch` ni acciones remotas equivalentes. No intentar desbloquear autenticación, pedir credenciales, modificar configuración Git ni eludir el candado con CLI, API, otro agente o herramienta. Dejar cambios documentales/código autorizado sin stage y reportar rutas y diff resumido, conservando cambios previos del operador.
- El operador autoriza descargar/instalar skills expresamente seleccionadas en el MD de la fase activa, desde fuente oficial verificable y mediante el mecanismo admitido de Codex. Inspeccionar SKILL.md, licencia, versión/commit y alcance antes de aplicarla; registrar origen y versión, no ejecutar scripts auxiliares desconocidos ni compartir código/datos privados. Instalar fuera del repositorio, sin alterar dependencias, AGENTS, hooks, CI o configuración del proyecto. Si no hay mecanismo autorizado, permisos, compatibilidad o procedencia verificable, continuar con el método del MD y declarar la limitación. No instalar herramientas compañeras automáticamente.
- Carpeta canónica acumulativa: `docs/audit/rfa/`. Conservar IDs, referencias y evidencia de fases previas; no crear una radiografía paralela ni sobrescribir hallazgos sin registrar su evolución. El CHECKPOINT único identifica fase, modelo, baseline, cobertura, pendientes y siguiente acción. Los MD de cada fase son órdenes de trabajo, no entregables duplicados de la radiografía.
- Al terminar cada fase, detenerse y entregar resumen verificable, archivos, cambios, limitaciones y estado de gate; dejar pendiente la revisión del operador. La revisión puede pedir correcciones dentro de esa misma fase. Solo iniciar la siguiente con nueva instrucción explícita del operador tras revisión; no cambiar de modelo automáticamente. El operador elige modelo/esfuerzo y publica manualmente cuando corresponda. No asumir que un commit nuevo invalida toda la auditoría: comparar cambios relevantes y actualizar solo evidencia afectada.

## 1. Método, fuentes y economía de contexto

- RFA0: leer AGENTS aplicables, estado vigente de `docs/CURRENT_STATE.md`, instrucciones operativas y los requisitos/ADR pertinentes. Identificar documentos reales y versiones. Documento Maestro sept. 2026 es baseline de intención, no prueba de implementación ni autoridad para sobrescribir decisiones posteriores aprobadas. Registrar contradicciones. Si falta un documento, usar las fuentes disponibles y declararlo.
- Baseline: rama, SHA, diff/huella de cambios locales y árbol (sin volcar secretos), identidad de build frontend/API, entorno y versión de esquema cuando se pueda consultar sin riesgo. Separar remoto, local, entorno de prueba y DEV servido. Registrar comandos, resultados y fecha. No publicar PII, tokens, UUID de clientes reales ni payloads privados.
- Usar Git/rg, referencias del IDE/compilador TS y C#, router, endpoints/OpenAPI si existe, registros DI, handlers, EF, SQL/RLS, tests y herramientas de navegador existentes. No instalar GitNexus ni depender de él; si se usa, exigir licencia comercial e índice vigente. No crear grafo de cada método; mapear actores, acciones y fronteras de negocio.
- `ub-mode`: revisar el SKILL.md original, versión/commit, licencia y compatibilidad; activar sus principios de dominio, TDD, corrección de causa raíz, pasos pequeños y control de contexto. No instalar automáticamente herramientas compañeras ni seguir instrucciones de commit/push/limpieza destructiva. Las restricciones del encargo prevalecen. En auditoría no refactorizar por sugerencia de la skill. `frontend-skill` solo en la futura etapa de presentación cuando aporte valor.
- Herramientas CRAP, duplicación, mutación: opcionales y focalizadas. Las herramientas originales de Uncle Bob consultadas documentan TS/TSX pero no C# en su lista de lenguajes; verificar soporte/licencia/seguridad de la versión concreta antes de ejecutar. Aprovechar cobertura/analizadores .NET y Vitest ya presentes; no añadir paquetes sin autorización. No imponer CRAP<10 o similitud>=0.8 como gate global ni abstraer código por una puntuación. Medir hotspots solo si ayuda a priorizar; mutación en reglas críticas, no en todo el monorepo.
- Inspeccionar una unidad representativa (Caja) de principio a fin y validar la plantilla de documentación; expandir por familias de módulos. Indexar rutas, acciones, endpoints y tests una vez; reutilizar hallazgos por símbolo y versión. Leer funciones y contratos relevantes, no todo el repositorio en cada caso. Guardar resultados extensos en archivos, extraer solo resumen y evidencia necesaria. Subagentes de solo lectura opcionales si existe beneficio y ámbitos independientes; un coordinador reconcilia IDs y evidencia. No repetir trabajo. Sin presupuestos de tokens arbitrarios que reduzcan cobertura; limitar logs, análisis y pruebas repetitivas, no razonamiento de riesgo.
- Checkpoint único `docs/audit/rfa/CHECKPOINT.md`: baseline, familias completadas, IDs pendientes, fuentes ya inspeccionadas, comandos y resultados, bloqueos, siguiente acción. Actualizar al terminar cada familia y antes de perder contexto. Reanudar sin releer todo; invalidar solo evidencia afectada por cambios. No sobrescribir el estado operativo histórico.

## 2. RFA1 — Inventario completo y reconstrucción AS-IS

Crear matriz desde router/navigation, roles, permisos backend, endpoints, feature flags, jobs, integraciones y requisitos. No limitarse al menú del dueño. Cubrir acciones visibles, rutas secundarias/deep links y acciones automáticas. Distinguir implementado, no expuesto, no permitido, desactivado, no implementado y no determinado. Relacionar cada acción de UI con un caso o variante; relacionar cada endpoint operativo con consumidor o uso interno. No asumir que un botón existe porque lo dice el Documento Maestro. No llamar "eliminar" a desactivar/anular.

Familias iniciales (ajustar según inventario real):

| Orden | Familia | Flujos a rastrear, no funcionalidades asumidas |
|---|---|---|
| 1 | Caja y arqueos + POS | Abrir, consultar, vender, pagar, ticket, anular, cerrar, diferencia, recuperar/historial, sencillo; concurrencia y permisos |
| 2 | Productos y catálogo | Crear/asociar, editar, activar/desactivar, eliminación si existe, presentaciones, precios, categorías, búsqueda, importación/recuperación |
| 3 | Compras e inventario | Proveedores, borrador, líneas, lotes, recepción, confirmación, consulta, ajustes, mínimos, vencimiento, FEFO |
| 4 | Transferencias | Mercadería y efectivo; solicitud, aceptación, rechazo, despacho, recepción, diferencias, cancelación, recuperación según contrato |
| 5 | Reportes y comisiones | Filtros, alcance, cálculo, reversión, exportación, frescura de datos |
| 6 | Notificaciones y auditoría | Consulta, contador, lectura, acción, persistencia, trazabilidad, estados |
| 7 | Acceso, personal, sedes y configuración | Autenticación, onboarding, invitaciones, roles, horarios, sesión, cambios de sede, datos fiscales, licencia |
| 8 | Superadmin | Tenants, licencias, catálogo global, soporte, suspensión, purga (solo análisis/fixtures), políticas de acceso |
| 9 | PWA/contingencia e integraciones | Caché, offline, outbox, sync, conflictos, reintentos, jobs, CPE/DIGEMID según estado efectivo |

Por caso rastrear: actor/rol/sede → navegación → página/acción → estado UI/hooks → cliente HTTP/DTO → endpoint/autorización → handler/dominio → transacción/EF/SQL/RLS → registro/auditoría/outbox → respuesta → invalidación/caché → confirmación y consulta posterior. Incluir cambios de contexto, abandonos, errores, dobles clics, reentrada y recarga según aplicabilidad. Marcar ramas no inspeccionadas. Dibujar Mermaid únicamente para flujos con decisiones/transiciones relevantes (no un gráfico por función). Consolidar estados comerciales en tabla de transición: origen, evento, guarda, destino, efectos, actor, compensación/reintento. No inventar transiciones.

### Formato canónico por caso (una ficha por operación significativa)

`UC-{FAMILIA}-{NN}` estable; enlazar FR/BR/UC originales cuando aplique. Mantener variantes junto al caso, no duplicar fichas por cada rol.

- **Título / objetivo:** lenguaje de botica; actor, roles, sede, permiso y punto de entrada.
- **AS-IS usuario:** pasos numerados tal como el código los permite; acciones, campos, confirmaciones, vuelta y consulta posterior. Separar lo observado de lo deducido por lectura. Anotar clics solo si medidos; no inventarlos.
- **AS-IS técnico:** ruta/símbolos, endpoint/método, DTO, handler, entidades/tablas reales, transacción, validaciones, auditoría, idempotencia, respuesta y caché. Evidencia `ruta:línea` + símbolo + baseline; si línea varía, resolver símbolo.
- **Reglas e invariantes:** dinero decimal, stock, permisos, versiones, licencia/horario, tenant/sede, estados y efectos laterales donde correspondan.
- **Alternativos/fallos:** cancelación, permisos, datos inválidos, conflicto, red incierta, refresco, reintento y recuperación. Declarar N/A con motivo si no aplica.
- **Postcondición y recuperabilidad:** qué debe quedar registrado y cómo se consulta tras recargar/reingresar; diferenciar listado parcial vs inexistencia de dato.
- **Requisito/TO-BE:** requisito vigente y diferencia; propuesta marcada como tal, cambios de comportamiento requieren aprobación. No mezclar TO-BE con descripción del código.
- **Evidencia y veredicto:** ID de prueba, resultado, entorno, fuente y limitaciones; estado de implementación y verificación; enlace a hallazgo si existe.

## 3. RFA2 — Validación conductual y reconciliación real

No declarar PASS por leer un test, por recibir HTTP 200 o por un fixture que devuelve el resultado deseado. Escala de evidencia: E0 no verificado; E1 código rastreado; E2 test ejecutado aislado (indicar unitario/DOM/mocks); E3 integración real API/aplicación y PostgreSQL de pruebas con efectos comprobados; E4 recorrido navegador + API real + PostgreSQL de pruebas, comprobando también recarga/consulta; E5 aceptación operador en ambiente autorizado. No son niveles automáticos: indicar exactamente qué frontera cubre cada evidencia. Diferenciar PASS/FAIL/BLOCKED/NOT_RUN/N/A, sin ocultar skips. Un flujo puede tener varias evidencias y fallar a nivel de experiencia aunque persista correctamente.

Reutilizar suites existentes, comprobar que apuntan a fixture aislado. Verificar motor real PostgreSQL/RLS en pruebas de integridad, no sustituirlo por in-memory. Ejecutar caso feliz y variantes de riesgo por familia; evitar producto cartesiano de todas las combinaciones. Acciones monetarias, stock y seguridad requieren pruebas de invariantes y, si el contrato lo exige, concurrencia/idempotencia. Validar API/BD mediante lectura posterior, incluyendo auditoría, IDs, conteos y ausencia de efectos duplicados. Probar interfaz poblada, error, resultado incierto, vuelta y recarga. Si no hay navegador/auth/DB aislada, no inventar resultados; registrar reproducción manual exacta y bloqueo. No usar producción ni sesión real para transacciones. Conservar evidencias sanitizadas fuera de artefactos públicos.

### Primer caso obligatorio: apertura/cierre e historial de caja

Reporte del operador: abre con S/100, cierra contando S/10 y el listado queda vacío. En la base remota histórica, `frontend/src/features/cash/CashPage.tsx` usa listado de activas para Owner y `frontend/src/features/cash/api.ts` ofrece active/current/status/reconciliation por ID, open/close. `backend/tests/MediPOS.IntegrationTests/Modules/Cash/CashClosePersistenceTests.cs` afirma que cerrar quita de activas y permite recuperar conciliación; existencia de test no significa ejecución. Confirmar HEAD y contratos reales locales antes de concluir.

En tenant/sede de prueba sin movimientos: abrir 100, capturar ID sintético; consultar current/active según rol; cerrar 10; verificar estado closed, apertura 100, esperado 100, contado 10, diferencia -90 (si convención contado menos esperado), fecha/actor, conciliación y auditoría según esquema real. Confirmar que ya no aparece como activa y no se borra el registro; recargar/reingresar y comprobar si el usuario lo encuentra por flujo normal sin UUID manual. Probar reenvío/resultado incierto, intento de vender con caja cerrada y aislamiento. Clasificar por separado: persistencia, consulta, caché, alcance/permisos, UX/recuperación, defecto de requisitos. Si falta endpoint de historial, proponer contrato mínimo paginado con filtros/roles y pruebas, no simularlo ni implementar durante auditoría. No afirmar pérdida de datos sin evidencia.

## 4. RFA3 — Hallazgos, TO-BE y priorización

Registro único `F-{NNN}` con título, familia/UC/FR, pasos reproducibles, esperado/observado, fuente de expectativa, evidencia/entorno, alcance, causa confirmada o hipótesis, severidad, confianza, dependencias, propuesta mínima, pruebas de aceptación y aprobación requerida. Tipos: defecto funcional, integridad/seguridad, brecha de contrato, UX/flujo, deuda técnica, test insuficiente, bloqueo externo. Separar síntomas y agrupar causas comunes. P0: pérdida/corrupción de datos, fuga tenant, doble cargo/stock negativo u otro riesgo crítico; P1: operación esencial rota o no recuperable; P2: fricción, incoherencia o deuda relevante; P3: mejoras menores. No elevar estética a P0.

TO-BE por hallazgo o familia: reducir pasos/estados redundantes sin eliminar obligaciones comerciales, confirmar datos antes de cambios irreversibles, modelo de estados coherente, una fuente de verdad, roles claros y recuperación habitual accesible. Precisar qué cambia: UI, contrato, dominio, persistencia, migración, o política de negocio. Estimar impacto/dependencias (S/M/L, no horas inventadas). Si ya funciona y está claro, no tocar. Diferenciar cambios de comportamiento (bugfix/feature) de refactor puro (preservar comportamiento). Proponer fases de corrección agrupadas por causa, empezando por P0/P1; no decidir reglas no especificadas ni modificar backend automáticamente.

## 5. Entregables de auditoría (fuente única, legible y trazable)

Crear bajo `docs/audit/rfa/`:

- `README.md`: índice, baseline, resumen ejecutivo, mapa de módulos y dependencias, cómo navegar la radiografía, decisiones y cobertura. Operador debe comprenderlo sin conocer el código.
- `USE_CASES.md`: índice por módulo y actor, enlaces a fichas, acciones visibles, estado implementación, evidencia y veredicto. Detalle en `flows/01-caja-pos.md` etc. Una ficha canónica por ID, con pasos del usuario primero y detalle técnico después. Mermaid dentro de cada familia cuando ayude. No duplicar íntegramente el mismo flujo en otro archivo.
- `MATRIX.csv`: una fila por caso/variante relevante, columnas `uc_id,module,actor,entry,action,implementation,evidence_level,verdict,requirement_ids,finding_ids,flow_ref,test_ref`. Incluir acciones/endpoints sin consumidor y rutas no cubiertas en secciones del índice o tabla asociada. Contar denominadores reales y estados; sin porcentaje ficticio.
- `FINDINGS.md`: registro de hallazgos y evidencias, gravedad/confianza/causa. `ROADMAP.md`: propuestas TO-BE y backlog ordenado por dependencias, pruebas, cambios autorizables y gates. No repetir las fichas completas.
- `CHECKPOINT.md`: continuidad y registro breve de comandos/resultados. Evidencia extensa en ubicación local sanitizada enlazada, no volcar logs completos al chat. Generar Markdown + Mermaid + CSV como export portable, sin herramientas nuevas; opcional HTML solo si ya hay renderizador disponible y no introduce dependencias. No exportar datos de farmacia reales.

Gate RFA3: todas las familias del inventario tienen estado explícito; cada acción/ruta relevante tiene caso/variante o justificación; cada caso tiene evidencia y limitaciones; todos los P0/P1 tienen reproducción o bloqueo documentado; mapa de dependencias, matriz, hallazgos y backlog coinciden. No llamar a la auditoría "completa" si hay casos no inspeccionados: distinguir inventario completo de validación parcial. Entrega final compacta: enlaces, conteos, principales causas, bloqueos, propuesta de primer lote y GO/NO-GO para iniciar correcciones (no para producción). Detenerse al llegar al gate de aprobación.

## 6. RFA4–RFA5 — Corrección y preparación de producción (orden futura, no ejecutar ahora)

RFA4: operador aprueba IDs y alcance, incluidas decisiones de dominio/contrato/esquema si proceden. Para cada lote, fijar comportamiento correcto existente mediante tests de caracterización, reproducir defecto con test que falle por la causa esperada; corregir lo mínimo y verificar; refactorizar en paso separado conservando pruebas. No congelar un bug como conducta esperada. Mantener Blue Studio y patrones UAX. Simplificar estados, hooks, límites y transacciones según evidencia, no reescribir monolito ni imponer arquitectura ceremonial. Si hay migración aprobada, compatibilidad, prueba de upgrade/rollback o recuperación, revisión RLS y backup/restauración antes de release. Revalidar consumidores e invariantes; actualizar ficha, evidencia y hallazgo sin borrar historial AS-IS. Pruebas focalizadas y ampliar por impacto.

RFA5: ejecutar gates finales del repositorio en entorno aislado: build/lint/test frontend, build/test backend, integración PostgreSQL, pruebas E2E de recorridos críticos, seguridad multiempresa, permisos, concurrencia, exactitud de caja/stock/comisiones, offline/recuperación, responsive y accesibilidad, revisión de dependencias/secretos, observabilidad, backup y restauración probada, rendimiento con datos representativos. Registrar identidad del artefacto y resultados. CI del SHA definitivo, DEV autorizado, aceptación comercial, integraciones y gates externos previos siguen independientes. NO-GO ante P0/P1 abierto que comprometa release, gates requeridos fallidos o sin verificar, datos no reconciliados o build no identificado. Excepciones solo con decisión explícita y evidencia. No publicar automáticamente.

## Referencias de método

- https://github.com/unclebob/ub-mode/blob/master/SKILL.md
- https://github.com/unclebob/ub-mode/blob/master/LICENSE
- https://github.com/unclebob/crapper ; https://github.com/unclebob/dryer ; https://github.com/unclebob/mutator
- https://developers.openai.com/api/docs/guides/model-selection
- https://developers.openai.com/blog/rethinking-skills-and-prompts-for-gpt-6-astra
