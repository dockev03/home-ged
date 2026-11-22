<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { listDocumentIds, listDocuments, thumbnailUrl, type DocumentSummary } from '../api/documents'
import { ApiError } from '../api/http'
import OfflineDocuments from '../components/OfflineDocuments.vue'
import { isNetworkError } from '../offline/db'
import type { SavedView } from '../api/savedViews'
import BaseDropdown from '../components/BaseDropdown.vue'
import BulkActions from '../components/BulkActions.vue'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import FilterBar from '../components/FilterBar.vue'
import SavedViewDialog from '../components/SavedViewDialog.vue'
import TagBadge from '../components/TagBadge.vue'
import UploadButton from '../components/UploadButton.vue'
import { MOBILE_QUERY, useMediaQuery } from '../composables/mediaQuery'
import { fromQuery, toApiParams, toQuery, type DocumentFilters } from '../filters/documentFilters'
import { differsFromView, filtersFromView, viewFromFilters, viewQuery } from '../filters/savedViews'
import { formatShortDate } from '../format/dates'
import { parseHighlights } from '../format/highlights'
import { useAuthStore } from '../stores/auth'
import { useDocumentListStore } from '../stores/documentList'
import { usePreferencesStore, type DisplayChoice } from '../stores/preferences'
import { useReferencesStore } from '../stores/references'
import { useSavedViewsStore } from '../stores/savedViews'
import { useUploadsStore } from '../stores/uploads'

/**
 * Liste des documents (UI-02) : vignettes, détails ou tableau ; filtres dans l'URL (UI-03) ;
 * sélection multiple et actions en masse (UI-06) ; vues sauvegardées (UI-08), la vue ouverte
 * étant repérée par le paramètre `vue` de l'URL.
 */
const PAGE_SIZE = 25

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const preferences = usePreferencesStore()
const references = useReferencesStore()
const list = useDocumentListStore()
const mobile = useMediaQuery(MOBILE_QUERY)

const filters = computed(() => fromQuery(route.query))
const page = computed(() => {
  const value = Number(route.query.page)
  return Number.isSafeInteger(value) && value > 0 ? value : 1
})

const documents = ref<DocumentSummary[]>([])
const count = ref(0)
/** Dernière page chargée (défilement infini) ou affichée (pagination). */
const loadedPage = ref(0)
const loading = ref(false)
const error = ref<string | null>(null)
/** Liste injoignable faute de réseau : les documents gardés sur l'appareil sont proposés (PWA-09). */
const unreachable = ref(false)
let generation = 0

const pageCount = computed(() => Math.max(1, Math.ceil(count.value / PAGE_SIZE)))
const hasMore = computed(() => documents.value.length < count.value)

const DISPLAYS: { value: DisplayChoice; label: string }[] = [
  { value: 'vignettes', label: 'Vignettes' },
  { value: 'details', label: 'Détails' },
  { value: 'tableau', label: 'Tableau' },
]

function errorMessage(e: unknown) {
  if (e instanceof ApiError) return e.message
  return 'Impossible de joindre Home-GED. Vérifiez votre connexion.'
}

/** Charge une page ; en défilement infini, l'ajoute à la suite. Les réponses périmées sont ignorées. */
async function load(target: number, append: boolean) {
  const current = ++generation
  loading.value = true
  error.value = null
  unreachable.value = false
  try {
    const result = await listDocuments(
      toApiParams(filters.value, { page: target, pageSize: PAGE_SIZE, userId: auth.user?.id ?? null }),
    )
    if (current !== generation) return
    documents.value = append ? [...documents.value, ...result.results] : result.results
    count.value = result.count
    loadedPage.value = target
    list.rememberQuery(route.query)
  } catch (e) {
    if (current !== generation) return
    // Page hors limites (documents supprimés entre-temps) : retour à la première.
    if (e instanceof ApiError && e.status === 404 && target > 1 && !append) {
      void goToPage(1)
      return
    }
    error.value = errorMessage(e)
    unreachable.value = isNetworkError(e)
  } finally {
    if (current === generation) loading.value = false
  }
}

function reload() {
  if (mobile.value) void load(1, false)
  else void load(page.value, false)
}

