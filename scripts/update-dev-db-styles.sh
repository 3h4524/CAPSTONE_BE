#!/usr/bin/env bash
# ==============================================================================
# APCS - Database Style Presets Seed Script
# Target: APCS dev (port 5432) or prod (port 2510) database on VM via IAP
# Account: de180430nguyensinhnhat@gmail.com
# ==============================================================================

set -euo pipefail

TARGET="dev"
CONTAINER="apcs-dev-postgres-dev-1"

if [[ "${1:-}" == "--prod" ]]; then
    TARGET="prod"
    CONTAINER="apcs-postgres-prod-1"
    echo "⚠️  TARGETING APCS PRODUCTION DATABASE (container: ${CONTAINER}, port 2510)"
else
    echo "ℹ️  TARGETING APCS DEV DATABASE (container: ${CONTAINER}, port 5432)"
fi

PROJECT="apcs-prod"
ZONE="asia-southeast1-b"
VM_NAME="vm-apcs-prod"
GCLOUD_ACCOUNT="de180430nguyensinhnhat@gmail.com"
SQL_FILE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/sql/style-art-presets.sql"

if [[ ! -f "$SQL_FILE" ]]; then
    echo "❌ Error: SQL file not found at ${SQL_FILE}"
    exit 1
fi

echo "Connecting to VM ${VM_NAME} via gcloud IAP and executing SQL into ${CONTAINER} (db: apcs)..."
gcloud compute ssh "${VM_NAME}" \
    --project="${PROJECT}" \
    --zone="${ZONE}" \
    --account="${GCLOUD_ACCOUNT}" \
    --tunnel-through-iap \
    --command="sudo docker exec -i ${CONTAINER} psql -U postgres -d apcs -v ON_ERROR_STOP=1" < "${SQL_FILE}"

echo ""
echo "✅ Successfully applied 12 Style Presets to APCS ${TARGET}!"
echo "Verifying seeded records:"
gcloud compute ssh "${VM_NAME}" \
    --project="${PROJECT}" \
    --zone="${ZONE}" \
    --account="${GCLOUD_ACCOUNT}" \
    --tunnel-through-iap \
    --command="sudo docker exec ${CONTAINER} psql -U postgres -d apcs -c '
SELECT name, is_system_template, length(style_modifiers) as mod_len, jsonb_array_length(recommendations) as rec_count, (preview_image_url IS NOT NULL) as has_image
FROM public.style_art_presets
WHERE is_system_template = true
ORDER BY name;
'"
