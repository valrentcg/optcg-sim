using System.Collections.Generic;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// One compact row per finished game — the atomic unit of the simulation league (§11).
    /// Millions of these stream to sharded JSONL; keep it small and flat.
    /// </summary>
    public sealed class GameRecord
    {
        public string exp;          // experimentId
        public long g;              // global game index (deterministic)
        public string sDeck;        // south deck id
        public string nDeck;        // north deck id
        public string sAgent;       // south policy name
        public string nAgent;       // north policy name
        public string seed;         // full engine seed string (reproduces the game exactly)
        public string first;        // seat forced to go first: "south" | "north"
        public string winner;       // "south" | "north" | null (no result / cap)
        public string winnerAgent;  // policy name that won, or null
        public bool firstWon;       // did the player who went first win?
        public string end;          // "life" | "deckout" | "cap" | "stall" | "crash"
        public int turns;
        public int commands;        // total commands applied (deadlock/EV signal)
        public string error;        // exception summary when end == "crash"
    }

    /// <summary>
    /// One decision the required decision log (§12.1). Written only when the experiment enables
    /// decision logging, and typically SAMPLED — the full cross product of millions of games ×
    /// dozens of decisions is far too large to keep in whole. Enough to distil heuristics from.
    /// </summary>
    public sealed class DecisionRecord
    {
        public long g;              // game index (join key back to GameRecord)
        public int turn;
        public string phase;
        public string seat;
        public string agent;
        public string cmdType;      // chosen command type (semantic tag)
        public string cmdInstance;  // primary instance id involved, if any
        public string cmdTarget;
        // Cheap public-state features — the raw material for feature attribution / decision trees.
        public int sLife, nLife;    // life counts (public)
        public int sHand, nHand;    // hand SIZES only (never contents of the opponent hand — §13)
        public int sBoard, nBoard;  // characters in play
        public int sDon, nDon;      // active DON available
        public int sDeck, nDeck;    // cards left in deck
    }
}
