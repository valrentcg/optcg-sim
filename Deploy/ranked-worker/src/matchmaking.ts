// Server-authoritative ranked matchmaker: skill-range pairing + League-style
// ready check, on Cloudflare D1. Stateless/serverless — all pairing and ready-check
// progress happens opportunistically on each request (enqueue/poll/ready), keyed
// off timestamps, so no background loop or Durable Object is needed.
//
// Flow:
//   join  → find a waiting opponent within an MMR range (widening with wait time);
//           if found, create a ready-check proposal; else park as 'waiting'.
//   poll  → report my state; a waiting player also retries pairing (so two long-
//           waiters converge as their ranges widen).
//   ready → accept/decline. Both accept → 'accepted' (host then makes the session).
//           Decline/timeout → the accepter is requeued, the other leaves.
//   host-ready → host posts the UGS session join code → 'matched'; guest polls it.
//
// The pure helpers (range/inRange/pickOpponent) are exported for unit testing.

export const READY_SECS = 15;          // ready-check window
const BASE_RANGE = 100;                // starting ± MMR match window
const RANGE_STEP = 50;                 // widen by this…
const RANGE_INTERVAL_MS = 5000;        // …every this long waiting
const MAX_RANGE = 800;                 // cap (past this, essentially "anyone")
// An accepted/matched proposal older than this is a finished or abandoned match, not a
// live connect handshake (which completes in seconds). Past it we drop the proposal so a
// re-queue isn't trapped on a dead session — the never-expiring-matched-proposal bug that
// stranded players pinned to a match that already ended, unable to queue again.
const STALE_MATCH_MS = 120_000;        // 2 min — generous vs. real UGS session setup

// ── Pure, testable matchmaking math ──────────────────────────────────────────

export function mmrRange(waitMs: number): number {
  const steps = Math.floor(Math.max(0, waitMs) / RANGE_INTERVAL_MS);
  return Math.min(BASE_RANGE + steps * RANGE_STEP, MAX_RANGE);
}

/// Two players match if their MMR gap is within EITHER player's (wait-widened) range.
export function inRange(mmrA: number, waitA: number, mmrB: number, waitB: number): boolean {
  return Math.abs(mmrA - mmrB) <= Math.max(mmrRange(waitA), mmrRange(waitB));
}

export interface Waiter { player_id: string; mmr: number; enqueued_at: number; username?: string | null; }

/// Closest in-range opponent (tie-break: longest-waiting), or null.
export function pickOpponent(myMmr: number, myEnqueuedAt: number, candidates: Waiter[], now: number): Waiter | null {
  const myWait = now - myEnqueuedAt;
  let best: Waiter | null = null;
  let bestDiff = Infinity;
  for (const c of candidates) {
    const cWait = now - c.enqueued_at;
    if (!inRange(myMmr, myWait, c.mmr, cWait)) continue;
    const diff = Math.abs(myMmr - c.mmr);
    if (diff < bestDiff || (diff === bestDiff && best !== null && c.enqueued_at < best.enqueued_at)) {
      best = c; bestDiff = diff;
    }
  }
  return best;
}

// ── D1-backed handlers (each already has the authenticated playerId) ──────────

interface Env { DB: D1Database; }
interface QueueRow { player_id: string; mmr: number; username: string | null; mode: string; enqueued_at: number; proposal_id: string | null; }
interface Proposal {
  id: string; player_a: string; player_b: string; host_id: string; created_at: number;
  a_accepted: number; b_accepted: number; state: string; match_id: string | null;
}

const clampInt = (v: any, lo: number, hi: number) => {
  const n = Math.round(Number(v) || 0);
  return n < lo ? lo : n > hi ? hi : n;
};

async function getQueue(env: Env, playerId: string): Promise<QueueRow | null> {
  return await env.DB.prepare("SELECT * FROM queue WHERE player_id = ?").bind(playerId).first<QueueRow>();
}
async function getProposal(env: Env, id: string): Promise<Proposal | null> {
  return await env.DB.prepare("SELECT * FROM proposals WHERE id = ?").bind(id).first<Proposal>();
}

// Drop a dead proposal and remove BOTH players' queue rows, so neither is left pinned to
// a session that no longer exists. Used when a re-queue (or a still-polling stuck client)
// finds an accepted/matched proposal too old to be a live handshake. The re-joining
// player is re-enqueued fresh by the caller right after.
async function clearProposal(env: Env, p: Proposal): Promise<void> {
  await env.DB.prepare("DELETE FROM queue WHERE proposal_id = ?").bind(p.id).run();
  await env.DB.prepare("DELETE FROM proposals WHERE id = ?").bind(p.id).run();
}

