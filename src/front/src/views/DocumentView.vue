<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, toRaw, watch } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter, type RouteLocationRaw } from 'vue-router'
import {
  deleteDocument,
  downloadUrl,
  getDocument,
  getSuggestions,
  previewUrl,
  thumbnailUrl,
  updateDocument,
  type DocumentDetail,
  type DocumentSuggestions,
  type Note,
} from '../api/documents'
import { ApiError } from '../api/http'
import BaseDropdown from '../components/BaseDropdown.vue'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import CustomFieldsEditor from '../components/CustomFieldsEditor.vue'
import DocumentHistory from '../components/DocumentHistory.vue'
import DocumentMetadataPanel from '../components/DocumentMetadataPanel.vue'
import DocumentNotes from '../components/DocumentNotes.vue'
import DocumentPermissions from '../components/DocumentPermissions.vue'
import DocumentVersions from '../components/DocumentVersions.vue'
import ReferenceSelect from '../components/ReferenceSelect.vue'
import ShareDialog from '../components/ShareDialog.vue'
import TagPicker from '../components/TagPicker.vue'
import { MOBILE_QUERY, useMediaQuery } from '../composables/mediaQuery'
import { formatDate } from '../format/dates'
import { diff, isDirty, toForm, validate, type DocumentForm } from '../documents/form'
import { useDocumentListStore } from '../stores/documentList'
import { useOpenDocumentsStore } from '../stores/openDocuments'
import { useReferencesStore } from '../stores/references'

/** Fiche document (UI-04) et modification de ses détails (UI-05). */
const route = useRoute()
const router = useRouter()
const list = useDocumentListStore()
const references = useReferencesStore()
const openDocuments = useOpenDocumentsStore()
const mobile = useMediaQuery(MOBILE_QUERY)

type Tab = 'details' | 'contenu' | 'metadonnees' | 'notes' | 'historique' | 'permissions' | 'versions'
const TABS: { value: Tab; label: string }[] = [
  { value: 'details', label: 'Détails' },
  { value: 'contenu', label: 'Contenu' },
  { value: 'metadonnees', label: 'Métadonnées' },
  { value: 'notes', label: 'Notes' },
  { value: 'historique', label: 'Historique' },
  { value: 'permissions', label: 'Permissions' },
  { value: 'versions', label: 'Versions' },
]

const id = computed(() => Number(route.params.id))
const doc = ref<DocumentDetail | null>(null)
const original = ref<DocumentForm | null>(null)
const form = ref<DocumentForm | null>(null)
const notes = ref<Note[]>([])
const tab = ref<Tab>('details')
const loadError = ref<string | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)
const saved = ref(false)
/** Version affichée dans l'aperçu ; null = la version actuelle. */
const previewVersion = ref<number | null>(null)

const canEdit = computed(() => doc.value?.user_can_change !== false)
const dirty = computed(() => !!original.value && !!form.value && isDirty(original.value, form.value))
const neighbours = computed(() => list.neighbours(id.value))
const listRoute = computed<RouteLocationRaw>(() => ({ name: 'documents', query: list.query }))

const errorMessage = (e: unknown) =>
  e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED. Vérifiez votre connexion.'

function show(document: DocumentDetail) {
  doc.value = document
  openDocuments.open({ id: document.id, title: document.title })
  original.value = toForm(document)
  form.value = toForm(document)
}

async function load() {
  const requested = id.value
  doc.value = null
  form.value = null
  original.value = null
  loadError.value = null
  saveError.value = null
  saved.value = false
  previewVersion.value = null
  suggestions.value = null
  suggestError.value = null
  try {
    const document = await getDocument(requested)
    if (requested !== id.value) return
    show(document)
    notes.value = document.notes ?? []
  } catch (e) {
    if (requested !== id.value) return
    if (e instanceof ApiError && e.status === 404) openDocuments.close(requested)
    loadError.value = e instanceof ApiError && e.status === 404 ? 'Ce document n’existe pas ou ne vous est pas accessible.' : errorMessage(e)
  }
}

watch(id, () => {
  sharing.value = false
  void load()
})
onMounted(() => {
  void references.ensureLoaded()
  void load()
})

