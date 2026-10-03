(function (root, factory) {
  var api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  root.QrProtocol = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  var enc = new TextEncoder();
  var dec = new TextDecoder();

  function hex(bytes, n) {
    var out = "";
    for (var i = 0; i < n; i++) out += bytes[i].toString(16).padStart(2, "0");
    return out;
  }

  function b64(bytes) {
    var bin = "";
    for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
    return btoa(bin);
  }

  function unb64(text) {
    var bin = atob(text);
    var bytes = new Uint8Array(bin.length);
    for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
  }

  async function sha256(text) {
    var buf = await crypto.subtle.digest("SHA-256", enc.encode(text));
    return new Uint8Array(buf);
  }

  async function hmac(password, message) {
    var key = await crypto.subtle.importKey(
      "raw",
      enc.encode(password),
      { name: "HMAC", hash: "SHA-256" },
      false,
      ["sign"]
    );
    var sig = await crypto.subtle.sign("HMAC", key, enc.encode(message));
    return new Uint8Array(sig);
  }

  function currentWindow(nowMs) {
    var ms = nowMs == null ? Date.now() : nowMs;
    return Math.floor(ms / 1000 / 5);
  }

  function fresh(window, nowMs) {
    var delta = currentWindow(nowMs) - window;
    if (delta < 0) delta = -delta;
    return delta <= 9;
  }

  async function passwordHash(password) {
    return hex(await sha256(password), 8);
  }

  async function sign(login, password, device, window) {
    var mac = await hmac(password, login + "\n" + device + "\n" + window);
    return hex(mac, 4);
  }

  async function studentPayload(login, password, device, window) {
    var value = window == null ? currentWindow() : window;
    var sig = await sign(login, password, device, value);
    return "KG1|S|" + login + "|" + password + "|" + device + "|" + value + "|" + sig;
  }

  function parseStudent(text) {
    if (!text) return null;
    var parts = String(text).trim().split("|");
    if (parts.length !== 7 || parts[0] !== "KG1" || parts[1] !== "S") return null;
    if (!parts[2] || !parts[3] || !parts[4] || !parts[6]) return null;
    if (!/^\d+$/.test(parts[5])) return null;
    return {
      login: parts[2],
      password: parts[3],
      device: parts[4],
      window: Number(parts[5]),
      sig: parts[6].toLowerCase()
    };
  }

  async function signatureValid(ticket) {
    var sig = await sign(ticket.login, ticket.password, ticket.device, ticket.window);
    return sig === ticket.sig.toLowerCase();
  }

  function parseFrame(text) {
    if (!text) return null;
    var parts = String(text).trim().split("|");
    if (parts.length < 6 || parts[0] !== "KG1" || parts[1].length !== 1) return null;
    var kind = parts[1];
    if (kind !== "C" && kind !== "R") return null;
    var index = Number(parts[3]);
    var count = Number(parts[4]);
    if (!index || !count || index < 1 || index > count || count > 80 || !parts[2]) return null;
    return {
      kind: kind,
      rollId: parts[2].toLowerCase(),
      index: index,
      count: count,
      chunk: parts.slice(5).join("|")
    };
  }

  function compress(text) {
    return globalThis.pako.deflate(enc.encode(text));
  }

  function decompress(bytes) {
    return dec.decode(globalThis.pako.inflate(bytes));
  }

  function encodeFrames(kind, rollId, body, chunkSize) {
    var encoded = b64(compress(body));
    var chunks = [];
    if (!encoded.length) chunks.push("");
    else {
      for (var i = 0; i < encoded.length; i += chunkSize) chunks.push(encoded.slice(i, i + chunkSize));
    }
    var frames = [];
    for (var n = 0; n < chunks.length; n++) {
      frames.push("KG1|" + kind + "|" + rollId + "|" + (n + 1) + "|" + chunks.length + "|" + chunks[n]);
    }
    return frames;
  }

  function encodeSession(rollId, body) {
    var single = encodeFrames("C", rollId, body, 100000);
    if (single.length === 1 && single[0].length <= 1200) return single;
    return encodeFrames("C", rollId, body, 320);
  }

  function encodeResult(rollId, body) {
    var single = encodeFrames("R", rollId, body, 100000);
    if (single.length === 1 && single[0].length <= 1400) return single;
    return encodeFrames("R", rollId, body, 320);
  }

  function tryDecode(frames) {
    if (!frames.length) return null;
    var count = frames[0].count;
    var roll = frames[0].rollId;
    var kind = frames[0].kind;
    if (frames.length !== count) return null;
    var ordered = new Array(count);
    for (var i = 0; i < frames.length; i++) {
      var frame = frames[i];
      if (frame.count !== count || frame.rollId !== roll || frame.kind !== kind) return null;
      ordered[frame.index - 1] = frame.chunk;
    }
    if (ordered.some(function (chunk) { return chunk == null; })) return null;
    try {
      return decompress(unb64(ordered.join("")));
    } catch (e) {
      return null;
    }
  }

  function Collector() {
    this.roll = null;
    this.kind = null;
    this.count = 0;
    this.chunks = {};
  }

  Collector.prototype.add = function (frame) {
    if (this.roll !== frame.rollId || this.kind !== frame.kind || this.count !== frame.count) {
      this.roll = frame.rollId;
      this.kind = frame.kind;
      this.count = frame.count;
      this.chunks = {};
    }
    this.chunks[frame.index] = frame.chunk;
    var got = Object.keys(this.chunks).length;
    if (got < this.count) return null;
    var frames = [];
    for (var i = 1; i <= this.count; i++) {
      if (this.chunks[i] == null) return null;
      frames.push({ kind: this.kind, rollId: this.roll, index: i, count: this.count, chunk: this.chunks[i] });
    }
    return tryDecode(frames);
  };

  function sanitize(text) {
    return String(text || "").replace(/[\t\n\r|]/g, " ").trim();
  }

  function parseRoster(body) {
    var list = [];
    String(body).split("\n").forEach(function (line) {
      if (!line) return;
      var parts = line.split("\t");
      if (parts.length < 5 || !/^\d+$/.test(parts[0])) return;
      list.push({
        id: Number(parts[0]),
        login: parts[1],
        hash: parts[2],
        name: parts[3],
        device: parts[4]
      });
    });
    return list;
  }

  function parseMarks(body) {
    var list = [];
    String(body).split("\n").forEach(function (line) {
      if (!line) return;
      var parts = line.split("\t");
      if (parts.length < 3 || parts[0].length !== 1 || !/^\d+$/.test(parts[1])) return;
      list.push({ code: parts[0], id: Number(parts[1]), extra: parts[2] });
    });
    return list;
  }

  function buildResult(present, suspicious) {
    var lines = [];
    present.forEach(function (row) {
      lines.push("P\t" + row.id + "\t" + sanitize(row.device));
    });
    suspicious.forEach(function (row) {
      lines.push("S\t" + row.ownerId + "\t" + sanitize(row.attempted));
    });
    return lines.length ? lines.join("\n") + "\n" : "";
  }

  return {
    currentWindow: currentWindow,
    fresh: fresh,
    passwordHash: passwordHash,
    sign: sign,
    studentPayload: studentPayload,
    parseStudent: parseStudent,
    signatureValid: signatureValid,
    parseFrame: parseFrame,
    encodeFrames: encodeFrames,
    encodeSession: encodeSession,
    encodeResult: encodeResult,
    Collector: Collector,
    sanitize: sanitize,
    parseRoster: parseRoster,
    parseMarks: parseMarks,
    buildResult: buildResult
  };
});
