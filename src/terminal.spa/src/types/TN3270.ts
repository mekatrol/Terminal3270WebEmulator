export type TN3270Color =
  | 'neutral'
  | 'blue'
  | 'red'
  | 'pink'
  | 'green'
  | 'turquoise'
  | 'yellow'
  | 'white'

export type TerminalFieldDefinition = {
  id: string
  row: number
  col: number
  length: number
  label: string
  labelColor: TN3270Color
  value: string
  protected: boolean
  intensified?: boolean
}

export type TerminalStaticText = {
  row: number
  col: number
  text: string
  color: TN3270Color
  intensified?: boolean
}

export type TerminalCell = {
  char: string
  color: TN3270Color
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

export type TN3270SessionBootstrap = {
  title: string
  instructions: TerminalStaticText[]
  fields: TerminalFieldDefinition[]
}
