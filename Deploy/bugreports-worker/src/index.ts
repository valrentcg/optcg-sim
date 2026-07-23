// One Piece TCG — centralized bug-report Worker.
//
// Any player's client posts a bug report here; every report lands in one D1 table so it can be
// scraped from anywhere at any time. Two trust levels:
//   • POST /submit    — needs X-App-Secret (the shared app secret the client ships with). Lets ANY
//                        player submit. This secret is decompilable, so it only blocks casual abuse.
//   • GET  /list       — needs X-Admin-Secret (a stronger, client-UNKNOWN secret). Returns reports as
//     GET  /export       JSON (/list) or newline-delimited raw JSON (/export) for scraping.
//   • POST /addressed  — needs X-Admin-Secret. Marks report(s) fixed (id(s) + note) without deleting.
//   • GET  /health     — open; { ok: true }.
//
// The admin endpoints are what I query to triage + close out reports after a fix ships.

export interface Env {
  DB: D1Database;
  APP_SECRET: string;
  ADMIN_SECRET: string;
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

const str = (v: unknown): string | null =>
  v === undefined || v === null ? null : String(v);
const int = (v: unknown): number | null => {
  const n = Number(v);
  return Number.isFinite(n) ? Math.trunc(n) : null;
};

async function handleSubmit(req: Request, env: Env): Promise<Response> {
  let r: any;
  try { r = await req.json(); } catch { return json({ error: "bad json" }, 400); }
  if (!r || typeof r !== "object") return json({ error: "bad body" }, 400);

  const now = Date.now();
  // Trust the client Id for de-dup, but re-mint on collision so one client can't overwrite another's.
  let id = str(r.Id) || String(now);
  const existing = await env.DB.prepare("SELECT id FROM bug_reports WHERE id = ?").bind(id).first();
  if (existing) id = `${id}-${now.toString(36)}`;

  const cmdHistory = r.CommandHistory !== undefined ? JSON.stringify(r.CommandHistory) : null;

  await env.DB.prepare(
    `INSERT INTO bug_reports
       (id, created_at, created_iso, addressed, addressed_at, addressed_note,
        description, app_version, player_identity, local_seat,
        card_id, card_name, card_zone, card_instance,
        turn, phase, active_seat, match_status,
        seed, south_deck_id, north_deck_id, south_leader, north_leader,
        state_summary, command_history, raw_json)
     VALUES (?,?,?,?,?,?, ?,?,?,?, ?,?,?,?, ?,?,?,?, ?,?,?,?,?, ?,?,?)`,
  ).bind(
    id, now, str(r.CreatedAtIso), 0, null, null,
    str(r.Description), str(r.AppVersion), str(r.PlayerIdentity), str(r.LocalSeat),
    str(r.CardId), str(r.CardName), str(r.CardZone), str(r.CardInstanceId),
    int(r.Turn), str(r.Phase), str(r.ActiveSeat), str(r.Status),
    str(r.Seed), str(r.SouthDeckId), str(r.NorthDeckId), str(r.SouthLeaderId), str(r.NorthLeaderId),
    str(r.StateSummary), cmdHistory, JSON.stringify(r),
  ).run();

  return json({ ok: true, id });
}

// A compact row shape for /list (omits the heavy command_history + raw_json by default).
const LIST_COLS =
  "id, created_at, created_iso, addressed, addressed_at, addressed_note, description, app_version, " +
  "player_identity, local_seat, card_id, card_name, card_zone, turn, phase, active_seat, match_status, " +
  "seed, south_deck_id, north_deck_id, south_leader, north_leader, state_summary";

async function handleList(url: URL, env: Env): Promise<Response> {
  // ?addressed=0|1|all (default 0 = open only), ?limit (default 200, max 1000), ?offset, ?card=<id>, ?full=1
  const addressed = url.searchParams.get("addressed") ?? "0";
  const limit = Math.min(1000, Math.max(1, int(url.searchParams.get("limit")) ?? 200));
  const offset = Math.max(0, int(url.searchParams.get("offset")) ?? 0);
  const card = url.searchParams.get("card");
  const full = url.searchParams.get("full") === "1";
  const cols = full ? "*" : LIST_COLS;

  const where: string[] = [];
  const binds: unknown[] = [];
  if (addressed === "0" || addressed === "1") { where.push("addressed = ?"); binds.push(Number(addressed)); }
  if (card) { where.push("card_id = ?"); binds.push(card); }
  const whereSql = where.length ? `WHERE ${where.join(" AND ")}` : "";

  const rows = await env.DB.prepare(
    `SELECT ${cols} FROM bug_reports ${whereSql} ORDER BY created_at DESC LIMIT ? OFFSET ?`,
  ).bind(...binds, limit, offset).all();

  const openCount = (await env.DB.prepare("SELECT COUNT(*) AS c FROM bug_reports WHERE addressed = 0").first<any>())?.c ?? 0;
  const total = (await env.DB.prepare("SELECT COUNT(*) AS c FROM bug_reports").first<any>())?.c ?? 0;
  return json({ ok: true, open: openCount, total, count: rows.results?.length ?? 0, reports: rows.results ?? [] });
}

// Newline-delimited full raw_json — the exact same JSONL a scraper gets from the local file, but
// aggregated across every player. ?addressed=0|1|all (default all).
async function handleExport(url: URL, env: Env): Promise<Response> {
  const addressed = url.searchParams.get("addressed") ?? "all";
  const whereSql = addressed === "0" || addressed === "1" ? "WHERE addressed = ?" : "";
  const stmt = whereSql
    ? env.DB.prepare(`SELECT raw_json FROM bug_reports ${whereSql} ORDER BY created_at DESC`).bind(Number(addressed))
    : env.DB.prepare("SELECT raw_json FROM bug_reports ORDER BY created_at DESC");
  const rows = await stmt.all<any>();
  const body = (rows.results ?? []).map((r) => r.raw_json).filter(Boolean).join("\n");
  return new Response(body + (body ? "\n" : ""), { headers: { "content-type": "application/x-ndjson" } });
}

async function handleAddressed(req: Request, env: Env): Promise<Response> {
  let b: any;
  try { b = await req.json(); } catch { return json({ error: "bad json" }, 400); }
  const ids: string[] = Array.isArray(b?.ids) ? b.ids.map(String) : (b?.id ? [String(b.id)] : []);
  if (ids.length === 0) return json({ error: "no id(s)" }, 400);
  const note = str(b?.note) ?? "";
  const at = new Date().toISOString();
  let updated = 0;
  for (const id of ids) {
    const res = await env.DB.prepare(
      "UPDATE bug_reports SET addressed = 1, addressed_at = ?, addressed_note = ? WHERE id = ? AND addressed = 0",
    ).bind(at, note, id).run();
    updated += res.meta?.changes ?? 0;
  }
  return json({ ok: true, updated });
}

export default {
  async fetch(req: Request, env: Env): Promise<Response> {
    const url = new URL(req.url);
    try {
      if (req.method === "GET" && url.pathname === "/health") return json({ ok: true });

      // Submission: shared app secret (client ships it) — lets any player submit.
      if (req.method === "POST" && url.pathname === "/submit") {
        if (env.APP_SECRET && req.headers.get("X-App-Secret") !== env.APP_SECRET)
          return json({ error: "forbidden" }, 403);
        return await handleSubmit(req, env);
      }

      // Admin (scrape + triage): the stronger, client-unknown secret.
      const admin = env.ADMIN_SECRET && req.headers.get("X-Admin-Secret") === env.ADMIN_SECRET;
      if (url.pathname === "/list" || url.pathname === "/export" || url.pathname === "/addressed") {
        if (!admin) return json({ error: "forbidden" }, 403);
        if (req.method === "GET" && url.pathname === "/list") return await handleList(url, env);
        if (req.method === "GET" && url.pathname === "/export") return await handleExport(url, env);
        if (req.method === "POST" && url.pathname === "/addressed") return await handleAddressed(req, env);
      }
      return json({ error: "not found" }, 404);
    } catch (e: any) {
      return json({ error: "server error", detail: String(e?.message ?? e) }, 500);
    }
  },
};
