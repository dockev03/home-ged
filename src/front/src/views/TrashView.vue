<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ApiError } from '../api/http'
import {
  TRASH_PAGE_SIZE,
  daysLeft,
  deleteForever,
  emptyTrash,
  listTrash,
  restoreDocuments,
  type TrashPage,
} from '../api/trash'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import TagBadge from '../components/TagBadge.vue'
import { formatDate } from '../format/dates'
import { useOpenDocumentsStore } from '../stores/openDocuments'
import { useReferencesStore } from '../stores/references'

/** Corbeille (UI-10) : documents supprimés, restaurables jusqu'à leur effacement par Paperless. */
const references = useReferencesStore()
const openDocuments = useOpenDocumentsStore()

const page = ref(1)
const data = ref<TrashPage | null>(null)
const loadError = ref<string | null>(null)
const notice = ref<string | null>(null)
const actionError = ref<string | null>(null)
const selected = ref<number[]>([])
const busy = ref(false)
/** Action en attente de confirmation. */
const confirming = ref<'supprimer' | 'vider' | null>(null)

const errorMessage = (e: unknown) =>
  e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED. Vérifiez votre connexion.'

const pages = computed(() => Math.max(1, Math.ceil((data.value?.count ?? 0) / TRASH_PAGE_SIZE)))
const pageIds = computed(() => data.value?.results.map((d) => d.id) ?? [])
const allSelected = computed(() => pageIds.value.length > 0 && pageIds.value.every((id) => selected.value.includes(id)))
const plural = (n: number) => (n > 1 ? 's' : '')
const label = (n: number) => `${n} document${plural(n)}`

async function load() {
  loadError.value = null
  try {
    const result = await listTrash(page.value)
    // Dernière page vidée : on revient à la précédente.
    if (result.results.length === 0 && page.value > 1) {
      page.value--
      return load()
    }
    data.value = result
    selected.value = selected.value.filter((id) => result.results.some((d) => d.id === id))
  } catch (e) {
    loadError.value = errorMessage(e)
  }
}

onMounted(() => {
  void references.ensureLoaded()
  void load()
})

function goTo(target: number) {
  page.value = target
  selected.value = []
  void load()
}

function toggle(id: number) {
  selected.value = selected.value.includes(id) ? selected.value.filter((v) => v !== id) : [...selected.value, id]
}

function toggleAll() {
  selected.value = allSelected.value ? [] : [...pageIds.value]
}

function purgeLabel(deletedAt: string) {
  const days = daysLeft(deletedAt, data.value?.delai ?? null)
  if (days === null) return null
  if (days === 0) return 'Effacement imminent'
  return `Effacé dans ${days} jour${plural(days)}`
}

/** `erased` : documents effacés définitivement, retirés des documents ouverts. */
async function run(action: () => Promise<unknown>, message: string, erased: number[] = []) {
  busy.value = true
  actionError.value = null
  notice.value = null
  try {
    await action()
    notice.value = message
    selected.value = []
    erased.forEach((id) => openDocuments.close(id))
    await load()
  } catch (e) {
    actionError.value = errorMessage(e)
  } finally {
    busy.value = false
    confirming.value = null
  }
}

function restore(ids: number[]) {
  const n = ids.length
  void run(() => restoreDocuments(ids), `${label(n)} restauré${plural(n)}.`)
}

function confirm() {
  if (confirming.value === 'vider') {
    void run(emptyTrash, 'Corbeille vidée.', pageIds.value)
  } else {
    const ids = [...selected.value]
    void run(() => deleteForever(ids), `${label(ids.length)} supprimé${plural(ids.length)} définitivement.`, ids)
  }
}

const correspondent = (id: number | null) => (id === null ? null : references.correspondants.get(id)?.name ?? null)
const tagsOf = (ids: number[]) => ids.map((id) => references.tags.get(id)).filter((t) => t !== undefined)
</script>

