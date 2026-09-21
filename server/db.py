import json
import os
from contextlib import contextmanager
from pathlib import Path

import psycopg
from psycopg.rows import dict_row
from psycopg.types.json import Jsonb

from core.protocol import block_id, canonical, digest, shard


def config():
    return json.loads(Path(os.environ.get("FAIRDECK_CONFIG", "server/config.local.json")).read_text("utf-8-sig"))


class Store:
    def __init__(self, cfg):
        self.cfg = cfg

    @contextmanager
    def connection(self, number=None):
        key = self.cfg["index_dsn_env"] if number is None else self.cfg["shard_dsn_env"][str(number)]
        with psycopg.connect(os.environ[key], row_factory=dict_row) as conn:
            yield conn

    def initialize(self):
        sql = Path(__file__).parent / "sql"
        for number, file in ((None, "index.sql"), (1, "shard.sql"), (2, "shard.sql")):
            with self.connection(number) as conn:
                conn.execute((sql / file).read_text("utf-8"))

    def create(self, identifier, owner, visibility, body):
        identifier = block_id(identifier)
        number, content_hash = shard(identifier), digest(canonical(body))
        if visibility not in ("private", "public"):
            raise ValueError("invalid visibility")
        # Index transaction serializes multi-port writers. Shard writes are idempotent;
        # a crash before index commit can leave an orphan, but never a visible partial record.
        with self.connection() as index:
            index.execute("SELECT pg_advisory_xact_lock(%s)", (int(identifier[:15], 16),))
            old = index.execute("SELECT * FROM entity_index WHERE block_id=%s", (identifier,)).fetchone()
            if old:
                if (old["owner"], old["visibility"], old["content_hash"]) != (owner, visibility, content_hash):
                    raise ValueError("immutable entity conflict")
                return old
            with self.connection(number) as data:
                data.execute("INSERT INTO entities(block_id,body) VALUES(%s,%s) ON CONFLICT DO NOTHING",
                             (identifier, Jsonb(body)))
                stored = data.execute("SELECT body FROM entities WHERE block_id=%s", (identifier,)).fetchone()
                if digest(canonical(stored["body"])) != content_hash:
                    raise ValueError("orphan content conflict")
            return index.execute("""INSERT INTO entity_index(block_id,shard,owner,visibility,content_hash)
              VALUES(%s,%s,%s,%s,%s) RETURNING *""", (identifier, number, owner, visibility, content_hash)).fetchone()

    def read(self, identifier, viewer, cloud):
        with self.connection() as conn:
            record = conn.execute("SELECT * FROM entity_index WHERE block_id=%s", (block_id(identifier),)).fetchone()
        if not record or (record["visibility"] != "public" and record["owner"] != viewer):
            raise LookupError("not found")
        if record["storage_state"] == "archived":
            raw = cloud.get(record["object_key"])
            if digest(raw) != record["content_hash"]:
                raise ValueError("archive integrity failure")
            body = json.loads(raw)
        else:
            with self.connection(record["shard"]) as conn:
                body = conn.execute("SELECT body FROM entities WHERE block_id=%s", (identifier,)).fetchone()["body"]
        if digest(canonical(body)) != record["content_hash"]:
            raise ValueError("entity integrity failure")
        return {"index": record, "body": body}

    def archive_one(self, cloud):
        # Durable queue is entity_index.pending. SKIP LOCKED allows multiple workers.
        with self.connection() as conn:
            row = conn.execute("SELECT * FROM entity_index WHERE storage_state='pending' FOR UPDATE SKIP LOCKED LIMIT 1").fetchone()
            if not row:
                return False
            with self.connection(row["shard"]) as data:
                body = data.execute("SELECT body FROM entities WHERE block_id=%s", (row["block_id"],)).fetchone()["body"]
            raw = canonical(body)
            if digest(raw) != row["content_hash"]:
                raise ValueError("hot data integrity failure")
            key = "fairdeck/" + row["block_id"] + "/" + row["content_hash"] + ".json"
            cloud.put(key, raw)
            if digest(cloud.get(key)) != row["content_hash"]:
                raise ValueError("cloud verification failed; hot copy retained")
            conn.execute("UPDATE entity_index SET storage_state='archived',object_key=%s,updatetime=now() WHERE block_id=%s", (key, row["block_id"]))
        # Keep hot replica in v0.1. Eviction is deliberately deferred until recovery tests.
        return True


if __name__ == "__main__":
    Store(config()).initialize()
    print("Initialized blockchain index and two entity shards.")
