import type { TN3270ScreenSnapshot, TN3270SessionBootstrap } from '@/types/TN3270'

export interface TerminalSessionTransport {
  connect(): Promise<TN3270SessionBootstrap>
  disconnect(): Promise<void>
  submitAidKey(aidKey: string, payload: Record<string, string>): Promise<void>
}

export class MockTerminalSessionTransport implements TerminalSessionTransport {
  async connect(): Promise<TN3270SessionBootstrap> {
    return {
      title: 'TN 3270 INFORMATION DISPLAY',
      instructions: [
        {
          row: 13,
          col: 8,
          text: 'TYPE INTO THE UNPROTECTED FIELDS. ENTER OR TAB ADVANCES TO THE NEXT FIELD.',
          color: 'blue',
        },
        {
          row: 14,
          col: 8,
          text: 'THIS SESSION SERVICE STUB WILL LATER BE FED FROM HOST 3270 DATA OVER WSS.',
          color: 'turquoise',
        },
      ],
      fields: [
        {
          id: 'system',
          row: 2,
          col: 34,
          length: 22,
          label: 'SYSTEM:',
          labelColor: 'green',
          value: 'TERMINAL3270 DEMO',
          protected: true,
        },
        {
          id: 'sessionId',
          row: 4,
          col: 34,
          length: 18,
          label: 'SESSION ID . . . .:',
          labelColor: 'neutral',
          value: 'A17C9',
          protected: true,
        },
        {
          id: 'user',
          row: 6,
          col: 34,
          length: 18,
          label: 'USER . . . . . . .:',
          labelColor: 'neutral',
          value: 'OPERATOR',
          protected: false,
        },
        {
          id: 'account',
          row: 8,
          col: 34,
          length: 18,
          label: 'ACCOUNT . . . . .:',
          labelColor: 'neutral',
          value: 'CICS001',
          protected: false,
        },
        {
          id: 'password',
          row: 10,
          col: 34,
          length: 18,
          label: 'PASSWORD . . . . .:',
          labelColor: 'yellow',
          value: '',
          protected: false,
          intensified: true,
        },
        {
          id: 'command',
          row: 12,
          col: 34,
          length: 18,
          label: 'COMMAND . . . . .:',
          labelColor: 'yellow',
          value: '',
          protected: false,
          intensified: true,
        },
        {
          id: 'pfkeys',
          row: 17,
          col: 8,
          length: 66,
          label: 'PF3=EXIT  PF5=REFRESH  ENTER=SEND  TAB=NEXT FIELD  SHIFT+TAB=PREV FIELD',
          labelColor: 'pink',
          value: '',
          protected: true,
        },
        {
          id: 'colors',
          row: 19,
          col: 8,
          length: 70,
          label: '3270 COLOR MAP: NEUTRAL BLUE RED PINK GREEN TURQUOISE YELLOW WHITE',
          labelColor: 'white',
          value: '',
          protected: true,
        },
      ],
    }
  }

  async disconnect(): Promise<void> {
    return
  }

  async submitAidKey(_aidKey: string, _payload: Record<string, string>): Promise<void> {
    return
  }
}

export function createTerminalSessionTransport(): TerminalSessionTransport {
  return new MockTerminalSessionTransport()
}

export function summarizeSnapshot(snapshot: TN3270ScreenSnapshot): string {
  return `${snapshot.title}. ${snapshot.statusMessage}. ${snapshot.connectionState}.`
}
