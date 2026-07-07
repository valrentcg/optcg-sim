# Card Asset Loading — WebGL Migration Map

Every `Application.dataPath + StreamingAssets` call site found in the project, and
what it becomes under `CardAssets`. Do these in Unity so each file compiles as you go.
The pattern is always: paths become Cards-relative forward-slash strings; `File.Exists`
→ `CardAssets.Exists`; `File.ReadAllBytes/Text` → `await CardAssets.ReadBytesAsync/TextAsync`.

**Boot order requirement:** `await UpdateChecker.CheckAsync()` then `await CardAssets.InitAsync()`
before any manager touches card data (e.g. at the top of MainMenu init).

## DeckBuilderManager.cs

| Line (approx) | Today | Change |
|---|---|---|
| 801 `LoadLibrary()` | `File.ReadAllText(...official-card-library.json)` | make method `async`, `await CardAssets.ReadTextAsync("official-card-library.json")` |
| 4528–4533 `ArtPath()` | probes 4 `File.Exists` candidates | `CardAssets.FirstExisting(CardAssets.ArtCandidates(id))` — returns relPath, not disk path |
| 4548 `ResolveThumbPath()` | `File.Exists(Thumbs/{set}/{id}.jpg)` | `CardAssets.Exists(CardAssets.ThumbCandidate(id))`, fallback to ArtPath as today |
| 4563 `LoadFaceData()` | `File.ReadAllText(face-data.json)` | `await CardAssets.ReadTextAsync("face-data.json")` |
| 4606 face-detect fallback | `tex.LoadImage(File.ReadAllBytes(p))` | `await CardAssets.ReadBytesAsync(rel)` then LoadImage (or skip edge-density heuristic on WebGL — face-data.json covers shipped cards) |
| ~3710 IO stage feeding decode queue | `File.ReadAllBytes` into `item.data` | swap the IO step to `CardAssets.ReadBytesAsync`; the decode queue (line ~3751) is untouched — it already consumes `byte[]` |

## GameManager.cs

| Line | Today | Change |
|---|---|---|
| 252–253 library load | `File.Exists` + read | `await CardAssets.ReadTextAsync("official-card-library.json")`, null check replaces Exists |
| 5701 `GetBackSprite()` | `optcg_card_back.jpg` via File.Exists/LoadFile | `await CardAssets.LoadTextureAsync("optcg_card_back.jpg")` |
| 5712 `GetDonSprite()` | `donCardAltArt.png` | same pattern |
| 5723 `GetDonBackSprite()` | `CardBackDon.png` | same pattern |
| 5741–5744 `LoadFront()` | candidate probing + sync LoadFile | `CardAssets.FirstExisting(CardAssets.ArtCandidates(id))` + `LoadTextureAsync`. **Callers are sync** — convert to request-and-apply: return placeholder/cached immediately, kick the Task, assign the sprite when it completes (mirror DeckBuilder's queue UX). `texCache`/`spriteCache` stay as-is |
| 5767 `LoadColorIcon()` | `ColorIcons/{name}.png` | `$"ColorIcons/{fileName}"` + LoadTextureAsync, cache unchanged |
| 5852–5858 `LoadFile()` | ReadAllBytes + LoadImage w/ mips | becomes thin wrapper over `CardAssets.LoadTextureAsync(rel, mipChain: true)` |

## MainMenuManager.cs

| Line | Today | Change |
|---|---|---|
| 282–287 art candidates | same 4-path probe | `CardAssets.ArtCandidates` + `FirstExisting` |
| 303 | `LoadImage(File.ReadAllBytes(p))` | `ReadBytesAsync` → LoadImage |
| 1504–1505 library | File read | `ReadTextAsync("official-card-library.json")` |
| 1522–1523 face-data | File read | `ReadTextAsync("face-data.json")` |
| 1561–1567 thumbs | `Thumbs/{set}/{id}.jpg` File read | `ThumbCandidate` + `ReadBytesAsync` |

## Notes

- `DeckBuilderManager.cs:116/136` are deck-save files (persistent data, not card art) — leave on `File`/`Application.persistentDataPath` for desktop; WebGL persists via PlayerPrefs/IndexedDB — handle separately if web users need saved decks (or store decks in Cloud Save, which is already integrated).
- Editor still uses direct file IO (`UseCdn=false`), so iteration speed in-editor is unchanged and nothing needs the CDN until you actually build for WebGL.
- After migrating, `StreamingAssets/Cards` can be excluded from WebGL builds entirely (a build script can move it out temporarily, or use a `BuildPlayerProcessor`).
