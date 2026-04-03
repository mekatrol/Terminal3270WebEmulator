import { describe, it, expect } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import App from '../App.vue'
import router from '../router'

describe('App', () => {
  it('renders the fullscreen TN 3270 emulator route for authorized users', async () => {
    router.push('/terminal')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router],
      },
    })

    await flushPromises()

    expect(() => wrapper.get('[data-testid="TN-3270-terminal"]')).not.toThrow()
    expect(wrapper.text()).toContain('TN 3270 INFORMATION')
    expect(wrapper.text()).toContain('TERMINAL 3270 EMULATOR')
    expect(wrapper.text()).toContain('connected')
  })
})
