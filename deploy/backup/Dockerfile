# Conteneur de sauvegarde Home-GED : pg_dump + restic (SFTP) planifiés par cron.
# Basé sur l'image PostgreSQL pour que pg_dump soit de la même version majeure
# que le serveur.
FROM docker.io/library/postgres:18-alpine

RUN apk add --no-cache restic openssh-client tzdata

COPY entrypoint.sh backup.sh /usr/local/bin/
RUN chmod 0755 /usr/local/bin/entrypoint.sh /usr/local/bin/backup.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["cron"]
