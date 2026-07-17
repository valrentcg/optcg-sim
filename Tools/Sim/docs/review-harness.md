# Solo-loop review harness

The purpose: reproduce independent review **inside Claude Code** so a defect doesn't need an external
reviewer (Codex) to be caught. Before Claude declares any nontrivial change "done", it spawns a
**fresh-context reviewer agent** whose only job is to FALSIFY the claim, fixes what the reviewer finds, and
re-reviews until clean.

## Why it's needed (the failure it targets)

Every defect caught during the observation-boundary work fit one of three patterns:
1. **Tests written to confirm, not falsify** — green meant "the code does what I assumed", while the bug
   lived in a gated/unreached path no test touched.
2. **Strong words the mechanism didn't earn** — "complete", "immutable", "structural", "secure".
3. **Opportunistic coverage accepted** — "not reached by these decks" got a pass instead of forcing the case.

A single agent reviewing its own work shares its own blind spots. A fresh-context reviewer does not.

## The loop

1. Build the change + write **falsification-first** tests (each assertion must be *able* to fail).
2. Spawn a reviewer agent (general-purpose or `claude`, read-only intent — it REPORTS, does not edit) with:
   - the **requirement** (what the code must guarantee, in property terms),
   - the **files/diff** to inspect,
   - the **adversarial checklist** below,
   - the instruction: *report findings ranked P0/P1 with `file:line` and a concrete failure scenario; do
     not fix.*
3. Triage findings; fix P0/P1; re-run the falsification tests.
4. Re-review (fresh agent) until it returns nothing material.
5. Only then say "done", naming the mechanism, not an adjective.

## The adversarial checklist (give this to the reviewer verbatim)

1. **Coverage**: does a test actually *exercise* every case the requirement names? Is any relevant code
   path gated/unreachable and therefore untested? Construct the input that reaches it.
2. **Name↔type**: does every field/property name match what it actually holds? (e.g. a `CardId`-named field
   assigned an `InstanceId`.)
3. **Literal↔emitter**: does every `Type == "…"` / string literal match what the producer actually emits?
   (e.g. checking `"attack"` when the engine emits `"declareAttack"`.)
4. **Fail-closed**: does every required field/reference fail closed when absent, rather than emit a partial
   or wrong result?
5. **Claim↔mechanism**: is every strong claim ("complete", "immutable", "structural", "secure",
   "deterministic") actually enforced by the code, or merely asserted? Name the exact line that enforces it,
   or downgrade the claim.
6. **Falsify the property**: can you build an input that violates the stated property? For secrecy, poison
   the hidden fields and walk every reachable reference. For legality, push edge counts. For determinism,
   vary the seed and the process.

## Notes

- The reviewer is READ-ONLY by intent: it returns findings, Claude applies fixes. This keeps the author
  responsible for the change and the reviewer responsible for doubt.
- Prefer a genuinely fresh context (a new agent) over "re-reading my own diff" — the point is the blind spot.
- `/code-review` (and `/code-review ultra`) is a heavier built-in variant for larger diffs.
