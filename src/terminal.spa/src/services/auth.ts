export type AuthState = {
  isAuthenticated: boolean
  hasRequiredRole: boolean
  displayName: string
  roles: string[]
}

type OpenIdConfiguration = {
  authorization_endpoint: string
  token_endpoint: string
  end_session_endpoint?: string
}

type StoredAuthorizationRequest = {
  codeVerifier: string
  nonce: string
  returnToPath: string
  state: string
}

type StoredAuthSession = {
  accessToken: string
  expiresAtUtc: string
  idToken: string | null
  refreshToken: string | null
  roles: string[]
  displayName: string
}

const authorizationRequestStorageKey = 'terminal.oidc.authorizationRequest'
const authSessionStorageKey = 'terminal.oidc.session'
const postLogoutReturnToPathStorageKey = 'terminal.oidc.postLogoutReturnToPath'
const defaultAuthority = 'http://localhost:5099/mock-entra/terminaltenant/v2.0'
const defaultClientId = 'terminal-spa'
const defaultScopes = [
  'openid',
  'profile',
  'email',
  'offline_access',
  'api://terminal-api/Terminal.Access',
  'api://terminal-api/Terminal.Admin',
].join(' ')
const testAuthState: AuthState = {
  isAuthenticated: true,
  hasRequiredRole: true,
  displayName: 'Mock Operator',
  roles: ['Terminal.User', 'Terminal.Admin', 'Server.Admin'],
}

export interface BrowserAuthService {
  beginSignIn(returnToPath?: string): Promise<void>
  completeSignInCallback(url?: string): Promise<string>
  ensureSession(): Promise<boolean>
  getAccessToken(): Promise<string | null>
  getState(requiredRole?: string): AuthState
  isAuthorized(requiredRole?: string): boolean
  signOut(returnToPath?: string): Promise<void>
}

function isTestMode(): boolean {
  return import.meta.env.MODE === 'test'
}

function base64UrlEncode(bytes: Uint8Array): string {
  let value = ''

  for (const byte of bytes) {
    value += String.fromCharCode(byte)
  }

  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/u, '')
}

function base64UrlDecode(value: string): string {
  const normalized = value.replace(/-/g, '+').replace(/_/g, '/')
  const padded = normalized.padEnd(normalized.length + ((4 - (normalized.length % 4)) % 4), '=')
  return atob(padded)
}

function createRandomString(length = 32): string {
  const bytes = new Uint8Array(length)
  crypto.getRandomValues(bytes)
  return base64UrlEncode(bytes)
}

async function createPkceChallenge(codeVerifier: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(codeVerifier))
  return base64UrlEncode(new Uint8Array(digest))
}

function getStorageItem<T>(storageKey: string): T | null {
  const rawValue =
    window.sessionStorage.getItem(storageKey) ?? window.localStorage.getItem(storageKey)

  if (!rawValue) {
    return null
  }

  return JSON.parse(rawValue) as T
}

function setSessionStorageItem(storageKey: string, value: unknown): void {
  window.sessionStorage.setItem(storageKey, JSON.stringify(value))
}

function setLocalStorageItem(storageKey: string, value: unknown): void {
  window.localStorage.setItem(storageKey, JSON.stringify(value))
}

function removeStorageItem(storageKey: string): void {
  window.sessionStorage.removeItem(storageKey)
  window.localStorage.removeItem(storageKey)
}

function resolveDefaultReturnToPath(): string {
  const storedReturnToPath = getStorageItem<string>(postLogoutReturnToPathStorageKey)

  if (typeof storedReturnToPath === 'string' && storedReturnToPath.startsWith('/')) {
    return storedReturnToPath
  }

  return `${window.location.pathname}${window.location.search}${window.location.hash}`
}

function resolveAuthority(): string {
  return (import.meta.env.VITE_OIDC_AUTHORITY || defaultAuthority).replace(/\/$/u, '')
}

function resolveClientId(): string {
  return import.meta.env.VITE_OIDC_CLIENT_ID || defaultClientId
}

function resolveScopes(): string {
  return import.meta.env.VITE_OIDC_SCOPES || defaultScopes
}

function resolveRedirectUri(): string {
  return `${window.location.origin}/auth/callback`
}

function resolvePostLogoutRedirectUri(): string {
  return `${window.location.origin}/`
}

function resolveDiscoveryUrl(): string {
  const configuredDiscoveryUrl = import.meta.env.VITE_OIDC_DISCOVERY_URL

  if (configuredDiscoveryUrl) {
    return configuredDiscoveryUrl
  }

  return `${resolveAuthority()}/.well-known/openid-configuration`
}

