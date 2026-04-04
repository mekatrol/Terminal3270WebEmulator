import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { createTerminalSessionTransport } from '@/services/terminalSession'
import { Tn3270TerminalScreen } from '@/services/tn3270Screen'
import type {
  TN3270Color,
  TN3270ScreenSnapshot,
  Tn3270AidKey,
  Tn3270Frame,
} from '@/types/TN3270'

const transport = createTerminalSessionTransport()

function createInitialSnapshot(
  connectionState: 'connecting' | 'disconnected' | 'connected',
  statusMessage: string,
  statusColor: TN3270Color,
): TN3270ScreenSnapshot {
  const screen = Tn3270TerminalScreen.fromTerminalType('IBM-3279-2-E')
  return screen.toSnapshot(connectionState, statusMessage, statusColor)
}

function summarizeSnapshot(snapshot: TN3270ScreenSnapshot): string {
  return `${snapshot.title}. ${snapshot.statusMessage}. ${snapshot.connectionState}.`
}

function mapFunctionKeyToAid(event: KeyboardEvent): Tn3270AidKey | null {
  if (!/^F\d{1,2}$/.test(event.key)) {
    return null
  }

  const functionNumber = Number.parseInt(event.key.slice(1), 10)
  if (Number.isNaN(functionNumber) || functionNumber < 1 || functionNumber > 12) {
    return null
  }

  const pfNumber = event.shiftKey ? functionNumber + 12 : functionNumber
  return `PF${pfNumber}` as Tn3270AidKey
}

function mapModifiedKeyToAid(event: KeyboardEvent): Tn3270AidKey | null {
  if (!event.ctrlKey) {
    return null
  }

  switch (event.key.toLowerCase()) {
    case 'c':
      return 'PA1'
    case 'l':
      return 'CLEAR'
    case 's':
      return 'SYSREQ'
    default:
      return null
  }
}

