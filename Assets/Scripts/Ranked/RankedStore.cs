// One Piece TCG - Bounty ranked system (visible ladder + persistence).
//
// This is the VISIBLE layer that sits on top of the hidden Glicko-2 rating
// (Glicko2.cs). Your rank IS your Bounty in Berries: tiers are bounty brackets
// themed to One Piece notoriety, divisions (III→I) are the thirds of a bracket.
// Design spec: the "Bounty Ladder" doc. Summary of the locked mechanics:
//
//   • Two ratings   - hidden Glicko-2 MMR matches you + scales payouts; the
//                     visible Bounty (Berries) is what you climb.
//   • 9 tiers       - Apprentice → … → Yonko, plus Pirate King (top crown),
//                     capped at Gol D. Roger's real ฿5,564,800,000.
//   • Bo1           - one finished match = one Glicko result = one bounty update.
//   • Rampage       - rank-scaled win-streak bonus: huge low, ~0 at the top.
//   • Vivre Card    - loss-prevention that absorbs a demotion loss; lower tiers
//                     only (Apprentice→Supernova), charged by losses.
//   • Season reset  - graduated by ladder third (drop 0 / 1 / 2 tiers).
//   • Win/loss only - no performance/MVP weighting; Rampage only fires on wins.
//
// AUTHORITATIVE STORE IS SERVER-SIDE. Rating/bounty changes are computed by the
// Cloudflare Worker (Deploy/ranked-worker) against D1, using an authenticated
// dual-report cross-check so a modded client can't move its own rating. This
// class is now a thin client: it REPORTS finished PvP matches and READS standings
// over HTTP. Apply()/the tier table below are kept in sync with the worker's
// ranking.ts (verified identical) and drive the profile UI's display helpers; the
// client no longer computes authoritative results. Guests skipped, every call
// try/catch'd so ranked can never block or break match end.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.Networking;

// ── Serializable model (JsonUtility: plain public fields, no dictionaries) ──

[Serializable]
public sealed class RankedProfile
{
    // Hidden Glicko-2 rating (human 1500/350 scale). Never shown to players.
    public double rating     = Glicko2.DefaultRating;
    public double rd         = Glicko2.DefaultRd;
    public double volatility = Glicko2.DefaultVolatility;

    // Visible ladder.
    public long bounty      = 0;   // Berries
    public long peakBounty  = 0;   // best ever — the retained "formerly wanted for…" emblem

    // Progression bookkeeping.
    public int  placementGamesLeft = RankedStore.PlacementGames; // hidden bounty until 0
    public int  winStreak          = 0;  // >0 win streak / <0 loss streak; Rampage needs ≥3
    public int  vivreCharge        = 0;  // losses accumulated toward a Vivre Card
    public bool vivreReady         = false;
    public int  seasonId           = 0;  // which StatsStore season this bounty belongs to
    public int  games              = 0;  // ranked games recorded (placement + display)

    // Last-match feedback for the UI ("+฿8,000,000", "Vivre Card saved you!").
    public long lastDeltaBounty = 0;
    public bool lastVivreSaved  = false;
}

/// <summary>One rung of the ladder. Berry floors are the real canon bounties;
/// base gains scale per tier so games-to-promote stays roughly flat across the
/// millions→billions range.</summary>
public struct RankTierDef
{
    public string Name;
    public long   Floor;                // inclusive bounty floor
    public long   Ceil;                 // exclusive ceiling (Pirate King uses the ฿ cap)
    public long   BaseGain;             // base Berries per win at this tier
    public float  RampagePeak;          // hot-streak bonus fraction (0.60 = +60%)
    public bool   VivreActive;          // loss prevention available here?
    public int    VivreLossesToCharge;  // losses to fully charge a Vivre Card
    public double MmrThreshold;         // hidden-rating lower bound for this skill band