// Requeue accepters (reset their wait), remove non-accepters; mark proposal done.
async function endProposal(env: Env, p: Proposal, finalState: string, now: number): Promise<void> {
  for (const [pid, acc] of [[p.player_a, p.a_accepted], [p.player_b, p.b_accepted]] as [string, number][]) {
    if (acc) await env.DB.prepare("UPDATE queue SET proposal_id = NULL, enqueued_at = ? WHERE player_id = ?").bind(now, pid).run();
    else await env.DB.prepare("DELETE FROM queue WHERE player_id = ?").bind(pid).run();
  }
  await env.DB.prepare("UPDATE proposals SET state = ? WHERE id = ?").bind(finalState, p.id).run();
}

// Build the view of a proposal from `me`'s perspective, expiring it if the ready
// window has passed.
async function proposalView(env: Env, me: string, proposalId: string, now: number): Promise<any> {
  const p = await getProposal(env, proposalId);
  if (!p) return { status: "idle" };

  if (p.state === "pending" && now > p.created_at + READY_SECS * 1000) {
    const iAccepted = me === p.player_a ? !!p.a_accepted : !!p.b_accepted;
    await endProposal(env, p, "expired", now);
    return { status: iAccepted ? "requeued" : "idle" };
  }
  if (p.state === "declined" || p.state === "expired") {
    return { status: "idle" };
  }
  // Stuck-client recovery: an accepted/matched proposal this old means the connect never
  // completed (the "Connecting to your opponent…" screen has no Cancel). Clear it so the
  // client falls back to idle on its next poll instead of hanging forever.
  if ((p.state === "accepted" || p.state === "matched") && now - p.created_at > STALE_MATCH_MS) {
    await clearProposal(env, p);
    return { status: "idle" };
  }

  const amHost = me === p.host_id;
  const opponentId = me === p.player_a ? p.player_b : p.player_a;
  if (p.state === "matched") {
    // matchId is the UGS session id the host created; the guest joins it by id
    // (JoinByIdAsync) — no join code anywhere, fully automatic.
    return { status: "matched", role: amHost ? "host" : "guest", matchId: p.match_id, opponentId };
  }
  if (p.state === "accepted") {
    return { status: "accepted", role: amHost ? "host" : "guest", opponentId };
  }
  // pending
  const iAccepted = me === p.player_a ? !!p.a_accepted : !!p.b_accepted;
  const oppAccepted = me === p.player_a ? !!p.b_accepted : !!p.a_accepted;
  return {
    status: "proposed", proposalId: p.id, deadline: p.created_at + READY_SECS * 1000,
    amHost, opponentId, iAccepted, oppAccepted,
  };
}

async function tryPair(env: Env, me: string, myMmr: number, myEnqueuedAt: number, mode: string, now: number): Promise<any> {
  // Only pair within the same queue (ranked with ranked, casual with casual).
  const rows = await env.DB.prepare(
    "SELECT player_id, mmr, enqueued_at, username FROM queue WHERE proposal_id IS NULL AND mode = ? AND player_id != ?",
  ).bind(mode, me).all<Waiter>();
  const opp = pickOpponent(myMmr, myEnqueuedAt, rows.results ?? [], now);
  if (!opp) {
    const wait = now - myEnqueuedAt;
    return { status: "waiting", elapsedMs: wait, range: mmrRange(wait) };
  }

  const id = crypto.randomUUID();
  const host = me < opp.player_id ? me : opp.player_id;
  await env.DB.prepare(
    "INSERT INTO proposals (id, player_a, player_b, host_id, created_at, a_accepted, b_accepted, state) VALUES (?,?,?,?,?,0,0,'pending')",
  ).bind(id, me, opp.player_id, host, now).run();

  // Claim both queue rows only if still unproposed (guards a double-pair race).
  const r1 = await env.DB.prepare("UPDATE queue SET proposal_id = ? WHERE player_id = ? AND proposal_id IS NULL").bind(id, me).run();
  const r2 = await env.DB.prepare("UPDATE queue SET proposal_id = ? WHERE player_id = ? AND proposal_id IS NULL").bind(id, opp.player_id).run();
  if (r1.meta.changes !== 1 || r2.meta.changes !== 1) {
    await env.DB.prepare("UPDATE queue SET proposal_id = NULL WHERE proposal_id = ?").bind(id).run();
    await env.DB.prepare("DELETE FROM proposals WHERE id = ?").bind(id).run();
    const wait = now - myEnqueuedAt;
    return { status: "waiting", elapsedMs: wait, range: mmrRange(wait) };
  }
  return await proposalView(env, me, id, now);
}

