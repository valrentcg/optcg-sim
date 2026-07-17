using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>Deck-level context the value function needs to judge a position for THIS deck (blueprint
    /// §5.1 fingerprint / §4.1 win conditions). Built by DeckFingerprint; defaults to "generic".</summary>
    public sealed class DeckContext
    {
        public bool HasLowLifePayoff;   // deck has cards that reward being at low life (Yellow) → life-context flips
        public string AltWin = "";      // "" | "self-deckout" (Nami) | "opp-deckout" | "blocker-trigger" (Roger) | ...
        public double AvgCost;          // curve
        public int Blockers, Counters, Removal, Searchers; // rough archetype shape (per copy)
        public int Events, MainActivations, DonMinusEffects;
        // A leader whose engine is an [Activate: Main] ability (Enel, ST03 Crocodile, etc.) must be
        // routed through the guarded activation layer regardless of its coarse curve archetype.
        public bool RequiresMainActivation;
        // WS-1 DON-engine identity (detected from printed text, never a card-id/Enel profile).
        public int DonDeckCap = 10;      // DON!! deck size; a leader can shrink it ("consists of 6 cards" → Enel).
        public int DonBandThreshold = -1; // the "N or less DON!! cards on your field" the deck keys off; -1 = none.
        public int DonBandCards = 0;      // how many copies care about that band (strength of the preference).
        /// <summary>A deck built around DON manipulation: it pays DON!! −N costs and/or its leader re-ramps DON
        /// every turn (activation engine). For these, active DON!! is fuel to SPEND, not a hoard to protect —
        /// and if the DON-deck cap can never exceed the band (Enel: cap 6 ≤ band 6) the bonuses are always on,
        /// so the lever is spending DON on effects, not sitting on it.</summary>
        public bool IsDonEngine => DonMinusEffects >= 3 || (RequiresMainActivation && DonDeckCap < 10) || DonBandCards >= 3;
        /// <summary>Archetype-level: the deck runs DON!!-recovery cards (leader OR characters that set a DON!!
        /// active / pull one from the DON!! deck), so returning DON!! is part of its plan, not a dead cost.</summary>
        public bool HasDonRecovery;
        /// <summary>Copies that play/add a card FROM the trash. A deck built on this treats the trash as a
        /// resource — bodies there are discounted future plays, so losing one to the trash is only a partial
        /// loss and a stocked trash (plus the self-mill that fills it) is an asset, not card loss.</summary>
        public int TrashRecurCards;
        public bool IsTrashRecursion => TrashRecurCards >= 4;
        /// <summary>True when the deck can never exceed its band (cap ≤ threshold), so the band bonuses are
        /// permanently on and there is nothing to "manage" — only spend into.</summary>
        public bool DonBandAlwaysOn => DonBandThreshold >= 0 && DonDeckCap <= DonBandThreshold;
        /// <summary>"aggro" | "midrange" | "control" | "combo" — how this deck WANTS to win, decided once
        /// from the list at match start (both lists are known pre-match, Comp. Rules §8.1).
        /// ⚠ Derived from AvgCost/Blockers ONLY. Counters and Removal look like archetype signals and are
        /// not: measured across all 41 meta decks they span 18-42 and 18-51 with no separation, because
        /// `Counter > 0` is true of nearly every Character and the Removal scan matches the substring
        /// "trash" (as in "trash this card" / "from your trash"). Curve and blocker count are the only
        /// columns in this fingerprint that actually discriminate.</summary>
        public string Archetype = "midrange";
        public static readonly DeckContext Generic = new DeckContext();
    }

    /// <summary>
    /// The value function: how good is a position for <c>seat</c>, from 0..1-ish (higher = more likely to
    /// win). This is the "evaluate every legal line, keep the highest feasible win chance" scorer, and its
    /// ~two dozen WEIGHTS are the tunable knobs the tournament evolves (far more than the old 6). Terminal
    /// wins/losses are hard ±. Everything else is a weighted sum of context features that compare MY deck
    /// and board to the OPPONENT's — life race, board, resources, threats, mechanic outcomes (frozen /
    /// removed / DON denied), deck-out, and — critically — a POSITIVE finish-pressure term so it wants to
    /// close the game (the old eval had this negative, which is why it sat back).
    /// </summary>
    public static class ValueFunction
    {
        // Attribute names — index = weight index. Keep in sync with Attributes().
        public static readonly string[] Names =
        {
            "MyLife", "OppLife", "LifeDanger", "LowLifeEnabler",
            "MyBoardPow", "OppBoardPow", "MyChars", "OppChars", "MyBlockers", "OppBlockers",
            "OppRested", "MyActiveAtk", "MyHand", "OppHand", "MyCounterReserve",
            "MyActiveDon", "OppActiveDon", "MyAttachedDon", "MyDeck", "OppDeck", "MyTrash",
            "FinishPressure", "LethalProximity", "AltWinProgress",
            // MyDoubleAttackers — ready attackers with [Double Attack], which deals TWO Life on a leader
            // hit (GameEngine.HasDoubleAttack / ResolveAttack). Rush, Double Attack and Banish appear
            // NOWHERE in this eval, so a bot whose entire job is finding lethal cannot see that one of its
            // attackers threatens double damage.
            // This is ADDED ALONGSIDE LethalProximity, never instead of it — that distinction is measured,
            // not stylistic. Replacing LethalProximity's power ratio with a damage count cost -9.2pp: the
            // ratio encodes "can my attacks get THROUGH their blockers", which a damage count discards.
            // Adding info the eval LACKS works (--leader-pow +10pp, granted-Blocker +1.7pp); recombining
            // what it already has fails (UsableDon -20pp); replacing what works fails (--dbl-attack -9.2pp).
            "MyDoubleAttackers",
        };
        public static int N => Names.Length;

        /// <summary>Typical magnitude of each RAW feature, measured over 448 real decision states with
        /// `value-audit`. <see cref="Attributes"/> divides by these, so every feature arrives at the
        /// weighted sum with mean magnitude ~1 and a weight therefore MEANS "this feature's average
        /// contribution to the score" — directly comparable across features.
        ///
        /// This exists for the MUTATION OPERATOR, not for the eval. Raw feature ranges differ ~100x
        /// (myPow/1000 spans 0..39 while life spans 0..6), and PlannerTournament.Perturb adds the same
        /// ±0.6 to every gene — so one mutation step moved the score by ±9.7 through MyBoardPow but only
        /// ±0.08 through MyBlockers, a 120x spread. Evolution was not exploring 24 knobs; it was tuning
        /// the two board terms and jiggling noise on the rest (which is why MyCounterReserve→0.009 was
        /// findable and little else ever moved). After normalising, ±0.6 perturbs every feature's
        /// contribution equally.
        ///
        /// A floor of 0.25 keeps rarely-firing features (MyBlockers averages 0.14) from being amplified
        /// into pure noise genes. AltWinProgress measures EXACTLY 0.00 — it is dead (see AltWinProgress)
        /// — so it gets 1.0 rather than a division by zero.
        /// NOTE: these are a REPARAMETERISATION, not a behaviour change. Scale[i] is folded back out in
        /// DefaultWeights, so w[i]*a[i] is unchanged and the bot plays identically (verified: the duel
        /// reproduces the same win rate bit-for-bit). Any positive constants would be valid; these are
        /// chosen so that weights are interpretable and mutation is uniform.</summary>
        public static readonly double[] Scale =
        {
            /*MyLife*/ 2.40, /*OppLife*/ 3.51, /*LifeDanger*/ 1.17, /*LowLifeEnabler*/ 0.41,
            /*MyBoardPow*/ 16.19, /*OppBoardPow*/ 15.08, /*MyChars*/ 2.72, /*OppChars*/ 3.43,
            /*MyBlockers*/ 0.25, /*OppBlockers*/ 0.88, /*OppRested*/ 1.25, /*MyActiveAtk*/ 2.43,
            /*MyHand*/ 5.31, /*OppHand*/ 3.40, /*MyCounterReserve*/ 6.81,
            /*MyActiveDon*/ 1.84, /*OppActiveDon*/ 3.06, /*MyAttachedDon*/ 0.56,
            /*MyDeck*/ 3.51, /*OppDeck*/ 3.46, /*MyTrash*/ 0.89,
            /*FinishPressure*/ 1.67, /*LethalProximity*/ 2.14, /*AltWinProgress*/ 1.00,
            /*MyDoubleAttackers*/ 0.50,   // usually 0-1 on board; small so the weight reads as a real contribution
        };

        /// <summary>Raw (un-normalised) default weights — the historical hand-tuned values. Kept separate
        /// so the normalisation above is provably behaviour-preserving: DefaultWeights folds Scale back in.</summary>
        private static readonly double[] RawDefaults =
        {
            /*MyLife*/ 0.8, /*OppLife*/ -1.6, /*LifeDanger*/ -1.2, /*LowLifeEnabler*/ 0.0,
            /*MyBoardPow*/ 0.9, /*OppBoardPow*/ -0.7, /*MyChars*/ 0.5, /*OppChars*/ -0.5,
            /*MyBlockers*/ 0.4, /*OppBlockers*/ -0.4, /*OppRested*/ 0.25, /*MyActiveAtk*/ 0.3,
            /*MyHand*/ 0.4, /*OppHand*/ -0.3, /*MyCounterReserve*/ 0.3,
            /*MyActiveDon*/ 0.1, /*OppActiveDon*/ -0.25, /*MyAttachedDon*/ 0.15,
            /*MyDeck*/ 0.02, /*OppDeck*/ -0.02, /*MyTrash*/ 0.0,
            /*FinishPressure*/ 1.5, /*LethalProximity*/ 1.0, /*AltWinProgress*/ 1.2,
            // 2.0 raw × Scale 0.5 = a NORMALISED weight of 1.0, the value measured at 59.2% (n=120) vs
            // 56.7% without it. +3.0 gave bit-identical games, so the effect saturates the moment the
            // feature exists at all — [Double Attack] attackers are rare, so this acts as a tie-breaker and
            // any positive weight flips the same decisions.
            /*MyDoubleAttackers*/ 2.0,
        };

        /// <summary>Defaults in NORMALISED space: w[i]*Scale[i], so w[i]*(a[i]/Scale[i]) == the original
        /// product and the bot is unchanged. Each value now reads directly as that feature's mean
        /// contribution to the score — e.g. MyBoardPow ≈ 14.6 vs OppLife ≈ 5.6 makes it plain, in the
        /// source, that board power outweighs the opponent's entire life bar. The tournament perturbs
        /// these across the population.</summary>
        public static double[] DefaultWeights()
        {
            var w = new double[RawDefaults.Length];
            for (int i = 0; i < w.Length; i++) w[i] = RawDefaults[i] * Scale[i];
            return w;
        }

        /// <summary>--arch-weights: apply per-archetype RULES OF ENGAGEMENT (see <see cref="ApplyArchetype"/>).
        /// Off by default so every existing measurement is reproducible on this binary.</summary>
        public static bool ArchetypeWeights = false;

        // Weight indices (order matches Names/Scale/RawDefaults).
        private const int I_OppLife = 1, I_MyBoardPow = 4, I_OppBoardPow = 5,
                          I_MyActiveDon = 15, I_FinishPressure = 21, I_LethalProximity = 22;

        /// <summary>WS-1 A/B toggle. When true (default), DON-engine decks (DON-minus payers / re-ramp
        /// activation leaders / low-DON-band decks) are scored with DON as FUEL, not a hoard: the standing
        /// reward for parked active DON!! is removed so spending it on the engine's effects is not penalised,
        /// and decks that can cross a DON band are nudged to sit in it. Off = the generic DON scoring, so the
        /// change can be A/B'd on identical seeds. Deck-context driven — no card-id logic.</summary>
        public static bool DonEngineAware = true;

        /// <summary>WS-trash A/B toggle. When true (default), a trash-recursion deck values the bodies in its
        /// own trash as recur fuel — so a stocked trash is an asset, self-mill that fills it is not card loss,
        /// and losing a body to the trash is only a PARTIAL loss (it can come back), which frees the bot to
        /// trade and chump-block more willingly. Deck-context driven, no card ids.
        /// DEFAULT OFF: measured 0/24 inert on Yamato-Lucy — like every passive eval-term this session, it
        /// changes no decisions (the engine already executes; the matchup loss is real, not a bot gap). The
        /// IsTrashRecursion DETECTION is retained as infrastructure.</summary>
        public static bool TrashRecursionAware = false;

        /// <summary>Bend a weight vector toward how THIS deck actually wins. Measured 2026-07-15: in MIRROR
        /// matches (same deck both sides, so deck strength contributes nothing) the planner wins 66.7% with
        /// control decks and 26.2% with aggro decks — z≈3.8, p≈0.0001. One vector cannot serve both, and the
        /// vector we have is a control player's: board outweighs winning 25.1 to 12.2 in mean contribution,
        /// so the bot grinds board with every deck it is handed. That is correct for control and fatal for
        /// aggro, which must convert tempo into Life damage before the grind starts.
        ///
        /// This is why weight EVOLUTION never found anything: a single vector is a forced compromise between
        /// two opposite strategies, so every perturbation helps one archetype and hurts the other and reads
        /// as "worse". The "sharp local optimum" in the notes is that compromise, not a peak.
        ///
        /// MULTIPLIERS, not replacements — so this composes with any evolved genome, and so the feature
        /// SEMANTICS the existing weights were tuned against are preserved (the notes' own warning: fixing a
        /// feature to match its name broke the role it played). Identity when the flag is off.</summary>
        public static double[] ApplyArchetype(double[] w, string archetype)
        {
            if (!ArchetypeWeights || w == null) return w;
            var r = (double[])w.Clone();
            switch (archetype)
            {
                case "aggro":
                    // Halve the board terms, double the race terms: flips mean contribution from
                    // board 25.1 / winning 12.2 to roughly board 12.6 / winning 22.5.
                    r[I_MyBoardPow] *= 0.5; r[I_OppBoardPow] *= 0.5;
                    r[I_OppLife] *= 2.0; r[I_FinishPressure] *= 2.0; r[I_LethalProximity] *= 2.0;
                    break;
                // control/combo/midrange deliberately UNCHANGED for now: control already wins 66.7% with the
                // current vector, so it is the one archetype the defaults suit. Touching it would be
                // changing two things at once.
            }
            return r;
        }

        public const double WIN = 1000, LOSS = -1000;

        public static double Score(GameState s, string seat, double[] w, DeckContext ctx = null)
        {
            ctx ??= DeckContext.Generic;
            if (s.Status == "finished")
            {
                for (int i = s.EventLog.Count - 1; i >= 0; i--)
                {
                    var m = s.EventLog[i].Message;
                    if (string.IsNullOrEmpty(m) || !m.Contains("wins")) continue;
                    if (m.StartsWith("South")) return seat == "south" ? WIN : LOSS;
                    if (m.StartsWith("North")) return seat == "north" ? WIN : LOSS;
                }
                return 0;
            }
            var a = Attributes(s, seat, ctx);
            double v = 0;
            for (int i = 0; i < a.Length; i++) v += w[i] * a[i];
            if (DonEngineAware && ctx.IsDonEngine) v += DonEngineAdjust(s, seat, ctx, w, a);
            if (TrashRecursionAware && ctx.IsTrashRecursion) v += TrashRecursionValue(s, seat);
            return v;
        }

        /// <summary>Recur fuel in the trash: each own-Character in the trash is a discounted future play for a
        /// recursion deck, so it offsets part of the cost of that body having died and rewards building the
        /// trash. Capped so it never dominates board/life.</summary>
        private static double TrashRecursionValue(GameState s, string seat)
        {
            var me = s.Players[seat];
            int fuel = me.Trash.Count(c => c != null && CardData.GetCard(c.CardId)?.Type == "character");
            return 0.25 * Math.Min(fuel, 8);
        }

        /// <summary>WS-1 DON-engine scoring, applied only to decks whose identity is DON manipulation.
        /// (1) Cancel the generic reward for parked active DON!! — for these decks it is fuel the leader
        /// refills, so hoarding it is not an advantage and spending it on DON!! −N effects must not read as a
        /// loss. (2) For decks that CAN cross a low-DON band, nudge toward sitting in it (strength scales with
        /// how many copies care). Enel and any cap ≤ band deck are always in-band, so (2) is skipped there —
        /// only (1) applies. Returns a delta added on top of the weighted sum.</summary>
        private static double DonEngineAdjust(GameState s, string seat, DeckContext ctx, double[] w, double[] a)
        {
            double d = 0;
            // (1) neutralise the hoard bias (a[I_MyActiveDon] is already normalised; w folds Scale back in).
            d -= w[I_MyActiveDon] * a[I_MyActiveDon];
            // (2) band management, only where the band is actually reachable/exceedable.
            if (ctx.DonBandThreshold >= 0 && !ctx.DonBandAlwaysOn)
            {
                int fieldDon = s.Players[seat].CostArea.Count; // DON!! on your field (active + rested/given)
                double overshoot = Math.Max(0, fieldDon - ctx.DonBandThreshold);
                double bandFit = fieldDon <= ctx.DonBandThreshold ? 1.0 : -overshoot;
                d += 0.3 * Math.Min(1.0, ctx.DonBandCards / 8.0) * bandFit;
            }
            return d;
        }

        /// <summary>The feature vector for a position (from seat's view). Scaled so weights are ~O(1).</summary>
        public static double[] Attributes(GameState s, string seat, DeckContext ctx)
        {
            var me = s.Players[seat];
            var op = s.Players[GameEngine.OtherSeat(seat)];

            int myPow = BoardPow(s, me), opPow = BoardPow(s, op);
            int myChars = me.CharacterArea.Count(c => c != null), opChars = op.CharacterArea.Count(c => c != null);
            int myBlock = Blockers(s, me), opBlock = Blockers(s, op);
            int opRested = op.CharacterArea.Count(c => c != null && c.Rested);
            // "Active attacker" must mean CAN ACTUALLY ATTACK. !Rested is not that: a Character played this
            // turn is unrested but summoning-sick and cannot attack unless it has [Rush]
            // (GameEngine.IsSummoningSick, line 848 — the engine's own rule, which this eval was
            // re-deriving badly). The planner scores END-OF-TURN states, so it saw MyActiveAtk rise the
            // instant it played a body that cannot swing until next turn — over-valuing fresh Characters as
            // threats. Same defect class as granted-[Blocker]: ask the engine instead of guessing from
            // fields. It also makes [Rush] matter for the first time — Rush is the EXCEPTION to this rule
            // and appears nowhere else in the eval.
            int myActiveAtk = SummoningSickAware
                ? me.CharacterArea.Count(c => c != null && !c.Rested && !GameEngine.IsSummoningSick(s, c))
                : me.CharacterArea.Count(c => c != null && !c.Rested);
            int myDon = GameEngine.ActiveDonCount(me), opDon = GameEngine.ActiveDonCount(op);
            int myCounter = CounterReserveIsAffordable ? AffordableCounterPower(me, myDon)
                                                       : me.Hand.Sum(c => GameEngine.GetCounterPower(c));
            int myAttached = Attached(me);

            // finish pressure: POSITIVE as the opponent's life falls (the fix vs the old negative weight).
            double finish = Math.Max(0, 5 - op.Life.Count);
            // lethal proximity: my active attack power vs what it takes to kill (opp life + their blocker wall).
            double lethalProx = Math.Min(3.0, myPow / 1000.0 / Math.Max(1, op.Life.Count + opBlock));
            // NOTE: an earlier version REPLACED the ratio above with a Life-damage count when
            // LethalCountsDoubleAttack was set. That cost -9.2pp (47.5% vs 56.7%) and is reverted: the power
            // ratio encodes "can my attacks get THROUGH their blockers", which a bare damage count discards.
            // Double Attack is a real blind spot, but it belongs in its OWN feature (MyDoubleAttackers)
            // ALONGSIDE this one — add, never replace.
            // low-life enabler: for decks that want low life, being AT low life is good (flips the usual sign).
            double lowLifeEnabler = (ctx.HasLowLifePayoff && me.Life.Count <= 2) ? (3 - me.Life.Count) : 0;
            // alt-win progress (deck-aware).
            double altWin = AltWinProgress(s, seat, ctx);
            // danger: opponent's swing potential vs my life. Same summoning-sickness correction — a body the
            // opponent just played cannot swing at us this turn, so counting it inflates the threat.
            int opCanSwing = SummoningSickAware
                ? op.CharacterArea.Count(c => c != null && !c.Rested && !GameEngine.IsSummoningSick(s, c))
                : op.CharacterArea.Count(c => c != null && !c.Rested);
            double danger = Math.Max(0, opCanSwing + (opDon >= 1 ? 1 : 0) - me.Life.Count);

            var a = new double[]
            {
                me.Life.Count, op.Life.Count, danger, lowLifeEnabler,
                myPow / 1000.0, opPow / 1000.0, myChars, opChars, myBlock, opBlock,
                opRested, myActiveAtk, me.Hand.Count, op.Hand.Count, myCounter / 1000.0,
                myDon, opDon, myAttached, me.Deck.Count / 10.0, op.Deck.Count / 10.0, me.Trash.Count / 5.0,
                finish, lethalProx, altWin,
                DoubleAttackers(s, me),
            };
            // Normalise to mean magnitude ~1 so a weight means "average contribution" and one mutation
            // step is worth the same on every gene. DefaultWeights folds Scale back in, so this does not
            // change how the bot plays — see Scale.
            for (int i = 0; i < a.Length; i++) a[i] /= Scale[i];
            return a;
        }

        /// <summary>Count the LEADER's power in board power. Off = the historical behaviour (CharacterArea
        /// only), which is a blind spot: the leader is the primary attacker every single turn AND the main
        /// DON!! sink, so attaching DON to the leader — a core OPTCG play — barely registers in the eval.
        /// It also feeds LethalProximity via myPow, so lethal detection currently ignores the attacker that
        /// swings most often. Kept as a switch because it changes both the feature's meaning and its SCALE
        /// (see Scale[]), so it must be measured against the 45.0% knee anchor rather than assumed.</summary>
        /// DEFAULT ON as of 2026-07-15: measured +10pp vs the old behaviour, REPLICATED (51.7% and 52.5%
        /// at n=120). The flag is kept only so the old behaviour can be A/B'd.
        public static bool BoardPowIncludesLeader = true;

        /// <summary>Count only the counter power the bot can actually PAY for. The historical
        /// MyCounterReserve sums GetCounterPower over the whole hand, but the engine gates counter EVENTS
        /// on DON: CanCounterFromHand rejects an event when ActiveDonCount &lt; def.Cost. Counter values
        /// printed on characters/stages are free (you just discard them), so only events need the check.
        /// Without this the bot overstates its defensive reserve with events it cannot cast, and — more
        /// importantly — nothing in the eval LINKS DON to defence at all.
        /// (User domain correction, 2026-07-15: active DON is not wasted tempo; it is counter-event mana,
        /// and holding it up is a real line — plus a bluff against a human.)</summary>
        public static bool CounterReserveIsAffordable = false;

        /// <summary>DEAD FLAG — kept only so --dbl-attack does not error. The behaviour it enabled
        /// (replacing LethalProximity's power ratio with a Life-damage count) measured -9.2pp and is gone;
        /// Double Attack now lives in its own MyDoubleAttackers feature instead.</summary>
        public static bool LethalCountsDoubleAttack = false;

        /// <summary>Count only Characters that can ACTUALLY attack — exclude summoning-sick ones via the
        /// engine's own IsSummoningSick (played this turn AND no [Rush]). Affects MyActiveAtk and
        /// LifeDanger, both of which used a bare `!Rested` that silently counts bodies which cannot swing.
        /// Off by default so it is measured, not assumed.</summary>
        public static bool SummoningSickAware = false;

        /// <summary>Ready attackers carrying [Double Attack] — each threatens TWO Life on a leader hit
        /// rather than one. Uses the engine's own HasDoubleAttack (printed OR granted OR modifier), the same
        /// authority ResolveAttack uses, rather than re-reading the printed Keywords array — that mistake is
        /// exactly what made granted [Blocker] invisible to both the eval and the move generator.</summary>
        private static double DoubleAttackers(GameState s, PlayerState p)
        {
            double n = 0;
            if (p.Leader != null && !p.Leader.Rested && GameEngine.HasDoubleAttack(s, p.Leader)) n++;
            foreach (var c in p.CharacterArea)
                if (c != null && !c.Rested && GameEngine.HasDoubleAttack(s, c)) n++;
            return n;
        }

        /// <summary>Counter power reachable with the DON currently active. Mirrors the engine's own gate in
        /// CanCounterFromHand rather than re-deriving the rule.</summary>
        private static int AffordableCounterPower(PlayerState me, int activeDon)
        {
            int total = 0;
            foreach (var c in me.Hand)
            {
                int cp = GameEngine.GetCounterPower(c);
                if (cp <= 0) continue;
                var d = CardData.GetCard(c.CardId);
                if (d != null && d.Type == "event" && d.Cost > activeDon) continue;   // cannot pay for it
                total += cp;
            }
            return total;
        }

        private static int BoardPow(GameState s, PlayerState p)
        {
            int pow = p.CharacterArea.Where(c => c != null).Sum(c => GameEngine.GetPower(s, c));
            if (BoardPowIncludesLeader && p.Leader != null) pow += GameEngine.GetPower(s, p.Leader);
            return pow;
        }
        /// <summary>Blockers via the ENGINE's own check (printed OR granted OR modifier). The old version
        /// read the printed Keywords array only, so a Character granted [Blocker] by an effect was invisible
        /// to the eval — and LegalActions had the identical bug, meaning the bot could not even propose the
        /// block. Adding information the eval was BLIND to is the only change class that has paid
        /// (--leader-pow +10pp); recombining what it already had has not (UsableDon −20pp).</summary>
        private static int Blockers(GameState s, PlayerState p) =>
            p.CharacterArea.Count(c => c != null && GameEngine.HasBlocker(s, c));
        private static int Attached(PlayerState p)
        {
            int n = p.Leader?.AttachedDonIds?.Count ?? 0;
            foreach (var c in p.CharacterArea) if (c != null) n += c.AttachedDonIds?.Count ?? 0;
            return n;
        }

        private static double AltWinProgress(GameState s, string seat, DeckContext ctx)
        {
            var me = s.Players[seat]; var op = s.Players[GameEngine.OtherSeat(seat)];
            switch (ctx.AltWin)
            {
                case "self-deckout": return Math.Max(0, 10 - me.Deck.Count) / 2.0;   // Nami-style: my deck emptying is progress
                case "opp-deckout":  return Math.Max(0, 10 - op.Deck.Count) / 2.0;   // milling them out
                default: return 0;
            }
        }
    }
}