    public RankTierDef(string name, long floor, long ceil, long baseGain,
                       float rampagePeak, bool vivreActive, int vivreLosses, double mmrThreshold)
    {
        Name = name; Floor = floor; Ceil = ceil; BaseGain = baseGain;
        RampagePeak = rampagePeak; VivreActive = vivreActive;
        VivreLossesToCharge = vivreLosses; MmrThreshold = mmrThreshold;
    }
}

/// <summary>One row of the Most Wanted leaderboard (GET /leaderboard).</summary>
[Serializable]
public sealed class LeaderboardEntry
{
    public int rank;
    public string playerId;
    public string username;
    public long bounty;
    public long peakBounty;
    public int games;
    public string tierName;
}

/// <summary>Matchmaker state for the local player (response of any /queue/* call).
/// One flat shape covers every state; unused fields stay default.</summary>
[Serializable]
public sealed class QueueStatus
{
    public string status = "idle"; // waiting|proposed|accepted|matched|requeued|idle|error
    public int elapsedMs;
    public int range;              // current MMR match window (±)
    public string proposalId;
    public long deadline;          // epoch ms the ready check expires
    public bool amHost;
    public string opponentId;
    public bool iAccepted;
    public bool oppAccepted;
    public string role;            // "host" | "guest" (accepted/matched)
    public string matchId;         // UGS session id to join, when matched
}

public static class RankedStore
{
    // ── Server config (fill WorkerBase after deploying Deploy/ranked-worker) ──
    // Until WorkerBase points at the deployed URL, ranked runs "not configured":
    // the profile reads as unranked and reports are skipped — never throws.
    public const string WorkerBase = "https://optcg-ranked.valrentcg.workers.dev";
    // Must equal the worker's APP_SECRET (see Deploy/ranked-worker/README.md).
    // The real value lives in the gitignored AppConfig.Local.cs — never in source.
    private static string AppSecret => AppConfig.AppSecret;
    private static bool Configured => !string.IsNullOrEmpty(WorkerBase) && !WorkerBase.Contains("REPLACE");
    public static bool IsConfigured => Configured;

    // ── Tuning constants (design-locked; easy to move to Remote Config later) ──
    public const int  PlacementGames = 5;
    public const long BountyCap      = 5_564_800_000L;  // Gol D. Roger — no bounty exceeds the Pirate King's
    private const double LossFactor  = 0.8;             // losses cost a bit less than wins pay → climb at >50% WR
    private const double GapK        = 0.25;            // MMR-gap payout swing
    private const int    GapClampAbs = 2;               // gap clamped to ±2 tiers
    private const int    RampageStreak = 3;             // wins before Rampage ignites

    /// <summary>The 9-tier ladder, low → high. Floors are canon bounties;
    /// Supernova is pinned to the ฿100M "Worst Generation" line, Pirate King to
    /// Roger's ฿5.564B ceiling.</summary>
    public static readonly RankTierDef[] Tiers =
    {
        //             name                 floor            ceil             baseGain        ramp   vivre  losses  mmr
        new RankTierDef("Apprentice",       0L,              15_000_000L,     600_000L,       0.60f, true,  2,      double.MinValue),
        new RankTierDef("Rookie",           15_000_000L,     50_000_000L,     1_500_000L,     0.55f, true,  2,      1150.0),
        new RankTierDef("Notorious",        50_000_000L,     100_000_000L,    3_000_000L,     0.45f, true,  3,      1280.0),
        new RankTierDef("Supernova",        100_000_000L,    300_000_000L,    8_000_000L,     0.35f, true,  4,      1410.0),
        new RankTierDef("New World Pirate", 300_000_000L,    700_000_000L,    16_000_000L,    0.25f, false, 0,      1540.0),
        new RankTierDef("Warlord",          700_000_000L,    1_500_000_000L,  32_000_000L,    0.15f, false, 0,      1670.0),
        new RankTierDef("Conqueror",        1_500_000_000L,  3_000_000_000L,  60_000_000L,    0.08f, false, 0,      1800.0),
        new RankTierDef("Yonko",            3_000_000_000L,  5_000_000_000L,  90_000_000L,    0.03f, false, 0,      1950.0),
        new RankTierDef("Pirate King",      5_000_000_000L,  BountyCap,       40_000_000L,    0.00f, false, 0,      2150.0),
    };

