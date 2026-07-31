// One Piece TCG — authoritative ranked (Bounty) Worker.
//
// The Unity client can no longer move its own rating. It only:
//   • POST /report   — "I played match <id> vs <opponent>, I <won|lost>"
//   • GET  /profile  — read a player's authoritative standing
//   • GET  /leaderboard — top bounties (the whole ladder lives in one D1 table)
//
// Anti-forgery = authenticated dual-report cross-check:
//   - /report requires a valid Unity Authentication token (auth.ts) → reporter id.
//   - A rating only moves when BOTH players report the SAME match and AGREE on
//     who won. A lone modded client cannot produce the opponent's signed half.
//   - Disagreements are recorded 'disputed' and change nothing.
// Because the server holds both players' real ratings, the Glicko update is fully
// correct here (no even-match approximation the solo client had to make).

import { verifyUnityToken } from "./auth";
import {
  RankedProfile, freshProfile, applyMatch, tierIndexForBounty, TIERS,
} from "./ranking";
import {
  handleQueueJoin, handleQueuePoll, handleQueueReady, handleQueueHostReady, handleQueueCancel,
} from "./matchmaking";
import {
  handleChatSend, handleChatHistory, handleChatRead, handleChatPoll,
  handleInviteSend, handleInvitePoll, handleInviteRespond, handleInviteStatus, handleInviteCancel,
  sweepExpiredInvites,
} from "./social";

export interface Env {
  DB: D1Database;
  APP_SECRET: string;
}

// ── Seasons (mirror StatsStore.Seasons; end exclusive, UTC) ──
const SEASONS: [number, string, string][] = [
  [1, "2026-07-01T00:00:00Z", "2026-10-01T00:00:00Z"],
  [2, "2026-10-01T00:00:00Z", "2027-01-01T00:00:00Z"],
  [3, "2027-01-01T00:00:00Z", "2027-04-01T00:00:00Z"],
];
function currentSeasonId(nowMs: number): number {
  for (const [id, s, e] of SEASONS) {
    if (nowMs >= Date.parse(s) && nowMs < Date.parse(e)) return id;
  }
  return 0;
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });

// ── D1 row <-> profile ──
function rowToProfile(row: any): RankedProfile {
  const p = freshProfile();
  if (!row) return p;
  p.rating = row.rating; p.rd = row.rd; p.volatility = row.volatility;
  p.bounty = row.bounty; p.peakBounty = row.peak_bounty;
  p.placementGamesLeft = row.placement_games_left; p.winStreak = row.win_streak;
  p.vivreCharge = row.vivre_charge; p.vivreReady = !!row.vivre_ready;
  p.seasonId = row.season_id; p.games = row.games;
  return p;
}

async function loadProfile(env: Env, playerId: string): Promise<RankedProfile> {
  const row = await env.DB.prepare("SELECT * FROM ranked_profiles WHERE player_id = ?")
    .bind(playerId).first();
  return rowToProfile(row);
}

function upsertStmt(env: Env, playerId: string, p: RankedProfile, username: string | null, now: number) {
  return env.DB.prepare(
    `INSERT INTO ranked_profiles
       (player_id, rating, rd, volatility, bounty, peak_bounty, placement_games_left,
        win_streak, vivre_charge, vivre_ready, season_id, games, username, updated_at)
     VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)
     ON CONFLICT(player_id) DO UPDATE SET
       rating=excluded.rating, rd=excluded.rd, volatility=excluded.volatility,
       bounty=excluded.bounty, peak_bounty=excluded.peak_bounty,
       placement_games_left=excluded.placement_games_left, win_streak=excluded.win_streak,
       vivre_charge=excluded.vivre_charge, vivre_ready=excluded.vivre_ready,
       season_id=excluded.season_id, games=excluded.games,
       username=COALESCE(excluded.username, ranked_profiles.username),
       updated_at=excluded.updated_at`,
  ).bind(
    playerId, p.rating, p.rd, p.volatility, p.bounty, p.peakBounty, p.placementGamesLeft,
    p.winStreak, p.vivreCharge, p.vivreReady ? 1 : 0, p.seasonId, p.games, username, now,
  );
}

