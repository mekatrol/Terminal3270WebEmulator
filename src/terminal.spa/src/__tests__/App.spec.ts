import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import App from '../App.vue'
import router from '../router'

const showSessionLauncher = ref(true)
const sessionLauncherMessage = ref('Start a new terminal session.')
const startSession = vi.fn(async () => {})
const fetchMock = vi.fn<typeof fetch>()

vi.stubGlobal('fetch', fetchMock)

vi.mock('@/composables/useTN3270Session', () => ({
  useTN3270Session: (): ReturnType<
    typeof import('@/composables/useTN3270Session').useTN3270Session
  > => ({
    accessibleSummary: computed(() => 'TERMINAL 3270 EMULATOR. CONNECTED. connected.'),
    handleKeydown: vi.fn(async () => {}),
    sessionLauncherMessage: computed(() => sessionLauncherMessage.value),
    showSessionLauncher: computed(() => showSessionLauncher.value),
    snapshot: ref({
      rows: 1,
      cols: 3,
      cells: [
        [
          {
            char: 'A',
            color: 'green',
            backgroundColor: 'black',
            protected: true,
            intensified: false,
            fieldId: null,
          },
          {
            char: 'B',
            color: 'green',
            backgroundColor: 'black',
            protected: true,
            intensified: false,
            fieldId: null,
          },
          {
            char: 'C',
            color: 'green',
            backgroundColor: 'black',
            protected: true,
            intensified: false,
            fieldId: null,
          },
        ],
      ],
      cursor: null,
      connectionState: 'connected',
      statusMessage: 'CONNECTED',
      statusColor: 'green',
      title: 'TN 3270 INFORMATION',
    }),
    startSession,
  }),
}))

describe('App', () => {
  beforeEach(() => {
    showSessionLauncher.value = true
    sessionLauncherMessage.value = 'Start a new terminal session.'
    startSession.mockClear()
    fetchMock.mockReset()
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ sessions: [] }), {
        status: 200,
        headers: {
          'Content-Type': 'application/json',
        },
      }),
    )
  })

  it('starts on the HTTP-side start-session page for authorized users', async () => {
    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    expect(wrapper.text()).toContain('Start a new terminal session')
    expect(wrapper.text()).not.toContain('The terminal session ended. Start a new session.')
  })

  it('renders the terminal grid once a session is active', async () => {
    showSessionLauncher.value = false

    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    expect(() => wrapper.get('[data-testid="TN-3270-terminal"]')).not.toThrow()
    expect(wrapper.text()).not.toContain('Start a new terminal session')
  })

  it('shows an HTTP-side restart component after the terminal session ends', async () => {
    sessionLauncherMessage.value = 'The terminal session ended. Start a new session.'

    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    expect(wrapper.text()).toContain('Start a new terminal session')
    expect(wrapper.text()).toContain('The terminal session ended. Start a new session.')
    await wrapper.get('button').trigger('click')
    expect(startSession).toHaveBeenCalledOnce()
  })

  it('submits the start-session form when the launcher form is submitted', async () => {
    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()
    await wrapper.get('form').trigger('submit')

    expect(startSession).toHaveBeenCalledOnce()
  })

  it('moves focus to the start-session button when the launcher is shown', async () => {
    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      attachTo: document.body,
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    expect(document.activeElement).toBe(wrapper.get('button').element)
  })

  it('moves focus to the terminal surface when the session launcher is dismissed', async () => {
    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      attachTo: document.body,
      global: {
        plugins: [router],
      },
    })

    await flushPromises()
    showSessionLauncher.value = false
    await flushPromises()

    expect(document.activeElement).toBe(wrapper.get('[role="application"]').element)
  })

  it('renders the admin sessions route with a semantic table', async () => {
    router.push('/admin/sessions')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    expect(wrapper.get('table').element.tagName).toBe('TABLE')
    expect(wrapper.text()).toContain('Terminal sessions')
    expect(wrapper.text()).toContain('No terminal sessions are currently recorded.')
  })
})
