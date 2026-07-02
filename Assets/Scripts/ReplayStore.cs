// One Piece TCG - Replay persistence.
// A finished match is fully reproducible from {Seed, deck ids, CommandHistory} because
// GameEngine.CreateMatch/ApplyCommand are deterministic (seeded RNG, no wall-clock reads).
// Saving a replay is therefore just recording the command log, not the mutable GameState.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using OnePieceTcg.Engine;

// JsonUtility does not support nullable value types (int?/bool?), which GameCommand uses
// for Amount/SlotIndex/GoingFirst/Mulligan — serializing GameCommand directly would silently
// drop those fields. This DTO mirrors it with plain fields + "Has*" flags instead.
[Serializable]
public sealed class SerializableCommand
{
    public string Type;
    public string Seat;
    public string InstanceId;
    public string Target;
    public string Attacker;
    public string Blocker;
    public int Amount;
    public bool HasAmount;
    public int SlotIndex;
    public bool HasSlotIndex;
    public string EffectId;
    public bool GoingFirst;
    public bool HasGoingFirst;
    public bool Mulligan;
    public bool HasMulligan;
    public List<string> OrderedInstanceIds = new List<string>();
    public List<string> DonInstanceIds = new List<string>();

    public static SerializableCommand From(GameCommand c) => new SerializableCommand
    {
        Type = c.Type,
        Seat = c.Seat,
        InstanceId = c.InstanceId,
        Target = c.Target,
        Attacker = c.Attacker,
        Blocker = c.Blocker,
        Amount = c.Amount ?? 0,
        HasAmount = c.Amount.HasValue,
        SlotIndex = c.SlotIndex ?? 0,
        HasSlotIndex = c.SlotIndex.HasValue,
        EffectId = c.EffectId,
        GoingFirst = c.GoingFirst ?? false,
        HasGoingFirst = c.GoingFirst.HasValue,
        Mulligan = c.Mulligan ?? false,
        HasMulligan = c.Mulligan.HasValue,
        OrderedInstanceIds = c.OrderedInstanceIds != null ? new List<string>(c.OrderedInstanceIds) : null,
        DonInstanceIds = c.DonInstanceIds != null ? new List<string>(c.DonInstanceIds) : null,
    };

    public GameCommand ToCommand() => new GameCommand
    {
        Type = Type,
        Seat = Seat,
        InstanceId = InstanceId,
        Target = Target,
        Attacker = Attacker,
        Blocker = Blocker,
        Amount = HasAmount ? (int?)Amount : null,
        SlotIndex = HasSlotIndex ? (int?)SlotIndex : null,
        EffectId = EffectId,
        GoingFirst = HasGoingFirst ? (bool?)GoingFirst : null,
        Mulligan = HasMulligan ? (bool?)Mulligan : null,
        OrderedInstanceIds = OrderedInstanceIds,
        DonInstanceIds = DonInstanceIds,
    };
}

[Serializable]
public sealed class ReplayRecord
{
    public string Id;              // timestamp-sortable, also the filename stem
    public string SavedAtIso;
    public string Seed;
    public string SouthDeckId;     // deck-builder deck id; empty = starter default (st01)
    public string NorthDeckId;     // empty = starter default (st02)
    public string SouthDeckName;
    public string NorthDeckName;
    public string WinnerName;
    public int TurnCount;
    public List<SerializableCommand> CommandHistory = new List<SerializableCommand>();
}

public static class ReplayStore
{
    private static string Dir => Path.Combine(Application.persistentDataPath, "Replays");

    /// <summary>Call once a match's GameState.Status has flipped to "finished".</summary>
    public static ReplayRecord Save(GameState state, MatchConfig config)
    {
        if (state == null || config == null) return null;

        var (southName, northName) = ParseMatchup(state);
        var record = new ReplayRecord
        {
            Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),
            SavedAtIso = DateTime.UtcNow.ToString("o"),
            Seed = config.Seed,
            SouthDeckId = config.SouthDeckDef?.Id,
            NorthDeckId = config.NorthDeckDef?.Id,
            SouthDeckName = southName,
            NorthDeckName = northName,
            WinnerName = ExtractWinner(state),
            TurnCount = state.TurnNumber,
        };
        foreach (var cmd in state.CommandHistory)
            record.CommandHistory.Add(SerializableCommand.From(cmd));

        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, record.Id + ".json"), JsonUtility.ToJson(record, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to save replay: {ex.Message}");
        }
        return record;
    }

    /// <summary>CreateMatch always logs "Match created: {south} vs {north}." as EventLog[0].</summary>
    private static (string south, string north) ParseMatchup(GameState state)
    {
        const string prefix = "Match created: ";
        if (state.EventLog.Count == 0) return (null, null);
        string msg = state.EventLog[0].Message ?? "";
        if (!msg.StartsWith(prefix) || !msg.EndsWith(".")) return (null, null);
        string mid = msg.Substring(prefix.Length, msg.Length - prefix.Length - 1);
        var parts = mid.Split(new[] { " vs " }, StringSplitOptions.None);
        return parts.Length == 2 ? (parts[0], parts[1]) : (null, null);
    }

    /// <summary>Every win path logs "{name} wins." as a "system" entry right before Status flips.</summary>
    private static string ExtractWinner(GameState state)
    {
        const string suffix = " wins.";
        for (int i = state.EventLog.Count - 1; i >= 0; i--)
        {
            string msg = state.EventLog[i].Message;
            if (!string.IsNullOrEmpty(msg) && msg.EndsWith(suffix))
                return msg.Substring(0, msg.Length - suffix.Length);
        }
        return null;
    }

    public static List<ReplayRecord> ListAll()
    {
        var results = new List<ReplayRecord>();
        if (!Directory.Exists(Dir)) return results;
        foreach (var path in Directory.GetFiles(Dir, "*.json"))
        {
            try
            {
                var record = JsonUtility.FromJson<ReplayRecord>(File.ReadAllText(path));
                if (record != null) results.Add(record);
            }
            catch { /* skip corrupt file */ }
        }
        results.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id)); // Id is timestamp-sortable -> newest first
        return results;
    }

    public static void Delete(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        string path = Path.Combine(Dir, id + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}