/**
 * Rechargement après un changement de versions : le document (versions, contenu) est relu, le
 * formulaire n'est remplacé que s'il n'a pas de modification en cours.
 */
async function refresh() {
  if (!doc.value) return
  try {
    const document = await getDocument(doc.value.id)
    if (document.id !== id.value) return
    if (dirty.value) doc.value = document
    else show(document)
  } catch (e) {
    saveError.value = errorMessage(e)
  }
}

function cancel() {
  if (original.value) form.value = structuredClone(toRaw(original.value))
  saveError.value = null
}

async function save(close: boolean) {
  if (!doc.value || !form.value || !original.value || saving.value) return
  saveError.value = validate(form.value)
  saved.value = false
  if (saveError.value) return
  const update = diff(original.value, form.value)
  saving.value = true
  try {
    if (Object.keys(update).length > 0) show(await updateDocument(doc.value.id, update))
    if (close) await leave(listRoute.value)
    else saved.value = true
  } catch (e) {
    saveError.value = errorMessage(e)
  } finally {
    saving.value = false
  }
}

// Modifications non enregistrées : confirmation avant de quitter la fiche ou de changer de document.
const pendingLeave = ref<RouteLocationRaw | null>(null)
let bypassGuard = false

async function leave(target: RouteLocationRaw) {
  bypassGuard = true
  try {
    await router.push(target)
  } finally {
    bypassGuard = false
  }
}

function guard(to: RouteLocationRaw) {
  if (bypassGuard || !dirty.value) return true
  pendingLeave.value = to
  return false
}
onBeforeRouteLeave((to) => guard(to.fullPath))
onBeforeRouteUpdate((to) => guard(to.fullPath))

async function confirmLeave() {
  const target = pendingLeave.value
  pendingLeave.value = null
  if (target) await leave(target)
}

function onBeforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value) event.preventDefault()
}
window.addEventListener('beforeunload', onBeforeUnload)
onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload))

// Suppression : Paperless place le document dans sa corbeille.
const confirmingDelete = ref(false)
/** Fenêtre de création d'un lien de partage protégé (F-SHARE). */
const sharing = ref(false)
const deleting = ref(false)

async function remove() {
  if (!doc.value) return
  deleting.value = true
  try {
    await deleteDocument(doc.value.id)
    list.forget(doc.value.id)
    openDocuments.close(doc.value.id)
    confirmingDelete.value = false
    await leave(listRoute.value)
  } catch (e) {
    confirmingDelete.value = false
    saveError.value = errorMessage(e)
  } finally {
    deleting.value = false
  }
}

// Suggestions de classement de Paperless (UI-12) : appliquées au formulaire, pas enregistrées.
const suggestions = ref<DocumentSuggestions | null>(null)
const suggesting = ref(false)
const suggestError = ref<string | null>(null)

async function suggest() {
  if (!doc.value || suggesting.value) return
  const requested = doc.value.id
  suggesting.value = true
  suggestError.value = null
  try {
    const result = await getSuggestions(requested)
    if (requested === id.value) suggestions.value = result
  } catch (e) {
    if (requested === id.value) suggestError.value = errorMessage(e)
  } finally {
    suggesting.value = false
  }
}

interface Suggestion {
  key: string
  label: string
  apply: () => void
}

/** Suggestions pas encore présentes dans le formulaire, avec leur nom (éléments connus seulement). */
const suggestionItems = computed<Suggestion[]>(() => {
  const s = suggestions.value
  const f = form.value
  if (!s || !f) return []
  const items: Suggestion[] = []
  for (const tagId of s.tags) {
    const tag = references.tags.get(tagId)
    if (tag && !f.tags.includes(tagId))
      items.push({ key: `t${tagId}`, label: `Étiquette : ${tag.name}`, apply: () => f.tags.push(tagId) })
  }
  const single = (
    ids: number[],
    names: Map<number, { name: string }>,
    field: 'correspondent' | 'document_type' | 'storage_path',
    prefix: string,
  ) => {
    for (const refId of ids) {
      const item = names.get(refId)
      if (item && f[field] !== refId)
        items.push({ key: `${field}${refId}`, label: `${prefix} : ${item.name}`, apply: () => (f[field] = refId) })
    }
  }
  single(s.correspondents, references.correspondants, 'correspondent', 'Correspondant')
  single(s.document_types, references.types, 'document_type', 'Type')
  single(s.storage_paths, references.chemins, 'storage_path', 'Chemin')
  for (const date of s.dates) {
    if (f.created !== date)
      items.push({ key: `d${date}`, label: `Date : ${formatDate(date)}`, apply: () => (f.created = date) })
  }
  return items
})

