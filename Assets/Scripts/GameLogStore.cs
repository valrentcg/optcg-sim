// One Piece TCG - Full per-turn game-state logging, for later ML training / puzzle
// extraction / exact state restoration.
//
// Design: ReplayStore already captures everything needed to deterministically reproduce a
// match (Seed + deck ids + CommandHistory), but only the FINAL state — inspecting any
// intermediate point today means re-simulating (see GameManager.ResimulateReplayTo). This
// store does that re-simulation once, right after a match ends, and writes out a full state
// snapshot at every turn boundary as it replays — so the resulting file needs no engine
// access to read, unlike the replay JSON.
//
// Format: Markdown with an embedded JSON block per turn. Human-browsable (headers per turn,
// a plain-English action list pulled from the real EventLog) while still exactly
// machine-parseable (each JSON block is a complete, self-contained state snapshot — hand,
// board, trash, DON counts, for both seats — good enough to reconstruct a position for a
// puzzle or a training example without touching the engine).
//
// This file is a DERIVED view, not a source of truth: if GameEngine's rules or card texts
// change, old .md logs may no longer match a fresh re-simulation of the paired replay JSON.
// The replay JSON (same `id`) remains authoritative for exact reproduction.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using OnePieceTcg.Engine;

public static class GameLogStore
{
    // ── Cloud upload (optcg-gamelogs-upload Worker, see Deploy/gamelogs-worker) ──────
    // Writes the same Markdown this class saves locally into a shared R2 bucket + D1
    // index, tagged with both players' usernames, so matches can be searched by player
    // later. Fire-and-forget: a failed upload never blocks match end or touches the
    // local file, which always saves first regardless of network state.
    //
    // AppSecret is a shared app-level secret, not a per-player credential — it blocks
    // casual abuse of the public endpoint (randos hitting it directly), not a determined
    // attacker willing to decompile the client. Fine for this project's current stage;
    // would need real per-player auth (tied into AccountManager's login) to be
    // attacker-resistant.
    private const string UploadUrl = "https://optcg-gamelogs-upload.valrentcg.workers.dev/upload";
    // The real value lives in the gitignored AppConfig.Local.cs — never in source.
    private static string AppSecret => AppConfig.AppSecret;

    [Serializable]
    private sealed class UploadPayload
    {
        public string matchId;
        public string category;
        public string markdown;
        public string replayJson;
        public string southUsername;
        public string northUsername;
        public string southDeckName;
        public string northDeckName;
        public string winnerUsername;
        public int turnCount;
        public string firstPlayer;
    }

    // Uploads the same ReplayRecord ReplayStore.Save() already wrote locally, alongside the
    // markdown log, so it's downloadable by other players later (see ReplayCloudStore.cs for
    // the search/download side) — the engine only ever knows players as "South"/"North" (see
    // GameEngine.CreateMatch), so the JSON blob itself carries no real player identity; that
    // association lives entirely in the D1 index row (south_username/north_username), same as
    // the markdown log already does.
    private static async void UploadAsync(string markdown, ReplayRecord record, string category,
        string southUsername, string northUsername)
    {
        try
        {
            string winnerUsername = record.WinnerName == "South" ? southUsername
                : record.WinnerName == "North" ? northUsername
                : null;
            string json = JsonUtility.ToJson(new UploadPayload
            {
                matchId = record.Id,
                category = category,
                markdown = markdown,
                replayJson = JsonUtility.ToJson(record),
                southUsername = southUsername,
                northUsername = northUsername,
                southDeckName = record.SouthDeckName,
                northDeckName = record.NorthDeckName,
                winnerUsername = winnerUsername,
                turnCount = record.TurnCount,
                firstPlayer = record.FirstPlayer,
            });

            using var req = new UnityWebRequest(UploadUrl, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-App-Secret", AppSecret);
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[GameLogStore] Cloud upload failed for {record.Id}: {req.error}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GameLogStore] Cloud upload exception for {record.Id}: {ex.Message}");
        }
    }

