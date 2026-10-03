const $ = (id) => document.getElementById(id);
const store = localStorage;
const P = QrProtocol;

function deviceId() {
  let id = store.getItem("device");
  if (id && id.length >= 8) return id;
  const bytes = new Uint8Array(8);
  crypto.getRandomValues(bytes);
  id = [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
  store.setItem("device", id);
  return id;
}

function drawQr(text) {
  const level = text.length > 700 ? "L" : "M";
  let made = null;
  for (let type = 1; type <= 40; type++) {
    try {
      const qr = qrcode(type, level);
      qr.addData(text);
      qr.make();
      made = qr;
      break;
    } catch (e) {
      made = null;
    }
  }
  return made ? made.createDataURL(8, 2) : "";
}

let login = store.getItem("login") || "";
let password = store.getItem("password") || "";
let shownWindow = -1;
let device = "";

function showLogin() {
  $("loginCard").hidden = false;
  $("qrBlock").hidden = true;
  $("logoutButton").hidden = true;
}

function showStudentQr() {
  $("loginCard").hidden = true;
  $("qrBlock").hidden = false;
  $("logoutButton").hidden = false;
  shownWindow = -1;
  refreshStudent();
}

async function refreshStudent() {
  if (!login) return;
  const window = P.currentWindow();
  if (window === shownWindow) return;
  shownWindow = window;
  const payload = await P.studentPayload(login, password, device);
  $("qrImage").src = drawQr(payload);
}

$("loginButton").onclick = () => {
  const nextLogin = $("loginInput").value.trim();
  const nextPassword = $("passwordInput").value.trim();
  if (!nextLogin || !nextPassword) {
    $("loginError").hidden = false;
    $("loginError").textContent = "Введите логин и пароль";
    return;
  }
  $("loginError").hidden = true;
  login = nextLogin;
  password = nextPassword;
  store.setItem("login", login);
  store.setItem("password", password);
  showStudentQr();
};

$("logoutButton").onclick = () => {
  login = "";
  password = "";
  store.removeItem("login");
  store.removeItem("password");
  showLogin();
};

setInterval(() => {
  if (!$("student").hidden && !$("qrBlock").hidden) refreshStudent();
}, 500);

let mode = "student";
let rollId = "";
let roster = [];
let byLogin = new Map();
let byDevice = new Map();
let present = new Map();
let suspicious = [];
let sessionCollector = new P.Collector();
let resultFrames = [];
let resultIndex = 0;
let resultTimer = 0;
let scanTimer = 0;
let stream = null;
let scanKind = "session";
let lastText = "";
let lastAt = 0;
let busy = false;

function setTeacherStatus(text, kind) {
  const node = $("teacherStatus");
  node.textContent = text;
  node.className = "status" + (kind === "ok" ? " ok" : kind === "bad" ? " bad" : "");
}

function teacherIdle() {
  $("modeSwitch").hidden = false;
  $("teacherHome").hidden = false;
  $("teacherCamera").hidden = true;
  $("teacherResult").hidden = true;
  $("teacherLogo").className = "logo big";
  $("teacherGroup").hidden = true;
  $("teacherCount").hidden = true;
  $("teacherList").hidden = true;
  $("teacherSecondary").hidden = true;
  $("teacherTransfer").hidden = true;
  $("teacherPrimary").textContent = "Сканировать сессию с ПК";
  setTeacherStatus("Наведите камеру на код сессии на экране ПК. Сеть не нужна.");
}

function teacherReady() {
  $("modeSwitch").hidden = false;
  $("teacherHome").hidden = false;
  $("teacherCamera").hidden = true;
  $("teacherResult").hidden = true;
  $("teacherLogo").className = "logo small";
  $("teacherGroup").hidden = false;
  $("teacherGroup").textContent = "Сессия " + rollId;
  $("teacherCount").hidden = false;
  $("teacherCount").textContent = present.size + " / " + roster.length;
  $("teacherList").hidden = false;
  $("teacherList").textContent = roster.map((person) => (present.has(person.id) ? "●  " : "○  ") + person.name).join("\n");
  $("teacherSecondary").hidden = false;
  $("teacherTransfer").hidden = false;
  $("teacherPrimary").textContent = "Сканировать студентов";
  setTeacherStatus("Группа считана. Сканируйте QR студентов.", "ok");
}

async function startCamera(kind) {
  scanKind = kind;
  $("modeSwitch").hidden = true;
  $("teacherHome").hidden = true;
  $("teacherResult").hidden = true;
  $("teacherCamera").hidden = false;
  $("cameraStatus").textContent = kind === "session" ? "Держите камеру на коде сессии" : "Наведите на QR студента";
  $("cameraStatus").className = "";
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    $("cameraStatus").textContent = "Камера недоступна. Откройте по HTTPS и разрешите камеру.";
    $("cameraStatus").className = "bad";
    return;
  }
  try {
    stream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: { facingMode: { ideal: "environment" } }
    });
    $("camera").srcObject = stream;
    clearInterval(scanTimer);
    scanTimer = setInterval(scanFrame, 180);
  } catch (e) {
    $("cameraStatus").textContent = "Разрешите камеру, чтобы сканировать QR";
    $("cameraStatus").className = "bad";
  }
}

