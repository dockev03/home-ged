<script setup lang="ts">
import { computed } from 'vue'
import type { CustomField, CustomFieldValue } from '../api/documents'
import { formatMonetary, parseMonetary } from '../documents/form'
import { useReferencesStore } from '../stores/references'

/** Champs personnalisés du document : ajout, saisie selon le type, retrait (UI-05). */
defineProps<{ disabled?: boolean }>()
const model = defineModel<CustomFieldValue[]>({ required: true })

const references = useReferencesStore()
const definitions = computed(() => new Map(references.lists.champs.map((f) => [f.id, f])))
const available = computed(() => references.lists.champs.filter((f) => !model.value.some((v) => v.field === f.id)))

const definition = (id: number): CustomField =>
  definitions.value.get(id) ?? { id, name: `Champ n° ${id}`, data_type: 'string' }

function set(index: number, value: unknown) {
  model.value = model.value.map((item, i) => (i === index ? { field: item.field, value } : item))
}

function remove(index: number) {
  model.value = model.value.filter((_, i) => i !== index)
}

function add(event: Event) {
  const select = event.target as HTMLSelectElement
  if (select.value) {
    const field = Number(select.value)
    model.value = [...model.value, { field, value: definition(field).data_type === 'boolean' ? false : null }]
  }
  select.value = ''
}

const inputValue = (value: unknown) => (value === null || value === undefined ? '' : String(value))
const target = (event: Event) => event.target as HTMLInputElement

function number(event: Event, integer: boolean) {
  const text = target(event).value
  if (text === '') return null
  const value = Number(text)
  return Number.isFinite(value) ? (integer ? Math.trunc(value) : value) : null
}

const currency = (field: CustomField) => field.extra_data?.default_currency || 'EUR'

function setMonetary(index: number, field: CustomField, part: 'currency' | 'amount', text: string) {
  const current = parseMonetary(model.value[index]!.value, currency(field))
  const next = { ...current, [part]: text }
  set(index, formatMonetary(next.currency, next.amount))
}
</script>

<template>
  <fieldset>
    <legend class="form-label fs-6 mb-2">Champs personnalisés</legend>
    <p v-if="model.length === 0" class="text-body-secondary small">Aucun champ personnalisé.</p>
    <div v-for="(item, index) in model" :key="item.field" class="champ">
      <label :for="`champ-${item.field}`" class="form-label small mb-1">{{ definition(item.field).name }}</label>
      <div class="input-group">
        <template v-if="definition(item.field).data_type === 'boolean'">
          <div class="form-control">
            <input
              :id="`champ-${item.field}`"
              type="checkbox"
              class="form-check-input"
              :checked="item.value === true"
              :disabled="disabled"
              @change="set(index, target($event).checked)"
            />
          </div>
        </template>
        <textarea
          v-else-if="definition(item.field).data_type === 'longtext'"
          :id="`champ-${item.field}`"
          class="form-control"
          rows="3"
          :value="inputValue(item.value)"
          :disabled="disabled"
          @input="set(index, ($event.target as HTMLTextAreaElement).value || null)"
        />
        <select
          v-else-if="definition(item.field).data_type === 'select'"
          :id="`champ-${item.field}`"
          class="form-select"
          :value="inputValue(item.value)"
          :disabled="disabled"
          @change="set(index, ($event.target as HTMLSelectElement).value || null)"
        >
          <option value="">(aucune valeur)</option>
          <option v-for="option in definition(item.field).extra_data?.select_options ?? []" :key="option.id" :value="option.id">
            {{ option.label }}
          </option>
        </select>
        <template v-else-if="definition(item.field).data_type === 'monetary'">
          <input
            class="form-control devise"
            aria-label="Devise"
            maxlength="3"
            :value="parseMonetary(item.value, currency(definition(item.field))).currency"
            :disabled="disabled"
            @change="setMonetary(index, definition(item.field), 'currency', target($event).value)"
          />
          <input
            :id="`champ-${item.field}`"
            type="number"
            step="0.01"
            inputmode="decimal"
            class="form-control"
            :value="parseMonetary(item.value, currency(definition(item.field))).amount"
            :disabled="disabled"
            @change="setMonetary(index, definition(item.field), 'amount', target($event).value)"
          />
        </template>
        <input
          v-else-if="definition(item.field).data_type === 'integer' || definition(item.field).data_type === 'float'"
          :id="`champ-${item.field}`"
          type="number"
          :step="definition(item.field).data_type === 'integer' ? 1 : 'any'"
          inputmode="decimal"
          class="form-control"
          :value="inputValue(item.value)"
          :disabled="disabled"
          @change="set(index, number($event, definition(item.field).data_type === 'integer'))"
        />
        <input
          v-else-if="definition(item.field).data_type === 'documentlink'"
          :id="`champ-${item.field}`"
          class="form-control"
          readonly
          :value="Array.isArray(item.value) && item.value.length ? `Documents n° ${item.value.join(', ')}` : 'Aucun document lié'"
        />
        <input
          v-else
          :id="`champ-${item.field}`"
          :type="definition(item.field).data_type === 'date' ? 'date' : definition(item.field).data_type === 'url' ? 'url' : 'text'"
          :maxlength="definition(item.field).data_type === 'string' ? 128 : undefined"
          class="form-control"
          :value="inputValue(item.value)"
          :disabled="disabled"
          @input="set(index, target($event).value || null)"
        />
        <button
          v-if="!disabled"
          type="button"
          class="btn btn-outline-danger"
          :aria-label="`Retirer le champ ${definition(item.field).name}`"
          @click="remove(index)"
        >
          ×
        </button>
      </div>
    </div>
    <select v-if="!disabled && available.length" class="form-select mt-2" aria-label="Ajouter un champ personnalisé" @change="add">
      <option value="">Ajouter un champ…</option>
      <option v-for="field in available" :key="field.id" :value="field.id">{{ field.name }}</option>
    </select>
  </fieldset>
</template>

<style scoped>
.champ {
  margin-bottom: 0.75rem;
}

.devise {
  flex: 0 0 5rem;
  text-transform: uppercase;
}
</style>
