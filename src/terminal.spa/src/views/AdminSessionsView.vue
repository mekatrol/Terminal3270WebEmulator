<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import {
  clearAdminSessions,
  fetchAdminSessions,
  terminateAdminSessions,
  type AdminSession,
} from '@/services/adminSessions'

const sessions = ref<AdminSession[]>([])
const selectedSessionIds = ref<string[]>([])
const isLoading = ref(false)
const isSubmitting = ref(false)
const errorMessage = ref('')
const statusMessage = ref('')

const allSelectableSessionIds = computed(() =>
  sessions.value.map((session) => session.terminalSessionId),
)
const hasSessions = computed(() => sessions.value.length > 0)
const selectedCount = computed(() => selectedSessionIds.value.length)
const areAllSessionsSelected = computed(
  () =>
    allSelectableSessionIds.value.length > 0 &&
    selectedSessionIds.value.length === allSelectableSessionIds.value.length,
)

function formatDateTime(value: string | null): string {
  if (!value) {
    return 'Open'
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(new Date(value))
}

function isSessionSelected(sessionId: string): boolean {
  return selectedSessionIds.value.includes(sessionId)
}

function toggleSessionSelection(sessionId: string): void {
  if (isSessionSelected(sessionId)) {
    selectedSessionIds.value = selectedSessionIds.value.filter(
      (selectedId) => selectedId !== sessionId,
    )
    return
  }

  selectedSessionIds.value = [...selectedSessionIds.value, sessionId]
}

function toggleSelectAll(): void {
  selectedSessionIds.value = areAllSessionsSelected.value ? [] : [...allSelectableSessionIds.value]
}

function syncSelection(): void {
  const allowedSessionIds = new Set(allSelectableSessionIds.value)
  selectedSessionIds.value = selectedSessionIds.value.filter((sessionId) =>
    allowedSessionIds.has(sessionId),
  )
}

async function loadSessions(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    sessions.value = await fetchAdminSessions()
    syncSelection()
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Unable to load terminal sessions right now.'
  } finally {
    isLoading.value = false
  }
}

async function applyAction(action: 'terminate' | 'clear'): Promise<void> {
  if (selectedSessionIds.value.length === 0) {
    return
  }

  isSubmitting.value = true
  errorMessage.value = ''
  statusMessage.value = ''

  try {
    const response =
      action === 'terminate'
        ? await terminateAdminSessions(selectedSessionIds.value)
        : await clearAdminSessions(selectedSessionIds.value)

    statusMessage.value = response.message
    await loadSessions()
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'The selected session action could not be completed.'
  } finally {
    isSubmitting.value = false
  }
}

onMounted(async () => {
  await loadSessions()
})
</script>

