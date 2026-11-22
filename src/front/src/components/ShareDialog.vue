<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { ApiError } from '../api/http'
import {
  MAX_DOWNLOADS_LIMIT,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  createShare,
  durationChoices,
  generatePassword,
  getShareSettings,
  shareUrl,
  type ShareLink,
} from '../api/shares'
import { copyText } from '../composables/clipboard'
import { formatDate } from '../format/dates'

/**
 * Création d'un lien de partage protégé (SHARE-01, 02) depuis la fiche document (bouton « Envoyer ») :
 * mot de passe obligatoire, durée de validité bornée, nombre de téléchargements facultatif.
 */
const props = defineProps<{ open: boolean; documentId: number; documentTitle: string }>()
const emit = defineEmits<{ close: [] }>()

const maxDays = ref<number | null>(null)
const password = ref('')
const showPassword = ref(false)
const days = ref(7)
const limitDownloads = ref(false)
const maxDownloads = ref(1)
const saving = ref(false)
const error = ref<string | null>(null)
const created = ref<ShareLink | null>(null)
const copied = ref<boolean | null>(null)
const passwordInput = useTemplateRef<HTMLInputElement>('passwordInput')

const choices = computed(() => (maxDays.value === null ? [] : durationChoices(maxDays.value)))
const expiry = computed(() => new Date(Date.now() + days.value * 86_400_000))
const url = computed(() => (created.value ? shareUrl(created.value.id) : ''))
const limitLabel = computed(() => {
  const max = created.value?.telechargementsMax ?? null
  return max === null ? '' : `, ${max} téléchargement${max > 1 ? 's' : ''} au plus`
})
const canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function'

const errorMessage = (e: unknown) =>
  e instanceof ApiError ? e.message : 'Impossible de joindre Home-GED. Vérifiez votre connexion.'

watch(
  () => props.open,
  async (open) => {
    if (!open) return
    password.value = ''
    showPassword.value = false
    limitDownloads.value = false
    maxDownloads.value = 1
    error.value = null
    created.value = null
    copied.value = null
    saving.value = false
    try {
      const settings = await getShareSettings()
      maxDays.value = settings.joursMax
      days.value = Math.min(7, settings.joursMax)
    } catch (e) {
      error.value = errorMessage(e)
    }
    await nextTick()
    passwordInput.value?.focus()
  },
  { immediate: true },
)

function generate() {
  password.value = generatePassword()
  showPassword.value = true
}

function validate(): string | null {
  if (password.value.length < PASSWORD_MIN_LENGTH) return `Le mot de passe doit contenir au moins ${PASSWORD_MIN_LENGTH} caractères.`
  if (limitDownloads.value && !(Number.isInteger(maxDownloads.value) && maxDownloads.value >= 1 && maxDownloads.value <= MAX_DOWNLOADS_LIMIT))
    return `Le nombre de téléchargements doit être compris entre 1 et ${MAX_DOWNLOADS_LIMIT}.`
  return null
}

async function submit() {
  error.value = validate()
  if (error.value) return
  saving.value = true
  try {
    created.value = await createShare({
      document: props.documentId,
      motDePasse: password.value,
      jours: days.value,
      telechargementsMax: limitDownloads.value ? maxDownloads.value : null,
    })
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    saving.value = false
  }
}

async function copy() {
  copied.value = await copyText(url.value)
}

async function share() {
  try {
    await navigator.share({ title: props.documentTitle, url: url.value })
  } catch {
    // Partage annulé par l'utilisateur : rien à faire.
  }
}

const daysLabel = (d: number) => (d === 1 ? '1 jour' : `${d} jours`)
</script>

