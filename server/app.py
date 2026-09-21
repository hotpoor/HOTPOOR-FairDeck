import hmac
import ipaddress
import json
import os
import secrets
import threading
import uuid
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit

from cryptography.exceptions import InvalidSignature
from psycopg.types.json import Jsonb

from core.protocol import NETWORK, address, block_id, digest, endpoint, verify
from server.db import Store, config
from server.storage import Cloud


class API(BaseHTTPRequestHandler):
    server_version = "FairDeck/0.1"

    def log_message(self, *_):
        pass  # Never log authorization, signatures, wallet bodies or query parameters.

    def reply(self, status, value):
        raw = json.dumps(value, default=str, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        origin = self.headers.get("Origin")
        if origin in self.server.cfg["allowed_origins"]:
            self.send_header("Access-Control-Allow-Origin", origin)
            self.send_header("Vary", "Origin")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_OPTIONS(self):
        if self.headers.get("Origin") not in self.server.cfg["allowed_origins"]:
            return self.reply(403, {"error": "origin denied"})
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", self.headers["Origin"])
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Authorization, Content-Type")
        self.send_header("Vary", "Origin")
        self.end_headers()

    def do_GET(self):
        self.handle_api("GET")

    def do_POST(self):
        self.handle_api("POST")

    def viewer(self, required=True):
        bearer = self.headers.get("Authorization", "")
        token = bearer[7:] if bearer.startswith("Bearer ") else ""
        with self.server.store.connection() as conn:
            row = conn.execute("SELECT address FROM sessions WHERE token_hash=%s AND expires_at>now()", (digest(token.encode()),)).fetchone() if token else None
        if not row and required:
            raise PermissionError("authentication required")
        return row["address"] if row else None

    def admin(self):
        expected = os.environ.get(self.server.cfg["admin_token_env"], "")
        actual = self.headers.get("Authorization", "").removeprefix("Bearer ")
        if not expected or not hmac.compare_digest(actual, expected):
            raise PermissionError("operator authorization required")

    def handle_api(self, method):
        try:
            if self.headers.get("Origin") and self.headers["Origin"] not in self.server.cfg["allowed_origins"]:
                raise PermissionError("origin denied")
            length = int(self.headers.get("Content-Length", "0"))
            if length < 0 or length > 1024 * 1024:
                return self.reply(413, {"error": "body too large"})
            body = json.loads(self.rfile.read(length)) if method == "POST" and length else {}
            status, data = self.route(method, urlsplit(self.path).path, body)
            self.reply(status, data)
        except (PermissionError, InvalidSignature):
            self.reply(403, {"error": "authorization or signature rejected"})
        except LookupError:
            self.reply(404, {"error": "not found"})
        except (ValueError, TypeError, KeyError):
            self.reply(400, {"error": "invalid request or conflicting immutable data"})
        except Exception:
            self.reply(503, {"error": "dependency unavailable; request not confirmed"})

    def route(self, method, path, body):
        store = self.server.store
        now = datetime.now(timezone.utc)
        if method == "GET" and path == "/health":
            return 200, {"service": "FairDeck", "network": NETWORK, "consensus": "not-implemented"}
        if method == "POST" and path == "/v1/wallets/challenge":
            public = {key: body[key] for key in ("modulus", "exponent")}
            addr = address(public)
            if body["address"] != addr:
                raise ValueError("address mismatch")
            identifier = uuid.uuid4().hex
            expiry = now + timedelta(minutes=2)
            nonce = secrets.token_hex(32)
            message = f"FairDeck login\nnetwork={NETWORK}\naudience={self.server.cfg['domain']}\naddress={addr}\nid={identifier}\nnonce={nonce}\nexpires={int(expiry.timestamp())}"
            with store.connection() as conn:
                old = conn.execute("SELECT public_key FROM wallets WHERE address=%s", (addr,)).fetchone()
                if old and old["public_key"] != public:
                    return 409, {"error": "address_conflict", "action": "create_new_wallet"}
                conn.execute("INSERT INTO challenges(id,address,public_key,message,expires_at) VALUES(%s,%s,%s,%s,%s)", (identifier, addr, Jsonb(public), message, expiry))
            return 200, {"challenge_id": identifier, "message": message, "address": addr}
        if method == "POST" and path == "/v1/wallets/verify":
            with store.connection() as conn:
                row = conn.execute("SELECT * FROM challenges WHERE id=%s AND expires_at>now() AND NOT used FOR UPDATE", (block_id(body["challenge_id"]),)).fetchone()
                if not row:
                    raise PermissionError("expired or replayed challenge")
                verify(row["public_key"], row["message"], body["signature"])
                conn.execute("INSERT INTO wallets(address,public_key) VALUES(%s,%s) ON CONFLICT DO NOTHING", (row["address"], Jsonb(row["public_key"])))
                existing = conn.execute("SELECT public_key FROM wallets WHERE address=%s", (row["address"],)).fetchone()
                if existing["public_key"] != row["public_key"]:
                    return 409, {"error": "address_conflict", "action": "create_new_wallet"}
                conn.execute("UPDATE challenges SET used=true WHERE id=%s", (row["id"],))
                token = secrets.token_urlsafe(32)
                conn.execute("INSERT INTO sessions(token_hash,address,expires_at) VALUES(%s,%s,%s)", (digest(token.encode()), row["address"], now + timedelta(minutes=30)))
            return 200, {"address": row["address"], "token": token, "expires_in": 1800}
        if method == "POST" and path == "/v1/nodes/heartbeat":
            viewer = self.viewer()
            ip, port = endpoint(body["ip"], body["port"], self.server.cfg["allow_private_nodes"])
            if not isinstance(body.get("public", False), bool):
                raise ValueError("public must be boolean")
            observed = self.client_address[0]
            if self.server.cfg.get("trust_loopback_proxy") and ipaddress.ip_address(observed).is_loopback:
                observed = str(ipaddress.ip_address(self.headers.get("X-Real-IP", observed)))
            with store.connection() as conn:
                conn.execute("""INSERT INTO nodes(address,ip,port,public,observed_ip) VALUES(%s,%s,%s,%s,%s)
                 ON CONFLICT(address) DO UPDATE SET ip=excluded.ip,port=excluded.port,public=excluded.public,
                 observed_ip=excluded.observed_ip,updatetime=now()""", (viewer, ip, port, body.get("public", False), observed))
            return 200, {"address": viewer, "observed_ip": observed, "lease_seconds": 90}
        if method == "GET" and path == "/v1/nodes/me":
            viewer = self.viewer()
            with store.connection() as conn:
                row = conn.execute("SELECT * FROM nodes WHERE address=%s", (viewer,)).fetchone()
            return 200, {"node": row}
        if method == "GET" and path == "/v1/nodes":
            with store.connection() as conn:
                rows = conn.execute("SELECT address,host(ip) AS ip,port,updatetime FROM nodes WHERE public AND updatetime>now()-interval '90 seconds' ORDER BY address LIMIT 200").fetchall()
            return 200, {"nodes": rows}
        if method == "GET" and path.startswith("/v1/games/") and path.endswith("/peers"):
            game = block_id(path.split("/")[3])
            viewer = self.viewer()
            with store.connection() as conn:
                if not conn.execute("SELECT 1 FROM game_members WHERE game_id=%s AND address=%s", (game, viewer)).fetchone():
                    raise PermissionError("membership required")
                rows = conn.execute("""SELECT n.address,host(n.ip) AS ip,n.port,n.updatetime FROM nodes n
                 JOIN game_members g USING(address) WHERE g.game_id=%s AND n.updatetime>now()-interval '90 seconds'""", (game,)).fetchall()
            return 200, {"nodes": rows}
        if method == "POST" and path == "/v1/operator/entities":
            self.admin()
            return 201, store.create(body["block_id"], body["owner"], body["visibility"], body["body"])
        if method == "POST" and path.endswith("/lock") and path.startswith("/v1/operator/entities/"):
            self.admin()
            identifier = block_id(path.split("/")[4])
            with store.connection() as conn:
                row = conn.execute("UPDATE entity_index SET storage_state='pending',updatetime=now() WHERE block_id=%s AND storage_state='hot' RETURNING block_id", (identifier,)).fetchone()
                if not row and not conn.execute("SELECT 1 FROM entity_index WHERE block_id=%s", (identifier,)).fetchone():
                    raise LookupError()
            return 202, {"block_id": identifier, "archive": "queued-or-already-archived"}
        if method == "GET" and path.startswith("/v1/entities/"):
            return 200, store.read(path.split("/")[3], self.viewer(required=False), self.server.cloud)
        raise LookupError()


def make_server(cfg, port):
    server = ThreadingHTTPServer((cfg["bind"], port), API)
    server.cfg, server.store, server.cloud = cfg, Store(cfg), Cloud(cfg["storage"])
    return server


if __name__ == "__main__":
    cfg = config()
    servers = [make_server(cfg, port) for port in cfg["ports"]]
    for server in servers:
        threading.Thread(target=server.serve_forever, daemon=True).start()
    print("FairDeck development API ports:", cfg["ports"], flush=True)
    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        for server in servers:
            server.shutdown()
