<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { listGroups, listUsers, type DocumentPermissions, type NamedItem, type PaperlessUser } from '../api/documents'
import { createManaged, updateManaged, type ManagedItem, type ManagedUpdate } from '../api/gestion'
import { ApiError } from '../api/http'
import { DATA_TYPES, MATCHING_ALGORITHMS, usesMatchText, type KindConfig } from '../gestion/kinds'
import PermissionsEditor from './PermissionsEditor.vue'

/**
 * Création ou modification d'une étiquette, d'un correspondant, d'un type, d'un chemin ou d'un
 * champ personnalisé (UI-07) : nom, classement automatique, champs propres à la liste et, pour un
 * élément existant, propriétaire et droits.
 */
const props = defineProps<{
  open: boolean
  config: KindConfig
  /** Élément modifié ; null pour une création. */
  item: ManagedItem | null
}>()
const emit = defineEmits<{ saved: [item: ManagedItem]; cancel: [] }>()

interface SelectOption {
  id?: string
  label: string
}

const name = ref('')
const color = ref('#a6cee3')
const isInbox = ref(false)
const algorithm = ref(6)
const match = ref('')
const insensitive = ref(true)
const path = ref('')
const dataType = ref<(typeof DATA_TYPES)[number]['value']>('string')
const options = ref<SelectOption[]>([])
const currency = ref('')
const permissions = ref<DocumentPermissions>({ owner: null, permissions: emptyPermissions() })
let initialPermissions = ''

const users = ref<PaperlessUser[]>([])
const groups = ref<NamedItem[]>([])
const permissionsLoaded = ref(false)

const saving = ref(false)
const error = ref<string | null>(null)
const nameInput = useTemplateRef<HTMLInputElement>('nameInput')

const kind = computed(() => props.config.kind)
const readOnly = computed(() => props.item?.user_can_change === false)
const title = computed(() => (props.item ? `Modifier ${props.config.the}` : props.config.create))
const algorithmHelp = computed(() => MATCHING_ALGORITHMS.find((a) => a.value === algorithm.value)?.help ?? '')
const showPermissions = computed(() => props.config.matchable && props.item !== null && permissionsLoaded.value)

function emptyPermissions(): DocumentPermissions['permissions'] {
  return { view: { users: [], groups: [] }, change: { users: [], groups: [] } }
}

/** Copie profonde (structuredClone refuse les objets réactifs de Vue). */
const copy = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

function randomColor() {
  return `#${Math.floor(Math.random() * 0x1000000)
    .toString(16)
    .padStart(6, '0')}`
}

function reset(item: ManagedItem | null) {
  name.value = item?.name ?? ''
  color.value = item?.color ?? randomColor()
  isInbox.value = item?.is_inbox_tag ?? false
  algorithm.value = item?.matching_algorithm ?? 6
  match.value = item?.match ?? ''
  insensitive.value = item?.is_insensitive ?? true
  path.value = item?.path ?? ''
  dataType.value = item?.data_type ?? 'string'
  options.value = (item?.extra_data?.select_options ?? []).map((o) => ({ ...o }))
  currency.value = item?.extra_data?.default_currency ?? ''
  permissions.value = { owner: item?.owner ?? null, permissions: copy(item?.permissions ?? emptyPermissions()) }
  initialPermissions = JSON.stringify(permissions.value)
  error.value = null
}

async function loadGrantees() {
  if (permissionsLoaded.value) return
  try {
    ;[users.value, groups.value] = await Promise.all([listUsers(), listGroups()])
    permissionsLoaded.value = true
  } catch {
    // Sans la liste des comptes, les permissions se règlent dans Paperless.
    permissionsLoaded.value = false
  }
}

watch(
  () => props.open,
  async (open) => {
    if (!open) return
    reset(props.item)
    if (props.config.matchable && props.item) void loadGrantees()
    await nextTick()
    nameInput.value?.focus()
  },
  { immediate: true },
)

function addOption() {
  options.value = [...options.value, { label: '' }]
}

function removeOption(index: number) {
  options.value = options.value.filter((_, i) => i !== index)
}

