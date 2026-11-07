#!/usr/bin/env bash
set -euo pipefail

[ "$(id -u)" -eq 0 ] || { echo "Run as root: ssh azureuser@<ip> 'sudo DEPLOY_PUBLIC_KEY=\"...\" bash -s' < bootstrap-vm.sh" >&2; exit 1; }
: "${DEPLOY_PUBLIC_KEY:?Set DEPLOY_PUBLIC_KEY to the public key GitHub Actions uses (contents of apcs_deploy.pub)}"
DEV_DB_HOSTNAME="${DEV_DB_HOSTNAME:-$(hostname -f)}"

export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get upgrade -yq
apt-get install -yq ca-certificates curl gnupg jq htop fail2ban unattended-upgrades openssl rsync

timedatectl set-timezone Asia/Ho_Chi_Minh

cat > /etc/ssh/sshd_config.d/99-apcs.conf <<'SSHD'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
SSHD
systemctl reload ssh

if [ ! -f /swapfile ]; then
  fallocate -l 4G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile
  swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
echo 'vm.swappiness=10' > /etc/sysctl.d/99-apcs.conf
sysctl --system > /dev/null

if ! command -v docker > /dev/null; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get install -yq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
cat > /etc/docker/daemon.json <<'DOCKER'
{ "log-driver": "local", "log-opts": { "max-size": "20m", "max-file": "5" } }
DOCKER
systemctl restart docker

id deploy > /dev/null 2>&1 || useradd -m -s /bin/bash deploy
usermod -aG docker deploy
install -d -m 700 -o deploy -g deploy /home/deploy/.ssh
echo "$DEPLOY_PUBLIC_KEY" > /home/deploy/.ssh/authorized_keys
chown deploy:deploy /home/deploy/.ssh/authorized_keys
chmod 600 /home/deploy/.ssh/authorized_keys

install -d -m 755 -o deploy -g deploy /opt/apcs /opt/apcs/deploy /opt/apcs/releases
install -d -m 700 -o deploy -g deploy /opt/apcs/env

tls=/opt/apcs/env/tls
if [ ! -f "$tls/server.key" ]; then
  install -d -m 750 -o root -g 999 "$tls"
  openssl req -x509 -newkey rsa:2048 -nodes -days 825 -subj "/CN=$DEV_DB_HOSTNAME" \
    -keyout "$tls/server.key" -out "$tls/server.crt" 2> /dev/null
  chown root:999 "$tls/server.key" "$tls/server.crt"
  chmod 640 "$tls/server.key"
  chmod 644 "$tls/server.crt"
fi

cat > /etc/cron.d/apcs-restore-test <<'CRON'
0 3 * * 0 deploy cd /opt/apcs/deploy && set -a && . /opt/apcs/env/backup.env && set +a && ./scripts/restore-test.sh latest >> /var/log/apcs-restore-test.log 2>&1
CRON
touch /var/log/apcs-restore-test.log && chown deploy:deploy /var/log/apcs-restore-test.log

cat <<INFO

Bootstrap done.
Next (as deploy):
  1. docker login ghcr.io -u <github-user>   (classic PAT with read:packages)
  2. copy deploy/ to /opt/apcs/deploy and fill /opt/apcs/env/*.env from deploy/env/*.example
  3. cd /opt/apcs/deploy && cp .env.example .env && edit .env
INFO
