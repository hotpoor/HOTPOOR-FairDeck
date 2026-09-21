-- Apply unchanged to blockchain1 and blockchain2; each has only this application table.
CREATE TABLE IF NOT EXISTS entities (
 block_id char(32) PRIMARY KEY CHECK(block_id ~ '^[0-9a-f]{32}$'),
 body jsonb NOT NULL,
 updatetime timestamptz NOT NULL DEFAULT now(),
 createtime timestamptz NOT NULL DEFAULT now()
);
