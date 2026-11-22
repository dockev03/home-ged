<script setup lang="ts">
import { NAlert, NButton, NCard, NSpin, NTag, NText } from 'naive-ui'
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError } from '../api/http'
import { listSessions, revokeSession, type SessionInfo } from '../api/sessions'
import AppHeader from '../components/AppHeader.vue'
import { formatDate, formatDateTime, formatRelative } from '../format/dates'
import { describeDevice } from '../format/device'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const router = useRouter()

const sessions = ref<SessionInfo[] | null>(null)
const loadError = ref<string | null>(null)
const actionError = ref<string | null>(null)
/** Session dont la révocation attend confirmation (confirmation dans la carte, sans fenêtre). */
const confirming = ref<string | null>(null)
const revoking = ref<string | null>(null)

/** Cet appareil d'abord, puis les plus récemment utilisées. */
const sorted = computed(() =>
  [...(sessions.value ?? [])].sort(
    (a, b) => Number(b.current) - Number(a.current) || Date.parse(b.lastSeenAt) - Date.parse(a.lastSeenAt),
  ),
)

const errorMessage = (e: unknown) =>
  e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED. Vérifiez votre connexion.'

async function load() {
  loadError.value = null
  try {
    sessions.value = await listSessions()
  } catch (e) {
    loadError.value = errorMessage(e)
  }
}

async function revoke(session: SessionInfo) {
  actionError.value = null
  revoking.value = session.id
  try {
    await revokeSession(session.id)
  } catch (e) {
    // Déjà révoquée ou expirée ailleurs : le résultat voulu est atteint.
    if (!(e instanceof ApiError && e.code === 'session_introuvable')) {
      actionError.value = errorMessage(e)
      return
    }
  } finally {
    revoking.value = null
    confirming.value = null
  }
  if (session.current) {
    // Le BFF a supprimé le cookie : c'est une déconnexion.
    auth.clear()
    await router.replace({ name: 'connexion' })
    return
  }
  sessions.value = sessions.value?.filter((s) => s.id !== session.id) ?? null
}

onMounted(load)
</script>

<template>
  <AppHeader retour />
  <main class="sessions">
    <h1>Mes sessions</h1>
    <NText depth="3">
      Appareils connectés à votre compte. Révoquez une session que vous ne reconnaissez pas ou un appareil perdu.
    </NText>

    <NAlert v-if="loadError" type="error" :show-icon="false">
      {{ loadError }}
      <NButton text type="primary" class="reessayer" @click="load">Réessayer</NButton>
    </NAlert>
    <NAlert v-if="actionError" type="error" closable @close="actionError = null">{{ actionError }}</NAlert>

    <NSpin v-if="sessions === null && !loadError" class="chargement" />

    <ul v-if="sessions" class="liste">
      <li v-for="session in sorted" :key="session.id">
        <NCard size="small" :data-session="session.id">
          <div class="entete">
            <strong>{{ describeDevice(session.userAgent) }}</strong>
            <NTag v-if="session.current" type="success" size="small" round>Cet appareil</NTag>
          </div>
          <dl class="details">
            <dt>Dernière activité</dt>
            <dd :title="formatDateTime(session.lastSeenAt)">{{ formatRelative(session.lastSeenAt) }}</dd>
            <dt>Adresse IP</dt>
            <dd>{{ session.ip ?? 'inconnue' }}</dd>
            <dt>Connectée le</dt>
            <dd>{{ formatDate(session.createdAt) }}</dd>
            <dt>Expire le</dt>
            <dd>{{ formatDate(session.expiresAt) }}</dd>
          </dl>

          <div v-if="confirming === session.id" class="actions" role="group" aria-label="Confirmer la révocation">
            <NText class="question">{{ session.current ? 'Cet appareil sera déconnecté.' : 'Révoquer cette session ?' }}</NText>
            <NButton size="large" @click="confirming = null">Annuler</NButton>
            <NButton size="large" type="error" :loading="revoking === session.id" @click="revoke(session)">
              Confirmer
            </NButton>
          </div>
          <div v-else class="actions">
            <NButton size="large" secondary type="error" @click="confirming = session.id">
              {{ session.current ? 'Révoquer (se déconnecter)' : 'Révoquer' }}
            </NButton>
          </div>
        </NCard>
      </li>
    </ul>
  </main>
</template>

<style scoped>
.sessions {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 40rem;
  margin: 0 auto;
  padding: 0 1rem 2rem;
}

h1 {
  margin: 0.5rem 0 0;
}

.liste {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.entete {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.details {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 1rem;
  margin: 0.75rem 0;
  font-size: 0.9rem;
}

.details dt {
  opacity: 0.7;
}

.details dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 0.5rem;
}

.question {
  flex-basis: 100%;
  text-align: right;
}

.chargement {
  align-self: center;
  padding: 2rem;
}

.reessayer {
  margin-left: 0.5rem;
}
</style>
