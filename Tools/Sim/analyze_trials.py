#!/usr/bin/env python3
"""Interim heuristic analysis over trials.part*.jsonl (blueprint §12.2).

The in-process Distiller only writes heuristics.md when a run FINISHES. This reads the sharded trial
rows a run is *currently* producing and recomputes the same paired-difference statistics from partial
data — so heuristics can be inspected mid-run, and features can be cross-cut / interacted in ways the
built-in report doesn't. Read-only; safe to run alongside a live overnight job.

Each trial row is one MATCHED-SEED pair: {aWon, bWon, valid, sDeck, nDeck, f{features}}. The paired
difference d = aWon - bWon in {-1,0,+1} is the causal effect of choosing A over B on that seed (§10.5).

Usage:
  python analyze_trials.py Results/overnight-turnorder [--min 3000] [--interact] [--json policy.json]

--json writes the promoted rules as a machine-readable POLICY the advanced bot can consume: for each
family, the conditions (generic deck/hand features) under which to prefer choice A or B, with effect
size, CI and sample count. The bot computes the same features from its deck vs the opponent's and
looks up the recommended choice — no per-leader script (blueprint §19).
"""
import sys, os, json, glob, math, hashlib
from collections import defaultdict

def split_of(seed, frac):
    """Deterministic train/holdout split by a stable hash of the seed (§11.3)."""
    if frac <= 0: return "train"
    h = int(hashlib.md5(seed.encode()).hexdigest()[:8], 16) % 1000
    return "hold" if h < frac * 1000 else "train"

# family -> (choiceA, choiceB), mirroring Tools/Sim/Heuristics/DecisionFamily.cs
FAMILY_CHOICES = {
    "turn-order":      ("first", "second"),
    "mulligan":        ("keep", "mulligan"),
    "counter-economy": ("conserve", "defend"),
    "aggression":      ("face-rush", "baseline-target"),
}

def wilson_free_ci(sum_d, sum_d2, n):
    """95% CI (normal approx) on the mean paired difference, in percentage points."""
    if n < 2: return (0.0, 0.0, 0.0)
    mean = sum_d / n
    var = (sum_d2 - sum_d*sum_d/n) / (n-1)
    se = math.sqrt(max(0.0, var) / n)
    return (100*mean, 100*(mean-1.96*se), 100*(mean+1.96*se))

def is_bool(f): return f.startswith("hand_has") or f.endswith("_ok") or f in ("hand_flooded","i_am_faster")
def is_float(f): return ("avg_cost" in f) or ("low_curve" in f) or ("density" in f)

def binlabel(f, v):
    if is_bool(f): return "true" if v >= 0.5 else "false"
    if is_float(f):
        step = 0.5 if "cost" in f else 0.1
        return f"{round(v/step)*step:.1f}"
    iv = int(round(v))
    return "6+" if iv >= 6 else str(iv)

