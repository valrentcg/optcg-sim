# Effect Resolver Coverage Report — 2026-07-08

Sweep of `official-card-library.json` (4,572 entries → 2,634 unique cards, 2,313 with
ability text, 3,259 ability lines) against a Python mirror of
`GameEngine.IsAutomatedEffectPattern` + the trigger resolver + printed-passive handling.

| Metric | Session start | Wave 1 | Wave 2 | Wave 3 | Wave 4 (final) |
|---|---|---|---|---|---|
| Ability lines auto-resolvable | 63.6% | 78.3% | 85.4% | 97.9% | **100%** |
| Cards with EVERY line resolvable | 55.8% | 72.5% | 81.5% | 97.2% | **100%** |

Reminder-only lines ("(This card deals 2 damage.)") are excluded as no-ops; deck-building
rules text ("Under the rules of this game …") is enforced by CardData helpers / the deck
builder, not the match engine.


## How 100% was reached (waves 3–4 additions)

**New frameworks:** unified K.O./removal resolver (multi-target, rested/Blocker filters,
static + DYNAMIC caps like "cost equal to the number of your opponent's Life cards",
total-power budgets), base-power overrides (`BasePowerOverrides` — "base power becomes N",
copy-opponent's-Leader, swaps, turn-scoped auras), timed power bonuses
(`TimedPowerBonuses` — "until the start of your next turn / end of opponent's next turn"),
effect negation (`effectsNegated` modifier + continuous negation auras + [On Play]
suppression), removal-replacement effects ("if this Character would be removed … instead"),
generic cost engine (auto-payable costs: self-rest, rest/return DON!!, trash self, return
trash to deck, Life face-up flips; pick-based costs: hand/trash/board sacrifices; Life
top-or-bottom choices), deck-play modes (play from deck/look, play rested, trash-selected
looks, place-on-top rearrange), reveal-top auto-resolution, Life-zone moves (field↔Life,
hand→Life, face-up tracking), continuous auras (power/cost/counter/keyword, color + base-
power/cost + multi-name filters, "for every X" scaling), reactive hooks (event tax,
DON-return triggers, hand-trash negation, removal reactions, end-of-battle effects,
K.O.-reaction lines), opponent-made choices, attack-target redirection, printed passives
(cannot attack variants, played-rested, Rush-vs-characters, K.O./rest/removal immunities).

## Known simplifications (all logged in the combat log when they occur)

1. Opponent auto-choices: forced discards/reveals take the LAST card of the hand instead of
   letting the opponent pick (hotseat-friendly default).
2. "Look at Life and rearrange" effects reveal the cards in the log but keep their order.
3. "Next time you play [X] …" discounts are applied as a this-turn cost reduction on the
   matching hand cards.
4. Multi-cost picks pay costs the moment you click (no refunds after partial payment).
5. Field→Life placements always go to the TOP of the Life pile.
6. A handful of interrupt-window effects (e.g. OP12-081's "can be activated when your
   opponent plays a Character") resolve on your own turn instead of as true interrupts.
7. DON-give "each" distributions and given-DON moves pull donors automatically
   (leader first) rather than asking which DON to move.

These are the right places to revisit once an interrupt/priority window system exists.


## Verification status (Session 13)
Headless fuzz harness (Mono): 316+ full games across every starter deck and random decks
drawn from the whole library — 0 exceptions, 0 deadlocks, 2 known manual-resolution
fallbacks. Remaining known simplifications are listed above; remaining reactive-window
conditions ("if this Character would be K.O.'d…" as an If-clause outside a removal event)
correctly evaluate as not-met and log.