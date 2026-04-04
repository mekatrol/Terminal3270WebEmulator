import type { TN3270Color, TN3270ScreenSnapshot, TerminalCell, Tn3270AidKey } from '@/types/TN3270'

type TerminalFieldState = {
  attributeAddress: number
  startAddress: number
  endAddress: number
  isProtected: boolean
  isNumeric: boolean
  isHidden: boolean
  isIntensified: boolean
  isModified: boolean
  foreground: TN3270Color
  background: TN3270Color
}

type TerminalCellState = {
  character: string
  foreground: TN3270Color
  background: TN3270Color
  isProtected: boolean
  isFieldAttribute: boolean
  isHidden: boolean
  isIntensified: boolean
  fieldIndex: number
}

const COMMAND_WRITE = 0xf1
const COMMAND_ERASE_WRITE = 0xf5
const COMMAND_ERASE_WRITE_ALTERNATE = 0x7e
const COMMAND_ERASE_ALL_UNPROTECTED = 0x6f
const ORDER_PROGRAM_TAB = 0x05
const ORDER_GRAPHIC_ESCAPE = 0x08
const ORDER_SET_BUFFER_ADDRESS = 0x11
const ORDER_ERASE_UNPROTECTED_TO_ADDRESS = 0x12
const ORDER_INSERT_CURSOR = 0x13
const ORDER_START_FIELD = 0x1d
const ORDER_SET_ATTRIBUTE = 0x28
const ORDER_START_FIELD_EXTENDED = 0x29
const ORDER_MODIFY_FIELD = 0x2c
const ORDER_REPEAT_TO_ADDRESS = 0x3c
const AID_SYSREQ = 0xf0
const AID_PF1 = 0xf1
const AID_PF2 = 0xf2
const AID_ENTER = 0x7d
const AID_PF3 = 0xf3
const AID_PF4 = 0xf4
const AID_PF5 = 0xf5
const AID_PF6 = 0xf6
const AID_PF7 = 0xf7
const AID_PF8 = 0xf8
const AID_PF9 = 0xf9
const AID_PF10 = 0x7a
const AID_PF11 = 0x7b
const AID_PF12 = 0x7c
const AID_PF13 = 0xc1
const AID_PF14 = 0xc2
const AID_PF15 = 0xc3
const AID_PF16 = 0xc4
const AID_PF17 = 0xc5
const AID_PF18 = 0xc6
const AID_PF19 = 0xc7
const AID_PF20 = 0xc8
const AID_PF21 = 0xc9
const AID_PF22 = 0x4a
const AID_PF23 = 0x4b
const AID_PF24 = 0x4c
const AID_PA1 = 0x6c
const AID_PA2 = 0x6e
const AID_PA3 = 0x6b
const AID_CLEAR = 0x6d

const ADDRESS_ENCODING_TABLE = [
  0x40, 0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f,
  0x50, 0xd1, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f,
  0x60, 0x61, 0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f,
  0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f,
] as const

const ADDRESS_DECODING_TABLE = new Map<number, number>(
  ADDRESS_ENCODING_TABLE.map((value, index) => [value, index]),
)

