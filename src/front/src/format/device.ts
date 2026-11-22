/** Navigateurs reconnus, du plus spécifique au plus générique (Edge et Opera se disent aussi Chrome). */
const BROWSERS: [RegExp, string][] = [
  [/\bEdg(A|iOS)?\//, 'Edge'],
  [/\b(OPR|Opera)\//, 'Opera'],
  [/\bSamsungBrowser\//, 'Samsung Internet'],
  [/\b(Firefox|FxiOS)\//, 'Firefox'],
  [/\b(Chrome|CriOS|Chromium)\//, 'Chrome'],
  [/\bSafari\//, 'Safari'],
]

/** Systèmes reconnus ; Android avant Linux, iPhone et iPad avant macOS. */
const SYSTEMS: [RegExp, string][] = [
  [/\bAndroid\b/, 'Android'],
  [/\biPhone\b/, 'iPhone'],
  [/\biPad\b/, 'iPad'],
  [/\bWindows\b/, 'Windows'],
  [/\bCrOS\b/, 'ChromeOS'],
  [/\bMac OS X\b|\bMacintosh\b/, 'macOS'],
  [/\bLinux\b/, 'Linux'],
]

const find = (table: [RegExp, string][], value: string) => table.find(([pattern]) => pattern.test(value))?.[1]

/** Libellé lisible d'un user-agent : « Firefox sur Android », « Chrome », « Appareil inconnu »… */
export function describeDevice(userAgent: string | null | undefined): string {
  if (!userAgent) return 'Appareil inconnu'
  const browser = find(BROWSERS, userAgent)
  const system = find(SYSTEMS, userAgent)
  if (browser && system) return `${browser} sur ${system}`
  return browser ?? system ?? 'Appareil inconnu'
}
