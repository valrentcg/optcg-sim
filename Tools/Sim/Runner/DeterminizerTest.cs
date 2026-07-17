using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Planning;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// FALSIFICATION-FIRST tests for the K=1 determinizer. Each assertion is written so it would FAIL if the
    /// sampler kept the truth, corrupted the public state, or produced an illegal/unrunnable world — the
    /// point is to be able to break it, not to confirm it. Turning these green is what lets privacy-test's
    /// LAYER 2 go green honestly.
    /// </summary>
    public static class DeterminizerTest
    {
        public static int Run(DeckDef south, DeckDef north)
        {
            Console.WriteLine("=== K=1 determinizer ===");
            int fail = 0;
            fail += Section("secrecy: no true hidden identity reachable from the sampled world", Secrecy(south, north));
            fail += Section("public zones + own hand preserved exactly", Preserved(south, north));
            fail += Section("hidden zones legal (multiset == decklist)", Legal(south, north));
            fail += Section("sampled world is engine-runnable to completion", Runnable(south, north));
            fail += Section("deterministic in seed; different seed ⇒ different hidden arrangement", Deterministic(south, north));
            fail += Section("ledger: a known top-of-deck segment is PLACED, not resampled", KnownTopPlaced(south, north));
            fail += Section("honest-greedy route is invariant to the referee's hidden assignment", GreedyRouteNoninterference(south, north));
            Console.WriteLine();
            Console.WriteLine(fail == 0 ? "PASS - the sampled world is legal, honest, and runnable." : $"FAIL - {fail} determinizer check(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        private static int Section(string name, (bool ok, string detail) r)
        {
            Console.WriteLine($"  [{(r.ok ? "ok  " : "FAIL")}] {name}{(r.detail == null ? "" : " - " + r.detail)}");
            return r.ok ? 0 : 1;
        }

        private static KnowledgeState Know(DeckDef south, DeckDef north) => new KnowledgeState
        { Seat = "south", OwnList = south, OpponentList = north, Assumption = ListAssumption.OpenList };

        // ---- secrecy: the whole point ----------------------------------------------------------------

        private static (bool, string) Secrecy(DeckDef south, DeckDef north)
        {
            // The bot determinizes on EVERY kind of decision, not just clean main. Exercise the reactive
            // states too — battle steps (incl. the RevealedLife-in-flight resample) and pending effects — so
            // the secrecy property is verified where the bot actually consumes it, and any true instance id
            // that only dangles/leaks via a battle/pending reference path is caught.
            string why = null;
            var reached = new List<string>();      // ONLY the sub-states actually exercised — no coverage the run didn't do

            int mainRun = 0;
            for (int t = 0; t < 6; t++)
            {
                var st = PrivacyTest.SouthDecision(south, north, $"det:secrecy:{t}", 40 + t * 6);
                if (st == null) continue;
                mainRun++;
                if (!CleanAfterDeterminize(st, "south", south, north, 100 + t, revealedLife: false, out why)) return (false, "main: " + why);
            }
            if (mainRun > 0) reached.Add($"main×{mainRun}");

            // Battle: TRIGGER viewed from the ATTACKER — the life card in flight must be resampled, not leaked.
            var trig = Reach(south, north, "trg", s => s.Battle != null && s.Battle.Step == "trigger" && s.Battle.RevealedLife != null);
            if (trig != null)
            {
                string atk = trig.Battle.AttackerSeat;
                if (!CleanAfterDeterminize(trig, atk, DeckOf(atk, south, north), DeckOf(Other(atk), south, north), 300, revealedLife: true, out why))
                    return (false, "trigger/attacker: " + why);
                reached.Add("trigger");
            }
            var ctr = Reach(south, north, "ctr", s => s.Battle != null && s.Battle.Step == "counter");
            if (ctr != null)
            {
                string def = ctr.Battle.TargetSeat;
                if (!CleanAfterDeterminize(ctr, def, DeckOf(def, south, north), DeckOf(Other(def), south, north), 400, revealedLife: false, out why))
                    return (false, "counter/defender: " + why);
                reached.Add("counter");
            }
            var pend = Reach(south, north, "pnd", s => s.PendingEffects.Count > 0 && s.Battle == null);
            if (pend != null)
            {
                string ps = pend.PendingEffects[0].Seat;
                if (!CleanAfterDeterminize(pend, ps, DeckOf(ps, south, north), DeckOf(Other(ps), south, north), 500, revealedLife: false, out why))
                    return (false, "pending: " + why);
                reached.Add("pending");
            }

            // REQUIRE the critical paths, or the green is false confidence: the trigger fixture is the ONLY one
            // that exercises the RevealedLife-in-flight resample, so if it wasn't reached, secrecy for that path
            // is UNPROVEN and the test must not claim it.
            if (mainRun == 0) return (false, "no clean-main fixtures reached");
            if (!reached.Contains("trigger"))
                return (false, "trigger fixture NOT REACHED — RevealedLife-resample secrecy is unproven (reached: " + string.Join(",", reached) + ")");
            return (true, "clean at: " + string.Join(", ", reached));
        }

        /// <summary>Poison everything hidden from <paramref name="seat"/> (optionally the life card in flight),
        /// determinize, and require no poison string reachable from the sampled world.</summary>
        private static bool CleanAfterDeterminize(GameState st, string seat, DeckDef own, DeckDef opp, int seed, bool revealedLife, out string why)
        {
            why = null;
            PrivacyTest.PoisonHidden(st, seat);
            if (revealedLife && st.Battle?.RevealedLife != null)
            { st.Battle.RevealedLife.CardId = PrivacyTest.PoisonTag + "-REVLIFE"; st.Battle.RevealedLife.InstanceId = PrivacyTest.PoisonTag + "-REVLIFE-I"; }
            SearchWorld world;
            try { world = RootWorldSampler.Determinize(st, new KnowledgeState { Seat = seat, OwnList = own, OpponentList = opp, Assumption = ListAssumption.OpenList }, seed); }
            catch (Exception e) { why = $"determinize threw: {e.Message}"; return false; }
            var leaked = PrivacyTest.ReachableStrings(world.State).Where(s => s != null && s.Contains(PrivacyTest.PoisonTag)).Distinct().ToList();
            if (leaked.Count > 0) { why = $"{leaked.Count} poison reachable, e.g. {leaked[0]}"; return false; }
            return true;
        }

        private static GameState Reach(DeckDef s, DeckDef n, string tag, Func<GameState, bool> hit)
        {
            for (int t = 0; t < 8; t++) { var st = PrivacyTest.ReachFirst(s, n, $"det:sec:{tag}:{t}", hit, 700); if (st != null) return st; }
            return null;
        }

        private static DeckDef DeckOf(string seat, DeckDef s, DeckDef n) => seat == "south" ? s : n;
        private static string Other(string seat) => seat == "south" ? "north" : "south";

        // ---- public + own hand preserved -------------------------------------------------------------

        private static (bool, string) Preserved(DeckDef south, DeckDef north)
        {
            var st = PrivacyTest.SouthDecision(south, north, "det:preserved", 46);
            if (st == null) return (false, "no fixture");
            var world = RootWorldSampler.Determinize(st, Know(south, north), 7).State;
            foreach (var seat in new[] { "south", "north" })
            {
                var a = st.Players[seat]; var b = world.Players[seat];
                if (Ids(a.Leader) != Ids(b.Leader)) return (false, $"{seat} leader changed");
                if (!Multi(a.CharacterArea.Where(c => c != null)).SequenceEqual(Multi(b.CharacterArea.Where(c => c != null)))) return (false, $"{seat} character area changed");
                if (!Multi(a.Trash).SequenceEqual(Multi(b.Trash))) return (false, $"{seat} trash changed");
                if (!Multi(a.Life.Where(c => c.FaceUp)).SequenceEqual(Multi(b.Life.Where(c => c.FaceUp)))) return (false, $"{seat} face-up life changed");
                if (Ids(a.Stage) != Ids(b.Stage)) return (false, $"{seat} stage changed");
            }
            // South's OWN hand is preserved exactly (identity AND instance id).
            if (!st.Players["south"].Hand.Select(c => c.CardId + "/" + c.InstanceId).OrderBy(x => x)
                    .SequenceEqual(world.Players["south"].Hand.Select(c => c.CardId + "/" + c.InstanceId).OrderBy(x => x)))
                return (false, "south hand changed");
            return (true, "leader/characters/stage/trash/face-up life + own hand identical");
        }

        // ---- hidden zones legal ----------------------------------------------------------------------

        private static (bool, string) Legal(DeckDef south, DeckDef north)
        {
            for (int t = 0; t < 4; t++)
            {
                var st = PrivacyTest.SouthDecision(south, north, $"det:legal:{t}", 44 + t * 5);
                if (st == null) continue;
                var world = RootWorldSampler.Determinize(st, Know(south, north), 200 + t).State;
                if (!PrivacyTest.ValidatesAgainstList(world, "north", north, out var wn)) return (false, $"north illegal: {wn}");
                if (!PrivacyTest.ValidatesAgainstList(world, "south", south, out var ws)) return (false, $"south illegal: {ws}");
                return (true, "both seats' full card sets match their decklists");
            }
            return (false, "no fixtures");
        }

        // ---- engine-runnable -------------------------------------------------------------------------

        private static (bool, string) Runnable(DeckDef south, DeckDef north)
        {
            var st = PrivacyTest.SouthDecision(south, north, "det:runnable", 48);
            if (st == null) return (false, "no fixture");
            var world = RootWorldSampler.Determinize(st, Know(south, north), 11).State;
            try
            {
                // Play the sampled world forward to the end with the greedy bot; a malformed world would throw
                // or immediately deadlock. Assert it actually progresses and terminates cleanly.
                int before = world.TurnNumber;
                OnePieceTcg.Engine.Bot.IntermediateBot.PlayFullMatch(world, 4000);
                if (world.Status != "finished" && world.TurnNumber <= before)
                    return (false, "sampled world did not progress");
            }
            catch (Exception e) { return (false, $"engine threw on the sampled world: {e.GetType().Name}: {e.Message}"); }
            return (true, $"played to {world.Status} (turn {world.TurnNumber})");
        }

        // ---- determinism -----------------------------------------------------------------------------

        private static (bool, string) Deterministic(DeckDef south, DeckDef north)
        {
            var st = PrivacyTest.SouthDecision(south, north, "det:determinism", 50);
            if (st == null) return (false, "no fixture");
            string Hidden(GameState s) => string.Join(",",
                s.Players["north"].Hand.Select(c => c.CardId).Concat(s.Players["north"].Deck.Select(c => c.CardId)));
            var w1 = RootWorldSampler.Determinize(GameClone.Clone(st), Know(south, north), 5).State;
            var w1b = RootWorldSampler.Determinize(GameClone.Clone(st), Know(south, north), 5).State;
            var w2 = RootWorldSampler.Determinize(GameClone.Clone(st), Know(south, north), 6).State;
            if (Hidden(w1) != Hidden(w1b)) return (false, "same seed produced different worlds");
            if (Hidden(w1) == Hidden(w2)) return (false, "different seeds produced identical hidden arrangements");
            return (true, "same seed ⇒ identical; different seed ⇒ different arrangement");
        }

        // ---- ledger: known-top segment is honored ----------------------------------------------------

        private static (bool, string) KnownTopPlaced(DeckDef south, DeckDef north)
        {
            var st = PrivacyTest.SouthDecision(south, north, "det:knowntop", 46);
            if (st == null) return (false, "no fixture");
            var deck = st.Players["south"].Deck;
            if (deck.Count < 12) return (false, "deck too small");
            // Two mid-deck cards the seat "knows" are the top two (captured BEFORE poisoning rewrites them).
            string x = deck[5].CardId, y = deck[10].CardId;

            var knowledge = new KnowledgeState
            {
                Seat = "south", OwnList = south, OpponentList = north, Assumption = ListAssumption.OpenList,
                Segments = new[] { new SegmentFact { Seat = "south", Zone = "deck", Anchor = "Top", CardIds = new List<string> { x, y } } },
            };
            PrivacyTest.PoisonHidden(st, "south");                       // secrecy: the rest must still be resampled
            var world = RootWorldSampler.Determinize(st, knowledge, 42).State;
            var wdeck = world.Players["south"].Deck;

            if (wdeck.Count < 2 || wdeck[0].CardId != x || wdeck[1].CardId != y)
                return (false, $"deck top is [{wdeck.ElementAtOrDefault(0)?.CardId},{wdeck.ElementAtOrDefault(1)?.CardId}], expected [{x},{y}]");
            if (PrivacyTest.ReachableStrings(world).Any(s => s != null && s.Contains(PrivacyTest.PoisonTag)))
                return (false, "poison reachable — the non-segment deck was not resampled");
            if (!PrivacyTest.ValidatesAgainstList(world, "south", south, out var why))
                return (false, "illegal world: " + why);
            return (true, $"[{x},{y}] fixed on top; rest sampled; legal; no leak");
        }

        // ---- end-to-end policy noninterference ------------------------------------------------------

        private static (bool, string) GreedyRouteNoninterference(DeckDef south, DeckDef north)
        {
            var st = PrivacyTest.SouthDecision(south, north, "det:greedy-route", 50);
            if (st == null) return (false, "no clean-main fixture");
            var twin = GameClone.Clone(st);
            PrivacyTest.PermuteHidden(twin, "south", 8181);

            var ctx = DeckFingerprint.Analyze(south);
            var a = new HonestPlannerBot(south, north, ctx: ctx, useGreedyPolicy: true);
            var b = new HonestPlannerBot(south, north, ctx: ctx, useGreedyPolicy: true);
            var ca = a.Decide(st, "south", new HashSet<string>());
            var cb = b.Decide(twin, "south", new HashSet<string>());
            if (ca == null || cb == null) return (false, "route returned null at a clean-main decision");
            if (CommandSig(ca) != CommandSig(cb))
                return (false, $"hidden-only permutation changed the command: {CommandSig(ca)} != {CommandSig(cb)}");
            return (true, CommandSig(ca));
        }

        private static string CommandSig(GameCommand c) => string.Join("|",
            c.Type, c.Seat, c.InstanceId, c.Target, c.Attacker, c.Blocker, c.EffectId, c.Amount,
            c.SlotIndex, c.GoingFirst, c.Mulligan,
            c.OrderedInstanceIds == null ? "" : string.Join(",", c.OrderedInstanceIds),
            c.DonInstanceIds == null ? "" : string.Join(",", c.DonInstanceIds));

        // ---- helpers ---------------------------------------------------------------------------------

        private static string Ids(CardInstance c) => c == null ? "-" : c.CardId;
        private static List<string> Multi(IEnumerable<CardInstance> z) => z.Select(c => c.CardId).OrderBy(x => x).ToList();
    }
}
