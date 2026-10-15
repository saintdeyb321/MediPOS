import { useState } from 'react'
import { BrowserRouter } from 'react-router'
import { FlaskConical, X } from 'lucide-react'
import { RuntimeProvider } from './app/runtime'
import type { Runtime } from './app/contexts'
import { AppRoutes } from './app/routes'
import { HttpClient } from './services/http'
import { createMediPosService } from './services/medipos'
import { scenarios } from './types/development'
import type { Scenario } from './types/development'
const http = new HttpClient()
const realRuntime: Runtime = { service: createMediPosService(http), http, preview: false }
export default function App() {
  const [runtime, setRuntime] = useState(realRuntime)
  const [scenario, setScenario] = useState<Scenario>('owner')
  const [revision, setRevision] = useState(0)
  async function preview(next: Scenario) {
    if (!import.meta.env.DEV) return
    const { createPreviewService } = await import('./development/fixtures')
    window.history.replaceState(null, '', '/')
    setScenario(next); setRuntime({ service: createPreviewService(next), preview: true, initiallyExpired: next === 'expired' }); setRevision(value => value + 1)
  }
  function exitPreview() {
    window.history.replaceState(null, '', '/'); setRuntime(realRuntime); setRevision(value => value + 1)
  }
  return <RuntimeProvider key={revision} runtime={runtime}><BrowserRouter>
    {import.meta.env.DEV && runtime.preview && <div className="development-banner"><span><FlaskConical size={16} /><strong>Vista de desarrollo</strong><span>Datos sintéticos · sin sesión real</span></span><div><label htmlFor="preview-scenario" className="sr-only">Escenario de desarrollo</label><select id="preview-scenario" value={scenario} onChange={e => void preview(e.target.value as Scenario)}>{Object.entries(scenarios).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select><button onClick={exitPreview} aria-label="Salir de la vista de desarrollo"><X size={17} /></button></div></div>}
    <AppRoutes onPreview={import.meta.env.DEV ? () => void preview('owner') : undefined} />
  </BrowserRouter></RuntimeProvider>
}
