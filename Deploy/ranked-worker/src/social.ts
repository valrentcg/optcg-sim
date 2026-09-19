// One Piece TCG — social layer: persistent friend chat + quick game invites.
//
// Same shape as matchmaking.ts: stateless D1-backed handlers, each already handed the
// authenticated playerId (token sub) by the router, so a client can only ever act as
// itself. Poll-based (no Durable Objects / WebSockets), matching the ranked queue.
//
// Friendship itself is NOT stored here — the relationship graph lives in UGS Friends.
// The client only exposes chat/invite for actual friends; the worker just stores and
// relays. (A future block-list check could move server-side if abuse appears.)

interface Env { DB: D1Database; }

const MAX_MSG_LEN = 1000;      // per-message body cap
export const INVITE_TTL_MS = 30 * 60_000;  // a pending invite auto-expires after this

// ── Chat ──────────────────────────────────────────────────────────────────────

/// POST /chat/send { toId, body } → store one message from me to toId.
export async function handleChatSend(env: Env, me: string, body: any): Promise<any> {
  const toId = String(body.toId ?? "").trim();
  const text = String(body.body ?? "").trim();
  if (!toId) return { error: "toId required" };
  if (toId === me) return { error: "cannot message yourself" };
  if (!text) return { error: "empty message" };
  const clipped = text.slice(0, MAX_MSG_LEN);
  const now = Date.now();
  const res = await env.DB.prepare(
    "INSERT INTO dm_messages (from_id, to_id, body, created_at) VALUES (?,?,?,?)",
  ).bind(me, toId, clipped, now).run();
  return { ok: true, id: res.meta.last_row_id, createdAt: now };
}

/// GET /chat/history?withId&sinceId&beforeId&limit → chronological messages.
/// The first page is the newest conversation history; sinceId tails newer messages,
/// while beforeId pages older messages. The client never has to poll through years
/// of old messages before it can display the current conversation.
export async function handleChatHistory(env: Env, me: string, url: URL): Promise<any> {
  const withId = url.searchParams.get("withId")?.trim();
  if (!withId) return { error: "withId required" };
  const sinceId = Math.max(0, parseInt(url.searchParams.get("sinceId") ?? "0", 10) || 0);
  const beforeId = Math.max(0, parseInt(url.searchParams.get("beforeId") ?? "0", 10) || 0);
  const limit = Math.min(Math.max(parseInt(url.searchParams.get("limit") ?? "80", 10) || 80, 1), 200);
  const base = `SELECT id, from_id, to_id, body, created_at, read_at FROM dm_messages
    WHERE ((from_id = ? AND to_id = ?) OR (from_id = ? AND to_id = ?))`;
  const rows = beforeId > 0
    ? await env.DB.prepare(`${base} AND id < ? ORDER BY id DESC LIMIT ?`)
        .bind(me, withId, withId, me, beforeId, limit).all<any>()
    : sinceId > 0
      ? await env.DB.prepare(`${base} AND id > ? ORDER BY id ASC LIMIT ?`)
          .bind(me, withId, withId, me, sinceId, limit).all<any>()
      : await env.DB.prepare(`${base} ORDER BY id DESC LIMIT ?`)
          .bind(me, withId, withId, me, limit).all<any>();
  const ordered = (beforeId > 0 || sinceId === 0)
    ? [...(rows.results ?? [])].reverse() : (rows.results ?? []);
  const messages = ordered.map((r: any) => ({
    id: r.id, fromId: r.from_id, toId: r.to_id, body: r.body,
    createdAt: r.created_at, readAt: r.read_at, mine: r.from_id === me,
  }));
  return { messages };
}

/// POST /chat/read { withId } → mark every message FROM withId TO me as read.
export async function handleChatRead(env: Env, me: string, body: any): Promise<any> {
  const withId = String(body.withId ?? "").trim();
  if (!withId) return { error: "withId required" };
  const now = Date.now();
  await env.DB.prepare(
    "UPDATE dm_messages SET read_at = ? WHERE to_id = ? AND from_id = ? AND read_at IS NULL",
  ).bind(now, me, withId).run();
  return { ok: true };
}

/// GET /chat/poll → unread summary grouped by sender, for badges. Cheap to poll.
export async function handleChatPoll(env: Env, me: string): Promise<any> {
  const rows = await env.DB.prepare(
    `SELECT from_id, COUNT(*) AS cnt, MAX(id) AS last_id FROM dm_messages
      WHERE to_id = ? AND read_at IS NULL GROUP BY from_id`,
  ).bind(me).all<any>();
  const unread = (rows.results ?? []).map((r: any) => ({
    fromId: r.from_id, count: r.cnt, lastId: r.last_id,
  }));
  const total = unread.reduce((a: number, b: any) => a + (b.count as number), 0);
  return { unread, total };
}

