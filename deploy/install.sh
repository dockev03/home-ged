#!/usr/bin/env bash
# Home-GED — installation et mise à jour de la stack sur un hôte Docker.
#
#   ./install.sh                 prépare .env, télécharge les images et démarre la stack
#   ./install.sh --env-seulement prépare seulement .env (à charger ensuite dans Portainer)
#
# Rejouable à volonté : un secret déjà renseigné n'est jamais remplacé.
# Documentation : docs/deploiement/README.md
set -euo pipefail

cd "$(dirname "$0")"
ENV_FILE=.env
ENV_ONLY=false
case "${1:-}" in
  "") ;;
  --env-seulement) ENV_ONLY=true ;;
  -h | --help) sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
  *) echo "Option inconnue : $1 (voir --help)" >&2; exit 2 ;;
esac

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m/!\\\033[0m %s\n' "$*" >&2; }
fail() { printf '\033[1;31mErreur :\033[0m %s\n' "$*" >&2; exit 1; }

# Valeur d'une variable du .env (dernière occurrence, sans guillemets).
get_var() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -n1 | sed -e 's/^"\(.*\)"$/\1/' -e "s/^'\(.*\)'$/\1/"; }

# Remplace (ou ajoute) une variable. Les valeurs écrites ici n'ont ni « | » ni « & » ni « \ ».
set_var() {
  if grep -q "^$1=" "$ENV_FILE"; then
    sed -i "s|^$1=.*|$1=$2|" "$ENV_FILE"
  else
    printf '%s=%s\n' "$1" "$2" >> "$ENV_FILE"
  fi
}

# Secret aléatoire : lettres et chiffres uniquement (sûr dans une chaîne de connexion).
secret() { LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c "${1:-48}" || true; }

# --- 1. Prérequis -----------------------------------------------------------
command -v docker > /dev/null || fail "Docker n'est pas installé (https://docs.docker.com/engine/install/)."
docker compose version > /dev/null 2>&1 || fail "Le plugin « docker compose » (v2) est requis."
docker info > /dev/null 2>&1 || fail "Docker ne répond pas : lance ce script avec sudo, ou ajoute ton utilisateur au groupe docker."

# --- 2. Fichier .env ----------------------------------------------------------
if [[ ! -f $ENV_FILE ]]; then
  info "Création de $ENV_FILE à partir de .env.example"
  (umask 077 && cp .env.example "$ENV_FILE")
fi
chmod 600 "$ENV_FILE"

generated=()
for name in PAPERLESS_SECRET_KEY:64 POSTGRES_PASSWORD:40 HOMEGED_DB_PASSWORD:40 RESTIC_PASSWORD:48; do
  var=${name%%:*}
  value=$(get_var "$var")
  if [[ -z $value || $value == a-generer ]]; then
    set_var "$var" "$(secret "${name##*:}")"
    generated+=("$var")
  fi
done
if ((${#generated[@]})); then
  info "Secrets générés : ${generated[*]}"
  if [[ " ${generated[*]} " == *" RESTIC_PASSWORD "* ]]; then
    warn "Copie RESTIC_PASSWORD dans ton gestionnaire de mots de passe, HORS du VPS : sans lui, les sauvegardes sont illisibles."
  fi
fi

# --- 3. Réseau de Nginx Proxy Manager --------------------------------------------
network=$(get_var NPM_NETWORK)
if ! docker network inspect "$network" > /dev/null 2>&1; then
  # Réseaux des conteneurs NPM en cours d'exécution.
  # shellcheck disable=SC2016 # gabarit Go, pas une expansion shell
  mapfile -t candidates < <(
    docker ps --format '{{.ID}} {{.Image}}' | awk '$2 ~ /nginx-proxy-manager/ {print $1}' |
      xargs -r docker inspect --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{"\n"}}{{end}}' |
      sed '/^$/d' | sort -u)
  if ((${#candidates[@]} == 1)); then
    network=${candidates[0]}
    info "Réseau de Nginx Proxy Manager détecté : $network"
    set_var NPM_NETWORK "$network"
  else
    fail "Réseau « $network » introuvable. Renseigne NPM_NETWORK dans $ENV_FILE (réseaux disponibles : docker network ls)."
  fi
fi

subnet=$(docker network inspect "$network" --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' | tr ' ' '\n' | grep -m1 '\.' || true)
[[ -n $subnet ]] || fail "Sous-réseau IPv4 du réseau « $network » introuvable : renseigne NPM_SUBNET à la main."
if [[ $(get_var NPM_SUBNET) != "$subnet" ]]; then
  info "Sous-réseau de « $network » : $subnet"
  set_var NPM_SUBNET "$subnet"
fi

# --- 4. Valeurs à saisir soi-même -------------------------------------------------
todo=()
[[ $(get_var PAPERLESS_URL) == *example.fr* ]] && todo+=("PAPERLESS_URL (adresse publique de Paperless)")
[[ $(get_var SFTP_HOST) == 203.0.113.* ]] && todo+=("SFTP_HOST et SFTP_PORT (NAS des sauvegardes)")
ssh_dir=$(get_var BACKUP_SSH_DIR)
[[ -f ${ssh_dir:-/nonexistent}/id_ed25519 ]] || todo+=("clé SSH de sauvegarde absente de ${ssh_dir:-BACKUP_SSH_DIR} (docs/deploiement/sauvegardes.md)")
if ((${#todo[@]})); then
  warn "À compléter dans $(pwd)/$ENV_FILE :"
  printf '      - %s\n' "${todo[@]}" >&2
  [[ $ENV_ONLY == true ]] || fail "Complète ces valeurs, puis relance ./install.sh."
fi

if [[ $ENV_ONLY == true ]]; then
  info "Fichier prêt : $(pwd)/$ENV_FILE — à charger dans Portainer (« Load variables from .env file »)."
  exit 0
fi

# --- 5. Images et démarrage ----------------------------------------------------
info "Téléchargement des images"
if ! docker compose pull --quiet; then
  fail "Téléchargement impossible. Les images Home-GED sont privées : « docker login ghcr.io » avec un token GitHub classic de portée read:packages (docs/deploiement/README.md)."
fi

info "Démarrage de la stack"
docker compose up -d --remove-orphans

info "Attente de Home-GED (jusqu'à 5 minutes au premier démarrage, le temps que Paperless s'initialise)"
for _ in $(seq 60); do
  status=$(docker inspect --format '{{.State.Health.Status}}' "$(docker compose ps -q app)" 2> /dev/null || echo absent)
  [[ $status == healthy ]] && break
  sleep 5
done

docker compose ps --format 'table {{.Service}}\t{{.Status}}'
if [[ ${status:-} != healthy ]]; then
  fail "Home-GED n'est pas prêt. Consulte « docker compose logs app webserver init-db »."
fi

info "Home-GED est démarré. Étapes suivantes : publier ged.<domaine> et paperless.<domaine> dans NPM (docs/deploiement/npm.md)."