export function useTN3270Session(): {
  accessibleSummary: ComputedRef<string>
  handleKeydown: (event: KeyboardEvent) => Promise<void>
  snapshot: Ref<TN3270ScreenSnapshot>
} {
  let screen: Tn3270TerminalScreen | null = null

  const snapshot = ref<TN3270ScreenSnapshot>(
    createInitialSnapshot('connecting', 'CONNECTING TO TERMINAL SESSION', 'yellow'),
  )

  function updateSnapshot(
    connectionState: 'disconnected' | 'connecting' | 'connected',
    statusMessage: string,
    statusColor: TN3270Color,
  ): void {
    snapshot.value =
      screen?.toSnapshot(connectionState, statusMessage, statusColor) ??
      createInitialSnapshot(connectionState, statusMessage, statusColor)
  }

  async function connect(): Promise<void> {
    screen = null
    updateSnapshot('connecting', 'CONNECTING TO TERMINAL SESSION', 'yellow')

    try {
      const ready = await transport.connect({
        onFrame(frame: Tn3270Frame) {
          console.debug('[TN3270] processing inbound frame', {
            dataType: frame.dataType,
            payloadLength: frame.data.length,
          })

          if (!screen || frame.dataType !== 0x00) {
            updateSnapshot('connected', `HOST RECORD TYPE ${frame.dataType} RECEIVED`, 'white')
            return
          }

          screen.applyInboundRecord(frame.data)
          console.debug('[TN3270] screen updated from host record', {
            rows: screen.rows,
            cols: screen.columns,
          })
          updateSnapshot('connected', 'HOST SCREEN UPDATED', 'green')
        },
        onError(message) {
          console.error('[TN3270] session error', message)
          updateSnapshot('disconnected', message, 'red')
        },
        onDisconnect() {
          console.debug('[TN3270] session disconnected')
          if (snapshot.value.connectionState !== 'disconnected') {
            updateSnapshot('disconnected', 'TERMINAL SESSION DISCONNECTED', 'red')
          }
        },
      })

      screen = Tn3270TerminalScreen.fromTerminalType(ready.terminalType)
      console.debug('[TN3270] screen model created', {
        terminalType: ready.terminalType,
        rows: screen.rows,
        cols: screen.columns,
      })
      updateSnapshot('connected', `CONNECTED TO ${ready.host}:${ready.port}`, 'green')
    } catch (error) {
      const message =
        error instanceof Error ? error.message : 'Unable to connect to terminal session.'
      console.error('[TN3270] connect failed', message)
      updateSnapshot('disconnected', message, 'red')
    }
  }

  async function disconnect(): Promise<void> {
    await transport.disconnect()
    updateSnapshot('disconnected', 'TERMINAL SESSION CLOSED', 'red')
  }

  async function submitAid(aidKey: Tn3270AidKey): Promise<void> {
    if (!screen) {
      return
    }

    await transport.sendFrame({
      dataType: 0x00,
      requestFlag: 0x00,
      responseFlag: 0x00,
      sequenceNumber: 0,
      data: screen.buildAidRecord(aidKey),
    })

    updateSnapshot('connected', `AID ${aidKey} SENT TO HOST`, 'white')
  }

  async function handleKeydown(event: KeyboardEvent): Promise<void> {
    if (!screen || snapshot.value.connectionState !== 'connected') {
      return
    }

    if (event.key === 'Tab') {
      event.preventDefault()
      if (screen.moveToAdjacentField(!event.shiftKey)) {
        updateSnapshot('connected', 'FIELD CHANGED', 'white')
      }
      return
    }

    if (event.key === 'Enter') {
      event.preventDefault()
      await submitAid('ENTER')
      return
    }

    const modifiedAid = mapModifiedKeyToAid(event)
    if (modifiedAid) {
      event.preventDefault()
      await submitAid(modifiedAid)
      return
    }

    if (event.key === 'Pause') {
      event.preventDefault()
      const pauseAid = event.shiftKey ? 'PA2' : event.ctrlKey ? 'PA3' : 'PA1'
      await submitAid(pauseAid)
      return
    }

    const functionAid = mapFunctionKeyToAid(event)
    if (functionAid) {
      event.preventDefault()
      await submitAid(functionAid)
      return
    }

    if (event.key === 'ArrowLeft') {
      event.preventDefault()
      if (screen.moveCursor(-1, 0)) {
        updateSnapshot('connected', 'CURSOR MOVED', 'white')
      }
      return
    }

    if (event.key === 'ArrowRight') {
      event.preventDefault()
      if (screen.moveCursor(1, 0)) {
        updateSnapshot('connected', 'CURSOR MOVED', 'white')
      }
      return
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault()
      if (screen.moveCursor(0, -1)) {
        updateSnapshot('connected', 'CURSOR MOVED', 'white')
      }
      return
    }

    if (event.key === 'ArrowDown') {
      event.preventDefault()
      if (screen.moveCursor(0, 1)) {
        updateSnapshot('connected', 'CURSOR MOVED', 'white')
      }
      return
    }

    if (event.key === 'Home') {
      event.preventDefault()
      if (screen.moveCursorToFieldStart()) {
        updateSnapshot('connected', 'CURSOR MOVED TO FIELD START', 'white')
      }
      return
    }

    if (event.key === 'End') {
      event.preventDefault()
      if (screen.moveCursorToFieldEnd()) {
        updateSnapshot('connected', 'CURSOR MOVED TO FIELD END', 'white')
      }
      return
    }

    if (event.key === 'Backspace') {
      event.preventDefault()
      if (screen.backspace()) {
        updateSnapshot('connected', 'BACKSPACE PROCESSED', 'green')
      }
      return
    }

    if (event.key === 'Delete') {
      event.preventDefault()
      if (screen.delete()) {
        updateSnapshot('connected', 'DELETE PROCESSED', 'green')
      }
      return
    }

    if (event.ctrlKey || event.metaKey || event.altKey) {
      return
    }

    if (event.key.length === 1) {
      event.preventDefault()
      if (screen.tryWriteCharacter(event.key.toUpperCase())) {
        updateSnapshot('connected', 'FIELD UPDATED', 'green')
      }
    }
  }

  onMounted(() => {
    void connect()
  })

  onBeforeUnmount(() => {
    void disconnect()
  })

  const accessibleSummary = computed(() => summarizeSnapshot(snapshot.value))

  return {
    accessibleSummary,
    handleKeydown,
    snapshot,
  }
}
