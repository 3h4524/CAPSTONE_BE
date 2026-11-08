#!/usr/bin/env bash
set -euo pipefail

[ "$(id -u)" -eq 0 ] || { echo "Run as root: gcloud compute ssh <vm> --tunnel-through-iap --command 'sudo bash -s' < bootstrap-vm.sh" >&2; exit 1; }
REGISTRY_HOST="${REGISTRY_HOST:-asia-southeast1-docker.pkg.dev}"
DEV_DB_HOSTNAME="${DEV_DB_HOSTNAME:-$(curl -fsS -H 'Metadata-Flavor: Google' http://metadata.google.internal/computeMetadata/v1/instance/network-interfaces/0/access-configs/0/external-ip 2> /dev/null || hostname -f)}"

export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get upgrade -yq
apt-get install -yq ca-certificates curl gnupg jq htop fail2ban unattended-upgrades openssl rsync python3

timedatectl set-timezone Asia/Ho_Chi_Minh

if [ ! -f /swapfile ]; then
  fallocate -l 4G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile
  swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
echo 'vm.swappiness=10' > /etc/sysctl.d/99-apcs.conf
sysctl --system > /dev/null

install -m 0755 -d /etc/apt/keyrings
if [ ! -f /etc/apt/sources.list.d/google-cloud-sdk.list ]; then
  curl -fsSL https://packages.cloud.google.com/apt/doc/apt-key.gpg | gpg --dearmor -o /etc/apt/keyrings/cloud.google.gpg
  echo "deb [signed-by=/etc/apt/keyrings/cloud.google.gpg] https://packages.cloud.google.com/apt cloud-sdk main" \
    > /etc/apt/sources.list.d/google-cloud-sdk.list
fi
if ! command -v docker > /dev/null; then
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    > /etc/apt/sources.list.d/docker.list
fi
apt-get update -q
apt-get install -yq google-cloud-cli docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
cat > /etc/docker/daemon.json <<'DOCKER'
{ "log-driver": "local", "log-opts": { "max-size": "20m", "max-file": "5" } }
DOCKER
systemctl restart docker

id deploy > /dev/null 2>&1 || useradd -m -s /bin/bash deploy
usermod -aG docker deploy
install -d -m 755 -o deploy -g deploy /opt/apcs /opt/apcs/deploy /opt/apcs/migrations /opt/apcs/releases /opt/apcs/incoming
install -d -m 700 -o deploy -g deploy /opt/apcs/env
sudo -iu deploy /usr/bin/gcloud auth configure-docker "$REGISTRY_HOST" --quiet > /dev/null

tls=/opt/apcs/env/tls
if [ ! -f "$tls/server.key" ]; then
  install -d -m 750 -o root -g deploy "$tls"
  openssl req -x509 -newkey rsa:2048 -nodes -days 825 -subj "/CN=$DEV_DB_HOSTNAME" \
    -keyout "$tls/server.key" -out "$tls/server.crt" 2> /dev/null
  chown root:deploy "$tls/server.key" "$tls/server.crt"
  chmod 640 "$tls/server.key"
  chmod 644 "$tls/server.crt"
fi

cat > /etc/cron.d/apcs-restore-test <<'CRON'
0 3 * * 0 deploy cd /opt/apcs/deploy && set -a && . /opt/apcs/env/backup.env && set +a && ./scripts/restore-test.sh latest >> /var/log/apcs-restore-test.log 2>&1
CRON
touch /var/log/apcs-restore-test.log && chown deploy:deploy /var/log/apcs-restore-test.log

echo "Bootstrap done on $(hostname) ($DEV_DB_HOSTNAME)"