// Public shape returned to the client (camelCase, plus derived tier for convenience).
function publicProfile(playerId: string, p: RankedProfile, username?: string | null) {
  const ti = p.placementGamesLeft > 0 ? -1 : tierIndexForBounty(p.bounty);
  return {
    playerId,
    username: username ?? null,
    rating: Math.round(p.rating), rd: Math.round(p.rd), // exposed rounded; not secret but not precise
    bounty: p.bounty, peakBounty: p.peakBounty,
    placementGamesLeft: p.placementGamesLeft,
    winStreak: p.winStreak, vivreCharge: p.vivreCharge, vivreReady: p.vivreReady,
    seasonId: p.seasonId, games: p.games,
    lastDeltaBounty: p.lastDeltaBounty, lastVivreSaved: p.lastVivreSaved,
    tierIndex: ti, tierName: ti < 0 ? "Placement" : TIERS[ti].name,
  };
}

// ── Routes ──
async function handleReport(req: Request, env: Env): Promise<Response> {
  let playerId: string;
  try {
    ({ playerId } = await verifyUnityToken(req.headers.get("Authorization")));
  } catch (e: any) {
    return json({ error: "unauthorized", detail: String(e?.message ?? e) }, 401);
  }

  const body = await req.json<any>().catch(() => null);
  if (!body) return json({ error: "bad json" }, 400);
  const matchId = String(body.matchId ?? "").trim();
  const opponentId = String(body.opponentId ?? "").trim();
  const result = String(body.result ?? "").trim();
  const username = body.username ? String(body.username).slice(0, 40) : null;

  if (!matchId || !opponentId) return json({ error: "matchId and opponentId required" }, 400);
  if (result !== "win" && result !== "loss") return json({ error: "result must be win|loss" }, 400);
  if (opponentId === playerId) return json({ error: "cannot play yourself" }, 400);

  const now = Date.now();
  const season = currentSeasonId(now);

  // Record this half (idempotent on (match_id, reporter_id)).
  await env.DB.prepare(
    `INSERT OR IGNORE INTO match_reports
       (match_id, reporter_id, opponent_id, result, username, season_id, created_at)
     VALUES (?,?,?,?,?,?,?)`,
  ).bind(matchId, playerId, opponentId, result, username, season, now).run();

  // Already settled? Just return current standing.
  const existing = await env.DB.prepare("SELECT status FROM match_results WHERE match_id = ?")
    .bind(matchId).first<{ status: string }>();
  if (existing) {
    const p = await loadProfile(env, playerId);
    return json({ status: existing.status, profile: publicProfile(playerId, p, username) });
  }

  // Opponent's half in yet? (Also pull the opponent's own reported username so BOTH profiles get a
  // name at settlement — otherwise whoever reported FIRST is saved with null and shows as "Unknown
  // Pirate" on the Most Wanted board.)
  const opp = await env.DB.prepare(
    "SELECT result, opponent_id, username FROM match_reports WHERE match_id = ? AND reporter_id = ?",
  ).bind(matchId, opponentId).first<{ result: string; opponent_id: string; username: string | null }>();

  if (!opp) {
    const p = await loadProfile(env, playerId);
    return json({ status: "pending", profile: publicProfile(playerId, p, username) });
  }

  // Both present — do they corroborate? (point at each other, opposite outcomes)
  const agree =
    opp.opponent_id === playerId &&
    ((result === "win" && opp.result === "loss") || (result === "loss" && opp.result === "win"));

  if (!agree) {
    await env.DB.prepare(
      "INSERT OR IGNORE INTO match_results (match_id, winner_id, loser_id, status, settled_at) VALUES (?,?,?,?,?)",
    ).bind(matchId, null, null, "disputed", now).run();
    const p = await loadProfile(env, playerId);
    return json({ status: "disputed", profile: publicProfile(playerId, p, username) });
  }

  const winnerId = result === "win" ? playerId : opponentId;
  const loserId = result === "win" ? opponentId : playerId;

  // Claim settlement atomically — the PK guard means exactly one request computes.
  const claim = await env.DB.prepare(
    "INSERT OR IGNORE INTO match_results (match_id, winner_id, loser_id, status, settled_at) VALUES (?,?,?,?,?)",
  ).bind(matchId, winnerId, loserId, "settled", now).run();

  if (claim.meta.changes === 1) {
    const w = await loadProfile(env, winnerId);
    const l = await loadProfile(env, loserId);
    const wPreR = w.rating, wPreRd = w.rd, lPreR = l.rating, lPreRd = l.rd;
    applyMatch(w, true, lPreR, lPreRd, season);
    applyMatch(l, false, wPreR, wPreRd, season);

    // usernames: each player's OWN reported name — this settling report for one side, the opponent's
    // stored match_reports row for the other. Both sides get named so neither shows as "Unknown Pirate".
    const oppUsername = opp.username ?? null;
    const winnerName = winnerId === playerId ? username : oppUsername;
    const loserName = loserId === playerId ? username : oppUsername;
    await env.DB.batch([
      upsertStmt(env, winnerId, w, winnerName, now),
      upsertStmt(env, loserId, l, loserName, now),
    ]);
  }

  const p = await loadProfile(env, playerId);
  return json({ status: "settled", profile: publicProfile(playerId, p, username) });
}