const EBCDIC_TO_UNICODE = [
  '\u0000',
  '\u0001',
  '\u0002',
  '\u0003',
  '\u009c',
  '\t',
  '\u0086',
  '\u007f',
  '\u0097',
  '\u008d',
  '\u008e',
  '\u000b',
  '\u000c',
  '\r',
  '\u000e',
  '\u000f',
  '\u0010',
  '\u0011',
  '\u0012',
  '\u0013',
  '\u009d',
  '\u0085',
  '\b',
  '\u0087',
  '\u0018',
  '\u0019',
  '\u0092',
  '\u008f',
  '\u001c',
  '\u001d',
  '\u001e',
  '\u001f',
  '\u0080',
  '\u0081',
  '\u0082',
  '\u0083',
  '\u0084',
  '\n',
  '\u0017',
  '\u001b',
  '\u0088',
  '\u0089',
  '\u008a',
  '\u008b',
  '\u008c',
  '\u0005',
  '\u0006',
  '\u0007',
  '\u0090',
  '\u0091',
  '\u0016',
  '\u0093',
  '\u0094',
  '\u0095',
  '\u0096',
  '\u0004',
  '\u0098',
  '\u0099',
  '\u009a',
  '\u009b',
  '\u0014',
  '\u0015',
  '\u009e',
  '\u001a',
  ' ',
  '\u00a0',
  'â',
  'ä',
  'à',
  'á',
  'ã',
  'å',
  'ç',
  'ñ',
  '¢',
  '.',
  '<',
  '(',
  '+',
  '|',
  '&',
  'é',
  'ê',
  'ë',
  'è',
  'í',
  'î',
  'ï',
  'ì',
  'ß',
  '!',
  '$',
  '*',
  ')',
  ';',
  '¬',
  '-',
  '/',
  'Â',
  'Ä',
  'À',
  'Á',
  'Ã',
  'Å',
  'Ç',
  'Ñ',
  '¦',
  ',',
  '%',
  '_',
  '>',
  '?',
  'ø',
  'É',
  'Ê',
  'Ë',
  'È',
  'Í',
  'Î',
  'Ï',
  'Ì',
  '`',
  ':',
  '#',
  '@',
  "'",
  '=',
  '"',
  'Ø',
  'a',
  'b',
  'c',
  'd',
  'e',
  'f',
  'g',
  'h',
  'i',
  '«',
  '»',
  'ð',
  'ý',
  'þ',
  '±',
  '°',
  'j',
  'k',
  'l',
  'm',
  'n',
  'o',
  'p',
  'q',
  'r',
  'ª',
  'º',
  'æ',
  '¸',
  'Æ',
  '¤',
  'µ',
  '~',
  's',
  't',
  'u',
  'v',
  'w',
  'x',
  'y',
  'z',
  '¡',
  '¿',
  'Ð',
  'Ý',
  'Þ',
  '®',
  '^',
  '£',
  '¥',
  '·',
  '©',
  '§',
  '¶',
  '¼',
  '½',
  '¾',
  '[',
  ']',
  '¯',
  '¨',
  '´',
  '×',
  '{',
  'A',
  'B',
  'C',
  'D',
  'E',
  'F',
  'G',
  'H',
  'I',
  '\u00ad',
  'ô',
  'ö',
  'ò',
  'ó',
  'õ',
  '}',
  'J',
  'K',
  'L',
  'M',
  'N',
  'O',
  'P',
  'Q',
  'R',
  '¹',
  'û',
  'ü',
  'ù',
  'ú',
  'ÿ',
  '\\',
  '÷',
  'S',
  'T',
  'U',
  'V',
  'W',
  'X',
  'Y',
  'Z',
  '²',
  'Ô',
  'Ö',
  'Ò',
  'Ó',
  'Õ',
  '0',
  '1',
  '2',
  '3',
  '4',
  '5',
  '6',
  '7',
  '8',
  '9',
  '³',
  'Û',
  'Ü',
  'Ù',
  'Ú',
  '\u009f',
] as const

const UNICODE_TO_EBCDIC = new Map<string, number>()

for (const [index, value] of EBCDIC_TO_UNICODE.entries()) {
  if (!UNICODE_TO_EBCDIC.has(value)) {
    UNICODE_TO_EBCDIC.set(value, index)
  }
}

function createDefaultCell(): TerminalCellState {
  return {
    character: ' ',
    foreground: 'neutral',
    background: 'neutral',
    isProtected: true,
    isFieldAttribute: false,
    isHidden: false,
    isIntensified: false,
    fieldIndex: -1,
  }
}

function createDefaultField(): TerminalFieldState {
  return {
    attributeAddress: -1,
    startAddress: 0,
    endAddress: 0,
    isProtected: true,
    isNumeric: false,
    isHidden: false,
    isIntensified: false,
    isModified: false,
    foreground: 'neutral',
    background: 'neutral',
  }
}

