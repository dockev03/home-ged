<script setup lang="ts">
import { NAlert, NButton, NForm, NFormItem, NInput } from 'naive-ui'
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
const codeInput = useTemplateRef<InstanceType<typeof NInput>>('codeInput')

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
    <p class="aide">Connectez-vous avec votre compte Paperless.</p>

    <NForm class="formulaire" :show-require-mark="false" @submit.prevent="submit">
      <NFormItem label="Identifiant" path="username">
        <NInput
          v-model:value="username"
          :input-props="{ autocomplete: 'username', autocapitalize: 'none', spellcheck: false, name: 'username' }"
          size="large"
          placeholder=""
          autofocus
        />
      </NFormItem>
      <NFormItem label="Mot de passe" path="password">
        <NInput
          v-model:value="password"
          type="password"
          show-password-on="click"
          :input-props="{ autocomplete: 'current-password', name: 'password' }"
          size="large"
          placeholder=""
        />
      </NFormItem>
      <NFormItem v-if="mfaRequired" label="Code de vérification" path="code">
        <NInput
          ref="codeInput"
          v-model:value="code"
          :input-props="{ autocomplete: 'one-time-code', inputmode: 'numeric', pattern: '[0-9 ]*', name: 'code' }"
          :maxlength="7"
          size="large"
          placeholder="123 456"
        />
      </NFormItem>
      <NAlert v-if="mfaRequired && !error" type="info" :show-icon="false" class="message">
        Saisissez le code affiché par votre application d'authentification.
      </NAlert>
      <NAlert v-if="error" type="error" :show-icon="false" class="message" role="alert">{{ error }}</NAlert>
      <NButton attr-type="submit" type="primary" size="large" block :loading="submitting">Se connecter</NButton>
    </NForm>
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
  opacity: 0.75;
}

.formulaire {
  width: 100%;
  max-width: 22rem;
}

.message {
  margin-bottom: 1rem;
}
</style>
