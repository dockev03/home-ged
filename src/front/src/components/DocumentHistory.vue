<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getHistory, type HistoryEntry } from '../api/documents'
import { ApiError } from '../api/http'
import { describeHistory } from '../documents/history'
import { formatDateTime, formatRelative } from '../format/dates'
import { useAuthStore } from '../stores/auth'
import { useReferencesStore } from '../stores/references'

/** Onglet Historique : journal d'audit Paperless, en lecture seule. */
const props = defineProps<{ documentId: number }>()

const references = useReferencesStore()
const auth = useAuthStore()
const entries = ref<HistoryEntry[] | null>(null)
const error = ref<string | null>(null)

const items = computed(() =>
  describeHistory(entries.value ?? [], {
    correspondent: references.correspondants,
    document_type: references.types,
    storage_path: references.chemins,
    // Propriétaire : seul le nom de l'utilisateur courant est connu à coup sûr.
    owner: auth.user ? new Map([[auth.user.id, { name: auth.user.username }]]) : undefined,
  }),
)

onMounted(async () => {
  try {
    entries.value = await getHistory(props.documentId)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Historique indisponible, vérifiez votre connexion.'
  }
})
</script>

<template>
  <div>
    <p v-if="error" class="alert alert-info">{{ error }}</p>
    <div v-else-if="entries === null" class="text-center p-3">
      <span class="spinner-border spinner-border-sm" role="status"><span class="visually-hidden">Chargement…</span></span>
    </div>
    <p v-else-if="items.length === 0" class="text-body-secondary">Aucune modification enregistrée.</p>
    <ol v-else class="list-unstyled chronologie">
      <li v-for="item in items" :key="item.id" :data-entree="item.id">
        <div class="d-flex flex-wrap gap-2 align-items-baseline">
          <strong>{{ item.action }}</strong>
          <span class="text-body-secondary small">
            par {{ item.actor }}, <span :title="formatDateTime(item.timestamp)">{{ formatRelative(item.timestamp) }}</span>
          </span>
        </div>
        <ul v-if="item.lines.length" class="list-unstyled small mb-0 mt-1 lignes">
          <li v-for="(line, i) in item.lines" :key="i">
            <span class="text-body-secondary">{{ line.label }} :</span>
            <template v-if="line.text !== undefined"> {{ line.text }}</template>
            <template v-else>
              <del class="text-body-secondary">{{ line.before }}</del> → <ins>{{ line.after }}</ins>
            </template>
          </li>
        </ul>
      </li>
    </ol>
  </div>
</template>

<style scoped>
.chronologie > li {
  padding: 0.5rem 0 0.5rem 1rem;
  border-left: 2px solid var(--bs-border-color);
}

.lignes {
  overflow-wrap: anywhere;
}

ins {
  text-decoration: none;
  font-weight: 500;
}
</style>
