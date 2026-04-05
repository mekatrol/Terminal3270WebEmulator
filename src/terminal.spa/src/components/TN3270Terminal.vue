<template>
  <main class="terminal-page">
    <section
      ref="terminalRef"
      class="terminal-shell"
      :tabindex="showSessionLauncher ? -1 : 0"
      role="application"
      aria-label="TN 3270 emulator"
      :aria-describedby="'terminal-instructions terminal-status'"
      @mousedown.prevent="focusTerminal"
      @keydown="handleKeydown"
    >
      <h2 class="sr-only">{{ snapshot.title }}</h2>
      <p id="terminal-instructions" class="sr-only">
        Keyboard-driven 24 by 80 TN 3270 terminal. Use Tab to move across input fields, Enter to
        submit, function keys F1 through F12 for program function keys, Shift plus F1 through F12
        for PF13 through PF24, Ctrl+C for PA1, Ctrl+L for Clear, and Ctrl+S for SysReq.
      </p>
      <p id="terminal-status" class="sr-only" aria-live="polite">
        {{ accessibleSummary }}
      </p>

      <div class="terminal-frame">
        <section
          v-if="showSessionNotice"
          class="session-notice-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="session-notice-title"
          aria-describedby="session-notice-message"
          @mousedown.stop
          @click.stop
        >
          <div class="session-notice-panel">
            <h3 id="session-notice-title">{{ sessionNoticeTitle }}</h3>
            <p id="session-notice-message" class="session-notice-message">
              {{ sessionNoticeMessage }}
            </p>
            <div class="session-notice-actions">
              <button class="session-notice-button" type="button" @click="dismissSessionNotice">
                Close
              </button>
            </div>
          </div>
        </section>
        <section
          v-if="showSessionLauncher"
          class="session-launcher"
          aria-labelledby="session-launcher-title"
        >
          <form
            class="session-launcher-form"
            :aria-describedby="canStartSession ? undefined : 'session-launcher-copy'"
            @submit.prevent="handleStartSessionSubmit"
          >
            <p class="session-launcher-eyebrow">HTTP SESSION READY</p>
            <h3 id="session-launcher-title">{{ sessionLauncherTitle }}</h3>
            <p id="session-launcher-copy" class="session-launcher-copy">
              {{ sessionLauncherMessage }}
            </p>
            <button
              v-if="canStartSession"
              ref="sessionLauncherButtonRef"
              class="session-launcher-button"
              type="submit"
              @click="handleStartSessionClick"
            >
              Start session
            </button>
          </form>
        </section>
        <div v-else class="terminal-grid" data-testid="TN-3270-terminal" aria-hidden="true">
          <div
            v-for="(row, rowIndex) in flattenedRows"
            :key="`row-${rowIndex}`"
            class="terminal-row"
            :style="{ gridTemplateColumns: `repeat(${snapshot.cols}, 1ch)` }"
          >
            <span
              v-for="cell in row"
              :key="cell.key"
              class="terminal-cell"
              :class="cell.classes"
              :style="cell.style"
            >
              {{ cell.char }}
            </span>
          </div>
        </div>
      </div>
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'

import { useTN3270Session } from '@/composables/useTN3270Session'
import type { TN3270Color, TerminalCell } from '@/types/TN3270'
import '@/styles/tn3270-terminal.css'

const terminalRef = ref<HTMLElement | null>(null)
const sessionLauncherButtonRef = ref<HTMLButtonElement | null>(null)
const {
  accessibleSummary,
  canStartSession,
  dismissSessionNotice,
  handleKeydown,
  sessionLauncherTitle,
  sessionNoticeMessage,
  sessionNoticeTitle,
  sessionLauncherMessage,
  showSessionNotice,
  showSessionLauncher,
  snapshot,
  startSession,
} = useTN3270Session()

