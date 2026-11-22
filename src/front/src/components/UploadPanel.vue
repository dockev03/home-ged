<script setup lang="ts">
import { computed, ref } from 'vue'
import { useUploadsStore, type Upload, type UploadStatus } from '../stores/uploads'

/**
 * Suivi des envois (PWA-07) : avancement de chaque fichier, puis traitement par Paperless (OCR,
 * classement) jusqu'au document créé. Hors ligne, les fichiers attendent le réseau sur l'appareil
 * (PWA-10). En bas à droite sur PC, en tête du contenu sur mobile.
 */
defineProps<{ floating?: boolean }>()

const uploads = useUploadsStore()
const collapsed = ref(false)

const LABELS: Record<UploadStatus, string> = {
  attente: 'En attente',
  reseau: 'En attente du réseau',
  envoi: 'Envoi',
  traitement: 'Traitement par Paperless…',
  termine: 'Ajouté',
  echec: 'Échec',
  annule: 'Annulé',
}

const running = computed(
  () => uploads.items.filter((u) => ['attente', 'reseau', 'envoi', 'traitement'].includes(u.status)).length,
)
/** Envois gardés sur l'appareil en attendant le réseau (PWA-10). */
const offline = computed(() => uploads.items.filter((u) => u.status === 'reseau').length)
const plural = (n: number) => (n > 1 ? 's' : '')
const summary = computed(() =>
  offline.value === running.value && offline.value
    ? `${offline.value} envoi${plural(offline.value)} en attente du réseau`
    : running.value
      ? `${running.value} envoi${plural(running.value)} en cours`
      : `${uploads.items.length} envoi${plural(uploads.items.length)} terminé${plural(uploads.items.length)}`,
)

function size(bytes: number) {
  if (bytes === 0) return '0 Ko'
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} Ko`
  return `${(bytes / 1024 / 1024).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mo`
}

const statusText = (u: Upload) =>
  u.status === 'envoi' ? `Envoi ${Math.round(u.progress * 100)} %` : u.message && u.status !== 'termine' ? u.message : LABELS[u.status]
</script>

<template>
  <section v-if="uploads.items.length" class="card envois" :class="{ flottant: floating }" aria-label="Envois de documents">
    <div class="card-header d-flex align-items-center gap-2">
      <span class="fw-semibold me-auto" role="status">{{ summary }}</span>
      <button
        type="button"
        class="btn btn-sm btn-link"
        :aria-expanded="!collapsed"
        aria-controls="liste-envois"
        @click="collapsed = !collapsed"
      >
        {{ collapsed ? 'Afficher' : 'Réduire' }}
      </button>
      <button
        v-if="running === 0"
        type="button"
        class="btn-close"
        aria-label="Fermer le suivi des envois"
        @click="uploads.clearFinished()"
      />
    </div>
    <ul v-show="!collapsed" id="liste-envois" class="list-group list-group-flush liste">
      <li v-for="u in uploads.items" :key="u.id" class="list-group-item" :data-upload="u.status">
        <div class="d-flex align-items-center gap-2">
          <span class="nom" :title="u.name">{{ u.name }}</span>
          <small class="text-body-secondary text-nowrap">{{ size(u.size) }}</small>
          <button
            v-if="['attente', 'reseau', 'envoi'].includes(u.status)"
            type="button"
            class="btn btn-sm btn-link text-danger p-0"
            :aria-label="`Annuler l'envoi de ${u.name}`"
            @click="uploads.cancel(u.id)"
          >
            Annuler
          </button>
          <RouterLink
            v-else-if="u.status === 'termine' && u.documentId"
            class="btn btn-sm btn-link p-0"
            :to="{ name: 'document', params: { id: u.documentId } }"
          >
            Ouvrir
          </RouterLink>
        </div>
        <div
          v-if="u.status === 'envoi'"
          class="progress mt-1"
          role="progressbar"
          :aria-label="`Envoi de ${u.name}`"
          :aria-valuenow="Math.round(u.progress * 100)"
          aria-valuemin="0"
          aria-valuemax="100"
        >
          <div class="progress-bar" :style="{ width: `${u.progress * 100}%` }" />
        </div>
        <small
          class="d-flex align-items-center gap-1 mt-1"
          :class="{
            'text-danger': u.status === 'echec',
            'text-success': u.status === 'termine',
            'text-body-secondary': !['echec', 'termine'].includes(u.status),
          }"
        >
          <span v-if="u.status === 'traitement'" class="spinner-border spinner-border-sm" aria-hidden="true" />
          {{ statusText(u) }}
        </small>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.envois {
  margin-bottom: 1rem;
}

.flottant {
  position: fixed;
  right: 1rem;
  bottom: 1rem;
  z-index: 1040;
  width: 24rem;
  margin: 0;
  box-shadow: var(--bs-box-shadow);
}

.liste {
  max-height: 40dvh;
  overflow-y: auto;
}

.nom {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.progress {
  height: 0.4rem;
}
</style>
