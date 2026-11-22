# Déploiement sur le VPS : Paperless-ngx et Home-GED

## Contenu de la stack (`deploy/docker-compose.yml`)

| Service | Image | Rôle |
|---|---|---|
| `webserver` | `paperless-ngx:3.5.1` | Paperless (interface, API, OCR, classement) |
| `db` | `postgres:18-alpine` | Bases de données : `paperless` et `homeged` (sessions Home-GED) |
| `broker` | `valkey:9-alpine` | File de tâches (compatible Redis) |
| `tika` | `apache/tika:3.3.1.0` | Extraction du texte des fichiers Office et e-mails |
| `gotenberg` | `gotenberg:8.38` | Conversion Office → PDF |
| `app` | `ghcr.io/dockev03/home-ged` | Home-GED : PWA et BFF (seul service exposé à Internet) |
| `backup` | `ghcr.io/dockev03/home-ged-backup` | `pg_dump` + restic vers le NAS, chaque nuit |

Les deux images Home-GED sont construites par la CI GitHub (`.github/workflows/ci.yml`) à chaque push sur `main` et à chaque tag `vX.Y.Z`. La variable `HOMEGED_VERSION` choisit le tag déployé : `main`, `sha-xxxxxxx` ou une version (`1.0.0`).

Paperless est figé en version exacte. PostgreSQL et Valkey sont figés en version majeure, comme dans la configuration officielle de Paperless. Les mises à jour se font volontairement, après lecture du changelog.

Choix de configuration notables :
- **OCR uniquement si nécessaire** et **archive PDF/A uniquement pour les scans**, ce qui évite de doubler la place prise par les PDF déjà numériques ;
- 2 workers à 1 thread (4 vCPU) et des limites mémoire par service (environ 6 Go au maximum en cumulé) ;
- aucun port publié : seul NPM joint Home-GED et Paperless ;
- conteneur Home-GED durci : système de fichiers en lecture seule, aucune capacité Linux, utilisateur non root, image « chiseled » sans shell.

## Installation, dans l'ordre

1. **Préparer le NAS et la clé SSH** : [sauvegardes.md](sauvegardes.md), étapes 1 à 6.
2. **Récupérer le dépôt sur le VPS** :

   ```bash
   sudo git clone https://github.com/dockev03/home-ged.git /opt/home-ged/repo
   ```

   Si le dépôt est privé, utilise une clé de déploiement GitHub en lecture seule, ou un token à portée limitée.

3. **Créer le fichier `.env`** à partir de `deploy/.env.example` et générer les secrets :

   ```bash
   cd /opt/home-ged/repo/deploy && sudo cp .env.example .env && sudo chmod 600 .env
   ```

   ```bash
   openssl rand -base64 48 | tr -d '\n/+=' | cut -c1-64
   ```

4. **Autoriser le VPS à télécharger les images** (paquets GHCR privés). GHCR n'accepte pas les tokens « fine-grained » : sur GitHub, **Settings** > **Developer settings** > **Personal access tokens** > **Tokens (classic)**, crée un token avec la seule portée `read:packages`. Puis, sur le VPS :

   ```bash
   sudo docker login ghcr.io -u dockev03
   ```

   Le mot de passe demandé est ce token.

5. **Démarrer la base, puis créer la base `homeged`** (une seule fois) :

   ```bash
   sudo docker compose pull
   ```

   ```bash
   sudo docker compose up -d db
   ```

   Puis suis [Créer la base homeged](#créer-la-base-homeged).

6. **Démarrer la stack** :

   ```bash
   sudo docker compose up -d
   ```

7. **Publier Home-GED et Paperless via NPM** : [npm.md](npm.md).
8. **Configurer Paperless** : [configuration-paperless.md](configuration-paperless.md).
9. **Lancer la première sauvegarde et tester la restauration** : [sauvegardes.md](sauvegardes.md) §7, puis [restauration.md](restauration.md).

### Créer la base homeged

Home-GED a sa propre base dans le même PostgreSQL, avec un rôle `homeged` qui n'a aucun droit sur la base Paperless. Il y stocke ses sessions et les clés de chiffrement des tokens Paperless. Les tables sont créées par Home-GED à son démarrage.

Le script d'initialisation de PostgreSQL ne s'exécute qu'à la création du volume `pgdata`, déjà fait : la base se crée à la main, une seule fois.

1. Créer le rôle. Au mot de passe demandé, colle la valeur de `HOMEGED_DB_PASSWORD` du fichier `.env` :

   ```bash
   sudo docker compose exec db createuser -U paperless --pwprompt homeged
   ```

2. Créer la base, propriété du rôle :

   ```bash
   sudo docker compose exec db createdb -U paperless -O homeged homeged
   ```

3. Empêcher les autres rôles de s'y connecter, et `homeged` de se connecter à la base Paperless :

   ```bash
   sudo docker compose exec db psql -U paperless -d paperless \
     -c "REVOKE CONNECT ON DATABASE homeged FROM PUBLIC" \
     -c "REVOKE CONNECT ON DATABASE paperless FROM PUBLIC"
   ```

   Le propriétaire d'une base garde ses droits : `paperless` reste connecté à `paperless`, et `homeged` à `homeged`.

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

### Et Portainer ?

Toutes les images sont désormais téléchargeables : la stack est entièrement gérable depuis Portainer. Déclare le registre une fois (**Registries** > **Add registry** > **Custom registry**, URL `ghcr.io`, utilisateur `dockev03`, mot de passe : le token `read:packages`), puis crée la stack depuis le dépôt Git (**Stacks** > **Add stack** > **Repository**, chemin `deploy/docker-compose.yml`) en saisissant les variables de `.env.example`.

## Mettre à jour Home-GED

> **Première mise à jour avec la base `homeged`** (version qui ajoute la connexion) : ajoute `HOMEGED_DB_PASSWORD` dans `.env` (ou dans les variables de la stack Portainer), puis suis [Créer la base homeged](#créer-la-base-homeged) **avant** de mettre à jour. Sinon Home-GED ne démarre pas, et la sauvegarde échoue sur la base manquante.

```bash
sudo docker compose pull app backup && sudo docker compose up -d
```

Avec `HOMEGED_VERSION=main`, cette commande prend le dernier build de `main`. Pour figer une version, mets un tag (`1.0.0`) ou un `sha-xxxxxxx`.

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
