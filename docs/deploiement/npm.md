# Nginx Proxy Manager : publier Home-GED et Paperless

Aucun service ne publie de port sur le VPS. NPM les joint par le réseau Docker défini dans `NPM_NETWORK`, sous les noms `home-ged` et `paperless`.

| Domaine | Cible | Accès |
|---|---|---|
| `ged.<domaine>` | `home-ged:8080` | **Internet** (usage quotidien, PC et téléphones) |
| `paperless.<domaine>` | `paperless:8000` | IP fixe de la maison uniquement (administration) |

> ⚠️ **Exposition réseau** : `paperless.<domaine>` doit être restreint à l'IP fixe de la maison. Sans liste d'accès, l'interface d'administration serait ouverte à tout Internet.

## 1. Trouver le réseau de NPM

```bash
docker inspect $(docker ps -qf name=npm) --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{"\n"}}{{end}}'
```

Reporte le nom obtenu (par exemple `npm_default`) dans la variable `NPM_NETWORK`, et son sous-réseau dans `NPM_SUBNET` :

```bash
docker network inspect npm_default --format '{{(index .IPAM.Config 0).Subnet}}'
```

Home-GED ne fait confiance à l'en-tête `X-Forwarded-For` (IP du client) que s'il vient de ce sous-réseau. C'est cette IP qui sert à limiter les tentatives de connexion par appareil.

## 2. Liste d'accès « Maison »

**Access Lists** > **Add Access List** :
- **Details** : nom `Maison`, *Satisfy Any* décoché ;
- **Authorization** : aucun utilisateur ;
- **Access** : `allow <IP_FIXE_MAISON>`, puis `deny all`.

## 3. Proxy host Paperless

**Hosts** > **Proxy Hosts** > **Add Proxy Host** :

| Onglet | Réglage |
|---|---|
| Details | Domain : `paperless.<domaine>` ; Scheme : `http` ; Forward Hostname : `paperless` ; Port : `8000` |
| Details | ✅ Block Common Exploits ; ✅ Websockets Support (suivi des imports en temps réel) |
| Details | Access List : `Maison` |
| SSL | Certificat Let's Encrypt ; ✅ Force SSL ; ✅ HTTP/2 ; ✅ HSTS |
| Advanced | voir ci-dessous |

Configuration personnalisée (onglet **Advanced**) :

```nginx
client_max_body_size 200m;
proxy_read_timeout 300s;
proxy_send_timeout 300s;
```

## 4. Proxy host Home-GED

> ⚠️ **Exposition réseau** : `ged.<domaine>` est ouvert à Internet, c'est voulu (usage depuis les téléphones). La sécurité repose sur la connexion Paperless avec 2FA, les sessions côté serveur et la limitation des tentatives.

| Onglet | Réglage |
|---|---|
| Details | Domain : `ged.<domaine>` ; Scheme : `http` ; Forward Hostname : `home-ged` ; Port : `8080` |
| Details | ✅ Block Common Exploits ; pas de liste d'accès |
| SSL | Certificat Let's Encrypt ; ✅ Force SSL ; ✅ HTTP/2 ; ✅ HSTS |
| Advanced | même configuration que pour Paperless (`client_max_body_size 200m` et délais) |

## 5. DNS

Crée les enregistrements `A` (et `AAAA` si besoin) `ged.<domaine>` et `paperless.<domaine>` qui pointent vers l'IP du VPS.

## 6. Vérification

- Depuis la maison, `https://paperless.<domaine>` affiche la page de connexion.
- Depuis le réseau mobile (Wi-Fi coupé), la même URL renvoie **403 Forbidden**.
- Depuis le réseau mobile, `https://ged.<domaine>` affiche Home-GED et `https://ged.<domaine>/health/ready` renvoie `Healthy`.
