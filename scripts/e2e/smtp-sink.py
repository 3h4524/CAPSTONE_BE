#!/usr/bin/env python3
"""Minimal SMTP sink that records every accepted message as one JSON line.

Playwright E2E needs the verification token that only ever leaves the application by email, so
mail is captured on loopback instead of being relayed anywhere. One JSON object per line keeps the
file append-only and safe for several processes to read while the sink is still writing.
"""

from __future__ import annotations

import argparse
import base64
import binascii
import email
import email.policy
import json
import os
import quopri
import signal
import socket
import socketserver
import sys
import threading
from datetime import datetime, timezone

CRLF = b"\r\n"
DATA_TERMINATOR = b"."


def decode_part(part: email.message.Message) -> str:
    """Return the decoded text of one MIME part, or an empty string when it is not text."""
    payload = part.get_payload(decode=True)
    if payload is None:
        raw = part.get_payload()
        return raw if isinstance(raw, str) else ""

    charset = part.get_content_charset() or "utf-8"
    try:
        return payload.decode(charset, errors="replace")
    except LookupError:
        return payload.decode("utf-8", errors="replace")


def decode_body(message: email.message.Message) -> str:
    """Flatten the message to plain text, preferring text/plain and walking alternative parts.

    MailKit writes a single text/plain part, but a real relay would offer alternatives, so every
    non-multipart leaf that decodes as text is collected in document order.
    """
    if message.is_multipart():
        chunks: list[str] = []
        for part in message.walk():
            if part.is_multipart():
                continue
            if (part.get_content_maintype() or "").lower() != "text":
                continue
            text = decode_part(part).strip()
            if text:
                chunks.append(text)
        return "\n".join(chunks)

    return decode_part(message)


def decode_header(message: email.message.Message, name: str) -> str:
    """Decode one header, collapsing RFC 2047 encoded words."""
    values = message.get_all(name)
    if not values:
        return ""

    decoded = email.header.decode_header(values[0])
    parts: list[str] = []
    for text, charset in decoded:
        if isinstance(text, bytes):
            parts.append(text.decode(charset or "utf-8", errors="replace"))
        else:
            parts.append(text)

    return "".join(parts).strip()


def manual_decode(raw: bytes) -> email.message.Message:
    """Parse a message, falling back to quoted-printable/base64 recovery when parsing loses text.

    ``email.policy.default`` is strict enough to raise on some real-world payloads, and a sink that
    drops the one mail a test depends on is worse than one that recovers it loosely.
    """
    try:
        return email.message_from_bytes(raw, policy=email.policy.default)
    except Exception:  # noqa: BLE001 - any parse failure falls back to the compat32 parser
        pass

    try:
        return email.message_from_bytes(raw)
    except Exception:  # noqa: BLE001 - a totally unparsable body still gets recorded
        return email.message_from_string("")


def relaxed_quopri_decode(raw: bytes) -> bytes:
    """Decode quoted-printable while tolerating input that is not valid QP at all."""
    try:
        return quopri.decodestring(raw)
    except Exception:  # noqa: BLE001 - a malformed escape must not lose the whole body
        return raw


def relaxed_base64_decode(raw: bytes) -> bytes:
    """Decode base64 after repairing the padding real senders get wrong."""
    cleaned = b"".join(raw.split())
    padding = len(cleaned) % 4
    if padding:
        cleaned += b"=" * (4 - padding)

    try:
        return base64.b64decode(cleaned, validate=False)
    except (binascii.Error, ValueError):
        return raw


def repair_body(message: email.message.Message, raw: bytes) -> str:
    """Recover plain text from an encoded payload the standard parser could not surface.

    ``Content-Transfer-Encoding`` is honoured explicitly because a mislabelled body decodes to
    garbage otherwise, which is exactly the case a test would silently assert against.
    """
    text = decode_body(message)
    if text.strip():
        return text

    payload = message.get_payload()
    if not isinstance(payload, str):
        return text

    encoding = (message.get("Content-Transfer-Encoding") or "").strip().lower()
    raw_payload = payload.encode("utf-8", errors="replace")

    if encoding == "quoted-printable":
        recovered = relaxed_quopri_decode(raw_payload).decode("utf-8", errors="replace")
    elif encoding == "base64":
        recovered = relaxed_base64_decode(raw_payload).decode("utf-8", errors="replace")
    else:
        recovered = payload

    # A hard-parse fallback can leave the headers glued to the body; keep only the part after them.
    if raw[:64].strip().lower().startswith(b"content-type") or b"\n\n" in raw_payload:
        recovered = raw_payload.split(b"\r\n\r\n", 1)[-1].split(b"\n\n", 1)[-1].decode(
            "utf-8", errors="replace"
        )

    return recovered.strip() or text


