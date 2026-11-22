<script setup lang="ts">
import { computed, onMounted, ref, toRaw } from 'vue'
import {
  getPermissions,
  listGroups,
  listUsers,
  updatePermissions,
  type DocumentPermissions,
  type Grantees,
  type NamedItem,
  type PaperlessUser,
} from '../api/documents'
import { ApiError } from '../api/http'
import { useAuthStore } from '../stores/auth'

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

const ACTIONS = [
  { key: 'view', label: 'Peuvent consulter' },
  { key: 'change', label: 'Peuvent modifier' },
] as const

const editable = computed(() => current.value !== null && (current.value.owner === null || current.value.owner === auth.user?.id))
const dirty = computed(() => JSON.stringify(current.value) !== JSON.stringify(draft.value))

const userName = (id: number) => {
  const user = users.value.find((u) => u.id === id)
  if (!user) return id === auth.user?.id ? auth.user.username : `Utilisateur n° ${id}`
  const full = [user.first_name, user.last_name].filter(Boolean).join(' ')
  return full ? `${full} (${user.username})` : user.username
}
const groupName = (id: number) => groups.value.find((g) => g.id === id)?.name ?? `Groupe n° ${id}`

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

function grantees(action: 'view' | 'change'): Grantees {
  return draft.value!.permissions[action]
}

function add(action: 'view' | 'change', event: Event) {
  const select = event.target as HTMLSelectElement
  const [kind, id] = select.value.split(':')
  select.value = ''
  if (!kind || !id) return
  const list = grantees(action)[kind as keyof Grantees]
  if (!list.includes(Number(id))) list.push(Number(id))
  saved.value = false
}

function remove(action: 'view' | 'change', kind: keyof Grantees, id: number) {
  const target = grantees(action)
  target[kind] = target[kind].filter((value) => value !== id)
  saved.value = false
}

function setOwner(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  draft.value!.owner = value ? Number(value) : null
  saved.value = false
}

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

      <div>
        <label class="form-label" for="proprietaire">Propriétaire</label>
        <select id="proprietaire" class="form-select" :disabled="!editable" :value="draft.owner ?? ''" @change="setOwner">
          <option value="">(aucun : visible par tous)</option>
          <option v-if="draft.owner !== null && !users.some((u) => u.id === draft!.owner)" :value="draft.owner">
            {{ userName(draft.owner) }}
          </option>
          <option v-for="user in users" :key="user.id" :value="user.id">{{ userName(user.id) }}</option>
        </select>
      </div>

      <fieldset v-for="action in ACTIONS" :key="action.key" :data-action="action.key">
        <legend class="form-label fs-6 mb-1">{{ action.label }}</legend>
        <ul class="list-unstyled d-flex flex-wrap gap-2 mb-2">
          <li v-for="id in grantees(action.key).users" :key="`u${id}`" class="badge rounded-pill text-bg-secondary beneficiaire">
            {{ userName(id) }}
            <button
              v-if="editable"
              type="button"
              class="btn-close btn-close-white ms-1"
              :aria-label="`Retirer ${userName(id)}`"
              @click="remove(action.key, 'users', id)"
            />
          </li>
          <li v-for="id in grantees(action.key).groups" :key="`g${id}`" class="badge rounded-pill text-bg-info beneficiaire">
            Groupe {{ groupName(id) }}
            <button
              v-if="editable"
              type="button"
              class="btn-close ms-1"
              :aria-label="`Retirer le groupe ${groupName(id)}`"
              @click="remove(action.key, 'groups', id)"
            />
          </li>
          <li v-if="!grantees(action.key).users.length && !grantees(action.key).groups.length" class="text-body-secondary small">
            Personne en dehors du propriétaire
          </li>
        </ul>
        <select
          v-if="editable && (users.length || groups.length)"
          class="form-select"
          :aria-label="`Ajouter : ${action.label.toLocaleLowerCase('fr')}`"
          @change="add(action.key, $event)"
        >
          <option value="">Ajouter une personne ou un groupe…</option>
          <optgroup v-if="groups.length" label="Groupes">
            <option v-for="group in groups" :key="group.id" :value="`groups:${group.id}`">{{ group.name }}</option>
          </optgroup>
          <optgroup v-if="users.length" label="Personnes">
            <option v-for="user in users" :key="user.id" :value="`users:${user.id}`">{{ userName(user.id) }}</option>
          </optgroup>
        </select>
      </fieldset>

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

<style scoped>
.beneficiaire {
  display: inline-flex;
  align-items: center;
  font-weight: 500;
}

.beneficiaire .btn-close {
  width: 0.6em;
  height: 0.6em;
}
</style>
