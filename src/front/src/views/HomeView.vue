<script setup lang="ts">
import { NButton, NEmpty, NText } from 'naive-ui'
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { getAppInfo } from '../api/http'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const router = useRouter()
const version = ref<string | null>(null)
const loggingOut = ref(false)

onMounted(async () => {
  try {
    version.value = (await getAppInfo()).version
  } catch {
    version.value = null
  }
})

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
    <NText v-if="auth.user" depth="2">{{ auth.user.username }}</NText>
    <NButton quaternary size="large" :loading="loggingOut" @click="logout">Se déconnecter</NButton>
  </header>
  <main class="accueil">
    <h1>Home-GED</h1>
    <NEmpty description="La recherche arrive bientôt." />
    <NText v-if="version" depth="3" class="version">Version {{ version }}</NText>
  </main>
</template>

<style scoped>
.barre {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
}

.accueil {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1.5rem;
  padding: 2rem 1rem;
}

.version {
  font-size: 0.8rem;
}
</style>
