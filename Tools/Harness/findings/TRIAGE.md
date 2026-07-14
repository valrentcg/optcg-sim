# Findings Triage

Hand-authored triage of the auto-generated reports (`effect-coverage.md`, `invariant-violations.md`).
Updated 2026-07-13. The `.md`/`.tsv` reports are regenerated each run; this file is the durable record.

## Bugs found + FIXED — "Draw N and trash/place M from your hand" dropped-clause class (Session 14)

Reported by playtest: **ST25-003 Crocodile & Mihawk** — "[On Play] Draw 2 cards and trash 1 card
from your hand. Then, play up to 1 {Cross Guild} … from your hand." In game, the player drew 2,
then the *first* prompt was the "play a card" step — clicking it PLAYED the card they meant to
trash. Root cause: the generic Draw handler matches any text with "Draw"+"card" and returns
`Resolved`, silently DROPPING a conjoined "and trash/place M from your hand" clause. The `. Then,`
clone path can't own it either — it re-runs partA on selection re-entry, which would draw twice.

Fixes (all in `GameEngine.TryResolveKnownEffect` / `IsAutomatedEffectPattern`):
1. **Draw-prefix fire-once handler** (before the `. Then,` split): resolve "Draw N cards and
   <rest>" by drawing N, STRIPPING the draw from the effect's own `Text` (persists, so it never
   re-draws), and recursing — the remainder then resolves as its OWN ordered prompt (the
   trash/place button, before any "Then,").
2. **Standalone recognition** in `IsAutomatedEffectPattern` for plain `Trash N cards from your
   hand` and `Place N cards from your hand at the top/bottom of your deck`, so the `. Then,` split
   separates trash/place from a trailing "play"/etc. instead of the greedy play handler swallowing it.
3. **New resolver** for `Place N cards from your hand at the top/bottom of your deck [in any order]`
   (~15 cards, had NO resolver — silently dropped). Default: top→top, bottom/"top or bottom"→bottom
   (documented simplification, matches the Life-scry/rearrange keep-order default).
4. **Coverage detector** (`EffectCoverage.ActionCues`) gained trash/place-from-hand cues so a
   dropped self-hand-disposal clause can never silently regress (it was invisible before — no cue).

Impact: golden diff = **74 changed, 0 added/removed, 0 true regressions**. ~90 "Draw N and trash M
from your hand" cards (e.g. OP03-044 Kaya, EB02-024 Sogeking, and the reported ST25-003) now prompt
the trash/place step IN ORDER. Test: `CrocodileMihawkDrawTrashThenPlay`. Two golden diffs were
honest RECLASSIFICATIONS, not regressions: ST06-016 White Out OK→NotAutomated (it was silently
dropping "none of your Characters can be K.O.'d this turn" — now visible), and OP11-092 Helmeppo
NotAutomated→OK (bonus fix).

### Still OPEN from this class (pre-existing, now surfaced by the hardened detector — NOT caused by the fix)
- **Board-wide K.O. immunity + trailing draw/trash** (OP04-083 Sabo, ST06-016 White Out):
  "None of your Characters can be K.O.'d … until the start of your next turn / this turn" has no
  resolver AND isn't recognized by `IsAutomatedEffectPattern`, so a following ". Then, draw N and
  trash M" doesn't split and the greedy Draw handler swallows it. Needs a turn/next-turn-scoped
  board-wide immunity flag checked in the K.O. path. ~several cards.
- **Deck-look rider trailing trash** (OP16-067 Tsuru, OP09-034 Perona, OP14-113 Marguerite,
  OP13-086, OP15-118 Enel, OP10-057 Leo): "Look at N …; … place the rest at the bottom … and
  trash 1 card from your hand." The DeckLook state machine completes the look but drops the
  trailing "and trash 1 card from your hand". Needs a stored tail-clause on `DeckLookState`
  resolved on look completion. ~6+ cards.

## Session 15 — sequential button display + harness reaches every timing + rulebooks

