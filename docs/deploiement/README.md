# Déploiement — étape 0 : Paperless-ngx sur le VPS

## Contenu de la stack (`deploy/docker-compose.yml`)

| Service | Image | Rôle |
|---|---|---|
| `webserver` | `paperless-ngx:3.5.1` | Paperless (interface, API, OCR, classement) |
| `db` | `postgres:18-alpine` | Base de données |
| `broker` | `valkey:9-alpine` | File de tâches (compatible Redis) |
| `tika` | `apache/tika:3.3.1.0` | Extraction du texte des fichiers Office et e-mails |
| `gotenberg` | `gotenberg:8.38` | Conversion Office → PDF |
| `backup` | construite localement (`deploy/backup/`) | `pg_dump` + restic vers le NAS, chaque nuit |

Paperless est figé en version exacte. PostgreSQL et Valkey sont figés en version majeure, comme dans la configuration officielle de Paperless. Les mises à jour se font volontairement, après lecture du changelog.

Choix de configuration notables :
- **OCR uniquement si nécessaire** et **archive PDF/A uniquement pour les scans**, ce qui évite de doubler la place prise par les PDF déjà numériques ;
- 2 workers à 1 thread (4 vCPU) et des limites mémoire par service (environ 6 Go au maximum en cumulé) ;
- aucun port publié : seul NPM joint Paperless.

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

4. **Construire l'image de sauvegarde et démarrer la stack** :

   ```bash
   sudo docker compose build backup
   ```

   ```bash
   sudo docker compose up -d
   ```

5. **Publier Paperless via NPM** : [npm.md](npm.md).
6. **Configurer Paperless** : [configuration-paperless.md](configuration-paperless.md).
7. **Lancer la première sauvegarde et tester la restauration** : [sauvegardes.md](sauvegardes.md) §7, puis [restauration.md](restauration.md).

### Et Portainer ?

Le service `backup` utilise une image construite localement, que l'éditeur de stacks de Portainer ne sait pas construire. Pour l'étape 0, on déploie donc en ligne de commande. Portainer affiche alors la stack en mode « limité » : logs, console, redémarrages et statistiques fonctionnent. À l'étape 1, la CI GitHub publiera l'image de sauvegarde sur GHCR, et la stack deviendra entièrement gérable depuis Portainer.

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
