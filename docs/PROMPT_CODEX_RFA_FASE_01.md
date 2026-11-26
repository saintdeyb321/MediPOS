# MediPOS — RFA | FASE 01 · Radiografía AS-IS

Actúa como Principal Software Engineer y arquitecto de dominio senior. Ejecuta íntegramente `docs/MEDIPOS_RFA_FASE_01_AS_IS.md` v1.1, bajo el gobierno de `docs/MEDIPOS_FUNCTIONAL_ARCHITECTURE_AUDIT.md` v1.1. Solo RFA0–RFA1.

## MISIÓN

Recuperar el conocimiento funcional completo de MediPOS: actores, acciones reales, casos de uso, pasos del usuario, validaciones, estados, dependencias, persistencia y recuperación. No confundir implementación con funcionamiento probado.

## EJECUCIÓN

1. Lee AGENTS aplicables y estado vigente; verifica HEAD, rama y árbol local. Preserva Blue Studio/UAX y cambios del operador. Usa `ub-mode` según el MD; si falta, está autorizada únicamente su instalación oficial y verificada. Si no es viable, continúa sin ella.
2. Inventaría una vez rutas, acciones, roles, endpoints, jobs y pruebas. Guarda el inventario global antes de profundizar.
3. Traza Caja y Arqueos como patrón, incluido el reporte S/100 → S/10 → listado vacío. Distingue persistencia, historial, permisos, caché y recuperación sin ejecutar transacciones ni asumir pérdida de datos.
4. Extiende la trazabilidad a todas las familias: usuario → UI/hook → API/DTO → autorización → dominio → EF/SQL/RLS → respuesta → consulta posterior. Incluye alternativas, errores y estados con referencias exactas al código.
5. Genera incrementalmente las fichas, diagramas Mermaid pertinentes, inventario, matriz CSV y hallazgos estáticos en `docs/audit/rfa/`, siguiendo el MD. Actualiza CHECKPOINT tras el inventario, Caja y cada familia.

## EFICIENCIA Y LÍMITES

Prioriza amplitud antes que profundidad. Git/rg y referencias TS/C#; reutiliza contratos y hallazgos vigentes. Sin subagentes, GitNexus, análisis de complejidad, exploraciones redundantes, suites, build ni lint en esta fase. No modificar código, tests, API, base de datos, AGENTS, configuración o DEV. No ejecutar escrituras operativas. Respeta el gobierno permanente del master: Git de solo lectura; el operador hace manualmente stage, commit, push y publicación. No solicitar ni intentar eludir autenticación.

## ENTREGA Y GATE

Reconcilia rutas, acciones, casos, matriz y referencias; evidencia E0/E1 y ejecución NOT_RUN/BLOCKED/N/A según corresponda. No inventar PASS. Informa cobertura con denominadores, familias pendientes, indicios, artefactos y diff resumido. Guarda CHECKPOINT con próxima acción exacta. Declara FASE_01_COMPLETA/PARCIAL/BLOCKED según evidencia. Detente en RFA1 y deja todo listo para la revisión del operador; no iniciar RFA2 ni corregir código. Si se reanuda, continuar únicamente pendientes y cambios relevantes.

Empieza a trabajar.
