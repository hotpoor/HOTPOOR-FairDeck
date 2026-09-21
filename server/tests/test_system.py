import base64
import concurrent.futures
import json
import os
import secrets
import tempfile
import threading
import unittest
import uuid
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa

from core.protocol import address, block_id, shard, verify
from server.app import make_server
from server.db import Store
from server.storage import Cloud


def identity():
    key = rsa.generate_private_key(65537, 3072)
    p = key.public_key().public_numbers()
    data = {"modulus": base64.b64encode(p.n.to_bytes(384, "big")).decode(), "exponent": "AQAB"}
    data["address"] = address(data)
    return key, data


class ProtocolTests(unittest.TestCase):
    def test_archive_envelope_authentication(self):
        os.environ["FAIRDECK_TEST_ARCHIVE_KEY"] = base64.b64encode(secrets.token_bytes(32)).decode()
        cloud = Cloud({"archive_key_env": "FAIRDECK_TEST_ARCHIVE_KEY"})
        encrypted = cloud.seal("object-a", b'private content')
        self.assertNotIn(b'private content', encrypted)
        self.assertEqual(cloud.unseal("object-a", encrypted), b'private content')
        with self.assertRaises(Exception): cloud.unseal("object-b", encrypted)
        with self.assertRaises(Exception): cloud.unseal("object-a", encrypted[:-1] + bytes([encrypted[-1] ^ 1]))

    def test_shards_and_validation(self):
        self.assertEqual(shard("0" * 32), 1)
        self.assertEqual(shard("f" * 32), 2)
        self.assertEqual(shard("1" + "0" * 31), 1)
        for bad in ("../secret", "A" * 32, "0" * 31, "g" * 32):
            with self.assertRaises(ValueError):
                block_id(bad)

    def test_unity_signature_fixture(self):
        fixture = Path("apps/unity/HOTPOOR_Poker/Logs/wallet-proof.json")
        if not fixture.exists():
            self.skipTest("Run SetupDesk.ValidateWallet in Unity first")
        proof = json.loads(fixture.read_text("utf-8-sig"))
        self.assertEqual(address(proof), proof["address"])
        verify(proof, proof["message"], proof["signature"])


