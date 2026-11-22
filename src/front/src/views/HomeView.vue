<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getAppInfo } from '../api/http'
import { getDeadlines, getStatistics, type Deadlines, type Statistics } from '../api/savedViews'
import DocumentsWidget from '../components/DocumentsWidget.vue'
import TagBadge from '../components/TagBadge.vue'
import UploadButton from '../components/UploadButton.vue'
import { defaultFilters, toQuery } from '../filters/documentFilters'
import { filtersFromView, viewQuery } from '../filters/savedViews'
import { formatRelative, formatShortDate } from '../format/dates'
import { useAuthStore } from '../stores/auth'
import { useReferencesStore } from '../stores/references'
import { useSavedViewsStore } from '../stores/savedViews'

/**
 * Tableau de bord (UI-09) : dépôt de fichiers, vues sauvegardées choisies pour le tableau de bord,
 * derniers ajouts, échéances à venir (champ personnalisé « Échéance ») et statistiques.
 */
const DEADLINES = 10

const auth = useAuthStore()
const references = useReferencesStore()
const savedViews = useSavedViewsStore()
const userId = computed(() => auth.user?.id ?? null)

const version = ref<string | null>(null)
const statistics = ref<Statistics | null>(null)
const statisticsError = ref(false)
const deadlines = ref<Deadlines | null>(null)
const deadlinesError = ref(false)

const recentFilters = { ...defaultFilters(), tri: 'added' as const }
const recentQuery = toQuery(recentFilters)

const dashboardViews = computed(() =>
  savedViews.dashboard.map((view) => ({
    view,
    filters: filtersFromView(view, userId.value).filters,
    query: viewQuery(view, userId.value),
  })),
)

/** Documents de la boîte de réception (étiquettes « boîte de réception » de Paperless). */
const inboxQuery = computed(() =>
  statistics.value?.inbox_tags?.length
    ? toQuery({ ...defaultFilters(), tags: statistics.value.inbox_tags, tagsMode: 'un' })
    : null,
)

const counters = computed(() =>
  statistics.value
    ? [
        { label: 'Étiquettes', value: statistics.value.tag_count, type: 'tags' },
        { label: 'Correspondants', value: statistics.value.correspondent_count, type: 'correspondants' },
        { label: 'Types de documents', value: statistics.value.document_type_count, type: 'types' },
        { label: 'Chemins de stockage', value: statistics.value.storage_path_count, type: 'chemins' },
      ]
    : [],
)

const number = new Intl.NumberFormat('fr-FR')

/** Échéance lue en heure locale (date seule) pour le délai relatif. */
function until(date: string) {
  const [y, m, d] = date.split('-').map(Number)
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const target = new Date(y!, m! - 1, d!)
  return target.getTime() === today.getTime() ? "aujourd'hui" : formatRelative(target, today)
}

const deadlineTags = (ids: number[]) => ids.map((id) => references.tags.get(id)).filter((tag) => tag !== undefined)
const correspondent = (id: number | null) => (id === null ? null : references.correspondants.get(id)?.name ?? null)

async function loadStatistics() {
  statisticsError.value = false
  try {
    statistics.value = await getStatistics()
  } catch {
    statisticsError.value = true
  }
}

async function loadDeadlines() {
  deadlinesError.value = false
  try {
    deadlines.value = await getDeadlines(DEADLINES)
  } catch {
    deadlinesError.value = true
  }
}

onMounted(async () => {
  void references.ensureLoaded()
  void savedViews.ensureLoaded()
  void loadStatistics()
  void loadDeadlines()
  try {
    version.value = (await getAppInfo()).version
  } catch {
    version.value = null
  }
})
</script>

