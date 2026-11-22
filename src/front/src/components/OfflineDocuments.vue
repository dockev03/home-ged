<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { formatDate, formatDateTime } from '../format/dates'
import { listOfflineDocuments, MAX_OFFLINE_DOCUMENTS, type OfflineDocument } from '../offline/documents'
import { useAuthStore } from '../stores/auth'

/** Documents lisibles hors ligne (PWA-09) : les derniers consultés sur cet appareil. */
const auth = useAuthStore()
const documents = ref<OfflineDocument[] | null>(null)

onMounted(async () => {
  documents.value = auth.user ? await listOfflineDocuments(auth.user.id) : []
})
</script>

<template>
  <section class="card" aria-labelledby="hors-ligne-titre">
    <div class="card-header">
      <h2 id="hors-ligne-titre" class="h6 mb-0">Disponibles hors ligne</h2>
    </div>
    <p v-if="documents && !documents.length" class="card-body text-body-secondary mb-0">
      Aucun document n'est disponible hors ligne. Les {{ MAX_OFFLINE_DOCUMENTS }} derniers documents ouverts avec une
      connexion restent lisibles sur cet appareil.
    </p>
    <ul v-else-if="documents" class="list-group list-group-flush">
      <li v-for="d in documents" :key="d.document.id" class="list-group-item">
        <RouterLink :to="{ name: 'document', params: { id: d.document.id } }" class="fw-semibold titre">
          {{ d.document.title }}
        </RouterLink>
        <small class="d-block text-body-secondary">
          {{ formatDate(d.document.created) }} · copie du {{ formatDateTime(d.savedAt) }}
        </small>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.titre {
  overflow-wrap: anywhere;
  text-decoration: none;
}
</style>
