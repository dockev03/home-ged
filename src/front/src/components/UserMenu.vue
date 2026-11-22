<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { usePreferencesStore, type ThemeChoice } from '../stores/preferences'
import BaseDropdown from './BaseDropdown.vue'

const auth = useAuthStore()
const preferences = usePreferencesStore()
const router = useRouter()
const loggingOut = ref(false)

const THEMES: { choice: ThemeChoice; label: string }[] = [
  { choice: 'systeme', label: 'Comme le système' },
  { choice: 'clair', label: 'Clair' },
  { choice: 'sombre', label: 'Sombre' },
]

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
  <BaseDropdown end button-class="btn btn-link nav-link menu-utilisateur" aria-label="Menu utilisateur">
    <template #toggle>
      <span v-if="loggingOut" class="spinner-border spinner-border-sm me-1" aria-hidden="true" />
      <span class="nom">{{ auth.user?.username ?? 'Menu' }}</span>
    </template>
    <template #default="{ close }">
      <h6 class="dropdown-header">Thème</h6>
      <button
        v-for="{ choice, label } in THEMES"
        :key="choice"
        type="button"
        class="dropdown-item"
        role="menuitemradio"
        :aria-checked="preferences.theme === choice"
        :class="{ active: preferences.theme === choice }"
        @click="preferences.theme = choice"
      >
        {{ label }}
      </button>
      <hr class="dropdown-divider" />
      <RouterLink class="dropdown-item" role="menuitem" :to="{ name: 'sessions' }" @click="close()">
        Mes sessions
      </RouterLink>
      <button type="button" class="dropdown-item" role="menuitem" :disabled="loggingOut" @click="logout">
        Se déconnecter
      </button>
    </template>
  </BaseDropdown>
</template>

<style scoped>
.nom {
  display: inline-block;
  max-width: 10rem;
  overflow: hidden;
  text-overflow: ellipsis;
  vertical-align: bottom;
  white-space: nowrap;
}
</style>