export async function handleQueueJoin(env: Env, playerId: string, body: any): Promise<any> {
  const mmr = clampInt(body.mmr, 0, 4000);
  const username = body.username ? String(body.username).slice(0, 40) : null;
  const mode = body.mode === "casual" ? "casual" : "ranked";
  const now = Date.now();

  const myRow = await getQueue(env, playerId);
  if (myRow?.proposal_id) {
    const p = await getProposal(env, myRow.proposal_id);
    if (p && (p.state === "pending" || p.state === "accepted" || p.state === "matched")) {
      // A live ready-check (pending) or a genuine re-join during the connect handshake
      // (recently accepted/matched) — return the current view, don't re-enqueue.
      if (p.state === "pending" || now - p.created_at < STALE_MATCH_MS) {
        return await proposalView(env, playerId, p.id, now);
      }
      // Older accepted/matched = a match the player has already finished/abandoned. Drop
      // it so this deliberate re-queue starts a fresh search instead of a dead session.
      await clearProposal(env, p);
    }
    // declined/expired/missing/stale → fall through and re-enqueue fresh
  }

  const enqueuedAt = myRow && !myRow.proposal_id ? myRow.enqueued_at : now;
  await env.DB.prepare(
    `INSERT INTO queue (player_id, mmr, username, mode, enqueued_at, proposal_id) VALUES (?,?,?,?,?,NULL)
     ON CONFLICT(player_id) DO UPDATE SET mmr = excluded.mmr, username = excluded.username,
       mode = excluded.mode, enqueued_at = ?, proposal_id = NULL`,
  ).bind(playerId, mmr, username, mode, enqueuedAt, enqueuedAt).run();

  return await tryPair(env, playerId, mmr, enqueuedAt, mode, now);
}

export async function handleQueuePoll(env: Env, playerId: string): Promise<any> {
  const now = Date.now();
  const myRow = await getQueue(env, playerId);
  if (!myRow) return { status: "idle" };
  if (myRow.proposal_id) return await proposalView(env, playerId, myRow.proposal_id, now);
  // still waiting: retry pairing so two long-waiters converge as ranges widen
  return await tryPair(env, playerId, myRow.mmr, myRow.enqueued_at, myRow.mode, now);
}

export async function handleQueueReady(env: Env, playerId: string, body: any): Promise<any> {
  const now = Date.now();
  const myRow = await getQueue(env, playerId);
  if (!myRow?.proposal_id) return { status: "idle" };
  const p = await getProposal(env, myRow.proposal_id);
  if (!p || p.state !== "pending") return await proposalView(env, playerId, myRow.proposal_id, now);
  if (now > p.created_at + READY_SECS * 1000) { await endProposal(env, p, "expired", now); return { status: "idle" }; }

  if (body.accept === false) {
    const opp = playerId === p.player_a ? p.player_b : p.player_a;
    await env.DB.prepare("UPDATE queue SET proposal_id = NULL, enqueued_at = ? WHERE player_id = ?").bind(now, opp).run();
    await env.DB.prepare("DELETE FROM queue WHERE player_id = ?").bind(playerId).run();
    await env.DB.prepare("UPDATE proposals SET state = 'declined' WHERE id = ?").bind(p.id).run();
    return { status: "idle" };
  }

  const col = playerId === p.player_a ? "a_accepted" : "b_accepted";
  await env.DB.prepare(`UPDATE proposals SET ${col} = 1 WHERE id = ?`).bind(p.id).run();
  const p2 = await getProposal(env, p.id);
  if (p2 && p2.a_accepted && p2.b_accepted) {
    await env.DB.prepare("UPDATE proposals SET state = 'accepted' WHERE id = ?").bind(p.id).run();
  }
  return await proposalView(env, playerId, p.id, now);
}

export async function handleQueueHostReady(env: Env, playerId: string, body: any): Promise<any> {
  const now = Date.now();
  const myRow = await getQueue(env, playerId);
  if (!myRow?.proposal_id) return { status: "idle" };
  const p = await getProposal(env, myRow.proposal_id);
  if (!p) return { status: "idle" };
  if (p.host_id !== playerId) return await proposalView(env, playerId, p.id, now);
  if (p.state !== "accepted" && p.state !== "matched") return await proposalView(env, playerId, p.id, now);

  // Host has created the UGS session; publish its id so the guest can JoinByIdAsync.
  await env.DB.prepare("UPDATE proposals SET match_id = ?, state = 'matched' WHERE id = ?")
    .bind(body.matchId ?? null, p.id).run();
  return await proposalView(env, playerId, p.id, now);
}

export async function handleQueueCancel(env: Env, playerId: string): Promise<any> {
  const now = Date.now();
  const myRow = await getQueue(env, playerId);
  if (myRow?.proposal_id) {
    const p = await getProposal(env, myRow.proposal_id);
    if (p && (p.state === "pending" || p.state === "accepted")) {
      const opp = playerId === p.player_a ? p.player_b : p.player_a;
      await env.DB.prepare("UPDATE queue SET proposal_id = NULL, enqueued_at = ? WHERE player_id = ?").bind(now, opp).run();
      await env.DB.prepare("UPDATE proposals SET state = 'declined' WHERE id = ?").bind(p.id).run();
    }
  }
  await env.DB.prepare("DELETE FROM queue WHERE player_id = ?").bind(playerId).run();
  return { status: "idle" };
}