// ── Forfeit sweep ────────────────────────────────────────────────────────────────────────────
// /report needs BOTH halves to settle, which is deliberate anti-cheat: a lone client cannot
// fabricate a win, and a contradiction is recorded as "disputed". The gap was that a MISSING
// half never settled at all. A graceful leave concedes while still connected so both sides
// report, but on a hard quit (Alt-F4, killed process, pulled cable) the leaver's client never
// runs its concede path — so the survivor's report sat unmatched forever, no rating moved, the
// quitter escaped the loss and the honest player was denied the win. Ranked rage-quit was free.
//
// This sweep settles a report whose counterpart never arrived. Abuse bounds, because a naive
// "believe the reporter" would let a cheat claim a fake win and wait out the timer:
//   * a claimed LOSS is always safe to honour — nobody fabricates their own loss;
//   * a claimed WIN is honoured only after the grace window AND only up to
//     MAX_FORFEIT_WINS_PER_SWEEP per reporter, so a farm cannot mass-settle;
//   * settled rows are marked "forfeit" (not "settled") so they stay auditable and revertible.
const FORFEIT_GRACE_MS = 15 * 60 * 1000;
const MAX_FORFEIT_WINS_PER_SWEEP = 3;

export async function sweepForfeits(env: Env, nowMs: number): Promise<{ settled: number; skipped: number }> {
  const cutoff = nowMs - FORFEIT_GRACE_MS;
  // Reports past the grace window with no counterpart and no settled/disputed result yet.
  const orphans = await env.DB.prepare(
    `SELECT r.match_id, r.reporter_id, r.opponent_id, r.result, r.username, r.season_id
       FROM match_reports r
       LEFT JOIN match_reports o
         ON o.match_id = r.match_id AND o.reporter_id = r.opponent_id
       LEFT JOIN match_results res ON res.match_id = r.match_id
      WHERE r.created_at < ? AND o.match_id IS NULL AND res.match_id IS NULL
      ORDER BY r.created_at ASC
      LIMIT 200`,
  ).bind(cutoff).all<{
    match_id: string; reporter_id: string; opponent_id: string;
    result: string; username: string | null; season_id: number;
  }>();

  let settled = 0, skipped = 0;
  const winsThisSweep = new Map<string, number>();

  for (const row of orphans.results ?? []) {
    if (row.result === "win") {
      const used = winsThisSweep.get(row.reporter_id) ?? 0;
      if (used >= MAX_FORFEIT_WINS_PER_SWEEP) { skipped++; continue; }
      winsThisSweep.set(row.reporter_id, used + 1);
    }
    const winnerId = row.result === "win" ? row.reporter_id : row.opponent_id;
    const loserId  = row.result === "win" ? row.opponent_id : row.reporter_id;

    // Same exactly-once claim the dual-report path uses: the PK guard means only one writer computes.
    const claim = await env.DB.prepare(
      "INSERT OR IGNORE INTO match_results (match_id, winner_id, loser_id, status, settled_at) VALUES (?,?,?,?,?)",
    ).bind(row.match_id, winnerId, loserId, "forfeit", nowMs).run();
    if (claim.meta.changes !== 1) { skipped++; continue; }

    const w = await loadProfile(env, winnerId);
    const l = await loadProfile(env, loserId);
    const wPreR = w.rating, wPreRd = w.rd, lPreR = l.rating, lPreRd = l.rd;
    applyMatch(w, true, lPreR, lPreRd, row.season_id);
    applyMatch(l, false, wPreR, wPreRd, row.season_id);
    const reporterName = row.username ?? null;
    await env.DB.batch([
      upsertStmt(env, winnerId, w, winnerId === row.reporter_id ? reporterName : null, nowMs),
      upsertStmt(env, loserId,  l, loserId  === row.reporter_id ? reporterName : null, nowMs),
    ]);
    settled++;
  }
  return { settled, skipped };
}

