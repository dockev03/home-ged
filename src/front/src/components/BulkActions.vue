<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  bulkDownload,
  bulkEdit,
  listGroups,
  listUsers,
  type ArchiveContent,
  type BulkAction,
  type CustomFieldValue,
  type DocumentPermissions,
  type NamedItem,
  type PaperlessUser,
} from '../api/documents'
import { ApiError } from '../api/http'
import { useReferencesStore } from '../stores/references'
import BaseDropdown from './BaseDropdown.vue'
import ConfirmDialog from './ConfirmDialog.vue'
import CustomFieldsEditor from './CustomFieldsEditor.vue'
import PermissionsEditor from './PermissionsEditor.vue'
import ReferenceSelect from './ReferenceSelect.vue'
import TagPicker from './TagPicker.vue'

/**
 * Barre de la sélection multiple (UI-06) : sélection de la page ou de tous les résultats, puis
 * action en masse sur la sélection (étiquettes, correspondant, type, chemin, champs personnalisés,
 * permissions, téléchargement, suppression).
 */
const props = defineProps<{
  selected: number[]
  /** Nombre total de résultats des filtres en cours. */
  count: number
  /** Mobile : barre collée en bas de l'écran, à portée de pouce ; sinon en haut de la liste. */
  bottom?: boolean
}>()
const emit = defineEmits<{
  selectPage: []
  selectAll: []
  clear: []
  /** Action terminée : message de compte rendu ; `modified` si la liste doit être rechargée. */
  applied: [message: string, modified: boolean]
}>()

type Dialog = 'tags' | 'correspondants' | 'types' | 'chemins' | 'champs' | 'permissions' | 'telecharger' | 'supprimer'

const ACTIONS: { value: Dialog; label: string }[] = [
  { value: 'tags', label: 'Étiquettes…' },
  { value: 'correspondants', label: 'Correspondant…' },
  { value: 'types', label: 'Type de document…' },
  { value: 'chemins', label: 'Chemin de stockage…' },
  { value: 'champs', label: 'Champs personnalisés…' },
  { value: 'permissions', label: 'Permissions…' },
  { value: 'telecharger', label: 'Télécharger…' },
]

const REFERENCE_LABELS = {
  correspondants: 'Correspondant',
  types: 'Type de document',
  chemins: 'Chemin de stockage',
} as const

const references = useReferencesStore()
const dialog = ref<Dialog | null>(null)
const busy = ref(false)
const error = ref<string | null>(null)

// Formulaires des fenêtres, remis à zéro à chaque ouverture.
const addTags = ref<number[]>([])
const removeTags = ref<number[]>([])
const reference = ref<number | null>(null)
const addFields = ref<CustomFieldValue[]>([])
const removeFields = ref<number[]>([])
const permissions = ref<DocumentPermissions>(emptyPermissions())
const merge = ref(true)
const users = ref<PaperlessUser[]>([])
const groups = ref<NamedItem[]>([])
const archive = ref<ArchiveContent>('archive')

function emptyPermissions(): DocumentPermissions {
  return { owner: null, permissions: { view: { users: [], groups: [] }, change: { users: [], groups: [] } } }
}

const n = computed(() => props.selected.length)
const documentsLabel = computed(() => `${n.value} document${n.value > 1 ? 's' : ''}`)

async function open(target: Dialog) {
  addTags.value = []
  removeTags.value = []
  reference.value = null
  addFields.value = []
  removeFields.value = []
  permissions.value = emptyPermissions()
  merge.value = true
  archive.value = 'archive'
  error.value = null
  dialog.value = target
  if (target === 'permissions') {
    const [u, g] = await Promise.allSettled([listUsers(), listGroups()])
    users.value = u.status === 'fulfilled' ? u.value : []
    groups.value = g.status === 'fulfilled' ? g.value : []
  }
}

function close() {
  if (!busy.value) dialog.value = null
}

const title = computed(() => {
  switch (dialog.value) {
    case 'tags':
      return `Étiquettes de ${documentsLabel.value}`
    case 'correspondants':
    case 'types':
    case 'chemins':
      return `${REFERENCE_LABELS[dialog.value]} de ${documentsLabel.value}`
    case 'champs':
      return `Champs personnalisés de ${documentsLabel.value}`
    case 'permissions':
      return `Permissions de ${documentsLabel.value}`
    case 'telecharger':
      return `Télécharger ${documentsLabel.value}`
    case 'supprimer':
      return `Supprimer ${documentsLabel.value} ?`
    default:
      return ''
  }
})

