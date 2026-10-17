import { useEffect } from 'react'
import { useLocation } from 'react-router'
import { useRuntime } from '../features/access'
import { offlineStorage } from '../features/offline'

export function OfflineBoundary() {
  const { pathname } = useLocation(); const { http } = useRuntime()
  useEffect(() => {
    if (import.meta.env.PROD && location.protocol === 'https:' && 'serviceWorker' in navigator) {
      // Failure leaves ordinary online behavior intact; no API responses are cached.
      void navigator.serviceWorker.register('/sw.js', { scope: '/', updateViaCache: 'none' }).catch(() => undefined)
    }
  }, [])
  useEffect(() => http?.subscribeUnauthorized(() => { void offlineStorage.lock().catch(() => undefined) }), [http])
  useEffect(() => {
    let current = true
    void offlineStorage.lease().then(lease => {
      if (!current || !lease || pathname === '/contingencia') return
      const base = `/boticas/${lease.tenantId}/sucursales/${lease.branchId}`
      if (pathname !== base && !pathname.startsWith(base + '/')) return offlineStorage.lock()
    }).catch(() => undefined)
    return () => { current = false }
  }, [pathname])
  return null
}
