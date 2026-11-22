<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ApiError } from '../api/http'
import { isExhausted, listShares, revokeShare, shareUrl, type ShareLink } from '../api/shares'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import { copyText } from '../composables/clipboard'
import { formatDate, formatDateTime, formatRelative } from '../format/dates'

/** Mes liens (SHARE-04) : liens de partage actifs de l'utilisateur, révocables en un clic. */
const links = ref<ShareLink[] | null>(null)
const loadError = ref<string | null>(null)
const notice = ref<string | null>(null)
const actionError = ref<string | null>(null)
const revoking = ref<ShareLink | null>(null)
const busy = ref(false)

const errorMessage = (e: unknown) =>
  e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED. Vérifiez votre connexion.'

async function load() {
  loadError.value = null
  try {
    links.value = await listShares()
  } catch (e) {
    loadError.value = errorMessage(e)
  }
}

onMounted(load)

const documentLabel = (link: ShareLink) => link.titre ?? `Document n° ${link.document}`

function downloadsLabel(link: ShareLink) {
  const n = link.telechargements
  if (link.telechargementsMax === null) return `${n} téléchargement${n > 1 ? 's' : ''}`
  return `${n} / ${link.telechargementsMax} téléchargement${link.telechargementsMax > 1 ? 's' : ''}`
}

async function copy(link: ShareLink) {
  actionError.value = null
  notice.value = null
  if (await copyText(shareUrl(link.id))) notice.value = `Lien vers « ${documentLabel(link)} » copié.`
  else actionError.value = `Copie impossible. Lien : ${shareUrl(link.id)}`
}

async function revoke() {
  const link = revoking.value
  if (!link) return
  busy.value = true
  actionError.value = null
  notice.value = null
  try {
    await revokeShare(link.id)
    notice.value = `Lien vers « ${documentLabel(link)} » révoqué : il ne permet plus d'accéder au document.`
  } catch (e) {
    // Déjà révoqué ou expiré entre-temps : la liste à jour le montrera.
    if (!(e instanceof ApiError && e.status === 404)) actionError.value = errorMessage(e)
  } finally {
    busy.value = false
    revoking.value = null
    await load()
  }
}
</script>

<template>
  <div class="liens">
    <h1 class="h3 mb-0">Mes liens</h1>
    <p class="text-body-secondary mb-0">
      Liens de partage protégés par mot de passe que vous avez créés avec le bouton « Envoyer » d'un document.
      Un lien révoqué ou expiré ne donne plus accès au document.
    </p>

    <div v-if="notice" class="alert alert-success alert-dismissible mb-0" role="status">
      {{ notice }}
      <button type="button" class="btn-close" aria-label="Fermer" @click="notice = null" />
    </div>
    <div v-if="actionError" class="alert alert-danger mb-0 texte" role="alert">{{ actionError }}</div>
    <div v-if="loadError" class="alert alert-danger d-flex align-items-center gap-2 mb-0" role="alert">
      <span class="me-auto">{{ loadError }}</span>
      <button type="button" class="btn btn-sm btn-outline-danger" @click="load">Réessayer</button>
    </div>

    <div v-if="links === null && !loadError" class="chargement">
      <span class="spinner-border" role="status"><span class="visually-hidden">Chargement…</span></span>
    </div>

    <template v-if="links">
      <p v-if="links.length === 0" class="vide">Aucun lien actif.</p>
      <ul v-else class="list-group">
        <li v-for="link in links" :key="link.id" class="list-group-item element" :data-lien="link.id">
          <div class="infos">
            <RouterLink
              v-if="link.titre !== null"
              class="fw-semibold texte"
              :to="{ name: 'document', params: { id: link.document } }"
            >
              {{ link.titre }}
            </RouterLink>
            <span v-else class="fw-semibold texte">
              {{ documentLabel(link) }} <span class="badge text-bg-secondary fw-normal">inaccessible</span>
            </span>
            <div class="small text-body-secondary">
              Créé le {{ formatDate(link.creeLe) }} ·
              <span :title="formatDateTime(link.expireLe)">expire {{ formatRelative(link.expireLe) }}</span> ·
              {{ downloadsLabel(link) }}
              <span v-if="isExhausted(link)" class="badge text-bg-warning fw-normal">épuisé</span>
            </div>
          </div>
          <div class="actions">
            <button type="button" class="btn btn-sm btn-outline-primary" @click="copy(link)">Copier le lien</button>
            <button type="button" class="btn btn-sm btn-outline-danger" :disabled="busy" @click="revoking = link">Révoquer</button>
          </div>
        </li>
      </ul>
    </template>

    <ConfirmDialog
      :open="revoking !== null"
      title="Révoquer ce lien ?"
      confirm-label="Révoquer"
      danger
      :busy="busy"
      @confirm="revoke"
      @cancel="revoking = null"
    >
      <p class="mb-0">
        Le lien vers « {{ revoking ? documentLabel(revoking) : '' }} » cessera immédiatement de fonctionner. Le document
        lui-même n'est pas modifié.
      </p>
    </ConfirmDialog>
  </div>
</template>

<style scoped>
.liens {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 60rem;
}

.element {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
}

.infos {
  flex: 1 1 16rem;
  min-width: 0;
}

.texte {
  overflow-wrap: anywhere;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

/* Cibles tactiles ≥ 44 px sur mobile (§4). */
@media (max-width: 767.98px) {
  .actions .btn {
    min-height: 44px;
  }
}

.vide {
  padding: 2rem;
  text-align: center;
  color: var(--bs-secondary-color);
}

.chargement {
  display: flex;
  justify-content: center;
  padding: 3rem;
}
</style>
