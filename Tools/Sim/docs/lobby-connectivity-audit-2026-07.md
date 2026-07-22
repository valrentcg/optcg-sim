# Lobby / Connectivity Audit — Ranked · Casual · Custom (2026-07-22)

## Architecture (as-built)

Two independent layers:

1. **Signaling / matchmaking** — Cloudflare Worker + D1 (`Deploy/ranked-worker`).
   - `/queue/*` = ranked + casual pairing (poll-based ready-check). Same table, `mode`
     column separates the two pools (`matchmaking.ts`).
   - Custom lobbies **do not touch the worker** — they use UGS Lobby directly
     (`LobbyManager.CreateLobbyAsync` / `JoinByCodeAsync` / browse).
2. **Transport** — UGS **Relay** + Netcode-for-GameObjects (NGO). Host creates a UGS
   session; guest joins by id (`JoinByIdAsync`). Gameplay is a deterministic
   `GameCommand` log over NGO custom named messages (`MatchNetworkSync`). No NetworkObjects,
   no scene sync (deliberately disabled — see `NetworkBootstrap`).

Connect handshake (ranked/casual):
`join → poll → proposed → ready(accept) → accepted → host CreateSession + host-ready →
matched(+matchId) → guest JoinById → NGO peer connects → both exchange name/deck →
host TryHostLaunch → SendMatchStart → both LaunchNetworkedMatch`.

## Findings (prioritized)

### P1 — Ranked/Casual "Connecting…" modal has NO cancel and NO client timeout ⚠️ [FIX LANDED]
`BuildRankedQueueModal` shows a bare spinner for `accepted`/`matched`; the Cancel button
exists only in the searching branch (`MainMenuManager.cs:5947` vs `:5965`). If the Relay
handshake stalls (guest joined the session but NGO never establishes the peer, or a worker
blip during connect), the player is stuck until the **server's** `STALE_MATCH_MS` (120 s,
`matchmaking.ts:26`) clears the proposal → poll returns idle → `EndRankedQueue`. Up to a
2-minute frozen spinner with no escape. **Fix:** client-side connect timeout (45 s) +
Cancel button on the connecting modal.

### P2 — Custom-lobby Start Match lacks the guest-handlers-ready guard ranked has ⚠️ [FIX LANDED]
**This is the leading explanation for the user's report: "won't connect on most modes aside
from ranked."** Ranked gates the match-start on `rankedGuestReady` — proof a real message
arrived from the guest, i.e. its NGO receive-handlers are registered
(`MainMenuManager.cs:5850`, rationale at `:5687`). The **custom** manual Start button only
checked `IsPeerConnected`, which can turn true a beat before the guest registers handlers →
the `ReliableFragmentedSequenced` match-start is dropped (NGO drops a named message with no
handler) → guest hangs on "Connecting…". A human clicking Start the instant it lights up is
exactly the trigger. Ranked is the ONE mode hardened against this, which is precisely why it
works and custom doesn't.
**Fix:** gate the custom Start button (and the "Finishing connection…" note) on
`peerHandlersReady = lobbyPeerName != null || lobbyPeerDeck != null` — proof a peer message
arrived. Guest always sends its name on connect (`OnLobbyNetworkClientConnected` →
`SendPeerName`, non-null), so it can't deadlock. Added `ResetPeerLobbyState()` at all 5
fresh-lobby entry points (create / join-by-code / browse-join / invite-host / invite-accept)
so a stale name from a prior match can't re-open the race, and a repaint in
`OnPeerNameReceived` so the button enables the moment the name lands.

### P6 — Casual auto-launch was reported as RANKED
`TryHostLaunch` hard-set `lobbyRanked = true`, so a CASUAL match counted toward the bounty
ladder. Fixed to `lobbyRanked = lobbyMode == "ranked"`. (Correctness, not connectivity.)

