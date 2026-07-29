# Targeting Arrow — Claude session handoff

**Date:** 2026-07-28
**Author:** Claude (Opus 5), session `5fe12cf1`
**Audience:** Codex, or any agent picking this up
**Companion doc:** `2026-07-27/i-n/outputs/claude-professional-unity-vfx-generation-standard.md`
(Codex's production standard — I read it in full; it is referenced throughout and I agree with
almost all of it. One substantive disagreement is recorded in §7.)

---

## 0. TL;DR for whoever reads this next

1. **The blocker is not technique. It is that no approved visual reference exists.** Dozens of
   variants across two days and two agents have been rejected. Every one was an agent generating
   an aesthetic from nothing. Codex's own standard makes reference decomposition a *required
   input*; the Stage-2 spec fails its own gate on that alone. **Get 2–3 reference images first.**
2. Nothing has been built in Unity. All exploration is browser prototypes, which the standard
   explicitly disqualifies as integration evidence.
3. Two small C# defects in the live project were found and fixed (§5). They compile clean.
4. `dotnet build Assembly-CSharp.csproj` type-checks the game assembly in ~12 s with no Editor.
   I previously believed this impossible and said so repeatedly. It works (§6).

---

## 1. Current state of the live code

`Assets/Scripts/TargetingArrowGraphic.cs` + `Assets/ArrowBeam.shader` are **unchanged in
behaviour**. The shipping arrow is still codex's beam. Two defect fixes are applied but
**uncommitted**, sitting alongside ~19 other uncommitted commits on branch
`advanced-bot-search-knee`.

### Verified project facts (independently match Codex's Part I)
Unity `6000.5.0f1` · URP `17.5` · **URP 2D Renderer** · Linear colour space · HDR on ·
MSAA 1× · render scale 1 · uGUI `ScreenSpaceOverlay` + `ScaleWithScreenSize` ·
Shader Graph via URP · **VFX Graph NOT installed** · Bloom override present, **intensity 0**.

### Consequence that constrains every glow asset
`ScreenSpaceOverlay` composites *after* post-processing, and the Bloom intensity is 0 anyway.
**No camera bloom will ever reach the UI.** Any luminous fringe must be authored into the asset.
Do not thin a halo on the assumption something downstream will bloom it.

---

## 2. Defects found in the shipping arrow (diagnosed, not all fixed)

| # | Defect | Location | Status |
|---|---|---|---|
| 1 | `DestroyImmediate` used at runtime | `TargetingArrowGraphic.cs` `OnDestroy` | **fixed** — branches on `Application.isPlaying` |
| 2 | `beamLastFrame` dictionary is write-only | `GameManager.cs` ~7676 | **fixed** — removed; it implied a sweep mechanism the comment says was deliberately abandoned |
| 3 | Pure additive blending | `ArrowBeam.shader:28` `Blend One One` | **open** |
| 4 | Uniform-alpha collapse | `TargetingArrowGraphic.cs:275` | **open** |
| 5 | ~64 distance evaluations per fragment on a near-fullscreen quad | shader + `Rebuild()` | **open** |

### On #3 — additive cannot produce a hard edge
Additive only *adds* light. It has no opacity, so it can never be solid, never occlude, never
carry a dark keyline, and it washes out over bright card art. Riot's "mixture of soft and hard
edges" is *unreachable* in pure additive. **Premultiplied alpha** (`Blend One OneMinusSrcAlpha`)
gives both behaviours per pixel: `alpha=1` → solid occluding core, `alpha=0, RGB>0` → pure glow.

`ColorMask RGB` on line 29 does **not** block this — it prevents *writing* alpha to the target,
not the blend equation from *using* source alpha. And the URP "premultiplied behaves like
alpha-blend" caveat applies to Shader Graph's preset surface modes, not to a hand-written
explicit `Blend` state.

### On #5 — the cost case
`TargetingArrowGraphic.cs:404-407` emits **one axis-aligned quad covering the padded bounding box
of the whole arrow**. The fragment shader then runs `SPINE_SEGMENTS 32` plus `HEAD_SEGMENTS 16`
twice. On a diagonal drag at 1440p that approaches full screen: ~3.7 M fragments × 64 evaluations
≈ **236 M distance computations per frame**, with the arrow's real footprint at maybe 4–6 % of
that box. Transparent queue, no z-write, so nothing is discarded early.

---

## 3. Why the tip kept breaking (the two-day bug)

The reported symptom was "the bloom doesn't converge to a point at the tip." I produced **nine
wrong diagnoses** before the real one. The root cause:

> **`ArrowBeam.shader` does not have a distance field. It has a *ratio* field.**
> `outerD = dist / (half * 3.4 + floor)` — dividing by a radius that varies along the arrow
> destroys the unit-gradient property that defines an SDF.

Everything downstream silently breaks on a ratio field:

