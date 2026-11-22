<script setup lang="ts">
import { onMounted, ref, useId, watch } from 'vue'
import type { LocationQueryRaw } from 'vue-router'
import { listDocuments, type DocumentSummary } from '../api/documents'
import { ApiError } from '../api/http'
import { toApiParams, type DocumentFilters } from '../filters/documentFilters'
import { formatShortDate } from '../format/dates'
import { useAuthStore } from '../stores/auth'
import { useReferencesStore } from '../stores/references'
import TagBadge from './TagBadge.vue'

/**
 * Widget du tableau de bord (UI-09) : premiers documents d'une vue sauvegardée ou des derniers
 * ajouts, avec un lien vers la liste complète.
 */
const props = withDefaults(
  defineProps<{
    title: string
    filters: DocumentFilters
    /** Adresse de la liste complète (paramètres de la page Documents). */
    query: LocationQueryRaw
    /** Date affichée : celle du document ou celle de l'ajout. */
    date?: 'created' | 'added'
    size?: number
  }>(),
  { date: 'created', size: 5 },
)

const auth = useAuthStore()
const references = useReferencesStore()
const documents = ref<DocumentSummary[]>([])
const count = ref<number | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
let generation = 0

async function load() {
  const current = ++generation
  loading.value = true
  error.value = null
  try {
    const page = await listDocuments(
      toApiParams(props.filters, { page: 1, pageSize: props.size, userId: auth.user?.id ?? null }),
    )
    if (current !== generation) return
    documents.value = page.results
    count.value = page.count
  } catch (e) {
    if (current !== generation) return
    error.value = e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED.'
  } finally {
    if (current === generation) loading.value = false
  }
}

onMounted(load)
watch(() => JSON.stringify(props.filters), load)

const correspondent = (doc: DocumentSummary) =>
  doc.correspondent === null ? null : references.correspondants.get(doc.correspondent)?.name ?? null
const tagsOf = (doc: DocumentSummary) => doc.tags.map((id) => references.tags.get(id)).filter((tag) => tag !== undefined)
const headingId = useId()
</script>

<template>
  <section class="card widget" :aria-labelledby="headingId">
    <div class="card-header d-flex align-items-center gap-2">
      <h2 :id="headingId" class="h6 mb-0 me-auto">{{ title }}</h2>
      <span v-if="loading" class="spinner-border spinner-border-sm" role="status">
        <span class="visually-hidden">Chargement…</span>
      </span>
      <RouterLink class="btn btn-sm btn-link p-0" :to="{ name: 'documents', query }">
        Tout voir<template v-if="count !== null"> ({{ count }})</template>
      </RouterLink>
    </div>
    <div v-if="error" class="card-body">
      <p class="text-danger mb-2">{{ error }}</p>
      <button type="button" class="btn btn-sm btn-outline-secondary" @click="load">Réessayer</button>
    </div>
    <p v-else-if="count === 0" class="card-body text-body-secondary mb-0">Aucun document.</p>
    <ul v-else class="list-group list-group-flush">
      <li v-for="doc in documents" :key="doc.id" class="list-group-item ligne">
        <div class="texte">
          <RouterLink :to="{ name: 'document', params: { id: doc.id } }" class="lien">{{ doc.title }}</RouterLink>
          <small class="d-block text-body-secondary">
            <template v-if="correspondent(doc)">{{ correspondent(doc) }} · </template>{{ formatShortDate(date === 'added' ? doc.added : doc.created) }}
          </small>
        </div>
        <div class="etiquettes"><TagBadge v-for="tag in tagsOf(doc)" :key="tag.id" :tag="tag" /></div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.ligne {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.25rem 0.5rem;
  min-height: 2.75rem;
}

.texte {
  flex: 1 1 12rem;
  min-width: 0;
}

.lien {
  font-weight: 600;
  color: inherit;
  text-decoration: none;
  overflow-wrap: anywhere;
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
</style>