function stopCamera() {
  clearInterval(scanTimer);
  if (stream) {
    stream.getTracks().forEach((track) => track.stop());
    stream = null;
  }
  $("camera").srcObject = null;
}

function scanFrame() {
  const video = $("camera");
  if (!video.videoWidth) return;
  const canvas = scanFrame.canvas || (scanFrame.canvas = document.createElement("canvas"));
  canvas.width = video.videoWidth;
  canvas.height = video.videoHeight;
  const ctx = canvas.getContext("2d", { willReadFrequently: true });
  ctx.drawImage(video, 0, 0);
  const image = ctx.getImageData(0, 0, canvas.width, canvas.height);
  const code = jsQR(image.data, image.width, image.height, { inversionAttempts: "dontInvert" });
  if (code && code.data) onTeacherQr(code.data);
}

function onTeacherQr(text) {
  const now = Date.now();
  if (text === lastText && now - lastAt < 1600) return;
  lastText = text;
  lastAt = now;
  if (scanKind === "session") onSession(text);
  else onStudent(text);
}

function onSession(text) {
  const frame = P.parseFrame(text);
  if (!frame || frame.kind !== "C") {
    $("cameraStatus").textContent = "Это не код сессии. Наведите на экран ПК.";
    $("cameraStatus").className = "bad";
    return;
  }
  const body = sessionCollector.add(frame);
  if (!body) {
    $("cameraStatus").textContent = "Сессия " + Object.keys(sessionCollector.chunks).length + " / " + sessionCollector.count;
    $("cameraStatus").className = "";
    return;
  }
  roster = P.parseRoster(body);
  byLogin = new Map(roster.map((person) => [person.login.toLowerCase(), person]));
  byDevice = new Map(roster.filter((person) => person.device).map((person) => [person.device, person]));
  present = new Map();
  suspicious = [];
  rollId = frame.rollId;
  if (!roster.length) {
    $("cameraStatus").textContent = "В коде сессии нет студентов";
    $("cameraStatus").className = "bad";
    return;
  }
  stopCamera();
  teacherReady();
}

