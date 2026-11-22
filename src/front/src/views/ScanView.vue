<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'
import { onBeforeRouteLeave, useRouter, type RouteLocationRaw } from 'vue-router'
import { MAX_UPLOAD_BYTES } from '../api/documents'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import CornerEditor from '../components/CornerEditor.vue'
import ScanPages from '../components/ScanPages.vue'
import TagPicker from '../components/TagPicker.vue'
import { findPersonField, movePage, scanFileName, type ScanPage } from '../scan/document'
import { fullFrame, isConvexQuad, type Quad } from '../scan/geometry'
import { decodePhoto, encodePage, type DecodedPhoto } from '../scan/images'
import { buildPdf } from '../scan/pdf'
import type { ScanFilter } from '../scan/protocol'
import { createScanner } from '../scan/scanner'
import { useReferencesStore } from '../stores/references'
import { useUploadsStore } from '../stores/uploads'

/**
 * Scanner (SCAN-01 à 06) : chaque page est photographiée, recadrée et filtrée, puis ajoutée au
 * document ; les pages sont réordonnées ou recadrées à volonté, assemblées en un PDF et envoyées à
 * Paperless avec titre, personne et étiquettes facultatifs. Tout le traitement des images se fait
 * dans le navigateur : seul le PDF final est envoyé.
 */
type Step = 'document' | 'recadrage' | 'resultat'

const FILTERS: { value: ScanFilter; label: string }[] = [
  { value: 'scan', label: 'Scan' },
  { value: 'gris', label: 'Niveaux de gris' },
  { value: 'couleur', label: 'Couleur' },
]

const scanner = createScanner()
const uploads = useUploadsStore()
const references = useReferencesStore()
const router = useRouter()
const camera = useTemplateRef<HTMLInputElement>('camera')
const gallery = useTemplateRef<HTMLInputElement>('gallery')

const step = ref<Step>('document')
const busy = ref<string | null>(null)
const error = ref<string | null>(null)
/** Confirmation après l'envoi du dernier document. */
const sent = ref<string | null>(null)

/** Pages du document en cours, dans l'ordre. */
const pages = ref<ScanPage[]>([])
let nextPageId = 1

// Page en cours de préparation : photo, coins, filtre, rendu ; `editing` si elle remplace une page.
const source = ref<Blob | null>(null)
const editing = ref<number | null>(null)
const photo = ref<Omit<DecodedPhoto, 'image'> | null>(null)
const corners = ref<Quad | null>(null)
const detected = ref<Quad | null>(null)
const filter = ref<ScanFilter>('scan')
/** Page redressée affichée (JPEG et son URL blob:). */
const result = ref<{ jpeg: Blob; url: string } | null>(null)

// Saisie facultative avant envoi (SCAN-06).
const title = ref('')
const tags = ref<number[]>([])
const person = ref('')
const personField = computed(() => findPersonField(references.lists.champs))

const canValidate = computed(() => corners.value !== null && isConvexQuad(corners.value))
const editingIndex = computed(() => pages.value.findIndex((p) => p.id === editing.value))
/** Pages non envoyées ou page en préparation : quitter l'écran les perdrait. */
const dirty = computed(() => pages.value.length > 0 || step.value !== 'document')

const message = (e: unknown) => (e instanceof Error ? e.message : 'Le traitement de l’image a échoué.')

function setResult(value: { jpeg: Blob; url: string } | null) {
  if (result.value) URL.revokeObjectURL(result.value.url)
  result.value = value
}

function setPhoto(value: Omit<DecodedPhoto, 'image'> | null) {
  if (photo.value) URL.revokeObjectURL(photo.value.preview)
  photo.value = value
}

/** Confie une photo au worker et passe au recadrage ; `page` : coins et filtre d'une page à reprendre. */
async function load(file: Blob, page: ScanPage | null) {
  error.value = null
  sent.value = null
  busy.value = 'Préparation de la photo…'
  try {
    const { image, ...decoded } = await decodePhoto(file)
    const found = await scanner.charger(image)
    setResult(null)
    setPhoto(decoded)
    source.value = file
    editing.value = page?.id ?? null
    detected.value = found
    corners.value = page?.corners ?? found ?? fullFrame(decoded.width, decoded.height)
    filter.value = page?.filter ?? filter.value
    step.value = 'recadrage'
  } catch (e) {
    error.value = message(e)
  } finally {
    busy.value = null
  }
}