function loadMore() {
  if (!loading.value && !error.value && hasMore.value) void load(loadedPage.value + 1, true)
}

watch([() => JSON.stringify(filters.value), page, mobile], reload)

/**
 * Identifiants de tous les résultats (précédent / suivant de la fiche) : une requête par changement
 * de filtres, pas par page. Un échec laisse seulement la fiche sans précédent / suivant.
 */
let idsGeneration = 0
async function loadIds() {
  const current = ++idsGeneration
  const params = toApiParams(filters.value, { page: 1, pageSize: PAGE_SIZE, userId: auth.user?.id ?? null })
  params.delete('page')
  params.delete('page_size')
  try {
    const ids = await listDocumentIds(params)
    if (current === idsGeneration) list.rememberIds(ids)
  } catch {
    if (current === idsGeneration) list.rememberIds([])
  }
}
watch(() => JSON.stringify(filters.value), loadIds)

// Sélection multiple (UI-06) : vidée quand les filtres changent (elle ne correspondrait plus à la liste).
const selected = ref<number[]>([])
const selectedSet = computed(() => new Set(selected.value))
const notice = ref<string | null>(null)
const pageSelected = computed(() => documents.value.length > 0 && documents.value.every((doc) => selectedSet.value.has(doc.id)))
watch(
  () => JSON.stringify(filters.value),
  () => (selected.value = []),
)

function toggle(id: number) {
  selected.value = selectedSet.value.has(id) ? selected.value.filter((value) => value !== id) : [...selected.value, id]
}

function selectPage() {
  selected.value = [...new Set([...selected.value, ...documents.value.map((doc) => doc.id)])]
}

function togglePage() {
  if (pageSelected.value) {
    const page = new Set(documents.value.map((doc) => doc.id))
    selected.value = selected.value.filter((id) => !page.has(id))
  } else selectPage()
}

/** Tous les résultats : identifiants déjà chargés pour précédent / suivant, sinon redemandés. */
async function selectAll() {
  if (list.ids.length === count.value) {
    selected.value = [...list.ids]
    return
  }
  await loadIds()
  selected.value = list.ids.length ? [...list.ids] : selected.value
}

// Un document ajouté par l'utilisateur apparaît dans la liste dès que Paperless l'a traité.
const uploads = useUploadsStore()
watch(
  () => uploads.added,
  () => {
    reload()
    void loadIds()
  },
)

function applied(message: string, modified: boolean) {
  notice.value = message
  if (!modified) return
  selected.value = []
  reload()
  void loadIds()
}

// Vues sauvegardées (UI-08).
const savedViews = useSavedViewsStore()
const userId = computed(() => auth.user?.id ?? null)
const viewId = computed(() => {
  const value = Number(route.query.vue)
  return Number.isSafeInteger(value) && value > 0 ? value : null
})
const activeView = computed(() => (viewId.value === null ? null : savedViews.byId.get(viewId.value) ?? null))
const viewModified = computed(() => activeView.value !== null && differsFromView(filters.value, activeView.value, userId.value))
const unsupportedRules = computed(() =>
  activeView.value ? filtersFromView(activeView.value, userId.value).unsupported : [],
)
const canChangeView = computed(() => activeView.value?.user_can_change !== false)

const viewDialog = ref<'creation' | 'modification' | null>(null)
const confirmingDelete = ref(false)
const viewBusy = ref(false)
const viewError = ref<string | null>(null)

function openView(view: SavedView) {
  void router.push({ name: 'documents', query: viewQuery(view, userId.value) })
}

function viewSaved(view: SavedView) {
  const created = viewDialog.value === 'creation'
  viewDialog.value = null
  notice.value = created ? `Vue « ${view.name} » enregistrée.` : `Vue « ${view.name} » modifiée.`
  if (created) void router.replace({ query: { ...toQuery(filters.value), vue: String(view.id) } })
}

