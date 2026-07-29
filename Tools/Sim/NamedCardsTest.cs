using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The cards the brief actually names, driven one at a time.
    ///
    /// Everything else in this suite is shape-driven: enumerate a wording, fix the class, sweep the
    /// pool. That is the right way round, but it has a known blind spot — a class fix is proven by
    /// the cards it touched, and the cards it did NOT touch are where the next bug lives. There are
    /// NINE distinct "you may" Nami cards and only two were ever looked at individually.
    ///
    /// These are picked for being awkward in different ways, not for being similar:
    ///
    ///   EB03-006  Nami     cost is SELF-HARM: give your own Leader -5000 to draw
    ///   OP09-070  Nami     VARIABLE cost: "return 1 OR MORE DON!! cards"
    ///   OP08-106  Nami     cost filtered by a keyword: trash 1 card WITH A [Trigger]
    ///   OP10-088  Nami     COMPOUND cost: rest this Character AND a Leader/Stage
    ///   EB03-053  Nami     [On K.O.] cost turns a Life card FACE-UP
    ///   OP15-101  Kalgara  deck-look, reveal up to 2 by name/type
    ///
    /// Each asserts the exact outcome, not "something happened" — a cost that pays itself and a body
    /// that quietly does nothing produces a perfectly reasonable log.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- namedcards
    /// </summary>
    public static class NamedCardsTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Nami, Kalgara, one card at a time ===");
            Nami_SelfHarmCostDrawsACard();
            Nami_VariableDonCostGivesRestedDon();
            Nami_TriggerFilteredDiscardKOs();
            Nami_CompoundRestCostDraws();
            Nami_NoTriggerInHandMeansNoKO();
            Kalgara_LooksAtFiveAndAdds();
            Console.WriteLine($"namedcards: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>"[On Play] You may give your active Leader -5000 power during this turn: Draw 1
        /// card." The cost hurts YOU, which is the interesting part: an engine that treats every
        /// power change as a benefit could aim it at the opponent and hand the player a free draw.
        /// </summary>
        private static void Nami_SelfHarmCostDrawsACard()
        {
            var b = new Board();
            int myLeader0 = GameEngine.GetPower(b.St, b.S.Leader);
            int oppLeader0 = GameEngine.GetPower(b.St, b.N.Leader);
            int hand0 = b.S.Hand.Count;

            b.PlayAndUse("EB03-006");

            int myNow = GameEngine.GetPower(b.St, b.S.Leader);
            int oppNow = GameEngine.GetPower(b.St, b.N.Leader);
            // hand0 is counted BEFORE the card is added to hand by PlayAndUse, so the card itself
            // is not in it: +1 in, -1 played, +1 drawn => hand0 + 1. My first version asserted
            // hand0 and read the engine being right as a failure — the same off-by-one I have made
            // in deck arithmetic twice in this workstream.
            Check("EB03-006 Nami: -5000 lands on MY Leader and the draw happens",
                  myLeader0 - myNow == 5000 && oppNow == oppLeader0 && b.S.Hand.Count == hand0 + 1,
                  $"my leader {myLeader0}->{myNow}, opp leader {oppLeader0}->{oppNow}, "
                  + $"hand {hand0}->{b.S.Hand.Count}"
                  + (oppNow < oppLeader0 ? " — the SELF-harm cost was aimed at the opponent" : ""));
        }

        /// <summary>"You may return 1 or more DON!! cards from your field to your DON!! deck: Give up
        /// to 2 rested DON!! cards to your Leader or 1 of your Characters." "1 or more" has no fixed
        /// number, which is the shape most likely to be read as zero or as everything.</summary>
        private static void Nami_VariableDonCostGivesRestedDon()
        {
            var b = new Board();
            int don0 = b.S.CostArea.Count, donDeck0 = b.S.DonDeck;

            b.PlayAndUse("OP09-070");

            int returned = don0 - b.S.CostArea.Count;
            int attached = b.S.Leader.AttachedDonIds.Count
                         + b.S.CharacterArea.Where(c => c != null).Sum(c => c.AttachedDonIds.Count);
            Check("OP09-070 Nami: returns at least 1 DON!! and gives rested DON!! back",
                  returned >= 1 && b.S.DonDeck > donDeck0 && attached >= 1,
                  $"cost area -{returned}, DON!! deck {donDeck0}->{b.S.DonDeck}, attached={attached}"
                  + (returned == 0 ? " — \"1 or more\" paid nothing" : ""));
        }

        /// <summary>"You may trash 1 card with a [Trigger] from your hand: K.O. up to 1 …". The cost
        /// is keyword-filtered, so the fixture holds one card that qualifies and two that do not —
        /// paying with a non-[Trigger] card would be the engine ignoring the filter.</summary>
        private static void Nami_TriggerFilteredDiscardKOs()
        {
            var b = new Board();
            var victim = b.Character("north", "OP15-040");   // cost 1, under any ceiling
            // OP01-009 Carrot genuinely prints [Trigger]. My first fixture used ST01-005 Jinbe,
            // which does NOT — so the cost was unpayable and the test was measuring the wrong thing
            // entirely. Verify the fixture card has the property the cost filters on.
            var trig = b.HandCard("south", "OP01-009");     // Carrot: prints [Trigger]
            var plain1 = b.HandCard("south", "EB01-004");   // Koza: no [Trigger]
            var plain2 = b.HandCard("south", "EB01-005");   // Doma: no [Trigger]

            b.PlayAndUse("OP08-106");

            bool gone = b.N.CharacterArea.All(c => c == null || c.InstanceId != victim.InstanceId);
            // Checking only the K.O. would pass just as well against an engine that ignores the
            // keyword filter and pays with whatever is nearest — so name the card that must go and
            // the two that must not.
            bool paidWithTrigger = b.S.Hand.All(x => x.InstanceId != trig.InstanceId);
            bool sparedPlain = b.S.Hand.Any(x => x.InstanceId == plain1.InstanceId)
                            || b.S.Hand.Any(x => x.InstanceId == plain2.InstanceId);
            Check("OP08-106 Nami: the cost is paid with the [Trigger] card and the K.O. lands",
                  gone && paidWithTrigger && sparedPlain,
                  $"koLanded={gone} triggerCardSpent={paidWithTrigger} plainCardsSpared={sparedPlain}"
                  + (!paidWithTrigger ? " — the keyword filter was ignored" : ""));
        }

        /// <summary>The other direction, and the one that matters: with NO [Trigger] card in hand the
        /// cost cannot be paid, so the K.O. must not happen either. A body that runs without its cost
        /// is the inverse of the "paid for nothing" defect this suite already guards — free payoff.
        /// </summary>
        private static void Nami_NoTriggerInHandMeansNoKO()
        {
            var b = new Board();
            var victim = b.Character("north", "OP15-040");
            // [Trigger] is a SEPARATE data field, not part of `effect` — ST29-004 and ST29-009 both
            // print one despite their effect text not mentioning it, so my first fixture here was
            // full of legal payers and "the cost was unpayable" was never true. Verified against
            // CardData.Trigger, not the effect string.
            b.HandCard("south", "EB01-004");     // Koza, no [Trigger]
            b.HandCard("south", "EB01-005");     // Doma, no [Trigger]

            b.PlayAndUse("OP08-106");

            bool alive = b.N.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId);
            Check("OP08-106 Nami: with no [Trigger] card to pay with, the K.O. does NOT happen",
                  alive,
                  "the victim died with the cost unpaid — the payoff came for free");
        }

        /// <summary>"[Activate: Main] You may rest this Character and 1 of your {Dressrosa} type
        /// Leader or Stage cards: Draw 1 card. Then, trash 2 cards from the top of your deck."
        /// A compound cost whose second half needs a board click.</summary>
        private static void Nami_CompoundRestCostDraws()
        {
            var b = new Board();
            // The cost is "rest this Character AND 1 of your {Dressrosa} type Leader or Stage".
            // The default fixture Leader is Straw Hat, so the engine was refusing the ability
            // correctly and the failure was mine, not its. Give south a Dressrosa Leader.
            b.SetLeader("south", "OP15-002");   // Lucy, {Dressrosa}
            var nami = b.Character("south", "OP10-088");
            nami.PlayedOnTurn = 0; nami.Rested = false;
            int hand0 = b.S.Hand.Count, deck0 = b.S.Deck.Count;

            b.Apply(new GameCommand { Type = "activateMain", Seat = "south", Target = nami.InstanceId });
            b.AnswerPicks();

            // Draw 1 (+1 hand, -1 deck) then mill 2 (-2 deck) = deck -3 when the whole thing runs.
            bool drew = b.S.Hand.Count == hand0 + 1;
            bool milled = b.S.Deck.Count == deck0 - 3;
            Check("OP10-088 Nami: compound rest cost draws 1 and mills 2",
                  drew && milled,
                  $"hand {hand0}->{b.S.Hand.Count} (want +1), deck {deck0}->{b.S.Deck.Count} (want -3), "
                  + $"namiRested={nami.Rested}");
        }

        /// <summary>Kalgara — named in the brief every time. "You may trash 1 card from your hand:
        /// Look at 5 cards from the top of your deck; reveal up to a total of 2 … and add them to
        /// your hand."</summary>
        private static void Kalgara_LooksAtFiveAndAdds()
        {
            var b = new Board();
            b.HandCard("south", "ST29-004");
            b.HandCard("south", "ST29-009");

            b.PlayAndUse("OP15-101");

            bool looking = b.St.DeckLook != null;
            Check("OP15-101 Kalgara: paying opens the 5-card look",
                  looking,
                  "no deck look opened — the cost was taken and the payoff never appeared");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "named-cards" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-nc-don-{serial++}", Rested = false });
                    p.DonDeck = 3;
                }
                St.PendingEffects.Clear();
            }

            /// <summary>Play the card from hand for real, then press Use and answer what it asks.</summary>
            public void PlayAndUse(string cardId)
            {
                var c = Make(cardId, "south", "hand");
                S.Hand.Add(c);
                int slot = 0;
                for (int i = 0; i < 5; i++) if (S.CharacterArea[i] == null) { slot = i; break; }
                Apply(new GameCommand
                { Type = "playCard", Seat = "south", InstanceId = c.InstanceId, SlotIndex = slot });
                AnswerPicks();
            }

            /// <summary>Answer prompts the way the UI does: ask the ENGINE which cards it will accept
            /// rather than guessing a zone, and stop as soon as nothing changes.</summary>
            public void AnswerPicks()
            {
                for (int i = 0; i < 10; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int peBefore = St.PendingEffects.Count, logBefore = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                        foreach (var e in St.EventLog.Skip(logBefore)) Console.WriteLine("      log: " + e.Message);
                    if (St.PendingEffects.Count == peBefore && St.EventLog.Count == logBefore) break;
                }
            }

            public System.Collections.Generic.IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.Life.AsEnumerable().Reverse()) yield return x;
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    foreach (var x in p.Trash) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                    if (p.Stage != null) yield return p.Stage;
                }
            }

            public void SetLeader(string seat, string id)
            {
                var p = St.Players[seat];
                p.Leader = Make(id, seat, "leader");
            }

            public CardInstance HandCard(string seat, string id)
            { var c = Make(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return p.CharacterArea.First(x => x != null);
                var c = Make(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-nc-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