async function handleProfile(url: URL, env: Env): Promise<Response> {
  const playerId = url.searchParams.get("playerId")?.trim();
  if (!playerId) return json({ error: "playerId required" }, 400);
  const row = await env.DB.prepare("SELECT * FROM ranked_profiles WHERE player_id = ?")
    .bind(playerId).first<any>();
  const p = rowToProfile(row);
  // Same NULL-username fallback the leaderboard uses, so a player's own profile and their row on the
  // board never disagree about who they are.
  let name: string | null = row?.username ?? null;
  if (!name) {
    const reported = await env.DB.prepare(
      `SELECT username FROM match_reports
        WHERE reporter_id = ? AND username IS NOT NULL
        ORDER BY created_at DESC LIMIT 1`,
    ).bind(playerId).first<{ username: string }>();
    name = reported?.username ?? null;
  }
  return json({ profile: publicProfile(playerId, p, name) });
}

// All /queue/* endpoints are authenticated (identity = token sub), like /report.
async function handleQueueRoute(req: Request, url: URL, env: Env): Promise<Response> {
  let playerId: string;
  try {
    ({ playerId } = await verifyUnityToken(req.headers.get("Authorization")));
  } catch (e: any) {
    return json({ error: "unauthorized", detail: String(e?.message ?? e) }, 401);
  }
  const body = req.method === "POST" ? await req.json<any>().catch(() => ({})) : {};
  switch (url.pathname) {
    case "/queue/join":       return json(await handleQueueJoin(env, playerId, body));
    case "/queue/poll":       return json(await handleQueuePoll(env, playerId));
    case "/queue/ready":      return json(await handleQueueReady(env, playerId, body));
    case "/queue/host-ready": return json(await handleQueueHostReady(env, playerId, body));
    case "/queue/cancel":     return json(await handleQueueCancel(env, playerId));
    default:                  return json({ error: "not found" }, 404);
  }
}

// All /chat/* and /invite/* endpoints are authenticated (identity = token sub), like /queue/*.
async function handleSocialRoute(req: Request, url: URL, env: Env): Promise<Response> {
  let playerId: string;
  try {
    ({ playerId } = await verifyUnityToken(req.headers.get("Authorization")));
  } catch (e: any) {
    return json({ error: "unauthorized", detail: String(e?.message ?? e) }, 401);
  }
  const body = req.method === "POST" ? await req.json<any>().catch(() => ({})) : {};
  switch (url.pathname) {
    case "/chat/send":      return json(await handleChatSend(env, playerId, body));
    case "/chat/history":   return json(await handleChatHistory(env, playerId, url));
    case "/chat/read":      return json(await handleChatRead(env, playerId, body));
    case "/chat/poll":      return json(await handleChatPoll(env, playerId));
    case "/invite/send":    return json(await handleInviteSend(env, playerId, body));
    case "/invite/poll":    return json(await handleInvitePoll(env, playerId));
    case "/invite/respond": return json(await handleInviteRespond(env, playerId, body));
    case "/invite/status":  return json(await handleInviteStatus(env, playerId, url));
    case "/invite/cancel":  return json(await handleInviteCancel(env, playerId, body));
    default:                return json({ error: "not found" }, 404);
  }
}

