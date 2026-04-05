<script setup lang="ts">
import TN3270Terminal from '@/components/TN3270Terminal.vue'
import { getBrowserAuthService } from '@/services/auth'
import '@/styles/terminal-view.css'

const authState = getBrowserAuthService().getState()
</script>

<template>
  <section class="terminal-page" aria-label="Mainframe terminal session">
    <header class="sr-only">
      <h1>TN 3270 Terminal Emulator</h1>
      <p>
        Authenticated for {{ authState.displayName }} with roles:
        {{ authState.roles.join(', ') }}
      </p>
    </header>
    <TN3270Terminal />
  </section>
</template>
