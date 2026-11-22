<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router'
import { MOBILE_QUERY, useMediaQuery } from '../composables/mediaQuery'
import { viewQuery } from '../filters/savedViews'
import { KINDS } from '../gestion/kinds'
import { useAuthStore } from '../stores/auth'
import { useOpenDocumentsStore } from '../stores/openDocuments'
import { useSavedViewsStore } from '../stores/savedViews'
import { useUploadsStore } from '../stores/uploads'
import UploadPanel from './UploadPanel.vue'
import UserMenu from './UserMenu.vue'

/**
 * Coque de l'application (UI-01) : barre du haut avec recherche globale et menu utilisateur,
 * menu latéral fixe sur PC, en tiroir sur mobile. Dépôt de fichiers par glisser-déposer et suivi
 * des envois (PWA-07).
 */
const route = useRoute()
const router = useRouter()

interface MenuItem {
  label: string
  to?: string
  params?: Record<string, string>
  query?: LocationQueryRaw
  /** Vue sauvegardée ouverte par l'entrée (UI-08). */
  viewId?: number
}

const auth = useAuthStore()
const savedViews = useSavedViewsStore()
onMounted(() => void savedViews.ensureLoaded())

// Documents ouverts (UI-11) : ceux de l'utilisateur connecté.
const openDocuments = useOpenDocumentsStore()
watch(
  () => auth.user?.id,
  (id) => id !== undefined && openDocuments.load(id),
  { immediate: true },
)

/** Fiche fermée depuis le menu : si c'est celle affichée, retour à la liste. */
function closeDocument(id: number) {
  openDocuments.close(id)
  if (route.name === 'document' && Number(route.params.id) === id) void router.push({ name: 'documents' })
}

function closeAllDocuments() {
  const showing = route.name === 'document'
  openDocuments.closeAll()
  if (showing) void router.push({ name: 'documents' })
}

/** Entrées du menu ; celles sans route arrivent aux étapes suivantes. */
const sections = computed<{ title?: string; items: MenuItem[]; open?: boolean }[]>(() => [
  {
    items: [
      { label: 'Tableau de bord', to: 'accueil' },
      { label: 'Documents', to: 'documents' },
    ],
  },
  ...(savedViews.sidebar.length
    ? [
        {
          title: 'Vues',
          items: savedViews.sidebar.map((view) => ({
            label: view.name,
            to: 'documents',
            query: viewQuery(view, auth.user?.id ?? null),
            viewId: view.id,
          })),
        },
      ]
    : []),
  ...(openDocuments.documents.length ? [{ title: 'Documents ouverts', items: [], open: true }] : []),
  {
    title: 'Gérer',
    items: [
      ...KINDS.map((k) => ({ label: k.title, to: 'gerer', params: { type: k.slug } })),
      { label: 'Corbeille', to: 'corbeille' },
    ],
  },
  {
    title: 'Home-GED',
    items: [{ label: 'Scanner' }, { label: 'Mes liens', to: 'liens' }],
  },
])

/** Vue ouverte dans la liste des documents : elle est mise en avant à la place de « Documents ». */
const openViewId = computed(() => (route.name === 'documents' && typeof route.query.vue === 'string' ? route.query.vue : null))

function linkClasses(item: MenuItem) {
  if (item.viewId !== undefined) return { 'active-class': '', 'exact-active-class': '', active: openViewId.value === String(item.viewId) }
  if (item.to === 'documents') {
    return { 'active-class': '', 'exact-active-class': '', active: openViewId.value === null && (route.name === 'documents' || route.name === 'document') }
  }
  return { 'active-class': item.to === 'accueil' ? '' : 'active', 'exact-active-class': 'active', active: false }
}

const drawerOpen = ref(false)
const drawer = useTemplateRef<HTMLElement>('drawer')
const search = ref('')

async function openDrawer() {
  drawerOpen.value = true
  await nextTick()
  drawer.value?.focus()
}

