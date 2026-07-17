using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// A persistent record of legally-ACQUIRED knowledge a <see cref="GameState"/> cannot represent — the
    /// FIRST slice: the cards a seat knows sit on TOP of its OWN deck after arranging them there in a deck
    /// look. Owned by ONE seat; it gains only from THAT seat's legal to-top arrangement and never records the
    /// opponent's. The determinizer consumes this to PLACE the known cards instead of reshuffling them (the
    /// rayleigh problem).
    ///
    /// HONESTY DISCIPLINE: knowledge is GAINED only from the owner's own to-top confirm (cards it saw). It is
    /// verified against the real deck after every command to INVALIDATE, keyed on INSTANCE ID, not CardId — so
    /// a shuffle that coincidentally leaves the same CardId on top does NOT launder the knowledge back (the
    /// instance moved). A draw pops the drawn instances; any reorder clears. So it can lose knowledge but
    /// never assert knowledge the seat did not legally hold.
    ///
    /// ⚠ SCOPE: own-deck top only. Bottom segments, opponent knowledge, hand/Life facts, grouped ambiguity
    /// constraints are later slices (design §2/§4). A NEW to-top look replaces the prior segment (safe, may
    /// forget a still-valid deeper segment).
    /// </summary>
    public sealed class KnowledgeLedger
    {
        // Ordered (InstanceId, CardId) the owner knows are on top of its own deck (index 0 = topmost).
        private readonly List<(string inst, string card)> _top = new List<(string, string)>();

        /// <summary>The owner's known top-of-own-deck CardIds (index 0 = topmost), or empty. <paramref name="seat"/>
        /// must be the owner; returns empty for anyone else (this ledger never holds another seat's knowledge).</summary>
        public IReadOnlyList<string> KnownDeckTop(string seat)
            => _owner != null && seat == _owner ? _top.Select(t => t.card).ToList() : (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>The legal deck-top knowledge as observation <see cref="SegmentFact"/>s (empty if none or
        /// for a non-owner seat). The determinizer places these at Top(0) before sampling the rest.</summary>
        public IReadOnlyList<SegmentFact> Segments(string seat)
        {
            var top = KnownDeckTop(seat);
            return top.Count == 0
                ? Array.Empty<SegmentFact>()
                : new[] { new SegmentFact { Seat = seat, Zone = "deck", Anchor = "Top", CardIds = top.ToList() } };
        }

        private string _owner;

        /// <summary>Update from ONE applied command, given the states BEFORE and AFTER it, for the ledger's
        /// OWNER seat. GAIN fires only for the owner's own to-top deck-look confirm; RECONCILE re-checks the
        /// owner's real deck top by INSTANCE ID (pops drawn cards, clears on any reorder).</summary>
        public void Observe(GameCommand cmd, GameState before, GameState after, string ownerSeat)
        {
            if (cmd == null || after == null || ownerSeat == null) return;
            _owner = ownerSeat;

            // GAIN — the owner just arranged EVERY looked card onto the top of its own deck (to-top ConfirmOrder
            // places them all on top, so the deck-count delta equals the number topped). Record their instance
            // ids + card ids straight off the resulting deck top. Scry is excluded (it can split top/bottom).
            if (cmd.Seat == ownerSeat && cmd.Type == "deckLookConfirmOrder"
                && before?.DeckLook != null && before.DeckLook.Seat == ownerSeat && before.DeckLook.ToTop
                && after.Players.TryGetValue(ownerSeat, out var pa) && before.Players.TryGetValue(ownerSeat, out var pb))
            {
                int placed = pa.Deck.Count - pb.Deck.Count;
                if (placed > 0)
                {
                    _top.Clear();
                    _top.AddRange(pa.Deck.Take(placed).Select(c => (c.InstanceId, c.CardId)));
                }
            }

            Reconcile(after, ownerSeat);
        }

        /// <summary>Keep the tracked segment consistent with the real deck, only ever shrinking it. Drawn
        /// instances (no longer in the deck) are popped from the front; if a still-in-deck known instance is no
        /// longer at the top, the order was disturbed (shuffle / effect) and the knowledge is gone.</summary>
        private void Reconcile(GameState after, string ownerSeat)
        {
            if (_top.Count == 0) return;
            if (!after.Players.TryGetValue(ownerSeat, out var p)) { _top.Clear(); return; }
            var deckInst = p.Deck.Select(c => c.InstanceId).ToList();

            while (_top.Count > 0)
            {
                if (IsPrefix(_top, deckInst)) return;                 // aligned — the known head still tops the deck
                if (!deckInst.Contains(_top[0].inst)) { _top.RemoveAt(0); continue; }   // drawn/left the deck → pop
                _top.Clear();                                         // still in deck but not on top ⇒ reordered → forget
                return;
            }
        }

        private static bool IsPrefix(List<(string inst, string card)> prefix, List<string> deckInst)
        {
            if (prefix.Count > deckInst.Count) return false;
            for (int i = 0; i < prefix.Count; i++) if (prefix[i].inst != deckInst[i]) return false;
            return true;
        }
    }
}
