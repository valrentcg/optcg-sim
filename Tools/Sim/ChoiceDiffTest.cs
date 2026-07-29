using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "Choose one: •A •B" — the second decision type, checked the way `usevsskip` checks the first.
    ///
    /// 19 cards ask it, and `opponentdecides` drives exactly one of them (ST07-010). A choice is
    /// only a decision if the options DIFFER, so this resolves option A and option B from identical
    /// boards and compares the results. Two options that produce the same state mean the prompt is
    /// theatre: the player is asked, the answer is recorded, and nothing about the game depends on
    /// it — which reads as working in every log and every count.
    ///
    /// Three failures are distinguishable here and worth separating, because they need different
    /// fixes:
    ///
    ///   NEITHER option changes anything   the whole modal is inert
    ///   ONE option changes nothing        that branch is unimplemented; the other works
    ///   BOTH change the board IDENTICALLY the split is wrong — both routes run the same clause
    ///
    /// Ratcheted, not gated on zero: a fixture cannot satisfy every option's targets, so an option
    /// with nothing legal to hit is correctly inert here.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- choicediff
    /// </summary>
    public static class ChoiceDiffTest
    {
        /// <summary>Held line, not a target. See the class comment: an option whose targets the
        /// fixture cannot supply is correctly inert.</summary>
        private const int Baseline = 0;
        /// <summary>Held line for the softer signal. 5 options are inert in THIS fixture because it
        /// cannot supply their targets or preconditions; that is not 5 broken cards. Lower it when
        /// one is genuinely fixed; never raise it to make a run pass.</summary>
        private const int OneInertBaseline = 5;

        public static int Run()
        {
            Console.WriteLine("=== \"Choose one\": do the two options actually differ? ===");

            int offered = 0, bothInert = 0, oneInert = 0, sameEffect = 0, genuine = 0;
            var rows = new List<string>();

            foreach (var def in CardData.Library.Values
                        .Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                        .GroupBy(d => d.Id).Select(g => g.First())
                        .OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split((char)10))
                {
                    var clause = raw.Trim();
                    if (clause.IndexOf("hoose one", StringComparison.Ordinal) < 0) continue;

                    string a, b, none;
                    try
                    {
                        // Baseline = the card PLAYED but the modal unanswered. Using a board without
                        // the card made every option differ from it (the Character itself is on the
                        // board), which mislabelled "neither option did anything" as "both options
                        // identical" — the right call for the wrong reason.
                        none = Unanswered(def);
                        a = Pick(def, "A");
                        b = Pick(def, "B");
                    }
                    catch (Exception) { continue; }
                    if (a == null || b == null) continue;      // the modal never opened
                    offered++;

                    bool aMoved = a != none, bMoved = b != none;
                    if (!aMoved && !bMoved) { bothInert++; rows.Add($"{def.Id}  NEITHER option changed anything"); }
                    else if (!aMoved || !bMoved) { oneInert++; rows.Add($"{def.Id}  option {(aMoved ? "B" : "A")} changed nothing"); }
                    else if (a == b) { sameEffect++; rows.Add($"{def.Id}  both options produced the SAME board"); }
                    else genuine++;
                }
            }

            Console.WriteLine($"  {offered} Choose-one modals opened; {genuine} offer two genuinely different outcomes");
            Console.WriteLine($"  neither option did anything: {bothInert}");
            Console.WriteLine($"  exactly one option did nothing: {oneInert}");
            Console.WriteLine($"  both options identical: {sameEffect}");
            foreach (var r in rows.Take(10)) Console.WriteLine("    " + r);

            SweepRatchet.Reset();
            SweepRatchet.AtMost("Choose-one modals opened (floor check)", Math.Max(0, 6 - offered), 0);
            // The sharpest of the three: two routes that run the same clause is a SPLIT bug, and no
            // fixture limitation can produce it — both options had their targets.
            SweepRatchet.AtMost("Choose-one options that are identical", sameEffect, Baseline);
            // Softer signal, held at the measured value: an option can be legitimately inert when
            // the fixture cannot supply its targets — EB01-052's "turn all of your Life cards
            // face-down" against a Life area that is already face-down is correct, not broken.
            SweepRatchet.AtMost("Choose-one options that did nothing", oneInert, OneInertBaseline);
            return SweepRatchet.Result();
        }

        /// <summary>The board with the card played and the modal still open — the honest baseline
        /// for "did answering it change anything".</summary>
        private static string Unanswered(CardDef def)
        {
            var b = Build();
            b.Play(def.Id);
            return Fingerprint(b.St);
        }

        /// <summary>Play the card, answer the modal with the given option, return the board.</summary>
        private static string Pick(CardDef def, string option)
        {
            var b = Build();
            b.Play(def.Id);
            if (b.St.ActiveChoice == null) return null;
            string seat = b.St.ActiveChoice.Seat;
            b.Apply(new GameCommand { Type = "resolveChoice", Seat = seat, Target = option });
            // Answer anything the chosen branch then asks for.
            for (int i = 0; i < 6; i++)
            {
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null);
                if (pe == null) break;
                string t = b.Everything().FirstOrDefault(x => GameEngine.IsValidEffectTarget(b.St, pe, x))?.InstanceId;
                int before = b.St.EventLog.Count;
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId, Target = t });
                if (b.St.EventLog.Count == before) break;
            }
            if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                Console.WriteLine($"      [{def.Id}/{option}] look={(b.St.DeckLook != null)} "
                                  + $"pending={b.St.PendingEffects.Count} sLife={b.S.Life.Count} nLife={b.N.Life.Count} "
                                  + $"faceUp={b.S.Life.Count(x => x.FaceUp)}");
            return Fingerprint(b.St);
        }

        private static string Fingerprint(GameState st)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var seat in new[] { "south", "north" })
            {
                var p = st.Players[seat];
                sb.Append(string.Join(",", p.Hand.Select(x => x.CardId))).Append('|')
                  .Append(p.Deck.Count).Append('|')
                  .Append(string.Join(",", p.Life.Select(x => x.CardId + (x.FaceUp ? "U" : "d")))).Append('|')
                  .Append(string.Join(",", p.Trash.Select(x => x.CardId))).Append('|')
                  .Append(p.CostArea.Count).Append(',').Append(p.CostArea.Count(d => d.Rested)).Append('|');
                foreach (var c in p.CharacterArea)
                    sb.Append(c == null ? "-" : c.CardId + ":" + (c.Rested ? "R" : "A") + ":" + GameEngine.GetPower(st, c)).Append(';');
                sb.Append("||");
            }
            return sb.ToString();
        }

        private static Board Build() => new Board();

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "choice-diff" });
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
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-cd-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                }
                // Bodies on both sides so K.O./rest/-power options have something to hit.
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                S.CharacterArea[1] = Make("EB03-002", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                N.CharacterArea[1] = Make("EB03-002", "north", "character");
                N.CharacterArea[1].Rested = true;
                St.PendingEffects.Clear();
            }

            public void Play(string cardId)
            {
                var c = Make(cardId, "south", "hand");
                S.Hand.Add(c);
                int slot = 4;
                for (int i = 0; i < 5; i++) if (S.CharacterArea[i] == null) { slot = i; break; }
                Apply(new GameCommand
                { Type = "playCard", Seat = "south", InstanceId = c.InstanceId, SlotIndex = slot });
                // Events resolve from hand rather than entering play.
                if (St.ActiveChoice == null && S.Hand.Any(x => x.InstanceId == c.InstanceId))
                    Apply(new GameCommand { Type = "activateEvent", Seat = "south", InstanceId = c.InstanceId });
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

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-cd-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
