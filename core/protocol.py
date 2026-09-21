import base64
import hashlib
import ipaddress
import json
import re

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa

NETWORK = "fairdeck-devnet-v1"


def block_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{32}", value):
        raise ValueError("block_id must be 32 lowercase UUID hex characters")
    return value


def shard(value):
    return 1 + int(block_id(value), 16) % 2


def canonical(body):
    return json.dumps(body, sort_keys=True, separators=(",", ":"), ensure_ascii=False,
                      allow_nan=False).encode("utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def public_key(public):
    n_bytes = base64.b64decode(public["modulus"], validate=True)
    e_bytes = base64.b64decode(public["exponent"], validate=True)
    if len(n_bytes) != 384 or n_bytes[0] == 0 or e_bytes != b"\x01\x00\x01":
        raise ValueError("RSA-3072 with exponent 65537 required")
    return rsa.RSAPublicNumbers(int.from_bytes(e_bytes, "big"), int.from_bytes(n_bytes, "big")).public_key()


def address(public):
    public_key(public)
    # Explicit cross-language encoding. This is not an Ethereum address.
    value = "FairDeck-RSA3072-v1|" + public["modulus"] + "|" + public["exponent"]
    return "fd1_" + digest(value.encode("ascii"))


def verify(public, message, signature):
    public_key(public).verify(base64.b64decode(signature, validate=True),
                              message.encode("utf-8"), padding.PKCS1v15(), hashes.SHA256())


def endpoint(ip, port, allow_private=False):
    parsed = ipaddress.ip_address(ip)
    if not allow_private and not parsed.is_global:
        raise ValueError("public advertised IP required")
    if isinstance(port, bool) or not isinstance(port, int) or not 1 <= port <= 65535:
        raise ValueError("invalid port")
    return str(parsed), port
