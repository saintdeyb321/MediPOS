import { BrowserRouter } from 'react-router'
import { RuntimeProvider } from './providers/runtime'
import type { Runtime } from '../features/access'
import { AppRoutes } from './router/routes'
import { HttpClient } from '../shared/api/http'
import { createMediPosService } from '../features/access'
import { OfflineBoundary } from './OfflineBoundary'
const http = new HttpClient()
const runtime: Runtime = { service: createMediPosService(http), http, preview: false }
export default function App() {
  return <RuntimeProvider runtime={runtime}><BrowserRouter><OfflineBoundary /><AppRoutes /></BrowserRouter></RuntimeProvider>
}
