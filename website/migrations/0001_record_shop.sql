PRAGMA foreign_keys = ON;

CREATE TABLE listeners (
  id TEXT PRIMARY KEY,
  google_sub TEXT NOT NULL UNIQUE,
  handle TEXT NOT NULL UNIQUE COLLATE NOCASE,
  display_name TEXT NOT NULL,
  public INTEGER NOT NULL DEFAULT 0 CHECK (public IN (0, 1)),
  created_at INTEGER NOT NULL
);

CREATE TABLE sessions (
  token_hash TEXT PRIMARY KEY,
  listener_id TEXT NOT NULL REFERENCES listeners(id) ON DELETE CASCADE,
  expires_at INTEGER NOT NULL
);

CREATE TABLE oauth_states (
  state_hash TEXT PRIMARY KEY,
  nonce TEXT NOT NULL,
  verifier TEXT NOT NULL,
  return_to TEXT NOT NULL,
  expires_at INTEGER NOT NULL
);

CREATE TABLE device_grants (
  code_hash TEXT PRIMARY KEY,
  user_code TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  listener_id TEXT REFERENCES listeners(id) ON DELETE CASCADE,
  expires_at INTEGER NOT NULL,
  consumed_at INTEGER,
  last_poll_at INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE devices (
  id TEXT PRIMARY KEY,
  listener_id TEXT NOT NULL REFERENCES listeners(id) ON DELETE CASCADE,
  token_hash TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  last_seen_at INTEGER,
  revoked_at INTEGER
);

CREATE TABLE tracks (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT NOT NULL,
  duration_seconds REAL,
  artwork_key TEXT,
  created_at INTEGER NOT NULL
);

CREATE TABLE listen_events (
  device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
  event_id TEXT NOT NULL,
  listener_id TEXT NOT NULL REFERENCES listeners(id) ON DELETE CASCADE,
  track_id TEXT NOT NULL REFERENCES tracks(id),
  started_at INTEGER NOT NULL,
  ended_at INTEGER NOT NULL,
  seconds REAL NOT NULL CHECK (seconds > 0 AND seconds <= 30),
  PRIMARY KEY (device_id, event_id)
);
CREATE INDEX listen_chart ON listen_events(ended_at, track_id, listener_id);
CREATE INDEX listen_overlap ON listen_events(listener_id, started_at, ended_at);
CREATE INDEX listen_profile ON listen_events(listener_id, ended_at);
CREATE INDEX session_expiry ON sessions(expires_at);
