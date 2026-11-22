# Home-GED

Surcouche web (PWA mobile-first) au-dessus de Paperless-ngx : recherche, consultation, upload, scan et partage des documents du foyer.

- Spécification : [docs/CAHIER_DES_CHARGES.md](docs/CAHIER_DES_CHARGES.md)
- Déploiement : [docs/deploiement/](docs/deploiement/README.md)

## Structure

| Dossier | Contenu |
|---|---|
| `src/back/HomeGed.Api` | BFF ASP.NET Core (.NET 10) : API, sessions, client Paperless ; sert le front buildé |
| `src/front` | PWA Vue 3 + TypeScript + Vite, composants Naive UI |
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
