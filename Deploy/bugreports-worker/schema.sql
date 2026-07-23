-- One Piece TCG — centralized bug reports (D1 schema).
-- Apply with:  wrangler d1 execute optcg-bugreports --file=./schema.sql            (local)
--          or  wrangler d1 execute optcg-bugreports --file=./schema.sql --remote   (prod)
--
-- One row per submitted bug report. `raw_json` keeps the ENTIRE client BugReport verbatim
-- (future-proof: new client fields survive even before this schema knows about them); the
-- extracted columns exist only so the reports can be filtered/sorted without parsing JSON.

CREATE TABLE IF NOT EXISTS bug_reports (
  id              TEXT PRIMARY KEY,          -- client Id (yyyyMMdd-HHmmss-fff); server re-mints on collision
  created_at      INTEGER NOT NULL,          -- epoch ms at server receipt (sort key)
  created_iso     TEXT,                      -- client CreatedAtIso
  addressed       INTEGER NOT NULL DEFAULT 0,-- 0 = open, 1 = fixed
  addressed_at    TEXT,
  addressed_note  TEXT,

  description     TEXT,                       -- the player's words
  app_version     TEXT,
  player_identity TEXT,                       -- reporter (account key / guest)
  local_seat      TEXT,

  card_id         TEXT,
  card_name       TEXT,
  card_zone       TEXT,
  card_instance   TEXT,

  turn            INTEGER,
  phase           TEXT,
  active_seat     TEXT,
  match_status    TEXT,

  seed            TEXT,
  south_deck_id   TEXT,
  north_deck_id   TEXT,
  south_leader    TEXT,
  north_leader    TEXT,

  state_summary   TEXT,                       -- human-skimmable snapshot
  command_history TEXT,                       -- JSON array — the exact-repro command log
  raw_json        TEXT                        -- the full client BugReport JSON
);

CREATE INDEX IF NOT EXISTS idx_bug_open    ON bug_reports (addressed, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_bug_card    ON bug_reports (card_id);
CREATE INDEX IF NOT EXISTS idx_bug_created ON bug_reports (created_at DESC);
