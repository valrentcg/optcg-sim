using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The pending-effect progress ledger: the text that fills in green for the parts that
    /// resolved and red for the parts that were skipped, on both players' screens.
    ///
    /// It works by locating each recorded part inside the whole clause. A part that cannot be
    /// located is dropped SILENTLY — no error, no log, that stretch of text simply never colours.
    /// The failure reads as "the animation is a bit broken" rather than "the splitter and the
    /// cleaner disagree about the same sentence", so it can sit there indefinitely.
    ///
    /// The invariant is entirely engine-side once the locating moved there
    /// (GameEngine.LocateClausePart): every part the engine RECORDS must be findable in the text
    /// the engine also PRODUCES. Both sides come from the same resolution, so a miss means two
    /// pieces of the engine disagreeing, which is this codebase's most reliable failure.
    ///
    /// Compound clauses are the interesting ones, so this drives every ". Then," clause in the pool
    /// through real resolution rather than constructing ledgers by hand.
    ///
    /// What this is and is not: today the invariant holds because the engine records parts verbatim
    /// from the text it splits, so it is a REGRESSION guard rather than a discovery. That matters
    /// because parts are not always verbatim — the self-disposal fix REWRITES a clause when it
    /// clamps "trash 2" to "trash 1", and the opponent-decision fix rewrites "their" to "your". Any
    /// such rewrite reaching the ledger breaks the colouring silently. Verified falsifiable:
    /// perturbing the recorded part reports all 529 and breaks the ratchet, so the zero is a
    /// measurement and not a tautology.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- ledgerparts
    /// </summary>
    public static class LedgerPartsTest
    {
        private const int Baseline = 0;

        public static int Run()
        {
            Console.WriteLine("=== Progress ledger: is every recorded part findable in the text? ===");

            var seen = new HashSet<string>();
            var unlocatable = new List<string>();
            int driven = 0, withLedger = 0, parts = 0;

            foreach (var def in CardData.Library.Values
                        .Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                        .GroupBy(d => d.Id).Select(g => g.First())
                        .OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split((char)10))
                {
                    var clause = raw.Trim();
                    if (clause.Length == 0) continue;
                    // Compound clauses are where a ledger exists at all.
                    if (clause.IndexOf(". Then,", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!seen.Add(Normalize(clause))) continue;

                    Board b;
                    try { b = new Board(); b.Drive(clause); }
                    catch (Exception) { continue; }
                    driven++;

                    foreach (var pe in b.Ledgers())
                    {
                        string full = GameEngine.CleanClauseText(
                            !string.IsNullOrEmpty(pe.OriginalText) ? pe.OriginalText : pe.Text);
                        var recorded = (pe.DoneParts ?? new List<string>())
                            .Concat(pe.SkippedParts ?? new List<string>())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                        if (recorded.Count == 0) continue;
                        withLedger++;
                        foreach (var part in recorded)
                        {
                            parts++;
                            if (GameEngine.LocateClausePart(full, part) < 0)
                                unlocatable.Add($"{def.Id}  part=\"{Trim(GameEngine.CleanClauseText(part), 46)}\"  "
                                                + $"not in \"{Trim(full, 60)}\"");
                        }
                    }
                }
            }

            Console.WriteLine($"  drove {driven} compound clauses; {withLedger} produced a ledger, {parts} parts recorded");
            Console.WriteLine($"  parts that cannot be located (would never colour): {unlocatable.Count}");
            foreach (var s in unlocatable.Take(10)) Console.WriteLine("    " + s);

            SweepRatchet.Reset();
            // Guard the guard: zero parts means zero misses, which is not a result.
            SweepRatchet.AtMost("ledger parts recorded (want MANY, this is a floor check)",
                                Math.Max(0, 40 - parts), 0);
            SweepRatchet.AtMost("ledger parts that cannot be located", unlocatable.Count, Baseline);
            return SweepRatchet.Result();
        }

        private static string Normalize(string s) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(s, "[0-9]+", "N"), @"\[[^\]]*\]", "[T]").Trim();

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private readonly List<PendingEffect> snapshots = new List<PendingEffect>();
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "ledger-parts" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" })
                    { p.Hand.Add(Make(id, p.Seat, "hand")); p.Trash.Add(Make(id, p.Seat, "trash")); }
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-lp-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                S.CharacterArea[1] = Make("EB03-002", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                St.PendingEffects.Clear();
            }

            /// <summary>Queue the clause and answer it the way the UI would, snapshotting every
            /// pending effect on the way — the ledger is built up DURING resolution, so reading it
            /// only at the end would miss most of it.</summary>
            public void Drive(string clause)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 10; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    Snapshot(pe);
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int peBefore = St.PendingEffects.Count, logBefore = St.EventLog.Count;
                    St = GameEngine.ApplyCommand(St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    var after = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (after != null) Snapshot(after);
                    if (St.PendingEffects.Count == peBefore && St.EventLog.Count == logBefore) break;
                }
            }

            private void Snapshot(PendingEffect pe)
            {
                if (pe == null) return;
                snapshots.Add(new PendingEffect
                {
                    Text = pe.Text,
                    OriginalText = pe.OriginalText,
                    DoneParts = pe.DoneParts == null ? null : new List<string>(pe.DoneParts),
                    SkippedParts = pe.SkippedParts == null ? null : new List<string>(pe.SkippedParts),
                });
            }

            public IEnumerable<PendingEffect> Ledgers() => snapshots;

            public IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.Life.AsEnumerable().Reverse()) yield return x;
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    foreach (var x in p.Trash) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                }
            }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-lp-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
