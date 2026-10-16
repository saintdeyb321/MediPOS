import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { QueryClient } from '@tanstack/react-query'
import { screen, waitFor } from '@testing-library/react'
import { expect, it } from 'vitest'
import { TransfersPage } from './TransfersPage'
import { CashTransfersPage } from './CashTransfersPage'
import { cashServer, sentCash } from './cash.test.support'
import { goodsSettled, goodsSuperseded } from './model'
import { id, mountTransfers, transfer, transferServer, workspace } from './transfers.test.support'

it('does not interpret a concurrent cancellation as a successful approval or dispatch', () => {
  const cancelled = { ...transfer, status: 'cancelled' as const, events: [...transfer.events, { ...transfer.events[0]!, id: id(99), eventType: 'cancelled' as const }] }
  expect(goodsSettled(cancelled, 'approve')).toBe(false); expect(goodsSettled(cancelled, 'dispatch')).toBe(false); expect(goodsSettled(cancelled, 'cancel')).toBe(true)
  expect(goodsSuperseded(cancelled, 'approve')).toBe(true); expect(goodsSuperseded({ ...transfer, status: 'approved' }, 'dispatch')).toBe(false)
  expect(goodsSuperseded({ ...transfer, status: 'in_transit' }, 'cancel')).toBe(true); expect(goodsSuperseded({ ...transfer, status: 'approved' }, 'cancel')).toBe(false)
})
function SeedCache({ capture }: { capture: (value: QueryClient) => void }) {
  const client = useQueryClient()
  useEffect(() => { client.setQueryData(['tenant', workspace.tenantId, workspace.branchId, 'stock'], []); client.setQueryData(['tenant', workspace.tenantId, workspace.branchId, 'current-cash'], null); capture(client) }, [client, capture])
  return null
}
it('reconciles a terminal receive loaded from the saved URL and invalidates the retained branch stock cache', async () => {
  const fake = transferServer(); fake.state.records = [{ ...transfer, status: 'received', events: [...transfer.events, { ...transfer.events[0]!, id: id(98), eventType: 'received' }] }]; let client!: QueryClient
  mountTransfers(fake, <><SeedCache capture={value => { client = value }} /><TransfersPage /></>, '?goodsId=' + transfer.id + '&goodsPending=receive')
  await screen.findByRole('heading', { name: 'Transferencia: RECIBIDA' })
  await waitFor(() => expect(client.getQueryState(['tenant', workspace.tenantId, workspace.branchId, 'stock'])?.isInvalidated).toBe(true))
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('goodsPending')); expect(fake.writes()).toHaveLength(0)
})
it('resolves a dispatch made impossible by a concurrent terminal cancellation without claiming dispatch success', async () => {
  const fake = transferServer(); fake.state.records = [{ ...transfer, status: 'cancelled', events: [...transfer.events, { ...transfer.events[0]!, id: id(97), eventType: 'cancelled' }] }]
  mountTransfers(fake, <TransfersPage />, '?goodsId=' + transfer.id + '&goodsPending=dispatch')
  await screen.findByRole('heading', { name: 'Transferencia: CANCELADA' })
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('goodsPending'))
  expect(screen.getByText(/Otra transición impide la operación pendiente/)).toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('recovers a received cash transfer from the saved URL and invalidates current cash without another POST', async () => {
  const fake = cashServer(); fake.cash.records = [{ ...sentCash, status: 'received', destinationCashSessionId: id(302), receivedAt: sentCash.dispatchedAt, receivedByActorId: workspace.session.userId }]; let client!: QueryClient
  mountTransfers(fake, <><SeedCache capture={value => { client = value }} /><CashTransfersPage /></>, '?cashTransferId=' + sentCash.id + '&cashTransferPending=receive')
  await screen.findByRole('heading', { name: 'Sencillo: RECIBIDO' })
  await waitFor(() => expect(client.getQueryState(['tenant', workspace.tenantId, workspace.branchId, 'current-cash'])?.isInvalidated).toBe(true))
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('cashTransferPending')); expect(fake.writes()).toHaveLength(0)
})
