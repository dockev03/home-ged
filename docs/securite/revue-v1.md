# Revue de sécurité — V1 (lot 5)

Revue avant la mise en production : code de Home-GED, stack Docker (`deploy/docker-compose.yml`), CI et documentation de déploiement. Référentiel : exigences des §3.2, §4 et §5 du cahier des charges, et principaux risques OWASP (contrôle d'accès, authentification, injection, configuration, composants vulnérables).

**Légende** : ✅ conforme · ⚠️ point d'attention accepté ou action recommandée · ❌ défaut à corriger.

## Synthèse

Aucun défaut bloquant n'a été relevé. Un défaut mineur (❌) concerne le message d'erreur à la connexion. Les points ⚠️ sont des risques résiduels acceptés ou des durcissements facultatifs, listés en fin de document.

Ce qui est vérifié **automatiquement en CI** à chaque PR :

| Vérification | Où |
|---|---|
| Cloisonnement : un utilisateur ne voit, ne modifie, ne supprime ni ne partage un document qu'on ne lui a pas partagé dans Paperless | `tests/HomeGed.IntegrationTests/AcceptanceIntegrationTests.cs` (Paperless réel) |
| Liens de partage : mot de passe obligatoire, blocage après 5 échecs, lien expiré, révoqué ou épuisé refusé, journal de chaque tentative | même fichier (Paperless et PostgreSQL réels) |
| Image Docker : healthcheck, en-têtes de sécurité, aucun en-tête `Server`, API fermée sans session, processus non root, fonctionnement en lecture seule et sans capacité Linux | `tests/smoke/image.sh` |
| Dépendances : NuGet (paquets vulnérables, transitifs compris), npm (sévérité haute et critique) | job `audit` de `ci.yml`, et Dependabot (NuGet, npm, Actions, images Docker) |

## 1. Exposition réseau

| Point | État | Constat |
|---|---|---|
| Ports publiés sur l'hôte | ✅ | Aucun. Seul NPM joint `app` et `webserver` par le réseau `${NPM_NETWORK}`. |
| Paperless exposé | ✅ | `paperless.<domaine>` est restreint à l'IP fixe du domicile par une liste d'accès NPM (§8, point 2, et `docs/deploiement/npm.md`). Les liens publics ne passent jamais par Paperless : le fichier transite par Home-GED. |
| Services internes (PostgreSQL, Valkey, Tika, Gotenberg) | ✅ | Joignables uniquement depuis le réseau interne `ged`. |
| Gotenberg | ✅ | JavaScript désactivé et accès limité aux fichiers locaux (`--chromium-allow-list`). |
| IP client (`X-Forwarded-For`) | ⚠️ | L'en-tête n'est accepté que depuis `NPM_SUBNET`, avec un seul saut de proxy (`ForwardLimit = 1`). En revanche, **tout** conteneur branché sur le réseau de NPM peut fournir une IP falsifiée. Conséquence limitée : seul le blocage par IP est contournable. Les blocages par identifiant et par lien restent actifs. Recommandation : ne brancher sur le réseau NPM que des conteneurs de confiance. |
| En-tête `Host` | ⚠️ | `AllowedHosts` vaut `*`. Pas d'impact connu : Home-GED ne construit aucune URL à partir de l'hôte, et NPM ne transmet que les domaines configurés. Durcissement facultatif : `AllowedHosts=ged.<domaine>` dans les variables du service `app`. |

## 2. Authentification et sessions

| Point | État | Constat |
|---|---|---|
| Identifiants | ✅ | Ceux de Paperless (`/api/token/`) ; Home-GED ne stocke aucun mot de passe d'utilisateur. Le 2FA TOTP est pris en charge. |
| Cookie de session | ✅ | `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`. Jeton aléatoire, dont seule l'empreinte est en base. |
| Durée de vie | ✅ | 30 jours d'inactivité, 90 jours au maximum ; sessions listables et révocables par l'utilisateur. |
| Limitation des tentatives | ✅ | 5 échecs en 15 minutes, par IP et par identifiant, puis blocage de 15 minutes. Paperless ajoute sa propre limite (`PAPERLESS_TOKEN_THROTTLE_RATE` à 5/min). |
| Persistance des blocages | ⚠️ | Compteurs en mémoire : un redémarrage du conteneur les remet à zéro. Accepté (instance unique, redémarrages rares) ; la limite de Paperless reste active. |
| Token Paperless au repos | ⚠️ | Chiffré par ASP.NET Data Protection, mais les clés sont dans la même base. Protège d'une fuite de la seule table `sessions`, pas d'un vol de la base entière. Documenté dans `docs/deploiement/README.md`. |
| Compte sans droit « Paramètres de l'interface » | ❌ | Paperless répond 403 sur `/api/ui_settings/`, et Home-GED affiche « Paperless ne répond pas » (503). Le refus lui-même est correct, mais le message égare l'utilisateur. **Contournement documenté** (droit *afficher* sur « Paramètres de l'interface » pour le groupe Foyer, `configuration-paperless.md`). **Correction proposée** pour une prochaine étape : renvoyer un message explicite (« compte Paperless sans le droit Paramètres de l'interface »). |
| Compte administrateur créé au démarrage | ⚠️ | `PAPERLESS_ADMIN_PASSWORD` reste lisible dans la configuration de la stack tant qu'il n'est pas vidé. La documentation demande de le vider une fois le compte créé et son 2FA activé. |

## 3. Contrôle d'accès aux documents

| Point | État | Constat |
|---|---|---|
| Droits sur les documents | ✅ | Toutes les requêtes partent avec le token de l'utilisateur : c'est Paperless qui applique les droits. Home-GED n'a aucun accès direct à la base ni au dossier `media` de Paperless (§3.4). Vérifié en CI avec un second utilisateur réel. |
| Création d'un lien de partage | ✅ | Le droit de lecture est vérifié dans Paperless avec le token du créateur. Un document invisible donne 404 (testé). |
| Journal et révocation d'un lien | ✅ | Réservés au propriétaire du lien. |
| Compte de service des liens | ✅ | Lecture seule (`Document > Afficher`), utilisé uniquement pour les pages publiques ; jamais le compte du propriétaire. |
| Rôle PostgreSQL `homeged` | ✅ | Propriétaire de sa seule base ; `CONNECT` retiré à `PUBLIC` sur les deux bases (`init-db`). Vérifié : `homeged` ne peut pas se connecter à la base `paperless`. |

## 4. Liens de partage publics

| Point | État | Constat |
|---|---|---|
| Mot de passe | ✅ | Obligatoire, 8 caractères au moins, haché en Argon2id. |
| Réponse unique | ✅ | Même message et même coût de calcul pour un lien inconnu, expiré, révoqué, épuisé ou un mauvais mot de passe : rien ne révèle si le lien existe. |
| Blocage | ✅ | 5 échecs, par lien et par IP, puis blocage de 15 minutes, y compris avec le bon mot de passe (testé). |
| Ticket d'accès | ✅ | Cookie `__Secure-`, `SameSite=Strict`, limité au chemin du lien, chiffré, valable 30 minutes. L'état du lien est revérifié à chaque requête : une révocation coupe immédiatement un ticket déjà délivré (testé). |
| Quota de téléchargements | ✅ | Compté de façon atomique en base, jamais dépassé même en parallèle (testé). |
| Cache et indexation | ✅ | `Cache-Control: no-store` et `X-Robots-Tag: noindex, nofollow`. |
| Identifiant du lien | ✅ | 128 bits aléatoires. |

## 5. Navigateur : en-têtes et injection

| Point | État | Constat |
|---|---|---|
| CSP | ✅ | `default-src 'self'`, ni `unsafe-inline` ni `unsafe-eval` pour les pages, `object-src 'none'`, `frame-ancestors 'none'`. Seule exception : `unsafe-eval` pour le worker de recadrage OpenCV, confiné à ce worker sans DOM ni réseau (`connect-src 'none'`). |
| Aperçu des documents | ✅ | Affichable en cadre seulement depuis Home-GED (`SAMEORIGIN`), et en `sandbox` si le type n'est pas inerte (HTML, SVG…). |
| Autres en-têtes | ✅ | `X-Content-Type-Options`, `Referrer-Policy: no-referrer`, `Permissions-Policy` (caméra pour le scan uniquement), `Cross-Origin-Opener-Policy`, HSTS d'un an en HTTPS. Pas d'en-tête `Server`. |
| CSRF | ✅ | Cookies `SameSite=Strict`, plus l'en-tête `X-Requested-With` obligatoire sur toute requête d'API qui modifie des données. |
| Injection SQL | ✅ | EF Core paramétré. Dans `init-db`, le mot de passe passe par une variable psql (`:'pwd'`, échappée par `format('%L')`), jamais par concaténation. Vérifié avec un mot de passe contenant `'`, `;` et `--`. |
| Taille des envois | ✅ | 200 Mo, alignés entre NPM, Kestrel et Paperless (§5). |

## 6. Conteneurs, secrets et chaîne d'approvisionnement

| Point | État | Constat |
|---|---|---|
| Conteneur Home-GED | ✅ | Image « chiseled » sans shell, utilisateur non root, système de fichiers en lecture seule, aucune capacité Linux, `no-new-privileges`, mémoire limitée. Vérifié par `tests/smoke/image.sh`. |
| Autres conteneurs | ⚠️ | Paperless, Tika et Gotenberg tournent avec les réglages de leurs images officielles (écriture nécessaire). Accepté : ils ne sont pas joignables depuis Internet. |
| Secrets | ✅ | Variables d'environnement (§4) ; `.env` en `600`, ignoré par Git, et généré par `install.sh` avec des secrets aléatoires. Aucun secret dans le dépôt ni dans les images. |
| Secrets visibles par `docker inspect` | ⚠️ | Inhérent aux variables d'environnement. Accepté : l'accès à Docker équivaut déjà à un accès root sur le VPS. |
| Images | ✅ | Paperless figé en version exacte, PostgreSQL et Valkey en version majeure. Images Home-GED privées sur GHCR, publiées seulement depuis `main` ou un tag. |
| Dépendances | ✅ | Audits NuGet et npm bloquants en CI ; Dependabot hebdomadaire. |
| Analyse des images | ⚠️ | Pas d'analyse de vulnérabilités des images construites (paquets système). Recommandation : ajouter un scanner d'images (par exemple Trivy) au job Docker. C'est un nouvel outil, à décider. |
| Workflows GitHub | ✅ | `permissions: contents: read` par défaut ; `packages: write` seulement pour la publication. |

## 7. Exploitation

| Point | État | Constat |
|---|---|---|
| Logs | ✅ | JSON sur stdout, rotation (`10m` × 3). Aucun mot de passe ni token journalisé : les échecs de connexion n'enregistrent que l'identifiant et l'IP. |
| Healthchecks | ✅ | Sur tous les services qui en exposent un. `/health` (vivacité) ne dépend de rien ; `/health/ready` vérifie Paperless et la base. |
| Sauvegardes | ✅ | restic chiffré hors du VPS, rétention 7/4/12, procédure de restauration documentée (à exécuter une fois : critère §7). |

## Actions

| # | Action | Priorité | Statut |
|---|---|---|---|
| 1 | Message explicite quand le compte n'a pas le droit « Paramètres de l'interface » (au lieu de « Paperless ne répond pas ») | Basse | À planifier (correction de code) |
| 2 | Documenter le droit « Paramètres de l'interface » pour le groupe Foyer | Haute | ✅ Fait (`configuration-paperless.md`) |
| 3 | Vider `PAPERLESS_ADMIN_PASSWORD` après la première connexion | Moyenne | Documenté ; à faire à l'installation |
| 4 | Ne brancher que des conteneurs de confiance sur le réseau NPM | Moyenne | Recommandation d'exploitation |
| 5 | `AllowedHosts=ged.<domaine>` pour le service `app` | Basse | Facultatif |
| 6 | Analyse des vulnérabilités des images en CI | Basse | À décider (nouvel outil) |
