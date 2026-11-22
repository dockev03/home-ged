<script setup lang="ts">
/** Pages du document scanné (SCAN-04) : vignettes dans l'ordre, déplacement, recadrage, suppression. */
defineProps<{ pages: readonly { id: number; url: string }[]; disabled?: boolean }>()
const emit = defineEmits<{ move: [id: number, delta: -1 | 1]; edit: [id: number]; remove: [id: number] }>()
</script>

<template>
  <ol class="pages list-unstyled" aria-label="Pages du document">
    <li v-for="(page, index) in pages" :key="page.id" class="card" data-test="page">
      <img :src="page.url" :alt="`Page ${index + 1}`" class="card-img-top vignette" />
      <div class="card-body">
        <span class="fw-semibold">Page {{ index + 1 }}</span>
        <div class="outils">
          <button
            type="button"
            class="btn btn-sm btn-outline-secondary"
            :aria-label="`Déplacer la page ${index + 1} avant`"
            title="Déplacer avant"
            :disabled="disabled || index === 0"
            @click="emit('move', page.id, -1)"
          >
            <span aria-hidden="true">◀</span>
          </button>
          <button
            type="button"
            class="btn btn-sm btn-outline-secondary"
            :aria-label="`Déplacer la page ${index + 1} après`"
            title="Déplacer après"
            :disabled="disabled || index === pages.length - 1"
            @click="emit('move', page.id, 1)"
          >
            <span aria-hidden="true">▶</span>
          </button>
          <button
            type="button"
            class="btn btn-sm btn-outline-secondary"
            :aria-label="`Recadrer la page ${index + 1}`"
            :disabled="disabled"
            @click="emit('edit', page.id)"
          >
            Recadrer
          </button>
          <button
            type="button"
            class="btn btn-sm btn-outline-danger"
            :aria-label="`Supprimer la page ${index + 1}`"
            :disabled="disabled"
            @click="emit('remove', page.id)"
          >
            Supprimer
          </button>
        </div>
      </div>
    </li>
  </ol>
</template>

<style scoped>
.pages {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
  gap: 0.75rem;
  margin: 0;
}

.vignette {
  aspect-ratio: 1 / 1.414;
  object-fit: contain;
  background: var(--bs-tertiary-bg);
}

.card-body {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 0.5rem;
}

.outils {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

/* Cibles tactiles ≥ 44 px sur mobile (§4). */
@media (max-width: 767.98px) {
  .outils .btn {
    min-height: 44px;
    min-width: 44px;
    flex: 1 1 auto;
  }
}
</style>