async function onStudent(text) {
  if (busy) return;
  const ticket = P.parseStudent(text);
  if (!ticket) return fail("Не тот код");
  if (!P.fresh(ticket.window)) return fail("Код устарел — пусть студент обновит экран");
  busy = true;
  try {
    if (!(await P.signatureValid(ticket))) return fail("Не получилось, попробуйте ещё раз");
    const person = byLogin.get(ticket.login.toLowerCase());
    if (!person) return fail("Студент не из этой группы");
    if ((await P.passwordHash(ticket.password)) !== person.hash) return fail("НЕВЕРНЫЙ ЛОГИН ИЛИ ПАРОЛЬ");
    if (person.device && person.device !== ticket.device) return fail("Аккаунт привязан к другому телефону");
    const owner = byDevice.get(ticket.device);
    if (owner && owner.id !== person.id) {
      suspicious.push({ ownerId: owner.id, attempted: ticket.login });
      fail("Что-то не так");
      flash(owner.name, "#B34A4A");
      return;
    }
    if (present.has(person.id)) {
      $("cameraStatus").textContent = "Уже отмечен: " + person.name;
      $("cameraStatus").className = "ok";
      return;
    }
    present.set(person.id, ticket.device);
    if (!person.device) {
      person.device = ticket.device;
      byDevice.set(ticket.device, person);
    }
    $("cameraStatus").textContent = "Отмечен: " + person.name + "  ·  " + present.size + " / " + roster.length;
    $("cameraStatus").className = "ok";
    flash(person.name, "#2E7D32");
  } finally {
    busy = false;
  }
}

function fail(message) {
  $("cameraStatus").textContent = message;
  $("cameraStatus").className = "bad";
}

function flash(name, color) {
  const node = $("flashName");
  node.hidden = false;
  node.textContent = name;
  node.style.color = color;
  setTimeout(() => { node.hidden = true; }, 1200);
}

function showTransfer() {
  $("modeSwitch").hidden = true;
  const marks = [];
  present.forEach((deviceId, id) => marks.push({ id, device: deviceId }));
  const body = P.buildResult(marks, suspicious);
  resultFrames = P.encodeResult(rollId, body);
  resultIndex = 0;
  stopCamera();
  $("teacherHome").hidden = true;
  $("teacherCamera").hidden = true;
  $("teacherResult").hidden = false;
  paintResult();
  clearInterval(resultTimer);
  if (resultFrames.length > 1) resultTimer = setInterval(() => {
    resultIndex = (resultIndex + 1) % resultFrames.length;
    paintResult();
  }, 700);
}

function paintResult() {
  $("resultQr").src = drawQr(resultFrames[resultIndex]);
  $("resultLabel").textContent = resultFrames.length === 1
    ? "Покажите этот код камере ПК"
    : "Кадр " + (resultIndex + 1) + " из " + resultFrames.length + ". Держите телефон перед камерой ПК.";
}

$("teacherPrimary").onclick = () => startCamera(roster.length ? "students" : "session");
$("teacherSecondary").onclick = () => {
  roster = [];
  sessionCollector = new P.Collector();
  startCamera("session");
};
$("teacherTransfer").onclick = () => {
  if (!present.size && !suspicious.length) {
    setTeacherStatus("Сначала отметьте студентов", "bad");
    return;
  }
  showTransfer();
};
$("cameraCancel").onclick = () => {
  stopCamera();
  if (roster.length) teacherReady();
  else teacherIdle();
};
$("resultBack").onclick = () => {
  clearInterval(resultTimer);
  teacherReady();
};
$("saveResult").onclick = () => {
  const blob = new Blob([resultFrames.join("\n") + "\n"], { type: "text/plain" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = "KubGAU-rollcall.kjournal";
  link.click();
  URL.revokeObjectURL(link.href);
  $("resultLabel").textContent = "Файл KubGAU-rollcall.kjournal сохранён";
};

$("modeSwitch").onclick = () => {
  mode = mode === "student" ? "teacher" : "student";
  const teacher = mode === "teacher";
  document.body.classList.toggle("teacher", teacher);
  $("student").hidden = teacher;
  $("teacher").hidden = !teacher;
  $("modeSwitch").textContent = teacher ? "Режим студента" : "Режим преподавателя";
  document.querySelector('meta[name="theme-color"]').content = teacher ? "#FFFFFF" : "#0A0D1A";
  if (!teacher) {
    stopCamera();
    clearInterval(resultTimer);
  }
};

if (!window.isSecureContext) $("httpsWarn").hidden = false;
if ("serviceWorker" in navigator && window.isSecureContext) {
  navigator.serviceWorker.register("sw.js").catch(() => {});
}
device = deviceId();
if (login && password) showStudentQr();
else showLogin();
teacherIdle();