@unittest.skipUnless(os.environ.get("FAIRDECK_TEST_DSN"), "Set isolated test PostgreSQL DSNs")
class IntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        os.environ["FAIRDECK_TEST_ADMIN"] = secrets.token_urlsafe(32)
        cls.cfg = {"bind": "127.0.0.1", "domain": "blockchain.xialiwei.com", "index_dsn_env": "FAIRDECK_TEST_DSN",
                   "shard_dsn_env": {"1": "FAIRDECK_TEST_SHARD1", "2": "FAIRDECK_TEST_SHARD2"},
                   "admin_token_env": "FAIRDECK_TEST_ADMIN", "allowed_origins": ["https://hotpoor.github.io"],
                   "allow_private_nodes": True, "storage": {"driver": "local", "directory": cls.tmp.name}}
        cls.store = Store(cls.cfg)
        cls.store.initialize()
        cls.servers = [make_server(cls.cfg, 0), make_server(cls.cfg, 0)]
        for server in cls.servers:
            threading.Thread(target=server.serve_forever, daemon=True).start()
        cls.bases = ["http://127.0.0.1:" + str(s.server_port) for s in cls.servers]

    @classmethod
    def tearDownClass(cls):
        for s in cls.servers:
            s.shutdown(); s.server_close()
        cls.tmp.cleanup()

    def call(self, path, data=None, token=None, port=0):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        req = Request(self.bases[port] + path, json.dumps(data).encode() if data is not None else None, headers)
        try:
            with urlopen(req, timeout=10) as res:
                return res.status, json.load(res)
        except HTTPError as error:
            return error.code, json.load(error)

    def login(self):
        private, public = identity()
        status, challenge = self.call("/v1/wallets/challenge", public)
        self.assertEqual(status, 200)
        sig = private.sign(challenge["message"].encode(), padding.PKCS1v15(), hashes.SHA256())
        proof = {"challenge_id": challenge["challenge_id"], "signature": base64.b64encode(sig).decode()}
        status, session = self.call("/v1/wallets/verify", proof, port=1)
        self.assertEqual(status, 200)
        return public, session["token"], proof

    def test_cross_port_login_replay_and_same_wallet(self):
        public, token, proof = self.login()
        self.assertEqual(self.call("/v1/wallets/verify", proof)[0], 403)
        self.assertEqual(self.call("/v1/wallets/challenge", public)[0], 200)
        self.assertEqual(self.call("/v1/nodes/me", token=token)[0], 200)

    def test_invalid_signature_does_not_consume_challenge(self):
        private, public = identity()
        _, challenge = self.call("/v1/wallets/challenge", public)
        wrong = base64.b64encode(bytes(384)).decode()
        self.assertEqual(self.call("/v1/wallets/verify", {"challenge_id": challenge["challenge_id"], "signature": wrong})[0], 403)
        valid = base64.b64encode(private.sign(challenge["message"].encode(), padding.PKCS1v15(), hashes.SHA256())).decode()
        self.assertEqual(self.call("/v1/wallets/verify", {"challenge_id": challenge["challenge_id"], "signature": valid})[0], 200)

    def test_concurrent_replay_one_winner(self):
        private, public = identity()
        _, challenge = self.call("/v1/wallets/challenge", public)
        proof = {"challenge_id": challenge["challenge_id"], "signature": base64.b64encode(private.sign(challenge["message"].encode(), padding.PKCS1v15(), hashes.SHA256())).decode()}
        with concurrent.futures.ThreadPoolExecutor() as pool:
            codes = list(pool.map(lambda i: self.call("/v1/wallets/verify", proof, port=i)[0], (0, 1)))
        self.assertEqual(sorted(codes), [200, 403])

    def test_nodes_membership_and_private_address(self):
        owner, token, _ = self.login()
        guest, guest_token, _ = self.login()
        game = uuid.uuid4().hex
        self.assertEqual(self.call("/v1/nodes/heartbeat", {"ip": "127.0.0.1", "port": 7001}, token)[0], 200)
        self.assertEqual(self.call("/v1/games/" + game + "/peers", token=guest_token)[0], 403)
        with self.store.connection() as conn:
            conn.execute("INSERT INTO game_members(game_id,address) VALUES(%s,%s)", (game, owner["address"]))
        code, peers = self.call("/v1/games/" + game + "/peers", token=token)
        self.assertEqual(code, 200)
        self.assertEqual(peers["nodes"][0]["port"], 7001)
        self.assertNotIn(owner["address"], [n["address"] for n in self.call("/v1/nodes")[1]["nodes"]])

    def test_storage_archive_acl_and_integrity(self):
        owner, token, _ = self.login()
        identifier = uuid.uuid4().hex
        admin = os.environ["FAIRDECK_TEST_ADMIN"]
        payload = {"block_id": identifier, "owner": owner["address"], "visibility": "private", "body": {"ciphertext": "opaque-test-data"}}
        self.assertEqual(self.call("/v1/operator/entities", payload, token)[0], 403)
        self.assertEqual(self.call("/v1/operator/entities", payload, admin)[0], 201)
        self.assertEqual(self.call("/v1/entities/" + identifier)[0], 404)
        self.assertEqual(self.call("/v1/entities/" + identifier, token=token)[1]["body"], payload["body"])
        self.assertEqual(self.call("/v1/operator/entities/" + identifier + "/lock", {}, admin)[0], 202)
        cloud = Cloud(self.cfg["storage"])
        class FailingCloud:
            def put(self, *_): raise IOError("offline")
        with self.assertRaises(IOError): self.store.archive_one(FailingCloud())
        self.assertEqual(self.call("/v1/entities/" + identifier, token=token)[1]["body"], payload["body"])
        while self.store.archive_one(cloud): pass
        archived = self.call("/v1/entities/" + identifier, token=token)[1]
        self.assertEqual(archived["index"]["storage_state"], "archived")
        self.assertEqual(archived["body"], payload["body"])
        cloud.put(archived["index"]["object_key"], b'{}')
        self.assertEqual(self.call("/v1/entities/" + identifier, token=token)[0], 400)

    def test_immutable_write_conflict(self):
        owner, token, _ = self.login()
        identifier = uuid.uuid4().hex
        self.store.create(identifier, owner["address"], "public", {"n": 1})
        with self.assertRaises(ValueError): self.store.create(identifier, owner["address"], "public", {"n": 2})


if __name__ == "__main__":
    unittest.main()
