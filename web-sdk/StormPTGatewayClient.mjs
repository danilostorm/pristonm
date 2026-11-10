/**
 * Browser client for the StormPT LAB gateway.
 * Same-origin by default, ephemeral token in memory, never in localStorage.
 * Not a complete game client.
 */
const ACCOUNT = /^[A-Za-z0-9]{1,32}$/;
const TOKEN = /^[A-Za-z0-9_-]{43}$/;

export class StormPTGatewayError extends Error {
  constructor(message, status = 0) {
    super(message);
    this.name = "StormPTGatewayError";
    this.status = status;
  }
}

export class StormPTGatewayClient {
  #origin;
  #fetch;
  #token = null;
  #expiresAt = 0;

  constructor({ baseUrl = "", fetchImpl } = {}) {
    if (baseUrl) {
      const url = new URL(baseUrl);
      const local = ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname);
      if ((url.protocol !== "https:" && !(url.protocol === "http:" && local)) ||
          url.username || url.password || url.search || url.hash || url.pathname !== "/")
        throw new TypeError("Gateway requires a safe HTTPS origin or loopback.");
      if (typeof globalThis.location !== "undefined" &&
          url.origin !== globalThis.location.origin)
        throw new TypeError("Use a same-origin reverse proxy for the Web gateway.");
      this.#origin = url.origin;
    } else {
      const pageLocation = globalThis.location;
      if (pageLocation && pageLocation.protocol !== "https:" &&
          !(pageLocation.protocol === "http:" &&
            ["localhost", "127.0.0.1", "[::1]"].includes(pageLocation.hostname))) {
        throw new TypeError("Web gateway login requires HTTPS outside loopback.");
      }
      this.#origin = "";
    }

