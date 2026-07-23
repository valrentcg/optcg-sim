// One Piece TCG — in-game bug reporting.
//
// A right-click "Report Bug" on any card (see GameManager.OpenBugReport) captures the player's
// typed description PLUS the full game context at that moment, and appends it here. The store is a
// single JSONL file (one complete JSON object per line) so it is trivial to scrape later:
//
//   <persistentDataPath>/BugReports/bugs.jsonl
//   Windows: C:\Users\<user>\AppData\LocalLow\<company>\<product>\BugReports\bugs.jsonl
//
// Each report is EXACTLY reproducible: like ReplayStore, it records {Seed, deck ids, CommandHistory},
// which deterministically rebuilds the precise state via GameEngine.CreateMatch/ApplyCommand — no need
// to trust the human-readable StateSummary (that is only a convenience for quick scanning). Reports
// carry an `Addressed` flag (+ note/timestamp) so that, after a fix ships, the item can be marked
// resolved without deleting it — MarkAddressed rewrites the file in place.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using OnePieceTcg.Engine;

[Serializable]
public sealed class BugReport
{
    public string Id;               // yyyyMMdd-HHmmss-fff (UTC) — timestamp-sortable, unique
    public string CreatedAtIso;     // ISO-8601 UTC
    public bool Addressed;          // flipped true once a fix is confirmed (post-scrape)
    public string AddressedAtIso;
    public string AddressedNote;    // e.g. "fixed in v1.0.20 — clause-normalization"

    public string Description;      // the player's typed bug experience
    public string AppVersion;
    public string PlayerIdentity;   // AccountManager.CurrentIdentityKey (account or guest)
    public string LocalSeat;        // which seat the reporter controls ("south"/"north")

    // The card the report was filed on (right-clicked).
    public string CardId;
    public string CardName;
    public string CardZone;         // hand / character / leader / stage / trash / life / don
    public string CardInstanceId;

    // Game context at submission.
    public int Turn;
    public string Phase;
    public string ActiveSeat;
    public string Status;

    // Exact deterministic reproduction (same triplet ReplayStore uses).
    public string Seed;
    public string SouthDeckId;
    public string NorthDeckId;
    public string SouthLeaderId;
    public string NorthLeaderId;
    // Full deck LISTS ("cardId:qty") + leader, so a CUSTOM-deck position is reconstructable even when the
    // deck id isn't a shareable/known deck. Without these, replaying the CommandHistory needs the deck
    // definitions, which live only on the reporter's machine — so custom-deck bugs couldn't be reproduced.
    public string SouthDeckLeader;
    public string NorthDeckLeader;
    public List<string> SouthDeck = new List<string>();
    public List<string> NorthDeck = new List<string>();
    public List<SerializableCommand> CommandHistory = new List<SerializableCommand>();

    // A compact, engine-free snapshot so the report can be skimmed without re-simulating.
    public string StateSummary;
}

public static class BugReportStore
{
    private static string Dir => Path.Combine(Application.persistentDataPath, "BugReports");
    private static string FilePath => Path.Combine(Dir, "bugs.jsonl");

    // Centralized cloud collection (Deploy/bugreports-worker) — so EVERY player's reports land in one
    // D1 database I can scrape from anywhere, not just this machine's local JSONL. Fire-and-forget:
    // Save() always writes the local file first, then best-effort uploads; a failed/absent network
    // never blocks or throws. Mirrors GameLogStore's upload design + shared app secret.
    private const string UploadUrl = "https://optcg-bugreports.valrentcg.workers.dev/submit";
    private static string AppSecret => AppConfig.AppSecret;

    /// <summary>Builds a report from the live match state + the card the player right-clicked.
    /// Captures the deterministic replay triplet and a human-readable snapshot. `config` may be null
    /// (older/rare paths) — the report still saves, just without deck-id/seed repro metadata.</summary>
    public static BugReport Build(GameState state, MatchConfig config, CardInstance card, string zone,
        string description, string localSeat)
    {
        var def = card != null ? GameEngine.GetCard(card) : null;
        state.Players.TryGetValue("south", out var southP);
        state.Players.TryGetValue("north", out var northP);
        var report = new BugReport
        {
            Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),
            CreatedAtIso = DateTime.UtcNow.ToString("o"),
            Addressed = false,
            Description = description ?? "",
            AppVersion = Application.version,
            PlayerIdentity = SafeIdentity(),
            LocalSeat = localSeat,
            CardId = card?.CardId,
            CardName = def?.Name,
            CardZone = string.IsNullOrEmpty(zone) ? card?.Zone : zone,
            CardInstanceId = card?.InstanceId,
            Turn = state.TurnNumber,
            Phase = state.Phase,
            ActiveSeat = state.ActiveSeat,
            Status = state.Status,
            Seed = config?.Seed ?? state.Seed,
            SouthDeckId = config?.SouthDeckDef?.Id,
            NorthDeckId = config?.NorthDeckDef?.Id,
            SouthLeaderId = southP?.Leader?.CardId,
            NorthLeaderId = northP?.Leader?.CardId,
            StateSummary = BuildSummary(state),
        };
        if (state.CommandHistory != null)
            foreach (var cmd in state.CommandHistory)
                report.CommandHistory.Add(SerializableCommand.From(cmd));

