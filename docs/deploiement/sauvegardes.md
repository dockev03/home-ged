# Sauvegardes : VPS → ReadyNAS 102 en SFTP

Chaque nuit, le conteneur `backup` :
1. fait un `pg_dump` de la base Paperless ;
2. envoie le dump et les volumes `data` et `media` dans un dépôt **restic chiffré** sur le NAS, en SFTP ;
3. applique la rétention (7 jours, 4 semaines, 12 mois) et vérifie le dépôt (5 % des données relues le dimanche).

Le NAS ne voit que des blocs chiffrés : sans `RESTIC_PASSWORD`, il est impossible d'y lire tes documents.

> ⚠️ **Exposition réseau** : ce guide ouvre un port SSH de ta box vers le NAS. Les protections ci-dessous (clé uniquement, utilisateur enfermé sans shell, connexion limitée à l'IP du VPS) sont **toutes** nécessaires.

> ⚠️ **ReadyNAS 102** : ce modèle ne reçoit plus de mises à jour de Netgear, donc son serveur SSH n'est plus corrigé. C'est une raison de plus pour n'autoriser que l'IP du VPS. Pense à le remplacer à moyen terme.

## 1. Sur le VPS : générer la clé de sauvegarde

```bash
sudo mkdir -p /opt/home-ged/backup-ssh
```

```bash
sudo ssh-keygen -t ed25519 -N '' -C home-ged-backup -f /opt/home-ged/backup-ssh/id_ed25519
```

```bash
sudo cat /opt/home-ged/backup-ssh/id_ed25519.pub
```

Garde la clé publique affichée pour l'étape 3. La clé privée ne quitte jamais le VPS.

## 2. Sur le NAS : activer SSH et créer l'utilisateur

1. Interface d'administration du ReadyNAS > **Système** > **Paramètres** > **Services** > activer **SSH**. Le compte `root` utilise le mot de passe de l'admin.
2. **Comptes** > **Utilisateurs** > créer l'utilisateur `backup-ged`, avec un mot de passe long et aléatoire (il ne servira pas, la connexion se fait par clé).
3. Depuis un PC du réseau local, connecte-toi au NAS :

```bash
ssh root@<ip-locale-du-nas>
```

**Garde cette session ouverte jusqu'à la fin de l'étape 4** : elle te permet de corriger une erreur de configuration sans te retrouver bloqué dehors.

## 3. Sur le NAS : dossier enfermé (chroot) et clé autorisée

Le dossier de chroot doit appartenir à `root` et ne pas être modifiable par d'autres. Seul le sous-dossier `restic` est accessible en écriture à `backup-ged`.

```bash
mkdir -p /data/sftp-chroot/backup-ged/restic
```

```bash
chown root:root /data/sftp-chroot /data/sftp-chroot/backup-ged && chmod 755 /data/sftp-chroot /data/sftp-chroot/backup-ged
```

```bash
chown backup-ged /data/sftp-chroot/backup-ged/restic && chmod 700 /data/sftp-chroot/backup-ged/restic
```

```bash
mkdir -p /etc/ssh/authorized_keys && chmod 755 /etc/ssh/authorized_keys
```

Crée le fichier `/etc/ssh/authorized_keys/backup-ged` avec **une seule ligne**, en remplaçant `<IP_VPS>` et la clé publique de l'étape 1 :

```text
from="<IP_VPS>",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty ssh-ed25519 AAAA... home-ged-backup
```

```bash
chmod 644 /etc/ssh/authorized_keys/backup-ged
```

## 4. Sur le NAS : configurer le serveur SSH

Sauvegarde la configuration actuelle :

```bash
cp /etc/ssh/sshd_config /etc/ssh/sshd_config.avant-home-ged
```

Ajoute à la **fin** de `/etc/ssh/sshd_config` (adapte `<IP_VPS>` et le réseau local `192.168.1.*`) :

```text
# --- Home-GED : sauvegardes restic ---
# Seuls backup-ged (depuis le VPS) et root (depuis le réseau local) peuvent se connecter.
AllowUsers backup-ged@<IP_VPS> root@192.168.1.*

Match User backup-ged
    ChrootDirectory /data/sftp-chroot/backup-ged
    ForceCommand internal-sftp
    AuthorizedKeysFile /etc/ssh/authorized_keys/%u
    PasswordAuthentication no
    AllowTcpForwarding no
    AllowAgentForwarding no
    X11Forwarding no
    PermitTTY no
```

Vérifie la syntaxe **avant** de redémarrer. La commande ne doit rien afficher :

```bash
/usr/sbin/sshd -t
```

```bash
systemctl restart ssh
```

Ouvre une **deuxième** session root depuis le réseau local pour vérifier que tu peux toujours te connecter. Ce n'est qu'ensuite que tu fermes la première.

En cas de problème, restaure la configuration d'origine :

```bash
cp /etc/ssh/sshd_config.avant-home-ged /etc/ssh/sshd_config && systemctl restart ssh
```

> ℹ️ Le ReadyNAS peut réécrire `sshd_config` quand on désactive puis réactive SSH dans l'interface, ou lors d'une mise à jour. Vérifie que le bloc est toujours présent après ce genre d'opération (la sauvegarde nocturne échouera s'il a disparu).

## 5. Sur la box : redirection de port

Crée une redirection **TCP** d'un port externe non standard (par exemple `22022`) vers le port `22` du NAS. Si ta box permet de restreindre l'IP source, limite-la à l'IP du VPS.

## 6. Sur le VPS : empreinte du NAS (known_hosts)

```bash
ssh-keyscan -p 22022 -t ed25519 <IP_BOX> | sudo tee /opt/home-ged/backup-ssh/known_hosts
```

Vérifie que l'empreinte correspond à celle du NAS, en comparant le résultat de ces deux commandes.

Sur le VPS :

```bash
ssh-keygen -lf /opt/home-ged/backup-ssh/known_hosts
```

Sur le NAS :

```bash
ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
```

## 7. Configurer et tester

Dans `.env` (ou les variables de la stack Portainer) :

```text
SFTP_HOST=<IP_BOX>
SFTP_PORT=22022
SFTP_USER=backup-ged
RESTIC_REPOSITORY=sftp:nas:/restic
RESTIC_PASSWORD=<64 caractères aléatoires>
BACKUP_SSH_DIR=/opt/home-ged/backup-ssh
```

> 🔑 Range `RESTIC_PASSWORD` dans ton gestionnaire de mots de passe **hors du VPS**. Si tu perds le VPS et ce mot de passe, tu perds les sauvegardes.

Lancer une première sauvegarde manuelle (elle initialise le dépôt) :

```bash
docker compose exec backup /usr/local/bin/entrypoint.sh backup
```

Lister les sauvegardes :

```bash
docker compose exec backup restic snapshots
```

Vérifier que le compte ne donne pas de shell. Le message attendu est « This service allows sftp connections only. » :

```bash
docker compose exec backup ssh nas id
```

Ensuite, fais **une fois** le test de [restauration](restauration.md).

## Supervision (optionnel)

Si tu utilises Uptime Kuma, crée un moniteur de type **Push** avec un intervalle de 25 h et mets son URL dans `BACKUP_HEALTHCHECK_URL`. Le conteneur l'appelle après chaque sauvegarde réussie, et Uptime Kuma t'alerte si le ping n'arrive pas.
