import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import App from '../App.vue'
import router from '../router'

function getSessionLauncherButton(
  wrapper: ReturnType<typeof mount>,
): ReturnType<typeof wrapper.get> {
  return wrapper.get('button.session-launcher-button')
}

const showSessionLauncher = ref(true)
const showSessionNotice = ref(false)
const sessionLauncherMessage = ref('Start a new terminal session.')
const sessionNoticeMessage = ref<string | null>(null)
const sessionNoticeTitle = ref<string | null>(null)
const startSession = vi.fn(async () => {})
const dismissSessionNotice = vi.fn(() => {
  showSessionNotice.value = false
  sessionNoticeTitle.value = null
  sessionNoticeMessage.value = null
})
const fetchMock = vi.fn<typeof fetch>()

vi.stubGlobal('fetch', fetchMock)

vi.mock('@/composables/useTN3270Session', () => ({
  useTN3270Session: (): ReturnType<
    typeof import('@/composables/useTN3270Session').useTN3270Session
  > => ({
    accessibleSummary: computed(() => 'TERMINAL 3270 EMULATOR. CONNECTED. connected.'),
    dismissSessionNotice,
    handleKeydown: vi.fn(async () => {}),
    sessionNoticeMessage: computed(() => sessionNoticeMessage.value),
    sessionNoticeTitle: computed(() => sessionNoticeTitle.value),
    sessionLauncherMessage: computed(() => sessionLauncherMessage.value),
    showSessionNotice: computed(() => showSessionNotice.value),
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
    showSessionNotice.value = false
    sessionLauncherMessage.value = 'Start a new terminal session.'
    sessionNoticeMessage.value = null
    sessionNoticeTitle.value = null
    dismissSessionNotice.mockClear()
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
    await getSessionLauncherButton(wrapper).trigger('click')
    expect(startSession).toHaveBeenCalledOnce()
  })

  it('renders a selectable notice overlay for copyable terminal errors', async () => {
    showSessionNotice.value = true
    sessionNoticeTitle.value = 'Terminal Connection Failed'
    sessionNoticeMessage.value =
      'Unable to establish the terminal session connection. Confirm the server is available and try again.'

    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    expect(wrapper.get('[role="dialog"]').text()).toContain('Terminal Connection Failed')
    expect(wrapper.text()).toContain(
      'Unable to establish the terminal session connection. Confirm the server is available and try again.',
    )

    await wrapper.get('button.session-notice-button').trigger('click')
    expect(dismissSessionNotice).toHaveBeenCalledOnce()
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

    expect(document.activeElement).toBe(getSessionLauncherButton(wrapper).element)
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
