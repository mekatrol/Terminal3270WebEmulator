<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'
import '@/styles/auth-views.css'

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
  <main class="app-screen auth-view">
    <section class="auth-panel app-panel">
      <p class="app-kicker">Authentication</p>
      <h1>Completing sign-in</h1>
      <p v-if="!errorMessage">
        The browser is finalizing the OpenID Connect authorization code flow.
      </p>
      <p v-else class="app-message app-message-error" role="alert">{{ errorMessage }}</p>
    </section>
  </main>
</template>
