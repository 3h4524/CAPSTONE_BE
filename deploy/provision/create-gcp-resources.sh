#!/usr/bin/env bash
set -euo pipefail

deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
set -a
. "$deploy_dir/env/common.env"
set +a

GITHUB_REPOS="${GITHUB_REPOS:-3h4524/CAPSTONE_BE 3h4524/CAPSTONE_FE}"
BACKEND_REPO="${BACKEND_REPO:-3h4524/CAPSTONE_BE}"
MACHINE_TYPE="${MACHINE_TYPE:-e2-standard-2}"
IP_NAME="${IP_NAME:-ip-apcs-prod}"
AR_REPO="${AR_REPO:-apcs}"
OPEN_DEV_DB="${OPEN_DEV_DB:-true}"
EXTRA_OWNER="${EXTRA_OWNER:-}"
IAP_RANGE=35.235.240.0/20

p=(--project "$GCP_PROJECT" --quiet)
exists() { "$@" > /dev/null 2>&1; }
sa() { echo "$1@$GCP_PROJECT.iam.gserviceaccount.com"; }
VM_SA="$(sa apcs-vm)"
DEPLOYER_SA="$(sa apcs-deployer)"
MIGRATOR_SA="$(sa apcs-dev-migrator)"
PROJECT_NUMBER="$(gcloud projects describe "$GCP_PROJECT" --format='value(projectNumber)')"
POOL="projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/github"

echo "Account: $(gcloud config get-value account 2> /dev/null)  Project: $GCP_PROJECT ($PROJECT_NUMBER)"

gcloud services enable compute.googleapis.com iam.googleapis.com iamcredentials.googleapis.com sts.googleapis.com \
  iap.googleapis.com secretmanager.googleapis.com artifactregistry.googleapis.com oslogin.googleapis.com \
  storage.googleapis.com "${p[@]}"

for name in apcs-vm apcs-deployer apcs-dev-migrator; do
  exists gcloud iam service-accounts describe "$(sa "$name")" "${p[@]}" \
    || gcloud iam service-accounts create "$name" --display-name "$name" "${p[@]}"
done

bind_project() { gcloud projects add-iam-policy-binding "$GCP_PROJECT" --member "$1" --role "$2" --condition None --quiet > /dev/null; }
bind_project "serviceAccount:$VM_SA" roles/logging.logWriter
bind_project "serviceAccount:$VM_SA" roles/monitoring.metricWriter
for member in "serviceAccount:$DEPLOYER_SA" "serviceAccount:$MIGRATOR_SA"; do
  bind_project "$member" roles/iap.tunnelResourceAccessor
  bind_project "$member" roles/compute.viewer
done
gcloud iam service-accounts add-iam-policy-binding "$VM_SA" --member "serviceAccount:$DEPLOYER_SA" \
  --role roles/iam.serviceAccountUser "${p[@]}" > /dev/null
if [ -n "$EXTRA_OWNER" ]; then
  bind_project "user:$EXTRA_OWNER" roles/owner
fi

exists gcloud compute addresses describe "$IP_NAME" --region "$GCP_REGION" "${p[@]}" \
  || gcloud compute addresses create "$IP_NAME" --region "$GCP_REGION" --network-tier PREMIUM "${p[@]}"

for rule in default-allow-ssh default-allow-rdp; do
  exists gcloud compute firewall-rules describe "$rule" "${p[@]}" && gcloud compute firewall-rules delete "$rule" "${p[@]}"
done

firewall() {
  local name="$1" ports="$2" source="$3"
  if exists gcloud compute firewall-rules describe "$name" "${p[@]}"; then
    gcloud compute firewall-rules update "$name" --allow "$ports" --source-ranges "$source" "${p[@]}"
  else
    gcloud compute firewall-rules create "$name" --network default --direction INGRESS \
      --allow "$ports" --source-ranges "$source" --target-tags apcs "${p[@]}"
  fi
}
firewall apcs-allow-web tcp:80,tcp:443,udp:443 0.0.0.0/0
firewall apcs-allow-iap tcp:22,tcp:5432 "$IAP_RANGE"
if [ "$OPEN_DEV_DB" = true ]; then
  firewall apcs-allow-dev-postgres tcp:5432 0.0.0.0/0
else
  exists gcloud compute firewall-rules describe apcs-allow-dev-postgres "${p[@]}" \
    && gcloud compute firewall-rules delete apcs-allow-dev-postgres "${p[@]}"
fi

exists gcloud artifacts repositories describe "$AR_REPO" --location "$GCP_REGION" "${p[@]}" \
  || gcloud artifacts repositories create "$AR_REPO" --repository-format docker --location "$GCP_REGION" "${p[@]}"
cleanup="$(mktemp)"
cat > "$cleanup" <<'JSON'
[
  {"name": "keep-latest-5", "action": {"type": "Keep"}, "mostRecentVersions": {"keepCount": 5}},
  {"name": "delete-older", "action": {"type": "Delete"}, "condition": {"tagState": "any", "olderThan": "1d"}}
]
JSON
gcloud artifacts repositories set-cleanup-policies "$AR_REPO" --location "$GCP_REGION" --policy "$cleanup" --no-dry-run "${p[@]}" > /dev/null
rm -f "$cleanup"
gcloud artifacts repositories add-iam-policy-binding "$AR_REPO" --location "$GCP_REGION" \
  --member "serviceAccount:$DEPLOYER_SA" --role roles/artifactregistry.writer "${p[@]}" > /dev/null
