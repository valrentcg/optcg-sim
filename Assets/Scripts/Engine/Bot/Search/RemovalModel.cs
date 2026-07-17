using System.Text.RegularExpressions;

namespace OnePieceTcg.Engine.Bot.Search
{
    /// <summary>
    /// WS-3 shared foundation: classify what KIND of removal/disruption an effect clause is, and judge whether
    /// a candidate target is worth spending it on. The engine's <see cref="GameEngine.IsValidEffectTarget"/>
    /// already enforces LEGALITY (cost/power thresholds, ownership, rested/active), so the candidate list can
    /// never contain a body the effect cannot touch. What the generic bot lacks is VALUE fidelity: it ranks
    /// every removal target by <c>cost*1000 + power</c> regardless of kind, so it will sink a temporary
    /// −power or a rest onto a body that has already acted and change nothing.
    ///
    /// The distinction that matters:
    ///   • PERMANENT removal (K.O. / bounce / trash) is worth the target's full investment — remove the biggest threat.
    ///   • SOFT disruption (−power / −cost / rest / freeze) is worth something only if the target is still a LIVE
    ///     threat this exchange — an unrested attacker or a Blocker. On a spent (rested, non-blocking) body it is wasted.
    ///
    /// WS-1 (DON-minus) and WS-2 (cost-manipulation) build on this classifier rather than re-deriving it.
    /// </summary>
    public enum RemovalKind { None, Ko, Bounce, Trash, PowerDown, CostDown, Rest, Freeze }

    public static class RemovalModel
    {
        /// <summary>Classify the PRIMARY action of one effect clause from its printed text. Callers resolve
        /// multi-clause effects one clause at a time (the engine splits on "Then,"), so this usually sees a
        /// single action. Precedence favours the hard removal a clause achieves over an incidental debuff.</summary>
        public static RemovalKind Classify(string text)
        {
            if (string.IsNullOrEmpty(text)) return RemovalKind.None;
            string t = StripTags(text).ToLowerInvariant();

            if (t.Contains("k.o.")) return RemovalKind.Ko;
            // "return … to the owner's/their hand" is removal-to-hand (a bounce). A DON-return COST returns DON
            // "to your DON!! deck", never to a hand, so the hand phrases alone distinguish the two — an earlier
            // blanket "don!!" exclusion here wrongly hid a bounce whose COST is DON-minus (ST03 Crocodile's
            // leader: "DON!! −4: Return up to 1 Character … to the owner's hand").
            if (t.Contains("return") && (t.Contains("owner's hand") || t.Contains("their hand") || t.Contains("to hand")))
                return RemovalKind.Bounce;
            // Trash a board Character (not "trash N cards from your hand", which is a cost).
            if (t.Contains("trash") && t.Contains("character")
                && !t.Contains("from your hand") && !t.Contains("from the top")) return RemovalKind.Trash;
            // "will not become active …" / "cannot attack" keep a body out of the fight without moving it.
            if (t.Contains("will not become active") || t.Contains("cannot attack")) return RemovalKind.Freeze;
            if (HasSignedNumber(t, "cost")) return RemovalKind.CostDown;
            if (HasSignedNumber(t, "power")) return RemovalKind.PowerDown;
            // "rest up to 1 of your opponent's Characters" (a rest aimed outward).
            if (Regex.IsMatch(t, @"\brest\b") && t.Contains("character") && t.Contains("opponent")) return RemovalKind.Rest;
            return RemovalKind.None;
        }

        /// <summary>Permanent board removal — worth the target's full investment.</summary>
        public static bool IsPermanent(RemovalKind k) => k == RemovalKind.Ko || k == RemovalKind.Bounce || k == RemovalKind.Trash;

        /// <summary>Soft disruption whose value depends on the target still mattering this exchange.</summary>
        public static bool IsSoft(RemovalKind k) =>
            k == RemovalKind.PowerDown || k == RemovalKind.CostDown || k == RemovalKind.Rest || k == RemovalKind.Freeze;

        /// <summary>Is this card still a live threat — an unrested body that can attack, or a Blocker that can
        /// wall — as opposed to a spent (rested, non-blocking) body that has already acted this turn?
        /// A Freeze ("will not become active") is the exception: its whole point is a RESTED target, which
        /// this treats as live because keeping it rested denies the opponent its next-turn attacker.</summary>
        public static bool IsLiveThreat(GameState state, CardInstance card, RemovalKind kind = RemovalKind.None)
        {
            if (card == null) return false;
            if (card.Zone == "leader") return true;               // the Leader always attacks
            if (kind == RemovalKind.Freeze) return card.Rested;   // freezing only matters on a body that would refresh
            if (!card.Rested) return true;                        // unrested → will attack
            return GameEngine.HasBlocker(state, card);            // rested but can still wall
        }

        /// <summary>WS-2: the magnitude of a "-N cost" reduction in a clause, or 0 if none.</summary>
        public static int CostDownAmount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var m = Regex.Match(text.ToLowerInvariant(), @"[-−–‑‒—]\s?(\d+)\s*cost");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        /// <summary>WS-2: the best EFFECTIVE-cost "K.O. … cost of N or less" threshold the seat could bring to
        /// bear (from a card in hand or an ability on the board/leader), or -1 if none. Only effective-cost
        /// K.O.s count — a "base cost" K.O. is unaffected by a −cost reduction, so it is excluded. This lets a
        /// −cost debuff be valued by the K.O. it unlocks: reduce a body under threshold N, then a KO-by-cost
        /// finisher removes it. Capability scan (does the deck HAVE the tool), not a full affordability proof.</summary>
        public static int BestEffectiveCostKoThreshold(GameState state, string seat)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return -1;
            var p = state.Players[seat];
            int best = -1;
            void Consider(CardInstance c)
            {
                if (c == null) return;
                var def = GameEngine.GetCard(c);
                string e = (def?.Effect ?? "").ToLowerInvariant();
                if (e.IndexOf("k.o.", System.StringComparison.Ordinal) < 0) return;
                if (e.IndexOf("opponent", System.StringComparison.Ordinal) < 0) return;
                foreach (Match m in Regex.Matches(e, @"(?<!base )cost of (\d+) or less"))
                    best = System.Math.Max(best, int.Parse(m.Groups[1].Value));
            }
            foreach (var c in p.Hand) Consider(c);
            foreach (var c in p.CharacterArea) Consider(c);
            Consider(p.Leader);
            return best;
        }

        // Strip leading [Timing]/[Once Per Turn]/[DON!! xN] tags and a leading "If …," so the phrase checks
        // see the same action text the resolver acts on. Mirrors the engine's own normalisation intent.
        private static string StripTags(string text)
        {
            string s = Regex.Replace(text, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
            s = Regex.Replace(s, @"^If [^,]+,\s*", "", RegexOptions.IgnoreCase);
            return s;
        }

        // A signed number attached to a stat word: "-5000 power" / "−1 cost" / "+2 cost" all count as
        // manipulating that stat. Only NEGATIVE deltas are disruption; a positive is a buff, not removal.
        private static bool HasSignedNumber(string lowerText, string stat) =>
            Regex.IsMatch(lowerText, @"[-−–‑‒—]\s?\d{1,5}\s+" + stat);
    }
}
