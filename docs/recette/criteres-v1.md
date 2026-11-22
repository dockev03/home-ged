# Recette V1 : critères d'acceptation (§7)

Fiche à dérouler **sur l'installation de production** (`https://ged.<domaine>`), avant de déclarer la V1 en service. Coche chaque ligne et note la date, l'appareil et toute anomalie.

Les critères de contrôle d'accès et de partage sont aussi **testés automatiquement** en CI contre un vrai Paperless (`tests/HomeGed.IntegrationTests/AcceptanceIntegrationTests.cs`). Cette fiche vérifie le comportement réel, de bout en bout, sur les appareils du foyer.

| Appareil | Modèle et version | Navigateur | Testé le | Par |
|---|---|---|---|---|
| iPhone | iOS … (17 ou plus) | Safari | | |
| Android | Android … | Chrome | | |
| PC | … | Chrome, Edge ou Firefox | | |

## Préparation

- [ ] Deux comptes Paperless ordinaires, A et B, membres du groupe `Foyer`, avec le droit *afficher* sur **Paramètres de l'interface** ([configuration-paperless.md](../deploiement/configuration-paperless.md) §2).
- [ ] Un document **privé** de A : retire le groupe `Foyer` de ses permissions dans Home-GED (fiche > Permissions). Note son titre : …
- [ ] Le compte de service des liens est configuré (`HOMEGED_SHARE_TOKEN`) : aucun avertissement « Share__ServiceToken non renseigné » dans `docker compose logs app`.

## 1. Installer, scanner, retrouver (iPhone **et** Android)

À faire une fois par téléphone, avec une **vraie facture de 2 pages**.

| # | Étape | Attendu | iPhone | Android |
|---|---|---|---|---|
| 1.1 | Ouvrir `https://ged.<domaine>`, puis installer : Safari > Partager > **Sur l'écran d'accueil** ; Chrome > menu > **Installer l'application** | Icône Home-GED sur l'écran d'accueil ; l'appli s'ouvre en plein écran, sans barre d'adresse | ☐ | ☐ |
| 1.2 | Se connecter avec le compte A (code 2FA si activé) | Liste des documents | ☐ | ☐ |
| 1.3 | **Scanner** : photographier la page 1, puis **Ajouter une page** et photographier la page 2 | Recadrage automatique proposé et ajustable ; 2 pages dans l'aperçu | ☐ | ☐ |
| 1.4 | Envoyer, et **noter l'heure** : … | Suivi du traitement par Paperless (OCR, classement) | ☐ | ☐ |
| 1.5 | Rechercher un mot **imprimé dans la facture** (pas dans son titre), par exemple le numéro de facture | Le document apparaît, **moins de 2 minutes** après l'heure notée. Délai mesuré : … | ☐ | ☐ |
| 1.6 | Ouvrir le document | Aperçu lisible des 2 pages, dans le bon sens | ☐ | ☐ |

## 2. Partager un document

Depuis un téléphone ou un PC, compte A. Ouvre le lien depuis une **fenêtre de navigation privée** (destinataire anonyme).

| # | Étape | Attendu | ☐ |
|---|---|---|---|
| 2.1 | Fiche document > **Envoyer** : essayer de créer le lien sans mot de passe | Création refusée : mot de passe obligatoire (8 caractères au moins) | ☐ |
| 2.2 | Créer le lien avec un mot de passe, 1 jour de validité | Lien `https://ged.<domaine>/s/…` copié | ☐ |
| 2.3 | Ouvrir le lien sans saisir de mot de passe | Aucun aperçu, mot de passe demandé | ☐ |
| 2.4 | Saisir 5 fois un **mauvais** mot de passe | « Lien indisponible ou mot de passe incorrect » à chaque fois | ☐ |
| 2.5 | Saisir le **bon** mot de passe | Refus : trop de tentatives, réessayer dans 15 minutes | ☐ |
| 2.6 | Après 15 minutes, saisir le bon mot de passe | Titre, aperçu et téléchargement disponibles | ☐ |
| 2.7 | Dans **Mes liens**, **révoquer** le lien, puis recharger la page du destinataire | « Lien indisponible » ; le téléchargement ne fonctionne plus | ☐ |
| 2.8 | **Mes liens** > **Journal** (avant la révocation, ou sur un autre lien) | Chaque tentative apparaît : date, IP, navigateur, succès ou échec | ☐ |
| 2.9 | Lien expiré : créer un lien d'1 jour et le rouvrir le lendemain | « Lien indisponible », même avec le bon mot de passe | ☐ |

## 3. Cloisonnement

Compte B, sur un autre appareil ou dans une autre session.

| # | Étape | Attendu | ☐ |
|---|---|---|---|
| 3.1 | Rechercher le titre du document privé de A | Aucun résultat | ☐ |
| 3.2 | Ouvrir `https://ged.<domaine>/documents/<id>` avec l'identifiant du document privé de A | Document introuvable | ☐ |
| 3.3 | A partage le document avec B (fiche > Permissions > B en lecture) ; B recharge | B le voit, mais ne peut pas le modifier | ☐ |

## 4. Opérations courantes sans l'interface Paperless

Compte B (membre du foyer, pas administrateur). **Ne jamais ouvrir `paperless.<domaine>` pendant ce test.** Une fois sur PC, une fois sur mobile.

| # | Opération | PC | Mobile |
|---|---|---|---|
| 4.1 | Rechercher (plein texte, filtres, vue sauvegardée) | ☐ | ☐ |
| 4.2 | Consulter (aperçu, contenu, métadonnées, notes, historique) | ☐ | ☐ |
| 4.3 | Classer (correspondant, type, étiquettes, champ `Personne`), y compris en créant une étiquette à la volée | ☐ | ☐ |
| 4.4 | Modifier (titre, date, champs personnalisés) et ajouter une note | ☐ | ☐ |
| 4.5 | Ajouter (envoi de fichier ; partage depuis une autre appli sur Android) | ☐ | ☐ |
| 4.6 | Supprimer, puis restaurer depuis la corbeille | ☐ | ☐ |
| 4.7 | Partager (lien protégé) | ☐ | ☐ |

## 5. Hors ligne (mobile)

| # | Étape | Attendu | iPhone | Android |
|---|---|---|---|---|
| 5.1 | Consulter un document, passer en mode avion, le rouvrir | Document consultable hors ligne | ☐ | ☐ |
| 5.2 | En mode avion, envoyer un fichier, puis réactiver le réseau | Envoi mis en attente, puis transmis automatiquement | ☐ | ☐ |

## 6. Restauration complète

| # | Étape | ☐ |
|---|---|---|
| 6.1 | Restauration depuis la sauvegarde restic sur une **machine vierge**, selon [restauration.md](../deploiement/restauration.md) | ☐ |
| 6.2 | Après restauration : connexion à Home-GED, les documents et les liens de partage actifs sont présents | ☐ |

## Anomalies relevées

| # | Critère | Appareil | Description | Suite donnée |
|---|---|---|---|---|
| | | | | |

**Décision** : ☐ V1 acceptée · ☐ À reprendre. Date : … Signature : …