    // Placements find your footing, not your ceiling: a strong 5-game run seeds
    // you no higher than mid-Supernova (index 3). Everything above is climbed —
    // and the MMR gap + Rampage let a genuine smurf rocket up fast anyway. (Also
    // damps the swing from the solo even-match assumption; revisit once real
    // opponent ratings and matchmaking are wired.)
    private const int MaxPlacementTierIndex = 3;

    // ── Cache + change signal (profile UI subscribes like StatsStore) ──
    private static RankedProfile _cache;
    public static event Action Changed;

    // ── Read API: authoritative standing from the worker (GET /profile) ─────────

    public static async Task<RankedProfile> LoadAsync(bool forceRefresh = false)
    {
        if (AccountManager.IsGuest || !Configured) return new RankedProfile();
        if (_cache != null && !forceRefresh) return _cache;

        var profile = new RankedProfile();
        try
        {
            await AccountManager.EnsureReadyAsync();
            string playerId = AuthenticationService.Instance.PlayerId;
            if (string.IsNullOrEmpty(playerId)) return profile;

            string url = $"{WorkerBase}/profile?playerId={UnityWebRequest.EscapeURL(playerId)}";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("X-App-Secret", AppSecret);
            req.timeout = 15;
            await SendAsync(req);

            if (req.result == UnityWebRequest.Result.Success)
            {
                var wrap = JsonUtility.FromJson<ProfileResponse>(req.downloadHandler.text);
                if (wrap?.profile != null) profile = wrap.profile;
            }
            else
            {
                // Offline / not-yet-deployed: default renders as "unranked".
                // Not cached, so the next call retries.
                Debug.LogWarning($"RankedStore load failed: {req.error}");
                return profile;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"RankedStore load exception: {ex.Message}");
            return profile;
        }

        _cache = profile;
        return profile;
    }

    /// <summary>Cached profile if one is already loaded, else null.</summary>
    public static RankedProfile Cached => _cache;

    // ── Write API: report a finished PvP match (authenticated dual-report) ──────