- **`fwidth` antialiasing is impossible**, so the shader hardcodes `aa = 0.70 * px` — named in the
  literature as the naive mistake. With a true SDF, `clamp(0.5 - d/fwidth(d), 0, 1)` handles
  sub-pixel geometry automatically: as the taper narrows below a pixel it **fades opacity and
  keeps its footprint**, which is precisely the behaviour that was missing at the tip.
- **`min()` is invalid.** Union of SDFs is exterior-accurate only; taking `min()` of three
  *pre-normalised* ratios where all three interiors overlap is doubly wrong.
- With a real SDF, core / outline / halo are all **offsets of one number** (`d`, `abs(d)-t`,
  falloff on `d`), which is where hard-and-soft edges come from without reconciling layers.

### The recommended rebuild (not implemented)
Chain **exact `sdUnevenCapsule`** primitives along the spine — exact *because* it corrects for the
taper slope (`b=(r1-r2)/h; a=sqrt(1-b*b)`), which naive `d - r(t)` does not — union with an exact
triangle or swept barb for the head, keep a true metric, antialias with `fwidth`. Replaces
`SampleArm`, `ApexEnvelope`, all three profile functions, the floors, the convergence term, the
forward cut, and the 64-iteration loop.

### The junction lesson, learned four times
The shaft/head join broke four ways: clip at the head's rear → **gap**; run the tube to the tip at
full width → **stub past the point**; clip inside the V → **flat rectangle edge visible through the
V**; triangle-minus-notch head → **reads as two shapes**. In every case the fix is the same:

> **Make the body converge to a point rather than terminate anywhere.**
> A shape that ends at a point has no edge to hide.

### The tip, part two — three more rounds after the above (all in the prototype)
Even with the body converging, the head kept failing. In order:

1. **Shape-language clash.** The head was two straight strokes of *constant width* meeting at a
   hard angle, bolted onto an organic taper. Riot's "contradictory shapes that look muddled".
   Fix: barbs are swept quadratic curves that **taper**, matching the shaft's construction.
2. **A "bulb" at the tip.** Not geometry — the halo. Along the body a falloff is a two-sided band;
   at a convergence the same falloff radiates in **all directions** and becomes a disc.
3. **A "ball" that survived pinching the halo.** Because the halo was only ~a fifth of what draws
   there. **Every style inflates the shape for its inner core** — `aa(d + uW*0.55)` and similar —
   and *inflating a point by a constant produces a circle*. Fixing one term only moves the ball.

> **Fix for 2 and 3: make the FIELD anisotropic past the tip, not any single term.**
> `ds += max(dot(p - B_, tipDir), 0.0) * pinch;` immediately after the head is unioned in.
> Everything downstream — halo, cores, surge, flare — then tapers along the axis while staying
> full width at the sides. One line, applies to all 45 styles.

4. **And the actual defect underneath all of it:** the barb loop seeded its running radius with
   `rp = w0` (full thickness) while the in-loop formula said radius at `tt=0` is `w0 * tipSharp`.
   **The tip was ~8.5 px wide the whole time and the sharpness slider never affected segment 0.**
   I spent two rounds adjusting glow to compensate for a geometry bug.
   Fix: one `barbW(tt, w0)` function used for both the seed and the loop, so they cannot disagree.

**Generalisable:** when an initial value duplicates a formula that also appears in a loop, they
will drift. Extract the function.

---

## 4. Art direction learned from the user (this is the valuable part)

Feedback across the session, in order received. Each line overturned an assumption.

| User said | What it means |
|---|---|
| "horrendous, worse than amateur" | Procedural sketches with no reference are not a style |
| "doesn't feel **mystical**" | I had optimised for **Riot clarity doctrine** — restraint, readability, "VFX artists carry the heavy burden of restraint." That is competitive-MOBA doctrine and close to the *opposite* brief for a card game arrow |
| "**overprocessed** garbage" | But not by adding layers either. Magic = confident clean form + one or two restrained atmospheric layers. Stacking eight processing passes is the named anti-pattern: "noise should modulate a coherent form, not replace it" |
| "none of those are actually creating **arrows**" | I had shipped 27 variants with **no arrowhead geometry at all** — tapered glowing tubes. Silhouette first, always |
| "extended tip past the arrow head" / "rectangle edge then chevron" | The junction bugs in §3 |
| "just a line slowly moving up the arrows" | Constant-speed uniform motion is the tell. `easeOut` travel *decelerates into* the target, so the pulse dwells on the arrowhead |
| "no differences" when switching progression | Real: the surge was scaled by a control I had set to 0.25 |
| "tapered body and an **ugly head**" | **Shape-language clash.** Organic taper + constant-width straight strokes at a hard angle = contradictory shapes. Head must taper and sweep like the body |

