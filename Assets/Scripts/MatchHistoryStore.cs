// One Piece TCG - Account-persistent match history (Unity Cloud Save Player Data).
// Where ReplayStore keeps the full deterministic replay on the local disk, this store
// keeps a compact, render-ready SUMMARY of each finished match on the player's account,
// so history follows them across devices. One Cloud Save key ("matchHistory") holds a
// JSON array of the newest 20 summaries — small enough to round-trip on every save
// (action text is truncated, decklists are id+count pairs; display data like names,
// colors, and art are resolved from the local card library at render time, never stored).
//
// Everything here is fire-and-forget-safe by design: offline play must never break
// match end, so every service call is wrapped in try/catch + Debug.LogWarning, and
// guests (AccountManager.IsGuest) skip the cloud entirely — they keep the local-only
// history that ReplayStore already provides.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave;
using UnityEngine;
using OnePieceTcg.Engine;

// JsonUtility can't serialize a top-level array, so the summaries ride inside
// MatchHistoryFile. All shapes below are deliberately flat/plain-field so
// JsonUtility handles them without any DTO mirroring (compare SerializableCommand).

/// <summary>One classified action inside a turn. s = "you"/"opp", k = tag key
/// (draw|don|play|event|attack|block|counter|life|ko), t = display text (≤ ~90 chars).</summary>
[Serializable]
public sealed class MatchAct
{
    public string s;
    public string k;
    public string t;
}

/// <summary>One turn of the compact log: turn number, whose turn it was, its actions.</summary>
[Serializable]
public sealed class MatchTurn
{
    public int turn;
    public string active;   // "you" | "opp"
    public List<MatchAct> acts = new List<MatchAct>();
}

/// <summary>Compact decklist entry — card id + copy count. Name/cost/color/type/counter
/// are looked up in the local official-card-library at render time, not stored.</summary>
[Serializable]
public sealed class MatchDeckCount
{
    public string cardId;
    public int count;
}

/// <summary>Everything the Match History list + detail views need for one match,
/// already rotated into the local player's perspective ("you" vs "opp").</summary>
[Serializable]
public sealed class MatchSummary
{
    public string id;              // == ReplayRecord.Id, so local replays can be matched up
    public string savedAtIso;
    public string result;          // "win" | "loss"
    public string mode;            // "ranked"|"casual"|"custom"|"ai"|"self" — game type (older records: null)
    public string youLeaderId;
    public string oppLeaderId;
    public string oppName;
    public bool youFirst;
    public int youFinalLife;
    public int oppFinalLife;
    public int youMaxLife;         // leader base Life (pip total)
    public int oppMaxLife;
    public int turnCount;
    public int durationSeconds;
    public List<MatchTurn> turns = new List<MatchTurn>();
    public List<MatchDeckCount> youDeck = new List<MatchDeckCount>();
    public List<MatchDeckCount> oppDeck = new List<MatchDeckCount>();
}

[Serializable]
public sealed class MatchHistoryFile
{
    public List<MatchSummary> matches = new List<MatchSummary>();
}

public static class MatchHistoryStore
{
    private const string CloudSaveKey = "matchHistory";
    private const int MaxEntries = 20;
    private const int MaxActionTextChars = 90;   // keeps 20 matches comfortably under Cloud Save's per-key limit

    // In-memory cache so navigating in and out of the Match History stage doesn't
    // hammer Cloud Save. LoadAsync(forceRefresh: true) bypasses it (used right
    // after a match ends / when the player re-enters the stage).
    private static List<MatchSummary> _cache;

    /// <summary>Fired after a successful save so any open history UI can repaint.</summary>
    public static event Action Changed;

    // ── Load ──────────────────────────────────────────────────────────────────