function body(): ManagedUpdate {
  const result: ManagedUpdate = { name: name.value.trim() }
  if (props.config.matchable) {
    result.matching_algorithm = algorithm.value
    result.match = usesMatchText(algorithm.value) ? match.value : ''
    result.is_insensitive = insensitive.value
  }
  if (kind.value === 'tags') {
    result.color = color.value
    result.is_inbox_tag = isInbox.value
  }
  if (kind.value === 'chemins') result.path = path.value.trim()
  if (kind.value === 'champs') {
    if (!props.item) result.data_type = dataType.value
    if (dataType.value === 'select') {
      result.extra_data = {
        select_options: options.value
          .map((o) => ({ ...o, label: o.label.trim() }))
          .filter((o) => o.label.length > 0),
      }
    } else if (dataType.value === 'monetary') {
      result.extra_data = { default_currency: currency.value.trim().toUpperCase() || null }
    }
  }
  // Droits envoyés seulement s'ils ont changé : Paperless les réserve au propriétaire.
  if (showPermissions.value && JSON.stringify(permissions.value) !== initialPermissions) {
    result.owner = permissions.value.owner
    result.set_permissions = permissions.value.permissions
  }
  return result
}

const valid = computed(
  () =>
    name.value.trim().length > 0 &&
    (kind.value !== 'chemins' || path.value.trim().length > 0) &&
    (!usesMatchText(algorithm.value) || !props.config.matchable || match.value.trim().length > 0) &&
    (kind.value !== 'champs' || dataType.value !== 'monetary' || /^([A-Za-z]{3})?$/.test(currency.value.trim())),
)

