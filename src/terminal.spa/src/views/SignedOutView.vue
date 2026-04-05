<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'
import '@/styles/auth-views.css'

const route = useRoute()
const authService = getBrowserAuthService()
const returnToPath = computed(() =>
  typeof route.query.returnTo === 'string' && route.query.returnTo.startsWith('/')
    ? route.query.returnTo
    : '/terminal',
)

async function handleSignIn(): Promise<void> {
  await authService.beginSignIn(returnToPath.value)
}
</script>

<template>
  <main class="app-screen auth-view">
    <section class="auth-panel app-panel" aria-labelledby="signed-out-title">
      <p class="app-kicker">Session state</p>
      <h1 id="signed-out-title">Signed out</h1>
      <p>
        This browser tab was signed out because the shared session ended in another tab or window.
      </p>
      <p>Select Sign in to start a new authenticated session.</p>
      <button type="button" class="app-button app-button-primary" @click="handleSignIn">
        Sign in
      </button>
    </section>
  </main>
</template>
