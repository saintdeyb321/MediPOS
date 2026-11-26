# MediPOS — BLUE STUDIO | UX ARCHITECTURE · UAX1–UAX5

Actúa como Principal Frontend Engineer, Software Architect y Senior Product Designer. Implementa íntegramente `docs/MEDIPOS_BLUE_STUDIO_UX_ARCHITECTURE.md` v2.0. Es la especificación ejecutable y fuente de verdad de este encargo; el master Blue Studio anterior conserva su identidad y decisiones compatibles.

Repositorio: `C:\Proyectos\MediPOS`. Referencia auditada: `7d453285252d2bf9279cb7c42e1ac95f4f8c70c6`, rama `ci/postgres-diagnostics-8bc6b4c`. Verifica HEAD, rama y diff reales; conserva cambios locales posteriores. No cambies de rama ni reviertas trabajo ajeno.

## MISIÓN

Completar la refactorización de TODOS los módulos, rutas secundarias y estados visibles, incluidos Superadmin, POS, Caja, Arqueos y Contingencia. Resolver arquitectura de presentación, distribución, jerarquía, densidad, navegación, redacción, accesibilidad y eficiencia operativa. No limitarse a colores ni reconstruir D1–D5.

Prioridad: integridad comercial > claridad y accesibilidad > consistencia y eficiencia > decoración.

## MÉTODO DE EJECUCIÓN

1. Lee AGENTS aplicables, el MD UAX, el bloque vigente de `docs/CURRENT_STATE.md` y solo las secciones necesarias del master Blue Studio. Identifica rutas/roles, primitivas, consumidores y baseline real. No hagas otra auditoría general.
2. Usa `frontend-skill` oficial si está instalada; si no, continúa con el MD salvo instalación autorizada. GitNexus solo con índice vigente y permiso comercial acreditado; si no, git, rg y referencias TS. TailAdmin es la referencia principal; Tabler y shadcn son referencias secundarias, no frameworks nuevos.
3. Ejecuta UAX1 → UAX2 → UAX3 → UAX4 → UAX5 en orden y sin esperar instrucciones entre bloques. Primero patrones compartidos; después Productos e Inventario como referencia visual y funcional. Corrige defectos compartidos antes de propagar. Migra los demás módulos por familias de composición.
4. Por corte de trabajo: inspecciona ruta → página → primitivas → hook/contrato → prueba; implementa, prueba focalizadamente, verifica en navegador si disponible, corrige y registra checkpoint. Si una dependencia bloquea una tarea, documenta la causa concreta y continúa lo independiente.

## REQUISITOS NO NEGOCIABLES

- Blue Studio, sidebar izquierda, tokens únicos, React/TypeScript/Tailwind y primitivas existentes. Composición A–D, DRY, KISS y SOLID sin mega componentes ni abstracciones innecesarias.
- Productos: una superficie de catálogo con tabs, toolbar y acciones contextuales, Importar Excel y Nuevo producto agrupados; tabla dominante. Inventario: búsqueda inline, filtros compactos y ayuda por icono/popover, no enlaces de instrucciones dispersos.
- Compras y Transferencias: consultas separadas de editores activos, navegación explícita de retorno, borradores y estados seguros. Notificaciones: bandeja coherente. Aplicar los contratos particulares del MD a todas las demás rutas.
- Formularios, modales, botones, tablas, iconos, labels, estados y espaciado consistentes; responsive según ancho útil, teclado y foco. Eliminar ruido visual y lenguaje técnico sin ocultar datos ni avisos necesarios. No introducir CTA falsas, botones rutinarios de actualizar ni confirmaciones redundantes.
- Reutiliza antes de crear. Elimina código/CSS obsoleto solo tras verificar consumidores. Dependencia especializada únicamente si resuelve una necesidad demostrable y cumple compatibilidad, licencia comercial, seguridad y coste de bundle; no instalar otro sistema UI completo.

## EFICIENCIA DE CONTEXTO Y PRUEBAS

Lee solo lo necesario para la decisión actual; profundiza en contratos y transiciones cuando haga falta. No vuelques repositorios, lockfiles, logs completos ni documentación a contexto. Reutiliza hallazgos vigentes. Razona a fondo sobre cambios compartidos y de riesgo; ejecuta migraciones mecánicas con el patrón validado. No impongas un límite artificial que comprometa cobertura.

Evita exploración duplicada, ensayo/error sin hipótesis y suites completas por cada ajuste. Pruebas focalizadas por corte; amplía cobertura si cambia una primitiva. Suite completa, build y lint al cierre; repite los gates afectados si corriges fallos. Usa agentes auxiliares solo para tareas independientes de beneficio claro, sin escrituras solapadas ni exploración redundante.

Mantén un único checkpoint UAX en `docs/CURRENT_STATE.md`: HEAD/diff, bloque y rutas, pruebas/evidencia, bloqueos y próxima acción exacta. Guarda antes de compactar contexto o terminar sesión. Al reanudar, verifica cambios y continúa desde ahí, sin repetir fases válidas. Reserva capacidad para validar y cerrar. Salidas breves, sin informes ni planes adicionales.

## INTEGRIDAD Y ALCANCE

No modificar backend, contratos API, esquema/migraciones, RLS, PostgreSQL, datos/configuración DEV, OAuth, SUNAT, S4 ni CI. Preservar autorización, tenant/sede, idempotencia, versiones, precisión decimal, recuperación, operaciones pendientes, PWA y sincronización. No inventar historial, filtros globales, indicadores o capacidades que el contrato no soporte; registrar brechas específicas y continuar.

Sin commit, push, merge ni despliegue automáticos. No reiniciar DEV sin autorización vigente y específica ni ejecutar escrituras reales de prueba sin permiso. Sigue las reglas operativas del MD.

## CRITERIO DE TERMINACIÓN Y ENTREGA

Usa la matriz real de rutas/roles del MD. Ningún módulo se considera verificado solo por cambio de color o Vitest. Comprueba patrones, recorridos completos, estados, densidad (objetivo >=4 filas a 1366×768 en directorios ordinarios), responsive, accesibilidad, solicitudes HTTP y bundle. Compara antes/después con mismos fixtures, rol, viewport y zoom; distingue pruebas sintéticas de navegador real y aceptación del operador.

Ejecuta `npm run build`, `npm run lint`, `npm run test` en frontend y `git diff --check` sobre el árbol final. Actualiza `frontend/AGENTS.md` sin duplicar reglas y `docs/CURRENT_STATE.md` con evidencia. Identifica assets compilados y servidos: si DEV sigue anterior, informa `BUILD_LISTO_PENDIENTE_REINICIO`, no simules aceptación.

Entrega una conclusión consolidada: rutas/módulos y primitivas, evidencia visual y recorridos, PASS/FAIL/SKIP reales, clics/GET/POST/bundle comparables, brechas y bloqueos, build y estado DEV, GO/NO-GO técnico, visual y de publicación por separado. Marca pendientes no verificados; no declares producción lista.

Empieza por la implementación. No respondas únicamente con un plan ni abras una nueva fase de auditoría.