function mapColor(value: number): TN3270Color {
  switch (value) {
    case 0xf1:
      return 'blue'
    case 0xf2:
      return 'red'
    case 0xf3:
      return 'pink'
    case 0xf4:
      return 'green'
    case 0xf5:
      return 'turquoise'
    case 0xf6:
      return 'yellow'
    case 0xf7:
      return 'white'
    case 0xf8:
      return 'black'
    case 0xf9:
      return 'deepBlue'
    case 0xfa:
      return 'orange'
    case 0xfb:
      return 'purple'
    case 0xfc:
      return 'paleGreen'
    case 0xfd:
      return 'paleTurquoise'
    case 0xfe:
      return 'grey'
    default:
      return 'neutral'
  }
}

function mapAid(aidKey: Tn3270AidKey): number {
  switch (aidKey) {
    case 'SYSREQ':
      return AID_SYSREQ
    case 'PF1':
      return AID_PF1
    case 'PF2':
      return AID_PF2
    case 'PF3':
      return AID_PF3
    case 'PF4':
      return AID_PF4
    case 'PF5':
      return AID_PF5
    case 'PF6':
      return AID_PF6
    case 'PF7':
      return AID_PF7
    case 'PF8':
      return AID_PF8
    case 'PF9':
      return AID_PF9
    case 'PF10':
      return AID_PF10
    case 'PF11':
      return AID_PF11
    case 'PF12':
      return AID_PF12
    case 'PF13':
      return AID_PF13
    case 'PF14':
      return AID_PF14
    case 'PF15':
      return AID_PF15
    case 'PF16':
      return AID_PF16
    case 'PF17':
      return AID_PF17
    case 'PF18':
      return AID_PF18
    case 'PF19':
      return AID_PF19
    case 'PF20':
      return AID_PF20
    case 'PF21':
      return AID_PF21
    case 'PF22':
      return AID_PF22
    case 'PF23':
      return AID_PF23
    case 'PF24':
      return AID_PF24
    case 'PA1':
      return AID_PA1
    case 'PA2':
      return AID_PA2
    case 'PA3':
      return AID_PA3
    case 'CLEAR':
      return AID_CLEAR
    case 'ENTER':
      return AID_ENTER
  }
}

function aidRequiresModifiedFieldData(aidKey: Tn3270AidKey): boolean {
  switch (aidKey) {
    case 'ENTER':
    case 'PF1':
    case 'PF2':
    case 'PF3':
    case 'PF4':
    case 'PF5':
    case 'PF6':
    case 'PF7':
    case 'PF8':
    case 'PF9':
    case 'PF10':
    case 'PF11':
    case 'PF12':
    case 'PF13':
    case 'PF14':
    case 'PF15':
    case 'PF16':
    case 'PF17':
    case 'PF18':
    case 'PF19':
    case 'PF20':
    case 'PF21':
    case 'PF22':
    case 'PF23':
    case 'PF24':
      return true
    default:
      return false
  }
}

export class Tn3270TerminalScreen {
  private readonly cells: TerminalCellState[]
  private readonly fields: TerminalFieldState[] = []
  private readonly fieldAttributes = new Map<number, TerminalFieldState>()
  private cursorAddress = 0

  constructor(
    public readonly columns: number,
    public readonly rows: number,
  ) {
    this.cells = Array.from({ length: columns * rows }, () => createDefaultCell())
    this.clearScreen()
  }

  static fromTerminalType(terminalType: string): Tn3270TerminalScreen {
    const normalized = terminalType.trim().toUpperCase()

    switch (normalized) {
      case 'IBM-3278-3':
      case 'IBM-3278-3-E':
      case 'IBM-3279-3':
      case 'IBM-3279-3-E':
        return new Tn3270TerminalScreen(80, 32)
      case 'IBM-3278-4':
      case 'IBM-3278-4-E':
      case 'IBM-3279-4':
      case 'IBM-3279-4-E':
        return new Tn3270TerminalScreen(80, 43)
      case 'IBM-3278-5':
      case 'IBM-3278-5-E':
      case 'IBM-3279-5':
      case 'IBM-3279-5-E':
        return new Tn3270TerminalScreen(132, 27)
      default:
        return new Tn3270TerminalScreen(80, 24)
    }
  }

