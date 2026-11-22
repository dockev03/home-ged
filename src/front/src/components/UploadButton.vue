<script setup lang="ts">
import { useTemplateRef } from 'vue'
import { useUploadsStore } from '../stores/uploads'

/** Bouton « Ajouter » : choix d'un ou plusieurs fichiers sur l'appareil, envoyés à Paperless (PWA-07). */
defineProps<{ buttonClass?: string }>()

const uploads = useUploadsStore()
const input = useTemplateRef<HTMLInputElement>('input')

function pick(event: Event) {
  const target = event.target as HTMLInputElement
  if (target.files?.length) uploads.add([...target.files])
  // Même fichier choisi deux fois de suite : l'événement change doit repartir.
  target.value = ''
}
</script>

<template>
  <button type="button" :class="buttonClass ?? 'btn btn-primary'" @click="input?.click()">
    <span aria-hidden="true">+</span>&nbsp;Ajouter
  </button>
  <input
    ref="input"
    type="file"
    class="d-none"
    multiple
    aria-label="Fichiers à ajouter"
    data-upload-input
    @change="pick"
  />
</template>
