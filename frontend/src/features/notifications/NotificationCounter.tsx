import { useQuery } from '@tanstack/react-query'
import { useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { notificationsApi } from './api'

export function NotificationCounter() {
  const w = useWorkspace(); const { http } = useRuntime()
  // A branch is always explicit here, including for staff. The count is per membership.
  const query = useQuery({ queryKey: scopedKey(w, 'notification-count', w.session.userId, w.access.membershipId, w.branchId), queryFn: ({ signal }) => notificationsApi(http!).count(w, w.branchId, signal), enabled: Boolean(http), retry: false, staleTime: 30000, refetchOnWindowFocus: false })
  return query.data && !query.isError && !query.isFetching ? <small aria-label={`${query.data.literal} notificaciones no leídas en esta sucursal`}>{query.data.literal}</small> : null
}
