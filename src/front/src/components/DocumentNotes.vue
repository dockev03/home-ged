<script setup lang="ts">
import { computed, ref } from 'vue'
import { addNote, deleteNote, type Note } from '../api/documents'
import { ApiError } from '../api/http'
import { formatDateTime, formatRelative } from '../format/dates'

/** Onglet Notes : texte brut (jamais interprété), les plus récentes d'abord. */
const props = defineProps<{ documentId: number; disabled?: boolean }>()
const notes = defineModel<Note[]>({ required: true })

const text = ref('')
const busy = ref(false)
const error = ref<string | null>(null)
const sorted = computed(() => [...notes.value].sort((a, b) => Date.parse(b.created) - Date.parse(a.created)))

const author = (note: Note) =>
  [note.user?.first_name, note.user?.last_name].filter(Boolean).join(' ') || note.user?.username || 'Inconnu'

async function run(action: () => Promise<Note[]>) {
  busy.value = true
  error.value = null
  try {
    notes.value = await action()
    return true
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Action impossible, vérifiez votre connexion.'
    return false
  } finally {
    busy.value = false
  }
}

async function add() {
  if (!text.value.trim() || busy.value) return
  if (await run(() => addNote(props.documentId, text.value.trim()))) text.value = ''
}
</script>

<template>
  <div class="notes">
    <form v-if="!disabled" class="mb-3" @submit.prevent="add">
      <label class="form-label" for="nouvelle-note">Nouvelle note</label>
      <textarea id="nouvelle-note" v-model="text" class="form-control mb-2" rows="3" maxlength="10000" />
      <button type="submit" class="btn btn-primary" :disabled="busy || !text.trim()">Ajouter la note</button>
    </form>
    <p v-if="error" class="alert alert-danger" role="alert">{{ error }}</p>
    <p v-if="notes.length === 0" class="text-body-secondary">Aucune note.</p>
    <ul class="list-unstyled d-flex flex-column gap-2 mb-0">
      <li v-for="note in sorted" :key="note.id" class="card" :data-note="note.id">
        <div class="card-body py-2">
          <p class="texte mb-1">{{ note.note }}</p>
          <div class="d-flex align-items-center gap-2 small text-body-secondary">
            <span>{{ author(note) }}</span>
            <span :title="formatDateTime(note.created)">{{ formatRelative(note.created) }}</span>
            <button
              v-if="!disabled"
              type="button"
              class="btn btn-sm btn-link text-danger ms-auto"
              :disabled="busy"
              @click="run(() => deleteNote(documentId, note.id))"
            >
              Supprimer
            </button>
          </div>
        </div>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.texte {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
