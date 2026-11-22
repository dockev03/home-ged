<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import {
  activeCount,
  defaultFilters,
  SORT_FIELDS,
  TEXT_MODES,
  type DocumentFilters,
  type OwnerFilter,
} from '../filters/documentFilters'
import { useReferencesStore } from '../stores/references'
import BaseDropdown from './BaseDropdown.vue'
import FilterSelect from './FilterSelect.vue'

/** Barre de filtres de la liste des documents (UI-03), à la manière de Paperless. */
const props = defineProps<{ modelValue: DocumentFilters }>()
const emit = defineEmits<{ 'update:modelValue': [DocumentFilters] }>()

const references = useReferencesStore()
const lists = computed(() => references.lists)
const active = computed(() => activeCount(props.modelValue))
/** Sur mobile, les critères autres que le texte sont repliés. */
const expanded = ref(false)

function update(patch: Partial<DocumentFilters>) {
  emit('update:modelValue', { ...props.modelValue, ...patch })
}

// Texte : appliqué après une courte pause de frappe, ou tout de suite sur Entrée.
const text = ref(props.modelValue.texte)
let timer: ReturnType<typeof setTimeout> | undefined
watch(
  () => props.modelValue.texte,
  (value) => {
    if (value !== text.value.trim()) text.value = value
  },
)
function onText() {
  clearTimeout(timer)
  timer = setTimeout(applyText, 400)
}
function applyText() {
  clearTimeout(timer)
  if (text.value.trim() !== props.modelValue.texte) update({ texte: text.value.trim() })
}
onBeforeUnmount(() => clearTimeout(timer))

const OWNERS: { value: OwnerFilter; label: string }[] = [
  { value: 'tous', label: 'Tous' },
  { value: 'moi', label: 'Mes documents' },
  { value: 'partages', label: 'Partagés avec moi' },
  { value: 'aucun', label: 'Sans propriétaire' },
]

const datesCount = computed(
  () => Number(!!(props.modelValue.creeDu || props.modelValue.creeAu)) + Number(!!(props.modelValue.ajouteDu || props.modelValue.ajouteAu)),
)

function reset() {
  const { tri, desc } = props.modelValue
  text.value = ''
  emit('update:modelValue', { ...defaultFilters(), tri, desc })
}
</script>

