# Déploiement sur le VPS : Paperless-ngx et Home-GED

## Contenu de la stack (`deploy/docker-compose.yml`)

| Service | Image | Rôle |
|---|---|---|
| `webserver` | `paperless-ngx:3.5.1` | Paperless (interface, API, OCR, classement) |
| `db` | `postgres:18-alpine` | Bases de données : `paperless` et `homeged` (sessions Home-GED) |
| `broker` | `valkey:9-alpine` | File de tâches (compatible Redis) |
| `tika` | `apache/tika:3.3.1.0` | Extraction du texte des fichiers Office et e-mails |
| `gotenberg` | `gotenberg:8.38` | Conversion Office → PDF |
| `init-db` | `postgres:18-alpine` | Crée la base `homeged` et son rôle au démarrage, puis s'arrête |
| `app` | `ghcr.io/dockev03/home-ged` | Home-GED : PWA et BFF (seul service exposé à Internet) |
| `backup` | `ghcr.io/dockev03/home-ged-backup` | `pg_dump` + restic vers le NAS, chaque nuit |

Les deux images Home-GED sont construites par la CI GitHub (`.github/workflows/release.yml`) uniquement pour un tag de version `vX.Y.Z` posé sur `main`, en `linux/amd64`. La variable `HOMEGED_VERSION` choisit le tag déployé : une version figée (`1.0.0`, recommandé), la dernière corrective d'une version (`1.0`) ou la dernière version stable (`latest`).

Paperless est figé en version exacte. PostgreSQL et Valkey sont figés en version majeure, comme dans la configuration officielle de Paperless. Les mises à jour se font volontairement, après lecture du changelog.

Choix de configuration notables :
- **OCR uniquement si nécessaire** et **archive PDF/A uniquement pour les scans**, ce qui évite de doubler la place prise par les PDF déjà numériques ;
- 2 workers à 1 thread (4 vCPU) et des limites mémoire par service (environ 6 Go au maximum en cumulé) ;
- aucun port publié : seul NPM joint Home-GED et Paperless ;
- conteneur Home-GED durci : système de fichiers en lecture seule, aucune capacité Linux, utilisateur non root, image « chiseled » sans shell.

## Installation

Deux façons d'installer, au choix : **Docker en ligne de commande** (le script fait tout) ou **Portainer** (tout se fait dans l'interface web). Dans les deux cas :

