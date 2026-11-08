#!/usr/bin/env bash
set -euo pipefail

deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
set -a
. "$deploy_dir/env/common.env"
set +a
: "${ALERT_EMAIL:?Set ALERT_EMAIL to the address that receives VM alerts}"

api="https://monitoring.googleapis.com/v3/projects/$GCP_PROJECT"
token="$(gcloud auth print-access-token)"
call() { curl -fsS -H "Authorization: Bearer $token" -H 'Content-Type: application/json' "$@"; }

gcloud services enable monitoring.googleapis.com logging.googleapis.com --project "$GCP_PROJECT" --quiet

channel="$(call "$api/notificationChannels" | python3 -c "
import json,sys
for c in json.load(sys.stdin).get('notificationChannels', []):
    if c.get('labels', {}).get('email_address') == sys.argv[1]:
        print(c['name']); break
" "$ALERT_EMAIL")"
if [ -z "$channel" ]; then
  channel="$(call -X POST "$api/notificationChannels" \
    -d "{\"type\":\"email\",\"displayName\":\"APCS alerts\",\"labels\":{\"email_address\":\"$ALERT_EMAIL\"}}" \
    | python3 -c "import json,sys; print(json.load(sys.stdin)['name'])")"
fi

existing="$(call "$api/alertPolicies" | python3 -c "
import json,sys
print('\n'.join(p['displayName'] for p in json.load(sys.stdin).get('alertPolicies', [])))")"

policy() {
  local name="$1" filter="$2" threshold="$3" duration="$4"
  if grep -qxF "$name" <<< "$existing"; then
    echo "exists: $name"
    return
  fi
  call -X POST "$api/alertPolicies" -d "$(python3 -c "
import json,sys
name, flt, threshold, duration, channel = sys.argv[1:]
print(json.dumps({
  'displayName': name,
  'combiner': 'OR',
  'notificationChannels': [channel],
  'conditions': [{
    'displayName': name,
    'conditionThreshold': {
      'filter': flt,
      'comparison': 'COMPARISON_GT',
      'thresholdValue': float(threshold),
      'duration': duration,
      'aggregations': [{'alignmentPeriod': '60s', 'perSeriesAligner': 'ALIGN_MEAN'}]
    }
  }]
}))" "$name" "$filter" "$threshold" "$duration" "$channel")" > /dev/null
  echo "created: $name"
}

vm="resource.type = \"gce_instance\" AND resource.labels.instance_id = \"$(gcloud compute instances describe "$VM_NAME" --zone "$GCP_ZONE" --project "$GCP_PROJECT" --format='value(id)')\""
policy "APCS VM CPU above 90%" "$vm AND metric.type = \"compute.googleapis.com/instance/cpu/utilization\"" 0.9 600s
policy "APCS VM memory above 90%" "$vm AND metric.type = \"agent.googleapis.com/memory/percent_used\" AND metric.labels.state = \"used\"" 90 300s
policy "APCS VM disk above 85%" "$vm AND metric.type = \"agent.googleapis.com/disk/percent_used\" AND metric.labels.device = \"/dev/root\" AND metric.labels.state = \"used\"" 85 300s
echo "Alerts go to $ALERT_EMAIL"
