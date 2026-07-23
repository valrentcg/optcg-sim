using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>A procedurally-built puzzle candidate: a fresh main-phase board for the attacker plus teaching
    /// metadata. Produced deterministically from an integer seed (see <see cref="PuzzleGenerator"/>).</summary>
    public sealed class GeneratedPuzzle
    {
        public int Seed;
        public GameState State;
        public string Category;       // don-push / blocker-break / counter-pump / combo-wall
        public string Title;
        public string Teaches;        // one-line lesson shown after solving
        public int Difficulty;        // advisory only — the certifier MEASURES real difficulty and overrides
        public string AttackerSeat = "south";
    }

    /// <summary>
    /// Deterministic, procedural generator for the puzzle / brain-teaser mode. Unlike a "swing everything"
    /// warm-up, every scenario REQUIRES a non-attack action (attach DON!! to the right attacker, play a removal,
    /// use an ability) — the naive alpha-strike falls short, and the defender's Blockers / counters / counter
    /// events are LOAD-BEARING (a misplayed line gets punished by them). The offline `puzzlegen` pass proves each
    /// is forced-lethal AND that attacks-only cannot win, then grades difficulty by how few first moves keep the
    /// win. Every card is drawn from a color-legal pool (<see cref="PuzzlePalette"/>) for a wide, legal variety
    /// of leaders and colors on both sides.
    ///
    /// RNG is an explicit xorshift64 (NOT System.Random, whose sequence differs across Mono/.NET), so a baked
    /// seed reproduces byte-for-byte in the Unity client and the headless Tools/Sim.
    /// </summary>
    public static class PuzzleGenerator
    {
        // ── deterministic, runtime-independent RNG ───────────────────────────────
        private struct Rng
        {
            private ulong _s;
            public Rng(int seed)
            {
                _s = ((ulong)(uint)seed * 0x9E3779B97F4A7C15UL) ^ 0xD1B54A32D192ED03UL;
                if (_s == 0) _s = 0x1234_5678_9ABC_DEF0UL;
            }
            public uint U() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; ulong x = _s; return (uint)(x >> 32) ^ (uint)x; }
            public int Range(int lo, int hiInclusive) => lo + (int)(U() % (uint)(hiInclusive - lo + 1));
            public T Pick<T>(T[] a) => a[(int)(U() % (uint)a.Length)];
            public T Pick<T>(IReadOnlyList<T> a) => a[(int)(U() % (uint)a.Count)];
            public bool Chance(int pct) => (int)(U() % 100) < pct;
        }

        private static PuzzlePalette.ColorPool Pal(string color) => PuzzlePalette.ByColor[color];

        private enum Family { DonPush, BlockerBreak, CounterPump, ComboWall, DoubleStrike, RushFinisher, DoubleWall }

        public static GeneratedPuzzle Build(int seed)
        {
            var rng = new Rng(seed);
            var fam = rng.Pick(new[] { Family.DonPush, Family.BlockerBreak, Family.CounterPump, Family.ComboWall, Family.DoubleStrike, Family.RushFinisher, Family.DoubleWall });
            var g = new GeneratedPuzzle { Seed = seed };
            switch (fam)
            {
                case Family.DonPush:      BuildDonPush(ref rng, g); break;
                case Family.BlockerBreak: BuildBlockerBreak(ref rng, g); break;
                case Family.CounterPump:  BuildCounterPump(ref rng, g); break;
                case Family.ComboWall:    BuildComboWall(ref rng, g); break;
                case Family.DoubleStrike: BuildDoubleStrike(ref rng, g); break;
                case Family.RushFinisher: BuildRushFinisher(ref rng, g); break;
                case Family.DoubleWall:   BuildDoubleWall(ref rng, g); break;
            }
            return g;
        }

        // ── families (all REQUIRE a non-attack action; naive swinging loses) ─────

        // DON!! allocation: attackers sit below the 5000 Leader and must be lifted to connect. You have exactly
        // enough DON!! — a 4000 needs one, a 3000 needs two — so a wrong split leaves one short.
        private static void BuildDonPush(ref Rng rng, GeneratedPuzzle g)
        {
            string sColor = rng.Pick(PuzzlePalette.Colors);
            string nColor = rng.Pick(PuzzlePalette.Colors);
            var (st, S, N) = Blank("gen-" + g.Seed, sColor, nColor, ref rng); g.State = st;
            int life = rng.Chance(45) ? 1 : rng.Range(2, 3);   // bias toward the Easy 1-Life warm-ups
            SetLife(N, life, Pal(nColor).Life);
            int chars = life, don = 0;
            for (int i = 0; i < chars; i++)
            {
                bool small = rng.Chance(30) && Pal(sColor).P3000.Length > 0;     // a 3000 needs TWO DON
                AddChar(S, small ? rng.Pick(Pal(sColor).P3000) : rng.Pick(Pal(sColor).P4000));
                don += small ? 2 : 1;
            }
            AddDon(S, don);
            g.Category = "don-push"; g.Difficulty = life <= 1 ? 1 : (don >= 4 ? 3 : 2);
            g.Title = "Spread the DON!!";
            g.Teaches = "Lift every attacker to 5000: a 4000 needs one DON!!, a 3000 needs two. You have exactly enough — allocate so none fall short, and swing with the Leader too.";
        }

        // Blocker wall, TIGHT: the wall eats one swing, leaving you exactly one short. Remove it first.
        private static void BuildBlockerBreak(ref Rng rng, GeneratedPuzzle g)
        {
            string sColor = PickColorWith(ref rng, p => HasAnyRemoval(p));
            string nColor = PickColorWith(ref rng, p => p.Blockers.Length > 0);
            var (st, S, N) = Blank("gen-" + g.Seed, sColor, nColor, ref rng); g.State = st;
            int life = rng.Range(1, 2);
            SetLife(N, life, Pal(nColor).Life);
            var rem = ChooseRemoval(ref rng, Pal(sColor));
            var blk = ChooseBlocker(ref rng, Pal(nColor), rem.targetCap);
            AddChar(N, blk.id);
            for (int i = 0; i < life; i++) AddChar(S, rng.Pick(Pal(sColor).P6000));   // Leader + life = life+1 attackers
            S.Hand.Add(In(rem.id, "south", "hand"));
            AddDon(S, rem.playCost);                                                  // exactly enough DON to play it
            g.Category = "blocker-break"; g.Difficulty = rem.ko ? 3 : (life <= 1 ? 1 : 2);
            g.Title = rem.ko ? "K.O. the wall" : "Rest the wall";
            g.Teaches = rem.ko
                ? "Their Blocker eats one swing, leaving you one short. K.O. it first, THEN alpha-strike for lethal."
                : "Their Blocker eats one swing, leaving you one short. Rest it first (a rested Blocker can't block), then swing for lethal.";
        }

        // Counters/counter-events are LOAD-BEARING: your attackers are below 5000 and must be pumped, and the
        // defender will lift the Leader over any swing they can — so a wrong DON!! split gets punished.
        private static void BuildCounterPump(ref Rng rng, GeneratedPuzzle g)
        {
            string sColor = rng.Pick(PuzzlePalette.Colors);
            string nColor = PickColorWith(ref rng, p => p.Ctr1000.Length + p.Ctr2000.Length + p.Ev2000.Length + p.Ev4000.Length > 0);
            var (st, S, N) = Blank("gen-" + g.Seed, sColor, nColor, ref rng); g.State = st;
            int life = rng.Range(1, 2);
            SetLife(N, life, Pal(nColor).Life);
            int nCtr = rng.Range(1, 2);
            var pn = Pal(nColor);
            bool useEvents = rng.Chance(55) && (pn.Ev2000.Length > 0 || pn.Ev4000.Length > 0);
            int oppDon = 0;
            for (int i = 0; i < nCtr; i++)
            {
                if (useEvents)
                {
                    bool big = pn.Ev4000.Length > 0 && (pn.Ev2000.Length == 0 || rng.Chance(50));
                    var ev = big ? rng.Pick(pn.Ev4000) : rng.Pick(pn.Ev2000);
                    N.Hand.Add(In(ev.id, "north", "hand")); oppDon += ev.cost;
                }
                else
                {
                    var pool = pn.Ctr2000.Length > 0 && rng.Chance(60) ? pn.Ctr2000 : (pn.Ctr1000.Length > 0 ? pn.Ctr1000 : pn.Ctr2000);
                    N.Hand.Add(In(rng.Pick(pool), "north", "hand"));
                }
            }
            if (useEvents) AddDon(N, oppDon + rng.Range(0, 1));                       // DON!! to actually play the events
            int chars = life + nCtr;                                                 // Leader + these; they stop nCtr swings -> life+1 land
            for (int i = 0; i < chars; i++) AddChar(S, rng.Pick(Pal(sColor).P4000));
            AddDon(S, chars);                                                        // exactly one DON per attacker
            g.Category = "counter-pump"; g.Difficulty = useEvents ? 4 : 3;
            g.Title = useEvents ? "Push through the counter events" : "Push through the counters";
            g.Teaches = "Every 4000 attacker must be lifted to 5000 to connect, and they can stop one swing per counter. Pump ALL of them and bring one more threat than they can hold back.";
        }

        // Expert: a Blocker AND a counter, with DON!! only just enough to both clear the wall and pump your
        // attackers over the counter. One resource out of place and it isn't lethal.
        private static void BuildComboWall(ref Rng rng, GeneratedPuzzle g)
        {
            string sColor = PickColorWith(ref rng, p => HasAnyRemoval(p) && p.P4000.Length > 0);
            string nColor = PickColorWith(ref rng, p => p.Blockers.Length > 0 && (p.Ctr1000.Length + p.Ctr2000.Length > 0));
            var (st, S, N) = Blank("gen-" + g.Seed, sColor, nColor, ref rng); g.State = st;
            SetLife(N, 1, Pal(nColor).Life);
            var rem = ChooseRemoval(ref rng, Pal(sColor));
            var blk = ChooseBlocker(ref rng, Pal(nColor), rem.targetCap);
            AddChar(N, blk.id);
            var cpool = Pal(nColor).Ctr2000.Length > 0 ? Pal(nColor).Ctr2000 : Pal(nColor).Ctr1000;
            N.Hand.Add(In(rng.Pick(cpool), "north", "hand"));                          // one counter
            int chars = 2;                                                             // Leader + 2 pumped = 3 swings; wall+counter remove 2 -> 1 lands (life 1)
            for (int i = 0; i < chars; i++) AddChar(S, rng.Pick(Pal(sColor).P4000));
            S.Hand.Add(In(rem.id, "south", "hand"));
            AddDon(S, rem.playCost + chars);                                          // exactly: play removal + one DON per 4000
            g.Category = "combo-wall"; g.Difficulty = 4;
            g.Title = "Break the wall, beat the counter";
            g.Teaches = "One Blocker AND one counter, and DON!! only just enough. Clear the wall, pump every attacker to 5000, then swing — any resource out of place and it falls short.";
        }

        // Double Attack: your double-attacker deals 2 Life damage in ONE hit, but only if it connects. A
        // Blocker would absorb it — clear the wall so the double strike lands the finish.
        private static void BuildDoubleStrike(ref Rng rng, GeneratedPuzzle g)
        {
            string sColor = PickColorWith(ref rng, p => HasAnyRemoval(p) && p.DoubleAttack.Length > 0);
            string nColor = PickColorWith(ref rng, p => p.Blockers.Length > 0);
            var (st, S, N) = Blank("gen-" + g.Seed, sColor, nColor, ref rng); g.State = st;
            SetLife(N, 2, Pal(nColor).Life);                        // 2 Life: Leader (1) + double strike (2) = lethal
            var rem = ChooseRemoval(ref rng, Pal(sColor));
            var blk = ChooseBlocker(ref rng, Pal(nColor), rem.targetCap);
            AddChar(N, blk.id);
            AddChar(S, rng.Pick(Pal(sColor).DoubleAttack));         // the double-attacker finisher
            S.Hand.Add(In(rem.id, "south", "hand"));
            AddDon(S, rem.playCost);
            g.Category = "double-strike"; g.Difficulty = 3;
            g.Title = rem.ko ? "Double strike — K.O. the wall" : "Double strike — rest the wall";
            g.Teaches = "Your [Double Attack] Character deals 2 Life damage in one hit — but the Blocker will absorb it. Clear the wall first so the double strike lands for lethal.";
        }

        // Rush: you are one hit short on the board, but a [Rush] Character in hand can attack the turn it's
        // played. Play it and swing for the final point.
        private static void BuildRushFinisher(ref Rng rng, GeneratedPuzzle g)
        {
            string sColor = PickColorWith(ref rng, p => p.Rush.Length > 0);
            string nColor = rng.Pick(PuzzlePalette.Colors);
            var (st, S, N) = Blank("gen-" + g.Seed, sColor, nColor, ref rng); g.State = st;
            int life = rng.Range(1, 2);
            SetLife(N, life, Pal(nColor).Life);
            for (int i = 0; i < life - 1; i++) AddChar(S, rng.Pick(Pal(sColor).P6000));   // Leader + (life-1) = life hits on board
            var rush = rng.Pick(Pal(sColor).Rush);
            S.Hand.Add(In(rush.id, "south", "hand"));
            AddDon(S, rush.cost);                                   // exactly enough DON!! to play the rusher
            g.Category = "rush-finisher"; g.Difficulty = 2;
            g.Title = "Rush the last hit";
            g.Teaches = "You're one hit short on board. Play your [Rush] Character and attack with it this turn for the final point of lethal.";
        }

        // Two Blockers, each eats a swing. With exactly Leader + 1 attacker you must clear BOTH walls to land
        // the two hits — a multi-removal sequencing puzzle.
        private static void BuildDoubleWall(ref Rng rng, GeneratedPuzzle g)
        {
            string sColor = PickColorWith(ref rng, p => HasAnyRemoval(p) && p.P6000.Length > 0);
            string nColor = PickColorWith(ref rng, p => p.Blockers.Length > 0);
            var (st, S, N) = Blank("gen-" + g.Seed, sColor, nColor, ref rng); g.State = st;
            SetLife(N, 1, Pal(nColor).Life);
            var rem1 = ChooseRemoval(ref rng, Pal(sColor));
            var rem2 = ChooseRemoval(ref rng, Pal(sColor));
            AddChar(N, ChooseBlocker(ref rng, Pal(nColor), rem1.targetCap).id);
            AddChar(N, ChooseBlocker(ref rng, Pal(nColor), rem2.targetCap).id);
            AddChar(S, rng.Pick(Pal(sColor).P6000));                  // Leader + 1 attacker = 2 swings vs 2 walls
            S.Hand.Add(In(rem1.id, "south", "hand"));
            S.Hand.Add(In(rem2.id, "south", "hand"));
            AddDon(S, rem1.playCost + rem2.playCost);                 // exactly enough to play both
            g.Category = "double-wall"; g.Difficulty = 4;
            g.Title = "Break through two walls";
            g.Teaches = "Two Blockers will each absorb a swing, leaving you short. Clear BOTH before your attackers can land lethal.";
        }

        // ── color / removal / blocker selection ──────────────────────────────────

        private static bool HasAnyRemoval(PuzzlePalette.ColorPool p) => p.RestRemoval.Length + p.KoRemoval.Length > 0;

        private static string PickColorWith(ref Rng rng, Func<PuzzlePalette.ColorPool, bool> pred)
        {
            var ok = PuzzlePalette.Colors.Where(c => pred(Pal(c))).ToList();
            return ok.Count > 0 ? ok[(int)(rng.U() % (uint)ok.Count)] : PuzzlePalette.Colors[0];
        }

        private static (string id, int playCost, int targetCap, bool ko) ChooseRemoval(ref Rng rng, PuzzlePalette.ColorPool p)
        {
            var ko = p.KoRemoval.Select(r => (r.id, r.playCost, r.targetCap, ko: true));
            var rest = p.RestRemoval.Select(r => (r.id, r.playCost, r.targetCap, ko: false));
            var all = ko.Concat(rest).ToList();
            return all[(int)(rng.U() % (uint)all.Count)];
        }

        private static (string id, int cost) ChooseBlocker(ref Rng rng, PuzzlePalette.ColorPool p, int maxCost)
        {
            var ok = p.Blockers.Where(b => b.cost <= maxCost).ToList();
            if (ok.Count == 0) ok = p.Blockers.ToList();
            int minc = ok.Min(b => b.cost);                 // cheapest wall = most reliably removal-targetable
            var cheap = ok.Where(b => b.cost == minc).ToList();
            return cheap[(int)(rng.U() % (uint)cheap.Count)];
        }

        // ── board construction ────────────────────────────────────────────────────

        private static (GameState, PlayerState, PlayerState) Blank(string seed, string sColor, string nColor, ref Rng rng)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = seed });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; var N = st.Players["north"];
            S.TurnsStarted = 4; N.TurnsStarted = 4;
            for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
            S.Hand.Clear(); N.Hand.Clear(); S.CostArea.Clear(); N.CostArea.Clear();
            S.Life.Clear(); N.Life.Clear();
            SetLeader(S, rng.Pick(Pal(sColor).Leaders));
            SetLeader(N, rng.Pick(Pal(nColor).Leaders));
            return (st, S, N);
        }

        private static void SetLeader(PlayerState p, string leaderId)
        {
            p.Leader.CardId = leaderId;
            p.Leader.Rested = false;
            p.Leader.PlayedOnTurn = 0;
            p.Leader.AttachedDonIds.Clear();
        }

        private static void AddChar(PlayerState p, string id)
        {
            for (int i = 0; i < p.CharacterArea.Count; i++)
                if (p.CharacterArea[i] == null) { p.CharacterArea[i] = In(id, p.Seat, "character"); return; }
        }
        private static void SetLife(PlayerState p, int n, string lifeCardId)
        { p.Life.Clear(); for (int i = 0; i < n; i++) p.Life.Add(In(lifeCardId, p.Seat, "life")); }
        private static void AddDon(PlayerState p, int n)
        {
            for (int i = 0; i < n; i++)
                p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-don-{Guid.NewGuid():N}".Substring(0, 16), Rested = false });
        }

        private static CardInstance In(string cardId, string owner, string zone) => new CardInstance
        {
            InstanceId = $"{owner}-{cardId}-{Guid.NewGuid():N}".Substring(0, 22),
            CardId = cardId, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
        };
    }
}