async function onFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  // Même photo choisie deux fois de suite : l'événement change doit se déclencher encore.
  input.value = ''
  if (file) await load(file, null)
}

async function render() {
  if (!corners.value) return
  error.value = null
  busy.value = 'Redressement de la page…'
  try {
    const page = await scanner.redresser(corners.value, filter.value)
    const jpeg = await encodePage(page)
    setResult({ jpeg, url: URL.createObjectURL(jpeg) })
    step.value = 'resultat'
  } catch (e) {
    error.value = message(e)
  } finally {
    busy.value = null
  }
}

function chooseFilter(value: ScanFilter) {
  if (value === filter.value || busy.value) return
  filter.value = value
  void render()
}

function resetCorners(target: 'detection' | 'image') {
  if (!photo.value) return
  corners.value = target === 'detection' && detected.value ? detected.value : fullFrame(photo.value.width, photo.value.height)
}

/** Abandonne la page en préparation (les pages déjà ajoutées restent). */
function discardCurrent() {
  setResult(null)
  setPhoto(null)
  source.value = null
  editing.value = null
  corners.value = null
  detected.value = null
  step.value = 'document'
}

/** Ajoute la page au document, ou remplace celle qu'on recadrait. */
function keepPage() {
  if (!result.value || !source.value || !corners.value) return
  const page: ScanPage = {
    id: editing.value ?? nextPageId++,
    photo: source.value,
    detected: detected.value,
    corners: corners.value,
    filter: filter.value,
    jpeg: result.value.jpeg,
    url: result.value.url,
  }
  const index = editingIndex.value
  if (index >= 0) {
    URL.revokeObjectURL(pages.value[index]!.url)
    pages.value = pages.value.map((p, i) => (i === index ? page : p))
  } else {
    pages.value = [...pages.value, page]
  }
  // L'URL appartient désormais à la page : ne pas la libérer.
  result.value = null
  discardCurrent()
}

function editPage(id: number) {
  const page = pages.value.find((p) => p.id === id)
  if (page && !busy.value) void load(page.photo, page)
}

function onMove(id: number, delta: -1 | 1) {
  pages.value = movePage(pages.value, id, delta)
}

// Suppression d'une page, après confirmation.
const removing = ref<number | null>(null)
const removingIndex = computed(() => pages.value.findIndex((p) => p.id === removing.value))

function removePage() {
  const page = pages.value[removingIndex.value]
  removing.value = null
  if (!page) return
  URL.revokeObjectURL(page.url)
  pages.value = pages.value.filter((p) => p.id !== page.id)
}

function clearDocument() {
  for (const page of pages.value) URL.revokeObjectURL(page.url)
  pages.value = []
  title.value = ''
  tags.value = []
  person.value = ''
}

/** Assemble les pages en PDF (SCAN-05) et le confie à la file d'envoi, avec la saisie facultative. */
async function send() {
  if (pages.value.length === 0 || busy.value) return
  error.value = null
  busy.value = 'Assemblage du PDF…'
  try {
    const jpegs = await Promise.all(pages.value.map(async (p) => new Uint8Array(await p.jpeg.arrayBuffer())))
    const file = new File([buildPdf(jpegs)], scanFileName(title.value, new Date()), { type: 'application/pdf' })
    if (file.size > MAX_UPLOAD_BYTES) {
      error.value = 'Document trop volumineux (200 Mo au plus) : retirez des pages ou envoyez-le en plusieurs fois.'
      return
    }
    const field = personField.value
    uploads.add([file], {
      title: title.value,
      tags: tags.value,
      customFields: field && person.value ? { [field.id]: person.value } : {},
    })
    const count = pages.value.length
    clearDocument()
    sent.value = `Document de ${count} page${count > 1 ? 's' : ''} envoyé. Suivez son traitement dans le panneau des envois.`
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Le PDF n’a pas pu être assemblé.'
  } finally {
    busy.value = null
  }
}

