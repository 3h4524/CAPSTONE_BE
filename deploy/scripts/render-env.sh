#!/usr/bin/env bash
set -euo pipefail

deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
env_dir="${ENV_DIR:-/opt/apcs/env}"
set -a
. "$deploy_dir/env/common.env"
set +a

secret() {
  gcloud secrets versions access latest --secret="$1" --project="$GCP_PROJECT" --quiet
}

PROD_SECRETS="$(secret apcs-prod-secrets)"
PROD_EXTERNAL="$(secret apcs-prod-external)"
DEV_SECRETS="$(secret apcs-dev-secrets)"
DEV_MIGRATOR_PASSWORD="$(secret apcs-dev-migrator-password)"
export PROD_SECRETS PROD_EXTERNAL DEV_SECRETS DEV_MIGRATOR_PASSWORD

mkdir -p "$env_dir"
chmod 700 "$env_dir"

python3 - "$deploy_dir" "$env_dir" <<'PY'
import json
import os
import pathlib
import re
import sys

deploy_dir = pathlib.Path(sys.argv[1])
env_dir = pathlib.Path(sys.argv[2])

values = {}
for line in (deploy_dir / "env" / "common.env").read_text().splitlines():
    if line.strip() and not line.startswith("#"):
        key, _, value = line.partition("=")
        values[key.strip()] = value.strip()
values.update(json.loads(os.environ["PROD_EXTERNAL"]))
values.update(json.loads(os.environ["PROD_SECRETS"]))
values.update(json.loads(os.environ["DEV_SECRETS"]))
values["DEV_MIGRATOR_PASSWORD"] = os.environ["DEV_MIGRATOR_PASSWORD"].strip()

placeholder = re.compile(r"\$\{([A-Z0-9_]+)\}")
missing = set()

def render(text):
    def substitute(match):
        name = match.group(1)
        if name not in values:
            missing.add(name)
            return ""
        return str(values[name])
    return placeholder.sub(substitute, text)

outputs = {}
for template in sorted((deploy_dir / "env").glob("*.env.tpl")):
    name = template.name[: -len(".tpl")]
    target = deploy_dir / ".env" if name == "compose.env" else env_dir / name
    outputs[target] = render(template.read_text())

if missing:
    sys.exit("Secret Manager is missing: " + ", ".join(sorted(missing)))

compose_env = deploy_dir / ".env"
if compose_env.exists():
    kept = [line for line in compose_env.read_text().splitlines() if line.startswith(("API_TAG=", "FE_TAG="))]
    if kept:
        outputs[compose_env] = outputs[compose_env].rstrip("\n") + "\n" + "\n".join(kept) + "\n"

for target, content in outputs.items():
    temporary = target.with_suffix(target.suffix + ".tmp")
    temporary.write_text(content)
    temporary.chmod(0o600)
    temporary.replace(target)
    print(f"rendered {target}")
PY