const colorClassMap: Record<TN3270Color, string> = {
  neutral: 'var(--tn3270-fg-neutral)',
  blue: 'var(--tn3270-fg-blue)',
  red: 'var(--tn3270-fg-red)',
  pink: 'var(--tn3270-fg-pink)',
  green: 'var(--tn3270-fg-green)',
  turquoise: 'var(--tn3270-fg-turquoise)',
  yellow: 'var(--tn3270-fg-yellow)',
  white: 'var(--tn3270-fg-white)',
  black: 'var(--tn3270-fg-black)',
  deepBlue: 'var(--tn3270-fg-deep-blue)',
  orange: 'var(--tn3270-fg-orange)',
  purple: 'var(--tn3270-fg-purple)',
  paleGreen: 'var(--tn3270-fg-pale-green)',
  paleTurquoise: 'var(--tn3270-fg-pale-turquoise)',
  grey: 'var(--tn3270-fg-grey)',
}

const backgroundColorMap: Record<TN3270Color, string> = {
  neutral: 'transparent',
  blue: 'var(--tn3270-bg-blue)',
  red: 'var(--tn3270-bg-red)',
  pink: 'var(--tn3270-bg-pink)',
  green: 'var(--tn3270-bg-green)',
  turquoise: 'var(--tn3270-bg-turquoise)',
  yellow: 'var(--tn3270-bg-yellow)',
  white: 'var(--tn3270-bg-white)',
  black: 'var(--tn3270-bg-black)',
  deepBlue: 'var(--tn3270-bg-deep-blue)',
  orange: 'var(--tn3270-bg-orange)',
  purple: 'var(--tn3270-bg-purple)',
  paleGreen: 'var(--tn3270-bg-pale-green)',
  paleTurquoise: 'var(--tn3270-bg-pale-turquoise)',
  grey: 'var(--tn3270-bg-grey)',
}

const flattenedRows = computed(() =>
  snapshot.value.cells.map((row: TerminalCell[], rowIndex: number) =>
    row.map((cell: TerminalCell, colIndex: number) => {
      const isCursor =
        snapshot.value.cursor?.row === rowIndex && snapshot.value.cursor?.col === colIndex

      return {
        ...cell,
        key: `${rowIndex}-${colIndex}`,
        classes: [
          cell.intensified ? 'cell-intensified' : '',
          isCursor ? 'cell-cursor' : 'cell-host-rendered',
        ].filter(Boolean),
        style: resolveCellStyle(cell, isCursor),
      }
    }),
  ),
)

function resolveCellStyle(
  cell: TerminalCell,
  isCursor: boolean,
): { backgroundColor: string; color: string } {
  if (isCursor) {
    return {
      backgroundColor: 'var(--tn3270-cursor-bg)',
      color: 'var(--tn3270-cursor-fg)',
    }
  }

  return {
    color: colorClassMap[cell.color],
    backgroundColor:
      cell.backgroundColor === 'neutral'
        ? cell.protected
          ? 'transparent'
          : 'var(--tn3270-bg-input-neutral)'
        : shouldRenderHostBackground(cell)
          ? backgroundColorMap[cell.backgroundColor]
          : 'transparent',
  }
}

function shouldRenderHostBackground(cell: TerminalCell): boolean {
  if (!cell.protected) {
    return true
  }

  return cell.char.trim().length > 0
}

function focusTerminal(): void {
  if (showSessionLauncher.value) {
    return
  }

  terminalRef.value?.focus()
}

async function focusSessionLauncherButton(): Promise<void> {
  if (!showSessionLauncher.value) {
    return
  }

  await nextTick()
  sessionLauncherButtonRef.value?.focus()
}

async function focusActiveSurface(): Promise<void> {
  await nextTick()

  if (showSessionLauncher.value) {
    sessionLauncherButtonRef.value?.focus()
    return
  }

  terminalRef.value?.focus()
}

async function handleStartSessionSubmit(): Promise<void> {
  if (!canStartSession.value) {
    return
  }

  await startSession()
}

function handleStartSessionClick(event: MouseEvent): void {
  // Native browser form submission already handles Enter on the launcher. The explicit click
  // path keeps pointer activation deterministic in component tests and in any environment where
  // synthetic clicks do not automatically dispatch a submit event.
  event.preventDefault()
  if (!canStartSession.value) {
    return
  }

  void startSession()
}

watch(showSessionLauncher, (isVisible) => {
  if (isVisible) {
    void focusSessionLauncherButton()
    return
  }

  void focusActiveSurface()
})

onMounted(() => {
  void focusActiveSurface()
})
</script>
