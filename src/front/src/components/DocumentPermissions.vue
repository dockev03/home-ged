<script setup lang="ts">
import { computed, onMounted, ref, toRaw } from 'vue'
import {
  getPermissions,
  listGroups,
  listUsers,
  updatePermissions,
  type DocumentPermissions,
  type NamedItem,
  type PaperlessUser,
} from '../api/documents'
import { ApiError } from '../api/http'
import { useAuthStore } from '../stores/auth'
import PermissionsEditor from './PermissionsEditor.vue'

/**
 * Onglet Permissions : propriétaire, droits de lecture et de modification (personnes, groupes).
 * Paperless n'en accepte la modification que du propriétaire (ou d'un administrateur).
 */
const props = defineProps<{ documentId: number }>()

const auth = useAuthStore()
const current = ref<DocumentPermissions | null>(null)
const draft = ref<DocumentPermissions | null>(null)
const users = ref<PaperlessUser[]>([])
const groups = ref<NamedItem[]>([])
/** Comptes et groupes illisibles avec ce compte (droits « voir les utilisateurs / groupes » absents). */
const listsUnavailable = ref(false)
const error = ref<string | null>(null)
const saving = ref(false)
const saved = ref(false)

const editable = computed(() => current.value !== null && (current.value.owner === null || current.value.owner === auth.user?.id))
const dirty = computed(() => JSON.stringify(current.value) !== JSON.stringify(draft.value))

function show(permissions: DocumentPermissions) {
  current.value = permissions
  draft.value = structuredClone(permissions)
}

onMounted(async () => {
  try {
    show(await getPermissions(props.documentId))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Permissions indisponibles, vérifiez votre connexion.'
    return
  }
  const [u, g] = await Promise.allSettled([listUsers(), listGroups()])
  if (u.status === 'fulfilled') users.value = u.value
  if (g.status === 'fulfilled') groups.value = g.value
  listsUnavailable.value = u.status === 'rejected' || g.status === 'rejected'
})

function cancel() {
  if (current.value) draft.value = structuredClone(toRaw(current.value))
}

async function save() {
  if (!draft.value || saving.value) return
  saving.value = true
  error.value = null
  try {
    show(await updatePermissions(props.documentId, draft.value.owner, draft.value.permissions))
    saved.value = true
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible, vérifiez votre connexion.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div>
    <p v-if="error" class="alert alert-danger" role="alert">{{ error }}</p>
    <div v-if="draft" class="d-flex flex-column gap-3">
      <p v-if="!editable" class="alert alert-info mb-0">
        Seul le propriétaire du document peut modifier ses permissions (ou un administrateur, depuis Paperless).
      </p>
      <p v-if="listsUnavailable && editable" class="alert alert-warning mb-0 small">
        Votre compte ne peut pas lire la liste des personnes et des groupes. Un administrateur peut accorder les droits
        « voir les utilisateurs » et « voir les groupes » dans Paperless.
      </p>

      <PermissionsEditor
        v-model="draft"
        id-prefix="fiche"
        :users="users"
        :groups="groups"
        :disabled="!editable"
        @update:model-value="saved = false"
      />

      <div v-if="editable" class="d-flex align-items-center gap-2 justify-content-end">
        <span v-if="saved && !dirty" class="text-success me-auto" role="status">Permissions enregistrées.</span>
        <button type="button" class="btn btn-outline-secondary" :disabled="!dirty || saving" @click="cancel">
          Annuler
        </button>
        <button type="button" class="btn btn-primary" :disabled="!dirty || saving" @click="save">Enregistrer les permissions</button>
      </div>
    </div>
    <div v-else-if="!error" class="text-center p-3">
      <span class="spinner-border spinner-border-sm" role="status"><span class="visually-hidden">Chargement…</span></span>
    </div>
  </div>
</template>
