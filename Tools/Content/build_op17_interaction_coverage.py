#!/usr/bin/env python3
"""Build the machine-readable OP17 parser/resolver/test coverage ledger.

The current engine audits remain the executable oracle. This report records their latest verified
counts and adds the per-card evidence that the aggregate Markdown reports intentionally omit.
Run from anywhere: python Tools/Content/build_op17_interaction_coverage.py
"""

from __future__ import annotations

import json
import re
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LIBRARY = ROOT / "Assets/StreamingAssets/Cards/official-card-library.json"
OUT = ROOT / "Tools/Content/op17-interaction-coverage.json"
EFFECT_AUDIT = ROOT / "Tools/Sim/docs/effect-coverage-audit-v2.md"
CONDITION_AUDIT = ROOT / "Tools/Sim/docs/condition-audit.md"

TIMING_TAGS = {
    "On Play", "On K.O.", "When this Character is K.O.'d", "When Attacking",
    "On Block", "Activate: Main", "Main", "On Your Opponent's Attack",
    "End of Your Turn", "End of Your Opponent's Turn", "End of Opponent's Turn",
}

STALE_HYPOTHESES = {
    "OP17-015": "direct-regression-pass: opponent-effect replacement plus typed On K.O. revival",
    "OP17-023": "direct-regression-pass: either East Blue or Straw Hat Crew satisfies replacement type gate",
    "OP17-039": "direct-regression-pass: attack cost, reveal-type condition, and draw-two payoff",
    "OP17-043": "direct-regression-pass: self-only removal replacement pays exactly two hand cards",
    "OP17-045": "direct-regression-pass: ally removal replacement pays exactly two hand cards",
    "OP17-095": "direct-regression-pass: effective-cost-12 passive and chosen three-trash replacement resources",
    "OP17-117": "direct-regression-pass: opponent owns discard decision; decline enables capped K.O.",
    "OP17-118": "direct-regression-pass: counterless condition and different-name total-cost play budget",
}

SOURCE_WARNINGS = {}

SOURCE_RECONCILIATION = {
    "status": "verified-against-official-english-card-list",
    "sourceUrl": "https://en.onepiece-cardgame.com/cardlist/?series=569117",
    "verifiedCards": [
        "OP17-003", "OP17-067", "OP17-069", "OP17-074",
        "OP17-078", "OP17-092", "OP17-095", "OP17-113",
    ],
}

HISTORICAL_REPORTS = [
    {"id": "20260821-023758-905", "cards": ["OP17-040", "SEALED-RAINBOW-LUFFY"],
     "status": "fixed-and-direct-regression-pass",
     "note": "Board-card Leader-combat timing, nonstandard discard cost, type gate, and decline semantics."},
    {"id": "20260821-025919-608", "cards": ["OP17-095", "OP17-119"],
     "status": "current-engine-behavior-validated",
     "note": "Loki has effective cost +12; Zoro correctly observes effective cost 12+."},
    {"id": "20260821-030329-911", "cards": ["OP17-095", "OP17-119"],
     "status": "current-engine-behavior-validated",
     "note": "Second historical effective-cost/Loki report is covered by the same direct regression."},
    {"id": "20260917-001429-532", "cards": ["OP17-001"],
     "status": "fixed-and-deterministic-policy-regression-pass",
     "note": "Exact replay proved both activation and decline were legal. Advanced now declines the optional OP17-001 cost when attack power is strictly below current defense."},
    {"id": "20260917-001539-501", "cards": ["OP17-001"],
     "status": "fixed-and-deterministic-policy-regression-pass",
     "note": "Exact replay proved a 2000-counter card was discarded against an already harmless attack; the same narrow decline policy preserves it."},
]


def clauses(card: dict) -> list[dict]:
    rows: list[dict] = []
    for field in ("effect", "trigger"):
        for index, raw in enumerate((card.get(field) or "").splitlines()):
            text = raw.strip()
            if not text:
                continue
            tags = re.findall(r"\[([^\]]+)\]", text)
            if field == "trigger" or "Trigger" in tags:
                route = "trigger-route"
                recognition = "recognized"
            elif "Counter" in tags:
                route = "counter-route"
                recognition = "recognized"
            elif any(tag in TIMING_TAGS for tag in tags):
                route = "resolver-route"
                recognition = "recognized"
            elif re.search(r"would be (?:K\.O\.'d|removed from the field)|would leave the field", text, re.I):
                route = "removal-replacement"
                recognition = "specialized-route"
            else:
                route = "passive-or-continuous"
                recognition = "specialized-or-state-query"
            rows.append({"field": field, "index": index, "text": text, "tags": tags,
                         "route": route, "recognition": recognition})
    return rows