function parseJwtPayload(token: string): Record<string, unknown> {
  const segments = token.split('.')

  if (segments.length < 2) {
    throw new Error('The identity provider returned an invalid JWT.')
  }

  return JSON.parse(base64UrlDecode(segments[1] ?? ''))
}

function toStringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === 'string')
    : []
}

function readSession(): StoredAuthSession | null {
  const session = getStorageItem<StoredAuthSession>(authSessionStorageKey)

  if (!session) {
    return null
  }

  if (new Date(session.expiresAtUtc).getTime() <= Date.now()) {
    removeStorageItem(authSessionStorageKey)
    return null
  }

  return session
}

function buildAuthState(session: StoredAuthSession | null, requiredRole?: string): AuthState {
  if (!session) {
    return {
      isAuthenticated: false,
      hasRequiredRole: false,
      displayName: 'Anonymous',
      roles: [],
    }
  }

  return {
    isAuthenticated: true,
    hasRequiredRole: requiredRole ? session.roles.includes(requiredRole) : true,
    displayName: session.displayName,
    roles: session.roles,
  }
}

class DemoBrowserAuthService implements BrowserAuthService {
  async beginSignIn(): Promise<void> {}

  async completeSignInCallback(): Promise<string> {
    return '/terminal'
  }

  async ensureSession(): Promise<boolean> {
    return true
  }

  async getAccessToken(): Promise<string | null> {
    return 'test-access-token'
  }

  getState(): AuthState {
    return testAuthState
  }

  isAuthorized(): boolean {
    return true
  }

  async signOut(): Promise<void> {}
}

class OidcBrowserAuthService implements BrowserAuthService {
  private discoveryPromise: Promise<OpenIdConfiguration> | null = null

  async beginSignIn(returnToPath = resolveDefaultReturnToPath()): Promise<void> {
    const discovery = await this.getDiscoveryDocument()
    const state = createRandomString()
    const nonce = createRandomString()
    const codeVerifier = createRandomString(48)
    const codeChallenge = await createPkceChallenge(codeVerifier)
    const authorizationRequest: StoredAuthorizationRequest = {
      codeVerifier,
      nonce,
      returnToPath,
      state,
    }

    setSessionStorageItem(authorizationRequestStorageKey, authorizationRequest)

    const authorizationUrl = new URL(discovery.authorization_endpoint)
    authorizationUrl.searchParams.set('client_id', resolveClientId())
    authorizationUrl.searchParams.set('redirect_uri', resolveRedirectUri())
    authorizationUrl.searchParams.set('response_type', 'code')
    authorizationUrl.searchParams.set('scope', resolveScopes())
    authorizationUrl.searchParams.set('state', state)
    authorizationUrl.searchParams.set('nonce', nonce)
    authorizationUrl.searchParams.set('code_challenge', codeChallenge)
    authorizationUrl.searchParams.set('code_challenge_method', 'S256')

    window.location.assign(authorizationUrl.toString())
  }

  async completeSignInCallback(url = window.location.href): Promise<string> {
    const callbackUrl = new URL(url)
    const error = callbackUrl.searchParams.get('error')
    const errorDescription = callbackUrl.searchParams.get('error_description')

    if (error) {
      throw new Error(errorDescription || `Authentication failed with error '${error}'.`)
    }

    const code = callbackUrl.searchParams.get('code')
    const state = callbackUrl.searchParams.get('state')
    const authorizationRequest = getStorageItem<StoredAuthorizationRequest>(
      authorizationRequestStorageKey,
    )

    if (!code || !state || !authorizationRequest) {
      throw new Error('The authentication callback is missing required state.')
    }

    if (authorizationRequest.state !== state) {
      throw new Error(
        'The authentication callback state does not match the original sign-in request.',
      )
    }

    const discovery = await this.getDiscoveryDocument()
    const tokenResponse = await this.requestToken(
      discovery.token_endpoint,
      new URLSearchParams({
        grant_type: 'authorization_code',
        client_id: resolveClientId(),
        code,
        redirect_uri: resolveRedirectUri(),
        code_verifier: authorizationRequest.codeVerifier,
      }),
    )

    this.persistTokenResponse(tokenResponse)
    removeStorageItem(authorizationRequestStorageKey)
    removeStorageItem(postLogoutReturnToPathStorageKey)
    return authorizationRequest.returnToPath || '/terminal'
  }

  async ensureSession(): Promise<boolean> {
    if (readSession()) {
      return true
    }

    const expiredSession = getStorageItem<StoredAuthSession>(authSessionStorageKey)

    if (!expiredSession?.refreshToken) {
      removeStorageItem(authSessionStorageKey)
      return false
    }

    const discovery = await this.getDiscoveryDocument()

    try {
      const tokenResponse = await this.requestToken(
        discovery.token_endpoint,
        new URLSearchParams({
          grant_type: 'refresh_token',
          client_id: resolveClientId(),
          refresh_token: expiredSession.refreshToken,
        }),
      )

      this.persistTokenResponse(tokenResponse)
      return true
    } catch (error) {
      console.warn('[auth] refresh token exchange failed, clearing stored session', error)
      removeStorageItem(authSessionStorageKey)
      return false
    }
  }

