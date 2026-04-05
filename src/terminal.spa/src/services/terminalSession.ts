import type {
  SessionControlMessage,
  SessionEndedMessage,
  SessionReadyMessage,
  Tn3270Frame,
} from '@/types/TN3270'
import { appendAccessTokenToUrl, authorizedFetch } from '@/services/auth'

export interface TerminalSessionTransport {
  connect(handlers: {
    onDisconnect: (event: {
      code: number
      reason: string
      sessionEnded: SessionEndedMessage | null
      wasClean: boolean
    }) => void
    onError: (message: string) => void
    onFrame: (frame: Tn3270Frame) => void
  }): Promise<SessionReadyMessage>
  disconnect(): Promise<void>
  sendFrame(frame: Tn3270Frame): Promise<void>
}

function decodeFrame(payload: ArrayBuffer): Tn3270Frame {
  const bytes = new Uint8Array(payload)

  if (bytes.length < 5) {
    throw new Error('Terminal proxy binary frame is shorter than the TN3270E header.')
  }

  return {
    dataType: bytes[0] ?? 0,
    requestFlag: bytes[1] ?? 0,
    responseFlag: bytes[2] ?? 0,
    sequenceNumber: ((bytes[3] ?? 0) << 8) | (bytes[4] ?? 0),
    data: bytes.slice(5),
  }
}

function describeDataType(dataType: number): string {
  switch (dataType) {
    case 0x00:
      return 'Data3270'
    case 0x01:
      return 'ScsData'
    case 0x02:
      return 'Response'
    case 0x03:
      return 'BindImage'
    case 0x04:
      return 'UnbindImage'
    case 0x05:
      return 'NvtData'
    case 0x06:
      return 'Request'
    case 0x07:
      return 'SscpLuData'
    case 0x08:
      return 'PrintEod'
    default:
      return `Unknown(0x${dataType.toString(16).toUpperCase().padStart(2, '0')})`
  }
}

function encodeFrame(frame: Tn3270Frame): Uint8Array {
  const payload = new Uint8Array(5 + frame.data.length)
  payload[0] = frame.dataType
  payload[1] = frame.requestFlag
  payload[2] = frame.responseFlag
  payload[3] = (frame.sequenceNumber >> 8) & 0xff
  payload[4] = frame.sequenceNumber & 0xff
  payload.set(frame.data, 5)
  return payload
}

function parseControlMessage(rawValue: string): SessionControlMessage {
  const message = JSON.parse(rawValue) as Partial<SessionControlMessage>

  if (message.type === 'session-ready') {
    return message as SessionReadyMessage
  }

  if (message.type === 'session-error' && typeof message.message === 'string') {
    return message as SessionControlMessage
  }

  if (
    message.type === 'session-ended' &&
    (message.reason === 'administrator-terminated' ||
      message.reason === 'endpoint-server-terminated')
  ) {
    return message as SessionEndedMessage
  }

  throw new Error('Received an unknown terminal proxy control message.')
}

function resolveTerminalWebSocketUrl(): string {
  const configuredUrl = import.meta.env.VITE_TERMINAL_WS_URL
  if (configuredUrl) {
    return configuredUrl
  }

  const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
  return `${protocol}//${window.location.host}/ws/terminal`
}

function resolveTerminalProbeUrl(): string {
  const webSocketUrl = new URL(resolveTerminalWebSocketUrl(), window.location.origin)
  webSocketUrl.protocol = webSocketUrl.protocol === 'wss:' ? 'https:' : 'http:'
  return webSocketUrl.toString()
}

function describeWebSocketEndpoint(url: string): string {
  const resolvedUrl = new URL(url, window.location.origin)
  return `${resolvedUrl.origin}${resolvedUrl.pathname}`
}

async function probeTerminalSessionAccess(): Promise<void> {
  const response = await authorizedFetch(resolveTerminalProbeUrl(), {
    method: 'GET',
    headers: {
      Accept: 'text/plain',
    },
  })

  if (response.status === 403) {
    throw new Error('403 You do not have permission to open a terminal session.')
  }

  if (response.status === 400) {
    return
  }

  if (!response.ok) {
    throw new Error(`Unable to verify terminal session startup (${response.status}).`)
  }
}