<template>
  <section class="filtres" aria-label="Filtres">
    <div class="ligne-texte">
      <div class="input-group">
        <select
          class="form-select mode"
          aria-label="Type de recherche"
          :value="modelValue.mode"
          @change="update({ mode: ($event.target as HTMLSelectElement).value as DocumentFilters['mode'] })"
        >
          <option v-for="mode in TEXT_MODES" :key="mode.value" :value="mode.value">{{ mode.label }}</option>
        </select>
        <input
          v-model="text"
          type="search"
          class="form-control"
          aria-label="Texte recherché"
          placeholder="Rechercher"
          enterkeyhint="search"
          @input="onText"
          @keydown.enter.prevent="applyText"
        />
      </div>
      <button
        type="button"
        class="btn btn-outline-secondary d-lg-none"
        :aria-expanded="expanded"
        aria-controls="criteres"
        @click="expanded = !expanded"
      >
        Filtres<span v-if="active" class="badge text-bg-primary ms-1">{{ active }}</span>
      </button>
    </div>

    <div id="criteres" class="criteres" :class="{ ouverts: expanded }">
      <FilterSelect
        :model-value="modelValue.tags"
        label="Étiquettes"
        :items="lists.tags"
        none-label="Sans étiquette"
        @update:model-value="update({ tags: $event })"
      >
        <template #entete>
          <div class="btn-group btn-group-sm w-100 mb-2" role="group" aria-label="Combinaison des étiquettes">
            <button
              type="button"
              class="btn"
              :class="modelValue.tagsMode === 'tous' ? 'btn-secondary' : 'btn-outline-secondary'"
              :aria-pressed="modelValue.tagsMode === 'tous'"
              @click="update({ tagsMode: 'tous' })"
            >
              Toutes
            </button>
            <button
              type="button"
              class="btn"
              :class="modelValue.tagsMode === 'un' ? 'btn-secondary' : 'btn-outline-secondary'"
              :aria-pressed="modelValue.tagsMode === 'un'"
              @click="update({ tagsMode: 'un' })"
            >
              Au moins une
            </button>
          </div>
        </template>
      </FilterSelect>
      <FilterSelect
        :model-value="modelValue.correspondants"
        label="Correspondant"
        :items="lists.correspondants"
        none-label="Sans correspondant"
        @update:model-value="update({ correspondants: $event })"
      />
      <FilterSelect
        :model-value="modelValue.types"
        label="Type"
        :items="lists.types"
        none-label="Sans type"
        @update:model-value="update({ types: $event })"
      />
      <FilterSelect
        :model-value="modelValue.chemins"
        label="Chemin de stockage"
        :items="lists.chemins"
        none-label="Sans chemin"
        @update:model-value="update({ chemins: $event })"
      />
      <FilterSelect
        :model-value="modelValue.champs"
        label="Champs personnalisés"
        :items="lists.champs"
        @update:model-value="update({ champs: $event === 'aucun' ? [] : $event })"
      >
        <template #pied>
          <div class="px-3 pt-2 border-top mt-1">
            <label class="form-label small mb-1" for="champ-valeur">Valeur contient</label>
            <input
              id="champ-valeur"
              type="search"
              class="form-control form-control-sm"
              :value="modelValue.champValeur"
              @change="update({ champValeur: ($event.target as HTMLInputElement).value.trim() })"
            />
          </div>
        </template>
      </FilterSelect>

      <BaseDropdown
        :button-class="datesCount ? 'btn btn-primary' : 'btn btn-outline-secondary'"
        menu-role="dialog"
        menu-class="p-3 dates"
      >
        <template #toggle>Dates<span v-if="datesCount" class="badge text-bg-light ms-1">{{ datesCount }}</span></template>
        <fieldset>
          <legend class="fs-6">Date du document</legend>
          <div class="periode">
            <label>Du <input type="date" class="form-control form-control-sm" :value="modelValue.creeDu" @change="update({ creeDu: ($event.target as HTMLInputElement).value })" /></label>
            <label>Au <input type="date" class="form-control form-control-sm" :value="modelValue.creeAu" @change="update({ creeAu: ($event.target as HTMLInputElement).value })" /></label>
          </div>
        </fieldset>
        <fieldset class="mt-3">
          <legend class="fs-6">Date d'ajout</legend>
          <div class="periode">
            <label>Du <input type="date" class="form-control form-control-sm" :value="modelValue.ajouteDu" @change="update({ ajouteDu: ($event.target as HTMLInputElement).value })" /></label>
            <label>Au <input type="date" class="form-control form-control-sm" :value="modelValue.ajouteAu" @change="update({ ajouteAu: ($event.target as HTMLInputElement).value })" /></label>
          </div>
        </fieldset>
      </BaseDropdown>

      <BaseDropdown
        :button-class="modelValue.proprietaire !== 'tous' ? 'btn btn-primary' : 'btn btn-outline-secondary'"
        menu-role="dialog"
      >
        <template #toggle>Permissions</template>
        <fieldset class="px-3 py-1">
          <legend class="visually-hidden">Propriétaire</legend>
          <div v-for="owner in OWNERS" :key="owner.value" class="form-check">
            <input
              :id="`proprio-${owner.value}`"
              type="radio"
              class="form-check-input"
              name="proprietaire"
              :checked="modelValue.proprietaire === owner.value"
              @change="update({ proprietaire: owner.value })"
            />
            <label class="form-check-label" :for="`proprio-${owner.value}`">{{ owner.label }}</label>
          </div>
        </fieldset>
      </BaseDropdown>

      <div class="input-group tri">
        <select
          class="form-select"
          aria-label="Trier par"
          :value="modelValue.tri"
          @change="update({ tri: ($event.target as HTMLSelectElement).value as DocumentFilters['tri'] })"
        >
          <option v-for="field in SORT_FIELDS" :key="field.value" :value="field.value">{{ field.label }}</option>
        </select>
        <button
          type="button"
          class="btn btn-outline-secondary"
          :aria-label="modelValue.desc ? 'Ordre décroissant, passer en croissant' : 'Ordre croissant, passer en décroissant'"
          :disabled="modelValue.tri === 'pertinence'"
          @click="update({ desc: !modelValue.desc })"
        >
          {{ modelValue.desc ? '↓' : '↑' }}
        </button>
      </div>

      <button v-if="active" type="button" class="btn btn-link reinitialiser" @click="reset">
        Réinitialiser les filtres
      </button>
    </div>
    <p v-if="references.error" class="text-warning-emphasis small mb-0">{{ references.error }}</p>
  </section>
</template>

<style scoped>
.filtres {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.ligne-texte {
  display: flex;
  gap: 0.5rem;
}

.mode {
  flex: 0 0 auto;
  width: auto;
  max-width: 45%;
}

.criteres {
  display: none;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.criteres.ouverts {
  display: flex;
}

.tri {
  width: auto;
}

.tri .form-select {
  flex: 0 1 auto;
  width: auto;
}

.periode {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.5rem;
}

.periode label {
  font-size: 0.875rem;
}

:deep(.dates) {
  min-width: 18rem;
}

@media (min-width: 992px) {
  .ligne-texte {
    max-width: 40rem;
  }

  .criteres {
    display: flex;
  }
}
</style>
