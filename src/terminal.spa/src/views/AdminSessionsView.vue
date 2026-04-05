<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import {
  clearAdminSessions,
  fetchAdminSessions,
  terminateAdminSessions,
  type AdminSession,
} from '@/services/adminSessions'
import '@/styles/admin-sessions.css'

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
  <main class="app-screen admin-sessions-page">
    <section class="admin-sessions-panel app-panel" aria-labelledby="admin-sessions-title">
      <header class="page-header">
        <div>
          <p class="app-kicker">Administration</p>
          <h1 id="admin-sessions-title">Terminal sessions</h1>
          <p class="intro">
            Review active and completed terminal sessions, terminate live connections, and clear
            inactive history entries from the in-memory store.
          </p>
        </div>
        <button
          type="button"
          class="app-button"
          :disabled="isLoading || isSubmitting"
          @click="loadSessions"
        >
          Refresh
        </button>
      </header>

      <p v-if="errorMessage" class="app-message app-message-error" role="alert">
        {{ errorMessage }}
      </p>
      <p v-else-if="statusMessage" class="app-message app-message-status" role="status">
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
            class="app-button app-button-danger"
            :disabled="selectedCount === 0 || isSubmitting || isLoading"
            @click="applyAction('terminate')"
          >
            Terminate selected active sessions
          </button>
          <button
            type="button"
            class="app-button"
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
