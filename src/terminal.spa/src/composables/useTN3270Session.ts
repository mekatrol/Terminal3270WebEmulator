import { computed, onBeforeUnmount, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { getBrowserAuthService, hasTerminalSessionRole } from '@/services/auth'
import { createTerminalSessionTransport } from '@/services/terminalSession'
import { Tn3270TerminalScreen } from '@/services/tn3270Screen'
import type { TN3270Color, TN3270ScreenSnapshot, Tn3270AidKey, Tn3270Frame } from '@/types/TN3270'

const transport = createTerminalSessionTransport()
const terminalAccessDeniedMessage =
  'You are not permitted to open a terminal session on this application.'
const terminalAccessDeniedErrorMessage =
  '403 You do not have permission to open a terminal session.'

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
  canStartSession: ComputedRef<boolean>
  dismissSessionNotice: () => void
  handleKeydown: (event: KeyboardEvent) => Promise<void>
  sessionLauncherTitle: ComputedRef<string>
  sessionNoticeMessage: ComputedRef<string | null>
  sessionNoticeTitle: ComputedRef<string | null>
  sessionLauncherMessage: ComputedRef<string>
  snapshot: Ref<TN3270ScreenSnapshot>
  showSessionNotice: ComputedRef<boolean>
  showSessionLauncher: ComputedRef<boolean>
  startSession: () => Promise<void>
} {
  let screen: Tn3270TerminalScreen | null = null
  let isStartingSession = false
  let isUnmounting = false
  let startupFailureHandled = false
  const authState = getBrowserAuthService().getState()
  const canStartSession = computed(() => hasTerminalSessionRole(authState.roles))

  const snapshot = ref<TN3270ScreenSnapshot>(
    createInitialSnapshot('disconnected', 'START A NEW TERMINAL SESSION', 'white'),
  )
  const sessionNoticeMessage = ref<string | null>(null)
  const sessionNoticeTitle = ref<string | null>(null)
  const showSessionLauncher = ref(true)
  const sessionLauncherMessage = ref(resolveInitialSessionLauncherMessage())

  function resolveInitialSessionLauncherMessage(): string {
    return hasTerminalSessionRole(authState.roles)
      ? 'Start a new terminal session.'
      : terminalAccessDeniedMessage
  }

  const sessionLauncherTitle = computed(() =>
    canStartSession.value ? 'Start a new terminal session' : 'Terminal session unavailable',
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

  function showLauncher(message: string): void {
    showSessionLauncher.value = true
    sessionLauncherMessage.value = message
  }

  function dismissSessionNotice(): void {
    sessionNoticeTitle.value = null
    sessionNoticeMessage.value = null
  }

  function showSessionNotice(title: string, message: string): void {
    sessionNoticeTitle.value = title
    sessionNoticeMessage.value = message
  }

  function showStartupConnectionFailure(): void {
    startupFailureHandled = true
    const message =
      'Unable to establish the terminal session connection. Confirm the server is available and try again.'
    updateSnapshot('disconnected', 'UNABLE TO ESTABLISH TERMINAL CONNECTION', 'red')
    showLauncher(message)
    showSessionNotice('Terminal Connection Failed', message)
  }

  function showStartupAuthorizationFailure(): void {
    startupFailureHandled = true
    updateSnapshot('disconnected', '403 YOU DO NOT HAVE PERMISSION', 'red')
    showLauncher(terminalAccessDeniedMessage)
    showSessionNotice('403 Permission Denied', terminalAccessDeniedErrorMessage)
  }

  async function startSession(): Promise<void> {
    isStartingSession = true
    isUnmounting = false
    startupFailureHandled = false
    screen = null
    dismissSessionNotice()
    showSessionLauncher.value = false
    sessionLauncherMessage.value = 'Start a new terminal session.'
    updateSnapshot('connecting', 'CONNECTING TO TERMINAL SESSION', 'yellow')

    try {
      const ready = await transport.connect({
        onFrame(frame: Tn3270Frame) {
          console.log('[TN3270] processing inbound frame', {
            dataType: frame.dataType,
            payloadLength: frame.data.length,
          })

          if (!screen || frame.dataType !== 0x00) {
            updateSnapshot('connected', `HOST RECORD TYPE ${frame.dataType} RECEIVED`, 'white')
            return
          }

          screen.applyInboundRecord(frame.data)
          console.log('[TN3270] screen updated from host record', {
            rows: screen.rows,
            cols: screen.columns,
          })
          updateSnapshot('connected', 'HOST SCREEN UPDATED', 'green')
        },
        onError(message) {
          console.error('[TN3270] session error', message)
          if (message.includes('403')) {
            showStartupAuthorizationFailure()
            return
          }

          if (isStartingSession) {
            showStartupConnectionFailure()
            return
          }

          updateSnapshot('disconnected', message, 'red')
          showLauncher('The terminal session could not be started. Try again.')
          showSessionNotice('Terminal Session Error', message)
        },
        onDisconnect(event) {
          if (isUnmounting) {
            return
          }

          console.log('[TN3270] session disconnected')
          if (startupFailureHandled && !event.sessionEnded) {
            return
          }

          if (isStartingSession && !event.sessionEnded) {
            showStartupConnectionFailure()
            return
          }

          if (event.sessionEnded?.reason === 'administrator-terminated') {
            const message = 'Your terminal session was terminated by an administrator.'
            updateSnapshot('disconnected', 'SESSION TERMINATED BY ADMINISTRATOR', 'red')
            showLauncher(message)
            showSessionNotice('Session Terminated', message)
            return
          }

          if (event.sessionEnded?.reason === 'endpoint-server-terminated') {
            const terminalEndpointDisplayName =
              event.sessionEnded.terminalEndpointDisplayName || 'the endpoint server'
            const message = `Your terminal session was terminated by ${terminalEndpointDisplayName}.`
            updateSnapshot('disconnected', 'SESSION TERMINATED BY ENDPOINT SERVER', 'red')
            showLauncher(message)
            showSessionNotice('Session Terminated', message)
            return
          }

          if (event.reason === 'Terminal session terminated by administrator.') {
            const message = 'Your terminal session was terminated by an administrator.'
            updateSnapshot('disconnected', 'SESSION TERMINATED BY ADMINISTRATOR', 'red')
            showLauncher(message)
            showSessionNotice('Session Terminated', message)
            return
          }

          const endpointTerminationPrefix = 'Terminal session terminated by '
          if (event.reason.startsWith(endpointTerminationPrefix)) {
            const terminalEndpointDisplayName = event.reason
              .slice(endpointTerminationPrefix.length)
              .replace(/\.$/, '')
              .trim()

            const message = `Your terminal session was terminated by ${terminalEndpointDisplayName || 'the endpoint server'}.`
            updateSnapshot('disconnected', 'SESSION TERMINATED BY ENDPOINT SERVER', 'red')
            showLauncher(message)
            showSessionNotice('Session Terminated', message)
            return
          }

          if (snapshot.value.connectionState !== 'disconnected' || event.reason) {
            updateSnapshot('disconnected', 'TERMINAL SESSION DISCONNECTED', 'red')
          }

          showLauncher('The terminal session ended. Start a new session.')
        },
      })

      isStartingSession = false
      screen = Tn3270TerminalScreen.fromTerminalType(ready.terminalType)
      console.log('[TN3270] screen model created', {
        terminalType: ready.terminalType,
        rows: screen.rows,
        cols: screen.columns,
      })
      updateSnapshot('connected', `CONNECTED TO ${ready.host}:${ready.port}`, 'green')
    } catch (error) {
      isStartingSession = false

      if (isUnmounting) {
        return
      }

      if (startupFailureHandled) {
        return
      }

      const message =
        error instanceof Error ? error.message : 'Unable to connect to terminal session.'
      console.error('[TN3270] connect failed', message)
      if (message.includes('403')) {
        showStartupAuthorizationFailure()
        return
      }

      showStartupConnectionFailure()
    }
  }

  async function disconnect(): Promise<void> {
    isUnmounting = true
    await transport.disconnect()
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

  onBeforeUnmount(() => {
    void disconnect()
  })

  const accessibleSummary = computed(() => summarizeSnapshot(snapshot.value))

  return {
    accessibleSummary,
    canStartSession,
    dismissSessionNotice,
    handleKeydown,
    sessionLauncherTitle,
    sessionNoticeMessage: computed(() => sessionNoticeMessage.value),
    sessionNoticeTitle: computed(() => sessionNoticeTitle.value),
    sessionLauncherMessage: computed(() => sessionLauncherMessage.value),
    snapshot,
    showSessionNotice: computed(() => sessionNoticeMessage.value !== null),
    showSessionLauncher: computed(() => showSessionLauncher.value),
    startSession,
  }
}
