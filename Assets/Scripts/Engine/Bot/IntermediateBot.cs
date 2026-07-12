// One Piece TCG - Engine: rule-based "intermediate" bot.
// Pure C#, no UnityEngine dependency — sits directly on top of GameEngine's public API,
// the same surface the human UI (GameManager.cs) drives.
//
// Turn-sequencing heuristics below (deploy -> attach DON!! -> attack -> end turn, dumping
// leftover active DON!! onto the lead attacker right before declaring) were validated against
// ~20 real high-ranked-ladder OPTCGSim replays (OCR-extracted action logs), which consistently
// show that exact ordering. This is a first pass: single-ply, no lookahead, no opponent-hand
// modeling. It is meant to be a competent, non-random practice opponent — not a solved player.
//
// Robustness note: GameEngine is ~8800 lines of card-specific rules text-matching, and its
// target-legality checks aren't all centralized in IsValidEffectTarget — some (e.g. Jinbe
// ST01-005's "other than this card" self-exclusion) live only inside the resolver and reject
// a command without consuming the pending effect. A naive bot that always re-picks its
// highest-scoring candidate can loop on that forever. Every decision point here is therefore
// guarded by a per-call blacklist of command "signatures" checked against a precise per-command-
// type before/after success test (SnapshotFor/ChangedFor below) — NOT "did EventLog grow",
// since rejections still Log() a message, which would make every retry look like progress.
// A rejected pick is excluded and the bot falls through to its next-best option (ultimately:
// pass/end turn) instead of retrying indefinitely. This caught two real bugs during testing:
// the Jinbe self-target loop, and DecideDeckLook sending deckLookConfirmOrder for a "scry"-step
// effect (Bartholomew Kuma ST03-010) instead of deckLookScryConfirm — found via a 435-game
// all-starter-decks batch run, not the original st01/st02-only testing.
//
// Usage:
//   IntermediateBot.TakeAllAvailableActions(state, "south");   // acts for one seat until it
//                                                               // must yield (opponent's turn,
//                                                               // waiting on a defense decision).
//   IntermediateBot.PlayFullMatch(state);                      // drives BOTH seats until the
//                                                               // match finishes (self-play).
//   IntermediateBot.DecideOneCommand(...) / Snapshot / Succeeded / Signature  // single-step
//                                                               // hooks for a host that wants
//                                                               // to apply each command itself
//                                                               // (e.g. GameManager.Dispatch,
//                                                               // for UI/animation/network sync)
//                                                               // one "think tick" at a time —
//                                                               // see GameManager.IntermediateAiTick.