export class WebSocketTerminalSessionTransport implements TerminalSessionTransport {
  private socket: WebSocket | null = null

  async connect(handlers: {
    onDisconnect: (event: {
      code: number
      reason: string
      sessionEnded: SessionEndedMessage | null
      wasClean: boolean
    }) => void
    onError: (message: string) => void
    onFrame: (frame: Tn3270Frame) => void
  }): Promise<SessionReadyMessage> {
    await this.disconnect()
    await probeTerminalSessionAccess()

    const webSocketUrl = await appendAccessTokenToUrl(resolveTerminalWebSocketUrl())
    const webSocketEndpoint = describeWebSocketEndpoint(webSocketUrl)
    console.log('[TN3270] connecting websocket', { endpoint: webSocketEndpoint })

    const socket = new WebSocket(webSocketUrl)
    socket.binaryType = 'arraybuffer'

    const ready = await new Promise<SessionReadyMessage>((resolve, reject) => {
      let settled = false
      let sessionEndedMessage: SessionEndedMessage | null = null

      const fail = (error: Error): void => {
        if (settled) {
          return
        }

        settled = true
        reject(error)
      }

      socket.addEventListener('open', () => {
        console.log('[TN3270] websocket open', { endpoint: webSocketEndpoint })
      })

      socket.addEventListener('close', (event) => {
        this.socket = null
        handlers.onDisconnect({
          code: event.code,
          reason: event.reason,
          sessionEnded: sessionEndedMessage,
          wasClean: event.wasClean,
        })
        console.log('[TN3270] websocket close', {
          endpoint: webSocketEndpoint,
          code: event.code,
          reason: event.reason,
          wasClean: event.wasClean,
        })

        if (!settled) {
          fail(
            new Error(
              `Unable to establish the terminal proxy WebSocket connection before startup completed (${event.code}).`,
            ),
          )
        }
      })

      socket.addEventListener('error', () => {
        console.error('[TN3270] websocket error', { endpoint: webSocketEndpoint })
        fail(new Error('Unable to establish the terminal proxy WebSocket connection.'))
      })

      socket.addEventListener('message', (event) => {
        if (typeof event.data === 'string') {
          const controlMessage = parseControlMessage(event.data)

          if (controlMessage.type === 'session-error') {
            handlers.onError(controlMessage.message)
            fail(new Error(controlMessage.message))
            return
          }

          if (controlMessage.type === 'session-ended') {
            sessionEndedMessage = controlMessage
            return
          }

          if (!settled) {
            settled = true
            console.log('[TN3270] session ready', controlMessage)
            resolve(controlMessage)
          }

          return
        }

        if (event.data instanceof ArrayBuffer) {
          const frame = decodeFrame(event.data)
          console.log('[TN3270] frame received', {
            dataType: describeDataType(frame.dataType),
            requestFlag: frame.requestFlag,
            responseFlag: frame.responseFlag,
            sequenceNumber: frame.sequenceNumber,
            payloadLength: frame.data.length,
            firstBytes: Array.from(frame.data.slice(0, 16)),
          })
          handlers.onFrame(frame)
        }
      })
    })

    this.socket = socket
    console.log('[TN3270] connect resolved', { endpoint: webSocketEndpoint })
    return ready
  }

  async disconnect(): Promise<void> {
    const socket = this.socket
    this.socket = null

    if (!socket) {
      return
    }

    if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
      socket.close(1000, 'Terminal SPA session closed.')
    }
  }

  async sendFrame(frame: Tn3270Frame): Promise<void> {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      return
    }

    const payload = encodeFrame(frame)
    const buffer = new ArrayBuffer(payload.byteLength)
    new Uint8Array(buffer).set(payload)
    console.log('[TN3270] frame sent', {
      dataType: describeDataType(frame.dataType),
      requestFlag: frame.requestFlag,
      responseFlag: frame.responseFlag,
      sequenceNumber: frame.sequenceNumber,
      payloadLength: frame.data.length,
      firstBytes: Array.from(frame.data.slice(0, 16)),
    })
    this.socket.send(buffer)
  }
}

export function createTerminalSessionTransport(): TerminalSessionTransport {
  return new WebSocketTerminalSessionTransport()
}