**Sequential action buttons (rulebook 8-3-1-1 / 8-1-3-4-4-1: effect text resolves top-to-bottom):**
the action button now shows ONLY the clause currently resolving, not the whole compound. Added
`PendingEffect.PendingContinuation`: the ". Then," split, when clause A needs picks, truncates
`Text` to clause A and stashes B; `ResolveEffect` queues B (via QueueAndAutoResolve) once A finishes.
ST25-003 now reads "trash 1 card from your hand" then "play up to 1 {Cross Guild}…" (was showing the
full "trash… Then, play…" on the trash button). NOTE: an earlier attempt that RECURSED ResolveEffect
threw "collection modified" when clause B fired a board watcher (OP07-091/OP11-118) — fixed by
queuing B instead of recursing.

**Harness now stages the previously-UNEXERCISED reactive/timed timings** (EffectCoverage.cs): added
`OnKo` (trash the staged Character → FireOnKoEffects), `OnBlock` (north attacks, south blocks with
the card), `OnOpponentAttack` (north declares an attack → defender's effect), `EndOfTurn` (south
endTurn). Also fixed DriveToQuiescence/probe prematurely NULLing a deck-look on the select→rearrange
transition (same object, changed step) — it must only bail when genuinely stuck (same step AND card
count). Exercised scenarios 2226 → 2491 (+265), unexercised 795 → 626, still 0 crash/stuck/invariant.
264/265 new-timing scenarios OK; 1 new real finding: **P-071 Marco [On K.O.] "add this Character to
hand"** unimplemented. `probe <cardId>` now also drives deck-look steps.

**Official rulebooks extracted** to `findings/rulebook_comprehensive.txt` (v1.2.0, 1/16/2026, 28pp)
and `findings/rulebook_manual.txt` (6/23/2023, 17pp) via pypdf, for deriving scenario assertions.
Key rules captured: §7 battle steps (attacker/target leaving play → skip to End of Battle; power ≥ →
win; 2+ dmg repeats Life-take), §8 resolution order (8-3-1-1 costs top-to-bottom; 8-6-1 turn player
resolves first), §9 defeat judgment, §10 keywords (Rush/Double Attack/Banish/Blocker/Trigger/
Unblockable; 10-2-1-3: [On K.O.]/"cannot be K.O.'d" apply ONLY to battle/effect K.O., NOT trash by
other methods — an engine correctness point to verify).

## Session 16 — more real dropped-clause classes + Life staging

- **Trigger "Draw N. Then, [if …] play this card/K.O." dropped the draw** (OP07-107 Franky, OP15-103
  Genbo, OP03-097, OP15-106): `TryResolveKnownTrigger`'s "Play this card" branch matched any trigger
  CONTAINING "play this card" and jumped straight to the play. Fixed: resolve a leading
  "Draw N card(s). Then, …" clause first, then continue on the remainder. 4 cards, 0 regressions.
- **P-071 Marco [On K.O.] "add this Character to hand"** implemented (self trash→hand).
- **Board-wide K.O. immunity** generalized to "by effects" / blanket "during this turn" (was only
  "by your opponent's effects"); recognized in IsAutomatedEffectPattern so OP04-083 Sabo's ". Then,
  draw 2 + trash 2" splits. Test `SaboImmunityThenDrawTrash` (§10-2-1).
- **"Trash N cards from the top of your Life cards"** (ST13-015 Luffy) implemented (Pop from top) —
  surfaced only after giving the coverage BaseState 4 Life cards (Life effects previously no-op'd on
  an empty Life area). Harness improvement: `EffectCoverage.BaseState` now seeds 4 Life + the
  select→rearrange deck-look stuck-check fix.
