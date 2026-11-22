<script setup lang="ts">
import type { Tag } from '../api/documents'

/** Badge d'étiquette aux couleurs définies dans Paperless (UI-02). */
defineProps<{ tag: Tag }>()
</script>

<template>
  <span class="badge rounded-pill etiquette" :style="{ backgroundColor: tag.color, color: tag.text_color }">
    {{ tag.name }}
  </span>
</template>

<style scoped>
.etiquette {
  font-weight: 500;
}
</style>
