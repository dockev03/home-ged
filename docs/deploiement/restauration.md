# Restauration depuis la sauvegarde restic

Procédure pour reconstruire la GED sur un VPS **vierge** (perte du VPS, migration) ou après une corruption.

> ⚠️ **Suppression de données** : les étapes 4 et 5 écrasent le contenu des volumes et de la base. Sur un VPS existant, ne lance la procédure qu'après avoir confirmé que les données actuelles sont perdues ou inutiles.

Ce qu'il te faut :
- le dépôt Git `home-ged` (dossier `deploy/`) ;
- les valeurs du fichier `.env`, et **surtout `RESTIC_PASSWORD`** (sans lui, rien n'est récupérable) ;
- la clé SSH de sauvegarde (`id_ed25519`) et le `known_hosts` dans `BACKUP_SSH_DIR` (voir [sauvegardes.md](sauvegardes.md)). Sur un nouveau VPS, tu peux générer une nouvelle clé et l'autoriser sur le NAS ;
- si l'IP du VPS change : mettre à jour la règle du pare-feu de la box et l'option `from=` de la clé sur le NAS.

Toutes les commandes se lancent depuis le dossier `deploy/` sur le VPS.

## 1. Préparer la stack sans démarrer Paperless

```bash
docker compose build backup
```

```bash
docker compose up -d db broker
```

## 2. Lister les sauvegardes disponibles

```bash
docker compose run --rm --no-deps backup restic snapshots
```

Repère l'ID du snapshot à restaurer, ou utilise `latest`.

## 3. Créer un dossier temporaire pour le dump

```bash
sudo mkdir -p /opt/home-ged/restore
```

## 4. Restaurer les fichiers dans les volumes

```bash
docker compose run --rm --no-deps \
  -v home-ged_data:/restore/source/data \
  -v home-ged_media:/restore/source/media \
  -v /opt/home-ged/restore:/restore/tmp/dumps \
  backup restic restore latest --target /restore
```

## 5. Restaurer les bases de données

Base Paperless :

```bash
docker compose run --rm --no-deps \
  -v /opt/home-ged/restore:/dumps:ro \
  backup pg_restore --clean --if-exists --no-owner --exit-on-error \
  -d paperless /dumps/paperless.dump
```

Base Home-GED (sessions et clés de chiffrement). Sur une machine vierge, crée d'abord le rôle et la base : [Créer la base homeged](README.md#créer-la-base-homeged). `--role=homeged` rend les tables restaurées propriété de ce rôle :

```bash
docker compose run --rm --no-deps \
  -v /opt/home-ged/restore:/dumps:ro \
  backup pg_restore --clean --if-exists --no-owner --role=homeged --exit-on-error \
  -d homeged /dumps/homeged.dump
```

Sans cette base, rien n'est perdu côté documents : il suffit de se reconnecter à Home-GED.

## 6. Démarrer et vérifier

```bash
docker compose up -d
```

```bash
docker compose exec webserver document_sanity_checker
```

Le contrôle d'intégrité doit se terminer sans erreur (fichiers présents, sommes de contrôle correctes). Vérifie ensuite dans l'interface que les documents s'ouvrent et que la recherche fonctionne. En cas de doute sur l'index de recherche :

```bash
docker compose exec webserver document_index reindex
```

## 7. Nettoyer

```bash
sudo rm -rf /opt/home-ged/restore
```

## Restaurer un seul fichier

Pour récupérer un document précis sans tout restaurer :

```bash
docker compose run --rm --no-deps -v /opt/home-ged/restore:/out \
  backup restic restore latest --target /out --include '/source/media/documents/originals/<chemin>'
```

## Test annuel

Rejouer cette procédure **au moins une fois par an** (et après chaque changement de la stack de sauvegarde), idéalement sur une machine de test, pour s'assurer que les sauvegardes sont réellement exploitables.
