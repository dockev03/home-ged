<script setup lang="ts">
import { computed, onBeforeUnmount, ref, useTemplateRef } from 'vue'
import {
  deleteVersion,
  downloadUrl,
  getTask,
  MAX_UPLOAD_BYTES,
  renameVersion,
  uploadVersion,
  type DocumentDetail,
  type DocumentVersion,
} from '../api/documents'
import { ApiError } from '../api/http'
import { formatDateTime } from '../format/dates'
import ConfirmDialog from './ConfirmDialog.vue'

/**
 * Onglet Versions (Paperless 3) : afficher, télécharger, renommer, supprimer une version, et en
 * envoyer une nouvelle, que Paperless traite ensuite (OCR) en tâche de fond.
 */
const props = defineProps<{ document: DocumentDetail; disabled?: boolean; shown: number | null }>()
const emit = defineEmits<{ show: [number | null]; changed: [] }>()

/** Suivi d'une tâche Paperless : toutes les 2 s, 5 minutes au plus. */
const POLL_MS = 2000
const POLL_MAX = 150

const versions = computed(() => props.document.versions ?? [])
const latest = computed(() => versions.value[0]?.id ?? null)
const error = ref<string | null>(null)
const busy = ref(false)

const title = (version: DocumentVersion) =>
  version.version_label || (version.is_root ? "Version d'origine" : `Version du ${formatDateTime(version.added)}`)

const run = async (action: () => Promise<unknown>) => {
  busy.value = true
  error.value = null
  try {
    await action()
    emit('changed')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Action impossible, vérifiez votre connexion.'
  } finally {
    busy.value = false
  }
}

// Renommer : saisie dans la carte.
const renaming = ref<number | null>(null)
const label = ref('')
function startRename(version: DocumentVersion) {
  renaming.value = version.id
  label.value = version.version_label ?? ''
}
async function rename(version: DocumentVersion) {
  await run(() => renameVersion(props.document.id, version.id, label.value.trim()))
  renaming.value = null
}

// Supprimer : avec confirmation.
const deleting = ref<DocumentVersion | null>(null)
async function confirmDelete() {
  const version = deleting.value
  deleting.value = null
  if (!version) return
  if (props.shown === version.id) emit('show', null)
  await run(() => deleteVersion(props.document.id, version.id))
}

// Nouvelle version.
const fileInput = useTemplateRef<HTMLInputElement>('fileInput')
const file = ref<File | null>(null)
const newLabel = ref('')
const progress = ref<number | null>(null)
const processing = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined
onBeforeUnmount(() => clearTimeout(timer))

function pick(event: Event) {
  file.value = (event.target as HTMLInputElement).files?.[0] ?? null
  error.value = file.value && file.value.size > MAX_UPLOAD_BYTES ? 'Fichier trop volumineux (200 Mo au plus).' : null
}

async function upload() {
  if (!file.value || file.value.size > MAX_UPLOAD_BYTES || progress.value !== null || processing.value) return
  error.value = null
  progress.value = 0
  try {
    const task = await uploadVersion(props.document.id, file.value, newLabel.value, (f) => (progress.value = f))
    progress.value = null
    processing.value = true
    await follow(task, 0)
  } catch (e) {
    progress.value = null
    processing.value = false
    error.value = e instanceof ApiError ? e.message : 'Envoi impossible, vérifiez votre connexion.'
  }
}

/** Suit le traitement Paperless jusqu'à son terme. */
async function follow(task: string, attempt: number): Promise<void> {
  let state
  try {
    state = await getTask(task)
  } catch {
    state = { status: 'pending' as const, message: null, document_id: null }
  }
  if (state.status === 'success') {
    processing.value = false
    file.value = null
    newLabel.value = ''
    if (fileInput.value) fileInput.value.value = ''
    emit('changed')
    return
  }
  if (state.status === 'failure' || state.status === 'revoked') {
    processing.value = false
    error.value = `Paperless n'a pas pu traiter la nouvelle version${state.message ? ` : ${state.message}` : '.'}`
    return
  }
  if (attempt >= POLL_MAX) {
    processing.value = false
    error.value = 'Le traitement prend du temps : la nouvelle version apparaîtra une fois traitée par Paperless.'
    return
  }
  await new Promise<void>((resolve) => (timer = setTimeout(resolve, POLL_MS)))
  return follow(task, attempt + 1)
}
</script>