// Pages non envoyées : confirmation avant de quitter l'écran.
const pendingLeave = ref<RouteLocationRaw | null>(null)
let bypassGuard = false

onBeforeRouteLeave((to) => {
  if (bypassGuard || !dirty.value) return true
  pendingLeave.value = to.fullPath
  return false
})

async function confirmLeave() {
  const target = pendingLeave.value
  pendingLeave.value = null
  if (!target) return
  bypassGuard = true
  try {
    await router.push(target)
  } finally {
    bypassGuard = false
  }
}

function onBeforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value) event.preventDefault()
}

onMounted(() => {
  window.addEventListener('beforeunload', onBeforeUnload)
  void references.ensureLoaded()
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', onBeforeUnload)
  scanner.fermer()
  setResult(null)
  setPhoto(null)
  clearDocument()
})
</script>

<template>
  <div class="scanner">
    <h1 class="h3 mb-0">Scanner</h1>

    <input
      ref="camera"
      class="d-none"
      type="file"
      accept="image/*"
      capture="environment"
      data-test="camera"
      @change="onFile"
    />
    <input ref="gallery" class="d-none" type="file" accept="image/*" data-test="galerie" @change="onFile" />

    <div v-if="error" class="alert alert-danger mb-0" role="alert">{{ error }}</div>
    <div v-if="sent" class="alert alert-success mb-0" role="status" data-test="envoye">{{ sent }}</div>

    <div v-if="busy" class="d-flex align-items-center gap-2" role="status">
      <span class="spinner-border spinner-border-sm" aria-hidden="true" />
      <span>{{ busy }}</span>
    </div>

    <template v-if="step === 'document'">
      <template v-if="pages.length === 0">
        <p class="text-body-secondary mb-0">
          Photographiez le document à plat, bien éclairé, sur un fond qui contraste avec le papier. Les bords sont détectés
          automatiquement et restent ajustables. Ajoutez autant de pages que nécessaire avant l'envoi.
        </p>
        <div class="actions">
          <button type="button" class="btn btn-primary" :disabled="!!busy" @click="camera?.click()">Prendre une photo</button>
          <button type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="gallery?.click()">
            Choisir une image
          </button>
        </div>
      </template>

      <template v-else>
        <ScanPages :pages="pages" :disabled="!!busy" @move="onMove" @edit="editPage" @remove="removing = $event" />
        <div class="actions">
          <button type="button" class="btn btn-outline-primary" :disabled="!!busy" @click="camera?.click()">Ajouter une page</button>
          <button type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="gallery?.click()">
            Ajouter une image
          </button>
        </div>

        <form class="envoi card" @submit.prevent="send">
          <div class="card-body">
            <h2 class="h5">Envoyer à Paperless</h2>
            <p class="text-body-secondary small">
              Facultatif : Paperless reconnaît le texte et propose lui-même un classement.
            </p>
            <div class="mb-3">
              <label class="form-label" for="scan-titre">Titre</label>
              <input
                id="scan-titre"
                v-model="title"
                class="form-control"
                maxlength="128"
                placeholder="Titre déduit du nom du fichier"
                :disabled="!!busy"
              />
            </div>
            <div v-if="personField" class="mb-3">
              <label class="form-label" for="scan-personne">{{ personField.name }}</label>
              <select id="scan-personne" v-model="person" class="form-select" :disabled="!!busy">
                <option value="">Non précisée</option>
                <option v-for="option in personField.extra_data?.select_options ?? []" :key="option.id" :value="option.id">
                  {{ option.label }}
                </option>
              </select>
            </div>
            <TagPicker id="scan-etiquettes" v-model="tags" class="mb-3" :disabled="!!busy" />
            <button type="submit" class="btn btn-primary" :disabled="!!busy">
              Envoyer {{ pages.length }} page{{ pages.length > 1 ? 's' : '' }}
            </button>
          </div>
        </form>
      </template>
    </template>

    <template v-else-if="step === 'recadrage' && photo && corners">
      <p class="mb-0" :class="detected ? 'text-body-secondary' : 'text-warning-emphasis'" data-test="consigne">
        <span v-if="editingIndex >= 0">Page {{ editingIndex + 1 }} : </span>
        {{
          detected
            ? 'Bords détectés : ajustez les coins si besoin.'
            : 'Bords non détectés : placez les 4 coins sur ceux de la page.'
        }}
      </p>
      <div class="zone-image">
        <CornerEditor v-model="corners" :src="photo.preview" :width="photo.width" :height="photo.height" />
      </div>
      <p v-if="!canValidate" class="text-danger mb-0" role="alert">Les coins se croisent : replacez-les sur ceux de la page.</p>
      <div class="actions">
        <button type="button" class="btn btn-primary" :disabled="!canValidate || !!busy" @click="render">Valider le recadrage</button>
        <button v-if="detected" type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="resetCorners('detection')">
          Bords détectés
        </button>
        <button type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="resetCorners('image')">Toute l'image</button>
        <button v-if="editing === null" type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="camera?.click()">
          Reprendre la photo
        </button>
        <button v-if="pages.length" type="button" class="btn btn-link" :disabled="!!busy" @click="discardCurrent">
          Retour au document
        </button>
      </div>
    </template>

    <template v-else-if="step === 'resultat' && result">
      <div class="btn-group filtres" role="group" aria-label="Rendu de la page">
        <button
          v-for="f in FILTERS"
          :key="f.value"
          type="button"
          class="btn"
          :class="filter === f.value ? 'btn-primary' : 'btn-outline-primary'"
          :aria-pressed="filter === f.value"
          :disabled="!!busy"
          @click="chooseFilter(f.value)"
        >
          {{ f.label }}
        </button>
      </div>
      <div class="zone-image">
        <img :src="result.url" alt="Page redressée" class="page" />
      </div>
      <div class="actions">
        <button type="button" class="btn btn-primary" :disabled="!!busy" @click="keepPage">
          {{ editingIndex >= 0 ? `Remplacer la page ${editingIndex + 1}` : 'Ajouter au document' }}
        </button>
        <button type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="step = 'recadrage'">Recadrer</button>
        <button v-if="editing === null" type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="camera?.click()">
          Reprendre la photo
        </button>
        <button v-if="pages.length" type="button" class="btn btn-link" :disabled="!!busy" @click="discardCurrent">
          Retour au document
        </button>
      </div>
    </template>

    <ConfirmDialog
      :open="removing !== null"
      :title="`Supprimer la page ${removingIndex + 1} ?`"
      confirm-label="Supprimer"
      danger
      @confirm="removePage"
      @cancel="removing = null"
    >
      La page est retirée du document en cours.
    </ConfirmDialog>

    <ConfirmDialog
      :open="pendingLeave !== null"
      title="Document non envoyé"
      confirm-label="Quitter sans envoyer"
      cancel-label="Rester"
      danger
      @confirm="confirmLeave"
      @cancel="pendingLeave = null"
    >
      Les pages scannées seront perdues.
    </ConfirmDialog>
  </div>
</template>

<style scoped>
.scanner {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 60rem;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.zone-image {
  display: flex;
  justify-content: center;
  background: var(--bs-tertiary-bg);
  border-radius: var(--bs-border-radius);
  padding: 0.5rem;
}

.page {
  display: block;
  max-width: 100%;
  max-height: 70vh;
  box-shadow: var(--bs-box-shadow-sm);
}

.filtres {
  align-self: flex-start;
}

/* Cibles tactiles ≥ 44 px sur mobile (§4). */
@media (max-width: 767.98px) {
  .actions .btn,
  .filtres .btn,
  .envoi .btn-primary {
    min-height: 44px;
  }

  .actions .btn {
    flex: 1 1 auto;
  }

  .envoi .btn-primary {
    width: 100%;
  }
}
</style>