/** Remplace les critères et le tri de la vue ouverte par les filtres affichés. */
async function updateView() {
  const view = activeView.value
  if (!view) return
  viewBusy.value = true
  viewError.value = null
  try {
    const definition = viewFromFilters(filters.value, userId.value)
    await savedViews.update(view.id, {
      ...definition,
      // Critères que Home-GED n'affiche pas : conservés tels quels.
      filter_rules: [...definition.filter_rules, ...unsupportedRules.value],
    })
    notice.value = `Vue « ${view.name} » mise à jour.`
  } catch (e) {
    viewError.value = errorMessage(e)
  } finally {
    viewBusy.value = false
  }
}

async function deleteView() {
  const view = activeView.value
  if (!view) return
  viewBusy.value = true
  viewError.value = null
  try {
    await savedViews.remove(view.id)
    confirmingDelete.value = false
    notice.value = `Vue « ${view.name} » supprimée.`
    void router.replace({ query: toQuery(filters.value) })
  } catch (e) {
    confirmingDelete.value = false
    viewError.value = errorMessage(e)
  } finally {
    viewBusy.value = false
  }
}

function updateFilters(next: DocumentFilters) {
  // Nouveaux filtres : retour en haut de la première page (le paramètre page disparaît) ; la vue ouverte reste repérée.
  void router.replace({ query: { ...toQuery(next), ...(viewId.value ? { vue: String(viewId.value) } : {}) } })
}

function goToPage(target: number) {
  return router.push({ query: { ...route.query, page: target > 1 ? String(target) : undefined } })
}

/** Numéros affichés : première, dernière et voisines de la page courante. */
const pageNumbers = computed(() => {
  const numbers = new Set([1, pageCount.value, page.value - 1, page.value, page.value + 1])
  return [...numbers].filter((n) => n >= 1 && n <= pageCount.value).sort((a, b) => a - b)
})

// Défilement infini (mobile) : la sentinelle en bas de liste déclenche la page suivante.
const sentinel = useTemplateRef<HTMLElement>('sentinel')
let observer: IntersectionObserver | null = null
watch(sentinel, (element) => {
  observer?.disconnect()
  if (!element || typeof IntersectionObserver === 'undefined') return
  observer = new IntersectionObserver((entries) => {
    if (entries.some((entry) => entry.isIntersecting)) loadMore()
  }, { rootMargin: '400px' })
  observer.observe(element)
})
onBeforeUnmount(() => observer?.disconnect())

const tagsOf = (doc: DocumentSummary) =>
  doc.tags.map((id) => references.tags.get(id)).filter((tag) => tag !== undefined)
const nameOf = (map: Map<number, { name: string }>, id: number | null) => (id === null ? null : map.get(id)?.name ?? null)
const highlights = (doc: DocumentSummary) =>
  doc.__search_hit__?.highlights ? parseHighlights(doc.__search_hit__.highlights) : null

onMounted(() => {
  void references.ensureLoaded()
  void savedViews.ensureLoaded()
  reload()
  void loadIds()
})
</script>

