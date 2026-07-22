# v1.0.18 — Card-effect correctness + smarter bots

A **big** release. The headline is a full second-pass sweep of the effect engine that caught a large
class of bugs where a card *looked* like it resolved but quietly did the **wrong** thing — buffs that
never applied, conditions that were ignored, costs that were skipped, immunities that lasted forever,
"you may" downsides you could skip for free, and abilities that were silently dead. On top of that: an
AI overhaul with a smooth difficulty curve, a friends blocked-list, and board/UI polish. Below is the
complete list, grouped by area.

## Headline fixes
- **Sentomaru** no longer instantly loses you the game when its effect searches your deck — this was hitting **every** "search your deck" card.
- **Roronoa Zoro leader (OP12-020)** now correctly re-stands after it battles a Character, and its "can't attack small Characters" drawback no longer gets misapplied to your **opponent's** card.
- **Trafalgar Law (ST17-002)** now resolves its second return when your Leader is a Seven Warlord.
- **Luffy & Ace leader (ST30-001)** no longer keeps a permanent **+3000** — the buff is correctly limited to your opponent's turn.
- **Crocodile (ST17-001)** now draws 2 and places a card after revealing a Warlord.
- **Marshall.D.Teach (ST17-005)** can now actually place a card on top of your deck.
- **Roronoa Zoro (OP07-034)** auto-gains **+2000** on attack instead of making you select it.
- **Power Mochi** now grants **+2000**, not +4000.
- **Doflamingo (OP01-060 / OP07-048)** — "you **may** play" the revealed card is now genuinely optional.

## Battle, K.O. & keyword interactions
- **Dosun (OP06-030)** could become **permanently unkillable in battle** — its "cannot be K.O.'d in battle" is now correctly limited to when it attacks, and its +2000 power now actually applies.
- A **negated Character** now correctly loses its keyword abilities — **[Double Attack]**, **[Banish]**, and **[Rush]** no longer keep working after the card's effects are negated.
- **ST13-003's** "face-up Life cards go to the bottom of your deck instead of your hand" now applies on normal **battle damage**, not just effect damage.
- Battle-K.O. immunity granted by a leader/aura condition, and by attacker attribute ("cannot be K.O.'d by ＜Strike＞…"), is now gated on its actual condition.

