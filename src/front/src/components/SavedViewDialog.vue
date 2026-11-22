<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { ApiError } from '../api/http'
import type { SavedView } from '../api/savedViews'
import type { ViewDefinition } from '../filters/savedViews'
import { useSavedViewsStore } from '../stores/savedViews'

/**
 * Création d'une vue à partir des filtres affichés, ou modification du nom et de l'affichage
 * (tableau de bord, menu) d'une vue existante (UI-08).
 */
const props = defineProps<{
  open: boolean
  /** Vue modifiée ; null pour une création. */
  view: SavedView | null
  /** Critères et tri de la nouvelle vue (création). */
  definition?: ViewDefinition
}>()
const emit = defineEmits<{ saved: [view: SavedView]; cancel: [] }>()

const store = useSavedViewsStore()
const name = ref('')
const dashboard = ref(false)
const sidebar = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)
const nameInput = useTemplateRef<HTMLInputElement>('nameInput')

const readOnly = computed(() => props.view !== null && props.view.user_can_change === false)
const title = computed(() => (props.view ? 'Modifier la vue' : 'Enregistrer comme nouvelle vue'))

watch(
  () => props.open,
  async (open) => {
    if (!open) return
    name.value = props.view?.name ?? ''
    dashboard.value = props.view ? store.onDashboard(props.view.id) : false
    sidebar.value = props.view ? store.inSidebar(props.view.id) : true
    error.value = null
    saving.value = false
    await nextTick()
    nameInput.value?.focus()
  },
  { immediate: true },
)

async function save() {
  const trimmed = name.value.trim()
  if (!trimmed && !readOnly.value) {
    error.value = 'Donnez un nom à la vue.'
    return
  }
  saving.value = true
  error.value = null
  try {
    const visibility = { tableau_de_bord: dashboard.value, menu: sidebar.value }
    const saved = props.view
      ? await store.update(props.view.id, readOnly.value ? visibility : { name: trimmed, ...visibility })
      : await store.create({ name: trimmed, ...props.definition, ...visibility })
    emit('saved', saved)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED. Vérifiez votre connexion.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <template v-if="open">
    <div
      class="modal d-block"
      tabindex="-1"
      role="dialog"
      aria-modal="true"
      aria-labelledby="vue-titre"
      @keydown.esc="emit('cancel')"
    >
      <div class="modal-dialog modal-dialog-centered">
        <form class="modal-content" novalidate @submit.prevent="save">
          <div class="modal-header">
            <h2 id="vue-titre" class="modal-title fs-5">{{ title }}</h2>
            <button type="button" class="btn-close" aria-label="Fermer" @click="emit('cancel')" />
          </div>
          <div class="modal-body d-flex flex-column gap-3">
            <p v-if="readOnly" class="alert alert-info mb-0">
              Cette vue est partagée avec vous : vous pouvez choisir où l'afficher, mais pas la renommer.
            </p>
            <div>
              <label class="form-label" for="vue-nom">Nom</label>
              <input
                id="vue-nom"
                ref="nameInput"
                v-model="name"
                class="form-control"
                maxlength="128"
                required
                autocomplete="off"
                :disabled="readOnly || saving"
              />
            </div>
            <div class="form-check form-switch">
              <input id="vue-tableau" v-model="dashboard" class="form-check-input" type="checkbox" role="switch" :disabled="saving" />
              <label class="form-check-label" for="vue-tableau">Afficher sur le tableau de bord</label>
            </div>
            <div class="form-check form-switch">
              <input id="vue-menu" v-model="sidebar" class="form-check-input" type="checkbox" role="switch" :disabled="saving" />
              <label class="form-check-label" for="vue-menu">Afficher dans le menu</label>
            </div>
            <p class="form-text mb-0">
              La vue est enregistrée dans Paperless. Son affichage sur le tableau de bord et dans le menu ne concerne que vous.
            </p>
            <div v-if="error" class="alert alert-danger mb-0" role="alert">{{ error }}</div>
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-outline-secondary" @click="emit('cancel')">Annuler</button>
            <button type="submit" class="btn btn-primary" :disabled="saving">
              <span v-if="saving" class="spinner-border spinner-border-sm me-1" aria-hidden="true" />
              Enregistrer
            </button>
          </div>
        </form>
      </div>
    </div>
    <div class="modal-backdrop fade show" />
  </template>
</template>
