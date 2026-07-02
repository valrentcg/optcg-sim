# Card Ruleset Implementation Progress

Tracks how much of the One Piece TCG comprehensive ruleset is actually
interactive in `Assets/Scripts/Engine/GameEngine.cs` / `GameManager.cs`,
versus just printed text with no behavior wired up.

**Scope note:** only `ST01` (Straw Hat Crew) and `ST02` (Worst Generation) —
34 cards total — are reachable in an actual match today (`MatchConfig`
always pairs `st01` vs `st02`, and there's no deck builder). The other
~4,538 cards in `official-card-library.json` are loaded into `CardData.Library`
but currently unreachable in gameplay. Effort is focused on the 34 first.

Architecture: a generic resolver (`TryResolveKnownEffect` / `TryResolveKnownTrigger`,
text-pattern matched) handles common templated effects; anything that doesn't fit
gets a hand-written per-card-id case (see `ActivateMain`'s switch, `AutomatedCounterPower`).
This mirrors how OPTCGSim's compiled client is structured (confirmed by inspecting
its assembly's method names — generic `Grant`/`Queue`/`OncePerTurn` helpers plus
one-off methods per unusual card like `BrookEffect`/`ViolaEffect`).

## Infrastructure status

| Piece | Status |
|---|---|
| Coin flip, choose first/second | Done |
| True per-game shuffle (random seed) | Done |
| Mulligan (look at hand, optional reshuffle, life dealt after) | Done |
| `[On Play]` / `[Main]` (event) effect queue + targeting | Done |
| `[Trigger]` resolution on life reveal | Done — "Play this card", "Activate this card's [Main] effect", and a generic catch-all for any other event trigger that matches a known resolvable pattern |
| `[Activate: Main]` ability button + `[Once Per Turn]` usage tracking | Done (infra); wired to 3 cards (Luffy Leader, Nami, Thousand Sunny) |
| `[DON!! x N]` conditional bonuses | Done for self power/keyword/board-state cases (`DonPowerBonuses` table) and the X.Drake-style board-wide aura case |
| Temporary "+power during this turn" buffs | Done (`GameState.TemporaryPowerBonus`, generic "+1000 power" resolver) |
| Battle-time Blocker restrictions (no-Blocker / power-cap) | Done (`BattleState.NoBlocker` / `BlockerPowerBan`, checked in `BlockAttack`) |
| "When Attacking" DON!!xN hook | Done (`ApplyWhenAttackingEffects`, called from `DeclareAttack`) |
| End-of-turn / post-battle auto-triggers | Done for the 2 cards that need them (Kid 7-cost end-of-turn, Hawkins post-battle) |
| Look-at-top-N / reveal-and-take mechanics | Not started |
| Optional hand-discard cost (Kid Leader) | Not started |

## ST01 — Straw Hat Crew (Leader: Monkey D. Luffy)

| Card | Effect | Status |
|---|---|---|
| ST01-001 | Leader Luffy — `[Activate: Main][Once Per Turn]` give up to 1 rested DON!! | **Done** |
| ST01-002 | Usopp — `[DON!!x2][When Attacking]` opponent can't Blocker with 5000+ power this battle | **Done** |
| ST01-003 | Karoo — vanilla | N/A (no effect) |
| ST01-004 | Sanji — `[DON!!x2]` gains Rush | **Done** |
| ST01-005 | Jinbe — `[DON!!x1][When Attacking]` +1000 power to another card this turn | **Done** (simplification: doesn't exclude targeting itself) |
| ST01-006 | Chopper — Blocker keyword | **Done** (keyword already generic) |
| ST01-007 | Nami — `[Activate: Main][Once Per Turn]` give up to 1 rested DON!! | **Done** |
| ST01-008/009/010 | Nico Robin / Vivi / Franky — vanilla | N/A |
| ST01-011 | Brook — `[On Play]` give up to 2 rested DON!! | **Done** |
| ST01-012 | Luffy (5-cost) — Rush keyword + `[DON!!x2][When Attacking]` no Blocker at all this battle | **Done** |
| ST01-013 | Zoro — `[DON!!x1]` +1000 power | **Done** |
| ST01-014 | Guard Point (event) — Counter +3000; Trigger +1000 power this turn | **Done** |
| ST01-015 | Gum-Gum Jet Pistol (event) — `[Main]` K.O. opp character ≤6000 power; Trigger activates Main | **Done** |
| ST01-016 | Diable Jambe (event) — `[Main]` no-Blocker-this-turn grant; Trigger K.O. opp Blocker ≤cost 3 | **Done** (simplification: doesn't filter by {Straw Hat Crew} type tag — moot for this deck anyway) |
| ST01-017 | Thousand Sunny (stage) — `[Activate: Main]` rest stage: +1000 power this turn | **Done** |

## ST02 — Worst Generation (Leader: Eustass "Captain" Kid)

| Card | Effect | Status |
|---|---|---|
| ST02-001 | Leader Kid — `[Activate: Main][Once Per Turn]` cost ③, optional trash 1: set Leader active | Not started (needs hand-discard UI) |
| ST02-002 | Vito — vanilla | N/A |
| ST02-003 | Urouge — `[DON!!x1]` +2000 power if 3+ Characters | **Done** |
| ST02-004 | Bege — Blocker keyword | **Done** |
| ST02-005 | Killer — `[On Play]` K.O. opp rested character cost ≤3; Trigger "Play this card" | **Done** |
| ST02-006 | Koby — vanilla | N/A |
| ST02-007 | Jewelry Bonney — `[Activate: Main]` cost ①+rest self: look top 5, take 1 {Supernovas}, rest to bottom | Not started (needs look/reveal/reorder UI) |
| ST02-008 | Scratchmen Apoo — `[DON!!x1][When Attacking]` rest 1 opponent DON!! | **Done** (auto-picks an active DON, no choice needed since DON are fungible) |
| ST02-009 | Trafalgar Law — `[On Play]` set own rested character cost ≤5 active | **Done** |
| ST02-010 | Heat — vanilla | N/A |
| ST02-011 | Bepo — vanilla | N/A |
| ST02-012 | Basil Hawkins — `[DON!!x1][Once Per Turn][Your Turn]` if battles opp Character, set self active | **Done** |
| ST02-013 | Kid (7-cost) — Blocker keyword + `[DON!!x1][End of Your Turn]` set self active | **Done** |
| ST02-014 | X.Drake — `[DON!!x1][Your Turn]` if rested, allies +1000 power | **Done** (board-wide aura) |
| ST02-015 | Scalpel (event) — Counter +2000 + set 1 DON active; Trigger set 2 DON active | **Done** |
| ST02-016 | Repel (event) — Counter +4000 + set 1 DON active | **Done** |
| ST02-017 | Straw Sword (event) — `[Main]` rest opp character; Trigger free-play {Supernovas} ≤cost 2 | Main effect done (was the reported bug); trigger (free-play from hand) not started |

**Remaining gaps (only 3 of 34 cards):** ST02-001 (Kid Leader's optional hand-discard cost),
ST02-007 (Bonney's look-top-5/reveal/reorder), and ST02-017's trigger (play a card from hand
for free). All three need new UI flows (discard picker, deck-peek UI, free-play-from-hand) that
go beyond the click-a-target pattern everything else above reuses.

## How OPTCGSim handles rules (reference, from inspecting its compiled client)

Checked `OPTCGSim_Data/Managed/Assembly-CSharp.dll` at the string/identifier
level only (no decompiling method bodies, no logic copied) to see how a
shipped implementation structures this problem. No card-effect database,
JSON, or scripting language (no Lua/Python DLL) is bundled — everything is
compiled C#. The architecture has three layers:

1. **A structured `Action` descriptor model**, not text matching. Identifiers
   like `ActionNeedsTarget`, `ActionCanChooseNoTarget`, `ActionGivesChoice`,
   `ActionHasDownsideCost`, `ActionDrawsFromDeck`, `ActionAttachesRestedDon`,
   `ActionEndsWithMill` read like boolean/enum fields on a per-effect data
   object. Every card's effect appears to compile down to one or more of
   these structured `Action` instances (with metadata describing *how* to
   resolve them generically — needs a target? optional? has a cost?) rather
   than re-deriving that from the printed sentence every time.
2. **A generic queue/resolver runtime** built on top of that model:
   `ActionStepResolver`, `ActionIsAlreadyQueued`, `QueueUpUsedATriggerActions`,
   `bCheckAutomaticResolve` / `bPickedForAutoResolve` / `PreventAutoResolves`
   (auto-resolve when there's nothing to choose, otherwise wait for input —
   the same shape as our `EffectResolution.Resolved / WaitingForTarget /
   NotAutomated`), `RefreshCardOncePerTurns` / `SetCardAbilityUsed` (once-per-
   turn flags, same as our `AbilityUsedThisTurn`), `EitherDonXOrMore` (DON!!xN
   threshold checks, same idea as our `DonPowerBonuses` table).
3. **Hand-written one-off methods only for true outliers** — `BrookEffect`,
   `CheckForCrocodileEffect`, `ViolaEffect`, etc. The generic layer handles
   the common templated effects; only oddly-shaped cards get bespoke code.

Other things visible only because it's a networked client (not applicable to
our local hotseat game, and not worth copying): every state-changing action
has a paired `...ClientRpc`/`...ServerRpc` method for client/server sync.
Our engine mutates one authoritative `GameState` directly with no network
boundary, which is strictly simpler for our use case — nothing to adopt here.

One UI-level optimization is visible too: `cardPool`, `buttonPool`,
`LoadAllCardsToPool` — card and button GameObjects are **pooled and reused**,
not destroyed and recreated. `LoadAndCacheSprite`/`InvalidateCache` confirms
sprite caching (we already do this via `texCache`/`spriteCache`).

### What this suggests for optimizing our implementation

- **Biggest one: `GameManager.Render()` tears down and rebuilds the entire
  UI on every single state change** (`Clear(boardRoot); Clear(sideRoot);`
  then re-`Instantiate`/`AddComponent` everything from scratch). At 34 cards
  this is invisible, but it's real GC churn per action and the architecture
  doesn't scale. OPTCGSim's `cardPool`/`buttonPool` pattern is the fix if
  this ever becomes a problem: pool the card/button GameObjects and only
  update their data/position instead of destroy+recreate. **Not worth doing
  now** — premature for the current scale — but worth knowing the fix if
  performance or flicker becomes noticeable.
- **Replace repeated `ContainsAll(text, ...)` string scanning with a
  precomputed per-card effect descriptor**, built once (e.g. at
  `CardData` load time) instead of re-pattern-matching the printed sentence
  every time an effect tries to resolve. This is the real lesson from their
  `Action*` flags model: classify each card's effect into structured data
  once, then have the generic resolver branch on that data instead of on
  text. It's both faster (no repeated string search) and more reliable —
  the Straw Sword bug happened specifically because there was no entry for
  its phrasing to match against; a descriptor table makes "this card has no
  entry yet" a visible gap instead of a silent no-op.
- Our once-per-turn tracking (`AbilityUsedThisTurn`) and DON!!xN table
  (`DonPowerBonuses`) already match their pattern — no changes needed there,
  just keep extending those tables as more cards get implemented.
- Texture/sprite caching is already in place and matches their approach —
  no action needed.

## Next up

Only the 3 cards listed above as remaining gaps. In priority order:

1. Bonney's look-top-5 / reveal-{Supernovas} / reorder-to-bottom UI (biggest lift, most reusable for future cards with similar deck-peek text).
2. Straw Sword's trigger — playing a card from hand for free (cost ≤2, no DON payment).
3. Kid Leader's optional "you may trash 1 card from hand" cost gating the set-active effect.
