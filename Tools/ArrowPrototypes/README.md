# Targeting-arrow prototypes

Self-contained browser pages used to design the targeting arrow. Open any of them
directly in a browser — no build step, no dependencies, no network access.

These are **design tools, not integration evidence.** Nothing here proves anything about
how the arrow behaves in Unity; see `claude-targeting-arrow-handoff.md` for what was and
was not verified.

## The pages

| File | What it is |
|---|---|
| `arrow-mystic.html` | **The one that mattered.** 45 styles across 6 families, 13 motion progressions, per-style defaults. Opens on the tuned **Eclipse** configuration that shipped. |
| `arrow-ten.html` | Ten construction styles from exact SDF primitives, with greyscale and flat-silhouette verification views |
| `arrow-sdf.html` | Exact-primitive rebuild; toggles a true SDF against a ratio field and draws the distance contours |
| `arrow-bench.html` | Faithful port of the *original* `ArrowBeam.shader` fragment stage, at its shipped constants. Reproduces the tip bug |
| `arrow-textures.html` | Generates 5 channel-packed PNGs (ribbon RGBA, arrowheads, gradient LUT, erosion mask, seamless detail noise) |
| `arrow-pro.html`, `arrow-v2.html`, `arrow-styles.html` | Earlier explorations, kept for the record |

## What shipped

`arrow-mystic.html` → **Eclipse**, progression **runout**, with the tuning exported from
the page. That is ported to `Assets/ArrowEclipse.shader` +
`Assets/Scripts/TargetingArrowEclipse.cs`.

**The shader is meant to stay numerically identical to the prototype.** If you change the
maths in one, change it in the other, or this stops being a port and becomes a
re-interpretation — which was tried first and produced something unrelated.

Every page has an **export settings** button (or equivalent) that dumps the current
configuration as JSON. Use it before reloading; sliders reset on load.

## `check.js`

Static checker for `arrow-mystic.html`. Run from this directory:

```
node check.js
```

Non-zero exit on failure. It exists because four shader bugs reached the user as blank
pages, each invisible to the checks that existed at the time:

| Bug | Why nothing caught it |
|---|---|
| `flat` / `active` used as identifiers | Reserved GLSL words, valid-looking C |
| Backticks inside a GLSL comment | Terminates the JS template literal holding the shader |
| `else if` appended past a chain's closing brace | **Brace counts stay balanced** — counting cannot see structure |
| Referencing an identifier a prior edit deleted | No check resolved names |
| Changing a function signature, missing a call site | GLSL has no overloading |

So it now asserts: JS parses, brace/paren balance, no stray backticks, branch-chain
contiguity by nesting depth, branch coverage, undeclared identifiers, and call arity —
with counts derived from a single source rather than hardcoded, because a stale
hardcoded assertion once passed while testing the wrong thing.

**It cannot compile GLSL.** Every page carries an on-page error panel for that reason; a
shader failure must be visible rather than a silent blank canvas.