    /// <summary>Reports the local player's half of a finished networked match to the
    /// worker (POST /report). The server moves ratings only once BOTH players report
    /// and agree. matchId = shared UGS session id, opponentId = opponent UGS player
    /// id. Solo/bot games don't count. Never throws — fire-and-forget from the
    /// match-end hook.</summary>
    public static async Task ReportMatchAsync(string matchId, string opponentId, bool won)
    {
        if (!Configured || AccountManager.IsGuest) return;
        if (string.IsNullOrEmpty(matchId) || string.IsNullOrEmpty(opponentId)) return;

        try
        {
            await AccountManager.EnsureReadyAsync();
            string token = AuthenticationService.Instance.AccessToken;
            if (string.IsNullOrEmpty(token)) return;

            var payload = new ReportRequest
            {
                matchId = matchId,
                opponentId = opponentId,
                result = won ? "win" : "loss",
                username = AccountManager.CurrentUsername ?? AccountManager.CachedUsername,
            };

            using var req = new UnityWebRequest($"{WorkerBase}/report", "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-App-Secret", AppSecret);
            req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = 20;
            await SendAsync(req);

            if (req.result == UnityWebRequest.Result.Success)
            {
                // Response carries our fresh authoritative standing (settled) or the
                // pre-settlement one (pending) — cache it so the profile updates.
                var wrap = JsonUtility.FromJson<ReportResponse>(req.downloadHandler.text);
                if (wrap?.profile != null) { _cache = wrap.profile; Changed?.Invoke(); }
            }
            else
            {
                Debug.LogWarning($"RankedStore report failed: {req.error}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"RankedStore report exception: {ex.Message}");
        }
    }

    // Await a UnityWebRequest without a coroutine (match the async store style).
    private static async Task SendAsync(UnityWebRequest req)
    {
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
    }

    [Serializable] private sealed class ReportRequest
    {
        public string matchId; public string opponentId; public string result; public string username;
    }
    [Serializable] private sealed class ReportResponse
    {
        public string status; public RankedProfile profile;
    }
    [Serializable] private sealed class ProfileResponse
    {
        public RankedProfile profile;
    }

    // ── Leaderboard (GET /leaderboard): the whole ladder, ranked by bounty ──────

    /// <summary>Top players by bounty (the Most Wanted board). Empty list when not
    /// configured or offline — never throws.</summary>
    public static async Task<List<LeaderboardEntry>> LoadLeaderboardAsync(int limit = 25)
    {
        var list = new List<LeaderboardEntry>();
        if (!Configured) return list;
        try
        {
            await AccountManager.EnsureReadyAsync();
            string url = $"{WorkerBase}/leaderboard?limit={limit}";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("X-App-Secret", AppSecret);
            req.timeout = 15;
            await SendAsync(req);
            if (req.result == UnityWebRequest.Result.Success)
            {
                var wrap = JsonUtility.FromJson<LeaderboardResponse>(req.downloadHandler.text);
                if (wrap?.entries != null) list.AddRange(wrap.entries);
            }
            else Debug.LogWarning($"Leaderboard load failed: {req.error}");
        }
        catch (Exception ex) { Debug.LogWarning($"Leaderboard exception: {ex.Message}"); }
        return list;
    }

    [Serializable] private sealed class LeaderboardResponse
    {
        public LeaderboardEntry[] entries;
    }

    // ── Matchmaking queue (POST /queue/*): server-authoritative pairing + ready check ─

    public static Task<QueueStatus> QueueJoinAsync(int mmr, string username, string mode) =>
        QueuePostAsync("/queue/join", JsonUtility.ToJson(new QueueJoinReq { mmr = mmr, username = username, mode = mode }));

    public static Task<QueueStatus> QueuePollAsync() => QueuePostAsync("/queue/poll", "{}");

    public static Task<QueueStatus> QueueReadyAsync(bool accept) =>
        QueuePostAsync("/queue/ready", JsonUtility.ToJson(new QueueReadyReq { accept = accept }));

    public static Task<QueueStatus> QueueHostReadyAsync(string matchId) =>
        QueuePostAsync("/queue/host-ready", JsonUtility.ToJson(new QueueHostReadyReq { matchId = matchId }));

    public static Task<QueueStatus> QueueCancelAsync() => QueuePostAsync("/queue/cancel", "{}");

    private static async Task<QueueStatus> QueuePostAsync(string path, string jsonBody)
    {
        if (!Configured || AccountManager.IsGuest) return new QueueStatus { status = "idle" };
        try
        {
            await AccountManager.EnsureReadyAsync();
            string token = AuthenticationService.Instance.AccessToken;
            if (string.IsNullOrEmpty(token)) return new QueueStatus { status = "idle" };

            using var req = new UnityWebRequest($"{WorkerBase}{path}", "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-App-Secret", AppSecret);
            req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = 15;
            await SendAsync(req);

            if (req.result == UnityWebRequest.Result.Success)
            {
                var s = JsonUtility.FromJson<QueueStatus>(req.downloadHandler.text);
                return s ?? new QueueStatus { status = "error" };
            }
            Debug.LogWarning($"Queue {path} failed: {req.error}");
        }
        catch (Exception ex) { Debug.LogWarning($"Queue {path} exception: {ex.Message}"); }
        return new QueueStatus { status = "error" };  // transient — caller keeps polling
    }

    [Serializable] private sealed class QueueJoinReq { public int mmr; public string username; public string mode; }
    [Serializable] private sealed class QueueReadyReq { public bool accept; }
    [Serializable] private sealed class QueueHostReadyReq { public string matchId; }

    // ── Pure update logic (unit-testable; a Cloud Code port mirrors this) ───────

    /// <summary>Applies one finished match to `p`. score is win/loss only.
    /// Order: season rollover → hidden MMR (Glicko-2) → placement reveal OR the
    /// visible bounty move (gap-scaled base ± Rampage, with Vivre Card).</summary>
    public static void Apply(RankedProfile p, bool won, double oppRating, double oppRd, int seasonId)
    {
        if (p == null) return;

        // 1) Season rollover. Brand-new profiles just adopt the current season;
        //    an existing profile crossing into a new season takes the graduated
        //    reset before this match counts.
        if (seasonId > 0)
        {
            if (p.seasonId == 0 && p.games == 0) p.seasonId = seasonId;
            else if (p.seasonId != seasonId) { ApplySeasonReset(p); p.seasonId = seasonId; }
        }

        // 2) Hidden Glicko-2 rating always updates (even during placements and
        //    even when a Vivre Card later shields the visible bounty).
        var r = Glicko2.Update(p.rating, p.rd, p.volatility, oppRating, oppRd, won ? 1.0 : 0.0);
        p.rating = r.Rating; p.rd = r.Rd; p.volatility = r.Volatility;
        p.games++;

        // Track the win/loss streak for Rampage (and UI) regardless of path.
        p.winStreak = won ? (p.winStreak > 0 ? p.winStreak + 1 : 1)
                          : (p.winStreak < 0 ? p.winStreak - 1 : -1);

        // 3) Placement games: bounty stays hidden ("assessing threat"), then the
        //    final game reveals a starting bounty seeded from the settled MMR.
        if (p.placementGamesLeft > 0)
        {
            p.placementGamesLeft--;
            if (p.placementGamesLeft == 0)
            {
                p.bounty = StartingBountyFromRating(p.rating);
                p.peakBounty = Math.Max(p.peakBounty, p.bounty);
                p.lastDeltaBounty = p.bounty;   // "placed at ฿…"
            }
            else p.lastDeltaBounty = 0;
            p.lastVivreSaved = false;
            return;
        }

        // 4) Normal bounty move.
        int tIndex = TierIndexForBounty(p.bounty);
        RankTierDef tier = Tiers[tIndex];
        int implied = ImpliedTierForRating(p.rating);
        int gap = Mathf.Clamp(implied - tIndex, -GapClampAbs, GapClampAbs);

        if (won)
        {
            double mult = p.winStreak >= RampageStreak ? (1.0 + tier.RampagePeak) : 1.0;
            long delta = (long)Math.Round(tier.BaseGain * (1.0 + GapK * gap) * mult);
            if (delta < 0) delta = 0;
            p.bounty += delta;
            p.lastDeltaBounty = delta;
            p.lastVivreSaved = false;
        }
        else
        {
            long loss = (long)Math.Round(tier.BaseGain * LossFactor * (1.0 - GapK * gap));
            if (loss < 0) loss = 0;

            bool wouldDemote = tIndex > 0 && (p.bounty - loss) < tier.Floor;

            if (tier.VivreActive && wouldDemote && p.vivreReady)
            {
                // Vivre Card absorbs the demotion loss: no bounty lost, card spent.
                p.vivreReady = false;
                p.vivreCharge = 0;
                p.lastDeltaBounty = 0;
                p.lastVivreSaved = true;
            }
            else
            {
                p.bounty -= loss;
                p.lastDeltaBounty = -loss;
                p.lastVivreSaved = false;
                if (tier.VivreActive)
                {
                    p.vivreCharge++;
                    if (p.vivreCharge >= tier.VivreLossesToCharge) p.vivreReady = true;
                }
            }
        }

        if (p.bounty < 0) p.bounty = 0;
        if (p.bounty > BountyCap) p.bounty = BountyCap;
        p.peakBounty = Math.Max(p.peakBounty, p.bounty);
    }

    // Graduated Saga reset: harder the higher you climbed (thirds of the ladder),
    // MMR soft-compressed toward the mean, peak-bounty emblem retained.
    private static void ApplySeasonReset(RankedProfile p)
    {
        int t = TierIndexForBounty(p.bounty);
        int drop = t <= 2 ? 0 : (t <= 5 ? 1 : 2);
        int newT = Math.Max(0, t - drop);
        p.bounty = Tiers[newT].Floor;

        double compress = t <= 2 ? 0.0 : (t <= 5 ? 0.25 : 0.5);
        p.rating += (Glicko2.DefaultRating - p.rating) * compress;
        p.rd = Math.Max(p.rd, 150.0);   // widen so a genuine climber re-converges fast

        p.winStreak = 0;
        p.vivreCharge = 0;
        p.vivreReady = false;
        p.lastDeltaBounty = 0;
        p.lastVivreSaved = false;
        // peakBounty kept; placement NOT re-triggered — you stay a placed player.
    }

    // ── Ladder helpers (shared by the write path and the profile UI) ────────────

    public static int TierIndexForBounty(long bounty)
    {
        int idx = 0;
        for (int i = 0; i < Tiers.Length; i++)
            if (bounty >= Tiers[i].Floor) idx = i;
        return idx;
    }

    public static int ImpliedTierForRating(double rating)
    {
        int idx = 0;
        for (int i = 0; i < Tiers.Length; i++)
            if (rating >= Tiers[i].MmrThreshold) idx = i;
        return idx;
    }

    private static long StartingBountyFromRating(double rating)
    {
        int i = Math.Min(ImpliedTierForRating(rating), MaxPlacementTierIndex);
        RankTierDef t = Tiers[i];
        long mid = t.Floor + (t.Ceil - t.Floor) / 2;
        return mid;
    }

    /// <summary>"III" (low third) / "II" / "I" (top third) within a tier band.
    /// Pirate King has no divisions — returns "" for it.</summary>
    public static string DivisionForBounty(long bounty)
    {
        int i = TierIndexForBounty(bounty);
        if (i == Tiers.Length - 1) return "";           // Pirate King: no divisions
        RankTierDef t = Tiers[i];
        float p = ProgressInTier(bounty);
        return p < 1f / 3f ? "III" : (p < 2f / 3f ? "II" : "I");
    }

    /// <summary>0..1 progress through the current tier's Berry band.</summary>
    public static float ProgressInTier(long bounty)
    {
        int i = TierIndexForBounty(bounty);
        RankTierDef t = Tiers[i];
        long span = t.Ceil - t.Floor;
        if (span <= 0) return 1f;
        float p = (float)(bounty - t.Floor) / span;
        return Mathf.Clamp01(p);
    }

    public static bool IsPlaced(RankedProfile p) => p != null && p.placementGamesLeft <= 0;

    // ── Formatting (font-safe: no ฿ glyph — mono OS fonts drop it) ───────────────

    /// <summary>Grouped Berries, e.g. 1500000000 → "1,500,000,000".</summary>
    public static string FormatBerries(long v) => v.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>Abbreviated Berries, e.g. 1,500,000,000 → "1.5B", 320000000 → "320M".</summary>
    public static string FormatBerriesShort(long v)
    {
        if (v >= 1_000_000_000L)
        {
            double b = v / 1_000_000_000.0;
            return (b >= 10 ? b.ToString("0", CultureInfo.InvariantCulture)
                            : b.ToString("0.##", CultureInfo.InvariantCulture)) + "B";
        }
        if (v >= 1_000_000L)
        {
            double m = v / 1_000_000.0;
            return (m >= 10 ? m.ToString("0", CultureInfo.InvariantCulture)
                            : m.ToString("0.#", CultureInfo.InvariantCulture)) + "M";
        }
        if (v >= 1_000L) return (v / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
        return v.ToString(CultureInfo.InvariantCulture);
    }
}
