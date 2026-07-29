using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The other half of costprefixsweep. That one counted which cards were denied a Use button;
    /// this one presses Use and checks something actually HAPPENS.
    ///
    /// The failure being hunted is silent: the player answers "yes, use it", the cost is not paid,
    /// no body runs, nothing is logged, and the effect simply disappears. That looks identical to a
    /// working card from the outside and is invisible to win-rate, which is why it needs a sweep
    /// rather than spot checks.
    ///
    /// An outcome is only counted as OK when the engine either did something observable or is
    /// legitimately WAITING for a pick. "Waiting" is not failure - "up to 1 card from your hand"
    /// must stop and ask - and conflating the two is how a fixture fault gets reported as a bug.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- costresolvesweep
    /// </summary>
    public static class CostPrefixResolveSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== \"You may <cost>:\" — does Use actually resolve? ===");

            int tested = 0, acted = 0, waiting = 0, silent = 0, threw = 0, unpayable = 0;
            var silentCards = new List<(string Id, string Name, string Clause)>();
            // A cost that names a COUNT of cards in a zone holding several of them is a choice the
            // player owns. If such a clause resolves straight through, the engine picked for them.
            var autoPicked = new List<(string Id, string Name, string Clause)>();
            // Declining has to mean something. A "you may" whose Skip still pays the cost or still
            // runs the body is a prompt in name only, and that is invisible from the Use side.
            int skipClean = 0;
            var skipDirty = new List<(string Id, string Name, string Clause)>();

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var clause = Regex.Replace(raw, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    if (!Regex.IsMatch(clause, @"^You may [^:]+:", RegexOptions.IgnoreCase)) continue;
                    tested++;

                    try
                    {
                        var b = new Board();
                        b.Populate();
                        var src = b.Character("south", def.Id);
                        int logBefore = b.St.EventLog.Count;

                        GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                        var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (pe == null) { silent++; silentCards.Add((def.Id, def.Name ?? "", clause)); continue; }

                        // press Use
                        b.Apply(new GameCommand
                        { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

                        bool stillWaiting = b.St.PendingEffects.Any(e => e != null && e.Seat == "south")
                                         || b.St.ActiveChoice != null || b.St.DeckLook != null;
                        // "acknowledged for manual resolution" is the engine giving up, not a payoff.
                        var did = b.St.EventLog.Skip(logBefore).Select(l => l.Message)
                                   .Where(m => m.IndexOf("is pending", StringComparison.OrdinalIgnoreCase) < 0
                                            && m.IndexOf("acknowledged for manual resolution", StringComparison.OrdinalIgnoreCase) < 0
                                            // "cost cannot be paid" is the engine DECLINING, not
                                            // acting. Counting it as an action put 21 reveal-from-
                                            // hand cards on the auto-picked list having done nothing.
                                            && m.IndexOf("cost cannot be paid", StringComparison.OrdinalIgnoreCase) < 0)
                                   .ToList();

                        if (stillWaiting) waiting++;
                        else if (did.Count > 0)
                        {
                            acted++;
                            var costM = Regex.Match(clause, @"^You may ([^:]+):", RegexOptions.IgnoreCase);
                            string cost = costM.Success ? costM.Groups[1].Value.ToLowerInvariant() : "";
                            // A cost is only a CHOICE if the player picks WHICH cards. "trash 1 card
                            // from the top of your Life cards" names one exact card - resolving it
                            // outright is correct, and counting it made this number look far worse
                            // than it is. Costs that name an end of a stack are excluded unless the
                            // wording offers both ("top or bottom").
                            bool positional = (cost.Contains("the top of") || cost.Contains("the bottom of"))
                                && !cost.Contains("top or bottom");
                            bool choiceShaped = !positional
                                && Regex.IsMatch(cost, @"\b(trash|return|reveal|place|add)\b.*\b(\d+)\b")
                                && (cost.Contains("from your hand") || cost.Contains("from your trash")
                                 || cost.Contains("of your life") || cost.Contains("from your deck"));
if (choiceShaped) autoPicked.Add((def.Id, def.Name ?? "", clause));
                        }
                        else if (b.St.EventLog.Skip(logBefore).Any(l =>
                                 (l.Message ?? "").IndexOf("cost cannot be paid", StringComparison.OrdinalIgnoreCase) >= 0))
                            unpayable++;   // the engine said so out loud; the fixture lacked the cards
                        else { silent++; silentCards.Add((def.Id, def.Name ?? "", clause)); }

                        // ---- second pass: the same clause, answered with Skip ----
                        var sb = new Board();
                        sb.Populate();
                        var ssrc = sb.Character("south", def.Id);
                        string before = sb.Fingerprint();
                        GameEngine.QueueClauseForTest(sb.St, "south", ssrc, "main", clause);
                        var spe = sb.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (spe != null)
                        {
                            sb.Apply(new GameCommand
                            { Type = "passEffect", Seat = "south", EffectId = spe.EffectId });
                            if (sb.Fingerprint() == before) skipClean++;
                            else skipDirty.Add((def.Id, def.Name ?? "", clause));
                        }
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  cost-prefix clauses tested        : {tested}");
            Console.WriteLine($"  Use produced an observable effect : {acted}");
            Console.WriteLine($"  Use correctly waits for a pick    : {waiting}");
            Console.WriteLine($"  Cost unpayable on this board      : {unpayable}");
            Console.WriteLine($"  Use did NOTHING (silent failure)  : {silent}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            Console.WriteLine($"  ...of those, resolved a CHOICE without asking : {autoPicked.Count}");
            Console.WriteLine($"  Skip left the board untouched     : {skipClean}");
            Console.WriteLine($"  Skip changed something anyway     : {skipDirty.Count}");
            if (skipDirty.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Skip was not honoured:");
                foreach (var s in skipDirty.Take(25))
                    Console.WriteLine($"    {s.Id,-10} {Trim(s.Name, 18),-18} {Trim(s.Clause, 110)}");
                if (skipDirty.Count > 25) Console.WriteLine($"    ... and {skipDirty.Count - 25} more");
            }
            if (autoPicked.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  auto-picked (player should have chosen):");
                foreach (var a in autoPicked.Take(25))
                    Console.WriteLine($"    {a.Id,-10} {Trim(a.Name, 18),-18} {Trim(a.Clause, 110)}");
                if (autoPicked.Count > 25) Console.WriteLine($"    ... and {autoPicked.Count - 25} more");
            }

            if (silentCards.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  silent (all):");
                foreach (var s in silentCards)
                    Console.WriteLine($"    {s.Id,-10} {Trim(s.Name, 18),-18} {Trim(s.Clause, 130)}");
                if (silentCards.Count > 30)
                    Console.WriteLine($"    ... and {silentCards.Count - 30} more");
            }

            // Reporting tool: the board fixture cannot satisfy every card's preconditions, so a
            // silent result is a lead to investigate rather than a proven defect. Always exits 0.
            return 0;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "cost-resolve" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear(); S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                St.PendingEffects.Clear();
            }

            /// <summary>Something of everything, so a clause is not reported silent merely because
            /// the fixture gave it nothing to act on.</summary>
            public void Populate()
            {
                // Costs and payoffs in this pool routinely demand RESTED bodies on either side, or
                // more than one of your own Characters. A board of one active Character each way
                // makes dozens of cards look silently broken when they are simply unpayable.
                var nRested = Character("north", "ST29-009"); if (nRested != null) nRested.Rested = true;
                Character("north", "OP15-040");
                var nDon = new DonInstance { InstanceId = $"north-cr-don-{serial++}", Rested = false };
                N.CostArea.Add(nDon);
                N.CostArea.Add(new DonInstance { InstanceId = $"north-cr-don-{serial++}", Rested = true });

                var own = Character("south", "ST29-009"); if (own != null) own.Rested = true;
                Character("south", "OP15-040");

                Hand("south", "ST29-004"); Hand("south", "OP15-020"); Hand("south", "ST29-009");
                for (int i = 0; i < 4; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
                for (int i = 0; i < 3; i++) S.Trash.Add(Card("ST01-005", "south", "trash"));
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance
                    { InstanceId = $"south-cr-don-{serial++}", Rested = i >= 8 });   // 8 active, 2 rested
                S.DonDeck = 2;
            }

            /// <summary>Every zone that a cost or a body could plausibly move a card between, plus
            /// rest state and Life face-up flags. Compared as a whole so the check does not have to
            /// guess which zone a given clause would have touched.</summary>
            public string Fingerprint()
            {
                string Z(PlayerState p) =>
                    $"h{p.Hand.Count}/d{p.Deck.Count}/t{p.Trash.Count}/l{p.Life.Count}" +
                    $"/f{p.Life.Count(x => x.FaceUp)}/c{p.CharacterArea.Count(x => x != null)}" +
                    $"/r{p.CharacterArea.Count(x => x != null && x.Rested)}" +
                    $"/don{p.CostArea.Count}/dr{p.CostArea.Count(x => x.Rested)}";
                return Z(S) + "|" + Z(N);
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

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); (seat == "south" ? S : N).Hand.Add(c); return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-cr-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
