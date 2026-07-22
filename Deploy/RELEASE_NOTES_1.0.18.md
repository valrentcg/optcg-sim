# v1.0.18 — Card-effect correctness + smarter bots

A big release. The headline is a full second-pass sweep of the effect engine that caught a large class of
bugs where a card looked like it resolved but quietly did the wrong thing — buffs that never applied,
conditions that were ignored, costs that were skipped, immunities that lasted forever, "you may" downsides
you could skip for free, and abilities that were silently dead. On top of that: an AI overhaul with a smooth
difficulty curve, a friends blocked-list, and board/UI polish.

Below is the complete card-fix list, split by area, followed by the bots and app changes.

## Playing & searching for cards
- **Sentomaru (ST03-007)** was playing the wrong card from your deck — now plays the correct [Pacifista].
- A roughly 40-card family of "play up to N [Name] from your hand" effects now enforce the name filter (e.g. ST04-002 and many named-play cards) instead of letting you play anything.
- A roughly 60-card family of "reveal a {type} card other than [Name] and add it to your hand" tutors now respect the "other than [Name]" exclusion and the cost range — the play-from-trash versions too.
- Playing a Character **from your trash while it's rested-cost** now works for the 16 cards that allow it (PRB02-013, OP09-085, OP03-013, and others).
- Playing a Character **from your hand as a rested-play** now works for the 10 cards that allow it (ST12-003, EB01-042, EB02-028, and others).
- Play-from-hand **color** filters are now enforced (7 cards, e.g. ST09-008 yellow, OP02-030 green, OP02-051 blue).
- **Kouzuki Momonosuke (OP16-084)** can now pay its self-trash cost and resolve.
- **Yamato leader (OP16-079)** now triggers its "when this is played from the trash" reactive.
- **Pudding (PRB02-010)** now enforces its "6000 to 8000 power" range when playing a Big Mom Pirates from hand.
- **Sanji leader (PRB01-001)** now correctly filters to a Character "without an On Play effect" and cost 8 or less.
- **Ace (PRB02-018)**, **Curly Dadan (ST13-006)** and **Garp (ST13-013 / ST13-019)** now handle their [Sabo] / [Portgas.D.Ace] / [Monkey.D.Luffy] play/reveal effects correctly.
- **Law (OP01-047)** now resolves its "return a Character, then play one from hand" sequence.
- **Kouzuki Oden (OP02-030)** now correctly plays a **green** {Land of Wano} Character from trash.
- **ST13-001** now enforces its "cost 3 or more and 7000 power or more" filter when adding a Character.
- Reveal-add tutors that check **exact power** (ST30-002 Inazuma, ST30-017) and a **"type includes"** payoff (ST22-003 Newgate, ST22-006 Jozu, ST22-007 Squard) now resolve correctly.
- The "add a card from your hand to your Life" effects now enforce their type/cost filters.
- Two general filter gaps in the play-from-trash and play-from-hand resolvers (cost range, name, power, "other than", [Trigger]) were closed.

## Battle, K.O. & keyword abilities
- **Dosun (OP06-030)** could become permanently unkillable in battle — its "cannot be K.O.'d in battle" is now correctly limited to when it attacks, and its +2000 power now actually applies.
- A **negated Character** now loses its keyword abilities — [Double Attack], [Banish] and [Rush] no longer keep working after the card's effects are negated.
- **P-007 Luffy** correctly can't be K.O.'d in battle by Strike-attribute Leaders/Characters; **Zephyr (ST05-010)** correctly gains +3000 when battling a Strike-attribute Character.
- **Izo (ST28-002)** granted [Banish] to any leader and **ST29-016** granted [Unblockable] to any leader — both now only grant to the correct {Land of Wano} / named-Luffy leader.
- **Zeus (ST07-011) / Prometheus (ST07-013)** now correctly grant [Banish] / [Double Attack].
- **OP06-096** now applies its "your Characters with cost 7 or less cannot be K.O.'d in battle this turn."
- **ST13-003's** "face-up Life cards go to the bottom of your deck instead of your hand" now applies on normal battle damage, not just effect damage.

## Power & "base power becomes" effects
- **Vista (OP14-053)** — "on your opponent's turn, this Character's base power becomes your Leader's" — was dead; now applies.
- Board Characters that set your **Leader's base power** now work — **EB04-003**, **Sanji (EB04-052)**, **Zeff (EB04-004)**, and **OP15-092's** trash-based "Leader becomes 7000 on defense" (it was leaving your Leader at 5000).
- "This Character's base power becomes the same as [target]" effects (**Catarina Devon OP16-104**, and the "becomes your opponent's Leader" cards) now apply automatically instead of getting stuck.
- **P-024** gives your Leader +1000 for each of your Characters correctly.
- Group power buffs now apply — **Cloven Rose Blizzard (EB02-007)** ("up to 3 of your Leader/Characters +1000") and **Fire Fist (OP15-020)** ("Leader +3000 and give an opponent Character -power").
- The give-power effect now checks the target's **color, cost, and base power**, not just its type/name (it could previously buff a card that didn't qualify).
- **Mr.3 (ST30-014)** now enforces its "6000 base power" filter when distributing DON.