const confirmLabel = computed(() =>
  dialog.value === 'supprimer' ? 'Supprimer' : dialog.value === 'telecharger' ? 'Télécharger' : 'Appliquer',
)

const incomplete = computed(
  () =>
    (dialog.value === 'tags' && addTags.value.length + removeTags.value.length === 0) ||
    (dialog.value === 'champs' && addFields.value.length + removeFields.value.length === 0),
)

function action(): BulkAction | null {
  switch (dialog.value) {
    case 'tags':
      return { method: 'modify_tags', parameters: { add_tags: addTags.value, remove_tags: removeTags.value } }
    case 'correspondants':
      return { method: 'set_correspondent', parameters: { correspondent: reference.value } }
    case 'types':
      return { method: 'set_document_type', parameters: { document_type: reference.value } }
    case 'chemins':
      return { method: 'set_storage_path', parameters: { storage_path: reference.value } }
    case 'champs':
      return {
        method: 'modify_custom_fields',
        parameters: { add_custom_fields: addFields.value, remove_custom_fields: removeFields.value },
      }
    case 'permissions':
      return {
        method: 'set_permissions',
        parameters: { owner: permissions.value.owner, set_permissions: permissions.value.permissions, merge: merge.value },
      }
    case 'supprimer':
      return { method: 'delete', parameters: {} }
    default:
      return null
  }
}

