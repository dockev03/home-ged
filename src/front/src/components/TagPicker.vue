<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef } from 'vue'
import { createReference } from '../api/documents'
import { ApiError } from '../api/http'
import { useReferencesStore } from '../stores/references'
import TagBadge from './TagBadge.vue'

/** Étiquettes du document : retrait d'un clic, ajout par liste, création à la volée (UI-05). */
const props = defineProps<{ id: string; disabled?: boolean }>()
const model = defineModel<number[]>({ required: true })

const references = useReferencesStore()
const selected = computed(() =>
  model.value.map((id) => references.tags.get(id) ?? { id, name: `Étiquette n° ${id}`, color: '#6c757d', text_color: '#ffffff', is_inbox_tag: false }),
)
const available = computed(() => references.lists.tags.filter((tag) => !model.value.includes(tag.id)))

function remove(id: number) {
  model.value = model.value.filter((value) => value !== id)
}

function addFromSelect(event: Event) {
  const select = event.target as HTMLSelectElement
  if (select.value) model.value = [...model.value, Number(select.value)]
  select.value = ''
}

/** Couleur au hasard parmi une palette lisible, comme Paperless propose une couleur aléatoire. */
const PALETTE = ['#a6cee3', '#1f78b4', '#b2df8a', '#33a02c', '#fb9a99', '#e31a1c', '#fdbf6f', '#ff7f00', '#cab2d6', '#6a3d9a']
const creating = ref(false)
const name = ref('')
const color = ref(PALETTE[0]!)
const saving = ref(false)
const error = ref<string | null>(null)
const nameInput = useTemplateRef<HTMLInputElement>('nameInput')

async function startCreate() {
  creating.value = true
  error.value = null
  color.value = PALETTE[Math.floor(Math.random() * PALETTE.length)]!
  await nextTick()
  nameInput.value?.focus()
}

function cancelCreate() {
  creating.value = false
  name.value = ''
}

async function create() {
  if (saving.value || !name.value.trim()) return
  saving.value = true
  error.value = null
  try {
    const tag = await createReference('tags', { name: name.value.trim(), color: color.value })
    references.add('tags', tag)
    model.value = [...model.value, tag.id]
    cancelCreate()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Création impossible, vérifiez votre connexion.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div>
    <label class="form-label" :for="id">Étiquettes</label>
    <ul class="choisies list-unstyled" aria-label="Étiquettes du document">
      <li v-for="tag in selected" :key="tag.id">
        <button
          v-if="!props.disabled"
          type="button"
          class="btn btn-sm p-0 border-0 retirer"
          :aria-label="`Retirer l'étiquette ${tag.name}`"
          @click="remove(tag.id)"
        >
          <TagBadge :tag="tag" /><span aria-hidden="true" class="croix">×</span>
        </button>
        <TagBadge v-else :tag="tag" />
      </li>
      <li v-if="selected.length === 0" class="text-body-secondary small">Aucune étiquette</li>
    </ul>
    <div v-if="!props.disabled" class="input-group">
      <select :id="id" class="form-select" @change="addFromSelect">
        <option value="">Ajouter une étiquette…</option>
        <option v-for="tag in available" :key="tag.id" :value="tag.id">{{ tag.name }}</option>
      </select>
      <button
        type="button"
        class="btn btn-outline-secondary"
        aria-label="Créer une étiquette"
        :aria-expanded="creating"
        @click="creating ? cancelCreate() : startCreate()"
      >
        +
      </button>
    </div>
    <form v-if="creating" class="creation" @submit.prevent="create" @keydown.esc.stop="cancelCreate">
      <input
        ref="nameInput"
        v-model="name"
        class="form-control form-control-sm"
        aria-label="Nom de la nouvelle étiquette"
        placeholder="Nom"
        maxlength="128"
        required
      />
      <input v-model="color" type="color" class="form-control form-control-sm form-control-color" aria-label="Couleur" />
      <button type="submit" class="btn btn-sm btn-primary" :disabled="saving">Créer</button>
      <button type="button" class="btn btn-sm btn-link" @click="cancelCreate">Annuler</button>
    </form>
    <p v-if="error" class="text-danger small mb-0 mt-1" role="alert">{{ error }}</p>
  </div>
</template>

<style scoped>
.choisies {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  margin: 0 0 0.5rem;
}

.retirer {
  display: inline-flex;
  align-items: center;
}

.croix {
  margin-left: 0.2rem;
  font-weight: bold;
}

.creation {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

.creation .form-control:not(.form-control-color) {
  flex: 1 1 10rem;
}
</style>
