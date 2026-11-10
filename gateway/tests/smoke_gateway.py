#!/usr/bin/env python3
"""Isolated integration test for gateway. Mock accounts only, localhost only."""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, ProxyHandler, build_opener
from urllib.error import HTTPError, URLError

PORT = 18878
MOCK_PORT = 18879
BASE = f"http://127.0.0.1:{PORT}"
OPENER = build_opener(ProxyHandler({}))  # Never route localhost tests through CI proxies.
PROJECT_ROOT = Path(__file__).resolve().parents[1]
DLL = PROJECT_ROOT / "StormPT.Gateway" / "bin" / "Release" / "net8.0" / "StormPT.Gateway.dll"


class MockPortal(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass  # Never print even test credentials.

    def do_POST(self):
        size = int(self.headers.get("Content-Length", "0"))
        if not (0 < size <= 1024):
            return self.answer(413, {"error": "size"})
        try:
            data = json.loads(self.rfile.read(size))
        except ValueError:
            return self.answer(400, {"error": "json"})

        if self.path == "/api/login":
            if data.get("username") == "TEST1" and data.get("password") == "dummyPassword!":
                return self.answer(200, {"authenticated": True, "username": "TEST1"})
            # A malicious/inconsistent backend response must not grant access to ALIAS.
            if data.get("username") == "ALIAS":
                return self.answer(200, {"authenticated": True, "username": "OTHER"})
            return self.answer(401, {"error": "credentials"})
        if self.path == "/api/profile":
            if data.get("username") == "TEST1":
                return self.answer(200, {
                    "username": "TEST1",
                    "characterCount": 1,
                    "characters": [{"name": "LabKnight", "classCode": 1,
                                    "className": "Fighter", "level": 42,
                                    "internalValue": "NOT_FOR_CLIENT"}],
                    "privateData": "NOT_FOR_CLIENT"
                })
            return self.answer(404, {"error": "not found"})
        return self.answer(404, {})

    def answer(self, status, payload):
        data = json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def request(method, path, payload=None, bearer=None):
    body = None if payload is None else json.dumps(payload).encode()
    headers = {"Content-Type": "application/json"} if payload is not None else {}
    if bearer is not None:
        headers["Authorization"] = f"Bearer {bearer}"
    req = Request(BASE + path, data=body, headers=headers, method=method)
    try:
        with OPENER.open(req, timeout=3) as response:
            return response.status, json.loads(response.read() or b"{}"), dict(response.headers)
    except HTTPError as e:
        content = e.read()
        return e.code, json.loads(content or b"{}") if content else {}, dict(e.headers)


def wait_for_server(proc):
    for _ in range(200):
        if proc.poll() is not None:
            raise RuntimeError(f"Gateway exited before becoming ready: {proc.returncode}")
        try:
            status, _, _ = request("GET", "/health")
            if status == 200:
                return
        except (URLError, TimeoutError, ConnectionResetError):
            pass
        time.sleep(0.15)
    raise RuntimeError("Gateway was not ready after 30 seconds; check bound port and process output")


def start_gateway(enabled):
    env = os.environ.copy()
    env.pop("STORMPT_GATEWAY_ENABLE_LAB_AUTH", None)
    env.pop("STORMPT_LAB_PORTAL_ORIGIN", None)
    if enabled:
        env["STORMPT_GATEWAY_ENABLE_LAB_AUTH"] = "1"
        env["STORMPT_LAB_PORTAL_ORIGIN"] = f"http://127.0.0.1:{MOCK_PORT}"
    proc = subprocess.Popen(
        ["dotnet", str(DLL)],
        cwd=str(PROJECT_ROOT),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    try:
        wait_for_server(proc)
    except Exception:
        proc.terminate()
        try:
            output, _ = proc.communicate(timeout=4)
        except subprocess.TimeoutExpired:
            proc.kill()
            output, _ = proc.communicate(timeout=4)
        # Only server startup diagnostics; no real user credentials are in this test.
        print((output or b"").decode(errors="replace")[-4000:], file=sys.stderr)
        raise
    return proc


def stop_gateway(proc):
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.wait(timeout=5)
    time.sleep(0.4)


def check(value, message):
    if not value:
        raise AssertionError(message)


def main():
    check(DLL.exists(), f"Compile first: {DLL}")
    mock = ThreadingHTTPServer(("127.0.0.1", MOCK_PORT), MockPortal)
    thread = threading.Thread(target=mock.serve_forever, daemon=True)
    thread.start()
    try:
        gateway = start_gateway(enabled=False)
        try:
            status, health, _ = request("GET", "/health")
            check(status == 200 and health["realLoginConfigured"] is False, "disabled by default")
            check(request("GET", "/v1/me")[0] == 401, "anonymous /v1/me denied")
            check(request("GET", "/v1/characters")[0] == 401, "anonymous characters denied")
            status, _, _ = request("POST", "/v1/sessions", {"username": "TEST1", "password": "dummyPassword!"})
            check(status == 503, "LAB auth must be disabled by default")
        finally:
            stop_gateway(gateway)

        gateway = start_gateway(enabled=True)
        try:
            check(request("POST", "/v1/sessions", {"username": "TEST1", "password": "wrong"})[0] == 401,
                  "wrong password denied")
            check(request("POST", "/v1/sessions", {"username": "ALIAS", "password": "dummyPassword!"})[0] == 401,
                  "mismatched identity response denied")
            status, result, headers = request("POST", "/v1/sessions",
                                              {"username": "TEST1", "password": "dummyPassword!"})
            check(status == 200 and result.get("token_type") == "Bearer", "valid mock authentication")
            check(headers.get("Cache-Control") == "no-store", "no-store response header")
            token = result["access_token"]
            check(len(token) == 43, "cryptographically-sized token")
            check(request("GET", "/v1/me", bearer="fake")[0] == 401, "fake bearer denied")
            status, me, _ = request("GET", "/v1/me", bearer=token)
            check(status == 200 and me["username"] == "TEST1", "session-bound identity")
            status, characters, _ = request("GET", "/v1/characters?username=OTHER", bearer=token)
            check(status == 200 and characters["username"] == "TEST1", "account cannot be switched")
            check(characters["characterCount"] == 1 and characters["characters"][0]["level"] == 42,
                  "read-only mock characters")
            check("privateData" not in characters and "internalValue" not in characters["characters"][0],
                  "upstream private fields filtered")
            check(request("GET", "/v1/characters")[0] == 401, "characters still private")
            check(request("POST", "/v1/logout", bearer=token)[0] == 204, "session revoked")
            check(request("GET", "/v1/me", bearer=token)[0] == 401, "revoked token rejected")
        finally:
            stop_gateway(gateway)
    finally:
        mock.shutdown()
        mock.server_close()
    print("PASS: gateway disabled-by-default; unauthorized access denied")
    print("PASS: mock-only credentials; session issuance; profile isolation and field filtering")
    print("PASS: bearer revocation; upstream username mismatch blocked; localhost only")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"FAIL: {type(exc).__name__}: {exc}", file=sys.stderr)
        raise
