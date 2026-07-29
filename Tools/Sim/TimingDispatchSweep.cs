using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The blind spot behind the EB03-053 Nami report, and the reason every other sweep was green
    /// while she was broken in play.
    ///
    /// costresolvesweep and friends queue a clause DIRECTLY via QueueClauseForTest with timing "main".
    /// That exercises the resolver and skips the dispatch entirely - so a card whose trigger never
    /// fires in the first place is invisible to all of them. "Nami on-KO still not prompting me" is
    /// precisely that failure: not a resolver bug, a card that is never asked about at all.
    ///
    /// This sweep drives the REAL trigger for each timing and asks only one question: did the player
    /// get offered anything? 1065 "you may" clauses live across 16 timings; the three driven here are
    /// the largest that can be caused from a headless board.
    ///
    ///   [On Play]         211   play the card from hand
    ///   [Activate: Main]  276   issue activateMain against it on the field
    ///   [On K.O.]          21   K.O. it by effect
    ///
    /// Not offered is a LEAD, not a proven defect - many of these gate on a Leader type, a [Once Per
    /// Turn] flag, or a board the fixture cannot build. The number that matters is the ratio, and any
    /// timing that comes back at or near zero is a dead dispatch path.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- timingsweep
    /// </summary>
    public static class TimingDispatchSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== Do \"you may\" effects OFFER themselves on the real trigger? ===");

            var onPlay = new List<CardDefLite>();
            var actMain = new List<CardDefLite>();
            var onKo = new List<CardDefLite>();

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || !line.StartsWith("[")) continue;
                    if (!Regex.IsMatch(line, @"\byou may\b", RegexOptions.IgnoreCase)) continue;
                    var tag = Regex.Match(line, @"^\[([^\]]+)\]").Groups[1].Value;
                    var lite = new CardDefLite { Id = def.Id, Name = def.Name ?? "", Type = def.Type ?? "", Line = line };
                    if (tag.Equals("On Play", StringComparison.OrdinalIgnoreCase)) onPlay.Add(lite);
                    else if (tag.Equals("Activate: Main", StringComparison.OrdinalIgnoreCase)) actMain.Add(lite);
                    else if (tag.Equals("On K.O.", StringComparison.OrdinalIgnoreCase)) onKo.Add(lite);
                }
            }

            int bad = 0;
            bad += Drive("[On Play]", onPlay, PlayFromHand);
            bad += Drive("[Activate: Main]", actMain, ActivateOnField);
            bad += Drive("[On K.O.]", onKo, KoOnField);

            Console.WriteLine();
            Console.WriteLine(bad == 0
                ? "  No timing came back empty - every driven dispatch path is alive."
                : "  A timing came back at 0% - that dispatch path is DEAD, not merely gated.");
            // Reporting sweep: per-card misses are usually unmet preconditions. Only a whole timing
            // at zero is unambiguous, and that is what the exit code reflects.
            return bad == 0 ? 0 : 1;
        }

        private static int Drive(string label, List<CardDefLite> cards, Func<CardDefLite, bool> drive)
        {
            int offered = 0, notOffered = 0, threw = 0;
            var misses = new List<CardDefLite>();
            foreach (var c in cards)
            {
                try
                {
                    if (drive(c)) offered++;
                    else { notOffered++; misses.Add(c); }
                }
                catch (Exception) { threw++; }
            }
            int usable = offered + notOffered;
            double pct = usable > 0 ? 100.0 * offered / usable : 0;
            Console.WriteLine();
            Console.WriteLine($"  {label}  {cards.Count} clauses");
            Console.WriteLine($"    offered a decision : {offered}");
            Console.WriteLine($"    nothing offered    : {notOffered}");
            if (threw > 0) Console.WriteLine($"    threw              : {threw}");
            Console.WriteLine($"    -> {pct:F1}% of drivable cards ask the player");
            // Split the misses by whether the CLAUSE ITSELF explains them. A conditional body
            // ("If your Leader is [X]") or a named type/trait the fixture has no copy of is an unmet
            // precondition, not a dead path. What is left over is the part worth looking at.
            bool Explained(CardDefLite m)
            {
                // Strip the LEADING timing tags first. Testing the raw line for "[Something]" matches
                // "[Activate: Main]" on every single card, which marks all of them explained and makes
                // the whole split meaningless - it reported 0 unexplained out of 115 before this.
                var body = Regex.Replace(m.Line, @"^(?:\[[^\]]+\]\s*/?\s*)+", "");
                return Regex.IsMatch(body, @"If (your|you|the|there)", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(body, @"\{[^}]+\}")                       // a {Type} the fixture lacks
                    || Regex.IsMatch(body, @"\[[A-Z][^\]]*\]")                  // a [Named Card] requirement
                    || Regex.IsMatch(body, @"cost of \d+ or (less|more)", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(body, @"\d{4,5} (base )?power", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(body, @"given DON!!|currently given", RegexOptions.IgnoreCase);
            }
            // Segment by the two features that recur among the misses. If either segment offers at a
            // much lower rate than the rest, that is a class-level defect rather than 50 separate
            // unmet preconditions - the distinction the raw count cannot make.
            bool OncePerTurn(CardDefLite m) => m.Line.IndexOf("[Once Per Turn]", StringComparison.OrdinalIgnoreCase) >= 0;
            bool CircledDon(CardDefLite m) => Regex.IsMatch(m.Line, @"[①-⑩➀-➉❶-❿]");
            void Segment(string name, Func<CardDefLite, bool> sel)
            {
                var inSeg = cards.Where(sel).ToList();
                var missSeg = misses.Where(sel).ToList();
                if (inSeg.Count == 0) return;
                double rate = 100.0 * (inSeg.Count - missSeg.Count) / inSeg.Count;
                Console.WriteLine($"    segment {name,-18} {inSeg.Count,4} clauses, {rate,5:F1}% offered");
            }
            Segment("[Once Per Turn]", OncePerTurn);
            Segment("no [OncePerTurn]", m => !OncePerTurn(m));
            Segment("circled-DON cost", CircledDon);
            Segment("no circled DON", m => !CircledDon(m));

            var unexplained = misses.Where(m => !Explained(m)).ToList();
            Console.WriteLine($"    of the misses, explained by an unmet precondition : {misses.Count - unexplained.Count}");
            Console.WriteLine($"    of the misses, UNEXPLAINED                        : {unexplained.Count}");
            foreach (var m in unexplained.Take(10))
                Console.WriteLine($"      ?? {m.Id,-10} {Trim(m.Name, 16),-16} {Trim(m.Line, 96)}");
            if (unexplained.Count > 10) Console.WriteLine($"      ... and {unexplained.Count - 10} more unexplained");
            return (usable > 0 && offered == 0) ? 1 : 0;
        }

        /// <summary>[On Play]: put the card in hand with DON!! to spare and play it for real.</summary>
        private static bool PlayFromHand(CardDefLite c)
        {
            if (!c.Type.Equals("character", StringComparison.OrdinalIgnoreCase)
                && !c.Type.Equals("stage", StringComparison.OrdinalIgnoreCase)) return true;  // not drivable this way
            var b = new Board();
            var hand = b.Hand("south", c.Id);
            b.Apply(new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = hand.InstanceId, SlotIndex = 0 });
            // If it never reached the field the fixture failed, not the dispatch - do not count it.
            bool landed = b.S.CharacterArea.Any(x => x != null && x.CardId == c.Id)
                       || (b.S.Stage != null && b.S.Stage.CardId == c.Id);
            if (!landed) return true;
            return b.Offered();
        }

        /// <summary>[Activate: Main]: the card is already on the field; activate it.</summary>
        private static bool ActivateOnField(CardDefLite c)
        {
            var b = new Board();
            CardInstance src;
            if (c.Type.Equals("stage", StringComparison.OrdinalIgnoreCase)) src = b.Stage("south", c.Id);
            else if (c.Type.Equals("leader", StringComparison.OrdinalIgnoreCase)) src = b.LeaderCard("south", c.Id);
            else src = b.Character("south", c.Id);
            if (src == null) return true;
            b.Apply(new GameCommand { Type = "activateMain", Seat = "south", Target = src.InstanceId });
            return b.Offered();
        }

        /// <summary>[On K.O.]: the card is on the field and gets K.O.'d by an effect - the EB03-053
        /// Nami case exactly.</summary>
        private static bool KoOnField(CardDefLite c)
        {
            if (!c.Type.Equals("character", StringComparison.OrdinalIgnoreCase)) return true;
            var b = new Board();
            var src = b.Character("south", c.Id);
            if (src == null) return true;
            GameEngine.AuditKoByEffect(b.St, "south", src.InstanceId);
            return b.Offered();
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        private sealed class CardDefLite
        { public string Id, Name, Type, Line; }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "timing-sweep" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                // A board with something of everything, so a card is not recorded as "never asked"
                // when it was really "asked about nothing available".
                for (int i = 0; i < 5; i++) S.Life.Add(Card("ST01-005", "south", "life"));
                for (int i = 0; i < 5; i++) N.Life.Add(Card("ST01-005", "north", "life"));
                for (int i = 0; i < 3; i++) S.Trash.Add(Card("ST01-005", "south", "trash"));
                // All 10 ACTIVE. With two of them pre-rested, playing a cost-8 card left nothing to
                // pay a "rest 2 DON!!" cost with, and OP14-049 Jinbe was recorded as never offering
                // when the fixture had simply spent his DON on his own play cost.
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-td-don-{serial++}", Rested = false });
                N.CostArea.Add(new DonInstance { InstanceId = $"north-td-don-{serial++}", Rested = false });
                S.DonDeck = 2; N.DonDeck = 2;
                Hand("south", "ST29-004"); Hand("south", "OP15-020");
                var nr = Character("north", "ST29-009"); if (nr != null) nr.Rested = true;
                Character("north", "OP15-040");
                St.PendingEffects.Clear();
            }

            /// <summary>Did the player end up with a decision to make? A pending effect, an open
            /// choice or a deck-look all count - any of them means the game asked.</summary>
            public bool Offered() =>
                St.PendingEffects.Any(e => e != null && e.Seat == "south")
                || St.ActiveChoice != null || St.DeckLook != null;

            public CardInstance Hand(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "hand");
                p.Hand.Add(c);
                return c;
            }

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return null;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public CardInstance Stage(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "stage");
                p.Stage = c;
                return c;
            }

            public CardInstance LeaderCard(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "leader");
                p.Leader = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-td-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
