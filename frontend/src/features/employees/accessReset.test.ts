import { QueryClient, QueryObserver } from '@tanstack/react-query'
import { expect, it } from 'vitest'
import { resetOwnWorkspace } from './accessReset'

it('clears the allowed context before reevaluation completes and retires only that tenant cache', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const key = ['tenant', 'current', 'branch', 'access', '/empleados']
  client.setQueryData(key, { isAllowed: true })
  client.setQueryData(['tenant', 'current', 'branch', 'product-search'], { private: true })
  client.setQueryData(['tenant', 'other', 'branch', 'access'], { isAllowed: true })
  let finish!: (value: { isAllowed: boolean }) => void
  const observer = new QueryObserver(client, { queryKey: key, queryFn: () => new Promise<{ isAllowed: boolean }>(resolve => { finish = resolve }), staleTime: Infinity })
  const unsubscribe = observer.subscribe(() => {})
  resetOwnWorkspace(client, 'current')
  expect(observer.getCurrentResult().isPending).toBe(true)
  expect(observer.getCurrentResult().data).toBeUndefined()
  expect(client.getQueryData(['tenant', 'current', 'branch', 'product-search'])).toBeUndefined()
  expect(client.getQueryData(['tenant', 'other', 'branch', 'access'])).toEqual({ isAllowed: true })
  finish({ isAllowed: false }); await observer.refetch()
  expect(observer.getCurrentResult().data).toEqual({ isAllowed: false })
  unsubscribe(); client.clear()
})
