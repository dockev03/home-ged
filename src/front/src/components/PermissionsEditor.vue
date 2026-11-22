<script setup lang="ts">
import type { DocumentPermissions, Grantees, NamedItem, PaperlessUser } from '../api/documents'
import { useAuthStore } from '../stores/auth'

/**
 * Propriétaire et droits de lecture et de modification (personnes, groupes) : formulaire commun à
 * l'onglet Permissions de la fiche et à l'action en masse (UI-04, UI-06).
 */
const props = defineProps<{
  users: PaperlessUser[]
  groups: NamedItem[]
  disabled?: boolean
  /** Préfixe des identifiants HTML (plusieurs formulaires dans la page). */
  idPrefix: string
}>()
const model = defineModel<DocumentPermissions>({ required: true })

const auth = useAuthStore()

const ACTIONS = [
  { key: 'view', label: 'Peuvent consulter' },
  { key: 'change', label: 'Peuvent modifier' },
] as const
type Action = (typeof ACTIONS)[number]['key']

const userName = (id: number) => {
  const user = props.users.find((u) => u.id === id)
  if (!user) return id === auth.user?.id ? auth.user.username : `Utilisateur n° ${id}`
  const full = [user.first_name, user.last_name].filter(Boolean).join(' ')
  return full ? `${full} (${user.username})` : user.username
}
const groupName = (id: number) => props.groups.find((g) => g.id === id)?.name ?? `Groupe n° ${id}`

const grantees = (action: Action): Grantees => model.value.permissions[action]

function setGrantees(action: Action, kind: keyof Grantees, ids: number[]) {
  const permissions = model.value.permissions
  model.value = {
    ...model.value,
    permissions: { ...permissions, [action]: { ...permissions[action], [kind]: ids } },
  }
}

function add(action: Action, event: Event) {
  const select = event.target as HTMLSelectElement
  const [kind, id] = select.value.split(':')
  select.value = ''
  if (!kind || !id) return
  const list = grantees(action)[kind as keyof Grantees]
  if (!list.includes(Number(id))) setGrantees(action, kind as keyof Grantees, [...list, Number(id)])
}

function remove(action: Action, kind: keyof Grantees, id: number) {
  setGrantees(action, kind, grantees(action)[kind].filter((value) => value !== id))
}

function setOwner(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  model.value = { ...model.value, owner: value ? Number(value) : null }
}
</script>

<template>
  <div class="d-flex flex-column gap-3">
    <div>
      <label class="form-label" :for="`${idPrefix}-proprietaire`">Propriétaire</label>
      <select :id="`${idPrefix}-proprietaire`" class="form-select" :disabled="disabled" :value="model.owner ?? ''" @change="setOwner">
        <option value="">(aucun : visible par tous)</option>
        <option v-if="model.owner !== null && !users.some((u) => u.id === model.owner)" :value="model.owner">
          {{ userName(model.owner) }}
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
            v-if="!disabled"
            type="button"
            class="btn-close btn-close-white ms-1"
            :aria-label="`Retirer ${userName(id)}`"
            @click="remove(action.key, 'users', id)"
          />
        </li>
        <li v-for="id in grantees(action.key).groups" :key="`g${id}`" class="badge rounded-pill text-bg-info beneficiaire">
          Groupe {{ groupName(id) }}
          <button
            v-if="!disabled"
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
        v-if="!disabled && (users.length || groups.length)"
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