  applyInboundRecord(record: Uint8Array): void {
    if (record.length === 0) {
      return
    }

    const command = record[0]!
    console.log('[TN3270] applyInboundRecord', {
      command: `0x${command.toString(16).toUpperCase().padStart(2, '0')}`,
      length: record.length,
      firstBytes: Array.from(record.slice(0, 24)),
    })

    switch (command) {
      case COMMAND_WRITE:
      case COMMAND_ERASE_WRITE:
      case COMMAND_ERASE_WRITE_ALTERNATE:
        if (record.length < 2) {
          return
        }

        this.applyWriteRecord(record.slice(2), command !== COMMAND_WRITE)
        break

      case COMMAND_ERASE_ALL_UNPROTECTED:
        this.eraseAllUnprotected()
        break
    }
  }

  moveToAdjacentField(forward: boolean): boolean {
    if (this.fields.length === 0) {
      return false
    }

    const editableFields = this.fields
      .filter((field) => !field.isProtected && field.startAddress !== field.attributeAddress)
      .slice()
      .toSorted(
        (left: TerminalFieldState, right: TerminalFieldState) =>
          left.startAddress - right.startAddress,
      )

    if (editableFields.length === 0) {
      return false
    }

    const currentFieldIndex = this.getFieldIndexAt(this.cursorAddress)
    const orderedIndex = editableFields.findIndex(
      (field: TerminalFieldState) => this.fields.indexOf(field) === currentFieldIndex,
    )

    let targetIndex = forward
      ? (orderedIndex + 1 + editableFields.length) % editableFields.length
      : (orderedIndex - 1 + editableFields.length) % editableFields.length

    if (orderedIndex < 0) {
      targetIndex = forward ? 0 : editableFields.length - 1
    }

    this.cursorAddress = editableFields[targetIndex]?.startAddress ?? this.cursorAddress
    return true
  }

  moveCursor(deltaColumn: number, deltaRow: number): boolean {
    const column = this.cursorAddress % this.columns
    const row = Math.floor(this.cursorAddress / this.columns)
    const nextColumn = Math.max(0, Math.min(this.columns - 1, column + deltaColumn))
    const nextRow = Math.max(0, Math.min(this.rows - 1, row + deltaRow))
    const targetAddress = nextRow * this.columns + nextColumn

    if (!this.canEdit(targetAddress)) {
      return false
    }

    this.cursorAddress = targetAddress
    return true
  }

  moveCursorToFieldStart(): boolean {
    const fieldIndex = this.getFieldIndexAt(this.cursorAddress)
    if (fieldIndex < 0) {
      return false
    }

    this.cursorAddress = this.fields[fieldIndex]?.startAddress ?? this.cursorAddress
    return true
  }

  moveCursorToFieldEnd(): boolean {
    const fieldIndex = this.getFieldIndexAt(this.cursorAddress)
    if (fieldIndex < 0) {
      return false
    }

    this.cursorAddress = this.fields[fieldIndex]?.endAddress ?? this.cursorAddress
    return true
  }

  tryWriteCharacter(value: string): boolean {
    if (!this.canEdit(this.cursorAddress)) {
      return false
    }

    const fieldIndex = this.getFieldIndexAt(this.cursorAddress)
    if (fieldIndex < 0) {
      return false
    }

    const field = this.fields[fieldIndex]
    if (!field) {
      return false
    }

    if (field.isNumeric && !/[0-9 ]/.test(value)) {
      return false
    }

    this.cells[this.cursorAddress]!.character = value
    field.isModified = true

    if (this.cursorAddress !== field.endAddress) {
      this.cursorAddress = this.advance(this.cursorAddress)
    }

    return true
  }

