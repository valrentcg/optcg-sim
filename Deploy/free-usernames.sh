#!/usr/bin/env bash
# Free claimed usernames + clear the player-id→username index in Cloud Save CUSTOM data.
#
# WHY THIS EXISTS: custom-data WRITES (including delete) are Service-Account-only —
#   DELETE /v1/data/projects/{projectId}/custom/{customId}/items/{key}
#   security: - ServiceAccount: []
# Cloud Code's own context token can setCustomItem but NOT deleteCustomItem, which is why
# the AdminClearIdentity script returned 403 on every key. There is also no dashboard UI for
# custom data (Cloud Save shows Game Data / Player Data / Player Files only), so this is the
# only route.
#
# USAGE (run from Git Bash; the key never leaves your machine):
#   export UNITY_SA_KEY_ID='...'
#   export UNITY_SA_SECRET='...'
#   bash Deploy/free-usernames.sh
#
# Create the key at: Unity Cloud → Administration → Service Accounts → new key,
# with a role granting Cloud Save write/admin on this project.

set -uo pipefail

PROJECT_ID="ad4ea220-a0bd-4716-ab99-6cc370dde5e5"
BASE="https://cloud-save.services.api.unity.com"

# Registry keys are the LOWERCASED username (ClaimUsername.js does rawName.toLowerCase()).
USERNAMES=(valren valren2 thereaper)
PLAYER_IDS=(P67WER7JHqws6UMWx8GsgY0KYJrV lPLxJ8uFNrxFmw5G5MdEGPCecwTx aC72gCPbGzmb42jxitfWoQIMKNWk)

: "${UNITY_SA_KEY_ID:?set UNITY_SA_KEY_ID first}"
: "${UNITY_SA_SECRET:?set UNITY_SA_SECRET first}"

BASIC=$(printf '%s:%s' "$UNITY_SA_KEY_ID" "$UNITY_SA_SECRET" | base64 -w0)

# The spec declares ServiceAccount as bearer, but Unity accepts Basic directly on most
# service APIs. Try Basic; if it is rejected, exchange for a stateless token and use Bearer.
AUTH="Basic $BASIC"
probe=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE \
  -H "Authorization: $AUTH" \
  "$BASE/v1/data/projects/$PROJECT_ID/custom/usernameRegistry/items/__connectivity_probe__")
if [ "$probe" = "401" ] || [ "$probe" = "403" ]; then
  echo "Basic rejected ($probe) — exchanging for a stateless token…"
  tok=$(curl -s -X POST -H "Authorization: Basic $BASIC" -H "Content-Length: 0" \
    "https://services.api.unity.com/auth/v1/token-exchange?projectId=$PROJECT_ID" \
    | sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
  if [ -z "$tok" ]; then echo "ERROR: token exchange failed — check the key and its role."; exit 1; fi
  AUTH="Bearer $tok"
  echo "token acquired."
else
  echo "Basic auth accepted (probe returned $probe)."
fi

del() { # entity, key
  code=$(curl -s -o /tmp/fu_body -w '%{http_code}' -X DELETE \
    -H "Authorization: $AUTH" \
    "$BASE/v1/data/projects/$PROJECT_ID/custom/$1/items/$2")
  case "$code" in
    2*) printf '  OK    %-18s %s\n' "$1" "$2" ;;
    404) printf '  none  %-18s %s (was not claimed)\n' "$1" "$2" ;;
    *)   printf '  FAIL  %-18s %s  HTTP %s  %s\n' "$1" "$2" "$code" "$(head -c 200 /tmp/fu_body)" ;;
  esac
}

echo
echo "Freeing usernames (usernameRegistry):"
for u in "${USERNAMES[@]}"; do del usernameRegistry "$u"; done
echo
echo "Clearing reverse index (playerUsernames):"
for p in "${PLAYER_IDS[@]}"; do del playerUsernames "$p"; done
echo
echo "Done. Re-run ClaimUsername in-game to confirm the name is free."
rm -f /tmp/fu_body
