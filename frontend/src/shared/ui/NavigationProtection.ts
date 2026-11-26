import { createContext, useContext, useId, useLayoutEffect } from 'react'
type Protection = { dirty?: boolean; busy?: boolean; pending?: boolean }
export const NavigationRegistry = createContext<{ entries: Map<string, Protection>; notify: () => void } | null>(null)
export function useNavigationProtection(state: Protection) {
  const registry = useContext(NavigationRegistry); const id = useId(); const { dirty, busy, pending } = state
  // Route effects must see the protection state of the current commit.
  useLayoutEffect(() => {
    registry?.entries.set(id, { dirty, busy, pending }); registry?.notify()
    return () => { registry?.entries.delete(id); registry?.notify() }
  }, [registry, id, dirty, busy, pending])
}