async function save() {
  if (saving.value || !valid.value || readOnly.value) return
  saving.value = true
  error.value = null
  try {
    const saved = props.item
      ? await updateManaged(kind.value, props.item.id, body())
      : await createManaged(kind.value, body())
    emit('saved', saved)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED. Vérifiez votre connexion.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <template v-if="open">
    <div
      class="modal d-block"
      tabindex="-1"
      role="dialog"
      aria-modal="true"
      aria-labelledby="gestion-titre"
      @keydown.esc="emit('cancel')"
    >
      <div class="modal-dialog modal-dialog-centered modal-dialog-scrollable modal-lg">
        <form class="modal-content" novalidate @submit.prevent="save">
          <div class="modal-header">
            <h2 id="gestion-titre" class="modal-title fs-5">{{ title }}</h2>
            <button type="button" class="btn-close" aria-label="Fermer" @click="emit('cancel')" />
          </div>
          <div class="modal-body d-flex flex-column gap-3">
            <p v-if="readOnly" class="alert alert-info mb-0">Vous pouvez consulter cet élément mais pas le modifier.</p>

            <fieldset :disabled="readOnly || saving" class="d-flex flex-column gap-3">
              <div>
                <label class="form-label" for="gestion-nom">Nom</label>
                <input
                  id="gestion-nom"
                  ref="nameInput"
                  v-model="name"
                  class="form-control"
                  maxlength="128"
                  required
                  autocomplete="off"
                />
              </div>

              <div v-if="kind === 'tags'" class="d-flex flex-wrap align-items-end gap-3">
                <div>
                  <label class="form-label" for="gestion-couleur">Couleur</label>
                  <div class="d-flex gap-2">
                    <input id="gestion-couleur" v-model="color" type="color" class="form-control form-control-color" />
                    <button type="button" class="btn btn-outline-secondary" @click="color = randomColor()">Au hasard</button>
                  </div>
                </div>
                <div class="form-check form-switch mb-2">
                  <input id="gestion-boite" v-model="isInbox" class="form-check-input" type="checkbox" role="switch" />
                  <label class="form-check-label" for="gestion-boite">Étiquette de boîte de réception</label>
                  <div class="form-text">Ajoutée à chaque nouveau document, à retirer une fois classé.</div>
                </div>
              </div>

              <div v-if="kind === 'chemins'">
                <label class="form-label" for="gestion-chemin">Chemin</label>
                <input
                  id="gestion-chemin"
                  v-model="path"
                  class="form-control font-monospace"
                  maxlength="2048"
                  required
                  placeholder="{{ correspondent }}/{{ created_year }}/{{ title }}"
                  aria-describedby="gestion-chemin-aide"
                />
                <div id="gestion-chemin-aide" class="form-text">
                  Modèle de chemin des fichiers dans Paperless, par exemple
                  <code v-text="'{{ correspondent }}/{{ created_year }}/{{ title }}'" />.
                </div>
              </div>

              <template v-if="kind === 'champs'">
                <div>
                  <label class="form-label" for="gestion-type">Type de données</label>
                  <select
                    id="gestion-type"
                    v-model="dataType"
                    class="form-select"
                    :disabled="item !== null"
                    aria-describedby="gestion-type-aide"
                  >
                    <option v-for="type in DATA_TYPES" :key="type.value" :value="type.value">{{ type.label }}</option>
                  </select>
                  <div v-if="item" id="gestion-type-aide" class="form-text">Le type d’un champ existant ne peut pas changer.</div>
                </div>

                <fieldset v-if="dataType === 'select'">
                  <legend class="form-label fs-6">Choix proposés</legend>
                  <ul class="list-unstyled d-flex flex-column gap-2 mb-2">
                    <li v-for="(option, index) in options" :key="option.id ?? `nouveau-${index}`" class="d-flex gap-2">
                      <input
                        v-model="option.label"
                        class="form-control"
                        maxlength="128"
                        :aria-label="`Choix ${index + 1}`"
                      />
                      <button
                        type="button"
                        class="btn btn-outline-danger"
                        :aria-label="`Retirer le choix ${option.label || index + 1}`"
                        @click="removeOption(index)"
                      >
                        Retirer
                      </button>
                    </li>
                  </ul>
                  <button type="button" class="btn btn-outline-secondary btn-sm" @click="addOption">+ Ajouter un choix</button>
                  <div v-if="item" class="form-text">Retirer un choix l’efface des documents qui l’utilisent.</div>
                </fieldset>

                <div v-if="dataType === 'monetary'">
                  <label class="form-label" for="gestion-devise">Devise par défaut</label>
                  <input
                    id="gestion-devise"
                    v-model="currency"
                    class="form-control devise text-uppercase"
                    maxlength="3"
                    placeholder="EUR"
                    autocomplete="off"
                  />
                  <div class="form-text">Code ISO à trois lettres ; vide pour la devise de Paperless.</div>
                </div>
              </template>

              <fieldset v-if="config.matchable" class="d-flex flex-column gap-2">
                <legend class="form-label fs-6 mb-0">Classement automatique</legend>
                <div>
                  <label class="form-label visually-hidden" for="gestion-algo">Algorithme de correspondance</label>
                  <select id="gestion-algo" v-model.number="algorithm" class="form-select" aria-describedby="gestion-algo-aide">
                    <option v-for="a in MATCHING_ALGORITHMS" :key="a.value" :value="a.value">{{ a.label }}</option>
                  </select>
                  <div id="gestion-algo-aide" class="form-text">{{ algorithmHelp }}</div>
                </div>
                <template v-if="usesMatchText(algorithm)">
                  <div>
                    <label class="form-label" for="gestion-match">Correspondance</label>
                    <input id="gestion-match" v-model="match" class="form-control" maxlength="256" autocomplete="off" />
                  </div>
                  <div class="form-check">
                    <input id="gestion-casse" v-model="insensitive" class="form-check-input" type="checkbox" />
                    <label class="form-check-label" for="gestion-casse">Ignorer les majuscules et minuscules</label>
                  </div>
                </template>
              </fieldset>

              <fieldset v-if="showPermissions">
                <legend class="form-label fs-6">Permissions</legend>
                <PermissionsEditor v-model="permissions" :users="users" :groups="groups" id-prefix="gestion" />
              </fieldset>
              <p v-else-if="config.matchable && !item" class="form-text mb-0">
                Créé avec vos permissions par défaut de Paperless ; modifiables ensuite.
              </p>
            </fieldset>

            <p v-if="error" class="alert alert-danger mb-0" role="alert">{{ error }}</p>
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-outline-secondary" @click="emit('cancel')">
              {{ readOnly ? 'Fermer' : 'Annuler' }}
            </button>
            <button v-if="!readOnly" type="submit" class="btn btn-primary" :disabled="saving || !valid">
              <span v-if="saving" class="spinner-border spinner-border-sm me-1" aria-hidden="true" />
              Enregistrer
            </button>
          </div>
        </form>
      </div>
    </div>
    <div class="modal-backdrop fade show" />
  </template>
</template>

<style scoped>
.devise {
  max-width: 6rem;
}
</style>
