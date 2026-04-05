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
  <section class="app-screen auth-view" aria-labelledby="signed-out-title">
    <section class="auth-panel app-panel" aria-labelledby="signed-out-title">
      <p class="app-kicker">Session state</p>
      <h1 id="signed-out-title">Signed out</h1>
      <p>No authenticated browser session is currently available for this route.</p>
      <p>
        Sign in to continue to the terminal route. If another tab ended the shared session, signing
        in here will establish a new one.
      </p>
      <button type="button" class="app-button app-button-primary" @click="handleSignIn">
        Sign in
      </button>
    </section>
  </section>
</template>
