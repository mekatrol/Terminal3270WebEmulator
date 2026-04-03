export type AuthState = {
  isAuthenticated: boolean
  hasRequiredRole: boolean
  displayName: string
  roles: string[]
}

const demoAuthState: AuthState = {
  isAuthenticated: true,
  hasRequiredRole: true,
  displayName: 'Contoso Operator',
  roles: ['Terminal.User'],
}

export interface BrowserAuthService {
  getState(): AuthState
  isAuthorized(): boolean
}

class DemoBrowserAuthService implements BrowserAuthService {
  getState(): AuthState {
    return demoAuthState
  }

  isAuthorized(): boolean {
    const state = this.getState()
    return state.isAuthenticated && state.hasRequiredRole
  }
}

const authService = new DemoBrowserAuthService()

export function getBrowserAuthService(): BrowserAuthService {
  return authService
}
