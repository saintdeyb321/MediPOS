import type { QueryClient } from '@tanstack/react-query'

export function resetOwnWorkspace(client: QueryClient, tenantId: string) {
  // Reset clears data synchronously: WorkspaceLayout's existing isPending/error guards
  // stop operations until fresh branch/access queries finish. Invalidate alone retains an allowed context.
  void client.cancelQueries({ queryKey: ['tenant', tenantId] })
  void client.resetQueries({ queryKey: ['tenant', tenantId] })
  void client.invalidateQueries({ queryKey: ['tenants'] })
}
