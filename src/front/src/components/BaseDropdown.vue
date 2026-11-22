<script setup lang="ts">
import { onBeforeUnmount, ref, useTemplateRef, watch } from 'vue'

/**
 * Menu déroulant Bootstrap piloté par Vue (sans le JavaScript de Bootstrap ni Popper) :
 * fermeture au clic extérieur et sur Échap, focus rendu au bouton.
 */
const props = defineProps<{
  /** Classes du bouton (ex. « btn btn-outline-secondary »). */
  buttonClass?: string
  /** Menu aligné à droite du bouton. */
  end?: boolean
  menuClass?: string
  ariaLabel?: string
  /** Rôle du menu : « menu » pour des actions, « dialog » pour un formulaire de filtre. */
  menuRole?: 'menu' | 'dialog'
  /** Menu ouvert au-dessus du bouton (barre collée en bas de l'écran). */
  up?: boolean
}>()

const open = ref(false)
const root = useTemplateRef<HTMLElement>('root')
const toggleButton = useTemplateRef<HTMLButtonElement>('toggleButton')

function close(refocus = false) {
  if (!open.value) return
  open.value = false
  if (refocus) toggleButton.value?.focus()
}

function onPointerDown(event: PointerEvent) {
  if (root.value && !root.value.contains(event.target as Node)) close()
}

watch(open, (value) => {
  if (value) document.addEventListener('pointerdown', onPointerDown)
  else document.removeEventListener('pointerdown', onPointerDown)
})
onBeforeUnmount(() => document.removeEventListener('pointerdown', onPointerDown))

defineExpose({ close })
</script>

<template>
  <div ref="root" :class="props.up ? 'dropup' : 'dropdown'" @keydown.esc.stop="close(true)">
    <button
      ref="toggleButton"
      type="button"
      class="dropdown-toggle"
      :class="props.buttonClass ?? 'btn btn-outline-secondary'"
      :aria-expanded="open"
      :aria-haspopup="props.menuRole ?? 'menu'"
      :aria-label="props.ariaLabel"
      @click="open = !open"
    >
      <slot name="toggle" />
    </button>
    <div
      v-if="open"
      class="dropdown-menu show"
      :class="[props.menuClass, { 'dropdown-menu-end': props.end }]"
      :role="props.menuRole ?? 'menu'"
      data-bs-popper="static"
    >
      <slot :close="close" />
    </div>
  </div>
</template>
