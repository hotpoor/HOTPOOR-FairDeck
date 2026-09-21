CREATE TABLE IF NOT EXISTS wallets (
 address text PRIMARY KEY, public_key jsonb NOT NULL,
 createtime timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS challenges (
 id char(32) PRIMARY KEY, address text NOT NULL, public_key jsonb NOT NULL,
 message text NOT NULL, expires_at timestamptz NOT NULL,
 used boolean NOT NULL DEFAULT false
);
CREATE TABLE IF NOT EXISTS sessions (
 token_hash char(64) PRIMARY KEY, address text NOT NULL REFERENCES wallets(address),
 expires_at timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS nodes (
 address text PRIMARY KEY REFERENCES wallets(address), ip inet NOT NULL,
 port integer NOT NULL CHECK(port BETWEEN 1 AND 65535),
 public boolean NOT NULL DEFAULT false, observed_ip inet NOT NULL,
 updatetime timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS game_members (
 game_id char(32) NOT NULL CHECK(game_id ~ '^[0-9a-f]{32}$'),
 address text NOT NULL REFERENCES wallets(address),
 PRIMARY KEY(game_id,address)
);
CREATE TABLE IF NOT EXISTS entity_index (
 block_id char(32) PRIMARY KEY CHECK(block_id ~ '^[0-9a-f]{32}$'),
 shard smallint NOT NULL CHECK(shard IN (1,2)), owner text NOT NULL REFERENCES wallets(address),
 visibility text NOT NULL CHECK(visibility IN ('private','public')),
 content_hash char(64) NOT NULL, storage_state text NOT NULL DEFAULT 'hot'
   CHECK(storage_state IN ('hot','pending','archived')),
 object_key text, createtime timestamptz NOT NULL DEFAULT now(),
 updatetime timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS entity_pending ON entity_index(storage_state);
CREATE INDEX IF NOT EXISTS node_seen ON nodes(updatetime);
