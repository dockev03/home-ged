<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { listDocuments, thumbnailUrl, type DocumentSummary } from '../api/documents'
import { ApiError } from '../api/http'
import FilterBar from '../components/FilterBar.vue'
import TagBadge from '../components/TagBadge.vue'
import { MOBILE_QUERY, useMediaQuery } from '../composables/mediaQuery'
import { fromQuery, toApiParams, toQuery, type DocumentFilters } from '../filters/documentFilters'
import { formatShortDate } from '../format/dates'
import { parseHighlights } from '../format/highlights'
import { useAuthStore } from '../stores/auth'
import { usePreferencesStore, type DisplayChoice } from '../stores/preferences'
import { useReferencesStore } from '../stores/references'

/** Liste des documents (UI-02) : vignettes, détails ou tableau ; filtres dans l'URL (UI-03). */
const PAGE_SIZE = 25

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const preferences = usePreferencesStore()
const references = useReferencesStore()
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
  try {
    const result = await listDocuments(
      toApiParams(filters.value, { page: target, pageSize: PAGE_SIZE, userId: auth.user?.id ?? null }),
    )
    if (current !== generation) return
    documents.value = append ? [...documents.value, ...result.results] : result.results
    count.value = result.count
    loadedPage.value = target
  } catch (e) {
    if (current !== generation) return
    // Page hors limites (documents supprimés entre-temps) : retour à la première.
    if (e instanceof ApiError && e.status === 404 && target > 1 && !append) {
      void goToPage(1)
      return
    }
    error.value = errorMessage(e)
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

function updateFilters(next: DocumentFilters) {
  // Nouveaux filtres : retour en haut de la première page (le paramètre page disparaît).
  void router.replace({ query: toQuery(next) })
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
  reload()
})
</script>

<template>
  <div class="documents">
    <div class="entete">
      <h1 class="h3 mb-0">Documents</h1>
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

    <p v-if="loadedPage > 0 && !loading && !error && documents.length === 0" class="vide">
      Aucun document ne correspond à ces filtres.
    </p>

    <!-- Vignettes -->
    <ul v-if="preferences.display === 'vignettes'" class="vignettes list-unstyled">
      <li v-for="doc in documents" :key="doc.id" class="card h-100" :data-document="doc.id">
        <img class="card-img-top apercu" :src="thumbnailUrl(doc.id)" alt="" loading="lazy" />
        <div class="card-body p-2">
          <p class="card-title titre mb-1">{{ doc.title }}</p>
          <div class="etiquettes"><TagBadge v-for="tag in tagsOf(doc)" :key="tag.id" :tag="tag" /></div>
          <small class="text-body-secondary">{{ formatShortDate(doc.created) }}</small>
        </div>
      </li>
    </ul>

    <!-- Détails -->
    <ul v-else-if="preferences.display === 'details'" class="details list-unstyled">
      <li v-for="doc in documents" :key="doc.id" class="card" :data-document="doc.id">
        <div class="d-flex">
          <img class="apercu-details" :src="thumbnailUrl(doc.id)" alt="" loading="lazy" />
          <div class="card-body py-2">
            <p class="card-title titre mb-1">
              <span v-if="nameOf(references.correspondants, doc.correspondent)" class="text-body-secondary">
                {{ nameOf(references.correspondants, doc.correspondent) }} :
              </span>
              {{ doc.title }}
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
          <tr v-for="doc in documents" :key="doc.id" :data-document="doc.id">
            <td>{{ doc.archive_serial_number ?? '' }}</td>
            <td>{{ nameOf(references.correspondants, doc.correspondent) ?? '' }}</td>
            <td class="titre">{{ doc.title }}</td>
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

.etiquettes {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
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
