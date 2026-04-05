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
          <form class="session-launcher-form" @submit.prevent="handleStartSessionSubmit">
            <p class="session-launcher-eyebrow">HTTP SESSION READY</p>
            <h3 id="session-launcher-title">Start a new terminal session</h3>
            <p class="session-launcher-copy">{{ sessionLauncherMessage }}</p>
            <button
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

const terminalRef = ref<HTMLElement | null>(null)
const sessionLauncherButtonRef = ref<HTMLButtonElement | null>(null)
const {
  accessibleSummary,
  dismissSessionNotice,
  handleKeydown,
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
  await startSession()
}

function handleStartSessionClick(event: MouseEvent): void {
  // Native browser form submission already handles Enter on the launcher. The explicit click
  // path keeps pointer activation deterministic in component tests and in any environment where
  // synthetic clicks do not automatically dispatch a submit event.
  event.preventDefault()
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

<style scoped>
.terminal-page {
  min-height: 100%;
}

.terminal-shell {
  --tn3270-fg-neutral: #8ce0b4;
  --tn3270-fg-blue: #73a8ff;
  --tn3270-fg-red: #ff7a6b;
  --tn3270-fg-pink: #ff9ad0;
  --tn3270-fg-green: #90f7a6;
  --tn3270-fg-turquoise: #6bf5eb;
  --tn3270-fg-yellow: #ffeb75;
  --tn3270-fg-white: #f4f7fb;
  --tn3270-fg-black: #0c1014;
  --tn3270-fg-deep-blue: #1f5dff;
  --tn3270-fg-orange: #ffb454;
  --tn3270-fg-purple: #c79cff;
  --tn3270-fg-pale-green: #b9ffb4;
  --tn3270-fg-pale-turquoise: #b5fff2;
  --tn3270-fg-grey: #b6c0c8;
  --tn3270-bg-input-neutral: rgb(140 224 180 / 0.06);
  --tn3270-bg-blue: #1b3156;
  --tn3270-bg-red: #5f1d1b;
  --tn3270-bg-pink: #5d2947;
  --tn3270-bg-green: #1f4a2a;
  --tn3270-bg-turquoise: #154c49;
  --tn3270-bg-yellow: #ffeb75;
  --tn3270-bg-white: #e7edf4;
  --tn3270-bg-black: #0c1014;
  --tn3270-bg-deep-blue: #16357c;
  --tn3270-bg-orange: #664114;
  --tn3270-bg-purple: #493263;
  --tn3270-bg-pale-green: #c4f3bf;
  --tn3270-bg-pale-turquoise: #b8efe8;
  --tn3270-bg-grey: #56616d;
  --tn3270-cursor-bg: #8ce0b4;
  --tn3270-cursor-fg: #071214;
  display: grid;
  min-height: 100%;
  width: 100%;
  grid-template-rows: auto 1fr;
  overflow: hidden;
  background:
    radial-gradient(circle at top, rgb(22 43 53 / 50%), transparent 40%),
    linear-gradient(180deg, #061114 0%, #020608 100%);
  color: #8ce0b4;
  font-family: 'TN Plex Mono', 'Courier New', monospace;
  outline: none;
}

.terminal-subtitle {
  display: block;
  margin-top: 0.25rem;
  color: rgb(223 249 240 / 72%);
  font-size: 0.88rem;
}

.connection-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0.7rem;
  border: 1px solid rgb(116 188 167 / 30%);
  background: rgb(255 255 255 / 4%);
  text-transform: uppercase;
  font-size: 0.82rem;
}

.dot {
  width: 0.7rem;
  height: 0.7rem;
  border-radius: 999px;
}

.dot-connecting {
  background: #ffeb75;
}

.dot-connected {
  background: #90f7a6;
}

.dot-disconnected {
  background: #ff7a6b;
}

.terminal-frame {
  position: relative;
  display: grid;
  min-height: 0;
  padding: min(2vw, 1.5rem);
  background:
    linear-gradient(180deg, rgb(40 72 80 / 20%), transparent 18%),
    linear-gradient(135deg, rgb(12 56 48 / 20%), transparent 45%);
}

.session-notice-overlay {
  position: absolute;
  inset: min(2vw, 1.5rem);
  z-index: 2;
  display: grid;
  place-items: center;
  padding: min(4vw, 2rem);
  background: rgb(2 8 11 / 74%);
  backdrop-filter: blur(4px);
}

.session-notice-panel {
  display: grid;
  gap: 1rem;
  width: min(40rem, 100%);
  padding: min(4vw, 2rem);
  border: 1px solid rgb(255 122 107 / 35%);
  box-shadow:
    inset 0 0 0 1px rgb(255 255 255 / 6%),
    0 1.4rem 4rem rgb(0 0 0 / 55%);
  background: linear-gradient(180deg, rgb(77 18 18 / 92%), rgb(22 8 8 / 96%)), #160808;
}

.session-notice-panel h3 {
  margin: 0;
  color: #f4f7fb;
  font-size: clamp(1.2rem, 2vw, 1.6rem);
}

.session-notice-message {
  margin: 0;
  color: #f7d7d1;
  white-space: pre-wrap;
  line-height: 1.6;
  user-select: text;
  cursor: text;
}

.session-notice-actions {
  display: flex;
  justify-content: flex-end;
}

.session-notice-button {
  padding: 0.8rem 1.2rem;
  border: 1px solid rgb(255 122 107 / 45%);
  background: linear-gradient(135deg, #3a1414 0%, #5b1f1f 100%);
  color: #f4f7fb;
  font: inherit;
  letter-spacing: 0.04em;
  cursor: pointer;
}

.session-notice-button:hover,
.session-notice-button:focus-visible {
  border-color: rgb(255 122 107 / 85%);
  outline: none;
  box-shadow: 0 0 0 3px rgb(255 122 107 / 16%);
}

.terminal-grid {
  display: grid;
  align-content: center;
  justify-content: center;
  min-height: 100%;
  padding: min(2vw, 1.5rem);
  border: 1px solid rgb(116 188 167 / 30%);
  box-shadow:
    inset 0 0 0 1px rgb(180 248 219 / 8%),
    0 1.4rem 4rem rgb(0 0 0 / 45%);
  background:
    linear-gradient(180deg, rgb(0 0 0 / 22%), rgb(0 0 0 / 38%)),
    repeating-linear-gradient(
      180deg,
      rgb(255 255 255 / 0.02) 0,
      rgb(255 255 255 / 0.02) 2px,
      transparent 2px,
      transparent 4px
    ),
    #081315;
  user-select: none;
}

.session-launcher {
  min-height: 100%;
  padding: min(5vw, 3rem);
  border: 1px solid rgb(116 188 167 / 30%);
  box-shadow:
    inset 0 0 0 1px rgb(180 248 219 / 8%),
    0 1.4rem 4rem rgb(0 0 0 / 45%);
  background:
    linear-gradient(180deg, rgb(0 0 0 / 18%), rgb(0 0 0 / 42%)),
    radial-gradient(circle at top right, rgb(107 245 235 / 10%), transparent 32%), #081315;
}

.session-launcher-form {
  display: grid;
  align-content: center;
  justify-items: start;
  gap: 1rem;
  min-height: 100%;
}

.session-launcher-eyebrow {
  margin: 0;
  color: #6bf5eb;
  letter-spacing: 0.18em;
  font-size: 0.78rem;
  text-transform: uppercase;
}

.session-launcher h3 {
  margin: 0;
  color: #f4f7fb;
  font-size: clamp(1.6rem, 3vw, 2.5rem);
}

.session-launcher-copy {
  max-width: 32rem;
  margin: 0;
  color: rgb(223 249 240 / 80%);
  line-height: 1.6;
}

.session-launcher-button {
  padding: 0.9rem 1.3rem;
  border: 1px solid rgb(144 247 166 / 45%);
  background: linear-gradient(135deg, #123228 0%, #1b5742 100%);
  color: #f4f7fb;
  font: inherit;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  cursor: pointer;
}

.session-launcher-button:hover,
.session-launcher-button:focus-visible {
  border-color: rgb(144 247 166 / 85%);
  outline: none;
  box-shadow: 0 0 0 3px rgb(144 247 166 / 15%);
}

.terminal-row {
  display: grid;
  line-height: 1.1;
}

.terminal-cell {
  display: inline-grid;
  width: 1ch;
  min-width: 1ch;
  height: 1.2em;
  place-items: center;
  white-space: pre;
  text-transform: uppercase;
}

.cell-intensified {
  font-weight: 700;
}

.cell-host-rendered,
.cell-cursor {
  transition:
    background-color 120ms ease-out,
    color 120ms ease-out;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

@media (max-width: 1100px) {
  .terminal-grid {
    overflow: auto;
    align-content: start;
    justify-content: start;
  }
}
</style>
