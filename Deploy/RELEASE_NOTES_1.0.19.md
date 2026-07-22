# v1.0.19 — The card-effect correctness release

This is a **big** card-rules release: a full second-pass sweep of the effect engine caught a large
class of bugs where a card *looked* like it resolved but quietly did the **wrong** thing — buffs that
never applied, conditions that were ignored, costs that were skipped, immunities that lasted forever,
"you may" downsides you could skip for free, and abilities that were silently dead. Plus a round of
board/UI polish. Below is the complete list, grouped by area.

## 🃏 Headline fixes
- **Sentomaru** no longer instantly loses you the game when its effect searches your deck — this was hitting **every** "search your deck" card.
- **Roronoa Zoro leader (OP12-020)** now correctly re-stands after it battles a Character, and its "can't attack small Characters" drawback no longer gets misapplied to your **opponent's** card.
- **Trafalgar Law (ST17-002)** now resolves its second return when your Leader is a Seven Warlord.
- **Luffy & Ace leader (ST30-001)** no longer keeps a permanent **+3000** — the buff is correctly limited to your opponent's turn.
- **Crocodile (ST17-001)** now draws 2 and places a card after revealing a Warlord.
- **Marshall.D.Teach (ST17-005)** can now actually place a card on top of your deck.
- **Roronoa Zoro (OP07-034)** auto-gains **+2000** on attack instead of making you select it.
- **Power Mochi** now grants **+2000**, not +4000.
- **Doflamingo (OP01-060 / OP07-048)** — "you **may** play" the revealed card is now genuinely optional.

## ⚔️ Battle, K.O. & keyword interactions
- **Dosun (OP06-030)** could become **permanently unkillable in battle** — its "cannot be K.O.'d in battle" is now correctly limited to when it attacks, and its +2000 power now actually applies.
- A **negated Character** now correctly loses its keyword abilities — **[Double Attack]**, **[Banish]**, and **[Rush]** no longer keep working after the card's effects are negated.
- **ST13-003's** "face-up Life cards go to the bottom of your deck instead of your hand" now applies on normal **battle damage**, not just effect damage.
- Battle-K.O. immunity granted by a leader/aura condition, and by attacker attribute ("cannot be K.O.'d by ＜Strike＞…"), is now gated on its actual condition.

