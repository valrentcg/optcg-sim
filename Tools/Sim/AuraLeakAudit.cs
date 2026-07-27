// Passive-aura sweep — the last path the stuck/reach audit cannot reach.
//
// An aura ("All of your {Straw Hat Crew} type Characters gain +1000 power") never becomes a pending
// effect. It is computed inside GetPower / GetCost while the board is read, so it has no target to
// click, nothing to resolve, and nothing for LeaderEffectAudit to ask about. It fails in its own way
// instead: it applies to cards it does not describe, or it fails to apply to cards it does.
//
// The oracle is differential and needs no hand-written expectations. For each aura clause, two boards
// are built that differ ONLY in whether the aura's source is present, each holding one card that
// matches the aura's filter and one that does not. Then:
//
//   the matching card's power/cost MUST change when the source is added;
//   the non-matching card's MUST NOT.
//
// A leak (the second half failing) is the more damaging direction and the one this engine has a
// history of — the aura scanners read a whole multi-sentence effect as one token stream, so a filter
// from one sentence lands on another sentence's aura.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    public static class AuraLeakAudit
    {
        static readonly Regex LeadingTags = new Regex(@"^\s*((?:\[[^\]]+\]\s*/?\s*)+)");
        static int serial;

        sealed class Finding { public string CardId, CardName, Kind, Clause, Detail; }

        public static int Run(string outPath = null)
        {
            var findings = new List<Finding>();
            int checkedClauses = 0, skipped = 0;

            foreach (var d in CardData.Library.Values
                         .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                         .OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                foreach (var raw in (d.Effect ?? "").Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (LeadingTags.IsMatch(line)) continue;          // timed clause — the other audit owns it
                    if (!IsPowerAura(line)) continue;

                    // The aura must name a {type} filter, or there is no "does not match" case to test.
                    var tag = Regex.Match(line, @"\{([^}]+)\}");
                    if (!tag.Success) { skipped++; continue; }
                    // Own-side auras only: an opponent-side aura needs the victim on the other board half,
                    // which changes what "the same board twice" means. Out of scope rather than guessed at.
                    if (line.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0) { skipped++; continue; }

                    var match = PickCharacter(withFeature: tag.Groups[1].Value.Trim(), want: true);
                    var miss = PickCharacter(withFeature: tag.Groups[1].Value.Trim(), want: false);
                    if (match == null || miss == null) { skipped++; continue; }
                    checkedClauses++;

                    var (mOff, xOff) = Measure(d, match, miss, withSource: false);
                    var (mOn, xOn) = Measure(d, match, miss, withSource: true);

                    if (mOn == mOff)
                        findings.Add(new Finding
                        {
                            CardId = d.Id, CardName = d.Name, Kind = "AURA-DEAD", Clause = line,
                            Detail = $"a {{{tag.Groups[1].Value}}} Character ({match.Id}) is unchanged with the aura present ({mOff} both ways)",
                        });
                    if (xOn != xOff)
                        findings.Add(new Finding
                        {
                            CardId = d.Id, CardName = d.Name, Kind = "AURA-LEAK", Clause = line,
                            Detail = $"a NON-{{{tag.Groups[1].Value}}} Character ({miss.Id}) changed {xOff} -> {xOn} — the aura reaches cards it does not describe",
                        });
                }
            }

            outPath ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs", "aura-leak-audit.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, Report(checkedClauses, skipped, findings));

            Console.WriteLine($"auraaudit: {checkedClauses} power auras tested ({skipped} skipped: no {{type}} filter, or opponent-side)");
            Console.WriteLine($"  AURA-LEAK (applies to cards it does not describe): {findings.Count(f => f.Kind == "AURA-LEAK")}");
            Console.WriteLine($"  AURA-DEAD (does not apply to cards it does):       {findings.Count(f => f.Kind == "AURA-DEAD")}");
            Console.WriteLine($"  report: {outPath}");
            return findings.Count == 0 ? 0 : 1;
        }

        // A continuous power aura granted to a GROUP — not a self-buff, and not a cost/keyword line.
        static bool IsPowerAura(string line) =>
            Regex.IsMatch(line, @"\bgains?\s*\+\d{3,5}\s*power", RegexOptions.IgnoreCase)
            // IgnoreCase matters: a leading condition lowercases the scope ("If you have 4 or less cards
            // in your hand, ALL OF YOUR {SMILE} type Characters gain +1000 power").
            && Regex.IsMatch(line, @"\b(?:all of your|your other|your)\b", RegexOptions.IgnoreCase)
            && line.IndexOf("this Character", StringComparison.OrdinalIgnoreCase) < 0
            && line.IndexOf("during this turn", StringComparison.OrdinalIgnoreCase) < 0
            && line.IndexOf("during this battle", StringComparison.OrdinalIgnoreCase) < 0;

        static CardDef PickCharacter(string withFeature, bool want) =>
            CardData.Library.Values.FirstOrDefault(x =>
                x != null && x.Type == "character" && !string.IsNullOrEmpty(x.Id)
                && string.IsNullOrEmpty(x.Effect)                 // vanilla, so it carries no aura of its own
                && (x.Features != null && x.Features.Any(f => f != null
                        && f.IndexOf(withFeature, StringComparison.OrdinalIgnoreCase) >= 0)) == want);

        /// <summary>Power of the matching and non-matching Characters on an otherwise identical board.</summary>
        static (int match, int miss) Measure(CardDef source, CardDef match, CardDef miss, bool withSource)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "aura-audit" });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 8;
            var s = st.Players["south"];
            s.TurnsStarted = 4;
            for (int i = 0; i < s.CharacterArea.Count; i++) s.CharacterArea[i] = null;
            s.Hand.Clear(); s.Trash.Clear(); st.PendingEffects.Clear();

            var m = Inst(match.Id); s.CharacterArea[0] = m;
            var x = Inst(miss.Id); s.CharacterArea[1] = x;
            if (withSource)
            {
                if (string.Equals(source.Type, "leader", StringComparison.OrdinalIgnoreCase)) s.Leader.CardId = source.Id;
                else if (string.Equals(source.Type, "stage", StringComparison.OrdinalIgnoreCase)) s.Stage = Inst(source.Id);
                else s.CharacterArea[2] = Inst(source.Id);
            }
            return (GameEngine.GetPower(st, m), GameEngine.GetPower(st, x));
        }

        static CardInstance Inst(string cardId) => new CardInstance
        {
            InstanceId = $"aura-{serial++}", CardId = cardId, Owner = "south", Zone = "character", PlayedOnTurn = 0,
        };

        static string Report(int tested, int skipped, List<Finding> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Passive-aura leak sweep");
            sb.AppendLine();
            sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"- Power auras tested: **{tested}**  (skipped {skipped}: no {{type}} filter to contrast, or opponent-side)");
            sb.AppendLine();
            sb.AppendLine("Two boards differing only in whether the aura's source is present, each holding one "
                + "Character that matches the aura's {type} filter and one that does not. The matching card's "
                + "power must change; the non-matching card's must not.");
            sb.AppendLine();
            foreach (var kind in new[] { "AURA-LEAK", "AURA-DEAD" })
            {
                var rows = findings.Where(f => f.Kind == kind).ToList();
                sb.AppendLine($"## {kind} — {rows.Count}");
                sb.AppendLine();
                if (rows.Count == 0) { sb.AppendLine("_none_"); sb.AppendLine(); continue; }
                sb.AppendLine("| Card | Name | Detail | Clause |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var f in rows)
                    sb.AppendLine($"| {f.CardId} | {f.CardName} | {f.Detail} | {(f.Clause ?? "").Replace("|", "\\|")} |");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
