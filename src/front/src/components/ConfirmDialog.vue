<script setup lang="ts">
import { nextTick, useTemplateRef, watch } from 'vue'

/** Fenêtre de confirmation (modale Bootstrap pilotée par Vue). */
const props = defineProps<{
  open: boolean
  title: string
  confirmLabel: string
  cancelLabel?: string
  danger?: boolean
  busy?: boolean
  /** Confirmation impossible tant que le formulaire est incomplet. */
  disabled?: boolean
}>()
const emit = defineEmits<{ confirm: []; cancel: [] }>()
const cancelButton = useTemplateRef<HTMLButtonElement>('cancelButton')

// Focus sur « Annuler » : une validation par erreur (Entrée) ne détruit rien.
watch(
  () => props.open,
  async (open) => {
    if (!open) return
    await nextTick()
    cancelButton.value?.focus()
  },
)
</script>

<template>
  <template v-if="open">
    <div
      class="modal d-block"
      tabindex="-1"
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirmation-titre"
      @keydown.esc="emit('cancel')"
      @click.self="emit('cancel')"
    >
      <div class="modal-dialog modal-dialog-centered modal-dialog-scrollable">
        <div class="modal-content">
          <div class="modal-header">
            <h2 id="confirmation-titre" class="modal-title fs-5">{{ title }}</h2>
          </div>
          <div class="modal-body"><slot /></div>
          <div class="modal-footer">
            <button ref="cancelButton" type="button" class="btn btn-outline-secondary" @click="emit('cancel')">
              {{ cancelLabel ?? 'Annuler' }}
            </button>
            <button
              type="button"
              class="btn"
              :class="danger ? 'btn-danger' : 'btn-primary'"
              :disabled="busy || disabled"
              @click="emit('confirm')"
            >
              <span v-if="busy" class="spinner-border spinner-border-sm me-1" aria-hidden="true" />
              {{ confirmLabel }}
            </button>
          </div>
        </div>
      </div>
    </div>
    <div class="modal-backdrop fade show" />
  </template>
</template>
