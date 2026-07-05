// One Piece TCG - Lifetime + seasonal account stats.
//
// Match history (MatchHistoryStore) is capped at 20 entries, so anything
// "for the life of an account" must be a RUNNING AGGREGATE incremented once
// per finished match, never recomputed from history. This store keeps one
// Cloud Save player-data key per scope:
//
//   stats_lifetime   - everything since the account existed
//   stats_s<N>       - one bucket per season (see SeasonIdForUtc below)
//
// Both are the same StatsBucket shape, so UI code renders either scope with
// the same path. Blobs are a few KB even with dozens of leaders tracked.
//
// Write path: GameManager.SaveFinishedMatchRecords() -> RecordMatchAsync(summary),
// right next to the MatchHistoryStore save, using the MatchSummary it already
// builds - stats never need their own view of GameState.
//
// TRUST MODEL (deliberate, revisit before ranked ships): increments are done
// client-side and written with the player's own Cloud Save access, which is
// fine for casual/solo but trivially forgeable. When ranked exists, move the
// read-modify-write into a Cloud Code endpoint (e.g. RecordMatchResult.js,
// cross-checking both players' reports) and this class's public API can stay
// identical - only the transport changes. Call sites won't move.
//
// Guests: skipped entirely, same policy as MatchHistoryStore. Guest sessions
// are display-only identities with nothing account-shaped to attach stats to.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave;
using UnityEngine;

// ── Serializable models (JsonUtility: no dictionaries, so breakdowns are lists) ──

[Serializable]
public sealed class LeaderStat
{
    public string leaderId;
    public int games;
    public int wins;
}

/// <summary>Per-(own leader, opponent leader) running record — feeds the My Profile
/// Deck History matchup grid. Added after launch: older buckets deserialize with an
/// empty list (JsonUtility default), so the grid starts filling from the first match
/// played after this shipped.</summary>
[Serializable]
public sealed class MatchupStat
{
    public string ownLeaderId;
    public string oppLeaderId;
    public int games;
    public int wins;
}

/// <summary>Per-(UTC month, own leader) running record — feeds the My Profile
/// Deck History lifetime chart (games bars + win-rate line). ym is "yyyy-MM".</summary>
[Serializable]
public sealed class MonthStat
{
    public string ym;
    public string leaderId;
    public int games;
    public int wins;
}

[Serializable]
public sealed class StatsBucket
{
    public int games;
    public int wins;
    public int losses;
    public int totalTurns;
    public int totalDurationSeconds;

    // Going first vs second, for "does the coin flip matter for me" stats.
    public int firstGames;
    public int firstWins;

    public int currentStreak;   // >0 = winning streak, <0 = losing streak
    public int bestWinStreak;

    // Breakdown by the leader YOU played (deck identity) and by the leader you
    // FACED (matchup winrates). Linear scans are fine at this scale (~50 ids).
    public List<LeaderStat> byOwnLeader = new List<LeaderStat>();
    public List<LeaderStat> byOpponentLeader = new List<LeaderStat>();

    // Deck-scoped detail for the My Profile Deck History tab: per-(own leader ×
    // opp leader) matchup records, and per-(month × own leader) play/win counts.
    // Same running-aggregate rule as everything else in this file: incremented
    // once per finished match in Apply(), never recomputed from history.
    public List<MatchupStat> matchups = new List<MatchupStat>();
    public List<MonthStat> months = new List<MonthStat>();

    public float WinRate => games == 0 ? 0f : (float)wins / games;
}

public static class StatsStore
{
    private const string LifetimeKey = "stats_lifetime";