    public static async Task<List<MatchSummary>> LoadAsync(bool forceRefresh = false)
    {
        if (AccountManager.IsGuest) return new List<MatchSummary>();   // guests: local-only history
        if (_cache != null && !forceRefresh) return new List<MatchSummary>(_cache);

        try
        {
            await AccountManager.EnsureReadyAsync();
            var results = await CloudSaveService.Instance.Data.Player.LoadAsync(
                new HashSet<string> { CloudSaveKey });
            if (results.TryGetValue(CloudSaveKey, out var item))
            {
                string json = item.Value.GetAs<string>();
                var file = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<MatchHistoryFile>(json);
                _cache = file?.matches ?? new List<MatchSummary>();
            }
            else
            {
                _cache = new List<MatchSummary>();   // no history saved yet
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Match history load failed: {ex.Message}");
            return _cache != null ? new List<MatchSummary>(_cache) : new List<MatchSummary>();
        }
        return new List<MatchSummary>(_cache);
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>Prepends `summary` to the account's history (newest first, capped at
    /// 20, deduped by id) and writes the whole list back as one Cloud Save key.
    /// Never throws — meant to be fire-and-forgotten from the match-end path.</summary>
    public static async Task SaveMatchAsync(MatchSummary summary)
    {
        if (summary == null) return;
        if (AccountManager.IsGuest) return;   // guests keep local-only history via ReplayStore

        try
        {
            // Read-modify-write against the freshest server copy (another device
            // may have played since we cached), then refresh the cache from what
            // we actually wrote.
            var current = await LoadAsync(forceRefresh: true);
            current.RemoveAll(m => m != null && m.id == summary.id);
            current.Insert(0, summary);
            if (current.Count > MaxEntries)
                current.RemoveRange(MaxEntries, current.Count - MaxEntries);

            string json = JsonUtility.ToJson(new MatchHistoryFile { matches = current });
            await CloudSaveService.Instance.Data.Player.SaveAsync(
                new Dictionary<string, object> { { CloudSaveKey, json } });

            _cache = current;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Match history save failed: {ex.Message}");
        }
    }

    // ── Summary construction (called from GameManager at the replay-save site) ──

    /// <summary>Builds a MatchSummary from the finished GameState + its just-saved
    /// ReplayRecord, rotated into `youSeat`'s perspective ("south" for solo play,
    /// the local seat for networked matches — mirrors how the replay itself is
    /// saved from the local viewpoint).</summary>
    public static MatchSummary BuildSummary(GameState state, ReplayRecord record, string youSeat)
    {
        if (state == null || record == null) return null;
        try
        {
            string oppSeat = youSeat == "north" ? "south" : "north";
            if (youSeat != "north") youSeat = "south";
            state.Players.TryGetValue(youSeat, out var you);
            state.Players.TryGetValue(oppSeat, out var opp);

            string youLeaderId = you?.Leader?.CardId;
            string oppLeaderId = opp?.Leader?.CardId;

            var summary = new MatchSummary
            {
                id = record.Id,
                savedAtIso = record.SavedAtIso,
                // WinnerName comes from the "{name} wins." log line — the same name
                // the engine put on PlayerState.Name, so a straight compare works.
                result = !string.IsNullOrEmpty(record.WinnerName) && record.WinnerName == you?.Name
                    ? "win" : "loss",
                youLeaderId = youLeaderId,
                oppLeaderId = oppLeaderId,
                oppName = string.IsNullOrEmpty(opp?.Name) ? "Opponent" : opp.Name,
                youFirst = state.FirstPlayer == youSeat,
                youFinalLife = you?.Life?.Count ?? 0,
                oppFinalLife = opp?.Life?.Count ?? 0,
                youMaxLife = LeaderBaseLife(youLeaderId),
                oppMaxLife = LeaderBaseLife(oppLeaderId),
                turnCount = record.TurnCount,
                durationSeconds = record.DurationSeconds,
                turns = BuildTurnLog(state, youSeat),
                youDeck = BuildDeckCounts(youSeat == "south" ? record.SouthDeckId : record.NorthDeckId),
                oppDeck = BuildDeckCounts(youSeat == "south" ? record.NorthDeckId : record.SouthDeckId),
            };
            return summary;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Match summary build failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Leader base Life from the engine card registry; 5 when unknown
    /// (the most common base life), so pips always render something sane.</summary>
    public static int LeaderBaseLife(string leaderId)
    {
        try
        {
            var def = string.IsNullOrEmpty(leaderId) ? null : CardData.GetCard(leaderId);
            if (def != null && def.Life.HasValue && def.Life.Value > 0) return def.Life.Value;
        }
        catch { /* registry not loaded yet — fall through */ }
        return 5;
    }

    // Compact decklist from the deck-builder store. Empty when the deck id is
    // missing/deleted (e.g. the ST01-vs-ST02 default matchup stores no ids) —
    // the Decklists tab shows a friendly "no decklist recorded" note instead.
    private static List<MatchDeckCount> BuildDeckCounts(string deckId)
    {
        var result = new List<MatchDeckCount>();
        try
        {
            var deck = string.IsNullOrEmpty(deckId) ? null : DeckStore.Get(deckId);
            if (deck?.cards != null)
                foreach (var e in deck.cards)
                    if (e != null && e.count > 0 && !string.IsNullOrEmpty(e.id))
                        result.Add(new MatchDeckCount { cardId = e.id, count = e.count });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Decklist snapshot failed: {ex.Message}");
        }
        return result;
    }

    // ── Turn log ──────────────────────────────────────────────────────────────

    /// <summary>Groups the finished EventLog by LogEntry.Turn and classifies each
    /// message into a tag key. Turn-0 setup chatter ("Match created", mulligans)
    /// is skipped; "system" entries are attributed to the turn's active seat.</summary>
    public static List<MatchTurn> BuildTurnLog(GameState state, string youSeat)
    {
        var turns = new List<MatchTurn>();
        if (state?.EventLog == null) return turns;

        MatchTurn current = null;
        foreach (var e in state.EventLog)
        {
            if (e == null || e.Turn <= 0 || string.IsNullOrEmpty(e.Message)) continue;
            if (current == null || current.turn != e.Turn)
            {
                current = new MatchTurn
                {
                    turn = e.Turn,
                    // Turn parity determines the active seat: FirstPlayer takes the
                    // odd turns. (TurnNumber increments per player turn, not round.)
                    active = ActiveSeatForTurn(state, e.Turn) == youSeat ? "you" : "opp",
                };
                turns.Add(current);
            }

            string actorSeat = e.Actor == "south" || e.Actor == "north"
                ? e.Actor
                : ActiveSeatForTurn(state, e.Turn);   // "system" lines belong to the active player
            string text = e.Message.Length > MaxActionTextChars
                ? e.Message.Substring(0, MaxActionTextChars - 1) + "…"
                : e.Message;
            current.acts.Add(new MatchAct
            {
                s = actorSeat == youSeat ? "you" : "opp",
                k = ClassifyLogMessage(e.Message),
                t = text,
            });
        }
        return turns;
    }

    private static string ActiveSeatForTurn(GameState state, int turn)
    {
        string first = state.FirstPlayer == "north" ? "north" : "south";
        string second = first == "south" ? "north" : "south";
        return (turn % 2 == 1) ? first : second;
    }

    /// <summary>Keyword classifier: LogEntry.Message → tag key. Checked most-specific
    /// first (a "K.O." line usually also contains "attack"/"damage" phrasing, so KO
    /// must win). Anything unrecognized falls back to "event" — the neutral chip.</summary>
    public static string ClassifyLogMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return "event";
        string m = message.ToLowerInvariant();

        if (m.Contains("k.o.") || m.Contains("ko'd") || m.Contains("knocked out")) return "ko";
        if (m.Contains("counter"))                                                  return "counter";
        if (m.Contains("block"))                                                    return "block";
        if (m.Contains("damage") || m.Contains("life"))                             return "life";
        if (m.Contains("attack"))                                                   return "attack";
        if (m.Contains("don"))                                                      return "don";
        if (m.Contains("draw") || m.Contains("drew"))                               return "draw";
        if (m.Contains("play"))                                                     return "play";
        if (m.Contains("activat") || m.Contains("trigger") || m.Contains("effect")) return "event";
        return "event";
    }
}
