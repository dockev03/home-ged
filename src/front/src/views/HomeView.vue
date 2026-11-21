<script setup lang="ts">
import { NEmpty, NText } from 'naive-ui'
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
  <main class="accueil">
    <h1>Home-GED</h1>
    <NEmpty description="La recherche arrive bientôt." />
    <NText v-if="version" depth="3" class="version">Version {{ version }}</NText>
  </main>
</template>

<style scoped>
.accueil {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1.5rem;
  padding: 2rem 1rem;
}

.version {
  font-size: 0.8rem;
}
</style>