    // ── Seasons ─────────────────────────────────────────────────────────────
    // Hardcoded windows for now - the mapping function is the only thing UI or
    // write-path code touches, so moving this table to Remote Config later is a
    // drop-in change. End dates are exclusive. UTC everywhere so two players in
    // different timezones agree on which season a match belongs to.
    private static readonly (int id, DateTime startUtc, DateTime endUtc)[] Seasons =
    {
        (1, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
        (2, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        (3, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
    };

    // 0 = "no active season" (before S1 / gap in the table): seasonal write is
    // skipped, lifetime still counts.
    public static int SeasonIdForUtc(DateTime utcNow)
    {
        foreach (var s in Seasons)
            if (utcNow >= s.startUtc && utcNow < s.endUtc) return s.id;
        return 0;
    }

    public static int CurrentSeasonId => SeasonIdForUtc(DateTime.UtcNow);
    private static string SeasonKey(int seasonId) => $"stats_s{seasonId}";

    // ── Cache + change signal (UI subscribes the same way as MatchHistoryStore) ──
    private static readonly Dictionary<string, StatsBucket> _cache = new Dictionary<string, StatsBucket>();
    public static event Action Changed;

    // ── Read API ────────────────────────────────────────────────────────────

    public static Task<StatsBucket> LoadLifetimeAsync(bool forceRefresh = false)
        => LoadBucketAsync(LifetimeKey, forceRefresh);

    public static Task<StatsBucket> LoadSeasonAsync(int seasonId, bool forceRefresh = false)
        => LoadBucketAsync(SeasonKey(seasonId), forceRefresh);

    private static async Task<StatsBucket> LoadBucketAsync(string key, bool forceRefresh)
    {
        if (!forceRefresh && _cache.TryGetValue(key, out var cached)) return cached;

        var bucket = new StatsBucket();
        try
        {
            if (!AccountManager.IsGuest)
            {
                await AccountManager.EnsureReadyAsync();
                var results = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { key });
                if (results.TryGetValue(key, out var item))
                {
                    var parsed = JsonUtility.FromJson<StatsBucket>(item.Value.GetAs<string>());
                    if (parsed != null) bucket = parsed;
                }
            }
        }
        catch (Exception ex)
        {
            // Offline or not signed in: an empty bucket renders as "no stats yet",
            // which is the least-wrong thing to show. Not cached, so the next call
            // retries the network.
            Debug.LogWarning($"StatsStore load '{key}' failed: {ex.Message}");
            return bucket;
        }

        _cache[key] = bucket;
        return bucket;
    }

    // ── Write API (the one hook GameManager calls) ──────────────────────────

    // Increments lifetime + current-season buckets from a finished match.
    // Never throws: stats must not be able to break match end. Fire-and-forget
    // from SaveFinishedMatchRecords, same as the match-history upload.
    public static async Task RecordMatchAsync(MatchSummary summary)
    {
        if (summary == null) return;
        if (AccountManager.IsGuest) return; // nothing account-shaped to attach to

        try
        {
            await AccountManager.EnsureReadyAsync();

            // Read-modify-write both buckets in one round trip each way. Two keys
            // written together keeps season/lifetime consistent barring a crash
            // between them - acceptable until this moves server-side for ranked.
            int seasonId = CurrentSeasonId;
            var keys = new HashSet<string> { LifetimeKey };
            if (seasonId > 0) keys.Add(SeasonKey(seasonId));

            var loaded = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);
            var toWrite = new Dictionary<string, object>();

            foreach (var key in keys)
            {
                var bucket = new StatsBucket();
                if (loaded.TryGetValue(key, out var item))
                {
                    var parsed = JsonUtility.FromJson<StatsBucket>(item.Value.GetAs<string>());
                    if (parsed != null) bucket = parsed;
                }
                Apply(bucket, summary);
                _cache[key] = bucket;
                toWrite[key] = JsonUtility.ToJson(bucket);
            }

            await CloudSaveService.Instance.Data.Player.SaveAsync(toWrite);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"StatsStore record failed (stats skipped for this match): {ex.Message}");
        }
    }

    // Pure increment logic, separated so a future Cloud Code port can mirror it
    // line-for-line in JS (and so it's unit-testable without services).
    public static void Apply(StatsBucket bucket, MatchSummary summary)
    {
        bool won = summary.result == "win";

        bucket.games++;
        if (won) bucket.wins++; else bucket.losses++;
        bucket.totalTurns += Mathf.Max(0, summary.turnCount);
        bucket.totalDurationSeconds += Mathf.Max(0, summary.durationSeconds);

        if (summary.youFirst)
        {
            bucket.firstGames++;
            if (won) bucket.firstWins++;
        }

        // Streak: same-direction extends, opposite direction restarts at ±1.
        bucket.currentStreak = won
            ? (bucket.currentStreak > 0 ? bucket.currentStreak + 1 : 1)
            : (bucket.currentStreak < 0 ? bucket.currentStreak - 1 : -1);
        if (bucket.currentStreak > bucket.bestWinStreak) bucket.bestWinStreak = bucket.currentStreak;

        Bump(bucket.byOwnLeader, summary.youLeaderId, won);
        Bump(bucket.byOpponentLeader, summary.oppLeaderId, won);
        BumpMatchup(bucket, summary.youLeaderId, summary.oppLeaderId, won);
        BumpMonth(bucket, summary.youLeaderId, MonthFromIso(summary.savedAtIso), won);
    }

    // "yyyy-MM" (UTC) from the summary's save timestamp; falls back to the
    // current UTC month for malformed/missing timestamps so the increment is
    // never dropped.
    public static string MonthFromIso(string iso)
    {
        if (!string.IsNullOrEmpty(iso) &&
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            return dt.ToString("yyyy-MM");
        return DateTime.UtcNow.ToString("yyyy-MM");
    }

    private static void BumpMatchup(StatsBucket bucket, string ownLeaderId, string oppLeaderId, bool won)
    {
        if (string.IsNullOrEmpty(ownLeaderId) || string.IsNullOrEmpty(oppLeaderId)) return;
        if (bucket.matchups == null) bucket.matchups = new List<MatchupStat>();
        var entry = bucket.matchups.Find(e => e.ownLeaderId == ownLeaderId && e.oppLeaderId == oppLeaderId);
        if (entry == null)
        {
            entry = new MatchupStat { ownLeaderId = ownLeaderId, oppLeaderId = oppLeaderId };
            bucket.matchups.Add(entry);
        }
        entry.games++;
        if (won) entry.wins++;
    }

    private static void BumpMonth(StatsBucket bucket, string leaderId, string ym, bool won)
    {
        if (string.IsNullOrEmpty(leaderId) || string.IsNullOrEmpty(ym)) return;
        if (bucket.months == null) bucket.months = new List<MonthStat>();
        var entry = bucket.months.Find(e => e.ym == ym && e.leaderId == leaderId);
        if (entry == null)
        {
            entry = new MonthStat { ym = ym, leaderId = leaderId };
            bucket.months.Add(entry);
        }
        entry.games++;
        if (won) entry.wins++;
    }

    private static void Bump(List<LeaderStat> list, string leaderId, bool won)
    {
        if (string.IsNullOrEmpty(leaderId)) return; // pre-feature replays lack leader ids
        var entry = list.Find(e => e.leaderId == leaderId);
        if (entry == null)
        {
            entry = new LeaderStat { leaderId = leaderId };
            list.Add(entry);
        }
        entry.games++;
        if (won) entry.wins++;
    }
}
