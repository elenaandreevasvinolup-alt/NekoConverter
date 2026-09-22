namespace MathGeo.Cli;

/// <summary>
/// 交互式预览的单文件 HTML 模板。
///
/// 用 @@占位符@@ 而不是 C# 的字符串插值：这份模板里全是 CSS 和 JS 的大括号，
/// 用插值语法要把每一个花括号都写成两个，改一次错一次。
///
/// 关键设计：
///   · **纯触摸**：拖动图形本身就是擦洗（scrub），不依赖鼠标滚轮、右键、快捷键；
///     所有按钮都是 44px 以上的触摸目标。光标只是桌面端的加分项。
///   · 主题切换是瞬时的：帧里只带浅色，切换时查颜色映射表换色，文件不翻倍。
///   · 完全离线：没有任何外部请求，双击就能用，也能塞进 U 盘和微信。
/// </summary>
internal static class InteractiveTemplate
{
    public const string Html = """
<!DOCTYPE html>
<html lang="@@LANG@@" dir="@@DIR@@">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
<title>@@APP_NAME@@</title>
<style>
:root {
@@THEME_LIGHT@@
  --tap: 46px;
}
html[data-theme="dark"] {
@@THEME_DARK@@
}
* { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
body {
  margin: 0; padding: 20px 16px 48px;
  background: var(--bg); color: var(--text);
  font: 15px/1.6 -apple-system, "Segoe UI", "Noto Sans SC", "Microsoft YaHei", sans-serif;
  overscroll-behavior: contain;
}
header { max-width: 1240px; margin: 0 auto 20px; display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
h1 { margin: 0; font-size: 20px; font-weight: 600; flex: 1 1 auto; }
h1 small { display: block; font-size: 12px; font-weight: 400; color: var(--muted); margin-top: 2px; }
.controls { display: flex; gap: 8px; flex-wrap: wrap; }
button, select {
  min-height: var(--tap); padding: 0 16px; border-radius: 10px;
  border: 1px solid var(--border); background: var(--card); color: var(--text);
  font: inherit; font-size: 14px; cursor: pointer;
}
button:active { background: var(--accent-soft); }
button.primary { background: var(--accent); border-color: var(--accent); color: #fff; font-weight: 600; }
.grid { max-width: 1240px; margin: 0 auto; display: grid; gap: 20px;
        grid-template-columns: repeat(auto-fill, minmax(380px, 1fr)); }
.card { background: var(--card); border: 1px solid var(--border); border-radius: 12px;
        padding: 16px; display: flex; flex-direction: column; }
.card h2 { margin: 0 0 2px; font-size: 15px; font-weight: 600; }
.card .file { font-size: 11px; color: var(--faint); font-family: Menlo, Consolas, monospace; }
.statement { margin: 10px 0 12px; font-size: 14px; }
.stage { position: relative; background: var(--figure); border: 1px solid var(--border);
         border-radius: 10px; overflow: hidden; touch-action: none; }
canvas { display: block; width: 100%; height: auto; touch-action: none;
         cursor: @@CURSOR_DEFAULT@@; }
.badge { position: absolute; inset-inline-start: 10px; top: 10px; font-size: 11px;
         padding: 3px 9px; border-radius: 999px; background: var(--accent-soft);
         color: var(--accent); pointer-events: none; }
.hint { margin: 10px 0 6px; font-size: 12px; color: var(--muted); }
.scrub { display: flex; align-items: center; gap: 12px; }
input[type=range] { flex: 1; height: var(--tap); accent-color: var(--accent); }
.counter { font-size: 12px; color: var(--faint); font-variant-numeric: tabular-nums;
           min-width: 5.5em; text-align: end; }
.answer { margin-top: 12px; padding: 10px 12px; border-radius: 10px;
          background: var(--accent-soft); font-size: 13px; }
.answer .value { font-weight: 600; color: var(--accent); }
.answer ol { margin: 6px 0 0; padding-inline-start: 20px; color: var(--muted); }
.diag { margin-top: 10px; padding: 8px 10px; border-radius: 8px; font-size: 12px;
        font-family: Menlo, Consolas, monospace; white-space: pre-wrap; }
.diag.error { background: color-mix(in srgb, var(--bad) 12%, transparent); color: var(--bad); }
.diag.warning { background: color-mix(in srgb, var(--warn) 12%, transparent); color: var(--warn); }
footer { max-width: 1240px; margin: 28px auto 0; font-size: 12px; color: var(--faint); }
@media (max-width: 520px) { .grid { grid-template-columns: 1fr; } }
</style>
</head>
<body>
<header>
  <h1>@@APP_NAME@@<small>@@TAGLINE@@ · @@SUMMARY@@</small></h1>
  <div class="controls">
    <button id="theme" type="button"></button>
    <select id="lang"></select>
  </div>
</header>
<div class="grid" id="grid"></div>
<footer>@@FOOTER@@</footer>
<script>
"use strict";
const LOCALES = @@LOCALES_JSON@@;
const PALETTE = @@PALETTE_JSON@@;
const PROBLEMS = @@PROBLEMS_JSON@@;
const CURSORS = @@CURSORS_JSON@@;

/* 当前语言。切语言只换界面文案，题目内容保持出题时的原文 —— 题目是内容，不是界面。 */
let LANG = @@LANG_JSON@@;
try { LANG = localStorage.getItem("mathgeo.lang") || LANG; } catch (e) { /* 隐私模式忽略 */ }
function t(key) {
  const table = LOCALES[LANG] || LOCALES.en || {};
  return table[key] || (LOCALES.en || {})[key] || key;
}

const ANCHORS = {
  c:  ["center", "middle", 0, 0],      t:  ["center", "middle", 0, -1],
  b:  ["center", "middle", 0, 1],      l:  ["right",  "middle", -1, 0],
  r:  ["left",   "middle", 1, 0],      tl: ["right",  "middle", -1, -1],
  tr: ["left",   "middle", 1, -1],     bl: ["right",  "middle", -1, 1],
  br: ["left",   "middle", 1, 1],
};

const LABEL_OFFSET = 12;

function isDark() { return document.documentElement.dataset.theme === "dark"; }

/* 主题切换靠查表换色，而不是重存一套帧 —— 否则一份 36 帧的动画会让文件翻倍。 */
function paint(color) {
  if (!color) return color;
  return isDark() ? (PALETTE[color.toUpperCase()] || color) : color;
}

function render(canvas, problem, frameIndex) {
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  const width = canvas.clientWidth || 380;
  const height = Math.round(width * 0.78);

  canvas.width = Math.round(width * dpr);
  canvas.height = Math.round(height * dpr);
  canvas.style.height = height + "px";

  const ctx = canvas.getContext("2d");
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

  const bg = paint(problem.background);
  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, width, height);

  const [minX, minY, w, h] = problem.view;
  const maxY = minY + h;
  const pad = 18;
  const scale = Math.min((width - pad * 2) / Math.max(w, 1e-6),
                         (height - pad * 2) / Math.max(h, 1e-6));
  const ox = pad + ((width - pad * 2) - w * scale) / 2;
  const oy = pad + ((height - pad * 2) - h * scale) / 2;

  const X = x => ox + (x - minX) * scale;
  const Y = y => oy + (maxY - y) * scale;

  const shapes = problem.frames[Math.min(frameIndex, problem.frames.length - 1)];
  const lineWidth = Math.max(1.1, Math.min(scale * 0.9, 2.6));

  for (const s of shapes) {
    ctx.lineJoin = "round";
    ctx.lineCap = "round";

    if (s.t === "l") {
      ctx.beginPath();
      ctx.moveTo(X(s.a[0]), Y(s.a[1]));
      ctx.lineTo(X(s.b[0]), Y(s.b[1]));
      ctx.strokeStyle = paint(s.s);
      ctx.lineWidth = Math.max(0.9, s.w * lineWidth / 1.6);
      ctx.setLineDash(s.d ? s.d.map(v => v * 0.8) : []);
      ctx.stroke();
      ctx.setLineDash([]);

    } else if (s.t === "p") {
      ctx.beginPath();
      s.p.forEach((p, i) => i ? ctx.lineTo(X(p[0]), Y(p[1])) : ctx.moveTo(X(p[0]), Y(p[1])));
      ctx.closePath();
      if (s.f) { ctx.fillStyle = paint(s.f); ctx.fill(); }
      if (s.s) {
        ctx.strokeStyle = paint(s.s);
        ctx.lineWidth = Math.max(0.9, s.w * lineWidth / 1.6);
        ctx.stroke();
      }

    } else if (s.t === "c") {
      ctx.beginPath();
      ctx.arc(X(s.c[0]), Y(s.c[1]), Math.abs(s.r) * scale, 0, Math.PI * 2);
      if (s.f) { ctx.fillStyle = paint(s.f); ctx.fill(); }
      if (s.s) {
        ctx.strokeStyle = paint(s.s);
        ctx.lineWidth = Math.max(0.9, s.w * lineWidth / 1.6);
        ctx.stroke();
      }

    } else if (s.t === "o") {
      ctx.beginPath();
      ctx.arc(X(s.a[0]), Y(s.a[1]), Math.max(2.2, s.r * 0.85), 0, Math.PI * 2);
      ctx.fillStyle = paint(s.f);
      ctx.fill();

    } else if (s.t === "x") {
      const [align, baseline, dx, dy] = ANCHORS[s.n] || ANCHORS.c;
      ctx.save();
      ctx.font = `${s.i ? "italic " : ""}${Math.max(12, s.z * 0.92)}px "Latin Modern Math", "STIX Two Math", "Times New Roman", serif`;
      ctx.textAlign = align;
      ctx.textBaseline = baseline;
      ctx.fillStyle = paint(s.f);
      ctx.fillText(s.x, X(s.a[0]) + dx * LABEL_OFFSET, Y(s.a[1]) + dy * LABEL_OFFSET);
      ctx.restore();

    } else if (s.t === "a") {
      ctx.beginPath();
      /* 世界坐标 Y 向上、逆时针为正；画布 Y 向下，所以角度取负。 */
      ctx.arc(X(s.c[0]), Y(s.c[1]), Math.abs(s.r) * scale, -s.b, -(s.b + s.e), s.e > 0);
      ctx.strokeStyle = paint(s.s);
      ctx.lineWidth = Math.max(0.9, s.w * lineWidth / 1.6);
      ctx.stroke();
    }
  }
}

function buildCard(problem, index) {
  const card = document.createElement("article");
  card.className = "card";

  const title = document.createElement("h2");
  title.textContent = problem.title || problem.file;
  card.appendChild(title);

  const file = document.createElement("div");
  file.className = "file";
  file.textContent = problem.file;
  card.appendChild(file);

  if (problem.statement) {
    const statement = document.createElement("div");
    statement.className = "statement";
    statement.textContent = problem.statement;
    card.appendChild(statement);
  }

  const stage = document.createElement("div");
  stage.className = "stage";
  const canvas = document.createElement("canvas");
  stage.appendChild(canvas);

  const badge = document.createElement("span");
  badge.className = "badge";
  badge.textContent = t("ui.kind." + problem.kind);
  stage.appendChild(badge);
  card.appendChild(stage);

  let frame = 0;
  let playing = false;
  let raf = 0;
  const total = problem.frames.length;
  const animated = total > 1;

  const hint = document.createElement("div");
  hint.className = "hint";
  hint.textContent = animated ? t("ui.drag.hint") : "";
  card.appendChild(hint);

  const scrubRow = document.createElement("div");
  scrubRow.className = "scrub";

  const play = document.createElement("button");
  play.type = "button";
  play.className = "primary";
  play.textContent = t("ui.play");
  play.disabled = !animated;

  const range = document.createElement("input");
  range.type = "range";
  range.min = "0";
  range.max = String(Math.max(total - 1, 0));
  range.value = "0";
  range.disabled = !animated;

  const counter = document.createElement("span");
  counter.className = "counter";

  scrubRow.append(play, range, counter);
  card.appendChild(scrubRow);

  function update() {
    counter.textContent = animated
      ? t("ui.frame").replace("{0}", frame + 1).replace("{1}", total)
      : "";
    range.value = String(frame);
    render(canvas, problem, frame);
  }

  function setFrame(value) {
    frame = Math.max(0, Math.min(total - 1, value));
    update();
  }

  /* ——— 纯触摸：在图上左右拖动就是擦洗。不依赖滚轮、右键、快捷键。 ——— */
  let dragging = false;
  let startX = 0;
  let startFrame = 0;

  canvas.addEventListener("pointerdown", event => {
    if (!animated) return;
    dragging = true;
    startX = event.clientX;
    startFrame = frame;
    playing = false;
    play.textContent = t("ui.play");
    cancelAnimationFrame(raf);
    canvas.setPointerCapture(event.pointerId);
    event.preventDefault();
  });

  canvas.addEventListener("pointermove", event => {
    if (!dragging) return;
    const span = Math.max(canvas.clientWidth * 0.8, 120);
    const delta = (event.clientX - startX) / span * (total - 1);
    setFrame(Math.round(startFrame + delta));
    event.preventDefault();
  });

  const stopDrag = () => { dragging = false; };
  canvas.addEventListener("pointerup", stopDrag);
  canvas.addEventListener("pointercancel", stopDrag);

  play.addEventListener("click", () => {
    if (!animated) return;
    playing = !playing;
    play.textContent = playing ? t("ui.pause") : t("ui.play");
    if (playing) tick();
    else cancelAnimationFrame(raf);
  });

  range.addEventListener("input", () => setFrame(Number(range.value)));

  let last = 0;
  function tick(now) {
    if (!playing) return;
    if (!last) last = now || performance.now();
    const elapsed = (now || performance.now()) - last;
    const stepMs = 1000 / 24;
    if (elapsed >= stepMs) {
      last = now || performance.now();
      setFrame(frame + 1 >= total ? 0 : frame + 1);
    }
    raf = requestAnimationFrame(tick);
  }

  if (problem.answer && (problem.answer.value || (problem.answer.steps || []).length)) {
    const box = document.createElement("div");
    box.className = "answer";
    if (problem.answer.value) {
      const value = document.createElement("span");
      value.className = "value";
      value.textContent = problem.answer.value;
      box.appendChild(value);
    }
    if ((problem.answer.steps || []).length) {
      const list = document.createElement("ol");
      problem.answer.steps.forEach(step => {
        const item = document.createElement("li");
        item.textContent = step;
        list.appendChild(item);
      });
      box.appendChild(list);
    }
    card.appendChild(box);
  }

  (problem.diagnostics || []).forEach(diagnostic => {
    const box = document.createElement("div");
    box.className = "diag " + diagnostic.severity;
    box.textContent = diagnostic.text;
    card.appendChild(box);
  });

  if (animated && !window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    play.textContent = t("ui.play");
  }

  function refresh() {
    badge.textContent = t("ui.kind." + problem.kind);
    hint.textContent = animated ? t("ui.drag.hint") : "";
    play.textContent = playing ? t("ui.pause") : t("ui.play");
    update();
  }

  return { card, update, refresh };
}

const rendered = PROBLEMS.map(buildCard);
const grid = document.getElementById("grid");
rendered.forEach(item => grid.appendChild(item.card));

/* 主题切换：只换色，不重建 DOM，也不重存帧。 */
const themeButton = document.getElementById("theme");
function applyTheme(theme) {
  document.documentElement.dataset.theme = theme;
  themeButton.textContent = theme === "dark" ? t("ui.theme.dark") : t("ui.theme.light");
  rendered.forEach(item => item.update());
}
themeButton.addEventListener("click", () => {
  const next = isDark() ? "light" : "dark";
  try { localStorage.setItem("mathgeo.theme", next); } catch (e) { /* 隐私模式忽略 */ }
  applyTheme(next);
});

let initial = "light";
try { initial = localStorage.getItem("mathgeo.theme") || "light"; } catch (e) { /* 同上 */ }
if (initial === "dark" || (!initial && window.matchMedia("(prefers-color-scheme: dark)").matches)) {
  initial = "dark";
}
applyTheme(initial);

/* 语言切换在前端完成：不重新加载、不重新渲染图形，只换界面文案。
   题目内容是出题时的原文，不跟着界面语言变 —— 它是内容，不是界面。 */
const langSelect = document.getElementById("lang");
Object.keys(LOCALES).forEach(code => {
  const option = document.createElement("option");
  option.value = code;
  option.textContent = LOCALES[code]["_name"] || code;
  option.selected = code === LANG;
  langSelect.appendChild(option);
});
langSelect.addEventListener("change", () => {
  LANG = langSelect.value;
  try { localStorage.setItem("mathgeo.lang", LANG); } catch (e) { /* 隐私模式忽略 */ }
  applyLanguage();
});

/* 阿拉伯语和希伯来语是 RTL。注意 RTL 只翻转界面方向：
   图形和数学式必须保持 LTR —— ∠A = 60° 被镜像就是错的。 */
function applyLanguage() {
  const meta = LOCALES[LANG] || {};
  document.documentElement.lang = LANG;
  document.documentElement.dir = meta._rtl ? "rtl" : "ltr";
  themeButton.textContent = isDark() ? t("ui.theme.dark") : t("ui.theme.light");
  langSelect.setAttribute("aria-label", t("ui.language"));
  document.querySelector("h1").firstChild.textContent = t("app.name");
  document.querySelector("h1 small").textContent = t("app.tagline");
  rendered.forEach(item => item.refresh());
}

/* 光标只服务鼠标；触摸设备上根本不会触发，所以它是纯加分项。
   八个光标以 CSS 变量的形式挂到根元素上，工具面板直接用 var(--cursor-point) 即可。 */
for (const [role, css] of Object.entries(CURSORS)) {
  document.documentElement.style.setProperty("--cursor-" + role, css);
}

window.addEventListener("resize", () => rendered.forEach(item => item.update()));
applyLanguage();
</script>
</body>
</html>
""";
}
