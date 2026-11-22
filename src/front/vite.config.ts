import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [
    vue(),
    // PWA installable (PWA-01) : manifeste, icônes et service worker qui ne met en cache que le front
    // buildé. Les réponses de /api (documents, session) ne sont jamais mises en cache.
    VitePWA({
      registerType: 'autoUpdate',
      // Fichier registerSW.js plutôt qu'un script en ligne : la CSP n'autorise que 'self'.
      injectRegister: 'script',
      includeAssets: ['favicon.svg', 'apple-touch-icon.png'],
      manifest: {
        id: '/',
        name: 'Home-GED',
        short_name: 'Home-GED',
        description: 'Documents du foyer : rechercher, consulter, classer, ajouter.',
        lang: 'fr',
        dir: 'ltr',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        orientation: 'any',
        theme_color: '#2f6f5e',
        background_color: '#ffffff',
        categories: ['productivity', 'utilities'],
        // « Partager vers Home-GED » depuis une autre application (PWA-11, Android) : reçu par le
        // service worker (public/partage-sw.js), jamais par le serveur.
        share_target: {
          action: '/partager',
          method: 'POST',
          enctype: 'multipart/form-data',
          params: {
            files: [
              {
                name: 'fichiers',
                accept: [
                  'application/pdf',
                  'image/*',
                  'text/plain',
                  'application/msword',
                  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
                  'application/vnd.ms-excel',
                  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                  'application/vnd.oasis.opendocument.text',
                  'application/vnd.oasis.opendocument.spreadsheet',
                  '.pdf',
                  '.doc',
                  '.docx',
                  '.xls',
                  '.xlsx',
                  '.odt',
                  '.ods',
                  '.txt',
                ],
              },
            ],
          },
        },
        icons: [
          { src: '/pwa-192x192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: '/pwa-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          { src: '/maskable-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
        // OpenCV (environ 10 Mo, écran Scanner seul) : téléchargé à la première utilisation, pas à l'installation.
        globIgnores: ['**/opencv-*.js', 'partage-sw.js'],
        // Réception des fichiers partagés (PWA-11), chargée avant les routes Workbox.
        importScripts: ['/partage-sw.js'],
        cleanupOutdatedCaches: true,
        // Navigation hors ligne vers l'application ; jamais pour l'API, les sondes ni les liens publics.
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/health/, /^\/s\//],
        // OpenCV gardé après sa première utilisation : le scan fonctionne ensuite hors ligne et les
        // pages partent dans la file d'envoi (PWA-10). Nom haché : une nouvelle version remplace l'ancienne.
        runtimeCaching: [
          {
            urlPattern: ({ url, sameOrigin }) => sameOrigin && /^\/assets\/opencv-[^/]+\.js$/.test(url.pathname),
            handler: 'CacheFirst',
            options: {
              cacheName: 'opencv',
              expiration: { maxEntries: 1 },
              cacheableResponse: { statuses: [200] },
            },
          },
        ],
      },
    }),
  ],
  // Worker de recadrage en module ES : OpenCV y est importé dynamiquement (fichier à part).
  worker: {
    format: 'es',
  },
  server: {
    // En développement, le BFF .NET tourne sur le port 5080 (launchSettings.json).
    proxy: {
      '/api': 'http://localhost:5080',
      '/health': 'http://localhost:5080',
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.spec.ts'],
  },
})
