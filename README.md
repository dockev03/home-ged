# Home-GED

Surcouche web (PWA mobile-first) au-dessus de Paperless-ngx : recherche, consultation, upload, scan et partage des documents du foyer.

- Spécification : [docs/CAHIER_DES_CHARGES.md](docs/CAHIER_DES_CHARGES.md)
- Déploiement détaillé : [docs/deploiement/](docs/deploiement/README.md)

## Structure

| Dossier | Contenu |
|---|---|
| `src/back/HomeGed.Api` | BFF ASP.NET Core (.NET 10) : API, sessions, client Paperless ; sert le front buildé |
| `src/front` | PWA Vue 3 + TypeScript + Vite, styles Bootstrap 5 (CSS seul, composants interactifs en Vue) |
| `tests/HomeGed.Api.Tests` | Tests unitaires du back |
| `tests/HomeGed.IntegrationTests` | Tests contre un Paperless et un PostgreSQL réels (`PAPERLESS_URL`, `PAPERLESS_USER`, `PAPERLESS_PASSWORD`, `HOMEGED_DB`) |
| `deploy` | Stack Docker Compose (Paperless, Home-GED, sauvegardes) |

## Développement

Prérequis : SDK .NET 10, Node 22, Docker.

Back (port 5080). Il attend un Paperless sur `http://localhost:8000` et un PostgreSQL local avec une base `homeged` (rôle et mot de passe `homeged`, voir `launchSettings.json`). Les tables sont créées au démarrage :

```bash
docker run -d --name homeged-db -p 5432:5432 -e POSTGRES_DB=homeged -e POSTGRES_USER=homeged -e POSTGRES_PASSWORD=homeged postgres:18-alpine
```

```bash
dotnet run --project src/back/HomeGed.Api
```

Le cookie de session est `Secure` : en HTTP, seul `localhost` l'accepte. Passe par `http://localhost:5173`, pas par `127.0.0.1`.

Nouvelle migration EF Core (outil local, `dotnet tool restore` une fois) :

```bash
dotnet ef migrations add <Nom> --project src/back/HomeGed.Api --output-dir Data/Migrations
```

Front (port 5173, relaie `/api` vers le back) :

```bash
cd src/front && npm ci && npm run dev
```

Vérifications lancées par la CI :

```bash
dotnet build HomeGed.slnx && dotnet test tests/HomeGed.Api.Tests && dotnet format HomeGed.slnx --verify-no-changes
```

```bash
cd src/front && npm run lint && npm test && npm run build
```

## Déploiement Docker

Images publiées sur GHCR (paquets privés, `linux/amd64`) à chaque version :

| Image | Rôle |
|---|---|
| `ghcr.io/dockev03/home-ged:<version>` | PWA et BFF |
| `ghcr.io/dockev03/home-ged-backup:<version>` | Sauvegardes `pg_dump` + restic |

Tags disponibles pour une version `v1.2.3` : `1.2.3` (figé, recommandé), `1.2` (dernier correctif de la 1.2) et `latest` (dernière version stable).

Prérequis : VPS Linux amd64, Docker avec le plugin `compose` v2, Nginx Proxy Manager et son réseau Docker, un token GitHub « classic » de portée `read:packages`.

```bash
echo "<token read:packages>" | sudo docker login ghcr.io -u dockev03 --password-stdin
```

```bash
sudo git clone https://github.com/dockev03/home-ged.git /opt/home-ged/repo
```

```bash
cd /opt/home-ged/repo/deploy && sudo ./install.sh
```

`install.sh` crée `.env` à partir de [`deploy/.env.example`](deploy/.env.example), génère les secrets, renseigne le réseau NPM, télécharge les images et démarre la stack [`deploy/docker-compose.yml`](deploy/docker-compose.yml). Complète ensuite les valeurs signalées (URL publique, SMTP, sauvegarde). Sans le script :

```bash
cp .env.example .env && sudo docker compose up -d
```

La version déployée est fixée par `HOMEGED_VERSION` dans `.env`. Pour mettre à jour, change cette valeur (par exemple `1.0.0` → `1.1.0`), puis :

```bash
sudo docker compose pull app backup && sudo docker compose up -d
```

Portainer, proxy, sauvegardes et restauration : [docs/deploiement/](docs/deploiement/README.md).

## Workflow Git

```
feature/xxx ─┐
fix/xxx ─────┼─► PR ─► DEV ─► PR ─► main
docs/xxx ────┘                       │
                                     └─► tag vX.Y.Z ─► images GHCR (amd64)
```

- Les branches de travail partent de `DEV` et y reviennent par pull request.
- **CI** (`.github/workflows/ci.yml`), sur les PR vers `DEV` et `main` et à chaque push sur `DEV` : build, format, tests unitaires et d'intégration, audit des dépendances (NuGet et npm), build Docker de contrôle. **Aucune image publiée.**
- **Release** (`.github/workflows/release.yml`), uniquement sur un tag `vX.Y.Z` : vérifie que le commit tagué appartient à `main`, rejoue la CI puis publie les images `linux/amd64` sur GHCR (`1.2.3`, `1.2`, `latest`). Un push sur `main` sans tag ne publie rien.
- Quand et comment poser un tag de version : voir [CLAUDE.md](CLAUDE.md#versions).
- Dependabot ouvre ses PR vers `DEV`.
