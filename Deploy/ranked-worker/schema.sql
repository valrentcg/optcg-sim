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
