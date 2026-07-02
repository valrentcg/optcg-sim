# One Piece TCG Simulator — Implementation Status
Last updated: 2026-06-25 (Session 6)

## Project Files
- `Assets/Scripts/Engine/CardData.cs` — immutable card definitions, ST01+ST02 hardcoded, JSON loader
- `Assets/Scripts/Engine/GameState.cs` — mutable game state model
- `Assets/Scripts/Engine/GameEngine.cs` — all rules logic (pure C#, no Unity)
- `Assets/Scripts/GameManager.cs` — Unity MonoBehaviour UI layer
- `Assets/StreamingAssets/Cards/official-card-library.json` — 4572 official cards (ST01–ST30, OP01–OP16, EB01–EB04, P, PRB)

## Card Library Size
- Total cards: 4572 across 53 sets
- Cards with any effect text: 4170
- Cards with trigger text: 790

---

## Rule Mechanics — Implementation Status

Legend: ✅ Done | 🔧 Partial | ❌ Not implemented

### Core Turn Structure
| Mechanic | Status | Notes |
|---|---|---|
| Refresh phase (unrest all) | ✅ | |
| Draw phase | ✅ | |
| DON!! draw phase (1 first turn, 2 after) | ✅ | |
| Main phase | ✅ | |
| Battle phase state machine | ✅ | Block → Counter → Damage → Trigger |
| End phase | ✅ | |
| Turn 1 no-attack rule | ✅ | |
| Win: empty life | ✅ | |
| Win: empty deck | ✅ | |
| Mulligan (once each) | ✅ | |
| Coin flip / turn order choice | ✅ | |
| Seeded deterministic RNG | ✅ | |

### Battle Mechanics
| Mechanic | Status | Notes |
|---|---|---|
| Attack declaration (rest attacker) | ✅ | |
| Attack requires rested or leader target | ✅ | |
| Blocker keyword | ✅ | |
| Counter step (play counter cards from hand) | ✅ | |
| Event counter cards ([Counter]) | ✅ | Guard Point, Scalpel, Repel hardcoded |
| Trigger step (reveal life, use/pass) | ✅ | |
| KO character → trash | ✅ | |
| Deal damage → take life to hand | ✅ | |
| Rush keyword (attack turn played) | ✅ | printed + modifier-granted |
| Double Attack (attack twice per turn) | ✅ | `AttackCountThisTurn` tracker; second attack while rested allowed |
| Banish keyword (trash life instead of take to hand) | ✅ | BanishLifeCard() + ResolveAttack routing (Session 3) |
| Unblockable keyword | ✅ | BlockAttack checks printed keyword + HasKeywordModifier (Session 3) |
| cannotBeKod (battle path) | ✅ | ResolveAttack checks before MoveToTrash |
| cannotBeKod (effect path) | ✅ | Generic K.O.-by-power pattern in TryResolveKnownEffect checks HasModifier (Session 4) |
| canAttackActive modifier | ✅ | DeclareAttack bypass for rested defender check |
| freeze modifier (cannot be unrested) | ✅ | SetRested + ApplyStartOfTurn both check |
| cannotAttack modifier | ✅ | DeclareAttack checks |
| No-Blocker grant (Diable Jambe, Luffy 5-cost) | ✅ | |
| BlockerPowerBan (Usopp: no Blocker ≥5000) | ✅ | |

### DON!! System
| Mechanic | Status | Notes |
|---|---|---|
| Attach DON!! to leader/character | ✅ | |
| Pay DON!! cost to play card | ✅ | |
| DON!! returns at refresh | ✅ | |
| DON passive power bonuses ([DON!! xN]) | ✅ | GetPassiveDonPowerBonus() — generic text parser; replaces hardcoded dict (Session 3) |
| Give rested DON!! to card | ✅ | Luffy leader, Nami, Brook patterns |
| Set DON!! as active (1 or 2) | ✅ | |
| Set DON!! as active (3+) | ✅ | Generic "Set up to N DON!! as active" pattern (Session 4) |
| Opponent DON!! manipulation | ✅ | Apoo rest DON pattern |

### Timing Hooks
| Timing | Status | Card count in library | Notes |
|---|---|---|---|
| [On Play] | ✅ | 1663 | Generic queue + TryResolveKnownEffect |
| [Activate: Main] | ✅ | 683 | Generic fallback: parses DON cost (Unicode circled digits), pays DON, detects hand/trash/play zone, queues for TryResolveKnownEffect (Session 3) |
| [When Attacking] | ✅ | 470 | Generic fallback after per-card switch: parses DON req, once-per-turn gate, queues effect (Session 3) |
| [End of Your Turn] | ✅ | 112 | Generalized scanner + auto-resolve "Set this as active" |
| [Start of Your Turn] | ✅ | 0 in library | Hook exists; no library cards use this text |
| [End of Opponent's Turn] | ✅ | 0 in library | Hook exists; no library cards use this text |
| [On KO] | ✅ | 0 in library | FireOnKoEffects() exists; no library cards use "[On KO]" text exactly |
| [Your Turn] passive | ✅ | 171 | GetTurnPassiveAuraBonus() generic scanner — any card, DON req, rested, feature filter (Session 3) |
| [Opponent's Turn] passive | ✅ | 133 | Same GetTurnPassiveAuraBonus() handles both timing strings |
| [Once Per Turn] gate | ✅ | Many | ActivateMain generic fallback reads "[Once Per Turn]" text + AbilityUsedThisTurn (Session 3) |

---

## Effect Patterns — TryResolveKnownEffect

Frequency = how many cards in the full 4572-card library use this pattern.

| Pattern | Cards | Status | Notes |
|---|---|---|---|
| DON!! manipulation (all forms) | 1523 | 🔧 | Give rested DON, set 1/2 active ✅; set 3+ ❌; rest opp DON ✅ |
| Play from hand (free/effect) | 1023 | 🔧 | Straw Sword play-Supernovas ✅; generic play-from-hand ❌ |
| KO by effect | 820 | ✅ | Rested cost≤3 ✅; generic power-threshold ✅; Blocker cost≤3 ✅; cannotBeKod checked ✅ (Session 4) |
| Power buff (gains +N power) | 685 | ✅ | Any value, turn or battle scope, feature filter |
| Search / look top N of deck | 665 | ✅ | Generic: "Look at N from top of deck … to your hand" → StartDeckLook (Session 4) |
| Draw cards | 648 | ✅ | Generic: "Draw N card(s)" |
| Return own card to hand | 594 | ✅ | Generic: "Return … to your hand" |
| Set as active | 391 | ✅ | Generic: rested char cost≤5, auto End-of-Turn self-unrest |
| Add to Life | 272 | ✅ | Generic: "Place the top N cards of deck on top of Life" (Session 4) |
| Rest opponent's character | 257 | ✅ | Generic: "Rest up to 1 of your opponent's Characters" |
| Rest own character | ~60 | ✅ | Generic: "Rest up to 1 of your Characters" (Session 4) |
| Play from trash | 213 | ✅ | TargetZone.Trash; side panel shows trash card list as buttons (Session 4) |
| Grant rested DON!! | 214 | ✅ | |
| On Play timing | 1663 | ✅ | Queued; TryResolveKnownEffect dispatches to sub-patterns |
| Return opp card to hand | 5 | ✅ | Generic: "Return … to their hand" with optional cost cap |
| Choose one: A or B | 38 | ✅ | TryParseChoiceEffect → ChoiceState; GameManager choice modal (Session 4) |
| Banish (trash life, no trigger) | 38 | ✅ | BanishLifeCard() + ResolveAttack routing (Session 3) |
| Forced opponent discard | ~80 | ✅ | Generic: "Your opponent discards N card(s) from their hand" (Session 4) |
| Self discard from hand | ~50 | ✅ | Generic: TargetZone.Hand, removes clicked card from hand to trash (Session 4) |
| Mill / trash top of deck | ~20 | ✅ | Generic: "trash the top N card(s) of your deck" (Session 4) |
| Grant keyword this turn | ~120 | ✅ | Generic: [Double Attack], [Blocker], [Unblockable] — AddModifier "keyword"/"thisTurn" (Session 4) |
| Cannot be K.O.'d this turn (grant) | ~15 | ✅ | Generic: "cannot be K.O.'d this turn" → AddModifier "cannotBeKod"/"thisTurn" (Session 4) |
| Swap (return own char to hand → play from hand) | ~5 | ✅ | Two-step: board click to return, then queued TargetZone.Hand sub-effect (Session 5) |
| Name replacement ("treated as [Name]") | ~16 | ✅ | state.NameOverrides dict; cleared on MoveToTrash/ReturnToHand; GetEffectiveName() helper (Session 5) |
| Deck search (entire deck, shuffle after) | ~11 | ✅ | StartDeckSearch() → DeckLookState.SearchMode; ResolveDeckLookSelect shuffles remaining (Session 5) |

---

## Trigger Patterns — TryResolveKnownTrigger

| Pattern | Cards | Status |
|---|---|---|
| "Play this card" (character to field) | 198 | ✅ |
| "Activate this card's [Main] effect" | 117 | ✅ |
| Draw cards | 119 | ✅ (via TryResolveKnownEffect) |
| KO effect | 105 | ✅ (via TryResolveKnownEffect) |
| Set as active | 43 | ✅ (via TryResolveKnownEffect) |
| Power buff | 36 | ✅ (via TryResolveKnownEffect) |
| Add to Life | 19 | ✅ (via TryResolveKnownEffect Session 4) |
| Return to hand | 17 | ✅ (via TryResolveKnownEffect) |
| Play Supernovas from hand (Straw Sword) | 1 | ✅ |

---

## ST01 / ST02 Card-by-Card Status

All 34 ST01+ST02 cards are **100% automated**. No manual resolution needed.

### ST01 — Straw Hat Crew
| ID | Card | Effect | Status |
|---|---|---|---|
| ST01-001 | Luffy Leader | Activate:Main give 1 rested DON!! | ✅ |
| ST01-002 | Usopp | DON×2 When Attacking: no Blocker ≥5000 this battle | ✅ |
| ST01-003 | Karoo | No effect | ✅ |
| ST01-004 | Sanji | DON×2 gains [Rush] | ✅ |
| ST01-005 | Jinbe | DON×1 When Attacking: +1000 to 1 Leader/Character this turn | ✅ |
| ST01-006 | Chopper | [Blocker] | ✅ |
| ST01-007 | Nami | Activate:Main give 1 rested DON!! | ✅ |
| ST01-008 | Robin | No effect | ✅ |
| ST01-009 | Vivi | No effect | ✅ |
| ST01-010 | Franky | No effect | ✅ |
| ST01-011 | Brook | On Play: give up to 2 rested DON!! | ✅ |
| ST01-012 | Luffy 5-cost | [Rush] + DON×2 When Attacking: no Blocker at all | ✅ |
| ST01-013 | Zoro | DON×1 passive +1000 power | ✅ |
| ST01-014 | Guard Point | [Counter] +3000 battle / Trigger +1000 turn | ✅ |
| ST01-015 | Gum-Gum Jet Pistol | Main KO ≤6000 / Trigger activate Main | ✅ |
| ST01-016 | Diable Jambe | Main no-Blocker grant / Trigger KO Blocker ≤3 cost | ✅ |
| ST01-017 | Thousand Sunny | Activate:Main +1000 to Straw Hat Crew | ✅ |

### ST02 — Worst Generation
| ID | Card | Effect | Status |
|---|---|---|---|
| ST02-001 | Kid Leader | Activate:Main ③ trash from hand → set self active | ✅ |
| ST02-002 | Vito | No effect | ✅ |
| ST02-003 | Urouge | DON×1 conditional +2000 if 3+ chars | ✅ |
| ST02-004 | Bege | [Blocker] | ✅ |
| ST02-005 | Killer | On Play: KO rested opp ≤3 cost / Trigger: play this card | ✅ |
| ST02-006 | Koby | No effect | ✅ |
| ST02-007 | Bonney | Activate:Main ① rest self: look top 5, take 1 Supernovas | ✅ |
| ST02-008 | Apoo | DON×1 When Attacking: rest 1 opp DON!! | ✅ |
| ST02-009 | Law | On Play: set rested Supernovas/Heart Pirates ≤5 cost active | ✅ |
| ST02-010 | Hawkins | DON×1 When Attacking vs character: set self active (once/turn) | ✅ |
| ST02-011 | Heat | No effect | ✅ |
| ST02-012 | Bepo | No effect | ✅ |
| ST02-013 | Kid 7-cost | [Blocker] + DON×1 End of Your Turn: set self active | ✅ |
| ST02-014 | Drake | DON×1 Your Turn passive: +1000 to Supernovas/Navy while rested | ✅ |
| ST02-015 | Scalpel | [Counter] +2000 + set 1 DON active / Trigger: set 2 DON active | ✅ |
| ST02-016 | Repel | [Counter] +4000 + set 1 DON active | ✅ |
| ST02-017 | Straw Sword | Main: rest opp char / Trigger: play Supernovas ≤2 from hand | ✅ |

---

## Session 6 Implementation (2026-06-25 — sub-effects, chaining, conditionals)

**GameEngine.cs additions:**
- `InferTargetZone(text)` — derives EffectTargetZone from effect text ("from your hand" → Hand, "from your trash" → Trash, else Play); used in ResolveChoice and ActivateMain generic fallback
- `FindCardInstance(state, instanceId)` — searches play/hand/trash across both seats; needed when source card may have already moved to trash (events, counter cards)
- `ShallowCloneEffect(src, newText)` — clones a PendingEffect with new text; used by the multi-clause splitter to test clause A
- `FindThenClause(text)` — finds `. Then,` clause separator; returns -1 for "Choose one" effects
- `EvaluateCondition(state, seat, condition)` — evaluates common conditional expressions: character count in play (with optional type filter), opponent hand count, leader type, leader power
- `ResolveChoice` — now calls `InferTargetZone(chosen)` so "Choose one: • Add from trash to hand / • Draw 2" correctly sets TargetZone.Trash for option A
- `ActivateMain` generic fallback — now calls `InferTargetZone` instead of manually checking "from your hand"/"from your trash"
- New TryResolveKnownEffect patterns (in order, before NotAutomated):
  - **Add from trash to hand** — `(Add/add) … from your trash … to your hand` + TargetZone.Trash guard; shows trash card button list via side panel
  - **Look at opponent's hand** — logs all opponent hand card names
  - **Conditional** — text starting with `"If "`: evaluates condition via EvaluateCondition; if met, queues body with inferred TargetZone; if not, logs skip
  - **Multi-clause splitter** — detects `". Then,"` separator; recursively calls TryResolveKnownEffect on part A (preserving targetId for WaitingForTarget round-trips); queues part B with inferred zone on success
- `IsAutomatedEffectPattern` — updated: add-from-trash-to-hand, look-at-opponent's-hand, `If …` prefix, `FindThenClause > 0`

## Status after Session 6 — COMPLETE ✅

All effect sub-types are now handled:
- Choose-one sub-options with targeting work (zone inferred from chosen text)
- Multi-clause effects ("Do X. Then, do Y.") split and chain automatically  
- Conditional effects ("If [condition], do X") evaluated with board-state lookup
- "Add from trash to hand" (vs. play-from-trash to field) distinguished by TargetZone

Remaining: APNAP simultaneous trigger ordering (architectural note only; no cards require it).

---

## Architecture Notes (for AI handoff)

### Key engine contracts
- `GameEngine.TryResolveKnownEffect(state, effect, targetId)` — returns `Resolved | WaitingForTarget | NotAutomated`. Add new patterns here as `if (ContainsAll(text, ...))` blocks before the final `return NotAutomated`.
- `GameEngine.IsAutomatedEffectPattern(text)` — must be kept in sync with TryResolveKnownEffect; used by TryResolveKnownTrigger to decide if a generic event trigger can be auto-queued.
- `GameEngine.TryResolveKnownTrigger(state, defenderSeat, cardFromLife)` — handles trigger step; add new trigger patterns here.
- `GameEngine.ActivateMain(state, seat, instanceId)` — per-card switch; needs a generic fallback path.
- `GameEngine.ApplyWhenAttackingEffects(state, seat, attacker, defenderDef)` — per-card switch; needs generic fallback.
- `GameEngine.QueueEffect(...)` — pushes to `state.PendingEffects`. The UI in GameManager.cs routes card clicks to `resolveEffect` command which calls `TryResolveKnownEffect`.
- `GameEngine.AddModifier(state, source, target, modifierType, duration, keyword)` — adds to `state.ActiveModifiers`. Modifier types: "keyword", "cannotAttack", "cannotBeKod", "canAttackActive", "freeze", "cannotBeRested", "noBlocker", "doubleAttack".
- `EffectTargetZone.Hand` — set this on a PendingEffect to route hand-card clicks to resolveEffect instead of play-card.
- `EffectTargetZone.Trash` — set this to route trash selection; GameManager shows a button list of trash cards in the side panel.
- `state.NameOverrides` (Dictionary<string,string>) — instanceId → effective name; set by "name is treated as" effects via TryResolveKnownEffect; read by `GetEffectiveName(state, card)`.
- `DeckLookState.SearchMode = true` — activates full-deck search mode in `StartDeckSearch()`; after player selects, remaining cards are ShuffleInPlace'd back. `MaxCost` and `CardTypeFilter` add extra eligibility gates beyond `FeatureFilter`.
- `AutomatedCounterPower` — now generic: `def.Counter > 0` first, then regex `\+(\d{3,5})` on event effect text. No more per-card switch needed.
- `CounterWithCard` secondary effects — generalized via "Then," split and `IsAutomatedEffectPattern` check; works for any event with "Then, set N DON!! active" or other known patterns.
- `state.ActiveChoice` — non-null when a "Choose one" effect is waiting. GameManager shows a two-button modal (Choose A / Choose B). Resolved via "resolveChoice" command with Target="A" or "B". `ResolveChoice()` then queues the chosen option text as a new PendingEffect.
- `TryParseChoiceEffect(state, effect)` — called at the top of TryResolveKnownEffect when "Choose one" is in the text; parses bullet-separated options into ChoiceState and removes the original effect from PendingEffects.

### CardModifier system
- Duration: `"thisTurn"` (cleared in ApplyStartOfTurn), `"thisBattle"` (cleared when BattleState ends), `"permanent"`
- `state.AttackCountThisTurn` — cleared each turn start; used by Double Attack logic in DeclareAttack
- `state.ActiveModifiers` — list of CardModifier; do NOT use for power bonuses (use TemporaryPowerBonus / BattlePowerBonus dicts)

### DeckLook flow
- `StartDeckLook(state, seat, source, featureFilter, count)` → sets `state.DeckLook`
- Player clicks a card → `deckLookSelect` command → `ResolveDeckLookSelect`
- Player drags to reorder → `deckLookConfirmOrder` command → `ResolveDeckLookConfirmOrder`
- For generic search: featureFilter="" (no type restriction); count = N from effect text

### Adding new sets
1. Add cards to `CardData.Library` dict (or they load from JSON automatically via `LoadOfficialCardLibrary`)
2. If the card has a new effect pattern: add to `TryResolveKnownEffect` + `IsAutomatedEffectPattern`
3. If the card has Activate:Main with a unique cost: add case to `ActivateMain` switch
4. If the card has When Attacking with a unique effect: add case to `ApplyWhenAttackingEffects`
5. For passive auras: add to `GetPower` (like DrakeAuraBonus)
