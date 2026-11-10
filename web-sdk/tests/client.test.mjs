import test from "node:test";
import assert from "node:assert/strict";
import { StormPTGatewayClient, StormPTGatewayError } from "../StormPTGatewayClient.mjs";

const T = "Q".repeat(43);
const asJson = (body, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

test("invalid and unsafe origins are rejected", () => {
  for (const url of ["http://remote.example", "https://user:pass@example.test/",
                     "https://example.test/path", "https://example.test/?debug=1"]) {
    assert.throws(() => new StormPTGatewayClient({ baseUrl: url, fetchImpl: async () => asJson({}) }), TypeError);
  }
  assert.doesNotThrow(() => new StormPTGatewayClient({
    baseUrl: "http://127.0.0.1:18878", fetchImpl: async () => asJson({}),
  }));
});

test("cannot fetch profile without a session", async () => {
  let calls = 0;
  const client = new StormPTGatewayClient({ fetchImpl: async () => { calls++; return asJson({}); } });
  await assert.rejects(client.me(), (e) => e instanceof StormPTGatewayError && e.status === 401);
  await assert.rejects(client.characters(), (e) => e.status === 401);
  assert.equal(calls, 0);
});

test("login, identity, characters, logout (synthetic data)", async () => {
  const requests = [];
  const client = new StormPTGatewayClient({
    fetchImpl: async (path, options) => {
      requests.push({ path, options });
      assert.equal(options.credentials, "omit");
      assert.equal(options.redirect, "error");
      assert.equal(options.cache, "no-store");
      if (path === "/v1/sessions") {
        assert.equal(options.method, "POST");
        assert.deepEqual(JSON.parse(options.body), { username: "TEST1", password: "dummyPassword!" });
        assert.equal(options.headers.Authorization, undefined);
        return asJson({ access_token: T, token_type: "Bearer", expires_in: 900 });
      }
      assert.equal(options.headers.Authorization, "Bearer " + T);
      if (path === "/v1/me") return asJson({ username: "TEST1" });
      if (path === "/v1/characters") return asJson({
        username: "TEST1",
        characters: [{ name: "LabKnight", classCode: 1,
                       className: "Fighter", level: 42, privateData: "FILTER_ME" }],
      });
      if (path === "/v1/logout") return new Response(null, { status: 204 });
      throw new Error("Unexpected path");
    },
  });
  assert.equal(client.authenticated, false);
  assert.deepEqual(await client.login("TEST1", "dummyPassword!"), { expiresInSeconds: 900 });
  assert.equal(client.authenticated, true);
  assert.deepEqual(await client.me(), { username: "TEST1" });
  const list = await client.characters();
  assert.equal(list.username, "TEST1");
  assert.deepEqual(list.characters, [{
    name: "LabKnight", classCode: 1, className: "Fighter", level: 42,
  }]);
  await client.logout();
  assert.equal(client.authenticated, false);
  await assert.rejects(client.me(), (e) => e.status === 401);
  assert.equal(requests.length, 4);
});

test("invalid credentials are rejected before any HTTP request", async () => {
  let calls = 0;
  const client = new StormPTGatewayClient({
    fetchImpl: async () => { calls++; return asJson({}); },
  });
  for (const username of ["bad account", "", "b".repeat(33)]) {
    await assert.rejects(client.login(username, "dummy"), (e) => e.status === 400);
  }
  assert.equal(calls, 0);
});

test("untrusted token or expiration is rejected", async () => {
  const client = new StormPTGatewayClient({
    fetchImpl: async () => asJson({ access_token: "weak", token_type: "Bearer", expires_in: 900 }),
  });
  await assert.rejects(client.login("TEST1", "dummy"), (e) => e.status === 502);
  assert.equal(client.authenticated, false);
});

test("401 clears token; expired token is never sent", async () => {
  let count = 0;
  const client = new StormPTGatewayClient({
    fetchImpl: async (path) => {
      count++;
      if (path === "/v1/sessions")
        return asJson({ access_token: T, token_type: "Bearer", expires_in: 900 });
      if (path === "/v1/me") return asJson({ error: "invalid" }, 401);
      throw new Error("Unexpected");
    },
  });
  await client.login("TEST1", "dummy");
  await assert.rejects(client.me(), (e) => e.status === 401);
  assert.equal(client.authenticated, false);
  await assert.rejects(client.characters(), (e) => e.status === 401);
  assert.equal(count, 2);
});

test("character records have strict public fields", async () => {
  const client = new StormPTGatewayClient({
    fetchImpl: async (path) => path === "/v1/sessions" ?
      asJson({ access_token: T, token_type: "Bearer", expires_in: 900 }) :
      asJson({ username: "TEST1", characters: [{ name: "Knight",
        classCode: 1, className: "Fighter", level: -99 }] }),
  });
  await client.login("TEST1", "dummy");
  await assert.rejects(client.characters(), (e) => e.status === 502);
});

test("server availability and throttling have typed errors", async () => {
  for (const status of [429, 503]) {
    const client = new StormPTGatewayClient({
      fetchImpl: async () => asJson({ error: "details-not-exposed" }, status),
    });
    await assert.rejects(client.login("TEST1", "dummy"), (e) =>
      e.status === status && !e.message.includes("details-not-exposed"));
  }
});

test("character reservation is authenticated, temporary and does not enter the world", async () => {
  const calls = [];
  const client = new StormPTGatewayClient({
    fetchImpl: async (path, options) => {
      calls.push({ path, options });
      if (path === "/v1/sessions")
        return asJson({ access_token: T, token_type: "Bearer", expires_in: 900 });
      assert.equal(options.headers.Authorization, "Bearer " + T);
      assert.equal(JSON.parse(options.body).name, "LabKnight");
      if (path === "/v1/characters/reserve")
        return asJson({ name: "LabKnight", expiresInSeconds: 90, worldAccessGranted: false });
      if (path === "/v1/characters/renew")
        return asJson({ name: "LabKnight", expiresInSeconds: 90, worldAccessGranted: false });
      if (path === "/v1/characters/release")
        return new Response(null, { status: 204 });
      throw new Error("unknown path");
    },
  });
  await assert.rejects(client.reserveCharacter("LabKnight"), (e) => e.status === 401);
  await client.login("TEST1", "dummy");
  const lease = await client.reserveCharacter(" LabKnight ");
  assert.deepEqual(lease, { name: "LabKnight", expiresInSeconds: 90, worldAccessGranted: false });
  assert.deepEqual(await client.renewCharacter("LabKnight"), { expiresInSeconds: 90 });
  await client.releaseCharacter("LabKnight");
  assert.deepEqual(calls.map(c => c.path), [
    "/v1/sessions", "/v1/characters/reserve", "/v1/characters/renew",
    "/v1/characters/release",
  ]);
  await assert.rejects(client.reserveCharacter("   "), (e) => e.status === 400);
  assert.equal(calls.length, 4);
});

test("another session's character lease reports conflict", async () => {
  const client = new StormPTGatewayClient({
    fetchImpl: async (path) =>
      path === "/v1/sessions"
        ? asJson({ access_token: T, token_type: "Bearer", expires_in: 900 })
        : asJson({ error: "private" }, 409),
  });
  await client.login("TEST1", "dummy");
  await assert.rejects(client.reserveCharacter("LabKnight"),
    (error) => error.status === 409 && !error.message.includes("private"));
});
