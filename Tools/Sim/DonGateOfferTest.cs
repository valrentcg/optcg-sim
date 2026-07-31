using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The population healsweep cannot reach, driven at the layer it actually lives in.
    ///
    /// That sweep force-queues clauses, which enters BELOW the offer layer — so `[DON!! xN]` is
    /// bypassed by design and a probe built there reports a correct engine as broken (I wrote one and
    /// deleted it). Declaring the population "out of scope for that fixture" is only half an answer.
    /// This is the other half: drive it through the real entry point, `activateMain`.
    ///
    ///     ST13-001 Sabo (Leader)
    ///     [DON!! x1] [Activate: Main] [Once Per Turn] You may add 1 of your Characters with a cost
    ///     of 3 or more and 7000 power or more to the top of your Life cards face-up: Up to 1 of
    ///     your Characters gains +2000 power until the start of your next turn.
    ///
    /// Chosen because it stacks the two things this workstream is about on one card:
    ///
    ///   OFFER GATE   [DON!! x1] on a LEADER — the ability must not be offered without an attached
    ///                DON!!, and must be offered with one. Both directions, or "not offered" proves
    ///                nothing (a dead ability is also never offered)
    ///   LIFE ADD     the COST puts a Character on top of Life FACE-UP — a source (the field) and a
    ///                facing (up) that almost no other heal uses, and the brief names flip-Life
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- dongate
    /// </summary>
    public static class DonGateOfferTest
    {
        private static int passed, failed;

        private const string SaboLeader = "ST13-001";
        private const string BigCharacter = "EB01-041";   // cost 6, power 8000, no text of its own

        public static int Run()
        {
            Console.WriteLine("=== [DON!! xN] is an OFFER gate: drive it through activateMain ===");
            NotOfferedWithoutTheAttachedDon();
            OfferedWithTheAttachedDon();
            PayingItPutsTheCharacterOnTopOfLifeFaceUp();
            Console.WriteLine($"dongate: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void NotOfferedWithoutTheAttachedDon()
        {
            var b = new Board(attachDon: 0);
            bool offered = b.ActivateLeaderAndWasOffered();
            Check("with NO DON!! attached the [DON!! x1] ability is not offered",
                  !offered,
                  "the gate is on the LEADER's attached DON!!, and nothing should be offered below it");
        }

        private static void OfferedWithTheAttachedDon()
        {
            var b = new Board(attachDon: 1);
            bool offered = b.ActivateLeaderAndWasOffered();
            Check("with 1 DON!! attached it IS offered",
                  offered,
                  "if this fails too, the case above passed because the ability is DEAD, not gated");
        }

        /// <summary>The cost is itself a Life mechanic: a Character leaves the field and lands on TOP
        /// of Life, FACE-UP. Face-down here would hide a card the rules say is exposed, and the
        /// bottom of Life would be the wrong end entirely.</summary>
        private static void PayingItPutsTheCharacterOnTopOfLifeFaceUp()
        {
            var b = new Board(attachDon: 1);
            if (!b.ActivateLeaderAndWasOffered())
            { Check("paying it moves the Character to the TOP of Life, face-up", false, "never offered"); return; }

            if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
            {
                var pe0 = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                foreach (var x in b.Everything())
                    if (GameEngine.IsValidEffectTarget(b.St, pe0, x))
                        Console.WriteLine($"      [clickable] {x.CardId} in {x.Zone}");
                Console.WriteLine($"      [pe] text={pe0?.Text}");
            }
            int life0 = b.S.Life.Count;
            var victim = b.S.CharacterArea.FirstOrDefault(c => c != null && c.CardId == BigCharacter);
            b.AnswerWith(victim?.InstanceId);

            var top = b.S.Life.LastOrDefault();
            bool moved = b.S.Life.Count == life0 + 1 && top != null && top.CardId == BigCharacter;
            bool faceUp = top != null && top.FaceUp;
            bool leftField = !b.S.CharacterArea.Any(c => c != null && c.InstanceId == victim?.InstanceId);
            Check("paying it moves the Character to the TOP of Life, face-up, and off the field",
                  moved && faceUp && leftField,
                  $"life {life0} -> {b.S.Life.Count}, top={top?.CardId ?? "none"}, faceUp={faceUp}, "
                  + $"leftField={leftField}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board(int attachDon)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "don-gate" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009" }) p.Hand.Add(Make(id, p.Seat, "hand"));
                    for (int i = 0; i < 3; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    foreach (var c in p.Life) c.FaceUp = false;
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-dg-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                    p.AbilityUsedThisTurn.Clear();
                }
                S.Leader = Make(SaboLeader, "south", "leader");
                S.Leader.Rested = false;
                for (int i = 0; i < attachDon && i < S.CostArea.Count; i++)
                    S.Leader.AttachedDonIds.Add(S.CostArea[i].InstanceId);
                S.CharacterArea[0] = Make(BigCharacter, "south", "character");
                S.CharacterArea[0].PlayedOnTurn = 0;
                St.PendingEffects.Clear();
            }

            /// <summary>The REAL entry point: activate the Leader's [Activate: Main] and report
            /// whether a decision was raised for south.</summary>
            public bool ActivateLeaderAndWasOffered()
            {
                int pe0 = St.PendingEffects.Count;
                St = GameEngine.ApplyCommand(St, new GameCommand
                { Type = "activateMain", Seat = "south", Target = S.Leader.InstanceId });
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.TakeLast(4)) Console.WriteLine("      log: " + e.Message);
                return St.PendingEffects.Count > pe0
                    || St.PendingEffects.Any(e => e != null && e.Seat == "south");
            }

            public void AnswerWith(string targetId)
            {
                for (int i = 0; i < 8; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string t = targetId != null
                            && GameEngine.IsValidEffectTarget(St, pe, Find(targetId))
                        ? targetId
                        : Everything().FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int before = St.EventLog.Count;
                    St = GameEngine.ApplyCommand(St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = t });
                    if (St.EventLog.Count == before) break;
                }
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.TakeLast(6)) Console.WriteLine("      log: " + e.Message);
            }

            private CardInstance Find(string id) => Everything().FirstOrDefault(x => x.InstanceId == id);

            public System.Collections.Generic.IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.Life.AsEnumerable().Reverse()) yield return x;
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                }
            }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-dg-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
