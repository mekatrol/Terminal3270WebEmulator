export type TN3270Color =
  | 'neutral'
  | 'blue'
  | 'red'
  | 'pink'
  | 'green'
  | 'turquoise'
  | 'yellow'
  | 'white'
  | 'black'
  | 'deepBlue'
  | 'orange'
  | 'purple'
  | 'paleGreen'
  | 'paleTurquoise'
  | 'grey'

export type TerminalCell = {
  char: string
  color: TN3270Color
  backgroundColor: TN3270Color
  protected: boolean
  intensified: boolean
  fieldId: string | null
}

export type CursorPosition = {
  row: number
  col: number
}

export type TN3270ScreenSnapshot = {
  rows: number
  cols: number
  cells: TerminalCell[][]
  cursor: CursorPosition | null
  connectionState: 'disconnected' | 'connecting' | 'connected'
  statusMessage: string
  statusColor: TN3270Color
  title: string
}

export type SessionReadyMessage = {
  type: 'session-ready'
  host: string
  port: number
  terminalType: string
  deviceName: string | null
  sessionLifetime: string
}

export type SessionErrorMessage = {
  type: 'session-error'
  message: string
}

export type SessionEndedMessage = {
  type: 'session-ended'
  reason: 'administrator-terminated' | 'endpoint-server-terminated'
  terminalEndpointDisplayName: string | null
}

export type SessionControlMessage = SessionReadyMessage | SessionErrorMessage | SessionEndedMessage

export type Tn3270Frame = {
  dataType: number
  requestFlag: number
  responseFlag: number
  sequenceNumber: number
  data: Uint8Array
}

export type Tn3270AidKey =
  | 'ENTER'
  | 'CLEAR'
  | 'SYSREQ'
  | 'PA1'
  | 'PA2'
  | 'PA3'
  | 'PF1'
  | 'PF2'
  | 'PF3'
  | 'PF4'
  | 'PF5'
  | 'PF6'
  | 'PF7'
  | 'PF8'
  | 'PF9'
  | 'PF10'
  | 'PF11'
  | 'PF12'
  | 'PF13'
  | 'PF14'
  | 'PF15'
  | 'PF16'
  | 'PF17'
  | 'PF18'
  | 'PF19'
  | 'PF20'
  | 'PF21'
  | 'PF22'
  | 'PF23'
  | 'PF24'
