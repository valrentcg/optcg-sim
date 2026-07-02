# OPTCG Sim — Current UI / Board Architecture Teardown

*Reverse-engineered from `OPTCGSim_Data/Managed/Assembly-CSharp.dll` (build 1.34a) plus the serialized scene `level1`. Everything below is read straight from the compiled game code (IL), so the formulas, sorting bands, and field names are exact. Per-zone coordinate numbers are inspector-serialized on a component in the scene (see "Where the numbers live"), so those are described by system rather than as hard values.*

---

## 1. The big picture

The whole match runs out of one monster `MonoBehaviour` called **`GameplayLogicScript`** (259 serialized fields, ~1,100 methods). It owns every board reference, the turn state machine, and all the per-frame layout. There is no separate "view" layer — board layout, card visuals, and HUD text are all driven imperatively from this one class. That's the single most important thing to know going into an overhaul: **the board is not laid out by Unity layout components (no HorizontalLayoutGroup, no anchors doing the work). Positions are computed in C# every time a zone changes and the card is animated to that point.**

Supporting cast:

- **`CardLogicScript`** — sits on every card GameObject. Holds the card's `LiveCard` data, drives its move animation, and toggles its child highlight objects. Card art and "face up/down" live here.
- **`LiveCard`** — pure data for a card instance (74 fields: tapped, face-up, attached DON list, the `CardDefinition`, etc.).
- **`LocationLogicScript` / `LocationPlayer` / `LocationSet`** — the data-driven coordinate table for every zone. This is the layout "config."
- **`PlayerState`** — per-player bundle of the card lists for each zone (`Lgo_MyDeck`, `Lgo_MyHand`, `Lgo_MyDeploy`, `Lgo_MyTrash`, `Lgo_MyDonCostArea`, `Lgo_MyLifeDeck`, `Lgo_MyLeader`, `Lgo_MyStage`, `Lgo_MyDonDeck`).
- **`ImageLibraryScript`** — the cosmetic atlas: `go_PlayerSheet`, `go_OpponentSheet` (the two play mats), and four card backs: `CardBackRegular`, `CardBackDon`, `CardBackMine`, `CardBackOpponent`.

---

## 2. The zone coordinate system

Every zone's geometry is described by a small struct, **`LocationSet`**:

```
LocationSet { float x; float y; float step; float step2; float width; }
```

- `x`, `y` — anchor (origin) of the zone in Canvas/UI pixel space.
- `step` — spacing between successive cards in the zone.
- `step2` — secondary spacing (used for the second axis / wrapping in some zones).
- `width` — total span the zone is allowed to occupy (used by the hand to compute fan spacing, and by the top-deck viewer).

Each player gets a **`LocationPlayer`** which is just one `LocationSet` per zone:

```
LocationPlayer {
  deck, donDeck, leader, hand, life, donCost,
  deploy, donEquipped, discard, stage, topDeck, topDeckSquish
}
```

And `LocationLogicScript.playerLocations` is an **array of these**. The code indexes it as `player + (bFlipField ? 2 : 0)`, so there are effectively **four entries: player/opponent in normal orientation (0,1) and player/opponent in the flipped orientation (2,3)**. `bFlipField` is the "flip my side to the bottom" toggle, and it swaps which set of coordinates is used rather than rotating a transform.

