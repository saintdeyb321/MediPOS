import { createBrowserRouter, RouterProvider } from 'react-router'
import { RuntimeProvider } from './providers/runtime'
import type { Runtime } from '../features/access'
import { AppRoutes } from './router/routes'
import { HttpClient } from '../shared/api/http'
import { createMediPosService } from '../features/access'
import { OfflineBoundary } from './OfflineBoundary'
import { useSession } from '../features/access'
import { NavigationSafety } from '../shared/ui/NavigationSafety'
const http = new HttpClient()
const runtime: Runtime = { service: createMediPosService(http), http, preview: false }
function RuntimeRoutes() {
  const session = useSession()
  return <NavigationSafety identity={session.data?.userId}><OfflineBoundary /><AppRoutes /></NavigationSafety>
}
const router = createBrowserRouter([{ path: '*', element: <RuntimeRoutes /> }])
export default function App() {
  return <RuntimeProvider runtime={runtime}><RouterProvider router={router} /></RuntimeProvider>
}
