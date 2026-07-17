# v1.0.14

A big card-correctness pass, a much smarter Advanced AI, and multiplayer/UI fixes from live play.

## Card effects — the whole database swept for things that weren't working

An audit of all 2,636 cards turned up a large batch of effects that either did **nothing** or fired **wrong**. All fixed, generally (by card text, not one-offs), so whole families work now.

**Effects that were silently doing nothing:**

- **Trigger abilities that re-use the card's own effect** — "Activate this card's [Main] / [Counter] / [On Play] / [On K.O.] effect." Only the [Main] version worked before; the rest silently sent the card to hand. (Blackbeard's Blockers, Green Star Rafflesia, Officer Agents, and more.)
- **Stage cards played from Life** via "Play this card." (Loguetown, Enies Lobby, Egghead, Moby Dick, Baratie, The Ark Noah — they used to just go to hand.)
- **23 cards whose Trigger never fired** because of how their Trigger text was stored (Carrot and others).
- **Removal/return/negate Triggers** that the trigger path couldn't run even though the engine could (Gum-Gum Champion Rifle, Black Vortex "negate," "Rest your opponent's Leader," bottom-of-deck bounces).
- **Board wipes and one-offs:** Kaido's `DON!! −6` "K.O. all Characters other than this," Birdcage's end-of-turn K.O., Egghead's "set a Character active," Nico Robin's "your Characters can't be K.O.'d by effects," Roronoa Zoro's `[Rush: Character]`, Meowban Brothers' opponent-DON move, Vinsmoke Reiju replaying an Event's [Main] from the trash, and PRB Luffy's delayed "opponent rests a DON."

**Effects that were firing WRONG:**

- **The big one (228 cards):** using a Character as a **Counter** no longer wrongly fires its unrelated On Play / Activate:Main "Then, …" clause. If a card's counter value bailed you out, it used to also, say, draw/trash from a completely different ability. (Haredas, Arlong, Nami, Nico Robin, and ~224 more.)
- Your **"give your Characters +X" buffs** no longer light up the **opponent's** cards as valid targets (a duration like "until your opponent's next turn" was confusing the ownership check).
- **ST14 Luffy Leader** no longer gains **+1000 without an 8-cost Character** in play, and his **"+1 cost to all your Characters"** now actually applies.
- **Franky (ST14-009)** gets his own **+2000 / "can't be K.O.'d"** — it used to skip him and **leak onto other Characters** instead.
- **"…with no base effect"** targeting now only hits vanilla Characters (it was ignoring the restriction).
- More conditions now evaluate: **"if your Leader is multicolored,"** trailing **"…gains +X if you have Y,"** DON/deck/board-state checks.

## A much smarter Advanced AI

The Advanced bot now understands the **engines** meta decks are actually built around — all read from card text, nothing hardcoded to a deck:

- **Trash / mill recursion** (Yamato, Moria, Five Elders, Nami, Teach) — the trash is a **resource**. The bot now runs the **sacrifice-to-recur** line (trash a small body to deploy a bigger one from the trash, e.g. Yamato's 6→8, Momonosuke's 5→9) and plans around replaying from the trash. *(+27% win rate in Yamato's best matchup in testing.)*
- **DON engines** (Enel's 6-DON cap "band," purple `DON!! −N`) — it reasons about the deck's real DON-deck cap and threshold bands, and values a `DON!! −N` play by the effect it buys **minus** the DON it returns (so it'll take Crocodile's leader bounce and Enel's event suite instead of hoarding DON).
- **Cost manipulation** (Blackbeard) — recognizes the **"lower a Character's cost → K.O. it by cost"** combo instead of reading −cost as a weak debuff.
- **Removal fidelity** — it now tells **cost-K.O. vs power-K.O. vs −power vs −cost vs rest/freeze** apart, so it only targets what an effect can **actually** remove, and treats a real K.O. differently from a temporary shrink.
- **Threat by effect, not just power** — a small **repeatable engine** (e.g. a per-turn cost-reducer) is valued as a bigger threat than a vanilla big body.
- **Multi-attack / restand** (Zoro-style "attack again"), **DON opportunity cost** (use an ability now vs hold DON for a Counter), **trigger use-or-hold**, and **lethal / stall** reads (grind vs go for the kill).

*(Also identified for future modeling: Life manipulation, green rest-lock/freeze control, and DON ramp decks.)*

## Multiplayer & friends

- **Fixed casual play hanging on "Connecting to your opponent."** The second match of a session was silently dropping every message (a stale message handler left over from the first match) — back-to-back games now connect.
- **Friends now persist.** The friends service is re-synced to your account when you sign in, so your list no longer comes up empty. *(Reminder: a friend request is two-way — the other person has to press **Accept**.)*

## Board & UI polish

- **Blocker shield:** a steel-grey **ring glow** that hugs the icon, rides high on the card's center-facing edge (and flips correctly for the top player). It turns **gold on hover** and shows a **Blocker info popout**, and **clicking the shield declares the block** (same as clicking the card).
- **DON groupings** now stay tight together instead of flying to opposite edges when you split them.
- **End-of-match "Show Results"** moved off the opponent's hand so you can actually read their final cards.
- Cards you played this turn **without Rush** are **faded/greyed** to show they can't attack yet; **rested cards tip onto their right side**.
- You can now **see your opponent moving cards around in their hand**.
- **Themed update screen** — the launch updater shows a smooth progress bar, your **current → new** version, and these patch notes.
