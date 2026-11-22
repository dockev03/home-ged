<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import UserMenu from './UserMenu.vue'

/**
 * Coque de l'application (UI-01) : barre du haut avec recherche globale et menu utilisateur,
 * menu latéral fixe sur PC, en tiroir sur mobile.
 */
const route = useRoute()
const router = useRouter()

/** Entrées du menu ; celles sans route arrivent aux étapes suivantes. */
const SECTIONS: { title?: string; items: { label: string; to?: string }[] }[] = [
  {
    items: [
      { label: 'Tableau de bord', to: 'accueil' },
      { label: 'Documents', to: 'documents' },
    ],
  },
  {
    title: 'Gérer',
    items: [
      { label: 'Étiquettes' },
      { label: 'Correspondants' },
      { label: 'Types de documents' },
      { label: 'Chemins de stockage' },
      { label: 'Champs personnalisés' },
    ],
  },
  {
    title: 'Home-GED',
    items: [{ label: 'Scanner' }, { label: 'Mes liens' }],
  },
]

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

  <div class="coque">
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
        <template v-for="section in SECTIONS" :key="section.title ?? 'principal'">
          <h2 v-if="section.title" class="menu-titre">{{ section.title }}</h2>
          <ul class="nav nav-pills flex-column">
            <li v-for="item in section.items" :key="item.label" class="nav-item">
              <RouterLink
                v-if="item.to"
                class="nav-link"
                :active-class="item.to === 'accueil' ? '' : 'active'"
                exact-active-class="active"
                :class="{ active: item.to === 'documents' && route.name === 'document' }"
                :to="{ name: item.to }"
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
      <RouterView />
    </main>
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

.menu-titre {
  margin: 1.25rem 0 0.25rem;
  padding: 0 1rem;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--bs-secondary-color);
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
