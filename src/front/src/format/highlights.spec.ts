import { describe, expect, it } from 'vitest'
import { parseHighlights } from './highlights'

describe('parseHighlights', () => {
  it('sépare texte et correspondances, entités décodées', () => {
    expect(parseHighlights('Facture <span class="match">EDF</span> &amp; gaz')).toEqual([
      { text: 'Facture ', match: false },
      { text: 'EDF', match: true },
      { text: ' & gaz', match: false },
    ])
  })

  it('neutralise le HTML hostile issu du contenu', () => {
    const parts = parseHighlights('<img src=x onerror="alert(1)"><script>alert(2)</script>texte <b>gras</b>')
    expect(parts).toEqual([{ text: 'texte gras', match: false }])
  })
})
