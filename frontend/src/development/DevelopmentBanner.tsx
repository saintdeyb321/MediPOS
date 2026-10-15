import { FlaskConical, X } from 'lucide-react'
import { scenarios } from '../types/development'
import type { Scenario } from '../types/development'
import { IconButton } from '../shared/ui/Button'

export function DevelopmentBanner({ scenario, onScenario, onExit }: { scenario: Scenario; onScenario: (scenario: Scenario) => void; onExit: () => void }) {
  return (
    <div className="development-banner">
      <span><FlaskConical size={18} aria-hidden="true" /><strong>Vista de desarrollo</strong><span>Datos sintéticos · sin sesión real</span></span>
      <div>
        <label htmlFor="preview-scenario" className="sr-only">Escenario de desarrollo</label>
        <select id="preview-scenario" value={scenario} onChange={event => onScenario(event.target.value as Scenario)}>
          {Object.entries(scenarios).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select>
        <IconButton label="Salir de la vista de desarrollo" onClick={onExit}><X size={18} /></IconButton>
      </div>
    </div>
  )
}
