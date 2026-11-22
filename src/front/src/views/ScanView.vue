<script setup lang="ts">
import { computed, onBeforeUnmount, ref, useTemplateRef } from 'vue'
import CornerEditor from '../components/CornerEditor.vue'
import { fullFrame, isConvexQuad, type Quad } from '../scan/geometry'
import { decodePhoto, encodePage, type DecodedPhoto } from '../scan/images'
import type { ScanFilter } from '../scan/protocol'
import { createScanner } from '../scan/scanner'

/**
 * Scanner (SCAN-01 à 03) : photo prise avec l'appareil du téléphone (ou choisie), bords détectés
 * puis ajustables, page redressée et filtrée. Tout se passe dans le navigateur : aucune image
 * intermédiaire n'est envoyée au serveur.
 */
type Step = 'choix' | 'recadrage' | 'resultat'

const FILTERS: { value: ScanFilter; label: string }[] = [
  { value: 'scan', label: 'Scan' },
  { value: 'gris', label: 'Niveaux de gris' },
  { value: 'couleur', label: 'Couleur' },
]

const scanner = createScanner()
const camera = useTemplateRef<HTMLInputElement>('camera')
const gallery = useTemplateRef<HTMLInputElement>('gallery')

const step = ref<Step>('choix')
const busy = ref<string | null>(null)
const error = ref<string | null>(null)

const photo = ref<Omit<DecodedPhoto, 'image'> | null>(null)
const corners = ref<Quad | null>(null)
const detected = ref<Quad | null>(null)
const filter = ref<ScanFilter>('scan')
/** Page redressée affichée (URL blob:). */
const page = ref<string | null>(null)

const canValidate = computed(() => corners.value !== null && isConvexQuad(corners.value))

const message = (e: unknown) => (e instanceof Error ? e.message : 'Le traitement de l’image a échoué.')

function setPage(url: string | null) {
  if (page.value) URL.revokeObjectURL(page.value)
  page.value = url
}

function setPhoto(value: Omit<DecodedPhoto, 'image'> | null) {
  if (photo.value) URL.revokeObjectURL(photo.value.preview)
  photo.value = value
}

async function onFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  // Même photo choisie deux fois de suite : l'événement change doit se déclencher encore.
  input.value = ''
  if (!file) return
  error.value = null
  busy.value = 'Préparation de la photo…'
  try {
    const { image, ...decoded } = await decodePhoto(file)
    const found = await scanner.charger(image)
    setPage(null)
    setPhoto(decoded)
    detected.value = found
    corners.value = found ?? fullFrame(decoded.width, decoded.height)
    step.value = 'recadrage'
  } catch (e) {
    error.value = message(e)
  } finally {
    busy.value = null
  }
}

async function render() {
  if (!corners.value) return
  error.value = null
  busy.value = 'Redressement de la page…'
  try {
    const result = await scanner.redresser(corners.value, filter.value)
    setPage(URL.createObjectURL(await encodePage(result)))
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

onBeforeUnmount(() => {
  scanner.fermer()
  setPage(null)
  setPhoto(null)
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

    <div v-if="busy" class="d-flex align-items-center gap-2" role="status">
      <span class="spinner-border spinner-border-sm" aria-hidden="true" />
      <span>{{ busy }}</span>
    </div>

    <template v-if="step === 'choix'">
      <p class="text-body-secondary mb-0">
        Photographiez le document à plat, bien éclairé, sur un fond qui contraste avec le papier. Les bords sont détectés
        automatiquement et restent ajustables.
      </p>
      <div class="actions">
        <button type="button" class="btn btn-primary" :disabled="!!busy" @click="camera?.click()">Prendre une photo</button>
        <button type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="gallery?.click()">
          Choisir une image
        </button>
      </div>
    </template>

    <template v-else-if="step === 'recadrage' && photo && corners">
      <p class="mb-0" :class="detected ? 'text-body-secondary' : 'text-warning-emphasis'" data-test="consigne">
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
        <button type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="camera?.click()">Reprendre la photo</button>
      </div>
    </template>

    <template v-else-if="step === 'resultat' && page">
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
        <img :src="page" alt="Page redressée" class="page" />
      </div>
      <div class="actions">
        <button type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="step = 'recadrage'">Recadrer</button>
        <button type="button" class="btn btn-outline-secondary" :disabled="!!busy" @click="camera?.click()">Reprendre la photo</button>
      </div>
    </template>
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
  .filtres .btn {
    min-height: 44px;
  }

  .actions .btn {
    flex: 1 1 auto;
  }
}
</style>
