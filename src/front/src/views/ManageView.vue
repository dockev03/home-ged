<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { deleteManaged, listManaged, type ManagedItem } from '../api/gestion'
import { ApiError } from '../api/http'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import ManageObjectDialog from '../components/ManageObjectDialog.vue'
import TagBadge from '../components/TagBadge.vue'
import { dataTypeLabel, deleteConsequence, kindBySlug, matchingLabel, type KindConfig } from '../gestion/kinds'
import { useReferencesStore } from '../stores/references'

/**
 * Écrans « Gérer » (UI-07) : étiquettes, correspondants, types de documents, chemins de stockage
 * et champs personnalisés. Liste paginée avec recherche et tri ; création, modification et
 * suppression dans des fenêtres. Paperless applique les droits de l'utilisateur.
 */
const PAGE_SIZE = 25
const SEARCH_DELAY_MS = 300

const route = useRoute()
const references = useReferencesStore()

const config = computed<KindConfig>(() => kindBySlug(route.params.type)!)

const items = ref<ManagedItem[] | null>(null)
const count = ref(0)
const page = ref(1)
const search = ref('')
const tri = ref('name')
const loading = ref(false)
const loadError = ref<string | null>(null)
const notice = ref<string | null>(null)

const editing = ref<ManagedItem | null>(null)
const formOpen = ref(false)
const deleting = ref<ManagedItem | null>(null)
const deleteBusy = ref(false)
const deleteError = ref<string | null>(null)

const pageCount = computed(() => Math.max(1, Math.ceil(count.value / PAGE_SIZE)))

const errorMessage = (e: unknown) =>
  e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED. Vérifiez votre connexion.'

let request = 0
async function load() {
  const current = ++request
  loading.value = true
  loadError.value = null
  try {
    const result = await listManaged(config.value.kind, { page: page.value, pageSize: PAGE_SIZE, nom: search.value, tri: tri.value })
    if (current !== request) return
    items.value = result.results
    count.value = result.count
  } catch (e) {
    if (current !== request) return
    loadError.value = errorMessage(e)
  } finally {
    if (current === request) loading.value = false
  }
}

// Nouvelle liste : critères remis à zéro.
watch(
  () => route.params.type,
  () => {
    if (!kindBySlug(route.params.type)) return
    items.value = null
    page.value = 1
    search.value = ''
    tri.value = 'name'
    notice.value = null
    void load()
  },
  { immediate: true },
)

let searchTimer: ReturnType<typeof setTimeout> | undefined
function onSearch() {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    page.value = 1
    void load()
  }, SEARCH_DELAY_MS)
}
onBeforeUnmount(() => clearTimeout(searchTimer))

function sortBy(field: string) {
  tri.value = tri.value === field ? `-${field}` : field
  page.value = 1
  void load()
}

const sortState = (field: string) =>
  tri.value === field ? 'ascending' : tri.value === `-${field}` ? 'descending' : 'none'

function goToPage(n: number) {
  page.value = Math.min(Math.max(1, n), pageCount.value)
  void load()
}

function openCreate() {
  editing.value = null
  formOpen.value = true
}

function openEdit(item: ManagedItem) {
  editing.value = item
  formOpen.value = true
}

async function saved(item: ManagedItem) {
  formOpen.value = false
  notice.value = `« ${item.name} » est enregistré.`
  await Promise.all([load(), references.reload()])
}

function askDelete(item: ManagedItem) {
  deleteError.value = null
  deleting.value = item
}

async function confirmDelete() {
  const item = deleting.value
  if (!item) return
  deleteBusy.value = true
  deleteError.value = null
  try {
    await deleteManaged(config.value.kind, item.id)
  } catch (e) {
    // Déjà supprimé ailleurs : le résultat voulu est atteint.
    if (!(e instanceof ApiError && e.code === 'introuvable')) {
      deleteError.value = errorMessage(e)
      deleteBusy.value = false
      return
    }
  }
  deleteBusy.value = false
  deleting.value = null
  notice.value = `« ${item.name} » est supprimé.`
  // Dernier élément de la page : page précédente.
  if (items.value?.length === 1 && page.value > 1) page.value--
  await Promise.all([load(), references.reload()])
}

const documentsLink = (item: ManagedItem) => ({ name: 'documents', query: { [config.value.documentsParam]: String(item.id) } })

const asTag = (item: ManagedItem) => ({
  id: item.id,
  name: item.name,
  color: item.color ?? '#a6cee3',
  text_color: item.text_color ?? '#000000',
  is_inbox_tag: item.is_inbox_tag ?? false,
})
</script>

