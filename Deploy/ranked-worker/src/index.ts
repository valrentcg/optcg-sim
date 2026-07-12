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

  // Opponent's half in yet?
  const opp = await env.DB.prepare(
    "SELECT result, opponent_id FROM match_reports WHERE match_id = ? AND reporter_id = ?",
  ).bind(matchId, opponentId).first<{ result: string; opponent_id: string }>();

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

    // usernames: prefer each player's own reported name.
    const winnerName = winnerId === playerId ? username : null;
    const loserName = loserId === playerId ? username : null;
    await env.DB.batch([
      upsertStmt(env, winnerId, w, winnerName, now),
      upsertStmt(env, loserId, l, loserName, now),
    ]);
  }

  const p = await loadProfile(env, playerId);
  return json({ status: "settled", profile: publicProfile(playerId, p, username) });
}

async function handleProfile(url: URL, env: Env): Promise<Response> {
  const playerId = url.searchParams.get("playerId")?.trim();
  if (!playerId) return json({ error: "playerId required" }, 400);
  const row = await env.DB.prepare("SELECT * FROM ranked_profiles WHERE player_id = ?")
    .bind(playerId).first<any>();
  const p = rowToProfile(row);
  return json({ profile: publicProfile(playerId, p, row?.username ?? null) });
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

async function handleLeaderboard(url: URL, env: Env): Promise<Response> {
  const limit = Math.min(Math.max(parseInt(url.searchParams.get("limit") ?? "100", 10) || 100, 1), 200);
  const rows = await env.DB.prepare(
    `SELECT player_id, username, bounty, peak_bounty, games
       FROM ranked_profiles
      WHERE placement_games_left = 0
      ORDER BY bounty DESC
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
