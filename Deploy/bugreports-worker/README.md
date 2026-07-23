# optcg-bugreports — centralized bug-report collection

Every player's in-game bug reports (right-click a card → "Report a Bug") POST here and land in one
D1 database, so they can be scraped and triaged from anywhere. The client also keeps a local copy
(`persistentDataPath/BugReports/bugs.jsonl`) and uploads best-effort — a down server never loses a report.

## One-time deploy

```bash
cd Deploy/bugreports-worker
npm i -g wrangler        # if needed
wrangler login

# 1) Create the D1 database, then paste the printed database_id into wrangler.toml
wrangler d1 create optcg-bugreports

# 2) Create the table (add --remote for the deployed DB; run without it for local dev)
wrangler d1 execute optcg-bugreports --file=./schema.sql --remote

# 3) Two secrets:
#    APP_SECRET   = the SAME value the client sends (Assets/.../AppConfig.Local.cs → AppConfig.AppSecret,
#                   the same secret the gamelogs/ranked workers already use). Lets any player SUBMIT.
wrangler secret put APP_SECRET
#    ADMIN_SECRET = a NEW strong value, NOT shipped in the client. Required to LIST/EXPORT/mark-addressed.
wrangler secret put ADMIN_SECRET

# 4) Deploy
wrangler deploy
```

The deployed URL is `https://optcg-bugreports.<your-subdomain>.workers.dev`. The client is wired to
`https://optcg-bugreports.valrentcg.workers.dev/submit` (matches the gamelogs/ranked subdomain). If your
workers.dev subdomain differs, update `UploadUrl` in `Assets/Scripts/BugReportStore.cs`.

## Endpoints

| Method | Path         | Auth            | Purpose |
|--------|--------------|-----------------|---------|
| POST   | `/submit`    | `X-App-Secret`  | A player's client submits one BugReport JSON. |
| GET    | `/list`      | `X-Admin-Secret`| Reports as JSON. `?addressed=0\|1\|all` (default 0=open), `?limit` (≤1000), `?offset`, `?card=<id>`, `?full=1` (include command_history + raw_json). Also returns `open`/`total` counts. |
| GET    | `/export`    | `X-Admin-Secret`| Full raw JSONL (one BugReport per line) across all players. `?addressed=0\|1\|all`. |
| POST   | `/addressed` | `X-Admin-Secret`| Mark fixed (not deleted). Body `{ "ids": ["..."], "note": "fixed in vX — ..." }` or `{ "id": "...", "note": "..." }`. |
| GET    | `/health`    | none            | `{ ok: true }`. |

## Scraping (what I run to triage)

```bash
# Open (unaddressed) reports, newest first, human-skimmable:
curl -s -H "X-Admin-Secret: $ADMIN" \
  "https://optcg-bugreports.valrentcg.workers.dev/list?addressed=0&limit=100"

# One specific report WITH its exact-repro command_history:
curl -s -H "X-Admin-Secret: $ADMIN" \
  "https://optcg-bugreports.valrentcg.workers.dev/list?full=1&limit=1"

# Everything as JSONL (same shape as the local bugs.jsonl, aggregated across players):
curl -s -H "X-Admin-Secret: $ADMIN" \
  "https://optcg-bugreports.valrentcg.workers.dev/export"

# After a fix ships, close reports out (kept, just flagged addressed):
curl -s -X POST -H "X-Admin-Secret: $ADMIN" -H "content-type: application/json" \
  -d '{"ids":["20260722-153000-123"],"note":"fixed in v1.0.20 — clause normalization"}' \
  "https://optcg-bugreports.valrentcg.workers.dev/addressed"
```

Each report carries `Seed` + deck ids + `command_history`, which deterministically rebuilds the exact
position via `GameEngine.CreateMatch` / `ApplyCommand` — so any report can be reproduced headlessly.
