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

  function rotr(x, n) { return (x >>> n) | (x << (32 - n)); }

  function sha256Bytes(bytes) {
    var K = [
      0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
      0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
      0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
      0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
      0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
      0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
      0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
      0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
    ];
    var h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a;
    var h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
    var bitLen = bytes.length * 8;
    var padded = new Uint8Array(((bytes.length + 9 + 63) >> 6) << 6);
    padded.set(bytes);
    padded[bytes.length] = 0x80;
    var view = new DataView(padded.buffer);
    view.setUint32(padded.length - 8, Math.floor(bitLen / 0x100000000));
    view.setUint32(padded.length - 4, bitLen >>> 0);
    var w = new Uint32Array(64);
    for (var offset = 0; offset < padded.length; offset += 64) {
      for (var t = 0; t < 16; t++) w[t] = view.getUint32(offset + t * 4);
      for (var t = 16; t < 64; t++) {
        var s0 = rotr(w[t - 15], 7) ^ rotr(w[t - 15], 18) ^ (w[t - 15] >>> 3);
        var s1 = rotr(w[t - 2], 17) ^ rotr(w[t - 2], 19) ^ (w[t - 2] >>> 10);
        w[t] = (w[t - 16] + s0 + w[t - 7] + s1) >>> 0;
      }
      var a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
      for (var t = 0; t < 64; t++) {
        var S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
        var ch = (e & f) ^ (~e & g);
        var temp1 = (h + S1 + ch + K[t] + w[t]) >>> 0;
        var S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
        var maj = (a & b) ^ (a & c) ^ (b & c);
        var temp2 = (S0 + maj) >>> 0;
        h = g; g = f; f = e; e = (d + temp1) >>> 0; d = c; c = b; b = a; a = (temp1 + temp2) >>> 0;
      }
      h0 = (h0 + a) >>> 0; h1 = (h1 + b) >>> 0; h2 = (h2 + c) >>> 0; h3 = (h3 + d) >>> 0;
      h4 = (h4 + e) >>> 0; h5 = (h5 + f) >>> 0; h6 = (h6 + g) >>> 0; h7 = (h7 + h) >>> 0;
    }
    var out = new Uint8Array(32);
    var ov = new DataView(out.buffer);
    ov.setUint32(0, h0); ov.setUint32(4, h1); ov.setUint32(8, h2); ov.setUint32(12, h3);
    ov.setUint32(16, h4); ov.setUint32(20, h5); ov.setUint32(24, h6); ov.setUint32(28, h7);
    return out;
  }

  function hmacBytes(key, message) {
    var block = 64;
    if (key.length > block) key = sha256Bytes(key);
    var k = new Uint8Array(block);
    k.set(key);
    var outer = new Uint8Array(block);
    var inner = new Uint8Array(block);
    for (var i = 0; i < block; i++) {
      outer[i] = k[i] ^ 0x5c;
      inner[i] = k[i] ^ 0x36;
    }
    var innerMsg = new Uint8Array(block + message.length);
    innerMsg.set(inner);
    innerMsg.set(message, block);
    var innerHash = sha256Bytes(innerMsg);
    var outerMsg = new Uint8Array(block + 32);
    outerMsg.set(outer);
    outerMsg.set(innerHash, block);
    return sha256Bytes(outerMsg);
  }

  async function sha256(text) {
    return sha256Bytes(enc.encode(text));
  }

  async function hmac(password, message) {
    return hmacBytes(enc.encode(password), enc.encode(message));
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