## Removal & "…instead" replacement effects
- "If this would be K.O.'d, you may **&lt;pay X&gt;** instead" protections now fire on a normal **battle K.O.** too, not only effect K.O.s — **Morley (OP16-033)** and the whole "rest N of your cards instead" family (OP14-029, OP15-035) now work in battle.
- Those protections also now fire when the opponent **bounces**, **places on deck**, or moves your Character to **Life** — previously only a literal "K.O." triggered them.
- A **"cannot be K.O.'d" aura** no longer wrongly blocks **bounce / trash / deck-placement** (those aren't K.O.s per the rules).
- **EB02-030 (Counter)** — "if any of your Characters would be K.O.'d in battle this turn, trash 1 from hand instead" — was completely dead and now works.
- **"Trash up to 1 of your opponent's Characters"** fixed four ways (power filter, K.O.-vs-trash handling, immunity, replacement); a partial-payment leak in "rest N instead" costs was plugged.
- **Stage-based [On K.O.]** removal (Miss Merry Christmas OP14-088) was unimplemented and now resolves.

## Base power & "becomes" effects
- **Vista (OP14-053)** — "[Opponent's Turn] this Character's base power becomes your Leader's" — was dead; now applies.
- Board Characters that set your **Leader's base power** ("becomes 7000", "becomes the opponent's Leader's") now apply correctly — **EB04-003**, **EB04-052 (Sanji)**, **Zeff (EB04-004)**, and **OP15-092's** ≥20-trash "Leader becomes 7000 on defense" bullet (which was leaving your Leader at 5000).
- "Base power becomes N" buffs that require a manual step now apply automatically when you attack instead of getting stuck.

## Counters
- **OP08-096 (Counter)** — "+5000 **if** the trashed card costs 6+" now actually mills the card and only grants the boost when it qualifies (it was always applying, and never milling).
- Conditional counters like **Gum-Gum Giant (OP09-078)** now honor their condition ("if your Leader has {Straw Hat Crew}…") instead of always granting the power.
- **Union Armada (ST05-017)** now also grants its "that Character cannot be K.O.'d this turn" rider to the card it saves.

## Mandatory downsides you could skip for free
- A **mandatory "trash N cards from your hand"** (e.g. "draw 1 and trash 1") can no longer be **skipped** — that was free card advantage.
- A **mandatory self-sacrifice** rider ("…then K.O. 1 of your own Characters", Orlumbus OP04-079) can no longer be skipped either.

## Costs, DON!! & payment
- A whole class of activation costs that were being **skipped (paid for free)** now actually cost you: turning Life cards face-up/down, giving your Leader −power, DON!! −N returns, self-trash, and more.
- Continuous **cost-reduction auras** on other cards (e.g. Sabo OP01-067 "give blue Events in your hand −1 cost") now apply.
- Big DON!!-cost plays (Doflamingo OP05-119 "DON!! −10: place all your Characters…") now resolve.

## Targeting, conditions & filters
- **Dual-type "{A} or {B}"** effects no longer ignore the second type — deck-look reveals, conditions ("if your Leader has {Fish-Man} **or** {East Blue}…", "if you have N {Amazon Lily} **or** {Kuja Pirates}…"), and give-DON targeting all now match either type.
- Removal/rest/give-power targeting now enforces the card's real filters — **base power**, **exact cost**, **color**, name, and "**other than [Name]**" — across K.O., rest, bounce, deck-place, and give-power effects (previously several ignored one or more filters and could hit any card).
- Compound conditions joined by "and" ("has {type} **and** is active") no longer fail closed and kill the ability (Sai OP06-088, OP09-017 [Rush]).
- Play-from-hand / play-from-trash / hand-to-Life tutors now enforce name, cost-range, color, power, "other than", and [Trigger] filters consistently (a ~60-card reveal-add family, a ~40-card named-play family, and more).

## Reactive & "when …" abilities that were dead or mis-firing
- Reactives that only fired on the *normal* version of an action now also fire on the **effect-driven** version — "**when you play a Character**" (now fires on effect-plays), "**when … is given a DON!!**" (OP02-002), "**when your opponent plays a Character**" (Sugar OP04-024).
- Several **completely dead** leader/character reactives now work: **Nami (OP11-040 / OP11-041)**, **OP16-041**, **PRB02-009**, **OP07-038** (was drawing without its "≤5 hand" gate), **Doflamingo (OP10-042)** ("Dressrosa removed **or K.O.'d** then draw" was broken by the wording).
- A "trailing **if**" on a draw ("draw 1 card **if** you have ≤N cards") was ignored and drew unconditionally — now gated.
- Various "when this becomes rested / when a card leaves your Life / when you draw off-turn / when a card is added from Life" reactives now resolve.

## Timing & end-of-turn
- "Set … as active **at the end of this turn**" no longer fires **immediately** (that was a ramp/restand exploit) — it's now correctly delayed.
- **[End of Your Turn]** targeted restands ("set up to 1 {Egghead}/{FILM} Character as active") no longer stall unresolved — they now auto-resolve at end of turn.
- End-of-turn abilities with a leading condition ("if you have ≤3 Life…") now check that condition.

## Multi-clause, modals & specific cards
- Multi-bullet **"Choose one"** modals with a leading condition, and 3-bullet modals with a "then, draw if…" rider (Nami OP05-096), resolve fully.
- Two-part effects where a leading opponent-removal clause swallowed the appended clause now run **both** halves.
- Plus targeted fixes to many individual cards — Kuzan (OP12-040), Buffalo (OP14-070), Enel (OP05-098), Rindo (OP14-115), Kingdom Come (EB01-059), Carmel (OP04-101), Black Hole (OP09-098), Reject (OP06-116), Fire Fist (OP15-020), Cloven Rose Blizzard (EB02-007), Hody Jones (OP06-035), Zephyr (OP11-006), Shanks (OP14-027), and more.

## Complete card-effect fix list (146 fixes)

Every card-effect bug identified and fixed in this correctness sweep (engine fix IDs #109–#317). These are the individually-documented fixes; each has an automated regression test in the repo. (Earlier first-pass card fixes, #1–#108, shipped in previous releases and aren't itemized here.)

- OP16-084 Kouzuki Momonosuke (now works): TryAutoPayCost self-trash branch broadened to
- OP16-079 Yamato leader: added FireOnPlayedFromTrash dispatcher (+2 call sites in the play-from-trash
- OP16-104 Catarina Devon "[When Attacking] ... base power becomes the same as the selected Character's
- P-002 "I Smell Adventure" (+ OP04-048): "Return all cards in your hand to your deck and shuffle. Then,
- P-007 Luffy "[DON!! x1] cannot be K.O.'d in battle by <Strike> attribute Leaders or Characters": the
- P-024 "I'm Gonna Be King of the Pirates!!": "Your Leader gains +1000 power for each of your Characters
- P-046 Yamato "[On Play] You may place all cards in your hand at the bottom of your deck in any order. If
- P-081 Mihawk condition "you have 3 or more blue {Cross Guild} type Characters" was logged Unknown →
- P-084 Buggy aura "If your Leader is [Buggy], all Characters with a cost of 3 or 4 cannot attack" — a
- P-090 Charlotte Smoothie dynamic cost cap "cost equal to or less than the number of DON!! cards on your
- PRB02-010 Pudding "play up to 1 {Big Mom Pirates} … with 6000 to 8000 power from your hand" — the
- PRB01-001 Sanji leader "Up to 1 of your Characters WITHOUT an [On Play] effect and with a cost of 8 or
- play-from-trash RESTED (16 cards incl. PRB02-013, OP09-085, OP03-013…) — both play-from-trash resolvers
- PRB02-017 Hancock attack-lock "your opponent's RESTED Leader or up to 1 of your opponent's Characters …
- PRB02-018 Ace dual-zone play "play up to 1 [Sabo], [Portgas.D.Ace], or [Monkey.D.Luffy] with a cost of 2
- ST03-007 Sentomaru "Play up to 1 [Pacifista] with a cost of 4 or less from your deck" — played the WRONG
- BIG (≈40 cards) play-from-hand NAMED filter — "Play up to N [Name] … from your hand" (ST04-002 [Page
- ST05-017 Union Armada "Up to 1 of your {FILM} … gains +4000 during this battle. If that card is a
- ST05-010 Zephyr "When this Character battles <Strike> attribute Characters, this Character gains +3000
- ST07-010 Charlotte Linlin "[On Play] Your opponent chooses one: - Trash 1 card from the top of your
- ST07-011 Zeus + ST07-013 Prometheus "Up to 1 of your [Charlotte Linlin] cards gains [Banish]/[Double
- play-from-hand COLOR filter (7 cards: ST09-008 yellow {Land of Wano}, OP02-030 green, OP02-051 blue,
- ST10-006 Luffy + OP06-048 "[Once Per Turn] When your opponent activates a [Blocker], K.O. up to 1 of your
- play-from-HAND RESTED (10 cards: ST12-003, EB01-042, EB02-028, EB03-005, OP05-091, OP07-025, OP07-049,
- ST11-005 "I'm invincible" "Set up to 1 of your [Uta] Leader as active" — the generic set-active resolver
- ST13-006 Curly.Dadan "Play up to 1 EACH of [Sabo], [Portgas.D.Ace], and [Monkey.D.Luffy] with a cost of 2
- ST13-013 Garp + ST13-019 "reveal up to 1 [Sabo], [Portgas.D.Ace], or [Monkey.D.Luffy] … add to hand" —
- ST14-004 Jinbe + ST14-008 Haredas + ST14-011 Heracles "Up to 1 of your BLACK {Straw Hat Crew} type
- ST15-003 Kingdew "[Opponent's Turn] When this Character is K.O.'d BY AN EFFECT, up to 1 of your Leader
- ST16-005 Luffy "If you have a rested [Uta], this Character gains +1000" — the condition "you have a rested
- ST20-005 Charlotte Linlin "Your opponent chooses one: • opponent TRASHES 2 from their hand • Trash 1 from
- reveal-conditional "type includes" payoff (ST22-003 Newgate + ST22-006 Jozu + ST22-007 Squard) — "Reveal 1
- base-cost Character-count condition (ST25-002 Cabaji + ST25-001 Alvida + ST25-005 Mohji) — "If you have 2
- ST28-002 Izo "Your {Land of Wano} type Leader gains [Banish]" granted Banish to ANY leader — the resolver
- ST29-002 Usopp "Rest up to 1 opp Character with a cost equal to or less than the number of your opponent's
- ST26-001 Soba Mask "[On Play] Return all of your [San-Gorou] and [Sanji] Characters to the owner's hand" —
- reveal-add EXACT power (ST30-002 Inazuma + ST30-017) — "reveal … Character card with 6000 power" (exact)
- named-Leader keyword grant (ST29-016) — "Your [Monkey.D.Luffy] Leader gains [Unblockable]" granted to any
- DON-give base-power filter (ST30-014 Mr.3) — "Give up to 2 Characters with 6000 base power up to 2 DON
- OP01-047 Law "You may return 1 Character to your hand: Play up to 1 Character with a cost of 3 or less
- OP06-096 "Your Characters with a cost of 7 or less cannot be K.O.'d IN BATTLE during this turn" — added a
- OP16-021 Moby Dick "[Activate: Main] You may trash this Stage: Give up to 1 rested DON!! card to your
- ST13-009 Shanks "[On Play] You may turn 1 of your face-up Life cards face-down: If your opponent has 7 or
- OP06-083 Oars "This Character's effect is negated during this turn" (self-negate — lets Oars bypass its own
- OP07-103 Chopper + ST20-003 Brulee "…Then, add this card to your hand" ([Trigger] remainder) — the battle
- OP09-052 was INERT — a UNIQUE pre-armed reactive "[Opponent's Turn] You may trash 1 card from your hand:
- trailing "Draw N card(s) if <condition>" drew UNCONDITIONALLY — the plain draw resolver (L12908) never
- (same #210 class — a condition ignored on a verb): the [End of Your Turn] "set this…as active" fast-path
- OP01-067 Sabo "[DON!! x1] Give blue Events in your hand −1 cost" — a CONTINUOUS cost-reduction aura on OTHER
- the "Look at N; reveal up to 1 card with a cost of <X> and add it to your hand" reveal-add handler (L13129)
- reveal-add "reveal up to 1 {tag} type card OTHER THAN [Name] and add it to hand" tutors (60 cards —
- the "other than [Name]" exclusion #214 fixed for the reveal-ADD path was ALSO dropped in the two PLAY-mode
- the "other than [Name]" exclusion + cost-range gaps reached the TRASH resolvers too.
- ST13-001 "[Activate: Main] You may add 1 of your Characters with a cost of 3 or more and 7000 power or more
- the hand→Life handler (L10160, "Add up to 1 [{T} type][Character] card [with a cost of N] from your hand to
- "You may turn N cards from the top of your Life cards face-up/down: <payoff>" cost (TryAutoPayCost L6558)
- (found via the shift to per-card deep-traces of complex cards): OP15-118 / OP15-060 "If you have 6 or less
- OP16-073 "[End of Your Turn] DON!! −2: Set this Character as active. Then, this Character gains [Blocker]
- the "You may give your active Leader −N power during this turn: <payoff>" self-debuff cost (~7 cards —
- OP15-092 Luffy "Apply each of the following effects based on the number of cards in your trash: • If there
- OP12-040 Kuzan: reactive "When a card is trashed from your hand by your {Navy} type card's effect, draw
- OP02-002 payoff "give up to 1 of your opponent's Characters WITH A COST OF 7 OR LESS −1 cost during this
- OP16-033 (also OP14-029, OP15-035): self/other K.O.-replacement "you may rest N of your CARDS instead."
- (same handler, whole "rest N of your X instead" family): PARTIAL-PAYMENT LEAK. The old code rested cards
- OP16-032 "[On Play] Up to 1 of your opponent's Characters OTHER THAN [Monkey.D.Luffy] cannot be rested
- OP04-119 aura "[Opponent's Turn] If this Character is rested, your active Characters with a base cost of 5
- (real bug, found tracing OP06-117 The Ark Maxim): "[Activate: Main][Once Per Turn] You may rest this card
- OP08-036 Electrical Luna (Event): "[Main] All of your opponent's rested Characters with a cost of 7 or less
- OP04-090 Monkey.D.Luffy "[Activate: Main][Once Per Turn] You may return 7 cards from your trash to the
- (real gap, found in the sweep): OP05-119 Doflamingo "[On Play] DON!! −10: Place all of your Characters
- OP09-093 Marshall.D.Teach clause 2: "negate the effect of up to 1 of your opponent's Characters AND that
- OP15-025 Kuro "[On Play] Give up to 2 DON!! cards from your opponent's cost area to 1 of your opponent's
- ST27-001 Avalo Pizarro "[Activate: Main][Once Per Turn] You may rest 1 of your [Fullalead] cards: If your
- LEADING-CONDITION modals (OP12-060 multicolored, OP15-054/ST11-003 Leader-name, OP06-065 DON-count,
- OP05-096 Nami 3-BULLET modal "Choose one: •K.O. •Return •Place-at-Life. Then, draw if {Celestial Dragons}"
- OP06-116 Reject bullet B "If your opponent has 1 Life card, deal 1 damage to your opponent. Then, add 1
- OP14-070 Buffalo "When this Character becomes rested BY YOUR OPPONENT'S CHARACTER'S EFFECT, you may return
- OP05-098 Enel (Leader) "[Opponent's Turn][Once Per Turn] When your number of Life cards becomes 0, add 1
- OP14-115 Rindo "[Opponent's Turn][On K.O.] Add up to 1 card from the top of your deck to the top of your
- OP05-053 Mozambia "[Your Turn][Once Per Turn] When you draw a card outside of your Draw Phase, this
- OP05-107 Lt. Spacey "[Your Turn][Once Per Turn] When a card is added to your hand from your Life, this
- EB02-023 Crocodile "[Your Turn][Once Per Turn] When your opponent's Character is returned to the owner's
- OP03-001 Portgas.D.Ace (Leader) "When this Leader attacks or is attacked, you may trash any number of Event
- OP06-048 Zeff "[Your Turn] When your opponent activates [Blocker] OR AN EVENT, if your Leader has the
- OP08-056 Moby Dick + OP13-078 Oro Jackson (both Stages): "[timing] When your Character with a type
- OP09-080 Thousand Sunny (Stage) "[Opponent's Turn] You may rest this Stage: When your {Straw Hat Crew}
- OP05-040 Birdcage (Stage) passive "If your Leader is [Donquixote Doflamingo], all Characters with a cost
- OP02-048 Land of Wano (Stage) "[Activate: Main] You may trash 1 {Land of Wano} type card from your hand and
- OP01-063 Arlong "…Choose 1 card from your opponent's hand; your opponent reveals that card. If the revealed
- OP04-040 Queen "[DON!! x1] [When Attacking] If you have a total of 4 or less cards in your Life area and
- EB03-025 Hina / EB03-027 Marguerite / OP14-058 Ocean Current Shoulder Throw / OP11-051 Sanji — "Return up to
- "K.O. up to N of your opponent's Characters with N BASE power or less" — EB01-010, EB04-033, OP04-003 Usopp,
- OP14-027 Shanks / OP14-038 — "Rest up to 1 of your opponent's Characters with 7000 base power or less." The
- OP11-006 Zephyr "[DON!! x1] [When Attacking] Give up to 1 of your opponent's <Special> attribute Characters
- "Place up to 1 of your OPPONENT's Characters with N power or less at the bottom of the owner's deck" —
- OP06-035 Hody Jones "[On Play] Rest up to a total of 2 of your opponent's Characters or DON!! cards. Then,
- EB02-007 Cloven Rose Blizzard "[Main] Up to a total of 3 of your Leader and Character cards gain +1000 power
- OP15-020 Fire Fist "[Main] Your Leader gains +3000 power during this turn AND give up to 1 of your
- A family of ". Then,"-less compounds where a leading opponent-removal/debuff clause and an appended
- OP09-098 Black Hole "[Main] If your Leader has the {Blackbeard Pirates} type, negate the effect of up to 1
- EB01-030 Loguetown (Stage) "[Activate: Main] You may place this card and 1 card from your hand at the bottom
- OP02-030 Kouzuki Oden "[On K.O.] Play up to 1 GREEN {Land of Wano} type Character card with a cost of 3 from
- The play-from-trash resolver (L14440) enforced name/cost-cap/power/feature/other-than/[Trigger] filters but
- The play-from-HAND resolver (L13676) checked "cost of N or less" (≤ cap) and "cost of N or more" (≥ min) but
- REST exact-cost: OP14-090 Mr.1 "Rest up to 1 of your opponent's Characters with a cost of 0." The rest-
- STAGE-only K.O. UNIMPLEMENTED: OP14-088 Miss.MerryChristmas "[On K.O.] …K.O. up to 1 of your opponent's
- The give-+power buff resolver (L13393) validated seat/type/name/feature but NOT the COLOUR, COST, or BASE-
- EB01-059 Kingdom Come "[Main] K.O. up to 1 of your opponent's Characters. Then, trash cards from the top of
- OP04-101 Carmel "[Trigger] Play this card. Then, K.O. up to 1 of your opponent's Characters with a cost of 2
- OP06-088 Sai — compound "{type} AND is active" leader-aura failed-CLOSED (blind AND-split)
- OP09-017 — same "blind AND-split fail-closed" class (elided-subject compound, [Rush] dead)
- Removal-replacement protections DID NOT fire on opponent BOUNCE / PLACE-ON-DECK (only K.O.)
- Removal-replacement missing on the field→Life "Place opponent's Character" path (extends #283)
- "Trash up to 1 of your opponent's Characters" handler — 4 bugs (power filter, isKo, immunity, replacement)
- K.O.-ONLY immunity AURAS wrongly blocked bounce/trash/deck-place (rule 10-2-1-3)
- ST13-003 "face-up Life → deck bottom instead of hand" not applied on NORMAL battle damage
- A NEGATED attacker still applied [Double Attack] / [Banish] (keyword effects survive negation)
- A NEGATED Character kept printed [Rush] (completes the keyword-negation sweep from #288)
- Rest-opponent handlers ignored PRINTED "cannot be rested by opponent's effects" immunity (only the modifier)
- "Set this Character or up to N DON as active" self-branch ignored freeze (OP13-035)
- "When you play a Character" reactive didn't fire on an EFFECT-play from hand (only normal hand-plays)
- "When ... is given a DON!! card" reactive (OP02-002) didn't fire on EFFECT-driven give-DON (same class as #292)
- "Set … as active AT THE END OF THIS TURN" fired IMMEDIATELY (ramp/restand exploit) — now delayed
- OP11-040 leader's BARE-TEXT "at the start of your turn" ability never fired (only the [tag] was matched)
- OP11-041 Nami leader's "when a card is removed from your/opponent's Life" draw was DEAD (regex mismatch)
- Two more DEAD bare-text reactives (phrasing-variant regex mismatch) — OP16-041 + PRB02-009
- "When your opponent plays a Character" reactive (OP04-024 Sugar) missed EFFECT-plays (#292 symmetry)
- OP07-038 leader's "removed by your effect" reactive DROPPED its "≤5 hand" gate → drew unconditionally
- OP10-042 Doflamingo leader's "Dressrosa removed OR K.O.'d → draw" was DEAD ("or K.O.'d" broke the regex)
- OP15-092's ≥20 "your Leader's base power becomes 7000" bullet was DEAD (Leader stayed 5000 on defense)
- OP06-030 Dosun: [When Attacking] battle-K.O. immunity was CONTINUOUS (permanently unkillable) + +2000/immunity never granted
- removal-REPLACEMENT never fired on a normal BATTLE-damage K.O. (sweep #283-285 gap)
- mandatory self "base power becomes the same as opponent's Leader" never AUTO-resolved (stayed pending)
- OP14-053 Vista: CONTINUOUS own-Leader base-power swap was DEAD (gate-invisible passive)
- queued "Leader's base power becomes N" (fixed number) also never AUTO-resolved (extends #304)
- EB04-003: CONTINUOUS board-Character-sets-owner's-Leader base power was DEAD
- MANDATORY "trash N cards from your hand" was SKIPPABLE via passEffect → free card advantage
- MANDATORY self-CHARACTER-removal rider skippable via passEffect (extends #308 / class 4l)
- deck-look "{A} or {B} type" dual-type reveal filter collapsed to only {A} (glow≠resolve)
- dual-type "{A} or {B}" CONDITIONS fail-closed in EvaluateCondition (dual-type sweep cont.)
- give-DON "to {A} or {B} type Leader or Character" mis-routed to Leader-only + missing {type} filter (dual-type sweep close)
- OP08-096 [Counter]: "+M if trashed card cost ≥N" applied UNCONDITIONALLY + never milled
- conditional [Counter] flat power boost applied UNCONDITIONALLY (general leading-If form)
- ST05-017 [Counter]: per-card K.O.-immunity rider dropped by flat-counter application
- EB02-030 [Counter]: turn-long battle-K.O. replacement was DEAD (no mechanism)
- [End of Your Turn] targeted "set up to N {tag} Character as active" STALLED (never resolved)

## Board & UI
- **Player names show everywhere** — pills, the turn banner, and match history — instead of "South/North".
- **Status tags on your opponent's cards read right-side-up** now (they were upside-down).
- The card preview no longer shows a **redundant name** under the art.
- The **"Show Result"** button no longer sits on top of the opponent's hand.
- **"Ready — waiting for opponent"** is now readable (white), and **black decks look black** on the life bar.
- The **updater's "What's New"** now shows what actually changed in each release.

---

# Smarter bots, a difficulty curve & friends

## A smooth difficulty curve
- **Beginner, Intermediate and Advanced now step up cleanly** — all three share one strong decision core, with the lower tiers playing looser **on purpose**.
- **Beginner** still races your life so it never feels brain-dead, but it's **sloppier with DON and counters**.
- **Intermediate** plays the same brain at **full discipline**.
- **Advanced** adds **tactical search** on top.

## Smarter AI (all tiers)
- **Pressures your life** instead of making pointless trades — it treats life as the clock, the way strong players do.
- **Stops over-spending counters**: it won't burn two cards to save one, and takes small hits to keep its resources.
- **Holds big counter cards back** for defence instead of always deploying them.
- **Commits DON one attacker at a time** instead of front-loading, so less is wasted.
- **Mulligans with more nuance** — weighs going first vs second, and keeps a rough hand when it has a **searcher** to fix its curve.

## More card fixes
- Fixed a **class of two-part effects joined by "…, and …"** that were **silently dropping their second half**.
- **Punk Vise, Lim, White Snake** and **Gum-Gum Giant Sumo Slap** now resolve their **full** effects (rest + DON, DON + play, counter + draw, counter + K.O.).

## Friends
- Friend-row buttons are now **compact so the whole strip fits on screen** — the Block chip used to run off the edge.
- **Block is now a two-step confirm** (one tap arms it, "Sure?", a second tap blocks) so a stray click can't block a friend.
- New **Blocked list** — a toggle at the top of the Friends screen shows everyone you've blocked, with an **Unblock** button for each.
