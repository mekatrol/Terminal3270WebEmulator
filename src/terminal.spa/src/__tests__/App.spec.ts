import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import App from '../App.vue'
import router from '../router'
import { getBrowserAuthService } from '../services/auth'

function getSessionLauncherButton(
  wrapper: ReturnType<typeof mount>,
): ReturnType<typeof wrapper.get> {
  return wrapper.get('button.session-launcher-button')
}

const showSessionLauncher = ref(true)
const showSessionNotice = ref(false)
const canStartSession = ref(true)
const sessionLauncherTitle = ref('Start a new terminal session')
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
    canStartSession: computed(() => canStartSession.value),
    dismissSessionNotice,
    handleKeydown: vi.fn(async () => {}),
    sessionLauncherTitle: computed(() => sessionLauncherTitle.value),
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
    canStartSession.value = true
    sessionLauncherTitle.value = 'Start a new terminal session'
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

  it('moves focus to the notice button and handles Enter there instead of the launcher form', async () => {
    showSessionNotice.value = true
    sessionNoticeTitle.value = 'Session Terminated'
    sessionNoticeMessage.value = 'Your terminal session was terminated by an administrator.'

    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      attachTo: document.body,
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    const noticeButton = wrapper.get('button.session-notice-button')
    expect(document.activeElement).toBe(noticeButton.element)
    expect(wrapper.get('.session-launcher').attributes('aria-hidden')).toBe('true')

    await noticeButton.trigger('keydown.enter')

    expect(dismissSessionNotice).toHaveBeenCalledOnce()
    expect(startSession).not.toHaveBeenCalled()
  })

  it('hides the start-session button and shows an unavailable heading when terminal access is denied', async () => {
    canStartSession.value = false
    sessionLauncherTitle.value = 'Terminal session unavailable'
    sessionLauncherMessage.value =
      'You are not permitted to open a terminal session on this application.'

    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    expect(wrapper.text()).toContain('Terminal session unavailable')
    expect(wrapper.text()).toContain(
      'You are not permitted to open a terminal session on this application.',
    )
    expect(wrapper.find('button.session-launcher-button').exists()).toBe(false)
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

  it('shows an admin sessions link in the title bar for server administrators', async () => {
    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    const adminLink = wrapper.get('a.header-link')
    expect(adminLink.text()).toBe('Admin sessions')
    expect(adminLink.attributes('href')).toBe('/admin/sessions')
    expect(adminLink.attributes('target')).toBeUndefined()
  })

  it('shows a terminal link in the title bar on the admin sessions route', async () => {
    router.push('/admin/sessions')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    const terminalLink = wrapper.get('a.header-link')
    expect(terminalLink.text()).toBe('Terminal')
    expect(terminalLink.attributes('href')).toBe('/terminal')
    expect(terminalLink.attributes('target')).toBeUndefined()
  })

  it('navigates to the signed-out route when another tab clears the shared session', async () => {
    router.push('/terminal')
    await router.isReady()

    const authService = getBrowserAuthService()
    const ensureSessionSpy = vi.spyOn(authService, 'ensureSession').mockResolvedValue(false)
    const getStateSpy = vi.spyOn(authService, 'getState').mockReturnValue({
      isAuthenticated: false,
      hasRequiredRole: false,
      displayName: 'Anonymous',
      roles: [],
    })

    mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    window.dispatchEvent(
      new StorageEvent('storage', {
        key: 'terminal.oidc.state-change',
        newValue: JSON.stringify({
          eventType: 'signed-out',
          occurredAtUtc: new Date().toISOString(),
        }),
      }),
    )

    await flushPromises()

    expect(router.currentRoute.value.name).toBe('signed-out')

    ensureSessionSpy.mockRestore()
    getStateSpy.mockRestore()
  })
})
