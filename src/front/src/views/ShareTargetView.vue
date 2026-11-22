<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { MAX_UPLOAD_BYTES } from '../api/documents'
import TagPicker from '../components/TagPicker.vue'
import { listSharedFiles, removeSharedFiles, type SharedFile } from '../offline/shared'
import { findPersonField } from '../scan/document'
import { useReferencesStore } from '../stores/references'
import { useUploadsStore } from '../stores/uploads'

/**
 * Documents partagés (PWA-11) : fichiers reçus d'une autre application par « Partager vers
 * Home-GED ». Le service worker les a rangés sur l'appareil ; ils restent là tant qu'ils ne sont
 * ni envoyés ni abandonnés. L'envoi passe par la file habituelle (reprise hors ligne, PWA-10).
 */
const ERRORS: Record<string, string> = {
  'aucun-fichier': 'Aucun fichier reçu : seuls des fichiers peuvent être partagés vers Home-GED.',
  reception: 'Les fichiers partagés n’ont pas pu être gardés sur l’appareil (espace de stockage insuffisant ?).',
  'service-worker':
    'Le partage n’a pas pu être reçu : ouvrez Home-GED une fois, attendez la fin du chargement, puis partagez à nouveau.',
}

const route = useRoute()
const router = useRouter()
const uploads = useUploadsStore()
const references = useReferencesStore()

const files = ref<SharedFile[]>([])
const loading = ref(true)
const sent = ref<string | null>(null)

const title = ref('')
const tags = ref<number[]>([])
const person = ref('')
const personField = computed(() => findPersonField(references.lists.champs))

const error = computed(() => {
  const code = route.query.erreur
  return typeof code === 'string' ? (ERRORS[code] ?? ERRORS.reception) : null
})

/** Fichier que Paperless refusera : signalé avant l'envoi. */
const problem = (file: File) =>
  file.size === 0 ? 'Fichier vide' : file.size > MAX_UPLOAD_BYTES ? 'Trop volumineux (200 Mo au plus)' : null

const sendable = computed(() => files.value.filter((f) => !problem(f.file)))

function size(bytes: number) {
  if (bytes < 1024 * 1024) return `${Math.max(bytes ? 1 : 0, Math.round(bytes / 1024))} Ko`
  return `${(bytes / 1024 / 1024).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mo`
}

const plural = (n: number) => (n > 1 ? 's' : '')

async function load() {
  files.value = await listSharedFiles()
  loading.value = false
}

async function remove(keys: string[]) {
  await removeSharedFiles(keys)
  files.value = files.value.filter((f) => !keys.includes(f.key))
}

/** Confie les fichiers acceptables à la file d'envoi, avec la saisie facultative. */
async function send() {
  const batch = sendable.value
  if (batch.length === 0) return
  const field = personField.value
  uploads.add(
    batch.map((f) => f.file),
    {
      // Un titre n'a de sens que pour un fichier seul ; sinon Paperless reprend le nom de chaque fichier.
      title: batch.length === 1 ? title.value : '',
      tags: tags.value,
      customFields: field && person.value ? { [field.id]: person.value } : {},
    },
  )
  await remove(files.value.map((f) => f.key))
  title.value = ''
  tags.value = []
  person.value = ''
  sent.value = `${batch.length} fichier${plural(batch.length)} envoyé${plural(batch.length)}. Suivez le traitement dans le panneau des envois.`
  clearQuery()
}

async function discard() {
  await remove(files.value.map((f) => f.key))
  clearQuery()
}

/** Retire « recus » et « erreur » de l'adresse : un rechargement ne réaffiche pas le message. */
function clearQuery() {
  if (Object.keys(route.query).length) void router.replace({ name: 'partager' })
}

onMounted(() => {
  void load()
  void references.ensureLoaded()
})
</script>

<template>
  <div class="partages">
    <h1 class="h3 mb-0">Documents partagés</h1>

    <div v-if="error" class="alert alert-danger mb-0" role="alert">{{ error }}</div>
    <div v-if="sent" class="alert alert-success mb-0" role="status">{{ sent }}</div>

    <p v-if="loading" class="text-body-secondary mb-0">Chargement…</p>
    <template v-else-if="files.length === 0">
      <p v-if="!sent" class="text-body-secondary mb-0" data-test="vide">
        Aucun document en attente. Depuis une autre application, choisissez « Partager », puis Home-GED.
      </p>
      <div>
        <RouterLink :to="{ name: 'documents' }" class="btn btn-outline-primary">Voir les documents</RouterLink>
      </div>
    </template>

    <template v-else>
      <ul class="list-group">
        <li v-for="f in files" :key="f.key" class="list-group-item d-flex align-items-center gap-2" data-test="fichier">
          <span class="nom me-auto" :title="f.file.name">{{ f.file.name }}</span>
          <small v-if="problem(f.file)" class="text-danger text-nowrap">{{ problem(f.file) }}</small>
          <small class="text-body-secondary text-nowrap">{{ size(f.file.size) }}</small>
          <button
            type="button"
            class="btn btn-sm btn-link text-danger p-0 retirer"
            :aria-label="`Retirer ${f.file.name}`"
            @click="remove([f.key])"
          >
            Retirer
          </button>
        </li>
      </ul>

      <form class="card" @submit.prevent="send">
        <div class="card-body">
          <h2 class="h5">Envoyer à Paperless</h2>
          <p class="text-body-secondary small">
            Facultatif : Paperless reconnaît le texte et propose lui-même un classement.
          </p>
          <div v-if="sendable.length === 1" class="mb-3">
            <label class="form-label" for="partage-titre">Titre</label>
            <input
              id="partage-titre"
              v-model="title"
              class="form-control"
              maxlength="128"
              placeholder="Titre déduit du nom du fichier"
            />
          </div>
          <div v-if="personField" class="mb-3">
            <label class="form-label" for="partage-personne">{{ personField.name }}</label>
            <select id="partage-personne" v-model="person" class="form-select">
              <option value="">Non précisée</option>
              <option v-for="option in personField.extra_data?.select_options ?? []" :key="option.id" :value="option.id">
                {{ option.label }}
              </option>
            </select>
          </div>
          <TagPicker id="partage-etiquettes" v-model="tags" class="mb-3" />
          <div class="actions">
            <button type="submit" class="btn btn-primary" :disabled="sendable.length === 0">
              Envoyer {{ sendable.length }} fichier{{ plural(sendable.length) }}
            </button>
            <button type="button" class="btn btn-outline-secondary" @click="discard">Abandonner</button>
          </div>
        </div>
      </form>
    </template>
  </div>
</template>

<style scoped>
.partages {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 60rem;
}

.nom {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.retirer,
.actions .btn {
  min-height: 44px;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
</style>
