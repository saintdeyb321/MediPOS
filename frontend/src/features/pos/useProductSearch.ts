import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useRuntime, useWorkspace } from '../access'
import { catalogApi } from '../catalog'
import { scopedKey } from '../../shared/api/contracts'

export function useProductSearch(input: string) {
  const { http } = useRuntime()
  const { tenantId, branchId } = useWorkspace()
  const text = input.trim()
  const [debounced, setDebounced] = useState('')
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(text), 300)
    return () => window.clearTimeout(timer)
  }, [text])
  const ready = Boolean(http) && text.length > 0 && text === debounced
  const query = useQuery({
    // Change the observer immediately while typing, aborting its previous request.
    queryKey: scopedKey({ tenantId, branchId }, 'product-search', text),
    queryFn: ({ signal }) => {
      if (!http) throw new Error('Real HTTP client required')
      return catalogApi(http).search({ tenantId, branchId }, text, 20, signal)
    },
    enabled: ready,
    staleTime: 0,
    retry: false,
  })
  return { query, ready, text, connected: Boolean(http) }
}
