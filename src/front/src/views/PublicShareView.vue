<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ApiError } from '../api/http'
import {
  getPublicShare,
  openPublicShare,
  PASSWORD_REQUIRED,
  publicDownloadUrl,
  publicPreviewUrl,
  type PublicShare,
} from '../api/publicShares'
import { MOBILE_QUERY, useMediaQuery } from '../composables/mediaQuery'
import { formatDateTime } from '../format/dates'

/**
 * Page publique d'un lien de partage (SHARE-03), hors de la coque de l'application : le destinataire
 * n'a pas de compte. Mot de passe, puis aperçu et téléchargement. Les refus restent génériques :
 * la page ne dit jamais si le lien existe, a expiré ou a été révoqué.
 */
const route = useRoute()
const id = computed(() => String(route.params.id))
const mobile = useMediaQuery(MOBILE_QUERY)

type State = 'chargement' | 'mot-de-passe' | 'ouvert' | 'indisponible'
const state = ref<State>('chargement')
const share = ref<PublicShare | null>(null)
const password = ref('')
const showPassword = ref(false)
const submitting = ref(false)
const error = ref<string | null>(null)

const UNREACHABLE = 'Impossible de joindre le serveur. Vérifiez votre connexion.'

// Un ticket encore valable (page rechargée) évite de ressaisir le mot de passe.
onMounted(async () => {
  try {
    share.value = await getPublicShare(id.value)
    state.value = 'ouvert'
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) {
      // Ticket valable mais lien révoqué, expiré ou épuisé depuis.
      state.value = 'indisponible'
      return
    }
    if (!(e instanceof ApiError)) error.value = UNREACHABLE
    else if (e.code !== PASSWORD_REQUIRED) error.value = e.message
    state.value = 'mot-de-passe'
  }
})

async function submit() {
  if (submitting.value) return
  if (password.value.length === 0) {
    error.value = 'Saisissez le mot de passe communiqué par l’expéditeur.'
    return
  }
  submitting.value = true
  error.value = null
  try {
    share.value = await openPublicShare(id.value, password.value)
    password.value = ''
    state.value = 'ouvert'
  } catch (e) {
    // lien_indisponible, trop_de_tentatives, partage_indisponible : le BFF fournit le message.
    error.value = e instanceof ApiError ? e.message : UNREACHABLE
  } finally {
    submitting.value = false
  }
}

const exhausted = computed(() => share.value?.telechargementsRestants === 0)

/** Le téléchargement suit le lien ; le compteur local évite de proposer un téléchargement qui serait refusé. */
function downloaded() {
  const s = share.value
  if (s && s.telechargementsRestants !== null) s.telechargementsRestants = Math.max(0, s.telechargementsRestants - 1)
}

const remainingLabel = computed(() => {
  const n = share.value?.telechargementsRestants
  if (n === null || n === undefined) return null
  if (n === 0) return 'Plus aucun téléchargement possible avec ce lien.'
  return `${n} téléchargement${n > 1 ? 's' : ''} restant${n > 1 ? 's' : ''}.`
})
</script>

<template>
  <main class="partage">
    <p class="marque text-body-secondary mb-0">Home-GED · document partagé</p>

    <div v-if="state === 'chargement'" class="chargement">
      <span class="spinner-border" role="status"><span class="visually-hidden">Chargement…</span></span>
    </div>

    <section v-else-if="state === 'mot-de-passe'" class="formulaire">
      <h1 class="h3">Document protégé</h1>
      <p class="text-body-secondary">Saisissez le mot de passe que l’expéditeur vous a communiqué.</p>
      <form novalidate @submit.prevent="submit">
        <div class="mb-3">
          <label class="form-label" for="mot-de-passe">Mot de passe</label>
          <div class="input-group">
            <input
              id="mot-de-passe"
              v-model="password"
              name="password"
              :type="showPassword ? 'text' : 'password'"
              class="form-control form-control-lg"
              autocomplete="off"
              autocapitalize="none"
              spellcheck="false"
              maxlength="128"
              autofocus
            />
            <button
              type="button"
              class="btn btn-outline-secondary"
              :aria-pressed="showPassword"
              @click="showPassword = !showPassword"
            >
              {{ showPassword ? 'Masquer' : 'Afficher' }}
            </button>
          </div>
        </div>
        <div v-if="error" class="alert alert-danger" role="alert">{{ error }}</div>
        <button type="submit" class="btn btn-primary btn-lg w-100" :disabled="submitting">
          <span v-if="submitting" class="spinner-border spinner-border-sm me-2" aria-hidden="true" />
          Ouvrir le document
        </button>
      </form>
    </section>

    <section v-else-if="state === 'indisponible'" class="formulaire">
      <h1 class="h3">Lien indisponible</h1>
      <p class="mb-0">Ce lien n’est plus disponible. Demandez un nouveau lien à l’expéditeur.</p>
    </section>

    <section v-else-if="share" class="document">
      <header class="entete">
        <div class="infos">
          <h1 class="h3 mb-1 titre">{{ share.titre || 'Document' }}</h1>
          <p class="small text-body-secondary mb-0">
            Lien valable jusqu’au {{ formatDateTime(share.expireLe) }}.
            <template v-if="remainingLabel">{{ remainingLabel }}</template>
          </p>
        </div>
        <a
          v-if="!exhausted"
          class="btn btn-primary btn-lg"
          :href="publicDownloadUrl(id)"
          download
          @click="downloaded"
        >
          Télécharger
        </a>
      </header>

      <template v-if="!exhausted">
        <a v-if="mobile" :href="publicPreviewUrl(id)" target="_blank" rel="noopener" class="btn btn-outline-primary btn-lg">
          Ouvrir l’aperçu
        </a>
        <iframe v-else :src="publicPreviewUrl(id)" :title="`Aperçu de ${share.titre}`" class="cadre" />
      </template>
    </section>
  </main>
</template>

<style scoped>
.partage {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1.5rem;
  padding: 2rem 1rem;
}

.formulaire {
  width: 100%;
  max-width: 24rem;
}

.document {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  width: 100%;
  max-width: 64rem;
}

.entete {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 1rem;
}

.infos {
  flex: 1 1 16rem;
  min-width: 0;
}

.titre {
  overflow-wrap: anywhere;
}

.cadre {
  width: 100%;
  height: calc(100dvh - 12rem);
  min-height: 30rem;
  border: var(--bs-border-width) solid var(--bs-border-color);
  border-radius: var(--bs-border-radius);
  background: var(--bs-secondary-bg);
}

.chargement {
  display: flex;
  justify-content: center;
  padding: 3rem;
}
</style>
