#!/usr/bin/env bash
set -euo pipefail

: "${LOCATION:?Set LOCATION, e.g. southeastasia}"
: "${VM_SIZE:?Set VM_SIZE, e.g. Standard_B2as_v2}"
: "${ADMIN_IP:?Set ADMIN_IP to your public IP, e.g. 203.0.113.7}"
: "${STORAGE_ACCOUNT:?Set STORAGE_ACCOUNT (3-24 lowercase letters/digits, globally unique)}"
: "${DNS_LABEL:?Set DNS_LABEL (unique within the region), e.g. apcs-prod-x7k2}"

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-apcs-prod}"
VM_NAME="${VM_NAME:-vm-apcs-prod}"
NSG_NAME="${NSG_NAME:-nsg-apcs-prod}"
IP_NAME="${IP_NAME:-ip-apcs-prod}"
SSH_PUBLIC_KEY="${SSH_PUBLIC_KEY:-$HOME/.ssh/apcs_azure.pub}"
OPEN_DEV_DB="${OPEN_DEV_DB:-true}"

[ -f "$SSH_PUBLIC_KEY" ] || { echo "Missing $SSH_PUBLIC_KEY (ssh-keygen -t ed25519 -f ${SSH_PUBLIC_KEY%.pub})" >&2; exit 1; }

echo "Subscription: $(az account show --query name -o tsv)"
az group create -n "$RESOURCE_GROUP" -l "$LOCATION" -o none

az network nsg show -g "$RESOURCE_GROUP" -n "$NSG_NAME" -o none 2> /dev/null \
  || az network nsg create -g "$RESOURCE_GROUP" -n "$NSG_NAME" -l "$LOCATION" -o none

rule() {
  local name="$1" priority="$2" port="$3" source="$4"
  if az network nsg rule show -g "$RESOURCE_GROUP" --nsg-name "$NSG_NAME" -n "$name" -o none 2> /dev/null; then
    az network nsg rule update -g "$RESOURCE_GROUP" --nsg-name "$NSG_NAME" -n "$name" \
      --source-address-prefixes "$source" --destination-port-ranges "$port" -o none
  else
    az network nsg rule create -g "$RESOURCE_GROUP" --nsg-name "$NSG_NAME" -n "$name" --priority "$priority" \
      --direction Inbound --access Allow --protocol Tcp \
      --source-address-prefixes "$source" --destination-port-ranges "$port" -o none
  fi
}

rule allow-ssh-admin 100 22 "$ADMIN_IP/32"
rule allow-http 110 80 Internet
rule allow-https 120 443 Internet
if [ "$OPEN_DEV_DB" = true ]; then
  rule allow-dev-postgres 130 5432 Internet
else
  az network nsg rule delete -g "$RESOURCE_GROUP" --nsg-name "$NSG_NAME" -n allow-dev-postgres -o none 2> /dev/null || true
fi

az network public-ip show -g "$RESOURCE_GROUP" -n "$IP_NAME" -o none 2> /dev/null \
  || az network public-ip create -g "$RESOURCE_GROUP" -n "$IP_NAME" -l "$LOCATION" \
       --sku Standard --allocation-method Static --dns-name "$DNS_LABEL" -o none

if ! az vm show -g "$RESOURCE_GROUP" -n "$VM_NAME" -o none 2> /dev/null; then
  az vm create -g "$RESOURCE_GROUP" -n "$VM_NAME" -l "$LOCATION" \
    --image Ubuntu2404 --size "$VM_SIZE" \
    --admin-username azureuser --ssh-key-values "$SSH_PUBLIC_KEY" --authentication-type ssh \
    --os-disk-size-gb 64 --storage-sku Premium_LRS \
    --public-ip-address "$IP_NAME" --nsg "$NSG_NAME" -o none
fi

if ! az storage account show -g "$RESOURCE_GROUP" -n "$STORAGE_ACCOUNT" -o none 2> /dev/null; then
  az storage account create -g "$RESOURCE_GROUP" -n "$STORAGE_ACCOUNT" -l "$LOCATION" \
    --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --allow-blob-public-access false -o none
fi
key="$(az storage account keys list -g "$RESOURCE_GROUP" -n "$STORAGE_ACCOUNT" --query '[0].value' -o tsv)"
az storage container create --account-name "$STORAGE_ACCOUNT" --account-key "$key" -n apcs-backups -o none

policy="$(mktemp)"
cat > "$policy" <<'JSON'
{"rules":[{"enabled":true,"name":"expire-backups","type":"Lifecycle","definition":{
  "filters":{"blobTypes":["blockBlob"],"prefixMatch":["apcs-backups/"]},
  "actions":{"baseBlob":{"delete":{"daysAfterModificationGreaterThan":35}}}}}]}
JSON
az storage account management-policy create -g "$RESOURCE_GROUP" --account-name "$STORAGE_ACCOUNT" --policy "@$policy" -o none
rm -f "$policy"

expiry="$(date -u -d '+60 days' +%Y-%m-%dT%H:%MZ)"
sas="$(az storage container generate-sas --account-name "$STORAGE_ACCOUNT" --account-key "$key" -n apcs-backups \
  --permissions racwdl --expiry "$expiry" --https-only -o tsv)"

ip="$(az network public-ip show -g "$RESOURCE_GROUP" -n "$IP_NAME" --query ipAddress -o tsv)"
fqdn="$(az network public-ip show -g "$RESOURCE_GROUP" -n "$IP_NAME" --query dnsSettings.fqdn -o tsv)"
cat <<INFO

Done.
  VM public IP : $ip
  Azure FQDN   : $fqdn
  SSH          : ssh -i ${SSH_PUBLIC_KEY%.pub} azureuser@$ip
  Backup SAS   : RCLONE_CONFIG_AZURE_SAS_URL=https://$STORAGE_ACCOUNT.blob.core.windows.net/apcs-backups?$sas
                 (expires $expiry; store it only in /opt/apcs/env/backup.env)
INFO
