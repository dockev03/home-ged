<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'
import { clampPoint, CORNER_LABELS, type Point, type Quad } from '../scan/geometry'

/**
 * Ajustement manuel des 4 coins d'une page (SCAN-02) : poignées déplaçables au doigt, à la souris
 * ou au clavier (flèches, Maj pour aller plus vite). Coordonnées dans l'espace de l'image.
 */
const props = defineProps<{
  src: string
  width: number
  height: number
}>()

const corners = defineModel<Quad>({ required: true })

const svg = useTemplateRef<SVGSVGElement>('svg')
/** Coin en cours de déplacement au pointeur. */
const dragging = ref<number | null>(null)

const polygon = computed(() => corners.value.map((p) => `${p.x},${p.y}`).join(' '))

/** Taille d'un pixel écran dans l'espace de l'image : les poignées gardent leur taille à l'écran. */
const unit = ref(1)

function measure() {
  const rect = svg.value?.getBoundingClientRect()
  if (rect?.width) unit.value = props.width / rect.width
}

function move(index: number, point: Point) {
  const next = [...corners.value] as Quad
  next[index] = clampPoint(point, props.width, props.height)
  corners.value = next
}

function toImage(event: PointerEvent): Point | null {
  const rect = svg.value?.getBoundingClientRect()
  if (!rect?.width || !rect.height) return null
  return {
    x: ((event.clientX - rect.left) / rect.width) * props.width,
    y: ((event.clientY - rect.top) / rect.height) * props.height,
  }
}

function onPointerDown(index: number, event: PointerEvent) {
  measure()
  dragging.value = index
  ;(event.currentTarget as Element).setPointerCapture?.(event.pointerId)
  ;(event.currentTarget as SVGElement).focus?.()
}

function onPointerMove(event: PointerEvent) {
  if (dragging.value === null) return
  const point = toImage(event)
  if (point) move(dragging.value, point)
}

function onPointerUp() {
  dragging.value = null
}

const ARROWS: Record<string, [number, number]> = {
  ArrowLeft: [-1, 0],
  ArrowRight: [1, 0],
  ArrowUp: [0, -1],
  ArrowDown: [0, 1],
}

function onKeyDown(index: number, event: KeyboardEvent) {
  const arrow = ARROWS[event.key]
  if (!arrow) return
  event.preventDefault()
  // 1 % de la largeur par appui, 5 % avec Maj.
  const step = (event.shiftKey ? 0.05 : 0.01) * Math.max(props.width, props.height)
  const corner = corners.value[index]
  move(index, { x: corner.x + arrow[0] * step, y: corner.y + arrow[1] * step })
}

/** Rayon visible et rayon de la zone de saisie (44 px de diamètre au moins, §4). */
const handleRadius = computed(() => 10 * unit.value)
const hitRadius = computed(() => 24 * unit.value)
const stroke = computed(() => 2 * unit.value)
</script>

<template>
  <div class="recadrage">
    <img :src="src" :width="width" :height="height" alt="Photo à recadrer" class="photo" draggable="false" @load="measure" />
    <svg
      ref="svg"
      class="calque"
      :viewBox="`0 0 ${width} ${height}`"
      preserveAspectRatio="none"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @pointercancel="onPointerUp"
    >
      <polygon class="cadre" :points="polygon" :stroke-width="stroke" />
      <g
        v-for="(corner, index) in corners"
        :key="index"
        class="poignee"
        :class="{ active: dragging === index }"
        tabindex="0"
        role="button"
        :aria-label="`Coin ${CORNER_LABELS[index]} : déplacer avec les flèches`"
        :data-coin="index"
        @pointerdown.prevent="onPointerDown(index, $event)"
        @keydown="onKeyDown(index, $event)"
      >
        <circle class="zone" :cx="corner.x" :cy="corner.y" :r="hitRadius" />
        <circle class="point" :cx="corner.x" :cy="corner.y" :r="handleRadius" :stroke-width="stroke" />
      </g>
    </svg>
  </div>
</template>

<style scoped>
.recadrage {
  position: relative;
  display: inline-block;
  max-width: 100%;
  line-height: 0;
  user-select: none;
}

.photo {
  display: block;
  max-width: 100%;
  max-height: 70vh;
  width: auto;
  height: auto;
}

.calque {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  overflow: visible;
  /* Le geste déplace la poignée, pas la page. */
  touch-action: none;
}

.cadre {
  fill: rgba(var(--bs-primary-rgb), 0.15);
  stroke: var(--bs-primary);
}

.zone {
  fill: transparent;
  cursor: grab;
}

.point {
  fill: var(--bs-body-bg);
  stroke: var(--bs-primary);
  pointer-events: none;
}

.poignee.active .zone {
  cursor: grabbing;
}

.poignee:focus {
  outline: none;
}

.poignee:focus-visible .point,
.poignee.active .point {
  fill: var(--bs-primary);
}
</style>