        // Capture the full deck lists (custom decks live only on this machine) so the position is exactly
        // reconstructable off-machine: CreateMatch(seed, {leader, list}) + replay CommandHistory.
        CaptureDeck(config?.SouthDeckDef, out report.SouthDeckLeader, report.SouthDeck);
        CaptureDeck(config?.NorthDeckDef, out report.NorthDeckLeader, report.NorthDeck);
        return report;
    }

    private static void CaptureDeck(DeckDef def, out string leader, List<string> into)
    {
        leader = def?.Leader;
        if (def?.List == null) return;
        foreach (var (cardId, qty) in def.List) into.Add($"{cardId}:{qty}");
    }

    /// <summary>Appends a report as one JSON line. Never throws to the caller — a failed save is logged
    /// and swallowed so a reporting hiccup can't crash the match.</summary>
    public static bool Save(BugReport report)
    {
        if (report == null) return false;
        string json;
        try
        {
            json = JsonUtility.ToJson(report, false);   // prettyPrint:false → one line; JsonUtility escapes
            Directory.CreateDirectory(Dir);             // any newlines in string fields, so JSONL holds.
            File.AppendAllText(FilePath, json + "\n", Encoding.UTF8);
            Debug.Log($"[BugReportStore] Saved bug {report.Id} on {report.CardName} ({report.CardId}).");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BugReportStore] Failed to save bug report locally: {ex.Message}");
            // Local write failed, but still try the cloud so the report isn't lost.
            json = SafeJson(report);
            if (json == null) return false;
        }
        UploadAsync(report.Id, json);   // best-effort; never blocks the local result
        return true;
    }

    private static string SafeJson(BugReport report)
    {
        try { return JsonUtility.ToJson(report, false); } catch { return null; }
    }

    // Best-effort POST to the bug-reports Worker. async void (fire-and-forget) so the caller — the
    // in-match submit button — returns immediately; a down/absent server just logs a warning.
    private static async void UploadAsync(string id, string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            using var req = new UnityWebRequest(UploadUrl, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-App-Secret", AppSecret);
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[BugReportStore] Cloud upload failed for {id}: {req.error}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BugReportStore] Cloud upload exception for {id}: {ex.Message}");
        }
    }

    /// <summary>All reports, newest first. Skips any corrupt/blank line rather than failing the read.</summary>
    public static List<BugReport> ListAll()
    {
        var results = new List<BugReport>();
        try
        {
            if (!File.Exists(FilePath)) return results;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { var r = JsonUtility.FromJson<BugReport>(line); if (r != null) results.Add(r); }
                catch { /* skip malformed line */ }
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[BugReportStore] Read failed: {ex.Message}"); }
        results.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
        return results;
    }

    public static int OpenCount() => ListAll().Count(r => !r.Addressed);

    /// <summary>Marks a report addressed (post-scrape, once a fix is confirmed) and rewrites the file in
    /// place. Returns true if a matching open report was found and updated.</summary>
    public static bool MarkAddressed(string id, string note)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var all = ListAll();
        bool changed = false;
        foreach (var r in all)
            if (r.Id == id && !r.Addressed)
            {
                r.Addressed = true;
                r.AddressedAtIso = DateTime.UtcNow.ToString("o");
                r.AddressedNote = note ?? "";
                changed = true;
            }
        if (!changed) return false;
        try
        {
            // ListAll returns newest-first; write oldest-first so the file stays append-ordered.
            all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var sb = new StringBuilder();
            foreach (var r in all) sb.Append(JsonUtility.ToJson(r, false)).Append('\n');
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            return true;
        }
        catch (Exception ex) { Debug.LogWarning($"[BugReportStore] MarkAddressed rewrite failed: {ex.Message}"); return false; }
    }

    private static string SafeIdentity()
    {
        try { return AccountManager.CurrentIdentityKey; } catch { return "local"; }
    }

    // A compact both-seats snapshot (counts + board/hand card ids) — enough to eyeball the position
    // without re-simulating the CommandHistory.
    private static string BuildSummary(GameState s)
    {
        var sb = new StringBuilder();
        sb.Append($"turn {s.TurnNumber} · {s.ActiveSeat} to act · phase {s.Phase}");
        foreach (var seat in new[] { "south", "north" })
        {
            if (!s.Players.TryGetValue(seat, out var p)) continue;
            sb.Append($"\n[{seat}] leader={p.Leader?.CardId} life={p.Life?.Count ?? 0} "
                + $"don={p.CostArea.Count(d => !d.Rested)}a/{p.CostArea.Count(d => d.Rested)}r "
                + $"hand={p.Hand.Count} deck={p.Deck.Count}");
            sb.Append("\n  board: " + JoinCards(p.CharacterArea));
            if (p.Stage != null) sb.Append($"\n  stage: {p.Stage.CardId}");
            sb.Append("\n  hand: " + JoinCards(p.Hand));
            sb.Append("\n  trash: " + JoinCards(p.Trash));
        }
        return sb.ToString();
    }

    private static string JoinCards(IEnumerable<CardInstance> zone)
    {
        var items = zone == null ? new List<string>()
            : zone.Where(c => c != null).Select(c =>
              {
                  var d = GameEngine.GetCard(c);
                  string tag = c.Rested ? "(R)" : "";
                  return $"{c.CardId}{tag}";
              }).ToList();
        return items.Count == 0 ? "—" : string.Join(", ", items);
    }
}