/** Enregistre l'archive reçue sous son nom (lien de téléchargement temporaire). */
function saveFile(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

async function apply() {
  if (busy.value || incomplete.value || n.value === 0) return
  busy.value = true
  error.value = null
  const documents = [...props.selected]
  const label = `${documents.length} document${documents.length > 1 ? 's' : ''}`
  try {
    if (dialog.value === 'telecharger') {
      const { blob, fileName } = await bulkDownload(documents, archive.value)
      saveFile(blob, fileName ?? 'documents.zip')
      emit('applied', `Archive de ${label} téléchargée.`, false)
    } else {
      const bulk = action()
      if (!bulk) return
      await bulkEdit(documents, bulk)
      emit(
        'applied',
        bulk.method === 'delete' ? `${label} placé${documents.length > 1 ? 's' : ''} dans la corbeille.` : `${label} modifié${documents.length > 1 ? 's' : ''}.`,
        true,
      )
    }
    dialog.value = null
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Action impossible, vérifiez votre connexion.'
  } finally {
    busy.value = false
  }
}

function toggleRemoveField(id: number) {
  removeFields.value = removeFields.value.includes(id)
    ? removeFields.value.filter((value) => value !== id)
    : [...removeFields.value, id]
}
</script>

<template>
  <div class="masse" :class="{ bas: bottom }" role="region" aria-label="Sélection">
    <span class="fw-semibold me-auto" aria-live="polite">
      {{ n }} sélectionné{{ n > 1 ? 's' : '' }}<span v-if="!bottom" class="text-body-secondary fw-normal"> sur {{ count }}</span>
    </span>

    <BaseDropdown button-class="btn btn-sm btn-outline-secondary" :up="bottom" aria-label="Sélectionner">
      <template #toggle>Sélectionner</template>
      <template #default="{ close: closeMenu }">
        <button type="button" class="dropdown-item" role="menuitem" @click="closeMenu(); emit('selectPage')">
          Les documents affichés
        </button>
        <button type="button" class="dropdown-item" role="menuitem" @click="closeMenu(); emit('selectAll')">
          Tous les résultats ({{ count }})
        </button>
        <button type="button" class="dropdown-item" role="menuitem" @click="closeMenu(); emit('clear')">Aucun</button>
      </template>
    </BaseDropdown>

    <BaseDropdown button-class="btn btn-sm btn-primary" end :up="bottom" aria-label="Actions sur la sélection">
      <template #toggle>Actions</template>
      <template #default="{ close: closeMenu }">
        <button
          v-for="item in ACTIONS"
          :key="item.value"
          type="button"
          class="dropdown-item"
          role="menuitem"
          :data-action="item.value"
          @click="closeMenu(); open(item.value)"
        >
          {{ item.label }}
        </button>
        <hr class="dropdown-divider" />
        <button type="button" class="dropdown-item text-danger" role="menuitem" data-action="supprimer" @click="closeMenu(); open('supprimer')">
          Supprimer…
        </button>
      </template>
    </BaseDropdown>
  </div>

  <ConfirmDialog
    :open="dialog !== null"
    :title="title"
    :confirm-label="confirmLabel"
    :danger="dialog === 'supprimer'"
    :busy="busy"
    :disabled="incomplete"
    @confirm="apply"
    @cancel="close"
  >
    <p v-if="error" class="alert alert-danger" role="alert">{{ error }}</p>

    <div v-if="dialog === 'tags'" class="d-flex flex-column gap-3">
      <TagPicker id="masse-ajout-etiquettes" v-model="addTags" label="Ajouter" />
      <TagPicker id="masse-retrait-etiquettes" v-model="removeTags" label="Retirer" />
    </div>

    <div v-else-if="dialog === 'correspondants' || dialog === 'types' || dialog === 'chemins'">
      <ReferenceSelect :id="`masse-${dialog}`" v-model="reference" :kind="dialog" :label="REFERENCE_LABELS[dialog]" />
      <p class="form-text mb-0">« (aucun) » retire la valeur actuelle des documents sélectionnés.</p>
    </div>

    <div v-else-if="dialog === 'champs'" class="d-flex flex-column gap-3">
      <CustomFieldsEditor v-model="addFields" />
      <fieldset v-if="references.lists.champs.length">
        <legend class="form-label fs-6 mb-1">Retirer</legend>
        <div v-for="field in references.lists.champs" :key="field.id" class="form-check">
          <input
            :id="`masse-retrait-champ-${field.id}`"
            type="checkbox"
            class="form-check-input"
            :checked="removeFields.includes(field.id)"
            @change="toggleRemoveField(field.id)"
          />
          <label class="form-check-label" :for="`masse-retrait-champ-${field.id}`">{{ field.name }}</label>
        </div>
      </fieldset>
    </div>

    <div v-else-if="dialog === 'permissions'" class="d-flex flex-column gap-3">
      <PermissionsEditor v-model="permissions" id-prefix="masse" :users="users" :groups="groups" />
      <div class="form-check form-switch">
        <input id="masse-fusion" v-model="merge" type="checkbox" class="form-check-input" role="switch" />
        <label class="form-check-label" for="masse-fusion">Ajouter aux droits existants (sinon, les remplacer)</label>
      </div>
      <p class="form-text mb-0">
        Paperless n'accepte ce changement que si vous êtes propriétaire de tous les documents sélectionnés.
      </p>
    </div>

    <fieldset v-else-if="dialog === 'telecharger'">
      <legend class="form-label fs-6">Contenu de l'archive</legend>
      <div class="form-check">
        <input id="masse-archive" v-model="archive" type="radio" class="form-check-input" value="archive" />
        <label class="form-check-label" for="masse-archive">Versions archivées (PDF avec texte reconnu)</label>
      </div>
      <div class="form-check">
        <input id="masse-originaux" v-model="archive" type="radio" class="form-check-input" value="originaux" />
        <label class="form-check-label" for="masse-originaux">Fichiers d'origine</label>
      </div>
      <div class="form-check">
        <input id="masse-les-deux" v-model="archive" type="radio" class="form-check-input" value="les_deux" />
        <label class="form-check-label" for="masse-les-deux">Les deux</label>
      </div>
    </fieldset>

    <p v-else-if="dialog === 'supprimer'" class="mb-0">
      {{ documentsLabel }} {{ n > 1 ? 'seront placés' : 'sera placé' }} dans la corbeille de Paperless.
    </p>
  </ConfirmDialog>
</template>

<style scoped>
.masse {
  position: sticky;
  top: 0;
  z-index: 10;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--bs-border-color);
  border-radius: var(--bs-border-radius);
  background: var(--bs-tertiary-bg);
}

.masse.bas {
  top: auto;
  bottom: 0;
  padding-bottom: max(0.5rem, env(safe-area-inset-bottom));
  box-shadow: 0 -0.25rem 0.75rem rgb(0 0 0 / 0.08);
}

/* Cibles tactiles d'au moins 44 px. */
.masse.bas :deep(.btn) {
  min-height: 2.75rem;
}
</style>
