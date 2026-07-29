using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "heal" is named in the brief every time, and while several Life suites exist, the heal
    /// WORDINGS had never been enumerated the way the Life sentences and the cost shapes were.
    /// There are 53 distinct shapes across 150 occurrences, and they differ in three ways that all
    /// matter:
    ///
    ///   SOURCE     top of your deck (most), your hand, your trash
    ///   POSITION   the top of your Life cards — never the bottom
    ///   FACING     face-DOWN by default; face-up only when the text says so
    ///
    /// The facing is the one worth chasing. A heal that lands face-up when it should be face-down
    /// shows the opponent a card they are not entitled to see, and changes what happens when that
    /// Life card is later dealt as damage — a face-up Life card's [Trigger] is already known. It is
    /// also completely invisible in a log that only counts Life.
    ///
    /// Each shape is driven and the resulting Life card inspected: did it come from the named zone,
    /// is it on top, and is its face right?
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- healsweep
    /// </summary>
    public static class HealSweep
    {
        private const int Baseline = 0;

        public static int Run()
        {
            Console.WriteLine("=== Heals: right source, top of Life, right facing ===");

            var shapes = Shapes();
            int driven = 0, healed = 0, faceUpWanted = 0;
            var wrongFacing = new List<string>();
            var wrongPosition = new List<string>();
            var wrongSource = new List<string>();

            foreach (var (id, clause) in shapes)
            {
                // Several boards per shape, not one. The gates pull in opposite directions — some
                // want FEW Life cards ("if you have 1 or less Life"), others want MANY ("2 or more
                // Life"), others want a full trash ("30 or more cards in your trash") — so no single
                // fixture can satisfy them and a one-board sweep silently exercises a third of the
                // pool. A shape counts as driven if ANY board heals; lowering the floor to match a
                // thin fixture would have been the wrong repair.
                Board b = null;
                bool any = false;
                string grewSeat = "south";
                foreach (var variant in new[] { 1, 4, 30 })
                {
                    Board t;
                    try { t = new Board(lifeCards: variant == 1 ? 1 : 4, trashCards: variant == 30 ? 34 : 0); }
                    catch (Exception) { continue; }
                    // BOTH seats. The face-up heals say "to the top of the OWNER's Life cards", so
                    // targeting an opponent Character grows NORTH's Life — measuring only south
                    // made every face-up shape look ungated, and left the facing check one-sided
                    // (20 face-down, 0 face-up), which would pass against an engine that hardcodes
                    // face-down.
                    int l0 = t.S.Life.Count, n0 = t.N.Life.Count;
                    try { t.Drive(clause); } catch (Exception) { continue; }
                    if (t.S.Life.Count > l0) { b = t; grewSeat = "south"; any = true; break; }
                    if (t.N.Life.Count > n0) { b = t; grewSeat = "north"; any = true; break; }
                }
                if (!any)
                {
                    driven++;
                    if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                        Console.WriteLine("    [no heal] " + Trim(clause, 96));
                    continue;
                }
                driven++; healed++;
                var deckIds = b.StartDeckIds;
                var handIds = b.StartHandIds;
                string topBefore = b.StartTopLifeId;

                var grewLife = grewSeat == "south" ? b.S.Life : b.N.Life;
                var added = grewLife[grewLife.Count - 1];
                bool wantsFaceUp = clause.IndexOf("face-up", StringComparison.OrdinalIgnoreCase) >= 0;
                if (wantsFaceUp) faceUpWanted++;
                if (added.FaceUp != wantsFaceUp)
                    wrongFacing.Add($"{id}  faceUp={added.FaceUp} wanted={wantsFaceUp}  :: {Trim(clause, 62)}");
                // The card the heal added must be the one on TOP, not buried under the old top.
                if (grewSeat == "south" && topBefore != null && added.InstanceId == topBefore)
                    wrongPosition.Add($"{id}  added below the existing top  :: {Trim(clause, 62)}");
                // And it must have come from the zone the clause names.
                bool fromDeck = clause.IndexOf("from the top of your deck", StringComparison.OrdinalIgnoreCase) >= 0;
                bool fromHand = clause.IndexOf("from your hand", StringComparison.OrdinalIgnoreCase) >= 0;
                if (fromDeck && !fromHand && !deckIds.Contains(added.InstanceId))
                    wrongSource.Add($"{id}  not from the deck  :: {Trim(clause, 62)}");
                if (fromHand && !fromDeck && !handIds.Contains(added.InstanceId))
                    wrongSource.Add($"{id}  not from the hand  :: {Trim(clause, 62)}");
            }

            Console.WriteLine($"  {shapes.Count} distinct heal shapes; drove {driven}, {healed} actually added a Life card");
            // A facing check where every driven shape wanted the SAME facing proves nothing, so say
            // the split. The remaining shapes are gated on Leader identity/type conditions this
            // sweep cannot synthesise — stated rather than implied, since "63 shapes" in a summary
            // would read as 63 verified.
            Console.WriteLine($"    of those: {faceUpWanted} want face-UP, {healed - faceUpWanted} want face-DOWN");
            Console.WriteLine($"    {shapes.Count - healed} never healed here (Leader-identity or type gates the fixture cannot meet)");
            Report("wrong FACING (face-up leaks a card the opponent may not see)", wrongFacing);
            Report("added below the existing TOP of Life", wrongPosition);
            Report("card came from the wrong ZONE", wrongSource);

            // The sweep never reached a FACE-UP heal (0 of 20), so its facing check is one-sided and
            // would pass against an engine that hardcodes face-down. Drive one explicitly.
            int faceUpProbe = FaceUpProbe();

            SweepRatchet.Reset();
            SweepRatchet.AtMost("face-up heal verified (0 = the facing check is one-sided)",
                                faceUpProbe == 1 ? 0 : 1, 0);
            // Guard the guard: no heals driven means no failures, which is not a result.
            SweepRatchet.AtMost("heals actually driven (floor check)", Math.Max(0, 20 - healed), 0);
            SweepRatchet.AtMost("heals with the wrong facing", wrongFacing.Count, Baseline);
            SweepRatchet.AtMost("heals landing below the top", wrongPosition.Count, Baseline);
            SweepRatchet.AtMost("heals from the wrong zone", wrongSource.Count, Baseline);
            return SweepRatchet.Result();
        }

        /// <summary>Drive a heal that must land FACE-UP and check it does.
        ///
        /// Without this the facing check is worthless: every shape the sweep reaches wants
        /// face-DOWN, so an engine that ignored the wording entirely and always set FaceUp=false
        /// would score a clean zero. OP03-123's wording is the counter-case.
        ///
        /// Returns 1 on success so the ratchet can gate on it.</summary>
        private static int FaceUpProbe()
        {
            var b = new Board(lifeCards: 2, trashCards: 0);
            b.AddOpponentCharacter("OP15-040");                    // cost 1, under any ceiling
            int n0 = b.N.Life.Count, s0 = b.S.Life.Count;

            b.Drive("Add up to 1 Character with a cost of 8 or less to the top or bottom of the owner's Life cards face-up.");

            if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
            {
                Console.WriteLine($"      [probe] southLife {s0}->{b.S.Life.Count}  northLife {n0}->{b.N.Life.Count}");
                foreach (var e in b.St.EventLog.Skip(Math.Max(0, b.St.EventLog.Count - 8)))
                    Console.WriteLine("      log: " + e.Message);
            }
            var grew = b.N.Life.Count > n0 ? b.N.Life : (b.S.Life.Count > s0 ? b.S.Life : null);
            if (grew == null)
            {
                Console.WriteLine("  face-up probe: the clause never moved a Character to Life — "
                                  + "the facing check remains ONE-SIDED (face-down only)");
                return 0;
            }
            // Whatever actually moved — "up to 1 Character" is unrestricted, so the controller may
            // legally pick their OWN Character, and my first version looked only for the opponent's
            // and reported a facing it had never inspected.
            var moved = grew[grew.Count - 1];
            bool ok = moved != null && moved.FaceUp;
            // The mirror: the SAME clause without the words "face-up" must land face-DOWN. Without
            // this, an engine that always set FaceUp=true would pass the case above perfectly.
            var b2 = new Board(lifeCards: 2, trashCards: 0);
            b2.AddOpponentCharacter("OP15-040");
            int m0 = b2.S.Life.Count, q0 = b2.N.Life.Count;
            b2.Drive("Add up to 1 Character with a cost of 8 or less to the top or bottom of the owner's Life cards.");
            var grew2 = b2.N.Life.Count > q0 ? b2.N.Life : (b2.S.Life.Count > m0 ? b2.S.Life : null);
            bool downOk = grew2 != null && !grew2[grew2.Count - 1].FaceUp;

            Console.WriteLine(ok && downOk
                ? "  facing probes: \"face-up\" lands face-UP and the same clause without it lands face-DOWN"
                : $"  facing probes: faceUpCase={ok} faceDownCase={downOk} — the facing does not follow the wording");
            return ok && downOk ? 1 : 0;
        }

        private static void Report(string label, List<string> rows)
        {
            Console.WriteLine($"  {label}: {rows.Count}");
            foreach (var r in rows.Take(6)) Console.WriteLine("    " + r);
        }

        /// <summary>Every distinct heal sentence in the pool, from BOTH the effect text and the
        /// separate trigger field — the field that hid 42 "you may" clauses from every sweep here
        /// until it was checked.</summary>
        private static List<(string Id, string Clause)> Shapes()
        {
            var seen = new HashSet<string>();
            var outp = new List<(string, string)>();
            foreach (var def in CardData.Library.Values.Where(d => d != null)
                                                       .GroupBy(d => d.Id).Select(g => g.First())
                                                       .OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                foreach (var src in new[] { def.Effect, def.Trigger })
                {
                    if (string.IsNullOrWhiteSpace(src)) continue;
                    foreach (var raw in src.Split((char)10))
                    {
                        var s = raw.Trim();
                        if (s.Length == 0) continue;
                        if (s.IndexOf("Life", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (!System.Text.RegularExpressions.Regex.IsMatch(
                                s, @"\badd\b[^.]*\bto (?:the top of )?(?:your|their|the owner's) Life",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                        var key = System.Text.RegularExpressions.Regex.Replace(s, "[0-9]+", "N");
                        if (!seen.Add(key)) continue;
                        outp.Add((def.Id, s));
                    }
                }
            }
            return outp;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public readonly HashSet<string> StartDeckIds = new HashSet<string>();
            public readonly HashSet<string> StartHandIds = new HashSet<string>();
            public string StartTopLifeId;

            public Board(int lifeCards = 1, int trashCards = 0)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "heal-sweep" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005", "EB01-004" })
                        p.Hand.Add(Make(id, p.Seat, "hand"));
                    // ONE Life card: most heals are gated on "if you have N or less Life", so a full
                    // Life area would leave most shapes untested and the sweep would read clean.
                    for (int i = 0; i < lifeCards; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < trashCards; i++) p.Trash.Add(Make("ST01-005", p.Seat, "trash"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-hs-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                St.PendingEffects.Clear();
                foreach (var x in S.Deck) StartDeckIds.Add(x.InstanceId);
                foreach (var x in S.Hand) StartHandIds.Add(x.InstanceId);
                StartTopLifeId = S.Life.Count > 0 ? S.Life[S.Life.Count - 1].InstanceId : null;
            }

            public void Drive(string clause)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 8; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int peBefore = St.PendingEffects.Count, logBefore = St.EventLog.Count;
                    St = GameEngine.ApplyCommand(St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.PendingEffects.Count == peBefore && St.EventLog.Count == logBefore) break;
                }
            }

            public CardInstance AddOpponentCharacter(string id)
            {
                var c = Make(id, "north", "character");
                for (int i = 0; i < 5; i++) if (N.CharacterArea[i] == null) { N.CharacterArea[i] = c; break; }
                return c;
            }

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
                InstanceId = $"{owner}-{id}-hs-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
