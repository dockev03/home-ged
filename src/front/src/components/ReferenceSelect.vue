<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef } from 'vue'
import { createReference, type CreatableKind } from '../api/documents'
import { ApiError } from '../api/http'
import { useReferencesStore } from '../stores/references'

/** Liste déroulante d'un correspondant, type ou chemin, avec création à la volée « + » (UI-05). */
const props = defineProps<{
  kind: Exclude<CreatableKind, 'tags'>
  label: string
  id: string
  disabled?: boolean
}>()
const model = defineModel<number | null>({ required: true })

const references = useReferencesStore()
const items = computed(() => references.lists[props.kind])

const creating = ref(false)
const name = ref('')
const path = ref('')
const saving = ref(false)
const error = ref<string | null>(null)
const nameInput = useTemplateRef<HTMLInputElement>('nameInput')

async function startCreate() {
  creating.value = true
  error.value = null
  await nextTick()
  nameInput.value?.focus()
}

function cancelCreate() {
  creating.value = false
  name.value = ''
  path.value = ''
}

async function create() {
  if (saving.value || !name.value.trim()) return
  saving.value = true
  error.value = null
  try {
    const item = await createReference(props.kind, {
      name: name.value.trim(),
      path: props.kind === 'chemins' ? path.value.trim() : undefined,
    })
    references.add(props.kind, item)
    model.value = item.id
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
    <label class="form-label" :for="id">{{ label }}</label>
    <div class="input-group">
      <select
        :id="id"
        class="form-select"
        :disabled="disabled"
        :value="model ?? ''"
        @change="model = ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null"
      >
        <option value="">(aucun)</option>
        <option v-for="item in items" :key="item.id" :value="item.id">{{ item.name }}</option>
        <!-- Valeur inconnue (pas le droit de voir l'élément) : conservée telle quelle. -->
        <option v-if="model !== null && !items.some((i) => i.id === model)" :value="model">Élément n° {{ model }}</option>
      </select>
      <button
        v-if="!disabled"
        type="button"
        class="btn btn-outline-secondary"
        :aria-label="`Créer : ${label.toLocaleLowerCase('fr')}`"
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
        :aria-label="`Nom du nouvel élément : ${label.toLocaleLowerCase('fr')}`"
        placeholder="Nom"
        maxlength="128"
        required
      />
      <input
        v-if="kind === 'chemins'"
        v-model="path"
        class="form-control form-control-sm"
        aria-label="Modèle de chemin"
        placeholder="{{ created_year }}/{{ correspondent }}/{{ title }}"
        required
      />
      <button type="submit" class="btn btn-sm btn-primary" :disabled="saving">Créer</button>
      <button type="button" class="btn btn-sm btn-link" @click="cancelCreate">Annuler</button>
    </form>
    <p v-if="error" class="text-danger small mb-0 mt-1" role="alert">{{ error }}</p>
  </div>
</template>

<style scoped>
.creation {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

.creation .form-control {
  flex: 1 1 10rem;
}
</style>
