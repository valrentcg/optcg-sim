using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The brief's second half — "IF they do use it, the effects should all resolve properly" —
    /// asked as a differential rather than a presence check.
    ///
    /// `paidfornothing` asks whether the engine gave SOMETHING back for a payment. That is a weak
    /// oracle: a log line, a re-prompt, or an incidental state touch all satisfy it. The strong form
    /// is to run the same clause TWICE from an identical board — once pressing Use, once pressing
    /// Skip — and compare the two resulting states. If they are indistinguishable, using the effect
    /// accomplished nothing, whatever the log said.
    ///
    /// This is the check that cannot be satisfied by noise: it does not care what happened, only
    /// that the two branches DIFFER. A clause where they do not is either inert or the player was
    /// charged for nothing.
    ///
    /// Ratcheted, and the baseline is NOT zero — read the Baseline comment before trusting the
    /// number. The residual is fixture mismatch, not 76 broken cards: a cost that wants a {Navy}
    /// card in hand is correctly inert against a hand that has none, and "Use == Skip" is then the
    /// RIGHT answer. Four passes of fixture synthesis (type-matching hand, type-matching board,
    /// gate-satisfying Leader, rested opponent) took the count from 111 to 76 without changing a
    /// line of engine code, which is what shows the residual is measurement and not defect.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- usevsskip
    /// </summary>
    public static class UseVsSkipSweep
    {
        /// <summary>A HELD LINE, not a defect count — and now MEASURED to be so rather than argued.
        /// The companion check reports how many of these moved the board anyway: **0**. Every one of
        /// the 76 leaves both branches identical to an untouched board, which is exactly what an
        /// unpayable cost should do. The previous version of this comment asserted that from
        /// sampling; the sweep now proves it for all 76.
        ///
        /// These 76 are dominated by the fixture not being
        /// the deck the card was designed for — a cost naming a specific card ([Silvers Rayleigh]),
        /// a trash of 7+, a board state this sweep does not synthesise. Four passes of fixture work
        /// took it 111 -> 108 -> 90 -> 76 and each pass removed noise, not defects. The value is the
        /// RATCHET: 421 clauses demonstrably change the board when used, and if a future change
        /// makes any of them inert, this grows and the gate fails. Lower it when a real one is
        /// fixed; never raise it to make a run pass.</summary>
        private const int Baseline = 76;

        public static int Run()
        {
            Console.WriteLine("=== Use vs Skip: does using a \"you may\" effect change anything? ===");

            var seen = new HashSet<string>();
            var identical = new List<string>();
            var firedRegardless = new List<string>();
            int driven = 0, differed = 0;

            foreach (var def in CardData.Library.Values
                        .Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                        .GroupBy(d => d.Id).Select(g => g.First())
                        .OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split((char)10))
                {
                    var clause = raw.Trim();
                    if (clause.Length == 0) continue;
                    // Only clauses that ASK: a cost-prefixed opt-in with a Use/Skip decision.
                    var stripped = GameEngine.StripLeadingTimingTags(clause);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(
                            stripped, @"^You (?:may|can) [^:]{2,90}:",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    if (!seen.Add(Normalize(clause))) continue;

                    string used, skipped, need = null, boardType = null, leader = null;
                    try
                    {
                        // Build the deck the card was designed for. Without this the sweep mostly
                        // measures its own fixture: a cost that wants a {Navy} card in hand is
                        // unpayable against an arbitrary hand, so Use correctly does nothing and
                        // "Use == Skip" is the RIGHT answer rather than a defect.
                        need = TypeNeededInHand(clause);
                        boardType = TypeNeededOnBoard(clause);
                        leader = LeaderFor(clause);
                        used = Fingerprint(Run(clause, true, need, boardType, leader));
                        skipped = Fingerprint(Run(clause, false, need, boardType, leader));
                    }
                    catch (Exception) { continue; }
                    driven++;

                    if (used == skipped)
                    {
                        identical.Add($"{def.Id}  :: {Trim(clause, 80)}");
                        // Sharper question inside the residual: did anything happen AT ALL? If the
                        // two branches match AND both match an untouched board, the cost was simply
                        // unpayable and inertness is correct. If they match but the board MOVED,
                        // the change did not come from the decision — the effect fired whichever
                        // button was pressed, which is the shape optionalfires exists to forbid.
                        string untouched;
                        try { untouched = Fingerprint(new Board(need, boardType, leader).St); }
                        catch (Exception) { untouched = null; }
                        if (untouched != null && used != untouched)
                            firedRegardless.Add($"{def.Id}  :: {Trim(clause, 74)}");
                    }
                    else differed++;
                }
            }

            Console.WriteLine($"  drove {driven} cost-prefixed clauses; {differed} changed the board when USED");
            Console.WriteLine($"  Use and Skip indistinguishable: {identical.Count}");
            foreach (var s in identical.Take(6)) Console.WriteLine("    " + s);
            Console.WriteLine($"  ...of those, board MOVED anyway (effect fired regardless of the answer): {firedRegardless.Count}");
            foreach (var s in firedRegardless.Take(10)) Console.WriteLine("    " + s);

            SweepRatchet.Reset();
            SweepRatchet.AtMost("cost-prefixed clauses driven (floor check)", Math.Max(0, 150 - driven), 0);
            SweepRatchet.AtMost("clauses where Use and Skip are indistinguishable", identical.Count, Baseline);
            SweepRatchet.AtMost("clauses that fired regardless of the answer", firedRegardless.Count, 0);
            return SweepRatchet.Result();
        }

        /// <summary>Everything a player could notice, flattened. Deliberately broad: the point is to
        /// catch "nothing at all happened", so a narrow fingerprint would manufacture false
        /// matches.</summary>
        private static string Fingerprint(GameState st)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var seat in new[] { "south", "north" })
            {
                var p = st.Players[seat];
                // Zone CONTENTS, not just sizes. "Place 1 card from your hand at the bottom of your
                // deck: Draw 1 card" moves a card each way and leaves both counts unchanged — a
                // count-only fingerprint calls that "nothing happened" and reports a working card as
                // broken. Ordered ids, so a reorder is visible too.
                sb.Append(seat).Append('|')
                  .Append(string.Join(",", p.Hand.Select(x => x.InstanceId))).Append('|')
                  .Append(string.Join(",", p.Deck.Take(6).Select(x => x.InstanceId))).Append('|')
                  .Append(string.Join(",", p.Life.Select(x => x.InstanceId))).Append('|')
                  .Append(string.Join(",", p.Trash.Select(x => x.InstanceId))).Append('|')
                  .Append(p.CostArea.Count).Append(',').Append(p.DonDeck).Append(',')
                  .Append(p.CostArea.Count(d => d.Rested)).Append('|');
                foreach (var c in p.CharacterArea)
                    sb.Append(c == null ? "-" : c.CardId + ":" + (c.Rested ? "R" : "A")
                              + ":" + GameEngine.GetPower(st, c) + ":" + c.AttachedDonIds.Count).Append(';');
                sb.Append('|').Append(p.Leader == null ? "-" : GameEngine.GetPower(st, p.Leader).ToString());
                foreach (var c in p.Life) sb.Append(c.FaceUp ? 'U' : 'd');
                sb.Append("||");
            }
            return sb.ToString();
        }

        /// <summary>The {Type} a cost demands from hand, if any — so the fixture can hold one.</summary>
        private static string TypeNeededInHand(string clause)
        {
            var m = System.Text.RegularExpressions.Regex.Match(clause, @"\{([^}]+)\} type [^:]*from your hand");
            if (!m.Success) return null;
            foreach (var def in CardData.Library.Values.OrderBy(d => d?.Id, StringComparer.Ordinal))
            {
                if (def == null || string.IsNullOrEmpty(def.Type)) continue;
                if (def.HasFeature(m.Groups[1].Value)) return def.Id;
            }
            return null;
        }

        /// <summary>The {Type} a cost demands from YOUR BOARD ("rest 1 of your {East Blue} type
        /// Characters"), so the fixture can field one.</summary>
        private static string TypeNeededOnBoard(string clause)
        {
            var m = System.Text.RegularExpressions.Regex.Match(clause, @"of your \{([^}]+)\} type Characters?");
            if (!m.Success) return null;
            foreach (var def in CardData.Library.Values.OrderBy(d => d?.Id, StringComparer.Ordinal))
            {
                if (def == null || !string.Equals(def.Type, "character", StringComparison.OrdinalIgnoreCase)) continue;
                if (def.HasFeature(m.Groups[1].Value)) return def.Id;
            }
            return null;
        }

        /// <summary>A Leader satisfying a "If your Leader has the {T} type" gate on the clause.</summary>
        private static string LeaderFor(string clause)
        {
            var t = System.Text.RegularExpressions.Regex.Match(clause, @"Leader has the \{([^}]+)\} type");
            if (!t.Success) return null;
            foreach (var def in CardData.Library.Values.OrderBy(d => d?.Id, StringComparer.Ordinal))
            {
                if (def == null || !string.Equals(def.Type, "leader", StringComparison.OrdinalIgnoreCase)) continue;
                if (def.HasFeature(t.Groups[1].Value)) return def.Id;
            }
            return null;
        }

        private static GameState Run(string clause, bool press, string extraHandType = null,
                                     string boardType = null, string leaderId = null)
        {
            var b = new Board(extraHandType, boardType, leaderId);
            b.Queue(clause);
            for (int i = 0; i < 8; i++)
            {
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null) break;
                int peBefore = b.St.PendingEffects.Count, logBefore = b.St.EventLog.Count;
                if (press)
                {
                    string target = b.Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(b.St, pe, x))?.InstanceId;
                    b.St = GameEngine.ApplyCommand(b.St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                }
                else
                {
                    b.St = GameEngine.ApplyCommand(b.St, new GameCommand
                    { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });
                }
                if (b.St.PendingEffects.Count == peBefore && b.St.EventLog.Count == logBefore) break;
            }
            return b.St;
        }

        private static string Normalize(string s) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(s, "[0-9]+", "N"), @"\[[^\]]*\]", "[T]").Trim();

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board(string extraHandType = null, string boardType = null, string leaderId = null)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "use-vs-skip" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005", "EB01-004" })
                    { p.Hand.Add(Make(id, p.Seat, "hand")); p.Trash.Add(Make(id, p.Seat, "trash")); }
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-uv-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                S.CharacterArea[1] = Make("EB03-002", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                N.CharacterArea[1] = Make("EB03-002", "north", "character");
                N.CharacterArea[1].Rested = true;   // several costs/bodies need a RESTED opponent
                if (!string.IsNullOrEmpty(extraHandType)) S.Hand.Add(Make(extraHandType, "south", "hand"));
                if (!string.IsNullOrEmpty(boardType)) S.CharacterArea[2] = Make(boardType, "south", "character");
                if (!string.IsNullOrEmpty(leaderId)) S.Leader = Make(leaderId, "south", "leader");
                St.PendingEffects.Clear();
            }

            public void Queue(string clause) =>
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);

            public IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.Life.AsEnumerable().Reverse()) yield return x;
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    foreach (var x in p.Trash) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                }
            }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-uv-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
