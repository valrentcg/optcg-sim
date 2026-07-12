# optcg-ranked — authoritative Bounty ranked Worker

Server-authoritative ranked backend on **Cloudflare Workers + D1**. The Unity client
can no longer move its own rating; it only reports match results and reads standings.

## Why it can't be forged

- **`/report` requires a Unity Authentication token.** `auth.ts` verifies the UGS
  JWT (RS256, against Unity's JWKS) and takes the reporter's identity from the
  token `sub` — not a client-supplied field.
- **Dual-report cross-check.** A rating only moves when **both** players report the
  same `matchId` and **agree** on who won (one `win`, one `loss`, pointing at each
  other). A lone modded client cannot produce the opponent's signed half.
- **Disagreements** are recorded `disputed` and change nothing.
- The server holds both players' real ratings, so the Glicko-2 update is fully
  correct (no even-match approximation).

The `X-App-Secret` header is a coarse gate (same pattern as the gamelogs worker) to
keep randos off the endpoint; the real security is the per-player token.

## Endpoints

| Method | Path           | Auth                 | Purpose |
|--------|----------------|----------------------|---------|
| POST   | `/report`      | UGS Bearer + secret  | Submit your half: `{ matchId, opponentId, result:"win"\|"loss", username? }` → `{ status:"pending"\|"settled"\|"disputed", profile }` |
| GET    | `/profile`     | secret               | `?playerId=` → `{ profile }` |
| GET    | `/leaderboard` | secret               | `?limit=` (≤200) → `{ entries[] }` ordered by bounty |
| GET    | `/health`      | secret               | `{ ok: true }` |

## Deploy (one-time)

Run these from `Deploy/ranked-worker/` (wrangler is already logged in on this machine):

```bash
npm install

# 1. Create the D1 database, then paste the printed database_id into wrangler.toml
wrangler d1 create optcg-ranked

# 2. Create the tables (local + remote)
wrangler d1 execute optcg-ranked --file=./schema.sql
wrangler d1 execute optcg-ranked --file=./schema.sql --remote

# 3. Set the shared app secret (use the SAME value the client sends in X-App-Secret)
wrangler secret put APP_SECRET

# 4. Ship it
npm run deploy
```

`wrangler deploy` prints the worker URL (e.g. `https://optcg-ranked.<subdomain>.workers.dev`).
Put that URL + the app secret into the Unity client (`RankedStore.WorkerBase` /
`RankedStore.AppSecret`).

## Keeping the math in sync

`src/ranking.ts` is a line-for-line port of the Unity client's `Glicko2.cs` +
`RankedStore.cs`. If you change a tuning constant in one, change it in the other.
Parity is checked by running the same scenarios through both (they matched exactly:
placement → Supernova ฿200M, Rampage +฿16.2M at streak 3, Vivre absorb, Yonko→Warlord
season drop).
