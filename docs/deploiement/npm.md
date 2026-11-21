# Nginx Proxy Manager : publier Paperless

Paperless ne publie aucun port sur le VPS. NPM le joint par le réseau Docker défini dans `NPM_NETWORK`, sous le nom `paperless`.

> ⚠️ **Exposition réseau** : `paperless.<domaine>` doit être restreint à l'IP fixe de la maison. Sans liste d'accès, l'interface d'administration serait ouverte à tout Internet.

## 1. Trouver le réseau de NPM

```bash
docker inspect $(docker ps -qf name=npm) --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{"\n"}}{{end}}'
```

Reporte le nom obtenu (par exemple `npm_default`) dans la variable `NPM_NETWORK`.

## 2. Liste d'accès « Maison »

**Access Lists** > **Add Access List** :
- **Details** : nom `Maison`, *Satisfy Any* décoché ;
- **Authorization** : aucun utilisateur ;
- **Access** : `allow <IP_FIXE_MAISON>`, puis `deny all`.

## 3. Proxy host

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

## 4. DNS

Crée un enregistrement `A` (et `AAAA` si besoin) `paperless.<domaine>` qui pointe vers l'IP du VPS.

## 5. Vérification

- Depuis la maison, `https://paperless.<domaine>` affiche la page de connexion.
- Depuis le réseau mobile (Wi-Fi coupé), la même URL renvoie **403 Forbidden**.