<template>
  <div class="corbeille">
    <div class="d-flex flex-wrap align-items-center gap-2">
      <h1 class="h3 mb-0 me-auto">Corbeille</h1>
      <button
        type="button"
        class="btn btn-outline-danger"
        :disabled="busy || !data?.count"
        @click="confirming = 'vider'"
      >
        Vider la corbeille
      </button>
    </div>

    <p class="text-body-secondary mb-0">
      Les documents supprimés restent ici
      <template v-if="data?.delai !== null && data?.delai !== undefined">{{ data.delai }} jours</template>
      avant d'être effacés définitivement par Paperless.
    </p>

    <div v-if="notice" class="alert alert-success alert-dismissible mb-0" role="status">
      {{ notice }}
      <button type="button" class="btn-close" aria-label="Fermer" @click="notice = null" />
    </div>
    <div v-if="actionError" class="alert alert-danger mb-0" role="alert">{{ actionError }}</div>
    <div v-if="loadError" class="alert alert-danger d-flex align-items-center gap-2 mb-0" role="alert">
      <span class="me-auto">{{ loadError }}</span>
      <button type="button" class="btn btn-sm btn-outline-danger" @click="load">Réessayer</button>
    </div>

    <div v-if="data === null && !loadError" class="chargement">
      <span class="spinner-border" role="status"><span class="visually-hidden">Chargement…</span></span>
    </div>

    <template v-if="data">
      <p v-if="data.count === 0" class="vide">La corbeille est vide.</p>

      <template v-else>
        <div class="selection">
          <div class="form-check mb-0">
            <input id="tout" class="form-check-input" type="checkbox" :checked="allSelected" @change="toggleAll" />
            <label class="form-check-label" for="tout">
              {{ selected.length ? `${label(selected.length)} sélectionné${plural(selected.length)}` : `${label(data.count)}` }}
            </label>
          </div>
          <div v-if="selected.length" class="d-flex flex-wrap gap-2 ms-auto">
            <button type="button" class="btn btn-sm btn-primary" :disabled="busy" @click="restore([...selected])">
              Restaurer
            </button>
            <button type="button" class="btn btn-sm btn-outline-danger" :disabled="busy" @click="confirming = 'supprimer'">
              Supprimer définitivement
            </button>
          </div>
        </div>

        <ul class="list-group">
          <li v-for="doc in data.results" :key="doc.id" class="list-group-item element">
            <input
              :id="`doc-${doc.id}`"
              class="form-check-input flex-shrink-0"
              type="checkbox"
              :checked="selected.includes(doc.id)"
              :aria-label="`Sélectionner ${doc.title}`"
              @change="toggle(doc.id)"
            />
            <div class="infos">
              <label class="fw-semibold titre" :for="`doc-${doc.id}`">{{ doc.title }}</label>
              <div class="small text-body-secondary">
                <span v-if="correspondent(doc.correspondent)">{{ correspondent(doc.correspondent) }} · </span>
                supprimé le {{ formatDate(doc.deleted_at) }}
                <span v-if="purgeLabel(doc.deleted_at)" class="text-danger-emphasis"> · {{ purgeLabel(doc.deleted_at) }}</span>
              </div>
              <div v-if="doc.tags.length" class="etiquettes"><TagBadge v-for="tag in tagsOf(doc.tags)" :key="tag.id" :tag="tag" /></div>
            </div>
            <button type="button" class="btn btn-sm btn-outline-primary flex-shrink-0" :disabled="busy" @click="restore([doc.id])">
              Restaurer
            </button>
          </li>
        </ul>

        <nav v-if="pages > 1" aria-label="Pages de la corbeille" class="d-flex justify-content-center">
          <ul class="pagination mb-0">
            <li class="page-item" :class="{ disabled: page === 1 }">
              <button type="button" class="page-link" :disabled="page === 1" @click="goTo(page - 1)">Précédente</button>
            </li>
            <li class="page-item disabled"><span class="page-link">{{ page }} / {{ pages }}</span></li>
            <li class="page-item" :class="{ disabled: page === pages }">
              <button type="button" class="page-link" :disabled="page === pages" @click="goTo(page + 1)">Suivante</button>
            </li>
          </ul>
        </nav>
      </template>
    </template>

    <ConfirmDialog
      :open="confirming !== null"
      :title="confirming === 'vider' ? 'Vider la corbeille ?' : 'Supprimer définitivement ?'"
      :confirm-label="confirming === 'vider' ? 'Vider la corbeille' : 'Supprimer définitivement'"
      danger
      :busy="busy"
      @confirm="confirm"
      @cancel="confirming = null"
    >
      <p v-if="confirming === 'vider'">
        Les {{ label(data?.count ?? 0) }} de la corbeille seront effacés de Paperless.
      </p>
      <p v-else>{{ label(selected.length) }} {{ selected.length > 1 ? 'seront effacés' : 'sera effacé' }} de Paperless.</p>
      <p class="mb-0 fw-semibold">Cette action est définitive : ils ne pourront pas être restaurés.</p>
    </ConfirmDialog>
  </div>
</template>

<style scoped>
.corbeille {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 60rem;
}

.selection {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  min-height: 2rem;
}

.element {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.infos {
  flex: 1 1 auto;
  min-width: 0;
}

.titre {
  display: block;
  overflow-wrap: anywhere;
  cursor: pointer;
}

.etiquettes {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
  margin-top: 0.25rem;
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
