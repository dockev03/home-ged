import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import type { LocationQuery } from 'vue-router'

/**
 * Dernière liste de documents consultée : ses filtres (retour à la liste depuis la fiche) et les
 * identifiants de tous ses résultats, toutes pages confondues (précédent / suivant de la fiche).
 */
export const useDocumentListStore = defineStore('documentList', () => {
  const query = ref<LocationQuery>({})
  const ids = ref<number[]>([])

  /** Filtres et page de la liste affichée (retour depuis la fiche). */
  function rememberQuery(listQuery: LocationQuery) {
    query.value = { ...listQuery }
  }

  /** Identifiants de tous les résultats des filtres en cours. */
  function rememberIds(all: number[]) {
    ids.value = all
  }

  function remember(listQuery: LocationQuery, all: number[]) {
    rememberQuery(listQuery)
    rememberIds(all)
  }

  const neighbours = computed(() => (id: number) => {
    const index = ids.value.indexOf(id)
    if (index < 0) return { previous: null, next: null, position: null }
    return {
      previous: index > 0 ? ids.value[index - 1]! : null,
      next: index < ids.value.length - 1 ? ids.value[index + 1]! : null,
      position: { index: index + 1, total: ids.value.length },
    }
  })

  /** Un document supprimé sort de la liste. */
  function forget(id: number) {
    ids.value = ids.value.filter((value) => value !== id)
  }

  return { query, ids, remember, rememberQuery, rememberIds, neighbours, forget }
})