<template>
  <template v-if="open">
    <div
      class="modal d-block"
      tabindex="-1"
      role="dialog"
      aria-modal="true"
      aria-labelledby="partage-titre"
      @keydown.esc="emit('close')"
    >
      <div class="modal-dialog modal-dialog-centered modal-dialog-scrollable">
        <form v-if="!created" class="modal-content" novalidate @submit.prevent="submit">
          <div class="modal-header">
            <h2 id="partage-titre" class="modal-title fs-5">Envoyer un lien protégé</h2>
            <button type="button" class="btn-close" aria-label="Fermer" @click="emit('close')" />
          </div>
          <div class="modal-body d-flex flex-column gap-3">
            <p class="mb-0">
              Le destinataire pourra consulter et télécharger <strong class="titre">{{ documentTitle }}</strong> avec ce mot de passe,
              sans compte Home-GED.
            </p>
            <div>
              <label class="form-label" for="partage-mdp">Mot de passe</label>
              <div class="input-group">
                <input
                  id="partage-mdp"
                  ref="passwordInput"
                  v-model="password"
                  class="form-control"
                  :type="showPassword ? 'text' : 'password'"
                  :minlength="PASSWORD_MIN_LENGTH"
                  :maxlength="PASSWORD_MAX_LENGTH"
                  autocomplete="new-password"
                  required
                  :disabled="saving"
                  aria-describedby="partage-mdp-aide"
                />
                <button
                  type="button"
                  class="btn btn-outline-secondary"
                  :aria-pressed="showPassword"
                  @click="showPassword = !showPassword"
                >
                  {{ showPassword ? 'Masquer' : 'Afficher' }}
                </button>
                <button type="button" class="btn btn-outline-secondary" :disabled="saving" @click="generate">Générer</button>
              </div>
              <div id="partage-mdp-aide" class="form-text">
                {{ PASSWORD_MIN_LENGTH }} caractères au moins. Transmettez-le par un autre moyen que le lien (SMS, appel…).
              </div>
            </div>
            <div>
              <label class="form-label" for="partage-duree">Validité</label>
              <select id="partage-duree" v-model.number="days" class="form-select" :disabled="saving || !choices.length">
                <option v-for="d in choices" :key="d" :value="d">{{ daysLabel(d) }}</option>
              </select>
              <div class="form-text">Le lien expirera le {{ formatDate(expiry) }}.</div>
            </div>
            <div>
              <div class="form-check form-switch">
                <input
                  id="partage-limite"
                  v-model="limitDownloads"
                  class="form-check-input"
                  type="checkbox"
                  role="switch"
                  :disabled="saving"
                />
                <label class="form-check-label" for="partage-limite">Limiter le nombre de téléchargements</label>
              </div>
              <div v-if="limitDownloads" class="mt-2 telechargements">
                <label class="visually-hidden" for="partage-max">Nombre maximal de téléchargements</label>
                <input
                  id="partage-max"
                  v-model.number="maxDownloads"
                  class="form-control"
                  type="number"
                  min="1"
                  :max="MAX_DOWNLOADS_LIMIT"
                  step="1"
                  inputmode="numeric"
                  :disabled="saving"
                />
                <span>téléchargement{{ maxDownloads > 1 ? 's' : '' }} au plus</span>
              </div>
            </div>
            <div v-if="error" class="alert alert-danger mb-0" role="alert">{{ error }}</div>
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-outline-secondary" @click="emit('close')">Annuler</button>
            <button type="submit" class="btn btn-primary" :disabled="saving || maxDays === null">
              <span v-if="saving" class="spinner-border spinner-border-sm me-1" aria-hidden="true" />
              Créer le lien
            </button>
          </div>
        </form>

        <div v-else class="modal-content">
          <div class="modal-header">
            <h2 id="partage-titre" class="modal-title fs-5">Lien créé</h2>
            <button type="button" class="btn-close" aria-label="Fermer" @click="emit('close')" />
          </div>
          <div class="modal-body d-flex flex-column gap-3">
            <div>
              <label class="form-label" for="partage-lien">Lien à envoyer</label>
              <input id="partage-lien" class="form-control" :value="url" readonly @focus="($event.target as HTMLInputElement).select()" />
              <div class="form-text">
                Valable jusqu'au {{ formatDate(created.expireLe) }}{{ limitLabel }}.
              </div>
            </div>
            <div class="d-flex flex-wrap gap-2">
              <button type="button" class="btn btn-primary" @click="copy">Copier le lien</button>
              <button v-if="canShare" type="button" class="btn btn-outline-primary" @click="share">Partager…</button>
            </div>
            <p v-if="copied === true" class="text-success mb-0" role="status">Lien copié.</p>
            <p v-else-if="copied === false" class="text-danger mb-0" role="alert">
              Copie impossible : sélectionnez le lien et copiez-le à la main.
            </p>
            <p class="alert alert-warning mb-0">
              Le mot de passe n'est pas dans le lien et ne pourra plus être affiché : transmettez-le au destinataire par un
              autre moyen. Vous pouvez révoquer le lien à tout moment depuis « Mes liens ».
            </p>
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-primary" @click="emit('close')">Terminé</button>
          </div>
        </div>
      </div>
    </div>
    <div class="modal-backdrop fade show" />
  </template>
</template>

<style scoped>
.titre {
  overflow-wrap: anywhere;
}

.telechargements {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.telechargements input {
  max-width: 7rem;
}
</style>
