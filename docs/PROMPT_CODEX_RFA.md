# MediPOS — RFA | Radiografía funcional y arquitectura operativa

Actúa como Principal Software Engineer, arquitecto de dominio y QA Engineer senior con experiencia en sistemas transaccionales .NET, PostgreSQL, React y PWA.

Ejecuta `docs/MEDIPOS_FUNCTIONAL_ARCHITECTURE_AUDIT.md`, únicamente **RFA0–RFA3**. Esa especificación es la fuente de verdad de este encargo. RFA4–RFA5 requieren aprobación posterior.

## MISIÓN

Reconstruir qué hace realmente MediPOS, no qué dicen sus pantallas o documentación que debería hacer. Auditar todos los módulos, rutas, roles, acciones, estados y casos de uso desde la interacción del usuario hasta persistencia y recuperación. Separar AS-IS, requisitos vigentes, defectos y TO-BE. Exportar una radiografía legible para el operador y trazable al código.

Primero Caja y Arqueos: reproducir apertura S/100, cierre contado S/10 y listado vacío en entorno aislado. Distinguir persistencia, consulta de activas, historial, permisos, caché y UX. No asumir pérdida de datos. Validar el patrón de auditoría y extenderlo a todas las familias del MD.

## MÉTODO

- Verifica rama, HEAD, cambios locales, versión de build y entorno. Preserva UAX/Blue Studio y trabajo posterior. No cambiar rama ni repetir auditorías visuales generales.
- Lee AGENTS aplicables, estado vigente y requisitos relevantes. Usa `ub-mode` si instalada y revisada, con las restricciones de este encargo por encima de sus reglas de modificación/commit/push. Si falta, aplica sus principios sin instalar automáticamente. No depender de GitNexus; Git, rg, referencias TS/C#, router, endpoints, handlers, EF/SQL/RLS y pruebas existentes bastan.
- Inventaría una vez rutas, actores, endpoints y tests; reconstruye cada caso por vertical: usuario → UI → API → dominio → transacción/datos → respuesta → recuperación. Incluye alternativas y fallos. Cita símbolo/archivo/línea/baseline. No inventes CRUD, estados, permisos ni funciones.
- Verifica con suites existentes y reproducciones aisladas; distingue lectura de código, mocks, integración PostgreSQL, navegador real y aceptación. Comprueba efecto persistido y consulta posterior, no solo éxito HTTP. Marca lo no ejecutado o bloqueado. Registra causa demostrada separada de hipótesis.
- Completa RFA0–RFA3 en orden, sin pedir otro prompt entre familias. Si una prueba externa bloquea, documenta y sigue lo independiente. No reparar código durante la auditoría.

## EFICIENCIA

Trabaja por familias y riesgo. Reutiliza mapas y evidencia vigente, lee solo lo necesario y evita volcados masivos de repositorio/logs. Profundiza en dinero, stock, seguridad, offline, estados y concurrencia. No ejecutar suites completas por cada caso ni instalar herramientas redundantes. Subagentes de lectura solo con ámbitos independientes y beneficio demostrable. Checkpoint único según MD antes de compactar/reanudar; no repetir trabajo. Reserva capacidad para consolidar la radiografía.

## INTEGRIDAD

Solo diagnóstico, documentos y pruebas aisladas autorizadas por el MD. No modificar código de aplicación, API, contratos, esquema, RLS, migraciones, dependencias, CI ni configuración. No escribir en DEV/producción, reiniciar servicios, borrar almacenamiento, enviar CPE, purgar datos reales, hacer commit/push/merge/deploy. Si no puedes acreditar aislamiento, no ejecutar escrituras. No exponer datos ni secretos.

## ENTREGA Y GATE

Genera `docs/audit/rfa/` conforme al MD: índice, fichas de casos de uso con pasos del usuario y trazado técnico, diagramas Mermaid, matriz CSV, hallazgos priorizados, TO-BE, backlog y checkpoint. Identifica brechas de contrato e invariantes; cada hallazgo con evidencia y pruebas de aceptación. Cobertura por rutas/acciones/roles, resultados PASS/FAIL/BLOCKED/NOT_RUN/N/A. No inventar verificación.

Entrega una conclusión única con principales causas, casos cubiertos/pendientes, rutas de archivos, bloqueos, y primer lote recomendado de corrección. Detente en RFA3 para aprobar el TO-BE y los cambios de comportamiento/backend antes de implementar. No declarar producción lista.

Empieza por Caja y continúa con todas las familias del MD.
