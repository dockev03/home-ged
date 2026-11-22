import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as api from '../api/savedViews'

/**
 * Vues sauvegardées de l'utilisateur (UI-08) et leur affichage dans le menu et sur le tableau de
 * bord. Chargées une fois par session, rechargées après chaque modification.
 */
export const useSavedViewsStore = defineStore('savedViews', () => {
  const views = ref<api.SavedView[]>([])
  const dashboardIds = ref<number[]>([])
  const sidebarIds = ref<number[]>([])
  const error = ref<string | null>(null)
  let loading: Promise<void> | null = null

  function ensureLoaded(): Promise<void> {
    loading ??= api.listSavedViews().then(
      (list) => {
        views.value = list.vues
        dashboardIds.value = list.tableau_de_bord
        sidebarIds.value = list.menu
        error.value = null
      },
      () => {
        error.value = 'Les vues sauvegardées n’ont pas pu être chargées.'
        loading = null
      },
    )
    return loading
  }

  function reload(): Promise<void> {
    loading = null
    return ensureLoaded()
  }

  const byId = computed(() => new Map(views.value.map((view) => [view.id, view])))
  const pick = (ids: number[]) => ids.map((id) => byId.value.get(id)).filter((view) => view !== undefined)
  const dashboard = computed(() => pick(dashboardIds.value))
  const sidebar = computed(() => pick(sidebarIds.value))

  const onDashboard = (id: number) => dashboardIds.value.includes(id)
  const inSidebar = (id: number) => sidebarIds.value.includes(id)

  async function create(body: api.SavedViewUpdate): Promise<api.SavedView> {
    try {
      return await api.createSavedView(body)
    } finally {
      await reload()
    }
  }

  async function update(id: number, body: api.SavedViewUpdate): Promise<api.SavedView> {
    try {
      return await api.updateSavedView(id, body)
    } finally {
      await reload()
    }
  }

  async function remove(id: number): Promise<void> {
    try {
      await api.deleteSavedView(id)
    } finally {
      await reload()
    }
  }

  /** Oublie les vues (déconnexion : un autre utilisateur n'a pas les mêmes). */
  function reset() {
    views.value = []
    dashboardIds.value = []
    sidebarIds.value = []
    error.value = null
    loading = null
  }

  return {
    views,
    error,
    byId,
    dashboard,
    sidebar,
    onDashboard,
    inSidebar,
    ensureLoaded,
    reload,
    create,
    update,
    remove,
    reset,
  }
})
