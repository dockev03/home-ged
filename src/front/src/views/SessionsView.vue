<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError } from '../api/http'
import { listSessions, revokeSession, type SessionInfo } from '../api/sessions'
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
  <div class="sessions">
    <h1 class="h3">Mes sessions</h1>
    <p class="text-body-secondary mb-0">
      Appareils connectés à votre compte. Révoquez une session que vous ne reconnaissez pas ou un appareil perdu.
    </p>

    <div v-if="loadError" class="alert alert-danger d-flex align-items-center gap-2" role="alert">
      <span class="me-auto">{{ loadError }}</span>
      <button type="button" class="btn btn-sm btn-outline-danger" @click="load">Réessayer</button>
    </div>
    <div v-if="actionError" class="alert alert-danger alert-dismissible erreur-action" role="alert">
      {{ actionError }}
      <button type="button" class="btn-close" aria-label="Fermer" @click="actionError = null" />
    </div>

    <div v-if="sessions === null && !loadError" class="chargement">
      <span class="spinner-border" role="status"><span class="visually-hidden">Chargement…</span></span>
    </div>

    <ul v-if="sessions" class="liste">
      <li v-for="session in sorted" :key="session.id" class="card" :data-session="session.id">
        <div class="card-body">
          <div class="entete">
            <strong>{{ describeDevice(session.userAgent) }}</strong>
            <span v-if="session.current" class="badge rounded-pill text-bg-success">Cet appareil</span>
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
            <span class="question">{{ session.current ? 'Cet appareil sera déconnecté.' : 'Révoquer cette session ?' }}</span>
            <button type="button" class="btn btn-outline-secondary" @click="confirming = null">Annuler</button>
            <button type="button" class="btn btn-danger" :disabled="revoking === session.id" @click="revoke(session)">
              <span v-if="revoking === session.id" class="spinner-border spinner-border-sm me-1" aria-hidden="true" />
              Confirmer
            </button>
          </div>
          <div v-else class="actions">
            <button type="button" class="btn btn-outline-danger" @click="confirming = session.id">
              {{ session.current ? 'Révoquer (se déconnecter)' : 'Révoquer' }}
            </button>
          </div>
        </div>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.sessions {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 40rem;
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
  font-weight: normal;
  color: var(--bs-secondary-color);
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
</style>