  backspace(): boolean {
    const fieldIndex = this.getFieldIndexAt(this.cursorAddress)
    if (fieldIndex < 0) {
      return false
    }

    const field = this.fields[fieldIndex]
    if (!field) {
      return false
    }

    let targetAddress = this.cursorAddress

    if (!this.canEdit(targetAddress) && this.cursorAddress > field.startAddress) {
      targetAddress -= 1
    } else if (this.canEdit(targetAddress) && this.cursorAddress > field.startAddress) {
      targetAddress -= 1
    }

    if (!this.canEdit(targetAddress)) {
      return false
    }

    this.cells[targetAddress]!.character = ' '
    field.isModified = true
    this.cursorAddress = targetAddress
    return true
  }

  delete(): boolean {
    if (!this.canEdit(this.cursorAddress)) {
      return false
    }

    const fieldIndex = this.getFieldIndexAt(this.cursorAddress)
    if (fieldIndex < 0) {
      return false
    }

    this.cells[this.cursorAddress]!.character = ' '
    const field = this.fields[fieldIndex]
    if (field) {
      field.isModified = true
    }

    return true
  }

  buildAidRecord(aidKey: Tn3270AidKey): Uint8Array {
    const payload: number[] = [mapAid(aidKey)]
    payload.push(...this.encodeBufferAddress(this.cursorAddress))

    if (!aidRequiresModifiedFieldData(aidKey)) {
      return Uint8Array.from(payload)
    }

    for (const field of this.fields.filter((entry) => entry.isModified && !entry.isProtected)) {
      if (field.startAddress === field.attributeAddress) {
        continue
      }

      payload.push(ORDER_SET_BUFFER_ADDRESS)
      payload.push(...this.encodeBufferAddress(field.startAddress))

      for (const address of this.enumerateFieldAddresses(field)) {
        payload.push(this.encodeCharacter(this.cells[address]!.character))
      }
    }

    return Uint8Array.from(payload)
  }

  toSnapshot(
    connectionState: 'disconnected' | 'connecting' | 'connected',
    statusMessage: string,
    statusColor: TN3270Color,
  ): TN3270ScreenSnapshot {
    const cells: TerminalCell[][] = []

    for (let row = 0; row < this.rows; row += 1) {
      const rowCells: TerminalCell[] = []

      for (let column = 0; column < this.columns; column += 1) {
        const cell = this.cells[row * this.columns + column]!
        rowCells.push({
          char: cell.isHidden ? ' ' : cell.character,
          color: cell.foreground,
          backgroundColor: cell.background,
          protected: cell.isProtected,
          intensified: cell.isIntensified,
          fieldId: cell.fieldIndex >= 0 ? cell.fieldIndex.toString() : null,
        })
      }

      cells.push(rowCells)
    }

    const cursorCoordinates = this.getCursorCoordinates()

    return {
      title: 'TN3270 TERMINAL',
      rows: this.rows,
      cols: this.columns,
      cells,
      cursor:
        connectionState === 'connected'
          ? { row: cursorCoordinates.row, col: cursorCoordinates.col }
          : null,
      connectionState,
      statusMessage,
      statusColor,
    }
  }

  private getCursorCoordinates(): { row: number; col: number } {
    return {
      row: Math.floor(this.cursorAddress / this.columns),
      col: this.cursorAddress % this.columns,
    }
  }