## Removal & "…instead" protections
- "If this would be K.O.'d, you may **pay X** instead" protections now fire on a normal **battle K.O.** too, not only effect K.O.s — **Morley (OP16-033)** and the "rest N of your cards instead" family (OP14-029, OP15-035) now work in battle.
- Those protections also now fire when the opponent **bounces**, **places on deck**, or moves your Character to **Life** — previously only a literal "K.O." triggered them.
- A **"cannot be K.O.'d" aura** no longer wrongly blocks bounce / trash / deck-placement (those aren't K.O.s per the rules).
- **EB02-030 (Counter)** — "if any of your Characters would be K.O.'d in battle this turn, trash 1 from hand instead" — was completely dead and now works.
- A partial-payment leak in the "rest N of your cards instead" costs was plugged (it could rest fewer than required).

## Resting, K.O.-ing & bouncing the opponent's cards
- Removal, rest, bounce and deck-place effects now enforce the card's stated filter — **base power** (K.O. cards EB01-010, EB04-033, OP04-003; rest cards OP14-027 Shanks, OP14-038), **exact cost** (Mr.1 OP14-090 "cost 0"), and **power** (deck-bottom placement) — instead of being able to hit any card.
- **Rest-opponent** effects now respect a printed "cannot be rested by your opponent's effects" immunity.
- **Hancock (PRB02-017)** attack-lock, **Usopp (ST29-002)** and **Smoothie (P-090)** dynamic cost caps ("cost equal to or less than the number of DON/Life…") now compute correctly.
- **Hody Jones (OP06-035)** ("rest up to 2 of the opponent's Characters or DON"), **Zephyr (OP11-006)** (give a Special-attribute Character -power), **OP16-032** ("opponent's Character can't be rested"), and the **Hina/Marguerite return family (EB03-025, EB03-027, OP14-058, OP11-051)** now resolve.
- **Kingdom Come (EB01-059)** ("K.O. one, then mill") and **Miss Merry Christmas (OP14-088)** (a Stage On-K.O. that K.O.s an opponent, previously unimplemented) now work.

## Costs & DON!!
- A whole class of activation costs that were being **skipped (paid for free)** now actually cost you: turning Life cards face-up/down, giving your Leader -power, DON!! -N returns, and self-trash (about 15 cards across these forms).
- Continuous **cost-reduction auras** now apply (e.g. **Sabo (OP01-067)** "give blue Events in your hand -1 cost").
- Big DON!!-cost plays now resolve — **Doflamingo (OP05-119)** "DON!! -10: place all your Characters…".
- **Luffy (OP04-090)** ("return 7 from trash"), **Kuro (OP15-025)** (push the opponent's DON onto their Character), **Avalo Pizarro (ST27-001)**, **Land of Wano (OP02-048)**, **Loguetown (EB01-030)**, **Moby Dick (OP16-021)** and **Shanks (ST13-009)** now pay their costs and resolve correctly.
- A systemic fix to "You may [cost]: Look at N cards…" Activate: Main abilities (OP02-092 and similar) so the look/search actually happens after the cost.
- **OP02-002** now gives an opponent's Character -cost with the correct "cost 7 or less" filter.

## Conditions & requirements
- **Dual-type "{A} or {B}"** effects no longer ignore the second type — deck-look reveals, conditions ("if your Leader has {Fish-Man} or {East Blue}…", "if you have N {Amazon Lily} or {Kuja Pirates}…"), and give-DON targeting now all match either type.
- Compound conditions joined by "and" ("has {type} and is active") no longer fail and kill the ability — **Sai (OP06-088)** and **OP09-017** ([Rush]) now work.
- A "trailing if" on a draw ("draw 1 card **if** you have N or fewer cards") was ignored and drew unconditionally — now gated.
- Many individual conditions that were unrecognized now evaluate correctly — **Mihawk (P-081)** (blue Cross Guild count), **Buggy (P-084)** (cost-3/4 can't attack aura), **Luffy (ST16-005)** ("if you have a rested [Uta]"), **Queen (OP04-040)** (Life + hand total), the **Cabaji/Alvida/Mohji (ST25-x)** base-cost counts, and the **black {Straw Hat Crew} (ST14-004/008/011)** filters.
- **P-002 "I Smell Adventure"**, **Yamato (P-046)**, **Soba Mask (ST26-001)** and **"I'm invincible" (ST11-005)** now resolve their conditional bodies.

## "When…" reactive abilities
- Reactives that only fired on the *normal* version of an action now also fire on the **effect-driven** version — "when you play a Character" (now fires on effect-plays), "when this is given a DON!!" (OP02-002), "when your opponent plays a Character" (Sugar OP04-024).
- Several **completely dead** reactives now work: **Nami (OP11-040 and OP11-041)**, **OP16-041**, **PRB02-009**, **OP07-038** (was drawing without its "5-or-fewer-cards" gate), and **Doflamingo (OP10-042)** (its "Dressrosa removed or K.O.'d, then draw" was broken by the wording).
- Reactive triggers on unusual events now resolve — when a card is **trashed from your hand** (Kuzan OP12-040), when this becomes **rested by the opponent** (Buffalo OP14-070), when your **Life reaches 0** (Enel OP05-098), when a card is **added from Life** (Lt. Spacey OP05-107), when you **draw off-turn** (Mozambia OP05-053), when the opponent's Character is **returned** (Crocodile EB02-023), on **[On K.O.]** (Rindo OP14-115, Kingdew ST15-003), and when the opponent activates a **Blocker or Event** (Zeff OP06-048, ST10-006 Luffy).
- **Ace leader (OP03-001)** ("when this Leader attacks or is attacked") and the **Stage reactives** on Moby Dick (OP08-056), Oro Jackson (OP13-078), Thousand Sunny (OP09-080) and Birdcage (OP05-040) now resolve.
- The pre-armed defensive reactive on **OP09-052** was inert and now works.

## End-of-turn & delayed timing
- "Set … as active **at the end of this turn**" no longer fires immediately (that was a ramp/restand exploit) — it's now correctly delayed.
- **[End of Your Turn]** targeted restands ("set up to 1 {Egghead} / {FILM} Character as active") no longer stall unresolved — they now auto-resolve at end of turn, and honor their leading condition.
- **OP16-073's** "DON!! -2: set this active, then gains [Blocker]" end-of-turn ability now pays and resolves.

## Choose-one & multi-part effects
- Multi-bullet **"Choose one"** modals now resolve fully, including ones with a leading condition and the 3-bullet **Nami (OP05-096)** modal ("K.O. / Return / Place-at-Life, then draw if…").
- "Your opponent chooses one" modals (**Charlotte Linlin ST07-010, ST20-005**) resolve correctly.
- Two-part effects where a leading opponent-removal or debuff clause swallowed the appended clause now run **both** halves, including **Black Hole (OP09-098)** and **Teach (OP09-093)** (negate + K.O.), and **Reject (OP06-116)**.

## Counters
- **OP08-096 (Counter)** — "+5000 **if** the trashed card costs 6 or more" — now actually mills the card and only grants the boost when it qualifies (it was always applying, and never milling).
- Conditional counters like **Gum-Gum Giant (OP09-078)** now honor their condition ("if your Leader has {Straw Hat Crew}…") instead of always granting the power.
- **Union Armada (ST05-017)** applies its full effect (the +4000 and the "that Character cannot be K.O.'d this turn" rider on the card it saves).

## A few more specific cards
- **Mandatory "trash N cards from your hand"** (e.g. "draw 1 and trash 1") and a **mandatory self-sacrifice** rider ("then K.O. 1 of your own Characters", Orlumbus OP04-079) can no longer be skipped for free.
- **Oars (OP06-083)** can self-negate to bypass its own drawback; **Chopper (OP07-103) / Brulee (ST20-003)** resolve their "add this to hand" [Trigger] remainder.
- **Arlong (OP01-063)** peeks at the opponent's hand correctly.
- "Set active" effects now skip a frozen card (OP13-035), and the **Land of Wano / Dressrosa auras** (Birdcage OP05-040, OP04-119) apply on the correct turn.

## Smarter bots & a difficulty curve
- **Beginner, Intermediate and Advanced now step up cleanly** — all three share one strong decision core, with the lower tiers playing looser on purpose. Beginner still races your life but is sloppier with DON and counters; Intermediate plays the same brain at full discipline; Advanced adds tactical search on top.
- Across all tiers the AI now **pressures your life** instead of making pointless trades, **stops over-spending counters** (it won't burn two cards to save one), **holds big counter cards back** for defence, **commits DON one attacker at a time** instead of front-loading, and **mulligans with more nuance** (weighing going first vs second, and keeping a rough hand when a searcher can fix its curve).

## Friends
- Friend-row buttons are now **compact so the whole strip fits on screen** — the Block chip used to run off the edge.
- **Block is now a two-step confirm** so a stray click can't block a friend.
- New **Blocked list** — a toggle at the top of the Friends screen shows everyone you've blocked, with an Unblock button for each.

## Board & UI
- **Player names show everywhere** — pills, the turn banner, and match history — instead of "South/North".
- **Status tags on your opponent's cards read right-side-up** now (they were upside-down).
- The card preview no longer shows a **redundant name** under the art.
- The **"Show Result"** button no longer sits on top of the opponent's hand.
- **"Ready — waiting for opponent"** is now readable, and **black decks look black** on the life bar.
- The **updater's "What's New"** now shows what actually changed in each release.
