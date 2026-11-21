# Configuration initiale de Paperless

À faire une fois, dans l'interface `https://paperless.<domaine>`. Les libellés peuvent légèrement varier selon la version de Paperless.

## 1. Compte administrateur

```bash
docker compose exec webserver python3 manage.py createsuperuser
```

Puis, dans l'interface : **Mon profil** > **Authentification à deux facteurs** > activer le TOTP et ranger les codes de secours.

## 2. Utilisateurs et groupe « Foyer » (G1, G2, G4)

1. **Paramètres** > **Utilisateurs et groupes** > créer le groupe `Foyer`, avec les droits *afficher / ajouter / modifier* sur Documents, Tags, Correspondants, Types de document, Champs personnalisés, Vues sauvegardées et Notes. Pas de suppression ni d'administration.
2. Créer un compte par personne (non superutilisateur), membre de `Foyer`. Chacun active ensuite son 2FA.

Les documents ont un **propriétaire** (celui qui l'importe). Pour qu'un document soit visible par tout le foyer, on lui donne des permissions pour le groupe `Foyer` : c'est automatique avec le workflow du §5.

## 3. Classement (B3, B4, B5, B11)

- **Types de document** : Facture, Fiche de paie, Contrat, Avis d'imposition, Ordonnance, Relevé bancaire, Attestation, Garantie…
- **Correspondants** : EDF, CAF, employeur, banque, mutuelle…
- **Champs personnalisés** :
  - `Personne` (liste de sélection) : une valeur par membre du foyer, plus `Foyer` ;
  - `Montant` (monétaire) ;
  - `Échéance` (date), qui sert aux rappels du §6.

Laisser l'algorithme de correspondance sur **Auto** : Paperless apprend de tes classements (B8). Il suffit de corriger les premiers documents à la main.

## 4. Import par e-mail (A4)

**Mail** > **Comptes mail** : ajouter la boîte dédiée (serveur IMAP, port 993, SSL).

**Règles mail** > nouvelle règle :
- Compte : la boîte dédiée, dossier `INBOX` ;
- Action : traiter les **pièces jointes uniquement** ;
- Après traitement : **déplacer** vers un dossier `Importé` (ou marquer comme lu).

## 5. Partage automatique avec le foyer

**Workflows** > nouveau workflow « Partage foyer » :
- Déclencheur : **Document ajouté** ;
- Action **Affectation** > permissions : ajouter le groupe `Foyer` en *afficher* et *modifier*.

Pour un document privé, retire ensuite le groupe dans ses permissions.

## 6. Rappels d'échéance (F1, F2)

Nécessite les variables `SMTP_*` (voir `.env.example`).

**Workflows** > nouveau workflow « Rappel échéance » :
- Déclencheur : **Planifié**, basé sur le champ personnalisé `Échéance`, décalage de **−30 jours** ;
- Action **E-mail** : destinataire toi (et/ou le foyer), sujet `Échéance : {doc_title}`.

## 7. Tableau de bord (B9, B10, F3)

Créer des **vues sauvegardées** et les afficher sur le tableau de bord et dans la barre latérale : « Échéances à venir », « Ajoutés récemment », « Impôts 2026 »…