class MailStore:
    """Append-only JSON-lines store shared by every connection thread."""

    def __init__(self, path: str) -> None:
        self._path = path
        self._lock = threading.Lock()

    @property
    def path(self) -> str:
        return self._path

    def reset(self) -> None:
        with self._lock:
            directory = os.path.dirname(os.path.abspath(self._path))
            os.makedirs(directory, exist_ok=True)
            with open(self._path, "w", encoding="utf-8"):
                pass

    def append(self, record: dict) -> None:
        line = json.dumps(record, ensure_ascii=False)
        with self._lock:
            directory = os.path.dirname(os.path.abspath(self._path))
            os.makedirs(directory, exist_ok=True)
            # O_APPEND keeps concurrent writers from interleaving a partial line.
            handle = os.open(self._path, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o600)
            try:
                os.write(handle, (line + "\n").encode("utf-8"))
            finally:
                os.close(handle)


class SmtpSession(socketserver.StreamRequestHandler):
    """Speaks just enough SMTP for MailKit, then records the message it delivered."""

    timeout = 60

    def handle(self) -> None:
        store: MailStore = self.server.store  # type: ignore[attr-defined]
        peer = "%s:%d" % self.client_address[:2]

        mail_from = ""
        rcpt_to: list[str] = []

        try:
            self._send(220, "apcs-e2e-sink ready")

            while True:
                line = self.rfile.readline()
                if not line:
                    return

                command = line.decode("utf-8", errors="replace").strip()
                if not command:
                    continue

                verb, _, argument = command.partition(" ")
                verb = verb.upper().strip()
                argument = argument.strip()

                if verb in {"EHLO", "HELO"}:
                    if verb == "EHLO":
                        self._send_multiline(
                            250,
                            [
                                "apcs-e2e-sink greets " + (argument or peer),
                                "SIZE 35882577",
                                "8BITMIME",
                                "AUTH PLAIN LOGIN",
                                "STARTTLS",
                                "ENHANCEDSTATUSCODES",
                            ],
                        )
                    else:
                        self._send(250, "apcs-e2e-sink")
                    continue

                if verb == "AUTH":
                    # Announced but deliberately not honoured: an anonymous sink must never
                    # validate credentials, and MailKit only offers AUTH when it is advertised.
                    self._send(235, "2.7.0 Authentication successful")
                    continue

                if verb == "STARTTLS":
                    # The sink speaks plaintext only, so the upgrade is refused rather than faked.
                    self._send(454, "4.7.0 TLS not available on the E2E sink")
                    continue

                if verb == "MAIL" and argument.upper().startswith("FROM:"):
                    # ESMTP parameters (SIZE=, BODY=) are not part of the address.
                    mail_from = self._strip_argument(argument[len("FROM:") :])
                    rcpt_to = []
                    self._send(250, "2.1.0 Sender ok")
                    continue

                if verb == "RCPT" and argument.upper().startswith("TO:"):
                    rcpt_to.append(self._strip_argument(argument[len("TO:") :]))
                    self._send(250, "2.1.5 Recipient ok")
                    continue

                if verb == "DATA":
                    self._send(354, "End data with <CR><LF>.<CR><LF>")
                    raw = self._read_data()
                    self._send(250, "2.0.0 Message accepted")
                    self._record(store, raw, mail_from, rcpt_to, peer)
                    mail_from = ""
                    rcpt_to = []
                    continue

                if verb == "RSET":
                    mail_from = ""
                    rcpt_to = []
                    self._send(250, "2.0.0 Reset")
                    continue

                if verb == "NOOP":
                    self._send(250, "2.0.0 Ok")
                    continue

                if verb == "QUIT":
                    self._send(221, "2.0.0 Bye")
                    return

                self._send(500, "5.5.2 Unrecognised command")
        except (socket.timeout, ConnectionResetError, BrokenPipeError, OSError):
            # A client that disappears mid-conversation is normal during a failing test; the
            # session is dropped without taking the sink down with it.
            return

    def _read_data(self) -> bytes:
        chunks: list[bytes] = []
        while True:
            line = self.rfile.readline()
            if not line:
                raise ConnectionResetError("client closed while sending DATA")

            if line in {b".\r\n", b".\n"}:
                break

            if line.startswith(b".."):
                line = line[1:]

            chunks.append(line)

        raw = b"".join(chunks)
        if raw.endswith(CRLF):
            raw = raw[: -len(CRLF)]
        return raw

    def _record(
        self,
        store: MailStore,
        raw: bytes,
        mail_from: str,
        rcpt_to: list[str],
        peer: str,
    ) -> None:
        message = manual_decode(raw)
        headers = {
            "from": decode_header(message, "From"),
            "replyTo": decode_header(message, "Reply-To"),
            "date": decode_header(message, "Date"),
            "messageId": decode_header(message, "Message-Id"),
            "contentType": (message.get_content_type() or "").lower(),
        }

        store.append(
            {
                "receivedAtUtc": datetime.now(timezone.utc).isoformat(),
                "peer": peer,
                "envelope": {
                    "mailFrom": mail_from,
                    "rcptTo": list(rcpt_to),
                },
                "from": headers["from"],
                "replyTo": headers["replyTo"],
                "date": headers["date"],
                "messageId": headers["messageId"],
                "contentType": headers["contentType"],
                "subject": decode_header(message, "Subject"),
                "to": self._addresses(message) or [self._strip_address(rcpt) for rcpt in rcpt_to],
                "body": repair_body(message, raw),
                "raw": raw.decode("utf-8", errors="replace"),
            }
        )

    def _addresses(self, message: email.message.Message) -> list[str]:
        values = message.get_all("To") or []
        addresses: list[str] = []
        for value in values:
            for part in str(value).split(","):
                cleaned = self._strip_address(part)
                if cleaned:
                    addresses.append(cleaned)
        return addresses

    @staticmethod
    def _strip_argument(value: str) -> str:
        return SmtpSession._strip_address(value.split(" ", 1)[0])

    @staticmethod
    def _strip_address(value: str) -> str:
        candidate = value.strip()
        if "<" in candidate and ">" in candidate:
            candidate = candidate[candidate.index("<") + 1 : candidate.index(">")]
        candidate = candidate.strip().strip("<>").strip()
        if candidate.lower().startswith("mailbox:"):
            candidate = candidate[len("mailbox:") :].strip()
        return candidate

    def _send(self, code: int, text: str) -> None:
        self.wfile.write(("%d %s%s" % (code, text, CRLF.decode())).encode("utf-8"))
        self.wfile.flush()

    def _send_multiline(self, code: int, lines: list[str]) -> None:
        for index, text in enumerate(lines):
            separator = "-" if index < len(lines) - 1 else " "
            self.wfile.write(("%d%s%s%s" % (code, separator, text, CRLF.decode())).encode("utf-8"))
        self.wfile.flush()