watch(() => route.fullPath, () => (drawerOpen.value = false))

// La recherche globale reflète la recherche en cours, pour l'affiner.
watch(
  () => (route.name === 'documents' && typeof route.query.q === 'string' ? route.query.q : ''),
  (q) => (search.value = q),
  { immediate: true },
)

function submitSearch() {
  const q = search.value.trim()
  void router.push({ name: 'documents', query: q ? { q, mode: 'avance', tri: 'pertinence' } : {} })
}

const mobile = useMediaQuery(MOBILE_QUERY)
const uploads = useUploadsStore()

// Glisser-déposer de fichiers n'importe où dans l'application (PC).
const dragging = ref(false)
let dragDepth = 0
const hasFiles = (event: DragEvent) => event.dataTransfer?.types.includes('Files') ?? false

function onDragEnter(event: DragEvent) {
  if (!hasFiles(event)) return
  dragDepth++
  dragging.value = true
}

function onDragLeave(event: DragEvent) {
  if (!hasFiles(event)) return
  dragDepth = Math.max(0, dragDepth - 1)
  if (dragDepth === 0) dragging.value = false
}

function onDragOver(event: DragEvent) {
  if (!hasFiles(event)) return
  event.preventDefault()
  if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy'
}

function onDrop(event: DragEvent) {
  if (!hasFiles(event)) return
  event.preventDefault()
  dragDepth = 0
  dragging.value = false
  const files = event.dataTransfer?.files
  if (files?.length) uploads.add([...files])
}

// Quitter ou recharger la page interromprait les envois en cours.
function onBeforeUnload(event: BeforeUnloadEvent) {
  if (uploads.active) event.preventDefault()
}
onMounted(() => window.addEventListener('beforeunload', onBeforeUnload))
onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload))

// Pas de défilement de la page derrière le tiroir ouvert.
watch(drawerOpen, (open) => document.body.classList.toggle('overflow-hidden', open))
onBeforeUnmount(() => document.body.classList.remove('overflow-hidden'))
</script>

<template>
  <a class="visually-hidden-focusable lien-evitement" href="#contenu">Aller au contenu</a>
  <header class="navbar bg-body-tertiary border-bottom sticky-top barre">
    <div class="container-fluid flex-nowrap gap-2">
      <button
        type="button"
        class="btn btn-link d-lg-none bouton-menu"
        aria-label="Ouvrir le menu"
        aria-controls="menu-principal"
        :aria-expanded="drawerOpen"
        @click="openDrawer"
      >
        <span class="navbar-toggler-icon" />
      </button>
      <RouterLink class="navbar-brand d-none d-sm-inline" :to="{ name: 'accueil' }">Home-GED</RouterLink>
      <form class="recherche" role="search" @submit.prevent="submitSearch">
        <input
          v-model="search"
          type="search"
          class="form-control"
          placeholder="Rechercher des documents"
          aria-label="Rechercher des documents"
          enterkeyhint="search"
        />
      </form>
      <UserMenu />
    </div>
  </header>

  <div
    class="coque"
    @dragenter="onDragEnter"
    @dragleave="onDragLeave"
    @dragover="onDragOver"
    @drop="onDrop"
  >
    <nav
      id="menu-principal"
      ref="drawer"
      class="offcanvas-lg offcanvas-start menu"
      :class="{ show: drawerOpen }"
      tabindex="-1"
      aria-label="Menu principal"
      @keydown.esc="drawerOpen = false"
    >
      <div class="offcanvas-header">
        <span class="offcanvas-title h5">Home-GED</span>
        <button type="button" class="btn-close" aria-label="Fermer le menu" @click="drawerOpen = false" />
      </div>
      <div class="offcanvas-body flex-column">
        <template v-for="section in sections" :key="section.title ?? 'principal'">
          <h2 v-if="section.title" class="menu-titre">{{ section.title }}</h2>
          <ul v-if="section.open" class="nav nav-pills flex-column documents-ouverts">
            <li v-for="d in openDocuments.documents" :key="d.id" class="nav-item">
              <RouterLink
                class="nav-link"
                active-class=""
                exact-active-class="active"
                :to="{ name: 'document', params: { id: d.id } }"
                :title="d.title"
              >
                {{ d.title }}
              </RouterLink>
              <button type="button" class="btn-close fermer" :aria-label="`Fermer ${d.title}`" @click="closeDocument(d.id)" />
            </li>
            <li class="nav-item">
              <button type="button" class="nav-link tout-fermer" @click="closeAllDocuments">Tout fermer</button>
            </li>
          </ul>
          <ul v-else class="nav nav-pills flex-column">
            <li v-for="item in section.items" :key="item.viewId ?? item.label" class="nav-item">
              <RouterLink
                v-if="item.to"
                class="nav-link"
                :active-class="linkClasses(item)['active-class']"
                :exact-active-class="linkClasses(item)['exact-active-class']"
                :class="{ active: linkClasses(item).active }"
                :to="{ name: item.to, params: item.params, query: item.query }"
              >
                {{ item.label }}
              </RouterLink>
              <span v-else class="nav-link disabled" aria-disabled="true">
                {{ item.label }} <span class="badge text-bg-secondary fw-normal">bientôt</span>
              </span>
            </li>
          </ul>
        </template>
      </div>
    </nav>
    <div v-if="drawerOpen" class="offcanvas-backdrop fade show d-lg-none" @click="drawerOpen = false" />

    <main id="contenu" class="contenu" tabindex="-1">
      <UploadPanel v-if="mobile" />
      <RouterView />
    </main>
  </div>
  <UploadPanel v-if="!mobile" floating />
  <div v-if="dragging" class="depot" aria-hidden="true">
    <p class="depot-texte">Déposez les fichiers pour les ajouter à Paperless</p>
  </div>
