# Déploiement sur le VPS : Paperless-ngx et Home-GED

## Contenu de la stack (`deploy/docker-compose.yml`)

| Service | Image | Rôle |
|---|---|---|
| `webserver` | `paperless-ngx:3.5.1` | Paperless (interface, API, OCR, classement) |
| `db` | `postgres:18-alpine` | Base de données |
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

5. **Démarrer la stack** :

   ```bash
   sudo docker compose pull
   ```

   ```bash
   sudo docker compose up -d
   ```

6. **Publier Home-GED et Paperless via NPM** : [npm.md](npm.md).
7. **Configurer Paperless** : [configuration-paperless.md](configuration-paperless.md).
8. **Lancer la première sauvegarde et tester la restauration** : [sauvegardes.md](sauvegardes.md) §7, puis [restauration.md](restauration.md).

### Et Portainer ?

Toutes les images sont désormais téléchargeables : la stack est entièrement gérable depuis Portainer. Déclare le registre une fois (**Registries** > **Add registry** > **Custom registry**, URL `ghcr.io`, utilisateur `dockev03`, mot de passe : le token `read:packages`), puis crée la stack depuis le dépôt Git (**Stacks** > **Add stack** > **Repository**, chemin `deploy/docker-compose.yml`) en saisissant les variables de `.env.example`.

## Mettre à jour Home-GED

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