The coordinate space is UI-canvas pixels measured from the board center: values are in the hundreds, both positive and negative (e.g. you'll see numbers like `±550`, `±410`, `160`, `100`, `40` in the scene data), and the opponent's set mirrors the player's across the center.

### Where the numbers live
The actual `x/y/step/width` values are **serialized on the `LocationLogicScript` component in the scene** (referenced from `GameplayLogicScript.sc_Locations`), not in code. This build ships with Unity type-trees stripped, so the cleanest way to read/edit the exact per-zone values is to open the project in the Unity editor and look at that component's inspector — that's also the easiest single place to retune the whole board for an overhaul, since every `Refresh*Positions` method reads from it live.

---

## 3. Per-zone placement formulas (exact, from IL)

All of these run inside `GameplayLogicScript` and reposition cards by setting a target and calling `CardLogicScript.MoveTo(...)` (which animates — see §5). `L` below = `sc_Locations.playerLocations[playerIndex]`. Cards also get `Canvas.overrideSorting = true` plus an explicit `sortingOrder` so stacking is deterministic.

**Deck** — `RefreshDeckPositions`
- Position: `x = L.deck.x + i*L.deck.step`, `y = L.deck.y (+ i*step on the second axis)`. A tight diagonal/stacked pile.
- Face-up only in debug/reveal states (`state == 26` or `34`); otherwise face-down.
- `sortingOrder = i + 10`.

**Trash / discard** — `RefreshTrashPositions`
- Position: `x = L.discard.x` (fixed), `y = L.discard.y + i*L.discard.step`. A vertically fanned stack so you can see depth.
- Always face-up, rotation reset to identity.
- `sortingOrder = i + 10`.

**Stage** — `RefreshStagePositions`
- Single slot at `L.stage.x, L.stage.y`. If the stage card is rested (`bTapped`) it's rotated `-90°` (`Quaternion.Euler(0,0,-90)`), else identity.

**Deploy (characters on the field)** — `RefreshDeployPositions`
- Position: `x = L.deploy.x + i*L.deploy.step`, `y = L.deploy.y`. A single horizontal row.
- Rested characters rotated `-90°`.
- `sortingOrder = 30` (fixed band).
- **Attached DON** under each character are placed relative to that character: `x = (deploy.x + i*deploy.step) + (L.donEquipped.x + j*L.donEquipped.step)`, with `donEquipped.y` offsetting them so they peek out behind/under the card.

**Hand** — `RefreshHandPositions` + `SetupHandCard`
- Spacing is computed to fan the hand across the zone width:
  `spacing = hand.width / max(1, handCount-1)`, then **clamped to a maximum of `hand.step`** so a small hand doesn't spread too far apart.
- Per card: `x = hand.x + handIndex*spacing`, `y = hand.y`.
- Newly drawn cards are spawned at `z = 80` (depth) and animated in.
- `sortingOrder = handIndex + 50` (hand always renders above the field), rotation identity.
- Own hand is face-up; opponent's hand is face-down (driven by game-style/state checks).

**DON cost area (available/rested DON pool)** — `RefreshDonPositions`
- Base: `x = L.donCost.x + i*L.donCost.step`, `y = L.donCost.y`.
- **Row wrapping via `lDonSpacers`:** the code walks a list of spacer thresholds; each spacer that the index passes adds `+20` to x and `-10` to y. This is how the DON pool wraps into staggered rows instead of one long line.
- Rested DON rotated `-90°`.
- `sortingOrder = i + 10`.

**Top-deck viewer (scry / "look at top N")** — `RefreshTopDeckPositions`
- Lays cards in a row across `topDeck.width` with `topDeck.step` spacing, paged with `go_TopDeckLeftArrow` / `go_TopDeckRightArrow` (`iTopDeckIdx` / `iTopDeckMax`).
- `topDeckSquish` compresses spacing when there are more cards than fit.

**Leader & Life** — single slot (`L.leader`) and a stack (`L.life`) respectively. These don't have a continuous `Refresh*Positions` loop; they're positioned at the moment cards move into them (the various `MoveLifeToTop` / leader-placement paths).

### Sorting-order map (the z-stacking contract)
```
deck pile        : index + 10
trash pile       : index + 10
DON cost pool    : index + 10
deploy row       : 30 (flat)
hand             : index + 50
hovered count badge (see §6) : 175  (drawn on top of everything)
```
If you overhaul rendering, this is the implicit layering contract to preserve or replace.

---

## 4. Card object structure & appearance

Cards are instances of a prefab (`prefab_CardTemplate`) carrying `CardLogicScript`. The visual is a Unity UI `Image` plus several child objects addressed **by sibling index** (this is brittle and worth noting for any redesign):

- **child 3** — "selected" highlight (enabled when `GameplayLogicScript.ImCurrentlySelected` is true for this card).
- **child 4** — "hover" highlight (enabled when `ImHovered` is true).
- **child 6** — a small name/ID text label (`TMP_Text`), set to `cardID` + character name when face-up, hidden when face-down.

`CardLogicScript.SetFaceUp(bool)`:
- **Face-up:** `Image.sprite = CardDatabase.GetCardImage(cardID)`; child-6 label shown with the card's name.
- **Face-down:** sprite = `ImageLibrary.CardBackRegular`, **unless** the card is a DON card (`cardID == "Don"`), in which case `CardBackDon`. (`CardBackMine` / `CardBackOpponent` exist for owner-specific backs / sleeves.)
- **Rested/tapped** state is shown purely by rotating the transform `-90°`; there's no separate rested sprite.

**`ScaleOnHover`** exists as a component but is a dead debug stub (it only `Debug.Log`s "Mouse is over GameObject"). The real hover feedback is the child-4 glow toggled in `CardLogicScript.Update`, not a scale tween. Worth deleting in a cleanup.

Mouse handling note: `CardLogicScript`'s `OnMouseEnter/Over/Down/Exit` are all empty. Hover/click is handled **centrally** by `GameplayLogicScript` (raycast → `go_FocusedObject`), not per-card. So input routing is centralized.

---

## 5. Card movement / animation

`CardLogicScript.MoveTo(Vector3 dest)` just stores `vDestination` and sets `bMoving = true`. The actual motion is in `CardLogicScript.Update`:

```
localPosition = Vector3.MoveTowards(localPosition, vDestination, fMoveSpeed * Time.deltaTime);
if (Vector3.Distance(localPosition, vDestination) < 0.1f) { localPosition = vDestination; bMoving = false; }
```

So every repositioning is a linear glide at `fMoveSpeed` (an inspector value on the prefab), snapping when within `0.1` units. There's no easing/DOTween on card movement itself even though DOTween ships in the build.

---

## 6. HUD count displays & hover reveals

This is handled in **`GameplayLogicScript.ShowFocusedCard`**, which runs every frame and does three things:

1. **Hides all ten count badges** at the top of the frame: `go_P{0,1}DeckCount`, `…DonCount`, `…TrashCount`, `…HandCount`, `…LifeCount` are each `SetActive(false)`.
2. **Card preview:** if `go_FocusedObject` is a face-up card, it enables `img_CardPreview`, sets its sprite to the full card art, and writes `cardID \n characterName` into the preview's label (child 0). If the focused card isn't face-up, the preview is cleared.
3. **Pile count reveal:** if the focused object is **not** a single card, it checks each player's zone list with `list.Contains(go_FocusedObject)`. The matching zone's badge is `SetActive(true)`, its `Canvas.sortingOrder` is forced to **175** (so it floats above the board), and its text is written:
   - Deck / Trash / Hand / Life → the list `.Count` as an integer.
   - **DON → `FormatDonCount`**, which renders **`"{active} ({rested})"`** — active = untapped DON in the cost area, rested = `DonOnField - active`. So a DON badge reads like `5 (2)`.

**Net behavior:** the numeric badges over deck/DON/trash/hand/life only appear while you're hovering that pile, and they're rendered on the very top layer. The big card-zoom preview appears whenever you hover any face-up card. This is exactly the "numbers appear when hovering decks, don, trash" behavior — it's all gated through `go_FocusedObject` + `Contains`, recomputed per frame.

---

## 7. Cosmetics & board chrome

- **Play mats:** `ImageLibraryScript.go_PlayerSheet` / `go_OpponentSheet` are the two half-mats. `GameplayLogicScript.go_P0Board` / `go_P1Board` are the board roots. `bPatronMat` / `bPatronSleeves` swap in patron (supporter) cosmetic mats and sleeves; `bFlipField` chooses which orientation's coordinate set is used (§2).
- **Turn / side glows:** `go_PlayerSideGlow`, `go_OpponentSideGlow`, `go_PlayerSideTurnGlow`, `go_OpponentSideTurnGlow` are the active-side and whose-turn indicators.
- **Attack arrow:** `go_AttackArrow` is the drag-to-attack pointer.
- **Misc HUD:** name plates (`go_P0Name`/`go_P1Name`), turn timer texts (`my_text_TurnTime` / `op_text_TurnTime`), `text_TurnCount`, combat log, chat box.

---

## 8. Implications for an overhaul

- **Everything geometric routes through `LocationSet` values + the `Refresh*Positions` methods.** Retuning the board (zone positions, spacing, hand fan width) can largely be done by editing the `LocationLogicScript` inspector data — no code change — *as long as* you keep using the existing formulas. That's the low-risk path.
- **Sibling-index child lookups** (`GetChild(3/4/6)`) on the card prefab are fragile; any prefab restructure must preserve those indices or the highlights/labels break. A redesign should replace these with named references.
- **Sorting is manual** (the bands in §3). If you move to Unity layout components or a different canvas structure, you'll need to reproduce or replace this ordering contract.
- **Per-frame HUD rebuild:** `ShowFocusedCard` disabling and re-enabling all badges every frame is cheap but couples preview, hover, and counts into one method. Splitting these is a natural refactor target.
- **Dead/!legacy bits to clean:** `ScaleOnHover` (debug stub), and card movement could adopt the already-bundled DOTween for nicer easing.
- **The two-orientation coordinate sets** (normal + flipped) mean any new layout must be authored twice (or the flip reworked to a transform rotation) to keep `bFlipField` working.

---

*Method/field names, formulas, sorting bands, the `-90°` rest rotation, the `+20/-10` DON wrap, hand-fan clamp, `z=80` draw-in, `0.1` snap, and the `"active (rested)"` DON format are all read directly from the IL and are exact. Absolute per-zone x/y/step pixel values are inspector-serialized and best read/edited in the Unity editor on the `LocationLogicScript` component.*
