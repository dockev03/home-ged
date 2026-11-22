<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getMetadata, type DocumentDetail, type DocumentMetadata } from '../api/documents'
import { ApiError } from '../api/http'
import { formatDateTime } from '../format/dates'

/** Onglet Métadonnées : dates, fichiers original et archivé, métadonnées extraites (lecture seule). */
const props = defineProps<{ document: DocumentDetail }>()

const metadata = ref<DocumentMetadata | null>(null)
const error = ref<string | null>(null)

const size = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 1 })
function formatSize(bytes: number | null) {
  if (bytes === null) return 'inconnue'
  if (bytes < 1024) return `${bytes} o`
  if (bytes < 1024 * 1024) return `${size.format(bytes / 1024)} Ko`
  return `${size.format(bytes / 1024 / 1024)} Mo`
}

onMounted(async () => {
  try {
    metadata.value = await getMetadata(props.document.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Métadonnées indisponibles, vérifiez votre connexion.'
  }
})
</script>

<template>
  <div>
    <dl class="infos">
      <dt>Modifié le</dt>
      <dd>{{ formatDateTime(document.modified) }}</dd>
      <dt>Ajouté le</dt>
      <dd>{{ formatDateTime(document.added) }}</dd>
      <dt>Nom du fichier original</dt>
      <dd>{{ document.original_file_name ?? '—' }}</dd>
      <dt>Type MIME</dt>
      <dd>{{ document.mime_type }}</dd>
      <template v-if="metadata">
        <dt>Taille de l'original</dt>
        <dd>{{ formatSize(metadata.original_size) }}</dd>
        <dt>Empreinte de l'original (MD5)</dt>
        <dd class="font-monospace">{{ metadata.original_checksum }}</dd>
        <template v-if="metadata.has_archive_version">
          <dt>Taille de l'archive</dt>
          <dd>{{ formatSize(metadata.archive_size) }}</dd>
          <dt>Empreinte de l'archive (MD5)</dt>
          <dd class="font-monospace">{{ metadata.archive_checksum }}</dd>
        </template>
      </template>
    </dl>
    <p v-if="error" class="alert alert-warning">{{ error }}</p>
    <details v-if="metadata?.original_metadata.length" class="mt-3">
      <summary>Métadonnées du fichier original ({{ metadata.original_metadata.length }})</summary>
      <table class="table table-sm mt-2">
        <tbody>
          <tr v-for="(entry, i) in metadata.original_metadata" :key="i">
            <th scope="row" class="fw-normal text-body-secondary">{{ entry.prefix }}:{{ entry.key }}</th>
            <td class="valeur">{{ entry.value }}</td>
          </tr>
        </tbody>
      </table>
    </details>
  </div>
</template>

<style scoped>
.infos {
  display: grid;
  grid-template-columns: minmax(8rem, auto) 1fr;
  gap: 0.35rem 1rem;
  margin: 0;
}

.infos dt {
  font-weight: normal;
  color: var(--bs-secondary-color);
}

.infos dd,
.valeur {
  margin: 0;
  overflow-wrap: anywhere;
}
</style>