def current_audit_counts() -> dict:
    """Read the executable audits' latest reports instead of baking their counts into this ledger."""
    effect = EFFECT_AUDIT.read_text(encoding="utf-8-sig")
    condition = CONDITION_AUDIT.read_text(encoding="utf-8-sig")

    def number(text: str, pattern: str) -> int:
        match = re.search(pattern, text, re.I)
        if not match:
            raise RuntimeError(f"Could not parse current audit count with {pattern!r}")
        return int(match.group(1))

    resolver_section, trigger_section = effect, ""
    if "## B. TRIGGER GAPS" in effect:
        resolver_section, trigger_section = effect.split("## B. TRIGGER GAPS", 1)

    return {
        "libraryCards": number(effect, r"Cards:\s*\*\*(\d+)"),
        "resolverRouteBodies": number(effect, r"Non-trigger event bodies[^:]*:\s*\*\*(\d+)"),
        "resolverGapsAllSets": number(effect, r"RESOLVER GAPS[^:]*:\s*(\d+)"),
        "resolverGapsOP17": len(set(re.findall(r"\|\s*(OP17-\d{3})\s*\|", resolver_section))),
        "triggerRouteBodies": number(effect, r"Trigger bodies:\s*\*\*(\d+)"),
        "triggerGapsAllSets": number(effect, r"TRIGGER GAPS:\s*(\d+)"),
        "triggerGapsOP17": len(set(re.findall(r"\|\s*(OP17-\d{3})\s*\|", trigger_section))),
        "printedConditions": number(condition, r"Distinct printed conditions:\s*\*\*(\d+)"),
        "unrecognizedConditionsAllSets": number(
            condition, r"Unrecognized \(fail-closed\) conditions:\s*(\d+)"
        ),
        "unrecognizedConditionsOP17": len(set(re.findall(r"OP17-\d{3}", condition))),
    }


def main() -> None:
    cards = [c for c in json.loads(LIBRARY.read_text(encoding="utf-8-sig"))
             if re.fullmatch(r"OP17-\d{3}", c.get("id", ""))]
    cards.sort(key=lambda c: c["id"])
    expected = [f"OP17-{n:03d}" for n in range(1, 120)]

    test_files = list((ROOT / "Tools/Sim").rglob("*Test.cs"))
    test_text = {p: p.read_text(encoding="utf-8-sig", errors="replace") for p in test_files}
    output_cards = []
    direct_ids = set()
    manual_review_ids = set()
    resolver_clauses = trigger_clauses = 0

    for card in cards:
        cid = card["id"]
        refs = sorted(str(p.relative_to(ROOT)).replace("\\", "/") for p, text in test_text.items()
                      if cid in text)
        if refs:
            direct_ids.add(cid)
        card_clauses = clauses(card)
        resolver_clauses += sum(c["route"] in {"resolver-route", "counter-route"} for c in card_clauses)
        trigger_clauses += sum(c["route"] == "trigger-route" for c in card_clauses)
        if any(c["route"] in {"removal-replacement", "passive-or-continuous"} for c in card_clauses) and not refs:
            manual_review_ids.add(cid)
        output_cards.append({
            "id": cid, "name": card.get("name"), "type": card.get("type"),
            "feature": card.get("feature", ""), "clauses": card_clauses,
            "testReferences": refs, "staleHypothesisDisposition": STALE_HYPOTHESES.get(cid),
            "sourceTextWarnings": SOURCE_WARNINGS.get(cid, []),
            "evidenceLevel": "direct-regression" if refs else
                ("manual-review-needed" if cid in manual_review_ids else "route-recognition"),
        })

    report = {
        "schemaVersion": 1,
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "set": "OP17",
        "verificationLayer": "pure-csharp-engine-source-audit-and-headless-regressions",
        "summary": {
            "cards": len(cards), "expectedCards": 119,
            "numberingComplete": [c["id"] for c in cards] == expected,
            "resolverOrCounterClauses": resolver_clauses, "triggerClauses": trigger_clauses,
            "cardsWithDirectTestReferences": len(direct_ids),
            "cardsWithSourceTextWarnings": len(SOURCE_WARNINGS),
            "specializedCardsWithoutLiteralDirectTest": len(manual_review_ids),
        },
        "currentAggregateAudits": current_audit_counts(),
        "staleAuditDisposition": {
            "previousCardCount": 2822,
            "previousOP17ResolverGaps": 8,
            "status": "stale; all eight are recognized by the current resolver and covered by the new-content regression suite",
        },
        "sourceTextReconciliation": SOURCE_RECONCILIATION,
        "historicalReports": HISTORICAL_REPORTS,
        "limitations": [
            "A recognized parser/resolver route is not proof of every board-state permutation.",
            "Literal test references vary in strength; evidenceLevel distinguishes direct tests from route recognition.",
            "No Unity UI, packaged Windows player, transport, or live multiplayer behavior was exercised by this audit.",
            "OP17-001 historical reports were resolver-capable policy defects; the exact harmless-attack class has deterministic bot-policy regressions.",
            "The eight former source-text warnings were reconciled against Bandai's official English OP-17 card list.",
        ],
        "cards": output_cards,
    }
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(report["summary"], indent=2))
    print(OUT)


if __name__ == "__main__":
    main()