### Direction that survives all of it
Mystical, but **restrained**. A clean confident arrow silhouette with a *hint* of atmosphere.
Head and body must share one shape language. Motion must have authored beats, not a scroll.

---

## 5. Research findings worth keeping

Sources are real and were read, not guessed. Most useful first.

- **Shapes (Freya Holmér)** — reference-quality Unity vector library. Generates quad strips with
  **per-point thickness *and* per-point colour**, in-shader AA. Critically: lines thinner than a
  pixel use **"opacity fading based on pixel coverage"** rather than shrinking to nothing. That is
  the exact inverse of what the arrow does.
- **Riot LoL VFX Style Guide** — value range, saturation never at 0 % or 100 %, limited
  analogous/triadic palettes, "**always hand-paint shapes using a mixture of soft and hard
  edges**", motion blur for direction, timing = anticipation → payoff → processing, keep effects
  short. Note the doctrine caveat above.
- **Gradient mapping (Harry Alisavakis)** — greyscale → LUT texture lookup. Replaces the entire
  `OuterProfile`/`MidProfile`/`CoreProfile` cascade with one sample from a PNG you can edit.
- **step vs smoothstep (torch in sky)** — `step` = hard edge, `smoothstep` = soft. Both together
  is the implementation of Riot's rule.
- **Channel packing** — R/G/B/A as four independent greyscale masks, one texture read.
- **SDF antialiasing (blog.pkh.me)** — `h = clamp(0.5 + d/fwidth(d), 0, 1)`; sub-pixel shapes are
  handled automatically by fwidth's adaptive width.
- **Inigo Quilez 2D SDFs** — `sdUnevenCapsule` (exact tapered segment), `sdTriangle`,
  `sdTriangleIsosceles`, exact quadratic bezier.
- **GDC 2023 VFX roundtable** — "**block in effect timing before adding complexity**"; "your
  effect is never done, it's taken away from you"; recognise **noodling** as the signal to stop;
  "**sketching defines timing and feel before editor work begins**". This session did all of that
  backwards.
- **Simon Schreibt, RiME** — the hand-crafted look comes from **intentional correspondence between
  texture layers**, not independent procedural generation.
- **One Piece canon (Haki)** — Armament = black base + user-coloured aura, often **steam-like**;
  advanced Armament in Wano carries **cherry-blossom-shaped particles**; Conqueror's = dark blue
  tint, purple aura, expanding circular wave, **gold sparkle** in Wano. Haki is **coloured per
  user** — a targeting arrow could take its hue from the acting card's colour identity.
- **Arcane circles** — built as **independent counter-rotating layers** (runes / stars / circles),
  never one texture. The documented "arcane rune beam" recipe is rings **+** glyph band **+**
  ribbon simultaneously.
- **Overdraw** — top of every practitioner's profiling list; Unity UI is all transparent queue.

---

## 6. Tooling discovered

### `dotnet build Assembly-CSharp.csproj` — ~12 s, no Unity Editor
Unity leaves the generated csproj in the project root; `dotnet` 8.0 builds it against the Editor's
reference assemblies.

```
cd "C:/Users/Nperr/One Piece TCG Simulator"
dotnet build Assembly-CSharp.csproj -v q -nologo
```

**Baseline: 0 errors, ~40 warnings.** All 40 are pre-existing `CS0649` on `JsonUtility` response
structs (filled by deserialisation). More than ~40 means you added some.

Proves: type errors, missing usings, signature mismatches, renamed members.
Does **not** prove: runtime behaviour, serialization, prefab/scene wiring, shaders.

### `scratchpad/check.js` — static gate for the browser prototypes
Three shader-breaking bugs reached the user as **blank pages** during this session:

1. **`flat` used as an identifier** — reserved GLSL interpolation qualifier. Also `active`. Also
   `sample`, `filter`, `input`, `output`, `buffer`, `shared`, `image`, `packed`, `common`.
2. **Backticks inside a GLSL comment** — terminates the JS template literal holding the shader.
3. **Appending `else if` past a chain's closing brace** — orphaned `else`. **Brace *counts* stay
   balanced**, so counting cannot catch it. Happened twice.

4. **Referencing an identifier a previous edit had deleted** (`dirH`). Every structural check still
   passed, because none of them resolve names.

The checker asserts (non-zero exit on failure): JS parses, brace/paren balance, zero stray
backticks, **branch-chain contiguity by nesting depth**, branch coverage, **undeclared-identifier
resolution** against declarations / function names / parameters / loop counters with a GLSL builtin
whitelist, and cross-checks style count against the progression table and grid cell count. Counts
are derived from one source rather than hardcoded — a stale hardcoded assertion passed silently once
while testing the wrong thing.

Two process notes that matter more than the checks themselves:

