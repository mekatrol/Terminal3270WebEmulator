const developmentServerHost = 'localhost'
const developmentServerHttpsPort = 7016
const developmentServerWssPort = 7016

function buildOrigin(protocol: 'https' | 'wss', port: number): string {
  return `${protocol}://${developmentServerHost}:${port}`
}

export function resolveDevelopmentApiOrigin(): string {
  return buildOrigin('https', developmentServerHttpsPort)
}

export function resolveDevelopmentTerminalWebSocketOrigin(): string {
  return buildOrigin('wss', developmentServerWssPort)
}
