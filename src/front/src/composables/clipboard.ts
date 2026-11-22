/** Copie un texte dans le presse-papiers ; false si le navigateur le refuse (contexte non sécurisé, permission). */
export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
}
