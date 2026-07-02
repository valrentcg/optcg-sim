# Holo cards — Unity integration

Two **new files** (drop into the project) + small edits to **two existing files**. Strict rule: only `SR` and `SEC` rarities get holo (SEC→gold pattern, SR→aurora pattern), at intensity 0.32 / coverage 0.25 / soften 12. The background mask (shine off the character) is computed **inside the shader** per pixel — no mask textures needed.

## New files
1. `CardHolo.shader` → put in `Assets/` (e.g. next to `RoundedCardUI.shader`).
2. `CardHoloDriver.cs` → put anywhere under `Assets/Scripts/`.

Then: **Project Settings → Graphics → Always Included Shaders** → add `UI/CardHolo` (so `Shader.Find` works in a build, same as your RoundedCard).

---

## Edit 1 — `Assets/Scripts/Engine/CardData.cs`

**(a)** Add a rarity field to `CardDef`. After the `Features` field (around line 29) add:
```csharp
        /// <summary>Card rarity string from the official library: "SR", "SEC", "L", "R", "C", "UC", "SP CARD", "TR", "P".</summary>
        public string Rarity = "";
```

**(b)** Make `UpsertCard` accept + store rarity. Replace the whole `UpsertCard` method with:
```csharp
        public static void UpsertCard(string id, string name, string type, string color, int cost,
                                      int power = 0, int? life = null, int counter = 0,
                                      string[] keywords = null, string effect = "", string trigger = "",
                                      string[] features = null, string rarity = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            var def = Card(id, name, type, color, cost, power, life, counter, keywords, effect, trigger, features);
            def.Rarity = rarity ?? "";
            Library[id] = def;
        }
```

---

## Edit 2 — `Assets/Scripts/GameManager.cs`

**(a)** In `OfficialCardRecord` (around line 103-111), add a field:
```csharp
        public string rarity = "";
```

**(b)** In `LoadOfficialCardLibrary`, pass rarity through. The `CardData.UpsertCard(...)` call currently ends with `NormalizeFeatures(card));` — change that last argument to:
```csharp
                    NormalizeFeatures(card),
                    card.rarity);
```

**(c)** Add two helpers anywhere in the `GameManager` class (e.g. just above `RoundedCardVisual`):
```csharp
    private Material _cardHoloBaseMaterial;
    private Material GetCardHoloBaseMaterial()
    {
        if (_cardHoloBaseMaterial == null)
            _cardHoloBaseMaterial = new Material(Shader.Find("UI/CardHolo"));
        return _cardHoloBaseMaterial;
    }

    // SR -> pattern 0 (aurora), SEC -> pattern 1 (gold). Everything else: no holo.
    private static bool IsHoloRarity(string cardId, out int pattern)
    {
        pattern = 0;
        if (string.IsNullOrEmpty(cardId)) return false;
        var def = CardData.GetCard(cardId);
        string r = def != null ? def.Rarity : null;
        if (r == "SEC") { pattern = 1; return true; }
        if (r == "SR")  { pattern = 0; return true; }
        return false;
    }
```

**(d)** Make `RoundedCardVisual` apply holo when given a cardId. Change its signature and the material/clip block:

Signature (add the trailing optional param):
```csharp
    private RectTransform RoundedCardVisual(string name, Transform parent, Sprite sprite, out Image image, string holoCardId = null)
```
Replace these two lines near the end of the method:
```csharp
        image.material = GetRoundedCardBaseMaterial();
        // ...
        image.gameObject.AddComponent<RoundedCardClip>().Init(image, RoundedCornerFraction);
```
with:
```csharp
        bool holo = IsHoloRarity(holoCardId, out int holoPattern);
        image.material = holo ? GetCardHoloBaseMaterial() : GetRoundedCardBaseMaterial();
        // ... keep the existing Stretch(...) line that's between these ...
        if (holo)
            image.gameObject.AddComponent<CardHoloDriver>().Init(image, RoundedCornerFraction, holoPattern, 0.32f, 0.25f, 12f);
        else
            image.gameObject.AddComponent<RoundedCardClip>().Init(image, RoundedCornerFraction);
```
(Keep the `Stretch(image.rectTransform, ...)` call that sits between the material line and the clip line.)

**(e)** Pass through `AddRoundedCardImage`. Change its signature + call:
```csharp
    private Image AddRoundedCardImage(Transform parent, string name, Sprite sprite, string holoCardId = null)
    {
        RoundedCardVisual(name, parent, sprite, out var image, holoCardId);
        return image;
    }
```

**(f)** Pass the cardId at the **face-up card-art** call sites (only face-up gets holo):

- **Board card** (~line 2943):
  `RoundedCardVisual("Art", cardBody, faceUp ? GetCardSprite(card.CardId) : GetBackSprite(), out var image);`
  → add a final arg: `, faceUp ? card.CardId : null);`

- **Hand hover art** (~line 2898):
  `RoundedCardVisual("Hand Hover Art", handHoverRoot, GetCardSprite(card.CardId), out var image);`
  → `, card.CardId);`

- **Hand drag ghost** (~line 1117):
  `var art = AddRoundedCardImage(ghost, "Art", GetCardSprite(card.CardId));`
  → `..., card.CardId);`

- **Dragging card art** (~line 4755):
  `manager.AddRoundedCardImage(ghost.transform, "Dragging Card Art", manager.GetCardSprite(card.CardId));`
  → `..., card.CardId);`

Leave every other `RoundedCardVisual` call (backs, DON, life, edges, pile) unchanged — they pass no cardId, so they stay plain.

---

## Behavior
- SR/SEC face-up cards get the holo material + `CardHoloDriver`. The foil sweeps toward the cursor while you hover, and pushes with the card's motion while you drag/reposition/place — the "moves as you drag" feel.
- The shader masks shine to the background (saturation + dark-desaturated + skin-tone exclusion), so it stays off character skin, hair, and outlines.
- Non-SR/SEC cards are untouched (plain `UI/RoundedCard`).

## Notes / tuning
- Per-card settings are the `0.32f, 0.25f, 12f` in the `CardHoloDriver.Init` call (intensity, coverage, soften). Change there.
- True 3D perspective tilt (rotateX/Y like the web viewer) isn't included: your cards render on a screen-space UI canvas with no perspective, so a transform tilt would just skew. The pointer-driven foil sweep + glare is the screen-space-correct equivalent. If you later move cards to a perspective canvas/camera, I can add real tilt.
- Vivid-colored clothing can still catch some shine (no color rule separates "blue coat" from "blue background"); skin/hair/muted clothing are excluded.
