#!/bin/sh
# Sauvegarde Home-GED :
#   1. pg_dump de chaque base listée dans PG_DATABASES
#   2. restic backup des dumps et des volumes Paperless (data, media)
#   3. rétention (forget --prune) puis vérification légère du dépôt
set -eu

[ -f /etc/backup.env ] && . /etc/backup.env

DUMP_DIR=/tmp/dumps

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }

ping_healthcheck() {
    # Optionnel : URL "push" (Uptime Kuma, healthchecks.io…) appelée uniquement
    # en cas de succès ; l'outil de supervision alerte si le ping n'arrive pas.
    [ -n "${HEALTHCHECK_URL:-}" ] || return 0
    wget -q -O /dev/null -T 10 "${HEALTHCHECK_URL}" || log "Avertissement : ping healthcheck impossible"
}

on_exit() {
    rc=$?
    rm -rf "$DUMP_DIR"
    [ "$rc" -eq 0 ] || log "ÉCHEC de la sauvegarde (code $rc)"
}

# Verrou : une seule sauvegarde à la fois (libéré automatiquement à la sortie).
exec 9> /tmp/backup.lock
if ! flock -n 9; then
    log "Une sauvegarde est déjà en cours, abandon."
    exit 0
fi
trap on_exit EXIT

log "Début de la sauvegarde"

# Dépôt restic : création au premier passage.
if ! restic cat config > /dev/null 2>&1; then
    log "Dépôt restic absent : initialisation"
    restic init
fi

mkdir -p "$DUMP_DIR"
for db in ${PG_DATABASES:-paperless}; do
    log "Dump PostgreSQL : $db"
    pg_dump --format=custom --no-owner --file="$DUMP_DIR/$db.dump" "$db"
done

log "Envoi vers le dépôt restic"
restic backup \
    --tag nightly \
    --exclude /source/data/log \
    "$DUMP_DIR" /source/data /source/media

log "Rétention : ${KEEP_DAILY:-7} j / ${KEEP_WEEKLY:-4} sem. / ${KEEP_MONTHLY:-12} mois"
restic forget --prune \
    --keep-daily "${KEEP_DAILY:-7}" \
    --keep-weekly "${KEEP_WEEKLY:-4}" \
    --keep-monthly "${KEEP_MONTHLY:-12}"

# Vérification : structure chaque jour, 5 % des données relues le dimanche.
if [ "$(date +%u)" = "7" ]; then
    restic check --read-data-subset=5%
else
    restic check
fi

log "Sauvegarde terminée"
ping_healthcheck
