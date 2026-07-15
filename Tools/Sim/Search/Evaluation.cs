using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Search
{
    /// <summary>Weighted linear evaluation of a state from a seat's view — the "how good is this position"
    /// function the search agent maximizes (blueprint §10.6 value model). Each weight is a GENE the eval
    /// tournament tunes. Features drawn from the blueprint: board development (§5.1), life/tempo, card &
    /// counter economy (§7), board control / blockers (§9). Add more features here → longer gene vector.</summary>
    public sealed class EvalWeights
    {
        // indexable genes (name, value, lo, hi)
        public double[] W;
        public static readonly (string name, double def, double lo, double hi)[] Genes =
        {
            ("BoardPowerPerK", 1.0, -1, 4),    // per 1000 (my-opp) total character power
            ("CharCount",      0.6, -1, 3),    // per (my-opp) characters in play
            ("Life",           1.2,  0, 4),    // per (my-opp) life
            ("HandAdv",        0.5, -1, 3),    // per (my-opp) hand card
            ("ActiveDon",      0.05,-1, 2),    // open (unrested) DON — reactive resource / tempo
            ("CounterReserve", 0.3, -1, 3),    // per 1000 counter power sitting in my hand (§7)
            ("Blockers",       0.4, -1, 3),    // per (my-opp) Blocker character in play (§9)
            ("RestedOpp",      0.2, -1, 2),    // per rested opponent character (attackable / tempo)
            ("TrashValue",     0.0, -1, 2),    // per 5 cards in my trash (recursion decks)
            ("AggroLowLife",   0.3, -1, 3),    // bonus scaling as opponent life drops (finish pressure)
            ("OppOpenDon",    -0.15,-2, 1),    // opponent's open DON — counter/removal threat (§7)
            ("AttachedDon",    0.20,-1, 2),    // (my - opp) DON attached to board — committed power
            ("DeckMargin",     0.05,-1, 1),    // (my - opp) deck size /5, capped — deck-out awareness
        };

        public EvalWeights() { W = Genes.Select(g => g.def).ToArray(); }
        public EvalWeights(double[] w) { W = w; }
        public double Get(int i) => W[i];
        public EvalWeights Clone() => new EvalWeights((double[])W.Clone());
        public override string ToString() => string.Join(" ", Genes.Select((g, i) => $"{g.name}={W[i]:0.00}"));
    }

    public static class Evaluation
    {
        public static double Score(GameState s, string seat, EvalWeights w)
        {
            string oppSeat = GameEngine.OtherSeat(seat);
            if (s.Status == "finished")
            {
                for (int i = s.EventLog.Count - 1; i >= 0; i--)
                {
                    var m = s.EventLog[i].Message;
                    if (string.IsNullOrEmpty(m) || !m.Contains("wins")) continue;
                    if (m.StartsWith("South")) return "south" == seat ? 1000 : -1000;
                    if (m.StartsWith("North")) return "north" == seat ? 1000 : -1000;
                }
                return 0;
            }
            var me = s.Players[seat]; var op = s.Players[oppSeat];
            int myPow = me.CharacterArea.Where(c => c != null).Sum(c => GameEngine.GetPower(s, c));
            int opPow = op.CharacterArea.Where(c => c != null).Sum(c => GameEngine.GetPower(s, c));
            int myChars = me.CharacterArea.Count(c => c != null), opChars = op.CharacterArea.Count(c => c != null);
            int myBlock = me.CharacterArea.Count(c => c != null && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false));
            int opBlock = op.CharacterArea.Count(c => c != null && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false));
            int restedOpp = op.CharacterArea.Count(c => c != null && c.Rested);
            int counterReserve = me.Hand.Sum(c => GameEngine.GetCounterPower(c));
            double aggro = System.Math.Max(0, 5 - op.Life.Count); // grows as opponent life falls

            int oppOpenDon = GameEngine.ActiveDonCount(op);
            int attachedDon = AttachedDon(me) - AttachedDon(op);
            int deckMargin = System.Math.Min(20, me.Deck.Count - op.Deck.Count);

            var W = w.W;
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
                 + W[11] * attachedDon
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
