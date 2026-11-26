# MediPOS — frontend

Complementa el [AGENTS.md raíz](../AGENTS.md). Mantén la dirección `app → features → shared` y la API pública de cada feature. Reutiliza React Router, TanStack Query, Tailwind y los componentes de `shared/ui`. La interfaz usa español del Perú, admite teclado y distingue carga, vacío, error y recuperación.

- Contrasta DTO, rutas y Problem Details con la API real. Usa el cliente HTTP común, HTTPS same-origin, cookies de sesión y CSRF. El backend decide permisos, importes, stock y estado.
- Separa caché y operaciones por sesión, tenant y sucursal. Al cambiar contexto, cancela solicitudes obsoletas. Conserva decimales exactos, expectedVersion y claves de idempotencia donde existan. Recupera la intención original ante un resultado incierto y evita repetir escrituras.
- Mantén los límites de la PWA y su contingencia offline. Conserva focus, accesibilidad y estados de formularios durante una operación incierta.
- Usa build, lint y Vitest focalizados según el cambio. Registra la aceptación Chrome real aparte de pruebas con dobles de transporte. Consulta el [README operativo](README.md).

## Diseño y frescura de vistas

- Aplica [Blue Studio](../docs/MEDIPOS_FRONTEND_BLUE_DESIGN_MASTER.md) con los tokens y primitivas existentes. Usa el degradado en navegación y bienvenida, tablas semánticas con scroll local y color de estado acompañado de texto. Respeta movimiento reducido. Usa español de botica, títulos cortos, una acción principal por tarea y formularios con etiquetas visibles. Muestra acciones y datos solo si existe el contrato.
- Corrige el reflow en las primitivas primero. Comprueba 320 px, móvil, tablet y escritorio en navegador. Conserva foco, teclado, objetivos táctiles, scroll de tablas contenido e impresión. Registra vistas pendientes por separado de Vitest.
- Carga por contexto y revalida en foco o reconexión según frescura y seguridad. Usa el acuse validado o invalida claves relacionadas. Reserva los controles manuales para errores, recuperación, filtros aplicados, verificación y acciones de sincronización. Conserva el bloqueo de reenvío ante resultado incierto.
