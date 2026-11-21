#!/bin/sh
# Point d'entrée du conteneur de sauvegarde.
#   cron        (défaut) planifie backup.sh selon BACKUP_CRON et reste au premier plan
#   backup      lance une sauvegarde immédiatement
#   <autre>     exécute la commande telle quelle (ex. : restic snapshots)
set -eu

# Configuration SSH pour le dépôt SFTP : clé et known_hosts montés en lecture
# seule dans /ssh, copiés avec les bons droits (ssh refuse une clé trop ouverte).
if [ -n "${SFTP_HOST:-}" ]; then
    mkdir -p /root/.ssh
    chmod 700 /root/.ssh
    for f in id_ed25519 known_hosts; do
        if [ ! -f "/ssh/$f" ]; then
            echo "ERREUR : /ssh/$f introuvable (voir docs/deploiement/sauvegardes.md)" >&2
            exit 1
        fi
    done
    install -m 600 /ssh/id_ed25519 /root/.ssh/id_ed25519
    install -m 644 /ssh/known_hosts /root/.ssh/known_hosts
    cat > /root/.ssh/config <<EOF
Host nas
    HostName ${SFTP_HOST}
    Port ${SFTP_PORT:-22}
    User ${SFTP_USER:-backup-ged}
    IdentityFile /root/.ssh/id_ed25519
    IdentitiesOnly yes
    StrictHostKeyChecking yes
    ServerAliveInterval 60
    ServerAliveCountMax 240
EOF
    chmod 600 /root/.ssh/config
fi

case "${1:-cron}" in
    cron)
        # crond ne transmet pas l'environnement : on le fige dans un fichier
        # lisible par root uniquement, rechargé par backup.sh.
        export -p > /etc/backup.env
        chmod 600 /etc/backup.env
        echo "${BACKUP_CRON:-30 2 * * *} /usr/local/bin/backup.sh > /proc/1/fd/1 2>&1" > /etc/crontabs/root
        echo "Sauvegarde planifiée : ${BACKUP_CRON:-30 2 * * *} (${TZ:-UTC})"
        exec crond -f -l 8
        ;;
    backup)
        exec /usr/local/bin/backup.sh
        ;;
    *)
        exec "$@"
        ;;
esac
