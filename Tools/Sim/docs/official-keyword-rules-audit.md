# Official keyword-rules audit

Audit baseline:

- ONE PIECE CARD GAME Comprehensive Rules v1.2.0, updated January 16, 2026:
  https://en.onepiece-cardgame.com/pdf/rule_comprehensive.pdf
- Official General Rules Q&A, updated February 28, 2025:
  https://en.onepiece-cardgame.com/pdf/qa_rules.pdf
- Engine/card snapshot: 2,636 unique definitions loaded from
  `Assets/StreamingAssets/Cards/official-card-library.json`.

This audit covers every term in Comprehensive Rules section 10. It verifies the
shared keyword/timing semantics. It does not claim that every unique sentence in
all 2,636 card effect bodies is automated; that separate concern remains covered
by the effect-coverage audit.

## Result

All 7 keyword effects and all 17 keywords in section 10 now have an engine route.
The focused executable rules suite passes 16/16:

```powershell
dotnet run --project Tools/Sim/Sim.csproj -c Release -- keywordrulescheck
```

| Official term | Engine status | Validation / entry point |
|---|---|---|
| `[Rush]` | Conforming | Played-this-turn attack gate uses `HasRush`. |
| `[Double Attack]` | Conforming | Damage is fixed at 2; one-Life overkill does not defeat. |
| `[Banish]` | Conforming | Life goes to trash and no Trigger step opens. |
| `[Blocker]` | Conforming | Only an active other card can redirect; one Blocker ends the Block Step. |
| `[Trigger]` | Corrected | Decline sends Life to hand; activation trashes the Trigger card unless its effect moves it; first-damage Trigger resolves before Double Attack's second damage. |
| `[Rush: Character]` | Corrected | Only waives summoning sickness against a normally legal opponent Character target. It is not full Rush and does not permit attacks on active Characters. |
| `[Unblockable]` | Corrected | Printed OP16 tags are recognized even though the imported keyword arrays are empty. |
| `K.O.` | Conforming | Battle/effect K.O. fires On K.O.; direct trashing and the sixth-Character replacement rule do not. |
| `[Activate: Main]` | Conforming | Available to the turn player in Main outside battle. |
| `[Main]` | Conforming | Event Main clauses are played in Main outside battle; Trigger can explicitly invoke them. |
| `[Counter]` | Corrected edge case | Counter Event timing is restricted to the opponent's Counter Step; a Counter can now explicitly buff another own Leader/Character, as allowed by the General Rules Q&A. |
| `[When Attacking]` | Conforming | Fires for the attacking card after attack declaration. |
| `[On Play]` | Conforming | Fires when the card is played; mandatory bodies queue even if no useful target exists. |
| `[End of Your Turn]` | Conforming | Fires in the controller's End Phase before turn transfer. |
| `[End of Your Opponent's Turn]` | Corrected wording | The exact v1.2.0 tag is parsed, resolved, and displayed; the older shorthand remains accepted for compatibility. |
| `[DON!! xX]` | Conforming | Threshold is satisfied at X or more attached DON!!. |
| `DON!! -X` | Conforming | Returns a chosen total from cost area or attached field DON!!, including Stage-hosted DON!!. |
| `[Your Turn]` | Conforming | Continuous/conditional lines are live only on the controller's turn. |
| `[Opponent's Turn]` | Conforming | Continuous/conditional lines are live only on the opponent's turn. |
| `[Once Per Turn]` | Corrected | Usage is per card appearance; all prior usage keys clear when that physical card re-enters the field. |
| `Trash` | Conforming | Hand-trash instructions move the selected hand card to trash and fire hand-trash reactions. |
| `[On Block]` | Conforming | Fires only after that card's Blocker activation; DON thresholds/costs are honored. |
| `[On Your Opponent's Attack]` | Conforming | Fires after the opponent's When Attacking/Attack Step effects. |
| `[On K.O.]` | Conforming | Fires for that card's own effect/battle K.O., not for direct trashing or for K.O.'ing another card. |

## Confirmed corrections made by this audit

1. Effect-granted `[Rush: Character]` previously granted full Rush and permission
   to attack active Characters. Both extra permissions were removed.
2. Printed `[Unblockable]` on OP16 cards was missed because those JSON keyword
   arrays are empty. Clause-leading printed keywords are now recognized without
   treating inline keyword references as grants.
3. Activated pure-draw Triggers incorrectly returned their Life card to hand.
   Activated Trigger cards now go to trash, and Double Attack resumes after the
   first Trigger resolves.
4. Character/Stage Trigger bodies that do not play the Trigger card also
   incorrectly returned it to hand. They now go to trash.
5. Counter commands could only buff the battle target. An explicit legal own
   Leader/Character target is now supported, while omitted targets preserve the
   normal UI default.
6. Once Per Turn usage was tied to the permanent instance ID. Replaying the same
   physical card during the same turn now creates a fresh usage identity.
7. The engine recognized only `[End of Opponent's Turn]`; it now recognizes the
   official `[End of Your Opponent's Turn]` wording too.
8. A Trigger that played its own Character/Stage during Double Attack could
   discard the remaining damage. It now finishes that card's On Play and any
   nested choice/deck-look effects before the second Life damage resumes.

## Regression coverage

The focused suite uses real cards wherever possible and synthetic text only for
the currently unused exact End-of-Opponent timing tag. It covers:

- printed and granted Rush: Character restrictions;
- Rush, Double Attack, Banish, Blocker, Trigger, and Unblockable;
- Trigger decline versus activation destination;
- Trigger sequencing inside Double Attack, including play-this-card and nested
  effect completion;
- off-target Counter buffs;
- Once Per Turn field re-entry;
- exact official end-phase tags;
- K.O. versus direct trash distinction.

Broader engine checks run alongside it:

- `lethaltest`: 14/14 passed;
- `boundary-test`: all deterministic command/privacy boundaries passed;
- `docqtest`, `datest`, `vivitest`, and `odentest`: passed.