  private applyWriteRecord(orders: Uint8Array, erase: boolean): void {
    if (erase) {
      this.clearScreen()
    }

    let address = 0
    let currentField = createDefaultField()
    currentField.endAddress = this.bufferLength - 1
    let foreground: TN3270Color = 'neutral'
    let background: TN3270Color = 'neutral'
    let intensified = false

    for (let offset = 0; offset < orders.length; offset += 1) {
      const value = orders[offset]!

      switch (value) {
        case ORDER_PROGRAM_TAB:
          address = this.findNextEditableAddress(address)
          break

        case ORDER_GRAPHIC_ESCAPE:
          offset += 1
          break

        case ORDER_SET_BUFFER_ADDRESS:
          if (offset + 2 >= orders.length) {
            return
          }

          address = this.decodeBufferAddress(orders[offset + 1]!, orders[offset + 2]!)
          offset += 2
          break

        case ORDER_ERASE_UNPROTECTED_TO_ADDRESS: {
          if (offset + 2 >= orders.length) {
            return
          }

          const target = this.decodeBufferAddress(orders[offset + 1]!, orders[offset + 2]!)
          this.eraseUnprotectedRange(address, target)
          address = target
          offset += 2
          break
        }

        case ORDER_INSERT_CURSOR:
          this.cursorAddress = address
          break

        case ORDER_START_FIELD:
          if (offset + 1 >= orders.length) {
            return
          }

          currentField = this.createFieldFromBaseAttribute(address, orders[offset + 1]!)
          foreground = currentField.foreground
          background = currentField.background
          intensified = currentField.isIntensified
          this.writeFieldAttribute(address, currentField)
          address = this.advance(address)
          offset += 1
          break

        case ORDER_SET_ATTRIBUTE:
          if (offset + 2 >= orders.length) {
            return
          }

          this.applyExtendedAttribute(
            orders[offset + 1]!,
            orders[offset + 2]!,
            currentField,
            (nextForeground, nextBackground, nextIntensified) => {
              foreground = nextForeground
              background = nextBackground
              intensified = nextIntensified
            },
          )
          offset += 2
          break

        case ORDER_START_FIELD_EXTENDED: {
          if (offset + 1 >= orders.length) {
            return
          }

          const pairCount = orders[offset + 1]!
          if (offset + 1 + pairCount * 2 >= orders.length) {
            return
          }

          currentField = this.createFieldFromExtendedAttributes(
            address,
            orders.slice(offset + 2, offset + 2 + pairCount * 2),
          )
          foreground = currentField.foreground
          background = currentField.background
          intensified = currentField.isIntensified
          this.writeFieldAttribute(address, currentField)
          address = this.advance(address)
          offset += 1 + pairCount * 2
          break
        }

        case ORDER_MODIFY_FIELD: {
          if (offset + 1 >= orders.length) {
            return
          }

          const pairCount = orders[offset + 1]!
          if (offset + 1 + pairCount * 2 >= orders.length) {
            return
          }

          this.modifyFieldAt(address, orders.slice(offset + 2, offset + 2 + pairCount * 2))
          offset += 1 + pairCount * 2
          break
        }

        case ORDER_REPEAT_TO_ADDRESS: {
          if (offset + 3 >= orders.length) {
            return
          }

          const target = this.decodeBufferAddress(orders[offset + 1]!, orders[offset + 2]!)
          const repeatedCharacter = this.decodeCharacter(orders[offset + 3]!)

          while (address !== target) {
            this.writeCharacter(
              address,
              repeatedCharacter,
              currentField,
              foreground,
              background,
              intensified,
            )
            address = this.advance(address)
          }

          offset += 3
          break
        }

        default:
          this.writeCharacter(
            address,
            this.decodeCharacter(value),
            currentField,
            foreground,
            background,
            intensified,
          )
          address = this.advance(address)
          break
      }
    }

    this.rebuildFieldMembership()

    if (!this.canEdit(this.cursorAddress)) {
      this.cursorAddress = this.findFirstEditableAddress()
    }
  }

  private modifyFieldAt(address: number, pairs: Uint8Array): void {
    const field = this.fieldAttributes.get(address)
    if (!field) {
      return
    }

    let foreground = field.foreground
    let background = field.background
    let intensified = field.isIntensified

    for (let index = 0; index < pairs.length; index += 2) {
      this.applyExtendedAttribute(
        pairs[index]!,
        pairs[index + 1]!,
        field,
        (nextForeground, nextBackground, nextIntensified) => {
          foreground = nextForeground
          background = nextBackground
          intensified = nextIntensified
        },
      )
    }

    field.foreground = foreground
    field.background = background
    field.isIntensified = intensified
    this.writeFieldAttribute(address, field)
  }

