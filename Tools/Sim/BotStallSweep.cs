using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "Will the bot freeze?" — the health sweep behind report 20260725-045539-685.
    ///
    /// Drives a SHIPPED difficulty tier across many matchups and looks for the two ways the in-game bot can
    /// appear to stop playing:
    ///   SLOW      — a single decision takes long enough to read as a freeze (the Advanced tier's rollout).
    ///   STUCK     — the bot returns no command while it OWNS the open decision, or re-issues one rejected
    ///               command forever. Nothing can advance the game, so the match hangs. This is the failure
    ///               mode that matters for beginner/intermediate, which do no search and are always fast.
    ///
    /// Tiers mirror GameManager exactly: "advanced" → AdvancedContractBot; "intermediate"/"beginner" → the
    /// IntermediateBot core, with beginner setting the same 5 legacy knobs ApplyBotDifficultyKnobs() sets.
    ///
    /// ⚠️ RUN FROM Tools/Sim — BuildRegistry reads the RELATIVE path Decks/imported, so from the repo root it
    /// silently imports 0 meta decks and sweeps starters only. Check the "Imported N meta deck(s)" line.
    ///
    /// Run: dotnet run --project Sim.csproj -c Release -- botstall [tier|all] [games] [maxTurns]
    /// </summary>
    public static class BotStallSweep
    {
        private const double SlowMs = 1500;      // "the player would notice" (CoreCLR; Mono is slower)
        private const int RepeatLimit = 8;       // same signature this many times = a no-op loop

        public static int Run(string[] args, DeckRegistry reg)
        {
            string tierArg = args.Length > 1 ? args[1].ToLowerInvariant() : "all";
            int games = args.Length > 2 && int.TryParse(args[2], out var g) ? g : 30;
            int maxTurns = args.Length > 3 && int.TryParse(args[3], out var mt) ? mt : 24;

            var tiers = tierArg == "all"
                ? new[] { "beginner", "intermediate", "advanced" }
                : new[] { tierArg };

            int bad = 0;
            foreach (var tier in tiers) bad += Sweep(tier, reg, games, maxTurns);
            Console.WriteLine();
            Console.WriteLine(bad == 0
                ? "botstall: no slow or stuck decisions in any swept tier."
                : $"botstall: {bad} PROBLEM(S) found — see above.");
            Console.WriteLine("NOTE: CoreCLR timings. The shipped player runs Mono — assume several times slower.");
            return bad == 0 ? 0 : 1;
        }

        private static int Sweep(string tier, DeckRegistry reg, int games, int maxTurns)
        {
            var deckIds = reg.Ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
            Console.WriteLine();
            Console.WriteLine($"=== tier={tier}: {games} games over {deckIds.Count} decks, ≤{maxTurns} turns ===");

            var thinks = new List<double>();
            var slow = new List<(double ms, string deck, string cmd, string ctx)>();
            var stuck = new List<string>();
            int decisions = 0, noops = 0;

            for (int i = 0; i < games; i++)
            {
                string sId = deckIds[(i * 7) % deckIds.Count];
                string nId = deckIds[(i * 13 + 3) % deckIds.Count];
                ApplyTierKnobs(tier, "north");   // exactly what GameManager.ApplyBotDifficultyKnobs does

                var st = GameEngine.CreateMatch(new MatchConfig
                { SouthDeckDef = reg.Resolve(sId), NorthDeckDef = reg.Resolve(nId), Seed = $"stall-{i}" });
                string arch = AdvancedContractBot.ClassifyArchetype(reg.Resolve(nId));

                var tried = new HashSet<string>();
                var activated = new HashSet<string>();
                var repeats = new Dictionary<string, int>();
                int lastTurn = -1, guard = 0;

                while (st.Status != "finished" && st.TurnNumber <= maxTurns && guard++ < 6000)
                {
                    // GameManager clears the per-turn no-op blacklist on every turn change.
                    if (st.TurnNumber != lastTurn)
                    { lastTurn = st.TurnNumber; tried.Clear(); activated.Clear(); repeats.Clear(); }

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var cmd = Decide(tier, st, "north", tried, activated, arch);
                    sw.Stop();

                    if (cmd == null)
                    {
                        // Only a hang if the bot owns the open decision — waiting on the opponent is correct.
                        if (OwnsOpenDecision(st, "north"))
                        {
                            stuck.Add($"NULL while owning {Context(st)} — {sId} vs {nId} turn {st.TurnNumber}"
                                + Diag(st, "north", tried));
                            break;
                        }
                        var sc = IntermediateBot.DecideOneCommand(st, "south", tried);
                        if (sc == null) break;
                        object b2 = IntermediateBot.SnapshotFor(st, sc);
                        GameEngine.ApplyCommand(st, sc);
                        if (!IntermediateBot.Succeeded(st, sc, b2)) tried.Add(IntermediateBot.Signature(sc));
                        continue;
                    }

                    double ms = sw.Elapsed.TotalMilliseconds;
                    thinks.Add(ms); decisions++;
                    string ctx = Context(st);
                    if (ms > SlowMs) slow.Add((ms, $"{sId} vs {nId}", cmd.Type, ctx));

                    string sig = IntermediateBot.Signature(cmd);
                    repeats[sig] = repeats.TryGetValue(sig, out var r) ? r + 1 : 1;
                    if (repeats[sig] >= RepeatLimit)
                    {
                        stuck.Add($"LOOP x{repeats[sig]} on {sig} at {ctx} — {sId} vs {nId} turn {st.TurnNumber}"
                            + LoopDiag(st, "north", cmd));
                        break;
                    }

                    object before = IntermediateBot.SnapshotFor(st, cmd);
                    GameEngine.ApplyCommand(st, cmd);
                    if (!IntermediateBot.Succeeded(st, cmd, before)) { noops++; tried.Add(sig); }
                }
            }
            ApplyTierKnobs("intermediate", null);   // leave the statics clean for the next tier

            thinks.Sort();
            double P(double q) => thinks.Count == 0 ? 0 : thinks[Math.Min(thinks.Count - 1, (int)(q * thinks.Count))];
            Console.WriteLine($"  decisions={decisions}  no-ops={noops} ({(decisions == 0 ? 0 : 100.0 * noops / decisions):F2}%)");
            Console.WriteLine($"  think ms  p50={P(0.50):F1}  p90={P(0.90):F1}  p99={P(0.99):F1}  MAX={(thinks.Count == 0 ? 0 : thinks[^1]):F1}");
            Console.WriteLine($"  SLOW (>{SlowMs:F0} ms): {slow.Count}");
            foreach (var w in slow.OrderByDescending(w => w.ms).Take(5))
                Console.WriteLine($"     {w.ms,8:F1} ms  {w.ctx,-16} {w.cmd,-18} {w.deck}");
            Console.WriteLine($"  STUCK: {stuck.Count}");
            foreach (var s in stuck.Take(8)) Console.WriteLine($"     {s}");
            return slow.Count + stuck.Count;
        }

        private static GameCommand Decide(string tier, GameState st, string seat,
            HashSet<string> tried, HashSet<string> activated, string arch)
        {
            if (tier == "advanced")
                return AdvancedContractBot.Decide(GameClone.Clone(st), seat, new HashSet<string>(tried), activated, arch);
            return IntermediateBot.DecideOneCommand(st, seat, tried);
        }

        /// <summary>Mirror of GameManager.ApplyBotDifficultyKnobs: beginner reverts the resource-discipline
        /// habits for its seat; every other tier runs the core at full strength.</summary>
        private static void ApplyTierKnobs(string tier, string seat)
        {
            string s = tier == "beginner" ? seat : null;
            IntermediateBot.LegacyDonSeat = s;
            IntermediateBot.HoldCounterVariantSeat = s;
            IntermediateBot.LegacyStackCharCounterSeat = s;
            IntermediateBot.LegacyStackLeaderCounterSeat = s;
            IntermediateBot.LegacyMulliganSeat = s;
        }

        /// <summary>Is <paramref name="seat"/> the one seat the engine is currently waiting on? Only then does
        /// returning no command hang the match; waiting on the OPPONENT is correct behaviour, not a stall.
        ///
        /// This must follow the engine's PRECEDENCE, not just test each blocker independently. A naive
        /// "…|| Battle.PrioritySeat == seat" reported 8 false stalls per tier: with a [When Attacking] effect
        /// open, the defender is still Battle.PrioritySeat, but the ATTACKER's unresolved effect outranks the
        /// battle — so the defender correctly returns null (IntermediateBot: "some other seat's effect is
        /// blocking everyone"). Mirrors DecideNextCommand's own ordering.</summary>
        private static bool OwnsOpenDecision(GameState st, string seat)
        {
            if (st.ActiveChoice != null) return st.ActiveChoice.Seat == seat;
            if (st.PendingCharReplace != null) return st.PendingCharReplace.Seat == seat;
            if (st.DeckLook != null) return st.DeckLook.Seat == seat;
            if (st.PendingEffects.Count > 0) return st.PendingEffects[0].Seat == seat;
            if (st.Battle != null) return st.Battle.PrioritySeat == seat;
            return st.ActiveSeat == seat && st.Phase == "main";
        }

        /// <summary>Is the looping command genuinely a NO-OP (nothing in the state moves), and what is the
        /// engine blocked on? Distinguishes a real livelock from a command that legitimately repeats.</summary>
        private static string LoopDiag(GameState st, string seat, GameCommand cmd)
        {
            const string NL = "\n        ";
            var clone = GameClone.Clone(st);
            long Fp(GameState s) => s.PendingEffects.Count * 1000003L + s.EventLog.Count * 0
                + (s.Battle == null ? 0 : s.Battle.Step.GetHashCode() + s.Battle.CounterPower * 31)
                + s.TurnNumber * 101 + s.Players.Sum(p => p.Value.Hand.Count * 7 + p.Value.Trash.Count * 13
                    + p.Value.Life.Count * 17 + p.Value.CharacterArea.Count(c => c != null) * 19);
            long before = Fp(clone);
            GameEngine.ApplyCommand(clone, cmd);
            long after = Fp(clone);
            var e = st.PendingEffects.FirstOrDefault();
            var sb = new System.Text.StringBuilder();
            sb.Append(NL).Append($"is-no-op={before == after}  battleStep={st.Battle?.Step ?? "none"} " +
                $"targetSeat={st.Battle?.TargetSeat} prioritySeat={st.Battle?.PrioritySeat} deferredTrigger={st.DeferredActivatedTriggerSeat}");
            if (e != null)
                sb.Append(NL).Append($"blocking effect seat={e.Seat} id={e.EffectId} src={e.SourceCardId} text=\"{e.Text}\"");
            sb.Append(NL).Append($"engine says: \"{clone.EventLog.LastOrDefault()?.Message}\"");
            var alt = IntermediateBot.DecideOneCommand(st, seat, new HashSet<string>());
            sb.Append(NL).Append($"IntermediateBot would play: {(alt == null ? "(null)" : IntermediateBot.Signature(alt))}");
            return sb.ToString();
        }

        /// <summary>Everything needed to tell a real deadlock from a harness artifact at a stall point.</summary>
        private static string Diag(GameState st, string seat, HashSet<string> tried)
        {
            var e = st.PendingEffects.FirstOrDefault(x => x.Seat == seat) ?? st.PendingEffects.FirstOrDefault();
            var sb = new System.Text.StringBuilder();
            const string NL = "\n        ";
            if (e != null)
            {
                sb.Append(NL).Append($"effect id={e.EffectId} seat={e.Seat} optional={e.Optional} scope={e.Scope} " +
                    $"zone={e.TargetZone} src={e.SourceCardId} selRem={e.SelectionsRemaining} donPay={e.DonPaymentRemaining}");
                sb.Append(NL).Append($"text=\"{e.Text}\"");
            }
            sb.Append(NL).Append($"pendingCount={st.PendingEffects.Count} blacklistSize={tried.Count}");
            sb.Append(NL).Append("blacklisted-for-seat: " +
                string.Join(" , ", tried.Where(t => t.Contains("|" + seat + "|")).Take(6)));
            // Would a bare pass / bare resolve actually clear it? (what the UI's Skip button would send)
            foreach (var probe in new[] { "passEffect", "resolveEffect" })
            {
                var clone = GameClone.Clone(st);
                GameEngine.ApplyCommand(clone, new GameCommand { Type = probe, Seat = seat, EffectId = e?.EffectId });
                sb.Append(NL).Append($"probe {probe}: pending {st.PendingEffects.Count} -> {clone.PendingEffects.Count}");
            }
            return sb.ToString();
        }

        private static string Context(GameState st) =>
            st.DeckLook != null ? "deckLook"
            : st.ActiveChoice != null ? "choice"
            : st.PendingCharReplace != null ? "charReplace"
            : st.PendingEffects.Count > 0 ? "pendingEffect"
            : st.Battle != null ? "battle:" + st.Battle.Step
            : "main";
    }
}
