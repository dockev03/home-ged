<script setup lang="ts">
import { nextTick, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '../api/http'
import { safeRedirect } from '../router'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const username = ref('')
const password = ref('')
const code = ref('')
/** Paperless a demandé le code TOTP : le champ apparaît, les trois valeurs sont renvoyées ensemble. */
const mfaRequired = ref(false)
const submitting = ref(false)
const error = ref<string | null>(null)
const codeInput = useTemplateRef<HTMLInputElement>('codeInput')
const showPassword = ref(false)

// Un autre compte ne partage pas le 2FA du précédent.
watch(username, () => {
  mfaRequired.value = false
  code.value = ''
})

function messageFor(e: unknown): string {
  if (!(e instanceof ApiError)) return 'Impossible de joindre Home-GED. Vérifiez votre connexion.'
  switch (e.code) {
    case 'identifiants_invalides':
      return 'Identifiant ou mot de passe incorrect.'
    case 'code_invalide':
      return 'Code de vérification incorrect.'
    case 'requete_invalide':
      return mfaRequired.value
        ? 'Saisissez votre identifiant, votre mot de passe et le code à 6 chiffres.'
        : 'Saisissez votre identifiant et votre mot de passe.'
    default:
      // trop_de_tentatives, paperless_indisponible… : le BFF fournit le message.
      return e.message
  }
}

async function submit() {
  if (submitting.value) return
  error.value = null
  submitting.value = true
  try {
    await auth.login({
      username: username.value,
      password: password.value,
      code: mfaRequired.value ? code.value : undefined,
    })
    await router.replace(safeRedirect(route.query.suite))
  } catch (e) {
    if (e instanceof ApiError && e.code === 'mfa_requis') {
      mfaRequired.value = true
      await nextTick()
      codeInput.value?.focus()
      return
    }
    if (e instanceof ApiError && e.code === 'code_invalide') code.value = ''
    error.value = messageFor(e)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="connexion">
    <h1>Home-GED</h1>
    <p class="aide text-body-secondary">Connectez-vous avec votre compte Paperless.</p>

    <form class="formulaire" novalidate @submit.prevent="submit">
      <div class="mb-3">
        <label class="form-label" for="username">Identifiant</label>
        <input
          id="username"
          v-model="username"
          name="username"
          class="form-control form-control-lg"
          autocomplete="username"
          autocapitalize="none"
          spellcheck="false"
          autofocus
        />
      </div>
      <div class="mb-3">
        <label class="form-label" for="password">Mot de passe</label>
        <div class="input-group">
          <input
            id="password"
            v-model="password"
            name="password"
            :type="showPassword ? 'text' : 'password'"
            class="form-control form-control-lg"
            autocomplete="current-password"
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
      <div v-if="mfaRequired" class="mb-3">
        <label class="form-label" for="code">Code de vérification</label>
        <input
          id="code"
          ref="codeInput"
          v-model="code"
          name="code"
          class="form-control form-control-lg"
          autocomplete="one-time-code"
          inputmode="numeric"
          pattern="[0-9 ]*"
          maxlength="7"
          placeholder="123 456"
        />
      </div>
      <div v-if="mfaRequired && !error" class="alert alert-info">
        Saisissez le code affiché par votre application d'authentification.
      </div>
      <div v-if="error" class="alert alert-danger" role="alert">{{ error }}</div>
      <button type="submit" class="btn btn-primary btn-lg w-100" :disabled="submitting">
        <span v-if="submitting" class="spinner-border spinner-border-sm me-2" aria-hidden="true" />
        Se connecter
      </button>
    </form>
  </main>
</template>

<style scoped>
.connexion {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 3rem 1rem 2rem;
}

h1 {
  margin: 0;
}

.aide {
  margin: 0.5rem 0 2rem;
}

.formulaire {
  width: 100%;
  max-width: 22rem;
}
</style>