/** Entrée dans un champ de saisie des détails : enregistre (sauf dans un formulaire de création « + »). */
function onEnter(event: KeyboardEvent) {
  const target = event.target
  if (!(target instanceof HTMLInputElement) || target.type === 'checkbox' || target.closest('form')) return
  event.preventDefault()
  void save(false)
}

const goTo = (target: number | null) => target !== null && router.push({ name: 'document', params: { id: target } })
</script>

<template>
  <div class="fiche">
    <div v-if="loadError" class="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
      <span class="me-auto">{{ loadError }}</span>
      <button type="button" class="btn btn-sm btn-outline-danger" @click="load">Réessayer</button>
      <RouterLink :to="listRoute" class="btn btn-sm btn-outline-secondary">Retour à la liste</RouterLink>
    </div>

    <template v-else-if="doc && form">
      <header class="entete">
        <RouterLink :to="listRoute" class="retour">← Documents</RouterLink>
        <h1 class="h4 mb-0 titre">
          {{ doc.title }} <small class="text-body-secondary fw-normal">n° {{ doc.id }}</small>
        </h1>
        <div class="outils">
          <div v-if="neighbours.position" class="btn-group" role="group" aria-label="Navigation dans la liste">
            <button
              type="button"
              class="btn btn-outline-secondary"
              :disabled="neighbours.previous === null"
              aria-label="Document précédent"
              @click="goTo(neighbours.previous)"
            >
              ‹
            </button>
            <span class="btn btn-outline-secondary disabled position">
              {{ neighbours.position.index }} / {{ neighbours.position.total }}
            </span>
            <button
              type="button"
              class="btn btn-outline-secondary"
              :disabled="neighbours.next === null"
              aria-label="Document suivant"
              @click="goTo(neighbours.next)"
            >
              ›
            </button>
          </div>
          <BaseDropdown end button-class="btn btn-outline-secondary">
            <template #toggle>Télécharger</template>
            <a v-if="doc.archived_file_name" class="dropdown-item" role="menuitem" :href="downloadUrl(doc.id)" download>
              Version archivée (PDF/A)
            </a>
            <a class="dropdown-item" role="menuitem" :href="downloadUrl(doc.id, true)" download>Fichier original</a>
          </BaseDropdown>
          <button type="button" class="btn btn-outline-secondary" @click="sharing = true">Envoyer</button>
          <button v-if="canEdit" type="button" class="btn btn-outline-danger" @click="confirmingDelete = true">
            Supprimer
          </button>
        </div>
      </header>

      <div class="corps">
        <section class="apercu" aria-label="Aperçu">
          <template v-if="mobile">
            <a :href="previewUrl(doc.id, previewVersion)" target="_blank" rel="noopener" class="apercu-mobile">
              <img :src="thumbnailUrl(doc.id)" alt="" />
              <span class="btn btn-outline-primary">Ouvrir l'aperçu</span>
            </a>
          </template>
          <iframe v-else :key="`${doc.id}-${previewVersion}`" :src="previewUrl(doc.id, previewVersion)" :title="`Aperçu de ${doc.title}`" class="cadre" />
        </section>

        <section class="onglets">
          <ul class="nav nav-tabs flex-nowrap flex-lg-wrap mb-3 onglets-liste" role="tablist">
            <li v-for="t in TABS" :key="t.value" class="nav-item" role="presentation">
              <button
                :id="`onglet-${t.value}`"
                type="button"
                class="nav-link"
                :class="{ active: tab === t.value }"
                role="tab"
                :aria-selected="tab === t.value"
                :aria-controls="`panneau-${t.value}`"
                @click="tab = t.value"
              >
                {{ t.label }}<span v-if="t.value === 'notes' && notes.length" class="badge text-bg-secondary ms-1">{{ notes.length }}</span>
              </button>
            </li>
          </ul>

          <div :id="`panneau-${tab}`" role="tabpanel" :aria-labelledby="`onglet-${tab}`">
            <div v-if="tab === 'details'" class="details" @keydown.enter="onEnter">
              <p v-if="!canEdit" class="alert alert-info">Vous pouvez consulter ce document, pas le modifier.</p>
              <div v-else class="span-2 suggestions">
                <button type="button" class="btn btn-sm btn-outline-secondary" :disabled="suggesting" @click="suggest">
                  <span v-if="suggesting" class="spinner-border spinner-border-sm me-1" aria-hidden="true" />
                  Suggérer
                </button>
                <span v-if="suggestError" class="text-danger small" role="alert">{{ suggestError }}</span>
                <template v-else-if="suggestions">
                  <button
                    v-for="item in suggestionItems"
                    :key="item.key"
                    type="button"
                    class="btn btn-sm btn-outline-primary suggestion"
                    :title="`Appliquer ${item.label}`"
                    @click="item.apply"
                  >
                    + {{ item.label }}
                  </button>
                  <span v-if="!suggestionItems.length" class="text-body-secondary small" role="status">
                    Aucune autre suggestion pour ce document.
                  </span>
                </template>
              </div>
              <div class="span-2">
                <label class="form-label" for="titre">Titre</label>
                <input id="titre" v-model="form.title" class="form-control" maxlength="128" required :disabled="!canEdit" />
              </div>
              <div>
                <label class="form-label" for="numero">Numéro d'archive</label>
                <input
                  id="numero"
                  type="number"
                  min="0"
                  step="1"
                  inputmode="numeric"
                  class="form-control"
                  :value="form.archive_serial_number ?? ''"
                  :disabled="!canEdit"
                  @input="form.archive_serial_number = ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value)"
                />
              </div>
              <div>
                <label class="form-label" for="date">Date du document</label>
                <input id="date" v-model="form.created" type="date" class="form-control" required :disabled="!canEdit" />
              </div>
              <ReferenceSelect id="correspondant" v-model="form.correspondent" kind="correspondants" label="Correspondant" :disabled="!canEdit" />
              <ReferenceSelect id="type" v-model="form.document_type" kind="types" label="Type de document" :disabled="!canEdit" />
              <ReferenceSelect id="chemin" v-model="form.storage_path" kind="chemins" label="Chemin de stockage" :disabled="!canEdit" />
              <TagPicker id="etiquettes" v-model="form.tags" class="span-2" :disabled="!canEdit" />
              <CustomFieldsEditor v-model="form.custom_fields" class="span-2" :disabled="!canEdit" />
            </div>

            <pre v-else-if="tab === 'contenu'" class="contenu">{{ doc.content || 'Aucun texte extrait.' }}</pre>

            <DocumentMetadataPanel v-else-if="tab === 'metadonnees'" :document="doc" />

            <DocumentNotes v-else-if="tab === 'notes'" v-model="notes" :document-id="doc.id" :disabled="!canEdit" />

            <DocumentHistory v-else-if="tab === 'historique'" :key="`h${doc.id}-${doc.modified}`" :document-id="doc.id" />

            <DocumentPermissions v-else-if="tab === 'permissions'" :key="`p${doc.id}`" :document-id="doc.id" />

            <DocumentVersions
              v-else
              :document="doc"
              :disabled="!canEdit"
              :shown="previewVersion"
              @show="previewVersion = $event"
              @changed="refresh"
            />
          </div>
        </section>
      </div>

      <div v-if="canEdit" class="barre-actions">
        <p v-if="saveError" class="alert alert-danger mb-0 me-auto" role="alert">{{ saveError }}</p>
        <p v-else-if="saved && !dirty" class="text-success mb-0 me-auto" role="status">Document enregistré.</p>
        <span v-else-if="dirty" class="text-body-secondary small me-auto">Modifications non enregistrées</span>
        <button type="button" class="btn btn-outline-secondary" :disabled="!dirty || saving" @click="cancel">Annuler</button>
        <button type="button" class="btn btn-outline-primary" :disabled="saving" @click="save(true)">Enregistrer et fermer</button>
        <button type="button" class="btn btn-primary" :disabled="!dirty || saving" @click="save(false)">
          <span v-if="saving" class="spinner-border spinner-border-sm me-1" aria-hidden="true" />
          Enregistrer
        </button>
      </div>
    </template>

    <div v-else class="chargement">
      <span class="spinner-border" role="status"><span class="visually-hidden">Chargement…</span></span>
    </div>

    <ConfirmDialog
      :open="confirmingDelete"
      title="Supprimer le document ?"
      confirm-label="Supprimer"
      danger
      :busy="deleting"
      @confirm="remove"
      @cancel="confirmingDelete = false"
    >
      « {{ doc?.title }} » sera placé dans la corbeille de Paperless, d'où il pourra être restauré.
    </ConfirmDialog>

    <ShareDialog v-if="doc" :open="sharing" :document-id="doc.id" :document-title="doc.title" @close="sharing = false" />

    <ConfirmDialog
      :open="pendingLeave !== null"
      title="Modifications non enregistrées"
      confirm-label="Quitter sans enregistrer"
      cancel-label="Rester"
      danger
      @confirm="confirmLeave"
      @cancel="pendingLeave = null"
    >
      Les modifications de ce document seront perdues.
    </ConfirmDialog>
  </div>
