import { useState } from 'react'
import { BrowserRouter } from 'react-router'
import { DevelopmentBanner } from '../development/DevelopmentBanner'
import { RuntimeProvider } from './providers/runtime'
import type { Runtime } from '../features/access'
import { AppRoutes } from './router/routes'
import { HttpClient } from '../shared/api/http'
import { createMediPosService } from '../features/access'
import type { Scenario } from '../types/development'
const http = new HttpClient()
const realRuntime: Runtime = { service: createMediPosService(http), http, preview: false }
export default function App() {
  const [runtime, setRuntime] = useState(realRuntime)
  const [scenario, setScenario] = useState<Scenario>('owner')
  const [revision, setRevision] = useState(0)
  async function preview(next: Scenario) {
    if (!import.meta.env.DEV) return
    const { createPreviewService } = await import('../development/fixtures')
    window.history.replaceState(null, '', '/')
    setScenario(next)
    setRuntime({ service: createPreviewService(next), preview: true, initiallyExpired: next === 'expired' })
    setRevision(value => value + 1)
  }
  function exitPreview() {
    window.history.replaceState(null, '', '/')
    setRuntime(realRuntime)
    setRevision(value => value + 1)
  }
  return <RuntimeProvider key={revision} runtime={runtime}><BrowserRouter>
    {import.meta.env.DEV && runtime.preview && <DevelopmentBanner scenario={scenario} onScenario={next => void preview(next)} onExit={exitPreview} />}
    <AppRoutes onPreview={import.meta.env.DEV ? () => void preview('owner') : undefined} />
  </BrowserRouter></RuntimeProvider>
}
