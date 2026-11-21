# Home-GED — Cahier des charges

> Statut : **brouillon v0.1** — à valider avant tout développement.

## 1. Contexte et objectif

Les documents du foyer (PDF, Word, Excel, scans, photos) sont aujourd'hui stockés sur un NAS dans une arborescence de dossiers, ce qui rend la recherche longue et pénible.

Objectif : une **GED personnelle** accessible depuis un navigateur (PC et smartphone), hébergée en Docker, qui permet d'importer, classer, retrouver et partager les documents rapidement.

### Choix structurant : ne pas réinventer la roue

L'étude des solutions existantes montre que **Paperless-ngx 3.x** couvre environ 90 % du besoin (OCR, classement automatique, recherche plein texte, versioning, droits, IA locale). Le projet se compose donc de deux briques :

| Brique | Rôle |
|---|---|
| **Paperless-ngx 3.x** (existant) | Moteur GED : stockage, OCR, indexation, classement, versioning, droits, IA |
| **Home-GED** (à développer) | Surcouche : **PWA mobile-first**, **capture caméra avec recadrage**, **liens de partage protégés par mot de passe** |

Home-GED ne réimplémente **aucune** fonction déjà fournie par Paperless. Elle passe exclusivement par son API REST.

### Répartition des interfaces

| Interface | Usage | Utilisateurs |
|---|---|---|
| **Home-GED** (UI/UX propre, Vue 3) | **Usage quotidien** : recherche, consultation, upload, scan, métadonnées, notes, versions, partage | Tout le foyer |
| **Interface web Paperless** | **Administration ponctuelle** : utilisateurs et groupes, règles d'import mail, workflows (rappels, webhooks), définition des champs personnalisés, tâches de maintenance | Administrateur uniquement, accès restreint (voir §5) |

Ces écrans d'administration ne sont pas reproduits dans Home-GED. Ils pourront l'être plus tard, via l'API, seulement si le besoin apparaît à l'usage.

## 2. Périmètre fonctionnel

Les codes reprennent la grille de fonctionnalités validée (`FCT_HGED.xlsx`).

### 2.1 Fonctionnalités couvertes par Paperless-ngx (configuration, pas de développement)

| Code | Fonctionnalité | Mise en œuvre |
|---|---|---|
| A1 | Upload navigateur multi-fichiers | Natif (et via Home-GED) |
| A3 | Dossier surveillé | Natif (`consume`), alimentation à définir — voir §8 |
| A4 | Import par e-mail (IMAP) | Règles mail natives |
| A6 | Détection des doublons | Natif (checksum) |
| A8 | Limite 200 Mo | `client_max_body_size 200m` dans NPM et limite Kestrel dans Home-GED |
| B2–B6 | Tags, types, correspondants, champs perso, date document | Natif |
| B7, B8 | Classement auto par règles / apprentissage | Natif (algorithme « Auto » par défaut, sans config) |
| B9, B10 | Favoris, dossiers virtuels | Vues sauvegardées |
| B11 | Rattachement à une personne du foyer | Champ personnalisé « Personne » |
| C1–C6 | Recherche multicritère, plein texte, OCR, filtres, surlignage | Natif |
| D1–D3, D6 | Aperçu PDF/images/Office, miniatures, thème sombre | Natif (Tika + Gotenberg) |
| E1 | Versioning des documents | Natif (v3) |
| E3, E6 | Corbeille, notes | Natif |
| F1, F2 | Échéances et rappels par e-mail | Champ date perso + workflow programmé avec action e-mail |
| F3, F4 | Tableau de bord, API REST, webhooks | Natif |
| G1, G2, G4 | Multi-utilisateurs, droits, espace « Foyer » | Natif (utilisateurs + groupe « Foyer ») |
| H1, H4 | 2FA TOTP, journal d'audit | Natif |
| I3 | Export complet en format ouvert | `document_exporter` |
| C7, J1–J3 | IA : recherche en langage naturel, extraction, résumé, chat | **V2** — natif en v3 (Ollama), voir §8 |

