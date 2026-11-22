<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Selection } from '../filters/documentFilters'
import BaseDropdown from './BaseDropdown.vue'

/** Filtre à choix multiple avec recherche dans la liste et option « Aucun » (UI-03). */
const props = defineProps<{
  label: string
  items: { id: number; name: string; color?: string; text_color?: string }[]
  /** Libellé de l'option « non renseigné » ; absente si non fourni. */
  noneLabel?: string
}>()
const model = defineModel<Selection>({ required: true })

const search = ref('')
const count = computed(() => (model.value === 'aucun' ? 1 : model.value.length))
const visible = computed(() => {
  const term = search.value.trim().toLocaleLowerCase('fr')
  return term ? props.items.filter((item) => item.name.toLocaleLowerCase('fr').includes(term)) : props.items
})
const isChecked = (id: number) => Array.isArray(model.value) && model.value.includes(id)

function toggle(id: number) {
  const current = Array.isArray(model.value) ? model.value : []
  model.value = current.includes(id) ? current.filter((v) => v !== id) : [...current, id]
}

const toggleNone = () => (model.value = model.value === 'aucun' ? [] : 'aucun')
</script>

<template>
  <BaseDropdown
    :button-class="count ? 'btn btn-primary' : 'btn btn-outline-secondary'"
    menu-class="filtre-menu"
    menu-role="dialog"
    :aria-label="count ? `${label} (${count} choisi${count > 1 ? 's' : ''})` : label"
  >
    <template #toggle>
      {{ label }}<span v-if="count" class="badge text-bg-light ms-1">{{ count }}</span>
    </template>
    <div class="px-2 pb-2">
      <slot name="entete" />
      <input
        v-if="items.length > 7"
        v-model="search"
        type="search"
        class="form-control form-control-sm"
        :aria-label="`Chercher dans ${label.toLocaleLowerCase('fr')}`"
        placeholder="Filtrer la liste"
      />
    </div>
    <div class="liste">
      <label v-if="noneLabel" class="dropdown-item form-check">
        <input type="checkbox" class="form-check-input" :checked="model === 'aucun'" @change="toggleNone" />
        <em>{{ noneLabel }}</em>
      </label>
      <label v-for="item in visible" :key="item.id" class="dropdown-item form-check">
        <input
          type="checkbox"
          class="form-check-input"
          :checked="isChecked(item.id)"
          :disabled="model === 'aucun'"
          @change="toggle(item.id)"
        />
        <span
          v-if="item.color"
          class="pastille"
          :style="{ backgroundColor: item.color, borderColor: item.text_color }"
          aria-hidden="true"
        />
        {{ item.name }}
      </label>
      <p v-if="visible.length === 0" class="text-body-secondary small px-3 mb-1">Aucun résultat.</p>
    </div>
    <slot name="pied" />
  </BaseDropdown>
</template>

<style scoped>
.liste {
  max-height: min(20rem, 50dvh);
  overflow-y: auto;
}

.form-check {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding-left: 1rem;
  margin: 0;
  cursor: pointer;
}

.form-check-input {
  flex: none;
  margin: 0;
  float: none;
}

.pastille {
  flex: none;
  width: 0.8rem;
  height: 0.8rem;
  border: 1px solid;
  border-radius: 50%;
}

:deep(.filtre-menu) {
  min-width: 16rem;
}
</style>