gcloud artifacts repositories add-iam-policy-binding "$AR_REPO" --location "$GCP_REGION" \
  --member "serviceAccount:$VM_SA" --role roles/artifactregistry.reader "${p[@]}" > /dev/null

exists gcloud storage buckets describe "gs://$BACKUP_BUCKET" "${p[@]}" \
  || gcloud storage buckets create "gs://$BACKUP_BUCKET" --location "$GCP_REGION" \
       --uniform-bucket-level-access --public-access-prevention "${p[@]}"
lifecycle="$(mktemp)"
echo '{"rule":[{"action":{"type":"Delete"},"condition":{"age":35}}]}' > "$lifecycle"
gcloud storage buckets update "gs://$BACKUP_BUCKET" --lifecycle-file "$lifecycle" "${p[@]}" > /dev/null
rm -f "$lifecycle"
gcloud storage buckets add-iam-policy-binding "gs://$BACKUP_BUCKET" \
  --member "serviceAccount:$VM_SA" --role roles/storage.objectAdmin "${p[@]}" > /dev/null

for secret in apcs-prod-secrets apcs-prod-external apcs-dev-secrets apcs-dev-migrator-password; do
  exists gcloud secrets describe "$secret" "${p[@]}" \
    || gcloud secrets create "$secret" --replication-policy automatic "${p[@]}"
  gcloud secrets add-iam-policy-binding "$secret" --member "serviceAccount:$VM_SA" \
    --role roles/secretmanager.secretAccessor "${p[@]}" > /dev/null
done
gcloud secrets add-iam-policy-binding apcs-dev-migrator-password --member "serviceAccount:$MIGRATOR_SA" \
  --role roles/secretmanager.secretAccessor "${p[@]}" > /dev/null

exists gcloud iam workload-identity-pools describe github --location global "${p[@]}" \
  || gcloud iam workload-identity-pools create github --location global --display-name "GitHub Actions" "${p[@]}"
repo_list="$(printf "'%s'," $GITHUB_REPOS)"
condition="assertion.repository in [${repo_list%,}] && assertion.event_name in ['push', 'workflow_dispatch']"
mapping="google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.repo_ref=assertion.repository + '@' + assertion.ref_type"
if exists gcloud iam workload-identity-pools providers describe github-actions --workload-identity-pool github --location global "${p[@]}"; then
  gcloud iam workload-identity-pools providers update-oidc github-actions --workload-identity-pool github --location global \
    --attribute-mapping "$mapping" --attribute-condition "$condition" "${p[@]}"
else
  gcloud iam workload-identity-pools providers create-oidc github-actions --workload-identity-pool github --location global \
    --issuer-uri https://token.actions.githubusercontent.com \
    --attribute-mapping "$mapping" --attribute-condition "$condition" "${p[@]}"
fi
for repo in $GITHUB_REPOS; do
  gcloud iam service-accounts add-iam-policy-binding "$DEPLOYER_SA" --role roles/iam.workloadIdentityUser \
    --member "principalSet://iam.googleapis.com/$POOL/attribute.repo_ref/$repo@tag" "${p[@]}" > /dev/null
done
gcloud iam service-accounts add-iam-policy-binding "$MIGRATOR_SA" --role roles/iam.workloadIdentityUser \
  --member "principalSet://iam.googleapis.com/$POOL/attribute.repo_ref/$BACKEND_REPO@branch" "${p[@]}" > /dev/null

if ! exists gcloud compute instances describe "$VM_NAME" --zone "$GCP_ZONE" "${p[@]}"; then
  gcloud compute instances create "$VM_NAME" --zone "$GCP_ZONE" --machine-type "$MACHINE_TYPE" \
    --image-family ubuntu-2404-lts-amd64 --image-project ubuntu-os-cloud \
    --boot-disk-size 64GB --boot-disk-type pd-balanced \
    --address "$IP_NAME" --tags apcs \
    --service-account "$VM_SA" --scopes cloud-platform \
    --metadata enable-oslogin=TRUE \
    --shielded-secure-boot --shielded-vtpm --shielded-integrity-monitoring "${p[@]}"
fi
gcloud compute instances add-iam-policy-binding "$VM_NAME" --zone "$GCP_ZONE" \
  --member "serviceAccount:$DEPLOYER_SA" --role roles/compute.osAdminLogin "${p[@]}" > /dev/null

ip="$(gcloud compute addresses describe "$IP_NAME" --region "$GCP_REGION" --format 'value(address)' "${p[@]}")"
cat <<INFO

Done.
  VM public IP          : $ip   (set APP_DOMAIN=$ip.sslip.io in deploy/env/common.env until a domain exists)
  SSH                   : gcloud compute ssh $VM_NAME --zone $GCP_ZONE --tunnel-through-iap --project $GCP_PROJECT
  Registry              : $GCP_REGION-docker.pkg.dev/$GCP_PROJECT/$AR_REPO
  WIF provider          : $POOL/providers/github-actions
  Deployer / migrator   : $DEPLOYER_SA / $MIGRATOR_SA
  Secrets to fill       : apcs-prod-secrets (generated), apcs-prod-external (external keys), apcs-dev-secrets, apcs-dev-migrator-password
INFO