    // Same per-identity scoping as ReplayStore/DeckStore, plus a category subfolder so a
    // training pipeline can point at e.g. just "soloAi-intermediate" without filtering.
    private static string RootDir => Path.Combine(Application.persistentDataPath, "GameLogs");

    private static string CategoryDir(string category) =>
        Path.Combine(RootDir, AccountManager.CurrentIdentityKey, SanitizeCategory(category));

    private static string SanitizeCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return "uncategorized";
        foreach (char c in Path.GetInvalidFileNameChars()) category = category.Replace(c, '-');
        return category;
    }

    /// <summary>Call once, right after ReplayStore.Save() returns a non-null record for a
    /// finished match. Re-simulates that record's CommandHistory (the same deterministic
    /// replay ResimulateReplayTo uses) to capture a full state snapshot at every turn
    /// boundary, then writes one Markdown file named after the SAME id as the replay JSON so
    /// the two can be cross-referenced. Always saves locally first, then fires a best-effort
    /// cloud upload tagged with both players' usernames (either may be null — e.g. a bot
    /// opponent has no account) so matches can be searched by player later.</summary>
    public static void Export(GameState finishedState, MatchConfig config, ReplayRecord record, string category,
        string southUsername, string northUsername)
    {
        if (finishedState == null || config == null || record == null || string.IsNullOrEmpty(record.Id)) return;
        string markdown;
        try
        {
            markdown = BuildMarkdown(finishedState, config, record, category, southUsername, northUsername);
            string dir = CategoryDir(category);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, record.Id + ".md"), markdown);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to export game log: {ex.Message}");
            return;
        }
        UploadAsync(markdown, record, category, southUsername, northUsername);
    }

    private static string BuildMarkdown(GameState finishedState, MatchConfig config, ReplayRecord record,
        string category, string southUsername, string northUsername)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Match {record.Id}");
        sb.AppendLine();
        sb.AppendLine($"- Category: `{category}`");
        sb.AppendLine($"- Seed: `{record.Seed}`");
        sb.AppendLine($"- South: {(string.IsNullOrEmpty(southUsername) ? "" : southUsername + " — ")}{record.SouthDeckName} (leader `{record.SouthLeaderId}`)");
        sb.AppendLine($"- North: {(string.IsNullOrEmpty(northUsername) ? "" : northUsername + " — ")}{record.NorthDeckName} (leader `{record.NorthLeaderId}`)");
        sb.AppendLine($"- First player: {record.FirstPlayer}");
        sb.AppendLine($"- Winner: {record.WinnerName ?? "unknown"}");
        sb.AppendLine($"- Turns: {record.TurnCount}");
        sb.AppendLine();
        sb.AppendLine("Derived view — for exact reproduction, replay `CommandHistory` from the " +
            $"paired `Replays/{record.Id}.json` against `GameEngine.CreateMatch`. Each turn's JSON " +
            "block below is a complete snapshot at the start of that turn's main phase (after the " +
            "automatic refresh/draw/DON step, before the active player's first action).");
        sb.AppendLine();

        // Re-simulate to capture a snapshot at every turn boundary — mirrors
        // GameManager.ResimulateReplayTo exactly, just walking the whole history in one pass
        // instead of stopping at a cursor.
        var sim = GameEngine.CreateMatch(config);
        int lastTurn = -1;
        foreach (var cmd in finishedState.CommandHistory)
        {
            sim = GameEngine.ApplyCommand(sim, cmd);
            if (sim.Status == "active" && sim.TurnNumber != lastTurn)
            {
                lastTurn = sim.TurnNumber;
                AppendTurnSection(sb, sim, finishedState);
            }
        }

        sb.AppendLine("## Final State");
        sb.AppendLine();
        AppendStateJson(sb, sim);
        sb.AppendLine();

        return sb.ToString();
    }

    private static void AppendTurnSection(StringBuilder sb, GameState sim, GameState finishedState)
    {
        sb.AppendLine($"## Turn {sim.TurnNumber} — {sim.ActiveSeat}'s turn");
        sb.AppendLine();
        AppendStateJson(sb, sim);
        sb.AppendLine();

        var acts = finishedState.EventLog.Where(l => l.Turn == sim.TurnNumber).ToList();
        if (acts.Count > 0)
        {
            sb.AppendLine("**Actions taken this turn:**");
            foreach (var a in acts)
                sb.AppendLine($"- [{a.Actor}] {a.Message}");
            sb.AppendLine();
        }
    }

    private static void AppendStateJson(StringBuilder sb, GameState sim)
    {
        sb.AppendLine("```json");
        sb.AppendLine(BuildStateJson(sim));
        sb.AppendLine("```");
    }

    private static string BuildStateJson(GameState s)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"turn\": {s.TurnNumber},\n");
        sb.Append($"  \"activeSeat\": \"{s.ActiveSeat}\",\n");
        sb.Append($"  \"phase\": \"{Esc(s.Phase)}\",\n");
        sb.Append("  \"players\": {\n");
        sb.Append(PlayerJson(s, "south", "    "));
        sb.Append(",\n");
        sb.Append(PlayerJson(s, "north", "    "));
        sb.Append("\n  }\n");
        sb.Append("}");
        return sb.ToString();
    }

    private static string PlayerJson(GameState s, string seat, string indent)
    {
        var p = s.Players[seat];
        var sb = new StringBuilder();
        sb.Append($"{indent}\"{seat}\": {{\n");
        sb.Append($"{indent}  \"leader\": {CardJson(s, p.Leader, inPlay: true)},\n");
        sb.Append($"{indent}  \"life\": {p.Life.Count},\n");
        sb.Append($"{indent}  \"hand\": [{string.Join(", ", p.Hand.Select(c => CardJson(s, c, inPlay: false)))}],\n");
        sb.Append($"{indent}  \"characterArea\": [{string.Join(", ", p.CharacterArea.Select(c => CardJson(s, c, inPlay: true)))}],\n");
        sb.Append($"{indent}  \"stage\": {CardJson(s, p.Stage, inPlay: true)},\n");
        sb.Append($"{indent}  \"trash\": [{string.Join(", ", p.Trash.Select(c => CardJson(s, c, inPlay: false)))}],\n");
        sb.Append($"{indent}  \"donActive\": {p.CostArea.Count(d => !d.Rested)},\n");
        sb.Append($"{indent}  \"donRested\": {p.CostArea.Count(d => d.Rested)},\n");
        sb.Append($"{indent}  \"donDeckRemaining\": {p.DonDeck},\n");
        sb.Append($"{indent}  \"deckRemaining\": {p.Deck.Count}\n");
        sb.Append($"{indent}}}");
        return sb.ToString();
    }

    // inPlay=true adds board-only fields (rested/attachedDon/effective power) that are
    // meaningless for a card sitting in hand/trash/deck.
    private static string CardJson(GameState s, CardInstance c, bool inPlay)
    {
        if (c == null) return "null";
        var def = GameEngine.GetCard(c);
        var fields = new List<string>
        {
            $"\"id\": \"{Esc(c.CardId)}\"",
            $"\"name\": \"{Esc(def?.Name)}\"",
            $"\"type\": \"{Esc(def?.Type)}\"",
            $"\"cost\": {def?.Cost ?? 0}",
            $"\"printedPower\": {def?.Power ?? 0}",
        };
        if (inPlay)
        {
            fields.Add($"\"power\": {GameEngine.GetPower(s, c)}");
            fields.Add($"\"rested\": {(c.Rested ? "true" : "false")}");
            fields.Add($"\"attachedDon\": {c.AttachedDonIds.Count}");
        }
        return "{ " + string.Join(", ", fields) + " }";
    }

    private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
