using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// What happens when a decision is answered WRONGLY — the cases a real client produces without
    /// anyone trying to cheat.
    ///
    ///   the opponent answers your prompt        in PvP both clients send commands; the engine is the
    ///                                           only thing that can refuse one
    ///   the same Use arrives twice              a laggy click, a resend, a duplicated network frame
    ///   a stale effect id                       the effect resolved on the other client first
    ///   the source is gone before the answer    played, then K.O.'d while the prompt was open
    ///
    /// None of these should throw, and none should let an effect happen twice or happen for the wrong
    /// player. The first is the one with teeth: if the engine accepts a resolveEffect from the seat that
    /// does not own the decision, an opponent can spend YOUR optional effects in a networked game, and
    /// no amount of UI gating fixes that.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- promptabuse
    /// </summary>
    public static class PromptAbuseTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Answering a prompt wrongly: seat, duplicate, stale, dead source ===");
            OpponentCannotResolveYourEffect();
            OpponentCannotSkipYourEffect();
            OpponentCannotResolveWithAnEmptyEffectId();
            TheSameUseTwiceDoesNotApplyTwice();
            AStaleEffectIdIsIgnored();
            SourceRemovedBeforeTheAnswer();
            Console.WriteLine($"promptabuse: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private const string CLAUSE = "You may rest this Character: Draw 1 card.";

        /// <summary>The one with teeth. In PvP both clients send commands and the engine is the only
        /// referee; if it honours a resolve from the wrong seat, the opponent spends your effects.</summary>
        private static void OpponentCannotResolveYourEffect()
        {
            var b = new Board();
            var src = b.Character("south", "ST29-009");
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("opponent cannot resolve your effect", false, "nothing queued"); return; }

            int hand0 = b.S.Hand.Count;
            bool threw = false;
            try
            {
                b.Apply(new GameCommand   // NORTH answering SOUTH's prompt
                { Type = "resolveEffect", Seat = "north", EffectId = pe.EffectId });
            }
            catch (Exception) { threw = true; }

            bool stillPending = b.St.PendingEffects.Any(e => e != null && e.EffectId == pe.EffectId);
            Check("the opponent cannot resolve YOUR pending effect",
                  !threw && stillPending && b.S.Hand.Count == hand0 && !src.Rested,
                  threw ? "it threw" : $"stillPending={stillPending} hand {hand0}->{b.S.Hand.Count} "
                          + $"sourceRested={src.Rested} — north must not be able to spend south's effect");
        }

        private static void OpponentCannotSkipYourEffect()
        {
            var b = new Board();
            var src = b.Character("south", "ST29-009");
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("opponent cannot skip your effect", false, "nothing queued"); return; }

            bool threw = false;
            try { b.Apply(new GameCommand { Type = "passEffect", Seat = "north", EffectId = pe.EffectId }); }
            catch (Exception) { threw = true; }

            Check("the opponent cannot SKIP your pending effect away",
                  !threw && b.St.PendingEffects.Any(e => e != null && e.EffectId == pe.EffectId),
                  threw ? "it threw" : "north skipped south's decision for them");
        }

        /// <summary>The same question with the effect id LEFT OUT. FindPendingEffect gates on seat
        /// when an id is supplied, but its no-id fallback ends in PendingEffects[0] — which belongs
        /// to whoever queued first, not necessarily to the seat asking. A client that omits the id
        /// is ordinary (the UI has one pending effect and does not bother), so this is reachable
        /// without anyone trying to cheat.</summary>
        private static void OpponentCannotResolveWithAnEmptyEffectId()
        {
            var b = new Board();
            var src = b.Character("south", "ST29-009");
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("opponent cannot resolve with no effect id", false, "nothing queued"); return; }

            int hand0 = b.S.Hand.Count;
            bool threw = false;
            try { b.Apply(new GameCommand { Type = "resolveEffect", Seat = "north" }); }   // no EffectId
            catch (Exception) { threw = true; }

            bool stillPending = b.St.PendingEffects.Any(e => e != null && e.EffectId == pe.EffectId);
            Check("the opponent cannot resolve your effect by omitting the effect id",
                  !threw && stillPending && b.S.Hand.Count == hand0 && !src.Rested,
                  threw ? "it threw" : $"stillPending={stillPending} hand {hand0}->{b.S.Hand.Count} "
                          + $"sourceRested={src.Rested} — north resolved south's effect via the no-id path");
        }

        /// <summary>A duplicated frame or an impatient double-click must not pay twice.</summary>
        private static void TheSameUseTwiceDoesNotApplyTwice()
        {
            var b = new Board();
            var src = b.Character("south", "ST29-009");
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("double Use", false, "nothing queued"); return; }

            int hand0 = b.S.Hand.Count;
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            int afterFirst = b.S.Hand.Count;
            bool threw = false;
            try { b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId }); }
            catch (Exception) { threw = true; }

            Check("the same Use arriving twice draws once, not twice",
                  !threw && b.S.Hand.Count == afterFirst,
                  threw ? "it threw" : $"hand {hand0} -> {afterFirst} -> {b.S.Hand.Count} (want no change on the resend)");
        }

        private static void AStaleEffectIdIsIgnored()
        {
            var b = new Board();
            b.Character("south", "ST29-009");
            int hand0 = b.S.Hand.Count;
            bool threw = false;
            try
            {
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = "effect-99999" });
            }
            catch (Exception) { threw = true; }

            Check("an effect id that no longer exists is ignored, not a crash",
                  !threw && b.S.Hand.Count == hand0,
                  threw ? "it threw" : $"hand {hand0}->{b.S.Hand.Count}");
        }

        /// <summary>Play a card, open its prompt, then remove the card. Answering afterwards must not
        /// throw on the missing source — the prompt outlives the Character in a real game whenever a
        /// removal resolves first.</summary>
        private static void SourceRemovedBeforeTheAnswer()
        {
            var b = new Board();
            var src = b.Character("south", "ST29-009");
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("dead source", false, "nothing queued"); return; }

            GameEngine.AuditKoByEffect(b.St, "south", src.InstanceId);   // the source leaves first

            bool threw = false;
            try { b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId }); }
            catch (Exception ex) { threw = true; Console.WriteLine("      threw: " + ex.GetType().Name + ": " + ex.Message); }

            Check("answering a prompt whose source has left the field does not crash", !threw);
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "prompt-abuse" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-pa-don-{serial++}", Rested = false });
                }
                St.PendingEffects.Clear();
            }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-pa-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
