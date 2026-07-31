using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Two cards reported from live play, driven through their REAL entry points.
    ///
    ///   OP11-030 Shirahoshi  [Activate: Main] You may rest 1 of your DON!! cards and this Character:
    ///                        Look at 5 cards from the top of your deck; reveal up to 1 {Neptunian} or
    ///                        {Fish-Man Island} type card and add it to your hand. Then, place the rest
    ///                        at the bottom of your deck in any order.
    ///                        Reported: "isn't letting me select cards ... on the search as valid targets".
    ///
    ///   OP16-073 Borsalino   [On Play] Add up to 1 DON!! card from your DON!! deck and set it as active,
    ///                        and add up to 1 additional DON!! card and rest it.
    ///                        [End of Your Turn] DON!! -2: Set this Character as active. Then, ...
    ///                        Reported: "Didn't give me the option to skip effect, had to resolve".
    ///
    /// Entry point matters: force-queuing the body would test RESOLUTION and never the OFFER, which is
    /// exactly where both reports live. So activateMain / playCard / endTurn are used, not QueueEffect.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- reportedcards
    /// </summary>
    public static class ReportedCardsProbe
    {
        private const string ShiraSearcher = "OP11-030";
        private const string Borsalino = "OP16-073";
        private const string Neptunian = "OP11-016";       // filled in at runtime if this isn't one
        private static int serial;

        public static int Run()
        {
            Console.WriteLine("=== Reported cards, driven at their real entry points ===");
            ProbeShirahoshi();
            Console.WriteLine();
            ProbeBorsalino();
            Console.WriteLine();
            ProbeMarineford();
            Console.WriteLine();
            ProbeNavyHeadquarters();
            Console.WriteLine();
            ProbeMamaragan();
            Console.WriteLine();
            ProbeDonMinusTargets();
            Console.WriteLine();
            MeasureCostPrefixClass();
            return 0;
        }

        /// <summary>A "DON!! −N" return must accept EVERY DON!! on the field — cost-area active,
        /// cost-area rested, and DON!! attached to a Leader/Character — per Comprehensive 8-3-1-6
        /// ("select a total number of DON!! cards equal to X from their Leader area, Character area,
        /// and cost area"). Checks the ENGINE accepts each category.</summary>
        private static void ProbeDonMinusTargets()
        {
            Console.WriteLine("-- DON!! −N payment: are active / rested / attached DON!! all returnable? --");
            foreach (var kind in new[] { "cost-area ACTIVE", "cost-area RESTED", "ATTACHED to a Character" })
            {
                var st = NewBoard();
                var s = st.Players["south"];
                var bors = Make(Borsalino, "south", "character");
                bors.PlayedOnTurn = 0; bors.Rested = true;
                s.CharacterArea[0] = bors;
                s.CostArea.Clear();
                // 4 DON!! so the engine offers a CHOICE (TotalFieldDon > cost) rather than auto-paying.
                for (int i = 0; i < 4; i++)
                    s.CostArea.Add(new DonInstance { InstanceId = $"south-dm-{kind[10]}-{serial++}", Rested = kind.Contains("RESTED") });
                string payId;
                if (kind.StartsWith("ATTACHED"))
                {
                    payId = s.CostArea[0].InstanceId;
                    bors.AttachedDonIds.Add(payId);          // now attached, not loose in the cost area
                    s.CostArea.RemoveAt(0);
                    for (int i = 0; i < 2; i++)
                        s.CostArea.Add(new DonInstance { InstanceId = $"south-dm-x-{serial++}", Rested = false });
                }
                else payId = s.CostArea[0].InstanceId;

                st.PendingEffects.Clear();
                st = GameEngine.ApplyCommand(st, new GameCommand { Type = "endTurn", Seat = "south" });
                var pe = st.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null || pe.DonPaymentRemaining <= 0)
                { Console.WriteLine($"   {kind,-26} no payment prompt raised (setup did not reach the cost)"); continue; }

                int before = pe.DonPaymentRemaining;
                st = GameEngine.ApplyCommand(st, new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = payId });
                var after = st.PendingEffects.FirstOrDefault(e => e != null && e.EffectId == pe.EffectId);
                int now = after?.DonPaymentRemaining ?? 0;
                bool accepted = after == null || now < before;
                Console.WriteLine($"   {kind,-26} accepted={accepted}  (remaining {before} -> {now})");
            }
        }

        /// <summary>OP15-078 Mamaragan [Counter]: "Up to 1 of your Leader or Character cards gains
        /// +1000 power during this battle. Then, if you have 6 or less DON!! cards on your field,
        /// draw 1 card." The +1000 recipient is CHOSEN — it must not be auto-granted to the blocker.
        /// Reported: "Automatically gave the +1k to Kizaru, didn't have me select, then still had me
        /// select a character before giving me the draw".</summary>
        private static void ProbeMamaragan()
        {
            Console.WriteLine("-- OP15-078 Mamaragan [Counter]: who gets the +1000? --");
            var st = NewBoard();
            st.ActiveSeat = "north";
            var s = st.Players["south"];
            var n = st.Players["north"];

            var kizaru = Make("OP16-073", "south", "character");   // "Kizaru" = Borsalino, the blocker
            var kuzan = Make("OP16-063", "south", "character");
            foreach (var c in new[] { kizaru, kuzan }) { c.PlayedOnTurn = 0; c.Rested = false; }
            s.CharacterArea[0] = kizaru; s.CharacterArea[1] = kuzan;
            s.Hand.Clear();
            var mama = Make("OP15-078", "south", "hand");
            s.Hand.Add(mama);
            // "6 or less DON!! on your field" must be TRUE so the draw rider is live.
            while (s.CostArea.Count > 4) s.CostArea.RemoveAt(s.CostArea.Count - 1);

            var atk = Make("OP16-056", "north", "character");
            atk.PlayedOnTurn = 0; atk.Rested = false;
            n.CharacterArea[0] = atk;

            st = GameEngine.ApplyCommand(st, new GameCommand
            { Type = "declareAttack", Seat = "north", Attacker = atk.InstanceId, Target = s.Leader.InstanceId });
            st = GameEngine.ApplyCommand(st, new GameCommand
            { Type = "blockAttack", Seat = "south", Blocker = kizaru.InstanceId });
            int handBefore = st.Players["south"].Hand.Count;
            st = GameEngine.ApplyCommand(st, new GameCommand
            { Type = "counterWithCard", Seat = "south", InstanceId = mama.InstanceId });

            Console.WriteLine($"   counterPower auto-applied to the blocker: {st.Battle?.CounterPower}"
                            + "   (should be 0 — the recipient is CHOSEN)");
            var pes = st.PendingEffects.Where(e => e != null && e.Seat == "south").ToList();
            Console.WriteLine($"   pending effects queued for south: {pes.Count}");
            foreach (var pe in pes)
            {
                var lit = Everything(st, "south").Where(c => GameEngine.IsValidEffectTarget(st, pe, c)).ToList();
                Console.WriteLine($"     [pe] zone={pe.TargetZone} optional={pe.Optional} litTargets={lit.Count} text={Trim(pe.Text)}");
                foreach (var c in lit)
                    Console.WriteLine($"          lit: {c.CardId} ({CardData.GetCard(c.CardId)?.Name}) in {c.Zone}");
            }
            Console.WriteLine($"   hand {handBefore} -> {st.Players["south"].Hand.Count} (the draw rider resolves after the pick)");

            // Does PICKING actually apply the buff for this clause shape?
            {
                var pick = st.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pick != null)
                {
                    var recip = st.Players["south"].Leader;
                    int before = GameEngine.GetPower(st, recip);
                    bool valid = GameEngine.IsValidEffectTarget(st, pick, recip);
                    var st2 = GameEngine.ApplyCommand(st, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pick.EffectId, Target = recip.InstanceId });
                    int after = GameEngine.GetPower(st2, st2.Players["south"].Leader);
                    Console.WriteLine($"   picking the Leader: valid={valid}  power {before} -> {after}  (want an increase)");
                    foreach (var e in st2.EventLog.TakeLast(3)) Console.WriteLine("        log: " + e.Message);
                }
            }

            // SKIPPING the +1000 must NOT cost the draw — the two clauses are independent.
            // Needs its OWN board: ApplyCommand mutates the state in place, so the pick above already
            // consumed this one's pending effect.
            {
                var stS = NewBoard();
                stS.ActiveSeat = "north";
                var sS = stS.Players["south"];
                var kz = Make("OP16-073", "south", "character");
                kz.PlayedOnTurn = 0; kz.Rested = false;
                sS.CharacterArea[0] = kz;
                sS.Hand.Clear();
                var mm = Make("OP15-078", "south", "hand");
                sS.Hand.Add(mm);
                while (sS.CostArea.Count > 4) sS.CostArea.RemoveAt(sS.CostArea.Count - 1);
                var atkS = Make("OP16-056", "north", "character");
                atkS.PlayedOnTurn = 0; atkS.Rested = false;
                stS.Players["north"].CharacterArea[0] = atkS;
                stS = GameEngine.ApplyCommand(stS, new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = atkS.InstanceId, Target = sS.Leader.InstanceId });
                stS = GameEngine.ApplyCommand(stS, new GameCommand
                { Type = "counterWithCard", Seat = "south", InstanceId = mm.InstanceId });
                var pickS = stS.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pickS != null)
                {
                    int handBeforeSkip = stS.Players["south"].Hand.Count;
                    stS = GameEngine.ApplyCommand(stS, new GameCommand
                    { Type = "passEffect", Seat = "south", EffectId = pickS.EffectId });
                    Console.WriteLine($"   skipping the +1000: hand {handBeforeSkip} -> {stS.Players["south"].Hand.Count}"
                                    + "  (want +1 — the draw is independent of the pick)");
                    foreach (var e in stS.EventLog.TakeLast(3)) Console.WriteLine("        log: " + e.Message);
                }
                else Console.WriteLine("   skipping the +1000: no pick was offered (setup issue)");
            }

            // FILTERED recipient: EB04-019 Eleclaw "[Counter] Up to 1 of your {Minks} type Leader or
            // Character cards gains +3000". Only the {Minks} Character may light — a non-Minks Leader
            // and non-Minks Characters must stay dark.
            Console.WriteLine("   -- filtered recipient ({Minks}): ONLY valid targets may light --");
            var stF = NewBoard();
            stF.ActiveSeat = "north";
            var sF = stF.Players["south"];
            var mink = Make("OP08-027", "south", "character");     // Tristan, {Minks}, vanilla
            var notMink1 = Make("OP16-063", "south", "character"); // Kuzan, Admiral/Navy
            var notMink2 = Make("OP16-065", "south", "character"); // Sakazuki, Admiral/Navy
            foreach (var c in new[] { mink, notMink1, notMink2 }) { c.PlayedOnTurn = 0; c.Rested = false; }
            sF.CharacterArea[0] = mink; sF.CharacterArea[1] = notMink1; sF.CharacterArea[2] = notMink2;
            sF.Hand.Clear();
            var ele = Make("EB04-019", "south", "hand");
            sF.Hand.Add(ele);
            var atkF = Make("OP16-056", "north", "character");
            atkF.PlayedOnTurn = 0; atkF.Rested = false;
            stF.Players["north"].CharacterArea[0] = atkF;
            stF = GameEngine.ApplyCommand(stF, new GameCommand
            { Type = "declareAttack", Seat = "north", Attacker = atkF.InstanceId, Target = sF.Leader.InstanceId });
            stF = GameEngine.ApplyCommand(stF, new GameCommand
            { Type = "counterWithCard", Seat = "south", InstanceId = ele.InstanceId });
            foreach (var pe in stF.PendingEffects.Where(e => e != null && e.Seat == "south"))
            {
                var lit = Everything(stF, "south").Where(c => GameEngine.IsValidEffectTarget(stF, pe, c)).ToList();
                Console.WriteLine($"      litTargets={lit.Count}  text={Trim(pe.Text)}");
                foreach (var c in lit)
                {
                    var cd = CardData.GetCard(c.CardId);
                    Console.WriteLine($"         lit: {c.CardId} ({cd?.Name}) feat={string.Join("/", cd?.Features ?? new System.Collections.Generic.List<string>())}");
                }
            }

            // A chosen recipient is ALWAYS offered, even with a single candidate: "up to 1" permits
            // choosing zero (Comprehensive 8-4-4-1), e.g. deliberately taking the damage.
            Console.WriteLine("   -- single candidate (Leader only): must STILL offer the choice --");
            var st1 = NewBoard();
            st1.ActiveSeat = "north";
            var s1 = st1.Players["south"];
            for (int i = 0; i < 5; i++) s1.CharacterArea[i] = null;
            s1.Hand.Clear();
            var mama1 = Make("OP15-078", "south", "hand");
            s1.Hand.Add(mama1);
            while (s1.CostArea.Count > 4) s1.CostArea.RemoveAt(s1.CostArea.Count - 1);
            var atk1 = Make("OP16-056", "north", "character");
            atk1.PlayedOnTurn = 0; atk1.Rested = false;
            st1.Players["north"].CharacterArea[0] = atk1;
            st1 = GameEngine.ApplyCommand(st1, new GameCommand
            { Type = "declareAttack", Seat = "north", Attacker = atk1.InstanceId, Target = s1.Leader.InstanceId });
            st1 = GameEngine.ApplyCommand(st1, new GameCommand
            { Type = "counterWithCard", Seat = "south", InstanceId = mama1.InstanceId });
            int pend1 = st1.PendingEffects.Count(e => e != null && e.Seat == "south");
            Console.WriteLine($"      pending prompts: {pend1} (want 1 — the choice is still the player's)"
                            + $"   leader power now: {GameEngine.GetPower(st1, st1.Players["south"].Leader)} (want 5000 until they pick)");
            foreach (var e in st1.EventLog.TakeLast(3)) Console.WriteLine("        log: " + e.Message);

            // Isolate WHY the draw rider lights the board: is the leading "If <cond>," being stripped?
            Console.WriteLine("   -- isolating the rider's targeting --");
            foreach (var probe in new[]
            {
                "If you have 6 or less DON!! cards on your field, draw 1 card.",
                "draw 1 card.",
                "Draw 1 card.",
            })
            {
                var synth = new PendingEffect
                {
                    EffectId = "probe", Seat = "south", Timing = "counter",
                    SourceInstanceId = mama.InstanceId, SourceCardId = "OP15-078",
                    Text = probe, TargetZone = EffectTargetZone.Play,
                };
                int lit = Everything(st, "south").Count(c => GameEngine.IsValidEffectTarget(st, synth, c));
                Console.WriteLine($"      litTargets={lit}  <- \"{probe}\"");
            }
            foreach (var e in st.EventLog.TakeLast(5)) Console.WriteLine("     log: " + e.Message);
        }

        private static System.Collections.Generic.IEnumerable<CardInstance> Everything(GameState st, string seat)
        {
            var p = st.Players[seat];
            if (p.Leader != null) yield return p.Leader;
            foreach (var c in p.CharacterArea) if (c != null) yield return c;
            foreach (var c in p.Hand) yield return c;
            if (p.Stage != null) yield return p.Stage;
        }

        /// <summary>OP16-038 "Let's Go!! To the Navy Headquarters!!":
        /// "[Main] You may rest 6 of your DON!! cards: If you have 5 {Impel Down} type Characters with
        /// different card names, set your Leader and all of your Characters as active."
        /// Rebuilt from the reported board (all 5 {Impel Down}, 5 distinct names, everything rested).</summary>
        private static void ProbeNavyHeadquarters()
        {
            Console.WriteLine("-- OP16-038 Let's Go!! To the Navy Headquarters!! --");
            var st = NewBoard();
            var s = st.Players["south"];
            s.Leader = Make("OP16-022", "south", "leader");
            s.Leader.Rested = true;
            string[] board = { "ST30-014", "OP16-026", "OP16-048", "OP16-042", "OP16-034" };
            for (int i = 0; i < board.Length; i++)
            {
                var c = Make(board[i], "south", "character");
                c.PlayedOnTurn = 0; c.Rested = true;
                s.CharacterArea[i] = c;
            }
            s.Hand.Clear();
            var ev = Make("OP16-038", "south", "hand");
            s.Hand.Add(ev);
            foreach (var don in s.CostArea) don.Rested = false;   // 10 active DON!!: 1 to play, 6 to rest

            st = GameEngine.ApplyCommand(st, new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = ev.InstanceId });
            for (int i = 0; i < 6 && st.PendingEffects.Count > 0; i++)
            {
                var pe = st.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null) break;
                st = GameEngine.ApplyCommand(st, new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            }

            var sp = st.Players["south"];
            int restedChars = sp.CharacterArea.Count(c => c != null && c.Rested);
            Console.WriteLine($"   DON!! after: {sp.CostArea.Count(d => !d.Rested)} active / {sp.CostArea.Count(d => d.Rested)} rested");
            Console.WriteLine($"   Leader rested : {sp.Leader?.Rested}   (should be False — the effect sets it active)");
            Console.WriteLine($"   Characters still rested: {restedChars} of 5   (should be 0)");
            foreach (var e in st.EventLog.TakeLast(6)) Console.WriteLine("     log: " + e.Message);

            // NEGATIVE CONTROL. Swap one {Impel Down} Character for a Navy one: 4 qualify, the
            // condition fails, and NOTHING may restand. Without this the pass above is also what a
            // handler that ignores the "If" entirely would print.
            Console.WriteLine("   -- negative control: only 4 {Impel Down} (condition must FAIL) --");
            var st2 = NewBoard();
            var s2 = st2.Players["south"];
            s2.Leader = Make("OP16-022", "south", "leader");
            s2.Leader.Rested = true;
            string[] board2 = { "ST30-014", "OP16-026", "OP16-048", "OP16-042", "OP16-065" }; // OP16-065 = Navy
            for (int i = 0; i < board2.Length; i++)
            {
                var c = Make(board2[i], "south", "character");
                c.PlayedOnTurn = 0; c.Rested = true;
                s2.CharacterArea[i] = c;
            }
            s2.Hand.Clear();
            var ev2 = Make("OP16-038", "south", "hand");
            s2.Hand.Add(ev2);
            foreach (var don in s2.CostArea) don.Rested = false;
            st2 = GameEngine.ApplyCommand(st2, new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = ev2.InstanceId });
            for (int i = 0; i < 6 && st2.PendingEffects.Count > 0; i++)
            {
                var pe2 = st2.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe2 == null) break;
                st2 = GameEngine.ApplyCommand(st2, new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe2.EffectId });
            }
            var sp2 = st2.Players["south"];
            Console.WriteLine($"      Leader rested: {sp2.Leader?.Rested} (should be True)   "
                            + $"Characters still rested: {sp2.CharacterArea.Count(c => c != null && c.Rested)} of 5 (should be 5)");
            foreach (var e in st2.EventLog.TakeLast(2)) Console.WriteLine("        log: " + e.Message);
        }

        /// <summary>OP16-078 Marineford: "[Activate: Main] DON!! −1, You may rest this Stage: Draw 1
        /// card and trash 1 card from your hand." While the DON!! −1 is unpaid, NOTHING in hand may
        /// be a valid target — the cost comes first.</summary>
        private static void ProbeMarineford()
        {
            Console.WriteLine("-- OP16-078 Marineford: activateMain, cost BEFORE body --");
            var st = NewBoard();
            var s = st.Players["south"];
            var stage = Make("OP16-078", "south", "stage");
            s.Stage = stage;
            s.Hand.Clear();
            for (int i = 0; i < 3; i++) s.Hand.Add(Make("ST01-005", "south", "hand"));

            st = GameEngine.ApplyCommand(st, new GameCommand
            { Type = "activateMain", Seat = "south", Target = stage.InstanceId });

            var pe = st.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            Console.WriteLine($"   pending: {(pe == null ? "none" : $"donRemaining={pe.DonPaymentRemaining} text={Trim(pe.Text)}")}");
            if (pe != null && pe.DonPaymentRemaining > 0)
            {
                int litHand = st.Players["south"].Hand.Count(h => GameEngine.IsValidEffectTarget(st, pe, h));
                Console.WriteLine($"   hand cards the glow would light while the cost is UNPAID: {litHand}"
                                + "   (must be 0 — the DON!! is what has to be clicked)");
            }
            else Console.WriteLine("   (cost was auto-paid — not enough DON!! on field to offer a choice)");
            foreach (var e in st.EventLog.TakeLast(4)) Console.WriteLine("     log: " + e.Message);
        }

        /// <summary>How BIG is the "a cost is declinable" class? Calls the engine's real
        /// IsOptionalEffectText by reflection (it is private) and reports every clause that the old
        /// wording-only rule called mandatory and the cost rule now calls optional.</summary>
        private static void MeasureCostPrefixClass()
        {
            Console.WriteLine("-- Class size: clauses that become declinable because they carry a COST --");
            var mi = typeof(GameEngine).GetMethod("IsOptionalEffectText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (mi == null) { Console.WriteLine("   (could not reflect IsOptionalEffectText)"); return; }
            Func<string, bool> isOptional = t => (bool)mi.Invoke(null, new object[] { t });

            // The OLD rule, verbatim, so the delta is exactly what this change moved.
            Func<string, bool> oldRule = text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return true;
                var t = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").TrimStart();
                return t.StartsWith("You may", StringComparison.OrdinalIgnoreCase)
                    || t.StartsWith("If ", StringComparison.OrdinalIgnoreCase)
                    || text.IndexOf("up to", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0;
            };

            int changed = 0, scanned = 0;
            var samples = new System.Collections.Generic.List<string>();
            foreach (var kv in CardData.Library)
            {
                var d = kv.Value;
                if (d == null || string.IsNullOrWhiteSpace(d.Effect)) continue;
                foreach (var line in d.Effect.Split('\n'))
                {
                    var clause = line.Trim();
                    if (clause.Length == 0) continue;
                    scanned++;
                    if (oldRule(clause) || !isOptional(clause)) continue;
                    changed++;
                    if (samples.Count < 12) samples.Add($"{kv.Key,-10} {Trim(clause)}");
                }
            }
            Console.WriteLine($"   clauses scanned: {scanned}");
            Console.WriteLine($"   newly declinable (carry a cost, no 'may'/'up to'): {changed}");
            foreach (var s in samples) Console.WriteLine("     " + s);
            if (changed > samples.Count) Console.WriteLine($"     ... and {changed - samples.Count} more");
        }

        // ---------------------------------------------------------------- Shirahoshi OP11-030
        private static void ProbeShirahoshi()
        {
            Console.WriteLine("-- OP11-030 Shirahoshi: activateMain --");
            var st = NewBoard();
            var s = st.Players["south"];

            var shira = Make(ShiraSearcher, "south", "character");
            shira.PlayedOnTurn = 0;
            s.CharacterArea[0] = shira;

            // Stock the top of the deck with a KNOWN mix so "which of the 5 are selectable" is meaningful:
            // one {Neptunian}, one {Fish-Man Island}, and three that match neither.
            string nep = FindByFeature("Neptunian");
            string fmi = FindByFeature("Fish-Man Island", excludeFeature: "Neptunian");
            Console.WriteLine($"   deck seeded with  Neptunian={nep ?? "NONE FOUND"}   Fish-Man Island={fmi ?? "NONE FOUND"}");
            s.Deck.Clear();
            foreach (var id in new[] { nep, fmi, "ST01-005", "ST01-005", "ST01-005" })
                if (id != null) s.Deck.Add(Make(id, "south", "deck"));

            int peBefore = st.PendingEffects.Count;
            st = GameEngine.ApplyCommand(st, new GameCommand
            { Type = "activateMain", Seat = "south", Target = shira.InstanceId });

            Console.WriteLine($"   DeckLook opened: {(st.DeckLook != null ? "YES" : "NO")}");
            Console.WriteLine($"   PendingEffects: {peBefore} -> {st.PendingEffects.Count}");
            foreach (var pe in st.PendingEffects.Where(e => e != null))
                Console.WriteLine($"     [pe] zone={pe.TargetZone} optional={pe.Optional} text={Trim(pe.Text)}");

            var dl = st.DeckLook;
            if (dl != null)
            {
                Console.WriteLine($"     [dl] step={dl.Step} feature='{dl.FeatureFilter}' named='{dl.NamedCardFilter}' "
                                + $"type='{dl.CardTypeFilter}' selectCount={dl.SelectCount} playMode={dl.PlayMode} "
                                + $"maxCost={dl.MaxCost} postLook='{Trim(dl.PostLookClause)}'");
                Console.WriteLine($"     [dl] cards in the look: {dl.Cards.Count}");
                foreach (var c in dl.Cards)
                {
                    var def = CardData.GetCard(c.CardId);
                    // Mirrors GameEngine.FeatureMatches (private): empty filter passes, else ANY '|' branch.
                    bool ok = string.IsNullOrEmpty(dl.FeatureFilter)
                              || dl.FeatureFilter.Split('|').Any(f => def != null && def.HasFeature(f.Trim()));
                    Console.WriteLine($"        {c.CardId,-10} {def?.Name,-22} feat='{string.Join("/", def?.Features ?? new System.Collections.Generic.List<string>())}'  featureMatch={ok}");
                }
            }
            foreach (var e in st.EventLog.TakeLast(6)) Console.WriteLine("     log: " + e.Message);
        }

        // ---------------------------------------------------------------- Borsalino OP16-073
        private static void ProbeBorsalino()
        {
            Console.WriteLine("-- OP16-073 Borsalino: playCard (On Play), then endTurn (End of Your Turn) --");
            var st = NewBoard();
            var s = st.Players["south"];
            s.Hand.Clear();
            var bors = Make(Borsalino, "south", "hand");
            s.Hand.Add(bors);

            int donBefore = s.CostArea.Count;
            st = GameEngine.ApplyCommand(st, new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = bors.InstanceId, SlotIndex = 0 });

            Console.WriteLine($"   [On Play] DON!! in cost area {donBefore} -> {st.Players["south"].CostArea.Count}");
            Console.WriteLine($"   PendingEffects after play: {st.PendingEffects.Count}");
            foreach (var pe in st.PendingEffects.Where(e => e != null))
                Console.WriteLine($"     [pe] optional={pe.Optional} zone={pe.TargetZone} text={Trim(pe.Text)}");
            foreach (var e in st.EventLog.TakeLast(5)) Console.WriteLine("     log: " + e.Message);

            // Now the [End of Your Turn] DON!! -2 clause: does ending the turn OFFER it, or force it?
            var onField = st.Players["south"].CharacterArea.FirstOrDefault(c => c != null && c.CardId == Borsalino);
            if (onField == null) { Console.WriteLine("   Borsalino is not on the field; end-of-turn probe skipped."); return; }
            onField.Rested = true;                       // so "Set this Character as active" is a real change
            st.PendingEffects.Clear();
            int donPre = st.Players["south"].CostArea.Count;

            st = GameEngine.ApplyCommand(st, new GameCommand { Type = "endTurn", Seat = "south" });
            Console.WriteLine($"   [End of Your Turn] DON!! {donPre} -> {st.Players["south"].CostArea.Count} "
                            + $"(a DROP of 2 with no prompt = the cost was forced)");
            Console.WriteLine($"   PendingEffects after endTurn: {st.PendingEffects.Count}");
            foreach (var pe in st.PendingEffects.Where(e => e != null))
                Console.WriteLine($"     [pe] optional={pe.Optional} seat={pe.Seat} text={Trim(pe.Text)}");
            var after = st.Players["south"].CharacterArea.FirstOrDefault(c => c != null && c.CardId == Borsalino);
            Console.WriteLine($"   Borsalino rested after endTurn: {after?.Rested}");
            foreach (var e in st.EventLog.TakeLast(6)) Console.WriteLine("     log: " + e.Message);

            // 8-3-1-3: an activation cost that CANNOT be paid is not paid at all, so the effect is
            // never activated. A non-optional prompt demanding 2 DON!! from a player who has fewer
            // would be unanswerable — a soft-lock, and exactly the shape stallsweep hunts.
            Console.WriteLine();
            Console.WriteLine("-- OP16-073 Borsalino: [End of Your Turn] with only 1 DON!! (cost UNPAYABLE) --");
            var st2 = NewBoard();
            var s2 = st2.Players["south"];
            var b2 = Make(Borsalino, "south", "character");
            b2.PlayedOnTurn = 0; b2.Rested = true;
            s2.CharacterArea[0] = b2;
            s2.CostArea.Clear();
            s2.CostArea.Add(new DonInstance { InstanceId = "south-rc-don-solo", Rested = false });
            st2.PendingEffects.Clear();

            st2 = GameEngine.ApplyCommand(st2, new GameCommand { Type = "endTurn", Seat = "south" });
            Console.WriteLine($"   DON!! available: 1 (cost needs 2)");
            Console.WriteLine($"   PendingEffects after endTurn: {st2.PendingEffects.Count} "
                            + "(a non-optional pending effect here = unanswerable prompt)");
            foreach (var pe in st2.PendingEffects.Where(e => e != null))
                Console.WriteLine($"     [pe] optional={pe.Optional} donRemaining={pe.DonPaymentRemaining} text={Trim(pe.Text)}");
            Console.WriteLine($"   turn advanced to: {st2.ActiveSeat} (still 'south' = the turn did not end)");
            foreach (var e in st2.EventLog.TakeLast(5)) Console.WriteLine("     log: " + e.Message);
        }

        // ---------------------------------------------------------------- helpers
        private static string Trim(string t) =>
            string.IsNullOrEmpty(t) ? "" : (t.Length > 96 ? t.Substring(0, 96) + "…" : t).Replace("\n", " ");

        private static string FindByFeature(string feature, string excludeFeature = null)
        {
            foreach (var kv in CardData.Library)
            {
                var d = kv.Value;
                if (d == null || d.Type != "character") continue;
                if (!d.HasFeature(feature)) continue;
                if (excludeFeature != null && d.HasFeature(excludeFeature)) continue;
                if (!string.IsNullOrEmpty(d.Effect)) continue;      // vanilla, so it adds no side effects
                return kv.Key;
            }
            return null;
        }

        private static GameState NewBoard()
        {
            var st = GameEngine.CreateMatch(new MatchConfig
            { SouthDeck = "st01", NorthDeck = "st01", Seed = "reported-cards" });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 8;
            foreach (var p in st.Players.Values)
            {
                p.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                p.Hand.Clear(); p.Life.Clear(); p.Trash.Clear(); p.CostArea.Clear();
                for (int i = 0; i < 3; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                for (int i = 0; i < 10; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-rc-don-{serial++}", Rested = false });
                p.DonDeck = 6;
                p.AbilityUsedThisTurn.Clear();
            }
            st.PendingEffects.Clear();
            return st;
        }

        private static CardInstance Make(string cardId, string seat, string zone) => new CardInstance
        {
            InstanceId = $"{seat}-{cardId}-{serial++}",
            CardId = cardId,
            Owner = seat,
            Zone = zone,
        };
    }
}
