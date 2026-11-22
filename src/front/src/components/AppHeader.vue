<script setup lang="ts">
import { NButton, NDropdown, type DropdownOption } from 'naive-ui'
import { computed, h, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { usePreferencesStore, type ThemeChoice } from '../stores/preferences'

defineProps<{
  /** Affiche le bouton de retour à l'accueil. */
  retour?: boolean
}>()

const auth = useAuthStore()
const preferences = usePreferencesStore()
const router = useRouter()
const loggingOut = ref(false)

const THEMES: { choice: ThemeChoice; label: string }[] = [
  { choice: 'systeme', label: 'Comme le système' },
  { choice: 'clair', label: 'Clair' },
  { choice: 'sombre', label: 'Sombre' },
]

const options = computed<DropdownOption[]>(() => [
  {
    type: 'group',
    key: 'theme',
    label: 'Thème',
    children: THEMES.map(({ choice, label }) => ({
      key: `theme:${choice}`,
      label,
      // Coche visible (style en ligne : le menu est rendu hors du composant) ; état annoncé aux lecteurs d'écran.
      icon: () => h('span', { style: 'display:inline-block;width:1em;text-align:center' }, preferences.theme === choice ? '✓' : ''),
      props: { 'aria-checked': preferences.theme === choice, role: 'menuitemradio' },
    })),
  },
  { type: 'divider', key: 'separateur' },
  { key: 'sessions', label: 'Mes sessions' },
  { key: 'deconnexion', label: 'Se déconnecter' },
])

async function select(key: string) {
  if (key.startsWith('theme:')) {
    preferences.theme = key.slice('theme:'.length) as ThemeChoice
  } else if (key === 'sessions') {
    await router.push({ name: 'sessions' })
  } else if (key === 'deconnexion') {
    await logout()
  }
}

async function logout() {
  loggingOut.value = true
  try {
    await auth.logout()
  } catch {
    // Même si le BFF ne répond pas, on quitte l'écran : la session expirera d'elle-même.
  } finally {
    loggingOut.value = false
    await router.replace({ name: 'connexion' })
  }
}
</script>

<template>
  <header class="barre">
    <NButton v-if="retour" quaternary size="large" @click="router.push({ name: 'accueil' })">← Accueil</NButton>
    <span class="espace" />
    <NDropdown trigger="click" size="huge" placement="bottom-end" :options="options" @select="select">
      <NButton quaternary size="large" :loading="loggingOut" aria-haspopup="menu" class="menu">
        {{ auth.user?.username ?? 'Menu' }} ▾
      </NButton>
    </NDropdown>
  </header>
</template>

<style scoped>
.barre {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
}

.espace {
  flex: 1;
}
</style>