<template>
  <div class="documents">
    <div class="entete">
      <h1 class="h3 mb-0 me-auto titre-liste">
        {{ activeView?.name ?? 'Documents' }}
        <span v-if="viewModified" class="badge text-bg-warning fw-normal fs-6">modifiée</span>
      </h1>
      <BaseDropdown button-class="btn btn-sm btn-outline-secondary" end aria-label="Vues sauvegardées">
        <template #toggle>Vues</template>
        <template #default="{ close }">
          <template v-if="savedViews.views.length">
            <h2 class="dropdown-header">Ouvrir une vue</h2>
            <button
              v-for="view in savedViews.views"
              :key="view.id"
              type="button"
              class="dropdown-item"
              :class="{ active: view.id === activeView?.id }"
              role="menuitem"
              @click="close(); openView(view)"
            >
              {{ view.name }}
            </button>
            <hr class="dropdown-divider" />
          </template>
          <button type="button" class="dropdown-item" role="menuitem" @click="close(); viewDialog = 'creation'">
            Enregistrer comme nouvelle vue…
          </button>
          <template v-if="activeView">
            <button
              v-if="canChangeView"
              type="button"
              class="dropdown-item"
              role="menuitem"
              :disabled="!viewModified || viewBusy"
              @click="close(); updateView()"
            >
              Mettre à jour « {{ activeView.name }} »
            </button>
            <button type="button" class="dropdown-item" role="menuitem" @click="close(); viewDialog = 'modification'">
              Modifier la vue…
            </button>
            <button
              v-if="canChangeView"
              type="button"
              class="dropdown-item text-danger"
              role="menuitem"
              @click="close(); confirmingDelete = true"
            >
              Supprimer la vue…
            </button>
          </template>
        </template>
      </BaseDropdown>
      <UploadButton button-class="btn btn-sm btn-primary" />
      <div class="btn-group" role="group" aria-label="Affichage">
        <button
          v-for="display in DISPLAYS"
          :key="display.value"
          type="button"
          class="btn btn-sm"
          :class="preferences.display === display.value ? 'btn-secondary' : 'btn-outline-secondary'"
          :aria-pressed="preferences.display === display.value"
          @click="preferences.display = display.value"
        >
          {{ display.label }}
        </button>
      </div>
    </div>

    <FilterBar :model-value="filters" @update:model-value="updateFilters" />

    <div v-if="unsupportedRules.length" class="alert alert-warning mb-0" role="status">
      Cette vue contient {{ unsupportedRules.length }} critère{{ unsupportedRules.length > 1 ? 's' : '' }} que Home-GED
      ne sait pas encore afficher : {{ unsupportedRules.length > 1 ? 'ils sont ignorés' : 'il est ignoré' }} dans cette liste,
      mais {{ unsupportedRules.length > 1 ? 'conservés' : 'conservé' }} si vous mettez la vue à jour.
    </div>

    <div v-if="viewError" class="alert alert-danger alert-dismissible mb-0" role="alert">
      {{ viewError }}
      <button type="button" class="btn-close" aria-label="Fermer" @click="viewError = null" />
    </div>

    <BulkActions
      v-if="selected.length && !mobile"
      :selected="selected"
      :count="count"
      @select-page="selectPage"
      @select-all="selectAll"
      @clear="selected = []"
      @applied="applied"
    />

    <div v-if="notice" class="alert alert-success alert-dismissible mb-0" role="status">
      {{ notice }}
      <button type="button" class="btn-close" aria-label="Fermer" @click="notice = null" />
    </div>

    <p class="compteur text-body-secondary" aria-live="polite">
      <template v-if="loadedPage > 0">{{ count }} document{{ count > 1 ? 's' : '' }}</template>
      <span v-if="loading" class="spinner-border spinner-border-sm ms-2" role="status">
        <span class="visually-hidden">Chargement…</span>
      </span>
    </p>

    <div v-if="error" class="alert alert-danger d-flex align-items-center gap-2" role="alert">
      <span class="me-auto">{{ error }}</span>
      <button type="button" class="btn btn-sm btn-outline-danger" @click="hasMore && mobile && documents.length ? loadMore() : reload()">
        Réessayer
      </button>
    </div>
    <OfflineDocuments v-if="unreachable && !documents.length" />

    <p v-if="loadedPage > 0 && !loading && !error && documents.length === 0" class="vide">
      Aucun document ne correspond à ces filtres.
    </p>

    <!-- Vignettes -->
    <ul v-if="preferences.display === 'vignettes'" class="vignettes list-unstyled">
      <li
        v-for="doc in documents"
        :key="doc.id"
        class="card h-100"
        :class="{ 'border-primary choisi': selectedSet.has(doc.id) }"
        :data-document="doc.id"
      >
        <label class="choix" @click.stop>
          <input
            type="checkbox"
            class="form-check-input"
            :checked="selectedSet.has(doc.id)"
            :aria-label="`Sélectionner « ${doc.title} »`"
            @change="toggle(doc.id)"
          />
        </label>
        <img class="card-img-top apercu" :src="thumbnailUrl(doc.id)" alt="" loading="lazy" />
        <div class="card-body p-2">
          <p class="card-title titre mb-1">
            <RouterLink :to="{ name: 'document', params: { id: doc.id } }" class="stretched-link lien">{{ doc.title }}</RouterLink>
          </p>
          <div class="etiquettes"><TagBadge v-for="tag in tagsOf(doc)" :key="tag.id" :tag="tag" /></div>
          <small class="text-body-secondary">{{ formatShortDate(doc.created) }}</small>
        </div>
      </li>
    </ul>

    <!-- Détails -->
    <ul v-else-if="preferences.display === 'details'" class="details list-unstyled">
      <li
        v-for="doc in documents"
        :key="doc.id"
        class="card"
        :class="{ 'border-primary choisi': selectedSet.has(doc.id) }"
        :data-document="doc.id"
      >
        <label class="choix" @click.stop>
          <input
            type="checkbox"
            class="form-check-input"
            :checked="selectedSet.has(doc.id)"
            :aria-label="`Sélectionner « ${doc.title} »`"
            @change="toggle(doc.id)"
          />
        </label>
        <div class="d-flex">
          <img class="apercu-details" :src="thumbnailUrl(doc.id)" alt="" loading="lazy" />
          <div class="card-body py-2">
            <p class="card-title titre mb-1">
              <span v-if="nameOf(references.correspondants, doc.correspondent)" class="text-body-secondary">
                {{ nameOf(references.correspondants, doc.correspondent) }} :
              </span>
              <RouterLink :to="{ name: 'document', params: { id: doc.id } }" class="stretched-link lien">{{ doc.title }}</RouterLink>
            </p>
            <div class="etiquettes mb-1"><TagBadge v-for="tag in tagsOf(doc)" :key="tag.id" :tag="tag" /></div>
            <p v-if="highlights(doc)" class="extrait small mb-1">
              <template v-for="(part, i) in highlights(doc)" :key="i">
                <mark v-if="part.match">{{ part.text }}</mark><template v-else>{{ part.text }}</template>
              </template>
            </p>
            <p v-else-if="doc.content" class="extrait small text-body-secondary mb-1">{{ doc.content }}</p>
            <small class="text-body-secondary meta">
              <span>{{ formatShortDate(doc.created) }}</span>
              <span v-if="nameOf(references.types, doc.document_type)">{{ nameOf(references.types, doc.document_type) }}</span>
              <span v-if="doc.archive_serial_number !== null">N° {{ doc.archive_serial_number }}</span>
            </small>
          </div>
        </div>
      </li>
    </ul>

    <!-- Tableau -->
    <div v-else class="table-responsive">
      <table class="table table-hover align-middle tableau">
        <thead>
          <tr>
            <th scope="col" class="colonne-choix">
              <input
                type="checkbox"
                class="form-check-input"
                :checked="pageSelected"
                :disabled="documents.length === 0"
                aria-label="Sélectionner les documents affichés"
                @change="togglePage"
              />
            </th>
            <th scope="col">N°</th>
            <th scope="col">Correspondant</th>
            <th scope="col">Titre</th>
            <th scope="col">Étiquettes</th>
            <th scope="col">Type</th>
            <th scope="col">Créé le</th>
            <th scope="col">Ajouté le</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="doc in documents"
            :key="doc.id"
            :class="{ 'table-active': selectedSet.has(doc.id) }"
            :data-document="doc.id"
          >
            <td class="colonne-choix">
              <input
                type="checkbox"
                class="form-check-input"
                :checked="selectedSet.has(doc.id)"
                :aria-label="`Sélectionner « ${doc.title} »`"
                @change="toggle(doc.id)"
              />
            </td>
            <td>{{ doc.archive_serial_number ?? '' }}</td>
            <td>{{ nameOf(references.correspondants, doc.correspondent) ?? '' }}</td>
            <td class="titre">
              <RouterLink :to="{ name: 'document', params: { id: doc.id } }" class="lien">{{ doc.title }}</RouterLink>
            </td>
            <td><div class="etiquettes"><TagBadge v-for="tag in tagsOf(doc)" :key="tag.id" :tag="tag" /></div></td>
            <td>{{ nameOf(references.types, doc.document_type) ?? '' }}</td>
            <td class="text-nowrap">{{ formatShortDate(doc.created) }}</td>
            <td class="text-nowrap">{{ formatShortDate(doc.added) }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Mobile : défilement infini (bouton de secours si l'observateur n'est pas disponible). -->
    <div v-if="mobile && hasMore && !error" ref="sentinel" class="suite">
      <button type="button" class="btn btn-outline-secondary" :disabled="loading" @click="loadMore">Afficher plus</button>
    </div>

    <!-- Mobile : barre de la sélection en bas de l'écran, à portée de pouce. -->
    <BulkActions
      v-if="selected.length && mobile"
      bottom
      :selected="selected"
      :count="count"
      @select-page="selectPage"
      @select-all="selectAll"
      @clear="selected = []"
      @applied="applied"
    />

    <SavedViewDialog
      :open="viewDialog !== null"
      :view="viewDialog === 'modification' ? activeView : null"
      :definition="viewDialog === 'creation' ? viewFromFilters(filters, userId) : undefined"
      @saved="viewSaved"
      @cancel="viewDialog = null"
    />

    <ConfirmDialog
      :open="confirmingDelete"
      title="Supprimer la vue"
      confirm-label="Supprimer"
      danger
      :busy="viewBusy"
      @confirm="deleteView"
      @cancel="confirmingDelete = false"
    >
      <p class="mb-0">
        Supprimer la vue « {{ activeView?.name }} » ? Elle disparaît aussi de Paperless, pour tous ceux qui y ont accès.
        Aucun document n'est supprimé.
      </p>
    </ConfirmDialog>

    <!-- PC : pagination. -->
    <nav v-if="!mobile && pageCount > 1" aria-label="Pagination">
      <ul class="pagination justify-content-center flex-wrap">
        <li class="page-item" :class="{ disabled: page <= 1 }">
          <button type="button" class="page-link" :disabled="page <= 1" @click="goToPage(page - 1)">Précédente</button>
        </li>
        <template v-for="(n, i) in pageNumbers" :key="n">
          <li v-if="i > 0 && n - pageNumbers[i - 1]! > 1" class="page-item disabled"><span class="page-link">…</span></li>
          <li class="page-item" :class="{ active: n === page }">
            <button type="button" class="page-link" :aria-current="n === page ? 'page' : undefined" @click="goToPage(n)">
              {{ n }}
            </button>
          </li>
        </template>
        <li class="page-item" :class="{ disabled: page >= pageCount }">
          <button type="button" class="page-link" :disabled="page >= pageCount" @click="goToPage(page + 1)">Suivante</button>
        </li>
      </ul>
    </nav>
  </div>
</template>

<style scoped>
.documents {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.entete {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.titre-liste {
  overflow-wrap: anywhere;
}

.compteur {
  margin: 0;
  min-height: 1.5rem;
}

.vide {
  padding: 2rem 0;
  text-align: center;
}

.titre {
  font-weight: 600;
  overflow-wrap: anywhere;
}

.lien {
  color: inherit;
  text-decoration: none;
}

.lien:hover,
.lien:focus-visible {
  text-decoration: underline;
}

.etiquettes {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

/* Case de sélection au-dessus du lien étiré de la carte, zone tactile de 44 px. */
.choix {
  position: absolute;
  top: 0;
  left: 0;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2.75rem;
  height: 2.75rem;
  cursor: pointer;
}

.choix .form-check-input {
  margin: 0;
  width: 1.25rem;
  height: 1.25rem;
  box-shadow: 0 0 0 2px var(--bs-body-bg);
}

.choisi {
  box-shadow: 0 0 0 1px var(--bs-primary);
}

.colonne-choix {
  width: 2.5rem;
}

.vignettes {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
  gap: 0.75rem;
  margin: 0;
}

.apercu {
  aspect-ratio: 3 / 4;
  object-fit: cover;
  object-position: top;
  background: var(--bs-secondary-bg);
}

.details {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin: 0;
}

.apercu-details {
  flex: none;
  width: 5.5rem;
  aspect-ratio: 3 / 4;
  object-fit: cover;
  object-position: top;
  border-radius: var(--bs-card-inner-border-radius) 0 0 var(--bs-card-inner-border-radius);
  background: var(--bs-secondary-bg);
}

.extrait {
  display: -webkit-box;
  overflow: hidden;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  overflow-wrap: anywhere;
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0 1rem;
}

.tableau .titre {
  min-width: 12rem;
}

.suite {
  display: flex;
  justify-content: center;
  padding: 1rem 0;
}

@media (min-width: 768px) {
  .apercu-details {
    width: 7rem;
  }
}
</style>
