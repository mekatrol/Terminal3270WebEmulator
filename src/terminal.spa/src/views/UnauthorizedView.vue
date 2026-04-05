<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'
import '@/styles/auth-views.css'

const route = useRoute()
const authService = getBrowserAuthService()
const authState = authService.getState()
const requiredRole = computed(() =>
  typeof route.query.requiredRole === 'string' && route.query.requiredRole.length > 0
    ? route.query.requiredRole
    : 'the required application role',
)

async function handleSignOut(): Promise<void> {
  const returnToPath =
    typeof route.query.returnTo === 'string' && route.query.returnTo.startsWith('/')
      ? route.query.returnTo
      : '/'

  await authService.signOut(returnToPath)
}
</script>

<template>
  <section class="app-screen auth-view" aria-labelledby="unauthorized-title">
    <section class="auth-panel app-panel" aria-labelledby="unauthorized-title">
      <p class="app-kicker">Authorization</p>
      <h1 id="unauthorized-title">Access denied</h1>
      <p>
        The current identity is authenticated as {{ authState.displayName }}, but it does not hold
        the <code>{{ requiredRole }}</code> role required for this route.
      </p>
      <p>Sign out and authenticate with a different account if you need administrative access.</p>
      <button type="button" class="app-button app-button-danger" @click="handleSignOut">
        Sign out
      </button>
    </section>
  </section>
</template>
