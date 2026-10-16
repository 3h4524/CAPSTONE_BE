#!/usr/bin/env python3
"""Produces an ASP.NET Core Identity v3 password hash without a .NET project.

The E2E SQL seeding mode has to write a `users.password_hash` row directly, because
`Microsoft.AspNetCore.Identity.PasswordHasher<User>` is the only thing allowed to produce that
value in production. Reimplementing its format is what makes the SQL fallback possible at all:
PBKDF2-HMAC-SHA512, 100000 iterations, a 16-byte salt, and a 32-byte subkey packed behind
big-endian integers.

Read the format back out of a stored hash, or mint a new one:

    identity_hash.py 'Passw0rdTest1!'      # prints a hash
    identity_hash.py --verify HASH PW     # exits non-zero when the password does not match
"""

from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
import os
import struct
import sys

FORMAT_MARKER = 0x01
DEFAULT_ITERATIONS = 100000
DEFAULT_SALT_SIZE = 16
DEFAULT_SUBKEY_SIZE = 32

# Microsoft.AspNetCore.Identity.KeyDerivationPrf
ALGORITHMS = {0: "sha1", 1: "sha256", 2: "sha512"}


def split_hash(stored: str) -> tuple[int, int, bytes, bytes]:
    try:
        raw = base64.b64decode(stored, validate=True)
    except (binascii.Error, ValueError) as error:
        raise ValueError("the stored value is not valid base64") from error

    if len(raw) < 13 or raw[0] != FORMAT_MARKER:
        raise ValueError("the stored value is not an Identity v3 hash")

    prf = struct.unpack_from(">I", raw, 1)[0]
    if prf not in ALGORITHMS:
        raise ValueError(f"unsupported key derivation function id {prf}")

    iterations = struct.unpack_from(">I", raw, 5)[0]
    salt_size = struct.unpack_from(">I", raw, 9)[0]
    salt = raw[13 : 13 + salt_size]
    subkey = raw[13 + salt_size :]

    if len(salt) != salt_size or not subkey:
        raise ValueError("the stored hash is truncated")

    return prf, iterations, salt, subkey


def hash_password(
    password: str,
    salt: bytes | None = None,
    iterations: int = DEFAULT_ITERATIONS,
    prf: int = 1,
    subkey_size: int = DEFAULT_SUBKEY_SIZE,
) -> str:
    salt = salt if salt is not None else os.urandom(DEFAULT_SALT_SIZE)
    subkey = hashlib.pbkdf2_hmac(ALGORITHMS[prf], password.encode("utf-8"), salt, iterations, dklen=subkey_size)

    return base64.b64encode(
        b"".join(
            [
                bytes([FORMAT_MARKER]),
                struct.pack(">I", prf),
                struct.pack(">I", iterations),
                struct.pack(">I", len(salt)),
                salt,
                subkey,
            ]
        )
    ).decode("ascii")


def verify(stored: str, password: str) -> bool:
    prf, iterations, salt, expected = split_hash(stored)
    actual = hashlib.pbkdf2_hmac(ALGORITHMS[prf], password.encode("utf-8"), salt, iterations, dklen=len(expected))
    return actual == expected


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Mint or check an Identity v3 password hash.")
    parser.add_argument("password", nargs="?", help="plain password to hash")
    parser.add_argument("--verify", metavar="HASH", help="verify this stored hash instead of minting one")
    parser.add_argument("--iterations", type=int, default=DEFAULT_ITERATIONS)
    parser.add_argument("--salt", help="hex salt, for deterministic output in a test")
    args = parser.parse_args(argv)

    try:
        if args.verify:
            if args.password is None:
                parser.error("--verify needs the password as the positional argument")
            sys.stdout.write("match\n" if verify(args.verify, args.password) else "mismatch\n")
            return 0 if verify(args.verify, args.password) else 1

        if args.password is None:
            parser.error("a password is required")

        salt = bytes.fromhex(args.salt) if args.salt else None
        sys.stdout.write(hash_password(args.password, salt=salt, iterations=args.iterations) + "\n")
        return 0
    except ValueError as error:
        sys.stderr.write(f"identity_hash: {error}\n")
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))