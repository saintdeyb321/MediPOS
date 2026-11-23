# MediPOS frontend

PWA React/TypeScript con Vite, TanStack Query, React Router y Tailwind. El [estado del MVP](../docs/CURRENT_STATE.md) indica los recorridos pendientes de aceptación.

Desde `frontend/`, con Node 24 (versión local observada 24.18.1):

```powershell
npm ci
npm run build
npm run lint
npm run test -- src/app/router/EntryRouter.test.tsx src/features/platform/PlatformPage.test.tsx
```

El build genera `dist/`. Arranca el [backend DEV](../backend/README.md#arrancar-dev) con el perfil `https-local` para usar React y sus APIs bajo el mismo origen. Vite dev sirve para editar la UI. El flujo Google y las simulaciones usan [localhost:7243](https://localhost:7243/).

`src/app` compone rutas, layouts y providers. `src/features` organiza flujos y clientes por módulo. `src/shared` contiene HTTP, componentes y utilidades comunes. La [referencia histórica del frontend](README.history.md) conserva evidencias, bloqueos anteriores y recorridos históricos.