<template>
  <div class="d-flex flex-column gap-3">
    <p v-if="error" class="alert alert-danger mb-0" role="alert">{{ error }}</p>

    <ul class="list-unstyled d-flex flex-column gap-2 mb-0">
      <li v-for="version in versions" :key="version.id" class="card" :class="{ 'border-primary': shown === version.id || (shown === null && version.id === latest) }" :data-version="version.id">
        <div class="card-body py-2">
          <div class="d-flex flex-wrap align-items-baseline gap-2">
            <strong class="me-auto">{{ title(version) }}</strong>
            <span v-if="version.id === latest" class="badge text-bg-success">Actuelle</span>
          </div>
          <p class="small text-body-secondary mb-2">
            Ajoutée le {{ formatDateTime(version.added) }}
            <span v-if="version.checksum" class="font-monospace"> · {{ version.checksum.slice(0, 8) }}</span>
          </p>
          <form v-if="renaming === version.id" class="d-flex gap-2 mb-2" @submit.prevent="rename(version)" @keydown.esc.stop="renaming = null">
            <input v-model="label" class="form-control form-control-sm" maxlength="64" aria-label="Libellé de la version" placeholder="Libellé (facultatif)" />
            <button type="submit" class="btn btn-sm btn-primary" :disabled="busy">Renommer</button>
            <button type="button" class="btn btn-sm btn-link" @click="renaming = null">Annuler</button>
          </form>
          <div class="d-flex flex-wrap gap-2">
            <button type="button" class="btn btn-sm btn-outline-secondary" :aria-pressed="shown === version.id" @click="emit('show', version.id === latest ? null : version.id)">
              Afficher
            </button>
            <a class="btn btn-sm btn-outline-secondary" :href="downloadUrl(document.id, false, version.id)" download>Télécharger</a>
            <template v-if="!disabled">
              <button type="button" class="btn btn-sm btn-outline-secondary" @click="startRename(version)">Renommer</button>
              <button v-if="!version.is_root" type="button" class="btn btn-sm btn-outline-danger" :disabled="busy" @click="deleting = version">
                Supprimer
              </button>
            </template>
          </div>
        </div>
      </li>
    </ul>

    <form v-if="!disabled" class="card card-body d-flex flex-column gap-2" @submit.prevent="upload">
      <h3 class="fs-6 mb-0">Nouvelle version</h3>
      <input ref="fileInput" type="file" class="form-control" aria-label="Fichier de la nouvelle version" @change="pick" />
      <input v-model="newLabel" class="form-control" maxlength="64" aria-label="Libellé de la nouvelle version" placeholder="Libellé (facultatif)" />
      <div v-if="progress !== null" class="progress" role="progressbar" :aria-valuenow="Math.round(progress * 100)" aria-valuemin="0" aria-valuemax="100" aria-label="Envoi">
        <div class="progress-bar" :style="{ width: `${Math.round(progress * 100)}%` }">{{ Math.round(progress * 100) }} %</div>
      </div>
      <p v-if="processing" class="small mb-0" role="status">
        <span class="spinner-border spinner-border-sm me-1" aria-hidden="true" /> Paperless traite la nouvelle version (OCR)…
      </p>
      <div>
        <button type="submit" class="btn btn-primary" :disabled="!file || progress !== null || processing">Envoyer la nouvelle version</button>
      </div>
    </form>

    <ConfirmDialog
      :open="deleting !== null"
      title="Supprimer cette version ?"
      confirm-label="Supprimer"
      danger
      @confirm="confirmDelete"
      @cancel="deleting = null"
    >
      « {{ deleting ? title(deleting) : '' }} » sera définitivement supprimée.
    </ConfirmDialog>
  </div>
</template>