/// GET /chat/conversations?limit -> one newest-first summary per person I have
/// exchanged a message with. This is the inexpensive inbox index: the client loads
/// full history only after the player opens a conversation.
export async function handleChatConversations(env: Env, me: string, url: URL): Promise<any> {
  const limit = Math.min(Math.max(parseInt(url.searchParams.get("limit") ?? "50", 10) || 50, 1), 100);
  const rows = await env.DB.prepare(
    `WITH conversation_messages AS (
       SELECT id, to_id AS peer_id, body, created_at, 1 AS mine, 0 AS unread
         FROM dm_messages WHERE from_id = ?
       UNION ALL
       SELECT id, from_id AS peer_id, body, created_at, 0 AS mine,
              CASE WHEN read_at IS NULL THEN 1 ELSE 0 END AS unread
         FROM dm_messages WHERE to_id = ?
     ), latest AS (
       SELECT peer_id, MAX(id) AS last_id, SUM(unread) AS unread_count
         FROM conversation_messages
        GROUP BY peer_id
     )
     SELECT m.peer_id, m.id AS last_id, m.body AS last_body,
            m.created_at AS last_created_at, m.mine, l.unread_count
       FROM latest l
       JOIN conversation_messages m
         ON m.peer_id = l.peer_id AND m.id = l.last_id
      ORDER BY m.id DESC
      LIMIT ?`,
  ).bind(me, me, limit).all<any>();
  const conversations = (rows.results ?? []).map((r: any) => ({
    peerId: r.peer_id,
    lastId: r.last_id,
    lastBody: r.last_body,
    lastCreatedAt: r.last_created_at,
    lastMine: r.mine === 1,
    unreadCount: r.unread_count,
  }));
  return { conversations };
}

// ── Invites ─────────────────────────────────────────────────────────────────

/// POST /invite/send { toId, sessionId, lobbyName, fromName } → invite toId into my
/// custom lobby. Replaces any earlier still-pending invite from me to the same player.
export async function handleInviteSend(env: Env, me: string, body: any): Promise<any> {
  const toId = String(body.toId ?? "").trim();
  const sessionId = String(body.sessionId ?? "").trim();
  const lobbyName = body.lobbyName ? String(body.lobbyName).slice(0, 60) : null;
  const fromName = body.fromName ? String(body.fromName).slice(0, 40) : null;
  if (!toId || !sessionId) return { error: "toId and sessionId required" };
  if (toId === me) return { error: "cannot invite yourself" };
  const now = Date.now();
  await env.DB.prepare(
    "UPDATE game_invites SET status = 'cancelled' WHERE from_id = ? AND to_id = ? AND status = 'pending'",
  ).bind(me, toId).run();
  const id = crypto.randomUUID();
  await env.DB.prepare(
    `INSERT INTO game_invites (id, from_id, from_name, to_id, session_id, lobby_name, created_at, status)
     VALUES (?,?,?,?,?,?,?, 'pending')`,
  ).bind(id, me, fromName, toId, sessionId, lobbyName, now).run();
  return { ok: true, inviteId: id };
}

/// GET /invite/poll → my pending (unexpired) invites.
///
/// READ-ONLY on purpose. This used to run a table-wide
/// `UPDATE game_invites SET status='expired' ...` before the read, i.e. a D1 WRITE on every
/// poll — and every signed-in client in the menu polls this every 3 seconds (MainMenuManager's
/// social loop), so it was ~20 writes/minute per idle client purely for housekeeping. That is
/// write amplification on a read path, and a real client log shows this endpoint returning
/// HTTP 500 while the read-only /chat/poll beside it only ever timed out.
/// Expiry is now (a) enforced on READ by the age predicate below, so behaviour is identical and
/// an expired invite is never returned, and (b) actually written back by the scheduled sweep in
/// index.ts, which already runs for forfeits. Correctness does not depend on the write landing.
export async function handleInvitePoll(env: Env, me: string): Promise<any> {
  const now = Date.now();
  const rows = await env.DB.prepare(
    `SELECT id, from_id, from_name, session_id, lobby_name, created_at FROM game_invites
      WHERE to_id = ? AND status = 'pending' AND created_at >= ?
      ORDER BY created_at DESC LIMIT 5`,
  ).bind(me, now - INVITE_TTL_MS).all<any>();
  const invites = (rows.results ?? []).map((r: any) => ({
    id: r.id, fromId: r.from_id, fromName: r.from_name,
    sessionId: r.session_id, lobbyName: r.lobby_name, createdAt: r.created_at,
  }));
  return { invites };
}

