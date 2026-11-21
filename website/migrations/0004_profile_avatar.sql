ALTER TABLE listeners ADD COLUMN avatar_key TEXT;
CREATE UNIQUE INDEX listener_avatar ON listeners(avatar_key) WHERE avatar_key IS NOT NULL;
