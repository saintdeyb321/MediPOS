import { createContext } from 'react'
import type { MediPosService, Session, Branch, AccessContext } from '../types/contracts'
import type { HttpClient } from '../services/http'
export interface Runtime { service: MediPosService; http?: HttpClient; preview: boolean; initiallyExpired?: boolean }
export const RuntimeContext = createContext<Runtime | null>(null)
export const AuthContext = createContext<{ expired: boolean; reset: () => void }>({ expired: false, reset: () => {} })
export interface Workspace { tenantId: string; branchId: string; tenantName: string; branch: Branch; access: AccessContext; session: Session; base: string }
export const WorkspaceContext = createContext<Workspace | null>(null)