<template>
  <div class="accueil">
    <h1 class="h3 mb-0">Tableau de bord</h1>

    <div class="widgets">
      <!-- Dépôt de fichiers : le glisser-déposer fonctionne partout ; ce widget le rappelle et ouvre le sélecteur. -->
      <section class="card widget depot" aria-labelledby="widget-depot">
        <div class="card-body d-flex flex-column align-items-start gap-2">
          <h2 id="widget-depot" class="h6 mb-0">Ajouter des documents</h2>
          <p class="text-body-secondary small mb-0">
            Choisissez des fichiers sur l'appareil, ou glissez-les n'importe où dans Home-GED sur ordinateur.
            Paperless se charge de l'OCR et du classement.
          </p>
          <UploadButton />
        </div>
      </section>

      <DocumentsWidget
        v-for="item in dashboardViews"
        :key="item.view.id"
        :title="item.view.name"
        :filters="item.filters"
        :query="item.query"
      />

      <DocumentsWidget title="Derniers ajouts" :filters="recentFilters" :query="recentQuery" date="added" />

      <section class="card widget" aria-labelledby="widget-echeances">
        <div class="card-header">
          <h2 id="widget-echeances" class="h6 mb-0">Échéances à venir</h2>
        </div>
        <div v-if="deadlinesError" class="card-body">
          <p class="text-danger mb-2">Les échéances n'ont pas pu être chargées.</p>
          <button type="button" class="btn btn-sm btn-outline-secondary" @click="loadDeadlines">Réessayer</button>
        </div>
        <p v-else-if="deadlines && !deadlines.champ" class="card-body text-body-secondary small mb-0">
          Pour suivre vos échéances ici, créez dans Paperless un champ personnalisé de type date nommé « Échéance ».
        </p>
        <p v-else-if="deadlines && deadlines.count === 0" class="card-body text-body-secondary mb-0">Aucune échéance à venir.</p>
        <ul v-else-if="deadlines" class="list-group list-group-flush">
          <li v-for="item in deadlines.results" :key="item.id" class="list-group-item ligne">
            <div class="texte">
              <RouterLink :to="{ name: 'document', params: { id: item.id } }" class="lien">{{ item.title }}</RouterLink>
              <small v-if="correspondent(item.correspondent)" class="d-block text-body-secondary">
                {{ correspondent(item.correspondent) }}
              </small>
              <div class="etiquettes"><TagBadge v-for="tag in deadlineTags(item.tags)" :key="tag.id" :tag="tag" /></div>
            </div>
            <div v-if="item.echeance" class="text-end">
              <strong class="d-block">{{ formatShortDate(item.echeance) }}</strong>
              <small class="text-body-secondary">{{ until(item.echeance) }}</small>
            </div>
          </li>
          <li v-if="deadlines.count > deadlines.results.length" class="list-group-item text-body-secondary small">
            Et {{ deadlines.count - deadlines.results.length }} autre{{ deadlines.count - deadlines.results.length > 1 ? 's' : '' }}
            plus tard.
          </li>
        </ul>
      </section>

      <section class="card widget" aria-labelledby="widget-statistiques">
        <div class="card-header">
          <h2 id="widget-statistiques" class="h6 mb-0">Statistiques</h2>
        </div>
        <div v-if="statisticsError" class="card-body">
          <p class="text-danger mb-2">Les statistiques n'ont pas pu être chargées.</p>
          <button type="button" class="btn btn-sm btn-outline-secondary" @click="loadStatistics">Réessayer</button>
        </div>
        <div v-else-if="statistics" class="card-body">
          <div class="chiffres-cles">
            <RouterLink class="chiffre lien" :to="{ name: 'documents' }">
              <strong>{{ number.format(statistics.documents_total) }}</strong>
              <span>document{{ statistics.documents_total > 1 ? 's' : '' }}</span>
            </RouterLink>
            <RouterLink v-if="inboxQuery && statistics.documents_inbox !== null" class="chiffre lien" :to="{ name: 'documents', query: inboxQuery }">
              <strong>{{ number.format(statistics.documents_inbox) }}</strong>
              <span>dans la boîte de réception</span>
            </RouterLink>
          </div>
          <dl class="compteurs mb-0">
            <div v-for="counter in counters" :key="counter.type">
              <dt>
                <RouterLink :to="{ name: 'gerer', params: { type: counter.type } }">{{ counter.label }}</RouterLink>
              </dt>
              <dd class="mb-0">{{ number.format(counter.value) }}</dd>
            </div>
          </dl>
        </div>
      </section>
    </div>

    <p v-if="!savedViews.dashboard.length && !savedViews.error" class="text-body-secondary small mb-0">
      Astuce : depuis la liste des documents, enregistrez vos filtres comme vue (menu « Vues ») et choisissez
      « Afficher sur le tableau de bord » pour la retrouver ici.
    </p>
    <p v-if="savedViews.error" class="text-danger small mb-0">{{ savedViews.error }}</p>
    <p v-if="version" class="version text-body-secondary">Home-GED, version {{ version }}</p>
  </div>
</template>

<style scoped>
.accueil {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

/* Une colonne sur mobile, plusieurs dès que la largeur le permet. */
.widgets {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 22rem), 1fr));
  gap: 1rem;
  align-items: start;
}

.depot {
  border-style: dashed;
  border-width: 2px;
}

.ligne {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  min-height: 2.75rem;
}

.texte {
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

.chiffres-cles {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem 2rem;
  margin-bottom: 1rem;
}

.chiffre {
  display: flex;
  flex-direction: column;
  font-weight: normal;
}

.chiffre strong {
  font-size: 1.75rem;
  line-height: 1.2;
}

.compteurs {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.5rem 1rem;
}

.compteurs dt {
  font-weight: normal;
}

.compteurs dd {
  font-size: 1.25rem;
  font-weight: 600;
}

.version {
  margin-top: 1rem;
  font-size: 0.8rem;
}
</style>