  private writeFieldAttribute(address: number, field: TerminalFieldState): void {
    this.fieldAttributes.set(address, field)

    const cell = this.cells[address]!
    cell.character = ' '
    cell.foreground = field.foreground
    cell.background = field.background
    cell.isProtected = true
    cell.isFieldAttribute = true
    cell.isHidden = true
    cell.isIntensified = field.isIntensified
    cell.fieldIndex = -1
  }

  private createFieldFromBaseAttribute(address: number, attribute: number): TerminalFieldState {
    const field = createDefaultField()
    field.attributeAddress = address
    field.startAddress = address + 1
    field.isProtected = (attribute & 0x20) !== 0
    field.isNumeric = (attribute & 0x10) !== 0
    field.isModified = (attribute & 0x01) !== 0

    const displayBits = attribute & 0x0c
    field.isHidden = displayBits === 0x0c
    field.isIntensified = displayBits === 0x08
    return field
  }

  private createFieldFromExtendedAttributes(
    address: number,
    pairs: Uint8Array,
  ): TerminalFieldState {
    let field = createDefaultField()
    field.attributeAddress = address
    field.startAddress = address + 1
    let foreground = field.foreground
    let background = field.background
    let intensified = field.isIntensified

    for (let index = 0; index < pairs.length; index += 2) {
      const type = pairs[index]!
      const value = pairs[index + 1]!

      if (type === 0xc0) {
        field = this.createFieldFromBaseAttribute(address, value)
        foreground = field.foreground
        background = field.background
        intensified = field.isIntensified
        continue
      }

      this.applyExtendedAttribute(
        type,
        value,
        field,
        (nextForeground, nextBackground, nextIntensified) => {
          foreground = nextForeground
          background = nextBackground
          intensified = nextIntensified
        },
      )
    }

    field.foreground = foreground
    field.background = background
    field.isIntensified = intensified
    return field
  }

  private applyExtendedAttribute(
    type: number,
    value: number,
    field: TerminalFieldState,
    setValues: (foreground: TN3270Color, background: TN3270Color, intensified: boolean) => void,
  ): void {
    let foreground = field.foreground
    let background = field.background
    let intensified = field.isIntensified

    switch (type) {
      case 0x41:
        intensified = value === 0xf2 || value === 0xf8
        break
      case 0x42:
        foreground = mapColor(value)
        break
      case 0x45:
        background = mapColor(value)
        break
      case 0x46:
        field.isHidden = value === 0xf1
        break
    }

    field.foreground = foreground
    field.background = background
    field.isIntensified = intensified
    setValues(foreground, background, intensified)
  }

  private rebuildFieldMembership(): void {
    this.fields.length = 0

    for (const cell of this.cells) {
      cell.fieldIndex = -1
      if (!cell.isFieldAttribute) {
        cell.isProtected = true
        cell.isHidden = false
        cell.isIntensified = false
        cell.foreground = 'neutral'
        cell.background = 'neutral'
      }
    }

    const orderedAttributeAddresses = [...this.fieldAttributes.keys()].toSorted(
      (left: number, right: number) => left - right,
    )
    if (orderedAttributeAddresses.length === 0) {
      for (const cell of this.cells) {
        cell.isProtected = true
      }

      return
    }

    for (let index = 0; index < orderedAttributeAddresses.length; index += 1) {
      const attributeAddress = orderedAttributeAddresses[index]!
      const nextAttributeAddress =
        orderedAttributeAddresses[(index + 1) % orderedAttributeAddresses.length]!
      const field = this.fieldAttributes.get(attributeAddress)
      if (!field) {
        continue
      }

      field.startAddress = this.advance(attributeAddress)
      field.endAddress = this.normalizeAddress(nextAttributeAddress - 1)
      this.fields.push(field)

      let address = field.startAddress
      while (true) {
        const cell = this.cells[address]!
        cell.fieldIndex = index
        cell.isProtected = field.isProtected
        cell.isHidden = field.isHidden
        cell.isIntensified = field.isIntensified
        cell.foreground = field.foreground
        cell.background = field.background

        if (address === field.endAddress) {
          break
        }

        address = this.advance(address)
      }
    }
  }

