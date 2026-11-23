# MediPOS — frontend

Complementa el [AGENTS.md raíz](../AGENTS.md). Mantén la dirección `app → features → shared` y la API pública de cada feature. Reutiliza React Router, TanStack Query, Tailwind y los componentes de `shared/ui`. La interfaz usa español del Perú, admite teclado y distingue carga, vacío, error y recuperación.

- Contrasta DTO, rutas y Problem Details con la API real. Usa el cliente HTTP común, HTTPS same-origin, cookies de sesión y CSRF. El backend decide permisos, importes, stock y estado.
- Separa caché y operaciones por sesión, tenant y sucursal. Al cambiar contexto, cancela solicitudes obsoletas. Conserva decimales exactos, expectedVersion y claves de idempotencia donde existan. Recupera la intención original ante un resultado incierto y evita repetir escrituras.
- Mantén los límites de la PWA y su contingencia offline. Conserva focus, accesibilidad y estados de formularios durante una operación incierta.
- Usa build, lint y Vitest focalizados según el cambio. Registra la aceptación Chrome real aparte de pruebas con dobles de transporte. Consulta el [README operativo](README.md).
