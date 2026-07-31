// One Piece TCG — Sealed / Pre-Release: saving and restoring a run.
//
// A sealed run is meant to be revisitable forever — "nothing gets deleted", so you can come back to
// a pool, re-edit the deck, and still see exactly what came out of pack 4. That means the SEED and
// the DECK are the only things worth persisting: the packs themselves are a pure function of
// (set, seed), so re-generating them on load is both smaller on disk and guaranteed consistent with
// whatever the generator does today.
//
// JsonUtility cannot serialize Dictionary, so the deck round-trips through parallel id/count lists.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OnePieceTcg.Sealed
{
    [Serializable]
    public sealed class SealedRunRecord
    {
        public string Id;
        public string CreatedAtIso;
        public string SetCode;
        public string Seed;
        public int LeaderMode;              // SealedLeaderMode
        public string LeaderId;
        public List<string> DeckCardIds = new List<string>();
        public List<int> DeckCounts = new List<int>();
        public string Label;                // player-facing name, e.g. "OP16 · seed 842918361"

        // Event progress, when the run is part of one. Zero/empty for a casual run.
        public int EventRound;
        public int EventWins, EventLosses, EventDraws;
        public bool EventFinished;
    }

    [Serializable]
    internal sealed class SealedRunFile
    {
        public List<SealedRunRecord> runs = new List<SealedRunRecord>();
    }

    public static class SealedStore
    {
        private const int MaxRuns = 50;

        private static string Dir => Path.Combine(Application.persistentDataPath, "Sealed");
        private static string FilePath => Path.Combine(Dir, "runs.json");

        private static SealedRunFile cache;

        public static List<SealedRunRecord> All()
        {
            Load();
            return cache.runs.OrderByDescending(r => r.CreatedAtIso, StringComparer.Ordinal).ToList();
        }

        public static SealedRunRecord Get(string id)
        {
            Load();
            return cache.runs.FirstOrDefault(r => r.Id == id);
        }

        private static void Load()
        {
            if (cache != null) return;
            loadFailed = false;
            try
            {
                string json = OnePieceTcg.Engine.SafeFile.ReadWithRecovery(FilePath, out _, out bool failed);
                if (json != null)
                {
                    cache = JsonUtility.FromJson<SealedRunFile>(json);
                    // FromJson yields null for malformed input WITHOUT throwing, so an unparseable
                    // file would otherwise look like "no runs" and get overwritten by the next save.
                    if (cache == null) { cache = new SealedRunFile(); loadFailed = true; }
                }
                else { cache = new SealedRunFile(); loadFailed = failed; }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SealedStore] Could not read runs: {e.Message}");
                cache = new SealedRunFile();
                loadFailed = true;
            }
            cache.runs ??= new List<SealedRunRecord>();
        }

        /// Set when a runs file EXISTS but could not be read; Flush() then refuses to write, so a
        /// corrupt file is never overwritten with an empty one. Same aggregate-file hazard as
        /// DeckStore: everything lives in a single JSON, so one bad save costs every saved run.
        private static bool loadFailed;

        private static void Flush()
        {
            if (loadFailed)
            {
                Debug.LogError("[SealedStore] Refusing to save over an unreadable runs file "
                             + $"(would destroy existing runs). Recover {FilePath} or its .bak.");
                return;
            }
            // Atomic swap + .bak, instead of truncating the live file in place.
            OnePieceTcg.Engine.SafeFile.WriteAtomic(FilePath, JsonUtility.ToJson(cache, true));
        }

        /// <summary>Capture a pool as a record. The packs are NOT stored — they regenerate from the
        /// seed, which is what keeps a run replayable and the file small.</summary>
        public static SealedRunRecord ToRecord(SealedPool pool, string id = null, string label = null)
        {
            var rec = new SealedRunRecord
            {
                Id = id ?? NewId(),
                CreatedAtIso = DateTime.UtcNow.ToString("o"),
                SetCode = pool.SetCode,
                Seed = pool.Seed,
                LeaderMode = (int)pool.LeaderMode,
                LeaderId = pool.LeaderId,
                Label = label ?? $"{pool.SetCode} · seed {pool.Seed}",
            };
            foreach (var kv in pool.Deck.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                rec.DeckCardIds.Add(kv.Key);
                rec.DeckCounts.Add(kv.Value);
            }
            return rec;
        }

        /// <summary>Rebuild a pool from a record: re-open the packs from the seed, then re-apply the
        /// saved deck. Cards no longer in the pool (a library change) are dropped rather than
        /// resurrected, so a restored deck can never contain something you did not open.</summary>
        public static SealedPool ToPool(SealedRunRecord rec)
        {
            if (rec == null) return null;
            var product = SealedCatalog.Find(rec.SetCode);
            if (product == null) return null;
            product.LeaderMode = (SealedLeaderMode)rec.LeaderMode;

            var pool = SealedPool.Generate(product, rec.Seed);
            pool.LeaderMode = (SealedLeaderMode)rec.LeaderMode;
            pool.LeaderId = rec.LeaderId;

            for (int i = 0; i < rec.DeckCardIds.Count && i < rec.DeckCounts.Count; i++)
                pool.Add(rec.DeckCardIds[i], rec.DeckCounts[i]);   // Add() clamps to what the pool holds

            return pool;
        }

        public static void Save(SealedRunRecord rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.Id)) return;
            Load();
            int at = cache.runs.FindIndex(r => r.Id == rec.Id);
            if (at >= 0) cache.runs[at] = rec; else cache.runs.Add(rec);

            // Keep the file bounded; oldest runs fall off first.
            if (cache.runs.Count > MaxRuns)
                cache.runs = cache.runs
                    .OrderByDescending(r => r.CreatedAtIso, StringComparer.Ordinal)
                    .Take(MaxRuns).ToList();

            Flush();
        }

        public static void Delete(string id)
        {
            Load();
            cache.runs.RemoveAll(r => r.Id == id);
            Flush();
        }

        public static string NewId() => "sealed_" + DateTime.UtcNow.Ticks.ToString("x");
    }
}
