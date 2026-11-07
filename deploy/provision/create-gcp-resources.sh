#!/usr/bin/env bash
set -euo pipefail

: "${PROJECT_ID:?Set PROJECT_ID, e.g. apcs-prod-123456}"
: "${ADMIN_IP:?Set ADMIN_IP to your public IP, e.g. 203.0.113.7}"
: "${BUCKET:?Set BUCKET (globally unique), e.g. apcs-backups-x7k2}"

REGION="${REGION:-asia-southeast1}"
ZONE="${ZONE:-asia-southeast1-b}"
MACHINE_TYPE="${MACHINE_TYPE:-e2-standard-2}"
VM_NAME="${VM_NAME:-vm-apcs-prod}"
IP_NAME="${IP_NAME:-ip-apcs-prod}"
ADMIN_USER="${ADMIN_USER:-apcsadmin}"
SSH_PUBLIC_KEY="${SSH_PUBLIC_KEY:-$HOME/.ssh/apcs_gcp.pub}"
OPEN_DEV_DB="${OPEN_DEV_DB:-true}"
BACKUP_SA="apcs-backup"
BACKUP_SA_EMAIL="$BACKUP_SA@$PROJECT_ID.iam.gserviceaccount.com"

[ -f "$SSH_PUBLIC_KEY" ] || { echo "Missing $SSH_PUBLIC_KEY (ssh-keygen -t ed25519 -f ${SSH_PUBLIC_KEY%.pub})" >&2; exit 1; }

gcloud config set project "$PROJECT_ID" > /dev/null
gcloud services enable compute.googleapis.com storage.googleapis.com iam.googleapis.com

exists() { "$@" > /dev/null 2>&1; }

exists gcloud compute addresses describe "$IP_NAME" --region "$REGION" \
  || gcloud compute addresses create "$IP_NAME" --region "$REGION" --network-tier PREMIUM

rule() {
  local name="$1" port="$2" source="$3"
  if exists gcloud compute firewall-rules describe "$name"; then
    gcloud compute firewall-rules update "$name" --allow "tcp:$port" --source-ranges "$source" --quiet
  else
    gcloud compute firewall-rules create "$name" --network default --direction INGRESS \
      --allow "tcp:$port" --source-ranges "$source" --target-tags apcs --quiet
  fi
}

rule apcs-allow-ssh-admin 22 "$ADMIN_IP/32"
rule apcs-allow-http 80 0.0.0.0/0
rule apcs-allow-https 443 0.0.0.0/0
if [ "$OPEN_DEV_DB" = true ]; then
  rule apcs-allow-dev-postgres 5432 0.0.0.0/0
else
  exists gcloud compute firewall-rules describe apcs-allow-dev-postgres \
    && gcloud compute firewall-rules delete apcs-allow-dev-postgres --quiet
fi

exists gcloud iam service-accounts describe "$BACKUP_SA_EMAIL" \
  || gcloud iam service-accounts create "$BACKUP_SA" --display-name "APCS VM and backups"

exists gcloud storage buckets describe "gs://$BUCKET" \
  || gcloud storage buckets create "gs://$BUCKET" --location "$REGION" --uniform-bucket-level-access --public-access-prevention
lifecycle="$(mktemp)"
echo '{"rule":[{"action":{"type":"Delete"},"condition":{"age":35}}]}' > "$lifecycle"
gcloud storage buckets update "gs://$BUCKET" --lifecycle-file "$lifecycle" > /dev/null
rm -f "$lifecycle"
gcloud storage buckets add-iam-policy-binding "gs://$BUCKET" \
  --member "serviceAccount:$BACKUP_SA_EMAIL" --role roles/storage.objectAdmin > /dev/null

if ! exists gcloud compute instances describe "$VM_NAME" --zone "$ZONE"; then
  gcloud compute instances create "$VM_NAME" --zone "$ZONE" --machine-type "$MACHINE_TYPE" \
    --image-family ubuntu-2404-lts-amd64 --image-project ubuntu-os-cloud \
    --boot-disk-size 64GB --boot-disk-type pd-balanced \
    --address "$IP_NAME" --tags apcs \
    --service-account "$BACKUP_SA_EMAIL" --scopes cloud-platform \
    --metadata "ssh-keys=$ADMIN_USER:$(cat "$SSH_PUBLIC_KEY")"
fi

ip="$(gcloud compute addresses describe "$IP_NAME" --region "$REGION" --format 'value(address)')"
cat <<INFO

Done.
  VM public IP : $ip
  Temporary domain (until a real one): $ip.sslip.io  -> APP_DOMAIN=$ip.sslip.io
  SSH          : ssh -i ${SSH_PUBLIC_KEY%.pub} $ADMIN_USER@$ip
  Backups      : the VM's service account writes to gs://$BUCKET; in backup.env set
                 RCLONE_CONFIG_GCS_TYPE=google cloud storage
                 RCLONE_CONFIG_GCS_ENV_AUTH=true
                 RCLONE_CONFIG_GCS_BUCKET_POLICY_ONLY=true
                 BACKUP_REMOTES=gcs:$BUCKET gdrive:apcs-backups
INFO