### 2.2 Fonctionnalités développées dans Home-GED

#### F-PWA — Application mobile-first installable (A2, D4, D5)

| ID | Exigence | Priorité |
|---|---|---|
| PWA-01 | Application installable (manifest, service worker, icônes), plein écran | MVP |
| PWA-02 | Interface mobile-first, utilisable à une main ; reste confortable sur PC | MVP |
| PWA-03 | Thème clair/sombre, suit le système par défaut, choix mémorisé | MVP |
| PWA-04 | Recherche : barre unique (plein texte Paperless) et filtres en « chips » (tag, type, correspondant, personne, période) | MVP |
| PWA-05 | Liste de résultats avec miniature, titre, date, correspondant ; défilement infini | MVP |
| PWA-06 | Fiche document : aperçu, métadonnées (modifiables), notes, versions, téléchargement | MVP |
| PWA-07 | Upload de fichiers depuis l'appareil (multi-fichiers, ≤ 200 Mo), avec suivi de l'OCR et du classement par Paperless | MVP |
| PWA-08 | Raccourcis vers les vues sauvegardées Paperless (favoris, dossiers virtuels) | V1 |
| PWA-09 | Cache hors-ligne des derniers documents consultés | V1 |
| PWA-10 | File d'attente des uploads hors-ligne (IndexedDB), renvoi au retour du réseau | V1 |
| PWA-11 | Web Share Target : « Partager vers Home-GED » depuis une autre appli (Android) | V1 |
| PWA-12 | Tableau de bord « Foyer » : derniers ajouts, échéances à venir | V1 |

#### F-SCAN — Capture caméra (A2)