  private writeCharacter(
    address: number,
    character: string,
    field: TerminalFieldState,
    foreground: TN3270Color,
    background: TN3270Color,
    intensified: boolean,
  ): void {
    const cell = this.cells[address]!
    cell.character = character
    cell.foreground = foreground
    cell.background = background
    cell.isFieldAttribute = false
    cell.isProtected = field.isProtected
    cell.isHidden = field.isHidden
    cell.isIntensified = intensified || field.isIntensified
  }

  private clearScreen(): void {
    this.fieldAttributes.clear()
    this.fields.length = 0

    for (let index = 0; index < this.cells.length; index += 1) {
      this.cells[index] = createDefaultCell()
    }

    this.cursorAddress = 0
  }

  private eraseAllUnprotected(): void {
    for (const field of this.fields.filter((entry) => !entry.isProtected)) {
      for (const address of this.enumerateFieldAddresses(field)) {
        this.cells[address]!.character = ' '
      }

      field.isModified = false
    }

    this.cursorAddress = this.findFirstEditableAddress()
  }

  private eraseUnprotectedRange(startAddress: number, endAddress: number): void {
    let address = startAddress

    while (address !== endAddress) {
      if (this.canEdit(address)) {
        this.cells[address]!.character = ' '
      }

      address = this.advance(address)
    }
  }

  private findFirstEditableAddress(): number {
    return this.findNextEditableAddress(this.bufferLength - 1)
  }

  private findNextEditableAddress(startAddress: number): number {
    for (let offset = 1; offset <= this.bufferLength; offset += 1) {
      const candidate = this.normalizeAddress(startAddress + offset)
      if (this.canEdit(candidate)) {
        return candidate
      }
    }

    return 0
  }

  private canEdit(address: number): boolean {
    return (
      address >= 0 &&
      address < this.bufferLength &&
      !this.cells[address]!.isFieldAttribute &&
      !this.cells[address]!.isProtected
    )
  }

  private getFieldIndexAt(address: number): number {
    if (address < 0 || address >= this.bufferLength) {
      return -1
    }

    return this.cells[address]!.fieldIndex
  }

  private advance(address: number): number {
    return this.normalizeAddress(address + 1)
  }

  private *enumerateFieldAddresses(field: TerminalFieldState): Generator<number> {
    let address = field.startAddress

    while (true) {
      yield address

      if (address === field.endAddress) {
        return
      }

      address = this.advance(address)
    }
  }

  private normalizeAddress(address: number): number {
    const result = address % this.bufferLength
    return result < 0 ? result + this.bufferLength : result
  }

  private get bufferLength(): number {
    return this.columns * this.rows
  }

  private decodeBufferAddress(first: number, second: number): number {
    if ((first & 0xc0) === 0x00) {
      return ((first << 8) | second) & 0x3fff
    }

    return (this.decodeSixBit(first) << 6) | this.decodeSixBit(second)
  }

  private decodeSixBit(value: number): number {
    return ADDRESS_DECODING_TABLE.get(value) ?? 0
  }

  private encodeBufferAddress(address: number): number[] {
    const normalized = address & 0x0fff
    return [
      ADDRESS_ENCODING_TABLE[(normalized >> 6) & 0x3f]!,
      ADDRESS_ENCODING_TABLE[normalized & 0x3f]!,
    ]
  }

  private decodeCharacter(value: number): string {
    const decoded = EBCDIC_TO_UNICODE[value]
    if (!decoded || decoded < ' ' || decoded === '\u007f') {
      return ' '
    }

    return decoded
  }

  private encodeCharacter(value: string): number {
    return UNICODE_TO_EBCDIC.get(value) ?? 0x40
  }
}
