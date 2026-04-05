<script setup lang="ts">
import { useRoute } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'

const route = useRoute()
const authService = getBrowserAuthService()
const authState = authService.getState()

async function handleSignOut(): Promise<void> {
  const returnToPath =
    typeof route.query.returnTo === 'string' && route.query.returnTo.startsWith('/')
      ? route.query.returnTo
      : '/'

  await authService.signOut(returnToPath)
}
</script>

<template>
  <main class="unauthorized-page">
    <section class="unauthorized-panel" aria-labelledby="unauthorized-title">
      <h1 id="unauthorized-title">Access denied</h1>
      <p>
        The current identity is authenticated as {{ authState.displayName }}, but it does not hold
        the `Server.Admin` role required for this route.
      </p>
      <p>Sign out and authenticate with a different account if you need administrative access.</p>
      <button type="button" class="sign-out-button" @click="handleSignOut">Sign out</button>
    </section>
  </main>
</template>

<style scoped>
.unauthorized-page {
  display: grid;
  min-height: 100vh;
  place-items: center;
  padding: 2rem;
  background: linear-gradient(180deg, #f4f8f8 0%, #dde7e8 100%);
  color: #183136;
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

.unauthorized-panel {
  max-width: 42rem;
  border: 1px solid rgb(24 49 54 / 20%);
  border-radius: 1rem;
  padding: 2rem;
  background: rgb(255 255 255 / 90%);
  box-shadow: 0 1rem 2.5rem rgb(24 49 54 / 10%);
}

.sign-out-button {
  padding: 0.85rem 1.25rem;
  border: none;
  border-radius: 0.6rem;
  background: #1e5563;
  color: #fff;
  font: inherit;
  font-weight: 700;
  cursor: pointer;
}
</style>
