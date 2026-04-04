export type AdminSession = {
  terminalSessionId: string
  createdDateTimeUtc: string
  isActive: boolean
  closedDateTimeUtc: string | null
  userId: string
  userName: string
}

type AdminSessionListResponse = {
  sessions: AdminSession[]
}

export type AdminSessionActionResponse = {
  selectedCount: number
  updatedCount: number
  removedCount: number
  skippedCount: number
  message: string
}

function resolveAdminSessionsBaseUrl(): string {
  const configuredBaseUrl = import.meta.env.VITE_TERMINAL_API_BASE_URL

  if (configuredBaseUrl) {
    return `${configuredBaseUrl.replace(/\/$/, '')}/api/admin/sessions`
  }

  const configuredWebSocketUrl = import.meta.env.VITE_TERMINAL_WS_URL

  if (configuredWebSocketUrl) {
    const webSocketUrl = new URL(configuredWebSocketUrl)
    webSocketUrl.protocol = webSocketUrl.protocol === 'wss:' ? 'https:' : 'http:'
    webSocketUrl.pathname = '/api/admin/sessions'
    webSocketUrl.search = ''
    webSocketUrl.hash = ''
    return webSocketUrl.toString()
  }

  return '/api/admin/sessions'
}

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}.`)
  }

  return (await response.json()) as T
}

export async function fetchAdminSessions(signal?: AbortSignal): Promise<AdminSession[]> {
  const response = await fetch(resolveAdminSessionsBaseUrl(), {
    headers: {
      Accept: 'application/json',
    },
    signal,
  })

  const payload = await readJson<AdminSessionListResponse>(response)
  return payload.sessions
}

async function postSelection(
  actionPath: string,
  sessionIds: string[],
): Promise<AdminSessionActionResponse> {
  const response = await fetch(`${resolveAdminSessionsBaseUrl()}${actionPath}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: JSON.stringify({ sessionIds }),
  })

  return await readJson<AdminSessionActionResponse>(response)
}

export async function terminateAdminSessions(
  sessionIds: string[],
): Promise<AdminSessionActionResponse> {
  return await postSelection('/terminate', sessionIds)
}

export async function clearAdminSessions(
  sessionIds: string[],
): Promise<AdminSessionActionResponse> {
  return await postSelection('/clear', sessionIds)
}