  async getAccessToken(): Promise<string | null> {
    const hasSession = await this.ensureSession()
    return hasSession ? (readSession()?.accessToken ?? null) : null
  }

  getState(requiredRole?: string): AuthState {
    return buildAuthState(readSession(), requiredRole)
  }

  isAuthorized(requiredRole?: string): boolean {
    const state = this.getState(requiredRole)
    return state.isAuthenticated && state.hasRequiredRole
  }

  async signOut(returnToPath = '/'): Promise<void> {
    const discovery = await this.getDiscoveryDocument()
    removeStorageItem(authSessionStorageKey)
    removeStorageItem(authorizationRequestStorageKey)
    setSessionStorageItem(postLogoutReturnToPathStorageKey, returnToPath)

    if (!discovery.end_session_endpoint) {
      window.location.assign(returnToPath)
      return
    }

    const logoutUrl = new URL(discovery.end_session_endpoint)
    logoutUrl.searchParams.set('post_logout_redirect_uri', resolvePostLogoutRedirectUri())
    logoutUrl.searchParams.set('state', returnToPath)
    window.location.assign(logoutUrl.toString())
  }

  private async getDiscoveryDocument(): Promise<OpenIdConfiguration> {
    this.discoveryPromise ??= fetch(resolveDiscoveryUrl(), {
      headers: {
        Accept: 'application/json',
      },
    }).then(async (response) => {
      if (!response.ok) {
        throw new Error(`Unable to load OpenID Connect discovery (${response.status}).`)
      }

      return (await response.json()) as OpenIdConfiguration
    })

    return await this.discoveryPromise
  }

  private async requestToken(
    tokenEndpoint: string,
    body: URLSearchParams,
  ): Promise<Record<string, unknown>> {
    const response = await fetch(tokenEndpoint, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/x-www-form-urlencoded',
      },
      body,
    })

    const payload = (await response.json()) as Record<string, unknown>

    if (!response.ok) {
      const errorDescription =
        typeof payload.error_description === 'string'
          ? payload.error_description
          : `Token request failed with status ${response.status}.`

      throw new Error(errorDescription)
    }

    return payload
  }

  private persistTokenResponse(tokenResponse: Record<string, unknown>): void {
    const accessToken =
      typeof tokenResponse.access_token === 'string' ? tokenResponse.access_token : null
    const refreshToken =
      typeof tokenResponse.refresh_token === 'string' ? tokenResponse.refresh_token : null
    const idToken = typeof tokenResponse.id_token === 'string' ? tokenResponse.id_token : null
    const expiresIn =
      typeof tokenResponse.expires_in === 'number'
        ? tokenResponse.expires_in
        : Number(tokenResponse.expires_in ?? 3600)

    if (!accessToken) {
      throw new Error('The identity provider did not return an access token.')
    }

    const payload = parseJwtPayload(idToken ?? accessToken)
    const displayName =
      typeof payload.name === 'string'
        ? payload.name
        : typeof payload.preferred_username === 'string'
          ? payload.preferred_username
          : 'Authenticated user'

    const session: StoredAuthSession = {
      accessToken,
      expiresAtUtc: new Date(Date.now() + Math.max(60, expiresIn) * 1000).toISOString(),
      idToken,
      refreshToken,
      roles: toStringArray(payload.roles),
      displayName,
    }

    setLocalStorageItem(authSessionStorageKey, session)
  }
}

const authService: BrowserAuthService = isTestMode()
  ? new DemoBrowserAuthService()
  : new OidcBrowserAuthService()

export function getBrowserAuthService(): BrowserAuthService {
  return authService
}

export async function authorizedFetch(
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<Response> {
  const accessToken = await authService.getAccessToken()

  if (!accessToken) {
    throw new Error('The current browser session is not authenticated.')
  }

  const headers = new Headers(init?.headers)
  headers.set('Authorization', `Bearer ${accessToken}`)

  return await fetch(input, {
    ...init,
    headers,
  })
}

export async function appendAccessTokenToUrl(url: string): Promise<string> {
  const accessToken = await authService.getAccessToken()

  if (!accessToken) {
    throw new Error('The current browser session is not authenticated.')
  }

  const resolvedUrl = new URL(url, window.location.origin)
  resolvedUrl.searchParams.set('access_token', accessToken)
  return resolvedUrl.toString()
}
