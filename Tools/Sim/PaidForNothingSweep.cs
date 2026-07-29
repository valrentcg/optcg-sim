using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The other half of the brief - "IF they do use it, the effects should all resolve properly" - in
    /// the one form that can be asserted rather than merely reported.
    ///
    /// "Did it resolve?" is not gateable on its own, because an effect that does nothing is often
    /// correct: the cost may be unpayable, the body may have no legal target, rule 1-3-2 retires what
    /// cannot happen. costresolvesweep therefore prints leads and exits 0.
    ///
    /// But there is a strict subset with no innocent explanation: the engine TOOK THE PAYMENT and then
    /// produced nothing. The player rested a Character, turned a Life card face-up, or trashed a card
    /// from hand, and got no effect and no prompt in return. No board makes that correct, so it can be
    /// a gate.
    ///
    /// Payment is identified from the engine's own logs, which mark it two ways: a "(cost)" suffix on
    /// the auto-paid components, and a "cost: ..." prefix from the click-driven pick path.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- paidfornothing
    /// </summary>
    public static class PaidForNothingSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== Did anyone PAY a cost and get nothing back? ===");

            int tested = 0, paidAndActed = 0, paidAndWaiting = 0, neverPaid = 0, threw = 0, conditional = 0;
            var paidForNothing = new List<(string Id, string Name, string Clause, string Paid)>();

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var clause = Regex.Replace(raw, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    var m0 = Regex.Match(clause, @"^You may [^:]+:\s*(?<body>.+)$", RegexOptions.IgnoreCase);
                    if (!m0.Success) continue;
                    // A CONDITIONAL body has an innocent explanation for producing nothing: you are
                    // allowed to activate and pay even when the condition is false, and simply get
                    // nothing - legal, merely bad play. My first pass asserted over these and
                    // reported 52 "defects", every one an unmet "If your Leader is [X]". Only an
                    // UNCONDITIONAL body makes paying-for-nothing indefensible.
                    if (Regex.IsMatch(m0.Groups["body"].Value, @"^\s*If\b", RegexOptions.IgnoreCase))
                    { conditional++; continue; }
                    tested++;

                    try
                    {
                        var b = new Board();
                        var src = b.Character("south", def.Id);
                        if (src == null) continue;
                        int logBefore = b.St.EventLog.Count;

                        GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                        var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (pe == null) continue;
                        b.Apply(new GameCommand
                        { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

                        var lines = b.St.EventLog.Skip(logBefore).Select(l => l.Message ?? "").ToList();
                        var paid = lines.Where(IsPayment).ToList();
                        if (paid.Count == 0) { neverPaid++; continue; }

                        // Still answerable => the body is waiting for the player, which is fine.
                        if (b.St.PendingEffects.Any(e => e != null && e.Seat == "south")
                            || b.St.ActiveChoice != null || b.St.DeckLook != null)
                        { paidAndWaiting++; continue; }

                        bool acted = lines.Any(m => !IsPayment(m) && !IsNoise(m));
                        if (acted) paidAndActed++;
                        else paidForNothing.Add((def.Id, def.Name ?? "", clause, paid[0]));
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  unconditional-body clauses tested : {tested}");
            Console.WriteLine($"  skipped, conditional body         : {conditional}");
            Console.WriteLine($"  never paid (declined/unpayable)   : {neverPaid}");
            Console.WriteLine($"  paid, then acted                  : {paidAndActed}");
            Console.WriteLine($"  paid, then waiting for a pick     : {paidAndWaiting}");
            Console.WriteLine($"  PAID AND GOT NOTHING              : {paidForNothing.Count}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            if (paidForNothing.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  paid for nothing:");
                foreach (var p in paidForNothing.Take(25))
                {
                    Console.WriteLine($"    {p.Id,-10} {Trim(p.Name, 16),-16} {Trim(p.Clause, 88)}");
                    Console.WriteLine($"               paid: {Trim(p.Paid, 92)}");
                }
                if (paidForNothing.Count > 25) Console.WriteLine($"    ... and {paidForNothing.Count - 25} more");
            }

            return paidForNothing.Count == 0 ? 0 : 1;
        }

        /// <summary>The engine marks cost payment two ways: "(cost)" on auto-paid components, and a
        /// "cost:" prefix from the click-driven pick path.</summary>
        private static bool IsPayment(string m) => SweepText.IsPaymentLog(m);

        /// <summary>Bookkeeping and refusals - neither payment nor outcome.</summary>
        private static bool IsNoise(string m) => SweepText.IsBookkeepingLog(m);

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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "paid-for-nothing" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear(); S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                for (int i = 0; i < 5; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
                for (int i = 0; i < 3; i++) S.Trash.Add(Card("ST01-005", "south", "trash"));
                Hand("south", "ST29-004"); Hand("south", "OP15-020"); Hand("south", "ST29-009");
                var nr = Character("north", "ST29-009"); if (nr != null) nr.Rested = true;
                Character("north", "OP15-040");
                Character("south", "EB03-002");
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-pn-don-{serial++}", Rested = false });
                N.CostArea.Add(new DonInstance { InstanceId = $"north-pn-don-{serial++}", Rested = false });
                S.DonDeck = 2; N.DonDeck = 2;
                St.PendingEffects.Clear();
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return null;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-pn-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