class SmtpServer(socketserver.ThreadingTCPServer):
    """Threaded server so one wedged client cannot stall the next test's mail."""

    allow_reuse_address = True
    daemon_threads = True


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Loopback SMTP sink for E2E runs.")
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.environ.get("E2E_SMTP_PORT", "2525")),
        help="Port to listen on (default: 2525, or E2E_SMTP_PORT).",
    )
    parser.add_argument(
        "--host",
        default=os.environ.get("E2E_SMTP_HOST", "127.0.0.1"),
        help="Address to bind (default: 127.0.0.1).",
    )
    parser.add_argument(
        "--out",
        default=os.environ.get(
            "E2E_SMTP_FILE", os.path.join(os.path.dirname(os.path.abspath(__file__)), "mail.jsonl")
        ),
        help="JSON-lines output file.",
    )
    parser.add_argument(
        "--reset",
        action="store_true",
        help="Truncate the output file before binding.",
    )
    parser.add_argument(
        "--pid-file",
        default=os.environ.get("E2E_SMTP_PID_FILE"),
        help="Optional file to write the process id to.",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)

    store = MailStore(args.out)
    if args.reset:
        store.reset()
    else:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        if not os.path.exists(args.out):
            store.reset()

    server = SmtpServer((args.host, args.port), SmtpSession)
    server.store = store  # type: ignore[attr-defined]

    stopping = threading.Event()

    def shutdown(signum, frame) -> None:  # noqa: ARG001 - signal handler signature is fixed
        stopping.set()
        threading.Thread(target=server.shutdown, daemon=True).start()

    signal.signal(signal.SIGTERM, shutdown)
    signal.signal(signal.SIGINT, shutdown)

    if args.pid_file:
        with open(args.pid_file, "w", encoding="utf-8") as handle:
            handle.write(str(os.getpid()))

    print(
        "smtp-sink listening on %s:%d -> %s" % (args.host, args.port, args.out),
        flush=True,
    )

    try:
        server.serve_forever(poll_interval=0.2)
    finally:
        server.server_close()
        if args.pid_file and os.path.exists(args.pid_file):
            try:
                os.remove(args.pid_file)
            except OSError:
                pass

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))