</template>

<style scoped>
.lien-evitement {
  position: absolute;
  z-index: 1100;
  padding: 0.5rem 1rem;
  background: var(--bs-body-bg);
}

.recherche {
  flex: 1 1 auto;
  min-width: 0;
  max-width: 40rem;
  margin-right: auto;
}

.bouton-menu {
  padding: 0.25rem 0.5rem;
}

.coque {
  display: flex;
  min-height: calc(100dvh - 3.6rem);
}

.menu {
  --bs-offcanvas-width: 17rem;
}

/* Zone de dépôt : n'intercepte pas les événements, la coque les reçoit. */
.depot {
  position: fixed;
  inset: 0;
  z-index: 1060;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2rem;
  pointer-events: none;
  background: rgba(var(--bs-primary-rgb), 0.12);
  border: 3px dashed var(--bs-primary);
}

.depot-texte {
  margin: 0;
  padding: 1rem 1.5rem;
  border-radius: var(--bs-border-radius-lg);
  background: var(--bs-body-bg);
  font-size: 1.25rem;
  font-weight: 600;
}

.menu-titre {
  margin: 1.25rem 0 0.25rem;
  padding: 0 1rem;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--bs-secondary-color);
}

.documents-ouverts .nav-item {
  display: flex;
  align-items: center;
}

.documents-ouverts .nav-link {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.fermer {
  flex: 0 0 auto;
  margin: 0 0.5rem;
  font-size: 0.65rem;
}

.tout-fermer {
  text-align: start;
  color: var(--bs-secondary-color);
  font-size: 0.875rem;
}

.contenu {
  flex: 1 1 auto;
  min-width: 0;
  padding: 1rem;
}

.contenu:focus {
  outline: none;
}

@media (min-width: 992px) {
  .menu {
    flex: 0 0 15rem;
    padding: 1rem 0.5rem;
    border-right: var(--bs-border-width) solid var(--bs-border-color);
  }

  .menu .offcanvas-body {
    position: sticky;
    top: 4.5rem;
  }

  .contenu {
    padding: 1.5rem;
  }
}
</style>
