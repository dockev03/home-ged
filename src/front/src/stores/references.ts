import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { getReferences, type NamedItem, type References, type Tag } from '../api/documents'

const EMPTY: References = { tags: [], correspondants: [], types: [], chemins: [], champs: [] }

/**
 * Listes de référence (étiquettes, correspondants, types, chemins, champs personnalisés),
 * chargées une fois par session pour les filtres et les badges.
 */
export const useReferencesStore = defineStore('references', () => {
  const lists = ref<References>({ ...EMPTY })
  const error = ref<string | null>(null)
  let loading: Promise<void> | null = null

  function ensureLoaded(): Promise<void> {
    loading ??= Promise.all([
      getReferences('tags'),
      getReferences('correspondants'),
      getReferences('types'),
      getReferences('chemins'),
      getReferences('champs'),
    ]).then(
      ([tags, correspondants, types, chemins, champs]) => {
        lists.value = { tags, correspondants, types, chemins, champs }
        error.value = null
      },
      () => {
        // Filtres et badges dégradés, mais la liste reste utilisable ; nouvel essai au prochain appel.
        error.value = 'Les étiquettes et listes de filtres n’ont pas pu être chargées.'
        loading = null
      },
    )
    return loading
  }

  const byId = <T extends NamedItem>(items: T[]) => new Map(items.map((item) => [item.id, item]))
  const tags = computed(() => byId<Tag>(lists.value.tags))
  const correspondants = computed(() => byId(lists.value.correspondants))
  const types = computed(() => byId(lists.value.types))
  const chemins = computed(() => byId(lists.value.chemins))

  /** Oublie les listes (déconnexion : un autre utilisateur n'a pas les mêmes droits). */
  function reset() {
    lists.value = { ...EMPTY }
    error.value = null
    loading = null
  }

  return { lists, error, tags, correspondants, types, chemins, ensureLoaded, reset }
})