- la base `homeged` et son rôle sont **créés automatiquement** par le service `init-db` (voir [Base homeged](#base-homeged)) ;
- le premier compte administrateur Paperless peut être créé automatiquement (`PAPERLESS_ADMIN_USER` et `PAPERLESS_ADMIN_PASSWORD`).

### Avant de commencer (une seule fois)

1. **Nginx Proxy Manager** tourne déjà sur le VPS.
2. **Clé SSH de sauvegarde et NAS** : [sauvegardes.md](sauvegardes.md), étapes 1 à 6. Tu peux le faire après coup, mais la sauvegarde échouera tant que ce n'est pas fait.
3. **Accès aux images privées** : GHCR n'accepte pas les tokens « fine-grained ». Sur GitHub, **Settings** > **Developer settings** > **Personal access tokens** > **Tokens (classic)**, crée un token avec la seule portée `read:packages`. Il sert de mot de passe à l'étape « connexion au registre » ci-dessous.

### Option A : Docker en ligne de commande

```bash
sudo git clone https://github.com/dockev03/home-ged.git /opt/home-ged/repo
```

Si le dépôt est privé, utilise une clé de déploiement GitHub en lecture seule, ou un token à portée limitée.

```bash
sudo docker login ghcr.io -u dockev03
```

Le mot de passe demandé est le token `read:packages`.

```bash
cd /opt/home-ged/repo/deploy && sudo ./install.sh
```

Le script :

1. crée `.env` à partir de `.env.example` (droits `600`) ;
2. génère les secrets encore vides (`PAPERLESS_SECRET_KEY`, `POSTGRES_PASSWORD`, `HOMEGED_DB_PASSWORD`, `RESTIC_PASSWORD`), sans jamais remplacer un secret déjà renseigné ;
3. détecte le réseau Docker de NPM et calcule `NPM_SUBNET` ;
4. s'arrête en listant ce qui reste à saisir dans `.env` (`PAPERLESS_URL`, NAS de sauvegarde…) ;
5. une fois `.env` complété et le script relancé : télécharge les images, démarre la stack et attend que Home-GED soit prêt.

> ⚠️ Recopie `RESTIC_PASSWORD` dans ton gestionnaire de mots de passe, **hors du VPS** : sans lui, les sauvegardes sont illisibles.

Le script se relance sans risque, par exemple pour une mise à jour.

### Option B : Portainer

1. **Préparer les variables.** Le plus simple est de lancer, sur le VPS, `sudo ./install.sh --env-seulement` depuis une copie du dossier `deploy/` : il produit un `.env` complet (secrets, réseau et sous-réseau NPM) sans rien démarrer. Sinon, pars de `.env.example`, génère chaque secret avec la commande ci-dessous et trouve le sous-réseau NPM comme indiqué dans [npm.md](npm.md) :

   ```bash
   openssl rand -base64 48 | tr -d '\n/+=' | cut -c1-48
   ```

2. **Déclarer le registre** (une seule fois) : **Registries** > **Add registry** > **Custom registry**, URL `ghcr.io`, utilisateur `dockev03`, mot de passe : le token `read:packages`.
3. **Créer la stack** : **Stacks** > **Add stack**, nom `home-ged`, puis au choix :
   - **Web editor** : colle le contenu de `deploy/docker-compose.yml` ;
   - **Repository** : URL du dépôt, chemin `deploy/docker-compose.yml` (identifiants Git si le dépôt est privé). Portainer peut alors mettre à jour la stack à chaque modification du dépôt.
4. **Variables** : dans **Environment variables**, clique sur **Load variables from .env file** et charge le `.env` préparé à l'étape 1. Vérifie que toutes les valeurs apparaissent.
5. **Deploy the stack**. Au premier démarrage, Paperless met une à deux minutes à s'initialiser ; Home-GED démarre ensuite. Le conteneur `init-db` s'arrête normalement après quelques secondes (état *exited*, code 0) : c'est attendu.

### Ensuite, dans les deux cas

1. **Publier Home-GED et Paperless via NPM** : [npm.md](npm.md).
2. **Configurer Paperless** : [configuration-paperless.md](configuration-paperless.md). Si tu as utilisé `PAPERLESS_ADMIN_PASSWORD`, active le 2FA de ce compte puis vide la variable.
3. **Créer le compte de service des liens de partage** : [Compte de service des liens](#compte-de-service-des-liens).
4. **Lancer la première sauvegarde et tester la restauration** : [sauvegardes.md](sauvegardes.md) §7, puis [restauration.md](restauration.md).
5. **Dérouler la recette** : [../recette/criteres-v1.md](../recette/criteres-v1.md).

### Base homeged

Home-GED a sa propre base dans le même PostgreSQL, avec un rôle `homeged` qui n'a aucun droit sur la base Paperless. Il y stocke ses sessions, ses liens de partage et les clés de chiffrement des tokens Paperless. Les tables sont créées par Home-GED à son démarrage.

Le service `init-db` s'exécute à chaque démarrage de la stack, avant Home-GED et la sauvegarde, puis s'arrête. Il :

- crée le rôle `homeged` et la base `homeged` s'ils n'existent pas ;
- aligne le mot de passe du rôle sur `HOMEGED_DB_PASSWORD` : pour changer ce mot de passe, il suffit de modifier la variable et de redémarrer la stack ;
- retire l'accès par défaut (`PUBLIC`) aux deux bases : `homeged` ne peut pas se connecter à la base Paperless, ni l'inverse. Le propriétaire d'une base garde ses droits.

Il ne supprime jamais rien : sur une installation existante où la base a été créée à la main, il ne fait que vérifier.

### Connexion à Home-GED

- On se connecte avec ses **identifiants Paperless**. Si le 2FA est activé, le code TOTP est demandé ensuite. Les codes de secours ne sont acceptés que par l'interface Paperless.
- La session dure **30 jours sans utilisation**, et **90 jours au maximum**. Chacun voit et peut fermer ses sessions ouvertes (`/api/auth/sessions`). La déconnexion ferme la session Home-GED, pas le token Paperless. Pour tout couper, il faut régénérer le token dans Paperless (**Mon profil** > **Token API**).
- **5 échecs en 15 minutes**, par IP ou par identifiant, bloquent les tentatives pendant 15 minutes. Paperless ajoute sa propre limite (`PAPERLESS_TOKEN_THROTTLE_RATE`, 5 par minute et par IP).
- Ces valeurs se règlent par variables d'environnement du service `app` : `Auth__IdleTimeoutDays`, `Auth__AbsoluteLifetimeDays`, `Auth__MaxFailedAttempts`, `Auth__LockoutMinutes`.
- Le token Paperless est chiffré en base (ASP.NET Data Protection), mais les clés de chiffrement sont dans la même base. Cela protège d'une fuite de la seule table `sessions`, pas d'un vol de la base entière ou d'une sauvegarde déchiffrée.

### Liens de partage

- Le bouton **Envoyer** de la fiche document crée un lien protégé par un **mot de passe obligatoire** (8 caractères au moins, haché en Argon2id), avec une **durée de validité** de 30 jours au plus et, en option, un nombre maximal de téléchargements. Chacun retrouve et révoque ses liens dans **Mes liens**.
- La durée maximale se règle par la variable `Share__MaxExpirationDays` du service `app` (30 par défaut, 365 au plus).
- Les liens expirés ou révoqués sont supprimés de la base 30 jours plus tard, avec leur journal d'accès.
- Le destinataire ouvre `https://ged.<domaine>/s/<identifiant>`, saisit le mot de passe, puis voit l'aperçu et télécharge le fichier. Le fichier transite par Home-GED : Paperless n'est jamais exposé. Le mot de passe donne accès au lien pendant 30 minutes (cookie limité à ce lien), puis il faut le ressaisir.
- Quelle que soit la cause du refus (mauvais mot de passe, lien inconnu, expiré, révoqué ou épuisé), la réponse est la même : « Lien indisponible ou mot de passe incorrect ».
- **5 échecs en 15 minutes** sur un même lien, ou depuis une même IP, bloquent les tentatives pendant 15 minutes (mêmes réglages `Auth__MaxFailedAttempts` et `Auth__LockoutMinutes` que la connexion).
- Chaque tentative d'ouverture est journalisée (date, IP, navigateur, succès ou échec). Le propriétaire du lien la consulte dans **Mes liens** > **Journal**.

#### Compte de service des liens

La page publique lit les documents dans Paperless avec un **compte de service en lecture seule**, jamais avec le compte du propriétaire. Sans lui, les liens ne s'ouvrent pas (message générique) et un avertissement apparaît dans les logs au démarrage.

1. Dans l'administration Paperless (**Utilisateurs et groupes**), crée l'utilisateur `homeged-partage` : actif, sans statut superutilisateur, avec un mot de passe long que personne n'utilisera.
2. Donne-lui une seule permission globale : **Document > Afficher**. Vérifie qu'il n'hérite d'aucune autre permission par un groupe : un groupe qui porte des droits de modification les lui donnerait aussi.
3. Il doit **voir les documents partagés**. Selon la configuration, accorde-lui le droit de lecture sur les documents (permissions par défaut, ou workflow « Attribuer des permissions » sur `homeged-partage`). Un document qu'il ne voit pas donne la réponse « lien indisponible ».
4. Crée son token API : connecte-toi une fois avec ce compte, puis **Mon profil** > **Token API**. Tu peux aussi passer par l'administration Django (**Auth Token** > **Ajouter**).
5. Mets le token dans `HOMEGED_SHARE_TOKEN` (`.env` ou variables de la stack Portainer), puis `sudo docker compose up -d app`.

Pour couper l'accès public d'un coup, supprime ce token dans Paperless : tous les liens répondent alors « momentanément indisponible ».

## Mettre à jour Home-GED

Avec le script (Docker) :

```bash
cd /opt/home-ged/repo/deploy && sudo git pull && sudo ./install.sh
```

Ou à la main :

```bash
sudo docker compose pull app backup && sudo docker compose up -d
```

Avec Portainer : ouvre la stack, puis **Update the stack** en cochant **Re-pull image and redeploy** (ou **Pull and redeploy** pour une stack liée au dépôt Git).

Pour passer à une nouvelle version, change d'abord `HOMEGED_VERSION` (`.env` ou variables de la stack Portainer), par exemple `1.0.0` → `1.1.0`, après lecture de ses notes de version. Avec `1.0` ou `latest`, un simple pull suffit à prendre la dernière version publiée correspondante. Pour revenir en arrière, remets l'ancienne version.

> Les tags d'image `main` et `sha-xxxxxxx` ne sont plus publiés : une installation qui a encore `HOMEGED_VERSION=main` reste figée sur le dernier build de `main`. Passe-la sur une version (`1.0.0`).

## Mettre à jour Paperless

1. Lire le changelog de la nouvelle version.
2. Lancer une sauvegarde manuelle :

   ```bash
   sudo docker compose exec backup /usr/local/bin/entrypoint.sh backup
   ```

3. Changer le tag de l'image `webserver` dans `docker-compose.yml`, puis :

   ```bash
   sudo docker compose pull webserver && sudo docker compose up -d
   ```

## Commandes utiles

```bash
sudo docker compose ps
```

```bash
sudo docker compose logs -f webserver
```

```bash
sudo docker compose exec webserver document_sanity_checker
```