<template>
  <div class="gestion">
    <div class="d-flex flex-wrap align-items-center gap-2">
      <h1 class="h3 mb-0 me-auto">{{ config.title }}</h1>
      <button type="button" class="btn btn-primary" @click="openCreate">+ {{ config.create }}</button>
    </div>

    <input
      v-model="search"
      type="search"
      class="form-control recherche"
      placeholder="Filtrer par nom"
      :aria-label="`Filtrer les ${config.title.toLocaleLowerCase('fr')} par nom`"
      @input="onSearch"
    />

    <div v-if="notice" class="alert alert-success alert-dismissible mb-0" role="status">
      {{ notice }}
      <button type="button" class="btn-close" aria-label="Fermer" @click="notice = null" />
    </div>
    <div v-if="loadError" class="alert alert-danger d-flex align-items-center gap-2 mb-0" role="alert">
      <span class="me-auto">{{ loadError }}</span>
      <button type="button" class="btn btn-sm btn-outline-danger" @click="load">Réessayer</button>
    </div>

    <div v-if="items === null && !loadError" class="chargement">
      <span class="spinner-border" role="status"><span class="visually-hidden">Chargement…</span></span>
    </div>

    <template v-if="items">
      <p class="text-body-secondary small mb-0" aria-live="polite">
        {{ count === 0 ? 'Aucun élément' : count === 1 ? '1 élément' : `${count} éléments` }}
        <span v-if="loading" class="spinner-border spinner-border-sm ms-1" aria-hidden="true" />
      </p>

      <div v-if="items.length" class="table-responsive">
        <table class="table table-hover align-middle mb-0">
          <thead>
            <tr>
              <th scope="col" :aria-sort="sortState('name')">
                <button type="button" class="btn btn-link tri" @click="sortBy('name')">
                  Nom <span aria-hidden="true">{{ tri === 'name' ? '▲' : tri === '-name' ? '▼' : '' }}</span>
                </button>
              </th>
              <th v-if="config.kind === 'champs'" scope="col" class="d-none d-md-table-cell">Type de données</th>
              <th v-else scope="col" class="d-none d-md-table-cell">Classement automatique</th>
              <th v-if="config.kind === 'chemins'" scope="col" class="d-none d-lg-table-cell">Chemin</th>
              <th scope="col" class="text-end" :aria-sort="sortState('document_count')">
                <button type="button" class="btn btn-link tri" @click="sortBy('document_count')">
                  Documents
                  <span aria-hidden="true">{{ tri === 'document_count' ? '▲' : tri === '-document_count' ? '▼' : '' }}</span>
                </button>
              </th>
              <th scope="col"><span class="visually-hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in items" :key="item.id" :data-id="item.id">
              <td>
                <TagBadge v-if="config.kind === 'tags'" :tag="asTag(item)" />
                <span v-else class="fw-medium">{{ item.name }}</span>
                <span v-if="item.is_inbox_tag" class="badge text-bg-light border ms-1">Boîte de réception</span>
              </td>
              <td v-if="config.kind === 'champs'" class="d-none d-md-table-cell">{{ dataTypeLabel(item.data_type) }}</td>
              <td v-else class="d-none d-md-table-cell text-body-secondary">{{ matchingLabel(item) }}</td>
              <td v-if="config.kind === 'chemins'" class="d-none d-lg-table-cell"><code>{{ item.path }}</code></td>
              <td class="text-end">
                <RouterLink
                  v-if="item.document_count"
                  :to="documentsLink(item)"
                  :aria-label="`Voir les ${item.document_count} documents de ${item.name}`"
                >
                  {{ item.document_count }}
                </RouterLink>
                <span v-else class="text-body-secondary">0</span>
              </td>
              <td class="text-end text-nowrap">
                <button type="button" class="btn btn-sm btn-outline-secondary" @click="openEdit(item)">
                  {{ item.user_can_change === false ? 'Voir' : 'Modifier' }}
                  <span class="visually-hidden">{{ item.name }}</span>
                </button>
                <button
                  v-if="item.user_can_change !== false"
                  type="button"
                  class="btn btn-sm btn-outline-danger ms-1"
                  @click="askDelete(item)"
                >
                  Supprimer <span class="visually-hidden">{{ item.name }}</span>
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-else-if="!loading" class="text-body-secondary">
        {{ search.trim() ? 'Aucun élément ne correspond à ce nom.' : 'Aucun élément pour le moment.' }}
      </p>

      <nav v-if="pageCount > 1" aria-label="Pagination">
        <ul class="pagination justify-content-center align-items-center flex-wrap mb-0">
          <li class="page-item" :class="{ disabled: page <= 1 }">
            <button type="button" class="page-link" :disabled="page <= 1" @click="goToPage(page - 1)">Précédente</button>
          </li>
          <li class="page-item disabled">
            <span class="page-link">Page {{ page }} sur {{ pageCount }}</span>
          </li>
          <li class="page-item" :class="{ disabled: page >= pageCount }">
            <button type="button" class="page-link" :disabled="page >= pageCount" @click="goToPage(page + 1)">Suivante</button>
          </li>
        </ul>
      </nav>
    </template>

    <ManageObjectDialog :open="formOpen" :config="config" :item="editing" @saved="saved" @cancel="formOpen = false" />

    <ConfirmDialog
      :open="deleting !== null"
      :title="`Supprimer ${config.the} « ${deleting?.name ?? ''} » ?`"
      confirm-label="Supprimer définitivement"
      danger
      :busy="deleteBusy"
      @confirm="confirmDelete"
      @cancel="deleting = null"
    >
      <p v-if="deleting">{{ deleteConsequence(config, deleting) }}</p>
      <p class="mb-0">Cette suppression est définitive : elle ne passe pas par la corbeille.</p>
      <p v-if="deleteError" class="alert alert-danger mt-3 mb-0" role="alert">{{ deleteError }}</p>
    </ConfirmDialog>
  </div>
</template>

<style scoped>
.gestion {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.recherche {
  max-width: 24rem;
}

.chargement {
  display: flex;
  justify-content: center;
  padding: 2rem;
}

.tri {
  padding: 0;
  font-weight: inherit;
  color: inherit;
  text-decoration: none;
}

/* Cibles tactiles confortables sur mobile. */
@media (max-width: 767.98px) {
  .table .btn-sm {
    min-height: 44px;
    min-width: 44px;
  }
}
</style>
