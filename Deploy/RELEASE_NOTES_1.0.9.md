# v1.0.9 — Matchmaking fixes (Bug Testing Season)

This release makes Ranked & Casual matchmaking actually work end-to-end.

## Fixes
- **Queues now pair you up.** Fixed a server-side auth failure (the ranked worker
  fetched Unity's signing keys from an OpenID discovery URL that returns 404, so it
  rejected every token) that silently blocked *all* matchmaking and match reporting —
  the client just sat on "FINDING MATCH" forever. Also redeployed the worker with the
  `/queue/*` routes and matchmaking tables that were missing in production.
- **Opponent no longer gets stuck on "Connecting…".** Fixed a race where the host sent
  the match-start the instant the connection appeared, before the guest had registered
  its receive handler — Netcode dropped the message and the guest hung while the host
  loaded in alone. The host now waits until it's heard from the guest before starting.

## Quality of life
- Matchmaking now shows "Can't reach the matchmaking server. Retrying…" after a sustained
  run of failed queue calls, instead of an endless "FINDING MATCH" spinner.

## Notes
- Ready check is a 15-second window — both players should hit **Accept** promptly.
