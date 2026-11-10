#!/usr/bin/env python3
"""Offline LAB world-event tests; local mock HMAC signer, no game server."""
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, ProxyHandler, build_opener

BASE = "http://127.0.0.1:18878"
OPENER = build_opener(ProxyHandler({}))
ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / "StormPT.Gateway" / "bin" / "Release" / "net8.0" / "StormPT.Gateway.dll"

# Fixed, public synthetic test key. Never use outside this isolated mock.
KEY = bytes([0x42] * 32)
BOOT1 = "11111111-1111-4111-8111-111111111111"
BOOT2 = "22222222-2222-4222-8222-222222222222"


def request(method, path, payload=None, headers=None):
    raw = None if payload is None else json.dumps(payload, separators=(",", ":")).encode()
    all_headers = {"Content-Type": "application/json"} if raw is not None else {}
    all_headers.update(headers or {})
    req = Request(BASE + path, data=raw, headers=all_headers, method=method)
    try:
        with OPENER.open(req, timeout=3) as result:
            return result.status, json.loads(result.read() or b"{}")
    except HTTPError as exc:
        body = exc.read()
        return exc.code, json.loads(body or b"{}") if body else {}


def sign(message, *, nonce=None, timestamp=None, key=KEY):
    timestamp = str(int(time.time()) if timestamp is None else timestamp)
    nonce = secrets.token_hex(16) if nonce is None else nonce
    body = json.dumps(message, separators=(",", ":")).encode()
    mac = hmac.new(key, timestamp.encode() + b"\n" + nonce.encode() +
                   b"\n" + body, hashlib.sha256).hexdigest()
    return {
        "X-StormPT-Lab-Time": timestamp,
        "X-StormPT-Lab-Nonce": nonce,
        "X-StormPT-Lab-Signature": mac,
    }


def send(message, *, headers=None):
    return request("POST", "/__lab/world-events", message,
                   headers if headers is not None else sign(message))[0]


def event(kind, boot, sequence, *, session="test-session-1", account="TEST1",
          character="LabKnight", serial=0x10002000, mobile_server=1,
          mobile_index=101):
    return {
        "eventType": kind, "serverEpoch": boot, "sequence": sequence,
        "serverSession": session, "account": account, "character": character,
        "nativeObjectSerial": serial, "mobileServerId": mobile_server,
        "mobileIndex": mobile_index,
    }


def check(condition, case):
    if not condition:
        raise AssertionError(case)


