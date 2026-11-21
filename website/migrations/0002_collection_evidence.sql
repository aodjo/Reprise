ALTER TABLE listen_events ADD COLUMN evidence TEXT NOT NULL DEFAULT 'app_state';
ALTER TABLE listen_events ADD COLUMN provider TEXT NOT NULL DEFAULT 'unknown';
ALTER TABLE listen_events ADD COLUMN review_state TEXT NOT NULL DEFAULT 'accepted';
ALTER TABLE listen_events ADD COLUMN review_reason TEXT;
