import os
import base64
from pathlib import Path
from urllib.parse import quote
from urllib.request import urlopen


class Cloud:
    def __init__(self, cfg):
        self.cfg = cfg

    def local_path(self, key):
        root = Path(self.cfg["directory"]).resolve()
        target = (root / key).resolve()
        if root not in target.parents:
            raise ValueError("invalid storage path")
        return target

    def auth(self):
        from qiniu import Auth
        return Auth(os.environ[self.cfg["access_key_env"]], os.environ[self.cfg["secret_key_env"]])

    def archive_key(self):
        key = base64.b64decode(os.environ[self.cfg["archive_key_env"]], validate=True)
        if len(key) != 32:
            raise ValueError("archive key must be 32 bytes")
        return key

    def seal(self, key, raw):
        from cryptography.hazmat.primitives.ciphers.aead import AESGCM
        nonce = os.urandom(12)
        return b"FDAR1" + nonce + AESGCM(self.archive_key()).encrypt(nonce, raw, key.encode())

    def unseal(self, key, raw):
        from cryptography.hazmat.primitives.ciphers.aead import AESGCM
        if len(raw) < 33 or raw[:5] != b"FDAR1":
            raise ValueError("invalid encrypted archive")
        return AESGCM(self.archive_key()).decrypt(raw[5:17], raw[17:], key.encode())

    def put(self, key, raw):
        if self.cfg["driver"] == "local":
            target = self.local_path(key)
            target.parent.mkdir(parents=True, exist_ok=True)
            temp = target.with_suffix(".tmp")
            temp.write_bytes(raw)
            temp.replace(target)
            return
        from qiniu import put_data
        auth = self.auth()
        result, info = put_data(auth.upload_token(self.cfg["bucket"], key, 300), key, self.seal(key, raw))
        if not result or info.status_code != 200:
            raise RuntimeError("Qiniu upload failed")

    def get(self, key):
        if self.cfg["driver"] == "local":
            return self.local_path(key).read_bytes()
        base = self.cfg["download_base"].rstrip("/")
        if not base.startswith("https://"):
            raise ValueError("HTTPS storage URL required")
        url = self.auth().private_download_url(base + "/" + quote(key, safe="/"), expires=60)
        with urlopen(url, timeout=20) as response:
            return self.unseal(key, response.read(2 * 1024 * 1024))