</template>

<style scoped>
.fiche {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.entete {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem 1rem;
}

.retour {
  flex-basis: 100%;
  text-decoration: none;
}

.titre {
  flex: 1 1 16rem;
  overflow-wrap: anywhere;
}

.outils {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.position {
  opacity: 1;
  font-variant-numeric: tabular-nums;
}

.corps {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.apercu-mobile {
  display: flex;
  align-items: center;
  gap: 1rem;
  text-decoration: none;
}

.apercu-mobile img {
  width: 6rem;
  aspect-ratio: 3 / 4;
  object-fit: cover;
  object-position: top;
  border: var(--bs-border-width) solid var(--bs-border-color);
  border-radius: var(--bs-border-radius);
  background: var(--bs-secondary-bg);
}

.cadre {
  width: 100%;
  height: calc(100dvh - 12rem);
  min-height: 30rem;
  border: var(--bs-border-width) solid var(--bs-border-color);
  border-radius: var(--bs-border-radius);
  background: var(--bs-secondary-bg);
}

.onglets-liste {
  overflow-x: auto;
  overflow-y: hidden;
  scrollbar-width: none;
}

@media (min-width: 992px) {
  .onglets-liste {
    overflow: visible;
  }
}

.onglets-liste .nav-link {
  white-space: nowrap;
}

.details {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
  gap: 1rem;
}

.span-2 {
  grid-column: 1 / -1;
}

.suggestions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.suggestion {
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.contenu {
  max-height: 70dvh;
  padding: 1rem;
  overflow: auto;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  font-family: inherit;
  background: var(--bs-tertiary-bg);
  border-radius: var(--bs-border-radius);
}

.barre-actions {
  position: sticky;
  bottom: 0;
  z-index: 10;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 0.5rem;
  margin: 0 -1rem -1rem;
  padding: 0.75rem 1rem calc(0.75rem + env(safe-area-inset-bottom));
  background: var(--bs-body-bg);
  border-top: var(--bs-border-width) solid var(--bs-border-color);
}

.chargement {
  display: flex;
  justify-content: center;
  padding: 3rem;
}

@media (min-width: 992px) {
  .retour {
    flex-basis: auto;
  }

  .corps {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    align-items: start;
  }

  .onglets {
    order: 1;
  }

  .apercu {
    position: sticky;
    top: 4.5rem;
    order: 2;
  }

  .barre-actions {
    margin: 0 -1.5rem -1.5rem;
    padding-inline: 1.5rem;
  }
}
</style>
