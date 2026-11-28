import { useQuery } from '@tanstack/react-query'
import { useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { catalogApi } from './api'
import type { Category } from './api'

export function useCategories() {
  const w = useWorkspace()
  const { http } = useRuntime()
  return useQuery({ queryKey: scopedKey(w, 'catalog-categories', w.session.userId, w.access.membershipId), queryFn: async ({ signal }) => {
    if (!http) throw new ApiError(0, 'client.https_required')
    const result: Category[] = []
    for (let page = 0; page <= 10000; page += 100) {
      const rows = await catalogApi(http).categories(w, page, 100, signal); result.push(...rows)
      if (rows.length < 100) return result
    }
    throw new ApiError(0, 'catalog.category_page_limit')
  }, enabled: Boolean(http), retry: false, staleTime: 0 })
}
