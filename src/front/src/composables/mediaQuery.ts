import { onScopeDispose, ref, type Ref } from 'vue'

/** Suit une media query CSS ; faux si le navigateur ne sait pas l'évaluer (tests, très vieux navigateurs). */
export function useMediaQuery(query: string): Ref<boolean> {
  const matches = ref(false)
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return matches
  const list = window.matchMedia(query)
  matches.value = list.matches
  const update = (event: MediaQueryListEvent) => (matches.value = event.matches)
  list.addEventListener('change', update)
  onScopeDispose(() => list.removeEventListener('change', update))
  return matches
}

/** Sous ce seuil (point de rupture « lg » de Bootstrap), menu en tiroir et défilement infini. */
export const MOBILE_QUERY = '(max-width: 991.98px)'