### NOTE on CASUAL "won't connect"
Casual runs the **identical** client handshake to ranked (`StartQueue(mode)` → same
`HandleRankedStatus`/`TryHostLaunch`, guarded by `rankedGuestReady`), so it is NOT exposed to
the P2 race and should connect exactly as reliably as ranked *when an opponent exists*. If
casual still fails, suspect an **empty casual pool** (no one else queuing casual → "FINDING
CASUAL MATCH" forever, read as "won't connect") rather than a connect bug. Needs the user to
confirm the test setup (two clients queuing casual simultaneously).

### P3 — Error surfacing suppressed during connect
`serverDown = RankedServerDown && !proposed && !connecting` (`:5911`). A worker outage
*during* the connect phase is both invisible and (pre-P1) unrecoverable. P1's timeout is
the backstop; consider also surfacing `RankedServerDown` during connect.

### P4 — In-match desync from engine nondeterminism [AUDITED — engine is clean; version-skew guard LANDED]
Both clients replay the same `GameCommand` log, so any cross-process nondeterminism =
silently divergent boards. **Audit result: the engine is cross-process deterministic.**
- RNG = `SeededRng` → `HashString` is **FNV-1a** (not `string.GetHashCode`), keyed off
  `state.Seed` + purpose + `EffectSequence`. Coin flip / shuffles / mulligans all stable
  across processes.
- No `Guid.NewGuid` / unseeded `Random` in game logic. `DateTime.Now` only stamps a
  cosmetic event-log `Time` field (not state).
- No order-dependent iteration of string-keyed `Dictionary`/`HashSet`: the four
  `HashSet<string>` uses are membership (`.Contains`) only; no `foreach` over `state.Players`.
- Effect/modifier ordering is List/queue-based (`PendingEffects`, `ActiveModifiers`),
  deterministic.
The real residual desync cause was **version/card-data skew** — the `UpdateChecker` build
check is a startup nudge, NOT a matchmaking/connection gate, so two builds could match and
diverge. **Fixed:** `MatchStartPayload.build` carries the host's `CurrentBuildNumber`; the
guest aborts with a clear "different game version" message on mismatch (build==0 = old client
= mismatch). Covers all three modes (all use match-start). Ranked integrity safe — an aborted
guest never reports, so the host's resulting opponent-left "win" stays unsettled (dual-report).
Still no 2-client soak test (can't run Relay/NGO headless), but the determinism basis is now
verified rather than assumed.

### P7 — No grace period for a transient disconnect [NOT FIXED — needs runtime + reconnection design]
`OnPeerDisconnected` (GameManager) immediately concedes the match on ANY NGO client-disconnect.
A brief Relay/network hiccup therefore ends the match instantly as a forfeit — no reconnect
window. Proper fix = a short grace timer + Relay reconnection, which is a real feature and
unsafe to write blind. Logged for a future pass.

### P5 — Host `host-ready` has no retry
If `QueueHostReadyAsync` fails after the host created the UGS session
(`CreateRankedSessionAndReport:5816`), the guest never receives `matchId`; both recover
only via the 120 s STALE backstop. Add one retry.

## Status
- P1 (ranked/casual connect timeout + Cancel): FIX LANDED.
- P2 (custom Start guest-handlers-ready guard — the likely custom-connect bug): FIX LANDED.
- P3 (surface server-down during connect): FIX LANDED.
- P4 (in-match desync): AUDITED clean; version-skew guard LANDED.
- P5 (host-ready retry): FIX LANDED.
- P6 (casual mislabeled ranked): FIX LANDED.
- P7 (transient-disconnect grace period): NOT FIXED — needs runtime + reconnection design.
- ALL landed fixes need a 2-client Unity build test (headless Sim can't exercise Relay/NGO).

## Files touched
- `Assets/Scripts/MainMenuManager.cs` — P1, P2, P3, P5, P6, P4-version-stamp/guard.
- `Assets/Scripts/MatchNetworkSync.cs` — `MatchStartPayload.build` field (P4).

Verification path (per project norm): Unity build + real 2-client test — the headless Sim
can't exercise Relay/NGO.
