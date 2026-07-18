-- One Piece TCG ranked (Bounty) — D1 schema.
-- Apply with:  wrangler d1 execute optcg-ranked --file=./schema.sql   (add --remote for prod)

-- One row per player: the whole ladder lives here, so leaderboards are a single query.
CREATE TABLE IF NOT EXISTS ranked_profiles (
  player_id            TEXT PRIMARY KEY,       -- UGS Authentication player id (token sub)
  rating               REAL    NOT NULL DEFAULT 1500,   -- hidden Glicko-2
  rd                   REAL    NOT NULL DEFAULT 350,
  volatility           REAL    NOT NULL DEFAULT 0.06,
  bounty               INTEGER NOT NULL DEFAULT 0,       -- visible Berries
  peak_bounty          INTEGER NOT NULL DEFAULT 0,
  placement_games_left INTEGER NOT NULL DEFAULT 5,
  win_streak           INTEGER NOT NULL DEFAULT 0,
  vivre_charge         INTEGER NOT NULL DEFAULT 0,
  vivre_ready          INTEGER NOT NULL DEFAULT 0,       -- 0/1
  season_id            INTEGER NOT NULL DEFAULT 0,
  games                INTEGER NOT NULL DEFAULT 0,
  username             TEXT,
  updated_at           INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_ranked_bounty ON ranked_profiles (bounty DESC);

-- Each player's half of a match. Settlement needs both halves to agree.
CREATE TABLE IF NOT EXISTS match_reports (
  match_id    TEXT    NOT NULL,   -- shared UGS session id
  reporter_id TEXT    NOT NULL,   -- authenticated token sub
  opponent_id TEXT    NOT NULL,
  result      TEXT    NOT NULL,   -- 'win' | 'loss'
  username    TEXT,
  season_id   INTEGER NOT NULL,
  created_at  INTEGER NOT NULL,
  PRIMARY KEY (match_id, reporter_id)
);

-- Settlement guard: PK on match_id means a match is computed exactly once.
CREATE TABLE IF NOT EXISTS match_results (
  match_id   TEXT PRIMARY KEY,
  winner_id  TEXT,
  loser_id   TEXT,
  status     TEXT    NOT NULL,   -- 'settled' | 'disputed'
  settled_at INTEGER NOT NULL
);

-- ── Matchmaking (skill-range queue + ready check) ──
-- One row per player currently in the ranked queue.
CREATE TABLE IF NOT EXISTS queue (
  player_id   TEXT PRIMARY KEY,
  mmr         INTEGER NOT NULL,
  username    TEXT,
  mode        TEXT NOT NULL DEFAULT 'ranked',  -- 'ranked' | 'casual' (separate pools)
  enqueued_at INTEGER NOT NULL,
  proposal_id TEXT              -- NULL = actively waiting; set = in a ready-check proposal
);
CREATE INDEX IF NOT EXISTS idx_queue_waiting ON queue (mode, proposal_id, mmr);

-- A proposed match awaiting the ready check (accept/decline within READY_SECS).
CREATE TABLE IF NOT EXISTS proposals (
  id         TEXT PRIMARY KEY,
  player_a   TEXT NOT NULL,
  player_b   TEXT NOT NULL,
  host_id    TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  a_accepted INTEGER NOT NULL DEFAULT 0,
  b_accepted INTEGER NOT NULL DEFAULT 0,
  state      TEXT NOT NULL DEFAULT 'pending',  -- pending | accepted | matched | declined | expired
  match_id   TEXT                              -- UGS session id the host created; guest joins by id
);

-- ── Social: direct messages (persistent friend chat) ──
-- One row per message. History is a single indexed query per conversation; unread
-- badges are a grouped count of read_at IS NULL. Friendship is enforced client-side
-- (the graph lives in UGS Friends, not here) — the worker just stores/relays.
CREATE TABLE IF NOT EXISTS dm_messages (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  from_id    TEXT    NOT NULL,   -- authenticated token sub of the sender
  to_id      TEXT    NOT NULL,
  body       TEXT    NOT NULL,
  created_at INTEGER NOT NULL,
  read_at    INTEGER             -- NULL until the recipient has read it
);
CREATE INDEX IF NOT EXISTS idx_dm_pair   ON dm_messages (from_id, to_id, id);
CREATE INDEX IF NOT EXISTS idx_dm_unread ON dm_messages (to_id, read_at);

-- ── Social: quick game invites (invite a friend into a custom lobby) ──
-- The inviter hosts a UGS custom session, then rows its id here; the invitee polls,
-- accepts, and JoinByIdAsync's it — the exact "host publishes session id, guest joins
-- by id" handshake the ranked matchmaker already uses. Short-lived (INVITE_TTL_MS).
CREATE TABLE IF NOT EXISTS game_invites (
  id         TEXT    PRIMARY KEY,  -- uuid
  from_id    TEXT    NOT NULL,
  from_name  TEXT,                 -- inviter's display name (for the popup)
  to_id      TEXT    NOT NULL,
  session_id TEXT    NOT NULL,     -- UGS session id the invitee JoinByIdAsync's
  lobby_name TEXT,
  created_at INTEGER NOT NULL,
  status     TEXT    NOT NULL DEFAULT 'pending'  -- pending | accepted | declined | cancelled | expired
);
CREATE INDEX IF NOT EXISTS idx_invite_to ON game_invites (to_id, status, created_at);
