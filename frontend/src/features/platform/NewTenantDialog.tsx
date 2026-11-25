import { useState } from 'react'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { FormFooter, InlineHelp } from '../../shared/ui/FormLayout'
import type { TrialInput } from './api'

export function NewTenantForm({ locked, create, dirty }: { locked: boolean; create: (input: TrialInput) => Promise<boolean | undefined>; dirty: (value: boolean) => void }) {
  const [name, setName] = useState(''); const [email, setEmail] = useState(''); const [branches, setBranches] = useState(2)
  const validName = name.trim().length > 0 && name.trim().length <= 256
  const validEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()) && email.trim().length <= 320
  const validLicense = Number.isInteger(branches) && branches >= 1 && branches <= 5
  function save() {
    if (locked || !validName || !validEmail || !validLicense) return
    const start = new Date()
    void create({ tradingName: name.trim(), ownerEmail: email.trim(), startsAt: start.toISOString(), expiresAt: new Date(start.getTime() + 30 * 86400000).toISOString(), maxBranches: branches, status: 'trial' })
  }
  return <form className="operation-body" onSubmit={event => event.preventDefault()}>
    <fieldset disabled={locked}>
      <div className="form-section-fields">
        <TextField label="Nombre comercial" value={name} maxLength={256} onChange={e => { setName(e.target.value); dirty(Boolean(e.target.value || email || branches !== 2)) }} />
        <TextField label="Gmail del propietario" type="email" value={email} maxLength={320} autoComplete="off" onChange={e => { setEmail(e.target.value); dirty(Boolean(name || e.target.value || branches !== 2)) }} hint="Verifica el Gmail exacto antes de crear la botica. Puede ser una cuenta que aún no ingresó a MediPOS." />
        <TextField label="Máximo de sucursales" type="number" min={1} max={5} step={1} value={branches} onChange={e => { setBranches(Number(e.target.value)); dirty(Boolean(name || email || Number(e.target.value) !== 2)) }} />
        <dl className="summary-grid"><div><dt>Botica</dt><dd>{name.trim() || 'Por indicar'}</dd></div><div><dt>Propietario pendiente</dt><dd>{email.trim() || 'Por indicar'}</dd></div><div><dt>Licencia</dt><dd>Trial · 30 días desde la creación · {validLicense ? branches : '—'} sucursales</dd></div></dl>
        <p>La botica, licencia y asignación pendiente se guardarán juntas. El primer login Google verificado vinculará al propietario.</p>
      </div>
    </fieldset>
    <FormFooter><Button disabled={locked || !validName || !validEmail || !validLicense} onClick={save}>Crear botica y asignación pendiente</Button>
    </FormFooter>
    <InlineHelp title="¿Cómo recibe acceso el propietario?">Comparte la dirección habitual de MediPOS. No necesita invitación, identificador ni un ingreso previo. Ser Owner no concede autoridad de plataforma.</InlineHelp>
  </form>
}
