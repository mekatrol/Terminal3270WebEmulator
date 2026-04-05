export type AppConfig = {
  terminalEndpointDisplayName: string
}

const defaultTerminalEndpointDisplayName = 'Terminal 3270 Web Emulator'

function resolveAppConfigUrl(): string {
  const configuredUrl = import.meta.env.VITE_APP_CONFIG_URL

  if (configuredUrl) {
    return configuredUrl
  }

  return '/api/app-config'
}

export async function fetchAppConfig(): Promise<AppConfig> {
  try {
    const response = await fetch(resolveAppConfigUrl(), {
      headers: {
        Accept: 'application/json',
      },
    })

    if (!response.ok) {
      throw new Error(`Unable to load app configuration (${response.status}).`)
    }

    const payload = (await response.json()) as Partial<AppConfig>
    const terminalEndpointDisplayName =
      typeof payload.terminalEndpointDisplayName === 'string' &&
      payload.terminalEndpointDisplayName.trim().length > 0
        ? payload.terminalEndpointDisplayName
        : defaultTerminalEndpointDisplayName

    return {
      terminalEndpointDisplayName,
    }
  } catch {
    return {
      terminalEndpointDisplayName: defaultTerminalEndpointDisplayName,
    }
  }
}
