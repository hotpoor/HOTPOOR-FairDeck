"""Read a Unity wallet, authenticate and maintain an opt-in endpoint lease."""
import argparse
import base64
import getpass
import hashlib
import hmac
import json
import time
import xml.etree.ElementTree as ET
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import urlsplit

from cryptography.hazmat.primitives import hashes, padding as symmetric_padding
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

from core.protocol import NETWORK, address


def unlock(file, password):
    if Path(file).stat().st_size > 32768:
        raise ValueError("wallet too large")
    w = json.loads(Path(file).read_text("utf-8-sig"))
    if (w["version"], w["algorithm"], w["kdf"], w["iterations"]) != (1, "RSA3072-PKCS1-SHA256", "PBKDF2-HMAC-SHA256", 600000):
        raise ValueError("unsupported wallet")
    keys = hashlib.pbkdf2_hmac("sha256", password.encode(), base64.b64decode(w["salt"]), 600000, 64)
    message = "|".join(str(w[k]) for k in ("version", "algorithm", "kdf", "iterations", "address", "modulus", "exponent", "salt", "iv", "ciphertext"))
    if not hmac.compare_digest(hmac.digest(keys[32:], message.encode(), "sha256"), base64.b64decode(w["mac"])):
        raise ValueError("wrong password or modified wallet")
    decrypt = Cipher(algorithms.AES(keys[:32]), modes.CBC(base64.b64decode(w["iv"]))).decryptor()
    raw = decrypt.update(base64.b64decode(w["ciphertext"])) + decrypt.finalize()
    unpad = symmetric_padding.PKCS7(128).unpadder()
    xml = ET.fromstring(unpad.update(raw) + unpad.finalize())
    parts = {element.tag: int.from_bytes(base64.b64decode(element.text), "big") for element in xml}
    public = rsa.RSAPublicNumbers(parts["Exponent"], parts["Modulus"])
    private = rsa.RSAPrivateNumbers(parts["P"], parts["Q"], parts["D"], parts["DP"], parts["DQ"], parts["InverseQ"], public).private_key()
    if (address(w) != w["address"] or private.key_size != 3072 or
            parts["Modulus"] != int.from_bytes(base64.b64decode(w["modulus"]), "big") or
            parts["Exponent"] != int.from_bytes(base64.b64decode(w["exponent"]), "big")):
        raise ValueError("address mismatch")
    return w, private


def call(base, path, body=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = Request(base.rstrip("/") + path, None if body is None else json.dumps(body).encode(), headers)
    with urlopen(request, timeout=20) as response:
        return json.load(response)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--api", default="https://blockchain.xialiwei.com")
    parser.add_argument("--wallet", required=True)
    parser.add_argument("--ip", required=True, help="Advertised reachable IP")
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--publish", action="store_true", help="Opt in to public endpoint discovery")
    parser.add_argument("--game-id")
    args = parser.parse_args()
    if urlsplit(args.api).scheme != "https":
        raise ValueError("HTTPS required")
    wallet, private = unlock(args.wallet, getpass.getpass("Wallet password (local only): "))
    token, expires = None, 0
    while True:
        if time.time() >= expires:
            challenge = call(args.api, "/v1/wallets/challenge", {k: wallet[k] for k in ("address", "modulus", "exponent")})
            prefix = f"FairDeck login\nnetwork={NETWORK}\naudience={urlsplit(args.api).hostname}\naddress={wallet['address']}\nid={challenge['challenge_id']}\nnonce="
            if challenge["address"] != wallet["address"] or not challenge["message"].startswith(prefix):
                raise ValueError("challenge binding mismatch")
            signature = private.sign(challenge["message"].encode(), padding.PKCS1v15(), hashes.SHA256())
            session = call(args.api, "/v1/wallets/verify", {"challenge_id": challenge["challenge_id"], "signature": base64.b64encode(signature).decode()})
            token, expires = session["token"], time.time() + session["expires_in"] - 30
        call(args.api, "/v1/nodes/heartbeat", {"ip": args.ip, "port": args.port, "public": args.publish}, token)
        print(json.dumps(call(args.api, "/v1/nodes/me", token=token)))
        path = "/v1/games/" + args.game_id + "/peers" if args.game_id else "/v1/nodes"
        print(json.dumps(call(args.api, path, token=token)))
        time.sleep(30)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
    except Exception:
        raise SystemExit("Node stopped: check wallet, API availability, or membership. No automatic identity replacement.")
