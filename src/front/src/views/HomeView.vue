<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getAppInfo } from '../api/http'

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
    <RouterLink class="btn btn-primary" :to="{ name: 'documents' }">Parcourir les documents</RouterLink>
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