using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Bot
{
    public static class IntermediateBot
    {
        // ---- Public entry points -------------------------------------------------

        /// <summary>Applies commands for <paramref name="seat"/> until it has no further
        /// decision to make right now (opponent's turn, or waiting on the opponent's
        /// block/counter/trigger). Returns the number of commands applied.</summary>
        public static int TakeAllAvailableActions(GameState state, string seat, int maxCommands = 400)
        {
            int applied = 0;
            var blacklist = new HashSet<string>();
            for (int i = 0; i < maxCommands; i++)
            {
                var cmd = DecideNextCommand(state, seat, blacklist);
                if (cmd == null) break;
                object before = SnapshotFor(state, cmd);
                GameEngine.ApplyCommand(state, cmd);
                applied++;
                if (!ChangedFor(state, cmd, before))
                    blacklist.Add(Sig(cmd));
            }
            return applied;
        }

        // Rejections inside GameEngine still call Log(...), so "did EventLog grow" is NOT a
        // reliable success signal (that's exactly what caused the Jinbe self-target infinite
        // loop during testing — the rejection message itself grew the log every retry). Each
        // candidate-selection command type gets a precise before/after check instead.
        //
        // Public so a host driving one command at a time (GameManager.IntermediateAiTick) can
        // reuse the exact same success check around its own Dispatch() call instead of
        // reimplementing it: snapshot = SnapshotFor(...); Dispatch(cmd); if (!Succeeded(...)) blacklist.Add(Signature(cmd)).
        public static object SnapshotFor(GameState state, GameCommand cmd)
        {
            switch (cmd.Type)
            {
                case "attachDon":
                    return GameEngine.ActiveDonCount(state.Players[cmd.Seat]);
                case "deckLookSelect":
                    return state.DeckLook?.Cards.Count ?? -1;
                case "deckLookConfirmOrder":
                case "deckLookScryConfirm":
                    // Reference, not a count: a rejected confirm leaves DeckLook as the exact
                    // same object (ResolveDeckLookConfirmOrder/ScryConfirm return early without
                    // touching it — e.g. wrong Step, as happened when this bot used to send
                    // deckLookConfirmOrder for a "scry"-step effect and silently stalled).
                    return state.DeckLook;
                default:
                    return null;
            }
        }

        public static bool Succeeded(GameState state, GameCommand cmd, object before) => ChangedFor(state, cmd, before);

        private static bool ChangedFor(GameState state, GameCommand cmd, object before)
        {
            switch (cmd.Type)
            {
                case "resolveEffect":
                    return !state.PendingEffects.Any(e => e.EffectId == cmd.EffectId);
                case "playCard":
                case "counterWithCard":
                    return !state.Players[cmd.Seat].Hand.Any(c => c.InstanceId == cmd.InstanceId);
                case "attachDon":
                    return GameEngine.ActiveDonCount(state.Players[cmd.Seat]) != (int)before;
                case "declareAttack":
                    return state.Battle != null && state.Battle.AttackerId == cmd.Attacker;
                case "blockAttack":
                    return state.Battle != null && state.Battle.TargetId == cmd.Blocker;
                case "deckLookSelect":
                    return state.DeckLook == null || state.DeckLook.Cards.Count != (int)before;
                case "deckLookConfirmOrder":
                case "deckLookScryConfirm":
                    return !ReferenceEquals(state.DeckLook, before);
                case "endTurn":
                    // Success = it's genuinely no longer this seat's active main phase — covers
                    // both the ordinary case (ActiveSeat flips to the opponent) and the rarer
                    // one where ending the turn immediately finishes the match.
                    return state.Status == "finished" || state.ActiveSeat != cmd.Seat || state.Phase != "main";
                default:
                    return true; // remaining system/progression commands (passX, resolveAttack, useTrigger, ...) — not candidate loops, and not reachable while blocked the way endTurn was.
            }
        }

        /// <summary>Self-play driver: alternates both seats until the match finishes.
        /// Intended for bot-vs-bot practice generation. Guards against engine deadlocks
        /// with a total-command safety cap.</summary>
        public static void PlayFullMatch(GameState state, int maxTotalCommands = 20000)
        {
            int total = 0;
            while (state.Status != "finished" && total < maxTotalCommands)
            {
                int south = TakeAllAvailableActions(state, "south", maxTotalCommands - total);
                total += south;
                int north = TakeAllAvailableActions(state, "north", maxTotalCommands - total);
                total += north;
                if (south == 0 && north == 0)
                {
                    // Neither seat found a legal action — either the match ended between
                    // checks or both bots are stuck waiting on each other. Bail out rather
                    // than spin forever.
                    break;
                }
            }
        }

        private static string Sig(GameCommand c) =>
            string.Join("|", c.Type, c.Seat, c.InstanceId, c.Target, c.Attacker, c.Blocker, c.EffectId, c.Amount);

        /// <summary>Public alias of the same signature <see cref="TakeAllAvailableActions"/> uses
        /// internally to key its blacklist — expose it so a host-owned blacklist (e.g.
        /// GameManager's per-turn <c>aiTriedThisTurn</c> set) stays in the exact same key space.</summary>
        public static string Signature(GameCommand cmd) => Sig(cmd);

        // ---- Top-level decision dispatch ------------------------------------------

        /// <summary>Decides the single next command for <paramref name="seat"/> without applying
        /// it — for a host (GameManager) that wants to drive its own Dispatch() per command for
        /// UI/animation/network sync, one "think tick" at a time, instead of the batch
        /// <see cref="TakeAllAvailableActions"/>. Returns null when there's nothing to do right
        /// now (opponent's turn, or waiting on their defense decision). <paramref name="blacklist"/>
        /// should persist across ticks for as long as the same decision context is live (a
        /// per-turn set, cleared on turn change, mirrors what the batch method does per-call).</summary>
        public static GameCommand DecideOneCommand(GameState state, string seat, HashSet<string> blacklist) =>
            DecideNextCommand(state, seat, blacklist);

        private static GameCommand DecideNextCommand(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return null;

            if (state.Status == "coinflip")
            {
                if (state.CoinFlipWinner == seat)
                    return Try(blacklist, new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = true });
                return null;
            }

            if (state.Status == "mulligan")
            {
                var p = state.Players[seat];
                if (p.MulliganDecided) return null;
                return Try(blacklist, new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = ShouldMulligan(p) });
            }

            if (state.Status != "active") return null;

            // Bug found via a 435-game all-starter-decks batch run (game "st07 vs st01"): the
            // engine's IsTurnPlayerInMain gate requires PendingEffects.Count == 0 AND
            // DeckLook == null GLOBALLY — not just "no effect/look belonging to me". Falling
            // through to DecideMainPhase while the OTHER seat has an unresolved effect meant
            // every main-phase command (including the endTurn fallback, which bypasses Try()/
            // the blacklist entirely) silently no-opped forever: declareAttack/attachDon got
            // blacklisted after one try each, but endTurn kept getting re-issued and rejected
            // every single loop iteration, burning the whole command budget without ever
            // yielding back to the seat that actually owns the blocking effect.
            if (state.ActiveChoice != null && state.ActiveChoice.Seat != seat) return null;

            if (state.ActiveChoice != null && state.ActiveChoice.Seat == seat)
            {
                var a = Try(blacklist, new GameCommand { Type = "resolveChoice", Seat = seat, Target = "A" });
                if (a != null) return a;
                return Try(blacklist, new GameCommand { Type = "resolveChoice", Seat = seat, Target = "B" });
            }

            var myEffect = state.PendingEffects.FirstOrDefault(e => e.Seat == seat);
            if (myEffect != null)
                return DecideEffect(state, seat, myEffect, blacklist);
            if (state.PendingEffects.Count > 0) return null; // some other seat's effect is blocking everyone.

            if (state.DeckLook != null && state.DeckLook.Seat != seat) return null;
            if (state.DeckLook != null && state.DeckLook.Seat == seat)
                return DecideDeckLook(state, seat, blacklist);

            if (state.Battle != null)
            {
                if (state.Battle.TargetSeat == seat)
                    return DecideDefense(state, seat, blacklist);
                return null; // I'm the attacker; waiting on the defender.
            }

            if (state.ActiveSeat == seat && state.Phase == "main")
                return DecideMainPhase(state, seat, blacklist);

            return null;
        }

        /// <summary>Returns <paramref name="cmd"/> unless its signature is already known to be
        /// a no-op, in which case null (letting the caller fall through to the next option).</summary>
        private static GameCommand Try(HashSet<string> blacklist, GameCommand cmd) =>
            blacklist.Contains(Sig(cmd)) ? null : cmd;

        // ---- Setup ------------------------------------------------------------

        private static bool ShouldMulligan(PlayerState p)
        {
            if (p.Hand.Count == 0) return false;
            int cheapPlays = p.Hand.Count(c => (CardData.GetCard(c.CardId)?.Cost ?? 99) <= 2);
            double avgCost = p.Hand.Average(c => CardData.GetCard(c.CardId)?.Cost ?? 5);
            // No early plays at all, or a hand that's generally too expensive to develop from.
            return cheapPlays == 0 || avgCost > 4.5;
        }

        // ---- Pending effect / choice resolution --------------------------------

        private static GameCommand DecideEffect(GameState state, string seat, PendingEffect effect, HashSet<string> blacklist)
        {
            var candidates = CandidatesForZone(state, seat, effect.TargetZone)
                .Where(c => GameEngine.IsValidEffectTarget(state, effect, c))
                .ToList();

            bool negative = IsNegativeEffectText(effect.Text);
            var ranked = candidates
                .Select(c =>
                {
                    bool ownedBySelf = c.Owner == seat;
                    bool wantMax = (ownedBySelf && !negative) || (!ownedBySelf && negative);
                    double v = Value(state, c);
                    return (card: c, score: wantMax ? v : -v);
                })
                .OrderByDescending(x => x.score);

            foreach (var (card, _) in ranked)
            {
                var cmd = Try(blacklist, new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = effect.EffectId, Target = card.InstanceId });
                if (cmd != null) return cmd;
            }
            return new GameCommand { Type = "passEffect", Seat = seat, EffectId = effect.EffectId };
        }

        private static bool IsNegativeEffectText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.ToLowerInvariant();
            return t.Contains("k.o.") || t.Contains("trash") || t.Contains("rest ") ||
                   t.Contains("cannot") || t.Contains("-1000") || t.Contains("−1000") ||
                   t.Contains("discard") || System.Text.RegularExpressions.Regex.IsMatch(t, @"[-−]\d");
        }

        private static IEnumerable<CardInstance> CandidatesForZone(GameState state, string seat, EffectTargetZone zone)
        {
            var mine = state.Players[seat];
            var theirs = state.Players[GameEngine.OtherSeat(seat)];
            IEnumerable<CardInstance> Board(PlayerState p)
            {
                if (p.Leader != null) yield return p.Leader;
                foreach (var c in p.CharacterArea) if (c != null) yield return c;
                if (p.Stage != null) yield return p.Stage;
            }
            switch (zone)
            {
                case EffectTargetZone.Hand:
                    return mine.Hand;
                case EffectTargetZone.Trash:
                    return mine.Trash;
                case EffectTargetZone.Any:
                    return Board(mine).Concat(Board(theirs)).Concat(mine.Hand).Concat(mine.Trash);
                case EffectTargetZone.Play:
                default:
                    return Board(mine).Concat(Board(theirs));
            }
        }

        private static double Value(GameState state, CardInstance card)
        {
            var def = GameEngine.GetCard(card);
            if (def == null) return 0;
            // Cost dominates (bigger investment = more important card); power breaks ties
            // and matters on its own for board-state cards.
            return def.Cost * 1000 + GameEngine.GetPower(state, card);
        }

        // ---- Deck-look resolution (simplified: pick the single best matching card) --

        private static GameCommand DecideDeckLook(GameState state, string seat, HashSet<string> blacklist)
        {
            var dl = state.DeckLook;
            if (dl.Step == "select")
            {
                var eligible = dl.Cards.Where(c => DeckLookCardEligible(state, dl, c))
                    .OrderByDescending(c => Value(state, c));
                foreach (var c in eligible)
                {
                    var cmd = Try(blacklist, new GameCommand { Type = "deckLookSelect", Seat = seat, Target = c.InstanceId });
                    if (cmd != null) return cmd;
                }
                return new GameCommand { Type = "deckLookSelect", Seat = seat, Target = "" };
            }
            if (dl.Step == "scry")
            {
                // "Look at N, place each at top or bottom": OrderedInstanceIds here is the set
                // kept on TOP (in order) — everything else in dl.Cards goes to the bottom. Bare
                // "keep nothing on top" (empty list) is always a legal, terminating choice, so
                // default to that rather than risk misjudging which cards are worth keeping.
                return new GameCommand { Type = "deckLookScryConfirm", Seat = seat, OrderedInstanceIds = new List<string>() };
            }
            // "rearrange" (and any other step): keep remaining cards in their current order —
            // a no-op ordering that completes the effect without a deep bottom-of-deck strategy.
            return new GameCommand
            {
                Type = "deckLookConfirmOrder",
                Seat = seat,
                OrderedInstanceIds = dl.Cards.Select(c => c.InstanceId).ToList(),
            };
        }

        private static bool DeckLookCardEligible(GameState state, DeckLookState dl, CardInstance card)
        {
            var def = GameEngine.GetCard(card);
            if (def == null) return false;
            if (!string.IsNullOrEmpty(dl.CardTypeFilter) && !string.IsNullOrEmpty(dl.NamedCardFilter))
            {
                bool nameMatch = GameEngine.NameMatches(state, card, dl.NamedCardFilter);
                bool typeMatch = def.Type.Equals(dl.CardTypeFilter, StringComparison.OrdinalIgnoreCase);
                if (!nameMatch && !typeMatch) return false;
            }
            else if (!string.IsNullOrEmpty(dl.NamedCardFilter) && !GameEngine.NameMatches(state, card, dl.NamedCardFilter))
                return false;
            else if (!string.IsNullOrEmpty(dl.CardTypeFilter) && !def.Type.Equals(dl.CardTypeFilter, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(dl.FeatureFilter) && !def.HasFeature(dl.FeatureFilter)) return false;
            if (dl.MaxCost >= 0 && def.Cost > dl.MaxCost) return false;
            if (dl.MaxPower >= 0 && def.Power > dl.MaxPower) return false;
            if (dl.RequireTrigger && string.IsNullOrEmpty(def.Trigger)) return false;
            // "Play mode" search effects reject a Character pick outright when the board is
            // full (the engine re-inserts it and no-ops rather than advancing) — the blacklist
            // in TakeAllAvailableActions already prevents looping on this, but excluding it
            // here means the bot doesn't waste a turn even trying it first.
            if (dl.PlayMode && def.Type == "character" && state.Players.TryGetValue(dl.Seat, out var dlP)
                && !dlP.CharacterArea.Exists(c => c == null))
                return false;
            return true;
        }

        // ---- Defense (block / counter / trigger / resolve) ------------------------

        private static GameCommand DecideDefense(GameState state, string seat, HashSet<string> blacklist)
        {
            var battle = state.Battle;
            var me = state.Players[seat];
            switch (battle.Step)
            {
                case "block":
                {
                    var attacker = FindCard(state, battle.AttackerSeat, battle.AttackerId);
                    int attackPower = attacker != null ? GameEngine.GetPower(state, attacker) : 0;
                    var blockers = me.CharacterArea
                        .Where(c => c != null && !c.Rested
                            && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false))
                        .ToList();
                    if (blockers.Count == 0)
                        return new GameCommand { Type = "passBlock", Seat = seat };

                    bool leaderLethalRisk = battle.TargetId == me.Leader?.InstanceId && me.Life.Count == 0;
                    var surviving = blockers.Where(b => GameEngine.GetPower(state, b) >= attackPower)
                        .OrderBy(b => GameEngine.GetPower(state, b));
                    foreach (var b in surviving)
                    {
                        var cmd = Try(blacklist, new GameCommand { Type = "blockAttack", Seat = seat, Blocker = b.InstanceId });
                        if (cmd != null) return cmd;
                    }
                    if (leaderLethalRisk || me.Life.Count <= 1)
                    {
                        foreach (var b in blockers.OrderBy(b => GameEngine.GetPower(state, b)))
                        {
                            var cmd = Try(blacklist, new GameCommand { Type = "blockAttack", Seat = seat, Blocker = b.InstanceId });
                            if (cmd != null) return cmd;
                        }
                    }
                    return new GameCommand { Type = "passBlock", Seat = seat };
                }
                case "counter":
                {
                    int need = battle.AttackPower - battle.DefensePower;
                    var options = me.Hand.Where(c => GameEngine.CanCounterFromHand(state, seat, c))
                        .OrderBy(c => GameEngine.GetCounterPower(c));
                    bool worthSaving = me.Life.Count > 2 && need <= 0;
                    if (need > 0 || (battle.TargetId != me.Leader?.InstanceId && !worthSaving))
                    {
                        foreach (var c in options.Where(c => GameEngine.GetCounterPower(c) >= Math.Max(need, 0)))
                        {
                            var cmd = Try(blacklist, new GameCommand { Type = "counterWithCard", Seat = seat, InstanceId = c.InstanceId });
                            if (cmd != null) return cmd;
                        }
                    }
                    return new GameCommand { Type = "passCounter", Seat = seat };
                }
                case "trigger":
                    return new GameCommand { Type = "useTrigger", Seat = seat };
                case "damage":
                default:
                    return new GameCommand { Type = "resolveAttack", Seat = seat };
            }
        }

        private static CardInstance FindCard(GameState state, string seat, string instanceId)
        {
            if (string.IsNullOrEmpty(seat) || string.IsNullOrEmpty(instanceId)) return null;
            var p = state.Players[seat];
            if (p.Leader?.InstanceId == instanceId) return p.Leader;
            var c = p.CharacterArea.FirstOrDefault(x => x != null && x.InstanceId == instanceId);
            if (c != null) return c;
            if (p.Stage?.InstanceId == instanceId) return p.Stage;
            return null;
        }

        // ---- Main phase: deploy -> attach DON!! -> attack -> end turn -------------

        private static GameCommand DecideMainPhase(GameState state, string seat, HashSet<string> blacklist)
        {
            var p = state.Players[seat];

            // 1) Deploy the highest-value affordable card first (spends DON!! on board
            //    presence before combat math — matches the deploy-then-attach-then-attack
            //    ordering seen across the mined ranked-ladder replays).
            var playable = p.Hand.Where(c => GameEngine.CanPlayFromHand(state, seat, c))
                .OrderByDescending(c => Value(state, c));
            foreach (var c in playable)
            {
                var cmd = Try(blacklist, new GameCommand { Type = "playCard", Seat = seat, InstanceId = c.InstanceId });
                if (cmd != null) return cmd;
            }

            // 2) Attack planning. Only consider characters/leader that started the turn
            //    already in play (no summoning-sick check needed beyond Rush, which
            //    GameEngine.HasRush already folds in).
            if (p.TurnsStarted <= 1) return Try(blacklist, new GameCommand { Type = "endTurn", Seat = seat });

            var opponent = state.Players[GameEngine.OtherSeat(seat)];
            var attackers = new List<CardInstance>();
            if (p.Leader != null && !p.Leader.Rested) attackers.Add(p.Leader);
            attackers.AddRange(p.CharacterArea.Where(c => c != null && !c.Rested));
            attackers = attackers
                .Where(a => !GameEngine.HasModifier(state, a, "cannotAttack"))
                .Where(a => a.PlayedOnTurn != state.TurnNumber || GameEngine.HasRush(state, a))
                .OrderByDescending(a => GameEngine.GetPower(state, a))
                .ToList();

            foreach (var attacker in attackers)
            {
                int attackerPower = GameEngine.GetPower(state, attacker);

                // Prefer a favorable trade against a rested opposing character (removes a
                // threat); otherwise chip the leader.
                var restedTargets = opponent.CharacterArea.Where(c => c != null && c.Rested).ToList();
                var favorableTrade = restedTargets
                    .Where(t => attackerPower > GameEngine.GetPower(state, t))
                    .OrderByDescending(t => Value(state, t))
                    .FirstOrDefault();

                string targetId = favorableTrade?.InstanceId ?? opponent.Leader?.InstanceId;
                if (string.IsNullOrEmpty(targetId)) continue;

                // 3) Dump remaining active DON!! onto this attacker before declaring — cheap
                //    insurance against an unseen blocker/counter, and how the mined replay
                //    data consistently sequences it (Attach N Don immediately precedes Attack).
                int activeDon = GameEngine.ActiveDonCount(p);
                if (activeDon > 0)
                {
                    var attachCmd = Try(blacklist, new GameCommand { Type = "attachDon", Seat = seat, Target = attacker.InstanceId, Amount = activeDon });
                    if (attachCmd != null) return attachCmd;
                }

                var attackCmd = Try(blacklist, new GameCommand { Type = "declareAttack", Seat = seat, Attacker = attacker.InstanceId, Target = targetId });
                if (attackCmd != null) return attackCmd;

                // This attacker's only plan was rejected (blacklisted) — try the next one
                // rather than getting stuck retrying the same declaration.
            }
            // Try(), not a bare return: if endTurn itself is somehow illegal right now (the
            // original bug's actual failure mode — IsTurnPlayerInMain blocked by an unrelated
            // pending effect), yield (null) instead of re-issuing the same rejected command
            // forever. ChangedFor("endTurn") below detects success via ActiveSeat changing.
            return Try(blacklist, new GameCommand { Type = "endTurn", Seat = seat });
        }
    }
}
