import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { createTerminalSessionTransport, summarizeSnapshot } from '@/services/terminalSession'
import type {
  TN3270Color,
  TN3270ScreenSnapshot,
  TN3270SessionBootstrap,
  TerminalCell,
  TerminalFieldDefinition,
} from '@/types/TN3270'

const ROWS = 24
const COLS = 80
const BLANK = ' '

const transport = createTerminalSessionTransport()

function padText(value: string, length: number): string {
  return value.slice(0, length).padEnd(length, BLANK)
}

function setScreenCell(
  screen: TerminalCell[][],
  row: number,
  col: number,
  cell: TerminalCell,
): void {
  const targetRow = screen[row]
  if (!targetRow || col < 0 || col >= targetRow.length) {
    return
  }

  targetRow[col] = cell
}

function createEmptyScreen(): TerminalCell[][] {
  return Array.from({ length: ROWS }, () =>
    Array.from(
      { length: COLS },
      (): TerminalCell => ({
        char: BLANK,
        color: 'neutral',
        protected: true,
        intensified: false,
        fieldId: null,
      }),
    ),
  )
}

function createConnectedSnapshot(
  bootstrap: TN3270SessionBootstrap,
  fields: TerminalFieldDefinition[],
  activeFieldIndex: number,
  cursorOffset: number,
  statusMessage: string,
  statusColor: TN3270Color,
): TN3270ScreenSnapshot {
  const screen = createEmptyScreen()

  for (let row = 0; row < ROWS; row += 1) {
    for (let col = 0; col < COLS; col += 1) {
      if (row === 0 || row === ROWS - 1) {
        setScreenCell(screen, row, col, {
          char: BLANK,
          color: 'blue',
          protected: true,
          intensified: true,
          fieldId: null,
        })
      }
    }
  }

  const banner = ' TERMINAL 3270 EMULATOR '
  for (let index = 0; index < banner.length; index += 1) {
    setScreenCell(screen, 0, index + 2, {
      char: banner[index] ?? BLANK,
      color: 'white',
      protected: true,
      intensified: true,
      fieldId: null,
    })
  }

  for (let index = 0; index < bootstrap.title.length; index += 1) {
    setScreenCell(screen, 1, 25 + index, {
      char: bootstrap.title[index] ?? BLANK,
      color: 'turquoise',
      protected: true,
      intensified: true,
      fieldId: null,
    })
  }

  for (const item of bootstrap.instructions) {
    for (let index = 0; index < item.text.length; index += 1) {
      setScreenCell(screen, item.row, item.col + index, {
        char: item.text[index] ?? BLANK,
        color: item.color,
        protected: true,
        intensified: item.intensified ?? false,
        fieldId: null,
      })
    }
  }

  for (const field of fields) {
    if (field.label) {
      const labelStart = Math.max(field.col - field.label.length - 1, 0)
      for (let index = 0; index < field.label.length; index += 1) {
        setScreenCell(screen, field.row, labelStart + index, {
          char: field.label[index] ?? BLANK,
          color: field.labelColor,
          protected: true,
          intensified: field.intensified ?? false,
          fieldId: null,
        })
      }
    }

    const fieldText = padText(field.value, field.length)
    const fieldColor = field.protected ? field.labelColor : 'green'

    for (let index = 0; index < field.length; index += 1) {
      setScreenCell(screen, field.row, field.col + index, {
        char: fieldText[index] ?? BLANK,
        color: fieldColor,
        protected: field.protected,
        intensified: field.intensified ?? false,
        fieldId: field.id,
      })
    }
  }

  const statusText = ` ${statusMessage} `.padEnd(COLS - 2, BLANK)
  for (let index = 0; index < statusText.length; index += 1) {
    setScreenCell(screen, ROWS - 1, index + 1, {
      char: statusText[index] ?? BLANK,
      color: statusColor,
      protected: true,
      intensified: true,
      fieldId: null,
    })
  }

  const activeField = fields[activeFieldIndex]
  const cursor =
    activeField && !activeField.protected
      ? {
          row: activeField.row,
          col: Math.min(activeField.col + cursorOffset, activeField.col + activeField.length - 1),
        }
      : null

  return {
    rows: ROWS,
    cols: COLS,
    cells: screen,
    cursor,
    connectionState: 'connected',
    statusMessage,
    statusColor,
    title: bootstrap.title,
  }
}

