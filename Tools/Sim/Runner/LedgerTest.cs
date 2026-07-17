using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// FALSIFICATION-FIRST tests for the <see cref="KnowledgeLedger"/> tracking core, on synthetic
    /// before/after states with STABLE instance ids across transitions. Each assertion is written to FAIL if
    /// the ledger gained knowledge it shouldn't, failed to invalidate on a shuffle (including a CardId
    /// COINCIDENCE), mis-tracked a multi-draw, or recorded the opponent's deck.
    /// </summary>
    public static class LedgerTest
    {
        public static int Run()
        {
            Console.WriteLine("=== knowledge ledger (deck-top tracking, instance-id) ===");
            int fail = 0;
            fail += Section("gain: a to-top deck-look confirm records the placed top cards", Gain());
            fail += Section("draw: drawing the top card pops the known segment by one", Draw());
            fail += Section("multi-draw: drawing two pops two (keeps the rest)", MultiDraw());
            fail += Section("shuffle: a reorder clears the segment", Shuffle());
            fail += Section("shuffle COINCIDENCE: same CardId back on top (diff instance) still CLEARS", Coincidence());
            fail += Section("unrelated command leaves a valid segment unchanged", Unchanged());
            fail += Section("a to-BOTTOM deck look gains NO top knowledge", NotTop());
            fail += Section("owner-scoped: the opponent's to-top look is NOT recorded", CrossSeat());
            fail += Section("Segments() surfaces the deck-top fact", SegmentsFact());
            Console.WriteLine();
            Console.WriteLine(fail == 0 ? "PASS - the ledger gains only legal own-deck top knowledge and self-invalidates by instance id." : $"FAIL - {fail} ledger check(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        private static int Section(string name, (bool ok, string detail) r)
        {
            Console.WriteLine($"  [{(r.ok ? "ok  " : "FAIL")}] {name}{(r.detail == null ? "" : " - " + r.detail)}");
            return r.ok ? 0 : 1;
        }

        // ---- scenarios -------------------------------------------------------------------------------

        private static (bool, string) Gain()
        {
            var led = GainLedger(out _);
            return Eq(led.KnownDeckTop("south"), new[] { "A", "B" }, out var d) ? (true, "known top = A,B") : (false, d);
        }

        private static (bool, string) Draw()
        {
            var led = GainLedger(out var after);
            led.Observe(Cmd("drawCard", "south"), after, St(("iB", "B"), ("iX", "X"), ("iY", "Y"), ("iZ", "Z")), "south");
            return Eq(led.KnownDeckTop("south"), new[] { "B" }, out var d) ? (true, "popped to B") : (false, d);
        }

        private static (bool, string) MultiDraw()
        {
            // known top A,B,C over rest; draw TWO (A,B) leaving C on top.
            var before = St(("iX", "X"));
            before.DeckLook = new DeckLookState { Seat = "south", ToTop = true, Step = "arrange",
                Cards = new List<CardInstance> { C("iA", "A"), C("iB", "B"), C("iC", "C") } };
            var after = St(("iA", "A"), ("iB", "B"), ("iC", "C"), ("iX", "X"));
            var led = new KnowledgeLedger();
            led.Observe(Cmd("deckLookConfirmOrder", "south"), before, after, "south");
            led.Observe(Cmd("drawCard", "south"), after, St(("iC", "C"), ("iX", "X")), "south");
            return Eq(led.KnownDeckTop("south"), new[] { "C" }, out var d) ? (true, "popped A,B → C") : (false, d);
        }

        private static (bool, string) Shuffle()
        {
            var led = GainLedger(out var after);
            // reorder: A,B no longer the prefix (A still in deck but not at top).
            led.Observe(Cmd("shuffle", "south"), after, St(("iX", "X"), ("iA", "A"), ("iB", "B"), ("iZ", "Z")), "south");
            return led.KnownDeckTop("south").Count == 0 ? (true, "cleared") : (false, $"kept {string.Join(",", led.KnownDeckTop("south"))}");
        }

        private static (bool, string) Coincidence()
        {
            var led = GainLedger(out var after);
            // A shuffle brings a DIFFERENT instance of the same CardId "A" to the top (iA2), while the real iA
            // moved down. CardId-based reconcile would relaunder [A]; instance-id reconcile must CLEAR.
            var shuffled = St(("iA2", "A"), ("iX", "X"), ("iA", "A"), ("iB", "B"), ("iY", "Y"), ("iZ", "Z"));
            led.Observe(Cmd("shuffle", "south"), after, shuffled, "south");
            return led.KnownDeckTop("south").Count == 0
                ? (true, "coincidental same-CardId top did NOT relaunder the segment")
                : (false, $"laundered false knowledge: {string.Join(",", led.KnownDeckTop("south"))}");
        }

        private static (bool, string) Unchanged()
        {
            var led = GainLedger(out var after);
            led.Observe(Cmd("attachDon", "south"), after, St(("iA", "A"), ("iB", "B"), ("iX", "X"), ("iY", "Y"), ("iZ", "Z")), "south");
            return Eq(led.KnownDeckTop("south"), new[] { "A", "B" }, out var d) ? (true, "still A,B") : (false, d);
        }

        private static (bool, string) NotTop()
        {
            var before = St(("iX", "X"), ("iY", "Y"), ("iZ", "Z"));
            before.DeckLook = new DeckLookState { Seat = "south", ToTop = false, Step = "arrange",
                Cards = new List<CardInstance> { C("iA", "A"), C("iB", "B") } };
            var after = St(("iX", "X"), ("iY", "Y"), ("iZ", "Z"), ("iA", "A"), ("iB", "B"));   // placed at BOTTOM
            var led = new KnowledgeLedger();
            led.Observe(Cmd("deckLookConfirmOrder", "south"), before, after, "south");
            return led.KnownDeckTop("south").Count == 0 ? (true, "no top knowledge from a bottom placement") : (false, "gained false top knowledge");
        }

        private static (bool, string) CrossSeat()
        {
            // NORTH does a to-top look; a SOUTH-owned ledger must not record it.
            var before = St(("iX", "X"));
            before.Players["north"] = new PlayerState { Seat = "north", Deck = new List<CardInstance> { C("niX", "NX") } };
            before.DeckLook = new DeckLookState { Seat = "north", ToTop = true, Step = "arrange",
                Cards = new List<CardInstance> { C("niA", "NA") } };
            var after = St(("iX", "X"));
            after.Players["north"] = new PlayerState { Seat = "north", Deck = new List<CardInstance> { C("niA", "NA"), C("niX", "NX") } };
            var led = new KnowledgeLedger();
            led.Observe(Cmd("deckLookConfirmOrder", "north"), before, after, "south");   // ledger owned by SOUTH
            return led.KnownDeckTop("south").Count == 0 && led.KnownDeckTop("north").Count == 0
                ? (true, "opponent's to-top look not recorded") : (false, "recorded the opponent's deck top");
        }

        private static (bool, string) SegmentsFact()
        {
            var before = St(("iX", "X"));
            before.DeckLook = new DeckLookState { Seat = "south", ToTop = true, Step = "arrange", Cards = new List<CardInstance> { C("iA", "A") } };
            var led = new KnowledgeLedger();
            led.Observe(Cmd("deckLookConfirmOrder", "south"), before, St(("iA", "A"), ("iX", "X")), "south");
            var segs = led.Segments("south");
            if (segs.Count != 1) return (false, $"expected 1 segment, got {segs.Count}");
            var s = segs[0];
            return s.Seat == "south" && s.Zone == "deck" && s.Anchor == "Top" && s.CardIds.SequenceEqual(new[] { "A" })
                ? (true, "deck Top(0) segment = [A]") : (false, "segment fields wrong");
        }

        // ---- builders --------------------------------------------------------------------------------

        // A ledger that has just gained known-top A,B over rest X,Y,Z; also returns the post-gain state.
        private static KnowledgeLedger GainLedger(out GameState after)
        {
            var before = St(("iX", "X"), ("iY", "Y"), ("iZ", "Z"));
            before.DeckLook = new DeckLookState { Seat = "south", ToTop = true, Step = "arrange",
                Cards = new List<CardInstance> { C("iA", "A"), C("iB", "B") } };
            after = St(("iA", "A"), ("iB", "B"), ("iX", "X"), ("iY", "Y"), ("iZ", "Z"));
            var led = new KnowledgeLedger();
            led.Observe(Cmd("deckLookConfirmOrder", "south"), before, after, "south");
            return led;
        }

        private static CardInstance C(string inst, string card) => new CardInstance { InstanceId = inst, CardId = card, Zone = "deck", Owner = "south" };

        private static GameState St(params (string inst, string card)[] deck)
        {
            var st = new GameState();
            st.Players["south"] = new PlayerState { Seat = "south", Deck = deck.Select(d => C(d.inst, d.card)).ToList() };
            st.Players["north"] = new PlayerState { Seat = "north" };
            return st;
        }

        private static GameCommand Cmd(string type, string seat) => new GameCommand { Type = type, Seat = seat };

        private static bool Eq(IReadOnlyList<string> actual, string[] expected, out string detail)
        {
            if (actual.SequenceEqual(expected)) { detail = null; return true; }
            detail = $"expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]";
            return false;
        }
    }
}
