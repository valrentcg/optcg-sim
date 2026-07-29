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
    /// TWO questions, the two this workstream keeps finding defects with:
    ///
    ///   1. did pressing the Trigger take cards out of a zone the player could have chosen from,
    ///      without asking?                                     (the `autopick` oracle)
    ///   2. having taken payment, did the payoff actually land?  (the `paidfornothing` oracle)
    ///
    /// The second needs its measurement window stated, because I got it wrong first time: the card
    /// is played DURING useTrigger and the cost pick is queued after it, so board growth has to be
    /// measured from before the Trigger press. Measuring around the payment reports all 14 as "paid
    /// for nothing" — an artefact of the fix's own ordering, not an engine defect.
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
        /// <summary>Use-vs-pass held line, set from the first measured run — see the printout for
        /// how many are driven. A Trigger whose payoff needs a board state this fixture cannot build
        /// is legitimately indistinguishable here.</summary>
        private const int UseVsPassBaseline = 0;

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
            var paidForNothing = new List<string>();

            foreach (var def in cards)
            {
                Board b;
                try { b = new Board(def.Id); }
                catch (Exception) { skipped++; continue; }

                if (!b.DealDamage()) { skipped++; continue; }
                if (!b.AtTriggerStep) { skipped++; continue; }

                driven++;
                int hand0 = b.S.Hand.Count;
                // Captured BEFORE the Trigger is pressed. The play happens during useTrigger (the
                // cost pick is queued after it, deliberately, so the battle never waits on a pick to
                // decide whether the card arrives) — so measuring board growth around the PAYMENT
                // reports every card as "paid for nothing". That was my first version, and its 14
                // findings were an artefact of my own ordering, not an engine defect.
                int boardBefore = b.S.CharacterArea.Count(x => x != null);

                try { b.UseTrigger(); }
                catch (Exception) { skipped++; continue; }
                fired++;

                bool prompted = b.St.PendingEffects.Any(e => e != null && e.Seat == "south")
                             || b.St.ActiveChoice != null || b.St.DeckLook != null;
                if (prompted)
                {
                    asked++;
                    // Second oracle, the one paidfornothing applies to `effect` clauses: ANSWER the
                    // prompt and check the payoff actually lands. A cost that is taken while the
                    // body quietly does nothing is the worst outcome of the three — the player
                    // spends a card and the log still reads as if the card worked.
                    int handBeforePay = b.S.Hand.Count;
                    b.AnswerEverything();
                    int spent = handBeforePay - b.S.Hand.Count;
                    bool boardGrew = b.S.CharacterArea.Count(x => x != null) > boardBefore;
                    bool conditional = def.Trigger.IndexOf("If ", StringComparison.OrdinalIgnoreCase) >= 0;
                    // "Play this card" is the body for 33 of the 42; a conditional body may
                    // legitimately decline, so those are not counted.
                    bool playsThisCard = def.Trigger.IndexOf("play this card", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (spent > 0 && playsThisCard && !conditional && !boardGrew)
                        paidForNothing.Add($"{def.Id}  spent {spent} card(s), board unchanged  :: "
                                           + Trim(def.Trigger, 84));
                    continue;
                }

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
            // Third oracle, the differential: does USING the Trigger differ from PASSING it? The
            // two checks above can both pass while the Trigger is inert — nothing taken, nothing
            // owed. This is the one that says the decision mattered at all.
            int diffDriven = 0, sameAsPass = 0;
            var inertTriggers = new List<string>();
            foreach (var def in cards)
            {
                string useFp, passFp;
                try
                {
                    useFp = FingerprintAfter(def.Id, use: true);
                    passFp = FingerprintAfter(def.Id, use: false);
                }
                catch (Exception) { continue; }
                if (useFp == null || passFp == null) continue;
                diffDriven++;
                if (useFp == passFp) { sameAsPass++; inertTriggers.Add(def.Id + "  :: " + Trim(def.Trigger, 76)); }
            }
            Console.WriteLine($"  use-vs-pass driven: {diffDriven}; indistinguishable: {sameAsPass}");
            foreach (var s in inertTriggers.Take(8)) Console.WriteLine("    " + s);

            Console.WriteLine($"  paid a cost and the payoff never landed: {paidForNothing.Count}");
            foreach (var s in paidForNothing.Take(10)) Console.WriteLine("    " + s);

            SweepRatchet.Reset();
            SweepRatchet.AtMost("[Trigger] costs taken without asking", silentTakes.Count, Baseline);
            SweepRatchet.AtMost("[Trigger] costs paid for nothing", paidForNothing.Count, Baseline);
            SweepRatchet.AtMost("[Trigger]s where using and passing are indistinguishable",
                                sameAsPass, UseVsPassBaseline);
            return SweepRatchet.Result();
        }

        /// <summary>Damage the same card into Life, then either take the Trigger or pass it, and
        /// flatten what the player would see.</summary>
        private static string FingerprintAfter(string cardId, bool use)
        {
            var b = new Board(cardId);
            if (!b.DealDamage() || !b.AtTriggerStep) return null;
            b.Apply(new GameCommand { Type = use ? "useTrigger" : "passTrigger", Seat = "south" });
            for (int i = 0; i < 6; i++)
            {
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null) break;
                string t = b.S.Hand.Concat(b.S.CharacterArea.Where(x => x != null))
                    .FirstOrDefault(x => GameEngine.IsValidEffectTarget(b.St, pe, x))?.InstanceId;
                int before = b.St.EventLog.Count;
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = t });
                if (b.St.EventLog.Count == before) break;
            }
            var sb = new System.Text.StringBuilder();
            foreach (var seat in new[] { "south", "north" })
            {
                var p = b.St.Players[seat];
                sb.Append(string.Join(",", p.Hand.Select(x => x.CardId))).Append('|')
                  .Append(string.Join(",", p.Life.Select(x => x.CardId + (x.FaceUp ? "U" : "d")))).Append('|')
                  .Append(string.Join(",", p.Trash.Select(x => x.CardId))).Append('|')
                  .Append(p.Deck.Count).Append('|');
                foreach (var c in p.CharacterArea)
                    sb.Append(c == null ? "-" : c.CardId + ":" + (c.Rested ? "R" : "A")
                              + ":" + GameEngine.GetPower(b.St, c)).Append(';');
                sb.Append("||");
            }
            return sb.ToString();
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

            /// <summary>Answer whatever the Trigger raised, the way the UI would: let the ENGINE say
            /// which cards it accepts rather than guessing a zone.</summary>
            public void AnswerEverything()
            {
                for (int i = 0; i < 8; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = S.Hand.Concat(S.CharacterArea.Where(x => x != null))
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int peBefore = St.PendingEffects.Count, logBefore = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.PendingEffects.Count == peBefore && St.EventLog.Count == logBefore) break;
                }
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-tf-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