/// POST /invite/respond { inviteId, accept } → accept/decline. Returns the session id
/// on accept so the client can JoinByIdAsync it.
export async function handleInviteRespond(env: Env, me: string, body: any): Promise<any> {
  const inviteId = String(body.inviteId ?? "").trim();
  const accept = body.accept === true;
  if (!inviteId) return { error: "inviteId required" };
  const inv = await env.DB.prepare("SELECT * FROM game_invites WHERE id = ?").bind(inviteId).first<any>();
  if (!inv) return { error: "not found" };
  if (inv.to_id !== me) return { error: "not your invite" };
  if (inv.status !== "pending") return { status: inv.status, sessionId: inv.session_id };

  // Poll hides stale rows immediately, but a client can still respond to an invite it
  // rendered earlier. Enforce the same boundary here so that stale UI can never join an
  // expired room. Responding is already a write path, so persisting the derived state does
  // not create the idle write amplification avoided in handleInvitePoll.
  const now = Date.now();
  if (inv.created_at < now - INVITE_TTL_MS) {
    const expired = await env.DB.prepare(
      "UPDATE game_invites SET status = 'expired' WHERE id = ? AND status = 'pending'",
    ).bind(inviteId).run();
    if (expired.meta.changes !== 1) {
      const current = await env.DB.prepare(
        "SELECT status FROM game_invites WHERE id = ?",
      ).bind(inviteId).first<{ status: string }>();
      return { status: current?.status ?? "unknown", sessionId: inv.session_id };
    }
    return { status: "expired", sessionId: inv.session_id };
  }

  const newStatus = accept ? "accepted" : "declined";
  const changed = await env.DB.prepare(
    "UPDATE game_invites SET status = ? WHERE id = ? AND status = 'pending' AND created_at >= ?",
  ).bind(newStatus, inviteId, now - INVITE_TTL_MS).run();
  if (changed.meta.changes !== 1) {
    const current = await env.DB.prepare(
      "SELECT status FROM game_invites WHERE id = ?",
    ).bind(inviteId).first<{ status: string }>();
    return { status: current?.status ?? "unknown", sessionId: inv.session_id };
  }
  return { ok: true, status: newStatus, sessionId: inv.session_id };
}

/// GET /invite/status?inviteId → the inviter polls an invite it sent, so it can tell
/// when the guest accepted (guest is joining) or declined. Only the sender may read it.
export async function handleInviteStatus(env: Env, me: string, url: URL): Promise<any> {
  const inviteId = url.searchParams.get("inviteId")?.trim();
  if (!inviteId) return { error: "inviteId required" };
  const inv = await env.DB.prepare(
    "SELECT from_id, to_id, status, created_at FROM game_invites WHERE id = ?",
  ).bind(inviteId).first<any>();
  if (!inv || inv.from_id !== me) return { status: "unknown" };
  if (inv.status === "pending" && inv.created_at < Date.now() - INVITE_TTL_MS)
    return { status: "expired" };
  return { status: inv.status };
}

/// POST /invite/cancel { inviteId } → inviter withdraws a pending invite.
export async function handleInviteCancel(env: Env, me: string, body: any): Promise<any> {
  const inviteId = String(body.inviteId ?? "").trim();
  if (!inviteId) return { error: "inviteId required" };
  await env.DB.prepare(
    "UPDATE game_invites SET status = 'cancelled' WHERE id = ? AND from_id = ? AND status = 'pending'",
  ).bind(inviteId, me).run();
  return { ok: true };
}

/// Housekeeping write for expired invites, moved OFF the /invite/poll read path (see the note
/// there). Called from the scheduled sweep. Purely cosmetic for correctness — the poll already
/// filters by age — so a failure here must never take the cron down with it.
export async function sweepExpiredInvites(env: Env, nowMs: number): Promise<number> {
  const res = await env.DB.prepare(
    "UPDATE game_invites SET status = 'expired' WHERE status = 'pending' AND created_at < ?",
  ).bind(nowMs - INVITE_TTL_MS).run();
  return (res as any)?.meta?.changes ?? 0;
}
