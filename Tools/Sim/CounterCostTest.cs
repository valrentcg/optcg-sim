using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "[Counter] You may trash 1 card from your hand: Up to 1 of your Leader or Character cards gains
    /// +3000 power during this battle." - 13 counter Events share this shape (OP02-068 Gum-Gum Rain,
    /// OP03-072, OP03-097, OP04-016, OP05-037, OP06-115, OP13-076, OP15-096, OP16-020, EB04-029,
    /// OP14-036, OP07-056, P-059).
    ///
    /// AutomatedCounterPower grepped the "+N" straight out of the clause and applied it as a flat
    /// boost, ignoring the cost entirely, and the clause was then blocked from queueing BECAUSE it
    /// contains "gains +N". So the boost arrived for free, nothing was trashed, and the player was
    /// never asked - a strictly-better-than-printed counter.
    ///
    /// Both halves are asserted, because fixing only the first would trade a free boost for an
    /// unreachable one:
    ///   - declining must leave the power alone AND the hand alone
    ///   - paying must trash a card of the PLAYER'S choosing and then grant the boost
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- countercost
    /// </summary>
    public static class CounterCostTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== [Counter] \"You may <cost>: ... gains +N power\" ===");
            NoFreePowerWithoutPayingTheCost();
            DecliningLeavesHandAndPowerAlone();
            PayingGrantsTheBoost();
            PlainCounterStillAppliesItsFlatBoost();
            EveryCostPrefixedCounterIsAnEvent();
            Console.WriteLine($"countercost: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Every place a counter boost can land. The flat path accumulates into
        /// Battle.CounterPower, "during this battle" grants go to Battle.BattlePowerBonus, and
        /// "during this turn" grants to TemporaryPowerBonus. Reading only one of the three is how the
        /// first version of this test reported 0 for a counter that had plainly applied +3000 - it
        /// passed while measuring nothing.</summary>
        private static int TotalBoost(Board b)
        {
            int total = b.St.Battle?.CounterPower ?? 0;
            var id = b.S.Leader?.InstanceId;
            if (id != null)
            {
                if (b.St.Battle?.BattlePowerBonus != null
                    && b.St.Battle.BattlePowerBonus.TryGetValue(id, out int bb)) total += bb;
                if (b.St.TemporaryPowerBonus.TryGetValue(id, out int tb)) total += tb;
            }
            return total;
        }

        private static void NoFreePowerWithoutPayingTheCost()
        {
            var b = new Board();
            var gum = b.Hand("OP02-068");                 // "You may trash 1 card from your hand: ... +3000"
            b.Hand("ST29-004"); b.Hand("ST29-009");       // two other cards, so the discard is a real choice
            b.OpponentAttacks();
            int hand0 = b.S.Hand.Count;

            b.Apply(new GameCommand
            { Type = "counterWithCard", Seat = "south", InstanceId = gum.InstanceId, Target = b.S.Leader?.InstanceId });

            // The counter card itself leaves hand; nothing MORE may be trashed until the cost is paid.
            bool noExtraDiscard = b.S.Hand.Count == hand0 - 1;
            Check("the +3000 is not granted for free at counter time",
                  TotalBoost(b) == 0 && noExtraDiscard,
                  $"boost={TotalBoost(b)} (want 0) hand {hand0}->{b.S.Hand.Count} (want -1, the counter card only)");
        }

        private static void DecliningLeavesHandAndPowerAlone()
        {
            var b = new Board();
            var gum = b.Hand("OP02-068");
            b.Hand("ST29-004"); b.Hand("ST29-009");
            b.OpponentAttacks();
            b.Apply(new GameCommand
            { Type = "counterWithCard", Seat = "south", InstanceId = gum.InstanceId, Target = b.S.Leader?.InstanceId });
            int handAfterCounter = b.S.Hand.Count;

            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("declining leaves power and hand alone", false, "no decision was offered to decline"); return; }
            b.Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });

            Check("Skip: no boost, no discard",
                  TotalBoost(b) == 0 && b.S.Hand.Count == handAfterCounter,
                  $"boost={TotalBoost(b)} hand {handAfterCounter}->{b.S.Hand.Count}");
        }

        private static void PayingGrantsTheBoost()
        {
            var b = new Board();
            var gum = b.Hand("OP02-068");
            var pitch = b.Hand("ST29-004");               // the card we choose to discard
            b.Hand("ST29-009");
            b.OpponentAttacks();
            b.Apply(new GameCommand
            { Type = "counterWithCard", Seat = "south", InstanceId = gum.InstanceId, Target = b.S.Leader?.InstanceId });

            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("paying grants the boost", false, "nothing was offered to pay"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            // Answer whatever it asks for - the discard and/or the card to buff.
            for (int i = 0; i < 4 && b.St.PendingEffects.Any(e => e != null && e.Seat == "south"); i++)
            {
                var nxt = b.St.PendingEffects.First(e => e != null && e.Seat == "south");
                string target = b.S.Hand.Any(h => h.InstanceId == pitch.InstanceId)
                    ? pitch.InstanceId : b.S.Leader?.InstanceId;
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = nxt.EffectId, Target = target });
            }

            Check("Use: the boost lands and a card was actually discarded for it",
                  TotalBoost(b) >= 3000 && b.S.Trash.Any(c => c.InstanceId == pitch.InstanceId),
                  $"boost={TotalBoost(b)} (want >=3000) pitched={b.S.Trash.Any(c => c.InstanceId == pitch.InstanceId)}");
        }

        private static void PlainCounterStillAppliesItsFlatBoost()
        {
            // Negative control for the fix: a [Counter] with NO cost prefix must keep working exactly
            // as before, applied flat at counter time. Without this, "return 0 for cost-prefixed" could
            // silently widen and break every ordinary counter in the game.
            var b = new Board();
            var plain = b.Hand("EB01-019");               // "[Counter] Up to 1 of your Leader or Character
                                              // cards gains +4000 power during this battle." - a
                                              // plain [Counter] tag, no cost prefix, counter 0.
            b.OpponentAttacks();
            b.Apply(new GameCommand
            { Type = "counterWithCard", Seat = "south", InstanceId = plain.InstanceId, Target = b.S.Leader?.InstanceId });
            Console.WriteLine($"      [diag] battle={(b.St.Battle == null ? "null" : b.St.Battle.Step)} counterPower={b.St.Battle?.CounterPower} pending={b.St.PendingEffects.Count} hand={b.S.Hand.Count}");
            foreach (var l in b.St.EventLog.TakeLast(5)) Console.WriteLine("      [log] " + l.Message);
            Check("a plain [Counter] with no cost still applies its boost immediately",
                  TotalBoost(b) > 0,
                  $"boost={TotalBoost(b)} (want > 0)");
        }

        /// <summary>A latent trap left by the counter-cost split, found by auditing its call sites
        /// rather than by any failure.
        ///
        /// Making AutomatedCounterPower return 0 for a cost-prefixed clause was right — the boost
        /// must be bought, not free. But GameEngine gates counter LEGALITY on
        /// "AutomatedCounterPower(c) &lt;= 0 &amp;&amp; !counterEvent -&gt; not playable". Every card carrying
        /// this shape today is an EVENT, so counterEvent carries them and nothing breaks. The day a
        /// CHARACTER prints "[Counter] You may &lt;cost&gt;: ... gains +N", it becomes unplayable
        /// outright — not weakened, unusable — and nothing else in this suite would notice.
        ///
        /// So this asserts the property the gate silently depends on. It is a data-shape check on
        /// purpose: it goes red the moment a set makes the assumption false, which is precisely when
        /// somebody needs to look at that gate.</summary>
        private static void EveryCostPrefixedCounterIsAnEvent()
        {
            var offenders = new System.Collections.Generic.List<string>();
            int matched = 0;
            foreach (var def in CardData.Library.Values)
            {
                if (def == null || string.IsNullOrEmpty(def.Effect)) continue;
                foreach (var line in def.Effect.Split((char)10))
                {
                    var s = line.Trim();
                    if (!s.StartsWith("[Counter]")) continue;
                    if (!System.Text.RegularExpressions.Regex.IsMatch(s, "you may .{3,80}:",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    int gp = s.IndexOf("gains +", StringComparison.OrdinalIgnoreCase);
                    if (gp < 0 || gp + 7 >= s.Length || !char.IsDigit(s[gp + 7])) continue;
                    matched++;
                    if (!string.Equals(def.Type, "event", StringComparison.OrdinalIgnoreCase))
                        offenders.Add($"{def.Id} [{def.Type}]");
                }
            }
            // A shape-detector that silently matches NOTHING passes this check perfectly, which is
            // how a guard becomes decoration. 19 cards carry the shape today; require the detector
            // to still be finding them before believing the zero.
            Check("the cost-prefixed [Counter] detector still finds the known cards",
                  matched >= 15,
                  $"matched only {matched} — the detector has gone blind (card text or data shape "
                  + "changed), so the Event assertion below proves nothing");
            Check("every cost-prefixed [Counter] +N card is an Event (the legality gate assumes it)",
                  offenders.Count == 0,
                  offenders.Count == 0 ? "" :
                  $"{offenders.Count} non-Event card(s) carry this shape and are now UNPLAYABLE as "
                  + $"counters: {string.Join(", ", offenders.Take(6))} — see the "
                  + "AutomatedCounterPower<=0 && !counterEvent gate");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "counter-cost" });
                St.Status = "active"; St.Phase = "main"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                for (int i = 0; i < 4; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
                // Counter Events have a play cost of their own. With an empty cost area the engine
                // refuses them outright ("Not enough active DON!! to counter with ..."), which reads
                // as "the counter did nothing" and is a fixture gap, not an engine fault.
                S.CostArea.Clear(); N.CostArea.Clear();
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-cc-don-{serial++}", Rested = false });
                S.DonDeck = 0; N.DonDeck = 0;
                St.PendingEffects.Clear();
            }

            public CardInstance Hand(string id)
            { var c = Card(id, "south", "hand"); S.Hand.Add(c); return c; }

            /// <summary>North declares an attack on our Leader and the battle reaches the counter step.</summary>
            public void OpponentAttacks()
            {
                var atk = Card("OP15-040", "north", "character");
                N.CharacterArea[0] = atk;
                atk.Rested = false; atk.PlayedOnTurn = 0;
                St.ActiveSeat = "north";
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = atk.InstanceId, Target = S.Leader?.InstanceId });
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-cc-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
