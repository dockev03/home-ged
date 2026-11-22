<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getAppInfo } from '../api/http'
import UploadButton from '../components/UploadButton.vue'

const version = ref<string | null>(null)

onMounted(async () => {
  try {
    version.value = (await getAppInfo()).version
  } catch {
    version.value = null
  }
})
</script>

<template>
  <div class="accueil">
    <h1 class="h3">Tableau de bord</h1>
    <p class="text-body-secondary">
      Les widgets (vues sauvegardées, derniers ajouts, échéances) arrivent à l'étape 2.
    </p>
    <div class="d-flex flex-wrap gap-2">
      <UploadButton />
      <RouterLink class="btn btn-outline-primary" :to="{ name: 'documents' }">Parcourir les documents</RouterLink>
    </div>
    <p class="text-body-secondary small mb-0">
      Sur ordinateur, vous pouvez aussi glisser-déposer des fichiers n'importe où dans Home-GED.
    </p>
    <p v-if="version" class="version text-body-secondary">Home-GED, version {{ version }}</p>
  </div>
</template>

<style scoped>
.accueil {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.75rem;
}

.version {
  margin-top: 2rem;
  font-size: 0.8rem;
}
</style>