async function handleLeaderboard(url: URL, env: Env): Promise<Response> {
  const limit = Math.min(Math.max(parseInt(url.searchParams.get("limit") ?? "100", 10) || 100, 1), 200);
  // A profile row can carry a NULL username: it was written before both sides were named at
  // settlement, or that player's client had no display name to send at the time. Settlement only
  // names players going forward, so such a row shows as "Unknown Pirate" on the board forever,
  // however many matches they go on to play. Fall back to the most recent name they DID report, so
  // an existing row is repaired on read instead of waiting for something to rewrite it.
  const rows = await env.DB.prepare(
    `SELECT p.player_id,
            COALESCE(p.username,
                     (SELECT r.username
                        FROM match_reports r
                       WHERE r.reporter_id = p.player_id AND r.username IS NOT NULL
                       ORDER BY r.created_at DESC
                       LIMIT 1)) AS username,
            p.bounty, p.peak_bounty, p.games
       FROM ranked_profiles p
      WHERE p.placement_games_left = 0
      ORDER BY p.bounty DESC
      LIMIT ?`,
  ).bind(limit).all<any>();
  const entries = (rows.results ?? []).map((r: any, i: number) => ({
    rank: i + 1,
    playerId: r.player_id,
    username: r.username,
    bounty: r.bounty,
    peakBounty: r.peak_bounty,
    games: r.games,
    tierName: TIERS[tierIndexForBounty(r.bounty)].name,
  }));
  return json({ entries });
}

export default {
  // Cron-driven forfeit settlement. Without this a hard quit (Alt-F4 / kill / pulled cable) left the
  // survivor's report with no counterpart forever: no rating moved, the quitter escaped the loss and
  // the winner was denied the win. Cadence is set by [triggers] crons in wrangler.toml.
  async scheduled(_event: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    // Housekeeping write moved off /invite/poll's read path (see social.ts). Independent of the
    // forfeit sweep: its own waitUntil + catch, so a failure here can never stop forfeits settling.
    ctx.waitUntil(
      sweepExpiredInvites(env, Date.now())
        .then((n) => console.log(`invite expiry sweep: expired=${n}`))
        .catch((e) => console.log(`invite expiry sweep failed: ${e?.message ?? e}`)),
    );
    ctx.waitUntil(
      sweepForfeits(env, Date.now())
        .then((r) => console.log(`forfeit sweep: settled=${r.settled} skipped=${r.skipped}`))
        .catch((e) => console.log(`forfeit sweep failed: ${e?.message ?? e}`)),
    );
  },

  async fetch(req: Request, env: Env): Promise<Response> {
    const url = new URL(req.url);

    // Coarse gate (defense in depth): the same shared app-secret pattern the
    // gamelogs worker uses, to keep randos off the endpoint. Real security is
    // the per-player token on /report.
    if (env.APP_SECRET && req.headers.get("X-App-Secret") !== env.APP_SECRET) {
      return json({ error: "forbidden" }, 403);
    }

    try {
      if (url.pathname.startsWith("/queue/")) return await handleQueueRoute(req, url, env);
      if (url.pathname.startsWith("/chat/") || url.pathname.startsWith("/invite/"))
        return await handleSocialRoute(req, url, env);
      if (req.method === "POST" && url.pathname === "/report") return await handleReport(req, env);
      if (req.method === "GET" && url.pathname === "/profile") return await handleProfile(url, env);
      if (req.method === "GET" && url.pathname === "/leaderboard") return await handleLeaderboard(url, env);
      if (req.method === "GET" && url.pathname === "/health") return json({ ok: true });
      return json({ error: "not found" }, 404);
    } catch (e: any) {
      return json({ error: "server error", detail: String(e?.message ?? e) }, 500);
    }
  },
};