| ID | Exigence | Priorité |
|---|---|---|
| SCAN-01 | Prise de photo depuis la caméra arrière du téléphone | V1 |
| SCAN-02 | Détection automatique des bords du document, avec ajustement manuel des 4 coins | V1 |
| SCAN-03 | Correction de perspective et filtre « scan » (niveaux de gris / contraste) | V1 |
| SCAN-04 | Document multi-pages : ajouter, réordonner et supprimer des pages | V1 |
| SCAN-05 | Assemblage en un seul PDF côté client, puis envoi à Paperless (qui fait l'OCR) | V1 |
| SCAN-06 | Saisie optionnelle avant envoi : titre, personne, tags | V1 |

Traitement **100 % côté navigateur** (OpenCV.js / jscanify et pdf-lib) : aucune image intermédiaire stockée sur le serveur.

#### F-SHARE — Liens de partage protégés (G3)

| ID | Exigence | Priorité |
|---|---|---|
| SHARE-01 | Créer un lien de partage d'un document avec **mot de passe obligatoire** et **date d'expiration** (maximum configurable, 30 jours par défaut) | V1 |
| SHARE-02 | Option : nombre maximum de téléchargements | V1 |
| SHARE-03 | Page publique `/s/{id}` : saisie du mot de passe, puis aperçu et téléchargement | V1 |
| SHARE-04 | Lister ses liens actifs, révoquer un lien en un clic | V1 |
| SHARE-05 | Journal d'accès par lien (date, IP, succès/échec) | V1 |
| SHARE-06 | Limitation des tentatives : 5 échecs → lien bloqué 15 min (par lien et par IP) | V1 |
| SHARE-07 | Purge automatique des liens expirés | V1 |
| SHARE-08 | Notification e-mail au propriétaire lors du premier accès (option) | V2 |

Règles de sécurité :
- Identifiant de lien aléatoire de 128 bits minimum (CSPRNG), encodé en base64url.
- Mot de passe haché en **Argon2id**, jamais stocké en clair ni journalisé.
- Création autorisée seulement si l'utilisateur a accès au document dans Paperless (vérification par son token).
- Le fichier transite par Home-GED. Paperless n'est jamais exposé au destinataire.
- Les réponses ne révèlent pas si un lien existe, a expiré ou a été révoqué (message générique).

### 2.3 Hors périmètre (décidé)

A5 (import en masse du NAS), A7 (stockage S3), B1 (arborescence de dossiers), E2 (édition Office en ligne), E4 (durées de conservation), E5 (opérations PDF), H2 (SSO OIDC), H3 (chiffrement au repos), H6 (VPN / CrowdSec), H7 (antivirus), I4 (métriques Prometheus).

## 3. Architecture

```
            Internet
               │ HTTPS (Let's Encrypt)
      ┌────────▼─────────┐
      │ Nginx Proxy Mgr  │  (déjà en place sur le VPS)
      └───┬──────────┬───┘
          │          │ (liste d'accès IP, ou non exposé)
ged.<domaine>   paperless.<domaine>
          │          │
┌─────────▼───────┐  │
│ home-ged        │  │      Réseau Docker interne "ged"
│ ASP.NET Core    │  │
│ + front Vue 3   │──┼──API REST──► paperless-ngx ─┬─ postgres
│ (même image)    │  │                             ├─ redis
└────────┬────────┘  └────────────────────────────►├─ tika
         │                                         ├─ gotenberg
         └──► postgres (base "homeged")            └─ (ollama, V2)
```

### 3.1 Stack technique

| Couche | Choix |
|---|---|
| Front | **Vue 3** + TypeScript + Vite, Vue Router, Pinia, `vite-plugin-pwa` (Workbox) |
| UI | Bibliothèque de composants légère et mobile-friendly (à choisir au lot 1) |
| Scan | OpenCV.js / jscanify (bords et perspective), pdf-lib (assemblage PDF) |
| Back | **ASP.NET Core (.NET 10 LTS)**, API minimales, pattern BFF |
| Données | PostgreSQL (base `homeged` dédiée, même instance que Paperless), EF Core |
| Client Paperless | `HttpClient` typé, version d'API figée via l'en-tête `Accept` |
| Hachage | Argon2id (Konscious.Security.Cryptography ou équivalent) |
| Conteneur | Une seule image : le back sert le front buildé (fichiers statiques) |
| CI | GitHub Actions : build, tests, image Docker publiée sur GHCR |

### 3.2 Authentification et autorisations

- L'utilisateur se connecte à Home-GED avec ses **identifiants Paperless**.
- Home-GED obtient son token Paperless (`POST /api/token/`) et le garde **côté serveur**, en session. Le navigateur ne reçoit qu'un cookie de session `HttpOnly`, `Secure`, `SameSite=Strict`.
- Tous les appels se font avec le token de l'utilisateur, donc **les droits sont appliqués par Paperless**.
- Les liens de partage publics (`/s/*`) utilisent un **compte de service Paperless en lecture seule**.
- Limitation des tentatives de connexion et sessions révocables (H8).
- ⚠️ **À valider en premier (POC)** : comportement de `/api/token/` quand le 2FA TOTP est activé sur le compte.

### 3.3 Données propres à Home-GED

Home-GED ne stocke **aucune donnée de document** (Paperless est la seule source de vérité). Sa base contient uniquement :

- `share_links` : id, document_id, owner, password_hash, expires_at, max_downloads, download_count, revoked_at, created_at
- `share_access_log` : share_link_id, timestamp, ip, user_agent, success
- `user_preferences` : thème, filtres favoris…

### 3.4 Règles d'intégration avec Paperless

1. Accès **uniquement via l'API REST**, jamais à sa base ni à son dossier `media`.
2. Version d'API figée et image Paperless taguée précisément (pas de `latest`). Les montées de version sont volontaires, après lecture du changelog.
3. Tests d'intégration contre une instance Paperless réelle en CI (conteneur jetable).

## 4. Exigences non fonctionnelles

| Domaine | Exigence |
|---|---|
| Performance | Résultats de recherche affichés en moins d'1 s pour environ 10 000 documents ; premier affichage de la PWA en moins de 2 s en 4G |
| Compatibilité | Chrome/Edge/Firefox récents sur PC, Chrome Android, Safari iOS 17+ |
| Accessibilité | Contrastes AA, cibles tactiles ≥ 44 px |
| Sécurité | En-têtes HTTP (CSP, HSTS, X-Content-Type-Options, Referrer-Policy) ; secrets via secrets Docker ou variables d'environnement ; dépendances scannées en CI (Dependabot) |
| Exploitation | Healthchecks Docker sur tous les services ; logs structurés (JSON) sur stdout |
| Langue | Interface en français |

## 5. Déploiement (VPS OVH)

- Un `docker-compose.yml` unique, déployable par Portainer (stack), avec un réseau interne `ged`.
- **Seul Home-GED est exposé** sur `ged.<domaine>` via NPM.
- L'interface Paperless (administration, règles, workflows) est sur `paperless.<domaine>`, **restreinte par liste d'accès NPM** (IP fixe du domicile) ou non exposée (accès par tunnel SSH). Décision en §8.
- Volumes : `paperless-data`, `paperless-media`, `paperless-consume`, `paperless-export`, `pg-data`.
- Limite d'upload de 200 Mo alignée entre NPM, Home-GED et Paperless.

### Sauvegardes (I2)

- Toutes les nuits : `pg_dump` (bases paperless et homeged), puis `document_exporter` de Paperless.
- **restic** chiffré vers une cible **hors du VPS** : le NAS du domicile (SFTP / rest-server) et/ou OVH Object Storage.
- Rétention : 7 sauvegardes journalières, 4 hebdomadaires, 12 mensuelles.
- **Test de restauration documenté et exécuté au moins une fois** avant la mise en service.

## 6. Plan de réalisation

| Lot | Contenu | Livrable |
|---|---|---|
| **0** | Déploiement de Paperless-ngx 3.x sur le VPS, configuration (utilisateurs, groupe Foyer, champ Personne, règles mail), sauvegardes restic et test de restauration | GED utilisable avec l'interface Paperless |
| **1 (MVP)** | POC d'authentification (2FA), squelette Home-GED (Vue et .NET), CI et image GHCR, connexion, recherche, fiche document, upload, PWA installable, thème sombre | PWA utilisable au quotidien |
| **2** | Liens de partage protégés (SHARE-01 à 07) | Partage sécurisé |
| **3** | Capture caméra (SCAN-01 à 06) | Scan depuis le téléphone |
| **4** | Hors-ligne, Share Target, tableau de bord Foyer (PWA-08 à 12) | V1 complète |
| **V2** | IA (Ollama) exposée dans la PWA, notification de premier accès aux liens | — |

## 7. Critères d'acceptation (V1)

- Depuis un iPhone et un Android : installer la PWA, photographier une facture de 2 pages, la retrouver par recherche plein texte moins de 2 minutes plus tard.
- Partager un document : sans mot de passe → refus ; mauvais mot de passe 5 fois → blocage ; lien expiré ou révoqué → refus ; chaque accès est journalisé.
- Un utilisateur ne voit jamais un document qui ne lui est pas partagé dans Paperless.
- Restauration complète depuis la sauvegarde restic réussie sur une machine vierge.

## 8. Points ouverts

| # | Sujet | À décider |
|---|---|---|
| 1 | **Dimensionnement du VPS** (vCPU, RAM, disque) | Paperless avec Tika et Gotenberg demande au moins 4 Go de RAM. **Ollama en CPU seul sur un VPS sera lent** : il faudra peut-être prévoir de l'héberger ailleurs ou d'utiliser un petit modèle pour la V2 |
| 2 | Exposition de l'interface Paperless | Liste d'accès IP dans NPM, ou tunnel SSH uniquement |
| 3 | Alimentation du dossier surveillé (A3) sur le VPS | SFTP, Syncthing depuis le NAS, ou abandon au profit de l'import par e-mail et de la PWA |
| 4 | Cible des sauvegardes | NAS du domicile, OVH Object Storage, ou les deux |
| 5 | Boîte mail dédiée à l'import (A4) | Adresse et fournisseur |
| 6 | Bibliothèque de composants Vue | Choix au lot 1 |