def start(enabled):
    env = os.environ.copy()
    env.pop("STORMPT_LAB_ENABLE_WORLD_EVENTS", None)
    env.pop("STORMPT_LAB_WORLD_EVENT_KEY_B64", None)
    env.pop("STORMPT_GATEWAY_ENABLE_LAB_AUTH", None)
    env.pop("STORMPT_LAB_PORTAL_ORIGIN", None)
    if enabled:
        env["STORMPT_LAB_ENABLE_WORLD_EVENTS"] = "1"
        env["STORMPT_LAB_WORLD_EVENT_KEY_B64"] = base64.b64encode(KEY).decode()
    process = subprocess.Popen(["dotnet", str(DLL)], cwd=str(ROOT), env=env,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    try:
        for _ in range(150):
            if process.poll() is not None:
                raise RuntimeError(f"Gateway exited early ({process.returncode})")
            try:
                if request("GET", "/health")[0] == 200:
                    return process
            except (URLError, ConnectionError, TimeoutError):
                pass
            time.sleep(.1)
        raise RuntimeError("Gateway startup timeout")
    except Exception:
        stop(process, show_output=True)
        raise


def stop(process, show_output=False):
    process.terminate()
    try:
        data, _ = process.communicate(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        data, _ = process.communicate(timeout=5)
    if show_output:
        print((data or b"").decode(errors="replace")[-2500:], file=sys.stderr)
    time.sleep(.25)


def main():
    check(DLL.exists(), "Release build required")

    gateway = start(False)
    try:
        status, health = request("GET", "/health")
        check(status == 200 and health["trustedWorldEventsConfigured"] is False,
              "event channel defaults to disabled")
        check(send(event("boot", BOOT1, 1)) == 503,
              "unconfigured event endpoint is disabled")
        check(request("GET", "/__lab/world-events")[0] == 405,
              "no public GET to world-event channel")
    finally:
        stop(gateway)

    gateway = start(True)
    try:
        status, health = request("GET", "/health")
        check(status == 200 and health["trustedWorldEventsConfigured"] is True and
              health["realLoginConfigured"] is False,
              "world event channel enabled in mock, player login remains disabled")

        boot = event("boot", BOOT1, 1)
        spawn = event("spawn", BOOT1, 2)
        check(request("POST", "/__lab/world-events", boot)[0] == 401,
              "unsigned event forbidden")
        wrong = sign(boot, key=bytes([0x33] * 32))
        check(send(boot, headers=wrong) == 401,
              "incorrect HMAC key denied")
        old = sign(boot, timestamp=int(time.time()) - 90)
        check(send(boot, headers=old) == 401,
              "expired signature denied")
        future = sign(boot, timestamp=int(time.time()) + 90)
        check(send(boot, headers=future) == 401,
              "future signature denied")
        check(send(spawn) == 409,
              "spawn before server boot forbidden")
        check(send(boot) == 204, "signed boot event accepted")

        same_headers = sign(event("boot", BOOT2, 2))
        check(send(event("boot", BOOT2, 2), headers=same_headers) == 204,
              "new authenticated server boot clears prior epoch")
        check(send(event("boot", BOOT2, 2), headers=same_headers) == 409,
              "replayed nonce and MAC refused")
        check(send(event("boot", BOOT1, 1)) == 409,
              "out-of-order boot event refused")

        # Old epoch cannot spawn into a new instance. Fresh events are accepted.
        check(send(event("spawn", BOOT1, 3)) == 409, "stale spawn epoch refused")
        first = event("spawn", BOOT2, 3)
        check(send(first) == 204, "first signed player spawn accepted")
        check(send(event("spawn", BOOT2, 4, session="test-session-2",
                         serial=0x10002000, mobile_index=102)) == 409,
              "native ObjectSerial conflict refused")
        check(send(event("spawn", BOOT2, 4, session="test-session-2",
                         serial=0x10002001, mobile_index=101)) == 409,
              "mobile Ident conflict refused")
        check(send(event("spawn", BOOT2, 4, session="test-session-2",
                         serial=0x10002001, mobile_index=102)) == 204,
              "second, distinct entity accepted")
        check(send(event("spawn", BOOT2, 5, session="test-session-1",
                         serial=0x10002002, mobile_index=103)) == 409,
              "one session cannot claim two entities")

        check(send(event("despawn", BOOT2, 5, session="test-session-2",
                         serial=0x10002000)) == 409,
              "despawn from another session cannot remove entity")
        check(send(event("despawn", BOOT2, 5)) == 204,
              "server-confirmed exact despawn accepted")
        check(send(event("spawn", BOOT2, 6, session="test-session-3",
                         serial=0x10002000, mobile_index=101)) == 204,
              "freed native and mobile identifiers can be reused")
        check(send(event("spawn", BOOT2, 7, character="Bad\nName",
                         session="test-session-4", serial=0x10002004,
                         mobile_index=104)) == 422,
              "character control characters rejected")

        check(send(event("shutdown", BOOT2, 7)) == 422,
              "unsupported event type refused")
        check(send(event("spawn", BOOT2, 6, session="test-session-4",
                         serial=0x10002004, mobile_index=104)) == 409,
              "reused event sequence denied")
        check(request("GET", "/v1/me")[0] == 401,
              "world event cannot create a browser session")
        check(request("GET", "/v1/characters")[0] == 401,
              "world event cannot grant access to character profile")

        # Unknown/unexpected JSON fields must be rejected after auth verification.
        extra = dict(event("spawn", BOOT2, 7))
        extra["password"] = "synthetic-dummy"
        check(send(extra) == 400, "unknown fields rejected")
    finally:
        stop(gateway)

    print("PASS: signed boot, spawn, despawn; cross-epoch isolation; no player access.")
    print("PASS: HMAC, timestamps, nonces, sequence, strict schemas and ID conflicts.")
    print("PASS: events stayed loopback-only; no real game or SQL connection.")


if __name__ == "__main__":
    main()
