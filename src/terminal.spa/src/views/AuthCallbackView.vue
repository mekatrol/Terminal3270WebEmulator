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
  <section class="app-screen auth-view" aria-labelledby="auth-callback-title">
    <section
      class="auth-panel app-panel"
      :aria-busy="!errorMessage"
      aria-labelledby="auth-callback-title"
    >
      <p class="app-kicker">Authentication</p>
      <h1 id="auth-callback-title">Completing sign-in</h1>
      <p v-if="!errorMessage">
        The browser is finalizing the OpenID Connect authorization code with PKCE flow.
      </p>
      <p v-else class="app-message app-message-error" role="alert">{{ errorMessage }}</p>
    </section>
  </section>
</template>
