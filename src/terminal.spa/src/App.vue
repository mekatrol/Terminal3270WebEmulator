<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterView, useRoute } from 'vue-router'

import { fetchAppConfig } from '@/services/appConfig'
import { getBrowserAuthService, type AuthState } from '@/services/auth'

const route = useRoute()
const authService = getBrowserAuthService()
const authState = ref<AuthState>(authService.getState())
const terminalEndpointDisplayName = ref('Terminal 3270 Web Emulator')
const isAuthActionPending = ref(false)

const authButtonLabel = computed(() => (authState.value.isAuthenticated ? 'Sign out' : 'Sign in'))
const identitySummary = computed(() =>
  authState.value.isAuthenticated
    ? `${authState.value.displayName} · ${authState.value.roles.join(', ')}`
    : 'Not signed in',
)

async function refreshAuthState(): Promise<void> {
  await authService.ensureSession()

  const requiredRole =
    typeof route.meta.requiredRole === 'string' ? route.meta.requiredRole : undefined

  authState.value = authService.getState(requiredRole)
}

async function handleAuthAction(): Promise<void> {
  isAuthActionPending.value = true

  try {
    if (authState.value.isAuthenticated) {
      await authService.signOut(route.fullPath)
      return
    }

    await authService.beginSignIn()
  } finally {
    isAuthActionPending.value = false
  }
}

function handleWindowFocus(): void {
  void refreshAuthState()
}

watch(
  () => route.fullPath,
  () => {
    void refreshAuthState()
  },
  { immediate: true },
)

onMounted(() => {
  void fetchAppConfig().then((appConfig) => {
    terminalEndpointDisplayName.value = appConfig.terminalEndpointDisplayName
  })

  window.addEventListener('focus', handleWindowFocus)
  window.addEventListener('storage', handleWindowFocus)
})

onUnmounted(() => {
  window.removeEventListener('focus', handleWindowFocus)
  window.removeEventListener('storage', handleWindowFocus)
})
</script>

<template>
  <div class="app-shell">
    <header class="app-header">
      <div class="brand-block">
        <p class="brand-kicker">Terminal 3270</p>
        <h1 class="brand-title">{{ terminalEndpointDisplayName }}</h1>
      </div>
      <div class="identity-block" aria-live="polite">
        <p class="identity-label">Current identity</p>
        <p class="identity-value">{{ identitySummary }}</p>
      </div>
      <button
        type="button"
        class="auth-button"
        :disabled="isAuthActionPending"
        @click="handleAuthAction"
      >
        {{ authButtonLabel }}
      </button>
    </header>

    <main class="app-content">
      <RouterView />
    </main>
  </div>
</template>

<style scoped>
:global(html),
:global(body),
:global(#app) {
  margin: 0;
  min-height: 100%;
}

:global(body) {
  background:
    radial-gradient(circle at top left, rgb(18 55 70 / 38%), transparent 24%),
    linear-gradient(180deg, #020608 0%, #08131a 100%);
}

.app-shell {
  --app-header-height: 5.5rem;
  min-height: 100vh;
}

.app-header {
  position: sticky;
  top: 0;
  z-index: 10;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr) auto;
  gap: 1rem;
  align-items: center;
  padding: 1rem 1.5rem;
  border-bottom: 1px solid rgb(119 189 212 / 18%);
  background: rgb(4 12 17 / 88%);
  backdrop-filter: blur(12px);
  color: #d8eef5;
}

.brand-kicker {
  margin: 0;
  color: #77bdd4;
  font:
    700 0.78rem/1.2 'Segoe UI',
    Tahoma,
    Geneva,
    Verdana,
    sans-serif;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

.brand-title {
  margin: 0.18rem 0 0;
  font:
    700 1.4rem/1.1 Georgia,
    'Times New Roman',
    serif;
}

.identity-block {
  min-width: 0;
}

.identity-label {
  margin: 0;
  color: #77bdd4;
  font:
    600 0.78rem/1.2 'Segoe UI',
    Tahoma,
    Geneva,
    Verdana,
    sans-serif;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.identity-value {
  margin: 0.25rem 0 0;
  overflow: hidden;
  color: #f1fafc;
  text-overflow: ellipsis;
  white-space: nowrap;
  font:
    500 0.98rem/1.4 'Segoe UI',
    Tahoma,
    Geneva,
    Verdana,
    sans-serif;
}

.auth-button {
  padding: 0.85rem 1.15rem;
  border: 1px solid rgb(119 189 212 / 32%);
  border-radius: 999px;
  background: linear-gradient(135deg, #0d536b 0%, #1883a6 100%);
  color: #effbfd;
  font:
    700 0.95rem/1 'Segoe UI',
    Tahoma,
    Geneva,
    Verdana,
    sans-serif;
  cursor: pointer;
}

.auth-button:disabled {
  cursor: wait;
  opacity: 0.72;
}

.app-content {
  min-height: calc(100vh - var(--app-header-height));
}

@media (max-width: 900px) {
  .app-shell {
    --app-header-height: 9.5rem;
  }

  .app-header {
    grid-template-columns: 1fr;
    align-items: start;
  }

  .identity-value {
    white-space: normal;
  }
}
</style>
