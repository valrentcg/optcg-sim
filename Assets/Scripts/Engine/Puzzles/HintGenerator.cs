using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Chess-puzzle-style graduated hints, derived from the <see cref="LethalSolver"/>'s proven winning line
    /// so a hint can never point at a non-solution. Three escalating levels:
    ///   L1 (obscure)  : theme/category + the human framing (required hits = Life + 1); no card named.
    ///   L2 (targeted) : highlights the single key card with a blurb, without stating the move.
    ///   L3 (explicit) : spells out the first one or two moves.
    /// Each hint carries HighlightInstanceIds for the UI to glow.
    /// </summary>
    public static class HintGenerator
    {
        public sealed class Hint
        {
            public int Level;
            public string Text;
            public List<string> HighlightInstanceIds = new List<string>();
        }

        public enum Category { AttackOrder, DonAllocation, PlayBeforeAttack, AbilityFirst, CounterAware }

        public static List<Hint> Generate(GameState pos, string attacker, LethalSolver.Result solved)
        {
            var hints = new List<Hint>();
            if (solved == null || solved.Outcome != LethalSolver.Lethal.Win) return hints;

            var mine = solved.PrincipalVariation.Where(c => c.Seat == attacker).ToList();
            var attacks = mine.Where(c => c.Type == "declareAttack").ToList();
            bool hasDon = mine.Any(c => c.Type == "attachDon");
            bool hasPlay = mine.Any(c => c.Type == "playCard");
            bool hasActivate = mine.Any(c => c.Type == "activateMain");
            bool defenderHasCounter = DefenderHasCounter(pos, attacker);

            int life = pos.Players[GameEngine.OtherSeat(attacker)].Life.Count;
            int requiredHits = life + 1;

            var cat = hasActivate ? Category.AbilityFirst
                    : hasPlay ? Category.PlayBeforeAttack
                    : hasDon ? Category.DonAllocation
                    : defenderHasCounter ? Category.CounterAware
                    : Category.AttackOrder;

            string l1 = cat switch
            {
                Category.AbilityFirst => "There is exactly lethal here, but a plain swing is not enough - an ability has to fire before the attacks to open the door.",
                Category.PlayBeforeAttack => "There is exactly lethal here, but not with the board as-is - something has to hit the field first to reach it.",
                Category.DonAllocation => "You have the attackers you need; the whole puzzle is HOW you spread your DON!! so every swing gets through.",
                Category.CounterAware => "There is exactly lethal, but they can stop one swing. Count your reach against what they can hold back.",
                _ => "Everything you need is already on the board. The ORDER you attack in is the trick - remember taking Life hands them a card.",
            };
            l1 += $" You need {requiredHits} hit(s) to connect (their Life + 1).";
            hints.Add(new Hint { Level = 1, Text = l1 });

            string keyId = KeyCardInstance(mine, attacks);
            string keyName = CardData.GetCard(InstanceCardId(pos, keyId))?.Name ?? "your key piece";
            string l2 = cat switch
            {
                Category.AbilityFirst => $"Look at {keyName}. Its ability, not its attack, is what unlocks the line - use it before you start swinging.",
                Category.PlayBeforeAttack => $"Look at {keyName}. Getting it onto the field first is what gives you enough attacks - set it up before you attack.",
                Category.DonAllocation => $"{keyName} is where your DON!! wants to go. Give it enough to punch past what they can defend, then attack.",
                Category.CounterAware => $"They can only stop one swing. Lead with the attack they most want to stop, and make sure {keyName} still gets there.",
                _ => $"Start with {keyName}, and mind the sequence - the wrong order lets them survive on the last Life.",
            };
            var l2h = new Hint { Level = 2, Text = l2 };
            if (keyId != null) l2h.HighlightInstanceIds.Add(keyId);
            hints.Add(l2h);

            var firstMoves = mine.Take(2).ToList();
            string l3 = "Do this: " + string.Join(", then ", firstMoves.Select(m => DescribeMove(pos, attacker, m)));
            if (mine.Count > firstMoves.Count) l3 += ", and continue the swings from there.";
            var l3h = new Hint { Level = 3, Text = l3 };
            foreach (var m in firstMoves) { var id = MoveCardInstance(m); if (id != null) l3h.HighlightInstanceIds.Add(id); }
            hints.Add(l3h);

            return hints;
        }

        /// <summary>Graduated hints for a proven multi-turn line. These frame the setup/defense/finish phases
        /// without pretending the puzzle is a one-turn damage-count exercise.</summary>
        public static List<Hint> GenerateForced(GameState pos, string player, ForcedWinSolver.Result solved)
        {
            var hints = new List<Hint>();
            if (solved == null || solved.Outcome != ForcedWinSolver.ForcedWin.Win) return hints;

            var mine = solved.PrincipalVariation
                .Where(c => c != null && (c.Seat == null || c.Seat == player) && IsPlayerFacing(c))
                .ToList();
            if (mine.Count == 0) return hints;

            int pass = mine.FindIndex(c => c.Type == "endTurn");
            bool hasDefense = mine.Any(c => c.Type == "counterWithCard" || c.Type == "blockAttack"
                                         || c.Type == "useTrigger" || c.Type == "passCounter");
            string phaseText = pass >= 0
                ? "The winning sequence crosses a turn boundary: build the next-turn position before passing, then preserve the resources that make the finish forced."
                : "The forced win spans more than one turn. Treat survival, board development and the eventual lethal as one sequence.";
            if (hasDefense) phaseText += " Your defensive choice during their turn is part of the solution.";
            hints.Add(new Hint { Level = 1, Text = phaseText });

            var key = mine.FirstOrDefault(c => c.Type != "endTurn") ?? mine[0];
            string keyId = MoveCardInstance(key);
            string keyName = CardData.GetCard(InstanceCardId(pos, keyId))?.Name ?? "your first key piece";
            var h2 = new Hint
            {
                Level = 2,
                Text = $"Start by identifying what {keyName} preserves or enables for the following turn. Spending that resource in the obvious way may lose the forced finish.",
            };
            if (keyId != null) h2.HighlightInstanceIds.Add(keyId);
            hints.Add(h2);

            var firstMoves = mine.Take(Math.Min(3, mine.Count)).ToList();
            var h3 = new Hint
            {
                Level = 3,
                Text = "Begin with: " + string.Join(", then ", firstMoves.Select(m => DescribeMove(pos, player, m))) + ".",
            };
            foreach (var m in firstMoves)
            {
                var id = MoveCardInstance(m);
                if (id != null) h3.HighlightInstanceIds.Add(id);
            }
            hints.Add(h3);
            return hints;
        }

        private static string KeyCardInstance(List<GameCommand> mine, List<GameCommand> attacks)
        {
            var setup = mine.FirstOrDefault(c => c.Type == "activateMain" || c.Type == "playCard" || c.Type == "attachDon");
            if (setup != null) return MoveCardInstance(setup);
            return attacks.Count > 0 ? attacks[0].Attacker : (mine.Count > 0 ? MoveCardInstance(mine[0]) : null);
        }

        private static string MoveCardInstance(GameCommand c) => c.Type switch
        {
            "declareAttack" => c.Attacker,
            "attachDon" => c.Target,
            "activateMain" => c.Target,
            "playCard" => c.InstanceId,
            "blockAttack" => c.Blocker,
            _ => c.InstanceId ?? c.Target,
        };

        /// <summary>The full proven winning line as human-readable, numbered attacker steps — used to reveal
        /// the answer after the player strikes out (3 losses). Names resolve against the STARTING position
        /// (every instance the line references exists there). Returns null if no forced win is found.</summary>
        public static string DescribeSolution(GameState pos, string attacker)
            => DescribeSolution(pos, attacker, 1);

        public static string DescribeSolution(GameState pos, string attacker, int playerTurnLimit)
        {
            if (playerTurnLimit <= 1) return DescribeOneTurnSolution(pos, attacker);
            var proof = ForcedWinSolver.Solve(pos, attacker, new ForcedWinSolver.Options
            {
                WorkBudget = 2_000_000,
                MaxPlayerTurns = playerTurnLimit,
            });
            if (proof.Outcome != ForcedWinSolver.ForcedWin.Win) return null;
            var mine = proof.PrincipalVariation
                .Where(c => (c.Seat == null || c.Seat == attacker) && IsPlayerFacing(c))
                .ToList();
            if (mine.Count == 0) return null;
            return string.Join("\n", mine.Select((m, i) => $"{i + 1}. " + Cap(DescribeMove(pos, attacker, m))));
        }

        private static bool IsPlayerFacing(GameCommand c) => c.Type == "playCard"
            || c.Type == "activateMain" || c.Type == "attachDon" || c.Type == "declareAttack"
            || c.Type == "counterWithCard" || c.Type == "blockAttack" || c.Type == "useTrigger"
            || c.Type == "passCounter" || c.Type == "passBlock" || c.Type == "endTurn";

        private static string DescribeOneTurnSolution(GameState pos, string attacker)
        {
            var proof = LethalSolver.Solve(pos, attacker, new LethalSolver.Options { WorkBudget = 400_000 });
            if (proof.Outcome != LethalSolver.Lethal.Win) return null;
            // Only the player-facing actions — drop internal resolution steps (resolveEffect/passEffect/choices).
            var mine = proof.PrincipalVariation
                .Where(c => (c.Seat == null || c.Seat == attacker)
                         && (c.Type == "playCard" || c.Type == "activateMain" || c.Type == "attachDon" || c.Type == "declareAttack"))
                .ToList();
            if (mine.Count == 0) return null;
            return string.Join("\n", mine.Select((m, i) => $"{i + 1}. " + Cap(DescribeMove(pos, attacker, m))));
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        private static string DescribeMove(GameState s, string seat, GameCommand c)
        {
            string Name(string instId) => CardData.GetCard(InstanceCardId(s, instId))?.Name ?? "that card";
            switch (c.Type)
            {
                case "declareAttack":
                    string tgt = c.Target == s.Players[GameEngine.OtherSeat(seat)].Leader?.InstanceId
                        ? "their Leader" : $"their {Name(c.Target)}";
                    return $"attack {tgt} with {Name(c.Attacker)}";
                case "attachDon": return $"give a DON!! to {Name(c.Target)}";
                case "activateMain": return $"use {Name(c.Target)}'s ability";
                case "playCard": return $"play {Name(c.InstanceId)}";
                case "counterWithCard": return $"counter with {Name(c.InstanceId)}";
                case "blockAttack": return $"block with {Name(c.Blocker)}";
                case "useTrigger": return "use the revealed Trigger";
                case "passCounter": return "take no further Counter";
                case "passBlock": return "do not block";
                case "endTurn": return "end your turn";
                default: return c.Type;
            }
        }

        private static bool DefenderHasCounter(GameState s, string attacker)
        {
            var d = s.Players[GameEngine.OtherSeat(attacker)];
            return d.Hand.Any(c => (CardData.GetCard(c.CardId)?.Counter ?? 0) > 0);
        }

        private static string InstanceCardId(GameState s, string instId)
        {
            if (string.IsNullOrEmpty(instId)) return null;
            foreach (var p in s.Players.Values)
            {
                if (p.Leader?.InstanceId == instId) return p.Leader.CardId;
                foreach (var c in p.CharacterArea) if (c != null && c.InstanceId == instId) return c.CardId;
                foreach (var c in p.Hand) if (c.InstanceId == instId) return c.CardId;
            }
            return null;
        }
    }
}
