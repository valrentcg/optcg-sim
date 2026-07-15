// Weighted position evaluation for the Advanced (search) bot. Mirror of the out-of-ship Tools/Sim
// eval; weights are the values discovered by the search-eval evolution. Pure C#.

using System.Linq;

namespace OnePieceTcg.Engine.Bot.Search
{
    public static class Evaluation
    {
        // discovered weights: board power(/1k), char count, life, hand adv, active DON, counter
        // reserve(/1k), blockers, rested-opp, trash(/5), aggro-low-life, + threat (opp open DON),
        // committed DON (attached), deck-out margin. New features start at conservative defaults;
        // the eval-evolution can tune them.
        private static readonly double[] W = { 1.87, 0.60, 1.20, 0.50, -0.38, -0.49, 0.40, 0.38, 0.00, -0.43,
                                               -0.15, 0.20, 0.05 };

        public static double Score(GameState s, string seat)
        {
            string opp = GameEngine.OtherSeat(seat);
            if (s.Status == "finished")
            {
                for (int i = s.EventLog.Count - 1; i >= 0; i--)
                {
                    var m = s.EventLog[i].Message;
                    if (string.IsNullOrEmpty(m) || !m.Contains("wins")) continue;
                    if (m.StartsWith("South")) return seat == "south" ? 1000 : -1000;
                    if (m.StartsWith("North")) return seat == "north" ? 1000 : -1000;
                }
                return 0;
            }
            var me = s.Players[seat]; var op = s.Players[opp];
            int myPow = me.CharacterArea.Where(c => c != null).Sum(c => GameEngine.GetPower(s, c));
            int opPow = op.CharacterArea.Where(c => c != null).Sum(c => GameEngine.GetPower(s, c));
            int myChars = me.CharacterArea.Count(c => c != null), opChars = op.CharacterArea.Count(c => c != null);
            int myBlock = me.CharacterArea.Count(c => c != null && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false));
            int opBlock = op.CharacterArea.Count(c => c != null && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false));
            int restedOpp = op.CharacterArea.Count(c => c != null && c.Rested);
            int counterReserve = me.Hand.Sum(c => GameEngine.GetCounterPower(c));
            double aggro = System.Math.Max(0, 5 - op.Life.Count);
            int oppOpenDon = GameEngine.ActiveDonCount(op);                       // opponent's counter/removal threat (§7)
            int myAttachedDon = AttachedDon(me) - AttachedDon(op);               // committed board power / tempo
            int deckMargin = System.Math.Min(20, me.Deck.Count - op.Deck.Count); // deck-out awareness (capped)

            return W[0] * (myPow - opPow) / 1000.0
                 + W[1] * (myChars - opChars)
                 + W[2] * (me.Life.Count - op.Life.Count)
                 + W[3] * (me.Hand.Count - op.Hand.Count)
                 + W[4] * GameEngine.ActiveDonCount(me)
                 + W[5] * counterReserve / 1000.0
                 + W[6] * (myBlock - opBlock)
                 + W[7] * restedOpp
                 + W[8] * (me.Trash.Count / 5.0)
                 + W[9] * aggro
                 + W[10] * oppOpenDon
                 + W[11] * myAttachedDon
                 + W[12] * (deckMargin / 5.0);
        }

        private static int AttachedDon(PlayerState p)
        {
            int n = p.Leader?.AttachedDonIds?.Count ?? 0;
            foreach (var c in p.CharacterArea) if (c != null) n += c.AttachedDonIds?.Count ?? 0;
            return n;
        }
    }
}
