import type { Quad } from './geometry'

/** Rendu d'une page redressée (SCAN-03) : couleurs d'origine, niveaux de gris, ou « scan » contrasté. */
export type ScanFilter = 'couleur' | 'gris' | 'scan'

/** Messages de la page vers le worker de recadrage. */
export type ScanRequest =
  /** Confie la photo au worker (qui la garde) et demande la détection des coins. */
  | { id: number; type: 'charger'; image: ImageData }
  /** Redresse la photo confiée selon les coins, applique le filtre ; page plafonnée à `coteMax`. */
  | { id: number; type: 'redresser'; coins: Quad; filtre: ScanFilter; coteMax: number }

/** Réponses du worker : coins détectés (ou null), page redressée, ou message d'erreur. */
export type ScanResponse =
  | { id: number; coins: Quad | null }
  | { id: number; image: ImageData }
  | { id: number; erreur: string }
