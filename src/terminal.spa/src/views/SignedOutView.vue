<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'

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
  <main class="signed-out-page">
    <section class="signed-out-panel" aria-labelledby="signed-out-title">
      <h1 id="signed-out-title">Signed out</h1>
      <p>
        This browser tab was signed out because the shared session ended in another tab or window.
      </p>
      <p>Select Sign in to start a new authenticated session.</p>
      <button type="button" class="sign-in-button" @click="handleSignIn">Sign in</button>
    </section>
  </main>
</template>

<style scoped>
.signed-out-page {
  display: grid;
  min-height: 100vh;
  place-items: center;
  padding: 2rem;
  background: linear-gradient(180deg, #edf5f7 0%, #dce8ec 100%);
  color: #16303a;
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

.signed-out-panel {
  max-width: 40rem;
  padding: 2rem;
  border: 1px solid rgb(22 48 58 / 16%);
  border-radius: 1rem;
  background: rgb(255 255 255 / 92%);
  box-shadow: 0 1rem 2.5rem rgb(22 48 58 / 10%);
}

.sign-in-button {
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