def main():
    if len(sys.argv) < 2:
        print(__doc__); return 2
    path = sys.argv[1]
    minn = 3000
    interact = "--interact" in sys.argv
    if "--min" in sys.argv:
        minn = int(sys.argv[sys.argv.index("--min")+1])
    holdout = float(sys.argv[sys.argv.index("--holdout")+1]) if "--holdout" in sys.argv else 0.0

    files = sorted(glob.glob(os.path.join(path, "trials.part*.jsonl")))
    if not files:
        print(f"no trial shards in {path}"); return 2

    # bucket -> [sum_d, sum_d2, n, aWins, bWins]   (train set = the discovery set)
    overall = [0,0,0,0,0]
    feat = defaultdict(lambda: defaultdict(lambda: [0,0,0,0,0]))
    matchup = defaultdict(lambda: [0,0,0,0,0])
    inter = defaultdict(lambda: [0,0,0,0,0])
    # holdout set (§11.3): unseen data used only to re-check rules discovered on train.
    h_overall = [0,0,0,0,0]
    h_feat = defaultdict(lambda: defaultdict(lambda: [0,0,0,0,0]))
    h_matchup = defaultdict(lambda: [0,0,0,0,0])
    style = defaultdict(lambda: [0,0,0,0,0])   # opponent playstyle (nPol) -> bucket (§11.3 robustness)
    bystyle = "--by-style" in sys.argv
    fam = choiceA = choiceB = None
    total = skipped = 0

    def add(b, d, a, bw):
        b[0]+=d; b[1]+=d*d; b[2]+=1; b[3]+=1 if a else 0; b[4]+=1 if bw else 0

    for fn in files:
        with open(fn, encoding="utf-8") as fh:
            for line in fh:
                if not line.strip(): continue
                try:
                    r = json.loads(line)
                except json.JSONDecodeError:
                    continue  # torn tail line of a shard the live job is still writing — skip
                fam = fam or r.get("fam")
                total += 1
                if not r.get("valid", True): skipped += 1; continue
                a, bw = bool(r["aWon"]), bool(r["bWon"])
                d = (1 if a else 0) - (1 if bw else 0)
                f = r.get("f", {})
                mk = f'{r["sDeck"]} vs {r["nDeck"]}'
                add(style[r.get("nPol", "baseline")], d, a, bw)
                if split_of(r.get("seed", ""), holdout) == "hold":
                    add(h_overall, d, a, bw)
                    add(h_matchup[mk], d, a, bw)
                    for k, v in f.items():
                        add(h_feat[k][binlabel(k, v)], d, a, bw)
                    continue
                add(overall, d, a, bw)
                add(matchup[mk], d, a, bw)
                for k, v in f.items():
                    add(feat[k][binlabel(k, v)], d, a, bw)
                if interact:  # 2-way interactions among boolean features
                    bools = sorted(k for k in f if is_bool(k))
                    for i in range(len(bools)):
                        for j in range(i+1, len(bools)):
                            ki, kj = bools[i], bools[j]
                            lbl = f'{ki}={binlabel(ki,f[ki])} & {kj}={binlabel(kj,f[kj])}'
                            add(inter[lbl], d, a, bw)

    m, lo, hi = wilson_free_ci(overall[0], overall[1], overall[2])
    print(f"family={fam}  trials={overall[2]:,} (skipped {skipped:,})  files={len(files)}")
    print(f"OVERALL effect (A vs B): {m:+.1f} pp  [{lo:+.1f}, {hi:+.1f}]  (positive = prefer choice A)\n")

    if bystyle and len(style) > 1:
        print("By opponent playstyle (robust rule holds vs all):")
        for st in sorted(style):
            sm, slo, shi = wilson_free_ci(*style[st][:3])
            print(f"  vs {st:<14} {sm:+.1f} pp  [{slo:+.1f},{shi:+.1f}]  n={style[st][2]:,}")
        print()

    rows = []
    for k, bins in feat.items():
        for b, s in bins.items():
            rows.append((f"{k} = {b}", s))
    for k, s in matchup.items():
        rows.append((f"matchup: {k}", s))
    if interact:
        for k, s in inter.items():
            rows.append((k, s))

    promoted = []
    for cond, s in rows:
        m, lo, hi = wilson_free_ci(s[0], s[1], s[2])
        if s[2] >= minn and (lo > 0 or hi < 0):
            promoted.append((cond, m, lo, hi, s[2]))
    promoted.sort(key=lambda x: -abs(x[1]))

    def hold_bucket(cond):
        if cond.startswith("matchup: "): return h_matchup.get(cond[len("matchup: "):])
        if " = " in cond:
            k, b = cond.split(" = ", 1); return h_feat.get(k, {}).get(b)
        return None

    def validate(cond, train_effect):
        """Re-check a train-discovered rule on the holdout set (§11.3). Returns (status, hold_effect,
        hold_n): 'validated' if the sign agrees on enough holdout data, 'reversed' if it flips,
        'thin' if too little holdout data to judge."""
        hb = hold_bucket(cond)
        if not hb or hb[2] < max(200, int(0.5*minn*holdout)): return ("thin", None, hb[2] if hb else 0)
        hm, _, _ = wilson_free_ci(hb[0], hb[1], hb[2])
        same = (hm >= 0) == (train_effect >= 0)
        return ("validated" if same else "reversed", hm, hb[2])

    validated = {}
    print(f"PROMOTED findings (n>={minn:,}, 95% CI excludes 0): {len(promoted)}"
          + (f"   [holdout {holdout:.0%} cross-check]" if holdout > 0 else ""))
    for cond, m, lo, hi, n in promoted[:40]:
        pref = "A" if m >= 0 else "B"
        tag = ""
        if holdout > 0:
            status, hm, hn = validate(cond, m)
            validated[cond] = status
            tag = f"  | holdout: {status}" + (f" {hm:+.1f}pp n={hn:,}" if hm is not None else f" n={hn:,}")
        print(f"  [{pref}] {cond:<44}  {m:+.1f} pp  [{lo:+.1f},{hi:+.1f}]  n={n:,}{tag}")
    if holdout > 0:
        nv = sum(1 for c,_,_,_,_ in promoted if validated.get(c) == "validated")
        nr = sum(1 for c,_,_,_,_ in promoted if validated.get(c) == "reversed")
        print(f"  holdout summary: {nv} validated, {nr} reversed, {len(promoted)-nv-nr} thin")

    if "--json" in sys.argv:
        outp = sys.argv[sys.argv.index("--json")+1]
        cA, cB = FAMILY_CHOICES.get(fam, ("A", "B"))
        mo, loo, hio = wilson_free_ci(overall[0], overall[1], overall[2])
        rules = []
        for cond, m, lo, hi, n in promoted:
            prefer = cA if m >= 0 else cB
            # split "feature = value" / "matchup: X vs Y" into structured form
            if cond.startswith("matchup: "):
                feature, value = "matchup", cond[len("matchup: "):]
            elif " = " in cond:
                feature, value = cond.split(" = ", 1)
            else:
                feature, value = cond, ""
            rules.append({"feature": feature, "value": value, "prefer": prefer,
                          "effect_pp": round(m, 2), "ci95_pp": [round(lo, 2), round(hi, 2)], "n": n,
                          "holdout": validated.get(cond, "n/a")})
        policy = {"family": fam, "choiceA": cA, "choiceB": cB, "min_samples": minn,
                  "overall": {"prefer": cA if mo >= 0 else cB, "effect_pp": round(mo, 2),
                              "ci95_pp": [round(loo, 2), round(hio, 2)], "n": overall[2]},
                  "rules": rules}
        with open(outp, "w", encoding="utf-8") as fh:
            json.dump(policy, fh, indent=2)
        print(f"\npolicy -> {outp}  ({len(rules)} rules)")
    return 0

if __name__ == "__main__":
    sys.exit(main())
