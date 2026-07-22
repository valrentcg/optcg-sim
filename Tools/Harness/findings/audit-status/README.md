# OPTCG Engine Card-Effect Audit — Status & Resume Point

**This folder is the entry point for resuming the per-card correctness audit.**
Read this file first, then the authoritative ledger, then run the gate and continue.

---

## TL;DR — where we are

- **Fix IDs run through #317.** The current second-pass sweep is **#163–#317 = 155 fixes**
  (the earlier first pass was #1–#162). "~310 fixes" ballpark = correct.
- **Green state (must stay green):**
  - Coverage gate: `OK=2675  NOT_AUTOMATED=0  STUCK=0  CRASH=0  INVARIANT=0`
  - Sim smoke: `P(first)≈54.67%`, no crash / deadlock
- **Last commit:** `01ae085` on branch `advanced-bot-search-knee` (v1.0.19 — audit fixes + release prep, pushed).
- **Status: paused at a saturation point** — the high-value recurring bug classes are all swept & closed;
  what remains is a long tail of niche single-card edge cases (declining ROI, ~0–1 real bug per iteration).

## What this audit is

A **second-pass correctness sweep** of the engine's substring/regex effect interpreter
(`Assets/Scripts/Engine/GameEngine.cs` + `GameState.cs`). It hunts **"gate-invisible" bugs** —
cards that AUTO-RESOLVE fine (so the coverage gate stays green) but resolve **WRONG**. These are
invisible to the coverage gate, so each fix is found by deep-tracing a card's text vs its resolution
and proven with a uniquely-named Tools/Harness probe.

## Authoritative files (all in `Tools/Harness/findings/`)

| File | Purpose |
|---|---|
| **`card-audit-progress.md`** | THE per-fix ledger (#163–#317) + verified-clean families + recurring bug-class notes. **Read this before resuming.** 5398 lines. |
| `audit-cardlist.txt` | Ordered card-id list (2319 cards) for the index-walk mode. |
| `audit-flagged.tsv` | Auditor-flagged engine gaps (COST_UNPAYABLE / MANUAL-UNKNOWN). |
| `audit-status/README.md` | ← you are here (the resume entry point). |

Also: the user's persistent memory index at
`C:\Users\Nperr\.claude\projects\C--Users-Nperr\memory\MEMORY.md` — the top entry
(`project_optcg_playtest_bugs_v1018.md`) carries the count + the RECURRING BUG CLASSES (4a–4n),
the DO-NOT-FIX rulings, and TEST HYGIENE. Read it for the reusable knowledge.

## How to resume (commands)

```bash
# 1. Build + coverage gate (must stay 2675/0/0/0/0)
cd "Tools/Harness" && dotnet build -c Release && dotnet run -c Release --no-build coverage

# 2. Smoke (bot-vs-bot, ~900 games, P(first)≈54.67%, catches crash/deadlock)
cd "Tools/Sim" && dotnet build -c Release && dotnet run -c Release --no-build smoke

# 3. Run a single probe (each fix has one; named like op08096test)
cd "Tools/Harness" && dotnet run -c Release --no-build <probename>
```

Each fix needs a UNIQUELY-named `if (args[0]=="<name>")` probe block in `Tools/Harness/Program.cs`
+ a `## #NNN` entry appended to `card-audit-progress.md`, and coverage must stay green.

## Swept & CLOSED mechanic classes (don't re-chase — see tracker for detail)

- glow≠resolve target-filter drift (#261–#278) · ". Then,"/" and" split failures · removal-replacement
  incl. battle-K.O. (#283–#285, #303) · negation kills keyword effects · timing-gated battle immunity
  (#302) · **base-power "becomes"** all 3 paths (#304–#307) · **mandatory-clause skippable via passEffect**
  (#308/#309) · **dual-type "{A} or {B}" filters** everywhere (#310–#312) · **conditional [Counter] events**
  (#313–#316) · **[End of Your Turn] targeted inline resolution** (#317).

## What's LEFT (the long tail — pick any)

- **Fresh mechanic sweeps** — pick an un-probed area: e.g. `[On K.O.]` play-from-trash filters, `[Trigger]`
  life-card payoffs, `[Rush]`/`[Double Attack]`/`[Banish]` keyword interactions, cost-manip archetypes,
  multi-target selection effects, Life-zone manipulation edge cases.
- **Deferred flagged items** (niche, larger changes): OP11-102 (Event-OR-[Trigger] + mutual-Life-trash payoff);
  FireOnBecomeRested (rest not centralized); the "turn top Life face-down" literal-top-card edge (over-permissive,
  extremely rare).
- **Index-walk mode**: the ordered `audit-cardlist.txt` walk (the `idx 22` note is STALE — the audit long ago
  shifted to thematic sweeps, which find ~1 bug/iter vs the vanilla-heavy index walk).

## Method notes (what makes an iteration productive)

Prioritize cards with **conditions / costs / multi-clause / unusual targeting / passive auras / replacement
effects** — that's where gate-invisible bugs hide; fast-pass vanilla cards. The most productive pattern this
sweep: pick a mechanic → scan the DB for its variants → read the handler → find the phrasing/timing/filter the
resolver mishandles → probe → fix → verify green. Recurring classes (4a–4n in MEMORY.md) are the reusable lenses.