- **The depth check caught bug 3 the first time and I overrode it.** It printed the distinct depths
  as information; I had earlier labelled that line a false positive and pattern-matched it as noise.
  It now *asserts* a rule ("at most one depth change across the sorted chain") and names the exact
  offending branch pair. Do not let a check degrade into a number you skim.
- **Run a negative control.** After adding the identifier check I deleted a declaration in a copy
  and confirmed it failed. A check that has never failed is not known to work.

⚠️ It cannot compile GLSL. **Always ship an on-page error panel** so a shader failure is visible
instead of a blank canvas. That panel is what caught bug 3.

---

## 7. One substantive disagreement with the standard

Codex's Part V says:

> "taper bloom radii with geometric width near endpoints… avoid flat caps caused by a minimum blur
> radius that remains full-sized at a zero-width tip."

I argued the opposite — that a halo whose radius is a *multiple* of a width reaching zero has no
halo at the tip, which is the defect actually photographed.

**Proposed resolution (untested).** The artifact depends on the *form* of the minimum:

| Form | Continuity | Predicted result |
|---|---|---|
| `max(half·k, minRadius)` — a **clamp** | discontinuous where the clamp engages | flat cap — the standard's warning is correct |
| `half + constant` — an **offset** | continuous everywhere | smooth round cap flowing out of the body |

Both are non-zero at the tip; only the clamp has a kink. I believe the rule targets the clamp form
and over-generalises to the offset form. **Under the standard's own truthfulness gate this is not
settled** — it needs a flat-colour capture of both at ≥4× magnification, which nobody has produced.

---

## 8. Browser prototypes (exploration only — NOT integration evidence)

Per the standard: *"Do not infer integration from a scratch HTML."* These exist to explore shape
and motion, nothing more. All are self-contained WebGL2/Canvas pages.

| Artifact | Purpose |
|---|---|
| `arrow-bench.html` | Faithful port of `ArrowBeam.shader`'s fragment stage with the shipped constants; reproduces the tip bug |
| `arrow-sdf.html` | Exact-primitive rebuild with `fwidth` AA; toggles true-SDF vs ratio field and shows the distance contours |
| `arrow-textures.html` | Generates 5 channel-packed PNGs: ribbon RGBA, 3 arrowheads, gradient LUT, erosion mask, seamless detail noise. Verified by running the generators headlessly and inspecting pixel statistics |
| `arrow-ten.html` | Ten construction styles, exact SDFs, greyscale + flat-silhouette verification views |
| `arrow-mystic.html` | **Current focus.** 45 styles across 6 families, 12 motion progressions, per-style defaults, freely swappable |

Files live in this session's scratchpad:
`C:\Users\Nperr\AppData\Local\Temp\claude\C--Users-Nperr\5fe12cf1-4a19-4758-b5e1-40228fb4e5d3\scratchpad\`

### The 12 progressions
launch · pulse train · charge · breathe · flicker · crawl · ripple · bloom · shimmer · heartbeat ·
chase · drain. Four have **no travelling element at all** — a moving line is only one idea of
animation and probably the least mystical one. Travelling progressions use **linear travel inside
a window** with the remainder as rest, and amplitude enveloped to zero at both ends so the loop
point is invisible.

---

## 9. What I would do next

1. **Get reference images.** Everything else is blocked on this and has been from the start.
2. Timing blockout before any more visual work (GDC roundtable rule).
3. Stage 3 gate: **flat unlit silhouette capture** at native and ≥4×. Nobody has ever done this for
   this arrow, and it would have caught the missing arrowhead and every junction bug immediately.
4. Then, in order: premultiplied blend (§2.3), erosion dissolve replacing the uniform fade (§2.4),
   the SDF rebuild (§3) which also fixes the cost problem (§2.5).

## 10. Honest status

- ✅ Verified: the two C# fixes compile; the texture generators emit valid non-degenerate PNGs;
  the project/render facts in §1.
- ⚠️ Unverified: **every visual claim in this document.** I cannot see rendered output. Unity MCP
  is entitlement-blocked on this machine, so there are no captures, no profiling, no Play-test.
- ❌ Not done: anything in Unity by me.

### Addendum — `TargetingArrowGraphic.cs` was rewritten mid-session (not by me)
It is now **mesh-native**: real tapered ribbon geometry for the shaft and both barbs, all three
terminating at one shared apex coordinate in every luminous layer, with no separate head sprite,
endpoint cap, distance-field mask or carrier quad. That is the architecture §2.5 argued for and it
removes the near-fullscreen-quad cost case entirely.

**It compiles clean: 0 errors, 40 warnings**, matching the pre-existing baseline exactly.

⚠️ MSBuild gotcha found while checking it: a plain `dotnet build` reported `0 warnings / 0 errors`
in 0.61 s because the assembly was up to date — it had not compiled anything. Use
`--no-incremental` whenever the result matters (real run: 5.6 s, 40 warnings, 0 errors).