    const chosen = fetchImpl ?? globalThis.fetch;
    if (typeof chosen !== "function") throw new TypeError("fetch is unavailable");
    this.#fetch = chosen;
  }

  get authenticated() {
    if (!this.#token) return false;
    if (Date.now() >= this.#expiresAt) {
      this.#forget();
      return false;
    }
    return true;
  }

  #forget() {
    this.#token = null;
    this.#expiresAt = 0;
  }

  async #request(path, { method = "GET", json, authorized = false, signal } = {}) {
    const headers = { Accept: "application/json" };
    if (json !== undefined) headers["Content-Type"] = "application/json";
    if (authorized) {
      if (!this.authenticated) throw new StormPTGatewayError("Not authenticated.", 401);
      headers.Authorization = "Bearer " + this.#token;
    }
    let reply;
    try {
      reply = await this.#fetch(this.#origin + path, {
        method, headers,
        body: json === undefined ? undefined : JSON.stringify(json),
        credentials: "omit", redirect: "error", cache: "no-store",
        referrerPolicy: "no-referrer", signal,
      });
    } catch {
      throw new StormPTGatewayError("Gateway unreachable.", 0);
    }

    if (authorized && reply.status === 401) this.#forget();
    if (!reply.ok) {
      const status = Number.isInteger(reply.status) ? reply.status : 0;
      const message = status === 401 ? "Session invalid or expired." :
        status === 429 ? "Too many attempts." :
        status === 409 ? "Character reserved by another session." :
        status === 503 ? "LAB service unavailable." : "Request failed.";
      throw new StormPTGatewayError(message, status);
    }
    if (reply.status === 204) return null;
    try {
      return await reply.json();
    } catch {
      throw new StormPTGatewayError("Invalid gateway response.", 502);
    }
  }

  async login(username, password, { signal } = {}) {
    this.#forget();
    if (typeof username !== "string" || !ACCOUNT.test(username) ||
        typeof password !== "string" || password.length < 1 || password.length > 32)
      throw new StormPTGatewayError("Invalid credential format.", 400);

    const data = await this.#request("/v1/sessions", {
      method: "POST", json: { username, password }, signal,
    });
    if (!data || data.token_type !== "Bearer" ||
        typeof data.access_token !== "string" || !TOKEN.test(data.access_token) ||
        !Number.isInteger(data.expires_in) ||
        data.expires_in < 1 || data.expires_in > 900)
      throw new StormPTGatewayError("Untrusted login response.", 502);

    this.#token = data.access_token;
    this.#expiresAt = Date.now() + data.expires_in * 1000;
    return { expiresInSeconds: data.expires_in };
  }

  async me({ signal } = {}) {
    const data = await this.#request("/v1/me", { authorized: true, signal });
    if (!data || typeof data.username !== "string" || !ACCOUNT.test(data.username))
      throw new StormPTGatewayError("Invalid identity response.", 502);
    return { username: data.username };
  }

  async characters({ signal } = {}) {
    const data = await this.#request("/v1/characters", { authorized: true, signal });
    if (!data || typeof data.username !== "string" || !ACCOUNT.test(data.username) ||
        !Array.isArray(data.characters) || data.characters.length > 12)
      throw new StormPTGatewayError("Invalid character response.", 502);
    const characters = data.characters.map((ch) => {
      if (!ch || typeof ch.name !== "string" || !ch.name.length || ch.name.length > 64 ||
          typeof ch.className !== "string" || ch.className.length > 64 ||
          !Number.isInteger(ch.level) || ch.level < 0 || ch.level > 500 ||
          !Number.isInteger(ch.classCode) || ch.classCode < 0 || ch.classCode > 100)
        throw new StormPTGatewayError("Invalid character record.", 502);
      return { name: ch.name, classCode: ch.classCode,
        className: ch.className, level: ch.level };
    });
    return { username: data.username, characters };
  }

  /**
   * Reserve an owned character for this browser session. The game world is NOT
   * entered: this is only a 90-second LAB lease that must be renewed or released.
   */
  async reserveCharacter(name, { signal } = {}) {
    const wanted = this.#validateCharacterName(name);
    const data = await this.#request("/v1/characters/reserve", {
      method: "POST", json: { name: wanted }, authorized: true, signal,
    });
    if (!data || typeof data.name !== "string" ||
        data.name.toLowerCase() !== wanted.toLowerCase() ||
        !Number.isInteger(data.expiresInSeconds) ||
        data.expiresInSeconds < 1 || data.expiresInSeconds > 90 ||
        data.worldAccessGranted !== false) {
      throw new StormPTGatewayError("Invalid character reservation response.", 502);
    }
    return { name: data.name, expiresInSeconds: data.expiresInSeconds,
      worldAccessGranted: false };
  }

  async renewCharacter(name, { signal } = {}) {
    const wanted = this.#validateCharacterName(name);
    const data = await this.#request("/v1/characters/renew", {
      method: "POST", json: { name: wanted }, authorized: true, signal,
    });
    if (!data || data.worldAccessGranted !== false ||
        !Number.isInteger(data.expiresInSeconds) ||
        data.expiresInSeconds < 1 || data.expiresInSeconds > 90) {
      throw new StormPTGatewayError("Invalid character lease response.", 502);
    }
    return { expiresInSeconds: data.expiresInSeconds };
  }

  async releaseCharacter(name, { signal } = {}) {
    const wanted = this.#validateCharacterName(name);
    await this.#request("/v1/characters/release", {
      method: "POST", json: { name: wanted }, authorized: true, signal,
    });
  }

  #validateCharacterName(name) {
    if (typeof name !== "string") throw new StormPTGatewayError("Invalid character name.", 400);
    const wanted = name.trim();
    if (!wanted || wanted.length > 64 || /[\x00-\x1f\x7f]/.test(wanted))
      throw new StormPTGatewayError("Invalid character name.", 400);
    return wanted;
  }

  async logout({ signal } = {}) {
    if (!this.authenticated) { this.#forget(); return; }
    try {
      await this.#request("/v1/logout", { method: "POST", authorized: true, signal });
    } finally {
      this.#forget();
    }
  }
}
