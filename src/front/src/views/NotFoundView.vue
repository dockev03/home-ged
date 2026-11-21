<script setup lang="ts">
import { NButton, NResult } from 'naive-ui'
</script>

<template>
  <main class="introuvable">
    <NResult status="404" title="Page introuvable" description="Cette adresse ne correspond à aucune page.">
      <template #footer>
        <RouterLink v-slot="{ navigate }" to="/" custom>
          <NButton type="primary" @click="navigate">Retour à l'accueil</NButton>
        </RouterLink>
      </template>
    </NResult>
  </main>
</template>

<style scoped>
.introuvable {
  padding: 2rem 1rem;
}
</style>
