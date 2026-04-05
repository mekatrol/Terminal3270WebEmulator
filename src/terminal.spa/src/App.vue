<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'

import { fetchAppConfig } from '@/services/appConfig'
import {
  getBrowserAuthService,
  isAuthStateChangeStorageEvent,
  type AuthState,
} from '@/services/auth'
import '@/styles/app-shell.css'

const route = useRoute()
const router = useRouter()
const authService = getBrowserAuthService()
const authState = ref<AuthState>(authService.getState())
const terminalEndpointDisplayName = ref('Terminal 3270 Web Emulator')
const isAuthActionPending = ref(false)

const authButtonLabel = computed(() => (authState.value.isAuthenticated ? 'Sign out' : 'Sign in'))
const canAccessAdminSessions = computed(
  () => authState.value.isAuthenticated && authState.value.roles.includes('Server.Admin'),
)
const headerNavigationTarget = computed(() =>
  route.name === 'admin-sessions'
    ? {
        to: '/terminal',
        label: 'Terminal',
      }
    : {
        to: '/admin/sessions',
        label: 'Admin sessions',
      },
)
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

  if (
    route.meta.requiresAuth &&
    !authState.value.isAuthenticated &&
    route.name !== 'auth-callback' &&
    route.name !== 'signed-out'
  ) {
    await router.replace({
      name: 'signed-out',
      query: {
        returnTo: route.fullPath,
      },
    })
  }
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

function handleStorageChange(event: StorageEvent): void {
  if (!isAuthStateChangeStorageEvent(event)) {
    return
  }

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
  window.addEventListener('storage', handleStorageChange)
})

onUnmounted(() => {
  window.removeEventListener('focus', handleWindowFocus)
  window.removeEventListener('storage', handleStorageChange)
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
      <nav v-if="canAccessAdminSessions" class="header-nav" aria-label="Administrative navigation">
        <RouterLink class="header-link app-button" :to="headerNavigationTarget.to">
          {{ headerNavigationTarget.label }}
        </RouterLink>
      </nav>
      <button
        type="button"
        class="auth-button app-button app-button-primary"
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
