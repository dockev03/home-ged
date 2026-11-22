export interface HighlightPart {
  text: string
  match: boolean
}

/**
 * Extrait surligné de la recherche plein texte. Paperless le renvoie en HTML
 * (`<span class="match">…</span>`) construit à partir du contenu des documents : on ne l'injecte
 * jamais tel quel (v-html), on le lit avec DOMParser (inerte : aucun script, aucune image chargée)
 * et on n'en garde que le texte et les correspondances.
 */
export function parseHighlights(html: string): HighlightPart[] {
  const body = new DOMParser().parseFromString(html, 'text/html').body
  const parts: HighlightPart[] = []
  const push = (text: string, match: boolean) => {
    if (!text) return
    const last = parts.at(-1)
    if (last && last.match === match) last.text += text
    else parts.push({ text, match })
  }
  const walk = (node: Node, inMatch: boolean) => {
    for (const child of node.childNodes) {
      if (child.nodeType === Node.TEXT_NODE) push(child.textContent ?? '', inMatch)
      else if (child instanceof Element && !['SCRIPT', 'STYLE', 'TEMPLATE'].includes(child.tagName))
        walk(child, inMatch || (child.tagName === 'SPAN' && child.classList.contains('match')))
    }
  }
  walk(body, false)
  return parts
}
