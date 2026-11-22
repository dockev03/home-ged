# CLAUDE.md

## Projet

Home-GED : surcouche web (PWA mobile-first) au-dessus de **Paperless-ngx 3.x**, hébergée en Docker sur un VPS derrière Nginx Proxy Manager.

- Spécification de référence : `docs/CAHIER_DES_CHARGES.md`.
- Stack : front **Vue 3** + TypeScript + Vite ; back **ASP.NET Core (.NET 10)** en BFF ; PostgreSQL.
- Paperless est utilisé **exclusivement via son API REST** : jamais d'accès direct à sa base ni à son dossier `media`.
- L'UI/UX quotidienne est la nôtre. L'interface Paperless reste réservée à l'administration.
- Langue : interface, documentation et messages de commit en français.

## Méthode

- Étape N = phase N, après validation de la précédente. ≤ 6 fonctionnalités, regroupées par code touché.
- Avant de coder : plan court, attendre l'accord. Fin : build + tests + `dotnet format` verts, résumé fait / reste. Pas de refactor hors étape.
- Licence ou API non conforme à la spec : signaler, ne pas contourner. Aucun paquet sans accord.
- Changement à risque (droits, exposition réseau, suppression de données) : l'annoncer en une ligne en tête de réponse, attendre l'accord.

## Git

- Branches de travail `feature/etape-N-sujet`, `feature/`, `fix/` ou `docs/sujet`, créées depuis `dev`, jamais réutilisées.
- Flux : branche de travail → PR vers `dev` (CI : tests, audits, build Docker sans publication) → PR `dev` → `main` (publication des images GHCR).
- Jamais de commit direct sur `dev`, `main`, `master` ou `staging`.
- Aucun commit ni push sans accord explicite ; présenter le message (Conventional Commits en français) avant.
- Aucun identifiant de modèle d'IA dans commits, PR ou fichiers (pas de `Co-Authored-By` d'IA).
