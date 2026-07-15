#!/usr/bin/env python3
"""Consolidate every family's discovered heuristics into ONE policy.json (blueprint §19).

Runs analyze_trials.py --json over each Results/overnight-<family>/ directory (whatever data exists,
partial or complete) and merges the per-family policies into a single machine-readable rule set the
advanced bot loads: for each decision family, the deck/hand-feature conditions under which to prefer
choice A or B, with effect size, CI, sample count, and holdout status.

Usage:
  python build_policy.py [--min 5000] [--holdout 0.2] [--out policy.json] [Results/dir ...]

With no dirs, it discovers Results/overnight-* automatically. Read-only; safe alongside live runs.
"""
import sys, os, json, glob, subprocess, tempfile

def main():
    args = sys.argv[1:]
    def opt(name, default):
        return args[args.index(name)+1] if name in args else default
    minn = opt("--min", "5000")
    holdout = opt("--holdout", "0.2")
    out = opt("--out", "policy.json")
    # positional dirs = anything not a flag/flag-value
    flags = {"--min", "--holdout", "--out"}
    skip = set()
    for fl in flags:
        if fl in args: skip.add(args.index(fl)); skip.add(args.index(fl)+1)
    dirs = [a for i, a in enumerate(args) if i not in skip and not a.startswith("--")]
    if not dirs:
        dirs = sorted(d for d in glob.glob("Results/overnight-*") if os.path.isdir(d))
    if not dirs:
        print("no Results/overnight-* dirs found (and none given)"); return 2

    here = os.path.dirname(os.path.abspath(__file__))
    analyzer = os.path.join(here, "analyze_trials.py")
    families = {}
    for d in dirs:
        if not glob.glob(os.path.join(d, "trials.part*.jsonl")):
            continue
        with tempfile.NamedTemporaryFile("r", suffix=".json", delete=False) as tf:
            tmp = tf.name
        r = subprocess.run([sys.executable, analyzer, d, "--min", minn, "--holdout", holdout, "--json", tmp],
                           capture_output=True, text=True)
        try:
            with open(tmp, encoding="utf-8") as fh:
                pol = json.load(fh)
            fam = pol.get("family") or os.path.basename(d)
            families[fam] = pol
            nrules = len(pol.get("rules", []))
            nval = sum(1 for x in pol.get("rules", []) if x.get("holdout") == "validated")
            print(f"  {fam:<18} {nrules:>3} rules ({nval} holdout-validated)  from {d}")
        except Exception as e:
            print(f"  ! {d}: {e}  (analyzer stderr: {r.stderr.strip()[:200]})")
        finally:
            try: os.remove(tmp)
            except OSError: pass

    policy = {
        "schema": "optcg-heuristic-policy/1",
        "note": "Deck-agnostic decision heuristics from matched-seed counterfactuals. The bot computes "
                "the same generic features from its deck vs the opponent's and looks up the preferred "
                "choice. Prefer rules with holdout=='validated'.",
        "min_samples": int(minn), "holdout_frac": float(holdout),
        "families": families,
    }
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(policy, fh, indent=2)
    tot = sum(len(p.get("rules", [])) for p in families.values())
    print(f"\nconsolidated {len(families)} families, {tot} rules -> {out}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
