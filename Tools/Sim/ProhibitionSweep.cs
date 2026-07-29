using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// `lifewatcher` proved a prohibition is not executed, on ONE clause of ONE card (OP12-099
    /// Kalgara's "you cannot draw cards…", which drew a card). This asks it of every prohibition
    /// clause in the pool that names an action the interpreter can actually run.
    ///
    /// Two of them are Life mechanics, which is why this sweep matters beyond tidiness:
    ///
    ///   OP02-004 / OP06-020   "you cannot ADD LIFE CARDS TO YOUR HAND using your own effects…"
    ///
    /// Executed as an instruction, that sentence HANDS YOU A LIFE CARD — the exact opposite of what
    /// it says, and a free heal-in-reverse that no Life count check elsewhere would attribute to the
    /// right cause.
    ///
    /// The classifier is read from the ENGINE (GameEngine.IsProhibitionClause), not re-implemented
    /// here. Re-implementing it is the recurring bug in this codebase — two copies of one rule that
    /// drift — and a sweep with its own private copy would keep passing after the engine's changed.
    ///
    ///   NO-OP    every executable prohibition clause leaves hand, Life, field and trash untouched
    ///   FLOOR    the sweep saw a non-trivial number of clauses (a zero from zero is not a result)
    ///   LIVE     a NORMAL action clause on the same fixture DOES change state — otherwise "nothing
    ///            happened" proves only that the fixture is inert
    ///   NARROW   a "cannot" inside a relative clause that MODIFIES a target is still executed
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- prohibitionsweep
    /// </summary>
    public static class ProhibitionSweep
    {
        private const int Baseline = 0;

        public static int Run()
        {
            Console.WriteLine("=== A clause that FORBIDS an action must never perform it ===");

            var clauses = ExecutableProhibitions();
            var performed = new List<string>();
            int checkedCount = 0;

            foreach (var (id, clause) in clauses)
            {
                Board b;
                try { b = new Board(id); } catch (Exception) { continue; }
                var before = b.Fingerprint();
                try { b.Drive(clause); } catch (Exception) { continue; }
                checkedCount++;
                var after = b.Fingerprint();
                if (before != after)
                    performed.Add($"{id}  \"{Trim(clause, 66)}\"  {before} -> {after}");
            }

            Console.WriteLine($"  executable prohibition clauses found: {clauses.Count} ({checkedCount} driven)");
            Console.WriteLine($"  CLAUSES THAT CHANGED THE BOARD      : {performed.Count}");
            foreach (var s in performed.Take(10)) Console.WriteLine("    " + s);

            // LIVE control: the same fixture must be able to SEE a change, or every zero above is
            // just an inert board. This is the floor that the heal sweep originally lacked.
            var live = new Board("OP12-099");
            var f0 = live.Fingerprint();
            live.Drive("Draw 1 card.");
            bool fixtureCanSeeChange = live.Fingerprint() != f0;
            Console.WriteLine($"  control — a NORMAL action clause moved the board: {fixtureCanSeeChange}");

            // NARROW control: "cannot" buried in a relative clause modifying the target is NOT a
            // prohibition, and must still be treated as a live instruction.
            bool relativeClauseStillLive =
                !GameEngine.IsProhibitionClause("K.O. up to 1 Character that cannot be K.O.'d by effects.");
            Console.WriteLine($"  control — a relative-clause \"cannot\" stays executable  : {relativeClauseStillLive}");

            SweepRatchet.Reset();
            SweepRatchet.AtMost("prohibition clauses actually driven (floor)", Math.Max(0, 6 - checkedCount), 0);
            SweepRatchet.AtMost("fixture cannot detect a real change", fixtureCanSeeChange ? 0 : 1, 0);
            SweepRatchet.AtMost("relative-clause \"cannot\" wrongly suppressed", relativeClauseStillLive ? 0 : 1, 0);
            SweepRatchet.AtMost("clauses that PERFORMED the action they forbid", performed.Count, Baseline);
            return SweepRatchet.Result();
        }

        /// <summary>Every prohibition clause in the pool that names something the interpreter can do.
        /// Classification comes from the engine, so this cannot drift away from the guard it tests.</summary>
        private static List<(string Id, string Clause)> ExecutableProhibitions()
        {
            var outp = new List<(string, string)>();
            var action = new System.Text.RegularExpressions.Regex(
                @"\b(draw|trash|K\.O\.|play|attach|return|add)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var def in CardData.Library.Values.Where(d => d != null)
                                                       .GroupBy(d => d.Id).Select(g => g.First())
                                                       .OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                foreach (var field in new[] { def.Effect, def.Trigger })
                {
                    if (string.IsNullOrWhiteSpace(field)) continue;
                    foreach (var line in field.Split('\n'))
                    foreach (var part in SplitSentences(line))
                    {
                        var p = part.Trim();
                        if (p.Length == 0) continue;
                        if (!GameEngine.IsProhibitionClause(p)) continue;
                        if (!action.IsMatch(p)) continue;
                        outp.Add((def.Id, p));
                    }
                }
            }
            return outp;
        }

        private static IEnumerable<string> SplitSentences(string line)
        {
            foreach (var s in System.Text.RegularExpressions.Regex.Split(line, @"(?<=\.)\s+"))
            {
                var t = s.Trim();
                if (t.StartsWith("Then,", StringComparison.OrdinalIgnoreCase)) yield return t.Substring(5).Trim();
                else yield return t;
            }
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board(string cardId)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "prohibition" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" })
                    { p.Hand.Add(Make(id, p.Seat, "hand")); p.Trash.Add(Make(id, p.Seat, "trash")); }
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    foreach (var c in p.Life) c.FaceUp = false;
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-pz-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                    p.AbilityUsedThisTurn.Clear();
                }
                S.CharacterArea[0] = Make(cardId, "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                St.PendingEffects.Clear();
            }

            /// <summary>Hand / Life / field / trash on BOTH sides. A prohibition must move none of
            /// them; counting only the controller's hand would miss "your opponent trashes…".</summary>
            public string Fingerprint() =>
                $"S{S.Hand.Count}/{S.Life.Count}/{S.CharacterArea.Count(x => x != null)}/{S.Trash.Count}"
              + $" N{N.Hand.Count}/{N.Life.Count}/{N.CharacterArea.Count(x => x != null)}/{N.Trash.Count}";

            public void Drive(string clause)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 8; i++)
                {
                    if (St.DeckLook != null)
                    {
                        var order = St.DeckLook.Cards.Select(x => x.InstanceId).ToList();
                        int b0 = St.EventLog.Count;
                        St = GameEngine.ApplyCommand(St, new GameCommand
                        { Type = "deckLookConfirmOrder", Seat = "south", OrderedInstanceIds = order });
                        if (St.EventLog.Count == b0) break;
                        continue;
                    }
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int before = St.EventLog.Count;
                    St = GameEngine.ApplyCommand(St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.EventLog.Count == before) break;
                }
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
                InstanceId = $"{owner}-{id}-pz-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
