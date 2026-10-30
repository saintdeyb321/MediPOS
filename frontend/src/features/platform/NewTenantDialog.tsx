import { useState } from 'react'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Steps } from '../../shared/ui/Steps'
import { FormFooter, InlineHelp } from '../../shared/ui/FormLayout'
import type { TrialInput } from './api'

export function NewTenantForm({ locked, create, dirty }: { locked: boolean; create: (input: TrialInput) => Promise<void>; dirty: (value: boolean) => void }) {
  const [step, setStep] = useState(0)
  const [name, setName] = useState(''); const [email, setEmail] = useState(''); const [branches, setBranches] = useState(2)
  const [reviewed, setReviewed] = useState(false)
  const validName = name.trim().length > 0 && name.trim().length <= 256
  const validEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()) && email.trim().length <= 320
  const validLicense = Number.isInteger(branches) && branches >= 1 && branches <= 5
  return <form className="operation-body" onSubmit={event => {
    event.preventDefault()
    if (locked || step !== 2 || !reviewed || !validName || !validEmail || !validLicense) return
    const start = new Date(); setReviewed(false)
    void create({ tradingName: name.trim(), ownerEmail: email.trim(), startsAt: start.toISOString(), expiresAt: new Date(start.getTime() + 30 * 86400000).toISOString(), maxBranches: branches, status: 'trial' })
  }}>
    <Steps labels={['Negocio', 'Propietario y licencia', 'Revisar y crear']} current={step} focusOnChange label="Alta de botica" />
    <fieldset disabled={locked}>
      <div hidden={step !== 0} className="form-section-fields"><TextField label="Nombre comercial" value={name} maxLength={256} onChange={e => { setReviewed(false); setName(e.target.value); dirty(Boolean(e.target.value || email || branches !== 2)) }} /></div>
      <div hidden={step !== 1} className="form-section-fields">
        <TextField label="Gmail del propietario" type="email" value={email} maxLength={320} autoComplete="off" onChange={e => { setReviewed(false); setEmail(e.target.value); dirty(Boolean(name || e.target.value || branches !== 2)) }} hint="Puede ser una cuenta que aún no ingresó a MediPOS. Google comprobará su correo al iniciar sesión." />
        <TextField label="Máximo de sucursales" type="number" min={1} max={5} step={1} value={branches} onChange={e => { setReviewed(false); setBranches(Number(e.target.value)); dirty(Boolean(name || email || Number(e.target.value) !== 2)) }} />
        <p>Trial de 30 días desde la creación, no desde el primer ingreso del propietario.</p>
      </div>
      <div hidden={step !== 2} className="form-section-fields"><h3>{name}</h3><dl className="summary-grid"><div><dt>Propietario pendiente</dt><dd>{email.trim()}</dd></div><div><dt>Licencia</dt><dd>Trial · 30 días · {branches} sucursales</dd></div></dl>
        <p>La botica, licencia y asignación pendiente se guardarán juntas. El primer login Google verificado vinculará al propietario.</p>
        <label><input type="checkbox" checked={reviewed} onChange={e => setReviewed(e.target.checked)} /> Confirmo el negocio, Gmail y licencia.</label>
      </div>
    </fieldset>
    <FormFooter>{step > 0 && <Button variant="secondary" disabled={locked} onClick={() => setStep(step - 1)}>Anterior</Button>}
      {step < 2 ? <Button disabled={locked || (step === 0 ? !validName : !validEmail || !validLicense)} onClick={() => setStep(step + 1)}>Continuar</Button>
        : <Button type="submit" disabled={locked || !reviewed || !validName || !validEmail || !validLicense}>Crear botica y asignación pendiente</Button>}
    </FormFooter>
    <InlineHelp title="¿Cómo recibe acceso el propietario?">Comparte la dirección habitual de MediPOS. No necesita invitación, identificador ni un ingreso previo. Ser Owner no concede autoridad de plataforma.</InlineHelp>
  </form>
}
