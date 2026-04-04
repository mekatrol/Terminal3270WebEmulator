<template>
  <main class="terminal-page">
    <section
      ref="terminalRef"
      class="terminal-shell"
      tabindex="0"
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
        <div class="terminal-grid" data-testid="TN-3270-terminal" aria-hidden="true">
          <div
            v-for="(row, rowIndex) in flattenedRows"
            :key="`row-${rowIndex}`"
            class="terminal-row"
            :style="{ gridTemplateColumns: `repeat(${snapshot.cols}, 1ch)` }"
          >
            <span v-for="cell in row" :key="cell.key" class="terminal-cell" :class="cell.classes">
              {{ cell.char }}
            </span>
          </div>
        </div>
      </div>
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'

import { useTN3270Session } from '@/composables/useTN3270Session'
import type { TN3270Color, TerminalCell } from '@/types/TN3270'

const terminalRef = ref<HTMLElement | null>(null)
const { accessibleSummary, handleKeydown, snapshot } = useTN3270Session()

const colorClassMap: Record<TN3270Color, string> = {
  neutral: 'cell-neutral',
  blue: 'cell-blue',
  red: 'cell-red',
  pink: 'cell-pink',
  green: 'cell-green',
  turquoise: 'cell-turquoise',
  yellow: 'cell-yellow',
  white: 'cell-white',
  black: 'cell-black',
  deepBlue: 'cell-deep-blue',
  orange: 'cell-orange',
  purple: 'cell-purple',
  paleGreen: 'cell-pale-green',
  paleTurquoise: 'cell-pale-turquoise',
  grey: 'cell-grey',
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
          colorClassMap[cell.color],
          cell.protected ? 'cell-protected' : 'cell-input',
          cell.intensified ? 'cell-intensified' : '',
          isCursor ? 'cell-cursor' : '',
        ].filter(Boolean),
      }
    }),
  ),
)

function focusTerminal(): void {
  terminalRef.value?.focus()
}
</script>

<style scoped>
.terminal-page {
  min-height: 100vh;
}

.terminal-shell {
  display: grid;
  min-height: 100vh;
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
  display: grid;
  min-height: 0;
  padding: min(2vw, 1.5rem);
  background:
    linear-gradient(180deg, rgb(40 72 80 / 20%), transparent 18%),
    linear-gradient(135deg, rgb(12 56 48 / 20%), transparent 45%);
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

.cell-input {
  background: rgb(140 224 180 / 0.06);
}

.cell-protected {
  background: transparent;
}

.cell-intensified {
  font-weight: 700;
}

.cell-cursor {
  background: #8ce0b4;
  color: #071214;
}

.cell-neutral {
  color: #8ce0b4;
}

.cell-blue {
  color: #73a8ff;
}

.cell-red {
  color: #ff7a6b;
}

.cell-pink {
  color: #ff9ad0;
}

.cell-green {
  color: #90f7a6;
}

.cell-turquoise {
  color: #6bf5eb;
}

.cell-yellow {
  color: #ffeb75;
}

.cell-white {
  color: #f4f7fb;
}

.cell-black {
  color: #0c1014;
}

.cell-deep-blue {
  color: #1f5dff;
}

.cell-orange {
  color: #ffb454;
}

.cell-purple {
  color: #c79cff;
}

.cell-pale-green {
  color: #b9ffb4;
}

.cell-pale-turquoise {
  color: #b5fff2;
}

.cell-grey {
  color: #b6c0c8;
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