## 🛡️ Removal & "…instead" replacement effects
- "If this would be K.O.'d, you may **&lt;pay X&gt;** instead" protections now fire on a normal **battle K.O.** too, not only effect K.O.s — **Morley (OP16-033)** and the whole "rest N of your cards instead" family (OP14-029, OP15-035) now work in battle.
- Those protections also now fire when the opponent **bounces**, **places on deck**, or moves your Character to **Life** — previously only a literal "K.O." triggered them.
- A **"cannot be K.O.'d" aura** no longer wrongly blocks **bounce / trash / deck-placement** (those aren't K.O.s per the rules).
- **EB02-030 (Counter)** — "if any of your Characters would be K.O.'d in battle this turn, trash 1 from hand instead" — was completely dead and now works.
- **"Trash up to 1 of your opponent's Characters"** fixed four ways (power filter, K.O.-vs-trash handling, immunity, replacement); a partial-payment leak in "rest N instead" costs was plugged.
- **Stage-based [On K.O.]** removal (Miss Merry Christmas OP14-088) was unimplemented and now resolves.

## 📊 Base power & "becomes" effects
- **Vista (OP14-053)** — "[Opponent's Turn] this Character's base power becomes your Leader's" — was dead; now applies.
- Board Characters that set your **Leader's base power** ("becomes 7000", "becomes the opponent's Leader's") now apply correctly — **EB04-003**, **EB04-052 (Sanji)**, **Zeff (EB04-004)**, and **OP15-092's** ≥20-trash "Leader becomes 7000 on defense" bullet (which was leaving your Leader at 5000).
- "Base power becomes N" buffs that require a manual step now apply automatically when you attack instead of getting stuck.

## 🎴 Counters
- **OP08-096 (Counter)** — "+5000 **if** the trashed card costs 6+" now actually mills the card and only grants the boost when it qualifies (it was always applying, and never milling).
- Conditional counters like **Gum-Gum Giant (OP09-078)** now honor their condition ("if your Leader has {Straw Hat Crew}…") instead of always granting the power.
- **Union Armada (ST05-017)** now also grants its "that Character cannot be K.O.'d this turn" rider to the card it saves.

## 🚫 Mandatory downsides you could skip for free
- A **mandatory "trash N cards from your hand"** (e.g. "draw 1 and trash 1") can no longer be **skipped** — that was free card advantage.
- A **mandatory self-sacrifice** rider ("…then K.O. 1 of your own Characters", Orlumbus OP04-079) can no longer be skipped either.

## 💰 Costs, DON!! & payment
- A whole class of activation costs that were being **skipped (paid for free)** now actually cost you: turning Life cards face-up/down, giving your Leader −power, DON!! −N returns, self-trash, and more.
- Continuous **cost-reduction auras** on other cards (e.g. Sabo OP01-067 "give blue Events in your hand −1 cost") now apply.
- Big DON!!-cost plays (Doflamingo OP05-119 "DON!! −10: place all your Characters…") now resolve.

## 🎯 Targeting, conditions & filters
- **Dual-type "{A} or {B}"** effects no longer ignore the second type — deck-look reveals, conditions ("if your Leader has {Fish-Man} **or** {East Blue}…", "if you have N {Amazon Lily} **or** {Kuja Pirates}…"), and give-DON targeting all now match either type.
- Removal/rest/give-power targeting now enforces the card's real filters — **base power**, **exact cost**, **color**, name, and "**other than [Name]**" — across K.O., rest, bounce, deck-place, and give-power effects (previously several ignored one or more filters and could hit any card).
- Compound conditions joined by "and" ("has {type} **and** is active") no longer fail closed and kill the ability (Sai OP06-088, OP09-017 [Rush]).
- Play-from-hand / play-from-trash / hand-to-Life tutors now enforce name, cost-range, color, power, "other than", and [Trigger] filters consistently (a ~60-card reveal-add family, a ~40-card named-play family, and more).

## ♻️ Reactive & "when …" abilities that were dead or mis-firing
- Reactives that only fired on the *normal* version of an action now also fire on the **effect-driven** version — "**when you play a Character**" (now fires on effect-plays), "**when … is given a DON!!**" (OP02-002), "**when your opponent plays a Character**" (Sugar OP04-024).
- Several **completely dead** leader/character reactives now work: **Nami (OP11-040 / OP11-041)**, **OP16-041**, **PRB02-009**, **OP07-038** (was drawing without its "≤5 hand" gate), **Doflamingo (OP10-042)** ("Dressrosa removed **or K.O.'d** → draw" was broken by the wording).
- A "trailing **if**" on a draw ("draw 1 card **if** you have ≤N cards") was ignored and drew unconditionally — now gated.
- Various "when this becomes rested / when a card leaves your Life / when you draw off-turn / when a card is added from Life" reactives now resolve.

## ⏳ Timing & end-of-turn
- "Set … as active **at the end of this turn**" no longer fires **immediately** (that was a ramp/restand exploit) — it's now correctly delayed.
- **[End of Your Turn]** targeted restands ("set up to 1 {Egghead}/{FILM} Character as active") no longer stall unresolved — they now auto-resolve at end of turn.
- End-of-turn abilities with a leading condition ("if you have ≤3 Life…") now check that condition.

## 🧩 Multi-clause, modals & specific cards
- Multi-bullet **"Choose one"** modals with a leading condition, and 3-bullet modals with a "then, draw if…" rider (Nami OP05-096), resolve fully.
- Two-part effects where a leading opponent-removal clause swallowed the appended clause now run **both** halves.
- Plus targeted fixes to many individual cards — Kuzan (OP12-040), Buffalo (OP14-070), Enel (OP05-098), Rindo (OP14-115), Kingdom Come (EB01-059), Carmel (OP04-101), Black Hole (OP09-098), Reject (OP06-116), Fire Fist (OP15-020), Cloven Rose Blizzard (EB02-007), Hody Jones (OP06-035), Zephyr (OP11-006), Shanks (OP14-027), and more.

> The full per-fix engineering ledger (every card, the exact bug, and its regression test) lives in the repo
> at `Tools/Harness/findings/card-audit-progress.md` — this release lands **~150 card-effect correctness fixes**
> from that sweep. Every fix has an automated test, and the coverage gate is green (0 crashes / stuck / invariant
> violations across 2,675 cards).

## ✨ Board & UI
- **Player names show everywhere** — pills, the turn banner, and match history — instead of "South/North".
- **Status tags on your opponent's cards read right-side-up** now (they were upside-down).
- The card preview no longer shows a **redundant name** under the art.
- The **"Show Result"** button no longer sits on top of the opponent's hand.
- **"Ready — waiting for opponent"** is now readable (white), and **black decks look black** on the life bar.
- The **updater's "What's New"** now shows what actually changed in each release.
