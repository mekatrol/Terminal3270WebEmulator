import { beforeEach, describe, expect, it, vi } from 'vitest'

import { createTerminalSessionTransport } from '@/services/terminalSession'

const fetchMock = vi.fn<typeof fetch>()

vi.stubGlobal('fetch', fetchMock)

describe('terminalSession', () => {
  beforeEach(() => {
    fetchMock.mockReset()
  })

  it('rejects terminal startup when the API probe returns 403', async () => {
    fetchMock.mockResolvedValue(
      new Response('Forbidden', {
        status: 403,
        headers: {
          'Content-Type': 'text/plain',
        },
      }),
    )

    const transport = createTerminalSessionTransport()

    await expect(
      transport.connect({
        onDisconnect: vi.fn(),
        onError: vi.fn(),
        onFrame: vi.fn(),
      }),
    ).rejects.toThrow('403 You do not have permission to open a terminal session.')
  })
})
