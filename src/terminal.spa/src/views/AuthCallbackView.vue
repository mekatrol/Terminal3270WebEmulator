<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'

const router = useRouter()
const errorMessage = ref('')

onMounted(async () => {
  try {
    const returnToPath = await getBrowserAuthService().completeSignInCallback()
    await router.replace(returnToPath)
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Authentication could not be completed.'
  }
})
</script>

<template>
  <main class="auth-callback-page">
    <section class="auth-callback-panel">
      <h1>Completing sign-in</h1>
      <p v-if="!errorMessage">The browser is finalizing the OpenID Connect authorization code flow.</p>
      <p v-else role="alert">{{ errorMessage }}</p>
    </section>
  </main>
</template>

<style scoped>
.auth-callback-page {
  display: grid;
  min-height: 100vh;
  place-items: center;
  padding: 2rem;
  background: linear-gradient(180deg, #edf5f7 0%, #dce8ec 100%);
  color: #16303a;
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

.auth-callback-panel {
  max-width: 40rem;
  padding: 2rem;
  border: 1px solid rgb(22 48 58 / 16%);
  border-radius: 1rem;
  background: rgb(255 255 255 / 92%);
  box-shadow: 0 1rem 2.5rem rgb(22 48 58 / 10%);
}
</style>
