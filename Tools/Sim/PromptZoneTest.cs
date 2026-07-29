using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Does the prompt point at the zone the legal targets are actually IN?
    ///
    /// "Select a card in your hand" when the only legal target sits in the trash is worse than no
    /// prompt at all: the player hunts where nothing is highlighted and concludes the card is
    /// broken. It is also invisible to every other suite here — the engine resolves clicks and
    /// never words them, so a misdirecting prompt resolves perfectly and reads perfectly in the log.
    /// Checking it at all required moving the wording out of GameManager
    /// (GameEngine.DescribeTargetPrompt).
    ///
    /// The oracle: queue a clause, ask the engine which cards it will accept, and compare where
    /// those cards live against the zone the prompt names. Only clauses that actually stop for a
    /// pick are considered — a clause that resolves immediately never shows a prompt.
    ///
    /// Ratcheted rather than gated on zero: "Any" is deliberately vague ("board or in your hand"),
    /// and a handful of clauses legitimately accept targets in more than one zone.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- promptzone
    /// </summary>
    public static class PromptZoneTest
    {
        /// <summary>Held line, not a target. Lower it when a real one is fixed; never raise it.</summary>
        private const int Baseline = 0;

        public static int Run()
        {
            Console.WriteLine("=== Does the prompt name the zone the legal targets are in? ===");

            var seen = new HashSet<string>();
            var misdirected = new List<string>();
            int driven = 0, prompted = 0, noTargets = 0;

            foreach (var def in CardData.Library.Values
                        .Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                        .GroupBy(d => d.Id).Select(g => g.First())
                        .OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split((char)10))
                {
                    var clause = raw.Trim();
                    if (clause.Length == 0) continue;
                    if (!seen.Add(Normalize(clause))) continue;

                    Board b;
                    try { b = new Board(); b.Queue(clause); }
                    catch (Exception) { continue; }
                    driven++;

                    var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) continue;                    // resolved outright: no prompt shown
                    // The panel shows a target prompt ONLY when the effect is not an unpaid cost
                    // prefix — those route to the Use button instead. Without this filter the sweep
                    // reports 27 "misdirecting prompts" that no player can ever see, because the
                    // wording is computed for a branch that is not taken. Measuring a string the UI
                    // does not display is not a defect.
                    if (GameEngine.IsUnpaidCostPrefix(pe)) continue;
                    prompted++;

                    var legal = b.Everything()
                        .Where(x => GameEngine.IsValidEffectTarget(b.St, pe, x))
                        .ToList();
                    if (legal.Count == 0) { noTargets++; continue; }   // glowsweep's problem, not this one

                    string prompt = GameEngine.DescribeTargetPrompt(b.St, pe);
                    if (!PromptCoversZones(prompt, legal, b))
                        misdirected.Add($"{def.Id}  \"{prompt}\"  targets in [{ZonesOf(legal, b)}]  :: {Trim(clause, 68)}");
                }
            }

            Console.WriteLine($"  drove {driven} distinct clauses; {prompted} stopped for a pick, "
                              + $"{noTargets} had no legal target at all");
            Console.WriteLine($"  prompts naming the WRONG zone: {misdirected.Count}");
            foreach (var s in misdirected.Take(10)) Console.WriteLine("    " + s);

            SweepRatchet.Reset();
            SweepRatchet.AtMost("prompts naming a zone with no legal target", misdirected.Count, Baseline);
            return SweepRatchet.Result();
        }

        /// <summary>The prompt is honest if at least one legal target lives where it points. "Board
        /// or in your hand" and the Life-pile wording are deliberately broad and count as covering
        /// what they name.</summary>
        private static bool PromptCoversZones(string prompt, List<CardInstance> legal, Board b)
        {
            bool anyHand  = legal.Any(c => b.ZoneOf(c) == "hand");
            bool anyTrash = legal.Any(c => b.ZoneOf(c) == "trash");
            bool anyLife  = legal.Any(c => b.ZoneOf(c) == "life");
            bool anyBoard = legal.Any(c => b.ZoneOf(c) == "character" || b.ZoneOf(c) == "leader");

            if (prompt.IndexOf("Life pile", StringComparison.OrdinalIgnoreCase) >= 0) return anyLife;
            if (prompt.IndexOf("in your trash", StringComparison.OrdinalIgnoreCase) >= 0) return anyTrash;
            if (prompt.IndexOf("board or in your hand", StringComparison.OrdinalIgnoreCase) >= 0)
                return anyBoard || anyHand;
            if (prompt.IndexOf("in your Life area", StringComparison.OrdinalIgnoreCase) >= 0) return anyLife;
            if (prompt.IndexOf("in your hand", StringComparison.OrdinalIgnoreCase) >= 0) return anyHand;
            if (prompt.Equals("Select a highlighted target.", StringComparison.Ordinal))
                return anyHand || anyTrash || anyBoard || anyLife;   // deliberately broad: several zones
            return anyBoard || anyLife;    // "on the board" — Life cards are clicked on the board too
        }

        private static string ZonesOf(List<CardInstance> legal, Board b) =>
            string.Join(",", legal.Select(b.ZoneOf).Distinct().OrderBy(z => z));

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
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "prompt-zone" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    // Every pickable zone populated, so "the prompt names a zone with nothing in it"
                    // is a statement about the WORDING and not about a thin fixture.
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" })
                    { p.Hand.Add(Make(id, p.Seat, "hand")); p.Trash.Add(Make(id, p.Seat, "trash")); }
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-pz-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                S.CharacterArea[1] = Make("EB03-002", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                N.CharacterArea[1] = Make("EB03-002", "north", "character");
                St.PendingEffects.Clear();
            }

            public void Queue(string clause) =>
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);

            public string ZoneOf(CardInstance c) => c?.Zone ?? "";

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
                InstanceId = $"{owner}-{id}-pz-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