export function useTN3270Session(): {
  accessibleSummary: ComputedRef<string>
  fields: Ref<TerminalFieldDefinition[]>
  handleKeydown: (event: KeyboardEvent) => Promise<void>
  snapshot: ComputedRef<TN3270ScreenSnapshot>
} {
  const bootstrap = ref<TN3270SessionBootstrap | null>(null)
  const fields = ref<TerminalFieldDefinition[]>([])
  const activeFieldIndex = ref(0)
  const cursorOffset = ref(0)
  const connected = ref(false)
  const statusMessage = ref('SYSTEM READY  INSERT OFF  KEYBOARD ENABLED')
  const statusColor = ref<TN3270Color>('white')
  const lastAidKey = ref('ENTER')

  async function connect(): Promise<void> {
    const sessionBootstrap = await transport.connect()
    bootstrap.value = sessionBootstrap
    fields.value = sessionBootstrap.fields.map((field) => ({ ...field }))
    activeFieldIndex.value = Math.max(
      fields.value.findIndex((field) => !field.protected),
      0,
    )
    cursorOffset.value = 0
    connected.value = true
  }

  async function disconnect(): Promise<void> {
    await transport.disconnect()
    connected.value = false
  }

  function getActiveField(): TerminalFieldDefinition | null {
    const field = fields.value[activeFieldIndex.value]
    if (!field || field.protected) {
      return null
    }

    return field
  }

  function moveToField(index: number, toEnd = false): void {
    const field = fields.value[index]
    if (!field || field.protected) {
      return
    }

    activeFieldIndex.value = index
    cursorOffset.value = toEnd ? Math.min(field.value.length, field.length - 1) : 0
  }

  function moveToAdjacentField(direction: 1 | -1): void {
    const editableFields = fields.value
      .map((field, index) => ({ field, index }))
      .filter(({ field }) => !field.protected)

    if (!editableFields.length) {
      return
    }

    const currentPosition = editableFields.findIndex(
      ({ index }) => index === activeFieldIndex.value,
    )
    const nextPosition =
      (currentPosition + direction + editableFields.length) % editableFields.length
    const nextField = editableFields[nextPosition]
    if (nextField) {
      moveToField(nextField.index, direction < 0)
    }
  }

  function writeCharacter(character: string): void {
    const field = getActiveField()
    if (!field || cursorOffset.value >= field.length) {
      return
    }

    const chars = padText(field.value, field.length).split('')
    chars[cursorOffset.value] = character
    field.value = chars.join('').trimEnd()
    cursorOffset.value = Math.min(cursorOffset.value + 1, field.length - 1)
    statusMessage.value = `FIELD UPDATED  LAST AID=${lastAidKey.value}`
    statusColor.value = 'green'
  }

  function eraseCharacter(backward: boolean): void {
    const field = getActiveField()
    if (!field) {
      return
    }

    const chars = padText(field.value, field.length).split('')
    const deleteAt = backward
      ? Math.max(cursorOffset.value - 1, 0)
      : Math.min(cursorOffset.value, field.length - 1)

    if (backward && cursorOffset.value === 0 && chars[0] === BLANK) {
      return
    }

    for (let index = deleteAt; index < field.length - 1; index += 1) {
      chars[index] = chars[index + 1] ?? BLANK
    }

    chars[field.length - 1] = BLANK
    field.value = chars.join('').trimEnd()
    if (backward) {
      cursorOffset.value = deleteAt
    }
    statusMessage.value = 'FIELD UPDATED  DELETE PROCESSED'
    statusColor.value = 'yellow'
  }

  async function submitAidKey(aidKey: string): Promise<void> {
    const payload = Object.fromEntries(
      fields.value.filter((field) => !field.protected).map((field) => [field.id, field.value]),
    )
    await transport.submitAidKey(aidKey, payload)
    lastAidKey.value = aidKey
    statusMessage.value = `AID ${aidKey} SENT  HOST UPDATE PENDING`
    statusColor.value = aidKey === 'PF3' ? 'red' : 'white'
  }

  async function handleKeydown(event: KeyboardEvent): Promise<void> {
    const field = getActiveField()
    if (!field) {
      return
    }

    if (event.key === 'Tab') {
      event.preventDefault()
      moveToAdjacentField(event.shiftKey ? -1 : 1)
      return
    }

    if (event.key === 'Enter') {
      event.preventDefault()
      moveToAdjacentField(1)
      await submitAidKey('ENTER')
      return
    }

    if (event.key === 'F3') {
      event.preventDefault()
      await submitAidKey('PF3')
      return
    }

    if (event.key === 'F5') {
      event.preventDefault()
      await submitAidKey('PF5')
      return
    }

    if (event.key === 'ArrowLeft') {
      event.preventDefault()
      cursorOffset.value = Math.max(cursorOffset.value - 1, 0)
      return
    }

    if (event.key === 'ArrowRight') {
      event.preventDefault()
      cursorOffset.value = Math.min(cursorOffset.value + 1, field.length - 1)
      return
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault()
      moveToAdjacentField(-1)
      return
    }

    if (event.key === 'ArrowDown') {
      event.preventDefault()
      moveToAdjacentField(1)
      return
    }

    if (event.key === 'Home') {
      event.preventDefault()
      cursorOffset.value = 0
      return
    }

    if (event.key === 'End') {
      event.preventDefault()
      cursorOffset.value = Math.min(field.value.length, field.length - 1)
      return
    }

    if (event.key === 'Backspace') {
      event.preventDefault()
      eraseCharacter(true)
      return
    }

    if (event.key === 'Delete') {
      event.preventDefault()
      eraseCharacter(false)
      return
    }

    if (event.ctrlKey || event.metaKey || event.altKey) {
      return
    }

    if (event.key.length === 1) {
      event.preventDefault()
      writeCharacter(event.key.toUpperCase())
    }
  }

  onMounted(() => {
    void connect()
  })

  onBeforeUnmount(() => {
    void disconnect()
  })

  const snapshot = computed<TN3270ScreenSnapshot>(() => {
    if (!connected.value || !bootstrap.value) {
      return {
        rows: ROWS,
        cols: COLS,
        cells: createEmptyScreen(),
        cursor: null,
        connectionState: 'connecting',
        statusMessage: 'CONNECTING TO TERMINAL SESSION',
        statusColor: 'yellow',
        title: 'TN 3270 TERMINAL',
      }
    }

    return createConnectedSnapshot(
      bootstrap.value,
      fields.value,
      activeFieldIndex.value,
      cursorOffset.value,
      statusMessage.value,
      statusColor.value,
    )
  })

  const accessibleSummary = computed(() => summarizeSnapshot(snapshot.value))

  return {
    accessibleSummary,
    fields,
    handleKeydown,
    snapshot,
  }
}
