using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine.Bot.Search;   // LegalActions, GameClone

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>A stored puzzle: the metadata plus enough to reconstruct the exact position. `History` (the
    /// engine command log that reaches the position) is the source of truth for reproduction; the live
    /// position is materialized from it (or handed in directly for hand-authored puzzles). Serialization to
    /// disk/cloud is done by the client layer (GameCommand -> SerializableCommand), not here, so this type
    /// stays in the engine and is testable headlessly.</summary>
    public sealed class Puzzle
    {
        public string Id;
        public string Title;
        public string AttackerSeat = "south";   // the seat the player controls
        public string Category;                  // teaching tag (attack-order / don-allocation / ...)
        public int SolutionMoves;                // attacker moves in the canonical line (a difficulty proxy)
        public string CardVersion;
        // Deterministic reconstruction:
        public string Seed;
        public string SouthDeckId, NorthDeckId;
        public List<GameCommand> History = new List<GameCommand>();
    }

    /// <summary>
    /// The player-facing puzzle loop. The player controls the ATTACKER; the runtime auto-plays the defender's
    /// STRONGEST SURVIVING defense (the response that best refutes a non-lethal line), so the puzzle is honest
    /// — a wrong move lets the defender escape and the line fails. It accepts ANY verified winning line, not
    /// only an author's sequence: after each move it re-runs <see cref="LethalSolver"/> and reports whether a
    /// forced lethal is still available. Hints are the graduated, position-aware <see cref="HintGenerator"/>.
    ///
    /// All rules come from the real engine (LegalActions + ApplyCommand via clone). The runtime never trusts
    /// an author's "expected" answer — winning is defined solely by the engine reaching a finished state with
    /// the attacker as the winner.
    /// </summary>
    public sealed class PuzzleRuntime
    {
        public enum PuzzleStatus { NotStarted, InProgress, Solved, Failed }

        private GameState _state;
        private string _attacker;
        private readonly List<GameCommand> _playerMoves = new List<GameCommand>();

        public PuzzleStatus Status { get; private set; } = PuzzleStatus.NotStarted;
        public GameState State => _state;                 // live position (for the UI to render)
        public string Attacker => _attacker;
        public bool StillWinning { get; private set; }    // is a forced lethal still available from here?
        public string Message { get; private set; }       // short feedback for the UI
        public int HintsRevealed { get; private set; }
        public IReadOnlyList<GameCommand> PlayerMoves => _playerMoves;

        /// <summary>Options for live verification. The budget comfortably exceeds what the baked puzzles need to
        /// prove lethal (they certify in well under 80k work), so Start never reports a false "Unknown", while
        /// staying small enough that the solve is snappy.</summary>
        public LethalSolver.Options SolveOpts = new LethalSolver.Options { WorkBudget = 400_000 };

        /// <summary>Begin from a live position. Returns false (and marks Failed) if it is NOT a valid lethal
        /// puzzle — i.e. the solver cannot prove a forced win for the attacker. A valid puzzle must start Win.</summary>
        public bool Start(GameState position, string attackerSeat)
        {
            _attacker = attackerSeat;
            _state = GameClone.Clone(position);
            _playerMoves.Clear();
            HintsRevealed = 0;
            AdvanceThroughNonPlayer();
            var proof = LethalSolver.Solve(_state, _attacker, SolveOpts);
            StillWinning = proof.Outcome == LethalSolver.Lethal.Win;
            if (!StillWinning)
            {
                Status = PuzzleStatus.Failed;
                Message = proof.Outcome == LethalSolver.Lethal.Unknown
                    ? "Could not verify a forced win here (too complex for the budget)."
                    : "This position has no forced lethal.";
                return false;
            }
            Status = PuzzleStatus.InProgress;
            Message = "Find the lethal line.";
            return true;
        }

        /// <summary>The attacker's currently-legal moves (empty unless it is the player's decision).</summary>
        public List<GameCommand> LegalMoves()
        {
            if (Status != PuzzleStatus.InProgress) return new List<GameCommand>();
            if (LethalSolver.DecidingSeat(_state, _attacker) != _attacker) return new List<GameCommand>();
            return LegalActions.Validate(_state, _attacker, LegalActions.Candidates(_state, _attacker))
                               .Select(kv => kv.Key).ToList();
        }

        /// <summary>Play the chosen attacker move, then let the defender respond with its strongest surviving
        /// defense and auto-progress until it is the player's decision again, the turn ends, or the game ends.
        /// Returns false if the move was not legal.</summary>
        public bool ApplyMove(GameCommand cmd)
        {
            if (Status != PuzzleStatus.InProgress) return false;
            var legal = LegalActions.Validate(_state, _attacker, LegalActions.Candidates(_state, _attacker));
            var match = legal.FirstOrDefault(kv => SameCommand(kv.Key, cmd));
            if (match.Key == null) { Message = "That move isn't legal here."; return false; }

            _state = match.Value;              // already the applied clone
            _playerMoves.Add(match.Key);
            AdvanceThroughNonPlayer();
            UpdateStatus();
            return true;
        }

        // Auto-advance while it is NOT the player's decision. Defender decisions are resolved with the
        // strongest surviving defense; when control is the attacker's again (their own effect/target, or a
        // fresh main-phase decision) we stop so the player chooses.
        private void AdvanceThroughNonPlayer()
        {
            for (int guard = 0; guard < 500; guard++)
            {
                if (_state.Status == "finished") return;
                if (_state.ActiveSeat != _attacker) return;                 // turn ended
                string decider = LethalSolver.DecidingSeat(_state, _attacker);
                if (decider == _attacker) return;                          // player's decision
                var defense = StrongestDefense(_state, decider);
                if (defense.Key == null) return;                           // no legal defense
                _state = defense.Value;
            }
        }

        // Always defend OPTIMALLY, right to the end. A defense that REFUTES the attacker (NoLethal — only
        // possible after the player misplays) is best. Otherwise, among the losing defenses, play the one that
        // makes the attacker work HARDEST — i.e. actually block and counter to delay lethal instead of passing —
        // so the puzzle has a single optimal winning line and the bot resists until it takes the final blow.
        private KeyValuePair<GameCommand, GameState> StrongestDefense(GameState s, string defender)
        {
            var legal = LegalActions.Validate(s, defender, LegalActions.Candidates(s, defender));
            if (legal.Count == 0) return default;
            if (legal.Count == 1) return legal[0];

            var defenseOpts = new LethalSolver.Options { WorkBudget = 200_000 };
            KeyValuePair<GameCommand, GameState> best = default; int bestRank = -1; long bestWork = -1;
            foreach (var kv in legal)
            {
                var r = LethalSolver.Solve(kv.Value, _attacker, defenseOpts);
                int rank = r.Outcome == LethalSolver.Lethal.NoLethal ? 1 : 0;   // refute > any losing defense
                if (rank > bestRank || (rank == bestRank && r.Work > bestWork))
                { bestRank = rank; bestWork = r.Work; best = kv; }
            }
            return best;
        }

        private void UpdateStatus()
        {
            if (_state.Status == "finished")
            {
                bool won = LethalSolver.WinnerOf(_state) == _attacker;
                Status = won ? PuzzleStatus.Solved : PuzzleStatus.Failed;
                StillWinning = won;
                Message = won ? "Solved — that's lethal!" : "That line doesn't win.";
                return;
            }
            // Turn ended without lethal = the player is out of chances on this puzzle -> a real fail (this is
            // the ONLY mid-play fail; we deliberately do NOT re-solve after every move and tell the player
            // "that was wrong", which would hand them the answer like a multiple-choice question).
            if (_state.ActiveSeat != _attacker)
            {
                Status = PuzzleStatus.Failed; StillWinning = false;
                Message = "Out of lethal — your turn ended.";
                return;
            }
            Message = "Find the lethal line.";
        }

        /// <summary>A single graduated hint (1 obscure, 2 targeted, 3 explicit) for the CURRENT position, so
        /// hints adapt as the player progresses. Reveals up to `level` (tracks how many the player used).</summary>
        public HintGenerator.Hint Hint(int level)
        {
            HintsRevealed = Math.Max(HintsRevealed, level);
            var proof = LethalSolver.Solve(_state, _attacker, SolveOpts);
            return HintGenerator.Generate(_state, _attacker, proof).FirstOrDefault(h => h.Level == level);
        }

        /// <summary>Reset to a starting position and play again.</summary>
        public bool Restart(GameState position) => Start(position, _attacker);

        // Two commands are "the same move" if their action + the ids that identify it match. (Order-only
        // fields the engine fills in are ignored.)
        private static bool SameCommand(GameCommand a, GameCommand b)
        {
            if (a == null || b == null) return false;
            if (a.Type != b.Type) return false;
            return a.Attacker == b.Attacker && a.Target == b.Target && a.InstanceId == b.InstanceId
                && a.Blocker == b.Blocker && a.EffectId == b.EffectId
                && (a.Amount ?? 1) == (b.Amount ?? 1);
        }
    }
}
