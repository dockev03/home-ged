# Test : connexion à l'API Paperless avec le 2FA (étape 1.1)

> Paperless-ngx **3.5.1**, conteneur jetable, le 21/11/2026.
> Version d'API annoncée : en-têtes `X-Api-Version: 10` et `X-Version: 3.5.1`.

## Résultats

Compte administrateur avec le TOTP activé, appels à `POST /api/token/` :

| # | Requête | Réponse |
|---|---|---|
| 1 | identifiant + mot de passe, sans `code` | `400` `{"non_field_errors":["MFA code is required"]}` |
| 2 | `code` faux | `400` `{"non_field_errors":["Invalid MFA code"]}` |
| 3 | `code` TOTP valide | `200` `{"token":"…"}` |
| 4 | mauvais mot de passe + code valide | `400` `Unable to log in with provided credentials.` |
| 5 | **code de secours** | `400` `Invalid MFA code` : **non accepté par l'API** |
| 6 | Basic Auth sur `/api/documents/` | `401` `{"detail":"MFA required"}` |
| 7 | 6 échecs depuis la même IP | 5 × `400`, puis `429` (`PAPERLESS_TOKEN_THROTTLE_RATE`, 5/min) |
| 8 | même test avec un autre `X-Forwarded-For` | `400` : le compteur est **par valeur de `X-Forwarded-For`** |

Champ du corps (schéma OpenAPI `PaperlessAuthTokenRequest`) : `username`, `password`, `code` (facultatif, « MFA Code »).

## Constats

1. **La connexion fonctionne avec le 2FA** : il suffit d'envoyer le champ `code`. Un compte sans 2FA ignore ce champ.
2. **Le token est statique** (`rest_framework.authtoken`) : un seul token par utilisateur, renvoyé à l'identique à chaque connexion, sans expiration. C'est aussi celui de l'interface Paperless (« Mon profil > Token API »).
3. **Les codes de secours ne passent pas par l'API** : seul le code TOTP est accepté.
4. **Limite de tentatives** : Paperless identifie le client par `X-Forwarded-For` (`NUM_PROXIES` non défini) ou, à défaut, par l'IP source.

## Conséquences pour Home-GED

- **Écran de connexion** :
  - identifiant et mot de passe ; un champ « Code de vérification » apparaît si Paperless répond `MFA code is required` ;
  - Home-GED renvoie alors les trois valeurs ensemble : il ne garde aucun état intermédiaire et ne mémorise pas le mot de passe ;
  - message clair si le code est faux.
- **Codes de secours** : non pris en charge dans Home-GED. En cas de perte du téléphone, on se connecte à l'interface Paperless (qui les accepte) ou on demande à l'administrateur de désactiver le TOTP.
- **Token** :
  - il ne vit que côté serveur et est chiffré au repos (Data Protection) dans la base `homeged` ;
  - la déconnexion de Home-GED supprime la session, **pas** le token Paperless (sinon les autres usages du compte seraient cassés) ;
  - la révocation complète passe par la régénération du token dans Paperless.
- **Limite de tentatives** :
  - le BFF transmet l'IP du client (fournie par NPM) dans `X-Forwarded-For` en **remplaçant** la valeur reçue, pour que la limite de Paperless s'applique par appareil et non à tout le foyer ;
  - Home-GED ajoute sa propre limite, avant d'appeler Paperless ;
  - un `429` de Paperless est remonté en « Trop de tentatives, réessayez dans une minute ».
- **Version d'API** : elle est figée à `Accept: application/json; version=10`. Le BFF journalise un avertissement si `X-Version` change.

## Reproduire

```bash
docker run -d --name pl -p 8000:8000 -e PAPERLESS_BIND_ADDR=0.0.0.0 \
  -e PAPERLESS_REDIS=redis://<valkey>:6379 -e PAPERLESS_SECRET_KEY=poc \
  -e PAPERLESS_ADMIN_USER=admin -e PAPERLESS_ADMIN_PASSWORD=adminpass \
  -e PAPERLESS_URL=http://localhost:8000 ghcr.io/paperless-ngx/paperless-ngx:3.5.1
```

Ensuite, activer le TOTP (`GET` puis `POST /api/profile/totp/` avec `secret` et `code`) et rejouer les appels du tableau.
Pièges rencontrés : sans `PAPERLESS_BIND_ADDR=0.0.0.0`, le serveur ne démarre pas sur un hôte sans IPv6 ; l'API doit être appelée avec l'hôte de `PAPERLESS_URL` (`localhost`, pas `127.0.0.1`), sinon `DisallowedHost`.