<template>
  <main class="admin-sessions-page">
    <section class="admin-sessions-panel" aria-labelledby="admin-sessions-title">
      <header class="page-header">
        <div>
          <p class="eyebrow">Administration</p>
          <h1 id="admin-sessions-title">Terminal sessions</h1>
          <p class="intro">
            Review active and completed terminal sessions, terminate live connections, and clear
            inactive history entries from the in-memory store.
          </p>
        </div>
        <button
          type="button"
          class="refresh-button"
          :disabled="isLoading || isSubmitting"
          @click="loadSessions"
        >
          Refresh
        </button>
      </header>

      <p v-if="errorMessage" class="feedback feedback-error" role="alert">{{ errorMessage }}</p>
      <p v-else-if="statusMessage" class="feedback feedback-status" role="status">
        {{ statusMessage }}
      </p>

      <section class="actions" aria-labelledby="session-actions-title">
        <h2 id="session-actions-title">Bulk actions</h2>
        <div class="selection-controls">
          <label class="checkbox-label">
            <input
              type="checkbox"
              :checked="areAllSessionsSelected"
              :disabled="!hasSessions || isLoading || isSubmitting"
              @change="toggleSelectAll"
            />
            <span>Select all / deselect all</span>
          </label>
          <p>{{ selectedCount }} selected</p>
        </div>
        <div class="action-buttons">
          <button
            type="button"
            class="action-button action-button-danger"
            :disabled="selectedCount === 0 || isSubmitting || isLoading"
            @click="applyAction('terminate')"
          >
            Terminate selected active sessions
          </button>
          <button
            type="button"
            class="action-button"
            :disabled="selectedCount === 0 || isSubmitting || isLoading"
            @click="applyAction('clear')"
          >
            Clear selected entries
          </button>
        </div>
      </section>

      <div class="table-shell">
        <table>
          <caption class="sr-only">
            Terminal session administration table showing session lifecycle and identity details.
          </caption>
          <thead>
            <tr>
              <th scope="col">Select</th>
              <th scope="col">Status</th>
              <th scope="col">Started</th>
              <th scope="col">Closed</th>
              <th scope="col">User name</th>
              <th scope="col">User ID</th>
              <th scope="col">Session ID</th>
            </tr>
          </thead>
          <tbody v-if="hasSessions">
            <tr v-for="session in sessions" :key="session.terminalSessionId">
              <td>
                <label class="checkbox-label">
                  <input
                    type="checkbox"
                    :checked="isSessionSelected(session.terminalSessionId)"
                    :disabled="isSubmitting || isLoading"
                    @change="toggleSessionSelection(session.terminalSessionId)"
                  />
                  <span class="sr-only">Select session {{ session.terminalSessionId }}</span>
                </label>
              </td>
              <td>
                <span :class="session.isActive ? 'status-pill status-pill-active' : 'status-pill'">
                  {{ session.isActive ? 'Active' : 'Closed' }}
                </span>
              </td>
              <td>{{ formatDateTime(session.createdDateTimeUtc) }}</td>
              <td>{{ formatDateTime(session.closedDateTimeUtc) }}</td>
              <td>{{ session.userName }}</td>
              <td>{{ session.userId }}</td>
              <td class="session-id-cell">{{ session.terminalSessionId }}</td>
            </tr>
          </tbody>
          <tbody v-else>
            <tr>
              <td colspan="7" class="empty-row">
                {{
                  isLoading ? 'Loading sessions...' : 'No terminal sessions are currently recorded.'
                }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </main>
</template>

<style scoped>
.admin-sessions-page {
  min-height: 100vh;
  padding: 2rem;
  background:
    radial-gradient(circle at top right, rgb(216 235 222 / 80%), transparent 28%),
    linear-gradient(180deg, #f7f3e8 0%, #edf2ec 100%);
  color: #1c2c1f;
  font-family: Georgia, 'Times New Roman', serif;
}

.admin-sessions-panel {
  max-width: 90rem;
  margin: 0 auto;
  border: 1px solid rgb(28 44 31 / 12%);
  border-radius: 1.25rem;
  padding: 1.5rem;
  background: rgb(255 255 255 / 88%);
  box-shadow: 0 1.25rem 3rem rgb(28 44 31 / 12%);
}

.page-header {
  display: flex;
  gap: 1rem;
  justify-content: space-between;
  align-items: start;
}

.eyebrow {
  margin: 0 0 0.5rem;
  color: #55745d;
  font-size: 0.85rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
}

h1 {
  margin: 0;
  font-size: clamp(2rem, 4vw, 3.2rem);
}

.intro {
  max-width: 48rem;
  margin: 0.75rem 0 0;
  line-height: 1.5;
}

.refresh-button,
.action-button {
  border: 1px solid #2e4d35;
  border-radius: 999px;
  padding: 0.7rem 1.1rem;
  background: #faf8f1;
  color: #1c2c1f;
  font: inherit;
  cursor: pointer;
}

.action-button-danger {
  border-color: #8a3427;
  color: #8a3427;
}

.refresh-button:disabled,
.action-button:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.feedback {
  margin: 1rem 0 0;
  border-radius: 0.75rem;
  padding: 0.9rem 1rem;
}

.feedback-error {
  background: rgb(173 50 39 / 10%);
  color: #7a251b;
}

.feedback-status {
  background: rgb(54 103 64 / 10%);
  color: #24492b;
}

.actions {
  margin-top: 1.5rem;
  border-top: 1px solid rgb(28 44 31 / 10%);
  padding-top: 1.5rem;
}

.actions h2 {
  margin: 0;
  font-size: 1.2rem;
}

.selection-controls,
.action-buttons {
  display: flex;
  gap: 1rem;
  align-items: center;
  flex-wrap: wrap;
  margin-top: 0.9rem;
}

.checkbox-label {
  display: inline-flex;
  gap: 0.55rem;
  align-items: center;
}

.table-shell {
  margin-top: 1.5rem;
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
}

th,
td {
  border-bottom: 1px solid rgb(28 44 31 / 10%);
  padding: 0.85rem 0.75rem;
  text-align: left;
  vertical-align: top;
}

th {
  font-size: 0.88rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.status-pill {
  display: inline-block;
  border-radius: 999px;
  padding: 0.2rem 0.6rem;
  background: rgb(62 78 66 / 12%);
  font-size: 0.82rem;
  font-weight: 700;
}

.status-pill-active {
  background: rgb(46 105 60 / 14%);
  color: #1d5a2a;
}

.session-id-cell {
  font-family: 'Courier New', Courier, monospace;
  font-size: 0.88rem;
}

.empty-row {
  text-align: center;
  color: #55745d;
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

@media (max-width: 768px) {
  .admin-sessions-page {
    padding: 1rem;
  }

  .admin-sessions-panel {
    padding: 1rem;
  }

  .page-header {
    flex-direction: column;
  }
}
</style>
