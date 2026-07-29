using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The `autopick` oracle, applied to the population autopick cannot reach.
    ///
    /// Every pool-wide sweep here enumerates `def.Effect`. [Trigger] text lives in a SEPARATE data
    /// field, so 42 "you may" clauses were never driven by any of them — which is how the Hand[0]
    /// auto-pick in the trigger path survived this whole workstream.
    ///
    /// The obvious shortcut — feed `def.Trigger` into the existing sweeps — is wrong, and the
    /// existing sweep says why in its own comment: it queues clauses as main-timing, and forcing a
    /// reactive ability through that path "invents a question that never existed". A [Trigger] fires
    /// in exactly one situation: a Life card dealt as damage. So each card here is put on top of a
    /// real Life stack, actually damaged, and its Trigger actually pressed.
    ///
    /// The question asked is the one that caught the other five auto-pick defects:
    ///
    ///     did pressing the Trigger take cards out of a zone the player could have chosen from,
    ///     without asking?
    ///
    /// Ratcheted rather than gated on zero, like autopick: a trigger whose cost is DON!! or the top
    /// Life card is positional and legitimately silent.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- triggerfield
    /// </summary>
    public static class TriggerFieldSweep
    {
        /// <summary>Held line, not a target. Lower it when a real one is fixed; never raise it.</summary>
        private const int Baseline = 0;

        public static int Run()
        {
            Console.WriteLine("=== [Trigger] \"you may\" clauses, driven through real Life damage ===");

            var cards = CardData.Library.Values
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Trigger))
                .Where(d => d.Trigger.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(d => d.Id).Select(g => g.First())
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ToList();

            int driven = 0, fired = 0, asked = 0, skipped = 0;
            var silentTakes = new List<string>();

            foreach (var def in cards)
            {
                Board b;
                try { b = new Board(def.Id); }
                catch (Exception) { skipped++; continue; }

                if (!b.DealDamage()) { skipped++; continue; }
                if (!b.AtTriggerStep) { skipped++; continue; }

                driven++;
                int hand0 = b.S.Hand.Count;

                try { b.UseTrigger(); }
                catch (Exception) { skipped++; continue; }
                fired++;

                bool prompted = b.St.PendingEffects.Any(e => e != null && e.Seat == "south")
                             || b.St.ActiveChoice != null || b.St.DeckLook != null;
                if (prompted) { asked++; continue; }

                // Nothing to answer. That is only a defect if the engine helped itself to cards the
                // player held — the Life card itself moving is the Trigger working, not a choice.
                int taken = hand0 - b.S.Hand.Count;
                if (taken > 0 && hand0 > taken)
                    silentTakes.Add($"{def.Id}  took {taken} of {hand0} hand card(s)  :: "
                                    + Trim(def.Trigger, 88));
            }

            Console.WriteLine($"  {cards.Count} cards carry a \"you may\" [Trigger]; drove {driven}, fired {fired}, "
                              + $"{asked} raised a decision, {skipped} unreachable in this fixture");
            Console.WriteLine($"  took hand cards WITHOUT asking: {silentTakes.Count}");
            foreach (var s in silentTakes.Take(10)) Console.WriteLine("    " + s);

            SweepRatchet.Reset();
            SweepRatchet.AtMost("[Trigger] costs taken without asking", silentTakes.Count, Baseline);
            return SweepRatchet.Result();
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private CardInstance attacker;
            private int serial;

            public Board(string trigCardId)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "trigger-field" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "north"; St.TurnNumber = 9;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-tf-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                }
                S.Life.Add(Make("ST01-005", "south", "life"));
                S.Life.Add(Make("ST01-005", "south", "life"));
                S.Life.Add(Make(trigCardId, "south", "life"));      // top of Life = dealt first
                for (int i = 0; i < 4; i++) N.Life.Add(Make("ST01-005", "north", "life"));
                // Several distinguishable cards in hand, so "took one without asking" is meaningful
                // and the fixture never forces the engine's hand by holding exactly one card.
                foreach (var id in new[] { "EB01-004", "EB01-005", "EB01-006", "EB01-007" })
                    S.Hand.Add(Make(id, "south", "hand"));

                attacker = Make("EB03-002", "north", "character");   // 6000 beats a 5000 Leader
                N.CharacterArea[0] = attacker;
                St.PendingEffects.Clear();
            }

            public bool AtTriggerStep => St.Battle?.Step == "trigger";

            public bool DealDamage()
            {
                attacker.Rested = false; attacker.PlayedOnTurn = 0;
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = attacker.InstanceId, Target = S.Leader?.InstanceId });
                // The DEFENDER owns every step after the declaration.
                if (St.Battle?.Step == "block") Apply(new GameCommand { Type = "passBlock", Seat = "south" });
                if (St.Battle?.Step == "counter") Apply(new GameCommand { Type = "passCounter", Seat = "south" });
                if (St.Battle?.Step == "damage") Apply(new GameCommand { Type = "resolveAttack", Seat = "south" });
                return St.Battle != null;
            }

            public void UseTrigger() => Apply(new GameCommand { Type = "useTrigger", Seat = "south" });

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-tf-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
