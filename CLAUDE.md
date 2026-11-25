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

- Branches de travail `feature/etape-N-sujet`, `feature/`, `fix/` ou `docs/sujet`, créées depuis `DEV`, jamais réutilisées.
- Flux : branche de travail → PR vers `DEV` (CI : tests, audits, build Docker sans publication) → PR `DEV` → `main` → tag `vX.Y.Z` (publication des images GHCR, voir Versions).
- Jamais de commit direct sur `DEV`, `main`, `master` ou `staging`.
- Aucun commit ni push sans accord explicite ; présenter le message (Conventional Commits en français) avant.
- Aucun identifiant de modèle d'IA dans commits, PR ou fichiers (pas de `Co-Authored-By` d'IA).

## Versions

- SemVer `vX.Y.Z`. Le tag est la seule voie de publication des images GHCR (`release.yml`, `linux/amd64`) : un push sur `main` sans tag ne publie rien.
- Tag annoté posé **uniquement sur `main`**, juste après le merge d'une PR `DEV` → `main` dont la CI est verte. Un tag hors `main` est refusé par la CI.
- Quand taguer et quel numéro :
  - `MAJOR` : rupture de déploiement (variable obligatoire ajoutée ou renommée, étape manuelle, migration non rétrocompatible, compose à reprendre).
  - `MINOR` : fin d'un lot ou d'une étape du cahier des charges livrée dans `main`.
  - `PATCH` : correctifs seuls (`fix/`, Dependabot), sans nouvelle fonctionnalité.
  - Préversion `vX.Y.Z-rc.N` pour un essai sur le VPS : publiée, mais jamais en `latest`.
- Pas de tag pour un merge `docs/` seul.
- À chaque merge `DEV` → `main`, proposer le numéro du tag suivant avec sa justification ; ne jamais poser ni pousser un tag sans accord.
- Commande : `git tag -a vX.Y.Z -m "vX.Y.Z — résumé" origin/main` puis `git push origin vX.Y.Z`. Mettre à jour `HOMEGED_VERSION` sur le VPS ensuite.
- Un tag publié n'est jamais déplacé ni supprimé : corriger par une nouvelle version.