- Confirmed FALSE POSITIVES (gated/filter/scenario, not drops): OP08-002 Marco (needs DON!!x1
  attached), OP06-115 (needs 0 Life), OP09-105 (needs {Egghead} leader), the K.O.-with-specific-
  filter suspects (target doesn't match cost/power in the generic board), Law-class play-from-hand.
- Still OPEN: **OP15-116** "add up to 1 from the top of your DECK to the top of your Life" resolves
  as add-from-HAND (misparse) — niche event, deferred. Scenario totals: exercised 2491, OK 2489,
  NOT_AUTOMATED 2, 0 crash/stuck/invariant; suspects mostly confirmed false positives.

## Session 17 — DON-from-deck add + rulebook correctness tests

- **DON!! deck→cost "add active AND rested" dropped the active one** (EB04-031 King, OP09-061 P/B
  Luffy): "add up to 1 … set it as active, **and add up to 1 additional** DON!! card and rest it"
  must add 1 ACTIVE + 1 RESTED (2 DON). The single rested-flag handler saw "rest it" and added only
  1 rested. Fixed: parse the "set … as active" and "additional … rest it" counts separately. Test
  `DonAddActiveAndRested`.
- **§10-2-1-3 played-over/cost-trash ≠ K.O.**: `MoveToTrash` gained an `isKo` flag (default true);
  board-replace + cost-trash pass `isKo:false` so [On K.O.] doesn't wrongly fire. Test
  `PlayedOverIsNotAKo`.
- Rulebook-tagged scenario tests now cover: §10-2-1 immunity (Sabo), §10-2-1-3 (played-over),
  DON deck→cost active+rested, the draw+trash / deck-look-rider / trigger-draw sequences. 30 tests,
  0 crash/stuck/invariant across ~3300-game sweeps.

## Session 18 — whole-library CONDITION audit + DON!! xN + keyword census

**Condition-parsing gap (39 of 42 fixed).** The engine evaluates free-form English conditions via
hand-written parsers in `EvaluateCondition`; conditions appearing only on booster/promo cards outside
the tested starter decks had NO parser and FAILED SAFE (logged "Unknown condition" → treated as
not-met → the effect silently no-op'd). New `EffectCoverage` collector drives all 2636 cards and
aggregates these logs: found **42 distinct unparsed conditions**. Added parsers for 39: current-power
thresholds ("Character with N power or more"), self/opponent power, "there is a Character with a cost
of N or more", active-DON / rested-cards / given-DON counts, opponent Character counts, "less
Characters/Life than opponent", "{T} type Character[ with power/cost]", exact-zero Life/hand, face-up
Life, "second turn or later", total-Life "or more", "you only have {T}", "don't have [Name]", rested
{T}, Leader colors, bare `[Name]`/cost-char (and-split right halves), and 3 disjunctions. Golden: 63
snapshots changed (gated effects now evaluate correctly — Shachi/Penguin play each other; face-up-Life
correctly not-met in the empty-face-up scenario), **0 verdict regressions**. Then added the two per-turn trackers (Session 18b): `GameState.CharKoedThisTurn` (owner-seats whose
Character was K.O.'d, set in MoveToTrash when isKo=true) and `HighestEventCostThisTurn` (per-seat,
set on event activation in PlayCard) — both cleared in ApplyStartOfTurn. Now "opponent's Character
has been K.O.'d this turn" (OP16-100) and "activated an Event with a base cost of ≥N this turn"
(OP15-002) parse. **42 → 1 unparsed conditions.** Then (reported) FIXED the last one — OP12-034 Perona: the ＜Slash＞ attribute icon had been stripped
from official-card-library.json (leaving "the attribute" / "attribute card"). Restored ＜Slash＞ in
the JSON (all 3 printings) and added an EvaluateCondition parser for "Leader has the ＜X＞ attribute"
(reads X; falls back to the source card's OWN attribute when the icon is stripped, which is correct
for the other such cards where referenced == own). Perona is the one card where referenced (Slash) ≠
own (Special), so it needed the data. Test `PeronaSlashAttributeCondition`. **UNKNOWN CONDITIONS: 0
across all 2636 cards.** RESIDUAL DATA NOTE: ~21 cards had ＜X＞ icons stripped from effect TEXT; the
engine's K.O.-immunity heuristic (X = holder's own attribute) covers the combat-immunity ones, and
the condition parser covers the "Leader has the ＜X＞ attribute" ones; a few reveal/target FILTERS
("reveal ＜X＞ attribute card") may still under-filter where referenced ≠ own — lower severity (they
don't gate the whole effect). Full data restoration would need the official text per card.

### Session 18c — attribute-icon (＜X＞) data restoration, VALIDATED vs official cardlist
User pushed to validate against https://asia-en.onepiece-cardgame.com/cardlist/ — which overturned
the earlier "referenced attribute = card's own" assumption. Via limitlesstcg.com (mirrors official
text), verified all 18 stripped-attribute cards + Perona. FINDING: **5 cards reference a DIFFERENT
attribute than their own** — Perona (Special→Slash), OP04-042 & OP12-021 Ipponmatsu, OP12-028 Hiyori,
ST12-007 Rika (all Wisdom→Slash Slash-archetype support). The engine's own-attribute fallback was
WRONG for these (e.g., OP12-021's "Leader has ＜Slash＞" would never fire under a Zoro/Slash leader).
Referenced attr per card: Slash for all except OP13-025 Koby (Strike), P-054 Garp (Strike), P-025
Smoker (Special). RESTORED the ＜X＞ icons in official-card-library.json for all 19 (idempotent
per-card-id script; NOTE the JSON is NOT git-tracked). A raw-substring replace had briefly mis-set
P-054 Garp to ＜Slash＞ (its effect is a substring of OP03-032's) — corrected to ＜Strike＞.
ENGINE made icon-aware: IsBattleKoImmune parses "by ＜X＞ attribute cards/Characters"/"without the
＜X＞ attribute" and matches the ATTACKER's attribute against X (falls back to holder's own when
stripped); CardPassesFeatureFilter filters targets by ＜X＞ attribute (stripping Leader-attribute
conditions first); EvaluateCondition "Leader has the ＜X＞ attribute" reads X. Tests: existing Buggy
＜Slash＞ immunity + PeronaSlashAttributeCondition still pass (34 scenarios), 0 golden regressions.
UNKNOWN CONDITIONS: 0. Lesson: never assume card text — validate against the official source.

### Session 18d — replacement effects (§8-1-3-4 "…instead"), silent gap found
Systematic census of the 72 "instead" cards: `TryRemovalReplacement` only matched the trigger "would
be removed from the field by your opponent's effect" (34 cards) — NOT "would be K.O.'d" (30 cards) —
and handled only 3 actions (rest-self / rest-opp / leader-−power), ~6 of 72 cards. Reactive, so
neither coverage nor the bot sweep flagged it (the Character just got K.O.'d — fail-safe). Extended
`TryRemovalReplacement`: trigger now matches "would be K.O.'d" OR "would be removed"; non-self cases
gained base-cost + colour filters (avoid over-protecting); added actions "trash N from your hand
instead" (7 cards) and "return N DON!! … to your DON!! deck instead" (5 cards incl. EB04-030 Kaido,
EB04-031 King). Test `KingKoReplacementReturnDon` (King survives an effect K.O. with DON, K.O.'d
without). 36 scenarios, 0 crash/stuck/invariant. REMAINING replacement ACTIONS still unimplemented
(long tail, lower priority — all fail-safe): add-from-Life-instead, place-from-trash-instead,
turn-Life-face-up-instead, give-this/-Leader −power-instead, trash-this-instead, rest-N-cards-instead,
and ~8 one-offs (EB02-030 "you win the game"-style). Extend the same handler with each action as needed.

**DON!! xN is generic (x1/x2/x3), verified.** ParseDonThreshold parses any N; keyword grants (line
~775), When-Attacking (~2247), and passive power buffs all gate on `AttachedDonIds.Count >= N`. Test
`DonX2ThresholdGating` (EB02-003, base 3000: +2000 only with ≥2 DON on opponent's turn, NOT stacked
with raw-DON power on own turn). `PassiveKeywordGrants` (Rush vs summoning sickness; [DON!! x1] Blocker).

**Full effect-tag census (2636 cards):** every ACTIVE timing is staged by the harness — On Play 858,
Trigger 515, Activate:Main 363, Main(event) 328, When Attacking 246, Counter 191, On K.O. 161, End of
Turn 50, On-Opp-Attack 48, On Block 14. PASSIVE keywords (Blocker 355, DON!! xN 221, Rush 88, Double
Attack 32, Banish 22, Unblockable 8) validated by targeted assertions + the bot sweep. `coverage` now
prints an UNKNOWN CONDITIONS section so the list stays at its floor.

## Session 19 — validated engine against the official Card Q&A (qa_*.pdf)

Downloaded the per-set Q&A PDFs (asia-en.onepiece-cardgame.com/pdf/qa_<set>.pdf; JS page not needed),
extracted ~250 rulings (op01-09 + eb01) to scratchpad `qa/all_qa.txt`, reviewed the general-mechanic
rulings against the engine. ONE clear discrepancy found + FIXED:
- **OP02-027 Inuarashi Q&A: "cannot be removed from the field by your opponent's effects" also blocks
  return-to-hand / place-on-deck / trash — not only K.O.** The bounce ("Return … to owner's hand") and
  place-at-bottom handlers weren't checking removal immunity. Added `RemovalBlocked(state, target,
  removerSeat)` — target's own "cannot be removed …" text (+If gate), removal-immunity aura, and the
  remover's "All of your opponent's Characters cannot be removed …" board (OP14-079). Also added the
  gate condition "all of your DON!! cards are rested". Test `RemovalImmunityBlocksBounce`.
Rulings VERIFIED to already match the engine: battle-K.O.-immunity is battle-ONLY not effect (OP01-024);
delayed "trash this Character at end of turn" does NOT fire if it left the field (OP03-005 — EndTurn
uses FindAnyInPlay→null); set-a-Character-active lets it attack again (OP01-003 — AttackCountThisTurn
is tracked but NOT enforced as a cap; the real gate is "a rested card cannot attack"); "up to" allows
0 (OP02-062); activate an effect even if a sub-part can't resolve, e.g. empty DON!! deck (OP01-119);
"If you have N or less Life" = CURRENT count (OP02-023); "there is a Character with cost 0" is boolean
not per-character (OP02-102). Then implemented most of the §8-1-3-4 replacement-action long-tail (35 distinct actions across 72
cards; census in the git history). Now handled in `TryRemovalReplacement`: rest-this / rest-opponent /
Leader-−power / trash-N-from-hand (incl. filtered variants, loosely) / return-N-DON / give-this|that-
−power / trash-this(-and-draw, §10-2-1-3 "not K.O.'d") / place-N-from-trash-on-deck / add-N-from-Life-
to-hand / turn-N-Life-face-up / return-self-to-hand / K.O.-self / place-N-Characters-on-deck (Gecko
Moria) / rest-N-of-your-{cards,Characters,DON!!,type,leader}. All auto-pay (survive if payable; K.O.
proceeds if not — fail-safe). REMAINING niche one-offs (each 1 card, fail-safe): power/type-filtered
hand-trash exactness, Kyros "rest Leader or 1 [Corrida Coliseum]", "[Fish-Man Island]/[Shirahoshi]
leader" rest, "add to top of Life FACE-DOWN", OP05-001 Sabo Leader power-filtered −power. 37 scenarios,
0 crash/stuck/invariant across the sweeps.

## Bugs found + FIXED this cycle (shipped in the engine, each with a Scenarios.cs test)

Found by the testing system:
- **Mulligan zone desync** (`ResolveMulligan`): cards returned to the deck on a mulligan kept
  `Zone="hand"`. Found by the Layer 1 per-command zone invariant. Fixed: reset `Zone="deck"`.
- **OP08-118 Silvers Rayleigh — dropped `Then, K.O.`**: compound `[On Play]` resolved only the
  power reduction and dropped the K.O. Found by the Layer 0 COMPOUND_SUSPECT heuristic. Fixed by
  teaching `IsAutomatedEffectPattern` the "−N power until the end of your opponent's next turn"
  wording so the `. Then,` split fires. Test: `RayleighThenKoFires`.

Reported by playtest (this cycle):
- **OP02-082 Byrnndi World — `+792000 power` (REAL card text)**: the self-buff gate capped at
  `\d{3,5}`, so a 6-digit gain fell through to manual resolution. Fixed: widened to `\d{3,7}`.
  Test: `ByrnndiSixDigitPowerGain`. (Earlier triage wrongly called this a data anomaly — it is not.)
- **ST25-002 Cabaji — `[Opponent's Turn] This Character gains +5000` leaked board-wide**: the aura
  scan applied a `This Character gains` self-buff to every OTHER Character (and not to Cabaji).
  Fixed: self-scoped lines buff only their own source. Test: `CabajiSelfBuffScope`.
- **OP09-061 P/B Luffy — DON-return trigger never fired**: `NotifyDonReturned` only matched the
  singular "When a DON!! card … is returned" wording and passed no count, so "When **2 or more**
  DON!! cards … are returned" never triggered. Fixed: pass the returned count; match threshold
  wordings and gate on `count >= N`. Test: `PbLuffyDonReturnThreshold`.
- **P-084 Buggy — `all Characters with a cost of 3 or 4 cannot attack` unimplemented**: the
  continuous board aura was not enforced at all. Fixed: `DeclareAttack` now scans both boards for
  the aura, evaluates its `If your Leader is [Buggy]` gate, and blocks matching-cost attackers on
  BOTH sides ("all Characters" = both players). Test: `BuggyCannotAttackAura`. NOTE: the friend's
  original report (Usopp attacked under a P/B Luffy leader) was actually correct — but the real
  scenario (opponent bot HAS a Buggy leader) was a genuine miss, now fixed.

## Still OPEN — needs the Unity client (engine is fine)

- **Play a 6th Character over an existing one on a full board**: the ENGINE supports it (scenario
  `SixthCharacterReplace` passes — `playCard` with `SlotIndex` on a full board trashes the occupant
  and plays over it). The GameManager client has BOTH interaction paths and they are committed since
  the initial commit: drag a hand Character onto an occupied slot (`CanAcceptCharacterDrop` allows
  occupied slots only when the board is full → `PlayCharacterInSlot`), and select a hand Character
  then click one of your board Characters (HandleCardClick ~line 7640). No code defect found by
  inspection or headless test. Likely causes: (a) the friend's build predates the feature, or
  (b) discoverability — when the board is full and a Character is selected, NO slot shows a drop
  hint (placement indicators only render on empty slots, `AddCharacterSlot` ~8768), so there is no
  visual affordance that "drop onto an occupied slot = replace". Candidate fix (needs Unity to
  verify): show the gold placement hint on occupied slots too when the board is full and a hand
  Character is selected/dragged.

## NOT_AUTOMATED backlog (3) — niche, deferred

| Card | Notes |
|------|-------|
| ST20-003 Charlotte Brulee | `[Trigger]` dual-Life scry `Then, add this card to your hand` — the Life-scry clause resolves but doesn't chain the add-to-hand tail. Deferred (Life-scry chaining is intricate). |
| OP11-092 Helmeppo | Delayed "…place THAT Character at the bottom of the deck at the end of this turn" (2 cards) — unimplemented delayed effect. |
| OP06-083 Oars | "You may K.O. 1 of your {Thriller Bark Pirates}: this Character's effect is negated this turn" (self-negation-as-payoff, 2 cards). |

## COMPOUND_SUSPECT list (~37) — heuristic review backlog

After filtering `if`-gated clauses, the remainder is mostly condition-gated / filter-mismatch /
deck-contents false positives (the generic scenario doesn't satisfy "If you have 5 Characters",
"{ODYSSEY} type", etc.). To convert a suspect into a real test: stage the precondition and re-check,
then promote any genuine drop to a `Scenarios.cs` test + engine fix (as done for OP08-118).
