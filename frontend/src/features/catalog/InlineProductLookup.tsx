import { useEffect, useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { SearchField } from '../../shared/ui/TextField'
import { catalogApi } from './api'

export type ProductChoice = { id: string; name: string }

export function InlineProductLookup({ onSelect, disabled }: {
  onSelect: (product: ProductChoice) => void; disabled?: boolean
}) {
  const w = useWorkspace()
  const { http } = useRuntime()
  const id = useId()
  const [text, setText] = useState('')
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const term = text.trim()
  useEffect(() => {
    if (!open || disabled || term.length < 2 || term === query) return
    const timer = window.setTimeout(() => setQuery(term), 250)
    return () => window.clearTimeout(timer)
  }, [term, query, open, disabled])
  const showing = !disabled && open && term.length >= 2
  const current = showing && term === query
  const results = useQuery({
    queryKey: scopedKey(w, 'product-search', w.session.userId, w.access.membershipId, 'inventory-picker', current ? query : ''),
    queryFn: ({ signal }) => {
      if (!http) throw new ApiError(0, 'client.https_required')
      return catalogApi(http).search(w, query, 20, signal)
    },
    enabled: Boolean(http && current), retry: false, staleTime: 30_000, refetchOnWindowFocus: false,
  })
  const matches = current && !results.isError ? results.data?.directMatches ?? [] : []
  useEffect(() => {
    if (current && active >= 0) document.getElementById(`${id}-${active}`)?.scrollIntoView?.({ block: 'nearest' })
  }, [active, current, id])
  function choose(index: number) {
    const row = matches[index]
    if (!row || disabled) return
    onSelect({ id: row.businessProductId, name: row.name })
    setText('')
    setQuery('')
    setActive(-1)
    setOpen(false)
  }
  function searchOrChoose() {
    if (disabled || term.length < 2) return
    if (active >= 0 && matches[active]) { choose(active); return }
    setOpen(true)
    setQuery(term)
  }
  return <form className="inline-product-lookup" role="search" onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget)) { setOpen(false); setActive(-1) }
  }} onSubmit={event => { event.preventDefault(); searchOrChoose() }}>
    <SearchField label="Buscar producto" placeholder="Nombre, ingrediente o código" role="combobox"
      value={text} maxLength={256} autoComplete="off" disabled={disabled || !http}
      aria-autocomplete="list" aria-expanded={showing} aria-controls={showing ? id : undefined}
      aria-activedescendant={showing && matches[active] ? `${id}-${active}` : undefined}
      onFocus={() => setOpen(true)} onChange={event => { setText(event.target.value); setOpen(true); setActive(-1) }}
      onKeyDown={event => {
        if (event.nativeEvent.isComposing) return
        if (event.key === 'Enter') { event.preventDefault(); searchOrChoose(); return }
        if (event.key === 'Escape' && open) { event.preventDefault(); setOpen(false); setActive(-1) }
        if (!matches.length || (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')) return
        event.preventDefault()
        setActive(index => event.key === 'ArrowDown'
          ? (index + 1) % matches.length : index <= 0 ? matches.length - 1 : index - 1)
      }} />
    {showing && <div className="inline-product-results">
      {current && results.isFetching && <p role="status">Buscando productos…</p>}
      {current && results.isError && <ApiErrorState error={results.error} title="No pudimos buscar productos" retry={() => void results.refetch()} />}
      {current && results.isSuccess && !matches.length && <p role="status">No hay coincidencias directas.</p>}
      <div id={id} role="listbox" aria-label="Coincidencias de productos">{matches.map((row, index) =>
        <div key={row.businessProductId} id={`${id}-${index}`} role="option" aria-selected={index === active}
          onMouseDown={event => event.preventDefault()} onClick={() => choose(index)}>
          <strong>{row.name}</strong> <small>{row.internalCode}</small>
        </div>)}</div>
    </div>}
  </form>
}
