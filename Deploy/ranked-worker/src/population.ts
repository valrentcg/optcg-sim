// Authenticated, TTL-based population counters. A live count is only a client that
// refreshed within PRESENCE_TTL_MS; a match count is never inferred from a proposal.

const PRESENCE_TTL_MS = 90 * 1000;
const ACTIVITIES = new Set(["menu", "match_ranked", "match_casual", "match_custom"]);

export async function heartbeatPopulation(env: { DB: D1Database }, playerId: string, body: any) {
  const activity = ACTIVITIES.has(body?.activity) ? body.activity : "menu";
  const now = Date.now();
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO player_registry (player_id, first_seen_at, last_seen_at) VALUES (?,?,?)
      ON CONFLICT(player_id) DO UPDATE SET last_seen_at = excluded.last_seen_at`).bind(playerId, now, now),
    env.DB.prepare(`INSERT INTO player_presence (player_id, activity, last_seen_at) VALUES (?,?,?)
      ON CONFLICT(player_id) DO UPDATE SET activity = excluded.activity, last_seen_at = excluded.last_seen_at`).bind(playerId, activity, now),
  ]);
  return { ok: true, ttlSeconds: PRESENCE_TTL_MS / 1000 };
}

export async function populationSnapshot(env: { DB: D1Database }) {
  const cutoff = Date.now() - PRESENCE_TTL_MS;
  const [registered, online, waiting, matches] = await Promise.all([
    env.DB.prepare("SELECT COUNT(*) AS n FROM player_registry").first<{ n: number }>(),
    env.DB.prepare("SELECT COUNT(*) AS n FROM player_presence WHERE last_seen_at >= ?").bind(cutoff).first<{ n: number }>(),
    // Queue rows can survive an app crash. Count only waiting players with a
    // fresh authenticated client heartbeat; a stale row is not a live player.
    env.DB.prepare(`SELECT q.mode, COUNT(*) AS n FROM queue q
      JOIN player_presence p ON p.player_id = q.player_id
      WHERE q.proposal_id IS NULL AND p.last_seen_at >= ?
      GROUP BY q.mode`).bind(cutoff).all<{ mode: string; n: number }>(),
    env.DB.prepare("SELECT activity, COUNT(*) AS n FROM player_presence WHERE last_seen_at >= ? AND activity LIKE 'match_%' GROUP BY activity").bind(cutoff).all<{ activity: string; n: number }>(),
  ]);
  const queue: Record<string, number> = { ranked: 0, casual: 0 };
  for (const row of waiting.results ?? []) if (row.mode === "ranked" || row.mode === "casual") queue[row.mode] = row.n;
  const inMatch: Record<string, number> = { ranked: 0, casual: 0, custom: 0 };
  for (const row of matches.results ?? []) {
    const mode = row.activity.replace("match_", "");
    if (mode in inMatch) inMatch[mode] = row.n;
  }
  return { registered: registered?.n ?? 0, online: online?.n ?? 0, queue, inMatch, ttlSeconds: PRESENCE_TTL_MS / 1000 };
}
