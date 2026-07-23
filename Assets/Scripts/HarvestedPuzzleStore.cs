// Loads real-game HARVESTED puzzles (Tools/Sim `puzzleharvest` → StreamingAssets/Puzzles/harvested.json)
// into the puzzle set. Each entry is a reproduction recipe (seed + both deck lists + a command prefix) that
// HarvestedPuzzle.Build replays into the exact end-game position. These fill the Hard/Expert tiers that the
// procedurally-generated puzzles (relabelled down) no longer cover. Client-only (JsonUtility + StreamingAssets);
// the engine-side HarvestedPuzzle type is shared with the Sim so the SAME JSON round-trips both ways.

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using OnePieceTcg.Engine.Puzzles;

public static class HarvestedPuzzleStore
{
    private static List<AuthoredPuzzle> _cache;

    public static string FilePath => Path.Combine(Application.streamingAssetsPath, "Puzzles", "harvested.json");

    /// <summary>Harvested puzzles as AuthoredPuzzles (cached). Empty if the asset is missing or unreadable —
    /// the mode still works on the generated set alone.</summary>
    public static List<AuthoredPuzzle> All()
    {
        if (_cache != null) return _cache;
        _cache = new List<AuthoredPuzzle>();
        try
        {
            if (!File.Exists(FilePath)) return _cache;
            var set = JsonUtility.FromJson<HarvestedPuzzleSet>(File.ReadAllText(FilePath));
            if (set?.puzzles == null) return _cache;
            foreach (var hp in set.puzzles)
                if (hp != null) _cache.Add(hp.ToAuthored());
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[HarvestedPuzzleStore] Failed to load {FilePath}: {ex.Message}");
        }
        return _cache;
    }
}
