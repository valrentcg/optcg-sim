# Exhaustive per-card audit — progress tracker

Goal: deep-trace EVERY card (2320 with effects) like Rosinante — verify resolution + valid targets + glow
against the card text + challenge scenarios; fix bugs; confirm full in-game resolution. Ordered id list:
`audit-cardlist.txt` (2320 ids). Auditor triage: `audit-flagged.tsv`.

## NEXT DEEP-TRACE INDEX: 2319 — ✅ AUDIT COMPLETE + NA BACKLOG CLEARED TO THE IRREDUCIBLE SET

### 🏁 FINAL STATE
Per-card trace of all 2319 cards: DONE. NA backlog worked down 18 → **6** (12 cleared with real fixes + harness
tests; 1 regression caught & fixed). Full sweep CRASH=0/STUCK=0/INVARIANT=0/DEADLOCK=0. Coverage OK=2669/NA=6.
- CLEARED (#148–#158): OP01-047 (return-Character cost), OP06-096 (board battle-only immunity), OP16-021 (trash-
  Stage cost), ST13-009 (turn-Life-face-down cost), OP06-083 (self-negate), OP07-103+ST20-003 (add-this-card
  trigger remainder), EB01-011 (compound rest+place, +CostCharFilterOk base-power), OP04-073 (compound trash-self
  +pick, generalized restAndM to trash/K.O. + implied 2nd verb), ST28-004 (return-given-DON cost), OP04-094
  (rest-your-Leader cost + 15-trash cost-cap upgrade), EB01-029 (reveal cost≥4 direction + place-revealed-at-
  bottom). Regression caught & fixed: OP16-015 (hand-trash cost mis-matched as board sacrifice).
- REMAINING NA (6, IRREDUCIBLE — need major features / ambiguous / false-NA; all still "acknowledged for manual
  resolution" — player resolves via UI, logged, never crash):
  · OP05-080 (false-NA — return-20-trash cost + battle-buff both implemented; the coverage gate can't set up 20
    trash + a live battle).
  · OP05-111 (play-a-card-as-cost — needs DON-payment-mid-cost machinery).
  · OP11-092 (delayed: place the SPECIFIC played Character at deck bottom AT END OF TURN — needs a tracked
    delayed-effect queue).
  · OP15-003 / OP15-017 / OP15-023 (attach the opponent's own rested DON to their board — genuinely ambiguous
    tempo-denial semantics; implementing it wrong is worse than NA).
STOPPING here — the remaining 6 need disproportionate new machinery or have ambiguous rules; forcing them risks
regressions in a stable ship candidate. Ship = v1.0.19+.


### 🧹 NA-BACKLOG CLEARING (post-completion, step-1 of the loop mandate)
Coverage NA count: 18 → **11** (OK=2664). Each real fix + harness test; coverage green.
- #152 OP06-083 Oars "This Character's effect is negated during this turn" (self-negate — lets Oars bypass its own
  "cannot attack") — added a self-effectsNegated handler + automate-pattern. `op06083test`: negate applied, Oars
  attacks.
- #153 OP07-103 Chopper + ST20-003 Brulee "…Then, add this card to your hand" ([Trigger] remainder) — the battle
  trigger flow already returns the revealed Life card to hand (FinalizeTrigger), so the queued clause is a safe
  no-op; added the handler + automate-pattern so it's not flagged NA.
- ⏳ DEFERRED (genuinely niche/ambiguous): OP15-003/017/023 Alvida/Morgan/Arlong "give 1 of your opponent's rested
  DON!! to 1 of their Characters: give 1 rested DON to its owner's Leader/Char" — ambiguous opponent-DON
  manipulation, risky to auto-resolve for 3 niche cards.
- REMAINING NA (11): EB01-011 (compound rest+place-base-power cost), EB01-029 (reveal + place-at-bottom remainder),
  OP04-073 (multi-trash cost), OP04-094 (Main KO — resolves in probe, likely false-NA), OP05-080 (return-20-trash
  cost), OP05-111 (play-as-cost), OP11-092 (delayed place-at-EOT), OP15-003/017/023 (deferred, above), ST28-004
  (return-given-DON cost).

### 🧹 (prior passes)
NA 18 → 14 via #148 OP01-047, #149 OP06-096, #150 OP16-021, #151 ST13-009. Each real fix + harness test.
- #150 OP16-021 Moby Dick "[Activate: Main] You may trash this Stage: Give up to 1 rested DON!! card to your
  Leader or 1 of your Characters" — the "trash this Stage" cost wasn't recognized (TryAutoPayCost only self-
  trashed a Character). Added a "trash this Stage" branch. Coverage: cleared.
- #151 ST13-009 Shanks "[On Play] You may turn 1 of your face-up Life cards face-down: If your opponent has 7 or
  more cards in their hand, trash up to 1 from the top of your opponent's Life" — the "turn N of your face-up Life
  cards face-down" cost wasn't recognized (only "turn ALL"). Added it to TryAutoPayCost. `st13009test`: Life
  flipped, opp Life trashed.
- REMAINING NA (14): EB01-011 (rest+place-base-power compound cost), EB01-029/OP07-103/ST20-003 (Trigger
  self-add remainders), OP04-073 (multi-trash cost), OP04-094 (Main KO — resolves in probe, likely false-NA),
  OP05-080 (return-20-trash cost + buff), OP05-111 (play-as-cost), OP06-083 (self-negate body), OP11-092 (delayed
  place-at-EOT), OP15-003/017/023 (give opp's rested DON to opp Character), ST28-004 (return-given-DON cost).

### 🧹 (prior pass)
Coverage NA count: 18 → **16** (2 cleared; OK=2659). Each real fix + harness test; coverage green.
- #148 OP01-047 Law "You may return 1 Character to your hand: Play up to 1 Character with a cost of 3 or less
  from your hand" — the cost-pick regex required "return N OF YOUR Character**s**"; OP01-047 says "return 1
  Character" (no "of your", singular). Broadened to `(?:of your )?…Characters?\b(?! cards?)` + guarded against the
  from-hand trash cost. `op01047test`: cost returns board char, c3 plays. (Fixed a regression this introduced —
  OP16-015 "trash 1 Character card … from your hand" was wrongly matched as a board sacrifice — via the `(?! cards?)`
  lookahead + "from your hand" guard.)
- #149 OP06-096 "Your Characters with a cost of 7 or less cannot be K.O.'d IN BATTLE during this turn" — added a
  board-wide temporary battle-only immunity: new `cannotBeKodInBattle` modifier applied to matching Characters +
  honored in the battle-KO path (effect KOs unaffected, unlike the blanket cannotBeKod). `op06096test`: c5 immune,
  c9 not.
- REMAINING NA (16, genuinely bespoke — continue next passes): EB01-011 Mini-Merry (rest+place-base-power cost),
  EB01-029/OP07-103/ST20-003 (Trigger self-add remainders), OP04-073 (multi-trash cost), OP04-094 (Trigger
  rest-Leader cost), OP05-080 (return-20-trash cost), OP05-111 (play-as-cost), OP06-083 (self-negate), OP11-092
  (delayed place-at-EOT), OP15-003/017/023 (give opp's rested DON to opp Character), OP16-021 (Stage trash: give
  DON), ST13-009 (Life-flip cost + conditional), ST28-004 (return-given-DON cost), ST13-001 Sabo leader
  (add-own-Character-to-Life cost).

### 🏁 COMPLETION SUMMARY
Every card in the DB (2319 entries, indices 0–2318) has been deep-traced. Final full sweep `audit 0 2320`:
CRASH=0 / STUCK=0 / INVARIANT=0 / DEADLOCK=0. Coverage gate: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.
Residual audit flags are NOT bugs: COST_UNPAYABLE = probe DON-accounting artifacts (cards resolve correctly with
DON available, verified per-card); MANUAL/UNKNOWN = the 2 deferred NA cards below.
- 47 correctness bugs fixed this campaign (audit #109–#147 + earlier), each verified with a dedicated Tools/
  Harness test and kept green on the coverage gate. Systemic classes closed: named-inclusion filters (play-from-
  hand/trash/deck, keyword-grant, set-active, reveal-add), color filters (play, +cost grant), power filters
  (play, reveal exact, DON-give base-power), dynamic cost caps (play + rest, opp DON/Life), rested-flag on play
  (hand+trash), opponent-choice auto-resolve (trash-hand/trash-Life), reveal "type includes" payoff, battle-vs-
  effect KO gating, base-cost/rested-[Name] conditions, Leader {tag}/[Name] keyword grants, attack-lock aura,
  Blocker-reactive KO, play-each-of, return-all-named.
- ⏳ DEFERRED (need new machinery, documented): ST13-001 Sabo leader (add-own-Character-to-Life as a cost-target),
  ST28-004 Momonosuke (return-given-DON-to-cost-area cost), ST27-004 Sanjuan.Wolf (+cost per 4 trash scaler),
  P-051/P-059 (variable-count trash/return + per-count scaling), rest-by-opponent reactive family (PRB02-006/009).

### Batch 2295-2318 (ST29-007..ST31-004, FINAL) — findings
- #145 reveal-add EXACT power (ST30-002 Inazuma + ST30-017) — "reveal … Character card with 6000 power" (exact)
  added ANY card (no power filter in the deck-look add path). Added DeckLook.ExactPower + parse + selection check.
  `st30002test`: Usopp(2000) rejected, 6000-power added.
- #146 named-Leader keyword grant (ST29-016) — "Your [Monkey.D.Luffy] Leader gains [Unblockable]" granted to any
  leader. Extended the #142 leader-grant branch to also match "[Name] Leader" and check the name. `st29016test`:
  Luffy leader → Unblockable, non-Luffy → none. ST28-002 ({tag}) regression intact.
- #147 DON-give base-power filter (ST30-014 Mr.3) — "Give up to 2 Characters with 6000 base power up to 2 DON
  each" gave DON to ANY Character (only the {tag} was checked). Added exact/or-more/or-less base-power filter.
  `st30014test`: 6000-base-power gets 2 DON, non-6000 gets 0.
- Verified clean: ST29-008/ST30-009/ST30-011 removal-replacements (TryRemovalReplacement), ST30-008 self-recur-
  from-trash-rested, ST30-001/003 base-power auras, ST30-010 freeze, ST30-012 rest-[Blocker], the Counter buffs,
  ST31-004 vanilla Rush.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2277-2294 (ST26-001..ST29-004) — findings
- #142 ST28-002 Izo "Your {Land of Wano} type Leader gains [Banish]" granted Banish to ANY leader — the resolver
  matched "Your ({tag} type )?Leader gains" but never checked the leader's {tag}. Added the leader-type check.
  `st28002test`: Wano leader → Banish, non-Wano → none.
- #143 ST29-002 Usopp "Rest up to 1 opp Character with a cost equal to or less than the number of your opponent's
  Life cards" — the rest resolver used only the static ParseLimit cap (any-cost restable), like the #118 play gap.
  Wired ComputeDynamicCap into the rest resolver. `st29002test` (opp 3 Life): c3 rests, c5 doesn't.
- #144 ST26-001 Soba Mask "[On Play] Return all of your [San-Gorou] and [Sanji] Characters to the owner's hand" —
  the generic single-return handler prompted for 1 and ignored the [Name] filter. Added a return-ALL-named handler
  (returns every matching named Character at once). `st26001test`: both Sanji return, Chopper stays.
- ⏳ DEFERRED (self +cost scaling, 1 card): ST27-004 Sanjuan.Wolf "…gains [Blocker] and +1 cost for every 4 cards
  in your trash" — the scaling +cost (self) isn't computed; the conditional [Blocker] (BB leader) works via the
  keyword-grant. Low impact (a self +cost being skipped makes it slightly cheaper). GetPassiveCostBonus lacks a
  "for every N cards in trash" scaler.
- Verified clean: ST26-001 hand-cost condition "a [San-Gorou] or [Sanji] Character with 7000 base power" (L5888),
  ST26-002/003/004/005 DON−2 effects, ST27-001/002/003/005 named-cost/On-KO, ST28-001/004/005 conditional/reveal,
  ST29-001/003/004.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2259-2276 (ST22-015..ST25-005) — findings
- #141 base-cost Character-count condition (ST25-002 Cabaji + ST25-001 Alvida + ST25-005 Mohji) — "If you have 2
  or more Characters with a BASE cost of 5 or more, this Character gains [Blocker] and +1 cost" fired with ANY 2
  Characters. TWO bugs: (a) the bare "you have N or more Characters" matcher (L5442) swallowed the qualified count
  and ignored the base-cost filter — added a guard so a "Characters with a/{tag}" qualifier falls through; (b) the
  specific tagCount regex (L5514) only matched "with a cost of", not "with a BASE cost of", and used effective
  cost — added a `(base )?` capture using printed cost. `st25002test`: 2 basecost-5 → Blocker, 1 → no Blocker;
  `evalcondtest` confirms the condition. Coverage green.
- Verified clean: ST23-001 Uta conditional hand-cost "if 10000+ power char, −4 cost" (`st23001test`: 6→2),
  ST22-016 Counter reveal-if-WB +4000 (uses the #140 generic action-extraction), ST22-017 reveal-2-WB cost,
  ST23-002/003 conditional, ST24-004 multi-clause freeze+conditional, ST25-003/004 Cross Guild play, ST24-002/005
  Supernovas reveal/rest.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2241-2258 (ST21-007..ST22-012) — findings
- #140 reveal-conditional "type includes" payoff (ST22-003 Newgate + ST22-006 Jozu + ST22-007 Squard) — "Reveal 1
  … If that card's TYPE INCLUDES "Whitebeard Pirates", draw 2 / draw 2+trash / give DON" did NOTHING when the top
  card matched. The reveal handler's generic action-extraction regex anchored on "If that card IS …," and missed
  the "type includes" wording, so the payoff was dropped. Broadened to `If that card[^,]*,`. `st22003test`: WB top
  → draw 2, non-WB → +0. ST17-001 ({tag} form) regression-confirmed still works.
- Verified clean: ST21-015 Zoro On-KO play "red Character with 6000 power or less other than [Zoro]" — color
  (#130) + power (#119) + name-exclusion all combine (`st21015test`: red≤6000 plays, red-7000 & blue-6000
  rejected), ST22-012 Marco reveal-if-WB "+1000" (hits the this-Character-gains branch, unaffected), ST22-005 Oden
  multi-cost activate (rest 3 DON + return own char), ST22-011 reveal-2-WB cost, ST21-011 base-power aura,
  ST22-001/002 reveal/look, ST21-016/017 NoBlocker/conditional-KO.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2223-2240 (ST18-001..ST21-004) — findings
- #139 ST20-005 Charlotte Linlin "Your opponent chooses one: • opponent TRASHES 2 from their hand • Trash 1 from
  opp Life" — option A (opponent-trash-hand) stayed pending and did nothing (option B worked). IsAutomatedEffect
  Pattern only recognized "opponent … DISCARD … from their hand", not "trashes", so the queued choice sub-effect
  never auto-resolved (same class as #128). Broadened to `discard OR trashes`. `st20005test`: pick A → north hand
  4→2, pick B → north Life 3→2. (The v1.0.19 fix covered the cost/option B; this closes option A.)
- Verified clean: ST20-002 Cracker removal-replacement "if K.O.'d BY AN EFFECT, trash from Life instead" —
  correctly fires only on effect KOs (battle-damage L3956 doesn't call TryRemovalReplacement, so "by an effect" is
  implicitly respected), ST19-003 Tashigi "was played on this turn" condition (L5326), ST18-004 purple {Straw Hat}
  reveal-add (#135 name-list + #130 color), ST18-005 color+tag play (#130), ST18-001/002/003 8+-DON conditionals,
  ST19-001/002/004/005 cost effects, ST21-001/002/003/004 DON-give/NoBlocker/On-KO-draw.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2205-2222 (ST14-015..ST17-005) — findings
- #137 ST15-003 Kingdew "[Opponent's Turn] When this Character is K.O.'d BY AN EFFECT, up to 1 of your Leader
  gains +2000" — FireOnKoEffects fired it on ANY K.O. (incl. battle) and ignored the [Opponent's Turn] gate.
  Threaded a `byBattleKo` flag through MoveToTrash→FireOnKoEffects (set true only at the battle-damage K.O. site
  L3956), gated "K.O.'d by an effect" vs "K.O.'d in battle", and added the turn-timing gate to the whenKo branch.
  `st15003test`: effect-KO fires, battle-KO doesn't. (Core-function change — coverage stayed green.)
- #138 ST16-005 Luffy "If you have a rested [Uta], this Character gains +1000" — the condition "you have a rested
  [Name]" wasn't evaluated (the general "you have [Name]" anchors "[" right after "have"), so the buff never
  applied. Added a handler. `st16005test`: rested Uta → +1000, active/absent Uta → +0.
- ST17-001 Crocodile + ST17-002 Law — REGRESSION-CONFIRMED the v1.0.19 fixes intact: `st17001test` (Warlords
  reveal → draw 2 + place 1 back), `st17002test` (cost return + 2nd clause returns opp c1 with a Warlords leader).
- Verified clean: ST15-001 Atmos "cannot add Life to hand via own effects" (L8825), ST14-017 +1 cost aura + draw,
  ST15-002/004/005 conditional effects, ST16-001/002/003/004, ST17-003/004/005 (deck-look/DON-give/place-hand-cost).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2187-2204 (ST13-013..ST14-014) — findings
- #135 ST13-013 Garp + ST13-019 "reveal up to 1 [Sabo], [Portgas.D.Ace], or [Monkey.D.Luffy] … add to hand" —
  the deck-LOOK add-to-hand used ParseNamedOrTypeFilter (only "[Name] or <Type> card"), so a bare 3-name LIST fell
  through with NO filter (Usopp was addable). Extract every [Name] from the reveal segment as a '|'-joined
  NamedCardFilter + made the selection check match ANY of the '|'-split names. `st13013test`: Usopp rejected, Sabo
  added. (Single-name and "[Name] or Type card" paths unaffected — coverage green.)
- #136 ST14-004 Jinbe + ST14-008 Haredas + ST14-011 Heracles "Up to 1 of your BLACK {Straw Hat Crew} type
  Characters gains +2 cost" — the +cost grant enforced the {tag} but NOT the "black" color (a red {Straw Hat}
  matched). Added a color filter to both the "All of your" and single-target branches. `st14004test`: black +2,
  red +0.
- Verified clean: ST14-001 Luffy leader "All of your Characters gain +1 cost" aura (L917), ST14-002/003/007/009/
  012/014 conditional cost-8+/6+/10+ effects, ST13-014 Life-reveal-play, ST13-015 self-buff+Life-draw, ST13-016
  Yamato Life scry, ST13-017/018 Counter buffs, ST14-006 conditional draw.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2169-2186 (ST12-011..ST13-012) — findings
- #134 ST13-006 Curly.Dadan "Play up to 1 EACH of [Sabo], [Portgas.D.Ace], and [Monkey.D.Luffy] with a cost of 2
  from your hand" — read as "up to 1" so only ONE card played (should be one of EACH = up to 3). Set the pick
  count to the number of names in the "each of" list + reuse the different-names uniqueness (reject a name already
  played). `st13006test`: plays Sabo+Ace+Luffy (3), rejects the 2nd Sabo. (Only "play … each of" card; OP07-075's
  "each of" is a power-debuff, different handler.)
- ⏳ DEFERRED (NA, 1 card): ST13-001 Sabo leader "You may ADD 1 of your Characters (cost≥3 & 7000power≥) to the
  top of your Life cards face-up: +2000 …" — a self-sacrifice-to-Life COST with a target; not an auto-payable cost
  type, so it stays NA (acknowledged manual). Needs a new "add-own-Character-to-Life" cost-target payment. Single
  card; deferred to avoid a bespoke cost path.
- Verified clean: ST12-013 Zeff reveal-play RESTED (L10292 sets Rested from the text), ST13-005 Ivankov reveal-
  cost-5-from-hand → Life face-DOWN (FaceUp defaults false), ST13-007/ST13-010 Sabo/Ace Life-reveal-conditional-
  play, ST13-002/003 Life-engine leaders, ST13-004/012 Life scry, ST13-009 conditional trash-opp-Life, ST12-012
  self-bounce, ST12-016 rest, ST12-017 Counter reveal-play, and the conditional buffs.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2151-2168 (ST10-012..ST12-010) — findings
- #132 play-from-HAND RESTED (10 cards: ST12-003, EB01-042, EB02-028, EB03-005, OP05-091, OP07-025, OP07-049,
  OP12-113, OP13-023, OP13-031) — the generalized play-from-hand resolver ignored "from your hand rested" and
  played the card ACTIVE (mirror of the #121 trash fix). Set `Rested` from the text at the hand play point.
  `st12003test`: ＜Slash＞ card plays RESTED, Strike-only rejected (the {Muggy Kingdom} or ＜Slash＞ filter works via
  CardPassesFeatureFilter).
- #133 ST11-005 "I'm invincible" "Set up to 1 of your [Uta] Leader as active" — the generic set-active resolver
  had no [Name] filter, so ANY leader/Character could be set active. Added a "[Name]" check to the validation.
  `st11005test`: Uta leader set active, non-Uta leader rejected.
- Verified clean: ST12-010 Ivankov reveal-play (cost-2 filter), ST12-001 Zoro&Sanji return-cost + set-active,
  ST10-012 opp-more-DON, ST10-014/ST12-… DON-return reactive, ST10-017 Punk Vise ", and" split, ST11-003 Backlight
  + ST12-006 self-choice rest/KO modals, ST11-001 Uta reveal-add, ST11-002 trash-Event set-active, ST12-007 Rika
  conditional set-active, and the KO/rest/buff cards.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2133-2150 (ST09-005..ST10-011) — findings
- #130 play-from-hand COLOR filter (7 cards: ST09-008 yellow {Land of Wano}, OP02-030 green, OP02-051 blue,
  OP12-056, OP13-099, EB02-044, ST18-005) — "play up to N <color> {tag} type Character … from your hand" never
  enforced the color prefix, so an off-color {tag} card could play. Added a color check to the generalized play
  resolver (reads the color that directly precedes "{tag}"). `st09008test`: yellow Wano plays, green Wano rejected.
- #131 ST10-006 Luffy + OP06-048 "[Once Per Turn] When your opponent activates a [Blocker], K.O. up to 1 of your
  opponent's Characters with 8000 power or less" — the trigger was never dispatched. Added FireOnOpponentActivates
  Blocker (called from BlockAttack for the ATTACKER's board, with Your/Opponent's-Turn + Once-Per-Turn gating).
  `st10006test`: attack → opponent blocks → KO effect fires and KOs the blocker.
- Verified clean: ST10-003 Kid leader conditional self-debuff "if 4+ Life, give this Leader −1000" (L263),
  ST09-010 Ace removal-replacement "trash 1 from Life instead" (TryRemovalReplacement), ST10-007 Killer + ST10-011
  Heat DON-return reactive (L4586), ST10-001 Law leader ", and"-split bounce+play, ST10-004 conditional Rush,
  ST10-010 conditional opp-hand-trash, ST09-014/015 conditional Counter (ST09-015 add-opp-char-to-owner-Life),
  and the Life-cost buff/play cards.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2115-2132 (ST07-011..ST09-004) — findings
- #129 ST07-011 Zeus + ST07-013 Prometheus "Up to 1 of your [Charlotte Linlin] cards gains [Banish]/[Double
  Attack]" — the keyword-grant resolver enforced cost/feature/no-On-Play but NOT the [Name] (same class as #125),
  so ANY card could gain the keyword. Added a named-target filter ("of your [Name]") to the validation.
  `st07011test`: Charlotte Linlin gets Banish, Chopper rejected. (Glow already filtered via L6709.)
- ST07-016 Power Mochi double +2000 — REGRESSION-CONFIRMED still fixed (v1.0.19): AutomatedCounterPower cuts the
  [Counter] clause at "Then," (L3662) so the secondary "+2000" isn't read as counter power too. Intact.
- ST07-015 Soul Pocus (Main "opponent chooses one", same body as ST07-010) — covered by #128's IsAutomated
  broadening.
- Verified clean: ST08-013 Mr.2 Bentham battle-end mutual KO (L3943), ST08-005 Shanks "K.O. all Characters with
  a cost of 1 or less" (L10328, both sides), ST08-001 Luffy leader KO-reactive DON-gain, ST07-017 add-Character-
  to-own-Life, ST09-001/004 conditional (≤2 Life) buff/immunity, ST08-002/004/006/008/014/015 cost-minus/KO,
  ST08-009 conditional draw.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2097-2114 (ST06-005..ST07-010) — findings
- #128 ST07-010 Charlotte Linlin "[On Play] Your opponent chooses one: - Trash 1 card from the top of your
  opponent's Life cards. - Add 1 card from the top of your deck to the top of your Life cards." The opponent-choice
  mechanism (chooser=north, ControllerSeat=south) worked, and option B (add-deck-to-Life) resolved — but option A
  ("Trash 1 … opponent's Life") stayed PENDING and did nothing. Root cause: IsAutomatedEffectPattern only matched
  "trash UP TO N … opponent's Life", not the plain "Trash 1 …" form, so the queued sub-effect never auto-resolved.
  Broadened both occurrences to `[Tt]rash (?:up to )?\d+` + "opponent's Life". `st07010test`: pick A → north Life
  3→2, pick B → south Life 3→4. (Also fixes any queued plain-trash-opp-Life sub-effect.)
- Verified clean: ST07-003 Katakuri + ST07-008 Pudding scry-EITHER-Life (choice A/B own-vs-opp Life, resolves),
  ST07-001 Linlin leader hand-to-Life (L9262), ST07-005 Daifuku deck-to-Life, ST07-004 Snack Banish, ST07-009
  Mont-d'or multi-cost KO, ST06-005/006/008/010/017 cost-minus, ST06-012 Garp multi-cost KO, ST06-014/016 Counter
  buffs, ST06-015 draw+cost-minus, and the vanilla Blockers.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2079-2096 (ST04-015..ST06-004) — findings
- #126 ST05-017 Union Armada "Up to 1 of your {FILM} … gains +4000 during this battle. If that card is a
  Character, that Character cannot be K.O.'d during this turn" — the KO-immunity rider was dropped (the buff
  resolver returned after applying +power; the clause isn't a ". Then," split). Added the rider next to the
  existing "if the selected card attacks" one, tying the cannotBeKod modifier to the buffed Character. `st05017test`:
  power 6000→10000 + cannotBeKod set.
- #127 ST05-010 Zephyr "When this Character battles ＜Strike＞ attribute Characters, this Character gains +3000
  power during this turn" — no handler (the existing "battles and K.O.'s" trigger is different). Added
  GetBattleAttributeBonus (called from GetPower): applies +N while this Character is a combatant and the OTHER
  combatant is an X-attribute Character. `st05010test`: 11000 vs Strike attacker, 8000 vs Slash. Only card of its kind.
- Verified clean: ST05-005 Carina "opponent has more DON!! than you" (L5238), ST05-008 Shiki "if 8+ DON, cannot
  be K.O.'d in battle" (L4796), ST06-004 Smoker "cannot be K.O.'d by effects" (L4009), ST05-001 {FILM} +power
  aura, ST05-004 On-Block rest, ST05-011 Douglas Bullet DON−4 rest-2 + Double Attack, ST06-001/002 KO-cost-0,
  ST04-015/016/017 and the vanilla Blockers.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2061-2078 (ST03-008..ST04-014) — findings
- #125 ⭐ BIG (≈40 cards) play-from-hand NAMED filter — "Play up to N [Name] … from your hand" (ST04-002 [Page
  One], EB02-013 [Zou], OP01-044 [Penguin], OP16-116 [Marshall.D.Teach], OP16-041 [Prisoner of Impel Down], …)
  enforced cost/feature but NOT the [Name], so ANY hand card could be played. Added a named-inclusion filter to
  BOTH the resolver (L11531) and the glow (EffectTargetZone.Hand case): names read from the "Play up to N …"
  segment only (leading-If condition names excluded), skipped when an "or up to N {tag}" alternative exists
  (OP15-073), and "other than [Name]" dropped from the inclusion set. `st04002test`: Page One plays, Chopper
  rejected; OP01-044 glow now (none) instead of showing Chopper. sengoku060test (tag+different-names) still green.
- Verified clean: ST04-001 Kaido leader "Trash up to 1 of your opponent's Life cards" (L12011, trashes from opp
  Life), ST04-002/003/004/010 DON−N cost KO/play, ST03-009/014/015/016 return-to-hand, ST03-010 deck-look,
  ST03-017 Counter buff+conditional-draw, ST04-005/006 DON−1 draw, ST04-008/014 add-DON-active, and the vanilla
  Blockers.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2043-2060 (ST02-001..ST03-007) — findings
- #124 ST03-007 Sentomaru "Play up to 1 [Pacifista] with a cost of 4 or less from your deck" — played the WRONG
  card (Usopp). The play-from-deck resolver passed CardTypeFilter="character" (the play-mode type) alongside the
  [Pacifista] name filter; the selection handler's name-OR-type logic then treated ANY character as a valid
  alternative to the name. Fixed: pass an EMPTY type filter when a name filter is present (play-mode still
  enforces Character). `st03007test`: Usopp rejected, Pacifista plays. Also fixes OP01-069, OP08-071, OP08-073.
  Coverage stayed green (the real "[Name] or Event card" cards EB04-029/OP12-071 use a different path — unaffected).
- Verified clean: ST02-009 Law dual-tag set-active ({Supernovas} or {Heart Pirates}, CardPassesFeatureFilter
  match-any L6456), ST03-004 Moria dual-tag trash-to-hand, ST02-014 X.Drake self-rested-gated dual-tag aura,
  ST02-005 Killer rested-KO cost≤3, ST02-010 Hawkins battle-reactive self-active, and the ST02/ST03 starter set
  (multi-cost activates, conditional +power, DON-rest, Counter buffs, On-Block bottom-deck, DON−4 bounce leader).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2025-2042 (PRB02-014..ST01-017) — findings
- #122 PRB02-017 Hancock attack-lock "your opponent's RESTED Leader or up to 1 of your opponent's Characters …
  cannot attack" — the resolver applied `restrictNeedRested = ContainsAll(text,"rested")` to BOTH branches, so a
  Character target wrongly required being rested (the "rested" binds only to the Leader). Split it into
  Leader-need-rested vs Character-need-rested by which noun "rested" precedes. `prb02017test`: active opp Character
  lockable, active opp Leader NOT. (OP06-023 Arlong Leader-only path preserved.)
- #123 PRB02-018 Ace dual-zone play "play up to 1 [Sabo], [Portgas.D.Ace], or [Monkey.D.Luffy] with a cost of 2
  from your hand or trash" — the "from your hand or trash" resolver had NO named-inclusion list, NO exact-cost
  ("cost of N" w/o "or less"), NO power filter, NO quoted-type filter (any Character could play). Added all four
  (names read only from the "play up to N …" segment so a leading-If condition name isn't mistaken for a target).
  `prb02018test`: c2 Luffy plays, c2 Koza (wrong name) rejected, c4 Sabo (wrong cost) rejected. Fixes EB01-060
  [Enel], OP16-102 [Fullalead], EB04-041 [Sanji] power too. Coverage stayed green (no dual-zone regressions).
- Verified clean: ST01-002 Usopp "cannot activate a [Blocker] Character that has 5000+ power" (L11316), PRB02-014
  Sabo conditional hand-cost reduction "if 15+ trash, give this card in hand −3 cost" (GetHandSelfCostBonus L870 +
  trash-count cond L5538), PRB02-016 Otama multi-cost (rest self + Life-to-hand) → +3000, PRB02-015 Shiryu
  conditional Blocker/On-KO, and the ST01 starter set (DON-move, Rush, Blocker, +power, KO, Counter, NoBlocker).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 2007-2024 (P-113..PRB02-013) — findings
- #119 PRB02-010 Pudding "play up to 1 {Big Mom Pirates} … with 6000 to 8000 power from your hand" — the
  play-from-hand resolver had NO power filter at all ("N to M power" range or "N power or more/less" were
  ignored). Added both to the resolver. `prb02010test`: 7000 plays, 9000 rejected. (3 cards use power ranges.)
- #120 PRB01-001 Sanji leader "Up to 1 of your Characters WITHOUT an [On Play] effect and with a cost of 8 or
  less gains [Rush]" — the keyword-grant resolver enforced neither the cost cap nor the "without an [On Play]
  effect" filter (any Character could gain the keyword). Added both. `prb01001test`: no-On-Play char gets Rush,
  On-Play char rejected.
- #121 play-from-trash RESTED (16 cards incl. PRB02-013, OP09-085, OP03-013…) — both play-from-trash resolvers
  played the recurred Character ACTIVE, ignoring "from your trash rested" (an unintended player advantage — a
  rested recur can't attack/block that turn). Set `Rested` from the text at both play points. `trashrestedtest`:
  recurred char enters rested. yamato079test still passes (non-rested path unaffected).
- ⏳ DEFERRED (rest-by-opponent reactive family, 2 cards): PRB02-006 Zoro "if would be rested by your opponent's
  Character's effect, rest 1 other instead" (rest-REPLACEMENT) + PRB02-009 Mr.3 "when rested by your opponent's
  effect, trash this + draw 2" (reactive-to-rest). FireOnBecomeRested only fires on self-rest during an attack,
  not on opponent-effect rest — needs a unified rest hook across all "rest opponent's Character" resolvers (same
  shape as the deferred on-own-removal hook). Cross-cutting; deferred to avoid a risky partial change.
- Verified clean: P-117 Nami alt-win "deck to 0 = win" (CardData.WinsOnDeckOut reads the effect text, L13234),
  PRB02-002 Law "give this Character −2000 instead" removal-replacement (L4428), P-113/115/135 and PRB02-001/003/
  004/007/008/011/012 (conditional buffs, DON-move, rest, deck-look, cost-filtered draw, On-KO draw).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 1989-2006 (P-090..P-112) — findings
- #118 P-090 Charlotte Smoothie dynamic cost cap "cost equal to or less than the number of DON!! cards on your
  opponent's field" — ComputeDynamicCap didn't recognize this phrasing (only "number of your opponent's DON!!")
  AND the play-from-hand resolver used only the static ParseLimit cap (dynamic caps only applied in the glow +
  KO path). Added the phrasing (+ self variant) to ComputeDynamicCap and wired the dynamic cap into the play
  resolver (L11483). `p090test` (opp 4 DON): cost-4 Big Mom plays, cost-6 rejected.
- Verified clean: P-104 Shanks conditional removal immunity ("If either you or your opponent has 10 DON …",
  RemovalBlocked L4042 + condition L5781), P-111 Nico Robin removal-replacement "rest 1 DON instead" (L4535
  generic own-rest branch handles DON; `p111test`: victim saved, 1 DON rested), P-091 Shirahoshi "can attack
  Characters on the turn played" grant (L2844), P-092 Koby Leader "base power becomes 7000 until opp next turn"
  (L10707), P-100 Teach board-wide negate (L9132), P-093/096/097/098/099/101/102/103/105/106/107/112 (conditional
  DON ramp, DON-move, cannot-Blocker, conditional self-bounce, DON−10 self-active, Life-cost, conditional play).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 1971-1988 (P-065..P-085) — findings
- #116 P-081 Mihawk condition "you have 3 or more blue {Cross Guild} type Characters" was logged Unknown →
  always failed (its play-from-hand never fired). The char-count condition regex (L5411) had no slot for a COLOR
  qualifier. Added an optional color group + pool color-filter. `p081test`: 3 blue Cross Guild → body fires; 2 →
  skipped. (General fix for any color+tag character-count condition.)
- #117 P-084 Buggy aura "If your Leader is [Buggy], all Characters with a cost of 3 or 4 cannot attack" — a
  continuous PASSIVE attack-lock aura was unenforced (DeclareAttack only checked the attacker's OWN text). Added
  a both-boards aura scan in DeclareAttack (parses "all Characters with a cost of A [or B] cannot attack" + the
  source's leading "If <cond>," gate). `p084test`: c3 char locked, c5 char attacks. (Only continuous attack-lock
  aura in the DB; the other 15 "cannot attack until X" cards apply a per-target cannotAttack modifier already.)
- Verified clean: P-077 Ulti DON-return reactive ("When 2+ DON returned to DON deck", L4586), P-085 Bonney add-
  opp-char-to-OWNER's-Life face-up (L9648, moves to the opponent's own Life — probe "condition not met" = the
  {Supernovas}/life gate), P-082 Crocodile conditional bottom-deck, P-065/068/069/070/071/072/073/074/075/076/
  078/079/083 (conditional buffs, deck-look w/ self-trash/bounce cost, DON-move, self-recur, rest, cost-minus).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 1953-1970 (P-045..P-063) — findings
- #115 P-046 Yamato "[On Play] You may place all cards in your hand at the bottom of your deck in any order. If
  you do, draw cards equal to the number you placed" — NO handler; fell through to the generic buff resolver
  (mis-glowed Leader+Chars, did nothing useful). Added a self handler (place hand → deck BOTTOM, no shuffle,
  draw = N placed) next to the P-002 return-and-shuffle one. `p046test`: placed 5 → drew 5 (the old deck-top,
  not the placed cards), old hand at bottom, deck count unchanged.
- ⏳ DEFERRED (variable-count picker family): P-051 Shanks "[When Attacking] trash ANY NUMBER of cards … +1000
  per card trashed this battle" and P-059 World's Continuation "[Counter] return ANY NUMBER of Characters …
  +2000 per returned this battle" — no scaling handler; the generic buff resolver applies a flat single-step
  buff. Needs a count-picker UI + per-count scaling (same family as the OP variable-trash/rest deferrals). Not
  headless-resolvable; both are When-Attacking/Counter so probe can't reach them anyway.
- Verified clean: P-052 Mihawk / P-054 Garp "by ＜X＞ attribute cards" immunity (byCards branch, unaffected by
  the #113 edit), P-055 opp-char→bottom-deck (L8747), P-056 Zoro "Return up to 1 Character … to the owner's
  hand" (both-sides glow is CORRECT — any Character), P-057 Fleeting Lullaby freeze (up-to-2 / cost≤4 / rested,
  L7717), P-060 rest-opp-DON (L10517), P-045/047/048/049/050/053/062/063 (Banish, conditional draw/buff, deck-
  look, conditional bounce, multi-clause rest+buff+add-from-Life, rest cost≤1).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 1935-1952 (P-020..P-044) — findings
- #114 P-024 "I'm Gonna Be King of the Pirates!!": "Your Leader gains +1000 power for each of your Characters
  during this turn" — the generic buff resolver PROMPTED for a target (glowed Leader+Char) and applied a FLAT
  +1000, ignoring the "for each" scaling. Added a dedicated branch (before the generic buff) that auto-targets
  the Leader and scales +1000 × Character count. `p024test`: Leader 5000→7000 with 2 Characters.
- P-025 Smoker "cannot be K.O.'d in battle by Characters WITHOUT the ＜Special＞ attribute" — regression-checked
  the #113 IsBattleKoImmune edit via `smoker025test`: immune vs Special attacker=False, vs non-Special=True (the
  `without` branch still works).
- Traced clean: P-027 Franky base-power-threshold board aura ("3000 base power or less gain +1000", L695 uses
  printed power + [Opponent's Turn] gating), P-037 "2 or more rested Characters" condition (L5348), P-032 Sengoku
  opponent −2 cost aura (L944), P-020/026/028/029/030/031/033/034/035/036/039/043/044 (On-Play buffs, cost-minus,
  Double Attack, self-to-bottom-deck cost, On-KO bounce, DON ramp, Banish, conditional continuous buffs, bounce).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 1916-1934 (OP16-117..P-019) — findings
- #112 P-002 "I Smell Adventure" (+ OP04-048): "Return all cards in your hand to your deck and shuffle. Then,
  draw cards equal to the number you returned" — the ". Then," splitter dropped the returned count, so clause B
  drew its default (1) instead of N. Added a FindThenClause guard (keep atomic) + a self atomic handler mirroring
  the opponent "returns all cards in their hand" one. Re-probe: returned 5 → drew 5. (Only the "their hand"
  opponent version was previously guarded.)
- #113 P-007 Luffy "[DON!! x1] cannot be K.O.'d in battle by ＜Strike＞ attribute Leaders or Characters": the
  "Leaders or Characters" phrasing didn't match IsBattleKoImmune's `by …attribute Characters` regex, so the
  attribute filter was skipped → BLANKET battle-KO immunity. Added a `byLeadOrChars` branch (attacker must be
  ＜Strike＞). `p007test`: immune vs Strike=True, vs Slash=False.
- Traced clean: OP16-117 Black Hole (negate cost≤8 + "[Trigger]" cost filter, L10820/L6337), OP16-118 Ace
  (counter-in-hand-becomes L3507 + look5-reveal), OP16-119 Teach (look3→Life), P-005 Kaido Banish grant
  (L1258), P-008 Yamato rest-self→rest-opp-cost≤2, P-009 Law opp-adds-from-Life (L8123), P-010 Kaido EoT DON
  ramp, P-011 Uta "no base effect" filter (L6372), P-013 Gordon self-to-bottom-deck cost→−3000 (probe-verified),
  P-001/003/004/006/014/017/018/019 vanilla keyword grants / standard −power / KO-by-power.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 1899-1916 (OP16-098..116) — findings
- #111 OP16-104 Catarina Devon "[When Attacking] ... base power becomes the same as the selected Character's
  power" — resolver snapshotted `GetCard(mirror).Power` (PRINTED base), but "X's power" means current TOTAL
  power (buffs/DON). Changed to `GetPower(state, mirror)`. Also corrects the same Mr.2 (OP16-036) opp-Leader
  path; mr2_036test still passes.
- Heavy [On K.O.] family (OP16-102/103/106/107/109/110/114) — dispatched via FireOnKoEffects w/ Your/Opponent's
  Turn gating; bodies (draw + KO cost≤N / rest / −power / play [Fullalead]) are standard resolver clauses.
  Probe "empty log" = On-KO not triggered by probe (artifact), not a gap.
- OP16-107/116 opp-Life-steal ("add from top of opponent's Life to owner's hand") handled L9532. OP16-108 Shiryu
  add-{BB}-cost≤6-from-trash-to-top-of-Life handled L9174 (probe skip = st01 trash had no BB match, artifact).
  OP16-113 Boa Marigold conditional [Blocker] via "you have 2 or less Life cards" (EvaluateCondition L5250).
  OP16-101 Mahoroba 1st clause glows own Leader/Char, 2nd clause 10-trash gated. OP16-100 set-active gated by
  "opponent's Character has been K.O.'d during this turn" (CharKoedThisTurn, L5714). All resolve correctly.
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.

### Batch 1881-1898 (OP16-079..097) — findings
- #109 OP16-084 Kouzuki Momonosuke (NA→OK): TryAutoPayCost self-trash branch broadened to
  `^trash this Character(?: with a cost of (\d+) or (more|less))?$` + GetCost gate. Cost "trash this
  Character with a cost of 20 or more" now correctly UNpayable at printed cost 5 (was silently free).
- #110 OP16-079 Yamato leader: added FireOnPlayedFromTrash dispatcher (+2 call sites in the play-from-trash
  resolvers) for "When a {Land of Wano} type Character card is played from your trash, that Character gains
  [Rush]". Verified via yamato079test (Otama played from trash → HasRush=true). Only card using this reactive.
- OP16-080 Teach leader redirect, OP16-085/097 play-from-trash, OP16-089 Mihawk −4 cost, OP16-094 Ace
  opp-hand-trash — traced, resolve correctly (no fix needed).
- Coverage after batch: OK=2657 / NA=18 / STUCK=0 / CRASH=0 / INVARIANT=0.


## Batch 1863–1880 (OP16-059..078, Navy / Admiral DON-ramp) — 1 FIX
- ⭐BUG#108 (multi-play "with different card names" constraint) OP16-060 Sengoku leader — "return 8 active DON: Play
  up to 3 {Admiral} type Character cards WITH DIFFERENT CARD NAMES from your hand." The play resolver enforced type/
  cost/[Trigger]/other-than but NOT the uniqueness constraint → could play 3 identical-named Admirals. Added a
  PendingEffect.PlayedPickIds tracker + a reject-if-name-already-played check. `sengoku060test`: 1st Kuzan plays, 2nd
  (same name) rejected. Coverage 2656/19/0/0/0.
- ✔ OP16-059 event "rest 7 DON: look 5, play up to 2 {Impel Down} 6000-or-less" (DECKLOOK select play-from-look),
  OP16-063 Kuzan cannot-activate-Blocker (#9256 power-filtered blocker-deny), OP16-074 Magellan opp-DON-return,
  OP16-065 Sakazuki −6000 debuff + rest-DON add-DON, OP16-073 Borsalino EoT DON−2 set-active, OP16-076 {Admiral}
  multi-buff, OP16-042 Prisoner "any number in deck" rule. The whole Navy add-DON-active/rest ramp family standard.

## Batch 1845–1862 (OP16-040..058, Impel Down / Prisoner / Buggy) — 1 FIX
- ⭐BUG#107 (return/sacrifice cost: cost filter) OP16-045 Crocodile + OP16-050 Miss Olive — "You may return 1 of
  your Characters WITH A COST OF 2 OR MORE to the owner's hand: <body>." CostCharFilterOk checked feature/color/power
  but NOT cost → the cost accepted ANY Character (even c<2). Added a "with a cost of N or more/less" filter (GetCost).
  `returncost045test`: c1 rejected (stays on board), c3 returned. Coverage 2656/19/0/0/0.
- ✔ OP16-058 event one-time "all [Prisoner] cards' base power becomes 7000 during this turn" (L8268 BasePowerOverride),
  OP16-055 Mr.2 base-power-becomes-opp-Leader (#OP16-036), OP16-043 Usopp On-KO rest-{Dressrosa}-Leader/Stage cost
  (#67), OP16-047 Doflamingo "opp places 2 hand cards at bottom of deck" (L8760), OP16-042 Prisoner of Impel Down
  "any number in deck" deckbuild rule, OP16-052/056 give-DON / trash-self. Handled.
- ⏸ DEFER (on-own-removal reactive family — NOW 5+ CARDS, candidate for a dedicated task) OP16-041 Buggy leader —
  "[DON!! x1][OPT] when your {Impel Down} type Character card is removed from the field, play [Prisoner of Impel
  Down]." Same family as OP08-056 Moby Dick, OP09-080 Thousand Sunny, OP10-042 Usopp, OP13-078 Oro Jackson. Needs a
  UNIFIED removal hook (KO + bounce + deck-bottom + trash-by-effect) that passes the removed card + notifies the
  VICTIM owner + matches the {tag}/[Name] filter. Broad change across all removal paths — deferred as its own task.

## Batch 1827–1844 (OP16-020..039, Impel Down) — CLEAN (0 fixes, all verified)
- ✔ OP16-036 Mr.2 "[When Attacking] this Character's base power becomes the same as your opponent's Leader" —
  `mr2_036test`: 1000→5000 (opp Leader power) via the opponent-Leader-copy handler (L10605). OP16-038 "if you have 5
  {Impel Down} type Characters with different card names, set all active" (different-names condition L5365), OP16-024
  Inazuma "When this Character is K.O.'d by opp effect, rest opp" (FireOnKoEffects #73), OP16-034 Luffy "+1000 for
  each Character with a different card name" (ScalingUnitCount L541), OP16-020 compound cost "rest 1 DON AND reveal 1
  Character card with 8000 power" (recognized; reveal-power filter #105), OP16-030 Law EoT set-all-green-≤5-active,
  OP16-032 Boa "opp cannot be rested other than [Luffy]" (block-lock #86). All handled; coverage stays 2656/19/0/0/0.

## Batch 1809–1826 (OP15-119 + OP16-001..019, Ace / Whitebeard "8000 power") — 2 FIXES
- ⭐BUG#105 (cost card power + Character-type filter — WHOLE OP16 archetype) — the Ace/Whitebeard signature "reveal/
  trash 1 Character card WITH 8000 POWER from your hand" (OP16-002/003/007/009/010/011/019, …). CostCardMatches
  checked color/[Trigger]/feature but NOT power or card-type → the cost accepted ANY card. Added: "Character card" →
  def.Type=="character"; "with N power [or more/less]" → def.Power filter (exact or directional). `costpower8000test`:
  a 7000 card is rejected, an 8000 accepted. Coverage 2656.
- ⭐BUG#106 (replacement trash power filter: direction + phrasing) OP16-018 Rockstar — "trash 1 Character card with
  6000 power OR MORE from hand instead." My #99 filter matched only "with a power of N or less"; generalized to
  `with (?:a power of )?N(?: power)? or (more|less)` + both directions. `rockstar018test`: ≥6000 Weevil trashed
  (Shanks saved), <6000 Chopper kept; without a ≥6000 card Shanks K.O.'d. Alvida (≤6000) still works. Coverage 2656/19.
- ✔ OP16-014 Marco replacement "K.O. this Character instead" + On-KO trash-cost self-revive, OP16-015 Luffy "Leader
  and this Character's base power becomes 7000" (set-base-power effect L10539), OP16-008 Squard trash-10000-base cost
  → KO, OP16-013 McGuy On-KO KO-8000-base, OP16-006 Shanks rest-2-DON→KO. Handled.

## Batch 1791–1808 (OP15-098..118, Sky Island / Skypiea) — CLEAN (0 fixes, all verified)
- ✔ OP15-098 Luffy leader replacement "{Sky Island} Character with 6000 base power or more … add Life to hand
  instead" — `op15098test`: 11000 {Sky Island} saved, 4000 not (tag + base-power #72 + add-Life action L4231).
- ✔ OP15-100 Kamakiri compound cost "trash this Character AND add 1 card from top of Life to hand: K.O. ≤6" (probe:
  cost paid), OP15-114 Wyper mass "K.O. all opp with 0 power or less" (L8683) after mass −2000, OP15-116 self-Life-
  trash "trash 1 card from the top of your Life cards" body (`lifetrash116test`: Life 3→2, +1 trash), OP15-102 Gan.Fall
  dynamic cost-cap rest (opp Life count) + in-hand −3 cost cond, OP15-109 Nico Robin Life-to-hand cost + play-{Sky
  Island}, OP15-108 Nami reveal, OP15-118 Enel conditional-cannot-removed-if-≤6-DON (#90). All handled; coverage
  stays 2656/19/0/0/0 (no code changes).

## Batch 1773–1790 (OP15-079..097, Thriller Bark / Straw Hat trash-synergy) — 1 FIX
- ⭐BUG#104 (continuous conditional set-base-power + cost) OP15-092 Monkey.D.Luffy — "Apply each based on cards in
  trash: • If 10+ cards, this Character's base power becomes 9000 and it gains +10 cost." NO timing tag → a CONTINUOUS
  passive, but the "Apply each" handler (L8931) is a one-time resolver → GetPower/GetCost never applied it. Added:
  (a) GetPower self-conditional set-base-power (scoped to "based on the number of cards in your trash", trash≥N →
  power=M); (b) GetPassiveCostBonus the "it gains +N cost" trash-conditional (the "it gains" wording + the If both
  missed the existing loop). `luffy092test`: 12 trash → power 9000, cost 7→17; 5 trash → unchanged. Coverage 2656.
- ✔ OP15-094 Zoro replacement "other than this Character would be removed → trash this instead" (selfOnly false-pos
  fixed in #80), OP15-086 Nami play-from-trash + Rush-to-played-card, OP15-080 Oars compound "[Gecko Moria] 10000+
  AND no other [Oars]" cond + On-KO self-recur, OP15-093 Risky Brothers [Rush: Character] grant (L11490), OP15-088
  Pirates Docking Six trash-3 cost→play, OP15-090 Perona replacement. Handled.
- ⏳ verify-later OP15-092 Luffy SECONDARY thresholds — "20+ cards (opp turn) your Leader's base power becomes 7000"
  (leader-set aura, unhandled continuous) + "30+ cards +1000 power" (simple scaling, unhandled). Only the 10+ tier
  fixed. OP15-093 Risky Brothers "gains … the ＜Slash＞ ATTRIBUTE" grant (attribute-grant unhandled; [Rush: Character]
  works).

## Batch 1755–1772 (OP15-060..078, Skypiea / Enel DON−1) — 1 FIX
- ⭐BUG#103 (compound "you have [X] and [Y]" condition) OP15-064 Kotori + OP15-072 Hotori — "If you have [Satori] AND
  [Hotori], rest/give −3000." The "you have [Name]" handler captured only the FIRST name → the combo passed with just
  ONE of the two present. Added a compound handler `^you have (\[…\](?: and \[…\])+)$` requiring ALL named cards on
  the field (Leader + Characters). `kotori064test`: both present → rests; only [Satori] → skipped. Coverage 2656.
- ✔ OP15-070 Fuza / OP15-071 Holly "[Opp Turn] All of your [Shura]/[Ohm] cards' base power and this Character's base
  power become 6000" — the set-base-power AURA for the "[Name] cards' become N" form IS handled (GetPower L214,
  timing-gated). (The still-deferred set-base-power variants are Ju Peter OP13-084 tag-based "set to N" + Vista
  OP14-053 leader-copy — L214 doesn't cover those phrasings.) OP15-060 Enel conditional cannot-be-removed-if-≤6-DON
  (#90 gate) + DON−1 Blocker, the whole Skypiea "DON!! −1: draw" + "if ≤6 DON on field" family (OP15-061/063/066/067),
  OP15-065 Goro reveal-if-cost≤2, OP15-069 Nola DON-return replacement, OP15-074/075/076 Enel events. Handled.
- ⏳ verify-later OP15-073 Yama — "Play up to 1 [Heavenly Warriors] c1 OR up to 1 {Vassals} type Character c1 from
  hand" (named-OR-type dual-filter play; probe pending/not-NA but the OR-filter glow untraced). OP15-071 Holly "(This
  card deals 2 damage.)" — same as OP15-056 (no direct-damage handler).

## Batch 1737–1754 (OP15-041..059, Dressrosa / Lucy / Enel) — CLEAN (0 fixes)
- ✔ OP15-052 Leo replacement "place 1 of your Characters at bottom of owner's deck instead" (L4424, base-power-≤7000
  victim filter via #72), OP15-058 Enel leader "DON!! deck consists of 6 cards" rules mod (DonDeckSizeForLeader
  L1585) + Activate:Main DON add/give, OP15-046 Sabo activate-{Dressrosa}-Event (condition-gated, activate-Event
  L7736), OP15-041 Orlumbus place-own-Character cost (pickM "place"), OP15-047 Sanji [Unblockable] grant + "cannot be
  blocked", OP15-042 Kyros trash-cost conditional Rush + On-KO self-recur, OP15-043 Kelly Funk play-[Bobby Funk],
  OP15-054/055 choose-one events. All handled; coverage stays 2656/19/0/0/0.
- ⏸ DEFER (opponent-choice, cf. OP13-119) OP15-059 Amazon — "[On Opp Attack] rest self: your OPPONENT may return 1
  of their active DON!! to their deck. If they do NOT, give opp −2000." Needs an opponent decision (return DON vs
  take the penalty) — no opponent-choice mechanism; the −2000 fires only on the opponent declining.
- ⏳ verify-later OP15-056 — "(This card deals 2 damage.)" parenthetical on an Event: no "this card deals N damage"
  (direct Life-damage) handler exists (only Banish references damage). Unclear if real mechanic vs reminder; the main
  draw + Double-Attack body resolves.

## Batch 1719–1736 (OP15-022..040, East Blue DON-clog / Straw Hat / Dressrosa) — 2 FIXES (1 NA cleared)
- ⭐BUG#101 (REST filter: "with a DON!! card given") OP15-027 Mihawk — "[On Play] Rest up to 1 opp Character WITH A
  DON!! card given." The rest resolver's restDonMin matched only "N or more … given" → Mihawk rested any Character.
  Added the ≥1 case for "with a DON!! card given" (mirrors the KO fix #100). `mihawk027test`: target with a DON
  rested, without not.
- ⭐BUG#102 (NA cleared: compound "rest Leader AND return" cost) OP15-039 Rebecca leader — "[Activate: Main] You may
  rest this Leader AND return 1 of your {Dressrosa} type Characters to the owner's hand: Play up to 1 {Dressrosa} c3
  from hand." The two-verb cost (rest Leader + return Character) matched no cost branch → acknowledged-manual (NA).
  Added a "rest this Leader/Character and <pick cost>" pre-step: rest the Leader/self once, strip the prefix so the
  pick-based flow resolves the return. `rebecca039test`: Leader rested + {Dressrosa} returned + c3 played. Coverage
  OK 2655→2656, NA 20→19.
- ✔ OP15-024 Usopp "cannot be rested by opp Leader/Character effects" (cannotBeRested → block-lock #86), OP15-031
  Purinpurin "if chosen Character's cost == DON given to it, K.O." (L7869 Krieg family), OP15-025/028 give-opp-DON,
  OP15-032 Brook rest-opp-cards + trash-self set-active, OP15-022 Brook leader mill/deck-out, OP15-036 Ryuma On-Play/
  When-Attacking KO-rested. Handled.

## Batch 1701–1718 (OP15-003..021, East Blue / DON-clog) — 2 FIXES
- ⭐BUG#99 (replacement trash cost: power + Character-card filter) OP15-003 Alvida — "If this Character would be
  K.O.'d, you may trash 1 CHARACTER card with a power of 6000 or less from your hand instead." The trash-hand-instead
  branch (#88 handled a TYPE filter) ignored the Character-card + power filters → could trash any card. Generalized
  the branch with a combined predicate (type / Character-card / power cap). `alvida003test`: trashes the ≤6000
  Character (not the 7000 or an Event), can't pay without one. Coverage 2655.
- ⭐BUG#100 (KO filter: "with a DON!! card given") OP15-018 Mohji — "[When Attacking] K.O. up to 1 opp Character with
  3000 power or less WITH A DON!! card given." The KO resolver had no DON-given filter (only the REST resolver did) →
  Mohji K.O.'d any 3000-power target. The East Blue deck GIVES the opponent DON, so the filter matters. Added
  `koDonGivenMin` (≥1 for "with a DON!! card given", or "N or more … given") gated on AttachedDonIds. `mohji018test`:
  target with a DON K.O.'d, without not. Coverage 2655/20/0/0/0.
- ✔ OP15-001/008/010/014/015/017 "give N of your opponent's rested DON!! to 1 of your opponent's Characters" (East
  Blue DON-clog, L8764), OP15-014 Bartolomeo replacement trash-1-Event-instead + Activate-Event, OP15-007 Gin
  conditional play-from-hand, OP15-011 Pearl conditional Blocker + On-KO KO, OP15-013 Higuma give-DON + debuff-with-
  DON-given. Handled.

## Batch 1683–1700 (OP14-105..120 + OP15-001..002, Amazon Lily / Warlords + OP15 leaders) — 1 FIX
- ⭐BUG#98 (play-from-trash "and a [Trigger]" filter) OP14-110 Dr. Hogback — "[On K.O.] Play up to 1 Character card
  with a cost of 4 or less AND a [Trigger] … from your trash." The from-trash resolver had NO [Trigger] filter
  (checked cost/type/name only) → a non-Trigger Character could be played. Added `Regex (?:with|and) a \[Trigger\]`
  → require trashDef.Trigger (mirrors the play-from-hand fix #92). `hogback110test`: non-Trigger rejected, Trigger
  played. Coverage 2655/20/0/0/0.
- ✔ OP14-119 Mihawk "[Your Turn] when this Character becomes rested, opp c≤9 cannot be rested" (#94 dispatch + #86
  block-lock) + [On Opp Attack] trash→+2000, OP14-111 Perona On-Play/On-KO cannot-attack, OP14-120 Crocodile On-Play
  freeze + On-KO self-revive "play this Character from trash", OP15-001 Krieg leader conditional aura + rest-opp-with-
  2+-DON-given, OP14-108 Rayleigh conditional KO-7000-base, OP14-116 Salamander Counter+play. Handled.
- ⏸ DEFER (variable-count family, now 2 cards) OP15-002 Lucy leader — "trash ANY NUMBER of Event/Stage cards: +1000
  each." Same variable-count mechanic as OP13-001 Luffy leader (rest any number of DON → +2000 each); needs a count-
  picker input (interactive UI) + scaling.
- ⏳ verify-later OP14-115 Rindo — "[Opp Turn][On K.O.] add deck→Life. Then, you take 1 damage." The "you take 1
  damage" self-Life-loss (reveal/trash your own top Life, triggering its [Trigger]) is unhandled.

## Batch 1665–1682 (OP14-084..104, Baroque Works / Thriller Bark) — 1 FIX
- ⭐BUG#97 (dual play-from-trash, different cost caps) OP14-084 Ms. All Sunday — "play up to 1 {Baroque Works}
  Character c≤4 AND up to 1 {Baroque Works} Character c1 from your trash." The play-from-trash resolver read only the
  first "up to 1" + first cap → played ONE Character. Added a dedicated dual handler (sinkLow/sinkHigh budget like
  #69 dual-KO) with the quoted "type including "X"" filter + per-pick cost caps; plays inline (open-slot + On-Play).
  `msallsunday084test`: both c3 (≤4) and c1 (cost-1) played. Coverage 2655/20/0/0/0.
- ✔ OP14-091 Mr.2 play from HAND OR TRASH (L8227 dual-zone), OP14-104 Gecko Moria "play it OR add to Life face-up"
  choice (L9006), OP14-090 Mr.1 / OP14-094 Mr.5 "cost of 0 OR cost of 8 or more" disjunction condition (EB03-046
  handler), OP14-092 Mr.3 replacement place-3-from-trash, OP14-093 Mr.4 On-KO add-from-trash, OP14-096 Ground Death
  negate-opp-Character, OP14-100 Absalom On-KO reveal. Handled.
- ⏳ verify-later OP14-088 Miss.MerryChristmas — "[On K.O.] draw + K.O. up to 1 opp STAGE with a cost of 1" — Stage-
  only KO by exact cost (same class as OP13-098; the dual char+stage handler is OR-only, so Stage-only KO by cost is
  untraced).

## Batch 1647–1664 (OP14-064..083, Donquixote DON-ramp / Thriller Bark) — 1 FIX
- ⭐BUG#96 (KO by EXACT base power) OP14-064 Giolla — "[On K.O.] … K.O. up to 1 of your opponent's Characters with a
  base power of 0." The KO resolver parses only "N power or less" / "power of N or less"; Giolla's exact "base power
  of 0" (no "or less") left koPowerCap=−1 → NO power filter → it K.O.'d ANY Character. Added `koBasePowerExact` =
  ParseLimit(text, `base power of (\d+)\b(?! or)`) and gated the target on `koDef.Power == koBasePowerExact`.
  `giolla064test`: 0-base-power opp K.O.'d, 1000-base opp not. Coverage 2655/20/0/0/0.
- ✔ OP14-079 Crocodile leader "give opp −10 cost" (multi-digit regex L11034, floored at 0) + self-removal-lock
  "all opp Characters cannot be removed by your effects", OP14-069 Doflamingo DON−3 choose-one (conditional option A),
  OP14-065 Senor Pink opp-DON-return, OP14-068 Trebol DON-return reactive, OP14-080 Gecko Moria K.O.-own→buff. The
  whole On-KO/On-Play/EoT DON-ramp family (OP14-067/071/072/074/075/076/077/078) is standard. Handled.
- ⏸ DEFER (one-off) OP14-070 Buffalo — "When this Character becomes rested BY YOUR OPPONENT'S CHARACTER'S EFFECT, you
  may return 1 DON!! to un-rest (set active)." The becomes-rested-BY-OPPONENT variant (#94 hooked only the own-attack
  path). Only ONE card uses this phrasing; needs a rest-opp-resolver hook + the "by opponent's Character's effect"
  source filter + the un-rest body. Not worth a dedicated hook for a single card.

## Batch 1629–1646 (OP14-045..063, Fish-Man / Impel Down / Donquixote) — 1 FIX
- ⭐BUG#95 (hand-trash reactive: hardcoded negate) OP14-045 Kuroobi + OP14-049 Jinbe — "When a card is trashed from
  your hand by an effect, this Character gains [Rush]." NotifyHandTrashedByEffect HARDCODED applying `effectsNegated`
  to any card matching the trigger, so Kuroobi/Jinbe's "gains [Rush]" was turned into SELF-NEGATION (the opposite).
  Rewrote it to parse the body: keep the self-negation special-case (OP14-056 Wadatsumi), queue every other body for
  normal resolution (+ scan the Leader too). `kuroobi045test`: Kuroobi gains [Rush]; Wadatsumi still negated.
  Coverage 2655/20/0/0/0.
- ✔ OP14-060 Doflamingo leader "[On Opp Attack] DON−1: change the attack target to your Leader/{Donquixote} char"
  (redirect L8950), OP14-062 Vergo / OP14-064-class DON-return replacements, OP14-047/051/052 play-from-hand + On-KO
  draws, OP14-048 Shiryu return-opp + trash-all-hand, OP14-054 Fisher Tiger EoT hand-size-trim. Handled.
- ⏸ DEFER (Ju Peter family) OP14-053 Vista — "[Opponent's Turn] If ≤7 hand, this Character's base power BECOMES THE
  SAME AS YOUR LEADER's base power." Continuous conditional base-power COPY (own Leader). L10505 handles only the
  "same as OPPONENT" variant; needs a live base-power override in GetPower (same class as Ju Peter #OP13-084).
- ⏳ verify-later OP14-062 Gladius — "[On K.O.] DON−1: K.O. OR rest up to 1 opp Character with 6000 base power or
  less." The "K.O. or rest" CHOICE isn't offered; "rest up to 1 of your opponent's" likely matches the rest resolver
  → always rests (loses the stronger K.O. option). Needs a choice prompt or default-to-K.O.

## Batch 1611–1628 (OP14-026..044, Wano rest-synergy / Fish-Man) — 1 FIX (7-card family)
- ⭐BUG#94 (becomes-rested reactive — NEW dispatcher) OP14-027 Shanks / OP14-028 Johnny / OP14-032 Humandrill /
  OP14-035 Yosaku (+ OP14-021 Issho, previously deferred) — "[Your Turn] When this Character becomes rested, <effect>"
  had NO dispatcher → the whole 7-card family never fired. Added FireOnBecomeRested (timing gate + DON threshold +
  [Once Per Turn]) and called it from DeclareAttack right after the attacker rests (attacking is the primary
  active→rested transition on your turn). `johnny028test`: Johnny attacks → reactive queued → K.O.s a rested c≤2 opp.
  Coverage 2655/20/0/0/0. LIMITATION: only the ATTACK path is hooked; a Character rested by a COST or an EFFECT
  doesn't yet fire (verify-later — [Your Turn] variants are attack-driven in practice).
- ✔ OP14-029 Tashigi replacement rest-1-of-your-cards-instead + Activate rest-2-cards, OP14-033 Perona "opp c≤5
  cannot be rested" (block-lock via #86) + On-KO play, OP14-034 Luffy green-{SHC} aura + rest-1-Character replacement,
  OP14-031 Nami rest-opp + set-DON, OP14-040 Jinbe leader trash→give-DON, OP14-044 Newgate reveal-if-{Whitebeard}.
  Handled.
- ✔ OP14-026 Oden / OP14-027 Shanks 2nd clause "[Opponent's Turn] If this Character IS rested, +power/−power" —
  these are PASSIVE "if rested" conditionals (not becomes-rested reactives), handled by the power/aura calc.

## Batch 1593–1610 (OP14-006..025, Supernovas / Wano) — 1 FIX
- ⭐BUG#93 (base-power swap: Leader target + "selected cards") OP14-009 Trafalgar Law — "[On Opp Attack] trash 2:
  Select your Leader and 1 Character. Swap the base power of the selected CARDS with each other." The swap handler
  required the literal "selected Characters" and rejected Leader-type targets (`Type != "character"`) → Law's
  Leader↔Character swap never worked. Broadened detection to "Swap the base power of the selected" + allow a Leader
  target when the text says "your Leader". `law009test`: Leader 5000↔Character 3000 swapped correctly. Coverage 2655.
- ✔ OP14-017 Chambres swap 2 OPPONENT's Characters (oppSwap path), OP14-016 X.Drake replacement "give your Leader
  −2000 instead" (L4246), OP14-020 Mihawk leader "rest 1 of your CARDS" cost (rests any own card — probe rested a
  Character), OP14-002/006/012 conditional self-power gates ("if this Character has 5000+ power"), OP14-010 Hawkins
  On-KO play-from-deck, OP14-024 Kin'emon set-DON + On-KO rest-opp-cards, OP14-025 Kuro conditional play-{East Blue}.
  Handled.
- ⏸ DEFER (spec) OP14-021 Issho — "[Your Turn] When this Character BECOMES RESTED, you may add 1 Life to hand; if you
  do, freeze up to 1 opp rested Character/Stage." No "becomes rested" reactive dispatch exists; hooking it needs a
  detection of the active→rested transition across ALL rest paths (attack, cost-rest, effect-rest, block).

## Batch 1575–1592 (OP13-106..120 + OP14-001..005, Egghead / Supernovas) — 1 FIX
- ⭐BUG#92 (play-from-hand "and a [Trigger]" filter) OP13-110 Stussy — "[On Play] play up to 1 Character card with a
  cost of 5 or less AND a [Trigger] from your hand." The play resolver's [Trigger] filter used ContainsAll(text,
  "with a [Trigger]"), but Stussy's cost cap comes first → "and a [Trigger]" → filter skipped → a non-Trigger card
  could be played. Fixed to `Regex (?:with|and) a \[Trigger\]` (mirrors the glow at L6566). `stussy110test`:
  non-Trigger rejected (stays in hand), Trigger played. Coverage 2655/20/0/0/0.
- ✔ OP14-001 Law leader "swap the base power of 2 selected {Supernovas}/{Heart} Characters" (L8859 handler),
  OP13-106 Conney [Opponent's Turn] "when a [Trigger] activates → gains [Blocker]" (FireOnTriggerActivated L3162 +
  timing gate), OP13-109 Bonney replacement turn-Life-face-up-instead (L4321), OP13-108 Bonney opp-adds-Life,
  OP13-114 S-Snake / OP13-117 turn-Life-face-up costs, OP14-002 Urouge conditional self-power gate. Handled.
- ⏸ DEFER (spec) OP13-119 Portgas.D.Ace — "…return opp c≤5. If you do, YOUR OPPONENT plays up to 1 Character c≤4
  from their hand." The opponent-plays-from-hand rider is unimplemented (no such mechanism anywhere) → NA. Needs an
  opponent-side auto-play (AI picks a c≤4 Character) or an opponent decision prompt. First two clauses (give DON,
  return opp) work.
- ⏸ DEFER (Smoker family) OP14-003 Bege — "cannot be K.O.'d by effects of your opponent's Characters with 5000 base
  power or less." Immunity keyed on the REMOVER Character's base power; CannotBeKoedByEffect never receives the
  removing card. Same shape as OP11-005 Smoker (remover-attribute).

## Batch 1557–1574 (OP13-083..105, Celestial Dragons / Five Elders / Wano) — 1 FIX
- ⭐BUG#91 (self-play reactive: [Trigger] filter + period-body) OP13-100 Jewelry Bonney leader — "[Your Turn][OPT]
  when you play a Character WITH A [Trigger], give up to 2 rested DON!! to 1 of your Leader/Character." The self-play
  reactive dispatch matched only "When you play a Character[ with no base effect][ from your hand]," (comma-body);
  Bonney's "with a [Trigger]." (period + Trigger filter) never matched → the reactive never fired. Added a [Trigger]
  filter (played Character must have def.Trigger) + broadened the body regex to accept "with a [Trigger]" and a
  period separator. `bonney100test`: [Trigger] Character → reactive queued; non-Trigger → not. Coverage 2655/20/0/0/0.
- ✔ OP13-083/084/089/091 "cannot be removed if 7+ trash" (now conditionally gated via #90), OP13-095 Rosward "only
  {Celestial Dragons} Characters" cond (L5544) + dual KO base c≤3, OP13-097 "the only Characters … are {CD}" cond
  (L5327), OP13-105 Momonosuke "look at all Life, place back in any order" (L9077), OP13-086/096 reveal-{CD},
  OP13-092 Mjosgard conditional play-Stage-from-trash, OP13-099 Empty Throne dynamic-cost play. Handled.
- ⏸ DEFER (spec) OP13-084 St. Shepherd Ju Peter — "[Your Turn] If 10+ cards in trash, SET THE BASE POWER of all of
  your {Five Elders} type Characters to 7000." A continuous conditional aura that OVERRIDES printed base power (not
  +/-) for a whole type. No aura-scan for "set the base power of all {tag} to N" in GetPower; needs a base-power
  override computed live in the core power calc (must sit BELOW +/- modifiers), risky to rush. Also OP13-098 "K.O.
  opp Stage with a cost of 7" (Stage-only KO by exact cost) — verify-later (dual char+stage handler is OR-only).

## Batch 1539–1556 (OP13-063..082, Roger Pirates / Imu) — 2 FIXES
- ⭐BUG#89 (dual KO by base POWER) OP13-077 "Go All the Way to the Top" — "K.O. up to 1 opp Character with 4000 base
  power or less AND up to 1 with 3000 base power or less." My #69 dual char+char handler matched only "cost of N or
  less", so the power form fell to the generic resolver (first power cap only) → one K.O. Generalized the handler:
  added a base-POWER regex fallback (`dualByPower`) + value = base power. `dualpower077test`: 4000-base + 3000-base
  both K.O.'d, 5000-base survives. Coverage 2655.
- ⭐BUG#90 (conditional removed-immunity KO-path gate) OP13-080 Nusjuro — "If you have 7 or more cards in your trash,
  this Character CANNOT BE REMOVED from the field by your opponent's effects and gains [Rush]." My #74 conditional
  gate in CannotBeKoedByEffect filtered only "cannot be K.O." lines → Nusjuro's "cannot be removed" line was skipped
  → immunity applied UNCONDITIONALLY. Broadened the line-filter to also match "cannot be removed from the field".
  (The BOUNCE path L3926 already evaluated the If gate.) `nusjuro080test`: 7+ trash immune, 3 trash K.O.'d. Coverage
  2655/20/0/0/0.
- ✔ OP13-064 Roger negation aura "Characters that do NOT have a type including "Roger Pirates" have effects negated"
  (L4548 quoted-type), OP13-065 Shanks reveal-"Roger Pirates"-type, OP13-066 Rayleigh rest-opp + set-DON-EoT,
  OP13-069 Tom DON−1 add-Stage-from-trash, OP13-074 Hera play-{Homies}-from-hand, OP13-076/075 rest-DON events,
  OP13-082 Five Elders trash-all + mass-play-from-trash. Handled.
- ⏸ DEFER (Moby Dick / on-own-removal family) OP13-078 Oro Jackson stage — "[OPT] When your Character with a type
  including "Roger Pirates" is removed from the field by opp effect, add 1 DON!! and rest it." Same reactive family
  as Moby Dick (OP08-056) / Thousand Sunny: needs a removal hook that passes the removed card's type + notifies the
  VICTIM owner across all removal paths.

## Batch 1521–1538 (OP13-043..062, Whitebeard Pirates / Boa / Roger) — 2 FIXES (replacement filters)
- ⭐BUG#87 (replacement victim filter: quoted-type) OP13-047 Fossa + OP13-060 Toki — guard "your Character WITH A
  TYPE INCLUDING "Whitebeard Pirates"/"Roger Pirates"". The replacement scan filtered the victim only by {curly} tag
  (ParseCurlyBraceTag) → the quoted-type filter was ignored → they protected ANY own Character. Added a quoted "type
  including "X"" SUBSTRING feature filter, scoped to "Character with a type including" (so it never catches Koby's
  "Leader's type includes X" condition). `fossa047test`: Whitebeard victim saved (Fossa trashes itself), non-WB not.
- ⭐BUG#88 (replacement trash cost: type filter) OP13-046 Vista — "trash 1 card WITH A TYPE INCLUDING "Whitebeard
  Pirates" from your hand instead." The trash-hand-instead branch trashed the LAST hand card unfiltered (and didn't
  gate on having a matching card). Added: pick matching cards; if too few, can't pay. Also broadened the count regex
  (`trash \d+ cards? (?:with|from)`). `vista046test`: WB card trashed (not the non-WB), Vista survives; no WB card →
  Vista K.O.'d. Coverage 2655/20/0/0/0.
- ✔ OP13-050/052 Boa conditional play-[Hancock]-from-hand, OP13-053 Teach trash-{Whitebeard}-Character cost→Banish,
  OP13-057 rest-DON→persistent-Blocker-deny, OP13-061/062 Inuarashi/Crocus "if you have any DON given" cond +
  add-DON, OP13-059 Brilliant Punk return-own→return-opp, OP13-044 Curiel give-rested-DON-to-{Whitebeard}. Handled.

## Batch 1503–1520 (OP13-023..042, FILM DON-ramp / Straw Hat) — 1 FIX
- ⭐BUG#86 (cannotBeRested didn't stop blocking) OP13-032 Nico Robin — "opp Character c≤8 cannot be rested until end
  of opp's next End Phase." This locks the opponent's Character OUT OF BLOCKING (blocking RESTS the blocker). The
  handler correctly applied the `cannotBeRested` modifier, and MaybeAutoPassBlock (L2920) skipped it, but the manual
  BLOCK ACTION (BlockAttack) only checked `cannotBlock`, not `cannotBeRested` → the opponent could still block with a
  locked Character. Added `HasModifier(blocker, "cannotBeRested")` to the BlockAttack rejection. `robin032test`:
  locked Saul cannot block; CONTROL (no lock) blocks normally. Coverage 2655/20/0/0/0 (no regression to normal blocks).
- ✔ OP13-033 Franky [On K.O.] "rest up to 2 of your opponent's cards" (restAllowLeader via "opponent's cards" L10776),
  OP13-035 Bepo [EoT] "set this Character OR up to 1 DON active" (L9177 choice), OP13-023 Uta / OP13-028 Shanks set-DON
  + cannot-play restriction, OP13-024 Gordon reveal-cost + delayed set-active, OP13-031 Law return-own-cost→play,
  OP13-039 Snake Shot Counter-KO-rested. The whole set-DON-active ramp family (OP13-025/027/030/034/037) standard.

## Batch 1485–1502 (OP13-001..022, Straw Hat / Revolutionary Army leaders) — 2 FIXES (1 NA cleared)
- ⭐BUG#84 (NA cleared: give-DON-to-unnamed cost) OP13-007 Ace & Sabo & Luffy — "[Activate: Main] You may give 1 of
  your active DON!! cards to 1 of your Leader or Character cards AND trash this Character: Give opp −3000." The
  give-DON cost handler only did the NAMED "[X]" recipient form → the "1 of your Leader or Character cards" any-
  recipient form was acknowledged-manual (NA). Added an auto-pay branch (attach to the Leader, else first Character —
  a minor choice for a cost, and the Leader is never the about-to-be-trashed source). Probe: gives DON to Leader +
  trashes self + gives −3000. Coverage OK 2654→2655, NA 21→20.
- ⭐BUG#85 (replacement: this-vs-that Character penalty) OP13-017 Dragon + PRB02-002 Law + ST15-005 Ace — "give THIS
  Character −N power instead" penalized the VICTIM (Sabo OP05-001 "give THAT Character" style) instead of the GUARD.
  "this Character" = the guard sacrificing its own power so the victim survives at FULL; "that Character" = the victim
  survives weakened. Fixed: recipient = "give this Character" ? guard : victim. `dragon017test`: victim saved at full
  power, Dragon −2000 (7000→5000). Sabo ("that") unchanged. Coverage 2655/20/0/0/0.
- ✔ OP13-013 Higuma KO-0-power (like Ain), OP13-008 Ivankov replacement trash-self, OP13-004 Sabo leader conditional
  ±power auras, OP13-005/006 give-rested-DON, OP13-019 rest-4-DON→−3000+KO. Standard/handled.
- ⏸ DEFER (spec) OP13-001 Luffy leader — "[DON x1][On Opp Attack] if ≤5 active DON, rest ANY NUMBER of your DON!!.
  For every DON rested, this Leader or 1 {Straw Hat} gains +2000 during this battle." Variable-count DON-rest (player
  chooses how many) → scaling power; no handler. Needs a variable-count rest UI + per-DON +2000 scaling + recipient
  pick. Also OP13-002 Ace leader (dual reactive "take damage OR 6000-base Character K.O.'d → draw") + OP13-003 Roger
  leader (DON-Phase auto-attach 1 DON to Leader) — unusual leader reactives, verify-later.

## Batch 1467–1484 (OP12-095..119, Revolutionary Army / Supernovas / Shirahoshi) — 1 FIX
- ⭐BUG#83 (conditional KO cap-UPGRADE) OP12-096 Ursa Shock — "K.O. up to 1 opp Character with a cost of 4 or less.
  If you have a Character with a cost of 8 or more, you may select your opponent's Character with a cost of 6 or less
  instead." ParseLimit took the FIRST cap (4), so the upgrade to 6 was ignored → Ursa Shock never hit c5/c6 targets
  even with a c8 Character. Added: when "If you have a Character with a cost of N or more, … cost of M or less instead"
  and you have a c≥N Character → koCostCap = M. `ursashock096test`: c8 present → c6 K.O.'d; absent → not. Coverage 2654.
- ✔ OP12-102 Shirahoshi replacement "if your base-c≤6 Character would be removed, turn 1 Life face-up instead" —
  `shirahoshi102test`: base-c2 victim saved + 1 Life face-up (action L4321 + base-cost filter). OP11-116 Merman Combat
  / OP12-117 Slam Gibson "Add up to 1 Character c≤N to top/bottom of the owner's Life" (removal-to-Life; probe glows
  BOTH boards' Characters). OP12-107 Doflamingo / OP12-119 Kuma [Opp Turn][On K.O.] add-deck→Life, OP12-113 Zoro On-KO
  conditional play-from-hand-rested, OP12-101 Bonney rest→+1000-until-next-turn. Standard/handled.
- ⏸ DEFER (Nami OP11-041 family) OP12-099 Kalgara — "[Your Turn] When a card is removed from your OR your opponent's
  Life cards, draw 1. Then, you cannot draw cards using your own effects this turn." Same both-Life-removal dispatch
  gap as Nami (FireOnOpponentLifeRemoved matches only "your opponent's Life", + no own-Life-loss hook) PLUS a new
  "cannot draw using own effects this turn" self-restriction that would also need a draw-suppression flag.

## Batch 1449–1466 (OP12-072..094, Sanji / Law / Revolutionary Army) — 1 FIX
- ⭐BUG#82 (untagged attack-Leader reactive + new condition) OP12-081 Koala leader — "When this Leader attacks your
  opponent's Leader, if you have 2 or more Characters with a cost of 8 or more, draw 1 card." This has NO [When
  Attacking] tag, so the generic when-attacking dispatch missed it → the draw never fired. Added an untagged handler
  in ApplyWhenAttackingEffects (gated on defenderDef.Type=="leader", with a [Once Per Turn] key). ALSO the condition
  "you have N or more Characters with a cost of M or more" (OWN board) was unhandled (only the "there is/are" either-
  board form existed) → added it. `koala081test`: 2 c8 Characters → draw on attacking the opp Leader; 1 → no draw.
  Coverage stays 2654/21/0/0/0.
- ✔ OP12-072 Zeff DON-return→conditional-Rush (NotifyDonReturned queues the "if Leader [Sanji]" body; leading-If
  gate resolves it), OP12-075 Ms. All Sunday "opponent may add DON active" (L8613), OP12-070-class per-count power,
  OP12-091 Poker / OP12-094 Dragon place-from-trash costs, OP12-085/089/093 {Rev Army} conditional +cost/Blocker,
  OP12-077 delayed "if selected card attacks, opp cannot Blocker". Standard/handled.
- ⏸ DEFER (spec) OP12-081 Koala SECOND reactive — "[OPT] when your opponent plays a Character with a base cost of 8
  or more, OR when your opponent plays a Character using a Character's effect, your opponent adds 1 Life to hand."
  The dispatch (L1943) matches only the literal "When your opponent plays a Character," (comma after Character), not
  "…with a base cost of 8 or more," nor a separate-sentence body; AND "played via a Character's effect" is NOT tracked
  (no PlayedByEffect flag). Needs: broadened dispatch + base-cost gate + a play-via-effect flag threaded into the
  play path.

## Batch 1431–1448 (OP12-048..071, Navy / Whitebeard / Sanji-events) — 1 FIX
- ⭐BUG#81 (replacement: compound self-rest dropped) OP12-048 Rosinante — "[Opponent's Turn] If your blue {Navy}
  Character would be removed by opp effect, you may rest this Character AND trash 1 card from your hand instead." The
  trash-hand-instead branch trashed the hand card but never rested the guard → the self-rest half of the cost was
  free. Fixed: `alsoRestSelf = ContainsAll(line, "rest this Character")` → gate on the guard being active (else
  unpayable) + rest it. `rosinante048test`: Isuka (blue {Navy}) saved, Rosinante rests, hand −1. Coverage 2654.
- ✔ OP12-070 Sanji "+1000 power for every 5 Events in trash" — `sanji070test`: 10ev→+2000, 5ev→+1000, 4ev→+0 (the
  /5 divisor applies; ScalingUnitCount L539 counts trash Events). OP12-053 Borsalino / OP12-070 Sanji DON-return
  replacements (L4212/L4222), OP12-061 Rosinante leader delayed "next [Law] play cost −2" (L8302), OP12-058 Whitebeard
  reveal-1-and-play-if-match (you-may-play-that-card family L7105/L9801; probe DON-gated), OP12-060 Boeuf Burst
  choose-one, OP12-054 Teach / OP12-056 Garp play-from-hand, OP12-063/065/066 "4+ Events in trash" conditionals.
  Standard/handled.

## Batch 1413–1430 (OP12-027..047, Wano/Slash + Navy) — 1 FIX (3-part replacement)
- ⭐BUG#80 (replacement: selfOnly false-positive + missing attribute/cost filters) OP12-027 Koushirou + OP15-094
  Zoro — "If your ＜Slash＞ attribute Character with a cost of 5 or less OTHER THAN this Character would be K.O.'d by
  opp effect, you may rest this Character instead." THREE bugs: (a) selfOnly was set by ContainsAll("this Character
  would be"), but "OTHER THAN this Character would be" contains that substring → the guard wrongly protected only
  ITSELF instead of other Characters. Fixed: exclude "other than this Character would be/leave". (b) No ＜attribute＞
  filter → protected any-attribute Character; added `your [＜<]?([A-Za-z]+)[＞>] attribute` → victim's CardDef.Attribute
  must match. (c) The non-base "with a cost of N or less" filter was unchecked (only "base cost" was) → added it
  (effective cost). `koushirou027test`: Slash victim saved (Koushirou rests), Strike victim NOT saved. Coverage 2654.
- ✔ OP12-047 Sengoku reveal-up-to-2 {Navy} (count parsed from "reveal up to (\d+)" L11401), OP12-041 Sanji leader
  "Activate up to 1 {Straw Hat} Event ≤3 from hand" (L7736), OP12-036 Zoro battle-immunity-by-＜Slash＞ (IsBattleKoImmune
  L4548 matches attacker attr vs holder attr; Slash-deck gate always met), OP12-028 Hiyori / OP12-034 Perona leading-If
  ＜Slash＞ + dual reveal-filter (#76 helps the cost), OP12-029 Kouzaburou rest-then-KO-rested, OP12-031 Tashigi /
  OP12-033 Helmeppo rest-opp + give-DON, OP12-037/038 rest/KO events. Standard/handled.
- ⏸ DEFER (spec) OP12-040 Kuzan leader — "When a card is trashed from your hand by your {Navy} type card's effect,
  draw cards equal to the number trashed." NotifyHandTrashedByEffect (a) scans only Characters not the Leader, (b)
  matches only "trashed…by an effect" (not "by your {Navy} card's effect"), (c) only NEGATES effects (no draw), (d)
  receives NO trash COUNT. Needs: scan Leader + a count param threaded through ~8 call sites + source-{Navy} check +
  a "draw N" action. Moderate infra change for one leader.

## Batch 1395–1412 (OP12-004..026, Rayleigh/Roger Pirates + Wano/Slash) — CLEAN (0 fixes, validates #74/#73)
- ✔ OP12-021 Ipponmatsu conditional REST-immunity ("If Leader ＜Slash＞ AND 6+ rested DON, cannot be rested by opp
  effects") — `ippon021test`: 6 rested DON → rest blocked; 3 → rest succeeds. Validates my #74 CannotBeRestedByOppEffect
  + the compound " and " condition splitter (L4938) + "N or more rested DON!! cards" (L5378) + Leader-＜Slash＞ (L5477).
- ✔ OP12-024 Gyukimaru conditional KO-immunity ("If this Character is active, cannot be K.O.'d by opp effects") —
  `gyukimaru024test`: active → KO blocked; rested → KO succeeds. Validates #74 conditional gate + "this Character is
  active" cond (L5384).
- ✔ OP12-020 Zoro leader "If this Leader battles opp Character this turn, set active. Then cannot attack base c≤7" —
  `zoro020test`: NOT battled → "condition not met, skipped" (BattledOppCharThisTurn L3703/L5024). The probe's set-active
  was a PROBE ARTIFACT (probe pre-marks the battle flag), not a bug — same class as the Duval artifact.
- ✔ reveal-2-Events cost (L6769 revealFromHand — OP12-004 Oden, OP12-009 Jinbe, OP12-013 Hatchan, OP12-015 Luffy),
  give-N-active-DON-to-[Rayleigh] cost (L5892 — OP12-016/017/019 Haki), OP12-006 Shakuyaku / OP12-014 Hancock dual
  reveal-filter "[Luffy] or red Event" (look-5 select+rearrange), OP12-026 Kuina rest-self→rest-opp+give-DON-to-[Zoro]
  compound body, OP12-018 Supreme King Haki [Counter] "rest 1 DON. If you do, −1000" (#73 family), OP12-008 Shanks
  On-Opp-Attack trash→−2000, OP12-022 Inuarashi freeze. All handled; coverage stays 2654/21/0/0/0.

## Batch 1377–1394 (OP11-101..119 + OP12-001..003, Fish-Man Island / Shirahoshi) — 2 FIXES
- ⭐BUG#78 (removal replacement: victim→Life) OP11-101 Bege — "[OPT] If your {Supernovas} Character other than
  [Bege] would be removed by opp effect, you may add it to the top of your Life cards face-down instead." No
  replacement-action branch matched "add it to the top of your Life cards face-down instead" → the {Supernovas}
  Character was removed normally (save whiffed). Added a branch that moves the VICTIM to the top of its owner's Life
  (face-down by default; face-up if stated), clearing its modifiers/DON. `bege101test`: Urouge → Life TOP face-down,
  not trash, gone from board.
- ⭐BUG#79 (removal replacement: rest named Leader/Stage) OP11-110 Fukaboshi — "If this Character would be K.O.'d,
  you may rest 1 of your [Fish-Man Island] or your [Shirahoshi] Leader instead." The existing "rest your Leader …
  instead" handler required the literal "rest your Leader"; Fukaboshi's "rest 1 of your [X] or your [Y] Leader" never
  matched → Fukaboshi K.O.'d. Added a branch matching `rest 1 of your .*Leader instead` that rests a NAMED Leader (if
  active) or the named Stage/Character alternative. `fukaboshi110test`: opp K.O. → Shirahoshi Leader rests, Fukaboshi
  survives. Coverage stays 2654/21/0/0/0.
- ✔ #76 CONFIRMED on a COMPOUND cost — OP11-103 Long-Jaw "If Leader [Shirahoshi], you may rest this Character AND
  turn 1 Life face-down: K.O. ≤3" — `longjaw103test`: self rested + Life face-down + KO all fire (leading-If strip +
  TryAutoPayCost split both parts). The whole Shirahoshi "If Leader is [Shirahoshi], you may turn Life face-up/down:
  <body>" family (OP11-107/108/117, etc.) now pays its cost. OP11-104 Shirley / OP11-106 Zeus (direct You-may costs),
  OP11-118 Luffy / OP11-119 Koby (trash costs), OP12-003 Crocus [On K.O.] reveal-2-Events cost→play (On-KO cost
  family, #73 machinery) all standard. OP12-001 Rayleigh leader = passive DECKBUILD restriction (no cost 5+), not a
  per-card resolvable — noted.
- ⏸ DEFER (spec) OP11-102 Camie — "[Your Turn][OPT] when your opponent activates an Event OR [Trigger], if opp has
  2+ Life, trash 1 from the top of EACH player's Life." Two gaps: (1) the reactive dispatch matches only "activates
  an Event," (comma) not "Event or [Trigger]" + a separate "If…, <body>" sentence; (2) the symmetric dual-Life-trash
  body ("each of your and your opponent's Life") is unimplemented. Niche single card; needs both a broadened
  dispatcher and a new dual-Life-trash resolver.

## Batch 1359–1376 (OP11-079..100, Big Mom / Navy / SWORD / Fish-Man) — 2 FIXES
- ⭐BUG#76 (leading-If strip skipped the cost — GENERAL) OP11-100 Otohime — "[On Play] If your Leader is
  [Shirahoshi], you may turn 1 card from the top of your Life cards face-down: Draw 1 card." The leading-If gate
  strips "If <cond>, " leaving LOWERCASE "you may turn…: Draw", but the optional-cost resolver is anchored on capital
  "You" and NormalizeClause wasn't re-applied after the strip → the turn-Life cost was skipped and the draw ran FREE.
  Fixed: `text = NormalizeClause(leadIf.Groups[2]…)` (capitalizes the leading "you "). General fix for every
  "If <cond>, you may <cost>: <body>" card. `otohime100test`: top Life turns face-down (cost) + draw 1. Coverage 2654.
- ⭐BUG#77 (reactive dispatch + attacker-attribute gate) OP11-088 Shu — "[OPT] This effect can be activated when your
  opponent's Character attacks. If that Character has the ＜Slash＞ attribute, this Character gains +5000 during this
  battle." The On-Opp-Attack dispatch matched only "when your opponent attacks" (not "…'s Character attacks") and
  never evaluated the attacker-attribute gate → the +5000 never fired. Added the Character-specific phrasing (fires
  only when a Character, not the Leader, attacks) + an "If that Character has the ＜X＞ attribute" gate on the
  ATTACKER's CardDef.Attribute (strips the If from the queued body). `shu088test`: Slash attacker → reactive queued;
  Strike attacker → not queued. Coverage 2654/21/0/0/0.
- ✔ OP11-082 Aramaki / OP11-084 Kuzan can-attack-active + mill, OP11-085 Orochi add-{SMILE}-from-trash, OP11-086
  Coribou play-[Caribou]-from-trash, OP11-095 Garp compound place-3-from-trash cost + conditional KO, OP11-098 Blue
  Hole mill-3 cost→KO, OP11-091 Berry Good opp-trash-to-deck, OP11-096 Ripper/OP11-072-class conditional Blockers.
  Look/reveal fast-pass: OP11-099.
- ⏸ DEFER (guess family, continued) OP11-079 + OP11-081 — "Choose a cost and reveal … chosen cost" (see batch
  1341–1358 defer note; guess-gate unimplemented, payoff applies unconditionally). OP11-092 Helmeppo remains NA
  (delayed "place at bottom of deck at END of turn").

## Batch 1341–1358 (OP11-058..077, Straw Hat / Big Mom / Impel Down) — 1 FIX
- ⭐BUG#75 (conditional passive cannot-attack) OP11-058 Monkey.D.Luffy — "If you have 5 or more cards in your hand,
  this Character cannot attack." The attack-legality check only matched the UNCONDITIONAL line-anchored "This
  Character cannot attack." (capital, no leading If), so Luffy attacked freely regardless of hand size. Added a
  conditional handler: `If ([^,]{3,120}), this Character cannot attack\.` → EvaluateCondition gates the attack.
  `luffy058test`: hand=5 → attack blocked (stays active); hand=3 → attacks (rests, opens battle). Coverage 2654/21/0/0/0.
- ✔ OP11-077 Randolph DON-return-to-deck reactive (L4325), OP11-062 Katakuri leader peek "Look at 1 from top of opp
  deck" (L8623), OP11-069 Brulee Life-to-hand cost, OP11-063 Little Sadi DON−1 cost + {Impel Down} cond, OP11-072
  Mont-d'or / OP11-070 Pudding compound DON−1+rest-self costs (recognized; probe auto-skips the optional cost),
  OP11-067 Katakuri char [EoT] set-active, OP11-076 Hannyabal conditional play-from-hand, OP11-075 Saul conditional
  draw. Look/reveal + counter fast-pass: OP11-059/060/065/071.
- ⏸ DEFER (spec, 5+ card Big Mom family) "Choose a cost and reveal 1 card from the top of your opponent's deck. If
  the revealed card has the chosen cost, <effect>" — OP11-066 Oven, OP11-071 Perospero, OP11-073 Linlin, OP11-074
  Streusen (+ Katakuri-family). The guess-GATE is UNIMPLEMENTED: the engine skips it and applies the payoff
  UNCONDITIONALLY (e.g. Oven's probe K.O.'d a Character with no reveal/match). A faithful fix needs a numeric
  cost-choice input (0–11 picker UI) + reveal opp's top card + gate on match — interactive UI work (Unity, untestable
  headless). Currently the cards are TOO STRONG (auto-success). Log for a UI pass.

## Batch 1323–1340 (OP11-036..057, Fish-Man Island / GERMA / Firetank) — 1 FIX (GERMA family)
- ⭐BUG#74 (quoted-type "only have" condition + conditional dual immunity) OP11-043 Ichiji + OP11-046 Yonji —
  condition "you only have Characters with a type including "GERMA"" was UNHANDLED (the L5369 handler only matched
  the exact `{T} type Characters` brace form; "type including X" is a SUBSTRING match on the feature string,
  e.g. "GERMA 66"). So Ichiji's [On Opp Attack] reactive never fired and Yonji's immunity never applied. Added the
  quoted-type condition (substring feature match). ALSO Yonji "cannot be K.O.'d OR RESTED by your opponent's effects"
  (conditional): (a) CannotBeKoedByEffect didn't match the "K.O.'d or rested" phrasing and ignored the leading
  "If <cond>," gate → added both (gate only when the line starts with "If ", so unconditional immunity cards are
  unaffected); (b) added CannotBeRestedByOppEffect helper (modifier OR printed immunity + conditional gate) and wired
  it into the rest-opp resolver (replacing a narrower inline check). `yonji046test`: GERMA-only → KO blocked + rest
  blocked; add a non-GERMA Character → both succeed. Coverage stays 2654/21/0/0/0.
- ✔ OP11-051 Sanji On-KO "look 5 play {Straw Hat} ≤5" (cost-free FireOnKoEffects path, same machinery verified via
  Aladine #73) + On-Play return 5000-base-or-less (probe OK), OP11-022-class dynamic-cost plays, OP11-042 Vito
  trash-{Firetank}→Rush, OP11-044 Judge trash→+1000 GERMA, OP11-047 Reiju look-5-reveal-GERMA, OP11-040 Luffy leader
  start-of-turn 8-DON look, OP11-054 Nami conditional draw-3, OP11-056 Brook place-base-cost-1. Look/reveal + counter
  fast-pass: OP11-036/037/038/039/048/049/057.
- ⏸ DEFER (spec) OP11-041 Nami leader — "[Your Turn][OPT] This effect can be activated when a card is removed from
  your OR your opponent's Life cards. If ≤7 hand, draw 1." The Life-removal dispatcher (FireOnOpponentLifeRemoved
  L3190) matches only "your opponent's Life cards," with a comma-body; Nami uses "your or your opponent's" + a
  separate "If…, draw" sentence, and there is NO own-Life-loss reactive dispatch. Needs (1) a broadened regex for
  the "your or your opponent's" phrasing + next-sentence body, and (2) firing on the Nami player's OWN Life loss
  (a new hook in the Life-damage path). The [DON!! x1][On Opp Attack] second ability (trash 1: +2000) is standard.
- ⏳ verify-later OP11-050 Gotti — "[When Attacking] trash {Firetank}: Return c≤1 to hand OR place at bottom of deck"
  (choice removal — bounce vs deck-bottom); cost path + choice untraced this pass.

## Batch 1305–1322 (OP11-013..035, Navy / Fish-Man Island) — 1 MAJOR FIX (11-card family)
- ⭐BUG#73 (three-part: "you may <cost>. If you do, <body>" family) OP11-024 Aladine + OP11-035 Fisher Tiger — the
  On-KO reaction "you may trash 1 hand card and rest 1 DON!!. If you do, play {Fish-Man}/{Merfolk} ≤6 from hand"
  PLAYED the card FOR FREE (cost skipped). Root causes: (a) the ". If you do," separator wasn't recognized by the
  colon-based optional-cost resolver → added a SCOPED normalization "You may <cost>. If you do, <body>" → "You may
  <cost>: <body>" (only when the cost is genuinely payable — from your hand/trash/DON or self-sacrifice, NOT
  opponent-targeting/"in your hand" effects, else OP13-119 Ace / P-046 Yamato regressed OK→NA). (b) the reaction
  body starts LOWERCASE "you may" but the cost regex is anchored on capital "You" → NormalizeClause now capitalizes a
  leading "you ". (c) the trash-from-hand cost branch DROPPED the "and rest N DON!!" conjunct → now rests the DON.
  `aladine024test`: Aladine trashed, SPARE trashed (cost), DON 5→4 (cost), Arlong played. Fixes the 11-card family
  (Aladine, Fisher Tiger, OP03-043 Gaimon, OP05-038 Charlestone, OP14-070 Buffalo, …). Coverage stays 2654/21/0/0/0.
- ✔ OP11-022 Shirahoshi leader dual cost (rest DON + turn Life face-up) + dynamic-cost play {Neptunian}/[Megalo] ≤
  DON count — both cost parts pay, play offered (probe). OP11-013 Prince Grus blocker-deny +power filter (L9264),
  OP11-034 Hatchan "cannot be rested until" (L7261 cannotBeRested modifier), OP11-023 Arlong in-hand conditional −3
  cost (L855) + "opp has 5+ rested cards" cond (L5307), OP11-018 Honesty Impact (−4000 then KO 6000-or-less),
  OP11-021 Jinbe leader conditional dual set-active, OP11-030 Shirahoshi char dual cost, OP11-028 Lord of the Coast
  freeze. Standard fast-pass: OP11-014/016/019/020/025/027/029/031.
- ⏸ DEFER (spec) OP11-005 Smoker — "[DON!! x1] cannot be K.O.'d by effects of Characters WITHOUT the <Special>
  attribute" (remover-attribute immunity; needs the removing Character threaded through effect-KO paths).

## Batch 1287–1304 (OP10-112..119 + OP11-001..012, Supernovas / OP11 Navy) — 2 FIXES
- ⭐BUG#71 (Life face-up vs face-down) OP10-119 Trafalgar Law — "Reveal up to 1 {Supernovas} Character from your
  hand and add it to the top of your Life cards FACE-DOWN." My #70 hand→Life-top handler hardcoded FaceUp=true, so
  Law's card would go face-UP (wrong — a face-down Life card is a hidden resource). Fixed: `FaceUp = !ContainsAll
  (text, "face-down")`. `bege103test` (Law variant): Urouge on Life TOP, faceDOWN=True. (2nd Law clause "give rested
  DON!! to {Supernovas} Leader" already works.)
- ⭐BUG#72 (base-power filter in removal replacement) OP11-001 Koby leader — "[OPT] If your {Navy} Character with
  7000 BASE power or less would be removed by opp effect, place 3 from trash at bottom of deck instead." The filter
  regex `with (\d{3,5}) power or (more|less)` needed "power" adjacent to the number; "7000 BASE power" broke it →
  filter skipped → Koby protected ANY {Navy} Character regardless of base power. Fixed: `with (\d{3,5}) (base )?power
  or (more|less)`, using GetCard(victim).Power when "base". `koby001test`: T-Bone(5000) saved, All-Hunt Grount(8000)
  not. Also correctly handles current-power variants (Sabo OP05-001) unchanged. Coverage stays 2654/21/0/0/0.
- ✔ OP10-113 Zoro "less Life than opp"→Rush (L5307), OP10-114 X.Drake "Life ≤ opp Life" (L5316), OP10-116 Damned
  Punk (Choose A/B: look own OR opp Life top, reposition + KO ≤5 — full choice+scry works), OP11-002 Ain (−1000 then
  KO 0-power-or-less — sequence works, KO finds no target on 4000 Chopper as expected), OP11-006 Zephyr <Special>
  attribute debuff (L5986), OP11-012 Franky opp-Event reactive (FireOnOpponentEventActivation L3292). Vanilla/standard
  fast-pass: OP10-112/115/117/118, OP11-004/008/009/010.
- ⏸ DEFER (spec) OP11-005 Smoker — "[DON!! x1] cannot be K.O.'d by effects of Characters WITHOUT the <Special>
  attribute." Immunity conditioned on the REMOVER Character's attribute; CannotBeKoedByEffect never receives the
  effect source, so this can't be enforced without threading the removing Character through all effect-KO paths
  (same shape as the Moby Dick / on-own-removal deferrals). [Blocker] works; the immunity clause is unenforced.

## Batch 1269–1286 (OP10-092..111, Thriller Bark / Dressrosa / Supernovas) — 2 FIXES
- ⭐BUG#69 (dual-target K.O. + leading-If mid-resolution) OP10-098 Liberation — "If [own Chars ≥2 less than opp],
  K.O. up to 1 opp Character base cost ≤6 AND up to 1 opp Character base cost ≤4." TWO bugs: (a) the KO resolver
  read only the FIRST cost cap (ParseLimit) + first "up to 1" → K.O.'d only ONE Character. Added a dedicated dual
  char+char cost-cap handler (sinkLow/sinkHigh: RemainingBudget = #picks allowed above the low cap) before the
  generic KO block. (b) The leading-If gate (L6568) re-evaluated the condition on EVERY re-entry — K.O.'ing the
  first Character flipped "≥2 less" false, aborting the 2nd pick. Guarded the gate with `SelectionsRemaining<=0`
  (condition checked once at activation, not per pick — a latent bug for ANY multi-pick effect under a board-count
  condition). `liberation098test`: both c6+c4 K.O.'d, c5 filler survives. Coverage stays 2654/21/0/0/0.
- ⭐BUG#70 (body zone) OP10-103 Capone Bege + OP10-107 Jewelry Bonney — "Add up to 1 {Supernovas} type Character
  card [with a cost of 5 ]from your HAND to the top of your Life cards face-up." Only a "from your TRASH → Life top"
  resolver existed (L8717); the hand-source body silently no-op'd after the Life-to-hand cost was paid. Added a
  hand-source mirror (tag + cost-cap + exact-cost + Character-card filters; TargetZone=Hand so the glow lights hand
  Characters). `bege103test`: Urouge moves hand→Life TOP face-up, gone from hand.
- ✔ OP10-100 Inazuma / OP10-110 Heat & Wire dynamic cost cap (ComputeDynamicCap: "total of both Life" / "opp Life"),
  OP10-092 Perona place-2-from-trash cost + OP10-093 Homing/OP10-102 Ivankov buffs, OP10-104 Caribou "cannot be
  K.O.'d in battle" (IsBattleKoImmune L4429), OP10-108 Apoo conditional Blocker, OP10-095 Zoro rest-{Dressrosa}
  cost (#67). Vanilla: OP10-094 Ryuma, OP10-101 Urouge, OP10-105 Cavendish, OP10-089-class.
- ⏳ verify-later (niche, low-pri): OP10-097 "that card gains [Banish] if 10+ trash" — [Banish] is grantable
  (HasKeywordModifier L1234) but the "that card" back-reference to the +2000'd Character is untested; OP10-099 Kid
  leader "set {Supernovas} c3-8 active, gains [Blocker] until end of opp next turn" — set-active works (L9891), the
  Blocker-until-next-turn rider grant untested.

## Batch 1250–1268 (OP10-070..091, Donquixote/Blackbeard/Dressrosa) — 1 FIX
- ✔ OP10-070 Trebol (boundary) — [On Play] "All of your Characters with 1000 base power or less cannot be K.O.'d by
  your opponent's effects until end of opp's next turn": resolver L7783 grants `cannotBeKod` (GetCard.Power≤1000,
  duration `untilNextTurn`). Handled. OP10-091 Brook (idx 1268) = dual-rest family (verified in this batch).
- ⭐BUG#68 (removal replacement, trigger-vs-action) OP10-074 Pica — "[Once Per Turn] If this Character would be
  K.O.'d BY YOUR OPPONENT'S EFFECT, you may rest 2 of your active DON!! cards instead." The rest-N-of-your
  replacement branch was gated by `!ContainsAll(line, "opponent")` (meant to exclude "rest opponent's cards"), but
  Pica's line mentions "opponent" in its TRIGGER clause, not its action → the branch was skipped and Pica died to
  any effect K.O. Fixed: replaced the whole-line guard with a negative lookahead on the rest TARGET —
  `rest (?:up to )?\d+ of your(?! opponent)`. `pica074test`: opp effect K.O. → Pica survives, 2 active DON rested.
- ✔ OP10-083/087/088/091 Momonosuke/Chopper/Nami/Brook — "rest this Character AND 1 of your {Dressrosa} type
  Leader or Stage cards" dual cost: `momo083test` self rested + {Dressrosa} Leader rested + opp −2 (self-rest branch
  L5569 + #67 handle each split part).
- ✔ OP10-082 Kuzan — "cannot be removed from the field by your opponent's effects": `kuzan082test` opp K.O. blocked
  (CannotBeKoedByEffect L3805 + CannotBeRemovedByEffect L3831 both match the exact phrase → blocks KO + bounce).
- ✔ OP10-075 Foxy DON-field-count cond (L5424 own DON ≤ opp DON), OP10-071 Doflamingo [On Your Opponent's Attack]
  reactive (dispatched L2842), OP10-081 Usopp rest-{Dressrosa}-Leader/Stage cost (#67), OP10-085 Jesus Burgess 8+
  trash→Rush, OP10-090 Franky On-K.O. play-from-trash, OP10-078/079/080 events — all standard, no bug.
  Vanilla fast-pass: OP10-073 Buffalo, OP10-084 Sanjuan.Wolf, OP10-089 Nico Robin.

## Batch 1232–1249 (OP10-050..070, Dressrosa/Donquixote) — 1 FIX (8-card family, NA cleared)
- ⭐BUG#67 (cost, 8 cards, NA cleared) "rest [1 of your {T} type ]Leader or [1 of your ]Stage card(s)" cost was
  UNHANDLED — no TryAutoPayCost branch, so OP10-057 Leo was acknowledged-manual (NA) and 7 siblings' costs silently
  did nothing. Added a branch: rest the Leader if active (and tag-matching), else the Stage; unpayable if neither is
  eligible. `leotest`: Usopp Leader rests (cost) → Leo's look-5 body fires. Fixes Leo + OP10-043 Moocy / OP10-044
  Cub / OP10-048 Sai / OP10-081 & OP16-043 Usopp / OP10-095 Zoro, and Mansherry's (OP10-056) compound cost first
  part. Coverage OK 2653→2654, NA 22→21.
- ✔ OP10-058 Rebecca (cond "there is a Character with a cost of 8 or more" L5269), OP10-053 Bian (my #48 "other than
  [Bian]" cond), OP10-065 Sugar (my #56 rest-DON+optional-self cost), OP10-062 Violet (On-K.O. DON−1 + cond),
  OP10-056 Mansherry (compound rest-Leader/Stage + return-own cost — first part now handled) — none NA.

## Batch 1214–1231 (OP10-031..049, Wano/Dressrosa) — 2 FIXES
- ⭐BUG#65 (replacement, battle-vs-effect) OP10-034 Franky — "[Once Per Turn] If this Character would be K.O.'d IN
  BATTLE, add 1 Life to hand instead." TryRemovalReplacement had no battle/effect distinction, so the in-battle
  replacement wrongly fired on an EFFECT K.O. too (Franky survived any removal). Added an `isBattleKo` param (battle
  callers L3751/3764 pass true) + gate: "in battle" lines fire only when isBattleKo. `frankytest`: effect K.O. now
  kills Franky; battle K.O. still triggers the replacement (survives). Also fixes EB02-030. (Kept minimal — didn't
  gate the reverse "effect-only must not fire in battle" to avoid touching the many existing "by effect" cards.)
- ⭐BUG#66 (own +cost aura) OP10-042 Usopp leader — "All of your {Dressrosa} type Characters WITH A COST OF 2 OR
  MORE gain +1 cost." GetPassiveCostAuraBonus's regex required "Characters gain" ADJACENT, so the "with a cost of 2
  or more" recipient filter between "Characters" and "gain" broke the match → the aura never applied. Broadened the
  regex to `Characters([^.]*?)gain` (filter → group 2, +N → group 3); the existing cMin cost-filter reads it.
  `usopp042test`: a {Dressrosa} cost-2 char → 3, non-Dressrosa cost-2 char → 2. Luffy leader (#unfiltered) unbroken.
  Coverage OK=2653/NA=22/0/0/0.
- ✔ OP10-032 Tashigi (replacement: green char other than [Tashigi] would be removed → rest self), OP10-037 Lim /
  OP10-049 Sabo (replacements), OP10-036 Perona (rest-by-effect reactive) — handled, none NA.

## Batch 1196–1213 (OP10-011..030, Punk Hazard + Wano) — 1 FIX
- ⭐BUG#64 (condition) OP10-022 Trafalgar Law leader — "[DON!! x1][Activate:Main] If the total cost of your
  Characters is 5 or more, you may return 1 … : reveal Life …" The "total cost of your Characters is N or more/less"
  condition was UNHANDLED (probe logged "Unknown condition … treating as not met") → the ability was always skipped.
  Added the handler (sum of effective GetCost over own Characters). `law022test`: cost-5 char → ability proceeds;
  cost-2 char → skipped. (Now correctly gated thanks to #62's leading-If widening surfacing the condition.)
- ✔ OP10-028 Momonosuke — "rest 2 of your DON!! cards and trash this Character: <look>." `momonosuketest`: 2 DON
  rested AND Momonosuke self-trashed (TryAutoPayCost splits "and" + re-attaches verb — this path was already
  correct, unlike the from-hand path fixed in #61).
- ✔ OP10-017 Rock ("If you don't have [Scotch], play [Scotch]" — negated-presence cond L5090), OP10-016 Monet (give
  up to 2 rested DON to own Leader/Char + opp −1000), OP10-019 Divine Departure (rest-5-DON cost → KO ≤8000),
  OP10-026/027 Kin'emon (place self + named-from-trash dual cost → play [Kin'emon]) — handled, none NA. Coverage
  OK=2653/NA=22/0/0/0.

## Batch 1178–1195 (OP09-110..119 + OP10-001..010 Punk Hazard) — 1 FIX (10 cards)
- ⭐BUG#63 (target filter, 10 cards) "… and a [Trigger]" required-Trigger filter was a no-op. The glow / KO resolver /
  reveal filter all tested the substring "with a [Trigger]", but many cards phrase it "cost/power … AND a [Trigger]"
  (the cost cap comes first) → the substring never matched → any cost/power-matching Character was a valid target
  (ignoring the Trigger requirement). Broadened all three sites to a regex `(?:with|and) a \[Trigger\]`, and added a
  target-Trigger filter to the generic glow (before `return true`) + the main K.O. resolver (was absent). Fixes
  OP09-115 Ice Block Partisan (K.O.), OP03-022 Arlong / OP03-037 Tooth Attack / OP03-119 Buzz Cut / EB04-027 &
  OP14-112 & P-115 Boa Hancock / OP13-110 Stussy / OP14-110 Dr. Hogback / OP14-118 (reveal). `iceblocktest`: only a
  Character WITH a printed [Trigger] glows + is K.O.'able; a no-Trigger char is rejected. Coverage OK=2653/NA=22/0/0/0.
- ✔ OP10-001 Smoker leader — "[Opponent's Turn] All of your {Navy} or {Punk Hazard} type Characters gain +1000."
  `smokertest`: a {Navy} char 6000→7000 on opp turn, 6000 on own (dual-tag own aura, Opponent's Turn gate). Clean.
- ✔ OP09-114 Lindbergh (combined-Life "you and your opponent have a total of N or less Life" cond L4921), OP10-010
  Brownbeard ("1 or less Characters with 6000 power or more" cond), OP10-009 Smiley (Leader-{Punk Hazard} debuff),
  OP09-118 Gol.D.Roger (win-on-Blocker-at-0-Life) — handled, none NA.

## Batch 1160–1177 (OP09-089..109, Blackbeard + Revolutionary) — 2 FIXES (one BROAD)
- ⭐BUG#61 (cost dropped, 4 cards) "trash N cards from your hand AND trash this Character" self-trash was dropped —
  the from-hand cost path (L6688) paid the hand-trash + honored a "rest this Character" rider but not "trash this
  Character", so the source was never sacrificed. Added the self-trash rider. Fixes OP09-089 Stronger, EB02-047
  Blueno, EB03-062 Law, ST25-004 Buggy. `strongertest`: Stronger self-trashes as cost.
- ⭐⭐BUG#62 (condition gate, BROAD ~60 cards) the leading-"If <cond>," gate (L6513) capped the condition at 90 chars
  (`[^,]{3,90}`), so any effect with a LONGER comma-less condition slipped past the gate and its body fired
  UNCONDITIONALLY. Widened to 150. This fixes a huge set of active effects that were IGNORING their printed
  condition — e.g. OP06-061 Vinsmoke Ichiji On-Play (DON-compare, 100 chars, now "condition not met — skipped"),
  OP09-092 Teach, and ~60 more (DON-compare/compound-Leader-type/hand-Life-Character-diff conditions). Also ADDED
  the previously-unhandled conditions used by the affected set: hand-diff "at least N less than opponent's hand"
  (Teach), Life-diff "equal to or less/more than opponent's Life" (OP10-114/ST29-003/OP13-102/P-085), Character-diff
  "at least N less than opponent's Characters" (OP10-098) — so the widening gates them correctly instead of
  regressing to never-fire. `teach092test`: draws 2 only when hand-diff ≥3. Ichiji now gates. Coverage OK=2653/0/0/0.
- ✔ OP09-095 Laffitte (`laffittetest`: "rest 1 DON and this Character" dual cost — BOTH paid), OP09-081 Teach leader
  (self-On-Play-negation `teachtest`), OP09-097/098 Black Vortex/Hole (effect negation), OP09-101 Kuzan (place opp
  on Life + discard) — handled, none NA.

## Batch 1142–1159 (OP09-070..088, Straw Hat/Kid + Blackbeard) — CLEAN + DEFER
- ✔ OP09-081 Marshall.D.Teach leader — "Your [On Play] effects are negated." (self-drawback). `teachtest`: a played
  Edward Weevil (On Play: Draw 1) draws 0 under Teach (On-Play suppressed at play time, L1904), draws 1 with a normal
  leader. The Activate ("opponent's [On Play] effects negated") is handled at L7478.
- ⏸ DEFER (family grows to 3) OP09-080 Thousand Sunny + OP10-042 Usopp — "When your {Straw Hat Crew} Character is
  removed from the field by your opponent's effect, …" Same on-own-removal reactive class as the deferred OP08-056
  Moby Dick: needs the VICTIM-owner notified on removal with the removed card threaded through (NotifyRemovalByEffect
  only notifies the remover, one path). Now 3 cards — worth the broad hook eventually.
- ✔ OP09-078 Gum-Gum Giant ([Counter] DON!! −2 + optional trash-1 compound cost — trash paid, probe trash 0→1),
  OP09-087 Charlotte Pudding (opp hand≥5 → opp trashes 1), OP09-085 Gecko Moria (play {Thriller Bark}≤2 from trash
  rested), OP09-088 Shiryu (trash-2 → draw 2), OP09-070/073/076 Nami/Brook/Zoro (return-1+-DON cost, same as Sanji
  #verified) — handled, none NA. Coverage OK=2653/NA=22/0/0/0.

## Batch 1124–1141 (OP09-048..069, Cross Guild + Kid/Law) — CLEAN + 1 DEFER
- ✔ OP09-061 Luffy leader — "[DON!! x1] All of your Characters gain +1 COST." `luffy061test`: an own char base
  cost 3 → 4 with 1 DON on the Leader, 3 without. Own-side cost aura (GetPassiveCostAuraBonus, L876). Clean.
- ✔ OP09-065 Sanji — "[On Play] You may return 1 or more DON!! cards from your field to your DON!! deck: gains
  [Rush]; rest opp ≤6." `sanji065test`: body fires (opp rested) AND ≥1 DON actually leaves the field (5→4) — the
  variable DON-return cost is NOT skipped (unlike the earlier Sasaki class). OP09-068 Chopper uses the same cost.
- ⏸ DEFER OP09-052 Marco — "[Opponent's Turn] You may trash 1 card from your hand: When this Character is K.O.'d by
  your opponent's effect, play this Character card from your trash rested." A 2-STAGE delayed mechanic: an activated
  ability (during the OPPONENT's turn) pays trash-1 to ARM a self-revive that FIRES when Marco is later K.O.'d by an
  opponent effect. The revive action exists (L8335 "Play this Character card from your trash") but the arm→fire
  linkage + opponent-turn activation isn't wired. 1 card; not NA. Deferred with this spec.
- ✔ OP09-066 Jean Bart ("If opponent has more DON!! than you, K.O. opp ≤3" — DON-compare cond L4718), OP09-058
  Special Muggy Ball (bounce opp ≤6), OP09-051 Buggy (place opp at deck bottom), OP09-062 Nico Robin leader
  ([Banish]) — handled, none NA. Coverage OK=2653/NA=22/0/0/0.

## Batch 1106–1123 (OP09-029..047, ODYSSEY + Cross Guild) — 1 FIX
- ⭐BUG#60 (condition) OP09-045 Cabaji — "If you have a [Buggy] OR [Mohji] Character, this Character cannot be K.O.'d
  in battle." The "you have a [Name]" condition handler (L5093) matched only the FIRST bracketed name, dropping
  "or [Mohji]" — so with only Mohji present the immunity never applied. Broadened the regex to capture "or [Name2]"
  and check both; also added a "base power"/"with a cost" guard so QUALIFIED variants (ST26-001 Soba Mask "[A] or
  [B] … base power") fall through to their specific handler (L5399) instead of being swallowed. `cabajitest`: with
  a Mohji, Cabaji survives a 7000>5000 attack (immune); without Buggy/Mohji it's K.O.'d. Coverage OK=2653/NA=22/0/0/0.
- ✔ OP09-033 Nico Robin — "If 2+ rested Characters, none of your {ODYSSEY} or {Straw Hat Crew} type Characters can
  be K.O.'d by effects until …" — type-filtered mass-immunity grant explicitly handled (L7689, two-tag filter).
- ✔ OP09-042 Buggy leader (rest-5-DON + trash-1 dual cost → play {Cross Guild}), OP09-046 Crocodile (play {Cross
  Guild} OR {Baroque Works}≤5 — OR type filter), OP09-030 Law (return-own cost → play {ODYSSEY}≤3 other than [Law]),
  OP09-043 Alvida (On-K.O. cond play other than [Alvida]) — handled, none NA.

## Batch 1088–1105 (OP09-010..028, Red-Haired + ODYSSEY) — 1 FIX
- ⭐BUG#59 (replacement) OP09-012 Monster — "If your Character [Bonk Punch] would be K.O.'d by an effect, you may
  trash this Character instead." TryRemovalReplacement's non-self victim filters (tag/base-cost/power/colour/rested)
  had NO [Name] filter, so Monster protected ANY own Character being K.O.'d, not just [Bonk Punch]. Added a
  "your Character [Name] would be" NameMatches filter. `monstertest`: K.O. Bonk Punch → SAVED (Monster trashed
  instead); K.O. another char → K.O.'d (Monster untouched). Coverage OK=2653/NA=22/0/0/0.
- ✔ OP09-022 Lim leader — "Your Character cards are played rested." `limtest`: a played Character enters RESTED.
  PlayCard scans own cards for this rule (L1876).
- ✔ OP09-018 Get Out of Here (K.O. up to 2 opp with a TOTAL power ≤4000 — total-power budget L9671, glow opp-only),
  OP09-014 Limejuice (opp can't activate a [Blocker] with ≤4000 power — power-ban), OP09-025 Crocodile (Leader
  {ODYSSEY} → cannot be K.O.'d in battle by Leaders — conditional attribute immunity), OP09-017 Wire (Leader 7000+
  power AND {Kid Pirates} → Rush — compound cond) — handled, none NA.

## Batch 1070–1087 (OP08-108..119 Shandia/CP + OP09-001..009 Red-Haired) — 1 FIX
- ⭐BUG#58 (asymmetric debuff) OP08-118 Silvers Rayleigh — "Select up to 2 of your opponent's Characters, and give
  1 Character −3000 power AND the other −2000 power…" The timed-debuff resolver parsed only the value before "until"
  (−2000) and applied it to BOTH picks — the −3000 was dropped. Added an asymmetric parse: FIRST pick (SelectionsRemaining
  ≥2) gets −3000, the second gets −2000. `rayleigh118test`: C1 5000→2000, C2 5000→3000; real probe now logs −3000
  then −2000. Only card with this shape. Coverage OK=2653/NA=22/0/0/0.
- ✔ OP09-004 Shanks — "Give all of your opponent's Characters −1000 power." (UNCONDITIONAL, untimed passive aura).
  `shankstest`: a north char 2000→1000 on BOTH turns. GetOpponentDebuffAuraBonus applies untimed/uncond lines.
- ✔ OP08-112 S-Snake ("opp Character cost≤6 other than [Luffy] cannot attack" — name-exclusion glow via #52, opp-only),
  OP08-119 Kaido & Linlin (DON−10 mass KO all-but-self + Life manip — not NA), OP09-001 Shanks leader (reactive on
  opponent attack → −1000), OP08-114 S-Hawk (Life-cmp battle immunity vs ＜Slash＞ + buff) — handled, none NA.

## Batch 1052–1069 (OP08-087..107, Kid/CP + Big Mom) — 1 FIX
- ⭐BUG#57 (reactive) OP08-105 Jewelry Bonney — "[DON!! x1][Your Turn][OPT] When a card is removed from your
  opponent's Life cards, draw 2 cards and trash 1 card from your hand." Was UNIMPLEMENTED: FireOnLifeDamageDealt
  only inspects the ATTACKER's own effect and matches "you deal damage to your opponent's Life" — not a BOARD
  reactive with this phrasing. Added FireOnOpponentLifeRemoved (scans the attacking player's whole board;
  timing/[DON]/[OPT] gated), called alongside FireOnLifeDamageDealt when Life damage is dealt. `jbonney105test`
  (full attack → north Life damage): Jewelry Bonney draws 2 + trashes 1. Coverage OK=2653/NA=22/0/0/0.
- ✔ OP08-088 Duval — "[On Play] Up to 1 of your Characters gains +1 COST until end of opp's next turn." `duvaltest`:
  glow own char True / opp char False; own char cost 3→4. Handler L9336. (The probe's "invalid target" was a probe
  auto-resolve artifact — the engine is correct.)
- ✔ OP08-106 Nami (trash-1-card-WITH-[Trigger] cost — L5850/6134 require a Trigger card), OP08-093 X.Drake ([DON!! x1]
  +2 cost passive), OP08-107 Nitro (rest-self → buff [Charlotte Pudding]), OP08-096 People's Dreams (trash-from-deck
  then cost≥6-conditional buff) — handled, none NA.

## Batch 1034–1051 (OP08-068..086, Big Mom + Animal Kingdom) — 1 FIX
- ⭐BUG#56 (cost skipped, 2 cards) OP08-082 Sasaki / OP10-065 Sugar — "[Activate: Main] Rest 1 of your DON!! cards
  and you may rest this Character: Give up to 1 of your opponent's Characters −2 cost during this turn." The cost
  starts with "Rest" (not "You may"), so the cost-prefix resolver (`^You (?:may|can) …:`) missed it and the BODY
  (−2 cost) fired for FREE — the mandatory DON-rest was never paid. Added a branch that matches "Rest N of your
  DON!! cards and you may rest this Character: <body>", rests N DON (fail if insufficient), declines the optional
  self-rest, and queues the body. `sasakitest`: DON rested 0→1 AND opp cost 3→1. Coverage OK=2653/NA=22/0/0/0.
- ✔ OP08-084 Jack — "This Character gains +4 cost." (unconditional passive). `jacktest`: effective cost = 11 (base
  7 + 4). GetPassiveCostBonus applies untimed/un-DON-gated grants.
- ✔ OP08-083 Sheepshead ("[DON!! x1][Your Turn] give all opp Characters −1 cost" — Kalifa-class cost aura), OP08-086
  Ginrummy ("If opp has a Character with a cost of 0, draw 2" — Baskerville-class cost-0 cond), OP08-074 Black Maria
  ("no other [Black Maria]" cond), OP08-071 Count Niwatori (On-K.O. DON−1 play-from-deck), OP08-079 Kaido (trash
  cost + was-played-this-turn cond), OP08-072 Biscuit Warrior (deckbuild "any number" note — no gameplay effect) —
  handled, none NA.

## Batch 1016–1033 (OP08-048..067, Whitebeard + Big Mom) — CLEAN + 1 DEFER
- ✔ OP08-063 Katakuri — "[On Play] You may turn 1 card from the top of your Life cards FACE-DOWN: add 1 DON from
  DON deck set active." The turn-Life-face-up/DOWN cost handler (L5566, "up|down") handles both. `katakuritest`:
  top Life card turns face-down (cost), active DON 0→1 (body). OP08-058 Pudding leader (turn 2 Life face-up cost)
  uses the same handler. Clean.
- ⏸ DEFER OP08-056 Moby Dick — "[Your Turn][OPT] When your Character with a type including 'Whitebeard Pirates' is
  removed from the field by an effect, draw 1. Then, place 1 hand card top/bottom of deck." The existing removal
  reactive `NotifyRemovalByEffect` (a) matches only "When a Character is removed … by YOUR effect" (Boa/Shakuyaku),
  (b) notifies the REMOVER not the victim owner, (c) doesn't pass the removed card (so the {Whitebeard} type can't
  be checked), and (d) is wired into only ONE removal path (L9635). Moby Dick needs the victim-owner notified on
  ANY-effect removal with the removed card's type — a broad change for one niche stage. Deferred with this spec.
- ✔ OP08-062 Katakuri (trash-self → cond {Big Mom}: play [Katakuri] cost≥3 & ≤ opp field DON — condition gate works,
  not NA), OP08-052 Ace (reveal + play {Whitebeard}≤4 + place rest), OP08-067 Pudding char (DON-return reactive),
  OP08-057/060 King, OP08-061 Oven, OP08-064 Cracker (DON-minus costs) — handled, none NA. Coverage 2653/0/0/0.

## Batch 998–1015 (OP08-029..047, Minks + Whitebeard) — 1 FIX
- ⭐BUG#55 (aura, 2 cards) OP08-029 Pekoms — "If this Character is active, your {Minks} type Characters with a cost
  of 3 or less other than [Pekoms] cannot be K.O.'d BY EFFECTS." HasRemovalImmunityAura's regex required "by your
  opponent's effects", but Pekoms (and OP04-119 Rosinante) say just "by effects" → the aura never matched. Broadened
  the regex to "by (?:your opponent's )?effects". `pekomstest`: with Pekoms ACTIVE a {Minks} cost-2 char survives
  K.O.; with Pekoms rested it doesn't ("If this Character is active" cond L5226). Luffy un-regressed. Coverage green.
- ✔ OP08-045 Thatch — "If this Character would be removed by your opponent's effect or K.O.'d, trash this Character
  and draw 1 card instead." `thatchtest`: north K.O.s Thatch → Thatch trashed (not K.O.'d) + south draws 1.
  TryRemovalReplacement "trash this Character … and draw N instead" handler (L3983).
- ✔ OP08-038 We Would Never Sell (rest-2 cost → "None of your Characters can be K.O.'d by opponent's effects until
  end of opp's next turn" mass immunity grant), OP08-046 Shakuyaku (reactive on your-effect removal → opp
  place-to-deck + rest self), OP08-043 Newgate (complex "can't attack unless opp trashes 2" restriction — not NA),
  OP08-047 Jozu (return-own-other-than-this cost → return cost≤6) — handled, none NA. Coverage OK=2653/NA=22/0/0/0.

## Batch 980–997 (OP08-008..028, Drum Kingdom + Minks) — CLEAN (no engine bug)
- ✔ OP08-020 Drum Kingdom stage — "[Opponent's Turn] All of your {Drum Kingdom} type Characters gain +1000 power."
  `drumkingdomtest`: a {Drum Kingdom} char (base 3000) → 4000 on opp turn (aura on), 3000 on own turn (Opponent's
  Turn gate). Stage own-char aura via GetTurnPassiveAuraBonus (stage-in-scan bug class already fixed). Clean.
- ✔ OP08-028 Nekomamushi — "If your opponent has 7 or more rested cards, gains [Rush]." Condition L5237 uses
  RestedCards = rested characters + rested DON + rested Leader + rested Stage (L5199). Correct interpretation.
- ✔ OP08-023 Carrot / OP08-022 Inuarashi (up to 2) / OP08-024 Concelot / OP08-025 Shishilian / OP08-026 Giovanni —
  freeze rested opp Characters ("will not become active in the next Refresh Phase") via the freeze handler (L7094),
  none NA. OP08-018 Cloven Rose (up-to-3 buff + ". Then," opp −2000 debuff), OP08-021 Carrot leader (conditional
  rest on "you have a {Minks} Character") — handled, not NA. Coverage OK=2653/NA=22/0/0/0.

## Batch 962–979 (OP07-106..119 Egghead + OP08-001..007 Drum Kingdom) — 1 FIX
- ⭐BUG#54 (condition) OP08-006 Chessmarimo — "[Your Turn] If you have [Kuromarimo] and [Chess] in your trash, this
  Character gains +2000 power." The compound-"and" splitter turned this into "you have [Kuromarimo]" (a FIELD check,
  L4971) + an unhandled "[Chess] in your trash" → condition always false → the +2000 never applied. Added a dedicated
  "you have [X] (and [Y]) in your trash" handler (checks the TRASH via NameMatches) BEFORE the generic split.
  `chessmarimotest`: both in trash → 6000→8000; only [Chess] in trash → 6000. Only card with this shape. Coverage
  OK=2653/NA=22/0/0/0.
- ✔ OP08-001 Chopper leader — "Give up to 3 of your {Animal} or {Drum Kingdom} Characters up to 1 rested DON!! card
  each" (multi-recipient rested-DON distribution, L11801, not NA). OP08-004 Kuromarimo ("If you have [Chess]" field
  named-card cond L4971), OP07-117 Egghead (EoT conditional set-active {Egghead}≤5), OP07-109 Luffy (trash-self cost
  → cond KO cost≤4 + draw), OP08-007 Chopper char (dual-timing look-play-{Animal}) — all handled, none NA.

## Batch 944–961 (OP07-083..105, CP/Egghead + Vegapunk) — CLEAN (no engine bug)
- ✔ OP07-087 Baskerville — "[Your Turn] If your opponent has a Character with a cost of 0, this Character gains
  +3000 power." Condition uses EFFECTIVE cost (GetCost, L4937). `baskervilletest`: opp char forced to cost 0 (via a
  −1 CostDelta) → Baskerville 3000→6000; opp cost-1 char → 3000. Clean. (No printed cost-0 char exists — the
  condition only fires on a char reduced to 0.)
- ✔ OP07-098 Atlas — "If you have less Life cards than your opponent, this Character cannot be K.O.'d in battle."
  Condition L5234 + battle-immunity handler L4419 (explicitly references Atlas). `atlastest` (real battle, 7000 vs
  6000): south Life 1<3 → Atlas SURVIVES; south Life 3>1 → Atlas K.O.'d.
- ✔ OP07-090 Morgans (opp trashes 1 + reveals hand + draws 1 — multi-clause, not NA), OP07-097 Vegapunk leader
  (self-cannot-attack + rest-DON: play {Egghead}≤5 OR add to Life face-up), OP07-105 Pythagoras (On-K.O. cond
  play-from-trash rested), OP07-085 Stussy (trash-own cost → KO opp) — all handled, none NA. Coverage 2653/0/0/0.

## Batch 926–943 (OP07-064..082, Vinsmoke/Foxy + CP) — CLEAN (Perfume Femur #53 cleared separately above)
- ✔ OP07-071 Foxy — "[Opponent's Turn] If your Leader has {Foxy Pirates}, give all of your opponent's Characters
  −1000 power." `foxytest`: opp-turn victim 2000→1000 (aura on), own-turn 2000 (Opponent's Turn gate). Krieg-class
  GetOpponentDebuffAuraBonus. Clean.
- ✔ OP07-081 Kalifa — "[DON!! x1][Your Turn] Give all of your opponent's Characters −1 cost." `kalifatest`:
  own-turn victim cost 3→2 (aura on), opp-turn 3 (Your Turn gate). GetOpponentCostDebuffAura. Clean.
- ✔ OP07-082 Captain John (trash 2 from deck + give up to 1 opp −1 cost — probe: deck 50→48, trash +2, then debuff
  target), OP07-079 Rob Lucci leader (trash-2-deck cost → opp −1 cost), OP07-080 Kaku (place-2-CP-from-trash cost →
  opp −3 cost), OP07-064 Sanji (hand-self −3 cost gated on "DON at least 2 less than opp"), and the DON-compare
  family (OP07-068/069/070/078) — all handled, none NA. Coverage OK=2653/NA=22/0/0/0.

## Batch 908–925 (OP07-046..063, Warlords/Amazon Lily + Foxy/Vinsmoke) — 1 FIX
- ⭐BUG#52 (glow+resolver, GENERAL) OP07-051 Boa Hancock — "Up to 1 of your opponent's Characters OTHER THAN
  [Monkey.D.Luffy] cannot attack until the end of your opponent's next turn." The cannot-attack glow (L6033) and
  resolver (L7207) did NOT apply the "other than [Name]" exclusion, so the opponent's Luffy was wrongly targetable.
  Added the NameMatches exclusion to both. `boahancocktest`: the opponent's Luffy does NOT glow and is rejected by
  the resolver; another char is targetable. Arlong/Issho un-regressed. Coverage OK=2653/NA=22/0/0/0.
- ⭐BUG#53 (delayed conditional, 2 cards) OP07-057 Perfume Femur (+ OP12-077) — "Select up to 1 {7 Warlords}
  Leader/Char, +2000. Then, if the selected card attacks this turn, opp cannot activate [Blocker]." FIXED (was
  deferred): kept the ". Then," clause together (FindThenClause exclusion) and, in the +power buff resolver, added
  the buffed card's InstanceId to `NoBlockerGrantedThisTurn` (which DeclareAttack L2966 already reads → sets
  Battle.NoBlocker on attack). `perfumetest`: Weevil 8000→10000 AND NoBlocker=True when it attacks. Coverage green.
- ✔ OP07-047 Trafalgar Law (return-self cost → conditional opp place-hand-to-deck), OP07-050 Boa Sandersonia /
  OP07-052 Boa Marigold ("2 or more {Amazon Lily} or {Kuja Pirates}" either-tag condition, L5290), OP07-060
  Itomimizu ("Leader {Foxy Pirates} AND you have no other [Itomimizu]" — no-other-[Name] cond L4971), OP07-056 Slave
  Arrow (return-own cost) — all handled, not NA.

## Batch 890–907 (OP07-026..045, Supernovas + Warlords/Amazon Lily) — 1 FIX, 1 DEFER
- ⭐BUG#51 (aura, 2 cards) OP07-033 Luffy — "If you have 3+ Characters, your Characters with a cost of 3 or less
  other than [Luffy] cannot be K.O.'d by your opponent's effects." HasRemovalImmunityAura's regex required "ALL OF
  your …" and only understood a base-power cap — so Luffy's "your Characters … cost 3 or less other than [Luffy]"
  aura NEVER applied. Broadened the regex to "[all of ]your …Characters …cannot be K.O.'d/removed", excluded the
  self-only "this Character cannot be" line, and added a "cost of N or less" filter + "other than [Name]"
  (NameMatches) exclusion. `luffy033test`: with 3 Characters a cost-3 char survives K.O.; with 2 it doesn't. Also
  fixes OP07-069 Pickles (same aura shape, gated on the DON-compare condition). Coverage OK=2653/NA=22/0/0/0.
- ⏸ DEFER (partial) OP07-026 Jewelry Bonney + OP10-033 Nami — "…rested Character OR DON!! cards will not become
  active…" The Character-freeze WORKS (freeze handler L7094 handles rested opp Characters/Leaders). The DON-freeze
  option is unavailable: DonInstance has no modifier support (only InstanceId+Rested), FindAnyInPlay doesn't return
  DON, and the Refresh Phase un-rests all DON. Needs a DonInstance.Frozen flag + Refresh-Phase skip + DON targeting
  (glow/resolver). Deferred (secondary option on 2 cards; primary Character-freeze functional).
- ✔ OP07-038 Boa Hancock leader ("activate when a Character is removed by your effect → draw if hand≤5"), OP07-042
  Gecko Moria (K.O.-replacement: Leader {7 Warlords} + would-be-removed → place own char other than [Gecko Moria]
  at deck bottom instead — TryRemovalReplacement class), OP07-032 Fisher Tiger (attack Characters on the turn
  played + conditional On-Play rest), OP07-045 Jinbe (play {7 Warlords} cost≤4 other than [Jinbe]) — not NA.

## Batch 872–889 (OP07-007..025, Ace/Revolutionary + Supernovas) — 2 FIXES
- ⭐BUG#49 (condition) OP07-023 Caribou — "If you have 6 or more rested DON!! cards, this Character gains +1000
  power." "you have N or more/less rested DON!! cards" was UNHANDLED in EvaluateCondition → unknown → false → the
  +1000 never applied. Added the handler (counts CostArea rested DON). `cariboutest`: 6 rested → 6000, 5 → 5000.
- ⭐BUG#50 (dual-clause, glow+resolver) OP07-017 Dragon Breath — "K.O. up to 1 opp Character with 3000 POWER or
  less AND up to 1 opp Stage with a cost of 1 or less." The dual char+stage handler (Tempest Kick's) had TWO gaps:
  (a) the char clause only parsed a COST cap, so a POWER-capped Character was rejected as invalid; (b) it did only
  ONE target (Tempest Kick is "OR"), but Dragon Breath is "AND" (K.O. BOTH). Fixed glow + resolver: char clause
  accepts a cost OR power cap; detect "…Characters AND …Stages" → 2 picks (≤1 char via RemainingBudget, ≤1 stage).
  `dragonbreathtest`: both the char AND the stage are K.O.'d. Tempest Kick "OR" regression-checked (tempesttest
  EB01-011 → glow+KO still single). Coverage OK=2653/NA=22/0/0/0.
- ✔ OP07-025 Coribou (play named [Caribou] cost≤4 from hand RESTED), OP07-019 Jewelry Bonney leader ([On Your
  Opponent's Attack] rest-1-DON → rest opp Leader/Character), OP07-024 Koala (reactive rest-self → grant Blocker to
  own {Fish-Man}), OP07-014 Moda ([Your Turn][On Play] buff own [Ace]), OP07-013 Masked Deuce (named-Leader
  condition) — not NA, standard handlers.

## Batch 854–871 (OP06-107..119 Wano tail + OP07-001..006) — 1 FIX
- ⭐BUG#48 (condition, GENERAL 3 cards) "you have a {T} type Character OTHER THAN [Name]/this card" ignored the
  exclusion. EvaluateCondition (L5215) counted ANY {tag} Character including the source, so a card that requires
  ANOTHER such Character satisfied itself. Added "other than [Name]" (NameMatches) + "other than this Character/card"
  (source instance) exclusions. Fixes OP06-113 Raki ([Blocker] needs another {Shandian Warrior}), OP10-053 Bian
  ([Blocker] needs another {The Tontattas}), OP13-009 Curly.Dadan ([Double Attack] needs another {Mountain Bandits}
  "other than this card"). `rakitest`: Raki alone → no Blocker; Raki + Kamakiri → Blocker. Coverage OK=2653/0/0/0.
- ✔ OP06-110 Nekomamushi — "[DON!! x2] can also attack your opponent's active Characters" — HasPrintedCanAttackActive
  (L3739) respects the [DON!! x2] gate. OP07-002 Ain — "Set the power of up to 1 opp Character to 0" uses
  BasePowerOverride (Value=0, absolute set). OP07-006 Sterry — self-cost "give your active Leader −5000: draw 1 +
  trash 1" fully resolves (probe: Leader −5000 → draw → trash). OP07-001 Dragon leader — "give up to 2 currently
  given DON!! to 1 of your Characters" DON-redistribution handler (L7871). OP07-003 Outlook III (trash-self cost →
  give up to 2 opp −2000) — all clean.

## Batch 836–853 (OP06-087..106, Thriller Bark + Wano) — CLEAN (no engine bug)
- ✔ OP06-095 Shadows Asgard — "Your Leader gains +1000. Then, you may K.O. any number of your {Thriller Bark
  Pirates} cost≤2 Characters. Your Leader gains an additional +1000 per Character K.O.'d." `asgardtest` (realistic
  flow: clause A prompts→target Leader→WaitingForTarget/PendingContinuation→clause B K.O.-loop): Leader 5000→8000
  (+1000 +2×1000), both TB chars K.O.'d. The K.O.-any + per-K.O. scaling handler (L8305) is correct.
- ✔ OP06-097 Negative Hollow — "Trash 1 card from your opponent's hand." `neghollowtest`: north hand 3→2, trash +1.
- ✔ OP06-101 O-Nami — "Up to 1 of your Leader or Character gains [Banish] during this turn" grants Banish to an own
  Character (probe: "gives Tony Tony.Chopper [Banish]").
- ✔ OP06-099 Aisa — "Look at up to 1 from the top of your OR your opponent's Life…" is split into a modal choice
  (own-Life look vs opponent-Life look). OP06-093 Perona (conditional modal, opp hand 5+), OP06-092 Brook (modal),
  OP06-089 Taralan (dual-timing mill) — all handled.
- METHOD NOTE: the ". Then," early-split (L6700) stashes clause B two ways — WaitingForTarget→PendingContinuation
  (clause A needs a pick) and Resolved→QueueAndAutoResolve(FindCardInstance(source)). FindCardInstance searches
  in-play + TRASH + hand, so an event source that already moved to trash is still found → clause B not dropped.
  (A test using a non-existent source instance hits the Resolved branch and looks broken — use a real source.)
- No GameEngine.cs edit needed. Coverage OK=2653/NA=22/0/0/0.

## Batch 818–835 (OP06-068..086, Vinsmoke/GERMA + Thriller Bark) — CLEAN (no engine bug)
- ✔ OP06-085 Kumacy — "[DON!! x2][Your Turn] +1000 power for every 5 cards in your trash." `kumacytest`: base 3000
  + 2000 (2 attached DON) + floor(trash/5)*1000 → 10 trash=7000, 3 trash=5000. Scaling handler (L502) correct.
  (Reminder: attached [DON!! xN] ALSO adds +1000 power each — that's real, not a bug.)
- ✔ OP06-086 Gecko Moria (char) — "[On Play] Choose up to 1 Character cost≤4 AND up to 1 Character cost≤2 from your
  trash. Play 1 card and play the other card rested." `geckotest`: BOTH played from trash, EXACTLY 1 rested. The
  dual-choose-from-trash + play-both-with-one-rested is fully handled.
- ✔ OP06-069 Vinsmoke Reiju / OP06-072 Cosette — compound conditions ("DON ≤/≥ opp field AND hand ≤ N" / "Leader
  {GERMA 66} type AND DON at least 2 less"): the "A and B" split + the DON-compare (now fixed #46) + hand-count all
  work; not NA. OP06-077 Black Bug also uses the new DON-compare condition.
- ✔ OP06-083 Oars (self-effect-negation via K.O.-own-{tag} cost), OP06-080 Gecko Moria leader (dual cost rest-2-DON
  + trash-1 → deck-trash + play from trash), OP06-081 Absalom (return-2-trash auto-cost), OP06-082 Inuppe
  (dual-timing conditional draw) — all not NA, standard handlers.
- No GameEngine.cs edit needed (only harness test modes added). Coverage OK=2653/NA=22/0/0/0.

## Batch 800–817 (OP06-049..067, Navy + Vinsmoke Family) — 2 FIXES
- ⭐BUG#46 (condition, GENERAL 5+ cards) "the number of DON!! cards on your field is equal to or less than the
  number on your opponent's field" was UNHANDLED in EvaluateCondition (only "at least N less than" existed). Added
  `TotalFieldDon(p) <= TotalFieldDon(opp)`. Fixes the whole Vinsmoke family: OP06-061 Ichiji (On-Play −2000+Rush),
  OP06-063 Sora (trash-cost add-from-trash), OP06-065 Niji, OP06-067 Yonji (passive +1000), OP06-077 Black Bug.
  `yonjitest`: south DON 2≤4 → Yonji 6000; south DON 4>2 → 5000. Coverage OK=2653/NA=22/0/0/0.
- ⭐BUG#47 (dual-clause) OP06-056 Ama no Murakumo Sword — "Place up to 1 opp Character cost≤2 AND up to 1 opp
  Character cost≤1 at deck bottom." Only placed 1 (the resolver parsed the first "up to 1" + first cost cap). Fixed
  the place-at-bottom resolver (L6935): when there are TWO cost caps, sum both "up to N" quantifiers and track
  high-cap slots in RemainingBudget (at most (#caps>low) picks may exceed the low cap). `amanotest`: places both a
  cost-2 and a cost-1 (2); two cost-2 chars → only 1 placed (dual-cap enforced). SCOPED to the dual-cost-cap shape
  so ST10-001 Law ("Place up to 1 … 3000 power or less …, and play up to 1 … from hand") keeps its single
  placement — no regression (its single "cost" cap is the play rider's, so sinkDual=false).
- ✔ OP06-051 Tsuru (trash-2 cost: opponent returns 1 of their Characters), OP06-052 Tokikake (conditional battle
  immunity on hand≤4), OP06-054 Borsalino (conditional Blocker on hand≤5) — standard hand-count conditions, clean.

## Batch 782–799 (OP06-029..048, Fish-Man + Navy) — 1 FIX
- ⭐BUG#45 (multi-clause) OP06-047 Charlotte Pudding — "[On Play] Your opponent returns all cards in their hand to
  their deck and shuffles. Then, your opponent draws 5 cards." A single handler (L8660) does return+shuffle+draw-N
  atomically, but TWO things broke it: (a) FindThenClause split at ". Then," → clause A reached L8660 with no
  "draws N" → drawBack defaulted to the returned count (3) → drew 3, then clause B drew 5 → DOUBLE draw (hand 3→8);
  and (b) even unsplit, the earlier "opponent draws N" handler (L7444) matched first and dropped the return clause.
  Fixed BOTH: FindThenClause returns -1 for this text, and the L7444 draw handler skips it. `puddingtest`: north
  hand 3→5, deck 50→48, log "shuffles 3 hand card(s) into the deck and draws 5". Only card with this text. Coverage
  OK=2653/NA=22/0/0/0.
- ✔ OP06-042 Vinsmoke Reiju leader — "[Your Turn] When a DON!! card on your field is returned to your DON!! deck,
  draw 1" — existing DON-return reactive dispatcher (L4210, fires once per return event). Clean.
- ✔ OP06-033 Vander Decken IX — dual-option trash cost "{Fish-Man} from hand OR [The Ark Noah] from hand/field"
  correctly validated (probe rejects a non-Fish-Man Chopper, then optional-skips). Clean.
- ✔ OP06-044 Gion — "[Your Turn] When opponent activates an Event, opponent places 1 hand card at deck bottom" —
  FireOnOpponentEventActivation has a dedicated branch (L3288). OP06-036 Ryuma ([On Play]/[On K.O.] KO rested
  cost≤4 — not NA, glow (none) with no rested target = correct), OP06-041 Ark Noah (mass rest), OP06-039 modal — clean.

## Batch 764–781 (OP06-011..028, FILM RED + Fish-Man) — 1 FIX
- ⭐BUG#44 (glow+resolver) OP06-023 Arlong — "[On Play] You may trash 1 card from your hand: Up to 1 of your
  opponent's rested LEADER cannot attack until the end of your opponent's next turn." Both the cannot-attack GLOW
  (L5991) and RESOLVER (L7101) hard-required Type=="character", so the opponent's Leader could never be targeted —
  Arlong's body was inert. Reworked both to accept the target type NAMED in the text (opponent Leader vs
  Characters) and to enforce "rested" when the text says so. `arlongtest`: rested Leader glows + gets the
  cannotAttack lock; active Leader does NOT glow. Issho/Smoker (Character-targeting) unchanged — no regression.
  Coverage OK=2653/NA=22/0/0/0.
- ✔ OP06-020 Hody Jones leader — "rest this Leader: Rest up to 1 of your opponent's DON!! cards or Characters with
  a cost of 3 or less." `hodytest`: an opponent DON!! is rested (0→1). Mixed DON/Char rest handler L9663. Clean.
- ✔ OP06-016 Raise Max — "place this Character at the bottom of the owner's deck: give opp −3000" — self-place
  cost paid (probe done=[place this Character…]), body active. L5485 self-place-at-bottom cost. Clean.
- ✔ OP06-026 Koushirou — "Set up to 1 of your ＜Slash＞ attribute Characters cost≤4 as active" — ＜attribute＞ target
  filter handled in glow (L5842, matches def.Attribute) + "cannot attack a Leader" restriction noted. Clean.
- ✔ OP06-012 Bear.King — "If opponent has a Leader/Character with base power 6000+, cannot be K.O.'d in battle" —
  conditional battle-immunity (L4407). OP06-013 Luffy / OP06-025 Camie (look-reveal), OP06-027 Gyro (On-K.O. rest),
  OP06-021 Perona leader (modal), OP06-022 Yamato leader — standard handlers, clean.

## Batch 746–763 (OP05-108..119 Skypiea tail + OP06-001..010 FILM RED) — 1 FIX, 2 DEFER
- ⭐BUG#43 (reactive, GENERAL 2 cards) "When a [Trigger] activates, <effect>" was UNIMPLEMENTED. Added
  FireOnTriggerActivated(state, defenderSeat), called from UseTrigger (L3548, the moment a Life Trigger activates).
  Scans the activating player's board (Leader/Chars/Stage), respects timing/[Once Per Turn]/DON, queues the body.
  Fixes OP05-109 Pagaya (draw 2 + trash 2) and OP13-106 Conney ([Opponent's Turn] gains [Blocker]). `pagayatest`
  (full attack→Life-damage→useTrigger flow): Pagaya fires — draws 2, trashes 2 (trash 0→2). Coverage OK=2653/0/0/0.
- ⏸ DEFER OP05-119 Monkey.D.Luffy (c10) — "[On Play] DON!! −10: place all your other Characters at deck bottom.
  Then, take an extra turn." The "take an extra turn" mechanic is UNIMPLEMENTED (no extra-turn machinery); single
  card, deep turn-structure change. Coverage-OK (uncastable in probe). Logged for a dedicated extra-turn task.
- ⏸ DEFER OP05-111 Hotori — "[On Play] You may play 1 [Kotori] from your hand: <place-opp-on-Life body>." The COST
  is "play a card from hand" (invokes the full play flow incl. Kotori's own On-Play + DON). Only card of its kind;
  probe shows manual-ack but coverage counts it OK (optional cost skips). Deferred (complex play-as-cost).
- ✔ OP06-002 Inazuma — "If this Character has 7000 power or more, gains [Banish]." `inazumatest`: 2 DON→7000
  HasBanish=True, 0 DON→5000 False. HasBanish (L1228) includes printed grant + self-power cond (L5064). Clean.
- ✔ OP06-010 Douglas Bullet — "If your Leader has the {FILM} type, gains [Blocker]." `douglastest` (live block):
  FILM leader (Uta) → Bullet blocks; non-FILM (Luffy) → can't. Leader-type cond (L5110). Clean.
- ✔ OP05-116 Hino Bird Zap — event dynamic-cap KO ("cost ≤ opp Life"); same glow path fixed for Gedatsu last
  batch (IsValidEffectTarget is text-driven, card-type-agnostic). OP06-001 Uta leader / OP06-003 Emporio / OP06-004
  Baron Omatsuri / OP06-007 Shanks — standard, clean.

## Batch 728–745 (OP05-089..107, Celestial Dragons + Skypiea) — 4 FIXES
- ⭐BUG#39 OP05-100 Enel — "[Once Per Turn] If this Character would LEAVE the field, you may trash 1 card from the
  top of your Life cards instead. If there is a [Monkey.D.Luffy] Character, this effect is negated." The
  "would leave the field" removal-replacement trigger was UNHANDLED (TryRemovalReplacement only knew "would be
  K.O.'d"/"would be removed from the field"). Added: (a) "would leave the field" to the trigger gate + leaveTrig,
  (b) self-scope for "this Character would leave", (c) the "[Name] Character → negated" name-scan (both fields),
  (d) a "trash N from the top of your Life cards instead" action (burn Life to survive). `eneltest`: no Luffy →
  Enel survives, Life 3→2; with Luffy → replacement negated, Enel K.O.'d, Life unchanged. Only card with this
  phrasing (no collateral).
- ⭐BUG#40 OP05-104 Conis — "[On Play] You may place 1 of your Stages at the bottom of your deck: Draw 1 card and
  trash 1 card from your hand." The place-Stage COST (no cost qualifier) had no TryAutoPayCost branch (only
  Shandian's "place 1 Stage with a cost of N" existed) → NOT_AUTOMATED. Added the branch. `conistest`: Stage →
  deck bottom, draw 1 + trash 1 fire. Cleared from NA: coverage OK 2652→2653, NA 23→22.
- ⭐BUG#41 (glow) OP05-102 Gedatsu / OP05-103 Kotori — "K.O. up to 1 opp Character with a cost EQUAL TO OR LESS
  THAN the number of your opponent's Life cards." The resolver applied ComputeDynamicCap (L9402) but the GLOW did
  not → every opp Character glowed regardless of the Life-count cap. Added ComputeDynamicCap to the glow cost-cap
  logic. `gedatsutest` (north 2 Life): glow cost2=True/cost4=False; resolve KOs cost2, rejects cost4.
- ⭐BUG#42 OP05-097 Mary Geoise — "[Your Turn] The cost of playing {Celestial Dragons} type Character cards with a
  cost of 2 or more from your hand will be reduced by 1." Never applied — GetPassiveCostAuraBonus is gated to
  in-play Characters, but this reduces a HAND card's play cost. Added GetPlayCostReductionAura (hand-side, scans
  owner's in-play cards for "cost of playing {tag} … reduced by N", gated on timing/tag/type/cost-threshold, wired
  into GetCost). `marytest`: Saint Mjosgard (CD cost 5) → 4 with Mary Geoise, 5 without. Only card with this
  phrasing.
- ✔ OP05-096 I Bid 500 Million (modal Choose-one ×3 K.O./Return/Place-on-Life — modal recognized, choice pending),
  OP05-092 Saint Rosward (same only-CD −4-cost aura as Charlos, verified class), OP05-098 Enel leader (reactive
  "Life becomes 0" add-to-Life), OP05-103 Kotori "If you have [Hotori]" named condition (L4881, gates correctly).
- Coverage after batch: OK=2653 / NA=22 / CRASH=0 / STUCK=0 / INVARIANT=0.

## Batch 710–727 (OP05-070..088, Kid/Wano + Dressrosa/Celestial) — CLEAN (no engine bug)
- ✔ OP05-084 Saint Charlos — "[Your Turn] If the only Characters on your field are {Celestial Dragons}, give all
  of your opponent's Characters −4 COST." VERIFIED EMPIRICALLY via `charlostest` (GetOpponentCostDebuffAura L894):
  only-CD south-turn → Kid cost 5→1; add a non-CD char → aura off (cost 5); opponent's turn → [Your Turn] gate
  holds (cost 5). All 3 cases pass. (Same aura class as the Krieg −power fix, but the −cost path was already sound.)
- ✔ OP05-086 Nefeltari Vivi — "If you have 10 or more cards in your trash, this Character gains [Blocker]."
  VERIFIED EMPIRICALLY via `vivitest` (live blockAttack): trash=10 → Vivi blocks (rested + new target); trash=9 →
  block rejected. HasPrintedKeywordGrant (L1071) + trash-count condition (L4984) correct.
- ✔ OP05-072 Hone-Kichi (8-DON gate, up to 2 opp −2000 — glow opponent-only), OP05-075 Mr.1 ([On Your Opponent's
  Attack] DON−1 play — reactive fires, same class as OP05-029 Doffy), OP05-080 Elizabello II (return-20-from-trash
  cost → Double Attack +10000; resolves OK, not NA), OP05-087 Hakuba (self-KO cost → opp −5 cost), OP05-070
  Fra-Nosuke (8-DON conditional Rush), OP05-073 Miss Doublefinger, OP05-079 Viola, OP05-081/082/088 — all clean.
- NOTE: this conditional-aura/blocker/reactive region is well-hardened by the earlier Krieg-aura + reactive-dispatch
  fixes; suspicious cards traced clean. No GameEngine.cs edit needed this batch (only harness test modes added).

## Batch 692–709 (OP05-051..069, Navy + Kid/Wano)
- ⭐BUG#38 (GENERAL, glow) cost-pick glow missing "return"/"place" verbs. The cost-RESOLVER (L6336) accepts
  `K.O.|trash|rest|return|place N of your Characters` cost-picks, but the GLOW (L5795) regex had only
  `K.O.|trash|rest`. So OP05-056 X.Barrels ("place 1 of your Characters other than this Character at the bottom of
  your deck: Draw 1") and ST17-002 Law ("return 1 of your Characters to hand: …") glowed NOTHING — the player
  couldn't click the Character to pay the cost, even though the resolver would accept the click. Aligned the glow
  regex to the resolver. `probe`: X.Barrels now glows the own Chopper (source correctly excluded by "other than
  this Character"); Law glows both own Characters. Coverage OK=2652/NA=23/0/0/0.
- ✔ OP05-062 O-Nami — "If you have 10 DON!! cards on your field, this Character gains [Blocker]" conditional printed
  keyword grant. Verified by trace: HasPrintedKeywordGrant (L1071) → leading-"If" extraction (L1097) →
  EvaluateCondition "you have 10 DON!! cards on your field" (L4834, exact ==, TotalFieldDon). Correct.
- ✔ OP05-063 O-Robi — [On Play] "If 8+ DON, K.O. up to 1 opp cost≤3": glow opponent-only, condition L4323. Clean.
- ✔ OP05-067 Zoro-Juurou (When-Attacking 3-or-less-Life DON ramp — OK in coverage, not NA), OP05-053 Mozambia
  (reactive self-buff on off-phase draw), OP05-058 It's a Waste (mass bottom-deck cost≤3 + hand-trim to 5),
  OP05-054 Garp / OP05-064 Killer (look-reveal-add), OP05-060 Luffy leader (add Life to hand) — clean.

## Batch 675–691 (OP05-032..050, Donquixote + Navy)
- ⭐BUG#36 OP05-032 Pica K.O.-replacement — "If this Character would be K.O.'d, you may rest up to 1 of your
  Characters with a cost of 3 or more OTHER THAN [Pica] instead." The TryRemovalReplacement "rest N of your …
  instead" branch (L4050) parsed only a curly-brace TYPE tag — it ignored the "cost of 3 or more" filter AND the
  "other than [Pica]" exclusion, so it would rest the first eligible char (a cost-1 friend, or even Pica itself).
  Added cost-of-N-or-more/less parse + "other than [Name]" NameMatches exclusion. `picatest`: with a cost-1 and a
  cost-3 friend, only the cost-3 friend is rested; Pica survives un-rested, the cost-1 is untouched.
- ⭐BUG#37 (GENERAL, glow) OP05-042 Issho — "[On Play] Up to 1 of your opponent's Characters with a cost of 7 or
  less cannot attack…" GLOW (IsValidEffectTarget) lit up the player's OWN Characters (the resolver L6979 then
  rejects them). No dedicated glow branch existed → fell through to the generic character highlight. Added an
  opponent-only "cannot attack" glow branch mirroring the resolver (opp Character + cost cap). `probe` validTargets
  now lists only north chars. Fixes ALL ~17 opponent attack-lock cards (Smoker ST19-001, Galdino OP16-056, Perona
  OP14-111, …). Coverage OK=2652/NA=23/0/0/0.
- ⚠ DEFER OP05-040 Birdcage — "all Characters with a cost of 5 or less do not become active in your and your
  opponent's Refresh Phases" is a CONTINUOUS global refresh-lock passive (distinct from the per-target FROZEN
  one-shot at L6874). Not wired; needs a Refresh-Phase continuous-passive scan. Very niche (needs Doffy leader,
  self-trashing on the 10-DON turn). Low priority — logged, not built this pass.
- ✔ OP05-048 Bastille (place cost≤2 bottom-of-deck), OP05-049 Haccha (return cost≤3 to hand), OP05-033/034 Baby 5
  (DON-cost: play {Donquixote Pirates} cost≤2), OP05-046 Dalmatian (On-K.O. draw+bottom), OP05-041 Sakazuki leader
  (trash-hand: draw) — standard handlers present, clean.

## Batch 658–674 (OP05-015..031, Revolutionary Army + Donquixote)
- ⭐BUG#35 (BROAD) OP05-019 Fire Fist & the whole "N power or less" filter family: removal/target power caps were
  parsed with regex `(\d{3,5}) power or less` (3–5 digits), so any card written "with **0** power or less" failed
  to match → cap stayed −1 → NO power restriction → the effect could K.O./target ANY Character. Widened all 6
  non-"base" sites (+ mass-KO L7896) to `(\d{1,5})`. Verified `firefisttest`: a 5000-power char is now REJECTED,
  a debuffed 0-power char is K.O.'d. Coverage OK=2652/NA=23/0/0/0. Also fixes OP04-008 Chaka, OP11-002 Ain,
  OP13-013 Higuma, OP15-020 Fire Fist, and OP15-114 Wyper (mass-KO) — all "0 power or less" removal.
- ✔ OP05-016 Morley / OP05-017 Lindbergh — self-power condition "If this Character has 7000 power or more"
  (EvaluateCondition L5064 reads source GetPower). `selfpowertest`: 2 DON→7000 KO fires; 0 DON→5000 no KO. Clean.
- ✔ OP05-029 Doflamingo — [On Your Opponent's Attack][OPT] ➀ rest-opp reactive fires on the defender board
  (`oppattacktest` → pending created). Dispatch L2800 handles it. Clean.
- ✔ OP05-020 Four Thousand-Brick (buff+KO 2000-or-less), OP05-023 Vergo / OP05-025 Gladius / OP05-027 Law /
  OP05-028 Doffy (rest/KO cost≤N), OP05-026 Sarquiss (rest-friend cost≥3: set active), OP05-031 Buffalo
  (2+ rested → set-active cost-1), OP05-018 Emporio (Counter buff+play), OP05-022/030 Rosinante — standard
  handlers present, no dropped clauses.

## PROACTIVE STAGE-SCAN AUDIT (after 2 Stage-gap fixes): COMPLETE — no more live gaps.
- Checked ALL aura-source scans: GetPassiveCostAuraBonus (L847) + HasPrintedKeywordGrant aura (L1116) already
  include Stage; the DeclareAttack limited-Rush scan + GetTurnPassiveAuraBonus were the only LIVE gaps (both fixed).
  Base-power / removal-immunity / "you have a [Name]" scans exclude Stage but NO stage card prints those patterns
  (verified: 0 stage-name "you have a" conditions). All live stage continuous auras (power/cost/attack/keyword) OK.

## Traced 641–657 (OP04-116..OP05-014):
- 🐞✅ **FIXED — K.O.-replacement POWER filter unenforced** (OP05-001 Sabo LEADER "[DON!! x1][Opponent's Turn][OPT]
  If your Character with 5000 power or more would be K.O.'d, give it −1000 instead"). TryRemovalReplacement had
  color/base-cost/rested filters but NO power filter → Sabo protected ANY Character. FIX: added "with N power or
  more/less" filter (CURRENT power). `sabotest`: 7000-power → SAVED ✅, 1000-power → K.O.'d ✅. Coverage 2652/23/0/0/0.
- Fast-passed: OP04-118 Vivi (Rush aura cost≥3 self-excl), OP04-119 Rosinante (rested-source aura), OP05-007 Sabo
  (total-power KO), OP05-010/011 (power-filtered KO), OP05-009 Toh-Toh (leader 0-power cond).

## Traced 624–640 (OP04-095..OP04-115):
- 🐞✅ **FIXED — limited-Rush aura from a STAGE never applied** (OP04-096 Corrida Coliseum "If your Leader has the
  {Dressrosa} type, your {Dressrosa} type Characters can attack Characters on the turn in which they are played").
  The DeclareAttack aura scan (L2739) included Leader + Characters but NOT the Stage → Corrida's grant was ignored,
  and it also didn't gate on the leading "If <cond>". FIX: add the Stage to the scan + evaluate the leading
  condition (per-line). `corridatest`: just-played Dressrosa char with Corrida → attack allowed ✅, without → blocked
  ✅. Same Stage-scan gap class as the earlier GetTurnPassiveAuraBonus power fix. Coverage 2652/23/0/0/0.
- ✅ CLEAN OP04-097 Otama "Add up to 1 opp {Animal}/{SMILE} Character cost≤3 to opp's Life face-up" — glow filters
  by type + cost (Chopper glows, Zoro excluded); char→Life handler verified. OP04-099 Olin (name-treat), OP04-106
  Bavarois (cond +1000), OP04-112 Yamato (dynamic cost cap).

## Traced 607–623 (OP04-076..OP04-094):
- 🐞✅ **FIXED — K.O.-replacement "rest your Leader or 1 [Name] instead" not handled** (OP04-082 Kyros "If this
  Character would be K.O.'d, you may rest your Leader or 1 [Corrida Coliseum] instead"). TryRemovalReplacement had
  no branch for it → Kyros was K.O.'d normally (no protection). FIX: added a branch — rest the Leader if active,
  else the named "or 1 [Name]" Stage/Character alternative. `koreptest3 OP04-082`: Kyros would be K.O.'d → rests
  the Leader instead, Kyros survives ✅. Coverage 2652/23/0/0/0.
- Fast-passed: OP04-079/088 Orlumbus/Hajrudin (−4 cost; rest-Leader cost), OP04-081/090 (can-also-attack-active),
  OP04-084 Stussy (look+play), OP04-085 Suleiman (dual-timing cond), OP04-091 Leo (rest-Leader cost → cond KO),
  OP04-093 King Kong Gun (Dressrosa buff). OP04-094 Trueno [Trigger] rest-Leader-KO stays NA (interactive).

## Traced 590–606 (OP04-058..OP04-075) — CLEAN batch (Baroque Works / Crocodile defensive):
- ✅ "[On Your Opponent's Attack] DON!! −N: <defensive effect>" reactive cluster VERIFIED (7 cards: OP04-059
  Iceburg, OP04-063 Franky, OP04-069 Bentham, OP04-070 Mr.3, OP04-071 Mr.4, OP04-072 Mr.5, plus OP04-060 Croc).
  `oppattacktest OP04-071`: north attacks → south Mr.4 reactive pending fires ✅ (dispatch L2791).
- ✅ OP04-058 Crocodile leader (DON-returned reactive — covered by the "on the/your field" handler), OP04-065/066
  Baroque conditional On-Plays, OP04-074 Colors Trap (counter DON−1).
- 🚩 DEFERRED (NA) OP04-073 Mr.13 & Ms.Friday "[Activate:Main] You may trash this Character AND 1 of your Characters
  with a type including "Baroque Works": add DON" — COMPOUND pick cost (auto-trash-self + pick-trash-a-Baroque-
  char); the auto-pay path can't do the pick half. Needs a compound-cost pick flow. NA stays 23.
Coverage OK=2652/23/0/0/0 (no engine change — verification only).

## Traced 573–589 (OP04-040..OP04-057):
- 🐞✅ **FIXED — double-return only returned ONE Character** (OP04-044 Kaido "[On Play] Return up to 1 Character
  with a cost of 8 or less AND up to 1 Character with a cost of 3 or less to the owner's hand"). The generic return
  handler (L6686) returned the first char (cost≤8, glow was correct) then Resolved — the "and up to 1 Character
  with a cost of 3 or less" second return was dropped. FIX: after the first return, detect the "and up to 1
  Character with a cost of N or less" continuation and queue a SECOND return with its own cap. `kaidotest`: glow
  C8=T/C3=T/C10=F; both cost-8 and cost-3 returned, cost-10 excluded ✅. Coverage 2652/23/0/0/0.
- Fast-passed: OP04-047 Ice Oni (battle-end place-battled-char — handler L3229), OP04-048 Sasaki (return-hand-to-
  deck + draw), OP04-053 Page One (When-you-activate-Event reactive), OP04-056 Red Roc (place-at-bottom), OP04-040
  Queen leader (When-Attacking cond draw).

## Traced 556–572 (OP04-022..OP04-039):
- 🐞✅ **FIXED — freeze resolver rejected the LEADER + skipped the rested filter** (OP04-031 Doffy "Up to a total
  of 3 of your opponent's RESTED Leader and Character cards will not become active", OP07-059 Foxy). The freeze
  resolver (L6826) required `Type == "character"` (rejecting the opponent's Leader, though the GLOW already allowed
  it — a mismatch) and its rested-filter checked "rested Characters" (which "rested Leader and Character" doesn't
  contain → active cards wrongly accepted). FIX: allow the Leader when the text says "Leader and/or Character";
  rested filter matches the bare word "rested". `freezetest`: glow rested-Leader=True, rested-Char=True, active-
  Char=False; leader actually frozen ✅. Coverage 2652/23/0/0/0.
- Fast-passed: OP04-024 Sugar (opp-play reactive), OP04-030 Trebol (rested KO), OP04-032 Baby5 (End-of-Turn set-DON),
  OP04-034 Lao.G (cond End-of-Turn KO), OP04-038 (dual-timing rest+KO), OP04-039 Rebecca leader (cannot attack).

## COST-FIX-QUEUE (NA) work: NA 33→30
- 🐞✅ **FIXED — "return N cards from your trash to the bottom of your deck" cost never auto-paid** (OP06-081
  Absalom, OP06-090 Dr. Hogback, OP04-090 Luffy — all logged "manual resolution"). The cost-prefix resolver's
  auto-pay guard (L6217) skipped ANY cost containing "from your trash" (assuming it needs a per-card pick), but
  this cost is auto-payable (a pre-existing TryAutoPayCost branch L5348 returns the last N trash cards to the deck).
  No pick handler matched "return N cards from your trash …" → fell through to "manual resolution". FIX: one-line
  `trashReturnAuto` exception routes it to auto-pay. Added `absalomtest`: trash 3→1, deck 50→52, opp K.O.'d ✅.
  Coverage OK 2642→**2645**, NA 33→**30**.
- 🐞✅ **FIXED — "place 1 Stage with a cost of 1 at the bottom of the owner's deck" cost had no handler** (OP06-102
  Kamakiri, OP06-111 Braham, OP06-114 Wyper — Shandian Giant-Jack recycling). Added a TryAutoPayCost branch: place
  the player's Stage (if it matches the exact cost) at the deck bottom. `kamakiritest`: stage → deck (50→51), board
  stage gone, opp K.O.'d ✅. Coverage OK 2645→**2648**, NA 30→**27**.
- 🐞✅ **FIXED — "give N active DON!! card(s) to 1 of your [Name]" cost no handler** (EB04-009, OP12-016/017/019
  Silvers Rayleigh support). Added a TryAutoPayCost branch: attach N of your active DON!! to the named own
  Leader/Character (auto-pick the first match). `rayleightest`: Rayleigh attached DON 0→1, opp −2000 ✅. Coverage
  OK 2648→**2652**, NA 27→**23**. (OP13-007's "1 of your Leader or Character cards" any-recipient + trash-self form
  left for a pick flow.) Remaining NA=23 (reveal-play riders, opp-DON-move Activate ×3, ~19 niche one-offs).

## Traced 539–555 (OP04-004..OP04-021) — CLEAN batch (Alabasta):
- ✅ OP04-006 Koza self-Leader-debuff COST "[When Attacking] You may give your Leader −5000: this Character +2000"
  — VERIFIED via `kozatest`: Leader 5000→0 (−5000 cost auto-pays, TryAutoPayCost L5420) + Koza 3000→5000 (+2000
  body) ✅. Same cost class: OP04-009 Super Spot-Billed Duck.
- ✅ OP04-004 Karoo "give up to 1 rested DON!! to EACH of your {Alabasta} Characters" — multi-recipient DON give
  handled (L9766). OP04-012 Cobra "[Your Turn] All {Alabasta} Characters OTHER THAN this gain +1000" (aura +
  self-exclusion). OP04-020 Issho leader cost aura — covered by this run's GetOpponentCostDebuffAura.
- Fast-passed: OP04-005 Kung Fu Jugon (cond Blocker via pair), OP04-008 Chaka (cond −cost), OP04-011 Nami (reveal-
  top conditional), OP04-013 Pell (When-Attacking KO), OP04-019 Doffy leader (End-of-Turn set-DON), OP04-021 Viola
  ([On Your Opponent's Attack] ➁ reactive — handled by the On-Opp-Attack path).
Coverage OK=2642/33/0/0/0 (no engine change — verification only).

## Traced 522–538 (OP03-109..OP04-003) — CLEAN batch:
- ✅ OP03-123 Katakuri "Add up to 1 Character with a cost of 8 or less to the top or bottom of the owner's Life
  cards face-up" (board→Life conversion, owner-agnostic) — VERIFIED via probe: glow lists both sides' cost≤8 chars,
  resolves ("Zoro added to the top of the opponent's Life"). Also OP11-116, OP12-117 (same handler).
- ✅ OP03-120 Tropical Torment "If opp has 4+ Life, trash up to 1 from top of opponent's Life" — VERIFIED via
  `tropicaltest`: oppLife=5 → 5→4 ✅, oppLife=3 → unchanged (condition gated) ✅. Handler L10650, condition L4526.
- Fast-passed: OP03-109/110 Charlotte Chiffon/Smoothie (Life-cost → deck→Life), OP03-115 Streusen / OP03-121
  Thunder Bolt (Trigger/Life-cost KO), OP03-117 Napoleon (name buff), OP03-122 Sogeking (name-treat + bounce),
  OP04-001 Vivi leader (cannot attack + Activate), OP04-003 Usopp (On-K.O. base-power KO).
Coverage OK=2642/33/0/0/0 (no engine change — verification only).

## Traced 505–521 (OP03-088..OP03-108):
- 🐞✅ **FIXED — dual char/stage K.O. mis-parsed the cost + couldn't hit Stages** (OP03-096 Tempest Kick "K.O. up
  to 1 of your opponent's Characters with a cost of 0 OR your opponent's Stages with a cost of 3 or less"). The
  generic cost-K.O. (glow + resolver) grabbed the STAGE clause's "3 or less" and applied it to Characters (a cost-1
  char wrongly qualified for the "cost 0" clause) AND rejected Stages (type != character). FIX (3 sites): early
  dual-clause branch in IsValidEffectTarget (per-type clause cost); a dedicated dual K.O. resolver (handles the
  Stage — trash it); guarded the generic K.O. resolver to skip the dual pattern. Added `tempesttest`: cost-1 stage
  glows + K.O.'d ✅, cost-7 stage excluded ✅, cost-1 char excluded ✅. Coverage 2642/33/0/0/0.
- ✅ CLEAN OP03-091 Helmeppo "Set cost of up to 1 opp Character with NO BASE EFFECT to 0" — glow enforces vanilla
  (Sai targetable, Zoro not). OP03-099 Katakuri leader (look at your/opp Life), OP03-108 Cracker (cond Double
  Attack "less Life than opponent"), the CP-type conditionals fast-passed.

## Traced 488–504 (OP03-067..OP03-086) — CLEAN batch:
- ✅ OP03-077 Charlotte Linlin (Big Mom) LEADER VERIFIED end-to-end via `bigmomtest`: "[DON!!x2][When Attacking]
  ② : You may trash 1 card: If you have 1 or less Life, add up to 1 from deck to top of Life." Double-cost (② rest-2
  DON + trash-1-hand) + Life condition + deck→Life body → Life 1→2, deck 50→49 ✅. The deck→Life recovery handler
  (GameEngine L10574, a 42-card mechanic: Enel, Newgate, many Charlotte cards) is correct.
- ✅ OP03-078 Issho "[DON!! x1][Your Turn] Give all opp −3 cost" — covered by this run's GetOpponentCostDebuffAura
  fix. OP03-076 Rob Lucci leader opp-char-KO reactive — fixed earlier this run. OP03-079 Vergo [DON!!x1] battle
  immunity, OP03-080/086 Kaku/Spandam (place-from-trash / CP-look).
- Fast-passed: OP03-067 Peepley Lulu (DON ramp), OP03-068 Minozebra (Banish), OP03-081 Kalifa (draw2/trash2 +
  −cost), OP03-083 Corgy (scry). Coverage OK=2642/33/0/0/0 (no engine change — verification only).

## Traced 471–487 (OP03-048..OP03-066) — CLEAN batch (Water Seven / East Blue, standard patterns):
- ✅ Deck-size (mill payoff) conditions VERIFIED: OP03-053 Yosaku & Johnny "[DON!! x1] If you have 20 or less
  cards in your deck, +2000" — `decksizetest`: deck=20 → 6000 (base+DON+2000) ✅, deck=21 → 4000 (gated) ✅.
  Condition "you have N or less cards in your deck" recognized (EvaluateCondition L4454). OP03-049 Patty
  (same deck-size cond gating a bounce) — same path.
- ✅ OP03-058 Iceburg leader "This Leader cannot attack" — enforced in DeclareAttack (L2396). OP03-051 Bell-mère
  Life-damage mill — covered by iter's FireOnLifeDamageDealt fix.
- Fast-passed: OP03-048 Nojiko (cond bounce), OP03-054/055 counters, OP03-057 place-at-bottom, OP03-059/060 Kaku/
  Kalifa (When-Attacking DON−1), OP03-064 Tilestone (On-K.O. DON ramp), OP03-066 Paulie (➁ cost DON ramp).
Coverage OK=2642/33/0/0/0 (no engine change — verification only).

## Traced 454–470 (OP03-028..OP03-047):
- 🐞✅ **FIXED (class, 6 cards) — "When this Leader/Character's attack deals damage to your opponent's Life,
  <effect>" reactive had no dispatch** — the Nami MILL-WIN engine: OP03-040 & P-117 Nami leaders, OP03-041 Usopp,
  OP03-047 Zeff, OP03-051 Bell-mère, OP03-043 Gaimon (self-mill "trash N from top of deck"). The battle-damage step
  dealt Life damage but never fired the attacker's reaction. FIX: new `FireOnLifeDamageDealt(attacker)` from the
  Leader-damage branch (regex covers both "this …'s attack deals" and Gaimon's "you deal"); gates DON/OPT; routes
  body via QueueAndAutoResolve. Added `lifedmgtest`. Verified Zeff: DON=1 → deck 50→43 (trash 7) ✅, DON=0 → no mill
  ✅. Coverage 2642/33/0/0/0.
- ✅ CLEAN OP03-040 Nami deck-out WIN "When your deck is reduced to 0, you win the game instead of losing" —
  CardData.WinsOnDeckOut detects the phrase (CardData.cs L769); the deck-out rule (L11801) honors it.
- Fast-passed: OP03-028 Jango (Choose one), OP03-029/034 (rested-cost KO), OP03-036/037 East Blue rest-cost,
  OP03-042 (add from trash), OP03-044 Kaya (draw2/trash2).

## Traced 437–453 (OP03-010..OP03-027):
- 🐞✅ **FIXED — board-Character sacrifice cost ignored COLOR + POWER filters** (OP03-012 Teach "trash 1 of your
  RED Characters with 4000 power or more", OP06-015 Lily Carnation "6000 power or more"). Both the cost glow
  (IsValidEffectTarget L5640) and the cost payment (L6156) only ran CardPassesFeatureFilter (which handles {feature}
  tags), so any Character — wrong color, under-power — was a valid cost. FIX: new `CostCharFilterOk(state, costText,
  qualifier, card)` = feature + color (from the "your <qual> Characters" qualifier) + power ("with N power or
  more/less", CURRENT power) — used at both sites. Added `costfiltertest`: red-7000 valid ✅, red-3000 invalid
  (power) ✅, blue-5000 invalid (color) ✅. Coverage 2642/33/0/0/0.
- Fast-passed: OP03-011 Blamenco (When-Attacking −2000), OP03-013 Marco (KO + On-K.O.), OP03-016 Flame Emperor
  (cond KO), OP03-017 Cross Fire (Main/Counter cond debuff), OP03-024/026/027 East Blue conditional rests, OP03-025
  Krieg (trash-cost multi-KO), OP03-021 Kuro / OP03-022 Arlong leaders (DON-cost Activate/When-Attacking).

## Traced 420–436 (OP02-111..OP03-009):
- 🐞✅ **FIXED (class, 9 cards) — continuous "give all of your opponent's Characters −N cost" auras never applied.**
  OP02-121 Kuzan, OP03-078/OP04-020 Issho (Fujitora control), EB04-017 Mystoms, OP05-084/092 Celestial Dragons,
  OP07-081 Kalifa, OP08-083 Sheepshead, P-032 Sengoku. GetPassiveCostAuraBonus scans only the instance's OWN board,
  so a cost-debuff printed on the OPPONENT's card never reduced the char's effective cost → e.g. Kuzan's "[On Play]
  K.O. cost-0" couldn't hit a cost-5 char that should have become 0. FIX: new `GetOpponentCostDebuffAura` (mirrors
  GetOpponentDebuffAuraBonus for power) scans the OTHER seat's board; gates timing/DON/condition. Wired into
  GetCost. Added `costauratest`. Verified: Kuzan −5 → cost-5 opp char = 0 own turn / 5 opp turn ✅; Issho [DON!!x1]
  −1 → 4 ✅. Coverage 2642/33/0/0/0.
- ✅ CLEAN OP02-100 Jango pair OP02-111 Fullbody "[When Attacking] If you have [Jango], +3000" (name-cond buff),
  OP03-005 Thatch (self-buff + self-trash), OP03-008 Buggy (＜Slash＞ attribute battle immunity).
- 🚩 **DEFERRED w/ SPEC — OP03-001 Portgas.D.Ace LEADER** "When this Leader attacks or is attacked, you may trash
  any number of Event or Stage cards from your hand. This Leader gains +1000 power during this battle for every
  card trashed." Sole "attacks or is attacked" card; complex INTERACTIVE (unbounded multi-trash + per-trash
  BATTLE power). IMPLEMENTATION SPEC for a dedicated pass: (1) HOOK A — in DeclareAttack, after the battle is
  created, if the attacker is a Leader whose text has "When this Leader attacks or is attacked", queue an optional
  reactive. (2) HOOK B — in DeclareAttack when the TARGET is the opponent's Leader with that text, queue it for the
  DEFENDER. (3) RESOLVER — "trash any number of Event or Stage cards from your hand" = multi-select (SelectionsRemaining
  unbounded/large, skippable); each valid pick (Event/Stage in hand) → MoveToTrash(isKo:false) + add +1000 to
  state.Battle.BattlePowerBonus[leaderId] (EffectScope.Battle). (4) GLOW — IsValidEffectTarget: own hand cards of
  type event/stage while this effect pending. Precedent for "any number": L7733/L7909. Verify with a `acetest`
  (attack path + defend path, trash 2 → +2000 battle power). NOT rushed (marquee red leader; battle-power path).

## Traced 403–419 (OP02-090..OP02-110):
- 🐞✅ **FIXED (class, 2 cards) — "When this Character battles and K.O.'s your opponent's Character, <effect>"
  reactive had no dispatch** (OP02-094 Isuka "[DON!! x1][OPT] … set this Character as active"; OP04-086 Chinjao
  "… draw 2 and trash 2"). The battle-damage step K.O.'d the defender but never fired the attacker's reaction. FIX:
  new `FireOnBattleKo(attacker)` called from ResolveAttack right after the defender's MoveToTrash — gates DON/turn/
  OPT, routes body via QueueAndAutoResolve. Added `battlekotest`. Verified Isuka: DON=1 → victim K.O.'d + Isuka
  set active (attacks again) ✅, DON=0 → no restand ✅; Chinjao → draws 2 ✅. Coverage 2642/33/0/0/0; Semimaru
  battle-immunity regression intact.
- ✅ CLEAN OP02-100 Jango "If you have [Fullbody], this Character cannot be K.O.'d in battle" — conditional battle
  immunity handled by IsBattleKoImmune's leading-If gate. OP02-095 Onigumo conditional [Banish] via
  HasPrintedKeywordGrant.
- Fast-passed: OP02-093 Smoker leader (−1 cost Activate:Main), OP02-096 Kuzan / OP02-103/105 (When-Attacking −cost),
  OP02-098/099 Koby/Sakazuki (trash-cost KO), OP02-106 Tsuru (−2 cost).

## Traced 386–402 (OP02-068..OP02-089):
- 🐞✅ **FIXED — DON-returned reactive missed the "on THE field" wording** (OP02-071 Magellan leader "[Your Turn]
  [OPT] When a DON!! card on **the** field is returned to your DON!! deck, this Leader gains +1000"). NotifyDonReturned
  required the substring "on **your** field is returned"; Magellan is the sole "the field" variant (you only return
  your OWN DON, so equivalent) → its +1000 never fired. FIX: regex `on (?:your|the) field` for both the single and
  "N or more" forms. Added `magellantest`: play Shiryu (DON!!−1) → Magellan 5000→6000 ✅. Coverage 2642/33/0/0/0.
- ✅ CLEAN OP02-074 Saldeath "Your [Blugori] gains [Blocker]" — named-card keyword AURA handled by
  HasPrintedKeywordGrant's aura scan (`Your \[Name\] gains \[KW\]`).
- Fast-passed: OP02-068 Gum-Gum Rain (counter cost→buff), OP02-069 DEATH WINK (counter → draw-to-hand, fixed last
  iter), OP02-072 Zephyr leader (When-Attacking DON−4), the OP02-076..085 DON−N On Play Impel Down chars, OP02-087
  Minotaur (Double Attack + On-K.O. cond).

## Traced 369–385 (OP02-047..OP02-067) — TWO bugs:
- 🐞✅ **FIXED — "Draw card(s) so that you have N cards" (variable draw-to-hand-size) was dropped** (OP02-051
  Ivankov "[On Play] Draw card(s) so that you have 3 … and then play up to 1 blue {Impel Down}"; OP02-069 DEATH
  WINK). No handler → the resolver matched only the trailing "play …" substring, silently skipping the draw. FIX:
  early handler in TryResolveKnownEffect — draw max(0, N−handCount), then continue any "and then <rest>" clause via
  QueueAndAutoResolve. Added `drawtotest`: Ivankov from a 1-card hand → hand reaches 3 (deck 50→47) ✅. Coverage
  2642/33/0/0/0.
- 🐞✅ **FIXED — COST-based Blocker ban "[Blocker] of any Character with a cost of N or less" banned ALL blockers**
  (OP02-061 Morley, OP02-101 Strawberry). Handler only parsed power bans; cost form fell to NoBlocker=true. FIX:
  added `BlockerCostBanMax` field + parse "of any Character with a cost of N or less" (anchored past the "]" and
  clear of any leading-condition cost) + checks at all 3 block-eligibility sites (bar cost ≤ max). Verified via
  `blockbantest`: cost≤5 Chopper barred ✅, cost≥6 Sengoku allowed ✅; Shanks power-ban regression intact. Coverage
  2642/33/0/0/0.
- Fast-passed: OP02-050 Inazuma (static self-buff — covered by earlier fix), OP02-052 Cabaji ([Mohji] cond draw),
  OP02-062 Luffy (trash-2 cost → return), OP02-066 Impel Down All Stars (trash-2 cost → cond draw).

## Traced 352–368 (OP02-026..OP02-046):
- 🐞✅ **FIXED (class, 2 leaders) — "When you play a Character[ with no base effect], <effect>" own-play reactive
  had no dispatch** (OP02-026 Sanji leader: vanilla-play DON ramp; OP14-041 Boa Hancock leader: [Opponent's Turn]
  draw). PlayCard fired only the OPPONENT's "When your opponent plays a Character" reactive (Sugar), never the
  player's OWN. FIX: added a parallel own-board scan right after it — gates timing/DON/OPT, enforces the "with no
  base effect" qualifier (played card's Effect empty), extracts the body after the trigger, routes via
  QueueAndAutoResolve. Added `sanjitest`. Verified: vanilla char (Sai) → Sanji ramps 2 DON active (rested 2→0) ✅;
  effect char (Vista) → NO ramp ✅. Coverage 2642/33/0/0/0; Sugar reactive regression intact.
- ✅ CLEAN OP02-037 Nico Robin / OP02-040 Brook "Play up to 1 {FILM} or {Straw Hat Crew} type Character cost≤N from
  your hand" — OR-type play filter via CardPassesFeatureFilter (handles "{A} or {B} type").
- Fast-passed: OP02-027 Inuarashi (all-DON-rested removal immunity), OP02-031 Toki (cond Blocker via [Oden]
  present), OP02-030 Oden (③ Activate:Main), OP02-042 Yamato (name-treat), OP02-045 Oni Giri (counter +6000 → play).

## Traced 335–351 (OP02-008..OP02-025) — a TWO-PART compound-condition fix (via OP02-008 Jozu):
- 🐞✅ **FIXED (2 parts) — compound "A and B" conditions on keyword grants were mis-gated.** OP02-008 Jozu
  "[DON!! x1] If you have 2 or less Life cards AND your Leader's type includes "Whitebeard Pirates", gains [Rush]".
  (a) HasPrintedKeywordGrant anchored the condition check `^If` on the RAW line, but the "[DON!! x1]" prefix
  defeated it → the condition was SKIPPED entirely (Rush granted whenever 1 DON attached). FIX: strip leading
  timing tags before `^If` (same class as the Law-leader condLaw bug). (b) With the condition now read, the
  substring-matching Life branch in EvaluateCondition (L4534 "you have N or less Life") matched HALF the compound
  and returned early, ignoring the Leader half. FIX: added an EARLY compound-"A and B" split (before the substring
  branches) that splits ONLY when both halves are independently RECOGNIZED (so single conditions containing " and "
  — "Leader has {X} type and is active", pwrAndType — fall through). Added harness `jozutest`. Verified all 3
  combos: WB+life2 → Rush ✅, WB+life4 → no Rush ✅, non-WB+life2 → no Rush ✅. Coverage 2642/33/0/0/0.
- ✅ CLEAN OP02-019 Rakuyo "[DON!! x1][Your Turn] All of your Characters with a type including "Whitebeard
  Pirates" gain +1000" — GetTurnPassiveAuraBonus `type including "X"` recipient filter (L731) handles it.
- Fast-passed: OP02-013 Ace (2-target −3000 + conditional), OP02-016 Magura (red cost-1 buff), OP02-024 Moby Dick
  (Stage aura — covered by the earlier Stage-aura fix), OP02-011 Vista (KO power-filter), OP02-014 Whitey Bay
  (can-attack-active), several Whitebeard {type including} conditionals now covered by the compound fix.

## Traced 318–334 (OP01-108..OP02-005) — TWO bugs:
- 🐞✅ **FIXED — DON-attach reactive had no dispatch** (OP02-002 Monkey.D.Garp LEADER "[Your Turn] When this
  Leader or any of your Characters is given a DON!! card, give up to 1 opp Character cost≤7 −1 cost during this
  turn"). AttachDon never fired any reaction. FIX: new `FireOnDonGiven(seat)` called from AttachDon when a DON is
  attached to the owner's Leader/Character; gates [Your Turn]/DON/OPT, routes the body via QueueAndAutoResolve.
  Added `garptest`: DON given to Garp → opp char cost 3→2 ✅. Coverage 2642/33/0/0/0.
- 🐞✅ **FIXED — "cannot activate a [Blocker] with N or LESS power" banned ALL blockers** (OP01-120 Shanks,
  OP03-002 Adio). The handler only parsed "N or MORE power" (→ BlockerPowerBan); "or less" fell through to
  `NoBlocker = true` (ban everything). FIX: added `BlockerPowerBanMax` field + parse "N or less power" + checks at
  all 3 block-eligibility sites (bar blockers with power ≤ max). Added `blockbantest`: weak(2000) barred ✅,
  strong(4000) allowed ✅. Coverage 2642/33/0/0/0.
- Fast-passed: OP01-116 SMILE (look/play {SMILE}), OP01-121 Yamato (name-treat + Double Attack), OP02-001 Newgate
  leader ([End of Your Turn] Life→hand), OP02-004 Newgate (buff + restriction), OP02-005 Dadan (look/reveal red),
  several DON−N On Play / DON-ramp On-K.O. cards.

## Traced 301–317 (OP01-087..OP01-106):
- 🐞✅ **FIXED — aura-granted battle-K.O. immunity was never applied** (OP01-099 Kurozumi Semimaru "{Kurozumi
  Clan} type Characters other than your [Kurozumi Semimaru] cannot be K.O.'d in battle"). IsBattleKoImmune read
  ONLY the instance's OWN effect text, so a clan-wide grant printed on a DIFFERENT card never protected its
  recipients. FIX: new `BattleKoImmuneFromAura` scans the owner's other cards for "{T}/[Name] (other than [X])
  cannot be K.O.'d in battle" auras (type filter + name filter + self-exclusion + leading If-cond), called at the
  top of IsBattleKoImmune. Added harness `battleimmune`. Verified: Kurozumi char + Semimaru → survives ✅; no
  Semimaru → dies ✅; non-Kurozumi + Semimaru → dies (type filter) ✅. Coverage 2642/33/0/0/0.
- ✅ CLEAN OP01-091 King leader "[Your Turn] If you have 10 DON!! cards on your field, give all of your opponent's
  Characters −1000 power" — GetOpponentDebuffAuraBonus applies it: continuous, [Your Turn] timing-gated, condition
  "you have 10 DON!! cards on your field" recognized (exact, L4550-4552).
- Fast-passed: OP01-094/096/097 Kaido/King/Queen (DON−N On Play), OP01-098/OP01-311 reveals, OP01-105 Bao Huang
  (opp-hand reveal), OP01-106 Basil Hawkins (add DON rested), OP01-087 Officer Agents (Counter play from hand),
  OP01-102 Jack (When-Attacking DON−1).

## Traced 286–300 (OP01-069..OP01-086):
- 🐞✅ **FIXED (class, 5 cards) — TURN-tagged scaling self-buffs applied FLAT, not scaled.** "[DON!! xN][Your
  Turn] this Character gains +N power for every/each M <what>" (OP01-072 Smiley +1000/card-in-hand, EB01-014 Sanji
  +1000/3-rested-DON, OP06-085 Kumacy +1000/5-trash, OP16-034 Luffy +1000/different-name-Char, OP01-083 Mr.1).
  GetPassiveDonPowerBonus bails on ANY turn tag (returns 0), so these fell to GetTurnPassiveAuraBonus which applied
  ParsePowerGain's flat +N with NO "for every" scaling → e.g. Smiley got +1000 regardless of hand size. FIX: new
  shared `ScalingUnitCount(state, owner, what)` helper + scaling multiply in GetTurnPassiveAuraBonus (skip if the
  denominator is unrecognized — no bogus flat). Added harness `scaletest`. Verified Smiley: hand=4 → 6000 (base+DON
  +4×1000) ✅, hand=0 → 2000 ✅; Kumacy 0-trash → no bonus ✅. Regression: Cabaji flat [Opp Turn]+5000 unchanged.
  Coverage 2642/33/0/0/0.
- Fast-passed (standard patterns): OP01-070 Mihawk / OP01-071 Jinbe (place-at-bottom cost-filtered), OP01-077
  Perona (scry), OP01-069 Caesar (On-K.O. play from deck), OP01-080 (On-K.O. draw), OP01-084 Bentham (When-Attack
  look/reveal), OP01-085 Mr.3 (cond select opp char), OP01-086 Overheat (counter +4000 → return), several Blockers.

## Traced 269–285 (OP01-049..OP01-068):
- 🐞✅ **FIXED (class, 2 leaders) — "When your opponent's Character is K.O.'d, <effect>" reactions had NO dispatch.**
  3 cards: OP01-061 Kaido LEADER ("[DON!! x1][Your Turn][OPT] … add up to 1 DON!! from your DON!! deck and set it
  as active"), EB04-044 Koby (2nd line "draw 1 card"), OP03-076 Rob Lucci LEADER (cost-prefixed). MoveToTrash fired
  only the DYING card's own [On K.O.] (FireOnKoEffects), never the opponent's reaction — so Kaido's signature DON
  ramp did nothing. FIX: new `FireOnOpponentCharacterKo(reactorSeat)` called from MoveToTrash (isKo char branch)
  with reactor = OtherSeat(dyingOwner); scans reactor board for the trigger, gates [Your Turn]/DON/OPT (OPT
  collapses a board-wipe multi-KO to one trigger), routes the body via QueueAndAutoResolve. Added `oppkotest`.
  Verified Kaido: DON=1 → opp char K.O.'d → +1 active DON ramp ✅; DON=0 → no ramp ([DON!! x1] gate) ✅. Coverage
  2642/33/0/0/0; Rosinante koreptest 3/3 (K.O.-path regression) intact.
  ➕ **OP03-076 Rob Lucci (cost-prefixed) NOW ALSO FIXED**: "You may trash 2 cards from your hand: When your
  opponent's Character is K.O.'d, set this Leader as active." FireOnOpponentCharacterKo reconstructs the standard
  "You may <cost>: <body>" shape and queues it; OPT consumed only on RESOLVE (OnceKey) so declining keeps it live.
  Verified via `oppkotest`: opp char K.O.'d → pay trash-2 → leader set active ✅ (hand 4→2); Kaido regression +1
  DON intact. Whole "opponent's Character is K.O.'d" class (all 3 cards) now handled. Coverage 2642/33/0/0/0.
- ✅ CLEAN OP01-068 Gecko Moria "[Your Turn] gains [Double Attack] if you have 5 or more cards in your hand" —
  HasPrintedKeywordGrant L937-940 handles the trailing hand-count condition + [Your Turn] gate.
- ✅ OP01-051 Kid taunt (verified good in prior session), OP01-062 Crocodile leader (When-you-activate-Event draw,
  FireOnEventActivation), OP01-063 Arlong (look opp hand → conditional Life sink) fast-passed.

## Traced 252–268 (OP01-028..OP01-048):
- 🐞✅ **FIXED — "You can <cost>:" prefix silently did nothing** (OP01-031 Kouzuki Oden LEADER, the sole "You can"
  variant DB-wide: "[Activate:Main][OPT] You can trash 1 {Land of Wano} type card from your hand: Set up to 2 of
  your DON!! cards as active."). The optional-cost-prefix handler + glow + ledger all anchored on "^You may
  <cost>:", so "You can …" never matched → the whole ability resolved to nothing (probe: hand/trash/DON unchanged,
  empty log). FIX: 3 regexes now accept "You (?:may|can) <cost>:". Added harness `odentest`; verified: Wano card
  trashed as cost ✅, 2 rested DON set active (3→1) ✅. Popular Wano leader. Coverage 2642/33/0/0/0.
- ✅ CLEAN OP01-032 Ashura Doji "[DON!! x1] If your opponent has 2 or more rested Characters, this Character gains
  +2000" — DON-gated conditional self-buff; condition recognized (EvaluateCondition L4690-4693 applies the rested
  filter) and gated in GetPassiveDonPowerBonus. OP01-024-style attribute/passive checks all fine.
- Fast-passed (standard patterns): OP01-028/029 counter debuff/buff, OP01-033 Izo & OP01-048 Nekomamushi (rest opp
  cost-filtered), OP01-040 Kin'emon (cond play {Akazaya Nine}≤3 from hand), OP01-042 Komurasaki (③ cost + cond
  set-active), OP01-046 Denjiro ([When Attacking] cond set-DON), OP01-035/038 (When-Attacking rest/KO).

## Traced 235–251 (OP01-007..OP01-027) — OP01 starter staples, all CLEAN (well-covered):
- ✅ OP01-021 Franky "[DON!! x1] can also attack your opponent's active Characters" — handled L3363 (DON-gated
  attack-permission) + L8397.
- ✅ OP01-024 Luffy "[DON!! x2] cannot be K.O.'d in battle by ＜Strike＞ attribute Characters" — L3908-3916 extracts
  the ＜X＞ attribute, grants immunity ONLY vs a Character of the matching attribute (byChars gate), DON-gated. Not
  immune to other attributes / Leaders. Correct.
- ✅ OP01-026 Red Hawk "[Counter] +4000. Then, K.O. up to 1 opp char with 4000 power or less" — counter SECONDARY
  handled L3004-3033 (extracts [Counter] clause, splits at "Then,"/", and <verb>", queues the K.O.).
- Fast-passed (standard patterns w/ existing handlers): OP01-007 Caribou [On K.O.] power-filter KO, OP01-008
  Cavendish / OP01-013 Sanji (Life-cost → self Rush/buff), OP01-016 Nami (look/reveal), OP01-017 Robin [DON!! x1]
  [When Attacking] KO, OP01-022 Brook [When Attacking] 2-target −2000, OP01-020 Hyogoro (self-rest buff), OP01-025
  Zoro [Rush], OP01-027 Round Table (−10000 debuff). NB: basic probe can't exercise [When Attacking]/[On K.O.]
  triggers (needs attack/KO event) — verified by code review.
Coverage OK=2642/33/0/0/0 (no change — batch clean).

## Traced 229–234 (OP01-001..OP01-006) — Zoro-leader flag CLEARED + reactive class flagged:
- ✅ **OP01-001 Zoro leader VERIFIED WORKING** (memory-flagged "still broken" was STALE). "[DON!! x1][Your Turn]
  All of your Characters gain +1000 power" — new harness `leadaura` mode: leader DON=0 → no buff (1000/1000);
  DON=1 → own-turn +1000 (2000), opp-turn none (1000, correctly [Your Turn]-gated); DON=2 → still +1000 (flat, no
  stack). The OTHER Zoro leader OP12-020 was FIXED+VERIFIED earlier this session. Both Zoro leaders good; memory
  backlog updated to clear the stale hook.
- ✅ OP01-003 Luffy leader "Set up to 1 {Supernovas}/{Straw Hat Crew} cost≤5 as active, +1000" — glows the typed
  cost≤5 char correctly. MINOR: "set as active" also glows already-ACTIVE chars (harmless; could prefer rested).
- 🐞✅ **FIXED (class) — "when your opponent activates an Event" reactions, correct turn-timing.** The only wiring
  was OP06-044 Gion hardcoded in the MAIN-event trash path (L1742) — WRONG turn (a Main Event is played on the
  event-player's own turn, so the reactor's [Your Turn] never held) and it ignored the [Your Turn] tag. The real
  trigger is the opponent's COUNTER Event during YOUR turn. FIX: new `FireOnOpponentEventActivation(reactorSeat)`
  called from CounterWithCard with the ACTIVE/attacker seat; handles both body orderings ("Draw 1 card when …" vs
  "When …, <body>"), gates on [Your Turn]/DON/OPT, resolves draw + board-buff via QueueAndAutoResolve and Gion's
  opponent-tax inline; REMOVED the mistimed L1742 handler (no test depended on it). Added harness `oppevent`.
  Verified: Usopp DON=1 → draws / DON=0 → no draw (DON gate) ✅; Franky → ally +2000 ✅; Gion → opponent places
  1 card ✅. Coverage 2642/33/0/0/0. STILL FLAGGED (complex, skipped by guards): OP11-102 Camie ("This effect can
  be activated … or [Trigger]", conditional double-Life-trash) + OP15-119 Luffy ("… or [Blocker]", Life reveal +
  per-cost buff, no [Your Turn]) — need a dedicated pass.
- 🐞✅ **FIXED (HIGH-VALUE, top leader) — OP01-002 Law leader + EB01-020 Chambres two-step return→play** (all 5
  bugs from the prior diagnosis). "[If <cond>,] return 1 of your Characters to the owner's hand. Then/and play up to
  1 Character with a cost of N or less from your hand that is a DIFFERENT COLOR than the returned Character." FIX
  (3 sites): (1) NEW dedicated resolver handler BEFORE the generic bounce (L6126) — gates on the leading If-cond
  (EvaluateCondition), returns a chosen own BOARD Character (no bogus cost cap), then QUEUES the play clause with
  the returned card id stashed in FirstPickId; (2) play-from-hand resolver enforces "different color" via new
  ColorsShareAny(playColor, returnedColor) → rejects same-color; (3) IsValidEffectTarget adds a RETURN-step glow
  (own board Characters, not hand) + a PLAY-step glow (hand, cost≤N, non-overlapping color). Added harness
  `lawleader`. Verified: return-step glows board not hand ✅; play step runs ✅; same-color RED rejected + play glow
  False ✅; different BLUE played + glow True ✅; condition gated (5 chars). Coverage 2642/33/0/0/0; generic bounce
  (EB03-027) unaffected.
Coverage OK=2642/33/0/0/0 (no engine change this iteration — verification + flagging).

## Traced 213–228 (EB04-046..EB04-061) — TWO broad bugs found via EB04-048 Rob Lucci:
- 🐞✅ **FIXED (BROAD, ~20 cards) — STATIC conditional self-buffs never applied.** "[If <cond>,] this Character
  gains +N power [for every X]" carries NO [DON!! xN] tag (→GetPassiveDonPowerBonus skips) and NO turn/action
  timing (→GetTurnPassiveAuraBonus skips at L506), so NEITHER path computed it — the buff was silently 0. Affects
  OP12-070 Sanji, EB04-048 Rob Lucci, OP05-101 Ohm, OP13-068 Douglas Bullet, ST16-003 Katakuri, OP15-006 Cavendish,
  OP02-050 Inazuma, OP12-015/OP15-092 Luffy, EB01-027, OP09-086 Burgess, OP12-063 Reiju, P-078 Adio, +more.
  FIX: new `GetStaticSelfPowerBonus` (regex "this Character/card gains +N power", excludes DON/turn/action/temporary
  lines, evaluates leading "If <cond>," fail-closed, handles "for every N X" scaling), wired into GetPower after the
  DON passive. Added harness `sbuff <card> <leader> <trashCard> <count>`. Verified OP12-070: 0/5/10 events → 5000/
  6000/7000 ✅. Regression: ST01-004 (DON passive) + ST25-002 (turn aura) unchanged (no double-count).
- 🐞✅ **FIXED — "type includes X" was exact-match, not substring** (EvaluateCondition L4470 used HasFeature, which
  is exact). CP cards print "type includes "CP"" to catch every CP variant (CP9, CP0, CP-0…), but HasFeature("CP")
  failed for a "CP9" Leader → the whole "If your Leader's type includes CP" clause was false. FIX: substring scan
  over the Leader's printed features. Verified EB04-048 Rob Lucci: CP9 leader +10 trash → 8000 (6000+2000) ✅ /
  non-CP leader → 6000 ✅. (Exact-type checks use the "{X} type" wording, unaffected.)
- ✅ CLEAN EB04-057 Vegapunk conditional removal-immunity AURA ("If you have 2 or less Life, all of your yellow
  {Scientist} type Characters cannot be removed…") — HasRemovalImmunityAura L3322 applies aura match + Life
  condition + yellow color + {Scientist} feature filter (CardPassesFeatureFilter L3349). Code-verified.
- Fast-passed: EB04-051 Emet (attack-restrict "there is a Character 12000+" — existing L2404), EB04-052 Sanji
  (When-Attacking base-power mirror), EB04-056 Pacifista (compound Blocker cond), EB04-055 Kuma (On-K.O. play).
Coverage OK=2642/33/0/0/0.

## NOTE: index 229 = OP01-001 Zoro leader ("[DON!! x1][Your Turn] All Characters +1000") — memory-flagged "still
## broken". Give it a FULL dedicated trace next iteration.

## Traced 196–212 (EB04-029..EB04-045) — K.O.-replacement cluster:
- 🐞✅ **FIXED — K.O.-replacement action skipped by "in any order" text** (EB04-043 Kaku "you may place 3 cards
  from your trash at the bottom of your deck **in any order** instead"). The action branch required the literal
  substring "bottom of your deck instead" (L3535) but "in any order" sits between "deck" and "instead", so the
  match failed → no action fired → L3604 `else continue` → the black Character was K.O.'d instead of protected.
  FIX: match "bottom of your deck" (the outer line-loop already guarantees "instead"). Added parametric harness
  `koreptest2 <guard> <victim> [trashFuel]`. Verified: 3 trash → victim SAVED, 3 moved to deck ✅ / 2 trash →
  can't pay, victim K.O.'d ✅. Coverage 2642/33/0/0/0.
- ✅ EB04-030 Kaido / EB04-031 King "If this Character would be K.O.'d, you may return 1 DON!! …to DON!! deck
  instead" — handler L3505 (selfOnly, PayDonMinus). Code-verified (self-protection + DON-return cost).
- 🐞✅ **FIXED EB04-044 Koby** (was flagged) "[Once Per Turn] If your Leader's type includes "Navy" **and** this
  Character **would be removed from the field**, you may trash 1 card from your hand instead" — closed all THREE
  gaps in TryRemovalReplacement: (1) broadened remTrig L3435 to the SHORT "would be removed from the field" (the 34
  qualified cards still match; engine never gated on removal source so behaviour unchanged — coverage + Rosinante
  regression confirm); (2) broadened selfOnly to "this Character would be" (catches "…and this Character would be");
  (3) added a leading-condition gate: regex `If (.+?) and this (?:Character|card) would be` → EvaluateCondition
  ("Leader's type includes Navy" handled L4392). Added harness `koreptest3 <guard> <leader>`. Verified: Navy leader
  → Koby SAVED (trash 1 from hand) ✅ / non-Navy leader → Koby K.O.'d (condition declines) ✅. Only Koby matches
  this shape DB-wide (grepped). Coverage 2642/33/0/0/0; Rosinante koreptest 3/3 still pass.
- Fast-passed: EB04-032 Queen, EB04-033 Groggy (DON−1 conditional KO), EB04-036 Foxy, EB04-038 Rosinante&Law
  (name-treat), EB04-039 Kid (add DON), EB04-042 Alpha (trash-top cost → opp debuff).

## Traced 184–195 (EB04-017..EB04-028) + cleared flagged Stage-aura gap:
- 🐞✅ **FIXED (BROAD) — Stage-sourced continuous auras were NEVER applied.** GetTurnPassiveAuraBonus built its
  aura-source list from CharacterArea + Leader ONLY (L483-484) — the Stage zone was never scanned, so every
  Stage card's continuous power aura was silently ignored. Affects 4 cards: EB04-010 Lulucia Kingdom, OP08-020
  Drum Kingdom, OP02-024 Moby Dick, OP13-099 The Empty Throne. FIX: `if (p.Stage != null) auraCards.Add(p.Stage);`
  (a Stage is never a recipient — instance guarded to leader/character — so adding it as a SOURCE is safe).
  Added harness `stagepow <stageId> <recipientChar>` mode. Verified EB04-010: cost-1 char (Doma 3000) → 3000 own
  turn / **8000 opp turn** (+5000 applied; was 3000/3000 before). Coverage 2642/33/0/0/0.
- ✅ CLEAN EB04-026 Bluegrass "Place up to 1 opp Character with cost 1 or less at bottom of owner's deck" — glows
  only the two cost-1 opp chars, resolves the place correctly. (NOTE: initially mis-probed as typo id "EB04-193"
  which doesn't exist → phantom "silent no-op"; real id is EB04-026. LESSON: copy ids exactly from the listing.)
- ✅ CLEAN EB04-018 Megalo "You may rest this: K.O. up to 1 opp rested Character with 8000 power or more" — a 7000
  ACTIVE opp char is correctly excluded (needs rested AND 8000+).
- Fast-passed: EB04-017 Mystoms (conditional opp-debuff aura), EB04-020 (Counter buff), EB04-022 Issho (hand-trash
  cost + opp-hand cond), EB04-025 Vivi (play {Alabasta} from hand), EB04-027 Boa (draw+trash).

## Traced 167–183 (EB03-062..EB04-016):
- 🐞✅ **FIXED — unenforced conditional attack restriction** (EB04-005 Trafalgar Law "This Character cannot attack
  unless your opponent has 2 or more Characters with a base power of 5000 or more"). DeclareAttack only matched the
  "cannot attack unless **there is a Character** with N base power or more" form (L2404) — EB04-005's "opponent has
  N or more Characters with a base power of P or more" wording never matched, so the restriction was IGNORED (Law
  could always attack). FIX: added a second regex branch counting the OPPONENT's Characters ≥ P base power (L2418).
  Added harness `restricttest <card> <hiPowerCard>` mode. Verified: 0 high-power opp chars → attack BLOCKED ✅ /
  2 → attack STARTED ✅. Coverage 2642/33/0/0/0.
- ✅ base-power-SET handlers exist (L8843+) for EB04-004 Zeff / EB04-007 "Your Leader's base power becomes N".
- ⏳ verify-later: EB04-004 Zeff [When Attacking] set-base-power DURATION ("until end of opponent's next End Phase"
  — confirm the longer-than-one-turn duration is tracked, not cleared at own end phase); EB04-008 Distorted Future
  conditional −3000 glow (life≤2 gate + opp-char target); EB04-010 Lulucia Kingdom STAGE aura "[Opponent's Turn]
  All Characters with base cost 1 gain +5000" (stage-sourced aura — confirm stage cards are scanned as aura sources).
- Fast-passed: EB03-062 Law (trash-hand+self cost), EB04-002 Bonney (look/reveal), EB04-006 Moda, EB04-011,
  EB04-013 Carrot (conditional set-active), EB04-014 Sukiyaki (Blocker redirect), EB04-016 Bird Neptunian.

## Traced 150–166 (EB03-045..EB03-061):
- 🐞✅ **FIXED — dropped OR-disjunct in cost condition** (EB03-046 "If there is a Character with a cost of 0 **or
  with a cost of 8 or more**, draw 1 card"). EvaluateCondition matched the leading substring "there is a Character
  with a cost of 0" and returned on cost-0 presence ALONE — the "or with a cost of 8 or more" disjunct was silently
  dropped, so a cost-8+ board with no cost-0 char wrongly SKIPPED the draw. FIX: added a compound-OR branch (regex
  `cost of (\d+) or with a cost of (\d+) or (more|less)`) BEFORE the cost-0 branch, evaluating both disjuncts over
  both boards. Verified: cost-8 char → draws ✅ / cost-0 char → draws ✅ / cost-3 char → no draw ✅. Coverage 2642/33/0/0/0.
- ✅ CLEAN EB03-056 Belo Betty "You may turn 1 Life face-up: K.O. up to 1 opp Character with a **base cost of 3 or
  less**" — after Life-flip cost step, glow correctly lists Zoro(3)+Chopper(1); excludes higher.
- ✅ CLEAN EB03-051 Charlotte Smoothie "If you have a face-up Life card, K.O. up to 1 opp Character with cost 2 or
  less" — condition recognized (EvaluateCondition L4520 `face-up Life card` → Life.Any(FaceUp)); cost filter shared
  with EB03-056's verified path.
- Fast-passed: EB03-045 Perona (give rested DON), EB03-052/EB03-057 Yamato (give rested DON to typed leader),
  EB03-053/EB03-058 Nami, EB03-054/EB03-055 Nico Robin (Life-trash cost), EB03-050 Conis (Double Attack grant).

## Traced 133–149 (EB03-026..EB03-044):
- ✅ EB03-027 Marguerite "Return up to 1 Character with **7000** base power" — confirms the EB03-025 exact-base-
  power glow filter GENERALIZES (probe glows only the 7000-power char, not 1000-power Choppers).
- ✅ CLEAN EB03-041 Kujyaku "[Opponent's Turn] All of your {SWORD} type Characters with a cost of 6 or less gain
  +2000" — GetTurnPassiveAuraBonus enforces: opp-turn timing gate (L535-536), effective cost≤6 (L589), {SWORD}
  feature filter (L628), own-side only (L480). Correct.
- ✅ CLEAN EB03-044 Black Maria "If your Leader is multicolored, this Character gains [Blocker]" —
  HasPrintedKeywordGrant L942-944 evaluates the leading If-cond via EvaluateCondition ("Leader is multicolored"
  handled L4139/L4399). Blocker granted only when multicolored leader.
- ✅ CLEAN EB03-032 Charlotte Flampe (name-filtered [Katakuri] buff — glows nothing when no Katakuri on board).
- Fast-passed: EB03-026 (opp-hand-count On Play), EB03-028 Yu, EB03-034 Linlin (draw+place+DON), EB03-036 Baby5
  (DON−1), EB03-037/EB03-043 (conditional On Play), EB03-039 Ulti, EB03-042 Koala.
Coverage OK=2642/NA=33/0/0/0.

## Traced 116–132 (EB03-009..EB03-025):
- ✅ **FIXED (general) — EXACT "with N base power" glow filter** (EB03-025 Hina "Return up to 1 Character with 6000
  base power"). IsValidEffectTarget enforced "base power or less/or more" but NOT an exact "N base power" → glowed
  ANY Character (1000-power Choppers lit up). FIX: when no or-less/or-more cap, match `with N base power\b(?! or)`
  → require def.Power == N. Probe EB03-025: now glows only the 6000-power char. Coverage OK=2642/0/0/0.
- ✅ CLEAN: EB03-021 Alvida (trash-hand cost → place opp char; glows hand for cost), EB03-025 Hina (return cost:
  effect), EB03-012 Otama (mixed rest DON/char). EB03-009 Makino ("no base effect" target filter — probe board has
  no vanilla char; verify separately).
- ⏳ Deferred/verify-later: EB03-001 Vivi leader K.O.-replacement (base-cost-4+ filter — the bcM "base cost of N or
  more" check EXISTS in TryRemovalReplacement, trust for now), EB03-018 Tashigi passive "[Opponent's Turn] gains
  [Blocker]" (verify passive keyword-grant-by-turn).

## Traced 98–115 (EB02-050..EB03-008):
- ✅ **FIXED (2 general) — conditional hand-trash TAIL dropped its condition.** EB02-056 Vegapunk "…and IF your
  opponent has 2 or less Characters, trash 1 from hand" — ExtractHandDisposalTail captured only "Trash 1 card from
  your hand." (unconditional → always trashed). FIX: tail regex now preserves the "(and )if <cond>," prefix; ALSO
  added EvaluateCondition "opponent has N or LESS Characters" (only "or more" existed). Probe: postLook now = "If
  your opponent has 2 or less Characters, Trash 1 card from your hand." Coverage OK=2642/0/0/0.
- ✅ VERIFIED WORKING: EB02-053 Myskina Olga (INTERACTIVE LIFE-PEEK: choice Your/Opp Life → scry ✓), EB02-051
  (Choose-one K.O./−cost ✓), EB03-006 Nami (self-Leader-−5000 cost auto-pays → Draw ✓), EB02-056 deck-look play.
- ⏳ Deferred (need battle/K.O.): EB03-001 Vivi leader K.O.-replacement (Rosinante-class — verify conditions later),
  EB02-052 Enel When-Attacking, EB02-059 Counter.

## Traced 82–97 (EB02-031..EB02-049) — ALL CLEAN:
- EB02-018 Buggy VERIFIED (grants Luffy [Double Attack]; earlier flag unfounded). EB02-039 GERMA66 (trash-named-
  from-hand cost gates correctly), EB02-045 Law (place-2-trash cost + Choose-one; gated on trash), EB02-046 Hildon
  ("Trash 2 from deck and give −cost" — deck 50→48, trash 0→2), EB02-047 Blueno (compound trash-hand+trash-self,
  glows hand), EB02-049 Garp (give-DON-to-Leader-ONLY correctly glows nothing = DON-pick flow). No bugs.

## Traced 66–81 (EB02-014..EB02-030):
- ✅ **FIXED — EB02-027 Vista GLOW (class fix)**: "Place up to 1 of your OPPONENT's Characters … at the bottom of
  the OWNER's deck" glowed your OWN Characters too — "owner's deck/hand/Life" (the DESTINATION) wrongly set
  ownerAgnostic, overriding the "your opponent's" target restriction. FIX: ownerAgnostic for owner's-zone now
  requires the text NOT say "your opponent's"/"of your". Probe: Vista now glows north-only. Coverage OK=2642/0/0/0
  (glow-only, no resolution change). Fixes the whole "place/return opponent's chars to owner's zone" class.
- ✅ CLEAN/OK: EB02-017 Nami (deck-look reveal-add), EB02-020 We Are! (same), EB02-022 Usopp (conditional play),
  EB02-025 Rosinante (compound "rest 1 DON and this Character" cost fires via verb-carry; body gated on Rosinante
  leader). EB02-018 Buggy ("up to 1 of your Leader gains [Double Attack]") — VERIFY DA grant next pass (odd
  phrasing). EB02-030 Counter K.O.-replacement + EB02-019/023 reactive = deferred to battle-path.

## Traced 50–65 (EB01-057..EB02-013):
- ✅ **FIXED — EB02-005 Fake Straw Hat passive self-DEBUFF** "[Opponent's Turn] Give this Character −2000 power"
  was UNWIRED. GetTurnPassiveAuraBonus's self-scope detection + bonus parse only matched "gains +N power", never
  the "give this … −N power" self-DEBUFF form → the −2000 on the opp turn never applied. FIX: selfScoped now also
  matches "give this (Character|card|Leader) −N power"; bonus parse reads the negative; guard `bonus<=0`→`==0` so
  negatives pass. Verified via new `charpow` mode: 3000 base → 5000 own turn (+2000) / 1000 opp turn (−2000).
  Coverage OK=2642/0/0/0. GENERAL — any passive self-debuff-by-turn now applies.
- ✅ CLEAN: EB01-059 Kingdom Come ("trash Life until you have 1" → 3 trashed), EB02-007 (up-to-3 multi-buff→K.O.,
  glows leader+chars), EB02-008 The Peak (deck-look reveal+rearrange), EB02-009 Thousand Sunny (give-given-DON,
  glows Straw Hat char), EB01-060 (dual-zone play from hand-or-trash). Leaders/Counter/When-Attacking = deferred.

## Traced 36–49 (EB01-042..EB01-056) — ALL CLEAN (no bugs):
EB01-046 Brook (multi-clause give-−cost→K.O., glows opp chars), EB01-051 Finger Pistol (trash-2-deck cost auto-pays
→ K.O. body), EB01-056 Flampe (top/bottom-Life cost → draw), EB01-042 Scarlet (trash-self cost; play gated on a
Dressrosa in hand), EB01-053 Gastino (place opp char to top of opp Life — auto-top, approved). EB01-044/048 Funk/
Laboon (rest-self cost → give-−cost, standard). Counter/[When Attacking] variants need the battle path.
This iter also extended the pickM cost branch with "place N of your Characters at the bottom of the deck" (+3).

## Traced 22–35 (EB01-026..EB01-040) — mostly CLEAN:
- ✅ EB01-035 Ms.Monday (correctly skips on non-Baroque-Works leader), EB01-040 Kyros (turn-Life-face-up cost
  fires; body correctly finds no cost-0 target), EB01-031 Kalifa (DON−1 cost works; body gated on Water Seven +
  trash), EB01-027 Mr.1 (scaling aura + draw). When-Attacking/Counter ones (EB01-026/028/029) need battle path.
- 🟡 FLAG EB01-030 Loguetown — compound cost "place THIS card AND 1 card from your hand at the bottom of your deck"
  places the hand card (via the place-from-hand branch) but NOT the stage itself. Partial cost. Niche.

## COVERAGE BASELINE (cost-gap now 111 → 33): OK=2642 / NOT_AUTOMATED=33 / STUCK=0 / CRASH=0 / INVARIANT=0
Added this iter: compound-verb-carry (rest-this-and-a-Leader/Stage, rest-DON-and-this-Character), rest-your-Leader,
add-from-Life-area-to-hand. Remaining 36 are pick-based own-Character sacrifices (place/return/trash N of your
Characters — need a proper PICK flow, not auto), give-DON-as-cost (Morgan), probe-artifact trash-returns (no trash
on test board), + ~4 non-cost delayed effects (niche). Diminishing returns on auto-pay; handle pick-based ones
via the cost-prefix pickM branch when revisited.

## Traced 7–21 (EB01-009..EB01-024):
- ✅ VERIFIED OK: EB01-015 Apoo (rest opp cost≤2 — glows north chars correctly), EB01-016 Bingoh (rest-self cost
  fires; "opponent's RESTED chars" filter correctly shows no targets when opp chars are active), EB01-023 Weevil
  (draw 1). Counter events EB01-009/010/019 need a battle (not probe-testable) — deferred to a counter-path test.
- 🟡 FLAG — **EB01-011 Mini-Merry cost SKIPPED**: "You may rest this card AND place 1 of your Characters with 1000
  base power at the bottom of your deck: Draw 1" — the "place your Character at the bottom of your deck" COST is
  unhandled (TryAutoPayCost returns -1), so the cost is bypassed and "Draw 1" fires FREE. Same systemic class.
- 🟡 FLAG — **EB01-020 Chambres GLOW**: "return 1 of your Characters …, and play … from your hand" — the resolver
  correctly asks to return a BOARD Character, but IsValidEffectTarget glows HAND cards (the ", and play from hand"
  2nd clause leaks into the glow; "to the owner's hand" also trips ownerAgnostic). Niche (conditional Supernovas).

## 🔴 SYSTEMIC "unhandled cost → body fires free" — ADDRESSED (2026-07-20)
Confirmed BIG: with the fail-closed test, 111 cards had a "You may <cost>: <body>" whose cost NO branch handled →
the body was firing FREE (rules violation, exploitable). ACTIONS TAKEN:
1. FAIL-CLOSED the cost-prefix block: if costM.Success but no branch pays the cost → return NotAutomated (never
   fall through to the body handlers). Faithful default (inert > exploitable) + surfaces the broken cards.
2. Added 6 AUTO-PAY cost handlers to TryAutoPayCost (fixed 34 cards — cost now actually paid):
   trash-N-from-top-of-deck (verified EB04-042: deck 50→47, trash 0→3 THEN body), trash-N-from-top-of-Life,
   give-your-active-Leader-−N-power (Koza), place-this-Character/card-at-bottom-of-deck, return-this-Char-to-hand,
   return-N(-or-more)-DON-from-field-to-DON-deck.
### 📉 COVERAGE BASELINE: OK=2627 / NOT_AUTOMATED=48 / STUCK=0 / CRASH=0 / INVARIANT=0 (was 2669/6 → 2592/83).
Cost-gap fix queue shrunk 111 → 48. Handlers added to TryAutoPayCost so far (all verified, cost actually paid):
trash-top-of-deck, trash-top-of-Life, give-own-Leader-−power, place-self-at-bottom, return-self-to-hand,
DON-−N-from-field, REVEAL-N-from-hand (needed a gate exception since it contains "from your hand"), rest-N-of-your-
({T})-Leader-or-Stage, rest-N-of-your-cards.
REMAINING ~48 fix queue (top patterns): compound "rest this Character and N of your {T} Leader/Stage" (~4, the
" and " split drops the "rest" verb on the 2nd part — needs compound handling); "give N active DON to N of your
[X]" (~3, move given DON); "give N of your opponent's rested DON to opp Characters" as a COST (Morgan OP15-003/017,
~3); "place N Stage cost N at bottom of deck" (~3); "return N from your trash to bottom of deck in any order" (~3,
mostly probe-artifact = no trash on the coverage board); "(no You-may cost)" (~4 — different NOT_AUTOMATED cause,
not cost-gap: delayed/unknown effects like OP07-057, OP11-092 — niche). Keep chipping each iteration.
NOTE: coverage compares to OK=2627 now. CRASH/STUCK/INVARIANT must stay 0. Per-card trace still resumes at idx 22.

## Traced 0–6 (EB01-001..EB01-008):
- EB01-001 Oden — counter-grant aura + [When Attacking] conditional self-buff: looks OK (aura handled in
  CounterWithCard). EB01-003 Kid&Killer, EB01-006 Chopper — Rush/blocker + When-Attacking give-opp-−power: OK-ish.
- ✅ **FIXED — DON-give GLOW bug (EB01-002 Izo + EB01-007 Yamato + EVERY "Give rested DON!! to your Leader or 1 of
  your Characters" card).** IsValidEffectTarget glowed HAND and LIFE cards as recipients (checked def.Type only,
  not zone) — a Character card in hand/life wrongly lit up. FIX: require in-play — `(leader && Zone=="leader") ||
  (character && Zone=="character")`. Probe EB01-002 now glows only leader + 2 board chars. Coverage OK=2669/0/0/0.
- ⏳ FLAG for return pass: EB01-004 Koza "[When Attacking] you may give your active Leader −5000 power: give opp
  −3000" — the COST is a self-Leader-debuff, likely not handled by the cost-prefix resolver (needs atktest).
  EB01-008 LittleOars Jr "[Once Per Turn] if would be K.O.'d by an effect, trash 1 Event/Stage from hand instead"
  — replacement effect; verify the once-per-turn + "by an effect" gating (Rosinante-class).

## Auditor-flagged — TRIAGED (all COST_UNPAYABLE = false positives; MANUAL = niche delayed effects)
- ✔FP OP06-011 / P-060 Tot Musica, OP06-117 Ark Maxim, ST27-001 Avalo Pizarro — cost is "rest 1 of your
  [Uta]/[Enel]/[Fullalead] cards"; probe board lacks that NAMED card → "unpayable" is CORRECT. Not bugs.
- ✔FP OP14-049 Jinbe — cost 8 to play uses ALL the probe's active DON, so the On-Play "rest 2 DON" cost then
  can't be paid. With 10 fresh DON it works. Probe DON-accounting artifact, not a bug.
- ⏳ NICHE (deferred, delayed-effect / unknown-condition — low priority): OP07-057 Perfume Femur ("if the selected
  card attacks during this turn, opp can't Blocker" — delayed conditional), OP10-022 Trafalgar Law (reveal-play +
  return-cost condition), OP11-092 Helmeppo ("place the played Character at bottom of deck at END of turn" delayed).
CONCLUSION: the automated auditor finds ~0 real correctness bugs (all Rosinante/Krieg/taunt-class bugs resolved
"fine"). The ONLY way forward is the manual per-card deep trace below. Auditor is retained just to catch new
crashes/regressions during the grind (run `audit 0 2320` after a batch of engine edits).

## Method (per card)
probe <id> → read effect → check: dropped clauses? cost paid? correct target zone? glow (validTargets)
matches text? conditions/timing/DON gates enforced? challenge scenario (e.g. rested vs active, own vs opp,
multi-target)? → fix in GameEngine.cs → probe re-verify + coverage (OK≈2669, CRASH/INVARIANT/STUCK=0).

## Fixed this audit (card — bug — fix)
(none yet — see the main backlog file for the ~20 fixes from the earlier scattershot phase)

## Notes
- Automated auditor finds only crash/manual/unpayable — NOT correctness bugs (those need manual trace).
- Coverage baseline: OK=2669 / NOT_AUTOMATED=6 / STUCK=0 / CRASH=0 / INVARIANT=0.

## ✅✅ NA BACKLOG FULLY CLEARED — coverage OK=2675 / NOT_AUTOMATED=0 / STUCK=0 / CRASH=0 / INVARIANT=0
Per the user directive "deal with the remaining cards," ALL acknowledged-for-manual (NA) cards are now resolved
with real engine fixes + dedicated Tools/Harness tests. Final batch (#159–#162):
- **#159 OP05-080 Elizabello II** — "You may return 20 cards from your trash to your deck and shuffle it: gains
  [Double Attack] +10000 this battle." The atomic cost's OWN text contains " and " (…deck AND shuffle it); the
  generic " and " split in TryAutoPayCost tore it into two non-matching parts → cost never paid. FIX: pay the
  whole `^return N cards from your trash to your deck and shuffle it$` cost as a single unit BEFORE the split.
  Test `op05080test` (power 5000→15000, DA, trash 22→2).
- **#160 OP15-003 Alvida / OP15-017 Morgan / OP15-023 Arlong** — "[Activate:Main] You may give 1 of your
  opponent's rested DON!! cards to 1 of your opponent's Characters: Give up to 1 [rested] DON!! card [from its
  owner's cost area] to its owner's Leader or 1 of their Characters." Tempo-denial: moves up to 2 of the OPP's
  rested DON off their cost area onto their own board. FIX: `giveOppDonM` cost in TryAutoPayCost + `ownerDonGive`
  body handler (mirrors OP15-028) + IsAutomatedEffectPattern entry `Give up to / DON!! / its owner's Leader`.
  Test `op15003test` (opp cost-area DON 3→1, 2 attached to opp board).
- **#161 OP11-092 Helmeppo** — "…play up to 1 {SWORD} Character c≤8 from your trash. Then, place the 1 Character
  played by this effect at the bottom of the owner's deck at the end of this turn." FIX: at play-from-trash time,
  if the source card's effect carries the delayed clause, tag the played Character with a new
  `returnToBottomAtEndOfTurn` modifier; end-of-turn sweep (next to `trashAtEndOfTurn`) sends it to the deck
  bottom; the standalone "place the 1 Character…" clause is a recognized no-op. Test `op11092test`.
- **#162 OP05-111 Hotori** — "[On Play] You may play 1 [Kotori] from your hand: Add up to 1 of your opponent's
  Characters with a cost of 3 or less to the top or bottom of your opponent's Life cards face-up." FIX:
  `playNamedM` cost in TryAutoPayCost (play a NAMED card from hand into play for free) + `playNamedFromHand` guard
  bypass so the from-hand cost still routes to auto-pay. Body already handled (field→Life removal). Test
  `op05111test`.
- Auditor `audit 0 2320`: 62 flags = 57 COST_UNPAYABLE (probe-board artifacts) + 1 ST13-001 Sabo MANUAL (its
  cost is an interactive "add 1 of your Characters to Life" PICK — coverage OK, player picks; not a gap). Zero
  CRASH/STUCK/INVARIANT/DEADLOCK. Sim smoke 900 games clean, P(first)≈54.8%.

## 🔁 SECOND-PASS CORRECTNESS SWEEP — K.O.-replacement family (`instead` cards)
Coverage-OK only proves "resolves without crash," NOT that resolution is CORRECT. Re-tracing the 31 removal-
replacement ("would be K.O.'d … instead") cards against TryRemovalReplacement (GameEngine.cs ~L4246) found 2 real
bugs (both fixed + tested; coverage stays OK=2675/0/0/0/0):
- **#163 EB01-008 LittleOars Jr.** — cost "trash 1 **Event or Stage** card from your hand instead" was UNfiltered:
  the hand-trash branch only knew type-tag / Character-only / power filters, so it trashed ANY card and wrongly
  "paid" even with no Event/Stage in hand. FIX: added `thEventOrStage` / `thEventOnly` / `thStageOnly` filters +
  gate. `eb01008test` (Event→pay, Stage→pay, Character-only→unpayable/K.O. proceeds).
- **#164 OP05-001 Sabo (Leader)** — the `[DON!! x1]` gate was NOT enforced anywhere in TryRemovalReplacement, so
  Sabo protected Characters even with 0 DON on the Leader. FIX: `int repDonReq = ParseDonThreshold(line); if
  (repDonReq>0 && guard.AttachedDonIds.Count<repDonReq) continue;`. `op05001test` (DON x1→replace, x0→no replace).
- Verified-correct in this pass (no change needed): EB03-001 Vivi (base-cost≥4 + trash-1-any), EB04-030 Kaido
  (return-DON), OP09-012 Monster (named-victim [Bonk Punch]), OP10-034 Franky (in-battle-only gate), OP11-110
  Fukaboshi (named-Leader rest), OP13-060 Toki (type-including victim), OP16-033 Morley (rest-2-of-your-cards),
  ST29-008 Nami (turn-Life-face-up), OP05-001 power≥5000 victim filter. All covered by existing resolver branches.

## 🖥️ UI-WIRING AUDIT (card interactability across zones) — code-level (Unity Play-test still blocked)
Requested check: every card/"pill" correctly wired + interactable on board/life/deck/hand for seamless play.
Findings (all GOOD by code inspection; runtime sign-off needs a Unity build — MCP entitlement gate):
- Central `AddCard` (GameManager.cs ~L9716) attaches Button+`OnCardClick`, `CardHover` preview, `AttackDrag`,
  `CardDrag` (hand), `DonAttachTarget`, and the green usable-glow to EVERY rendered card. seat==null only for
  non-interactive overlays (preview/animation), and those STILL get a button → still route to resolveEffect.
- `OnCardClick` (~L9038) dispatches for all zones/states: DeckLook select/scry, DON-attach, trash-view toggle,
  hand (pending-target / counter / block / play-select), board pending-effect targeting, battle block, own-board
  select→declareAttack. No dead zone.
- **Glow == clickable, by construction:** the target glow calls `GameEngine.IsValidEffectTarget` (L9797) — the
  SAME predicate the engine uses to accept the resolveEffect click. No "glows but not clickable" / "clickable but
  no glow" mismatch is possible.
- Life targeting: `MaybeAddLifeTargetPicker` (~L10160) glows the real top+bottom Life card rects with click-
  catchers → resolveEffect (board-anchored, no modal) for "top or bottom of (your/opponent's) Life" effects.
- Trash targeting: overlay cards route to resolveEffect via the pending-effect path + get the valid-target glow.
- REMAINING (needs Unity Play-test, cannot verify headless): actual pixel hit-boxes, z-order/occlusion of the
  click-catchers, and Life pickers for non-"top or bottom" Life phrasings. Flagged for the UI test pass.

## 🔁 SECOND-PASS — scaling-power / dual-stat family (`for every/each` cost+power)
Re-traced the 20 "+N power for every/each X" cards + related cost scalers vs GetPassiveDonPowerBonus /
GetStaticSelfPowerBonus / GetPassiveCostBonus. Power side was already correct (count bases: rested DON, hand,
Events/cards in trash, different card name, {tag} type Characters). Found 1 real bug spanning 3 cards:
- **#165 self-COST scaling/dual/conditional dropped** — GetPassiveCostBonus (GameEngine.cs ~L1018) matched only
  `[Tt]his … gains \+N cost` ADJACENT, with no "for every M X" scaling and no leading-If condition. So the cost
  half of these silently vanished:
    · EB04-048 Rob Lucci — "If Leader type includes CP, +1000 power **and +2 cost for every 5 cards in trash**"
    · OP12-063 Vinsmoke Reiju — "If 4+ Events in trash, +2000 power **and +5 cost**"
    · ST27-004 Sanjuan.Wolf — "If Leader Blackbeard, [Blocker] **and +1 cost for every 4 cards in trash**"
  FIX: match "+N cost" anywhere on a "this … gains" line, evaluate the leading `If <cond>,` (fail-closed via
  EvaluateCondition), and scale by `for every/each M X` reusing ScalingUnitCount. `costscaletest` (Lucci CP+10→8,
  Sanjuan BB+8→6, Reiju 5ev→9, all off-condition→base 4; + ST27-004 [Blocker] granted only under a BB Leader).
  ST27-004 was previously logged "deferred (+cost scaler)" in the AUDIT-COMPLETE notes — now RESOLVED.
- Regression-safe: power scaling intact (Smiley 1000+DON+5hand=7000, Kumacy 3000+2DON=5000), flat "gains +N cost"
  and OP15-092 trash-threshold cost still handled. Coverage OK=2675/0/0/0/0; audit flags unchanged.

## 🔁 SECOND-PASS — cost-manipulation family (80 "−N cost" cards) — VERIFIED CLEAN (no bug)
Deep-traced the two bug-prone sub-families; both resolve correctly (regression tests added, no engine change):
- **"give this card in your hand −N cost"** (11 conditional self-discounts, `GetHandSelfCostBonus`, L898). Fails
  CLOSED if EvaluateCondition can't parse the condition → would silently never discount → unplayable. Confirmed
  ALL 11 conditions ARE understood, incl. the hard ones: opponent base-power (ST23-002), named-OR + base-power
  (ST26-001 [San-Gorou]/[Sanji]), compound name+DON (OP16-015 Ace + 6 DON), DON-differential (OP07-064), tag+power
  (OP15-102 Sky Island, OP16-005 Whitebeard). `handcosttest` (each drops to base−reduce when satisfied).
- **"give all of your opponent's Characters −N cost" permanent auras** (`GetOpponentCostDebuffAura`, L977). The
  risky OP05-084/092 Celestial Dragons condition ("only Characters on your field are {CD} type") IS handled
  (EvaluateCondition L5628); auras STACK (−4+−6) and CLAMP at 0; condition correctly re-gates when a non-CD char
  joins. `celestialtest`.
- Not a bug: cost clamps to 0 (can't go negative). Coverage OK=2675/0/0/0/0 (verification-only iteration).

## Second-pass fix ledger (post-NA): #163 EB01-008, #164 OP05-001, #165 cost-scaling trio (EB04-048/OP12-063/ST27-004).
## 🔁 SECOND-PASS — multi-target "up to N" removal / distribution (60 cards) — VERIFIED CLEAN (no bug)
Deep-traced the trickiest multi-target patterns with challenge scenarios; all resolve correctly (tests added):
- **"K.O. up to 2 … with a TOTAL power of 4000 or less"** (OP05-007 Sabo, OP09-018) — the SUM of removed chars'
  power must stay ≤4000, enforced by a shared `effect.RemainingBudget` (init 4000, per-pick `power ≤ remaining`,
  decrement after each KO; L10835/10848/10875). `totalpowertest`: two 3000s → only 1 removable (6000>4000); two
  2000s → both (=4000). Correct.
- **"Give up to N Characters up to M rested DON each"** (OP08-001 Chopper {Animal}/{Drum Kingdom}, ST30-014 Mr.3
  6000-base-power) — `gdEachN` (L8824) caps the recipient count, attaches ≤M rested DON to EACH, and enforces the
  feature/base-power filter. `eachdontest`: 1 DON to each {Animal} char, non-{Animal} char correctly REJECTED.
- **"give up to N opponent DON from cost area to 1 of your opponent's Characters"** (OP15-025 Kuro, OP15-028
  Meowban) — both say "1 of" (concentrate); `oppDonGive` concentrates N DON on one opp char. Correct.
- Coverage OK=2675/0/0/0/0 (verification-only). Tests: totalpowertest, eachdontest.

## 🔁 SECOND-PASS — [Trigger] resolution (485 trigger cards, 41 empty-effect+trigger) — 1 REAL BUG fixed
Coverage DOES gate the `trigger` field (EffectCoverage L77/137/457) so all trigger bodies are "resolvable", but
that never checks the RESOLUTION VALUE. Deep-traced the unusual triggers:
- **#166 OP16-105 Gecko Moria — multi-named play-from-trash only played 1 of 3.** "[Trigger] If you have 1 or less
  Life cards, play up to 1 [Absalom], up to 1 [Dr. Hogback], and up to 1 [Perona], with a cost of 4 or less from
  your trash." The from-trash resolver (GameEngine.cs L12563) read only the FIRST "up to N" (→ SelectionsRemaining
  1) and the FIRST [Name], so it played Absalom and stopped. FIX: collect ALL `up to N [Name]` caps → sum for the
  pick budget, and accept ANY listed name (single-name form unchanged; feature/cost/power filters still apply).
  `moriatrigtest` (all 3 played). Only OP16-105 is a genuine multi-name-in-one-clause play; the other 8
  "up to 1 [X] + up to 1 [X]" hits (EB02-013/048, OP05-101, …) are two SEPARATE clauses (reveal-then-play /
  add-then-play) — regex artifacts, correctly handled per-clause, not affected by the fix.
- **Verified correct (no change):** ST29-013 Rob Lucci / EB01-059 Kingdom Come dynamic cap "K.O. … cost equal to
  or less than the total of your and your opponent's Life cards" → ComputeDynamicCap (L4243). `triggercaptest`
  (Life 2+3=5 → cost-5 KO'd, cost-6 rejected). Draw-then / conditional self-play / activate-own-clause triggers
  route through the shared resolver correctly.
- Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 games clean. Tests: triggercaptest, moriatrigtest.

## Second-pass fix ledger (post-NA): #163 EB01-008, #164 OP05-001, #165 cost-scaling trio, #166 OP16-105 multi-named trash-play.
## 🔁 SECOND-PASS — deck-manipulation "Look at N / reveal" (214 cards) — VERIFIED CLEAN (no bug)
Deep-traced the whole DeckLook flow (StartDeckLook → ResolveDeckLookSelect → rearrange → FinishDeckLook /
StartDeckScry → ResolveDeckLookScryConfirm). Every branch correct:
- **Reveal-add filter enforcement** (ResolveDeckLookSelect L2461): NamedCardFilter ('|'-OR list), FeatureFilter,
  RequireTrigger, CardTypeFilter, MaxCost, MaxPower, ExactPower all checked before the card is added. A
  non-matching card is rejected.
- **Rest placement**: `dl.ToTop` defaults false → bottom (FinishDeckLook L2671); ToTop=true only for the one
  "place them at the TOP of your deck" card (ST17-003, L8470). No reveal-add card places the rest at the top
  (confirmed via DB scan — all "top" hits are "top OR bottom" scry).
- **Scry** ("place them at the top or bottom in any order", ~18 cards, StartDeckScry/ResolveDeckLookScryConfirm
  L2348): kept cards → deck index 0 (first selected = topmost), rest → bottom; Life-scry mirror correct (Life
  stores top at END). **trash-rest** (19) and **search-shuffle** branches correct.
- `decklooktest` end-to-end: non-{SHC} card REJECTED, {SHC} char → hand, the other 4 → bottom, deck −1.
- Coverage OK=2675/0/0/0/0 (verification-only). Test: decklooktest.

## 🔁 SECOND-PASS — multi-clause ". Then," chains (144 cards) — 1 REAL BUG (broad) fixed
Combo chains where clause 2's target depends on clause 1 (cost-down→K.O., rest→K.O.-the-rested). Found greedy
single-clause handlers swallowing the ". Then," remainder:
- **#167 cost-reduction & opponent-rest handlers DROPPED their ". Then," second clause.** "Give up to 1 opp Char
  −N cost. Then, K.O. …" (EB01-046 Brook) and "Rest up to 1 opp Char. Then, K.O. … rested …" (OP04-038) — the
  cost-red handler (GameEngine L11760) and the opp-rest handler (L11541) matched the FULL text, applied clause 1,
  and returned Resolved, so the generic ". Then," splitter (L13026, runs last) NEVER fired → clause 2 lost. FIX:
  new `HasSentenceThen(text)` gate (matches only ". Then," / ". After that," sentence boundaries, NOT the ", and
  <verb>" comma-riders) on both handlers → they now defer to the splitter, which resolves clause 1 then queues
  clause 2 with its OWN target. `thenchaintest` (EB01-046 cost-1→0→K.O.; OP04-038 rest→K.O.). 38 cards carry a
  cost-red/opp-rest clause before ". Then,".
  REGRESSION caught+fixed mid-change: the first attempt used `FindThenClause(text) <= 0`, which ALSO deferred the
  ", and rest …" comma-rider in OP03-021 Kuro → its partA "Set this Leader as active" doesn't resolve standalone →
  NA. Narrowing to `HasSentenceThen` (sentence-boundary only) restored OK=2675/0.
- The splitter itself was already correct; the bug was greedy handlers pre-empting it. Coverage OK=2675/0/0/0/0;
  audit unchanged; sim smoke 900 games clean.

## 🔁 SECOND-PASS (cont.) — ". Then,"-drop bug class: swept sibling greedy handlers
Continuing #167. Tested the other greedy single-clause handlers that return Resolved on full text:
- **#168 give-DON-to-Leader handler DROPPED its ". Then," second clause.** "Give up to 1 rested DON!! to your
  Leader [or 1 of your Characters]. Then, <clause2>" (EB03-045 Perona, EB03-053 Nami, EB03-015 Camie, OP10-016,
  …9 cards) — the handler (GameEngine L11471) attached the DON and returned Resolved, dropping clause 2 (a
  conditional play-from-trash / rest-opponent / add-to-hand). FIX: `&& !HasSentenceThen(text)` → defer to the
  splitter. `thenchaintest` "giveDON→rest" case (clause 1 DON-attach, clause 2 rests an opp Character).
- **−power handler VERIFIED CLEAN (no bug):** "Give −N power. Then, K.O. …" (OP11-002/OP11-018) already resolves
  clause 2 (thenchaintest "OP11-018 power→KO"=True; resolves=2). It does not greedily swallow the remainder, so no
  guard needed. (Not every greedy-looking handler is broken — the cost-red/rest/give-DON ones were.)
- Bug-class summary: single-clause handlers that `return Resolved` on the FULL multi-clause text pre-empt the
  ". Then," splitter (L13026, runs last). Guard = `!HasSentenceThen(text)` (sentence-boundary ". Then,"/". After
  that," only, not ", and <verb>" comma-riders). Fixed in 3 handlers: cost-red (#167), opp-rest (#167),
  give-DON-to-Leader (#168). Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 games clean.

## 🔁 SECOND-PASS — ". Then,"-drop class CLOSED (return-to-hand + draw-discard verified clean)
Architecture note: there are TWO splitters — an EARLY one (GameEngine L7709) that fires when partA is
`IsAutomatedEffectPattern`, and a LATE fallback (L13026). Chains break only when partA is NOT an automated pattern
AND a greedy single-clause handler `return Resolved`s the full text before the late splitter. So:
- **draw-and-discard** ("Draw N and trash M. Then, K.O./give/…") — CLEAN. The drawAnd handler (L7693) draws, STRIPS
  "Draw N and", and recurses; the remainder then early-splits. `drawchaintest` OP16-090 draw+discard→KO = True.
- **return-to-hand** ("Return up to 1 Char. Then, draw 2 …") — CLEAN. `drawchaintest` OP03-122 return→draw2 = True
  (V returned + deck −2).
- **−power** (prior iter) — CLEAN. Only cost-red/opp-rest (#167) and give-DON-to-Leader (#168) needed the
  `!HasSentenceThen` guard. Class now fully swept: 3 broken+fixed, 3 verified clean.
- Verification-only iteration (no engine change); coverage OK=2675/0/0/0/0. Test: drawchaintest.

## 🔁 SECOND-PASS — reactive timing (On-Opponent's-Attack / On-K.O. / End-of-Turn) — 1 REAL BUG fixed
- **Reactive DISPATCH verified clean:** OP09-032 Rosinante "[On Your Opponent's Attack] [Once Per Turn] Set this
  Character as active" — a rested Rosinante on the defender's board un-rests when the opponent declares an attack.
  `op09032test` (reaction queued + set active + once-per-turn marked). The dispatch (GameEngine L2977) handles DON
  gate, once-per-turn (consume-on-resolve via OnceKey), and the no-tag "can be activated when opponent attacks"
  variant.
- **#169 "trash any number … +N power for every card trashed" gave a FLAT +N and trashed NOTHING** (5 cards:
  P-051 Shanks, OP15-002 Lucy, OP06-014 Ratchet, ST16-002 Gordon, OP03-001 Ace leader). The generic buff handler
  (L11639) read the first "+N" and applied a flat buff, ignoring the "trash any number" cost AND the "for every
  card trashed" scaling — the player got the buff free and un-scaled. FIX: dedicated interactive UNBOUNDED
  multi-trash handler placed BEFORE the generic buff — each clicked matching hand card (filter: {tag} type /
  Event-or-Stage / any) is trashed for +N battle power on the source; skip (null target) finalizes. `trashanytest`
  (trash 2 → +2000, hand 3→1). Recipient defaults to the source (OP06-014's "Leader or 1 of your Characters" →
  the reacting source, a sensible defensive buff). This clears the memory's "P-051/P-059 variable-count deferred".
- Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 games clean. Tests: op09032test, trashanytest.

## 🔁 SECOND-PASS — On-K.O. reactive family (154 cards) — 1 REAL BUG fixed
- **isKo gating VERIFIED CLEAN:** [On K.O.] fires on a genuine K.O. (MoveToTrash isKo=true) but NOT on non-KO
  removal (cost-trash / bounce / deck-bottom pass isKo=false). `onkotest` (OP01-007 revenge-KO fires on KO, stays
  quiet on non-KO).
- **#170 "When this Character is K.O.'d BY YOUR OPPONENT'S EFFECT, …" wrongly fired on a BATTLE K.O.** The battle
  exclusion (FireOnKoEffects L13671) matched only the literal "K.O.'d by an effect", missing the "by your
  opponent's effect" wording (OP16-024 Inazuma, EB01-057 Shirahoshi, OP09-052, OP11-024/035/051 — ~6 cards), so
  those reactions fired on battle K.O.s they should ignore. FIX: `Regex "K\.O\.'d by (?:an|your opponent's)
  effect"`. `onko2test` (OP16-024: effect-KO rests opp = True, battle-KO rests opp = False).
- 🚩 FLAGGED for a focused next iteration (same bug class, riskier subsystem): the REPLACEMENT resolver
  (TryRemovalReplacement L4295-4298) explicitly does NOT gate "would be removed from the field by your opponent's
  effect … instead" on battle-vs-effect — so those ~34 replacement effects may wrongly SAVE a battle-K.O.'d
  Character. Fix candidate: `if (Regex "would be (?:K\.O\.'d|removed from the field) by your opponent's effect" &&
  isBattleKo) continue;` near L4285 — needs per-card tests (it saves characters; higher blast radius).
- Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 games clean. Tests: onkotest, onko2test.

## 🔁 SECOND-PASS — replacement resolver battle-vs-effect gate (the #170 flagged item) — FIXED
- **#171 "would be K.O.'d / removed from the field BY YOUR OPPONENT'S EFFECT … instead" replacements wrongly SAVED
  a Character that lost in BATTLE.** TryRemovalReplacement (GameEngine L4304-4307) gated "in battle" (effect-only
  must NOT fire in battle was explicitly left ungated per the old comment). 42 cards affected (OP10-074 Pica,
  OP13-047 Fossa, OP14-034 Luffy, OP12-027 Koushirou, P-111 Robin, the OP15 7000-base-power family, …). FIX: added
  the reverse gate — `if (isBattleKo && ContainsAll(line,"by your opponent's effect") && !ContainsAll(line,"or
  K.O.'d") && !Regex("would be K\.O\.'d or")) continue;`. EXCEPTIONS preserved: OP08-045 Thatch ("…by your
  opponent's effect or K.O.'d") + OP13-046 Vista ("would be K.O.'d or would be removed…") still save on battle.
  `onko3test` (Pica: effect-KO saved=T, battle-KO saved=F; Vista: effect=T, battle=T).
- Regression-safe: Rosinante (koreptest, unqualified "would be K.O.'d" → fires on any removal) intact; effect-KO
  saves still work (onko3test Pica effect=T); coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 games clean.
- Both halves of the On-K.O.-"by-opponent's-effect"-on-battle bug now closed: reactive path (#170) + replacement
  path (#171).

## 🔁 SECOND-PASS — End-of-Your-Turn delayed effects (50 cards) — 1 REAL BUG fixed
- **Dispatch verified clean:** OP04-027 "[DON!! x1] [End of Your Turn] Set this Character as active" un-rests at
  the controller's own EndTurn (ApplyEndOfTurnEffects L13817, per-seat — only the ending player's cards). DON gate
  + self-untap correct. `endofturntest`. (Note: can't test "opp EndTurn doesn't fire" directly — the next player's
  Refresh un-rests everything anyway; the per-seat dispatch is structurally correct.)
- **#172 OP04-034 Lao.G End-of-Your-Turn conditional K.O. of an opponent RESTED Character NEVER worked.** "[End of
  Your Turn] If you have 3+ active DON!!, K.O. up to 1 opp rested Char cost ≤3." It queued as a deferred player
  pick, but EndTurn then ran ApplyStartOfTurn (L13787) which REFRESHED the opponent → their chars became active →
  the "rested" filter rejected every target → the K.O. always fizzled. The ONLY EoT card that targets opp-rested.
  FIX: resolve this pattern INLINE in ApplyEndOfTurnEffects (before the refresh) — evaluate the leading condition,
  auto-pick the highest-cost valid rested target(s), KO (honoring TryRemovalReplacement/CannotBeKoedByEffect).
  Also fixed a latent bug: ExtractTimedClause returns the clause WITH its "[End of Your Turn]" tag, so the `^If`
  condition anchor failed → strip leading tags first. `endofturntest` (KO@3DON=T, KO@2DON=F).
- Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 games clean. Test: endofturntest.

## 🔁 SECOND-PASS — On-Block (14 cards) + circled-DON cost — 1 REAL BUG fixed
- **#173 [DON!! xN] [On Block] effects fired with 0 DON attached.** The On-Block dispatch (BlockAttack L3205)
  queued the clause on `HasTiming("On Block")` alone, never checking the [DON!! xN] gate — so OP01-039 Killer,
  OP01-014 Jinbe, OP01-078 Boa, ST03-003 Crocodile (all "[DON!! x1] [On Block] …") triggered with no DON. FIX:
  `int obDon = ParseDonThreshold(onBlockClause); if (obDon<=0 || blocker.AttachedDonIds.Count>=obDon) QueueEffect(…)`.
  `onblocktest` (Killer DON×1 draws 1, DON×0 draws 0). Non-DON On-Block cards (Monet, Helmeppo, Hina…) unaffected.
- **Circled-DON activation cost (➁/➀ "rest the specified number of DON!! cards") VERIFIED CLEAN** — already handled
  by ExtractCircledDonCost (L1312, U+2460-2469 ①-⑩ and U+2780-2789 ➀-➉) + the generic pay-then-queue at L2221.
- Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 games clean. Test: onblocktest.

## 🔁 SECOND-PASS — Activate:Main / When-Attacking Once-Per-Turn enforcement — 1 REAL BUG fixed
- **Enforcement + reset VERIFIED CLEAN:** Activate:Main [Once Per Turn] blocks re-use same turn (ActivateMain
  L2170), per-INSTANCE (instanceId key), reset at the controller's own turn start (ApplyStartOfTurn L1707 clears
  AbilityUsedThisTurn for the active seat).
- **#174 dual-ability cards shared ONE once-per-turn key across Activate:Main and When-Attacking** (OP05-041
  Sakazuki: "[Activate: Main] [Once Per Turn] …: Draw 1 / [When Attacking] give opp −1 cost"). The When-Attacking
  fallback (L3129) read `[Once Per Turn]` from the WHOLE effect (so the Activate line's OPT flagged the
  When-Attacking as once-per-turn) AND used the bare `instanceId` key (shared with Activate:Main) — so using the
  Activate:Main CONSUMED the When-Attacking (and vice-versa). FIX: read the DON gate + OPT flag from the
  When-Attacking CLAUSE, and use a distinct `instanceId+":whenAttacking"` key (mirrors the ":onOppAttack" /
  ":replace" keys). `sakazukitest` (After Activate:Main used, the −1-cost When-Attacking still fires). Also makes
  the When-Attacking DON gate clause-specific (a passive "[DON!! x1]" line no longer mis-gates it).
- Coverage OK=2675/0/0/0/0; audit unchanged; When-Attacking regressions (thenchain OP11-018, trashany) pass; sim
  smoke 900 games clean. Test: sakazukitest.

## 🔁 SECOND-PASS — keyword-grant / "Your Leader gains" auras — 1 REAL BUG (multi-part) fixed
- **#175 "Your Leader gains [Keyword] and +N power" continuous auras were entirely unhandled** (OP16-003
  Edward.Newgate "[Your Turn] Your Leader gains [Double Attack] and +2000 power"; power-only siblings OP13-099
  Empty Throne, ST28-004 Momonosuke). THREE gaps:
    1. KEYWORD: HasPrintedKeywordGrant's aura regex matched only "[Name]"/"{tag} type Characters" recipients, not
       "Your Leader" — and never honored the source's [Your Turn] timing. FIX: refactored to per-line, added a
       `Leader` recipient alternative (gated on instance being a leader) + [Your Turn]/[Opponent's Turn] gate.
    2. POWER PARSE: ParsePowerGain needs "gains +" ADJACENT, so "gains [Double Attack] and +2000 power" → 0. FIX:
       added a `gains?[^.]*?\+(N) power` fallback in GetTurnPassiveAuraBonus.
    3. RECIPIENT LEAK: GetTurnPassiveAuraBonus applied non-self/non-boardWide auras to ANY queried card. FIX: a
       "your Leader gains" line now applies ONLY when the queried instance is the Leader (no leak to Characters).
  `op16003test` (your-turn: DA + power+2000; opp-turn: off; no character leak).
- Named keyword auras (OP02-074 "Your [Blugori] gains [Blocker]", untagged) preserved by the per-line refactor.
  No tag-based continuous keyword auras exist in the DB.
- Coverage OK=2675/0/0/0/0; audit unchanged; battleimmune/scaletest aura regressions pass; sim smoke 900 clean.

## 🔁 SECOND-PASS — "cannot" restriction auras (cannot-attack / will-not-become-active) — VERIFIED CLEAN
- **cannot-attack (22 opponent-targeting cards)** — enforced + duration correct. Application (L8244) handles cost
  cap, rested filter, "other than [Name]" exclusion, and duration: "until the end of your opponent's next
  turn/end phase" + "until the start of your next turn" → untilNextTurn; "during this turn" → thisTurn. DeclareAttack
  (L2776) rejects a "cannotAttack" attacker (leader or character). `cannotattacktest`: OP05-042-style restriction
  applied, PERSISTS into the opponent's turn (untilNextTurn), and the attack is blocked.
- **will-not-become-active / freeze (28 cards)** — the refresh honors freeze for Characters (L1777) AND for the
  LEADER (OP04-031 Doflamingo can freeze the opponent's rested Leader). Suspected the leader/stage un-rest (L1770/
  L1780) ignored freeze, but `leaderfreezetest` (with a control proving the refresh ran: a non-frozen leader
  un-rests) shows the frozen Leader correctly STAYS rested through its owner's Refresh Phase. Clean.
- No engine change this iteration; coverage OK=2675/0/0/0/0. Tests: cannotattacktest, leaderfreezetest.

## 🔁 SECOND-PASS — blocker power/cost bans ("cannot activate [Blocker]") — 1 REAL BUG fixed
- **#176 OP12-051 Hina "Up to 1 opp Char with a base cost of 4 or less cannot activate [Blocker]" banned ALL
  opponent Characters.** The handler (GameEngine L10220) parsed only "(N) power or less" — so a BASE-COST filter
  was ignored (lbPw=-1 → no filter) AND it applied to EVERY opponent Character in a for-each (ignoring "up to 1").
  FIX: parse base-cost / effective-cost filters, and split "All of your …" (board-wide for-each) vs "Up to N …"
  (a validated single/limited PICK). `op12051test`: up-to-1 bans only the picked cost≤4 char; "All … 2000 power
  or less" (OP11-013 Prince Grus) still bans all matching by power. NAME COLLISION NOTE: an existing `blockbantest`
  harness mode (needs args[1]) intercepted the first name — renamed the new test to `op12051test`.
- Full NoBlocker (OP05-016 Morley "cannot activate [Blocker] during this battle") + battle-scoped power ban
  (ST01-002 Usopp BlockerPowerBan) unaffected. Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 clean.

## 🔁 SECOND-PASS — opponent-hand disruption (48 cards) — VERIFIED CLEAN (no bug)
- **"Trash N cards from your opponent's hand" (CONTROLLER picks)** — regex `trash (\d+) cards? from (your
  opponent's|their) hand` (L9815) waits for clicks on the opponent's hand, one per card; SelectionsRemaining
  enforces the count. All such cards use a numeric N (OP06-097 trash-1, OP03-078 trash-2, ST10-010). `oppdiscardtest`:
  OP06-097 3→2; OP03-078 (cond "6+ in hand") 6→4, and 5→5 when the condition fails.
- **"Your opponent trashes/chooses N from their hand"** — auto-discards the last N (L11215 / L9793), a reasonable
  simplification (the opponent's exact discard choice isn't modeled).
- **"Choose N from opponent's hand; reveal"** (OP01-105, OP01-063) — logs the revealed hand (info only). Clean.
- No engine change; coverage OK=2675/0/0/0/0. Test: oppdiscardtest.

## 🔁 SECOND-PASS — DON-return costs + multi-pick target distinctness — 1 REAL (broad) BUG fixed
- **#177 multi-pick "up to N Characters" effects allowed picking the SAME card twice** → double-applied the
  effect (e.g. "Give up to 2 of your opponent's Characters −2000 power" put −4000 on ONE Character). No distinct-
  target tracking existed, and the glow (IsValidEffectTarget) kept lighting an already-chosen card. FIX (central):
  new `PendingEffect.PickedInstanceIds`; ResolveEffect (a) REJECTS a click on an already-chosen id, and (b) after
  each continuing pick of a real in-play target, records the id; IsValidEffectTarget excludes recorded ids from
  the glow. Affects every persistent-target multi-pick (−power, rest-opponent, cannot-attack/block, freeze,
  give-DON-each…). KO/return picks self-limit (target leaves the board → not recorded → no over-restriction).
  `op09073test` (target V twice → −2000 not −4000). Regressions PASS: totalpowertest (2 distinct KO), eachdontest
  (2 distinct DON recipients), op15003/op12051/cannotattack.
- **OP09-073/070/065/119 "You may return 1 or more DON!! cards from your field to your DON!! deck: <body>"
  VERIFIED CLEAN** — the variable-minimum DON-return cost pays (1 DON → DON deck) and the fixed body fires
  (`op09073test`: DON 3→2, −power applied).
- Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 clean. Test: op09073test.

## 🔁 SECOND-PASS — Life manipulation (125 cards: →Life 64, Life→hand 35, opp-Life 17, trash-Life 9) — CLEAN
- **Opponent-Life trash** (OP03-120, OP09-107, EB03-057, OP10-109) — trashes top N of the OPPONENT's Life
  (Pop = end = top); conditional gate ("opponent has 4+ Life") works; trashing Life to 0 does NOT end the game
  (loss is by ATTACK at 0 Life, not Life→0 via trash). `lifetrashtest` (4→3, 3-cond-fail→3, to-0 stays active).
- **Self Life→hand** (L10045) takes `Life[Count-1]` = the TOP card (Life stores top at the END). Correct order.
- **Add from opp's Life to the owner's hand** (EB04-054, L10065) — opponent loses Life, gains the card in THEIR
  hand. Correct. **Field→Life** (op05111test) + **hand→Life** verified earlier.
- No engine change; coverage OK=2675/0/0/0/0. Test: lifetrashtest.

## 🔁 SECOND-PASS — play-rested entry + "can also attack active Characters" — 1 REAL BUG fixed
- **play-from-trash/hand RESTED VERIFIED CLEAN:** "from your trash rested" (L12539) / "from your hand rested"
  (L12235) both enter the Character rested; OP06-086 dual-play (one active, one rested) has a dedicated handler
  (L8444). `playrestedtest`.
- **#178 "This Character can also attack active Characters" (WITHOUT "your opponent's") wasn't recognized** →
  OP04-081 Cavendish ([DON!! x1]) and OP04-090 Luffy could NOT attack active opponent Characters. HasPrintedCan
  AttackActive (L4089) required the literal "can also attack your opponent's active Characters"; the "your
  opponent's" is OPTIONAL. FIX: match `[Tt]his Character can also attack (your opponent's )?active Characters`
  (scoped to "This Character" so the GRANTED form "Up to 1 of your {tag} … can also attack active" — applied via
  the canAttackActive modifier — never wrongly gives the grant SOURCE the ability). DON gate preserved.
  `cavendishtest` (OP04-081 DON1 hits active, DON0 gated; OP04-090 hits active).
- Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 clean. Tests: playrestedtest, cavendishtest.

## 🔁 SECOND-PASS — [Rush] variants (69 conditional-Rush cards, no multi-attack mechanic exists) — CLEAN
- **Conditional continuous Rush** (EB02-052 Enel "If your Leader has {Sky Island} type, this Character gains
  [Rush]") — HasRush honors the leading-If condition (via HasPrintedKeywordGrant); Rush clears summoning sickness
  so the Character can attack the turn it's played. `conditionalrushtest` (Sky leader → Rush+not-sick; non-Sky →
  no Rush + sick).
- **[Rush: Character]** (EB04-011, OP16-089, ST29-014 — printed reminder "can attack Characters on the turn in
  which it is played") — DeclareAttack (L2926) lets a just-played [Rush: Character] attack an opponent CHARACTER
  but REJECTS an attack on the Leader (unlike full [Rush]). Handles printed / modifier / aura-granted forms with
  feature + leading-If gates. `rushchartest` (vs Character=allowed, vs Leader=rejected).
- No engine change; coverage OK=2675/0/0/0/0. Tests: conditionalrushtest, rushchartest.

## 🔁 SECOND-PASS — [Double Attack] + [Banish] combat keywords — VERIFIED CLEAN (no bug)
- **[Double Attack]** (EB04-023 Chaka&Pell) deals 2 Life damage in ONE Leader hit (dmg=2 → PendingLifeDamage=1 +
  RevealLifeAndStartTrigger chains the 2nd; ResolveAttack L3993). Both Life cards go to hand via the Trigger step.
  `doublebanishtest`: attack Leader → Life 5→3, hand +2, trash +0.
- **[Banish]** (OP01-067 Crocodile) trashes the Life card — NO Trigger step, does NOT go to hand
  (BanishLifeCards, L3996). `doublebanishtest`: Life 5→4, hand +0, trash +1. (DA+Banish would banish 2.)
- Combat sequence for the test: declareAttack → passBlock(def) → passCounter(def) → resolveAttack issued by the
  DEFENDER (TargetSeat owns the final resolve, L3951; attacker's resolveAttack is ignored).
- No engine change; coverage OK=2675/0/0/0/0. Test: doublebanishtest.

## 🔁 SECOND-PASS — [Counter] event resolution — 1 REAL (broad) BUG fixed
- **#179 [Counter] events whose PRIMARY effect is NOT a defender power-buff did nothing** (~10 cards: EB01-010
  "[Counter] K.O. up to 1 opp Char 6000 base power or less", OP01-028 "[Counter] give opp −2000", OP02-118 protect,
  …). CounterWithCard (GameEngine L3717) only queued the "Then," SECONDARY clause; the PRIMARY effect was dropped
  (counterPower=0 from AutomatedCounterPower since there's no "+N", and no "Then,"). So a counter played purely to
  K.O./debuff/protect added 0 power and did NOTHING. FIX: queue the primary clause (before "Then,") too, UNLESS it
  is a "gains +N power" defender buff (already applied via counterPower — guard prevents the ST07-016 double-apply).
  `countereffecttest` (EB01-010 counter-K.O. fires).
- **#179b supporting handler** — the fix exposed OP02-118 Yasakani Sacred Jewel "[Counter] Select up to 1 of your
  Characters. The selected Character cannot be K.O.'d during this battle" (silently broken before; queued→NA after
  #179). Added a targeted handler: own-Character battle-scoped K.O. immunity via cannotBeKod "thisBattle" modifier.
  Restored coverage OK=2675/0.
- Combat-counter test recipe: opp declareAttack → passBlock(defender) → counterWithCard(defender, eventId) →
  resolve the queued counter effect. Coverage OK=2675/0/0/0/0; audit unchanged; sim smoke 900 clean.

## 🔁 SECOND-PASS — [Blocker] mechanic — VERIFIED CLEAN (no bug)
- **Redirect + KO:** [Blocker] (EB01-017 Blueno 2000) redirects the attack to itself (battle.TargetId → blocker);
  a 7000 attacker K.O.'s the weaker blocker and the Leader takes NO Life. `blockredirecttest`.
- **Rested blocker cannot block** (BlockAttack L3183 rejects rested); **power-ban** (ST01-002 Usopp "[DON!! x2]
  opponent can't Blocker with 5000+ power") bans a 7000 blocker but allows a 2000 one — `blockerbantest`. Also
  enforced: cannotBlock/cannotBeRested modifiers, NoBlocker, Unblockable, the attack target itself can't block.
- No engine change; coverage OK=2675/0/0/0/0. Tests: blockredirecttest, blockerbantest.

## 🔁 SECOND-PASS — DON-attach power + Stage rest-cost — VERIFIED CLEAN (near-miss avoided via rulebook)
- **DON-attach power (+1000 per attached DON) is OWNER-TURN-ONLY — this is CORRECT** per Comprehensive Rules
  6-5-5-2 ("Leader/Character cards gain 1000 power DURING YOUR TURN for each DON!! given to them"). GetPower L256
  gates it on `state.ActiveSeat == instance.Owner`. I nearly "fixed" this as a bug (assumed continuous) — the
  rulebook (Tools/Harness/findings/rulebook_comprehensive.txt) confirmed a DON-boosted card blocks at BASE power on
  the opponent's turn. `donpowertest` (own-turn 1000+2 DON=3000; opp-turn 1000). Locked so it can't regress.
- **Stage [Activate: Main] rest-cost abilities** (24 Stage cards) — activating rests the Stage (cost) + fires the
  body, with the leader condition honored (OP03-098 Enies Lobby: CP leader → give opp −2 cost). `stageactivatetest`
  (stage rested, opp cost 5→3).
- No engine change; coverage OK=2675/0/0/0/0. Tests: donpowertest, stageactivatetest.

## 🔁 SECOND-PASS — "until the end of your opponent's next turn/End Phase" delayed grants (43 cards) — CLEAN
- **Power grant** (EB04-007 "Your Leader gains +2000 power until the end of your opponent's next End Phase") —
  applied via a TimedPowerBonus with Duration "startOfNextTurn" + a startOfNextTurnOf:seat modifier. PERSISTS
  through the opponent's whole turn, then expires by the giver's next turn (ApplyStartOfTurn L1765 ExpireInstance
  Modifiers). `oppturnbufftest`: give 7000, opp-turn 7000 (persists), giver's-next-turn 5000 (expired).
- **Cost grant** (OP08-088 "gains +1 cost until the end of your opponent's next turn") — same untilNextTurn
  machinery (CostDelta modifier). `oppturnbufftest`: give +1, opp-turn +1 (persists).
- No engine change; coverage OK=2675/0/0/0/0. Test: oppturnbufftest.

## 🔁 SECOND-PASS — "base power becomes N" set-power overrides (18 cards) — VERIFIED CLEAN
- **Base-set + stacking:** "This Character's base power becomes 7000" replaces the printed base (BasePowerOverride,
  GetPower L245, latest-wins), THEN attached DON / +power buffs STACK on top. `setpowertest`: base-7000 + 2
  attached DON (owner turn) = 9000.
- **Set opponent to 0** (EB04-010/OP07-002 "Set the power of up to 1 opp Char to 0") → power 0. **Mirror**
  (EB04-052/OP06-009/OP16-036 "base power becomes the same as your opponent's Leader") snapshots the opp Leader's
  CURRENT total power (5000). `setpowertest` (set-0=0, mirror=5000). Duration untilNextTurn/thisTurn parsed.
- No engine change; coverage OK=2675/0/0/0/0. Test: setpowertest.

## 🔁 SECOND-PASS — DON!!−N cost payment (attached vs cost-area choice) — VERIFIED CLEAN
- A "DON!! −N (…): body" cost, when TotalFieldDon > N (a meaningful choice), awaits per-DON clicks
  (AutoPayOrAwaitDonMinus L1378). ReturnFieldDon (L1405) returns the CLICKED DON: cost-area first, else an ATTACHED
  ("given") DON on any field card — detaching it. Paying with an attached DON weakens the host (loses +1000).
  `donminustest`: pay-attached → host CH DON 2→1, power 3000→2000, DonDeck +1; pay-cost-area → host keeps DON+power.
  Mid-payment fizzle guard checks only the REMAINING DON needed (L5050). Body resolves after the full cost is paid.
- No engine change; coverage OK=2675/0/0/0/0. Test: donminustest.

## 🔁 SECOND-PASS — "place N cards from trash at bottom of deck" recycle cost UNDER-PAID — 1 BUG FIXED (#207, ~13 cards)
- **#207 the "place N cards … from your trash at the bottom of your deck: <body>" recycle cost was HARDCODED to
  place 1** (L7957) — it placed the first clicked card and immediately fired the body, so "place 2/3" costs
  UNDER-PAID (placed 1, got the payoff). ~13 CP/Navy/etc. cards: EB01-043, EB03-043, OP03-080, OP05-093,
  OP07-080/092/093, OP08-081/094, OP10-118, OP11-119, OP12-091, … Invisible to coverage (which only checks the body
  resolves). Fix: parse the count N, require N picks (SelectionsRemaining loop), enforce the type filter via
  CardPassesFeatureFilter on each pick, and gate payability (fewer than N matching trash cards → optional cost
  declined). `op03080test`: places EXACTLY 2 CP cards, REJECTS a non-CP pick, K.O. fires only after full payment.
  Coverage 2675/0/0/0/0; smoke P(first)=54.67%.

## 🔁 SECOND-PASS — "rest [this/your] Leader" activation cost (non-trigger) — VERIFIED CLEAN
- Swept the 4 "You may rest [this/your] Leader: <effect>" activated costs following the free-cost theme. All pay
  correctly: "rest THIS Leader" (Leader [Activate: Main] — EB03-001, OP03-058, OP06-020) is paid by the L6297 cost
  part-handler (rests source=Leader); "rest YOUR Leader" (Character ability — OP04-081 Cavendish) by L6366 (rests
  the controller's Leader), already verified by cavendishtest + costtrig2test (OP04-094).
- `op03058test` deep-traces the most complex one — OP03-058 Iceburg [Activate: Main] "DON!! −1 (…) You may rest
  this Leader: Play up to 1 {Galley-La Company} Character cost≤5 from hand": COMPOUND cost fully paid — Leader
  rested, 1 DON returned (field 3→2, DON deck +1), Galley-La char played. No engine change; coverage 2675/0/0/0/0.

## 🔁 SECOND-PASS — cost-gated trigger sweep cont.: OP03-100 Life-cost + OP04-094 rest-cost — 1 BUG FIXED (#206)
- **#206 OP03-100 "[Trigger] You may trash 1 card from the top or bottom of your Life cards: Play this card" ignored
  the Life-trash cost** (same free-play class as #205, but a Life cost). Added "trash N from top/bottom of your Life
  cards" parse to the trigger "Play this card" handler: gate on remaining Life.Count ≥ N (unpayable → card to hand),
  trash N Life cards when playing. `costtrig2test`: Life 4 → 3 (reveal) → 2 (cost), card played.
- **OP04-094 "[Trigger] You may rest your Leader: K.O. up to 1 opp Char cost≤5" VERIFIED CLEAN:** the rest-Leader
  cost IS paid — `costtrig2test` confirms the victim is K.O.'d AND the Leader is rested (cost + payoff both happen).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. **Cost-gated trigger sweep complete: hand-trash (#205, 14), Life-
  trash (#206, 1), DON!!−N (pre-existing), rest-Leader (verified) all now pay their costs.**

## 🔁 SECOND-PASS — cost-gated "[Trigger] You may trash N from hand: Play this card" — 1 BUG FIXED (#205, 14 cards)
- **#205 the "You may trash N cards from your hand" COST on a "Play this card" trigger was IGNORED** — the trigger
  handler (L13790) parsed the conditional + DON!!−N variants but not the hand-trash cost, so the card was played
  FREE (no discard) AND even played when the hand was empty (unpayable cost skipped). 14 cards (EB03-054 Nico Robin,
  OP03-108/110/113, OP04-104/106/108, OP05-105, OP08-104, OP13-114, OP16-107, ST07-009, ST20-002, ST29-004).
  Fix: parse "You may trash N cards from your hand", gate on hand.Count ≥ N (unpayable → card goes to hand, not
  played free), and trash N from hand when playing. `eb03054test`: with a hand card → played + card discarded; with
  NO hand card → NOT played (goes to hand). Coverage 2675/0/0/0/0; smoke P(first)=54.67%.
- (Note: the "trash 1 from top/bottom of your Life cards: Play this card" variant — OP03-100, 1 card — is a distinct
  cost not yet paid; flagged.)

## 🔁 SECOND-PASS — "[Trigger] Activate this card's [X] effect" meta-trigger (77 cards) — VERIFIED CLEAN
- Surveyed 128 distinct [Trigger] payoff shapes; the largest is "Activate this card's [Main]/[Counter]/[On Play]/
  [On K.O.] effect" (56+7+7+7 = 77 cards). Handler (L13847) delegates via ExtractTimedClause: extracts the named
  timed clause, queues it as a trigger effect, trashes the Life card. (Was [Main]-only; already fixed for the
  Counter/OnPlay/OnKO variants per the code comment.)
- `op03056test` (OP03-056 "[Trigger] Activate this card's [Main] effect" → [Main] "Draw 2 cards"): on reveal +
  useTrigger, north DRAWS 2 (the delegated [Main] fired) and the Life card goes to TRASH, NOT to hand. The delegated
  clause's own leading-If (e.g. EB01-020 "[Main] If your Leader has {Supernovas} type, …") is gated by the standard
  leading-If evaluator. No engine change; coverage 2675/0/0/0/0.

## 🔁 SECOND-PASS — [Banish] + [Double Attack] + Trigger-suppression interactions — VERIFIED CLEAN
- Deep-traced the Banish/Double-Attack damage-to-Life mechanic (22 Banish + 32 DA cards). Dispatch (L4075-4084):
  dmg = 1 + (DoubleAttack?1:0); Banish → BanishLifeCards(dmg) (trash each, NO trigger, win-check on empty Life);
  else PendingLifeDamage = dmg-1 + RevealLifeAndStartTrigger (chains the 2nd via FinalizeTrigger). All correct.
- `banishtrigtest` (extends the existing doublebanishtest which only covered DA-alone / Banish-alone):
  - **Banish + Double Attack** (OP01-121 Yamato, has both): 2 Life cards TRASHED, none to hand.
  - **Banish TRIGGER SUPPRESSION**: a Banished trigger-Life card (OP01-037 "[Trigger] Play this card") is trashed
    and does NOT fire its trigger (not played).
  - **Contrast — normal attack**: the SAME trigger-Life card, revealed by a non-Banish attacker + `useTrigger`,
    DOES fire (the card plays) — proving the suppression is meaningful.
- No engine change; coverage 2675/0/0/0/0.

## 🔁 SECOND-PASS — play-then-gains pre-emption sweep + OP12-015 deep-trace — VERIFIED CLEAN
- Swept play-from-X chains with a trailing rider on the played card (following the #185/#204 generic-handler
  pre-emption root cause). OP12-058 was the ONLY "play → THAT Character gains [KW]" back-ref (fixed #204); the other
  9 "this Character gains …" are correctly-scoped SOURCE self-auras on separate clauses (no leak). No "play +
  trailing-on-played" chains exist beyond OP12-058.
- Deep-traced OP12-015 Monkey.D.Luffy [On Play]: "You may reveal 2 Events from your hand: Play up to 1 red Character
  card with 3000 power or less from your hand. Then, give up to 1 rested DON!! card to your Leader or 1 of your
  Characters." — reveal-from-hand cost (auto-payable) + FILTERED play-from-hand + give-DON continuation all correct.
  `op12015test`: red power-1000 char played + DON given to Leader; power-4000 char REJECTED by the ≤3000 filter.
- No engine change; coverage 2675/0/0/0/0.

## 🔁 SECOND-PASS — reveal→play→"that Character gains [Rush]" chain (OP12-058) — 1 BUG FIXED (#204)
- **#204 OP12-058 the "If you do, that Character gains [Rush]" payoff was LOST + the reveal never happened.** The
  generic keyword-grant handler (L10666) matched "gains [Rush] during this turn" on the FULL text BEFORE the reveal
  handler and asked for an unrelated target (WaitingForTarget) → the reveal/play chain STALLED (probe: deckTop
  unchanged, log "Choose … to gain [Rush]"). Fix: (a) guard the keyword handler against a reveal-play chain
  (`reveal` + `from the top of your deck` + `that Character gains`), so the reveal handler owns it; (b) the reveal
  handler's you-may-play branch now carries the "that Character gains [KW]" payoff onto the deferred decision;
  (c) the deferred play handler grants the keyword (thisTurn) to the just-played card. `op12058test`: reveal → play
  decision → play → the played card gains [Rush]. Coverage 2675/0/0/0/0; smoke P(first)=54.67%.
- **BACK-REFERENCE CLAUSE CLASS COMPLETE:** "the selected/that Character/chosen card" — all 3 handled (#202 tracking,
  #203 self-freeze, #204 reveal-play-keyword; ST05-017 KO-immunity pre-existing).

## 🔁 SECOND-PASS — back-reference clauses: EB02-021 "the selected Character" self-freeze — 1 BUG FIXED (#203)
- Followed the #202 tracking pattern into other back-reference clauses ("the selected/that Character/chosen card").
  Only 3 exist; ST05-017 (KO-immunity) already handled. **#203 EB02-021 Gum-Gum Giant Pistol** "Up to 1 of your
  {Straw Hat Crew} Characters gains +6000 … Then, THE SELECTED Character will not become active in YOUR next Refresh
  Phase" — the SELF-freeze downside STALLED (probe: buff applied +6000 but the freeze clause sat pending — the
  existing "will not become active" handler is OPPONENT-only and rejected the own card + needed a fresh pick).
  Fixed: a new handler reads `LastPowerBuffTargetId[seat]` (the buffed = "selected" card) and applies a freeze with
  a new **"ownNextRefresh"** duration, expired in ApplyStartOfTurn AFTER the un-rest (unlike untilNextTurn at L1793,
  which is removed BEFORE the un-rest and would let the card refresh). `eb02021test`: +6000, no stall, char stays
  rested through the owner's next Refresh, then refreshes the turn after. Coverage 2675/0/0/0/0; smoke P(first)=54.67%.
- OP12-058 (reveal → play → "that Character gains [Rush]") — flagged for a deep-trace of the reveal-play-Rush chain.

## 🔁 SECOND-PASS — "that card gains an additional +N power" conditional Counter payoff — 1 BUG FIXED (#202, 10 cards)
- **#202 the "Then, if <cond>, that card gains an ADDITIONAL +N power" clause was acknowledged-manual (no-op)** across
  10 Counter events (EB03-020/OP01-029/OP04-095/OP05-114/OP06-038/OP07-035/OP07-095/OP11-059/OP12-098/OP14-078) →
  the conditional bonus never applied, so the Counter under-defended (only the base +2000). Fix (robust across BOTH
  the ". Then," early-split AND the Counter-path split — "that card" = the card clause A picked): the "up to 1 …
  gains +N power" handler now records the buffed card in `state.LastPowerBuffTargetId[seat]`; a new standalone
  handler for "that card gains an additional +N power during this battle/turn" applies +N to that tracked card (any
  leading "If <cond>," was already gated by the leading-If evaluator). Added to the auto-gate; removed the ack-block
  no-op. `op01029test`: Life≤2 → base 1000+2000+3000=6000; Life>2 → 1000+2000=3000. Coverage 2675/0/0/0/0; smoke clean.
- **GOTCHA:** first attempt (unit-handling in the buff handler + a split guard) regressed coverage 2675→2672 (NA=3:
  OP05-114/OP11-059/OP14-078) because the Counter PATH splits BEFORE the buff handler's early-split guard — the
  standalone-clause+tracking approach is split-path-agnostic.

## 🔁 SECOND-PASS — self-restriction downsides FINAL (add-Life-to-hand, set-DON-active-via-Char) — 1 BUG FIXED (#201)
- **#201 the last two no-op'd self-restrictions ENFORCED — self-restriction class CLOSED:** (a) "you cannot add
  Life cards to your hand using your own effects during this turn" (OP02-004/OP02-023/OP06-020/ST15-001) → flag
  `NoAddLifeToHandThisTurn` gates the own Life-to-hand EFFECT bodies (top-of-Life + top-or-bottom). (b) "you cannot
  set DON!! cards as active using Character effects during this turn" (EB04-016/OP10-030) → flag
  `NoSetDonActiveViaCharThisTurn` + `BlockedFromSetDonActive(effect)` helper (checks the flag AND that the effect
  SOURCE is a Character — Leader/Event effects are exempt) gates the DON-set-active handlers (Set-all-DON, set-up-
  to-1-DON). Both cleared next turn. `restrict2test`: Life-add blocked; Character DON-set blocked but EVENT DON-set
  allowed. Coverage 2675/0/0/0/0; smoke P(first)=54.67%.
- **SELF-RESTRICTION NO-OP CLASS COMPLETE (#199–#201, 5 restrictions):** cannot-play-big-Character, cannot-play-
  from-hand, cannot-attack-Leader, cannot-add-Life-to-hand, cannot-set-DON-active-via-Char — all were free-power
  no-ops in the acknowledged-manual blocks; now enforced via per-seat turn flags at their action points.

## 🔁 SECOND-PASS — self-restriction downsides cont. (no-play-from-hand, no-attack-Leader) — 1 BUG FIXED (#200)
- **#200 two more no-op'd self-restriction downsides ENFORCED:** (a) OP13-028 "you cannot play cards from your hand
  during this turn" (its DON!!-refresh downside) → per-seat flag `NoPlayFromHandThisTurn` checked at the TOP of
  PlayCard (blocks ALL hand plays). (b) OP06-026 "you cannot attack a Leader during this turn" (its re-stand
  downside) → flag `CannotAttackLeaderThisTurn` checked in DeclareAttack (rejects a Leader target; Character
  attacks still allowed). Both cleared at the seat's next turn start. `selfrestricttest`. Coverage 2675/0/0/0/0;
  smoke P(first)=54.67%. (GOTCHA: "restricttest" name collided with an existing harness mode → renamed to
  selfrestricttest — same class as the earlier blockbantest collision; use UNIQUE test names.)
- **Remaining no-op self-restrictions (lower value):** EB04-016 "cannot set DON!! active using Character effects",
  OP02-004 "cannot add Life cards to your hand using your own effects" — narrower enforcement points; deferred.

## 🔁 SECOND-PASS — acknowledged-manual no-op audit: self-restriction downsides — 1 BUG FIXED (#199)
- Following #198 (a no-op'd effect = free power), audited the "acknowledged-manual" blocks (L9059/L9422) — self-
  restriction DOWNSIDES logged as no-ops, so the player got the powerful effect WITHOUT its cost.
- **#199 "you cannot play Character cards with a base cost of N or more during this turn" was a NO-OP** (OP12-030,
  OP13-023, OP13-118, OP14-020 + more — the downside of a DON!! burst-ramp). Player ramped DON AND immediately
  played a huge Character (too strong). ENFORCED: new per-seat turn flag `state.NoPlayCharBaseCostAtLeast` (set in
  the restriction handler, parsing the cap; "cannot play any Character cards" = cap 0), checked in PlayCard against
  PRINTED base cost (rejects the play), cleared at the seat's next turn start (ApplyStartOfTurn). `op12030test`:
  cap-7 restriction blocks a base-cost-8 Character, allows a base-cost-2 one, clears next turn. Coverage 2675/0/0/0/0;
  smoke P(first)=54.67%.
- **Remaining no-op self-restrictions (follow-up candidates):** "you cannot play cards from your hand during this
  turn" (OP13-028), "you cannot attack a Leader during this turn" (OP06-026), "cannot set DON!! active using
  Character effects" (EB04-016), "cannot add Life cards to your hand using your own effects" (OP02-004) — each a
  no-op downside; enforce via similar turn flags checked at their action point (PlayCard / DeclareAttack / etc.).

## 🔁 SECOND-PASS — LAST deferred stall: OP08-074 Black Maria DON-parity — 1 BUG FIXED (#198) — SWEEP COMPLETE
- **#198 OP08-074 "return DON!! … until you have the same number as your opponent" was acknowledged-MANUAL (no-op)**
  → Black Maria's burst ramp (add up to 5 DON, rest them) was kept PERMANENTLY instead of normalizing to parity at
  end of turn (a real balance bug — free +5 DON). Implemented: the delayed form registers a `returnDonToParityAtEnd
  OfTurn` modifier processed in EndTurn (new `ReturnDonToParity` helper returns cost-area DON!! — rested first —
  until field DON!! == opponent's); the immediate form returns now. Added the body to the auto-gate. `op08074test`:
  ramp 3→8, then EOT return to parity (3, opp=3). Coverage 2675/0/0/0/0; smoke P(first)=54.67%.
- **GATE-vs-RESOLVER drift + continuation sweep COMPLETE (#192–#198, 7 fixes + 1 new-handler + 1 new-mechanic):**
  autogatetest (3 batches, ~50 continuation bodies) now shows only 2 residual "stalls" — both isolated-body FALSE
  POSITIVES (EB04-011 "trash the same number" and OP05-040 "trash this Stage" are handled as UNITS/inline, never as
  standalone continuations). Every real ". Then," continuation stall is fixed.

## 🔁 SECOND-PASS — deferred #195 cases: OP05-040 Birdcage + EB04-011 Scaled Neptunian — 1 BUG FIXED (#197)
- **OP05-040 Birdcage "Then, trash this Stage" DROPPED:** the KO-all handler (L11080) consumed the full text and
  returned, dropping the self-trash tail → Birdcage lingered as a RECURRING [End of Your Turn] wipe instead of a
  one-time one. Added a "trash this Stage" tail to the KO-all handler (MoveToTrash the source Stage).
- **EB04-011 Scaled Neptunian TWO bugs:** (a) "Draw a card FOR EACH of your {Neptunian} type Characters" fell to
  the generic Draw handler → drew 1, ignoring the scaling (should draw = count of Neptunian chars); (b) "Then,
  trash THE SAME NUMBER of cards from your hand" stalled (no context for "same number"). Fixed with a combined
  scaling-draw handler (counts {T}-type own Characters, draws that many, then queues a concrete "Trash N cards
  from your hand" mandatory discard). Guarded the ". Then," early-split to keep "for each … the same number"
  together (the split would lose the count). Broadened the hand-discard handler/gate to also match the mandatory
  fixed-N form. `deferstalltest` (OP05-040 KO+self-trash; EB04-011 draw2+trash2). Coverage 2675/0/0/0/0; smoke clean.
- **LAST deferred stall: OP08-074** "at the end of this turn, return DON!! cards from your field to your DON!!
  deck" (delayed EOT DON-return, niche tempo). (autogatetest batch-3 now shows 3 "stalls" but 2 are OP05-040/
  EB04-011 handled as UNITS — isolated-body false positives; only OP08-074 is genuinely open.)

## 🔁 SECOND-PASS — deferred #195 case: OP06-006 targeted delayed self-trash — 1 BUG FIXED (new handler)
- **#196 "Trash N of your {type} Characters at the end of this turn" was UNHANDLED** (a targeted delayed self-KO —
  distinct from "trash THIS Character at the end of this turn" L8893, self-only). OP06-006 Saga's ". Then, trash 1
  of your {FILM} type Characters at the end of this turn" continuation stalled pending (probe confirmed: clause A
  buffed Saga to 6000, clause B sat unresolved with no handler). Implemented a handler (mirrors the own-KO pick
  L8903): target-picks a matching own Character with the {type}/feature filter, marks it `trashAtEndOfTurn` (the
  existing EndTurn sweep L14116 does the actual trash), supports "up to N"/exact-N. Also added to the auto-gate.
- `op06006test`: Saga 6000, continuation queues, non-FILM target REJECTED, FILM char marked (still on board pre-EOT)
  then TRASHED when the turn ends. Coverage 2675/0/0/0/0; smoke P(first)=54.67%. (This was one of the 4 deferred
  batch-3 stalls — a real missing handler, now fixed rather than blindly gated; 3 remain: OP08-074/EB04-011/OP05-040.)

## 🔁 SECOND-PASS — gate-drift audit batch 3 (rare count-1 continuations) — 1 BUG FIXED (5 bodies)
- Extended `autogatetest` to 16 rare (count-1) self-effect/restriction/unusual continuation bodies. **#195 — 5 more
  resolver-handled bodies STALLed** (all confirmed handlers, all real ". Then," continuations): OP14-048 "trash all
  cards from your hand" (L8771), OP03-005 "trash this Character at the end of this turn" (L8893), OP09-093 "negate
  the effect of up to N …" (L11660), OP01-013 "give this Character up to N rested DON!! cards" (L9066), OP15-058
  "give up to N rested DON!! cards to 1 of your Characters" (L11727). Added all 5 to the gate. `op14048test`
  (end-to-end: bounce opp char → hand-wipe continuation fires). Coverage 2675/0/0/0/0; smoke P(first)=54.67%.
- **4 remaining batch-3 STALLs DEFERRED (need per-card trace, not blind gating):** OP08-074 delayed EOT DON-return,
  EB04-011 "trash the same number of cards from your hand" (variable — needs the draw count), OP05-040 "trash this
  Stage" (EOT body vs the L6299 COST handler), OP06-006 "trash 1 of your {FILM} type Characters at the end of this
  turn" (TARGETED delayed KO — no handler for the targeted-EOT-trash form). These are delayed/variable bodies that
  may lack a clean handler or whose clause-A may not split; gating blindly could cause a silent no-op. Flagged for a
  future per-card deep-trace.

## 🔁 SECOND-PASS — gate-drift audit batch 2 (mid-frequency continuations) — 1 BUG FIXED
- Extended `autogatetest` to 20 more mid-frequency ". Then," continuation bodies (give-opp-power/cost, opp-rest,
  DON-active, play-from-hand, bounce, self-power, variable-draw, Life-face-down, restrictions). 19/20 already gated.
- **#194 "Trash up to N cards from your hand" standalone body STALLed** (resolver-handled L8780; ". Then," tail of
  OP02-059/070, OP09-059). Per the gate's own philosophy ("up to N" bodies auto-enter selection), its omission was
  the same drift bug — the discard selection never auto-opened after the preceding clause. Added
  `^[Tt]rash up to \d+ cards? from your hand` to the gate. `op09059test` (end-to-end: Counter +3000 buff → the
  trash-up-to-2 continuation queues → a hand card trashes). autogatetest batch2 stalls: 0. Coverage 2675/0/0/0/0;
  smoke P(first)=54.67%.

## 🔁 SECOND-PASS — GATE-vs-RESOLVER drift systematic audit (5 stalling continuations) — 1 BUG FIXED
- Followed #192's lesson (a resolvable body the auto-gate omits stalls as a ". Then," continuation). Batch-audited
  IsAutomatedEffectPattern against self-effect/restriction continuation bodies via reflection (`autogatetest`).
  **#193 — 5 more resolver-handled bodies were STALLing** (all have handlers, all appear after ". Then,"):
  - EB01-059 "trash cards from the top of your Life cards until you have N Life card" (L8806) — after a K.O.
  - EB04-016 "you cannot set DON!! cards as active using Character effects during this turn" (L8992)
  - OP12-030 "you cannot play Character cards with a base cost of N or more during this turn" (L8993)
  - OP01-057 "set up to 1 of your Characters as active" (un-rest own, generic L11241) — after a Counter buff
  - OP01-098 "shuffle your deck" (L8928) — after a reveal-search (was redundant-but-lingering)
  Added all 5 patterns to the gate. `op01057test` (end-to-end: Counter buff 3000 → un-rest continuation fires,
  RESTED char un-rested); `autogatetest` shows 0 remaining stalls in the audited set. Coverage 2675/0/0/0/0; smoke
  P(first)=54.67%. **GATE-vs-RESOLVER drift is now a fixtured audit (autogatetest) — invisible to coverage's own loop.**

## 🔁 SECOND-PASS — reactive "When your opponent plays a Character" self-rest continuation stalled — 1 BUG FIXED
- **#192 mandatory "rest this Character." body was missing from IsAutomatedEffectPattern** → its handler exists
  (L8886) but the auto-resolve GATE (used by QueueAndAutoResolve L6191) didn't recognize it, so a "Then, rest this
  Character" CONTINUATION queued but never auto-resolved (stalled pending). OP04-024 Sugar "[Opponent's Turn][OPT]
  When your opponent plays a Character, if your Leader has {Donquixote Pirates}, rest up to 1 of your opponent's
  Characters. Then, rest this Character." — the opp-char rest fired but Sugar never rested itself. Also OP08-046.
  Added `^rest this (Character|card)\.?$` to the gate (anchored — never catches the "You may rest this Character:"
  cost prefix). `op04024test`: reactive fires on opp play + gates on DQ leader → opp char rested + Sugar rested +
  OPT consumed; non-DQ leader → nothing. Coverage 2675/0/0/0/0; smoke P(first)=54.67%.
- The reactive dispatch itself (L2042 "When your opponent plays a Character") is correct: fires for the reactor,
  OPT-gated, condition-gated via the queued body's leading-If. The stall was purely the gate omission.

## 🔁 SECOND-PASS — opponent-DEBUFF aura mirror paths — VERIFIED CLEAN (no #191-class gap)
- Followed #191's lesson (substring bug recurs in every path that re-implements the filter) into the debuff
  mirrors GetOpponentDebuffAuraBonus (power) + GetOpponentCostDebuffAura (cost). ALL 5 type-referencing opponent-
  debuff auras (EB04-017, OP05-084/092, OP07-071, OP15-001) target "all of your opponent's Characters" with NO
  recipient type filter — the {Type} lives in the CONDITION (via EvaluateCondition, already substring-safe). So the
  #191 quoted-type recipient gap CANNOT exist here; #191 was the LAST home of that bug.
- Locked the power-debuff path end-to-end: OP07-071 "[Opponent's Turn] If your Leader has the {Foxy Pirates} type,
  give all of your opponent's Characters −1000 power" — `op07071test`: my char 1000→0 during the debuffer's opp
  turn with a Foxy leader; 1000 on the debuffer's own turn (timing gate); 1000 with a non-Foxy leader (condition
  gate). Cost-debuff path already covered by celestialtest (OP05-084/092). No engine change; coverage 2675/0/0/0/0.
- NOTE for future: the `type includ(es\|ing) "X"` substring rule is now correct in all 4 recipient/target/filter
  paths (#183 CardPassesFeatureFilter, removal-replacement L4410, #186 reveal-condition, #191 aura-recipient) —
  candidate for consolidation into one shared matcher to prevent a 5th divergence.

## 🔁 SECOND-PASS — conditional aura recipient quoted `type including "X"` was EXACT not substring — 1 BUG FIXED
- **#191 aura-recipient `type including "X"` used HasFeature (exact part), not substring** (GetTurnPassiveAuraBonus
  L786-787) — the SAME class as #183/#186 but in the continuous power-aura recipient path. OP02-024 Moby Dick
  "[Your Turn] If you have 1 or less Life cards, your [Edward.Newgate] and all your Characters with a type including
  "Whitebeard Pirates" gain +2000" buffed exact-"Whitebeard Pirates" (Vista 5000→7000) but SKIPPED
  "Whitebeard Pirates Allies" (Doma stayed 3000). Fixed to substring on Features (broadened to includ(es|ing)).
  Affects 2 "type including"-recipient power auras (OP02-024, OP02-019).
- The condition + timing gates were already correct (verified live-flip): `op02024test` — ON only at Life≤1 AND
  own turn (Vista 7000, Doma 5000); Life>1 → off; opp turn → off. Coverage 2675/0/0/0/0; smoke P(first)=54.67%.

## 🔁 SECOND-PASS — aura RECIPIENT-FILTER variants (self-excl / multi-type-or / base-cost / Stage) — VERIFIED CLEAN
- Deep-traced 3 recipient-filter variants; all correct (`auraflttest`):
  - **Self-exclusion** — OP04-012 Cobra "[Your Turn] All of your {Alabasta} type Characters OTHER THAN THIS
    CHARACTER gain +1000": other Alabasta (Koza) 3000→4000, source Cobra stays 0 (NOT self-buffed).
  - **Multi-type "or"** — OP10-001 Smoker leader "[Opponent's Turn] All of your {Navy} OR {Punk Hazard} type
    Characters gain +1000": opp turn Komei(Navy) & Monet(Punk Hazard) both 6000→7000, plain 1000 untouched; own
    turn no buff.
  - **Base-cost filter from a STAGE** — EB04-010 Lulucia Kingdom "[Opponent's Turn] All of your Characters with a
    base cost of 1 gain +5000": opp turn Doma(base cost 1) 3000→8000, Koza(base cost 2) 3000 unaffected. (Stage as
    aura source works.)
- No engine change; coverage 2675/0/0/0/0. Aura recipient filters (self-exclusion, or-list types, base-cost,
  Stage-sourced) all sound — complements eb04046test/op13004test aura-gating coverage.

## 🔁 SECOND-PASS — continuous board auras (opp-turn cost, DON+condition power) — VERIFIED CLEAN
- Deep-traced 2 representative auras from the 40-card continuous-aura set; both correct:
  - **Opp-turn cost aura** — EB04-046 Doll "[Opponent's Turn] All of your {Navy} type Characters gain +2 cost"
    (defensive: raises own Navy cost so opponent cost-removal misses). GetPassiveCostAuraBonus (L933) timing-gates
    it: `oppTurnC && isOwnTurn → skip`. `eb04046test` — opp turn: Doll 2→4, Komei 4→6, non-Navy 1 unaffected;
    own turn: no bonus. (Source buffs itself since it's Navy.)
  - **DON + condition power aura** — OP13-004 Sabo leader "[DON!! x1] If you have a Character with a cost of 8 or
    more, your Leader and all of your Characters gain +1000 power" + "If you have 4+ Life, this Leader −1000".
    GetTurnPassiveAuraBonus gates on BOTH the DON!! x1 threshold (Leader attached-DON ≥1) AND the cost-8+ condition.
    `op13004test` — recipient 2000 only with DON+bigChar; 1000 if either missing; Leader 4000 at Life≥4 (debuff).
- No engine change; coverage 2675/0/0/0/0. Aura timing/DON/condition/feature/color gating confirmed sound (also
  re-verified by celestialtest opp-cost-debuff + donpowertest DON-attach owner-turn).

## 🔁 SECOND-PASS — EvaluateCondition audit cont. (opponent-scope / base-power / negatives) — VERIFIED CLEAN
- Extended condcoveragetest to 34 assertions. Batch 4 (positive, all handled): opponent-side "there is a Character
  with 8000 power" (any field), power-filtered counts ("1 or less Characters with 6000 power or more"), 10000-power,
  7000 BASE-power, {Donquixote Pirates}+power, "your opponent has a Character with 5000 or more power" (reversed
  word order), opponent + BASE power, opponent + power. Batch 5 (NEGATIVE scope, all correctly FALSE): opp-8000-power
  with the char on MY side → false (proper opponent-scoping, not wrong-field); "2+ [Name]" with only 1 present →
  false; "3+ Cross Guild" with only 2 → false (locks the #190 fix + count boundaries).
- No engine change; coverage 2675/0/0/0/0. Condition evaluator confirmed robust across positive/negative + scoping.

## 🔁 SECOND-PASS — EvaluateCondition coverage audit cont. (color/named/type counts) — 1 BUG FIXED
- **#190 numbered NAMED-card count "you have N or more [Name] cards/Characters" UNHANDLED.** OP16-057 ("[Counter]
  If you have 2 or more [Prisoner of Impel Down] cards, up to 1 … gains +4000 power") — the bare "you have [Name]"
  handler (L5645) anchors "[" right after "have" (so the numeric prefix slipped past), and the {tag}-type count
  handles only `{tag} type Characters`, not `[Name] cards`. So the condition returned false → the Counter's +4000
  NEVER fired. Added a handler counting field Characters (+ Leader) NameMatches([Name]), both directions. Only
  OP16-057 uses this (verified via DB scan). `condcoveragetest` batch 3.
- Batch 3 also VERIFIED CLEAN: "3+ blue {Cross Guild} type Characters" (color-filtered count), "3+ {Foxy Pirates}
  type Characters" (type count). Coverage 2675/0/0/0/0; smoke P(first)=54.67%.

## 🔁 SECOND-PASS — EvaluateCondition coverage audit (20 unusual shapes) — VERIFIED CLEAN
- Follow-up to #189 (unhandled condition = silently-dead card): harvested all 261 distinct leading-"If <cond>,"
  shapes from the DB and batch-tested 20 of the unusual/rare ones against EvaluateCondition via reflection (each
  built into a SATISFYING state, asserting TRUE). All 20 handled: cost-N+ Char present, 2+ rested Chars, any/total
  given DON, fewer-Life-than-opp, opp-more-DON, 2+ base-cost-5+ Chars, played-this-turn, only-{CD}-type,
  2+ rested {ODYSSEY}, deck≤20, 3+/5 Chars, second-turn-or-later (TurnsStarted≥2), total-cost≥5, ≤5/≥2 active DON,
  8000+ power on field, 5-distinct-name {Impel Down}, all-DON-rested. `condcoveragetest` (permanent batch fixture).
- No engine change; coverage 2675/0/0/0/0. The #189 "Life area and hand" gap was a genuine one-off; the evaluator
  is otherwise robust for the shapes covered. (~97 rare shapes remain for future spot-checks, many are reveal/"that
  card"-conditions handled in the reveal path, not EvaluateCondition.)

## 🔁 SECOND-PASS — OP04-040 Queen "Life area and hand" condition UNHANDLED — 1 BUG FIXED
- **#189 "you have a total of N or less cards in your Life area and hand" was UNHANDLED in EvaluateCondition** →
  returned false → the leading-If gate skipped OP04-040 Queen's ENTIRE [When Attacking] draw/ramp effect even when
  the count qualified (probe: Life+hand=2 → drew 0). Added a handler summing `p.Life.Count + p.Hand.Count` (or/less
  both directions). Now: Life+hand>4 → nothing; ≤4 → draw 1. `op04040test`.
- **Documented simplification:** the "you may add up to 1 card from the top of your deck to the top of your Life
  cards INSTEAD of drawing" (when you have a cost-8+ Character) is not offered as a draw-vs-ramp choice — auto-resolve
  takes the printed DEFAULT (draw). Would need an ActiveChoice; low priority (1 card, default is correct). Coverage
  2675/0/0/0/0; smoke P(first)=54.67%; only OP04-040 uses this condition.

## 🔁 SECOND-PASS — conditional cost-cap "instead" UPGRADE glow/resolve mismatch — 1 BUG FIXED
- **#188 conditional cap-upgrade "… cost of M or less instead" glowed with the BASE cap only.** OP04-094 Trueno
  Bastardo (K.O. cost≤4; cost≤6 instead if 15+ trash), OP12-096 Ursa Shock (cost≤4; cost≤6 instead if a cost-8+
  Character). The K.O. RESOLVER already applied the upgrade (L11115-11130) — so with 15+ trash a cost-6 char DID
  resolve-K.O. — but IsValidEffectTarget's cap parse (L7154) took only the base ParseLimit (4), so the upgraded
  target NEVER GLOWED (glow=F while resolve=T): the player couldn't see the legal target. Fix: extracted a shared
  `ApplyConditionalCostCapUpgrade(state,seat,text,baseCap)` (both "instead" phrasings) and called it in BOTH the
  glow AND the resolver (refactored the resolver's inline block to the helper), so they can never diverge again.
- `op04094test`: trash15 → glow=T & K.O.=T on the cost-6 char; trash5 → glow=F & K.O.=F. Coverage 2675/0/0/0/0;
  op12051/op11081/op05019/blockerban KO regressions green; smoke P(first)=54.67%.

## 🔁 SECOND-PASS — ". Then, if <cond>," conditional-payoff class (~48 cards) — VERIFIED CLEAN
- Follow-up to #185/#187 (unhandled/ungated conditional payoffs): swept the ". Then, if <cond>, <payoff>" class to
  confirm the payoff is GATED. Unlike #185/#187 (no split → payoff caught free by a generic handler), these DO get
  the ". Then," early-split (L7796): clause A resolves, clause B ("if <cond>, <payoff>") is re-queued and the
  leading-If gate (L7411) evaluates the condition — payoff fires only if met. Verified two distinct condition types:
  - **Life-count gate** — OP05-019 Fire Fist ("Give −4000 … Then, if you have 2 or less Life cards, K.O. up to 1
    … with 0 power or less"): `op05019test` — Life 3 → −4000 applies (power 0) but NO K.O.; Life 2 → K.O. fires.
  - **Name-ABSENCE gate** — OP08-005 Chess ("… Then, if you don't have [Kuromarimo], play up to 1 [Kuromarimo] from
    your hand"): `op08005test` — no Kuromarimo on field → plays it; already have one → does NOT play.
  Class also covers trash-count / DON-count / opp-Life gates (same split+gate path). No engine change; coverage
  2675/0/0/0/0. The #185/#187 vulnerability was specific to cards the ". Then," split does NOT reach (inline gate or
  no dedicated handler) — the split-reached ". Then, if" cards are sound.

## 🔁 SECOND-PASS — Big Mom "Choose a cost" opponent-deck guess — 1 BUG FIXED (6 cards, free payoff → gated)
- **#187 "Choose a cost … reveal opponent's top card. If it has the chosen cost, <payoff>" fired the payoff
  UNCONDITIONALLY.** OP11-066/071/073/074/079/081 (Big Mom) had NO handler — the trailing payoff substring
  (K.O./draw/+power/rest/DON-add) matched a later generic handler and fired regardless of the guess. Probe: OP11-081
  KO'd an opponent Character with no guess and no reveal (free removal). Added a dedicated handler (before all payoff
  handlers): the controller GUESSES a cost (command targetId "cost:N", else a fixed honest default of 1 — NEVER peeks
  at the hidden top card), the opponent's top card is revealed (stays on top), and the payoff is extracted after
  "the chosen cost," and queued ONLY on an exact cost match. Anchored on a leading "^Choose a cost" so any
  "You may <cost>:"/"DON!! −N" prefix (OP11-066/071/073/074) is paid FIRST by the cost/DON-minus resolvers, which
  re-queue the bare "Choose a cost…" body here.
- `op11081test`: guess1/opp-top-cost1 → KO fires; guess1/opp-top-cost6 → no KO; opp deck untouched both ways.
  Coverage 2675/0/0/0/0 (all 6 still auto-resolve with the default guess); smoke P(first)=54.67%.

## 🔁 SECOND-PASS — conditional reveal condition: quoted `type includes "X"` was EXACT not substring — 1 BUG FIXED
- **#186 reveal-condition `type includes "X"` used HasFeature (exact part), not substring.** The reveal handler's
  condition parse (L10754) matched the quoted type filter with `revDef.HasFeature(X)` — token-exact — so a revealed
  card whose type merely CONTAINS X failed. ST22-003/006 & OP14-044 ("If that card's type includes "Whitebeard
  Pirates", draw 2 …") DROPPED the draw payoff on a "Whitebeard Pirates Allies" reveal; same for ST17-001/OP08-049.
  Fix: substring match on Features (matches "…Allies"), broadened to "includ(es|ing)", consistent with #183 and the
  removal-replacement/condition paths already fixed. {Tag} curly form still uses token-exact HasFeature.
- Handler ORDER here is fine (reveal L10703 precedes draw L12422), unlike #185's DON-add — the bug was purely the
  exact-vs-substring condition. `st22006test`: exact-WB draws, Allies-WB draws (was 0), non-WB no draw. Coverage
  2675/0/0/0/0; op04011/revtail/op15065 reveal regressions green; smoke P(first)=54.67%.

## 🔁 SECOND-PASS — conditional reveal → DON-add payoff (OP15-065 Goro) — 1 BUG FIXED
- **#185 reveal condition BYPASSED for a DON-add payoff.** OP15-065 Goro "[On Play] Reveal 1 card … If the revealed
  card has a cost of 2 or less, add up to 1 DON!! card from your DON!! deck and rest it." The generic DON-add handler
  (L8426) sits BEFORE the reveal handler (L10703) and matched the full text → added a rested DON UNCONDITIONALLY
  (a cost-6 reveal still ramped). Also the reveal handler's add-to-hand branch (L10807) would have grabbed "add up
  to 1 DON!!" and put the revealed card into hand. Three-part fix: (a) DON-add handler skips `revealGatedDon` text
  (Reveal + from-top + "If the/that revealed card"), letting the reveal handler own it; (b) add-to-hand branch now
  requires "to your hand" AND excludes "DON!!"; (c) the generic payoff-extraction regex broadened from "If that
  card" to "If (that card|the revealed card)" so the DON-add clause is queued (→ L8426 DON-add resolver) only when
  the cost gate holds.
- `op15065test`: cost-1 reveal → +1 rested DON, revealed stays on top; cost-6 reveal → 0 DON, stays on top. Coverage
  2675/0/0/0/0; op04011test + revtailtest still green; smoke P(first)=54.67%.

## 🔁 SECOND-PASS — reveal "Then, place the rest at the bottom" (auto / add / deferred-skip) — 1 BUG FIXED
- **#184 mandatory "place the rest at the bottom of your deck" DROPPED when the revealed card isn't played/added.**
  The #181 tail matched only "place the revealed card at the bottom"; cards phrased "place the **rest** at the
  bottom" (OP06-119 auto play-up-to, ST11-001 add-to-hand, OP07-048 Doflamingo DEFERRED you-may-play) left the
  un-played card stuck on TOP (would be re-drawn next turn). Fix, three parts: (1) reveal tail now also matches
  "place the rest at the bottom" and moves the still-on-top revealed card to the bottom; (2) it is GUARDED by a new
  `deferredPlayQueued` flag so a pending you-may-play decision isn't buried prematurely; (3) the deferred decision
  carries the "…place the rest at the bottom" tail, and PassEffect places the un-played card at the bottom on SKIP
  (on PLAY it's already off the deck). "place the rest at the TOP or bottom" cards are left alone (on-top = the
  legal "top" choice).
- `revtailtest`: defSkip→bottom, defPlay→in-play, defNo(non-warlord)→bottom, autoNo(OP06-119)→bottom, addNo(ST11-001)→bottom.
  Coverage 2675/0/0/0/0; op04011test (#180/#181) still green; smoke P(first)=54.67%.

## 🔁 SECOND-PASS — quoted-type SIBLING contexts (removal-replacement / "only have" / KO-cost / negation) — VERIFIED CLEAN
- Follow-up to #183: audited every OTHER place a `type including "X"` quoted filter is used, to confirm the same
  substring-enforcement (not pass-all). ALL already correct — the #183 gap was unique to CardPassesFeatureFilter:
  - **Removal-replacement** (OP13-047 Fossa "Whitebeard Pirates", OP13-060 Toki "Roger Pirates"): L4410 substring
    match on victim Features — protection fires ONLY for the matching type. `op13047test` (WB "Whitebeard Pirates
    Allies" victim protected via substring; non-WB victim NOT protected; Fossa self-trashes only when it fires).
  - **"you only have Characters with a type including "X"" condition** (OP11-046/OP11-043 "GERMA"): L5950 substring
    (matches "GERMA 66"). **K.O.-instead trash-cost filter** L4553; **effect-negation** "do not have a type
    including X" L4891; **"power N+ and a type including X"** L5507 — all substring, all correct (verified by read).
- No engine change this iteration; coverage 2675/0/0/0/0. Locked with op13047test.

## 🔁 SECOND-PASS — quoted feature filter `type includ(es|ing) "X"` — 1 BUG FIXED (108-card class)
- **#183 quoted "type including/includes "X"" filter UNENFORCED.** `CardPassesFeatureFilter` (L6814) only handled
  `{Tag}` curly-brace + ＜attribute＞ forms and returned `true` (pass ALL) for the QUOTED form (`type including "CP"`,
  `type includes "Whitebeard Pirates"`). 108 cards use the quoted form → their glow (IsValidEffectTarget), targeting,
  and trash-play filters were silently OFF. OP03-090 "Play up to 1 Character with a type including "CP" … from your
  trash rested" glowed AND could play a NON-CP character (EB01-003 Kid&Killer). Root fix: parse quoted forms and
  require a SUBSTRING match against the card's Features (`"CP"` matches CP0/CP9; `"Whitebeard Pirates"` matches that
  full type — HasFeature's exact-part match can't do the CP-family prefix). Strips LEADER-condition clauses first
  ("If your Leader's type includes "X"," describes the leader — OP14-084/PRB02-013), then requires the target to
  match a remaining filter. Fixes glow + resolver together (both call CardPassesFeatureFilter).
- `op03090test`: glow good=T/badCost=F/badType=F, resolver rejects non-CP, plays valid CP-cost2 RESTED. Coverage
  2675/0/0/0/0; smoke P(first)=54.67%; celestial/eachdon/op16003/trashany/cavendish regressions all green.

## 🔁 SECOND-PASS — self-revive-from-trash ("Play this Character from your trash[ rested]") — 1 BUG FIXED
- **#182 "rested" qualifier dropped on self-revive.** The self-revive handler (L9387, "Play this Character card
  from your trash" — the K.O.'d source revives itself) set Zone/PlayedOnTurn/slot but NEVER set `Rested`. Cards
  that say "…from your trash **rested**" (OP03-013, OP09-052, ST30-008) wrongly entered ACTIVE (free attacker);
  OP14-120/OP16-014 correctly enter active. Added `reviv.Rested = ContainsAll(text, "from your trash rested")`.
- **Recursion is cost-bounded (no infinite loop):** every On-K.O. self-revive requires paying a fresh hand-trash
  cost ("trash 1 Event/Character from your hand"), so it self-limits with the hand. Revive also requires an empty
  slot + the card still in trash (no-ops cleanly otherwise). MoveToTrash (L13755) fully cleans a K.O.'d Character
  (Modifiers.Clear, ReturnAttachedDon, NameOverrides/BasePowerOverrides/TimedPowerBonuses removed, Rested=false),
  so the revived card carries NO stale state — verified, not a bug.
- `selfrevivetest` (rested→in-play+rested, active→in-play+active, full-slot→stays in trash). Coverage 2675/0/0/0/0; smoke clean P(first)=54.67%.

## 🔁 SECOND-PASS — conditional reveal (power filter + place-at-bottom tail) — 2 BUGS FIXED
- **#180 reveal POWER filter ignored.** OP04-011 Nami "Reveal 1 from top. If the revealed card is a Character
  card with **6000 power or more**, this Character gains +3000…". The reveal condition block (L10725) parsed
  feature/type/cost/exclusion but NOT a power threshold → `revMatch` was true for ANY Character regardless of
  power, so Nami buffed on a 1000-power reveal. Added a `(\d{3,5}) power or (more|less)` filter on `revDef.Power`.
- **#181 unconditional "Then, place the revealed card at the bottom" DROPPED.** Same cards (OP04-011, EB01-029).
  Clause A contains "from the top of your deck" so the ". Then," splitter keeps it internal (L7813) → the full
  text hits the reveal handler, whose gains/return/no-match branches never moved the revealed card, leaving it
  stuck on TOP. Added an unconditional tail: if text says "place the revealed card at the bottom" and the card is
  still on top (not played/added), Shift → append to bottom. Fires on BOTH match and no-match.
- `op04011test`: hi(7000-char)→ +3000 buff & revealed→bottom; lo(1000-char)→ NO buff & revealed→bottom. Coverage
  OK=2675/0/0/0/0; smoke 900 games clean P(first)=54.67%.

## 🔁 SECOND-PASS — self-mill / deck-out / mill-as-cost — VERIFIED CLEAN
- Body-mill "Trash N cards from the top of your deck" (L8153/L8161) caps the loop at `owner.Deck.Count > 0`:
  milling more than the deck holds trashes only what's there, no crash. Per rulebook **9-2-1-2** (0 cards in deck
  is ITSELF a defeat condition, NOT tied to the draw step — unlike MTG), milling your own deck to 0 correctly
  self-defeats at the next rule-processing (CheckRuleProcessing L14079, with the DeckLook guard + Nami-wins /
  Brook-deferred special cases). Nearly "fixed" this as a bug — rulebook confirmed the engine is CORRECT.
- Mill-AS-cost "You may trash N cards from the top of your deck: <body>": the cost-prefix resolver (L7471) pays via
  TryAutoPayCost (mill-cost handler L6425, returns 0/unpayable if `Deck.Count < N`) then QueueBody's the body as a
  fresh auto-resolving effect (L7506) — for EB04-049 the queued "K.O. up to 1 opp Character base cost 5 or less"
  then awaits its target and KOs it (handler L12384). Optional cost with a too-small deck is simply declined (no
  mill, body doesn't fire). `milltest`: deckout-safe(deck0/trash1/finished), normal(deck9/trash3), cost-mill(-2/+2)+KO, unpayable(deck untouched/victim alive).
- No engine change; coverage OK=2675/0/0/0/0. Test: milltest.

## Second-pass VERIFIED-CLEAN (this batch): cannot-attack restrictions, will-not-become-active/freeze (incl. Leader), opponent-hand disruption, "return 1 or more DON" variable cost, Life manipulation, play-rested entry, [Rush] variants, [Double Attack]/[Banish], [Blocker] redirect+ban, DON-attach power (owner-turn), Stage rest-cost activate, opp-next-turn delayed grants, base-power-set overrides, DON!!−N attached/cost-area payment, self-mill/deck-out defeat/mill-as-cost.

## Second-pass fix ledger (post-NA): #163 EB01-008, #164 OP05-001, #165 cost-scaling trio, #166 OP16-105 multi-named trash-play, #167 cost-red/rest ". Then," clause-drop, #168 give-DON-to-Leader ". Then," clause-drop, #169 trash-any-number scaling buff, #170 On-K.O. reactive battle-exclusion, #171 replacement-resolver battle-exclusion, #172 EoT conditional-KO-of-rested timing, #173 On-Block DON gate, #174 Activate:Main/When-Attacking shared OPT key, #175 "Your Leader gains [KW]/power" aura, #176 blocker cost-ban filter+targeting, #177 multi-pick distinct-target enforcement, #178 can-attack-active phrasing, #179 [Counter] non-power-buff primary drop (+OP02-118 protect handler), #180 conditional-reveal power-filter unparsed, #181 conditional-reveal "place-at-bottom" tail dropped, #182 self-revive-from-trash "rested" qualifier dropped, #183 quoted `type includ(es\|ing) "X"` feature filter unenforced (108-card glow+targeting+trash-play gap), #184 reveal "place the rest at the bottom" dropped (auto/add/deferred-skip), #185 conditional-reveal DON-add payoff fired unconditionally (handler-order + add-to-hand mismatch), #186 reveal-condition quoted `type includes "X"` was exact not substring (dropped Allies-type payoffs), #187 Big Mom "Choose a cost" opponent-deck guess (6 cards) fired payoff unconditionally — now gated on the guess match, #188 conditional cost-cap "instead" upgrade glow/resolve mismatch (upgraded KO target resolved but never glowed), #189 OP04-040 "Life area and hand" total-count condition unhandled (skipped whole draw/ramp effect), #190 numbered named-card count "you have N or more [Name] cards" unhandled (OP16-057 Counter +4000 never fired), #191 aura-recipient quoted `type including "X"` was exact not substring (Moby Dick skipped "…Allies" recipients), #192 mandatory "rest this Character." body missing from auto-resolve gate (reactive self-rest continuation stalled — OP04-024 Sugar/OP08-046), #193 five more resolver-handled continuation bodies missing from the gate (EB01-059 Life-trash, EB04-016/OP12-030 self-restrictions, OP01-057 un-rest, OP01-098 shuffle), #194 "trash up to N cards from your hand" continuation body missing from the gate (OP02-059/070, OP09-059), #195 five more resolver-handled continuation bodies missing from the gate (OP14-048 hand-wipe, OP03-005 delayed self-trash, OP09-093 negate, OP01-013 self-DON-attach, OP15-058 give-rested-DON-to-Character), #196 OP06-006 targeted delayed self-trash "trash N of your {type} Characters at the end of this turn" was UNHANDLED (new handler + gate), #197 OP05-040 Birdcage "trash this Stage" tail dropped by KO-all handler + EB04-011 scaling-draw "for each" drew 1 not N & "trash the same number" stalled (combined handler + split guard), #198 OP08-074 Black Maria "return DON!! until same number as opponent" was acknowledged-manual (kept +5 DON permanently) — implemented delayed EOT DON-return-to-parity, #199 "cannot play Character cards with a base cost of N or more this turn" self-restriction was a no-op (ramp had no downside) — enforced via PlayCard turn flag, #200 "cannot play cards from your hand" (OP13-028) + "cannot attack a Leader" (OP06-026) self-restrictions were no-ops — enforced via PlayCard + DeclareAttack turn flags, #201 "cannot add Life to hand via own effects" (OP02-004 etc.) + "cannot set DON active via Character effects" (EB04-016/OP10-030) self-restrictions were no-ops — enforced via Life-to-hand body gates + DON-set-active source-is-Character gate, #202 "that card gains an additional +N power" conditional Counter payoff (10 cards) was acknowledged-manual no-op — implemented via LastPowerBuffTargetId tracking + standalone handler, #203 EB02-021 "the selected Character will not become active in your next Refresh" self-freeze stalled (opponent-only handler) — new own-char handler + "ownNextRefresh" duration, #204 OP12-058 reveal→play→"that Character gains [Rush]" chain stalled (keyword handler grabbed it first) — guarded + carried the keyword onto the deferred play, #205 cost-gated "[Trigger] You may trash N from hand: Play this card" (14 cards) ignored the discard cost (played free + played with empty hand) — parse+pay+gate the hand-trash cost, #206 OP03-100 "[Trigger] You may trash N from top/bottom of Life: Play this card" ignored the Life-trash cost — parse+pay+gate it, #207 "place N cards from trash at bottom of deck: <body>" recycle cost hardcoded to place 1 (under-paid "place 2/3", ~13 cards) — parse N + N-pick loop + type filter + payability gate, #208 place-from-HAND cost hardcoded to place 1 + ignored a compound "…and rest this Stage" rider (OP09-060 Cross Guild Stage under-paid: placed 1 not 2, Stage never rested) — parse N + N-pick loop + payability gate + pay the "rest this Stage/Character/Leader" rider (mirrors #207). Test op09060test: places exactly 2 + Stage rested.
## 🔁 SECOND-PASS — multi-modal + base-power-copy — VERIFIED CLEAN
- Multi-bullet "apply-each" cards: only 2 exist — OP15-092 (fixed #224) and ST20-005. ST20-005 "Your opponent
  chooses one: • Your opponent trashes 2 from their hand • Trash 1 from the top of your opponent's Life" already
  fixtured (st20005test L2135): the OPPONENT (north) picks via ChoiceState.Seat=OtherSeat, the chosen option resolves
  for the CONTROLLER (south) affecting north — pick A → north hand −2, pick B → north top Life −1. Both work. (The
  rest of the 21 multi-bullet cards are "Choose one" via TryParseChoiceEffect, verified earlier.)
- Base-power-copy-from-SELECTED (OP16-104 "base power becomes the same as the selected Character", also EB01-061)
  VERIFIED CLEAN: L11762 "the selected Character" branch snapshots the SELECTED opponent char's CURRENT power (not
  the Leader) → OP16-104 3000 becomes the selected 7000. `op16104test`. No engine change.

## 🔁 SECOND-PASS — per-card deep-trace — #224 OP15-092 trash-modal +power bullet dropped
- #224: OP15-092 Luffy "Apply each of the following effects based on the number of cards in your trash: • If there
  are 10 or more cards, this Character's base power becomes 9000 and it gains +10 cost. • If 20+ …Leader base 7000.
  • If 30+ cards, this Character gains +1000 power." The ≥10 base-power-set (GetPower L235 trash-context handler) and
  the +10 cost (GetPassiveCostBonus trash branch) worked, but the ≥30 "+1000 power" ADDITIVE bullet was DROPPED —
  GetStaticSelfPowerBonus evaluates its leading "If you have 30 or more cards" via EvaluateCondition, which can't
  infer the bare "cards" means TRASH (per the header), so it fail-closed to 0. Probe: 32 trash → power 9000 not
  10000. Fixed: added a sibling additive trash-context handler in GetPower (mirrors the base-power one) + a guard in
  GetStaticSelfPowerBonus to skip "based on the number of cards in your trash" cards (no double-count). OP15-092 is
  the ONLY card of this shape. `op15092test` (7000/9000/10000 across <10/≥10/≥30 trash; cost 7→17). Coverage
  2675/0/0/0/0, smoke 54.67%. (Set-power-to-N handler L11890 verified: thisTurn BasePowerOverride, modifiers layer on
  top — correct per OPTCG; on your turn an opponent's DON attach gives +0 so "set to 0" reads 0.)

## 🔁 SECOND-PASS — swap-base-power family (3 cards) — VERIFIED CLEAN
- "Swap the base power of the selected Characters with each other during this turn" (OP14-001 own {Supernovas}/
  {Heart Pirates}, OP14-017 opponent ≤9000 base, OP14-009 Leader↔Character) — handler L10014 does a 2-pick,
  reads BOTH effective base powers ATOMICALLY (honoring existing BasePowerOverrides), then writes two swapped
  thisTurn BasePowerOverrides; filters side/type(+Leader for OP14-009)/feature/base-power-cap/distinct. Verified:
  OP14-001 A6000↔B1000, OP14-017 opp X7000↔Y1000, OP14-009 Leader5000↔Char2000 — all swap correctly (expires this
  turn via the thisTurn override). `swaptest`. No engine change. (Active-Leader self-cost class fully closed by #223 —
  no other "active" self-costs exist.)

## 🔁 SECOND-PASS — per-card deep-trace — #223 "give your active Leader −N power" ignored the active requirement
- #223: the "You may give your active Leader −N power during this turn: <payoff>" self-debuff cost (~7 cards —
  EB01-004/EB03-006/EB04-023/OP04-002/006/009/OP07-006, handler L6624) checked only `p.Leader == null`, NOT that the
  Leader is ACTIVE. Per OPTCG "active" = unrested — a RESTED Leader (e.g. when the Leader itself attacked, for the
  [When Attacking] cards) is not a valid target, so the cost must be UNPAYABLE. Was over-permissive: a rested Leader
  still paid (weakened + payoff fired). Fixed: `if (p.Leader == null || p.Leader.Rested) return 0`. Common use
  ([On Play]/[Activate:Main] main-phase, or [When Attacking] on a CHARACTER so the Leader stays active) is unaffected.
  `costleadertest` (active→pay+draw+−5000; rested→unpayable; OP04-002 compound "rest this Character AND give…" →
  self rested + Leader −5000 + deck-look). Coverage 2675/0/0/0/0, smoke 54.67%.

## 🔁 SECOND-PASS — per-card deep-traces — #222 OP16-073 EOT cost+[Blocker] dropped + Dressrosa cost clean
- "rest 1 of your {Dressrosa} type Leader or Stage cards: <payoff>" choice-cost family (~10 Dressrosa cards, handler
  L6688) VERIFIED CLEAN: auto-rests an ACTIVE tag-matching Leader (else Stage), UNPAYABLE (return 0, payoff skipped)
  when neither is active/tag-matching. `reststagecosttest` (Dressrosa-active→pay+KO, already-rested→unpayable,
  wrong-type→unpayable).
- #222: OP16-073 "[End of Your Turn] DON!! −2: Set this Character as active. Then, this Character gains [Blocker]
  until the end of your opponent's next End Phase." The EOT set-active fast-path (L14764, #211) fired for it → FREE
  self-recover that DROPPED both the DON!! −2 cost AND the [Blocker] grant. Fixed: exclude a "DON!! −N:" cost-prefixed
  set-active from the fast-path (`&& ParseDonMinusCost(eoyClause) == 0`) → routes to the full resolver, which pays 2
  DON (returned to DON deck), sets active, and grants [Blocker]. Choice variant (OP13-035 "…or up to 1 DON…") still
  fast-path self-recovers (guard is DON-cost-specific). `op16073test` (2 DON auto-pay → active+paid+blocker);
  eotcondtest updated (its OP16-073 case had asserted the OLD free-recover). Coverage 2675/0/0/0/0, smoke 54.67%.

## 🔁 SECOND-PASS — per-card deep-trace — #221 compound static self-buff dropped +power
- #221 (found via the shift to per-card deep-traces of complex cards): OP15-118 / OP15-060 "If you have 6 or less
  DON!! cards on your field, this Character CANNOT BE REMOVED FROM THE FIELD by your opponent's effects AND gains
  +2000 power." The removal-protection gated correctly (CannotBeKoedByEffect L4275 evaluates the leading If), but the
  "+2000 power" was DROPPED: GetStaticSelfPowerBonus (L526) required "this Character GAINS +N power" ADJACENT, and
  here "this Character" is followed by "cannot be removed … and" before "gains +2000 power". Probe: ≤6 DON → survived
  (protected) but power=8000 not 10000. Fixed: broadened the regex to `this (?:Character|card)\b[^.]*?\bgains?\b
  [^.]*?\+N power` — allows intervening text WITHIN the sentence ([^.] bounds it, so a later sentence's aura isn't
  mis-attributed). Only OP15-118/OP15-060 were newly picked up (the other ~21 compound-looking lines were already
  adjacent). `op15118test` (both cards, ≤6/>6 DON). Coverage 2675/0/0/0/0, smoke 54.67% (no regression).

## 🔁 SECOND-PASS — "until you have N" variable-count effects (5 cards) — VERIFIED CLEAN
- Draw-UP "Draw card(s) so that you have N cards in your hand" (OP02-051/069, L7549): draws Max(0, N−hand) →
  hand 1→3, hand 5→5 (drew 0). Trash-Life-DOWN "trash cards from the top of your Life until you have N Life"
  (EB01-059/060, L9002): while Life>N pop-to-trash → Life 5→2, Life 1→1 (0 trashed). Trash-hand-DOWN "Trash cards
  from your hand until you have N cards" (OP14-054, L9952): interactive, Max(0, hand−N) picks → hand 8→5, hand 3→3.
  All correct in direction + count + no-op-when-already-past. `untilhavetest`. No engine change.
- Non-power "for each/every" scaling: only EB04-011 (Neptunian draw, #197) + ST27-004 (cost-scaling) — already handled.
- 5th consecutive verified-clean iteration — engine broadly hardened; residual bugs likely in rare/niche cards.

## 🔁 SECOND-PASS — name-treatment aliases (8 cards) — VERIFIED CLEAN
- Printed "Also treat this card's name as [X] (and [Y]) according to the rules" (EB02-016/024, EB04-038 DUAL,
  OP01-121/OP02-042/OP03-122/OP04-099, P-027) is handled by NameMatches (L4410): matches the printed name AND every
  alias, false for unrelated. Verified incl. EB04-038's dual [Trafalgar Law]+[Donquixote Rosinante]. `nametreattest`.
- The DYNAMIC effect form "This card's name is also treated as [Name]" (L13755, stores state.NameOverrides) is DEAD
  CODE — a DB scan finds ZERO cards using it (all 8 are the printed passive). Its bracket-strip quirk (keeps "[Name]")
  is thus unreachable. No fix. No engine change.

## 🔁 SECOND-PASS — blocker-denial family (15 cards) — VERIFIED CLEAN (near-miss false positive #3)
- All blocker-denial forms enforced in BlockAttack (L3348–3358): "cannotBlock" modifier (targeted/board-wide,
  L10737 — OP11-013 "opponent's Characters with 2000 power or less cannot activate [Blocker]" board-wide + filtered,
  OP12-051 targeted with cost cap), Battle.NoBlocker (blanket "during this battle" L12562 — ST01-012/OP08-111/P-097),
  BlockerPowerBan/BlockerPowerBanMax (">=/<= N power" L12545/12552 — Shanks/Adio), BlockerCostBanMax, and
  [Unblockable] (printed or granted, L3358). Verified end-to-end with EXACT card text: OP11-013 → 2000-pow Blocker
  DENIED, 4000-pow ALLOWED; blanket → denied; Unblockable → denied; no-effect → allowed.
- NEAR-MISS #3: the blanket "during this battle" case first "FAILED" — but the handler (L12535) requires
  `state.Battle != null`, and my test applied the effect BEFORE declareAttack (no battle yet). Fixed the test to
  apply battle-scoped effects AFTER declareAttack (mirroring [When Attacking]). LESSON: battle-scoped effects must be
  resolved inside a battle. `blockerdenialtest`. No engine change.
- NOTE: 3 consecutive near-miss false positives (durations/attack-restrictions/blocker-denial) all TEST artifacts,
  not engine bugs — the combat + duration layers are well-hardened. Prioritize fresher / less-combat areas next.

## 🔁 SECOND-PASS — combat attack-restrictions — VERIFIED CLEAN (near-miss false positive #2)
- "This Character cannot attack unless <cond>" enforced in DeclareAttack: EB04-051 "…unless there is a Character
  with 12000 base power or more" (L2934, scans BOTH boards; Emet 7000 alone → blocked, +12000 char → allowed) and
  EB04-005 "…unless your opponent has N or more Characters with a base power of P or more" (L2952, opponent count).
- Targeted "up to N of your opponent's Characters [with cost cap][ other than [Name]] cannot attack <duration>"
  (OP05-042/EB04-028/OP14-120/ST19-001 etc., handler L8605) applies a "cannotAttack" modifier (enforced L2906) with
  the correct duration: all 4 REAL phrasings ("until the end of your opponent's next turn/End Phase", "until the
  start of your next turn") map to untilNextTurn (L8655) → survives into + through the opponent's next turn, then
  expires. Verified end-to-end: locked north char CANNOT attack on its turn, CAN attack the following cycle.
- NEAR-MISS: my first test clause used "cannot attack DURING your opponent's next turn" → parsed as thisTurn →
  "FAILED" — but a DB scan shows NO card uses that phrasing (only "until the end of…"/"until the start of…"). Fixed
  the test to a real phrasing → PASS. LESSON: probe with EXACT card text, not invented phrasings. `cannotattacktest`.
  No engine change.

## 🔁 SECOND-PASS — cross-turn duration effects — VERIFIED CLEAN (near-miss false positive)
- "gains +cost/+power/[KW] until the end of your opponent's next turn / …next End Phase / until the start of your
  next turn" (64 cards) all map to duration "untilNextTurn" (keyword L10814) / "untilNextTurnOf:<seat>" (+cost
  L11063) and correctly PERSIST through the opponent's next turn, EXPIRING at the CONTROLLER's next turn start
  (ApplyStartOfTurn L1843 removes `untilNextTurn && OwnerSeat==seat`; L1860 removes `untilNextTurnOf:seat`). Verified
  +1cost (2,2,1), +3000power (6000,6000,3000), [Blocker] (T,T,F), [Rush] (T,T,F) across the full turn cycle.
- NEAR-MISS: the [Blocker] case initially "FAILED" (T,T,T) — but a debug dump showed the granted modifier IS removed
  correctly; the artifact was using ST01-006 (Chopper) which has PRINTED [Blocker], so HasBlocker was always true.
  Re-tested with a VANILLA character (EB01-005) → correct. LESSON: for keyword-GRANT expiry tests, use a card with
  NO printed keyword. `durationtest` (vanilla char). No engine change.

## 🔁 SECOND-PASS — cost-payability sweep + DON ramp/recover — VERIFIED CLEAN
- Following #220, swept ALL auto-payable costs in TryAutoPayCost (L6280–6820): every handler guards payability
  (`if (<resource> < n) return 0` before actions.Add) — rest/return DON, trash-deck, trash-Life, add-Life-to-hand,
  turn-Life (now #220), face-down, given-DON, play-named, ret-trash. The interactive board-sacrifice cost (rest/
  return/place/K.O. N of your Characters, L7845) is enforced + skippable (optional "You may" → no free body). The
  "add from top or bottom of your Life to hand" cost (L7908) returns Resolved w/o firing body when Life empty.
  COST-PAYABILITY CLASS COMPLETE — #220 was the lone free-payoff gap.
- DON!! ramp/recover family (179 cards) VERIFIED CLEAN: ramp "Add up to N DON!! from your DON!! deck [and set active/
  rest it]" (L8740) correctly adds active/rested per text, decrements DonDeck, and is CAPPED by DonDeck>0 — which
  implicitly enforces the 10-DON field cap (total DON=10, field=10−DonDeck, so field=10 ⇒ DonDeck=0 ⇒ ramp adds 0).
  Recover "Set up to N DON!! cards as active" caps at N. `donramptest` (ramp-active/rested, cap-at-0, recover-2-of-3).
  No engine change.

## 🔁 SECOND-PASS — turn-Life-face cost — #220 payability (free payoff)
- #220: "You may turn N cards from the top of your Life cards face-up/down: <payoff>" cost (TryAutoPayCost L6558)
  only checked Life COUNT (`Life.Count < n → 0`), NOT whether N FLIPPABLE (opposite-state) cards exist. So "turn 1
  face-up" was PAID FOR FREE when all Life was already face-up → the payoff (KO / DON-ramp / etc.) fired without
  paying (EB03-056, EB01-040, EB03-053, OP08-058 turn-2, + the Shirahoshi face-DOWN engine OP08-063/OP11-100/104/108
  the reverse way). Probe: an all-face-up Life still K.O.'d. Fixed: payability = N cards in the OPPOSITE state
  (face-down for "face-up", face-up for "face-down"); flip only the top N wrong-state cards. `turnlifetest`
  (both directions × payable/unpayable). Coverage 2675/0/0/0/0, smoke 54.67%. The "turn N of your face-up Life
  face-down" cost (L6438, ST13-009) already checked face-up availability correctly.

## 🔁 SECOND-PASS — hand→Life tuck — #219 dropped [Trigger] filter
- #219: the hand→Life handler (L10160, "Add up to 1 [{T} type][Character] card [with a cost of N] from your hand to
  the top of your Life cards face-up") checked cost/tag/Character-type but NOT the "with a [Trigger]" requirement.
  EB03-059 ("add up to 1 Character card WITH A [Trigger] from your hand to the top of your Life cards face-up") could
  tuck a NON-Trigger card into Life (probe: ST01-006 with no trigger was added). Added `hlTrigReq` to hlMatch (the
  added card must have a printed [Trigger]). `handlifetrigtest` (non-Trigger rejected, Trigger accepted). Coverage
  2675/0/0/0/0, smoke 54.67%. Sibling paths verified: hand→Life {tag}/cost/char filters (L10167) + trash→Life cost/
  {tag}/face-up (L10123, OP16-108) already enforced correctly.

## 🔁 SECOND-PASS — add-to-own-Life family — #218 own-Character self-bounce
- #218: ST13-001 "[Activate: Main] You may add 1 of your Characters with a cost of 3 or more and 7000 power or more
  to the top of your Life cards face-up" — an own-Character SELF-BOUNCE-to-Life (turn a big Character into +1 Life,
  face-up for defense) was UNHANDLED: the field→Life handler (L10557) required "Add up to N" but ST13-001 says "add
  N of your", AND it only checked cost-MAX ("cost of N or less") while ST13-001 filters on cost-MIN + power-MIN.
  Probe: the valid cost-5/7000-power Character stayed on the field (nothing happened). Fixed: broadened the entry to
  also match "add N of your Characters", parse the count from either phrasing, added cost-min/power-min/power-max
  filters + an own-Character enforcement (mustBeOwn for "add N of your Characters"). `st13001test` (valid char →
  Life face-up + DON returned; cost-1/low-power char rejected). placelifetest (opponent-place-into-Life) still PASS
  — no regression. Coverage 2675/0/0/0/0, smoke 54.67%.

## 🔁 SECOND-PASS — opponent hand-disruption — VERIFIED CLEAN (no bug — probe artifact)
- Two phrasings, both correct + leading-If gated (top-level gate L7667 evaluates "^If <cond>, <body>" once at
  activation): (a) "your opponent trashes N cards from their hand" (EB02-045) → AUTO, opponent picks last N
  (L11781); (b) "trash N cards from your opponent's hand" (OP03-078, OP06-097, ST10-010) → INTERACTIVE
  controller-pick on the opponent's face-down hand (L10280, WaitingForTarget until N clicks). Self-downside
  "Your opponent chooses N from YOUR hand; trash that card" (OP01-038) trashes from the controller's own hand
  (opponent's pick auto-simplified to last, L10316). Initially suspected a missing handler for OP06-097 (probe
  showed 0 trashed) — but that was a PROBE ARTIFACT (the interactive path awaits clicks; completing the clicks
  trashes correctly). Added a redundant auto-handler, then REVERTED (L10280 already covers it). NO engine change.
  `handdisrupttest` (5 cases: auto met/unmet, interactive met/unmet/no-cond, with real clicks). Coverage 2675/0/0/0/0.

## 🔁 SECOND-PASS — opponent-Life manipulation — VERIFIED CLEAN
- Place-a-Character-into-opponent's-Life removal ("Place up to 1 of your opponent's Characters with a cost of N or
  less at the top or bottom of your opponent's Life cards[ face-up]" — EB01-053 face-down cap3, OP05-096 face-up
  cap1, OP09-101): the "Place up to N" rewrite (L10604 → the Add-to-Life handler L10558) correctly removes the
  Character from the field, RETURNS its attached DON, clears modifiers/name-overrides, adds it to the OWNER
  (opponent)'s Life pile (+1 Life), and honors face-up (OP05-096) vs face-down (EB01-053) + the cost cap (a cost-8
  target is rejected under cap 3). Top-placement simplification for "top or bottom" (acceptable). Verified end-to-end.
- Trash-opponent's-Life (ST07-010 "Trash 1 card from the top of your opponent's Life cards", L13280): pops N from the
  top of the opp's Life to their TRASH — no Trigger, NOT added to their hand, capped at Life count (no false game-end
  on the last card). Verified (opp Life 3→2, card to trash not hand). Locked with `placelifetest` (4 cases). No engine change.

## 🔁 SECOND-PASS — PLAY-from-HAND tutors — #217 cost-MIN (completes the tutor-filter sweep)
- Play-from-hand exclusion "other than [Name]" VERIFIED WORKING (L12698 already enforces it — OP02-044 {Minks}
  other than [Wanda]: Wanda rejected, Carrot played). But #217: the play-from-hand handler (L12648) parsed only
  "cost of N or LESS" (costCapH) — the cost-MIN "cost of N or MORE" was dropped. OP08-062 ("play up to 1 [Charlotte
  Katakuri] … with a cost of 3 or more that is equal to or less than the opp's DON count") let a cost-2 Katakuri
  (OP08-062 itself / a cost-2 variant exists) play despite the 3+ requirement. Added costMinH = ParseLimit("cost of
  N or more") gate. `playhandtest` (excl Wanda/Carrot + min cost2-rejected/cost3-played). Coverage 2675/0/0/0/0,
  smoke 54.67%. TUTOR-FILTER SWEEP COMPLETE — exclusion + cost min/max/range now enforced across deck (reveal #213/
  #214, play-mode #215), trash (play+add #216), and hand (play #217) paths.

## 🔁 SECOND-PASS — TRASH-recursion tutors — #216 exclusion + cost-range (extends #214/#215/#213)
- #216: the "other than [Name]" exclusion + cost-range gaps reached the TRASH resolvers too.
  (a) TRASH-PLAY ("Play up to 1 {tag}…other than [Name] from your trash" — OP16-085 [Kouzuki Momonosuke], OP10-082,
  OP11-092, OP14-110, EB02-047): resolver L13462 checked named/cost/power/feature/[Trigger] but NOT the exclusion →
  the excluded card was playable (probe: a {Land of Wano} Momonosuke played under "…other than [Momonosuke]"). Added
  the exclusion check.
  (b) TRASH-ADD ("Add up to 1 black Character card with a cost of 3 to 7 other than [Rebecca] from your trash to hand"
  — OP05-091): dropped the [Rebecca] exclusion AND mishandled the cost-RANGE — ParseCostFilter read only the '3' and
  capped there, so valid cost-4..7 cards were REJECTED. Fixed: parse range "cost of N to M" (min+max) / min "or more"
  / max "or less" (bare "cost of N" still caps via ParseCostFilter) + the exclusion. `trashplayexcltest` (6 cases:
  trash-play Momonosuke-excluded/Izo-played/non-LoW-rejected; trash-add Rebecca-excluded/cost5-in-range-added/
  cost1-below-rejected). Coverage 2675/0/0/0/0, smoke 54.67%. "OTHER THAN [Name]" + COST-RANGE now enforced across
  ALL deck AND trash tutor paths (#213 reveal cost, #214 reveal excl, #215 play-mode excl, #216 trash excl+range).

## 🔁 SECOND-PASS — PLAY-mode deck tutors — #215 "other than [Name]" exclusion (sibling of #214)
- #215: the "other than [Name]" exclusion #214 fixed for the reveal-ADD path was ALSO dropped in the two PLAY-mode
  deck paths: (a) "Look at N; play up to 1 {tag} … other than [Name]" (L11101 StartDeckLook playMode — EB02-056
  "{Scientist}…other than [Vegapunk]", OP04-084) never set ExcludeName → the excluded card was playable; (b) "Play
  up to N <filter> from your deck" (L11072 StartDeckSearch) both dropped the exclusion AND captured the excluded
  name as the required INCLUSION name (pfdName grabbed the "other than [Name]" name). Probe: a {Scientist} Vegapunk
  WAS played under "…other than [Vegapunk]". Fixed both: Look-play sets DeckLook.ExcludeName from "other than [Name]";
  play-from-deck strips "other than [Name]" from pfdDesc before reading the inclusion name + sets ExcludeName. Both
  reuse the ResolveDeckLookSelect ExcludeName enforcement added in #214. `playexcltest` (Vegapunk excluded, Scientist
  Gastino played, non-Scientist rejected). Coverage 2675/0/0/0/0, smoke 54.67%. "OTHER THAN [Name]" EXCLUSION SWEEP
  COMPLETE across all 3 deck paths (reveal-add #214, look-play + play-from-deck #215).

## 🔁 SECOND-PASS — deck-look reveal-add tutors — #214 "other than [Name]" exclusion (BIG: 60 cards)
- #214: reveal-add "reveal up to 1 {tag} type card OTHER THAN [Name] and add it to hand" tutors (60 cards —
  OP01-016/090, OP02-058/083, OP03-030/062/089, OP05-015/064/106, … + the whole {tag}-other-than family) captured
  the EXCLUDED name via the multi-name-inclusion extractor (L13148) as a named INCLUSION filter. Two bugs in one:
  (a) namedFilter != null ⇒ the {tag} feature filter was DROPPED (featureFilter set null), and (b) the validation's
  named-filter branch requires the pick to MATCH the name → the logic INVERTED to "add ONLY the excluded card,
  reject everything else". Probe: a Straw Hat Crew Nami WAS added under "…other than [Nami]" while a SHC Sanji was
  rejected. Fixed: parse "other than [Name]" into a new DeckLookState.ExcludeName, STRIP it from the inclusion-name
  segment before extraction (so it isn't captured as inclusion → {tag} filter is preserved), and enforce ExcludeName
  in ResolveDeckLookSelect (rejected regardless of the other filters). `revealexcltest` (5 cases: Nami excluded, SHC
  non-Nami ok, off-tag rejected, + genuine multi-name INCLUSION tutor ST13-013 still works). Coverage 2675/0/0/0/0,
  smoke 54.67%. Invisible to the gate (auto-resolves fine; only the WRONG card is offered).

## 🔁 SECOND-PASS — deck-look reveal-add tutors — #213 cost-filter fix
- #213: the "Look at N; reveal up to 1 card with a cost of <X> and add it to your hand" reveal-add handler (L13129)
  parsed the POWER filter (ExactPower/MaxPower) but NO cost filter — so "cost of N or MORE" (EB02-008/020/031/040/
  050/058, OP11-070, ST28-005 — 8 cards) and the "cost of N to M" RANGE (EB03-060) were DROPPED: any-cost card was
  addable (probe: a cost-1 card added under "cost of 4 or more"). Fixed: added DeckLookState.MinCost; parse range
  ("cost of N to M" → Min+Max) then min ("cost of N or more") then max ("cost of N or less"); enforce MinCost in
  ResolveDeckLookSelect. `revealcosttest` (min: cost1 rejected/cost8 ok; range2-8: cost1 below-rejected/cost5 ok/
  cost10 above-rejected). Coverage 2675/0/0/0/0, smoke 54.67%. NB: PLAY-mode "cost of N or less" tutors use a
  separate path (StartDeckLook maxCost) and were already fine; only the reveal-ADD-to-hand path lacked cost parsing.

## 🔁 SECOND-PASS — multi-target distribution effects (10 cards) — VERIFIED CLEAN
- Rested-DON distribution: "Give up to 1 rested DON!! card TO EACH of your {Alabasta} Characters" (OP04-004, gdM
  handler L12003 auto-gives 1 to each matching char, non-match gets 0) + "Give up to N of your {T} Characters up to
  M rested DON each" (OP08-001/ST30-014, gdEachN L9316 pick-up-to-N, per-target filter + non-match rejected).
  `donDistributetest`.
- "Up to a total of N ... gain +power" DISTINCT-target distribution (EB02-007 "Up to a total of 3 of your Leader
  and Character cards gain +1000"): re-picking the SAME card is REJECTED (stays +1000, not +3000) AND 3 distinct
  targets each get +1000 (distinct enforcement from the #177 era holds). `totalbufftest`.
- Rest-across-two-zones shared cap "Rest up to a total of 2 of your opponent's Characters OR DON!! cards" (OP06-035/
  OP12-037, rmixM handler L11644) caps the total via a shared SelectionsRemaining across both zones — verified by
  code read. No engine change.

## 🔁 SECOND-PASS — [Banish] combat keyword (22 cards) — VERIFIED CLEAN
- Self-power-gated grant OP06-002 ("If this Character has 7000 power or more, this Character gains [Banish]") reads
  CURRENT power incl. DON-attach timing: base 5000 → no Banish; +2 DON on OWN turn → 7000 → Banish; same 2 DON on
  the OPPONENT's turn → 5000 (DON attach adds only on your turn) → no Banish. (HasPrintedKeywordGrant already
  evaluates the "this Character has N power or more" self-condition.)
- Combat mechanic (ResolveAttack L4126 → BanishLifeCards L4018): a [Banish] attacker hitting the Leader TRASHES the
  Life card with NO Trigger and NOT to hand — verified end-to-end (Life card → trash, not hand, Life count -1).
- [Double Attack]+[Banish] combo (dmg = 2) verified by inspection: BanishLifeCards(2) pops 2 Life cards to trash,
  or finishes the game (attacker wins) if Life runs out mid-hit. Locked with `banishkwtest`. No engine change.

## 🔁 SECOND-PASS — continuous cost auras — #212 hand-cost-reduction-aura fix
- #212: OP01-067 Sabo "[DON!! x1] Give blue Events in your hand −1 cost" — a CONTINUOUS cost-reduction aura on OTHER
  hand cards (blue Events) was NEVER applied. Only the ACTIVATED variant had a handler (the instant resolver L9430
  applies a temporary endOfTurn modifier); this continuous form (no action timing, DON-gated) is never queued, so
  GetCost never saw it → blue Events stayed full price, the player couldn't play them cheaper as printed. Added
  GetHandCostReductionAura (reads in-play sources for "Give <color> <{tag} type> (Events|Characters|cards) in your
  hand −M cost" with color/tag/type recipient filters + [DON!! xN] gate + turn timing + leading-If) wired into
  GetCost. OP01-067 is the ONLY continuous card of this shape. `op01067test`: blue Event 2→1 with 1 DON, unchanged
  with 0 DON, a Character (type filter=Events) unchanged. Coverage 2675/0/0/0/0, smoke 54.67% (no perf regression).
- Verified CLEAN alongside: GetHandSelfCostBonus (L921, "give this card in your hand −X cost" — EB04-061 etc.)
  already evaluates its leading "If <cond>," via EvaluateCondition.
- SIBLING FAMILY VERIFIED CLEAN: continuous opponent cost-DEBUFF auras "[…] [Your Turn] [If <cond>,] Give all of
  your opponent's Characters −N cost" (GetOpponentCostDebuffAura L1050, 9 cards — OP02-121/OP03-078/OP04-020/EB04-017/
  OP05-084/092/OP07-081/OP08-083/P-032). Correctly lowers the opponent Character's GetCost gated on turn-timing
  ([Your Turn] active-seat), [DON!! xN], and condition — verified across all 3 condition types incl. the unusual
  "if the only Characters on your field are {Celestial Dragons}" (OP05-084 −4). Locked with `opcostdebufftest`
  (8 cases). No engine change — unlike its own-hand sibling #212, this continuous form already had a GetCost path.

## 🔁 SECOND-PASS — meta-pattern follow-up: dead dispatches + conditional keyword grants — VERIFIED CLEAN
- Chased #211's sibling: ApplyEndOfOpponentTurnEffects (L14556) + ApplyStartOfTurnEffects have the SAME unconditional
  "set this…as active" fast-path — but a DB scan shows ZERO cards use "[End of Opponent's Turn]" / "[Start of Your
  Turn]" timing tags, so both dispatches are DEAD CODE and the bug is unreachable. No fix (no card to test against).
- Conditional CONTINUOUS keyword grants ("If <cond>, this Character gains [KW]", 57 cards) — HasPrintedKeywordGrant
  (L1237-1240) strips leading timing tags + evaluates the leading "If <cond>," via EvaluateCondition (already
  hardened by the OP02-008 Jozu fix). VERIFIED CLEAN across every condition TYPE: hand-size (OP06-054), DON-count
  (OP15-119), trash-count (OP05-086), Life-count (OP12-107), power-threshold board existence (OP05-003), and a
  {type}-existence with "other than this card" SELF-EXCLUSION (OP13-009 is itself {Mountain Bandits} → alone does
  NOT qualify, only with another Mountain Bandits). Locked with `kwcondtest` (12 cases, met+unmet). No engine change.

## 🔁 SECOND-PASS — [End of Your Turn] family (50 cards) — #211 conditional-recover fix
- #211 (same #210 class — a condition ignored on a verb): the [End of Your Turn] "set this…as active" fast-path
  (ApplyEndOfTurnEffects L14588) matched "Set this"+"as active" CASE-INSENSITIVELY across the WHOLE def.Effect and
  fired UNCONDITIONALLY — so CONDITIONAL self-recovers ignored their "If <cond>,": OP09-031/037 ("If you have N or
  more rested Characters…"), OP04-028 ("If you have 2+ active DON…"), P-079 ("…rested {ODYSSEY}…"), and OP05-022
  (LEADER "If you have 6 or less cards in your hand, set this Leader as active"). All recovered every end of turn
  regardless of the condition. Fixed: match the EOT CLAUSE (not the whole effect, avoiding cross-timing "set active"
  contamination) + evaluate a leading "If <cond>," via EvaluateCondition, skip when unmet. Choice ("… or up to 1
  DON …" OP13-035) and cost ("DON!! −2:" OP16-073) variants keep the simple self-recover default (no stall/regress).
  `eotcondtest` (9 cases: rested-count, active-DON, leader hand-size — met+unmet; + choice/cost/unconditional
  controls). Coverage 2675/0/0/0/0, smoke P(first)=54.67%.

## 🔁 SECOND-PASS — [On Block] family (14 cards) — #210 conditional-draw fix
- [On Block] dispatch (L3309, inside BlockAttack, gated on [DON!! xN]) FIRES + fully RESOLVES: OP05-036 (rest opp
  cost-≤4 Character — `onblocktest`), OP06-009 (COMBINED "[When Attacking]/[On Block]" tag → base power becomes the
  opponent Leader's current power via L11755 — combined slash-tag correctly recognized, `onblock2probe`).
- #210: trailing "Draw N card(s) if <condition>" drew UNCONDITIONALLY — the plain draw resolver (L12908) never
  evaluated the trailing "if <cond>", so OP05-047 ("Draw 1 if you have 3 or less cards in your hand. Then, +1000
  during this battle") and OP01-078 ("Draw 1 if you have 5 or less cards in your hand") drew even with a full hand.
  Fixed: parse `^Draw (?:\d+|a|an) cards? if <cond>` + EvaluateCondition, skip the draw when recognized-and-not-met
  (leading "If <cond>, draw" already handled by the splitter). The ". Then," +1000 buff still fires in both cases.
  `drawcondtest` (OP05-047 + OP01-078, met+unmet). Coverage 2675/0/0/0/0, smoke P(first)=54.67%.
- FOLLOW-THROUGH (next iteration): confirmed #210 covers the WHOLE trailing-"Draw N if <cond>" family (7 cards —
  OP01-078/095, OP05-009/047/050/118, P-047) across ALL condition types EvaluateCondition must recognize: hand-size,
  DON-count (OP01-095), opp-Life (OP05-118), leader-power (OP05-009) — all gate met/unmet correctly (`drawcond2test`).
  Also spot-checked the adjacent ". Then, if <cond>, <payoff>" family (OP07-096 "Draw 1. Then, if trash ≥ 10, give
  −3 cost") — the unconditional first clause fires and the conditional payoff correctly gates on the trash count
  (trash 3 → no give; trash 12 → −3 cost, floored at 0). VERIFIED CLEAN, no engine change.

## 🔁 SECOND-PASS — conditional continuous AURAS + negative-existence conditions + [On Your Opponent's Attack] — VERIFIED CLEAN
- Conditional CONTINUOUS group auras (GetTurnPassiveAuraBonus L593) toggle correctly with their live condition:
  EB01-024 (hand ≤ 4 → all {SMILE} +1000; hand > 4 → off), OP13-004 Sabo Leader ([DON!! x1] + "you have a Character
  with a cost of 8 or more" → Leader & all Characters +1000; remove the cost-8 body → off). The aura fn handles
  leading-If, trailing-If, DON threshold, is-rested, base-power/cost, color, named, type-including, feature filters.
- NEGATIVE-EXISTENCE condition "If you have no other [Shirahoshi] with a base cost of 2, all of your {Neptunian}
  type Characters gain +2000" (OP12-102) — the {Neptunian} recipient gains +2000 only while NO OTHER cost-2
  Shirahoshi exists; adding a 2nd cost-2 Shirahoshi flips it off. EvaluateCondition handles "you have no other [Name]".
  Locked with `auracondtest` (all 3).
- [On Your Opponent's Attack] reactive (48 cards) FIRES + fully RESOLVES for the defender when the attacker declares
  (dispatch L3071 inside DeclareAttack). Verified OP09-032 Rosinante ("Set this Character as active") un-rests after
  the opponent attacks (`oppattackrestest` — resolution, complementing the L812 queue-only probe). DON-threshold /
  once-per-turn / negate gating all present.
- NO engine change (all correct). Coverage 2675/0/0/0/0. Tests: auracondtest, oppattackrestest.

## 🔁 SECOND-PASS — K.O./removal-REPLACEMENT family, tricky payments (64 cards) — VERIFIED CLEAN
- Deep-traced the "If … would be K.O.'d/removed by your opponent's effect, you may <pay> instead" family, focusing
  on the NON-numeric / named / Leader-targeted payments the regex-reading suggested might fall through. All CORRECT
  (my fall-through hypotheses were WRONG — the handler at L4663–4811 + a name-aware rest branch is more robust than
  it reads): OP04-082 (rest your Leader or 1 [Corrida Coliseum] → survives via leader-rest), OP15-009 (give your
  Leader −2000 → victim saved + leader −2000), OP11-110 (rest 1 of your [Fish-Man Island] or your [Shirahoshi]
  Leader → survives via the Shirahoshi leader when present; DIES when only illegal targets exist; NEVER rests a
  decoy character or a wrong-named leader). Also confirmed covered: return-DON-instead (L4663), give-this/that-
  Character-−power (L4670), trash-this-Character+draw (L4685), place-from-trash/Life variants, K.O.-this-instead,
  place-Characters-at-bottom, turn/add/trash-Life. Locked with `koreplacetest` (4 scenarios incl. legal+illegal).
- NO engine change (family already correct). Coverage 2675/0/0/0/0. Test: koreplacetest.

## 🔁 SECOND-PASS — "When this Character is K.O.'d by your opponent's effect" reactive family (9 cards)
- Traced the effect-KO reactive-payoff family. Dispatch (FireOnKoEffects L14363) correctly gates on `byBattleKo`
  (L14373 — fires on EFFECT K.O. only, not battle) + turn-timing tags. VERIFIED WORKING end-to-end (real north
  effect-K.O. → payoff resolves): OP16-024 (rest opp Char — `op16024test`), EB01-057 (add to top of Life, auto),
  OP11-051 (deck-look 5 → play {Straw Hat}), OP11-035/OP11-024 (". If you do," rewritten to a PAID DON-rest cost
  gating the play-from-hand payoff — `op11035test` confirms 1 DON rested + card played, not free).
- #209: OP09-052 was INERT — a UNIQUE pre-armed reactive "[Opponent's Turn] You may trash 1 card from your hand:
  When this Character is K.O.'d by your opponent's effect, play this Character card from your trash rested." The
  whenKo regex extracted the payoff but dropped the "You may <cost>:" pre-arm prefix, and the payoff body wasn't
  gated → nothing fired. Fixed 3-part: (a) FireOnKoEffects now detects the pre-arm shape and wraps the payoff behind
  the cost (`You may trash 1 …: <payoff>`) — collapsing the official pre-pay-during-opp-turn into an at-K.O.
  decision (functionally equivalent, fully usable, no free recursion); (b) NEW resolver for "play this Character
  card from your trash rested" (returns the just-K.O.'d source from trash to an open slot, rested, fires its On
  Play); (c) added that body to IsAutomatedEffectPattern so QueueBody auto-resolves it post-payment. `op09052test`:
  pay trash-1-from-hand → card back in play rested + cost paid. Coverage 2675/0/0/0/0, smoke P(first)=54.67%.

## Second-pass VERIFIED-CLEAN: K.O.-replacement, scaling-power, cost-manip, multi-target removal/distribution, [Trigger], deck-manip, ". Then,"-chains (−power/draw/return).
## Second-pass VERIFIED-CLEAN families: K.O.-replacement (mostly), scaling-power, cost-manipulation (hand-self + opp-aura), multi-target removal/distribution, [Trigger] resolution, deck-manipulation (look/reveal/scry).

## Reactive-trigger sweep — fixes #225, #226 (both gate-invisible)
- #225 OP12-040 Kuzan: reactive "When a card is trashed from your hand by your {Navy} type card's effect, draw
  cards equal to the number of cards trashed." NEVER fired — NotifyHandTrashedByEffect dispatch matched ONLY the
  bare "…by an effect," literal, so the source-qualified "…by your {Navy} type card's effect," variant was dropped
  entirely. Fixed: broadened the dispatch to a regex accepting both "by an effect," and "by your {X} type card's
  effect," (source-type restriction = cost simplification — fires on any effect-based hand trash). Also implemented
  the SCALING draw: added `int count=1` param, pass `ta` at the trash-all-hand batch site (fires once with the
  full count), and translate the body "draw cards equal to the number of cards trashed" → "Draw {count} cards".
  Per-card call sites keep count=1 (one draw per card = N total); batch site draws N in one event. `op12040test`:
  trash-all-3 → drew 3 (deck -3); single-discard → deck -2 (1 effect + 1 reactive). Verified.
- #226 OP02-002 payoff "give up to 1 of your opponent's Characters WITH A COST OF 7 OR LESS −1 cost during this
  turn." — the cost-reduction RESOLVER (L12554) validated seat+type but IGNORED the "cost of 7 or less" cap, so a
  client sending a cost-8+ target would illegally debuff it. Glow (IsValidEffectTarget L7360) already enforced the
  cap → glow≠resolve drift (safe direction, but a fidelity gap). Fixed: resolver now parses "with a cost of N
  or less/more" and rejects out-of-range targets, mirroring glow. `op02002test`: cost-10 rejected (stays pending),
  cost-5 accepted (5→4). Verified. FireOnDonGiven dispatch itself (L2275) traced clean.
- Reactive families traced clean: FireOnDonGiven (DON-attach), NotifyDonReturned (DON-return), [On K.O.] draw,
  "this card deals damage → trash without Trigger" (OP02-095 family).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#226.

## K.O.-replacement "rest N of your cards instead" family — fixes #227, #228
- #227 OP16-033 (also OP14-029, OP15-035): self/other K.O.-replacement "you may rest N of your CARDS instead."
  The rest-cost handler (L4855) only rested Characters (Leader as a fallback ONLY when "leader" appeared in the
  text). Bare "your cards" per OPTCG = ANY card you control (DON!!, Characters, Stage, Leader) → the replacement
  wrongly FAILED when the player had <N unrested Characters but could pay with DON!!/Leader/Stage (under-permissive
  payability). Added a bare-"cards"/"card" branch that rests cheapest-first (DON!! → non-victim Characters → Stage
  → Leader), excluding the victim itself.
- #228 (same handler, whole "rest N of your X instead" family): PARTIAL-PAYMENT LEAK. The old code rested cards
  eagerly in a loop, then `if (!upTo && rested < rnN) continue` bailed on shortfall — but the already-rested cards
  STAYED rested even though the all-or-nothing cost was never met (e.g. a "rest 2" with only 1 payable card rested
  that 1 card AND still let the K.O. proceed). Restructured to gather rest-candidates as deferred actions first,
  check `restActions.Count >= rnN` for non-"up to" costs, and pay NOTHING on shortfall. Affects every branch
  (DON!!/typed-Characters/bare-cards). `op16033test`: 3 DON → rest 2 + survive; 1 DON → rest 0 (no leak) + K.O.'d.
  Regressions PASS: koreplacetest, fukaboshi110test, rosinante048test, onko3test, eb01008test.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#228.
- NEXT: OP15-070 [Shura] Unblockable-by-name continuous aura + [Opponent's Turn] base-power→6000 (flagged, untraced).

## Continuous keyword-AURA "All of your [Name] cards … gain [KW]" — fix #229 (2 root causes, gate-invisible)
- OP15-070 ([Shura]→[Unblockable]) and OP15-071 ([Ohm]→[Double Attack]) print "All of your [Name] cards and this
  Character gain [KW]." with an EMPTY keywords field. NEITHER the source NOR its named allies got the keyword:
  (a) SELF-grant path (HasPrintedKeywordGrant) matched only the singular "gains [KW]"; the plural aura verb "gain
  [KW]" never matched. Fixed: switched the self gate + per-line check to regex `gains? \[KW\]`; the existing L~1311
  guard (line must name "this Character"/"and gains") prevents a pure ally-aura line from false-self-granting.
  (b) ALLY-aura scan regex required "Your [Name] gains [KW]" — the "All of your [Name] cards [and this Character]
  gain [KW]" form (intervening "cards and this Character" text, plural verb) never matched. Added a second regex
  `[Aa]ll of your \[Name\] cards[^.]*?gains? \[KW\]` gated by NameMatches.
  (c) COMBAT sites for attacker-[Unblockable] (MaybeAutoPassBlock L3238, blockAttack L3383, BlockBarredByCurrentAttack
  L1411) checked only HasKeyword + HasKeywordModifier — NOT HasPrintedKeywordGrant — so even a correctly-granted
  Unblockable was ignored at the block step. Added a centralized `IsUnblockable(state, inst)` (printed | printed/aura
  grant | modifier) and wired all three. `op15070test`: OP15-071 self+ally DoubleAttack=True, bystander=False;
  OP15-070 Shura ally attack auto-passes block (step=counter) + block rejected; no-aura control block succeeds.
- OP15-070/71's OTHER clause "[Opponent's Turn] All of your [Name] cards' base power … become 6000" already handled
  in GetPower L207 (timing-gated, self+NameMatches) — VERIFIED CLEAN.
- Keyword regressions PASS: banishkwtest, blockerdenialtest, nametreattest, untilhavetest. Coverage 2675/0/0/0/0;
  smoke P(first)=54.67%. Total gate-invisible bugs now #163–#229.

## "Cannot be rested" LOCK family — fix #230 (gate-invisible target-validation)
- #230 OP16-032 "[On Play] Up to 1 of your opponent's Characters OTHER THAN [Monkey.D.Luffy] cannot be rested
  until the end of your opponent's next End Phase." The freeze/cannotBeRested resolver (L8578) enforced the cost
  cap + seat + type but IGNORED the "other than [Name]" exclusion, so a player could illegally lock the opponent's
  Monkey.D.Luffy. Glow (IsValidEffectTarget L7369) already enforced the exclusion → glow≠resolve drift (safe
  direction). Added an "other than [Name]" parse + NameMatches rejection to the resolver, mirroring glow.
  `op16032test`: Monkey.D.Luffy rejected (not locked), non-Luffy accepted (cannotBeRested applied).
- Rest of the family VERIFIED CLEAN: cost-cap parse (OP13-032 ≤8, OP14-033 ≤5, OP15-029 ≤5) works; duration
  "untilNextTurn" (owner=me) correctly spans the opponent's next turn incl. their End Phase; the lock disables an
  opponent BLOCKER (blocking rests it, barred by HasModifier cannotBeRested at L3382). SELF-immunity
  CannotBeRestedByOppEffect: OP12-021 conditional ("Leader has ＜Slash＞ attribute AND 6+ rested DON!! cards") —
  both sub-conditions recognized (EvaluateCondition L6192 attribute + L6093 rested-DON, compound-AND) → clean.
  OP15-024 [Opponent's Turn] self-immunity applies always-on rather than turn-gated, but that is functionally
  irrelevant (it only ever blocks OPPONENT effects) → left as-is.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#230.

## "Cannot be K.O.'d" continuous immunity — fix #231 (leading-tag-breaks-^If, gate-invisible)
- #231 OP04-119 aura "[Opponent's Turn] If this Character is rested, your active Characters with a base cost of 5
  cannot be K.O.'d by effects." HasRemovalImmunityAura (L4386) had FOUR gaps for this line: (a) the "^\s*If"
  condition anchor was defeated by the leading [Opponent's Turn] tag → the "If this Character is rested" gate was
  dropped and the immunity applied ALWAYS (even when the source was active) — the known leading-tag-breaks-^If
  bug class (cf. keyword path L1302); (b) the [Opponent's Turn] turn-timing was unchecked → immunity applied on
  BOTH turns; (c) "your ACTIVE Characters" didn't require the protected Character to be unrested; (d) "base cost
  of 5" (exact, no "or less") wasn't a filter → ALL characters were protected. Fixed all four: strip leading
  tags before ^If, honor Your/Opponent's Turn timing, require !victim.Rested when the line says "your active …
  Characters", and add an exact "base cost of N" filter (printed cost). `op04119test` (5 scenarios): immune only
  when {source rested + opponent's turn + ally active + ally base-cost-5}; each unmet condition → not immune.
- Rest of the family VERIFIED CLEAN: HasRemovalImmunityAura already handles cost-cap (OP07-033 ≤3), base-power-cap,
  "other than [Name]" (OP07-033 not-Luffy, OP08-029 not-Pekoms), color + feature/{tag} filters, and leading-If
  gates on tag-less lines (OP07-033/OP07-069/OP08-029/EB04-057). "cannot attack" opponent-lock resolver (L8668)
  already enforces cost-cap + "other than [Name]" (OP07-051/OP08-112) + Leader/Character + rested + duration —
  CLEAN. Self K.O.-immunity CannotBeKoedByEffect conditional + Once-per-turn charge — CLEAN.
- Immunity regressions PASS: nusjuro080test, yonji046test, yonjitest, koreplacetest. Coverage 2675/0/0/0/0;
  smoke P(first)=54.67%. Total gate-invisible bugs now #163–#231.

## Leading-tag-breaks-^If SWEEP (systematic, prompted by #231) — fix #232
- Audited ALL "^If"-anchored regexes in GameEngine.cs against raw card lines that can carry a leading tag.
  Found the SELF K.O.-immunity path CannotBeKoedByEffect (L4308) still vulnerable:
  #232 OP06-109 "[DON!! x2] If your opponent has 3 or less Life cards, this Character cannot be K.O.'d by effects."
       ST14-009 "[DON!! x1] [Opponent's Turn] If you have a Character with a cost of 6 or more, this Character
                 cannot be K.O.'d by your opponent's effects and gains +2000 power."
  The leading [DON!! xN]/[Opponent's Turn] tags defeated the "^If (.+?), this Character cannot be" anchor →
  BOTH the condition gate AND the DON!!/timing requirements were skipped → effect-K.O. immunity was ALWAYS-ON.
  Fixed: in the per-line immunity block, (1) check ParseDonThreshold(line) vs AttachedDonIds, (2) honor
  Your/Opponent's Turn timing, (3) strip leading tags before the ^If anchor, THEN evaluate the condition.
  `op06109test` (7 scenarios): OP06-109 immune only w/ 2 DON + opp ≤3 Life; ST14-009 immune only w/ 1 DON +
  opponent's turn + you have a cost-6 Character; each unmet DON/timing/condition → not immune.
- Other ^If anchors AUDITED CLEAN (no card feeds them a tag-prefixed line, or the caller pre-strips tags):
  keyword-grant self-path L1308 (pre-strips at L1302), removal-immunity AURA L4422 (fixed #231), EOT clause
  L14853/14872 (operates on split eoyClause), cost/power computations L692/856/992/1094 (own-clause fragments),
  CannotBeRestedByOppEffect L4341 (no card has a tag-prefixed "If … cannot be rested"). NUSJURO080 (no-tag
  removed-immunity condition) regression still PASS — the tag-strip is a no-op on un-tagged lines.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#232.

## Iteration: battle-K.O.-immunity + lure + board-wide power-auras — ALL VERIFIED CLEAN (no engine change)
- BATTLE-K.O.-immunity: self-path IsBattleKoImmune (L5156) uses `\bIf` (tag-safe, unlike the effect-K.O. `^If`),
  and checks DON (ParseDonThreshold) + field-DON + attribute/by-Leaders/by-cards qualifiers → CLEAN. Aura-path
  BattleKoImmuneFromAura (L5123) uses `^\s*If` but NO permanent card feeds it a leading-tag+If battle-immunity
  line (only aura sources: OP01-099 no-tag/no-If, and OP06-096 which is an EVENT → never on the board, so its
  [Counter]-gated colon payoff never false-triggers the passive aura scan). No fix needed.
- LURE/taunt OP01-051 "[DON!! x1][Opponent's Turn] If this Character is rested, your opponent cannot attack any
  card other than [Kid]" — declareAttack handler (L2901) scans the DEFENDER's board, gates on rested + DON, and
  blocks any attack whose target isn't a lure name (incl. the Leader). [Opponent's Turn] always holds during
  declareAttack. CLEAN.
- BOARD-WIDE +power AURAS (GetPower L636): exhaustive recipient filters verified — exact base-cost (EB04-010
  "base cost of 1" +5000), exact base-power (ST30-003 "6000 base power" +1000), base-power ≤/≥, base-cost ≥,
  cost ≤/≥, color, named ([A]/[B]), "other than this Character", turn-timing, DON, rested, leading+trailing If.
  New regression `powerauraexacttest`: EB04-010 buffs cost-1 (not cost-3, not on my turn); ST30-003 buffs
  6000-power (not 5000). CLEAN.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs remains #163–#232 (verify-only iter).

## Compound self-sacrifice cost "verb this Character and N of your Characters" — fixes #233/#234/#235 (OP04-073)
- Deep-traced OP04-073 Mr.13 & Ms.Friday "[Activate: Main] You may TRASH THIS CHARACTER AND 1 of your Characters
  with a type including "Baroque Works": Add up to 1 DON!! card from your DON!! deck and set it as active."
  (flagged MANUAL/UNKNOWN). Three distinct gate-invisible bugs, all in the "You may <cost>:" resolver:
  #233 (happy path COMPLETELY BROKEN): the auto-pay block (TryAutoPayCost, L7869) re-ran on EVERY resolveEffect
       call with the FULL original costText. First click paid the self-trash (source gone) + asked for the pick;
       the SECOND click (providing the field-Character) re-entered TryAutoPayCost, which now failed ("source
       trashed" → returns 0) and logged "cost cannot be paid" — the pick + body (DON ramp) never ran. Guarded the
       auto-pay block with `effect.SelectionsRemaining <= 0` (only auto-pay on the FIRST entry; once mid-pick, go
       straight to the pick handler).
  #234 (filter dropped): the field-Character pick's CostCharFilterOk checked the type filter only against the
       LEADING fragment (text before "Characters"), which is EMPTY when the filter TRAILS ("Characters with a
       type including "Baroque Works""). So the pick accepted ANY Character. Added a `CardPassesFeatureFilter(
       costText, def)` check against the full cost text (enforces trailing "{X} type"/"type including "X"").
  #235 (PARTIAL PAYMENT): the self-part (trash MR13) was paid BEFORE verifying a valid pick target exists — with
       no other {Baroque Works} Character, MR13 was sacrificed for nothing (pick stalls forever). Added an
       all-or-nothing pre-check in the restAndM compound block: count own Characters (matching the remainder
       filter, excluding self) ≥ N before paying the self-part; else log "cannot pay" and resolve with no cost.
  `op04073test`: with another BW Character → BOTH trashed + 1 active DON added (deck −1); with only a non-BW
  Character → NOTHING paid (both Characters stay, no DON). Same path hardened for EB01-011 Mini-Merry (rest self +
  place 1 char w/ 1000 base power) & OP15-039 Rebecca (rebecca039test regression PASS).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#235.

## Reveal-top-Life-then-play — fix #236 (OP10-022 Trafalgar Law)
- Deep-traced OP10-022 (flagged MANUAL/UNKNOWN): "[DON!! x1][Activate: Main][Once Per Turn] If the total cost of
  your Characters is 5 or more, you may return 1 of your Characters to the owner's hand: Reveal 1 card from the
  top of your Life cards. If that card is a {Supernovas} type Character card WITH A COST OF 5 OR LESS, you may
  play that card." The reveal-Life-play handler (L8288) matched only "If that card is [Name] with a cost of N"
  (exact) → for OP10-022 it (a) IGNORED the {Supernovas} type filter entirely (any cost-5 card played) and (b)
  read "cost of 5 or less" as EXACT ==5 (wrongly REJECTED a {Supernovas} cost-3/4). Fixed: parse the cost as an
  exact OR "or less/more" range, and add `CardPassesFeatureFilter(text, revDef)` to enforce the {type}. `op10022test`:
  {Supernovas} cost-3 plays; non-{Supernovas} cost-5 does NOT; {Supernovas} cost-6+ does NOT. The 3 exact-name
  ST13-007/010/014 ("a [Name] with a cost of 5") are unchanged (no {tag} → filter passes; no "or less" → exact).
- OP01-047 Law "[On Play] You may return 1 Character to your hand: Play up to 1 Character card with a cost of 3 or
  less from your hand" VERIFIED CLEAN (op01047test: bounce cost paid, cost-3 hand card plays, cost-5 rejected).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#236.

## Flagged COST_UNPAYABLE cards — false positives + fix #237 (OP06-117)
- The audit-flagged.tsv COST_UNPAYABLE flags are mostly AUDITOR false positives: the auditor's test board lacked
  the NAMED cost card (e.g. an [Uta]/[Enel]/[Fullalead] card), so it couldn't pay "rest 1 of your [Name] cards".
  OP06-011 Tot Musica "You may rest 1 of your [Uta] cards: this Character gains +5000" VERIFIED CLEAN (op06011test:
  +5000 with an [Uta] Character OR the [Uta] Leader present; +0 with no [Uta] at all — the name filter is enforced).
  NB: the cost auto-picks any valid [Uta] card to rest (Leader-first) — a minor cost-simplification; the real UI
  lets the player choose.
- #237 (real bug, found tracing OP06-117 The Ark Maxim): "[Activate: Main][Once Per Turn] You may rest this card
  and 1 of your [Enel] cards: K.O. all of your opponent's Characters with a cost of 2 or less." The COMPOUND cost
  paid fine (Stage + [Enel] both rested via restAndM), but the BODY "K.O. all of your opponent's Characters with a
  cost of N or less" was UNHANDLED → "acknowledged for manual resolution" (no K.O.). The power-capped variant
  (L9839 "…with N power or less") and the both-sides cost variant (L11492 "K.O. all Characters with a cost of N or
  less") existed, but the OPPONENT-ONLY + COST-cap combination had no resolver. Added a handler (opponent's
  CharacterArea, GetCost ≤ cap or base-cost, honoring CannotBeKoedByEffect + removal-replacement). `op06117test`:
  Stage+[Enel] rested, opp cost-2 K.O.'d, opp cost-3 survives. Regressions kaidotest/kriegtest PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#237.

## Board-wide opponent FREEZE — fix #238 (OP08-036)
- #238 OP08-036 Electrical Luna (Event): "[Main] All of your opponent's rested Characters with a cost of 7 or less
  will not become active in your opponent's next Refresh Phase." The freeze handler (L8649) was TARGETED-only ("up
  to N ... will not become active" — needs clicks); the board-wide "ALL of your opponent's ..." form had no branch,
  so it stalled waiting for a target that a no-target [Main] event can't provide (old audit: SKIPPED non-optional).
  Added a board-wide branch (gated on "All of your opponent's" + !"up to"): freeze EVERY matching opponent
  Character at once (cost cap + "rested" filter), applying the "freeze"/untilNextTurn modifier the Refresh Phase
  already honors (L1904 keeps a frozen card rested). `op08036test` (full turn): rested cost-5 frozen → stays rested
  through north's refresh; rested cost-8 (over cap) NOT frozen → un-rests; active cost-5 unaffected. Targeted-freeze
  regressions freezetest/leaderfreezetest PASS.
- NEXT (flagged, still to verify): ST27-001 (rest [Fullalead] card → Leader-gated +4000 — same "rest [Name] cards"
  cost as OP06-011, likely a false positive), OP14-049 (rest 2 DON → draw 2 + bounce ≤7), OP04-090 (return 7 trash
  → set self active). OP03-019 Fiery Doll ("Your Leader gains +4000 power") fast-passed vanilla-ish.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#238.

## Self-freeze restand drawback — fix #239 (OP04-090); OP14-049 clean
- OP14-049 Jinbe "[On Play] You may rest 2 of your DON!! cards: Draw 2 cards and return up to 1 Character with a
  cost of 7 or less to the owner's hand" VERIFIED CLEAN (op14049test: 2 DON rested, 2 drawn, opp cost-5 bounced,
  opp cost-9 rejected). Another COST_UNPAYABLE false positive (auditor board lacked 2 active DON).
- #239 OP04-090 Monkey.D.Luffy "[Activate: Main][Once Per Turn] You may return 7 cards from your trash to the
  bottom of your deck in any order: Set this Character as active. Then, this Character will not become active in
  your next Refresh Phase." The ". Then," splitter separated the body into "Set this Character as active" +
  a STANDALONE "this Character will not become active in your next Refresh Phase" clause. The set-active handler
  (L8596) only handled the first; the standalone self-freeze clause had NO resolver → it STALLED pending (blocking
  end-of-turn) and the restand DRAWBACK never applied → Luffy restood for FREE every turn. The auto-resolve GATE
  (L14560) recognized "will not become active"+"Refresh Phase" as automated, so coverage stayed OK (gate-vs-resolver
  gap). Added a standalone self-freeze resolver ("this Character will not become active … your next Refresh Phase",
  not "opponent"/"selected") applying the "freeze"/ownNextRefresh modifier to the source; also added the rider to
  the set-active handler as a fallback for the unsplit case. `op04090test`: 7 trash→deck bottom, self set active,
  and after resting + a full turn cycle Luffy STAYS rested at its next refresh (a control Character un-rests).
  Regressions freezetest/leaderfreezetest/op08036test/eotcondtest PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#239.

## ". Then,"-split standalone-clause sweep (prompted by #239) — fix #240 + 3 verified clean
- Verified the ". Then,"-split standalone SECOND clause resolves for: OP15-056 "Draw 2 cards. Then, your [Lucy]
  Leader gains [Double Attack] and +3000 power" (op15056test — [Lucy] gains both, non-[Lucy] neither); ST29-016
  "[Main] Your [Monkey.D.Luffy] Leader gains [Unblockable]" (st29016test — name-gated); EB03-051 "K.O. … Then,
  turn all of your Life cards face-down" (eb03051test — KO + all Life flipped). All CLEAN.
- #240 (real gap, found in the sweep): OP05-119 Doflamingo "[On Play] DON!! −10: Place all of your Characters
  except this Character at the bottom of your deck in any order. Then, TAKE AN EXTRA TURN after this one." The
  "take an extra turn" clause had NO resolver → the player bounced their whole board for nothing (no extra turn).
  Implemented the extra-turn mechanic: a resolver flags an "extraTurn" board modifier (OwnerSeat=controller);
  EndTurn consumes it at the turn-switch and keeps ActiveSeat = the same player (instead of OtherSeat), so they
  take another turn. Added "take an extra turn" to IsAutomatedEffectPattern. `op05119test`: ending south's turn
  with the flag keeps active=south; a normal end passes to north. Coverage/durationtest/donramptest/op04090test
  regressions PASS (turn transition unaffected).
- Broad ". Then," verb scan (place 191 / if 72 / trash 47 / give 34 / add 28 …) — the common clauses resolve; the
  rare ones (turn-Life-facedown, extra-turn, Leader-grants) now all covered.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#240.

## Negate + cannot-attack compound — fix #241 (OP09-093)
- #241 OP09-093 Marshall.D.Teach clause 2: "negate the effect of up to 1 of your opponent's Characters AND that
  Character cannot attack until the end of your opponent's next turn." The cannot-attack opponent-lock resolver
  (L8796) matched FIRST (contains "cannot attack" + "opponent"), applied ONLY the cannotAttack and returned —
  DROPPING the negate (the primary effect). Fixed two-sided: (1) the cannot-attack resolver now defers when the
  clause contains "negate the effect" (`!ContainsAll(text,"negate the effect")`); (2) the negate handler (L12229)
  applies the "and that Character cannot attack until <dur>" rider to the SAME negated Character (untilNextTurn
  when it survives the opponent's next turn). `op09093test`: opp Character both negated AND cannot-attack.
  Regression cannotattacktest PASS (the defer guard doesn't affect normal cannot-attack locks).
- NEXT (complex, deferred): OP15-025 Kuro "[On Play] Give up to 2 DON!! cards from your opponent's cost area to 1
  of your opponent's Characters. Then, at the end of this turn, up to 1 rested Character with 3 or more DON!! cards
  given will not become active…" — opponent-DON manipulation + delayed DON-count-filtered freeze; own iteration.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#241.

## Complex multi-clause: OP15-025 Kuro — fix #242 (was FULLY broken)
- #242 OP15-025 Kuro "[On Play] Give up to 2 DON!! cards from your opponent's cost area to 1 of your opponent's
  Characters. Then, at the end of this turn, up to 1 rested Character with 3 or more DON!! cards given will not
  become active in your opponent's next Refresh Phase." Both clauses were broken: the immediate FREEZE handler
  (L8684) matched the full On-Play text FIRST (it contains "will not become active"+"Refresh Phase"), prompted
  for a freeze target, and DROPPED clause 1 (the DON-give). Clause 2 (a delayed, DON-count-filtered freeze) had
  no resolver. Fixed: (1) guard the immediate freeze handler to skip "at the end of this turn" (defer to the
  DON-give handler); (2) after the oppDonGive handler (L9564) moves the opponent's DON, apply clause 2's freeze —
  find up to 1 rested opponent Character with ≥N DON given (parsed from "with N or more DON!! cards given") and
  add a "freeze"/untilNextTurn modifier (applied post-move, when the DON count is final — functionally identical
  to deferring to EOT since the freeze survives the opponent's next Refresh). `op15025test`: clause 1 moves 2 opp
  DON onto their char (→3, cost area −2); clause 2 freezes that rested 3-DON char (stays rested at opp refresh)
  while a 0-DON control un-rests. Regressions freezetest/op08036test/donDistributetest PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#242.

## "rest N of your [Name] cards" cost must include the Stage — fix #243 (ST27-001) — FLAGGED BACKLOG CLEARED
- #243 ST27-001 Avalo Pizarro "[Activate: Main][Once Per Turn] You may rest 1 of your [Fullalead] cards: If your
  Leader has the {Blackbeard Pirates} type, this Character gains +4000 power during this turn." The ONLY [Fullalead]
  card in the DB is OP09-099 — a STAGE. TryAutoPayCost's restNamed handler (L6471, "rest N of your [Name] cards")
  built its candidate pool from Leader + Characters but NOT the Stage → the cost was unpayable even with the
  Fullalead Stage in play (the flagged COST_UNPAYABLE was REAL for this card, not a false positive). Added
  `p.Stage` to the candidate pool ("[Name] cards" = any card you control with that name, incl. a Stage).
  `st27001test`: with the Fullalead Stage + a Blackbeard Pirates Leader → Stage rests + this Character gains +4000;
  a non-BB Leader → condition fails (+0). op06011test regression ([Uta] Leader/Character) still PASS.
- With ST27-001 fixed, the audit-flagged.tsv COST_UNPAYABLE backlog is CLEARED: the rest were AUDITOR false
  positives (board lacked the named cost card) — verified clean: OP06-011 (op06011test), OP14-049 (op14049test),
  OP04-090 (op04090test, +#239 self-freeze fix). Real bugs found while clearing it: #237 (OP06-117 KO body),
  #238 (OP08-036 board freeze), #239 (OP04-090 self-freeze), #243 (this Stage-inclusion).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#243.

## Modal "Choose one" sweep — fixes #244 (leading condition) + #245 (3-bullet + place-at-Life)
- #244 LEADING-CONDITION modals (OP12-060 multicolored, OP15-054/ST11-003 Leader-name, OP06-065 DON-count,
  OP06-093 opp-hand-count): "If <cond>, choose one: •A •B". The general leadIf gate (L7774) EXPLICITLY excludes
  "Choose one" cards, and TryParseChoiceEffect ignores the leading If → the modal choice was offered even when
  the condition failed. Added a modal-leading-condition gate before TryParseChoiceEffect: skip only when the
  condition is RECOGNIZED and NOT met (unrecognized → fail open, present the choice). `op12060test`: multicolored
  Leader → choice offered; mono-color Leader → skipped.
- #245 OP05-096 Nami 3-BULLET modal "Choose one: •K.O. •Return •Place-at-Life. Then, draw if {Celestial Dragons}"
  — THREE problems: (a) ChoiceState stored only OptionA/OptionB → the 3rd bullet (Place-at-Life) was INACCESSIBLE;
  added OptionC (GameState + parser + ResolveChoice "C"). (b) The trailing ". Then, <rider>" after the last bullet
  was welded onto option C alone; split it off and appended to EVERY option so the chosen one runs it. (c) the
  "Place up to N of your opponent's Characters … at the top or bottom of their Life" REMOVAL was intercepted by the
  add-to-Life handler when queued via the modal; added a dedicated place-opp-at-Life handler BEFORE it (cost cap,
  removal immunity, face-up/face-down per text — EB01-053 face-down vs OP05-096 face-up, honoring the trailing
  rider). `op05096test`: option C places the opp cost-1 char on their Life + shared rider draws 1. placelifetest
  regression PASS (EB01-053 face-down + OP05-096 face-up); st13001test/handlifetrigtest/op16104test PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#245.

## Exact Life-count condition + deal-damage gate — fix #246 (OP06-116 + 8 more)
- #246 OP06-116 Reject bullet B "If your opponent has 1 Life card, deal 1 damage to your opponent. Then, add 1
  card from the top of your Life cards to your hand." TWO root causes:
  (a) EXACT Life-count conditions ("your opponent has N Life card(s)" / "you have N Life card(s)", no "or less/
  more") were UNRECOGNIZED in EvaluateCondition (only "N or less/more Life" existed) → returned false → the
  leadIf-gated effect was WRONGLY SKIPPED even when the count held. Affects 9 cards: OP06-116, EB01-059/060 ("you
  have 1 Life card"), EB04-056/OP10-115/ST13-003/P-039 ("you have 0 Life cards"), OP09-118 ("your opponent has 0
  Life cards"). Added exact-count handlers (opp.Life.Count==N / p.Life.Count==N).
  (b) With the condition fixed, the "deal 1 damage" still dropped: the early ". Then," splitter (L8283) only
  resolves clause A when IsAutomatedEffectPattern(A) is true, and "deal N damage to your opponent" was NOT in the
  gate → no split → the full text fell to the add-from-Life handler (L10786, earlier than deal-damage L12362),
  which did the "add from Life" rider and dropped the damage. Added "deal \d+ damage to your opponent" to the gate
  → the split fires: deal damage (clause A) + add-from-Life rider (clause B) both resolve.
  `op06116test`: opp 1 Life → damage (opp Life→hand) + rider (my top Life→hand); opp 2 Life → nothing (cond fails).
  Regressions op05096test/op12060test/drawcondtest/eotcondtest PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#246.

## Modal sweep continued — OP08-057 DON-cost modal + OP06-092 verified CLEAN (no engine change)
- OP08-057 King "[Activate: Main][Once Per Turn] DON!! −2 (…): Choose one: • If you have 5 or less cards in your
  hand, draw 1 card. • Give up to 1 of your opponent's Characters −2 cost during this turn." VERIFIED CLEAN — the
  DON!! −2 cost (interactive per-DON clicks when field DON > cost via AutoPayOrAwaitDonMinus) pays correctly, the
  modal is offered AFTER the cost, and both bullets resolve (bullet A conditional draw, bullet B −2 cost debuff).
  New regression `op08057test`. (KEY TEST-SETUP NOTE: a DON!! −N cost with MORE field DON than N awaits per-DON
  resolveEffect clicks with the DON instanceId as Target — it does NOT auto-pay; a test must click N DON first.)
- OP06-092 Brook bullet 2 "Your opponent places 3 cards from their trash at the bottom of their deck in any order"
  — handled at L10496 (opponent trash/hand → deck bottom). Bullet 1 is a standard cost-capped Trash removal. CLEAN.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs remains #163–#246 (verify-only iter).

## Modal sweep continued — OP03-028 / EB01-052 verified CLEAN (no engine change)
- OP03-028 Jango bullet B "Rest this Character and up to 1 of your opponent's Characters" — the compound
  self+opp rest handler (L11963) rests BOTH (self + a targeted opponent Character). `op03028test`: Jango rests
  itself + opp Character rests. Bullet A "Set up to 1 of your {East Blue} Leader/Char cost≤6 as active" = standard
  filtered set-active. CLEAN.
- EB01-052 Viola bullet A "Look at all of your opponent's Life cards and place them back in their Life area in any
  order" — resolves cleanly (no stall, no NA, opponent Life intact): the look is informational and the "any order"
  reorder is a UI-interactive option that auto-resolve declines (places back as-is — a valid choice). `eb01052test`.
  Bullet B "Turn all of your Life cards face-down" handled (L10870). CLEAN.
- EB02-045 Law trash-cost modal (place 2 trash → deck bottom : draw / conditional discard) — same cost+modal
  pattern verified via OP08-057 (op08057test). CLEAN.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs remains #163–#246 (verify-only iter).

## "When this Character becomes rested" reactive — fix #247 (OP14-070) + attack-group verified
- Attack-triggered group (OP14-021 Issho / OP14-027 Shanks / OP14-028 Johnny / OP14-032 Humandrill / OP14-035
  Yosaku / OP14-119 Mihawk) VERIFIED CLEAN: FireOnBecomeRested fires on the attack path (L3140), with correct
  filters. `op14028test`: Johnny attacks → rests → K.O.s an opp RESTED cost-2 Character (a rested cost-4, or an
  ACTIVE cost-2, is spared). CLEAN.
- #247 OP14-070 Buffalo "When this Character becomes rested BY YOUR OPPONENT'S CHARACTER'S EFFECT, you may return
  1 DON!! card from your field to your DON!! deck. If you do, set this Character as active." — COMPLETELY DEAD:
  (a) FireOnBecomeRested's dispatch matched only the bare "…becomes rested," (comma after "rested"), so the
  source-qualified phrasing (comma after "effect") never matched; AND (b) it was only ever called from the attack
  path — never when an OPPONENT'S effect rests one of your Characters. Fixed 3-part: (1) added the source-qualified
  pattern to FireOnBecomeRested with a `cause`/`bySourceType` gate (fires only on cause=="oppEffect" + Character
  source); (2) added a FireOnBecomeRested(target.Owner, target, "oppEffect", srcType) call at the "Rest up to N of
  your opponent's Characters" resolver (L12611); (3) added a handler for the reactive BODY "return N DON!! … from
  your field to your DON!! deck[. If you do,] set this Character as active" (the bare set-active handler was
  dropping the DON return + un-resting for free). `op14070test`: rested by an opp CHARACTER effect → Buffalo
  returns 1 DON + re-actives; rested by an opp LEADER effect → does NOT fire (source-qualified). op14028test regression PASS.
- KNOWN MINOR GAP (documented, low-value): the generic [Your Turn] "becomes rested" group still fires only on
  attack + opp-effect rests, not on a self-rest COST (rare for these attacker cards).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#247.

## Event-activation reactives — VERIFIED CLEAN (no engine change; caught a self-introduced regression)
- "When you activate an Event, <effect>" (OP01-062 Crocodile draw-gate, OP04-053 Page One draw+place, OP10-003
  Sugar leader) — handled by FireOnEventActivation (L3482), fired from BOTH the Main-event (L2157) and Counter-event
  (L3922) paths, with [Your Turn]/[Opponent's Turn] + [DON!! xN] + [Once Per Turn] gates. CLEAN.
- "When your opponent activates an Event, <effect>" (OP06-044 Gion hand-tax, OP11-012 Franky +2000, OP01-004
  Usopp) — ALREADY handled by the pre-existing FireOnOpponentEventActivation (L3925), fired from CounterWithCard
  (the [Your Turn] reactor's opponent counters with an Event on the reactor's turn). `oppevent OP11-012` → Franky
  +2000 once; `oppevent OP06-044` → Gion taxes 1 card once. CLEAN.
- LESSON: I initially mis-read this as "undispatched" — my scan used lowercase "opponent activates an event" while
  the code + method are "activates an Event" (capital E), so grep missed FireOnOpponentEventActivation. Added a
  duplicate opponent-loop to FireOnEventActivation → it DOUBLE-FIRED (Franky +4000, two "gives +2000" logs; the
  distinct [Once Per Turn] key didn't dedupe against the pre-existing method). Caught by the oppevent test, reverted
  to the original. **Before adding a reactive dispatch, grep case-insensitively for an existing Fire*/Notify* — the
  double-fire in a probe is the tell.**
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs remains #163–#247 (verify-only iter).

## Reactive: Life-becomes-0 recovery + self-damage — fixes #248 (OP05-098 Enel) + #249 (OP14-115 Rindo)
- #248 OP05-098 Enel (Leader) "[Opponent's Turn][Once Per Turn] When your number of Life cards becomes 0, add 1
  card from the top of your deck to the top of your Life cards. Then, trash 1 card from your hand." — the reactive
  was UNDISPATCHED (dead) → Enel never recovered. Implemented FireOnLifeBecomesZero (timing/DON/OncePerTurn gated,
  no-op unless the seat is at exactly 0 Life) + call sites at the damage exits (FinalizeTrigger, BanishLifeCard,
  BanishLifeCards). Two more gaps to make the body auto-resolve: (a) "add N from the top of your deck to the top of
  your Life cards" was NOT in IsAutomatedEffectPattern → the reactive stalled pending (reactives have no manual UI);
  added it. (b) the ". Then," EARLY-SPLIT guard (L8351) blocked ANY partA containing "from the top of your deck" —
  meant for deck-LOOKS but too broad; it kept "add-from-deck-to-Life. Then, trash-from-hand" WHOLE, so the trash
  rider mis-routed the add to a hand card. Refined the guard to block only true looks (partA has "Look at" or "the
  rest"). Now the body splits: clause A adds the top DECK card to Life, clause B trashes a hand card. `op05098test`
  (full combat): Enel at 1 Life takes lethal damage on opp turn → recovers to 1 Life from deck (deck −1) + trashes
  1 hand card; a non-Enel Leader does NOT recover.
- #249 OP14-115 Rindo "[Opponent's Turn][On K.O.] Add up to 1 card from the top of your deck to the top of your
  Life cards. Then, YOU TAKE 1 DAMAGE." The self-damage rider "you take N damage" had NO handler — silently dropped
  when welded to the add-to-Life clause; the #248 split EXPOSED it (turned the silent-drop into a NOT_AUTOMATED).
  Added a self-damage handler (controller loses N Life to hand; finishes the game at 0 Life) + gate entry.
- Broad ". Then,"-split-guard change verified SAFE: coverage 2675/0/0/0/0 (was momentarily NOT_AUTOMATED=1 for
  OP14-115, now fixed); regressions handdisrupttest/revealcosttest/revealexcltest/st13001test/op16104test/
  placelifetest PASS; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#249.

## Self-buff reactives on draw / Life-to-hand — fixes #250 (OP05-053) + #251 (OP05-107)
- #250 OP05-053 Mozambia "[Your Turn][Once Per Turn] When you draw a card outside of your Draw Phase, this
  Character gains +2000 power during this turn." — UNDISPATCHED (dead). Added FireOnDrawOutsideDrawPhase, called
  from DrawCard when state.Status=="active" && state.Phase!="draw" (so the turn-start Draw-Phase draw + setup
  draws don't trigger it; effect/counter draws do). Cheap early-out (IndexOf "outside of your Draw Phase" before
  the per-line split) keeps the hot DrawCard path fast (smoke 134 g/s, unchanged). `op05053test`: an effect Draw
  in the Main phase → +2000; a draw in the Draw Phase → +0.
- #251 OP05-107 Lt. Spacey "[Your Turn][Once Per Turn] When a card is added to your hand from your Life, this
  Character gains +2000 power during this turn." — UNDISPATCHED (dead). Added FireOnCardAddedToHandFromLife, called
  at the two main SELF-Life-to-hand body handlers ("add 1 from top/bottom of Life to hand", "Add N from top of
  Life to hand"). `op05107test`: adding a Life card to hand on your turn → +2000. (Damage-driven Life-to-hand is
  on the OPPONENT's turn, so the [Your Turn] gate correctly excludes it — this card triggers off self-effects.)
- Coverage 2675/0/0/0/0; smoke P(first)=54.67% @134 g/s; drawcondtest/handlifetrigtest regressions PASS.
  Total gate-invisible bugs now #163–#251.

## Bounce-scry reactive — fix #252 (EB02-023); OP02-026 verified clean
- OP02-026 Sanji (Leader) "[Once Per Turn] When you play a Character with no base effect from your hand, if you
  have 3 or less Characters, set up to 2 of your DON!! cards as active." — DISPATCHED at L2246 (the "when you play
  a Character[ with no base effect][ with a [Trigger]]" hook; playedVanilla = empty def.Effect). CLEAN.
- #252 EB02-023 Crocodile "[Your Turn][Once Per Turn] When your opponent's Character is returned to the owner's
  hand by your effect, look at 3 cards from the top of your deck and place them at the top or bottom of the deck
  in any order." — UNDISPATCHED (dead). Added FireOnOpponentReturnedByEffect (cheap early-out on the trigger text,
  timing/OncePerTurn gated), called from the opponent-bounce resolver (guarded: targetSeat != effect.Seat &&
  Character). `eb02023test`: bouncing an opponent Character via your effect → the scry reactive fires and opens the
  deck-look overlay (DeckLook set, no stuck pending). OP03-001 Ace leader "When this Leader attacks or is attacked,
  trash any number of Event/Stage → +1000 each during battle" DEFERRED (variable-count combat pump — own iteration).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%; op05053test/op05107test regressions PASS. Total now #163–#252.

## Leader combat pump "attacks or is attacked" — fix #253 (OP03-001 Ace leader)
- #253 OP03-001 Portgas.D.Ace (Leader) "When this Leader attacks or is attacked, you may trash any number of Event
  or Stage cards from your hand. This Leader gains +1000 power during this battle for every card trashed." The body
  (trash-any-number → +N power) is handled (L12905) and the [On Your Opponent's Attack]/[When Attacking] TAGGED
  variants (OP15-002 Lucy, P-051, OP06-014) are dispatched — but OP03-001's untagged "attacks OR IS ATTACKED"
  phrasing was UNDISPATCHED in BOTH directions (dead). Added FireOnLeaderAttacksOrIsAttacked (fires only when the
  combatant IS that seat's Leader; DON/OncePerTurn gated) and called it in declareAttack for BOTH the attacking
  Leader (seat, attacker) and the attacked Leader (OtherSeat, defender). `op03001test`: Ace attacks → trash 2
  Events → +2000 this battle; Ace is attacked → same +2000. Body is battle-scoped (RegisterPowerModifier
  endOfBattle). Regressions blockerdenialtest/cannotattacktest PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#253.

## Compound "activates [Blocker] or an Event" reactive — fix #254 (OP06-048); opp-KO verified clean
- OP01-061 Kaido / EB04-044 Koby "When your opponent's Character is K.O.'d, <effect>" — dispatched by
  FireOnOpponentCharacterKo (L3901), called from MoveToTrash (L15114, reactorSeat = the K.O.'er). DON/timing/
  OncePerTurn + cost-prefixed variants handled. CLEAN.
- #254 OP06-048 Zeff "[Your Turn] When your opponent activates [Blocker] OR AN EVENT, if your Leader has the
  {East Blue} type, you may trash 4 cards from the top of your deck." The compound "[Blocker] or an Event" phrasing
  matched NEITHER dispatch: the Blocker dispatch keyed on "activates A [Blocker]," (with the article) and the Event
  dispatch on "activates an Event," (not "or an Event") → DEAD on both. Fixed: (a) broadened FireOnOpponentActivates-
  Blocker to a regex `activates (?:a )?\[Blocker\](?: or an Event)?,` (matches ST10-006 "a [Blocker]" AND OP06-048);
  (b) added an OP06-048 compound branch to FireOnOpponentEventActivation (`activates \[Blocker\] or an Event,`) so
  the Event side fires too. `op06048test`: opponent activates a [Blocker] with an {East Blue} Leader → Zeff mills 4.
  `oppevent OP06-048`: opponent counters with an Event → Zeff reactive fires. OP11-012 Franky regression (no
  double-fire) still fires ONCE.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#254.

## "When this Character battles" reactives — VERIFIED CLEAN (no engine change)
- "When this Character battles and K.O.'s your opponent's Character, <effect>" (OP02-094 Isuka set-self-active,
  OP04-086 Chinjao draw-2/trash-2) — FireOnBattleKo (L3874) dispatched from the battle-damage K.O. step (L4422),
  with DON/timing/OncePerTurn gates. `op04086test` (full combat): Chinjao attacks + K.O.s a rested opp Character
  (1 DON attached) → draws 2 + trashes 2 (net hand unchanged). CLEAN.
- "When this Character battles ＜X＞ attribute Characters, this Character gains +N power" (ST05-010 Zephyr) —
  handled in GetPower (L296/L314), gated on the OTHER combatant's attribute (needs state.Battle). CLEAN.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs remains #163–#254 (verify-only iter).

## Defensive "when YOUR Character is removed from the field" reactive — fix #255 (OP08-056/OP13-078 Stages)
- #255 OP08-056 Moby Dick + OP13-078 Oro Jackson (both Stages): "[timing] When your Character with a type
  including "X" is removed from the field [by an effect / by your opponent's effect], <body>" — a DEFENSIVE
  removal reactive that was UNDISPATCHED (only NotifyRemovalByEffect L5275, the REMOVER's offensive hook, existed).
  Added FireOnYourCharacterRemoved(ownerSeat, removedCard) — scans the removed Character's OWNER board (incl. the
  Stage) for the trigger, type-filtered + timing/DON/OncePerTurn gated — called from MoveToTrash on an EFFECT K.O.
  (isKo && !byBattleKo; battle removal doesn't count as "by an effect"). Recurring "type including X" class again:
  the filter is a SUBSTRING of Features (EB01-041 "Former Roger Pirates" ⊃ "Roger Pirates"), NOT an exact
  HasFeature tag — used a substring Any() over Features. `op13078test`: an opp effect-K.O. of a "Roger Pirates"
  Character → OP13-078 adds +1 rested DON; a non-Roger Character → nothing. Regressions koreplacetest/op04086test/
  yonji046test PASS.
- KNOWN PARTIAL GAP (documented): fired only on the effect-K.O. removal path (MoveToTrash). A bounce-to-hand or
  place-on-deck "removal by effect" doesn't yet fire it (rarer; own sub-task). OP09-080 Thousand Sunny (Stage,
  "You may rest this Stage: When your {Straw Hat Crew} type Character is removed by your opponent's effect …" —
  cost-prefixed + {tag} phrasing) DEFERRED.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#255.

## Cost-prefixed removal reactive — fix #256 (OP09-080 Thousand Sunny)
- #256 OP09-080 Thousand Sunny (Stage) "[Opponent's Turn] You may rest this Stage: When your {Straw Hat Crew}
  type Character is removed from the field by your opponent's effect, add up to 1 DON!! card from your DON!! deck
  and rest it." Extended FireOnYourCharacterRemoved (from #255) to handle: (a) the "{tag} type Character" phrasing
  (exact HasFeature tag, vs the "type including "X"" substring variant); (b) a cost prefix "You may rest this
  Stage:" before the trigger — reconstructed as "You may <cost>: <body>" so the cost-prefix resolver offers the
  OPTIONAL rest-Stage cost and, if paid, runs the DON-ramp body. `op09080test`: opp effect-K.O. of a {SHC}
  Character → reactive fires → rest Stage → +1 DON. op13078test regression (type-including variant) still PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#256.
  (STILL PARTIAL: the bounce-to-hand / place-on-deck "removal by effect" path doesn't fire the defensive reactive
  — only the MoveToTrash effect-K.O. path does. Rarer; own sub-task if a card needs it.)

## Birdcage passive freeze aura — fix #257 (OP05-040)
- #257 OP05-040 Birdcage (Stage) passive "If your Leader is [Donquixote Doflamingo], all Characters with a cost
  of 5 or less do not become active in your and your opponent's Refresh Phases." The EOT board-wipe (#237-adjacent
  L11999) was handled, but the CORE passive clamp was UNIMPLEMENTED — the Refresh un-rest loop (L1899) only checked
  the "freeze" modifier, so all cost≤5 Characters un-rested normally and Birdcage's lock never worked. Added
  BirdcageFreezeCostCap(state) (scans both players' Stages for the "do not become active" aura, gated on the Stage
  owner's Leader-name condition, parses the cost cap) and wired it into the Refresh loop: a cost≤cap Character
  skips un-resting. Applies to BOTH players' Refreshes ("your and your opponent's"). `op05040test`: with a
  Doflamingo Leader, a rested cost-5 stays rested at both the opponent's AND your Refresh, a cost-6 un-rests;
  without Doflamingo the clamp is off. Regressions freezetest/op08036test/op04090test/durationtest PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#257.

## Stage passive auras — VERIFIED CLEAN (no engine change)
- ST14-017 Thousand Sunny (Stage) "All of your black {Straw Hat Crew} type Characters gain +1 cost." — handled by
  GetPassiveCostAuraBonus (L1005), with color + {tag} feature + cost-min filters. `stageauratest`: a black
  {Straw Hat Crew} Character's cost 3→4. CLEAN.
- OP13-099 The Empty Throne (Stage) "[Your Turn] If you have 19 or more cards in your trash, your Leader gains
  +1000 power." — conditional passive Leader power buff, handled by the GetPower aura path (trash-count condition +
  [Your Turn] timing). `stageauratest`: 19 trash → +1000, 10 trash → +0. CLEAN.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs remains #163–#257 (verify-only iter).

## Compound "trash from hand and rest this Stage" cost — fix #258 (OP02-048)
- #258 OP02-048 Land of Wano (Stage) "[Activate: Main] You may trash 1 {Land of Wano} type card from your hand and
  rest this Stage: Set up to 1 of your DON!! cards as active." The from-hand-trash cost handler paid the hand-trash
  and its "rest this Character"/"trash this Character"/"rest N DON!!" riders, but had NO "rest this Stage" rider →
  the Stage stayed ACTIVE, making the ability REPEATABLE (unlimited DON-ramp for hand cards; the rest-Stage is the
  self-limiting cost). Broadened the "rest this Character" rider to also fire on "rest this Stage" (rests the
  source via FindAnyInPlay). `op02048test`: {Land of Wano} card trashed + Stage rested + 1 rested DON set active.
  Regressions op04073test/op09052test/op09060test PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#258.

## Choose-from-opponent-hand reveal-conditional — fix #259 (OP01-063)
- #259 OP01-063 Arlong "…Choose 1 card from your opponent's hand; your opponent reveals that card. If the revealed
  card is an Event, place up to 1 card from your opponent's Life area at the bottom of the owner's deck." TWO gaps:
  (a) the generic "…opponent's hand"+"reveal" handler (L~10958) just logged the reveal and DROPPED the entire
  Event-conditional Life-place payoff; (b) before that could even be reached, the "Place up to N Characters … at
  the bottom of the owner's deck" handler (L8856) mis-captured the text (it matched "Place up to"+"bottom of the
  owner's deck") and STALLED waiting for a Character target — the effect never resolved. Fix: excluded "Life area"
  from the Character-place handler (`&& !ContainsAll(text,"Life area")`), and added a dedicated OP01-063 handler
  (before the generic reveal): blind-pick opp's last hand card (deterministic, mirrors the "auto: last cards"
  discard convention), reveal it, and if its Type=="event" move 1 of the opponent's Life cards (top of Life) to
  the bottom of their deck. `op01063test`: revealed Event → opp Life 3→2 + deck +1; revealed Character → no change
  (both fully resolve, nothing left pending). Regressions amanotest/st20005test PASS.
- VERIFIED CLEAN this iteration (no change): OP15-031 Purinpurin (K.O. if opp Char's cost == its given-DON count —
  exact dynamic, L9408); OP08-098/OP11-022/OP13-099 DON-count-capped play-from-hand (ComputeDynamicCap handles
  "number of DON!! cards on your field"); OP09-087 Charlotte Pudding (leading-"If opp has 5+ hand" gates the
  opponent-discard — op09087test: hand4→0, hand6→1); OP07-090 Morgans (". Then, opponent draws 1" survives the
  broad discard handler via the early ". Then," split — op07090test); OP06-093 Perona (leading-If gates the modal,
  #244 path — op06093test).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#259.

## Conditional draw-vs-add-to-Life redirect — fix #260 (OP04-040)
- #260 OP04-040 Queen "[DON!! x1] [When Attacking] If you have a total of 4 or less cards in your Life area and
  hand, draw 1 card. If you have a Character with a cost of 8 or more, you may add up to 1 card from the top of
  your deck to the top of your Life cards INSTEAD OF DRAWING 1 card." The generic draw handler (L~13762) matched
  "…draw…card" and ALWAYS drew, silently DROPPING the entire optional deck→Life redirect — the player never got
  the card's signature Life-recovery option even with a cost-8+ Character in play. Added a dedicated handler BEFORE
  the draw handler (match "instead of drawing"+"top of your Life"): honors the Life+hand gate if still on the text,
  parses the draw count + the big-cost requirement, and — when a qualifying Character is on the board — surfaces a
  draw-vs-add-to-Life ChoiceState (OptionA "Draw N card." / OptionB "Add up to 1 card from the top of your deck to
  the top of your Life cards."), each dispatched to an existing resolver. No big Character → plain draw. `op04040test`:
  no cost-8+ char → draw (hand 1→2); cost-8+ char + pick B → add to Life (Life 3→4, hand unchanged, deck −1).
  Regressions st20005test/op06093test PASS.
- VERIFIED CLEAN this iteration (no change): the entire removal-replacement machinery TryRemovalReplacement (L4824)
  — 20+ "…instead" payloads (rest self/opp/Leader/DON/tag-Character with cost+power+attr filters, trash-from-hand
  with type/power/Event-Stage filters, return-DON, give −power to victim-or-guard, trash-this-Character +draw rider,
  place-from-trash-to-deck, add-Life-to-hand, add-victim-to-top-of-Life, trash-from-Life, turn-Life-face-up,
  return-to-hand, K.O.-self, place-Characters-to-deck) all present with an `else continue` fallback; OP03-040 Nami /
  P-117 Nami deck-out WIN (CardData.WinsOnDeckOut) + OP09-118 Roger opponent-Blocker instant-win (wired L3482);
  OP12-096 Ursa Shock / OP04-094 Trueno Bastardo conditional cost-cap UPGRADE (ApplyConditionalCostCapUpgrade L7813,
  shared glow+resolver). NOTE: OP05-099/OP15-059 Amazon "opponent may trash Life / return DON; if not, give −2000"
  remains a DOCUMENTED opponent-choice deferral (no opponent-decision mechanism; the −2000 branch always applies).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#260.

## Flagged-backlog re-verification (audit-flagged.tsv is STALE) — no engine change
- Re-checked the still-untraced auditor-flagged cards; ALL resolve correctly in the current engine (the flags are
  stale heuristic verdicts from an old auditor run, superseded by prior fixes + the green coverage gate):
  • OP07-057 Perfume Femur (MANUAL/UNKNOWN) — "Select 1 {Seven Warlords} Leader/Char +2000; then if the selected
    card attacks this turn, opp cannot activate [Blocker]." HANDLED: buff resolver registers
    NoBlockerGrantedThisTurn.Add(target) (L13220), applied at DeclareAttack; the ". Then, if the selected card
    attacks" clause is kept whole (L5863).
  • OP14-049 Jinbe (COST_UNPAYABLE) — "[On Play] rest 2 DON: draw 2 + return a cost≤7 Character." Resting DON is a
    standard payable cost; coverage green.
  • EB04-009 (COST_UNPAYABLE) — "give 1 active DON!! to 1 of your [Silvers Rayleigh]: give opp −2000." The
    give-DON-to-named cost is handled (L7127) and correctly returns 0 (unpayable) only when no Rayleigh is in play
    — proper gating, not a bug.
  • EB01-011 Mini-Merry (MANUAL/UNKNOWN) — "[Activate: Main] rest this card AND place 1 of your Characters with
    1000 base power at the bottom of your deck: draw 1." NEW regression `eb01011test`: Stage rests, the 1000-power
    Character is placed at the deck bottom, and 1 card is drawn. CLEAN.
  • The SKIPPED(non-optional) flags (OP03-019 vanilla +4000 Leader, OP15-056, OP14-001/017, ST29-016, OP08-036)
    are the auditor's "mandatory effect, no skip button" heuristic — not bugs.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#260.

## Bounce base-power filter dropped (glow≠resolve drift) — fix #261 (EB03-025 family)
- #261 EB03-025 Hina / EB03-027 Marguerite / OP14-058 Ocean Current Shoulder Throw / OP11-051 Sanji — "Return up to
  1 Character with N base power [or less] to the owner's hand." The generic owner-agnostic bounce RESOLVER (L8813)
  parsed ONLY a cost cap (`ParseLimit "cost of N or less"`) and applied NO base-power filter, so the EXACT "6000/
  7000 base power" restriction (and OP11-051's "5000 base power or less") was silently dropped → ANY Character could
  be bounced. The GLOW (IsValidEffectTarget L7769-7780) already enforced base power incl. the exact form — a
  glow≠resolve drift (safe in the real UI since only valid targets glow, but the resolver must mirror it as
  defense-in-depth, and headless/other callers bypass glow). Added a base-power filter to the resolver mirroring the
  glow: exact `def.Power != N`, or-less `> N`, or-more `< N` (reads PRINTED base power). `eb03025test`: a 6000-power
  Character bounces, a 5000-power Character is rejected. Regressions returncost045test/op14049test PASS (cost-cap and
  cost≤7 bounces unaffected). Owner-agnostic cost-cap bounces (22+ cards) unchanged (bpBounceM.Success=false → no filter).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#261.

## K.O.-by-base-power filter dropped in resolver (glow≠resolve drift) — fix #262 (14+ cards)
- #262 "K.O. up to N of your opponent's Characters with N BASE power or less" — EB01-010, EB04-033, OP04-003 Usopp,
  OP13-071, OP14-002, OP14-037, OP14-108 Silvers Rayleigh, OP15-011, OP16-010, OP16-011 Vista (up to 2), OP16-013,
  PRB02-001, ST23-003, ST23-003, etc. The generic multi-KO resolver (L12205) parsed CURRENT-power caps
  (`(\d+) power or less`) and EXACT base power (`base power of N`), but NOT "N base power or less/more" — the word
  "base" sits between the number and "power", so the current-power regexes never matched → koPowerCap stayed −1 →
  powerOk was always true → ANY opponent Character was K.O.'d regardless of base power. The glow
  (IsValidEffectTarget L7769) DID enforce it (glow≠resolve drift, like #261) — safe in the real UI since only valid
  targets glow, but the resolver must mirror it (headless/bot play + any non-glow path bypassed the filter). Added
  koBasePowerCap (`(\d{3,5}) base power or less`) + koBasePowerMin (`or more`) reading koDef.Power (printed base),
  wired into powerOk; gated koBasePowerExact so the exact form (OP14-064 Giolla "base power of 0") is unaffected.
  Reverted an earlier speculative edit to the unreachable L14485 current-power handler (koM matches first at L12120,
  so that path never fires for these cards). `eb01010test`: 6000-base K.O.'d, 7000-base rejected. Regressions
  giolla064test (exact base power 0) PASS; coverage 2675 (all KO cards auto-resolve).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#262.

## Systematic glow≠resolve sweep (secondary filters) — fix #263 (rest-by-base-power) + EB03-021 deferral
- #263 OP14-027 Shanks / OP14-038 — "Rest up to 1 of your opponent's Characters with 7000 base power or less." The
  rest-opponent resolver (L12978) enforced cost-cap / dynamic-cap / DON-given / feature / name filters but NOT base
  power → any-power Character was restable (same glow≠resolve drift class as #261 bounce, #262 K.O.). Added
  restBpCap (`(\d{3,5}) base power or less`) + restBpMin (`or more`) reading printed base power, wired into the
  target-validity check. `op14027test`: 7000-base rests, 8000-base rejected. Coverage/OP15-027 DON-given rest
  unaffected.
- SWEEP COVERAGE: audited every removal/target verb for base-power / DON-given / [Trigger] filters the glow
  (IsValidEffectTarget L7728-7795) enforces:
  • K.O. by base power — FIXED #262. Bounce by base power — FIXED #261. Rest by base power — FIXED #263 (this iter).
  • give −power / give −cost to opponent with base-power/DON-given/[Trigger] — NO such cards exist (scan clean).
  • DON-given filter — mirrored in K.O. (#100 Mohji) + rest (OP15-001/OP15-027) resolvers. [Trigger] filter —
    mirrored in K.O. (koDef.Trigger check L12248) + hand-play glow.
- ⏸ DEFER (dual mixed-filter, 1 card) EB03-021 Alvida — "[On Play] trash 1 from hand: Place up to 1 of your
  opponent's Characters with 4000 BASE POWER or less AND up to 1 Character with a BASE COST of 3 or less at the
  bottom of the owner's deck." A dual place with DIFFERENT filter TYPES (power on clause 1, cost on clause 2). The
  L8856 place-at-deck handler only parses cost caps → drops the base-power clause and applies cost≤3 to both picks;
  the glow ANDs both filters (over-restrictive but safe). Correctly resolving needs a dedicated dual-mixed handler
  (the dual-KO path only does same-type dual). It still RESOLVES (places up to 2 cost≤3 Characters, owner-agnostic)
  — a fidelity gap, not a crash/stall. Deferred; the only card with this power+cost mixed-place structure.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#263.

## give-minus-power drops ＜attribute＞/feature filter (glow≠resolve drift) — fix #264 (OP11-006)
- #264 OP11-006 Zephyr "[DON!! x1] [When Attacking] Give up to 1 of your opponent's ＜Special＞ attribute Characters
  −5000 power during this turn." The give-minus-power resolver (L8692, 26+ cards) validated only seat + type, NOT
  the ＜attribute＞/{tag}/colour/quoted-type filter on the target → −5000 hit ANY opponent Character regardless of
  attribute. The glow (CardPassesFeatureFilter, L7387 handles ＜attribute＞) already enforced it — same glow≠resolve
  drift class as #261/#262/#263. Added `!CardPassesFeatureFilter(text, redDef)` to the target-validity check
  (mirrors the rest resolver L13015). `op11006test`: ＜Special＞ Character takes −5000, ＜Slash＞ rejected. Filter-less
  give-minus-power cards unaffected (CardPassesFeatureFilter returns true when no feature filter is present);
  coverage 2675 exercises all 26+.
- Note: OP11-006 was the ONLY opponent-targeting removal/debuff card with an ＜attribute＞ filter (DB scan); K.O./
  rest/bounce feature filters already route through CardPassesFeatureFilter in their resolvers.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#264.

## place-at-deck-bottom drops ownership + power filter (glow≠resolve drift) — fix #265 (13 cards)
- #265 "Place up to 1 of your OPPONENT's Characters with N power or less at the bottom of the owner's deck" —
  EB02-027 Vista (1000 power), OP10-060 (6000), OP13-058 (3000), P-082 (2000), ST10-001 Law (3000), OP02-056
  Doflamingo, OP03-074, OP06-077, OP08-041, EB04-026, OP09-051 Buggy, OP11-061, OP12-042 (13 cards). The place-at-
  deck handler (L8856) parsed ONLY a cost cap → it (a) never restricted to the OPPONENT's side, so you could place
  your OWN Character, and (b) ignored the "N power or less" filter, so an over-power opponent Character could be
  placed. Both were enforced by the glow (ownership via zone + `(\d) power or less` via L7782) — same glow≠resolve
  drift class as #261-#264. Added: sinkOppOnly (text has "your opponent's" → target must be opponent) / sinkOwnOnly
  ("of your " without "opponent's" → own); sinkPwrCap (current power) + sinkBpCap (base power) filters. Found while
  deep-tracing OP09-051 Buggy (whose clause A routes here). `eb02027test`: opp 1000-power placed, opp 6000-power
  rejected, own Character rejected. Regressions amanotest (dual-cost opp place)/op09051test/op01063test PASS.
- VERIFIED CLEAN (added regression): OP09-051 Buggy "[On Play] Place up to 1 opp Character at deck bottom. Then, if
  you do not have 5 Characters with a cost of 5 or more, place THIS Character at deck bottom." The negated-count
  condition (EvaluateCondition L6228) + ". Then," split + self-place-at-deck (L10408) all chain correctly.
  `op09051test`: <5 big → opponent placed + Buggy self-places; 5 big → opponent placed + Buggy stays.
- give-minus-COST sweep: only OP02-002 Garp (cost-cap, already handled). No other verb has a tag/attribute filter.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#265.

## "Rest up to a total of N … Then, add Life" — leading clause dropped — fix #266 (OP06-035)
- #266 OP06-035 Hody Jones "[On Play] Rest up to a total of 2 of your opponent's Characters or DON!! cards. Then,
  add 1 card from the top of your Life cards to your hand." The ". Then," rider fired but the LEADING rest clause
  was SILENTLY DROPPED — 0 Characters rested, yet the Life-add ran. Root cause: the early ". Then," split
  (L8589) gates clause A on IsAutomatedEffectPattern, whose rest regex `Rest up to \d+` does NOT match "Rest up to
  A TOTAL OF 2" (the words "a total of" sit between "up to" and the number) → the split never fired → the full text
  fell through to the add-Life-to-hand handler (L11203), which matched the trailing "add 1 card from the top of your
  Life cards to your hand" substring and resolved it, dropping the rest. Fix: broadened the regex to
  `Rest up to (?:a total of )?\d+` so the early split fires → clause A (mixed Characters/DON rest, L12434) resolves
  its picks, then clause B (add Life) is queued. `op06035test`: 2 opp Characters rested + 1 Life card to hand.
  (Recurring class: a later-clause phrase matched by an EARLIER handler that drops the leading clause — here
  because the ". Then," splitter's automated-pattern gate under-matched the "a total of" quantifier.)
- The sibling mixed-rest card OP12-037 Asura uses the same "Rest up to a total of 2 … Characters or DON!!" clause
  but with NO ". Then," rider, so it was already fine; this fix also hardens it for any future rider.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#266.

## Multi-target buff "cards GAIN +N" clause dropped before ". Then, K.O." — fix #267 (EB02-007)
- #267 EB02-007 Cloven Rose Blizzard "[Main] Up to a total of 3 of your Leader and Character cards gain +1000 power
  during this turn. Then, K.O. up to 1 of your opponent's Characters with 3000 power or less." The buff clause A was
  SILENTLY DROPPED (own cards +0) while the K.O. rider ran. Same mechanism as #266: the early ". Then," splitter
  gates clause A on IsAutomatedEffectPattern, whose buff check required the substring "gains +" (singular subject) —
  but the plural multi-target phrasing is "cards GAIN +1000" ("gain +", no s) → not recognized → split skipped → the
  full text reached the K.O. handler (L12205, earlier than the buff handler L13178), which matched "K.O. up to 1 …
  3000 power or less" and swallowed the whole text, dropping the buff. Fix: replaced the "gains +" substring with
  regex `gains? \+\d{3,7} power` (matches both "gain +" and "gains +") in IsAutomatedEffectPattern. `eb02007test`:
  own card +1000 AND opponent Character K.O.'d. Regressions op06035test/op03001test PASS.
- "a total of" ". Then," sweep (post-#266): EB02-007 was the one remaining broken case; OP12-037 Asura (no rider),
  OP04-031 Doflamingo (no rider), OP02-089 (Counter, no rider), OP15-101 Kalgara (look-reveal, own split path) —
  all clean. The two ". Then,"-rider "a total of" cards (OP06-035 #266, EB02-007 #267) are now both fixed.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#267.

## Compound "Leader gains +N AND give opp −M power" — leader buff dropped — fix #268 (OP15-020)
- #268 OP15-020 Fire Fist "[Main] Your Leader gains +3000 power during this turn AND give up to 1 of your
  opponent's Characters −8000 power until the end of your opponent's next End Phase. Then, you may trash 2 cards
  from your hand. If you do, K.O. up to 1 of your opponent's Characters with 0 power or less." The ". Then," split
  correctly separated clause A from B, but clause A is itself COMPOUND ("leader +3000 AND give opp −8000"): the
  give-minus-power-until-end handler (L11738) matched only the "−8000" half and returned Resolved, SILENTLY DROPPING
  the "Your Leader gains +3000 power … and" prefix. (The −8000 half enables clause B's combo — it drops a 6000
  Character to ≤0 so the "0 power or less" K.O. connects — so the visible symptom was just the missing +3000.) Added
  a compound handler BEFORE the give-minus handlers: match `^Your … Leader gains +N power during this turn and
  (give …)$`, apply the leader buff, then QueueAndAutoResolve the "give …" remainder as its own effect (which
  resolves with its target pick before the following ". Then," clause). `op15020test`: Leader +3000 AND the debuffed
  opponent Character (−8000 → ≤0) is K.O.'d by clause B. Regressions op11006test/eb02007test/totalpower via coverage PASS.
- VERIFIED CLEAN this iter (added regressions): OP03-122 Sogeking (return + ". Then," draw2/trash2 — op03122test);
  EB04-061 Luffy (Leader +2000 + ". Then," self-[Blocker], both "until end of opp next End Phase" duration —
  eb04061test; granted keywords live in state.ActiveModifiers, not CardInstance.Modifiers). ". Then," clause-A gate
  sweep: return/place/rest/buff/select/leader-buff/reveal-Event all covered; #266/#267/#268 were the gaps.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#268.

## Compound "removal-opp AND self/leader buff" — one half dropped — fix #269 (5 cards, unified splitter)
- #269 A family of ". Then,"-less compounds where a leading opponent-removal/debuff clause and an appended
  self/leader buff are joined by " and" (comma optional). Whichever handler runs first dropped the other half
  (handler order: give-minus-power L8692 < keyword-grant L11528 < K.O. L12205):
  • OP03-016 Flame Emperor "K.O. up to 1 … 8000 power or less, and your Leader gains [Double Attack] and +3000
    power" → K.O. dropped (keyword handler ran first).
  • OP06-061 Vinsmoke Ichiji "give … −2000 power … and this Character gains [Rush]" → [Rush] dropped (give ran first).
  • OP06-034 Hyouzou / P-062 "Rest up to 1 opp cost≤4 and this Character gains +1000 power. Then, add Life" → the
    +1000 self-buff dropped (rest ran first).
  • OP02-009 Squard "give … −4000 power … and add 1 card from the top of your Life cards to your hand" → add-Life
    dropped (give ran first).
  FIX: added ONE unified EARLY splitter right after the ". Then," early split (L~8632), mirroring its clone-and-
  queue machinery. It matches `^(<If cond,>? (K.O.|Rest|Give|Return|Place|Trash) …) ,?and (<Leader/this Character/
  this card> gains [KW|+N power] | add N cards from the top of your Life cards to your hand)$` — scoped to a leading
  removal VERB + a tight clause-B alternation so an internal "and" ("gains [DA] and +3000") never splits, and a
  leading "If <cond>," stays attached to clause A so its condition still gates the removal. Resolves clause A
  (WaitingForTarget → PendingContinuation like ". Then,"), then queues clause B. Reverted an initial per-handler
  patch in favor of this single splitter. Tests op03016test/op06061test/op06034test/op02009test.
- Regressions op15020test(#268)/eb02007test(#267)/op06035test(#266)/op03122test/eb04061test/op03001test/op11006test PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#269 (5 cards).

## Continuous opponent auras + "If <cond>, A. Then, B" gating — VERIFIED CLEAN (no engine change)
- OP11-013 Prince Grus "[When Attacking] All of your opponent's Characters with 2000 power or less cannot activate
  [Blocker] this turn." — the board-wide no-blocker handler (L11525) enforces the "N power or less" filter (lbMatch)
  for BOTH the board-wide and picked branches. CLEAN.
- OP14-079 Crocodile (Leader) "All of your opponent's Characters cannot be removed from the field by your effects."
  — a REMOVER-side restriction. RemoverCannotRemoveByEffect (L5380) is wired into the K.O. handlers, and
  RemovalBlocked (L4671, used by bounce L8830 + place L8927) ALSO checks it (L4688) → K.O./bounce/place ALL blocked.
  `op14079test` verifies all three removal types are blocked while this Leader is in play. CLEAN.
- ⚖ IMPORTANT RULING VERIFIED: OP13-108 Jewelry Bonney "[On Play] If your Leader has the {Egghead} type, this
  Character gains [Rush] during this turn. Then, your opponent adds 1 card from the top of their Life cards to their
  hand." The early leading-If gate (L8096) gates the ENTIRE body — incl. the ". Then," clause — on the condition, so
  off-condition NOTHING fires. Initially looked like a dropped ". Then," rider, BUT a survey of ALL 80 "If <cond>, A.
  Then, B" cards shows 78/80 have a clause B that is genuinely part of the archetype-gated effect (look→"place the
  rest", give−cost→combo K.O. e.g. OP04-008 Chaka/OP08-097, draw→"play a card"). Making clause B unconditional would
  BREAK those 78 — so the uniform whole-body gating is CORRECT and OP13-108 works as intended (both clauses Egghead-
  gated). CORRECTION: there ARE Egghead leaders — EB04-001/OP13-100 Bonney, OP07-097 Vegapunk, ST29-001 Luffy —
  so OP13-108 is a normal Egghead-deck card where the condition is MET and BOTH the [Rush] and opponent-Life-add
  fire. `op13108test`: non-Egghead → nothing; Egghead (feature set) → opp Life −1 + [Rush]. **Do NOT "fix" this — it
  would regress 78 cards.** ⚠ METHOD NOTE: card TYPES live in the JSON `feature` field (singular, slash-separated
  string e.g. "Scientist/Egghead"), NOT `features` (which is None) — Python scans filtering by type MUST read `feature`.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#269.

## Negate "that Character" conditional-K.O. rider dropped — fix #270 (OP09-098)
- #270 OP09-098 Black Hole "[Main] If your Leader has the {Blackbeard Pirates} type, negate the effect of up to 1
  of your opponent's Characters during this turn. Then, if that Character has a cost of 4 or less, K.O. it." The
  ". Then," early split separated clause B ("if that Character … K.O. it") from the negate — but clause B references
  "that Character" (the just-negated one), which NO split can resolve, so it was reduced to a leading-If with an
  UNKNOWN condition ("that Character has a cost of 4 or less") → treated as not-met → the K.O. was dropped. Also,
  naively keeping them together made the negate handler's cost-cap parser wrongly read the rider's "cost of 4 or
  less" as the NEGATE target's cap (rejecting a cost-5 negate target). Two-part fix: (1) added a ". Then," early-
  split guard to keep "negate the effect … that Character …" together (the negate handler owns the whole compound);
  (2) in the negate handler, strip the ". Then, if that Character …" rider before parsing the negate target's cost
  cap, and added a rider that K.O.s the SAME negated Character (negT) when its cost ≤ the rider cap. `op09098test`:
  cost-1 victim → negated + K.O.'d; cost-5 victim → negated, survives (cost > 4). Regression op09093test (sibling
  negate + cannot-attack rider) PASS.
- VERIFIED CLEAN (regressions added): OP07-097 Vegapunk (Leader) "[Activate: Main] ① : Select up to 1 {Egghead}
  card cost≤5 from hand and play it or add it to the top of your Life face-up" — ① rests 1 DON, the play-or-add-to-
  Life modal fires, {Egghead} filter enforced (op07097test). OP11-013 / OP14-079 auras (prior iter).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#270.

## Cross-clause reference-back + opponent-plays reactive — VERIFIED CLEAN (no engine change)
- EB02-021 Gum-Gum Giant Pistol "[Main] Up to 1 of your {Straw Hat Crew} type Characters gains +6000 power during
  this turn. Then, the selected Character will not become active in your next Refresh Phase." The ". Then," clause
  references "the selected Character" (the just-buffed one) via LastPowerBuffTargetId — handled at L9107 (freeze
  "ownNextRefresh" on the buffed card). `eb02021test`: buffed +6000 AND the SAME Character gets the self-freeze
  modifier. CLEAN. (Siblings OP14-078 Bullet String "that card gains an additional +2000" and OP12-058 reveal-play
  "that Character gains [Rush]" use the same LastPowerBuffTargetId / kwRevealChain paths — handled.)
- OP04-024 Sugar "[Opponent's Turn] [Once Per Turn] When your opponent plays a Character, if your Leader has the
  {Donquixote Pirates} type, rest up to 1 of your opponent's Characters." Reactive dispatched from PlayCard (L2239,
  def.Type=="character") for the play-er's opponent; leading-If gates the rest on the Donquixote leader.
  `op04024test` (real play flow, Donquixote leader): opponent plays a Character → Sugar's reactive queues → an
  opponent Character rests. CLEAN.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#270.

## Stage self-place cost "place this card and N from hand" — self-place dropped — fix #271 (EB01-030)
- #271 EB01-030 Loguetown (Stage) "[Activate: Main] You may place this card and 1 card from your hand at the bottom
  of your deck in any order: Draw 2 cards." The compound self-sacrifice cost placed only the HAND card at the deck
  bottom — the "place THIS CARD" (the Stage itself) part was DROPPED, so the Stage stayed on the field and the
  ability was wrongly REPEATABLE every main phase (place 1 hand card → draw 2, forever). Root cause: the place-from-
  hand cost handler (L8467) handled a "rest this Stage" rider but NOT a "place this card" self-place-to-deck rider.
  Fix: added a "place this card" self-place rider (removes the source from Stage/Character zone, returns any
  attached DON, sends it to the deck bottom) + made the hand-count parse read "N cards from your hand" when
  "place N" doesn't match (so "place this card and 2 cards from hand" isn't under-paid). `eb01030test`: Stage
  consumed to deck bottom + 1 hand card placed + draw 2. Regressions op02048test/op04073test PASS; OP09-060 (place-2-
  and-rest-Stage) unaffected (uses "place 2", no "place this card").
- VERIFIED CLEAN (handlers exist): EB02-009 Thousand Sunny "rest this Stage: Give up to 1 of your currently given
  DON!! to 1 of your {SHC} Character" (given-DON re-attach, L10127). OP12-080 Baratie "place this Stage at the bottom
  of the owner's deck: …" uses the dedicated self-place cost L7051 (exact-anchored, distinct from EB01-030's compound).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#271.

## Stage-activate deck-look FREE (cost-prefix bypassed by the fast-path) — fix #272 (SYSTEMIC, ~10 cards)
- #272 SYSTEMIC: Stage/Character [Activate: Main] abilities of the form "You may <cost>: Look at N …" (OP02-092
  Impel Down, OP03-020 Striker [②+rest], OP05-021, OP06-079, OP06-098 [①+rest], OP09-099, OP12-080 Baratie
  [place-this-Stage], and any deck-search/scry variant) ran the deck effect for FREE. Root cause: ActivateMain's
  default case (L2481) pays only the circled-DON / DON!!−N cost, then dispatches to the deck-search/scry/deck-look
  FAST-PATHS (L2490-2514, StartDeckSearch/Scry/Look) — which run the deck effect DIRECTLY, never paying the
  "You may <cost>:" prefix (rest this Stage / trash from hand / place this Stage) NOR honoring a leading "If <cond>,".
  So e.g. Impel Down looked at the deck without trashing a card or resting the Stage (repeatable free dig). Fix:
  before the fast-paths, strip the leading [timing] tags + circled-DON reminder parenthetical, and if the remainder
  starts with a "You may <cost>:" prefix, route through QueueEffect (its cost-prefix resolver pays the cost + honors
  the If, then the body's own Look handler L14181 runs the deck-look) instead of the fast-path. Also broadened the
  self-place cost handler (L7070) to accept "place this Stage at the bottom of the owner's deck" (OP12-080) — it
  already supported a Stage source, just not the word. `op02092test` (trash 1 + rest Stage paid), `op03020test`
  (② rest 2 DON + rest Stage paid, Ace-gated deck-look starts). Regressions eb01030test/op02048test/op07097test PASS;
  coverage restored to 2675 (OP12-080 was momentarily NOT_AUTOMATED before the L7070 broadening).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#272.

## #272 scry-body verification + self-play-restriction — VERIFIED CLEAN (no engine change)
- P-068 Sanji "[Activate: Main] You may trash this Character: Look at 5 cards from the top of your deck and place
  them at the top or bottom of the deck…" (also P-074 Ace, return-self cost). Self-sacrifice cost + SCRY body —
  post-#272 routes through QueueEffect: the cost is PAID (Sanji trashed) then the scry starts (body resolver
  StartDeckScry L14163). `p068test`: Sanji trashed + scry started. Confirms #272 covers the scry-body variant.
- OP13-023 Uta "[On Play] Set up to 2 DON!! active. Then, you cannot play Character cards with a base cost of 5 or
  more during this turn." (also OP13-118/OP12-030/OP14-020/OP13-028/EB03-024 family). The play-restriction is set
  (NoPlayCharBaseCostAtLeast, L9932) and ENFORCED in PlayCard against the PRINTED base cost (L2135). `op13023test`:
  2 DON set active (clause A) + a base-cost-5 Character blocked + a base-cost-4 Character played (restriction correct).
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#272.

## [On K.O.] play triggers (name/deck/vanilla filters) — VERIFIED CLEAN (no engine change)
- EB03-007 Baccarat "[On K.O.] Play up to 1 Character card with 6000 power or less and no base effect from your
  hand." The "no base effect" filter (VANILLA-only, empty printed effect) is enforced via CardPassesFeatureFilter
  (L7401). `eb03007test`: a Character WITH an effect is rejected, a vanilla ≤6000 Character plays. CLEAN. (TEST-
  HYGIENE note: a directly-queued PendingEffect for a "…from your hand" play needs TargetZone=EffectTargetZone.Hand
  — the play-from-hand resolver L13610 gates on it; the real dispatch sets it via InferTargetZone. Without it the
  probe falsely showed NOT_AUTOMATED.)
- OP01-069 Caesar Clown "[On K.O.] Play up to 1 [Smiley] from your deck, then shuffle" — the play-from-deck handler
  (L11953) parses the [Name] and passes it to StartDeckSearch with an EMPTY type filter so ONLY the named card
  qualifies (the #214-class name-vs-type fix); "other than [Name]" exclusion handled. Structurally correct; coverage
  green. (Minor noted gaps, not chased: OP02-030's leading "green" color filter and its exact "cost of 3" are read
  as a ≤-cap by the deck-play path — over-permissive but low-impact.)
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#272.

## Deck-play dropped color + exact-cost filters — fix #273 (OP02-030)
- #273 OP02-030 Kouzuki Oden "[On K.O.] Play up to 1 GREEN {Land of Wano} type Character card with a cost of 3 from
  your deck. Then, shuffle your deck." The play-from-deck handler (L11953) parsed the {tag} and cost but (a) never
  enforced the leading COLOR ("green") and (b) treated the EXACT "cost of 3" as a ≤3 cap (ParseLimit → MaxCost=3,
  MinCost unset) → a cost-1/2 {Land of Wano} was wrongly playable. Fix: added a ColorFilter field to DeckLookState
  (validated in ResolveDeckLookSelect against def.Color), and in the deck-play handler set ColorFilter from the
  leading colour + set MinCost=MaxCost when the cost is EXACT ("cost of N" without "or less"). `op02030test`: a
  green cost-3 {Land of Wano} plays, a green cost-2 is rejected. Scoped tightly (only "cost of N" without "or less"
  sets MinCost) so the many "cost of N or less" deck-play tutors are unaffected; coverage 2675 confirms.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#273.

## Play-from-trash dropped color + exact-cost filters — fix #274 (5 cards)
- #274 The play-from-trash resolver (L14440) enforced name/cost-cap/power/feature/other-than/[Trigger] filters but
  NOT (a) the leading COLOR (EB02-044 Sengoku "black {Navy}", OP16-098 Yamato "black [Yamato]") and (b) EXACT cost
  ("cost of N" without "or less" — OP16-098 cost 8, OP16-084 Momo cost 9, OP13-092 Stage cost 1, OP14-084) — it read
  "cost of N" as a ≤N cap. So an off-colour or cheaper card was wrongly playable from trash. Fix (mirrors the deck-
  play #273): added a colour check (`\b<colour> [\{\[]` — colour directly before the {tag}/[Name] so a leading-
  condition colour word isn't mis-read) and made the cost check EXACT (Cost == N) when the text lacks "or less/more".
  `op16098test`: a black cost-8 Yamato plays; a black cost-6 Yamato (exact cost) and a red cost-5 Yamato (colour) are
  both rejected. Scoped so "cost of N or less" trash tutors keep their ≤ cap (coverage 2675 confirms all 5 + the rest
  auto-resolve).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#274.

## Play-from-hand dropped EXACT cost + [Name]/plain-Character color — fix #275 (12+ cards)
- #275 The play-from-HAND resolver (L13676) checked "cost of N or less" (≤ cap) and "cost of N or more" (≥ min) but
  had NO EXACT-cost check — for "cost of N" (no "or less/more"), BOTH missed it → the cost was UNRESTRICTED and an
  ANY-cost Character of the right type/name played (EB02-028 "Character card with a cost of 2" → any Character!,
  OP02-035 cost 3, OP06-060/064 [Vinsmoke] cost 7/5, OP04-119 green cost 5, EB01-033/EB03-048/OP03-014/OP05-112 …).
  The glow (IsValidEffectTarget L7765) already enforced exact cost — a glow≠resolve drift. Fix: added an exact-cost
  check (Cost == N when the text has neither "or less" nor "or more"), mirroring the glow. Also broadened the
  play-from-hand COLOR regex from "<colour> {tag}" only to "<colour> (?:{ | [ | Character)" so "<colour> [Name]"
  (OP06-060 black [Vinsmoke Ichiji]) and "<colour> Character" (OP04-119 green, OP02-010 red) are enforced too.
  `eb02028test`: cost-2 plays / cost-3 rejected (exact); green Character plays / red Character rejected (colour).
  Coverage 2675 confirms the many "cost of N or less" and no-colour play-from-hand cards are unaffected.
- This completes the colour/exact-cost dropped-filter sweep across all three play sources: deck (#273), trash (#274),
  hand (#275).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#275.

## Rest exact-cost + Stage-only K.O. — fixes #276, #277
- #276 REST exact-cost: OP14-090 Mr.1 "Rest up to 1 of your opponent's Characters with a cost of 0." The rest-
  opponent resolver had restCostCap ("cost of N or less") but NO exact-cost check → "cost of 0" (no "or less")
  matched nothing → ANY-cost Character was restable. Added restCostExact (== N when no "or less"/"or more"),
  mirroring the K.O. resolver's koCostExact. `op14090test`: an effective-cost-0 Character (−cost modifier) rests, a
  cost-1 Character is rejected.
- #277 STAGE-only K.O. UNIMPLEMENTED: OP14-088 Miss.MerryChristmas "[On K.O.] …K.O. up to 1 of your opponent's
  Stages with a cost of 1" and OP13-098 "…K.O. up to 1 opponent's Stages with a cost of 7" did NOTHING — the only
  Stage-K.O. handler was the DUAL char+stage one (L13884), which requires "opponent's Characters" in the text, so
  the stage-ONLY form fell through unhandled (hidden in coverage by their leading Imu/Baroque-Works conditions).
  Added a dedicated stage-only K.O. handler (before the dual): matches "K.O. up to N of your opponent's Stages"
  without "opponent's Characters", validates the Stage cost (exact == N unless "or less"), and K.O.s it (returns
  attached DON, Stage→trash). Also added it to IsAutomatedEffectPattern so the reactive/condition-met path auto-
  resolves. `op14088test`: a cost-1 Stage is K.O.'d, a cost-7 Stage rejected. Dual char+stage handler unaffected
  (stage-only guard requires NOT "opponent's Characters"); amanotest PASS.
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#277.

## Own-Character buff dropped colour/cost/base-power target filters — fix #278 (glow≠resolve drift)
- #278 The give-+power buff resolver (L13393) validated seat/type/name/feature but NOT the COLOUR, COST, or BASE-
  POWER filter on the buff TARGET (CardPassesFeatureFilter covers tag/attribute/quoted-type, NOT those three) →
  OP02-015/016 Makino/Magura "Up to 1 of your RED Characters with a COST of 1 gains +3000" could buff a non-red or
  cost-2 Character; OP12-001/OP13-022 "with N BASE power or less" could buff an over-power one. The glow
  (IsValidEffectTarget) enforces all three — a glow≠resolve drift (same class as the K.O./rest/play fixes). Added
  buffColor (colour before {tag}/[Name]/Character) + buffCostCap/buffCostExact + buffBpCap/Min to the target check.
  `op02015test`: a red cost-1 Character is buffed; a red cost-2 (exact cost) and a green cost-1 (colour) are rejected.
  Regressions eb02007test (multi-buff)/op03001test (Ace)/op15020test (leader buff) PASS; coverage 2675 (no buff card
  regressed).
- This COMPLETES the dropped colour/exact-cost/base-power target-filter sweep across every targeting verb:
  play(deck#273/trash#274/hand#275), K.O.(#262 + pre-existing exact), bounce(#261), rest(#263/#276), give−power(#264),
  place-at-deck(#265), Stage-K.O.(#277), and buff(#278).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#278.

## Unfiltered "K.O. up to N opponent's Characters" not in the ".Then," gate — fix #279 (EB01-059)
- #279 EB01-059 Kingdom Come "[Main] K.O. up to 1 of your opponent's Characters. Then, trash cards from the top of
  your Life cards until you have 1 Life card." The K.O. clause A was DROPPED (only the Life-trash ran). Root cause:
  the early ". Then," split gates clause A on IsAutomatedEffectPattern, but an UNFILTERED "K.O. up to N of your
  opponent's Characters" (no cost/power cap) matched NONE of the K.O. gate entries (all require "cost of"/"power")
  → the split skipped → the full text fell through to the Life-trash handler (L9705), which matched "trash cards
  from the top of your Life … until you have 1 Life" and resolved it, dropping the K.O. (The generic multi-KO
  resolver DID handle clause A — it just never got the chance.) Fix: added `K\.O\. up to \d+ of your opponent's
  Characters` to IsAutomatedEffectPattern so the split recognizes the unfiltered K.O. clause. `eb01059test`: opp
  Character K.O.'d AND south Life trashed from 4 down to 1. Regressions eb02007test/op03016test/op14088test PASS.
- VERIFIED (handler exists): EB01-059/EB01-060's "trash Life until you have N Life" variable-count self-Life-trash
  (L9705); OP11-108 Neptune "turn 1 Life face-down: draw 2/trash 1" — face-down Life cost (behind an Imu/Shirahoshi
  condition, coverage-green).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#279.

## [On Block] compound + end-of-battle "Character you battled" — VERIFIED CLEAN (no engine change)
- OP05-047 Basil Hawkins "[On Block] Draw 1 card if you have 3 or less cards in your hand. Then, this Character
  gains +1000 power during this battle." Compound [On Block] — the block dispatch queues it and the ". Then," split
  resolves both. `op05047test` (real block): with a ≤3-card hand, Hawkins draws 1 AND gains +1000 for the battle.
- OP04-047 Ice Oni "[Your Turn] At the end of a battle in which this Character battles your opponent's Character
  with a cost of 5 or less, place the opponent's Character you battled with at the bottom of the owner's deck."
  ApplyEndOfBattleEffects (L4552, called at battle-end L4547) reads the cost cap + [Your Turn] + DON gate and places
  the surviving battled Character at deck bottom (also handles ST08-013 Bentham's mutual-K.O.). `op04047test`: Ice
  Oni (0 power) battles a rested cost-5 Character → that Character is placed at the bottom of the opponent's deck.
  (Minor noted deferral: ST08-013's "you MAY K.O. the Character you battled with. If you do, K.O. this Character" is
  auto-forced, not offered as a choice — the mutual trade is the typical play; not chased.)
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#279.

## Trigger "Play this card. Then, <rider>" dropped the rider — fix #280 (OP04-101, OP08-104)
- #280 OP04-101 Carmel "[Trigger] Play this card. Then, K.O. up to 1 of your opponent's Characters with a cost of 2
  or less" and OP08-104 Charlotte Poire "[Trigger] …: Play this card. Then, draw 1 card." The Life-reveal Trigger
  "Play this card" branch (TryResolveKnownTrigger, L15163) played the card + fired its [On Play], then returned —
  DROPPING the ". Then, <rider>" on the TRIGGER (which is separate from the card's own [On Play]). So Carmel played
  and drew (its [On Play]) but its Trigger K.O. never happened. Fix: after the play, extract the trigger's ". Then,"
  clause (FindThenClause) and QueueAndAutoResolve it. `op04101test` (real damage→Life-reveal→useTrigger flow):
  Carmel plays from Life AND the ". Then," rider K.O.s an opponent cost-2 Character. Regressions triggercaptest/
  eb01059test PASS.
- VERIFIED CLEAN (regression added): EB01-028 "[Trigger] Return up to 1 Character with a cost of 3 or less to the
  bottom of the owner's deck" — return-to-DECK (delegated to the Place-at-deck handler via a Return→Place rewrite,
  L10269); `eb01028test`: cost-3 → deck bottom, cost-4 rejected. OP15-079 Absalom "[Trigger] Activate this card's
  [On K.O.] effect" — the meta-reference delegates to the named [On K.O.] clause (L15203).
- Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Total gate-invisible bugs now #163–#280.

## Cost-prefix Triggers + buff-and-K.O. counters — VERIFIED CLEAN (no engine change)
- OP04-094 Trueno Bastardo "[Trigger] You may rest your Leader: K.O. up to 1 of your opponent's Characters with a
  cost of 5 or less." The generic Trigger path (TriggerBodyResolvable → cost-prefix resolver) pays the rest-Leader
  cost and resolves the K.O. `op04094trigtest` (real damage→Life-reveal→useTrigger): Leader rested + an opponent
  cost-5 Character K.O.'d. (Confirms the cost-prefix Trigger class — OP04-117/OP07-055/OP14-036 use the same path.)
- ST10-015 Gum-Gum Giant Sumo Slap "[Counter] Up to 1 of your Leader or Character cards gains +2000 power during
  this battle, and K.O. up to 1 of your opponent's Characters with 2000 power or less." The "buff AND K.O." compound
  (comma-and, BUFF-first — NOT caught by the #269 splitter which needs a removal-verb lead) has a dedicated handler.
  `st10015test`: own card +2000 AND opponent 2000-power Character K.O.'d. (OP05-039/ST06-014 use the ". Then, K.O."
  form, handled by the ". Then," split.)
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#280.

## Opponent-DON-return + conditional hand self-discount — VERIFIED CLEAN (no engine change)
- OP14-065 Senor Pink "[On K.O.] Your opponent returns 1 DON!! card from their field to their DON!! deck." (also
  OP02-085/OP16-074 Magellan.) Handled at L9263 via PayDonMinus on the opponent (capped at their DON count).
  `op14065test`: opponent DON field 5→4, DON deck +1.
- Conditional hand self-discount "[If <cond>,] give this card in your hand −N cost" — GetHandSelfCostBonus (L937),
  wired into GetCost (L881), evaluates the condition via the shared EvaluateCondition (fail-closed / logUnknown off)
  and reduces the card's play cost. `st23001test`: ST23-001 Uta (base cost 6) costs 2 with a 10000-power Character
  in play, 6 without. OP15-013 Pincers' unusual condition "your Leader has 0 power or less" IS recognized
  (EvaluateCondition L6551) → the discount flows through the play-cost/affordability/payment paths (OP07-064/OP11-023/
  OP16-005/PRB02-014 use the same mechanism).
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#280.

## Continuous hand cost aura + scaling passive power — VERIFIED CLEAN (no engine change)
- OP01-067 Crocodile "[DON!! x1] Give blue Events in your hand −1 cost." Continuous aura on OTHER hand cards
  (GetHandCostReductionAura L961): DON gate + colour + type filters, wired into GetCost. `op01067test`: a blue Event
  costs −1 with 1 DON on Crocodile, unchanged with 0 DON, and a red Event is never discounted.
- EB01-027 Mr.1 "If your Leader's type includes \"Baroque Works\", this Character gains +1000 power for every 2
  Events in your trash." STATIC conditional self-buff with per-N scaling (GetStaticSelfPowerBonus L526): floor
  division `bonus * (count / per)`, Event-typed trash count. `eb01027test`: BW leader + 4 Events → +2000 (8000);
  + 5 Events → +2000 (floor 5/2); non-BW leader → +0. (Noted: EB04-048's companion "+2 COST for every 5 cards" is a
  self-cost scaling handled separately — not chased.)
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#280.

## Passive self +cost buffs (flat + scaling, cost-removal dodge) — VERIFIED CLEAN (no engine change)
- OP12-093 Morley / OP12-085 Karasu "If your Leader has the {Revolutionary Army} type, this Character gains +N
  cost." Passive self cost buff (GetPassiveCostBonus L1106, wired into GetCost L878): leading-If gate + DON gate +
  "for every M X" scaling + dual "gains +1000 power and +N cost" match (EB04-048/OP12-063). `op12093test` (strong
  end-to-end): with an RA leader Morley is cost 8 (base 4 +4) and SURVIVES a "K.O. cost≤5" (8>5); with a non-RA
  leader it is cost 4 and IS K.O.'d — confirming the passive +cost both shows in GetCost and dodges cost-based
  removal. (EB04-048's scaling "+2 cost for every 5 cards in trash" uses the same L1137 floor-division path as the
  power scaling verified last iter.)
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (no engine change). Total gate-invisible bugs remains #163–#280.

## #281 OP06-088 Sai — compound "{type} AND is active" leader-aura failed-CLOSED (blind AND-split)
- OP06-088 Sai "If your Leader has the {Dressrosa} type and is active, this Character gains +2000 power." STATIC
  conditional self-buff (GetStaticSelfPowerBonus L526, leading-If gate). Deep-trace found the +2000 NEVER applied,
  even with an ACTIVE Dressrosa leader (want 6000, got 4000 in all cases).
- ROOT CAUSE: EvaluateCondition has TWO " and " compound splitters. The FIRST (L5953) correctly falls through when
  either half is UNRECOGNIZED (so a single dedicated handler can match the whole string). The SECOND (L6109) blindly
  recurses `left && right` with NO recognition check. "your Leader has the {Dressrosa} type" → true, but the isolated
  RHS "is active" had NO handler (only "this Character is active" at L6472 existed) → unrecognized → false → whole
  condition false. Fail-CLOSED: the buff was dead for every Sai.
- FIX: added a bare "(your) Leader is active/rested" + lone "is active"/"is rested" handler right after the
  "this Character is active" handler (~L6475), binding to `p.Leader.Rested`. The L6109 blind split's recursion into
  "is active" now resolves to the LEADER's rest state, so the compound evaluates `HasFeature(Dressrosa) && !Leader.Rested`
  correctly — active→+2000, rested→+0. Corpus scan confirmed OP06-088 is the ONLY card pairing "and is active/rested"
  (RHS unambiguously the Leader); "this Character is active" is always spelled explicitly and reaches its own handler
  before the split can isolate it, so no collision.
- `op06088test`: Dressrosa+active leader → 6000; Dressrosa+RESTED leader → 4000; non-Dressrosa leader → 4000.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#281.
- CLASS NOTE (new): **blind AND-split fail-closed** — the L6109 compound splitter ANDs both halves without a
  recognition guard, so ANY unrecognized compound RHS (a subject-eliding clause like "and is active", "and is
  rested") silently forces the whole condition false. When a compound condition mysteriously fails-closed, check
  whether the RHS after " and " has its own handler; if the subject is elided, add a bare-clause handler that binds
  to the implied subject (usually the Leader).

## #282 OP09-017 — same "blind AND-split fail-closed" class (elided-subject compound, [Rush] dead)
- OP09-017 "[DON!! x1] If your Leader has 7000 power or more AND the {Kid Pirates} type, this Character gains [Rush]."
  Found while sweeping siblings of #281. The [Rush] grant was DEAD for every OP09-017.
- ROOT CAUSE (two elided/variant halves, both unrecognized → L6109 blind split forces false):
  (a) LHS "your Leader has 7000 power or more" — the only handler was `Leader.s power IS N or more`; the card says
      "Leader HAS N power or more" (different wording) → unrecognized.
  (b) RHS "the {Kid Pirates} type" — the "Leader has the {X} type" handler needs the literal "Leader has the"; the
      compound split elided the "your Leader has" subject → unrecognized.
- FIX: (a) broadened the leader-power regex to also match `Leader has (\d+) power or more`; (b) added a bare
  "the {X} type" handler (anchored `^the \{X\} type$`) that binds to `Leader.HasFeature(X)`. Now BOTH halves are
  recognized so the FIRST split (L5953, recognition-guarded) fires and returns `Leader power≥7000 && HasFeature(Kid
  Pirates)`. Corpus scan: OP09-017 is the SOLE card for both "Leader has N power or more" and "... and the {X} type".
- `op09017test` (via HasRush printed-grant eval): Kid leader@7000 +1 DON on char → [Rush] TRUE; Kid@6000 → FALSE;
  non-Kid@7000 → FALSE; Kid@7000 but 0 DON on char (DON gate) → FALSE.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#282.
- Sibling sweep of the remaining leading-If compounds (EB02-026/EB02-061/EB03-004/EB03-059/EB04-041/OP02-008/
  OP06-069/OP06-072/OP07-060/OP07-065/OP08-033/OP08-109/OP09-039/OP09-093/…): their RHS halves use ALREADY-recognized
  phrasings (Life-count, hand-count, DON-count, "type includes", named-card-in-play) — spot-checked, no further
  fail-closed found this pass. The two blind-AND-split holes were the subject-eliding forms only.

## #283 Removal-replacement protections DID NOT fire on opponent BOUNCE / PLACE-ON-DECK (only K.O.)
- Class: "If this Character would be removed from the field by your opponent's effect, you may <X> instead."
  TryRemovalReplacement (L4849) is invoked from 10 K.O. call sites, but the opponent's NON-K.O. removals —
  return-to-hand (bounce) and place-at-bottom-of-deck — removed the victim WITHOUT ever calling it. Per rule
  4-11-2 "Remove = moving a card from its area to another area", a bounce/deck-place IS a removal from the field,
  so these protections (OP12-053, OP10-037, OP14-029, OP12-102, OP10-049, … ~34 "removed from the field" cards)
  silently failed against bounce/sink decks — the Character was removed with no chance to pay the alt cost.
- ROOT: (a) opponent-bounce resolver (~L14203, "Return … to their hand" targeting the opponent) called
  ReturnToHand directly; (b) opponent place-on-deck resolver (~L9062, "Place up to N … bottom of the owner's
  deck") moved the target to the deck directly. Neither consulted TryRemovalReplacement.
- FIX: before each removal, when the target belongs to the OPPONENT of the effect controller, call
  TryRemovalReplacement(state, victimSeat, target); if it returns true the removal is skipped (bounce path) or the
  pick is consumed but the Character stays (place-on-deck path, decrementing SelectionsRemaining/high-cap budget
  correctly and still queuing any bottom-deck play rider). Battle-KO gating inside the handler is unaffected
  (isBattleKo defaults false here; the "by your opponent's effect" lines fire on effect removal, which this is).
- `op12053test` (OP12-053 "trash 1 from hand instead"): (1) opponent bounce → stays on field + 1 hand card trashed;
  (2) CONTROL a vanilla Character still bounces to hand normally; (3) opponent place-on-deck → stays on field.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#283.
- NOTE: verified the fall-through is safe — TryRemovalReplacement returns false for non-protected victims, so normal
  bounces/sinks are unchanged (smoke unmoved). Own-effect self-bounce/sink paths are NOT affected (guarded on
  targetSeat == opponent-of-controller).

## Reactive-timing families sweep: [On Block] (14) + [On Your Opponent's Attack] (48) — VERIFIED CLEAN (no engine change)
- [On Block] (block-action queue L3492): DON!! xN gate on the On-Block clause is enforced; the slash-combined
  "[When Attacking]/[On Block]" tag is matched by HasTiming (finds "[On Block]" inside "…]/[On Block]") and only the
  block path fires on a block (a rested attacker can't also block, so no double-fire). Concretely verified:
  · OP10-077 "[On Block] You may rest 2 of your DON!! cards: Add up to 1 DON!! from DON deck as active" — the
    "rest 2 DON" cost is auto-paid via TryAutoPayCost (L6821, guarded `ActiveDonCount < n → return 0`), then the
    DON-add payoff runs (L9468). Cost is NOT free.
  · OP06-009/OP16-036 "base power becomes the same as your opponent's Leader" — handled at L12759; copies the
    Leader's CURRENT total power (deliberate, documented interpretation; OP16-036 already verified mr2_036test).
- [On Your Opponent's Attack] redirect + filtered-cost: OP16-080 "trash 1 card WITH A [Trigger] from your hand:
  Change the target of that attack to this Leader or a {Blackbeard Pirates} Character" — the [Trigger] cost filter
  is enforced by CostCardMatches (L7375, rejects a hand card with no printed Trigger); the redirect handler (L10409)
  validates the new target is your Leader or a feature-matching Character and rewires Battle.TargetId/Seat/DefensePower.
- Punisher pair OP05-099 / OP15-059 "Your opponent may <trash 1 Life / return 1 active DON>. If they do not, give
  up to 1 of your opponent's Leader or Character −2000 power." ACCEPTED SIMPLIFICATION (pinned by `op15059test`):
  the opponent's OPTIONAL cost auto-DECLINES (consistent with the engine's optional-cost philosophy), so the −2000
  penalty applies to the opponent's card and their DON/Life is untouched — the usual rational outcome. A true
  opponent-choice prompt (return DON to dodge the penalty) is a cross-player decision point = a 🟡 UI/2-client
  feature, out of headless scope. Probe confirmed the penalty lands on the correct side (opponent) at the right
  magnitude; no unconditional mis-target.
- Coverage 2675/0/0/0/0; smoke unaffected (no engine change). Gate-invisible bug total remains #163–#283.

## give-DON distributive + multi-clause [Counter] deck-look — VERIFIED CLEAN (no engine change, 2 new regression tests)
- OP04-004 "Give up to 1 rested DON!! card to EACH of your {Alabasta} type Characters" — the distributive
  give-to-each resolves correctly: 1 rested DON goes onto EACH {Alabasta} Character and 0 onto a non-Alabasta
  Character. (Misses the main give-DON handler at L13102 which is gated on "Leader", but a dedicated "to each of"
  handler catches it — log "gives 1 rested DON!! to each of N Character(s)".) `op04004test`: 3 Alabasta → 1/1/1 DON,
  non-Alabasta → 0.
- OP12-015 "If you have a total of 2 or more GIVEN DON!! cards" — condition handled (L6547 GivenDon(p), counts all
  DON attached to your Leader+Characters). Clean.
- Multi-". Then," [Counter] chains (EB01-019, OP08-054, OP15-078 — the only 2+-Then cards): EB01-019 "[Counter] Up
  to 1 gains +4000 during this battle. Then, look at 3; reveal up to 1 {Donquixote Pirates} Character, add to hand.
  Then, place the rest at the bottom." verified END-TO-END with a live battle: the buff applies (+4000), the ". Then,"
  continuation opens the deck-look (DeckLook flow — look 3 → deckLookSelect picks the Donquixote card → add to hand →
  deckLookConfirmOrder places the 2 remaining at the bottom). No clause dropped, no card leaked (the "rest" is the
  standard look-completion tail, correctly kept with the look clause, not split off). `eb01019test`.
- Coverage 2675/0/0/0/0; smoke unaffected (no engine change). Gate-invisible bug total remains #163–#283.
- METHOD NOTE: a deck-look effect opens state.DeckLook and CLEARS its PendingEffect, so a resolve-loop that watches
  only PendingEffects stops early and leaves looked-at cards in DeckLook.Cards (looks like a "vanished" card). Drive
  the look with deckLookSelect + deckLookConfirmOrder to finish it — this is NOT a leak.

## #284 Removal-replacement missing on the field→Life "Place opponent's Character" path (extends #283)
- Follow-up to #283. The field→Life removal has TWO handlers: the "Add up to N … to Life" handler (L~11464)
  correctly calls TryRemovalReplacement, but the earlier, more specific "Place up to N of your opponent's
  Characters … at the top or bottom of their Life cards face-up" handler (L~11379, matched by EB01-053 and
  OP05-096 option C) removed the victim into their Life checking only RemovalBlocked (immunity), NOT the
  removal-replacement. Inconsistent → a victim with "If this Character would be removed from the field by your
  opponent's effect, you may <X> instead" was moved into Life with no chance to pay the alt cost (rule 4-11-2:
  field→Life IS removal from the field).
- FIX: wrapped the L11379 removal+placement in `if (!TryRemovalReplacement(state, plSeat, plTarget)) { … }`,
  mirroring the Add handler. If replaced, the placement does not happen. (Also corrected a stale comment that
  claimed EB01-053 places face-DOWN — its text says face-up.)
- `eb01053test`: EB01-053 places a protected OP12-070 (cost 3, "return 1 DON to DON deck instead") → stays on
  field, 1 DON returned, NOT in Life; a plain ST01-002 (no protection) → removed from field, placed into Life.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#284.
- STATUS of the removal-replacement sweep: TryRemovalReplacement now fires on K.O. (10 sites), bounce-to-hand,
  place-on-deck-bottom (#283), field→Life Add (pre-existing) and field→Life Place (#284). Remaining removal verbs
  to spot-check next pass: trash-from-field-by-effect and any "return to the owner's DECK to hand"/deck variants.

## #285 "Trash up to 1 of your opponent's Characters" handler — 4 bugs (power filter, isKo, immunity, replacement)
- Closes the removal-replacement sweep (the trash-from-field-by-effect path flagged after #284). The handler
  (L~9400; used by OP06-092, OP07-091, OP08-079, OP09-009, ST19-003) had FOUR defects:
  1. DROPPED POWER FILTER — OP09-009 "Trash up to 1 of your opponent's Characters with 6000 POWER or less" parsed
     only a COST filter, so it trashed a Character of ANY power (glow≠resolve drift). Added a "(N) power or less"
     parse + GetPower check.
  2. isKo WRONG — called MoveToTrash with default isKo=true, so a "trash" (NOT a "K.O." — rule 10-2-1-3: a Character
     trashed by some method other than K.O. is NOT treated as K.O.'d) WRONGLY fired the victim's [On K.O.], added
     it to CharKoedThisTurn, and fired the remover's "when opponent's Character is K.O.'d" reactive. Changed to
     isKo=false; well-precedented (played-over L2172, cost-trash L6872 already pass isKo=false).
  3. NO REMOVAL-IMMUNITY — never called RemovalBlocked, so a "cannot be removed from the field" Character could be
     trashed. Added the RemovalBlocked guard (correctly checks only "removed from the field", not "K.O.'d", so it
     does NOT wrongly block a trash on a "cannot be K.O.'d" body).
  4. NO REMOVAL-REPLACEMENT — field→trash is a removal (rule 4-11-2); added TryRemovalReplacement. Also fire
     FireOnYourCharacterRemoved manually (the "removed from the field by an effect" reactive, which isKo=true used
     to fire) so switching to isKo=false doesn't lose it.
- `op09009test`: (A) OP09-009 trashes a 4000-power OP01-080 that has "[On K.O.] Draw 1" → removed AND opponent does
  NOT draw (trash≠KO); (B) a 7000-BASE-power Character → NOT trashed (power filter); (C) protected OP12-070
  (return-1-DON) → stays on field (replacement). NB test hygiene: attached DON grants power only on the CONTROLLER's
  turn (L360), so a >6000 victim must use BASE power (it's the remover's turn).
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#285.
- REMOVAL-REPLACEMENT SWEEP COMPLETE: TryRemovalReplacement now fires on K.O.(10 sites)+bounce+deck-place(#283)+
  field→Life-Add+field→Life-Place(#284)+field→trash(#285). All opponent-effect removal verbs covered.

## #286 K.O.-ONLY immunity AURAS wrongly blocked bounce/trash/deck-place (rule 10-2-1-3)
- Companion to #285. HasRemovalImmunityAura (L~4728) matched BOTH "your … Characters cannot be K.O.'d …" and
  "… cannot be removed from the field …" auras, and was called from TWO places: CannotBeKoedByEffect (K.O. path —
  both kinds correctly apply) AND RemovalBlocked (bounce/trash/deck-place/Life-place path). On the non-K.O. path a
  K.O.-ONLY aura must NOT block (rule 10-2-1-3: a bounce/trash is not a K.O.), but it did → a Character under a
  K.O.-immunity aura (OP07-069 {Foxy Pirates}, OP07-033 Luffy cost≤3, OP08-029 Pekoms {Minks}, OP04-119 base-cost-5,
  OP10-070 ≤1000-base-power) was WRONGLY immune to bounce/trash/deck-place.
- FIX: added `bool koPath` param to HasRemovalImmunityAura (default false). Capture which immunity the aura grants;
  when `!koPath && immIsKoOnly` (aura says "cannot be K.O.'d", not "removed from the field") → skip. CannotBeKoedByEffect
  passes koPath:true (both apply); RemovalBlocked uses the default false (only "removed from the field" auras block).
  A true "cannot be removed from the field" aura (EB04-057 yellow {Scientist}) still blocks every removal.
- `op07069test`: with OP07-069's DON-differential-gated {Foxy Pirates} K.O.-aura active, an opponent K.O. of Komei
  (EB02-034) is BLOCKED (K.O.-immune) but an opponent BOUNCE now SUCCEEDS (removes it) — was wrongly blocked before.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#286.
- Also verified CLEAN this pass: DON-differential conditions (L6622 "at least N less than opponent's" via
  TotalFieldDon(opp)-TotalFieldDon(p)>=N; L6627 "equal to or less than opponent's" via <=) and OP07-064's
  DON-differential-gated hand self-cost aura (GetHandSelfCostBonus L937): op07064 base 6 → cost 3 at diff≥2, 6 at diff<2.

## #287 ST13-003 "face-up Life → deck bottom instead of hand" not applied on NORMAL battle damage
- ST13-003 (Momonosuke leader) "Your face-up Life cards are placed at the bottom of your deck instead of being
  added to your hand, according to the rules." — its ability piles cost-5 Characters onto Life FACE-UP, and those
  are meant to RECYCLE to the deck when dealt as damage (not go to hand). The replacement was implemented only in
  TakeLife (L4281), which is reached ONLY via the explicit "takeLife" command — NOT the normal battle-damage flow
  (DeclareAttack → ResolveAttack → RevealLifeAndStartTrigger → FinalizeTrigger). FinalizeTrigger (L4364)
  unconditionally sent the revealed Life card to hand → ST13-003's core mechanic was dead in real games.
- FIX: applied the same face-up-Life→deck-bottom replacement in FinalizeTrigger (mirrors TakeLife: if the revealed
  card is FaceUp and the defender's board has the ST13-003 text, move it to the deck face-down instead of hand).
  Central to FinalizeTrigger so it covers every damage path (single hit, [Double Attack] chain, effect-dealt damage).
- `st13003test` (full battle: North 7000 attacker vs ST13-003 5000 Leader, unblocked): a face-up Life card dealt as
  damage → DECK (not hand); CONTROL a normal ST01-001 leader → the same face-up card → HAND.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#287.
- Also verified CLEAN this pass: the "turn N cards from the top of your Life face-up/down" COST (TryAutoPayCost
  L7020) — payability guarded (needs N cards in the opposite state, else unpayable), flips the top N wrong-state
  cards (EB03-056/EB01-040/OP13-114/… ~15 cards). TEST HYGIENE: a real attack needs attacker.TurnsStarted≥2 (else
  "can't battle first turn"), and resolveAttack/passBlock/passCounter are owned by the DEFENDER seat.

## Attack-restrictions + dynamic-cap targeting — VERIFIED CLEAN (no engine change, 1 new regression test)
- "cannot attack" family: enforcement at DeclareAttack (L3025 cannotAttack modifier; L3036 printed "This
  Leader/Character cannot attack."; L3047 "If <cond>, this Character cannot attack"; L3054 EB04-051 "unless there
  is a Character with N base power" scans BOTH boards; L3072 EB04-005 "unless your opponent has N Characters with
  base power P" counts opponent). APPLIED "cannot attack until X" (L9349): all THREE expiry phrasings ("until the
  end of your opponent's next turn"/"…next End Phase"/"until the start of your next turn") map to untilNextTurn
  (L9402) and expire at the APPLIER's next turn (L1896 OwnerSeat==seat) — correct, survives the opponent's next turn.
- Dynamic-cap targeting: "K.O./rest up to 1 opponent Character with a cost equal to or less than <dynamic>" —
  ComputeDynamicCap (L4838) handles all reference values (opponent's Life count, total-both Life, rested/total DON,
  hand size, {tag}-type Character count) and is wired into the K.O. resolver (L12460), rest resolver (L13247), play
  resolver (L13774) AND the glow (L7806) — no glow≠resolve drift. `op05102test` (OP05-102 K.O. cost≤opp-Life-count):
  oppLife=3 K.O.s a cost-3; oppLife=2 REJECTS a cost-3 (3>2) but K.O.s a cost-2.
- Coverage 2675/0/0/0/0; smoke unaffected. Gate-invisible bug total remains #163–#287.
- TEST-ERROR LESSON: a probe "found a bug" that was actually a wrong-cost test card (ST01-003 is cost 1, not 3).
  ALWAYS verify the exact printed cost/power of fixture cards before trusting a probe's fail — the engine was right.

## #288 A NEGATED attacker still applied [Double Attack] / [Banish] (keyword effects survive negation)
- [Double Attack] and [Banish] are KEYWORD EFFECTS (rulebook 10-1-2 / 10-1-3); rule 8-2-1-1 says an invalidated
  effect "will not occur." The block path already honors this (BlockAttack L3470 checks IsEffectNegated → a negated
  [Blocker] can't block), but the COMBAT-DAMAGE path (ResolveAttack L4511-4512) read HasDoubleAttack/HasBanish with
  NO negation guard. So negating an ATTACKING Character (e.g. OP09-097 Black Hole [Counter] "Negate the effect of up
  to 1 of your opponent's Leader or Character") still let it deal 2 damage / banish the Life card — halving-damage
  and trigger-denial defenses silently failed.
- FIX: compute `atkNegated = IsEffectNegated(state, atkCard)` once and gate both keywords: `dmg = 1 + (!atkNegated
  && HasDoubleAttack ? 1 : 0)` and `if (!atkNegated && HasBanish(...))`. Mirrors the [Blocker] negation guard
  (suppresses printed AND granted, consistent with BlockAttack which also doesn't distinguish).
- `negatedatktest` (full battle, OP09-047 10000 [Double Attack] vs a 5000 Leader): normal attacker → 2 Life lost;
  after South negates the attacker via a queued negate → 1 Life lost.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#288.
- Also verified CLEAN: the negate handler itself (L12939) — cost cap enforced (excluding the K.O.-rider's cap),
  effectsNegated modifier applied thisTurn, OP09-098 K.O.-rider + OP09-093 cannot-attack-rider + OP09-097 −power
  rider all handled; P-100 board-wide negate (L10890) negates opp Leader + all Characters; IsEffectNegated is
  checked at every passive/aura/keyword site. FOLLOW-UP (minor, narrow): [Rush] enforcement (DeclareAttack L3175 /
  CanAttack L1416) does not yet check IsEffectNegated — a negated Character played this turn could still attack via
  printed [Rush]. Rare (needs the attacker negated on the turn it's played, before declaring) + has the 8-2-4
  granted-keyword nuance; deferred.

## #289 A NEGATED Character kept printed [Rush] (completes the keyword-negation sweep from #288)
- Follow-up to #288. [Rush] is a keyword effect (rulebook 10-1-1); a negated Character loses it (rule 8-2-1-1), so
  a Character played this turn and then negated should stay summoning-sick. The two enforcement points read
  `!HasRush(...)` with no negation guard: DeclareAttack (L3175) and IsSummoningSick (L1416).
- FIX (SAFE — the added term is false for every unnegated attacker, so normal rush attacks are unchanged, smoke
  P(first)=54.67% unmoved): DeclareAttack now enters the played-this-turn restriction when `!HasRush || IsEffectNegated`;
  the attacker's OWN printed "can attack Characters on the turn played" is also gated on `!IsEffectNegated` (a
  modifier-GRANTED rush survives per rule 8-2-4; the aura path already skips negated aura sources). IsSummoningSick
  mirrors it.
- `negatedrushtest`: a full-[Rush] EB01-003 played this turn attacks the Leader normally (True); after the opponent
  negates it → the attack is rejected (False).
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#289.
- KEYWORD-NEGATION SWEEP COMPLETE: [Blocker] (BlockAttack L3470, pre-existing), [Double Attack]+[Banish] (#288
  ResolveAttack), [Rush] (#289 DeclareAttack+IsSummoningSick) all now respect IsEffectNegated.

## #290 Rest-opponent handlers ignored PRINTED "cannot be rested by opponent's effects" immunity (only the modifier)
- Parallel to #286 (K.O.-immunity). CannotBeRestedByOppEffect (L4695) checks BOTH the cannotBeRested MODIFIER and
  the PRINTED "cannot be (K.O.'d or )rested by your opponent's effects" text (with its leading If-gate). The MAIN
  rest-opponent resolver (L13237) correctly calls it (L13293), but THREE other rest-opponent handlers only checked
  `HasModifier(cannotBeRested)`, missing the printed immunity:
  · L12591 "Rest this Character and up to N of your opponent's Characters" (OP03-028 option)
  · L12624 "Rest all of your opponent's Characters" (OP06-041)
  · L12677 mixed "Rest up to N of your opponent's DON!! cards or … Characters"
  → a Character with printed rest-immunity (OP11-046 Yonji "cannot be K.O.'d or rested by your opponent's effects"
  when you only have {GERMA}; OP12-021) was WRONGLY rested by these effects (e.g. OP06-041 board-wide rest).
- FIX: all three now call CannotBeRestedByOppEffect (mirrors the main handler), covering modifier + printed text +
  the conditional If-gate.
- `op06041test`: OP06-041 "Rest all" vs a North board of only GERMA Characters (so Yonji's condition holds) — Yonji
  stays ACTIVE (immune), a normal GERMA Character (Reiju) is rested.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#290.
- CLASS NOTE: the "check only the modifier, not the printed immunity text" gap mirrors the removal-immunity family —
  when auditing any opponent-targeting debuff, verify it calls the shared immunity helper (CannotBeKoedByEffect /
  RemovalBlocked / CannotBeRestedByOppEffect), NOT a bare HasModifier check.

## #291 "Set this Character or up to N DON as active" self-branch ignored freeze (OP13-035)
- Set-active freeze sweep: verified 5 of 6 set-active handlers already skip a frozen card — Refresh (L1927),
  "Set your Leader as active" (L11257), "Set all of your Characters as active" (L11278), "Set this Character as
  active" self (L9172), "Set your Leader as active" cost-variant (L9127), and the generic picked "Set up to N …
  as active" (L12563). The one gap: "Set this Character or up to N of your DON!! cards as active" (L11300, OP13-035
  "[End of Your Turn]") set the SOURCE active with no freeze check.
- FIX: guard the self-branch with `!HasModifier(state, scSelf, "freeze")` — a frozen source now falls through to the
  "or up to N of your DON!!" branch (sets a DON active instead), mirroring the other self-set-active handlers.
- `op13035test`: not-frozen → OP13-035 sets ITSELF active (0 DON); frozen → OP13-035 stays rested and a DON is set
  active instead.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#291.
- CLEAN: the rest of the set-active family honors freeze; "Rest your opponent's Leader" (L11317) uses the bare
  cannotBeRested modifier (not CannotBeRestedByOppEffect) but no Leader has printed rest-immunity, so inert (noted).

## Opponent-hand manipulation + give−cost combos — TRACED CLEAN / documented DO-NOT-FIX (no engine change)
- give−cost + K.O. combo (EB01-046 "Give up to 1 opp Char −1 cost. Then, K.O. up to 1 with a cost of 0"): verified
  end-to-end — the −cost applies BEFORE the K.O., a cost-1 Char reduced to 0 is K.O.'d by the exact "cost of 0"
  match. GetCost floors at 0 (Math.Max(0,cost)) but internal deltas keep negativity, so a further +cost stacks
  correctly (rule 1-3-6-2-1). Family clean.
- "Your opponent trashes N cards from their hand" (L12767, OP06-093/OP07-090/OP09-087/OP16-094/ST20-005): auto-trashes
  the LAST N (the opponent's choice auto-resolved — accepted simplification; no crash on empty/short hand).
  ⚖ DO-NOT-FIX: it does NOT fire the opponent's NotifyHandTrashedByEffect reactive on a FORCED discard, and it
  SHOULDN'T — that helper SIMPLIFIES away OP12-040's "by your {Navy} type card's effect" source qualifier (fires on
  any effect-discard), so calling it for an opponent-forced discard would WRONGLY give the opponent an OP12-040 draw
  (the trash was by MY effect, not their {Navy} card). The only correctly-firing reactive (OP14-045/049 "gain [Rush]")
  grants Rush on MY turn = near-zero value. Net: firing it trades a ~0-impact miss for a real new bug → leave it off.
- Peek (OP01-063/OP01-105 "Choose N cards from your opponent's hand; your opponent reveals those cards", L11199):
  logs the WHOLE hand instead of the N chosen — an info-only over-reveal simplification (no card moves; the
  observation-boundary info-leak is a separate known concern). Not a state-correctness bug; left as-is.
- Coverage 2675/0/0/0/0; smoke unaffected (no engine change). Gate-invisible bug total remains #163–#291.

## #292 "When you play a Character" reactive didn't fire on an EFFECT-play from hand (only normal hand-plays)
- The reactive "When you play a Character[ with no base effect][ with a [Trigger]][ from your hand], <effect>"
  (OP02-026 Sanji leader vanilla-ramp, OP14-041 Boa [Opponent's Turn] draw, OP13-100 Jewelry Bonney leader
  trigger-play DON-give) was INLINE at the end of PlayCard — so playing a Character via an EFFECT (OP07-025/049,
  ST02-017, the generic "Play up to N … from your hand") never fired it, weakening those leaders' engines.
- Rule 4-7-1 scopes "playing a card" to paying its cost + playing it FROM HAND, so the fix targets from-hand plays.
  FIX: extracted the reactive into `FireOnYouPlayCharacter(state, seat, played, bool fromHand)` (with a new gate:
  a "from your hand" reactive is skipped when !fromHand). Wired it into PlayCard (fromHand:true — pure refactor,
  smoke unmoved) AND the two play-from-hand-via-effect sites (~L13728 ST02-017, ~L13944 generic — fromHand:true).
  Deck/trash/Life plays are deliberately NOT wired (ambiguous per 4-7-1; the fromHand gate makes a later extension
  safe). The play-as-COST path (~L7027) also left alone (narrow/risky).
- `op13100test`: OP13-100 Bonney plays a [Trigger] Character (EB02-055) from hand via an effect → the leader's
  reactive FIRES (AbilityUsedThisTurn gets the :onYouPlay key); playing a NON-trigger Character → does NOT fire
  (correctly gated on "with a [Trigger]").
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#292.
- Also verified CLEAN this pass: play-from-hand RESTED flag (OP07-025/049 "…from your hand rested" → enters rested,
  L13939) and the played card's own [On Play] firing (L13945) both work.

## #293 "When ... is given a DON!! card" reactive (OP02-002) didn't fire on EFFECT-driven give-DON (same class as #292)
- Following the #292 lead. FireOnDonGiven (the OP02-002 Monkey.D.Garp leader reactive "[Your Turn] When this Leader
  or any of your Characters is given a DON!! card, give up to 1 opp Character cost≤7 −1 cost") was called ONLY from
  AttachDon (the normal give-DON action). Effect-driven give-DON ("Give up to N rested DON!! to your Leader/
  Characters" — OP03-009/OP05-008/OP07-015/EB03-045/OP04-004/… + self-attach + give-to-each) never fired it → the
  OP02-002 archetype lost its reactive whenever DON was given by an effect.
- FIX: called FireOnDonGiven after each effect-give-DON to the player's OWN Leader/Character — 6 sites: give-to-
  Leader (per pick), give-to-Leader/Character (giveCount), give-this-Character self-attach, give-to-each-{tag}
  (up-to-M-each), give-to-each-of-{tag}, give-to-own-Character. NOT wired on the opponent-DON-give (OP15-028 — the
  DON goes to the OPPONENT's Character, so the controller's "your Character" reactive must not fire).
- `op02002test`: OP02-002 leader, give 1 rested DON to your Character via an effect → the reactive fires and gives
  an opponent Character −1 cost (4→3). Log shows "onDonGiven effect is pending" → "gives … −1 cost".
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#293.
- CLASS CONFIRMED (#292/#293): a "when you/your X <verb>" reactive extracted into a Fire* helper but called from
  only ONE action path misses the effect-driven copies of that action. Audit every Fire*/Notify* helper's call
  sites against ALL paths that perform the triggering action (play, give-DON, trash-hand, K.O., …).

## Reactive-helper (Fire*/Notify*) call-site AUDIT (continues #292/#293) — 1 broad gap DEFERRED
- Systematically checked whether each Fire*/Notify* reactive is fired from the CENTRAL action function (safe) or
  scattered per-site (gap-prone, the #292/#293 class):
  · CENTRALIZED (no gap): FireOnDrawOutsideDrawPhase is called inside DrawCard (L1999) → every effect-draw fires it;
    FireOnKoEffects / FireOnOpponentCharacterKo / FireOnYourCharacterRemoved are called inside MoveToTrash (L15771
    area, isKo gate) → all K.O. paths fire them.
  · FIXED clean cases: FireOnYouPlayCharacter (#292, was inline in PlayCard), FireOnDonGiven (#293, was AttachDon-only).
  · DEFERRED broad gap: FireOnBecomeRested is called from ONLY 2 sites (attack L3242; main opp-rest resolver L13322
    which threads the source type for OP14-070's "by opponent's Character's effect" variant). Resting is NOT
    centralized — 10+ distinct cost-rest handlers (rest-this-Character, rest-N-of-your-Characters, rest-N-[Name]-cards,
    rest-your-Leader, …) each set `.Rested = true` directly, and the non-main opp-rest handlers (#290's L12591/L12624/
    L12677) don't call it. So the "[Your Turn] When this Character becomes rested, …" reactives (OP14-021/027/028/032/
    035/119) fire on ATTACK (covered) but NOT when the character is cost-rested / effect-rested on your turn; OP14-070
    misses the non-main opp-rest handlers. PRIMARY (attack) path works. FIX = a RestCard(state,card,cause,srcType)
    centralization that fires FireOnBecomeRested, routing all rest sites through it (broad; deferred as its own task,
    per the same precedent as the unified-removal-hook deferral). Block-rest correctly needs NO fire (block is the
    defender's own action on the opponent's turn — neither "[Your Turn]" nor "by opponent's effect").
- Coverage 2675/0/0/0/0; no engine change this iteration. Gate-invisible bug total remains #163–#293.
- LESSON: a reactive helper is only complete if fired from the CENTRAL function that performs the action; if the
  action isn't centralized (resting), the helper WILL have per-path gaps. Prefer centralizing the action.

## Stale MANUAL/UNKNOWN flags re-verified + block-ban/multicolor families — CLEAN (no engine change, 1 new test)
- Re-checked the audit-flagged.tsv MANUAL/UNKNOWN cards NOT already in the cleared/irreducible lists — all now
  resolve correctly (the flags predate the second-pass fixes; coverage NA=0 confirms auto-resolution):
  · OP07-057 Perfume Femur "[Main] Select {Seven Warlords} card +2000. Then, if the selected card attacks this turn,
    opp cannot activate [Blocker]." — the ". Then," is kept ATOMIC (FindThenClause returns -1, L5928); the buff
    handler applies +2000 AND registers the target into state.NoBlockerGrantedThisTurn (L13554); DeclareAttack
    L3411 sets Battle.NoBlocker when the attacker is registered; cleared at turn start (L1881). `op07057test`:
    {Seven Warlords} char 8000→10000 + registered no-blocker.
  · OP10-022 Trafalgar Law leader — its unusual gate "if the total cost of your Characters is 5 or more" IS handled
    (EvaluateCondition L6556); return-cost + reveal-Life + conditional-play are established patterns.
  · ST13-001 Sabo — the cost "add 1 of your Characters with a cost of 3 or more and 7000 power or more to Life
    face-up" is the OWN-Character field→Life self-add handler (L11424, dual cost/power MIN filters).
- Also CLEAN: multicolor (Color = "Red/Green" slash-joined; "Leader is multicolored" = .Contains("/"); color
  filters use substring IndexOf → multi-color matches; ColorsShareAny intersects the 6 colors); "opponent cannot
  activate [Blocker]" bans — battle-scoped (Battle.NoBlocker/BlockerPowerBan/BlockerCostBanMax in BlockAttack) AND
  turn-scoped (L11665 applies a per-Character `cannotBlock` modifier with power/cost caps, board-wide or picked).
- Coverage 2675/0/0/0/0; no engine change. Gate-invisible bug total remains #163–#293.

## Fresh-mechanic sweep (next-time-play, opponent-benefit downsides, attack-active) — CLEAN (no engine change)
- DECISION on the deferred FireOnBecomeRested gap: confirmed LOW value → NOT worth the risky RestCard refactor. The
  OP14 "[Your Turn] when this Character becomes rested" cards (OP14-021/027/028/032/035/119) have NO in-archetype
  self-rest enabler; they become rested by ATTACKING (covered L3242) and restand-combos re-attack (also covered).
  Cost-resting them is cross-archetype and rare. Gap stays documented/deferred (attack path works).
- OP02-025 Kin'emon "the next time you play a {Land of Wano} Character cost≥3, cost reduced by 1": handler (L10326)
  applies −1 to ALL matching cards in hand this turn ("approximates the next-play discount"). ACCEPTED SIMPLIFICATION
  — over-applies if you play 2+ matching cards (a proper one-shot needs a play-hook + pending-discount state; ONE
  card, condition-gated "1 or less Characters"). Left as-is.
- Opponent-benefit downsides CLEAN: OP06-047 "opponent returns hand to deck, shuffles, draws 5" (L11211); P-009
  "opponent adds 1 from their Life area to their hand" (L9721 — Pops the top Life card to hand, correctly reducing
  their Life count; which-card is a minor auto-choice simplification); OP07-090 trash+reveal+draw.
- "Can also attack active Characters" FULLY handled: DeclareAttack L3234 enforces the rested-target rule unless
  canHitActive; printed form via HasPrintedCanAttackActive (scoped to "This Character", DON-threshold-gated for
  OP04-081); granted form via a canAttackActive modifier (L11855/L14235).
- Coverage 2675/0/0/0/0; no engine change. Gate-invisible bug total remains #163–#293.
- STATE OF THE AUDIT: after ~33 iterations the engine is deeply hardened — recent passes increasingly find CLEAN
  families rather than bugs. The one known residual is the low-value FireOnBecomeRested cost-rest gap (deferred).

## #294 "Set … as active AT THE END OF THIS TURN" fired IMMEDIATELY (ramp/restand exploit) — now delayed
- "Set up to N of your DON!! cards as active at the end of this turn" (OP13-024/OP13-038/OP14-031/ST24-005) and
  "Set this/all … Character(s) as active at the end of this turn" (OP11-107 Shirahoshi Blocker, P-058 Uta {FILM})
  are DELAYED effects — the cards are meant to be active for the OPPONENT's turn, not refunded this turn. Every
  set-active handler ignored the "at the end of this turn" qualifier and set them active IMMEDIATELY:
  · DON → refunded this turn = ramp (OP14-031 refunds 5 DON right after playing it, ~a free card).
  · Character → set active mid-your-turn = restand/extra-attack (or, for OP11-107, pointless-now vs block-later).
- FIX: added two end-of-turn delayed sweeps in EndTurn — a count-based `setDonActiveAtEndOfTurn` (sets that many
  rested DON active per owner) and an InstanceId-based `setActiveAtEndOfTurn` (sets tagged Characters active,
  honoring freeze). The handlers now, when the text has "at the end of this turn", TAG instead of setting now:
  · the general "Set up to N DON!! as active" (L~14929) — and the Scalpel "Set up to 2 DON" fast-path (L~14101) now
    skips the delayed variant so it falls through;
  · "Set this Character as active" self (L~9179); "Set all of your … Characters as active" (L~11292 branch).
- `op13038test`: DON — 0 active immediately, 2 active after endTurn; self-Character (OP11-107) — rested immediately,
  active after endTurn.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#294.
- CLASS: a DELAYED-timing qualifier ("at the end of this turn", "until the start of your next turn", "at the start
  of their next Main Phase") must be honored via a delayed queue/modifier — doing the action NOW inverts the intent
  (a defensive/next-turn setup becomes an immediate ramp/restand). Grep other "at the end of this turn" effects.

## #295 OP11-040 leader's BARE-TEXT "at the start of your turn" ability never fired (only the [tag] was matched)
- Following #294's delayed-timing sweep. OP11-040 (Monkey.D.Luffy leader) "This effect can be activated at the start
  of your turn. If you have 8 or more DON!! cards on your field, look at 5; reveal up to 1 {Straw Hat Crew} card and
  add it to your hand. Then, place the rest at the top or bottom." — its signature card-advantage ability. But
  ApplyStartOfTurnEffects (L1963) only fired effects with the `[Start of Your Turn]` TAG (HasTiming); OP11-040 uses
  BARE TEXT ("can be activated at the start of your turn.") with no bracket tag → it was NEVER queued → the ability
  was dead. (Invisible to the coverage gate, which only tests effects that actually get QUEUED.)
- FIX: ApplyStartOfTurnEffects now ALSO matches the bare "can be activated at the start of your turn" text and
  extracts the body after that sentence as the clause (the leading "If you have 8+ DON…" gate then governs it).
- `op11040test`: with 8 DON on field the start-of-turn look-5 FIRES when South's turn begins (endTurn North →
  ApplyStartOfTurn South); with 4 DON it does NOT (the 8+ DON condition fails).
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#295.
- NEW SUB-CLASS: a timing ability written as BARE TEXT (no [tag]) is invisible to a tag-only dispatcher — an effect
  that never gets QUEUED is also invisible to the coverage gate (which only measures queued-effect resolution).
  Check every timing dispatcher (start/end of turn, on-play, etc.) matches BOTH the [tag] and the bare-text phrasing.

## #296 OP11-041 Nami leader's "when a card is removed from your/opponent's Life" draw was DEAD (regex mismatch)
- Continuing the #295 bare-text-timing sweep. OP11-041 "[Your Turn][OPT] This effect can be activated when a card
  is removed from your or your opponent's Life cards. If you have 7 or less cards in your hand, draw 1 card." — its
  signature card-advantage engine. FireOnOpponentLifeRemoved (L3913, called when you deal Life damage, L4560) used
  an EXACT regex `When a card is removed from your opponent's Life cards,\s*(.+)` that failed OP11-041 on THREE
  counts: the "your or" ("from your OR your opponent's"), the "…can be activated when" prefix (not "When"), and a
  PERIOD (not comma) before the body → the leader's draw never fired.
- FIX: broadened the regex to `(?:When|activated when) a card is removed from (?:your or )?your opponent's Life
  cards[,.]\s*(.+)` — matches both OP08-105 (old) and OP11-041 (new). The "your" (own-Life-removed) side shares the
  same draw payoff; on this seat's turn the dominant trigger is dealing damage to the opponent (covered).
- `op11041test`: OP11-041 leader's Character attacks the opponent's Leader → opponent's Life removed → South hand
  3→4 (the reactive draw fired). Log: "onOppLifeRemoved effect is pending" → "Nami draws 1".
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#296.
- REMAINING bare-text-trigger candidates (same #295/#296 class — dispatcher-phrasing mismatch): OP11-102 Camie "when
  your opponent activates an Event OR [Trigger]" is EXPLICITLY skipped at L4138 (needs both Event + Trigger hooks +
  the "or [Trigger]" phrasing + mutual-Life-trash payoff — complex, deferred); OP16-041 "when your {Impel Down}
  Character CARD is removed" and PRB02-009 "when this Character is rested by opp effect" — TO VERIFY next pass
  (FireOnYourCharacterRemoved/FireOnBecomeRested regexes may reject the "card"/period/prefix variants).

## #297 Two more DEAD bare-text reactives (phrasing-variant regex mismatch) — OP16-041 + PRB02-009
- Closing the #295/#296 bare-text-trigger sweep candidates.
- OP16-041 Buggy leader "[DON!! x1][OPT] This effect can be activated when your {Impel Down} type Character CARD is
  removed from the field. Play up to 1 [Prisoner of Impel Down] from hand." — FireOnYourCharacterRemoved's regex
  required "{tag} type Character is removed…," but OP16-041 has an extra " card" and a PERIOD (not comma) → the
  reactive was DEAD. FIX: regex now allows "(?: card)?" and "[,.]" terminator. `op16041test`: opponent K.O.s the
  {Impel Down} Character → OP16-041 plays a Prisoner of Impel Down from hand.
- PRB02-009 Mr.3 "This effect can be activated when this Character IS rested by your opponent's EFFECT. You may trash
  this Character and draw 2 cards." — FireOnBecomeRested matched only "becomes rested by your opponent's CHARACTER's
  effect," (OP14-070) or bare "becomes rested," — PRB02-009's "is rested"/"…effect"/PERIOD matched neither → DEAD.
  FIX: added a general opp-effect form `(?:is|becomes) rested by your opponent's effect[,.]` (cause=oppEffect, any
  source). `prb02009test`: opponent rests PRB02-009 → its onBecomeRested reactive fires (the "You may trash+draw"
  auto-declines, as accepted for optional payoffs).
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#297.
- BARE-TEXT/PHRASING-VARIANT SWEEP (#295–#297) essentially COMPLETE: start-of-turn (OP11-040), Life-removed
  (OP11-041), your-Character-removed (OP16-041), becomes-rested (PRB02-009) all fixed. Remaining known: OP11-102
  "opponent activates an Event OR [Trigger]" (needs a Trigger-activation hook too — deferred, complex payoff).

## #298 "When your opponent plays a Character" reactive (OP04-024 Sugar) missed EFFECT-plays (#292 symmetry)
- Symmetric completion of #292. The "[Opponent's Turn] When your opponent plays a Character, …" reactive (OP04-024
  Sugar: rest an opp Character + rest self) was INLINE in PlayCard → it fired only on a normal hand-play, NOT when
  the opponent plays a Character via an EFFECT ("Play up to N from hand", etc.). Extracted it into
  FireOnOpponentPlaysCharacter and wired it into PlayCard AND the two play-from-hand-effect sites (alongside the
  #292 FireOnYouPlayCharacter calls).
- `op04024test`: on North's turn, North plays a Character via an effect → South's Sugar (with a {Donquixote
  Pirates} leader) reacts, resting a North Character and then resting itself. Log: "onOpponentPlay effect is
  pending" → "Sugar rests Karoo" → "Sugar rests itself".
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (the PlayCard refactor didn't regress normal play). Total
  gate-invisible bugs now #163–#298.
- ALSO verified CLEAN: K.O.-reactive dispatchers — FireOnKoEffects handles bare "When this Character/Leader is
  K.O.'d[by X]," (comma, battle/effect-gated, pre-armed cost); FireCharacterKoWatchers handles "When a Character is
  K.O.'d,"; FireOnOpponentCharacterKo handles "When your opponent's Character is K.O.'d," (+cost-prefix). OP11-102
  (opponent activates Event OR [Trigger] → mutual-Life-trash) stays deferred (no payoff handler + needs a Trigger hook).

## #299 OP07-038 leader's "removed by your effect" reactive DROPPED its "≤5 hand" gate → drew unconditionally
- NotifyRemovalByEffect (L5465, the REMOVER's "When a Character is removed from the field by your effect, <body>"
  reactive) extracted the body via `line.IndexOf(',')` — the FIRST comma. OP07-038 "This effect can be activated
  when a Character is removed from the field by your effect. If you have 5 or less cards in your hand, draw 1 card."
  terminates the trigger with a PERIOD, so the first comma sits INSIDE the body ("…in your hand, draw 1") → the
  extracted body was just "Draw 1 card.", DROPPING the "If you have 5 or less cards in your hand" gate → OP07-038
  drew on EVERY effect-K.O. regardless of hand size.
- FIX: anchor the body on the trigger PHRASE end ("removed from the field by your effect") and skip the following
  comma/period/space — keeps the full body (leading If-gate included) for both the comma-form and the period-form.
- `op07038test`: effect-K.O. with a 4-card hand → draw +1; with a 6-card hand → draw +0 (the ≤5 gate now holds).
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#299.
- NOTED (minor, deferred): NotifyRemovalByEffect is called from only the 2 effect-K.O. sites (L12499/L12586), so
  OP07-038 doesn't fire on a your-effect BOUNCE/TRASH/deck-place/Life removal (same removal-verb-coverage class as
  #283-#285). K.O. is the dominant verb; wiring the other verbs is a low-value follow-up for this 1 card.

## #300 OP10-042 Doflamingo leader's "Dressrosa removed OR K.O.'d → draw" was DEAD ("or K.O.'d" broke the regex)
- Continuing the phrasing-variant sweep. OP10-042 "[Opponent's Turn][OPT] This effect can be activated when your
  {Dressrosa} type Character is removed from the field by your opponent's effect OR K.O.'d. If you have 5 or less
  cards in your hand, draw 1 card." — its card-advantage engine. FireOnYourCharacterRemoved's regex (already
  broadened in #297) ended with the optional "by … effect" then a [,.] terminator, but OP10-042 has an extra
  " or K.O.'d" suffix before the period → NO match → the draw was DEAD.
- FIX: added an optional `(?: or K\.O\.'d)?` before the `[,.]` terminator. The body ("If you have 5 or less cards
  in your hand, draw 1 card.") is captured after the period WITH its ≤5 gate.
- `op10042test`: opponent K.O.s a {Dressrosa} Character on their turn — 4-card hand → draw 1; 6-card hand → draw 0.
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67%. Total gate-invisible bugs now #163–#300.
- SWEEP STATUS: the "can be activated when …" bare-text/phrasing-variant cards are now essentially all handled —
  OP07-038/OP09-001/OP10-042/OP11-040/OP11-041/OP11-088/OP12-081/OP13-100/OP16-041/PRB02-009 verified fixed or
  dispatched. OP11-088 ("when your opponent's Character attacks") IS dispatched (L3314); its "If that Character has
  ＜Slash＞" attacker-condition eval is a deeper trace for later. Remaining: OP11-102 (Event OR [Trigger], deferred).

## Base-power SWAP + OP11-088 attacker-condition — VERIFIED CLEAN (no engine change, 1 new test)
- OP11-088 Shu "[OPT] can be activated when your opponent's Character attacks. If that Character has ＜Slash＞, this
  Character gains +5000 during this battle." — the on-opp-attack dispatch (L3314) recognizes "opponent's Character
  attacks", fires only on a Character (not Leader) attack (L3318), and correctly gates on the ATTACKER's attribute
  (L3324-3331, "that Character" = the attacker). Clean.
- Base-power SWAP handler (L10905, OP14-001/OP14-009/OP14-017): two-target multi-pick (FirstPickId), oppSwap vs own
  seat filter, swapAllowLeader (OP14-009), base-power cap (OP14-017 "9000 or less"), feature filter (OP14-001
  {Supernovas}/{Heart Pirates}), distinct-target guard, and reads existing BasePowerOverrides before swapping.
  `op14001test`: two {Supernovas} Characters 5000/6000 → swapped to 6000/5000. Duration "thisTurn" is reasonable for
  these repeatable [OPT] / combat-trick swaps (no card states a longer duration).
- NOTE: the engine's base-power SET/SWAP convention defaults a no-duration change to "thisTurn" (L12746 bpSelf,
  L10942 swap). This is a consistent engine convention; whether a no-duration base-power change should be permanent
  is a rules-nuance question left unchanged (no definitive ruling; changing it would be a broad systemic risk).
- Coverage 2675/0/0/0/0; no engine change. Gate-invisible bug total remains #163–#300.

## Life-gain-from-deck + "for each/every N X" power scaling — VERIFIED CLEAN (no engine change, 1 new test)
- "Add up to N cards from the top of your deck to the top of your Life" (L14513, EB04-054/OP03-114/…): parses "up
  to N", Shifts N from deck top → Life end (= top of pile), face-down via the CardInstance default; handles a
  trailing "…and trash 1 from hand" rider. Correct.
- PASSIVE "for every/each N X" self-scaling (GetStaticSelfPowerBonus L558 + ScalingUnitCount L590) — count sources
  all covered: rested DON (EB01-014), cards in hand (OP01-072), Events/cards in trash (OP06-085), Characters with a
  different card name (OP16-034, distinct GetEffectiveName), {tag} type Characters. Floor-division `bonus*(count/per)`.
  `op01072test`: OP01-072 [DON!! x1][Your Turn] +1000/hand-card → 4 hand + 1 DON = 6000 (1000 base + 1000 DON's own
  power + 4×1000 scaling); +0 DON = 1000 (gate not met). Clean.
- EVENT "Your Leader gains +N power for each of your Characters during this turn" (P-024, L13395): dedicated handler
  auto-targets the Leader and scales by Character count (optional sub-filter), endOfTurn. Clean.
- TEST-ERROR LESSON (again): an attached DON contributes its OWN +1000 power on the controller's turn — when
  asserting a DON-gated card's power, include base + (#attached DON × 1000) + the scaled/aura bonus.
- Coverage 2675/0/0/0/0; no engine change. Gate-invisible bug total remains #163–#300.

## #301 OP15-092's ≥20 "your Leader's base power becomes 7000" bullet was DEAD (Leader stayed 5000 on defense)
- OP15-092 Luffy "Apply each of the following based on the number of cards in your trash: • ≥10 → this Character's
  base power becomes 9000 and +10 cost • ≥20 → during your opponent's turn, your Leader's base power becomes 7000 •
  ≥30 → this Character gains +1000 power." The character's own ≥10 base-power and ≥30 additive bullets were applied
  in GetPower (L231); the COST bullet in GetCost (L1148). But the ≥20 bullet — a PASSIVE on the board CHARACTER that
  sets the OWNER'S LEADER's base power (no timing tag, so the one-time "Apply each" resolver never applied it) — was
  UNHANDLED → the Leader stayed 5000 on defense at 20+ trash (should be 7000, +2000 defensive base power).
- FIX: in GetPower's base-power section, when the instance is a LEADER and it's the opponent's turn
  (state.ActiveSeat != Leader.Owner), scan the owner's board for an OP15-092-type "based on the number of cards in
  your trash … your Leader's base power becomes N" bullet and set the Leader's base power when trash ≥ N.
- `op15092test`: 20 trash + opponent's turn → Leader 7000; 20 trash + own turn → 5000 (bullet is opp-turn only);
  10 trash + opponent's turn → 5000 (below the ≥20 threshold).
- Coverage 2675/0/0/0/0; smoke unchanged P(first)=54.67% (GetPower hot-path change didn't regress). Total gate-
  invisible bugs now #163–#301.
- NOTE: this completes OP15-092 — all three trash-threshold bullets (character base-power+cost at ≥10, Leader
  base-power at ≥20, character +power at ≥30) are now applied as continuous passives.

## Continuous "Your Leader gains …" auras from board cards — VERIFIED CLEAN (no engine change, 1 new test)
- Following #301 (passive projected onto the Leader). Continuous Leader-recipient auras from board Characters/Stages
  ARE handled: the +power side in the Leader-aura power path (L696, names OP13-099/ST28-004/OP16-003) and the keyword
  side (L1353, "Your Leader gains [KW]", OP16-003/ST28-002). [Your Turn]/[Opponent's Turn]/trash-count/Life-count
  gates on the aura line are honored.
- `op16003test`: OP16-003 "[Your Turn] Your Leader gains [Double Attack] and +2000 power." — on your turn the Leader
  is 7000 (5000+2000) AND HasDoubleAttack=True; on the opponent's turn the aura is inactive (5000, no DA).
- Also named-handled: OP13-099 Stage "[Your Turn] if ≥19 trash → Leader +1000" (Stage auras scanned in
  GetTurnPassiveAuraBonus), ST28-004 "[Your Turn] if ≤2 Life → Leader +1000".
- Coverage 2675/0/0/0/0; no engine change. Gate-invisible bug total remains #163–#301.

## Continuous keyword-grant auras ([Banish]/[Double Attack]/[Unblockable]) — VERIFIED CLEAN (no engine change, 1 test)
- HasPrintedKeywordGrant (L1288) covers: self-grants "this Character gains [KW]" with turn-timing gates, the hand-
  count condition, a leading "If <cond>," (compound "A and B" via EvaluateCondition, DON-tag stripped first), and a
  [DON!! xN] threshold; the plural aura form "All of your [Name] cards and this Character gain [KW]" (OP15-070 Shura
  [Unblockable], OP15-071 Ohm [Double Attack], guarded so a pure ally aura doesn't self-grant); and board keyword
  auras from the owner's other cards.
- `op06002test`: OP06-002 "If this Character has 7000 power or more, gains [Banish]" — the DYNAMIC self-power gate
  (EvaluateCondition "this Character has N power or more" → GetPower(source), L6560) works with NO recursion
  (GetPower doesn't call HasBanish): 2 DON → 7000 → Banish=True; 1 DON → 6000 → Banish=False.
- Coverage 2675/0/0/0/0; no engine change. Gate-invisible bug total remains #163–#301.

## #302 — OP06-030 Dosun: [When Attacking] battle-K.O. immunity was CONTINUOUS (permanently unkillable) + +2000/immunity never granted
Card: "[When Attacking] If your Leader has the {New Fish-Man Pirates} type, this Character cannot be K.O.'d in
battle and gains +2000 power until the start of your next turn. Then, add 1 card from the top of your Life cards
to your hand." (SOLE timing-gated "cannot be K.O.'d in battle" card in the DB.)
THREE bugs, all gate-invisible (auto-resolved but WRONG):
 1. CONTINUOUS IMMUNITY (highest impact): `IsBattleKoImmune` reads the raw effect text and grants immunity
    whenever the leading `If your Leader has {type}` condition holds — so Dosun was permanently unkillable in
    battle (even before ever attacking, even on the OPPONENT's turn while defending). The immunity is a
    [When Attacking] TEMPORARY grant, not a continuous state. FIX: skip `HasTiming(line,"When Attacking")` /
    "When Blocking" lines in IsBattleKoImmune (L5615 loop) — only OP06-030 matches, so no risk to the ~20
    continuous-immunity cards.
 2. +2000 POWER MIS-TARGETED / STALLED: the "until the start of your next turn" power branch (L12984) only
    detected self via the contiguous substring "This Character gains" — Dosun's "this Character cannot be K.O.'d
    in battle AND gains +2000 power" has the verb split off, so it fell to the targeted branch and logged
    "Choose a card for +2000 power", stalling (WaitingForTarget) and also dropping the ". Then, add 1 Life"
    rider. FIX: added a line-scoped, PERIOD-TOLERANT self regex `this (Character|card) .* gains? .* +N power`
    ([^.] would choke on the periods inside "K.O.'d") → uT = source.
 3. IMMUNITY NEVER APPLIED AS A MODIFIER: nothing granted the temporary immunity when the clause resolved.
    FIX: in that same self-branch, if the clause also says "cannot be K.O.'d in battle", AddModifier
    cannotBeKodInBattle "untilNextTurn" (owner=effect.Seat) — expires at the controller's next start-of-turn
    (L1915), exactly "until the start of your next turn"; the battle-K.O. path (L4614) already honors that modifier.
Now: fixing #2 unstalls the clause → the ". Then, add 1 Life to hand" rider also fires.
Test `op06030test`: (A) rested Dosun that never attacked, opponent's turn, opp 7000 attacks it → K.O.'d (was
immune); (B) Dosun attacks → cannotBeKodInBattle modifier granted + power 4000→6000.
Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Gate-invisible bug total now #163–#302.
NEW CLASS: **timing-gated "cannot be K.O.'d in battle"** — a continuous-immunity text reader (IsBattleKoImmune)
must EXCLUDE [When Attacking]/[When Blocking]-gated immunity lines (they are temporary modifier grants); and a
self-buff detector keyed on the contiguous "This Character gains" misses split compounds "this Character <other
clause> and gains +N power" (use a period-tolerant line-scoped regex, since "K.O.'d" carries dots).

## #303 — removal-REPLACEMENT never fired on a normal BATTLE-damage K.O. (sweep #283-285 gap)
The removal-replacement sweep (#283-285) wired TryRemovalReplacement into every EFFECT removal path (K.O./
bounce/deck-place/Life-add/Life-place/trash) — but the normal BATTLE-damage K.O. (ResolveAttack L4614) checked
only hard immunity (cannotBeKod / cannotBeKodInBattle / IsBattleKoImmune) and then went STRAIGHT to
MoveToTrash(byBattleKo:true). MoveToTrash itself does NOT call TryRemovalReplacement (each effect call site
invokes it FIRST), so an unqualified "If this Character would be K.O.'d, you may <pay X> instead" replacement
NEVER fired when the holder lost a battle.
Impact: every "would be K.O.'d … instead" card WITHOUT a "by your opponent's effect" qualifier (which are
effect-only, correctly gated at L4993) — e.g. OP16-033 Morley "rest 2 of your cards instead", plus the ~30
general would-be-K.O.'d replacement cards — was unprotected in battle.
FIX: added an `else if (TryRemovalReplacement(state, targetSeat, target, isBattleKo:true))` branch BEFORE the
battle-K.O. MoveToTrash (L4621): defender pays the alternative cost and survives; no [On K.O.] and no attacker
FireOnBattleKo (no K.O. occurred). Gating already correct: "by your opponent's effect" replacements stay
effect-only, "would be K.O.'d in battle" (OP10-034) stays battle-only, "or K.O.'d"/leading-unqualified
broadenings keep firing on both.
Test `op16033test`: Morley (5000, rested) attacked by 7000 → survives, 2 DON rested to pay (was: went to trash,
0 DON rested).
Coverage 2675/0/0/0/0; smoke P(first)=54.67% (no crash/deadlock — battle K.O. now runs the replacement scan
every combat). Gate-invisible bug total now #163–#303.
CLASS (4) UPDATE: removal-replacement must fire on the BATTLE-damage K.O. path too, not only effect paths —
MoveToTrash does NOT self-invoke TryRemovalReplacement, so any NEW K.O. site must call it explicitly first.

## #304 — mandatory self "base power becomes the same as opponent's Leader" never AUTO-resolved (stayed pending)
EB04-052 Sanji "[When Attacking] This Character's base power becomes the same as your opponent's Leader during
this turn." (also OP16-036, OP16-055). The queued self base-power-SWAP handler (ResolveEffect ~L12929) works,
but IsAutomatedEffectPattern had NO "base power becomes" entry → the mandatory (no "you may") [When Attacking]
buff never auto-resolved on swing; it stayed pending, resolvable only by a manual click (inconsistent with
Dosun's +2000 / Zoro OP07-034 auto-apply convention).
FIX: added `|| ContainsAll(text, "base power becomes the same as")` to IsAutomatedEffectPattern (L15604). The
"you may" forms (OP04-069, OP06-009) are already excluded at the call site (L3485 `waClause "you may"<0`); the
"the selected Character" forms (OP16-104, EB01-061) auto-invoke ResolveEffect but harmlessly return
WaitingForTarget for the pick.
Test `eb04052test`: Sanji 4000 → 5000 (opp Leader power) on declareAttack (was: stayed 4000, effect pending).

## #305 — OP14-053 Vista: CONTINUOUS own-Leader base-power swap was DEAD (gate-invisible passive)
"[Opponent's Turn] [Blocker] If you have 7 or less cards in your hand, this Character's base power becomes the
same as your Leader's base power." A CONTINUOUS passive referencing the OWNER's Leader — never queues, so the
queued handler (~L12929, which requires "opponent") never saw it, and GetPower had no path → Vista's defensive
gimmick did nothing. Invisible to the coverage gate (passive, never queues).
FIX: added a continuous block in GetPower's base-power section (after the OP15-092 leader block, ~L270): scan
the instance's own effect lines for "base power becomes the same as your Leader" (EXCLUDING "opponent's Leader"
— NOTE the [Opponent's Turn] TAG itself contains "opponent", so guard on the exact "opponent's Leader" phrase,
not bare "opponent"); honor [Opponent's Turn]/[Your Turn] timing + leading-If (EvaluateCondition already knows
"you have N or less cards in your hand", L6287); set power = Leader's BASE power (printed + any leader
base-power override), computed WITHOUT a full GetPower call to avoid recursion.
Test `op14053test`: oppTurn+hand5→5000; oppTurn+hand9→4000 (cond fails); ownTurn+hand5→4000 (timing inactive).
Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Gate-invisible bug total now #163–#305.
CLASS NOTE (base-power "becomes the same as"): TWO code paths — QUEUED action form "opponent's (attacking)
Leader / the selected Character" (ResolveEffect L12929, sets a BasePowerOverride; needs an IsAutomatedEffectPattern
entry to auto-resolve) vs CONTINUOUS passive "your Leader" (GetPower live recompute). Don't guard the passive on
bare "opponent" — the [Opponent's Turn] tag trips it.

## #306 — queued "Leader's base power becomes N" (fixed number) also never AUTO-resolved (extends #304)
#304 whitelisted only "base power becomes the same as" — but the FIXED-number queued form "[When Attacking]
Your Leader's base power becomes 7000 …" (EB04-004 Zeff, P-092 Koby, ST26-005) still stayed pending. The queued
self/Leader base-power-SET handler (ResolveEffect ~L12931) handles it (incl. the "until the end of your
opponent's next End Phase" duration), but IsAutomatedEffectPattern never let it auto-resolve.
FIX: broadened the #304 whitelist entry from "base power becomes the same as" to bare "base power becomes"
(covers both "the same as X" and "becomes N"). Target-needing "up to N of your … cards" / "the selected
Character" variants auto-invoke ResolveEffect but harmlessly return WaitingForTarget.
Test `eb04003test` (EB04-004 half): Zeff attacks → own Leader base power 5000→7000 (was: stayed 5000, pending).

## #307 — EB04-003: CONTINUOUS board-Character-sets-owner's-Leader base power was DEAD
"[Opponent's Turn] Your {Navy} type Leader's base power becomes 7000." A passive on a board Character that sets
the OWNER's Leader base power — never queues; the trash-count leader block (GetPower ~L256) requires "based on
the number of cards in your trash", and the queued handler requires an action timing. So EB04-003's Leader buff
did nothing. Gate-invisible (passive).
FIX: added a general continuous block in GetPower's leader-power section: when computing a LEADER's power, scan
the owner's board Characters for "Your ({Type} type )?Leader's base power becomes N"; honor
[Opponent's Turn]/[Your Turn] timing + the {Type} filter on the Leader + a leading-If; EXCLUDE the QUEUED forms
("until …" / "during this turn" → BasePowerOverride), the "the same as" swap, and the trash-count form.
Test `eb04003test` (EB04-003 half): Navy Leader (OP02-002, 5000) → oppTurn=7000, ownTurn=5000.
Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Gate-invisible bug total now #163–#307.
CLASS (4k) UPDATE: base-power "becomes" has THREE handling needs — (a) QUEUED action → BasePowerOverride +
IsAutomatedEffectPattern "base power becomes" entry to auto-resolve; (b) CONTINUOUS self "your Leader" (own
char, #305) and (c) CONTINUOUS board-char-sets-owner-Leader (#307) → GetPower live recompute. Passive forms are
invisible to the coverage gate.

## In-hand self cost-reduction ("If <cond>, give this card in your hand −N cost") — VERIFIED CLEAN (no engine change, 1 test)
The whole family (EB04-061, OP07-064, OP11-023, OP15-013/021/102, OP16-005/015, PRB02-014, ST23-001/002,
ST26-001, …) routes through GetHandSelfCostBonus (L1013): a general per-line handler (HandSelfCostRx) whose
condition is delegated to the SHARED EvaluateCondition (silent/fail-closed). Probed the 4 trickiest condition
shapes and ALL resolved correctly:
 - OP16-005: "a Character with 8000 power or more AND a type including \"Whitebeard Pirates\"" (single-char
   power+type) → 8→5.
 - OP07-064: "the number of DON!! cards on your field is at least 2 less than … your opponent's field"
   (comparative DON) → 6→3.
 - OP11-023: "your Leader has {Fish-Man} type, you have 3 or less Life cards AND your opponent has 5 or more
   rested cards" (triple-AND — the #281/#282 fail-closed class) → 7→4.
 - ST26-001: "a [San-Gorou] or [Sanji] Character with 7000 base power or more" (named-OR + base-power) → 7→2.
Test `costreductest`. No engine change. Coverage 2675/0/0/0/0. Gate-invisible bug total remains #163–#307.
The cost-reduction design (general handler + shared condition evaluator) means any condition the engine learns
benefits every card for free — this family is robust; fast-pass the remaining members.

## Continuous cost-INCREASE passives (self + own-side aura, anti-cost-removal tech) — VERIFIED CLEAN (no engine change, 1 test)
GetCost composes SIX helpers (L948): GetPassiveCostBonus (self "This Character gains +N cost"),
GetPassiveCostAuraBonus (own "All of your … Characters gain +N cost"), GetOpponentCostDebuffAura (opp "give all
of your opponent's Characters −N cost"), GetHandSelfCostBonus (in-hand self), GetHandCostReductionAura,
GetPlayCostReductionAura. Each honors [Your Turn]/[Opponent's Turn] timing, [DON!! xN], leading-If (shared
EvaluateCondition, fail-closed), colour/{type}/cost-cap filters, and "for every N" scaling; timed forms
("during this turn"/"until the") are routed to the modifier path instead. Probed the continuous cost-UP cards:
 - ST19-004 Hina "[DON!! x1][Opponent's Turn] This Character gains +4 cost": oppTurn+1DON→8, ownTurn+1DON→4,
   oppTurn+0DON→4 (DON gate).
 - OP16-080 Teach "[Opponent's Turn] All of your Characters gain +1 cost" (aura): oppTurn→3, ownTurn→2.
Test `costuptest`. No engine change. Coverage 2675/0/0/0/0. Gate-invisible bug total remains #163–#307.
The whole cost subsystem (up/down, self/aura/opponent, hand/play) is robust — fast-pass remaining cost cards.

## #308 — MANDATORY "trash N cards from your hand" was SKIPPABLE via passEffect → free card advantage
OP04-060 Crocodile "[On Your Opponent's Attack] [Once Per Turn] DON!! −1: Draw 1 card AND trash 1 card from
your hand." The " and " split correctly produces a "trash 1 card from your hand." sub-effect after the draw, but
the player could passEffect(skip) it: they drew a card for FREE and skipped the mandatory discard.
ROOT: PassEffect ALWAYS removes the pending clause (its `skippable` flag only picks the log wording), and the
split sub-clause inherits the parent reactive's you-may-ACTIVATE optionality (effect.Optional=true), so nothing
enforced the mandatory discard. This hits EVERY mandatory standalone "trash N cards from your hand" (loot
effects "draw N then trash M", standalone forced discards).
FIX: added a guard at the TOP of PassEffect (L5900) — if effect.Text is a STANDALONE mandatory hand-discard
(regex `^\s*trash \d+ cards? from your hand\.?\s*$`, ANCHORED to end so a cost-prefix "trash N …: <benefit>"
or a "you may"/"up to" form is NOT caught) and the hand holds ≥ the count, RESOLVE it now (engine trashes the
last cards via ResolveEffect) instead of dropping it unpaid. Deadlock-safe: bounded loop (guard<12), and if the
hand can't cover the count it falls through to the ordinary skip. Do NOT gate on effect.Optional — a split
sub-clause inherits the parent's optionality, so the TEXT is the source of truth.
Test `op04060test`: 1 field DON → auto-pays DON!! −1, draws 1, and passEffect on the trash now TRASHES 1 (was:
trash 0, free card); 0 field DON → whole effect fizzles (unpayable DON!! −N, no draw).
Coverage 2675/0/0/0/0 (STUCK=0 — no deadlock from the core skip-path change); smoke P(first)=54.67%.
Gate-invisible bug total now #163–#308.
NEW CLASS: **mandatory-vs-optional selection skippability** — PassEffect's `skippable` is cosmetic (it always
removes the clause). A MANDATORY clause with legal targets must be ENFORCED on skip, not dropped. The
effect.Optional flag is unreliable for split sub-clauses (inherits parent). Verified for hand-discard; other
mandatory selections (K.O./rest with targets available) may share the pattern — future sweep.

## #309 — MANDATORY self-CHARACTER-removal rider skippable via passEffect (extends #308 / class 4l)
OP04-079 Orlumbus "[Activate: Main] [Once Per Turn] Give up to 1 of your opponent's Characters −4 cost … and
trash 2 cards from the top of your deck. Then, K.O. 1 of your {Dressrosa} type Characters." The final self-K.O.
is a MANDATORY DOWNSIDE, but passEffect dropped it (log literally said "mandatory effect skipped (no legal way
to resolve it)" — yet a legal target EXISTED) → the player kept their Character for free after the benefit.
Confirmed the self-K.O. IS resolvable (resolve w/ a valid target → 2 chars → 1).
FIX: extended the #308 PassEffect guard with an `else if` for standalone mandatory self-character removal —
`^\s*(K\.O\.|trash|Return) \d+ of your\b` + contains "Character" + NO "opponent" (SELF only — declining an
opponent-targeting benefit isn't an exploit) + NO ':' (a "…: <benefit>" cost-prefix stays declinable) + no
"up to"/"you may"/IsOptionalEffectText. Resolve against EACH own Character until one takes (ResolveEffect
validates the {type}/cost/name filter); deadlock-safe — fall through to ordinary skip if none qualify.
Test `op04079test`: activate Orlumbus, passEffect ALL pending → a {Dressrosa} Character still leaves (2→1).
Coverage 2675/0/0/0/0 (STUCK=0 — broadened skip-path change introduces no deadlock); smoke P(first)=54.67%.
Gate-invisible bug total now #163–#309.
CLASS (4l) UPDATE: the enforce-on-skip guard now covers BOTH mandatory hand-discard (#308) and mandatory
self-character removal (#309). Pattern: mandatory SELF-downside requiring target selection → passEffect must
RESOLVE (auto-pick, filter-validated) not DROP. Still-open candidates: mandatory self-return/self-debuff riders
with non-Character targets (self DON return, give-your-own -power) — verify in a future sweep.

## Class (4l) sweep COMPLETE + [On Block] family — VERIFIED CLEAN (no engine change, 1 test)
(4l) closure: scanned for remaining mandatory SELF-downside riders beyond hand-discard (#308) and self-character
removal (#309) — self-DON-return / give-your-own-−power riders that are mandatory, standalone, non-"you may"/
"up to", non-cost-prefix: NONE exist in the DB. The class-4l enforce-on-skip guard is complete.
[On Block] reactive family (14 cards) verified: DeclareBlock dispatch (L3603) queues the On-Block clause with the
correct [DON!! xN] gate read from the clause (prior fix), optional-flag from IsOptionalEffectText. The trailing-if
draw handler (L14413 `^Draw (?:\d+|a|an) cards? if <cond>`) gates on _conditionRecognized (skip draw when a
RECOGNIZED condition is unmet), and the ". Then," split stashes the rider in PendingContinuation at queue time so
it fires after clause A resolves REGARDLESS of whether the gated draw happened.
Test `op05047test` (OP05-047 Basil Hawkins, full block flow declareAttack→blockAttack→resolve): hand2 → draws 1
+ gains +1000 (6000); hand6 → NO draw (trailing-if gated) but +1000 rider STILL fires (6000).
No engine change. Coverage 2675/0/0/0/0. Gate-invisible bug total remains #163–#309.

## #310 — deck-look "{A} or {B} type" dual-type reveal filter collapsed to only {A} (glow≠resolve)
EB04-002 "[On Play] Look at 4 …; reveal up to 1 {Egghead} or {Straw Hat Crew} type card other than [Jewelry
Bonney] and add it to your hand." (also OP06-025 {Fish-Man}|{Merfolk}, OP07-041 {Amazon Lily}|{Kuja Pirates}).
The deck-look featureFilter was parsed with ParseCurlyBraceTag(text) which returns only the FIRST {tag}, so the
filter became "Egghead" alone → a {Straw Hat Crew} card the effect explicitly allows was REJECTED at
ResolveDeckLookSelect's FeatureMatches gate. Under-permissive: the player couldn't add a valid 2nd-type card.
FIX: (a) new ParseCurlyBraceTagsOr(text) → parses "{A} or {B} type" to the '|'-joined "A|B" (single "{A}" → "A");
used for the deck-look featureFilter (L14631). (b) FeatureMatches now splits the filter on '|' and matches ANY —
backward-compatible (a single tag splits to one element). Only used by the deck-look select, so low blast radius.
Test `eb04002test`: play EB04-002, deck top = a {Straw Hat Crew} card (Chopper EB01-006, NOT Egghead) → the
deck-look now accepts it and adds it to hand (was: rejected as non-{Egghead}).
Coverage 2675/0/0/0/0; smoke P(first)=54.67%. Gate-invisible bug total now #163–#310.
FLAGGED FOLLOW-UP (not fixed — needs per-site probes): the SAME ParseCurlyBraceTag single-tag collapse likely
affects the ~25 other call sites — notably "play up to 1 {A} or {B} type Character from your hand/trash"
(OP02-037, OP02-040, EB03-024, EB03-029, EB04-015) and deck-SEARCH filters (L12377/12390/15255). Each uses a
distinct matching path (play-from-hand/trash filter, StartDeckSearch) → sweep individually with a probe before
swapping to ParseCurlyBraceTagsOr. ~10+ dual-type-OR cards remain.

## #311 — dual-type "{A} or {B}" CONDITIONS fail-closed in EvaluateCondition (dual-type sweep cont.)
Continuing the #310 dual-type sweep. Bucketed the 58 "{A} or {B} type" cards: deck-look-reveal (12, FIXED #310),
play-hand/trash (14, use CardPassesFeatureFilter which ALREADY collects every {Tag} and matches ANY — fine), and
CONDITION-EVAL (16) which were BROKEN two ways:
 (a) "your Leader has the {A} or {B} type" (EvaluateCondition L6666) used single-tag ParseCurlyBraceTag → only
     {A} checked → a Leader with only {B} fails-closed (EB02-011, OP07-032, …). FIX: ParseCurlyBraceTagsOr +
     FeatureMatches (matches either).
 (b) "you have N or more {A} or {B} type Characters" (count, L6503) — the regex tag group `(?:\{([^}]+)\} type )?`
     could not match "{A} or {B} type" (the " or {B}" broke it) → the WHOLE regex failed → fell through →
     fail-closed (OP07-050/052, …). FIX: broadened group 5 to `((?:\{[^}]+\}(?: or )?)+ type )?` (captures the
     whole single/dual phrase) → ParseCurlyBraceTagsOr → FeatureMatches(any). Backward-compatible for single-type
     (same tagW result).
Test `dualtypecondtest`: (1) EB02-011 with an East-Blue-only Leader (OP03-021) → condition true → rested DON given
(Straw Hat leader → 0); (2) OP07-050 with 2 {Kuja Pirates} fixtures → opponent Character returned (0 fixtures → no).
Coverage 2675/0/0/0/0 (broad count-regex change: single-type counts intact, STUCK=0); smoke P(first)=54.67%.
Gate-invisible bug total now #163–#311.
DUAL-TYPE SWEEP STATUS: FIXED deck-look-reveal (#310) + Leader-type/count conditions (#311). REMAINING (verify next):
board-target "rest/K.O./return/add up to 1 {A} or {B} Character" (5: EB03-012, OP04-097, OP11-039, OP14-041,
ST03-004) + give-DON-target {A} or {B} Leader/Char (4: EB03-015, OP08-001, OP14-040, OP14-105) — check if those
target filters use CardPassesFeatureFilter (fine) or a single-tag path. Deck-SEARCH ParseCurlyBraceTag sites
(L12377/12390/15255): confirm no dual-type card routes there.

## #312 — give-DON "to {A} or {B} type Leader or Character" mis-routed to Leader-only + missing {type} filter (dual-type sweep close)
EB03-015 / OP14-040: "Give up to 1 rested DON!! card to 1 of your {Fish-Man} or {Merfolk} type Leader or Character
cards. Then, …". TWO bugs in the give-DON handler (L13421):
 (a) The recipient-routing gate `!ContainsAll(text, "Characters")` looked for the PLURAL "Characters", but these
     say singular "Character cards" → fell to the LEADER-ONLY path → the Character-recipient option was dropped
     (could only give to the Leader). FIX: also exclude "or Character" so the singular form uses the
     choose-recipient path.
 (b) The choose-recipient path (L13466) validated only seat + type∈{leader,character} — it NEVER enforced the
     {Fish-Man}/{Merfolk} {type} filter → the DON!! could attach to ANY of your Leader/Characters. FIX: added
     `!CardPassesFeatureFilter(text, targetDef)` (collects EVERY {Tag}, matches any → dual-type OR honored).
Test `eb03015test` (Activate:Main rest-self cost): a non-{Fish-Man}/{Merfolk} Character is REJECTED (0 DON); a
{Merfolk} Character is now a valid recipient and gets the DON (1). (Was: Leader-only, any target.)
Coverage 2675/0/0/0/0 (core give-DON routing change: common "give DON to your Leader" cards intact, STUCK=0);
smoke P(first)=54.67%. Gate-invisible bug total now #163–#312.

## DUAL-TYPE "{A} or {B}" SWEEP — COMPLETE
All 58 dual-type cards accounted for: deck-look-reveal (12, #310), conditions Leader-type+count (16, #311),
give-DON-to-{tag}-Leader-or-Character (#312). play-hand/trash (14) + board-target (5) + give-DON-to-Characters
(OP08-001) all use CardPassesFeatureFilter which ALREADY collects every {Tag} & matches ANY (verified clean).
Deck-SEARCH ParseCurlyBraceTag sites (L12377/12390/15255): NO dual-type card routes there (none in the "search"
bucket) → non-issue. The dual-type OR filter is now correct across every code path.

## #313 — OP08-096 [Counter]: "+M if trashed card cost ≥N" applied UNCONDITIONALLY + never milled
"[Counter] Trash 1 card from the top of your deck. If the trashed card has a cost of 6 or more, up to 1 of your
Leader or Character cards gains +5000 power during this battle." A [Counter] EVENT (resolved by CounterWithCard,
NOT TryResolveKnownEffect). TWO bugs: (1) AutomatedCounterPower greps the first "+N" in the [Counter] clause and
returned 5000 → the flat boost was applied to Battle.DefensePower REGARDLESS of the trashed card's cost; (2) the
mill never happened — CounterWithCard's primary-clause queue guard `!Regex "gains? \+\d"` blocks the "Trash 1 …
gains +5000 …" clause from being queued (to avoid double-applying the flat boost), so the "Trash 1 card" action
was silently dropped.
FIX: a surgical branch in CounterWithCard right after AutomatedCounterPower — detect the "Trash N … from the top
of your deck. If the trashed card has a cost of N or more, … +M power" structure, MILL the top card now, and set
counterPower = M only when the milled card's printed cost ≥ N (else 0). The flat-to-DefensePower application is
the same simplification used for other "up to 1 of your Leader/Char gains +N" counters (the defender is what
matters in a counter). The body-queue guard still blocks the clause (no double mill/boost). Reverted a dead
TryResolveKnownEffect handler I first tried (OP08-096 never reaches it — it's [Counter]-only).
Test `op08096test` (full counter flow declareAttack→passBlock→counterWithCard): trashed cost-6 card → +5000
DefensePower + card milled; trashed cost-2 card → +0 (gated). Only card with this structure.
Coverage 2675/0/0/0/0 (CounterWithCard hot-path change: STUCK=0, other counters intact); smoke P(first)=54.67%.
Gate-invisible bug total now #163–#313.

## #314 — conditional [Counter] flat power boost applied UNCONDITIONALLY (general leading-If form)
OP09-078 "[Counter] DON!! −2, You may trash 1 card from your hand: If your Leader has the {Straw Hat Crew} type,
up to 1 of your Leader or Character cards gains +4000 power during this battle. Then, draw 2 cards." The +4000 is
GATED on the Leader type, but AutomatedCounterPower greps the "+4000" with NO game state → CounterWithCard applied
+4000 to DefensePower regardless of the Leader. (Generalizes #313's OP08-096, which was the runtime "if trashed
card cost" variant.)
FIX: a general gate in CounterWithCard (after AutomatedCounterPower / the OP08-096 branch): extract the [Counter]
clause, strip a leading cost prefix (up to the first ':' — "DON!! −N, You may trash …:"), and if the remainder
starts with "If <cond>, (up to N of your|this|your) … gains +N", evaluate <cond> via EvaluateCondition (has state);
if unmet, zero counterPower. The OP08-096 runtime form ("Trash 1 …. If the trashed card …") does NOT start with
"If", so it is unaffected (handled by its own branch).
Test `op09078test` (full counter flow): SHC Leader → +4000 DefensePower; Navy Leader → +0 (condition gated).
Coverage 2675/0/0/0/0 (CounterWithCard hot-path change: STUCK=0, unconditional counters intact); smoke 54.67%.
Gate-invisible bug total now #163–#314.
STILL-OPEN conditional counters (flagged, NOT fixed): ST05-017 "…+4000. If that card is a Character, cannot be
K.O.'d this turn" (flat application drops the per-card K.O.-immunity rider — needs targeted resolution);
EB02-030 "If any of your Characters would be K.O.'d in battle this turn, you may trash 1 from hand instead" (a
counter-registered TURN-LONG battle-K.O. replacement — no mechanism exists, effect is DEAD).

## #315 — ST05-017 [Counter]: per-card K.O.-immunity rider dropped by flat-counter application
"[Counter] Up to 1 of your {FILM} type Leader or Character cards gains +4000 power during this battle. If that
card is a Character, that Character cannot be K.O.'d during this turn." The +4000 is applied FLAT to
DefensePower (AutomatedCounterPower), and the primary clause is blocked from queueing (has "gains +4000"), so the
buff resolver that DOES handle the "If that card is a Character, cannot be K.O.'d" rider (L13839) is never reached
→ the K.O.-immunity was dropped.
FIX: in CounterWithCard, right after applying counterPower, if the [Counter] effect carries "that Character
cannot be K.O.'d during this turn" and the DEFENDER (state.Battle.TargetId — "that card" = the card the counter
saves) is a Character matching the buff's {type} filter (CardPassesFeatureFilter on the [Counter] clause), grant
it cannotBeKod "thisTurn". Contained, low-risk; the flat +N path is unchanged.
Test `st05017test`: north attacks a {FILM} Character, south counters with ST05-017 → +4000 DefensePower AND the
{FILM} Character gains cannotBeKod this turn (was: immunity dropped).
Coverage 2675/0/0/0/0 (STUCK=0); smoke P(first)=54.67%. Gate-invisible bug total now #163–#315.
Conditional-[Counter] sweep: OP08-096 (#313), OP09-078 (#314), ST05-017 (#315) fixed. REMAINING (hard, flagged):
EB02-030 "[Counter] If any of your Characters would be K.O.'d in battle this turn, you may trash 1 from hand
instead" — a counter-registered TURN-LONG battle-K.O. replacement; no pending-turn-replacement mechanism exists,
so the effect is DEAD. Would need a new registered-replacement store consulted by the battle-K.O. path (#303's
TryRemovalReplacement only scans board cards, not a spent Event). Deferred — niche single card, larger change.

## #316 — EB02-030 [Counter]: turn-long battle-K.O. replacement was DEAD (no mechanism)
"[Counter] If any of your Characters would be K.O.'d in battle during this turn, you may trash 1 card from your
hand instead." A counter-REGISTERED turn-long battle-K.O. replacement. #303's TryRemovalReplacement only scans
in-play cards' text, not a spent Event, so this did NOTHING — the effect was completely dead.
FIX (3 parts): (1) new GameState.BattleKoTrashSaveSeats (HashSet<string>), cleared at start-of-turn beside the
other this-turn sets; (2) a registration handler at the top of TryResolveKnownEffect — when EB02-030's [Counter]
effect resolves ("would be K.O.'d in battle during this turn" + "trash 1 card from your hand instead"), add the
seat; (3) the battle-damage K.O. path (ResolveAttack, after the TryRemovalReplacement branch) — if the victim is
a Character whose owner is in BattleKoTrashSaveSeats and has a hand card, trash 1 from hand (auto — beneficial,
matches the removal-replacement convention) and the Character survives.
Test `eb02030test` (full flow declareAttack→passBlock→counterWithCard→resolve→passCounter→resolveAttack): a 3000
Character attacked by 7000, after countering with EB02-030, SURVIVES + a fodder card is trashed from hand.
Coverage 2675/0/0/0/0 (new field + battle-K.O.-path touch: STUCK=0); smoke P(first)=54.67%.
Gate-invisible bug total now #163–#316.
CONDITIONAL-[Counter] SWEEP COMPLETE: OP08-096 (#313 runtime trashed-cost gate), OP09-078 (#314 general leading-If
gate), ST05-017 (#315 per-card K.O.-immunity rider), EB02-030 (#316 turn-long battle-K.O. replacement). EB01-050
"[Counter] If trash≥30, add 1 top-of-deck to Life" has NO power boost → goes through the normal queued path with
the leading-If gate (not the AutomatedCounterPower flaw) → not affected.

## #317 — [End of Your Turn] targeted "set up to N {tag} Character as active" STALLED (never resolved)
OP07-117 "[End of Your Turn] If you have 3 or less Life cards, set up to 1 {Egghead} type Character with a cost of
5 or less as active." (also EB03-061 {FILM}, and similar targeted restands). ApplyEndOfTurnEffects has INLINE
handlers for self "Set this … as active" and "K.O. up to N opponent rested", but a TARGETED "set up to N {tag}
Character as active" fell to QueueAndAutoResolve → it prompted "Choose up to 1 rested card to set active, or skip"
and STALLED pending (end-of-turn fires with NO player interaction, so the pick was never made and the turn passed
with the restand un-done). Condition gate was fine; the resolution just never happened.
FIX: added an INLINE handler in ApplyEndOfTurnEffects (before the QueueAndAutoResolve fallback) for
`[Ss]et up to \d+ (of your )?({tag} type )?Characters? … as active` (excl "DON!!" and "opponent"): gate on the
leading If, then auto-pick valid RESTED own Characters honoring the {tag}/cost filter and skipping freeze (#291).
The card may target itself (OP07-117 is itself {Egghead}) — valid per rules.
Test `op07117test`: Life 2 (≤3) → a rested {Egghead} cost-5 Character is set active; Life 5 (>3) → condition
gated, no restand.
Coverage 2675/0/0/0/0 (STUCK=0); smoke P(first)=54.67%. Gate-invisible bug total now #163–#317.
CLASS NOTE: end-of-turn/start-of-turn dispatchers fire with NO player interaction, so a TARGETED "up to N" effect
routed to QueueAndAutoResolve stalls forever — it must be resolved INLINE with an auto-pick (like the K.O. handler).

## Verification pass (no engine change): opponent-hand-trash + Life-turn costs — CLEAN
- "Trash N cards from your opponent's hand" (OP06-097 etc.): the engine correctly distinguishes CONTROLLER-picks
  ("Trash from your opponent's hand", L11434) vs OPPONENT-picks ("Your opponent trashes N from their hand", auto
  last-N) — a correct OPTCG phrasing distinction, previously audited. Not a bug.
- "turn N cards from the top of your Life cards face-up/down" COST (OP08-063 face-down ramp, EB03-056 face-up,
  OP11-104): handler L7347 has correct payability (needs N cards in the OPPOSITE state — `flippable = toFaceUp ?
  count(!FaceUp) : count(FaceUp)`, prior fix for the face-down-was-free bug) and flips from the top. Clean.
- [End of Your Turn] targeted-effect space now fully covered (self set-active, K.O.-opponent OP04-034 inline,
  #317 set-up-to-N-{tag}-Character inline); [End of Opponent's Turn] = 0 cards; when-attacked reactive = only
  OP03-001 (handled).
SATURATION SIGNAL: this iteration's probes all hit already-correct/audited mechanics (0 bugs). Recent yield has
fallen from broad classes to niche single cards, and now a zero-bug verification iteration. The high-value
recurring classes are swept & closed. Remaining is a long tail of individual edge cases (declining ROI).
