# 系統介面改版 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `wwwroot` 前端改成《我獨自升級》的「系統視窗」視覺語言（A2 影之君主），並產出獎勵系統要沿用的共用元件（`window.UI`、`NAV_ITEMS`、`announce`），不動任何後端。

**Architecture:** `app.css` 以 `:root` 設計代號＋`<html data-accent>` 切換三套色票；新增 `ui.js` 以 `window.UI` 匯出視窗、狀態面板、系統訊息佇列、錯誤 toast、二段式確認與主題切換；`app.js` 保留 hash 路由與各畫面，改用 `UI` 元件，並以 `announce()` 比對前後 `/me`、`/today` 送出系統訊息。遷移期間舊畫面靠 `app.css` 尾端的 `legacy` 區塊維持可用，每個畫面改版時刪除對應段落，每個 Task 結束時 App 都完整可操作。

**Tech Stack:** 純 HTML＋原生 JS（無建置、無框架）、CSS 自訂屬性與 `color-mix()`、Google Fonts（Noto Serif TC／Noto Sans TC／IBM Plex Mono／Chakra Petch）；驗證用 `dotnet build`／`dotnet test`（207 個既有測試）、`node --check` 與 `docker compose` 手動清單。

**Spec:** `docs/superpowers/specs/2026-10-01-system-ui-redesign-design.md`（與獎勵系統共用的前端約定：`.superpowers/sdd/plan-drafting/frontend-contract.md`）

## Global Constraints

- 只動 `src/SoloLeveling.Api/wwwroot/index.html`、`app.css`、`app.js`，新增 `wwwroot/ui.js`；文件只改 `docs/ARCHITECTURE.md`、`CLAUDE.md`。不改任何 API、後端程式與測試。
- 仍是純 HTML＋JS，無建置步驟；不引入前端框架或 npm 套件；外部資源只有 Google Fonts。
- 前端不計算 EXP、等級、階段，一律用 API 回傳值（EXP 條寬度只是 `xp / xpNeeded` 的顯示換算）。
- 設計代號全部定義在 `app.css`；色碼與 `rgba()` **只能出現在** `/* tokens:start */`～`/* tokens:end */` 之間；`app.js`、`ui.js`、`index.html` 不得出現任何色碼。
- 主題：`<html data-accent="azure|violet|jade">`，預設 `azure`（藍光）；只覆寫色彩代號。
- 色票（spec 原值）：
  - `--bg`：azure `#050912`／violet `#07060b`／jade `#050c0a`
  - `--panel`：`rgba(14,26,52,.66)`／`rgba(28,20,44,.66)`／`rgba(14,38,32,.64)`
  - `--panel-solid`：`#0b1630`／`#140f22`／`#0b1f1a`
  - `--ink`：`#e6f1ff`／`#efe9ff`／`#e4fbf3`
  - `--muted`：`#8aa4c8`／`#a497c4`／`#8fb8aa`
  - `--accent`：`#5cb6ff`／`#a585ff`／`#4fdcaa`
  - `--accent-ink`：`#031024`／`#12082a`／`#02150f`
  - `--line`／`--line-soft`：主色 50%／16% 透明
  - `--ok`：`#6ee7c8`／`#6ee7c8`／`#c9ec7a`；`--warn`：`#f5c56b`；`--bad`：`#ff6b7a`
- 字體：`--serif` Noto Serif TC（視窗標題、系統訊息、稱號）、`--sans` Noto Sans TC（內文）、`--mono` IBM Plex Mono（數字，`font-variant-numeric: tabular-nums`）、`--latin` Chakra Petch（英文標籤）；皆附系統後備字。
- 系統訊息 `UI.sysMessage(lines)`：頂部實心視窗、`[ SYSTEM ]` 標頭、宋體多行、3.2 秒自動消失、點擊立即關閉、多則排隊。錯誤仍用 toast，配色用 `--bad`。
- 觸控：可點擊元素至少 40×40px；`touch-action: manipulation`；safe-area 內距補齊 Header、導覽與系統訊息。
- `prefers-reduced-motion: reduce` 時關閉光暈呼吸、閃爍與訊息位移動畫。
- 375px 寬無橫向捲動；`app.js`、`ui.js` 各不超過約 700 行。
- 與獎勵系統的約定名稱（必須一字不差）：`window.UI` 的 `h`、`sysMessage`、`toastError`、`win`、`statusPanel`、`confirmButton`、`setAccent`；CSS 類別 `.win`、`.win-title`、`.chip`、`.btn`、`.btn-primary`、`.btn-ghost`、`.btn-danger`、`.grid-cards`、`.rarity-E`／`.rarity-C`／`.rarity-A`／`.rarity-S`；代號 `--rarity-e`／`--rarity-c`／`--rarity-a`／`--rarity-s`；`app.js` 的 `NAV_ITEMS = [{ route, zh, en }]` 與 `announce(prevMe, prevToday, me, today, rewards)`；`index.html` 依序載入 `ui.js` 再 `app.js`。
- JS 風格：控制流一律加大括號；`catch` 只處理已知錯誤（`UI.ApiError`、`fetch` 網路層的 `TypeError`、`JSON.parse` 的 `SyntaxError`），其他照常往上丟。
- 註解與介面文案用繁體中文全形標點；檔案一律 LF、無 BOM；修改既有檔案用 Edit 工具（整段替換時 old_string 取 Read 到的原文）。
- 直接在 `main` commit（本專案指示），不 push；commit 格式 `type: 主旨`（≤50 字），body 數字條列，結尾兩行 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`、`Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4`。
- 既有 207 個測試（Domain 142＋Api 65）每個 Task 結束時都要全綠。

## Review Focus

1. **連點任務控制項**（Check 連點兩下、Count「＋」連按三下、網路慢）：同一時間只會送一個進度請求，數值不會因用舊值計算而少算，同一任務最多一則「完成」訊息。→ Task 4 Step 6 的「連點」檢查。
2. **任務名稱含 HTML 特殊字元**（`<img src=x onerror=alert(1)> & "引號"`）：今日列、系統訊息、最近 7 天表、設定清單都照字面顯示，不執行、不跑版。→ Task 2 Step 6（系統訊息逸出）與 Task 4 Step 6（今日列與宣告）、Task 5 Step 6（7 天表）。
3. **路由邊角**：未登入直接開 `/#today` 再登入（hash 已等於導向目標，設定 `location.hash` 不會觸發 `hashchange`）必須離開登入頁；網路慢時快速連點「進度」再點「設定」，最後畫面必須是設定頁。→ Task 3 Step 7。
4. **升級／升階快取**（`localStorage` 的 `seen:<userId>`）：同一瀏覽器換帳號不會拿到上一個帳號的等級而誤報升級；快取內容損毀時畫面照常、不丟例外；第一次登入不宣告任何事。→ Task 3 Step 7。
5. **375px 最長內容**（60 字無空白的任務名稱、帶單位的 Count 任務、7 天表、66 格日曆、展開的任務編輯表單、確認中的「再按一次確認封存」按鈕）：每個畫面 `scrollWidth <= innerWidth`，且所有按鈕與輸入框至少 40×40。→ Task 7 Step 3。

---

## 檔案結構

| 檔案 | 責任 |
| --- | --- |
| `wwwroot/index.html` | 外框：`<html data-accent>`、字體、`#header`（狀態面板）、`#view`、`#nav`（由 `NAV_ITEMS` 產生）；依序載入 `ui.js`、`app.js` |
| `wwwroot/app.css` | tokens（唯一可寫色碼處）→ 尺寸 → 基底 → 版面 → 共用元件 → 各畫面區段 → `legacy` 過渡區塊（Task 6 刪除）→ 響應式與減少動態 |
| `wwwroot/ui.js` | `window.UI`：`ApiError`、`h`、`setAccent`、`sysMessage`、`toastError`、`win`、`statusPanel`、`confirmButton`；不碰 API、不碰路由 |
| `wwwroot/app.js` | 區段依序為 helpers、shell（導覽、狀態面板、`announce`、`seen` 快取、`loadToday`）、auth、today、onboarding、progress、settings、router |

---

### Task 1: 設計代號、三套主題色、字體與共用樣式

**Files:**
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（整份改寫）
- Modify: `src/SoloLeveling.Api/wwwroot/index.html:2`、`:8`

**Interfaces:**
- Consumes: 無（舊 `app.js` 的標記由 `legacy` 區塊撐住）。
- Produces:
  - 色彩代號：`--bg`、`--panel`、`--panel-solid`、`--ink`、`--muted`、`--accent`、`--accent-ink`、`--line`、`--line-soft`、`--edge-hi`、`--edge-lo`、`--glow`、`--fx-hi`、`--fx-lo`、`--ok`、`--warn`、`--bad`、`--shadow`、`--rarity-e/c/a/s`
  - 字體代號：`--serif`、`--sans`、`--mono`、`--latin`
  - 尺寸代號：`--tap`（40px）、`--gutter`、`--nav-h`、`--safe-top/right/bottom/left`
  - 類別：`.hidden`、`.sub`、`.num`、`.sr-only`、`.header`、`.view`、`.win`、`.win-title`、`.btn`、`.btn-primary`、`.btn-ghost`、`.btn-danger`、`.btn-block`、`.btn[data-armed="1"]`、`.chips`、`.chip`、`.chip-dot`（`.warn`／`.ok`／`.bad`）、`.field`、`.form-row`、`.actions`、`.grid-cards`、`.rarity-E/-C/-A/-S`
  - 標記：`/* tokens:start */`、`/* tokens:end */`、`/* legacy:start */`、`/* legacy: <段名> */`、`/* legacy:end */`；之後各 Task 的新區段一律插在 `/* legacy:start` 那一行之前。

- [ ] **Step 1: 確認起點全綠**

Run: `dotnet build` 然後 `dotnet test`
Expected: 建置成功、0 警告；測試合計 207 個、Failed 0。

- [ ] **Step 2: 改寫 `app.css`**

先 Read `src/SoloLeveling.Api/wwwroot/app.css`，再用 Edit 把全檔內容（old_string 為 Read 到的整份原文）替換為：

```css
/* SoloLeveling 系統介面樣式。色碼只能寫在 tokens 區塊，主題由 <html data-accent="azure|violet|jade"> 切換。 */

/* tokens:start */
:root {
  color-scheme: dark;
  --serif: "Noto Serif TC", "Songti TC", "PMingLiU", serif;
  --sans: "Noto Sans TC", "PingFang TC", "Microsoft JhengHei", system-ui, sans-serif;
  --mono: "IBM Plex Mono", ui-monospace, "SFMono-Regular", Consolas, monospace;
  --latin: "Chakra Petch", "IBM Plex Mono", ui-monospace, monospace;
  --warn: #f5c56b;
  --bad: #ff6b7a;
  --shadow: rgba(0, 0, 0, .5);
  /* 稀有度色不隨主題變化 */
  --rarity-e: #a3adbd;
  --rarity-c: #6fcf8e;
  --rarity-a: #c58bff;
  --rarity-s: #ffc95c;
}

:root,
:root[data-accent="azure"] {
  --bg: #050912;
  --panel: rgba(14, 26, 52, .66);
  --panel-solid: #0b1630;
  --ink: #e6f1ff;
  --muted: #8aa4c8;
  --accent: #5cb6ff;
  --accent-ink: #031024;
  --line: rgba(92, 182, 255, .5);
  --line-soft: rgba(92, 182, 255, .16);
  --edge-hi: rgba(130, 196, 255, .85);
  --edge-lo: rgba(30, 90, 200, .15);
  --glow: 0 0 22px rgba(60, 150, 255, .35), inset 0 0 26px rgba(30, 90, 200, .16);
  --fx-hi: rgba(30, 100, 220, .35);
  --fx-lo: rgba(10, 30, 90, .55);
  --ok: #6ee7c8;
}

:root[data-accent="violet"] {
  --bg: #07060b;
  --panel: rgba(28, 20, 44, .66);
  --panel-solid: #140f22;
  --ink: #efe9ff;
  --muted: #a497c4;
  --accent: #a585ff;
  --accent-ink: #12082a;
  --line: rgba(165, 133, 255, .5);
  --line-soft: rgba(165, 133, 255, .16);
  --edge-hi: rgba(190, 160, 255, .8);
  --edge-lo: rgba(90, 60, 200, .15);
  --glow: 0 0 22px rgba(124, 82, 255, .35), inset 0 0 26px rgba(90, 50, 200, .16);
  --fx-hi: rgba(88, 52, 190, .35);
  --fx-lo: rgba(40, 20, 90, .5);
  --ok: #6ee7c8;
}

:root[data-accent="jade"] {
  --bg: #050c0a;
  --panel: rgba(14, 38, 32, .64);
  --panel-solid: #0b1f1a;
  --ink: #e4fbf3;
  --muted: #8fb8aa;
  --accent: #4fdcaa;
  --accent-ink: #02150f;
  --line: rgba(79, 220, 170, .5);
  --line-soft: rgba(79, 220, 170, .16);
  --edge-hi: rgba(130, 240, 200, .8);
  --edge-lo: rgba(20, 120, 90, .15);
  --glow: 0 0 22px rgba(40, 200, 150, .3), inset 0 0 26px rgba(20, 130, 100, .16);
  --fx-hi: rgba(26, 150, 110, .3);
  --fx-lo: rgba(8, 60, 46, .55);
  --ok: #c9ec7a;
}
/* tokens:end */

/* ---- 尺寸 ---- */
:root {
  --tap: 40px;
  --gutter: 16px;
  --nav-h: 58px;
  --safe-top: env(safe-area-inset-top, 0px);
  --safe-right: env(safe-area-inset-right, 0px);
  --safe-bottom: env(safe-area-inset-bottom, 0px);
  --safe-left: env(safe-area-inset-left, 0px);
}

/* ---- 基底 ---- */
*, *::before, *::after { box-sizing: border-box; }

html {
  background: var(--bg);
  -webkit-text-size-adjust: 100%;
}

body {
  margin: 0;
  min-height: 100dvh;
  display: flex;
  flex-direction: column;
  color: var(--ink);
  font: 15px/1.5 var(--sans);
  touch-action: manipulation;
  -webkit-tap-highlight-color: transparent;
}

/* 背景光源固定在視窗上，不隨內容捲動；body 本身不設底色，否則會蓋住這一層 */
body::before {
  content: "";
  position: fixed;
  inset: 0;
  z-index: -1;
  pointer-events: none;
  background:
    radial-gradient(90% 50% at 15% 0%, var(--fx-hi), transparent 65%),
    radial-gradient(80% 40% at 100% 100%, var(--fx-lo), transparent 70%),
    var(--bg);
}

.hidden { display: none !important; }
h1, h2, h3, p { margin: 0; }
button, input, select, textarea { font: inherit; color: inherit; }
a, button, input, select, textarea, label { touch-action: manipulation; }
:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.sub { display: block; color: var(--muted); font-size: 12px; }
.num { font-family: var(--mono); font-variant-numeric: tabular-nums; }
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}

/* ---- 版面 ---- */
.header {
  width: 100%;
  max-width: 640px;
  margin: 0 auto;
  padding: calc(12px + var(--safe-top)) calc(var(--gutter) + var(--safe-right)) 0 calc(var(--gutter) + var(--safe-left));
}

.view {
  flex: 1;
  width: 100%;
  max-width: 640px;
  margin: 0 auto;
  display: grid;
  gap: 14px;
  align-content: start;
  padding: 14px calc(var(--gutter) + var(--safe-right)) calc(var(--nav-h) + var(--safe-bottom) + 24px) calc(var(--gutter) + var(--safe-left));
}
.view > * { min-width: 0; }
.header.hidden + .view { padding-top: calc(24px + var(--safe-top)); }

/* ---- 系統視窗 ---- */
.win {
  position: relative;
  min-width: 0;
  padding: 12px 14px;
  background: var(--panel);
  border: 1px solid;
  border-image: linear-gradient(160deg, var(--edge-hi), var(--edge-lo) 60%, var(--edge-hi)) 1;
  box-shadow: var(--glow);
  -webkit-backdrop-filter: blur(6px);
  backdrop-filter: blur(6px);
}

.win-title {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: -2px 0 10px;
  color: var(--ink);
  font: 900 15px/1.4 var(--serif);
  letter-spacing: .3em;
}
.win-title > span { padding-left: .3em; }
.win-title::before,
.win-title::after {
  content: "";
  flex: 1;
  height: 1px;
  background: linear-gradient(90deg, transparent, var(--line));
}
.win-title::after { transform: scaleX(-1); }

/* ---- 按鈕 ---- */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-width: var(--tap);
  min-height: var(--tap);
  padding: 0 14px;
  border: 1px solid var(--line);
  border-radius: 0;
  background: transparent;
  color: var(--ink);
  font: 600 14px var(--sans);
  letter-spacing: .08em;
  text-decoration: none;
  cursor: pointer;
}
.btn:disabled { opacity: .5; cursor: default; }
.btn-primary {
  background: var(--accent);
  border-color: var(--accent);
  color: var(--accent-ink);
  font-family: "Chakra Petch", var(--sans);
  letter-spacing: .16em;
  text-transform: uppercase;
  box-shadow: 0 0 14px color-mix(in srgb, var(--accent) 45%, transparent);
}
.btn-ghost { border-color: var(--line-soft); color: var(--muted); }
.btn-danger { border-color: var(--bad); color: var(--bad); }
.btn-block { width: 100%; }
.btn[data-armed="1"] { border-color: var(--warn); color: var(--warn); background: transparent; box-shadow: none; }

@media (hover: hover) {
  .btn:hover:not(:disabled) { border-color: var(--accent); }
  .btn-ghost:hover:not(:disabled) { color: var(--ink); }
  .btn-danger:hover:not(:disabled) { border-color: var(--bad); }
  .btn-primary:hover:not(:disabled) { filter: brightness(1.08); }
}

/* ---- 數據籤 ---- */
.chips { display: flex; flex-wrap: wrap; gap: 6px; }
.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 3px 8px;
  border: 1px solid var(--line-soft);
  color: var(--muted);
  font-size: 12px;
}
.chip b { color: var(--ink); font: 600 12px var(--mono); font-variant-numeric: tabular-nums; }
.chip-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--accent); box-shadow: 0 0 8px var(--accent); }
.chip-dot.warn { background: var(--warn); box-shadow: 0 0 8px var(--warn); }
.chip-dot.ok { background: var(--ok); box-shadow: 0 0 8px var(--ok); }
.chip-dot.bad { background: var(--bad); box-shadow: 0 0 8px var(--bad); }

/* ---- 表單 ---- */
input[type="text"], input[type="email"], input[type="password"], input[type="number"], input[type="time"], select, textarea {
  width: 100%;
  min-height: var(--tap);
  padding: 8px 10px;
  border: 1px solid var(--line-soft);
  border-radius: 0;
  background: color-mix(in srgb, var(--bg) 72%, transparent);
  color: var(--ink);
  font-size: 16px;
}
textarea { resize: vertical; }
input:focus, select:focus, textarea:focus { outline: none; border-color: var(--accent); box-shadow: 0 0 0 1px var(--accent); }
input:disabled, select:disabled { opacity: .45; }
input[type="checkbox"], input[type="radio"] { width: 20px; height: 20px; accent-color: var(--accent); }
.field { display: grid; gap: 4px; margin: 10px 0; color: var(--muted); font-size: 13px; }
.field > input, .field > select, .field > textarea { color: var(--ink); }
.form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
.form-row > * { min-width: 0; }
.actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; margin-top: 12px; }

/* ---- 卡片網格與稀有度 ---- */
.grid-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(96px, 1fr)); gap: 10px; }
.rarity-E { --rarity: var(--rarity-e); }
.rarity-C { --rarity: var(--rarity-c); }
.rarity-A { --rarity: var(--rarity-a); }
.rarity-S { --rarity: var(--rarity-s); }
.rarity-E, .rarity-C, .rarity-A, .rarity-S { color: var(--rarity); border-color: var(--rarity); }

/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */

/* legacy: shared */
.card { padding: 14px; background: var(--panel); border: 1px solid var(--line-soft); }
.card h2 { margin: 0 0 10px; color: var(--ink); font: 900 15px var(--serif); letter-spacing: .3em; }
.section-title { margin: 18px 0 6px; color: var(--muted); font-size: 13px; letter-spacing: .08em; }
button.small, button.primary, .tabs button { min-height: 32px; padding: 4px 12px; border: 1px solid var(--line); background: transparent; color: var(--ink); cursor: pointer; }
button.primary, .tabs button.active { background: var(--accent); border-color: var(--accent); color: var(--accent-ink); }
button.danger { border-color: var(--bad); color: var(--bad); }
button.small:disabled, button.primary:disabled { opacity: .5; cursor: default; }
.badge { margin-left: 4px; padding: 2px 6px; border: 1px solid var(--line-soft); color: var(--muted); font-size: 11px; }
.quest { display: flex; align-items: center; gap: 12px; padding: 10px 0; border-bottom: 1px dashed var(--line-soft); }
.quest:last-child { border-bottom: 0; }
.quest .name { flex: 1; min-width: 0; }

/* legacy: header */
.header .row { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; }
.header .level { color: var(--accent); font: 700 26px var(--latin); }
.header .rank { color: var(--accent); font-weight: 600; }
.header .title { color: var(--muted); font-size: 14px; }
.header .meta { display: flex; flex-wrap: wrap; gap: 14px; margin-top: 6px; color: var(--muted); font-size: 14px; }
.header .meta b { color: var(--ink); }
.xpbar { height: 6px; margin-top: 8px; overflow: hidden; background: var(--line-soft); }
.xpbar > i { display: block; height: 100%; background: var(--accent); }

/* legacy: auth */
.auth { width: 100%; max-width: 380px; margin: 24px auto; }
.auth h1 { margin: 0 0 4px; font: 900 26px var(--serif); }
.auth p { margin: 0 0 20px; color: var(--muted); }
.auth .tabs { display: flex; gap: 8px; margin-bottom: 14px; }
.auth .tabs button { flex: 1; }

/* legacy: nav */
.nav { position: fixed; left: 0; right: 0; bottom: 0; display: flex; padding-bottom: var(--safe-bottom); background: var(--panel-solid); border-top: 1px solid var(--line-soft); }
.nav a { flex: 1; padding: 14px 0; color: var(--muted); text-align: center; text-decoration: none; font-weight: 600; }
.nav a.active { color: var(--accent); }

/* legacy: toast */
.toast { position: fixed; left: 50%; bottom: 84px; z-index: 10; max-width: 90vw; padding: 10px 14px; transform: translateX(-50%); background: var(--panel-solid); border: 1px solid var(--bad); color: var(--ink); }
.toast.ok { border-color: var(--ok); }

/* legacy: today */
.quest.done .name { color: var(--muted); text-decoration: line-through; }
.quest .ctl { display: flex; align-items: center; gap: 6px; white-space: nowrap; }
.quest .value { min-width: 64px; text-align: center; font-family: var(--mono); }
.check { width: 22px; height: 22px; }
.summary { display: flex; justify-content: space-between; align-items: center; }
.summary .ratio { font: 700 22px var(--mono); }
.summary .ratio.cleared { color: var(--ok); }
input[type="number"].inline { width: 88px; text-align: right; }
.stage { display: inline-block; margin-left: 6px; color: var(--accent); font-size: 11px; }

/* legacy: progress */
.grid66 { display: grid; grid-template-columns: repeat(11, 1fr); gap: 5px; }
.grid66 > i { display: flex; align-items: center; justify-content: center; aspect-ratio: 1; border: 1px solid var(--line-soft); color: var(--muted); font-size: 10px; font-style: normal; }
.grid66 > i.cleared { background: var(--accent); color: var(--accent-ink); }
.grid66 > i.missed { border-color: var(--line); }
.grid66 > i.today { outline: 2px solid var(--accent); outline-offset: -2px; color: var(--ink); }
.grid66 > i.future { opacity: .45; }
.stats { display: grid; grid-template-columns: repeat(5, 1fr); gap: 8px; text-align: center; }
.stats > div { padding: 8px 4px; border: 1px solid var(--line-soft); }
.stats b { display: block; font: 600 20px var(--mono); }
.stats span { color: var(--muted); font-size: 12px; }
.dots { width: 100%; border-collapse: collapse; font-size: 13px; }
.dots th { padding: 4px 2px; color: var(--muted); font-size: 11px; font-weight: 500; text-align: center; }
.dots td { padding: 6px 2px; border-top: 1px dashed var(--line-soft); text-align: center; }
.dots td.name { max-width: 160px; overflow: hidden; text-align: left; text-overflow: ellipsis; white-space: nowrap; }
.dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; border: 1px solid var(--line-soft); }
.dot.on { background: var(--ok); border-color: var(--ok); }

/* legacy: settings */
.switch { display: flex; align-items: center; justify-content: space-between; }
.switch input { width: 44px; height: 24px; }
.qrow { display: flex; align-items: center; gap: 8px; padding: 8px 0; border-bottom: 1px dashed var(--line-soft); }
.qrow:last-child { border-bottom: 0; }
.qrow .name { flex: 1; min-width: 0; }
.goal { display: flex; justify-content: space-between; align-items: flex-start; gap: 8px; padding: 8px 0; border-bottom: 1px dashed var(--line-soft); }

/* legacy: onboarding */
.choices { display: flex; flex-direction: column; gap: 8px; margin: 12px 0; }
.choice { display: flex; align-items: flex-start; gap: 10px; padding: 10px; border: 1px solid var(--line-soft); cursor: pointer; }
.choice input { margin-top: 3px; }
/* legacy:end */
```

- [ ] **Step 3: `index.html` 加上預設主題與字體**

Edit `src/SoloLeveling.Api/wwwroot/index.html`：

old_string：
```html
<html lang="zh-Hant">
```
new_string：
```html
<html lang="zh-Hant" data-accent="azure">
```

old_string：
```html
  <link rel="stylesheet" href="app.css">
```
new_string：
```html
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Chakra+Petch:wght@500;600;700&family=IBM+Plex+Mono:wght@400;500;600&family=Noto+Sans+TC:wght@400;500;700&family=Noto+Serif+TC:wght@600;900&display=swap">
  <link rel="stylesheet" href="app.css">
```

- [ ] **Step 4: 檢查色碼只在 tokens 區塊**

Run（Git Bash，repo 根目錄）：
```bash
awk '/tokens:start/{t=1} /tokens:end/{t=0; next} !t && /#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(|hsla?\(/ {print FNR": "$0}' src/SoloLeveling.Api/wwwroot/app.css
grep -nE "#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(" src/SoloLeveling.Api/wwwroot/index.html
```
Expected: 兩個指令都沒有任何輸出。

- [ ] **Step 5: 建置與測試**

Run: `dotnet build` 然後 `dotnet test`
Expected: 建置成功、0 警告；207 個測試、Failed 0。

- [ ] **Step 6: 手動檢查**

Run: `docker compose up -d --build`（根目錄 `.env` 需已有 `JwtSecret`），瀏覽器開 http://localhost:8080/ 並打開 DevTools。

1. 登入頁：背景是近黑的深藍，左上與右下各有一團藍色霧光，捲動時霧光不動；標題「SoloLeveling」為宋體。DevTools Network 篩 `font` 可看到 Noto／Chakra／Plex 字檔載入。
2. 註冊一個新帳號（例 `ui1@example.com`／`password123`／`測試玩家`），完成舊版引導（勾「閱讀」→ 填答 → 預覽 → 開始），進今日頁：舊版卡片換成半透明深藍底、細框；「開始」等主要按鈕是實心藍底深色字；勾選、累計、上限輸入、儲存反思都能照常操作。
3. Console 執行 `document.documentElement.dataset.accent = 'violet'`：底色、霧光、按鈕、框線全部變紫；改 `'jade'` 變翡翠綠；改回 `'azure'` 恢復藍。
4. 進度頁、設定頁都能正常顯示與操作（樣式為過渡版，沒有破版、沒有白底元素）。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Api/wwwroot/app.css src/SoloLeveling.Api/wwwroot/index.html
git ls-files --eol src/SoloLeveling.Api/wwwroot
git commit -m "feat: 新增系統介面設計代號、三套主題色與共用樣式" -m "1. 改版以設計代號統一著色，主題只需切換 data-accent，色碼集中在 tokens 區塊方便檢查
2. 先定義視窗、按鈕、數據籤、表單、卡片網格與稀有度等共用樣式，供後續畫面與獎勵系統沿用
3. 舊畫面樣式暫留在 legacy 區塊並改用代號著色，逐畫面改版時再刪除" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4"
```
Expected: `git ls-files --eol` 每一行開頭為 `i/lf`；commit 成功。

---

### Task 2: `ui.js` 共用元件與系統訊息佇列

**Files:**
- Create: `src/SoloLeveling.Api/wwwroot/ui.js`
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（新增狀態面板、系統訊息、toast 區段；刪除 `legacy: toast`）
- Modify: `src/SoloLeveling.Api/wwwroot/index.html`（移除 `#toast`，先載入 `ui.js`）
- Modify: `src/SoloLeveling.Api/wwwroot/app.js:12`、`:17`、`:26-32`

**Interfaces:**
- Consumes: Task 1 的代號與 `.win`、`.win-title`、`.chip`、`.chip-dot`、`.btn[data-armed="1"]`。
- Produces（`window.UI`，凍結物件）：
  - `UI.ApiError`：`class ApiError extends Error`，`constructor(message: string, status: number)`，屬性 `status`。
  - `UI.h(s: any): string`：HTML 逸出（`& < > " '`），`null`／`undefined` 回空字串。
  - `UI.setAccent(key: string): void`：設 `document.documentElement.dataset.accent`；不在 `azure|violet|jade` 內時用 `azure`。
  - `UI.sysMessage(lines: string[]): void`：排隊顯示；每則 3.2 秒或點擊關閉；待顯示最多保留 4 則（超過丟最舊）；內容以 `textContent` 寫入。
  - `UI.toastError(message: string): void`：紅色 toast，2.6 秒。
  - `UI.win({ title?: string, body?: string, cls?: string }): string`：`<section class="win …">`；`title` 會逸出，`body` 是原樣 HTML（呼叫端負責逸出）。
  - `UI.statusPanel({ me, today?: object|null, compact?: boolean }): string`：完整版含等級方塊、名稱、稱號、階級徽章、EXP 條、數據籤（連勝、最佳、今日達成率、`coins`、`shieldCount`、困難模式）；`compact: true` 只有等級、EXP 條、階級。
  - `UI.confirmButton(btn: HTMLButtonElement, label: string, action: () => Promise<void>): void`：第一下變「再按一次確認{label}」（`data-armed="1"`）3 秒後還原；第二下停用按鈕並執行 `action`，`UI.ApiError` 以 toast 顯示，其他例外往上丟。
  - CSS：`.status`、`.status.compact`、`.status-top`、`.status-bar`、`.lv`、`.who`、`.who-name`、`.who-title`、`.rank-badge`、`.xp-bar`、`.bar-row`、`.sysmsg-host`、`.sysmsg`、`.sysmsg.show`、`.sysmsg-head`、`.sysmsg-line`、`.toast`。

- [ ] **Step 1: 建立 `ui.js`**

Create `src/SoloLeveling.Api/wwwroot/ui.js`：

```js
/* SoloLeveling 共用介面元件：系統視窗、狀態面板、系統訊息、錯誤 toast、二段式確認與主題色。以 window.UI 匯出，供 app.js 與後續畫面共用；不呼叫 API、不處理路由。 */
(() => {
  const ACCENTS = ['azure', 'violet', 'jade'];
  const MESSAGE_MS = 3200;
  const LEAVE_MS = 250;
  const MAX_PENDING = 4;
  const TOAST_MS = 2600;
  const CONFIRM_MS = 3000;

  /** API 錯誤回應或網路失敗；message 可直接顯示給使用者。 */
  class ApiError extends Error {
    /**
     * @param {string} message 顯示給使用者的訊息
     * @param {number} status HTTP 狀態碼；網路失敗為 0
     */
    constructor(message, status) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
    }
  }

  /**
   * HTML 逸出，用於把任意值安全地放進 HTML 字串。
   * @param {*} s 任意值；null 與 undefined 視為空字串
   * @returns {string} 逸出後的字串
   */
  const h = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  /**
   * 切換主題色。
   * @param {string} key azure、violet 或 jade；其他值一律套用 azure
   * @returns {void}
   */
  function setAccent(key) {
    document.documentElement.dataset.accent = ACCENTS.includes(key) ? key : 'azure';
  }

  /* ---------- 系統訊息 ---------- */
  const pending = [];
  let host = null;
  let showing = false;

  function messageHost() {
    if (!host) {
      host = document.createElement('div');
      host.className = 'sysmsg-host';
      host.setAttribute('role', 'status');
      host.setAttribute('aria-live', 'polite');
      document.body.appendChild(host);
    }
    return host;
  }

  function showNext() {
    const lines = pending.shift();
    if (!lines) {
      showing = false;
      return;
    }
    showing = true;
    const el = document.createElement('div');
    el.className = 'sysmsg';
    const head = document.createElement('span');
    head.className = 'sysmsg-head';
    head.textContent = '[ SYSTEM ]';
    el.appendChild(head);
    lines.forEach((line) => {
      const span = document.createElement('span');
      span.className = 'sysmsg-line';
      span.textContent = line;
      el.appendChild(span);
    });
    let timer = null;
    let closed = false;
    const close = () => {
      if (closed) {
        return;
      }
      closed = true;
      clearTimeout(timer);
      el.classList.remove('show');
      // 以固定時間等退場動畫，減少動態設定關掉 transition 時也會照常換下一則
      setTimeout(() => {
        el.remove();
        showNext();
      }, LEAVE_MS);
    };
    el.addEventListener('click', close);
    messageHost().appendChild(el);
    // 先讓瀏覽器套用初始樣式，再加上 show 才會有進場動畫
    void el.offsetWidth;
    el.classList.add('show');
    timer = setTimeout(close, MESSAGE_MS);
  }

  /**
   * 排隊顯示一則系統訊息；每則 3.2 秒自動消失或點擊關閉，依序顯示。
   * 內容以純文字寫入，不解析 HTML。待顯示超過 4 則時丟掉最舊的，避免短時間大量觸發後訊息拖很久。
   * @param {string[]} lines 訊息的每一行；空陣列不顯示
   * @returns {void}
   */
  function sysMessage(lines) {
    const text = (Array.isArray(lines) ? lines : [lines]).map((line) => String(line ?? '')).filter((line) => line !== '');
    if (!text.length) {
      return;
    }
    pending.push(text);
    if (pending.length > MAX_PENDING) {
      pending.splice(0, pending.length - MAX_PENDING);
    }
    if (!showing) {
      showNext();
    }
  }

  /* ---------- 錯誤 toast ---------- */
  let toastEl = null;
  let toastTimer = null;

  /**
   * 顯示紅色錯誤 toast，2.6 秒後消失；連續呼叫時以最新訊息取代。
   * @param {string} message 錯誤訊息
   * @returns {void}
   */
  function toastError(message) {
    if (!toastEl) {
      toastEl = document.createElement('div');
      toastEl.className = 'toast hidden';
      toastEl.setAttribute('role', 'alert');
      document.body.appendChild(toastEl);
    }
    toastEl.textContent = String(message ?? '');
    toastEl.classList.remove('hidden');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toastEl.classList.add('hidden'), TOAST_MS);
  }

  /* ---------- 視窗與狀態面板 ---------- */

  /**
   * 產生系統視窗的 HTML。
   * @param {{ title?: string, body?: string, cls?: string }} options title 會逸出；body 原樣放入，呼叫端須自行逸出使用者資料；cls 為額外類別
   * @returns {string} 視窗 HTML
   */
  function win({ title = '', body = '', cls = '' } = {}) {
    const head = title ? `<h2 class="win-title"><span>${h(title)}</span></h2>` : '';
    return `<section class="win ${h(cls)}">${head}${body}</section>`;
  }

  /**
   * 產生狀態面板的 HTML。數值全部取自 API 回應，不自行計算等級或 EXP。
   * 完整版另顯示名稱、稱號與數據籤；me.player 帶有 coins 或 shieldCount（數字）時才顯示對應的籤。
   * @param {{ me: object, today?: object|null, compact?: boolean }} options me 為 GET /me 回應；today 為 GET /today 回應，有值時顯示今日達成率；compact 為 true 時只顯示等級、EXP 條與階級
   * @returns {string} 狀態面板 HTML
   */
  function statusPanel({ me, today = null, compact = false }) {
    const p = me.player;
    const pct = p.xpNeeded > 0 ? Math.min(100, Math.max(0, Math.round((p.xp / p.xpNeeded) * 100))) : 0;
    const lv = `<div class="lv"><small>LEVEL</small><b>${h(p.level)}</b></div>`;
    const rank = `<div class="rank-badge" role="img" aria-label="階級 ${h(p.rank)}">${h(p.rank)}</div>`;
    const bar = `
      <div class="xp-bar" role="img" aria-label="經驗值 ${h(p.xp)} / ${h(p.xpNeeded)}"><i style="width:${pct}%"></i></div>
      <div class="bar-row"><span>EXP ${h(p.xp)} / ${h(p.xpNeeded)}</span><span>${pct}%</span></div>`;
    if (compact) {
      return `<section class="win status compact"><div class="status-top">${lv}<div class="status-bar">${bar}</div>${rank}</div></section>`;
    }
    const chips = [
      `<span class="chip"><span class="chip-dot warn"></span>連勝 <b>${h(p.displayStreak)}</b> 天</span>`,
      `<span class="chip">最佳 <b>${h(p.bestStreak)}</b> 天</span>`,
    ];
    if (today) {
      const ratio = Math.round(today.completionRatio * 100);
      chips.push(`<span class="chip"><span class="chip-dot ${today.isCleared ? 'ok' : ''}"></span>今日 <b>${ratio}%</b>${today.isCleared ? ' 達標' : ''}</span>`);
    }
    if (typeof p.coins === 'number') {
      chips.push(`<span class="chip"><span class="chip-dot"></span>金幣 <b>${h(p.coins)}</b></span>`);
    }
    if (typeof p.shieldCount === 'number') {
      chips.push(`<span class="chip"><span class="chip-dot ok"></span>保險卡 <b>${h(p.shieldCount)}</b></span>`);
    }
    if (p.hardMode) {
      chips.push('<span class="chip"><span class="chip-dot bad"></span>困難模式</span>');
    }
    return `
      <section class="win status">
        <div class="status-top">
          ${lv}
          <div class="who">
            <span class="who-name">${h(me.user.displayName)}</span>
            ${p.title ? `<span class="who-title">稱號　<em>${h(p.title)}</em></span>` : ''}
          </div>
          ${rank}
        </div>
        ${bar}
        <div class="chips">${chips.join('')}</div>
      </section>`;
  }

  /* ---------- 二段式確認 ---------- */

  /**
   * 把按鈕綁成二段式確認：第一下改成「再按一次確認{label}」並在 3 秒後還原，第二下才執行 action。
   * 執行期間停用按鈕；action 丟出 UI.ApiError 時以紅色 toast 顯示，其他例外照常往上丟。
   * @param {HTMLButtonElement} btn 要綁定的按鈕，文字應為 label
   * @param {string} label 按鈕原本的文字
   * @param {() => Promise<void>} action 確認後要執行的動作
   * @returns {void}
   */
  function confirmButton(btn, label, action) {
    let timer = null;
    const disarm = () => {
      clearTimeout(timer);
      timer = null;
      btn.dataset.armed = '';
      btn.textContent = label;
    };
    btn.addEventListener('click', async () => {
      if (btn.dataset.armed !== '1') {
        btn.dataset.armed = '1';
        btn.textContent = `再按一次確認${label}`;
        timer = setTimeout(disarm, CONFIRM_MS);
        return;
      }
      disarm();
      btn.disabled = true;
      try {
        await action();
      } catch (err) {
        if (!(err instanceof ApiError)) {
          throw err;
        }
        toastError(err.message);
      } finally {
        btn.disabled = false;
      }
    });
  }

  window.UI = Object.freeze({ ApiError, h, setAccent, sysMessage, toastError, win, statusPanel, confirmButton });
})();
```

- [ ] **Step 2: 加入狀態面板、系統訊息與 toast 樣式**

Edit `src/SoloLeveling.Api/wwwroot/app.css`：

(a) 刪除 legacy toast 段。old_string：
```css
/* legacy: toast */
.toast { position: fixed; left: 50%; bottom: 84px; z-index: 10; max-width: 90vw; padding: 10px 14px; transform: translateX(-50%); background: var(--panel-solid); border: 1px solid var(--bad); color: var(--ink); }
.toast.ok { border-color: var(--ok); }

```
new_string：空字串。

(b) 在 `/* legacy:start` 那一行之前插入。old_string：
```css
/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */
```
new_string：
```css
/* ---- 狀態面板 ---- */
.status { display: grid; gap: 10px; }
.status-top { display: flex; align-items: center; gap: 12px; min-width: 0; }
.lv { display: grid; place-items: center; flex: none; min-width: 60px; padding: 4px 6px; border: 1px solid var(--line); }
.lv small { color: var(--muted); font: 600 10px var(--latin); letter-spacing: .2em; }
.lv b { color: var(--accent); font: 700 26px/1 var(--latin); text-shadow: 0 0 12px color-mix(in srgb, var(--accent) 50%, transparent); }
.status:not(.compact) .lv { animation: lv-breathe 4s ease-in-out infinite; }
@keyframes lv-breathe {
  0%, 100% { box-shadow: 0 0 0 transparent; }
  50% { box-shadow: 0 0 14px color-mix(in srgb, var(--accent) 45%, transparent); }
}
.who { display: grid; gap: 2px; min-width: 0; }
.who-name { font-size: 16px; font-weight: 700; overflow-wrap: anywhere; }
.who-title { color: var(--muted); font-size: 12px; }
.who-title em { color: var(--ink); font-family: var(--serif); font-style: normal; }
.rank-badge {
  display: grid;
  place-items: center;
  flex: none;
  width: 40px;
  height: 40px;
  margin-left: auto;
  border: 1px solid var(--line);
  border-radius: 50%;
  box-shadow: inset 0 0 12px var(--line-soft);
  font: 700 20px var(--latin);
}
.xp-bar { height: 6px; overflow: hidden; border-radius: 999px; background: var(--line-soft); }
.xp-bar > i {
  display: block;
  height: 100%;
  background: linear-gradient(90deg, color-mix(in srgb, var(--accent) 55%, transparent), var(--accent));
  box-shadow: 0 0 10px var(--accent);
  transition: width .4s ease;
}
.bar-row { display: flex; justify-content: space-between; color: var(--muted); font: 11px var(--mono); font-variant-numeric: tabular-nums; }
.status-bar { flex: 1; min-width: 0; display: grid; gap: 4px; }
.status.compact { padding: 8px 12px; }
.status.compact .lv { min-width: 52px; padding: 2px 4px; }
.status.compact .lv b { font-size: 20px; }
.status.compact .rank-badge { width: 34px; height: 34px; font-size: 16px; }

/* ---- 系統訊息 ---- */
.sysmsg-host {
  position: fixed;
  z-index: 30;
  top: calc(12px + var(--safe-top));
  left: calc(var(--gutter) + var(--safe-left));
  right: calc(var(--gutter) + var(--safe-right));
  max-width: 480px;
  margin: 0 auto;
  pointer-events: none;
}
.sysmsg {
  display: grid;
  gap: 2px;
  padding: 12px 14px;
  background: var(--panel-solid);
  border: 1px solid var(--accent);
  box-shadow: var(--glow), 0 12px 30px var(--shadow);
  color: var(--ink);
  font: 14px/1.7 var(--serif);
  cursor: pointer;
  pointer-events: auto;
  opacity: 0;
  transform: translateY(-8px);
  transition: opacity .25s ease, transform .25s ease;
}
.sysmsg.show { opacity: 1; transform: none; }
.sysmsg-head { color: var(--accent); font: 600 11px var(--latin); letter-spacing: .2em; }
.sysmsg-line { overflow-wrap: anywhere; }

/* ---- 錯誤 toast ---- */
.toast {
  position: fixed;
  z-index: 31;
  left: calc(var(--gutter) + var(--safe-left));
  right: calc(var(--gutter) + var(--safe-right));
  bottom: calc(var(--nav-h) + var(--safe-bottom) + 12px);
  max-width: 480px;
  margin: 0 auto;
  padding: 10px 14px;
  background: color-mix(in srgb, var(--bad) 16%, var(--panel-solid));
  border: 1px solid var(--bad);
  box-shadow: 0 0 18px color-mix(in srgb, var(--bad) 35%, transparent), 0 12px 30px var(--shadow);
  color: var(--ink);
  overflow-wrap: anywhere;
}

/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */
```

- [ ] **Step 3: `index.html` 先載入 `ui.js`，移除舊 toast 元素**

Edit `src/SoloLeveling.Api/wwwroot/index.html`，old_string：
```html
  <div id="toast" class="toast hidden"></div>
  <script src="app.js"></script>
```
new_string：
```html
  <script src="ui.js"></script>
  <script src="app.js"></script>
```

- [ ] **Step 4: 讓舊 `app.js` 改用 `UI`**

Edit `src/SoloLeveling.Api/wwwroot/app.js`：

(a) old_string：
```js
  const $nav = document.getElementById('nav');
  const $toast = document.getElementById('toast');
```
new_string：
```js
  const $nav = document.getElementById('nav');
```

(b) old_string：
```js
  const h = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
```
new_string：
```js
  const { h } = UI;
```

(c) old_string：
```js
  let toastTimer;
  function toast(msg, ok = false) {
    $toast.textContent = msg;
    $toast.className = 'toast' + (ok ? ' ok' : '');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => $toast.classList.add('hidden'), 2600);
  }
```
new_string：
```js
  // 成功訊息走系統訊息，錯誤走紅色 toast
  function toast(msg, ok = false) {
    if (ok) {
      UI.sysMessage([msg]);
    } else {
      UI.toastError(msg);
    }
  }
```

- [ ] **Step 5: 語法、色碼、建置與測試**

Run：
```bash
node --check src/SoloLeveling.Api/wwwroot/ui.js
node --check src/SoloLeveling.Api/wwwroot/app.js
awk '/tokens:start/{t=1} /tokens:end/{t=0; next} !t && /#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(|hsla?\(/ {print FNR": "$0}' src/SoloLeveling.Api/wwwroot/app.css
grep -nE "#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(" src/SoloLeveling.Api/wwwroot/ui.js src/SoloLeveling.Api/wwwroot/app.js src/SoloLeveling.Api/wwwroot/index.html
wc -l src/SoloLeveling.Api/wwwroot/ui.js
dotnet build
dotnet test
```
Expected: `node --check` 無輸出（exit 0）；awk 與 grep 無輸出；`ui.js` 約 230 行（≤ 700）；建置 0 警告；207 個測試、Failed 0。

- [ ] **Step 6: 手動檢查（含 Review Focus 2：系統訊息逸出）**

Run: `docker compose up -d --build`，開 http://localhost:8080/ 並以 Task 1 的帳號登入。

1. 今日頁按「儲存」反思：畫面頂部滑入實心深藍視窗，第一行藍色「[ SYSTEM ]」、第二行宋體「已儲存」，約 3 秒後淡出上移。
2. 登出後用錯誤密碼登入：底部（導覽列上方位置）出現紅框、偏紅底的 toast 顯示錯誤訊息，約 2.6 秒消失。
3. Console 執行 `UI.sysMessage(['第一則', '第二行']); UI.sysMessage(['第二則']); UI.sysMessage(['第三則'])`：三則依序出現，同時只有一則；在第一則上點一下立即關閉並換下一則。
4. Console 執行 `UI.sysMessage(['每日任務「<img src=x onerror=alert(1)> & \"引號\"」完成。'])`：訊息照字面顯示 `<img src=x onerror=alert(1)> & "引號"`，沒有跳出 alert、沒有破圖示。
5. Console 連續執行 10 次 `UI.sysMessage(['連發'])`（例 `for (let i = 0; i < 10; i++) { UI.sysMessage(['連發 ' + i]); }`）：只會看到「連發 0」與最後 4 則（6～9），不會排到 10 則。
6. Console 執行 `UI.setAccent('jade')` → 全畫面翡翠綠；`UI.setAccent('nope')` → `document.documentElement.dataset.accent` 為 `'azure'`、畫面回藍。
7. Console 執行：
   ```js
   document.querySelector('#view').insertAdjacentHTML('afterbegin', UI.statusPanel({ me: { user: { displayName: '<i>A</i>' }, player: { level: 7, xp: 120, xpNeeded: 220, rank: 'D', title: '百戰獵人', displayStreak: 5, bestStreak: 9, hardMode: true, coins: 340, shieldCount: 2 } }, today: { completionRatio: 0.5, isCleared: false } }) + UI.statusPanel({ me: { user: { displayName: 'B' }, player: { level: 3, xp: 0, xpNeeded: 0, rank: 'E', title: '', displayStreak: 0, bestStreak: 0, hardMode: false } }, compact: true }));
   ```
   第一個面板：LEVEL 方塊大字 7 並有緩慢呼吸光暈、名稱照字面 `<i>A</i>`、「稱號　百戰獵人」、右側圓形 D、EXP 條約 55% 發光、「EXP 120 / 220」「55%」、籤依序為連勝 5、最佳 9、今日 50%、金幣 340、保險卡 2、困難模式。第二個面板只有一列：LEVEL 3、空的 EXP 條（`xpNeeded` 為 0 不出錯）、0%、圓形 E。
8. Console 執行：
   ```js
   const b = document.createElement('button'); b.className = 'btn btn-danger'; b.textContent = '封存'; document.querySelector('#view').prepend(b); UI.confirmButton(b, '封存', async () => { throw new UI.ApiError('測試錯誤', 400); });
   ```
   點一下 → 文字變「再按一次確認封存」並轉為黃色框；等 3 秒 → 還原「封存」；再連點兩下 → 紅色 toast「測試錯誤」，按鈕恢復可按。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Api/wwwroot/ui.js src/SoloLeveling.Api/wwwroot/app.css src/SoloLeveling.Api/wwwroot/index.html src/SoloLeveling.Api/wwwroot/app.js
git ls-files --eol src/SoloLeveling.Api/wwwroot
git commit -m "feat: 新增 ui.js 共用元件與系統訊息佇列" -m "1. 系統視窗、狀態面板、系統訊息與二段式確認要給各畫面和獎勵系統共用，集中在 window.UI
2. 系統訊息排隊依序顯示、點擊可關閉，內容以純文字寫入避免任務名稱被當成 HTML
3. 舊 app.js 的 toast 改為成功走系統訊息、錯誤走紅色 toast" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4"
```
Expected: 所有 wwwroot 檔案 `i/lf`；commit 成功。

---

### Task 3: 前端外框、底部導覽、登入畫面與訊息宣告

**Files:**
- Modify: `src/SoloLeveling.Api/wwwroot/app.js`（開頭到 `renderLogin` 結尾整段改寫、刪除舊 `loadToday`、router 整段改寫）
- Modify: `src/SoloLeveling.Api/wwwroot/index.html`（導覽清空，改由 JS 產生）
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（新增導覽、登入區段；刪除 `legacy: header`、`legacy: auth`、`legacy: nav`）

**Interfaces:**
- Consumes: `UI.h`、`UI.ApiError`、`UI.toastError`、`UI.sysMessage`、`UI.win`、`UI.statusPanel`；Task 1 的 `.btn*`、`.field`、`.header`。
- Produces（`app.js` 閉包內，後續 Task 直接使用）：
  - `NAV_ITEMS: { route: string, zh: string, en: string }[]`
  - `state: { token: string|null, me: object|null, today: object|null }`
  - `showError(err: unknown): void`：`UI.ApiError` 顯示紅色 toast，其他例外重新丟出。
  - `api(method: string, path: string, body?: object): Promise<any>`：失敗一律丟 `UI.ApiError`（網路失敗 status 0、401 時先登出）。
  - `renderHeader(compact = true): void`
  - `announce(prevMe, prevToday, me, today, rewards): void`
  - `readSeen(me): { me, today } | null`、`writeSeen(me, today): void`
  - `loadToday(): Promise<void>`：取 `/me`、`/today`，以 `seen` 快取宣告後寫回 `state`。
  - `go(hash: string): void`：hash 已相同時直接重跑 `route()`。
  - `route()` 會呼叫 `renderProgress(isCurrent)`、`renderSettings(isCurrent)`（`isCurrent: () => boolean`）。
  - 過渡用：`toast(msg, ok)` 保留給尚未改版的舊畫面，Task 6 刪除。
  - CSS：`.nav`、`.nav a[aria-current="page"]`、`.auth`、`.auth-greet`、`.auth-sys`、`.auth-tabs`。

- [ ] **Step 1: 改寫 `app.js` 開頭到登入畫面**

Read `src/SoloLeveling.Api/wwwroot/app.js`，Edit 把「第一行 `/* SoloLeveling 第一階段前端…` 起，到 `renderLogin` 函式結尾的 `  }`（緊接在 `  /* ---------- today ---------- */` 那一行之前的空行為止）」整段替換為：

```js
/* SoloLeveling 前端：純 HTML + JS，hash 路由；共用元件在 ui.js（window.UI）。所有數值都由 API 回傳，前端不自行計算 EXP、等級與階段。 */
(() => {
  const API = '/api/v1';
  const STAT_NAMES = { STR: '力量', VIT: '體力', INT: '智力', WIL: '意志', SPI: '精神' };
  const STAT_ORDER = ['STR', 'VIT', 'INT', 'WIL', 'SPI'];
  const DIFFICULTIES = ['Easy', 'Normal', 'Hard'];
  const QUEST_TYPES = { Check: '勾選', Count: '累計', Limit: '上限' };
  // 底部導覽依此陣列產生；新增分頁時加一項並在 route() 補上對應分支
  const NAV_ITEMS = [
    { route: 'today', zh: '今日', en: 'TODAY' },
    { route: 'progress', zh: '進度', en: 'STATS' },
    { route: 'settings', zh: '設定', en: 'SYS' },
  ];

  const $header = document.getElementById('header');
  const $view = document.getElementById('view');
  const $nav = document.getElementById('nav');

  const state = { token: localStorage.getItem('token'), me: null, today: null };
  let routeSeq = 0;

  /* ---------- helpers ---------- */
  const { h } = UI;
  const num = (v) => (v === null || v === undefined ? '' : Number(v).toString());
  const addDays = (iso, n) => {
    const d = new Date(iso + 'T00:00:00Z');
    d.setUTCDate(d.getUTCDate() + n);
    return d.toISOString().slice(0, 10);
  };

  // 成功訊息走系統訊息，錯誤走紅色 toast
  function toast(msg, ok = false) {
    if (ok) {
      UI.sysMessage([msg]);
    } else {
      UI.toastError(msg);
    }
  }

  // 只顯示 API 與網路錯誤；其他例外屬程式錯誤，照常往上丟
  function showError(err) {
    if (!(err instanceof UI.ApiError)) {
      throw err;
    }
    UI.toastError(err.message);
  }

  async function api(method, path, body) {
    const headers = { 'Content-Type': 'application/json' };
    if (state.token) {
      headers.Authorization = 'Bearer ' + state.token;
    }
    let res;
    try {
      res = await fetch(API + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    } catch (err) {
      // fetch 只在網路層失敗（離線、連不到伺服器）時丟 TypeError
      if (err instanceof TypeError) {
        throw new UI.ApiError('無法連線到伺服器，請稍後再試', 0);
      }
      throw err;
    }
    if (res.status === 401 && state.token) {
      logout();
      throw new UI.ApiError('登入已過期，請重新登入', 401);
    }
    if (res.status === 204) {
      return null;
    }
    const data = await res.json().catch(() => null);
    if (!res.ok) {
      throw new UI.ApiError(data?.error?.message || `請求失敗（${res.status}）`, res.status);
    }
    return data;
  }

  function logout() {
    state.token = null;
    state.me = null;
    state.today = null;
    localStorage.removeItem('token');
    location.hash = '#login';
  }

  /* ---------- shell ---------- */
  function renderNav() {
    $nav.innerHTML = NAV_ITEMS.map((item) => `<a href="#${item.route}" data-route="${item.route}"><b>${h(item.en)}</b>${h(item.zh)}</a>`).join('');
  }

  function setActiveNav(route) {
    $nav.querySelectorAll('a').forEach((a) => {
      if (a.dataset.route === route) {
        a.setAttribute('aria-current', 'page');
      } else {
        a.removeAttribute('aria-current');
      }
    });
  }

  // 今日頁顯示完整狀態面板，其他頁顯示精簡版；未登入時隱藏
  function renderHeader(compact = true) {
    if (!state.me) {
      $header.classList.add('hidden');
      $header.innerHTML = '';
      return;
    }
    $header.innerHTML = UI.statusPanel({ me: state.me, today: state.today, compact });
    $header.classList.remove('hidden');
  }

  /**
   * 比對前後兩次 /me 與 /today，依序送出系統訊息：任務完成、今日達標、升級、目標升階。
   * 任務完成與達標只在兩份今日資料同一天、且前一份明確記錄為未完成（=== false）時宣告；
   * prevMe、prevToday 為 null（第一次看到這個帳號）時不宣告。rewards 為獎勵結果，目前未使用。
   */
  function announce(prevMe, prevToday, me, today, rewards) {
    if (prevToday && prevToday.date === today.date) {
      const before = new Map(prevToday.quests.map((q) => [q.id, q]));
      today.quests.forEach((q) => {
        if (q.isDone && before.get(q.id)?.isDone === false) {
          UI.sysMessage([`每日任務「${q.name}」完成。`, `獲得 ${q.xpReward} EXP、${q.statType} +${q.statReward}。`]);
        }
      });
      if (prevToday.isCleared === false && today.isCleared) {
        UI.sysMessage(['今日任務達成率已達門檻。', today.bonusGranted ? '今日達標，達標獎勵已發放。' : '今日達標。']);
      }
    }
    if (prevMe && me.player.level > prevMe.player.level) {
      UI.sysMessage([`等級提升。Lv.${prevMe.player.level} → Lv.${me.player.level}。`]);
    }
    if (prevToday) {
      const stages = new Map(prevToday.quests.filter((q) => q.progression).map((q) => [q.id, q.progression.stage]));
      today.quests.forEach((q) => {
        const before = stages.get(q.id);
        if (q.progression && before !== undefined && q.progression.stage > before) {
          UI.sysMessage([`目標升階。「${q.name}」進入第 ${q.progression.stage}/${q.progression.stageCount} 階。`]);
        }
      });
    }
  }

  const seenKey = (me) => `seen:${me.user.id}`;

  // 讀回上次看到的等級與各漸進任務階段，轉成 announce 可比對的形狀；沒有紀錄或內容損毀時回傳 null
  function readSeen(me) {
    const raw = localStorage.getItem(seenKey(me));
    if (raw === null) {
      return null;
    }
    let seen;
    try {
      seen = JSON.parse(raw);
    } catch (err) {
      if (err instanceof SyntaxError) {
        return null;
      }
      throw err;
    }
    if (!seen || typeof seen.level !== 'number' || typeof seen.stages !== 'object' || seen.stages === null) {
      return null;
    }
    return {
      me: { player: { level: seen.level } },
      today: { quests: Object.entries(seen.stages).map(([id, stage]) => ({ id, progression: { stage } })) },
    };
  }

  function writeSeen(me, today) {
    const stages = Object.fromEntries(today.quests.filter((q) => q.progression).map((q) => [q.id, q.progression.stage]));
    localStorage.setItem(seenKey(me), JSON.stringify({ level: me.player.level, stages }));
  }

  // 每次切換畫面都重取 /me 與 /today，並以上次看到的等級與階段宣告兩次開啟之間的升級與升階
  async function loadToday() {
    const [me, today] = await Promise.all([api('GET', '/me'), api('GET', '/today')]);
    const seen = readSeen(me);
    announce(seen ? seen.me : null, seen ? seen.today : null, me, today);
    writeSeen(me, today);
    state.me = me;
    state.today = today;
  }

  /* ---------- auth ---------- */
  function renderLogin(mode = 'login') {
    $header.classList.add('hidden');
    $nav.classList.add('hidden');
    const isRegister = mode === 'register';
    const greet = isRegister
      ? '偵測到新的玩家。<br>完成登錄後，系統將開始發布每日任務。'
      : '歡迎回來，玩家。<br>請完成身分驗證。';
    $view.innerHTML = `
      <div class="auth">
        ${UI.win({ title: '玩家登錄', body: `
          <p class="auth-greet"><span class="auth-sys">[ SYSTEM ]</span>${greet}</p>
          <div class="auth-tabs">
            <button type="button" data-mode="login" class="btn ${isRegister ? 'btn-ghost' : 'btn-primary'}" aria-pressed="${!isRegister}">登入</button>
            <button type="button" data-mode="register" class="btn ${isRegister ? 'btn-primary' : 'btn-ghost'}" aria-pressed="${isRegister}">註冊</button>
          </div>
          <form id="auth-form">
            <label class="field">Email<input name="email" type="email" required autocomplete="email"></label>
            <label class="field">密碼${isRegister ? '（至少 8 碼）' : ''}<input name="password" type="password" required minlength="${isRegister ? 8 : 1}" autocomplete="${isRegister ? 'new-password' : 'current-password'}"></label>
            ${isRegister ? '<label class="field">顯示名稱<input name="displayName" type="text" required maxlength="40"></label>' : ''}
            <button class="btn btn-primary btn-block" type="submit">${isRegister ? '建立帳號' : '登入'}</button>
          </form>` })}
      </div>`;
    $view.querySelectorAll('[data-mode]').forEach((b) => b.addEventListener('click', () => renderLogin(b.dataset.mode)));
    const form = $view.querySelector('#auth-form');
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      const f = new FormData(form);
      const body = { email: f.get('email'), password: f.get('password') };
      if (isRegister) {
        body.displayName = f.get('displayName');
        body.timeZoneId = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Asia/Taipei';
      }
      const submit = form.querySelector('button[type="submit"]');
      submit.disabled = true;
      try {
        const res = await api('POST', isRegister ? '/auth/register' : '/auth/login', body);
        state.token = res.token;
        localStorage.setItem('token', res.token);
        go('#today');
      } catch (err) {
        showError(err);
      } finally {
        submit.disabled = false;
      }
    });
  }
```

- [ ] **Step 2: 刪除舊 `loadToday`**

Edit `app.js`，old_string：
```js
  /* ---------- today ---------- */
  async function loadToday() {
    [state.me, state.today] = await Promise.all([api('GET', '/me'), api('GET', '/today')]);
  }

```
new_string：
```js
  /* ---------- today ---------- */
```

- [ ] **Step 3: 改寫 router**

Edit `app.js`，把從 `  /* ---------- router ---------- */` 到檔尾 `})();` 的整段替換為：

```js
  /* ---------- router ---------- */
  // hash 已是目標時設定 location.hash 不會觸發 hashchange，改為直接重跑路由
  function go(hash) {
    if (location.hash === hash) {
      route();
    } else {
      location.hash = hash;
    }
  }

  async function route() {
    const seq = ++routeSeq;
    // 快速連續切換時，只讓最後一次路由寫入畫面
    const isCurrent = () => seq === routeSeq;
    const hash = (location.hash || '#today').slice(1);
    if (!state.token) {
      renderLogin(hash === 'register' ? 'register' : 'login');
      return;
    }
    if (hash === 'login' || hash === 'register') {
      go('#today');
      return;
    }
    window.scrollTo(0, 0);
    try {
      await loadToday();
      if (!isCurrent()) {
        return;
      }
      // 沒有任何任務就強制走引導；引導完成前不開放其他頁
      if (state.me.needsOnboarding && hash !== 'onboarding') {
        go('#onboarding');
        return;
      }
      // 已完成引導就不該再進引導頁（例如使用者手動改網址）
      if (!state.me.needsOnboarding && hash === 'onboarding') {
        go('#today');
        return;
      }
      if (hash === 'onboarding') {
        $nav.classList.add('hidden');
        const { goals } = await api('GET', '/goals');
        if (!isCurrent()) {
          return;
        }
        await renderOnboarding({ single: false, excluded: goals.map((g) => g.category) });
        return;
      }
      const current = NAV_ITEMS.some((item) => item.route === hash) ? hash : 'today';
      $nav.classList.remove('hidden');
      setActiveNav(current);
      if (current === 'progress') {
        await renderProgress(isCurrent);
      } else if (current === 'settings') {
        await renderSettings(isCurrent);
      } else {
        renderToday();
      }
    } catch (err) {
      if (!(err instanceof UI.ApiError)) {
        throw err;
      }
      // 401 已在 api() 登出並導回登入頁，不再重複提示
      if (state.token) {
        UI.toastError(err.message);
      }
    }
  }

  renderNav();
  window.addEventListener('hashchange', route);
  route();
})();
```

- [ ] **Step 4: `index.html` 清空導覽**

Edit `src/SoloLeveling.Api/wwwroot/index.html`，old_string：
```html
  <nav id="nav" class="nav hidden">
    <a href="#today" data-route="today">今日</a>
    <a href="#progress" data-route="progress">進度</a>
    <a href="#settings" data-route="settings">設定</a>
  </nav>
```
new_string：
```html
  <nav id="nav" class="nav hidden" aria-label="主要分頁"></nav>
```

完成後 `index.html` 全文應為：
```html
<!doctype html>
<html lang="zh-Hant" data-accent="azure">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <meta name="color-scheme" content="dark">
  <title>SoloLeveling</title>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Chakra+Petch:wght@500;600;700&family=IBM+Plex+Mono:wght@400;500;600&family=Noto+Sans+TC:wght@400;500;700&family=Noto+Serif+TC:wght@600;900&display=swap">
  <link rel="stylesheet" href="app.css">
</head>
<body>
  <header id="header" class="header hidden"></header>
  <main id="view" class="view"></main>
  <nav id="nav" class="nav hidden" aria-label="主要分頁"></nav>
  <script src="ui.js"></script>
  <script src="app.js"></script>
</body>
</html>
```

- [ ] **Step 5: 導覽與登入樣式**

Edit `src/SoloLeveling.Api/wwwroot/app.css`：

(a) 刪除三段 legacy。old_string：
```css
/* legacy: header */
.header .row { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; }
.header .level { color: var(--accent); font: 700 26px var(--latin); }
.header .rank { color: var(--accent); font-weight: 600; }
.header .title { color: var(--muted); font-size: 14px; }
.header .meta { display: flex; flex-wrap: wrap; gap: 14px; margin-top: 6px; color: var(--muted); font-size: 14px; }
.header .meta b { color: var(--ink); }
.xpbar { height: 6px; margin-top: 8px; overflow: hidden; background: var(--line-soft); }
.xpbar > i { display: block; height: 100%; background: var(--accent); }

/* legacy: auth */
.auth { width: 100%; max-width: 380px; margin: 24px auto; }
.auth h1 { margin: 0 0 4px; font: 900 26px var(--serif); }
.auth p { margin: 0 0 20px; color: var(--muted); }
.auth .tabs { display: flex; gap: 8px; margin-bottom: 14px; }
.auth .tabs button { flex: 1; }

/* legacy: nav */
.nav { position: fixed; left: 0; right: 0; bottom: 0; display: flex; padding-bottom: var(--safe-bottom); background: var(--panel-solid); border-top: 1px solid var(--line-soft); }
.nav a { flex: 1; padding: 14px 0; color: var(--muted); text-align: center; text-decoration: none; font-weight: 600; }
.nav a.active { color: var(--accent); }

```
new_string：空字串。

(b) old_string：
```css
/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */
```
new_string：
```css
/* ---- 底部導覽 ---- */
.nav {
  position: fixed;
  z-index: 20;
  left: 0;
  right: 0;
  bottom: 0;
  display: grid;
  grid-auto-flow: column;
  grid-auto-columns: minmax(0, 1fr);
  padding: 0 var(--safe-right) var(--safe-bottom) var(--safe-left);
  background: color-mix(in srgb, var(--bg) 88%, transparent);
  border-top: 1px solid var(--line-soft);
  -webkit-backdrop-filter: blur(8px);
  backdrop-filter: blur(8px);
}
.nav a {
  position: relative;
  display: grid;
  justify-items: center;
  align-content: center;
  gap: 2px;
  min-height: var(--nav-h);
  color: var(--muted);
  font-size: 12px;
  text-decoration: none;
}
.nav a b { font: 600 10px var(--latin); letter-spacing: .2em; }
.nav a[aria-current="page"] { color: var(--accent); }
.nav a[aria-current="page"]::before {
  content: "";
  position: absolute;
  top: 0;
  left: 50%;
  width: 28px;
  height: 2px;
  margin-left: -14px;
  background: var(--accent);
  box-shadow: 0 0 8px var(--accent);
}

/* ---- 登入 ---- */
.auth { width: 100%; max-width: 400px; margin: 8vh auto 0; }
.auth-greet { margin-bottom: 14px; color: var(--ink); font: 14px/1.8 var(--serif); }
.auth-sys { display: block; color: var(--accent); font: 600 11px var(--latin); letter-spacing: .2em; }
.auth-tabs { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-bottom: 4px; }
.auth form .btn-block { margin-top: 8px; }

/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */
```

- [ ] **Step 6: 語法、色碼、建置與測試**

Run：
```bash
node --check src/SoloLeveling.Api/wwwroot/app.js
awk '/tokens:start/{t=1} /tokens:end/{t=0; next} !t && /#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(|hsla?\(/ {print FNR": "$0}' src/SoloLeveling.Api/wwwroot/app.css
grep -nE "#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(" src/SoloLeveling.Api/wwwroot/ui.js src/SoloLeveling.Api/wwwroot/app.js src/SoloLeveling.Api/wwwroot/index.html
grep -n "diffDays\|\.active\b" src/SoloLeveling.Api/wwwroot/app.js
dotnet build
dotnet test
```
Expected: `node --check` 無輸出；awk、兩個 grep 都無輸出；建置 0 警告；207 個測試、Failed 0。

- [ ] **Step 7: 手動檢查（含 Review Focus 3、4）**

Run: `docker compose up -d --build`，用無痕視窗開 http://localhost:8080/。

1. 登入頁：導覽與狀態面板都隱藏；畫面中央一個系統視窗，標題「玩家登錄」兩側有漸層細線；下方藍字「[ SYSTEM ]」與宋體「歡迎回來，玩家。／請完成身分驗證。」；「登入」實心、「註冊」細框。點「註冊」→ 文案變「偵測到新的玩家。…」並多出「顯示名稱」欄位。
2. 註冊新帳號 `ui3@example.com`／`password123`／`三號玩家` → 進舊版引導（無導覽）→ 完成 → 今日頁頂部是**精簡版**狀態面板（LEVEL 1、EXP 條、圓形 E；完整版在 Task 4 換上）；底部三格「TODAY 今日／STATS 進度／SYS 設定」，作用中那格為主色且頂端有一條發光短線。點「進度」「設定」，作用中格與內容跟著切換。第一次登入沒有任何系統訊息。
3. 登出 → 錯誤密碼登入 → 紅色 toast；連點「登入」兩下只送出一次請求（DevTools Network 只有一筆 `login`）。
4. **Review Focus 3（hash 已等於目標）**：登出後在網址列直接開 `http://localhost:8080/#today`（重新整理）→ 顯示登入頁；登入成功後必須進到今日頁，不能停在登入頁。
5. **Review Focus 3（快速切換）**：DevTools Network 節流選「Slow 3G」，在今日頁快速連點「進度」再點「設定」→ 等請求結束，畫面停在設定頁、導覽作用中為「設定」，不會被晚回來的進度頁蓋掉。關閉節流。
6. **Review Focus 4（快取損毀）**：Console 執行 `Object.keys(localStorage)` 可看到 `seen:<guid>`；執行 `const k = Object.keys(localStorage).find((x) => x.startsWith('seen:')); localStorage.setItem(k, '{bad'); location.reload();` → 畫面正常顯示、Console 無錯誤、沒有系統訊息；再看 `localStorage.getItem(k)` 已被寫回合法 JSON（`{"level":1,"stages":{…}}`）。
7. **升級宣告（快取比對）**：Console 執行 `const k = Object.keys(localStorage).find((x) => x.startsWith('seen:')); const s = JSON.parse(localStorage.getItem(k)); s.level = 0; Object.keys(s.stages).forEach((id) => { s.stages[id] = 0; }); localStorage.setItem(k, JSON.stringify(s)); location.reload();` → 依序出現「等級提升。Lv.0 → Lv.1。」以及每個漸進任務一則「目標升階。「…」進入第 1/N 階。」；再重新整理一次 → 不再出現。
8. **Review Focus 4（換帳號）**：把目前帳號的 `seen:` 等級手動改成 99（同上方式設 `s.level = 99`），登出，註冊另一個新帳號 → 完成引導進今日頁時**沒有**任何等級提升訊息（各帳號的快取鍵不同）。
9. **網路中斷**：`docker compose stop api` 後點導覽任一格 → 紅色 toast「無法連線到伺服器，請稍後再試」；`docker compose start api` 後重新整理恢復。

- [ ] **Step 8: Commit**

```bash
git add src/SoloLeveling.Api/wwwroot/app.js src/SoloLeveling.Api/wwwroot/index.html src/SoloLeveling.Api/wwwroot/app.css
git ls-files --eol src/SoloLeveling.Api/wwwroot
git commit -m "feat: 改寫前端外框、底部導覽、登入畫面與訊息宣告" -m "1. 導覽改由 NAV_ITEMS 產生，之後新增分頁只要加一項；Header 改為 UI 狀態面板
2. announce 統一比對前後 /me、/today 宣告完成、達標、升級與升階，等級與階段記在各帳號的 seen 快取
3. 修正 hash 已是目標時登入後停在登入頁，快速切換分頁只讓最後一次路由寫入畫面" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4"
```

---

### Task 4: 今日畫面

**Files:**
- Modify: `src/SoloLeveling.Api/wwwroot/app.js`（today 區段整段改寫）
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（新增今日區段；刪除 `legacy: today`）

**Interfaces:**
- Consumes: `state`、`api`、`showError`、`announce`、`writeSeen`、`renderHeader`、`num`、`h`、`STAT_ORDER`、`STAT_NAMES`（Task 3）；`UI.win`、`UI.sysMessage`、`UI.toastError`（Task 2）。
- Produces:
  - `renderToday(): void`（router 呼叫）
  - `setProgress(id: string, value: number|null): Promise<void>`：同時只允許一個請求。
  - `sameDayBase(prevToday, today)`：跨日時的比對基準。
  - CSS：`.ratio-row`、`.ratio`、`.ratio.cleared`、`.latin-label`（Task 5 沿用）、`.quest-list`、`.quest-list.busy`、`.quest-group`、`.group-title`、`.quest-row`、`.quest-row.done`、`.quest-main`、`.quest-name`、`.quest-meta`、`.stage`、`.quest-ctl`、`.tick`、`.step`、`.count`、`.count-value`、`.limit-input`。

- [ ] **Step 1: 改寫 today 區段**

Edit `app.js`，把從 `  /* ---------- today ---------- */` 到舊 `renderToday` 結尾（緊接在 `  /* ---------- onboarding ---------- */` 之前的空行為止）整段替換為：

```js
  /* ---------- today ---------- */
  let progressBusy = false;

  function questControl(q) {
    const name = h(q.name);
    if (q.questType === 'Check') {
      return `<button class="tick" type="button" data-act="toggle" data-id="${q.id}" aria-pressed="${q.isDone}" aria-label="完成 ${name}"><i aria-hidden="true">${q.isDone ? '✓' : ''}</i></button>`;
    }
    if (q.questType === 'Count') {
      return `
        <span class="count">
          <button class="step" type="button" data-act="step" data-delta="-1" data-id="${q.id}" aria-label="減少 ${name}">−</button>
          <span class="count-value num">${num(q.value ?? 0)} / ${num(q.targetValue)}</span>
          <button class="step" type="button" data-act="step" data-delta="1" data-id="${q.id}" aria-label="增加 ${name}">＋</button>
        </span>`;
    }
    return `
      <span class="count">
        <input class="limit-input num" type="number" inputmode="decimal" min="0" step="${num(q.step ?? 0.5)}" data-id="${q.id}" value="${num(q.value)}" placeholder="—" aria-label="${name} 今日數值">
        <span class="count-value num">≤ ${num(q.targetValue)}</span>
      </span>`;
  }

  function questRow(q) {
    const stage = q.progression ? `<span class="stage">第 ${q.progression.stage}/${q.progression.stageCount} 階</span>` : '';
    const unit = q.unit ? ` · ${h(q.unit)}` : '';
    return `
      <div class="quest-row ${q.isDone ? 'done' : ''}">
        <div class="quest-main">
          <div class="quest-name"><span>${h(q.name)}</span>${stage}</div>
          <div class="quest-meta">${h(q.difficulty)} · +${q.xpReward} EXP · ${q.statType} +${q.statReward}${unit}</div>
        </div>
        <div class="quest-ctl">${questControl(q)}</div>
      </div>`;
  }

  // 開著頁面跨過午夜時，前一份今日資料是昨天的；以「新的一天全部未完成」為比對基準，第一次勾選仍會宣告
  function sameDayBase(prevToday, today) {
    if (prevToday.date === today.date) {
      return prevToday;
    }
    return { ...prevToday, date: today.date, isCleared: false, quests: prevToday.quests.map((q) => ({ ...q, isDone: false })) };
  }

  // 同一時間只送一個進度請求：連點時忽略後續點擊，避免以舊值計算造成累計少算或重複宣告
  async function setProgress(id, value) {
    if (progressBusy) {
      return;
    }
    progressBusy = true;
    $view.querySelector('#quest-list')?.classList.add('busy');
    const prevMe = state.me;
    const prevToday = state.today;
    try {
      const today = await api('PUT', `/today/quests/${id}/progress`, { value });
      const me = await api('GET', '/me');
      announce(prevMe, sameDayBase(prevToday, today), me, today);
      writeSeen(me, today);
      state.today = today;
      state.me = me;
    } catch (err) {
      showError(err);
    } finally {
      progressBusy = false;
    }
    // 請求期間若已切到其他頁就不重繪，避免蓋掉新畫面
    if ($view.querySelector('#quest-list')) {
      renderToday();
    }
  }

  function renderToday() {
    renderHeader(false);
    const t = state.today;
    // 重繪前保留尚未儲存的反思草稿，勾選任務不會清掉正在輸入的內容
    const draft = $view.querySelector('#note')?.value;
    const groups = STAT_ORDER.map((s) => [s, t.quests.filter((q) => q.statType === s)]).filter(([, qs]) => qs.length);
    const list = groups.map(([s, qs]) => `
      <div class="quest-group">
        <div class="group-title"><span class="latin-label">${s}</span>${STAT_NAMES[s]}</div>
        ${qs.map(questRow).join('')}
      </div>`).join('');
    $view.innerHTML = `
      ${UI.win({ title: '每日任務', body: `
        <div class="ratio-row">
          <div><span class="latin-label">TODAY</span> <span class="num">${h(t.date)}</span><span class="sub">門檻 ${Math.round(t.threshold * 100)}%${t.bonusGranted ? '，已領達標獎勵' : ''}</span></div>
          <b class="ratio num ${t.isCleared ? 'cleared' : ''}">${Math.round(t.completionRatio * 100)}%</b>
        </div>
        <div id="quest-list" class="quest-list">${list || '<p class="sub">目前沒有任務，可到設定新增。</p>'}</div>` })}
      ${UI.win({ title: '今日反思', body: `
        <textarea id="note" rows="3" maxlength="2000" placeholder="寫點什麼…" aria-label="今日反思">${h(draft ?? t.note ?? '')}</textarea>
        <div class="actions"><button id="save-note" class="btn btn-ghost" type="button">儲存</button></div>` })}`;

    const $list = $view.querySelector('#quest-list');
    const questById = new Map(t.quests.map((q) => [q.id, q]));
    $list.addEventListener('click', (e) => {
      const btn = e.target.closest('button[data-act]');
      if (!btn) {
        return;
      }
      const q = questById.get(btn.dataset.id);
      if (btn.dataset.act === 'toggle') {
        setProgress(q.id, q.isDone ? 0 : 1);
        return;
      }
      const current = Number(q.value ?? 0);
      const next = Number(Math.max(0, current + Number(btn.dataset.delta) * Number(q.step ?? 1)).toFixed(2));
      if (next === current) {
        return;
      }
      setProgress(q.id, next);
    });
    $list.addEventListener('change', (e) => {
      const input = e.target.closest('input.limit-input');
      if (!input) {
        return;
      }
      const value = input.value === '' ? null : Number(input.value);
      if (value !== null && !(value >= 0)) {
        UI.toastError('請輸入 0 以上的數字');
        renderToday();
        return;
      }
      setProgress(input.dataset.id, value);
    });
    $view.querySelector('#save-note').addEventListener('click', async (e) => {
      const btn = e.currentTarget;
      btn.disabled = true;
      try {
        await api('PUT', '/today/note', { note: $view.querySelector('#note').value });
        UI.sysMessage(['今日反思已記錄。']);
      } catch (err) {
        showError(err);
      } finally {
        btn.disabled = false;
      }
    });
  }
```

- [ ] **Step 2: 今日樣式**

Edit `app.css`：

(a) old_string：
```css
/* legacy: today */
.quest.done .name { color: var(--muted); text-decoration: line-through; }
.quest .ctl { display: flex; align-items: center; gap: 6px; white-space: nowrap; }
.quest .value { min-width: 64px; text-align: center; font-family: var(--mono); }
.check { width: 22px; height: 22px; }
.summary { display: flex; justify-content: space-between; align-items: center; }
.summary .ratio { font: 700 22px var(--mono); }
.summary .ratio.cleared { color: var(--ok); }
input[type="number"].inline { width: 88px; text-align: right; }
.stage { display: inline-block; margin-left: 6px; color: var(--accent); font-size: 11px; }

```
new_string：空字串。

(b) old_string：
```css
/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */
```
new_string：
```css
/* ---- 今日 ---- */
.latin-label { color: var(--accent); font: 600 11px var(--latin); letter-spacing: .2em; }
.ratio-row { display: flex; align-items: flex-end; justify-content: space-between; gap: 12px; margin-bottom: 6px; color: var(--muted); font-size: 13px; }
.ratio-row .ratio { color: var(--ink); font-size: 24px; font-weight: 600; }
.ratio-row .ratio.cleared { color: var(--ok); text-shadow: 0 0 12px color-mix(in srgb, var(--ok) 50%, transparent); }
.quest-list.busy { opacity: .75; }
.quest-group + .quest-group { margin-top: 6px; }
.group-title { display: flex; align-items: center; gap: 8px; padding: 8px 2px 2px; color: var(--muted); font-size: 12px; }
.quest-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: 10px;
  padding: 6px 2px;
  border-top: 1px dashed var(--line-soft);
}
.quest-main { min-width: 0; }
.quest-name { display: flex; flex-wrap: wrap; align-items: baseline; gap: 6px; font-size: 14px; }
.quest-name > span:first-child { min-width: 0; overflow-wrap: anywhere; }
.quest-meta { color: var(--muted); font: 11px var(--mono); overflow-wrap: anywhere; }
.stage { padding: 0 5px; border: 1px solid var(--line-soft); color: var(--accent); font: 10px var(--mono); white-space: nowrap; }
.quest-row.done .quest-name > span:first-child {
  color: var(--muted);
  text-decoration: line-through;
  text-decoration-color: color-mix(in srgb, var(--muted) 60%, transparent);
}
.quest-ctl { display: flex; align-items: center; }
.tick, .step {
  display: grid;
  place-items: center;
  width: var(--tap);
  height: var(--tap);
  padding: 0;
  background: transparent;
  color: var(--ink);
  cursor: pointer;
}
.tick { border: 0; }
.tick i {
  display: grid;
  place-items: center;
  width: 24px;
  height: 24px;
  border: 1px solid var(--line);
  color: var(--accent-ink);
  font: 700 14px/1 var(--mono);
  font-style: normal;
}
.quest-row.done .tick i {
  background: var(--accent);
  border-color: var(--accent);
  box-shadow: 0 0 10px color-mix(in srgb, var(--accent) 60%, transparent);
}
.step { border: 1px solid var(--line-soft); font: 600 16px var(--mono); }
.count { display: flex; align-items: center; gap: 6px; font-size: 12px; }
.count-value { min-width: 44px; text-align: center; white-space: nowrap; }
.limit-input { width: 76px; text-align: right; }

/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */
```

- [ ] **Step 3: 語法、色碼、建置與測試**

Run：
```bash
node --check src/SoloLeveling.Api/wwwroot/app.js
awk '/tokens:start/{t=1} /tokens:end/{t=0; next} !t && /#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(|hsla?\(/ {print FNR": "$0}' src/SoloLeveling.Api/wwwroot/app.css
grep -nE "#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(" src/SoloLeveling.Api/wwwroot/app.js
grep -n "class=\"check\|class=\"inline\|class=\"summary\|card summary" src/SoloLeveling.Api/wwwroot/app.js
dotnet build
dotnet test
```
Expected: 前四個指令無輸出；建置 0 警告；207 個測試、Failed 0。

- [ ] **Step 4: 重新部署**

Run: `docker compose up -d --build`
Expected: `api` 容器重建並啟動，http://localhost:8080/ 可開。

- [ ] **Step 5: 手動檢查：畫面與宣告**

用 Task 3 的帳號登入今日頁。

1. 頂部是**完整**狀態面板：LEVEL 方塊緩慢呼吸光暈、顯示名稱、「稱號　…」、圓形階級、EXP 條與「EXP x / y」、數據籤（連勝、最佳、今日 %）。
2. 「每日任務」視窗：宋體標題兩側細線；左上「TODAY 2026-…」與「門檻 70%」，右側大字達成率；任務依 STR／VIT／INT／WIL／SPI 分組，每列名稱、漸進任務的「第 1/N 階」小框、等寬小字「Normal · +20 EXP · VIT +1」（有單位時接「· 分鐘」）。
3. 勾選一個 Check 任務：勾選框填滿主色並發光、名稱淡化加刪除線；頂部系統訊息「每日任務「…」完成。／獲得 N EXP、XXX +1。」；狀態面板 EXP 條同步更新。再按一次取消：刪除線消失、**沒有**系統訊息。
4. Count 任務按「＋」直到達標 → 一則完成訊息；按「−」回到未達標 → 沒有訊息。Limit 任務輸入不超過上限的值並離開欄位 → 完成訊息；輸入 `-1` → 紅色 toast「請輸入 0 以上的數字」且欄位還原。
5. 繼續勾到達成率 ≥ 70%：除完成訊息外另一則「今日任務達成率已達門檻。／今日達標，達標獎勵已發放。」，右上達成率變 `--ok` 色並發光。
6. 一路勾到 EXP 超過 100（Lv.1 → Lv.2）：出現「等級提升。Lv.1 → Lv.2。」；多則訊息依序排隊出現。
7. 在反思欄打字（不按儲存），接著勾選一個任務 → 重繪後文字仍在；按「儲存」→「今日反思已記錄。」；重新整理後文字仍在。
8. 切到 violet、jade（`UI.setAccent('violet')`／`UI.setAccent('jade')`）看今日頁，勾選框、達成率、階段小框、系統訊息框線都換成該主題色，jade 的達標色為淺黃綠。

- [ ] **Step 6: 手動檢查：Review Focus 1、2**

1. **連點（Review Focus 1）**：DevTools Network 節流「Slow 3G」。對一個 Count 任務快速連按「＋」三下 → Network 只有一筆 `progress` 請求；回應後數值只增加一個 step；最多一則完成訊息。對一個 Check 任務快速雙擊 → 只有一筆請求，結果為勾選。關閉節流。
2. **特殊字元（Review Focus 2）**：到「設定」新增一般任務，名稱 `<img src=x onerror=alert(1)> & "引號"`，類型勾選 → 回今日頁：任務列照字面顯示該字串；勾選它 → 系統訊息照字面顯示「每日任務「<img src=x onerror=alert(1)> & "引號"」完成。」，全程沒有 alert、沒有破圖示。
3. **錯誤**：`docker compose stop api` 後勾選任務 → 紅色 toast「無法連線到伺服器，請稍後再試」，畫面維持原狀、控制項可再按；`docker compose start api`。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Api/wwwroot/app.js src/SoloLeveling.Api/wwwroot/app.css
git commit -m "feat: 今日畫面改為系統視窗並宣告任務完成與達標" -m "1. 今日頁換上完整狀態面板與每日任務視窗，勾選框完成時填滿主色發光，觸控區至少 40px
2. 進度請求同一時間只送一個，避免連點以舊值計算造成少算與重複宣告；跨日時以新的一天為比對基準
3. 重繪時保留未儲存的反思草稿，成功訊息改走系統訊息" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4"
```

---

### Task 5: 進度畫面與可點格的 66 天日曆

**Files:**
- Modify: `src/SoloLeveling.Api/wwwroot/app.js`（progress 區段整段改寫）
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（新增進度區段；刪除 `legacy: progress`）

**Interfaces:**
- Consumes: `state`、`api`、`addDays`、`renderHeader`、`h`、`STAT_ORDER`、`STAT_NAMES`（Task 3）；`.ratio-row`、`.latin-label`（Task 4）；`UI.win`。
- Produces:
  - `renderProgress(isCurrent = () => true): Promise<void>`（router 以 `isCurrent` 呼叫）
  - `weekdayOf(iso: string): string`、`dayStatus(date, today, log): { cls, label }`、`dayCaption(cell: HTMLElement): string`
  - CSS：`.cal`、`.day`（`.cleared`／`.missed`／`.today`／`.future`、`[aria-pressed="true"]`）、`@keyframes day-flash`、`.cal-readout`、`.stats`、`.stat`、`.stats-foot`、`.week`、`.week-name`、`.week-dot`、`.week-dot.on`。

- [ ] **Step 1: 改寫 progress 區段**

Edit `app.js`，把從 `  /* ---------- progress ---------- */` 到舊 `renderProgress` 結尾（緊接在 `  /* ---------- settings ---------- */` 之前的空行為止）整段替換為：

```js
  /* ---------- progress ---------- */
  const WEEKDAYS = '日一二三四五六';
  const weekdayOf = (iso) => WEEKDAYS[new Date(iso + 'T00:00:00Z').getUTCDay()];

  function dayStatus(date, today, log) {
    if (date > today) {
      return { cls: 'future', label: '尚未到來' };
    }
    if (date === today) {
      return log?.isCleared ? { cls: 'today cleared', label: '今日・已達標' } : { cls: 'today', label: '今日・尚未達標' };
    }
    return log?.isCleared ? { cls: 'cleared', label: '已達標' } : { cls: 'missed', label: '未達標' };
  }

  function dayCaption(cell) {
    const { day, date, status } = cell.dataset;
    return `第 ${day} 天 · ${date}（${weekdayOf(date)}）· ${status}`;
  }

  async function renderProgress(isCurrent = () => true) {
    renderHeader();
    const { program, player } = state.me;
    const today = state.today.date;
    const end = addDays(program.startDate, program.lengthDays - 1);
    const histEnd = end < today ? end : today;
    const [history, week, quests] = await Promise.all([
      api('GET', `/history?from=${program.startDate}&to=${histEnd}`),
      api('GET', `/history?from=${addDays(today, -6)}&to=${today}`),
      api('GET', '/quests'),
    ]);
    if (!isCurrent()) {
      return;
    }
    const byDate = Object.fromEntries(history.map((d) => [d.date, d]));
    const cells = Array.from({ length: program.lengthDays }, (_, i) => {
      const date = addDays(program.startDate, i);
      const { cls, label } = dayStatus(date, today, byDate[date]);
      return `<button type="button" class="day ${cls}" data-day="${i + 1}" data-date="${date}" data-status="${label}" aria-pressed="false" aria-label="第 ${i + 1} 天，${date}，${label}">${i + 1}</button>`;
    });
    const weekDates = Array.from({ length: 7 }, (_, i) => addDays(today, i - 6));
    const weekByDate = Object.fromEntries(week.map((d) => [d.date, new Set(d.doneQuestIds)]));
    const weekRows = quests.map((q) => `
      <tr>
        <th scope="row" class="week-name">${h(q.name)}</th>
        ${weekDates.map((d) => {
          const on = weekByDate[d]?.has(q.id);
          return `<td><span class="week-dot ${on ? 'on' : ''}" role="img" aria-label="${d} ${on ? '完成' : '未完成'}"></span></td>`;
        }).join('')}
      </tr>`).join('');
    $view.innerHTML = `
      ${UI.win({ title: '66 天計畫', body: `
        <div class="ratio-row">
          <span>第 ${program.cycle} 週期 · 第 <b class="num">${program.dayNumber}</b> 天${program.isCompleted ? '（已完成）' : ''}</span>
          <span class="latin-label">DAY ${program.dayNumber} / ${program.lengthDays}</span>
        </div>
        <div class="cal" id="cal">${cells.join('')}</div>
        <p class="cal-readout" id="cal-readout" aria-live="polite"></p>` })}
      ${UI.win({ title: '五維屬性', body: `
        <div class="stats">${STAT_ORDER.map((s) => `<div class="stat"><span class="latin-label">${s}</span><b class="num">${h(player.stats[s.toLowerCase()])}</b><span class="sub">${STAT_NAMES[s]}</span></div>`).join('')}</div>
        <p class="sub stats-foot">累計完成 ${player.totalCompleted} 次 · 最佳連續 ${player.bestStreak} 天</p>` })}
      ${UI.win({ title: '最近 7 天', body: `
        <table class="week">
          <thead><tr><th scope="col"><span class="sr-only">任務</span></th>${weekDates.map((d) => `<th scope="col" title="${d}"><span class="num">${d.slice(8)}</span><span class="sub">${weekdayOf(d)}</span></th>`).join('')}</tr></thead>
          <tbody>${weekRows}</tbody>
        </table>` })}`;

    const $cal = $view.querySelector('#cal');
    const $readout = $view.querySelector('#cal-readout');
    const select = (cell) => {
      $cal.querySelectorAll('.day[aria-pressed="true"]').forEach((c) => c.setAttribute('aria-pressed', 'false'));
      cell.setAttribute('aria-pressed', 'true');
      $readout.textContent = dayCaption(cell);
    };
    $cal.addEventListener('click', (e) => {
      const cell = e.target.closest('.day');
      if (cell) {
        select(cell);
      }
    });
    const todayCell = $cal.querySelector('.day.today');
    if (todayCell) {
      select(todayCell);
    } else {
      $readout.textContent = '點選格子查看日期與達標狀況。';
    }
  }
```

- [ ] **Step 2: 進度樣式**

Edit `app.css`：

(a) old_string：
```css
/* legacy: progress */
.grid66 { display: grid; grid-template-columns: repeat(11, 1fr); gap: 5px; }
.grid66 > i { display: flex; align-items: center; justify-content: center; aspect-ratio: 1; border: 1px solid var(--line-soft); color: var(--muted); font-size: 10px; font-style: normal; }
.grid66 > i.cleared { background: var(--accent); color: var(--accent-ink); }
.grid66 > i.missed { border-color: var(--line); }
.grid66 > i.today { outline: 2px solid var(--accent); outline-offset: -2px; color: var(--ink); }
.grid66 > i.future { opacity: .45; }
.stats { display: grid; grid-template-columns: repeat(5, 1fr); gap: 8px; text-align: center; }
.stats > div { padding: 8px 4px; border: 1px solid var(--line-soft); }
.stats b { display: block; font: 600 20px var(--mono); }
.stats span { color: var(--muted); font-size: 12px; }
.dots { width: 100%; border-collapse: collapse; font-size: 13px; }
.dots th { padding: 4px 2px; color: var(--muted); font-size: 11px; font-weight: 500; text-align: center; }
.dots td { padding: 6px 2px; border-top: 1px dashed var(--line-soft); text-align: center; }
.dots td.name { max-width: 160px; overflow: hidden; text-align: left; text-overflow: ellipsis; white-space: nowrap; }
.dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; border: 1px solid var(--line-soft); }
.dot.on { background: var(--ok); border-color: var(--ok); }

```
new_string：空字串。

(b) old_string：
```css
/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */
```
new_string：
```css
/* ---- 進度 ---- */
/* 每格至少 40px 好點，375px 寬時一列 7 天 */
.cal { display: grid; grid-template-columns: repeat(auto-fill, minmax(var(--tap), 1fr)); gap: 4px; margin-top: 8px; }
.day {
  display: grid;
  place-items: center;
  aspect-ratio: 1;
  min-height: var(--tap);
  padding: 0;
  border: 1px solid var(--line-soft);
  background: transparent;
  color: var(--muted);
  font: 10px var(--mono);
  cursor: pointer;
}
.day.missed { border-color: var(--line); }
.day.cleared {
  background: var(--accent);
  border-color: var(--accent);
  color: var(--accent-ink);
  box-shadow: 0 0 8px color-mix(in srgb, var(--accent) 55%, transparent);
}
.day.future {
  border-color: color-mix(in srgb, var(--line-soft) 45%, transparent);
  color: color-mix(in srgb, var(--muted) 45%, transparent);
}
.day.today { border: 2px solid var(--accent); color: var(--ink); animation: day-flash 1.2s ease-out 1; }
.day.today.cleared { color: var(--accent-ink); }
.day[aria-pressed="true"] { outline: 2px solid var(--ink); outline-offset: 1px; }
@keyframes day-flash {
  0% { box-shadow: 0 0 0 transparent; }
  35% { box-shadow: 0 0 16px 2px var(--accent); }
  100% { box-shadow: 0 0 0 transparent; }
}
.cal-readout { min-height: 1.5em; margin-top: 10px; color: var(--ink); font: 12px var(--mono); }
.stats { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 6px; text-align: center; }
.stat { display: grid; gap: 2px; padding: 8px 2px; border: 1px solid var(--line-soft); }
.stat b { color: var(--ink); font-size: 20px; font-weight: 600; }
.stats-foot { margin-top: 10px; }
.week { width: 100%; table-layout: fixed; border-collapse: collapse; font-size: 13px; }
.week thead th { padding: 2px 0 6px; color: var(--muted); font-weight: 500; text-align: center; }
.week thead th:first-child { width: 34%; }
.week thead th span { display: block; line-height: 1.2; }
.week td { padding: 6px 0; border-top: 1px dashed var(--line-soft); text-align: center; }
.week-name {
  padding: 6px 6px 6px 0;
  border-top: 1px dashed var(--line-soft);
  color: var(--ink);
  font-weight: 400;
  text-align: left;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.week-dot { display: inline-block; width: 10px; height: 10px; border: 1px solid var(--line-soft); border-radius: 50%; }
.week-dot.on { background: var(--ok); border-color: var(--ok); box-shadow: 0 0 8px var(--ok); }

/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */
```

- [ ] **Step 3: 語法、色碼、建置與測試**

Run：
```bash
node --check src/SoloLeveling.Api/wwwroot/app.js
awk '/tokens:start/{t=1} /tokens:end/{t=0; next} !t && /#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(|hsla?\(/ {print FNR": "$0}' src/SoloLeveling.Api/wwwroot/app.css
grep -n "grid66\|class=\"dots\|class=\"dot " src/SoloLeveling.Api/wwwroot/app.js
dotnet build
dotnet test
```
Expected: 前三個指令無輸出；建置 0 警告；207 個測試、Failed 0。

- [ ] **Step 4: 重新部署**

Run: `docker compose up -d --build`
Expected: http://localhost:8080/ 可開。

- [ ] **Step 5: 手動檢查：日曆**

DevTools 切換裝置模擬「iPhone SE」（375×667），登入後點「進度」。

1. 頂部為精簡狀態面板（LEVEL、EXP 條、圓形階級一列）。
2. 「66 天計畫」視窗：左「第 1 週期 · 第 N 天」、右「DAY N / 66」；日曆每列 7 格、每格約 40px 見方；今日那格為主色粗框，進頁時閃一次光暈後停止；今日已達標則為主色實心加光暈；過去未達標為細框；未來為極淡框與淡字。
3. 進頁時下方讀數已顯示今日：「第 N 天 · 2026-10-0X（四）· 今日・已達標」（或「今日・尚未達標」），今日格有白色外框。
4. 點未來任一格 → 讀數變「第 30 天 · YYYY-MM-DD（X）· 尚未到來」，外框移到該格；用鍵盤 Tab 到某格按 Enter 也會更新。
5. 「五維屬性」：五格等寬，英文代碼（STR…）主色小字、大號等寬數字、中文名；下方「累計完成 x 次 · 最佳連續 y 天」。
6. 「最近 7 天」：表頭每欄上方日期數字、下方星期（例「28／日」）；今天已完成的任務在最後一欄為 `--ok` 色發光圓點。
7. Console 執行 `document.documentElement.scrollWidth <= window.innerWidth` → `true`。

- [ ] **Step 6: 手動檢查：Review Focus 2（7 天表）**

Task 4 建立的 `<img src=x onerror=alert(1)> & "引號"` 任務在「最近 7 天」第一欄照字面顯示（過長以「…」截斷），沒有 alert；把滑鼠移到表頭日期出現完整日期提示。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Api/wwwroot/app.js src/SoloLeveling.Api/wwwroot/app.css
git commit -m "feat: 進度畫面改版並讓 66 天日曆可點格看日期" -m "1. 原本日期只放在 title，手機點不到；格子改為按鈕，點選後在下方讀數顯示日期、星期與達標狀況
2. 每格至少 40px，窄螢幕一列 7 天；達標、未達標、今日、未來四種狀態依設計代號著色，今日閃一次
3. 屬性與最近 7 天改為系統視窗，7 天表頭改成日期加星期以容納 375px 寬" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4"
```

---

### Task 6: 設定與引導畫面，移除過渡樣式

**Files:**
- Modify: `src/SoloLeveling.Api/wwwroot/app.js`（onboarding、settings 區段整段改寫；刪除 `toast` 過渡函式）
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（新增設定、引導區段；刪除整個 `legacy` 區塊）

**Interfaces:**
- Consumes: `state`、`api`、`showError`、`renderHeader`、`logout`、`go`、`num`、`h`、`STAT_ORDER`、`STAT_NAMES`、`DIFFICULTIES`、`QUEST_TYPES`（Task 3）；`UI.win`、`UI.sysMessage`、`UI.toastError`、`UI.confirmButton`（Task 2）；`.chip`、`.btn*`、`.field`、`.form-row`、`.actions`（Task 1）。
- Produces:
  - `renderOnboarding({ single: boolean, excluded?: string[] }): Promise<void>`
  - `renderSettings(isCurrent = () => true): Promise<void>`
  - `questForm(q?: object): string`、`questionInput(q, name: string, value?: string): string`
  - CSS：`.win-step`、`.choices`、`.choice`、`.plan-title`、`.plan-quest`、`.switch`、`.switch-track`、`.goal-row`、`.goal-main`、`.quest-admin`、`.quest-admin-name`、`.quest-editor`、`.account`。
  - 完成後 `app.css` 不再有 `legacy` 字樣，`app.js` 不再有 `toast(`。

- [ ] **Step 1: 改寫 onboarding 區段**

Edit `app.js`，把從 `  /* ---------- onboarding ---------- */` 到舊 `renderOnboarding` 結尾（緊接在 `  /* ---------- progress ---------- */` 之前的空行為止）整段替換為：

```js
  /* ---------- onboarding ---------- */
  const CATEGORY_HINT = {
    Routine: '早睡早起，每 3 天提早幾分鐘',
    Exercise: '每天運動的分鐘數逐步增加',
    Reading: '每天閱讀的分鐘數逐步增加',
    ScreenTime: '每天手機使用上限逐步降低',
  };

  // value 為上一步已填過的答案，回上一步時帶回欄位
  function questionInput(q, name, value) {
    if (q.type === 'Time') {
      return `<input type="time" name="${name}" required value="${h(value ?? '')}">`;
    }
    const step = q.type === 'Integer' ? 1 : 0.25;
    return `<input type="number" inputmode="decimal" name="${name}" required min="${num(q.min)}" max="${num(q.max)}" step="${step}" value="${h(value ?? num(q.default))}">`;
  }

  // single=true 時只做一個類別（設定頁「新增目標」），不顯示基本任務；excluded 為已有進行中的類別
  async function renderOnboarding({ single, excluded = [] }) {
    renderHeader();
    const defs = await api('GET', '/goals/categories');
    const categories = defs.categories.filter((c) => !excluded.includes(c.category));
    let chosen = [];
    const answers = {};
    const goalsBody = () => chosen.map((c) => ({ category: c.category, answers: answers[c.category] }));
    const stepWin = (n, title, body) => UI.win({ title, cls: 'onboarding', body: `<span class="win-step">STEP ${n} / 3</span>${body}` });
    const cancelButton = single ? '<button type="button" class="btn btn-ghost cancel">取消</button>' : '';

    const renderStep1 = () => {
      if (!categories.length) {
        $view.innerHTML = stepWin(1, '新增目標', `<p class="sub">所有目標類別都已在進行中。</p><div class="actions">${cancelButton}</div>`);
        $view.querySelector('.cancel')?.addEventListener('click', () => renderSettings());
        return;
      }
      $view.innerHTML = stepWin(1, single ? '新增目標' : '先設定你的目標', `
        <p class="sub">選擇想改善的項目，系統會依你的現況安排每天的任務，並逐步逼近目標。</p>
        <div class="choices">${categories.map((c) => `
          <label class="choice"><input type="${single ? 'radio' : 'checkbox'}" name="cat" value="${c.category}" ${chosen.some((x) => x.category === c.category) ? 'checked' : ''}><span><b>${h(c.title)}</b><span class="sub">${h(CATEGORY_HINT[c.category] ?? '')}</span></span></label>`).join('')}</div>
        <div class="actions">${cancelButton}<button id="next" type="button" class="btn btn-primary">下一步</button></div>`);
      $view.querySelector('.cancel')?.addEventListener('click', () => renderSettings());
      $view.querySelector('#next').addEventListener('click', () => {
        chosen = [...$view.querySelectorAll('input[name=cat]:checked')].map((el) => categories.find((c) => c.category === el.value));
        if (!chosen.length) {
          UI.toastError('至少選一個目標');
          return;
        }
        renderStep2();
      });
    };

    const renderStep2 = () => {
      $view.innerHTML = stepWin(2, '回答幾個問題', `
        <form id="qa">${chosen.map((c) => `
          <div class="plan-title">${h(c.title)}</div>
          ${c.questions.map((q) => `<label class="field">${h(q.label)}${questionInput(q, `${c.category}.${q.key}`, answers[c.category]?.[q.key])}</label>`).join('')}`).join('')}
          <div class="actions"><button type="button" class="btn btn-ghost back">上一步</button><button type="submit" class="btn btn-primary">預覽計畫</button></div>
        </form>`);
      $view.querySelector('.back').addEventListener('click', renderStep1);
      const form = $view.querySelector('#qa');
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        chosen.forEach((c) => {
          answers[c.category] = Object.fromEntries(c.questions.map((q) => [q.key, form.elements[`${c.category}.${q.key}`].value]));
        });
        const submit = form.querySelector('button[type="submit"]');
        submit.disabled = true;
        try {
          const preview = await api('POST', '/goals/preview', { goals: goalsBody(), basicQuestIndexes: [] });
          renderStep3(preview);
        } catch (err) {
          showError(err);
        } finally {
          submit.disabled = false;
        }
      });
    };

    const renderStep3 = (preview) => {
      const replaced = new Set(chosen.flatMap((c) => c.replacesBasicQuestIndexes));
      const plan = preview.goals.map((g) => `
        <div class="plan-title">${h(g.title)}</div>
        ${g.quests.map((q) => `<div class="plan-quest"><b>${h(q.name)}</b><span class="sub">${h(q.startLabel)} → ${h(q.endLabel)}，共 ${q.stageCount} 階，每 ${q.daysPerStep} 天達標就${h(q.stepLabel)}</span></div>`).join('')}`).join('');
      const basics = single ? '' : UI.win({ title: '基本任務', body: `
        <p class="sub">也可以一併加入這些日常任務，被目標取代的已排除。</p>
        <div class="choices">${defs.basicQuests.map((b) => `<label class="choice"><input type="checkbox" name="basic" value="${b.index}" ${replaced.has(b.index) ? 'disabled' : 'checked'}><span>${h(b.name)} <span class="chip">${h(b.statType)}</span></span></label>`).join('')}</div>` });
      $view.innerHTML = `
        ${stepWin(3, '你的計畫', plan)}
        ${basics}
        <div class="actions"><button type="button" class="btn btn-ghost back">上一步</button><button id="confirm" type="button" class="btn btn-primary">開始</button></div>`;
      $view.querySelector('.back').addEventListener('click', renderStep2);
      $view.querySelector('#confirm').addEventListener('click', async (e) => {
        const btn = e.currentTarget;
        const basicQuestIndexes = single ? [] : [...$view.querySelectorAll('input[name=basic]:checked')].map((el) => Number(el.value));
        btn.disabled = true;
        try {
          await api('POST', '/goals', { goals: goalsBody(), basicQuestIndexes });
          UI.sysMessage(['計畫已建立。', '系統將依計畫發布每日任務。']);
          if (single) {
            await renderSettings();
          } else {
            go('#today');
          }
        } catch (err) {
          showError(err);
        } finally {
          btn.disabled = false;
        }
      });
    };

    renderStep1();
  }
```

- [ ] **Step 2: 改寫 settings 區段**

Edit `app.js`，把從 `  /* ---------- settings ---------- */` 到舊 `renderSettings` 結尾（緊接在 `  /* ---------- router ---------- */` 之前的空行為止）整段替換為：

```js
  /* ---------- settings ---------- */
  function questForm(q) {
    const isCheck = (q?.questType ?? 'Check') === 'Check';
    return `
      <form class="quest-form" data-id="${q?.id ?? ''}">
        <span class="win-step">${q ? 'EDIT QUEST' : 'NEW QUEST'}</span>
        <label class="field">名稱<input name="name" type="text" required maxlength="60" value="${h(q?.name ?? '')}"></label>
        <div class="form-row">
          <label class="field">屬性<select name="statType">${STAT_ORDER.map((s) => `<option value="${s}" ${q?.statType === s ? 'selected' : ''}>${STAT_NAMES[s]} ${s}</option>`).join('')}</select></label>
          <label class="field">難度<select name="difficulty">${DIFFICULTIES.map((d) => `<option ${q?.difficulty === d ? 'selected' : ''}>${d}</option>`).join('')}</select></label>
        </div>
        <div class="form-row">
          <label class="field">類型<select name="questType">${Object.entries(QUEST_TYPES).map(([k, v]) => `<option value="${k}" ${q?.questType === k ? 'selected' : ''}>${v} ${k}</option>`).join('')}</select></label>
          <label class="field">單位<input name="unit" type="text" maxlength="10" value="${h(q?.unit ?? '')}" ${isCheck ? 'disabled' : ''}></label>
        </div>
        <div class="form-row">
          <label class="field">目標值<input name="targetValue" type="number" inputmode="decimal" min="0.01" step="0.01" value="${num(q?.targetValue)}" ${isCheck ? 'disabled' : 'required'}></label>
          <label class="field">每次增減<input name="step" type="number" inputmode="decimal" min="0" step="0.01" value="${num(q?.step)}" ${isCheck ? 'disabled' : ''}></label>
        </div>
        <div class="actions">
          <button type="button" class="btn btn-ghost cancel">取消</button>
          <button type="submit" class="btn btn-primary">${q ? '儲存' : '新增'}</button>
        </div>
      </form>`;
  }

  async function renderSettings(isCurrent = () => true) {
    renderHeader();
    const [quests, goalsRes] = await Promise.all([api('GET', '/quests'), api('GET', '/goals')]);
    if (!isCurrent()) {
      return;
    }
    const goals = goalsRes.goals;
    const activeCategories = goals.map((g) => g.category);
    const { player, user, program } = state.me;
    const goalList = goals.length ? goals.map((g) => `
      <div class="goal-row" data-id="${g.id}">
        <div class="goal-main">
          <b>${h(g.title)}</b>
          <span class="sub">自 ${h(g.startDate)} 起，${g.lengthDays} 天</span>
          ${g.quests.map((q) => `<span class="sub">${q.isArchived ? '（已封存）' : ''}${h(q.name)} · 第 ${q.stage}/${q.stageCount} 階</span>`).join('')}
        </div>
        <button type="button" class="btn btn-danger archive-goal">封存</button>
      </div>`).join('') : '<p class="sub">還沒有目標</p>';
    const questList = quests.filter((q) => !q.goalId).map((q) => `
      <div class="quest-admin" data-id="${q.id}">
        <div class="quest-admin-name">
          <span>${h(q.name)}</span>
          <span class="chips"><span class="chip">${q.statType}</span><span class="chip">${h(q.difficulty)}</span><span class="chip">${QUEST_TYPES[q.questType]}</span></span>
        </div>
        <button type="button" class="btn btn-ghost edit">編輯</button>
        <button type="button" class="btn btn-danger archive">封存</button>
      </div>`).join('');
    $view.innerHTML = `
      ${UI.win({ title: '困難模式', body: `
        <label class="switch">
          <span>啟用困難模式<span class="sub">門檻 100%，漏一天扣 EXP</span></span>
          <input id="hard" class="sr-only" type="checkbox" role="switch" ${player.hardMode ? 'checked' : ''}>
          <span class="switch-track" aria-hidden="true"></span>
        </label>` })}
      ${UI.win({ title: '目標', body: `
        ${goalList}
        <div class="actions"><button id="add-goal" type="button" class="btn btn-primary">＋ 新增目標</button></div>` })}
      ${UI.win({ title: '任務管理', body: `
        <div id="quest-admin-list">${questList || '<p class="sub">沒有一般任務</p>'}</div>
        <div id="quest-editor" class="quest-editor"></div>
        <div class="actions"><button id="add-quest" type="button" class="btn btn-primary">＋ 新增任務</button></div>` })}
      ${UI.win({ title: '66 天計畫', body: `
        <p>目前第 ${program.cycle} 週期，自 <span class="num">${h(program.startDate)}</span> 起，第 ${program.dayNumber} 天。</p>
        <div class="actions"><button id="restart" type="button" class="btn btn-danger">開新 66 天</button></div>` })}
      ${UI.win({ title: '帳號', body: `
        <p class="account">${h(user.email)}<span class="sub">時區 ${h(user.timeZoneId)}</span></p>
        <div class="actions"><button id="logout" type="button" class="btn btn-ghost">登出</button></div>` })}`;

    const $editor = $view.querySelector('#quest-editor');
    const refresh = async () => {
      state.me = await api('GET', '/me');
      await renderSettings();
    };
    const bindForm = (q) => {
      $editor.innerHTML = questForm(q);
      const form = $editor.querySelector('form');
      const sync = () => {
        const isCheck = form.questType.value === 'Check';
        ['unit', 'targetValue', 'step'].forEach((n) => {
          form[n].disabled = isCheck;
        });
        form.targetValue.required = !isCheck;
      };
      form.questType.addEventListener('change', sync);
      form.querySelector('.cancel').addEventListener('click', () => {
        $editor.innerHTML = '';
      });
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const isCheck = form.questType.value === 'Check';
        const body = {
          name: form.name.value,
          statType: form.statType.value,
          difficulty: form.difficulty.value,
          questType: form.questType.value,
          targetValue: isCheck || form.targetValue.value === '' ? null : Number(form.targetValue.value),
          step: isCheck || form.step.value === '' ? null : Number(form.step.value),
          unit: isCheck || form.unit.value === '' ? null : form.unit.value,
        };
        const submit = form.querySelector('button[type="submit"]');
        submit.disabled = true;
        try {
          if (q) {
            await api('PUT', `/quests/${q.id}`, body);
          } else {
            await api('POST', '/quests', body);
          }
          UI.sysMessage([q ? `任務「${body.name}」已更新。` : `新任務「${body.name}」已登錄。`]);
          await refresh();
        } catch (err) {
          showError(err);
        } finally {
          submit.disabled = false;
        }
      });
    };

    $view.querySelector('#hard').addEventListener('change', async (e) => {
      const input = e.target;
      input.disabled = true;
      try {
        state.me = await api('PATCH', '/me', { hardMode: input.checked });
        renderHeader();
        UI.sysMessage([input.checked ? '困難模式已開啟。達標門檻提高為 100%。' : '困難模式已關閉。']);
      } catch (err) {
        input.checked = !input.checked;
        showError(err);
      } finally {
        input.disabled = false;
      }
    });
    $view.querySelector('#add-quest').addEventListener('click', () => bindForm(null));
    $view.querySelectorAll('.quest-admin .edit').forEach((b) => b.addEventListener('click', () => {
      bindForm(quests.find((q) => q.id === b.closest('.quest-admin').dataset.id));
      $editor.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    }));
    $view.querySelectorAll('.quest-admin .archive').forEach((b) => UI.confirmButton(b, '封存', async () => {
      await api('DELETE', `/quests/${b.closest('.quest-admin').dataset.id}`);
      UI.sysMessage(['任務已封存。']);
      await refresh();
    }));
    $view.querySelectorAll('.goal-row .archive-goal').forEach((b) => UI.confirmButton(b, '封存', async () => {
      await api('DELETE', `/goals/${b.closest('.goal-row').dataset.id}`);
      UI.sysMessage(['目標已封存。']);
      await refresh();
    }));
    $view.querySelector('#add-goal').addEventListener('click', () => renderOnboarding({ single: true, excluded: activeCategories }));
    UI.confirmButton($view.querySelector('#restart'), '開新 66 天', async () => {
      await api('POST', '/program/restart');
      UI.sysMessage(['新的 66 天計畫已開始。']);
      await refresh();
    });
    $view.querySelector('#logout').addEventListener('click', logout);
  }
```

- [ ] **Step 3: 刪除 `toast` 過渡函式**

Edit `app.js`，old_string：
```js
  // 成功訊息走系統訊息，錯誤走紅色 toast
  function toast(msg, ok = false) {
    if (ok) {
      UI.sysMessage([msg]);
    } else {
      UI.toastError(msg);
    }
  }

```
new_string：空字串。

- [ ] **Step 4: 設定與引導樣式，刪除 legacy 區塊**

Edit `app.css`，把從 `/* legacy:start 舊版畫面沿用的樣式；各畫面改版時刪除對應段落 */` 到 `/* legacy:end */`（含兩行標記；此時其中只剩 `legacy: shared`、`legacy: settings`、`legacy: onboarding` 三段）整段替換為：

```css
/* ---- 引導 ---- */
.win-step { display: block; margin: -4px 0 10px; color: var(--accent); font: 600 11px var(--latin); letter-spacing: .24em; text-align: center; }
.choices { display: grid; gap: 8px; margin: 12px 0; }
.choice {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  min-height: var(--tap);
  padding: 10px;
  border: 1px solid var(--line-soft);
  cursor: pointer;
  overflow-wrap: anywhere;
}
.choice:has(input:checked) { border-color: var(--accent); box-shadow: inset 0 0 12px var(--line-soft); }
.choice:has(input:disabled) { opacity: .5; cursor: default; }
.choice input { flex: none; margin-top: 2px; }
.choice .chip { margin-left: 4px; padding: 0 6px; font-size: 11px; }
.plan-title { margin: 12px 0 4px; color: var(--accent); font: 600 13px var(--serif); letter-spacing: .1em; }
.plan-quest { display: grid; gap: 2px; padding: 6px 0; border-top: 1px dashed var(--line-soft); overflow-wrap: anywhere; }

/* ---- 設定 ---- */
.switch { position: relative; display: flex; align-items: center; justify-content: space-between; gap: 12px; min-height: var(--tap); cursor: pointer; }
.switch-track { position: relative; flex: none; width: 48px; height: 26px; border: 1px solid var(--line); }
.switch-track::before {
  content: "";
  position: absolute;
  top: 3px;
  left: 3px;
  width: 18px;
  height: 18px;
  background: var(--muted);
  transition: transform .2s ease;
}
.switch input:checked + .switch-track { border-color: var(--accent); box-shadow: 0 0 10px color-mix(in srgb, var(--accent) 45%, transparent); }
.switch input:checked + .switch-track::before { background: var(--accent); transform: translateX(22px); }
.switch input:focus-visible + .switch-track { outline: 2px solid var(--accent); outline-offset: 2px; }
.switch input:disabled + .switch-track { opacity: .5; }
.goal-row, .quest-admin { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; padding: 8px 0; border-top: 1px dashed var(--line-soft); }
.goal-row:first-child, .quest-admin:first-child { border-top: 0; }
.goal-main { flex: 1 1 160px; min-width: 0; display: grid; gap: 2px; overflow-wrap: anywhere; }
.quest-admin-name { flex: 1 1 140px; min-width: 0; display: grid; gap: 4px; overflow-wrap: anywhere; }
.quest-admin .chip { padding: 1px 6px; font-size: 11px; }
.quest-editor:not(:empty) { margin-top: 10px; padding-top: 10px; border-top: 1px solid var(--line-soft); }
.account { overflow-wrap: anywhere; }
```

- [ ] **Step 5: 語法、殘留與色碼檢查、建置與測試**

Run：
```bash
node --check src/SoloLeveling.Api/wwwroot/app.js
grep -n "legacy" src/SoloLeveling.Api/wwwroot/app.css
grep -nE "toast\(|class=\"card|class=\"small|\"small |section-title|badge|qrow|class=\"goal\"|armConfirm" src/SoloLeveling.Api/wwwroot/app.js
awk '/tokens:start/{t=1} /tokens:end/{t=0; next} !t && /#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(|hsla?\(/ {print FNR": "$0}' src/SoloLeveling.Api/wwwroot/app.css
grep -nE "#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(" src/SoloLeveling.Api/wwwroot/ui.js src/SoloLeveling.Api/wwwroot/app.js src/SoloLeveling.Api/wwwroot/index.html
wc -l src/SoloLeveling.Api/wwwroot/app.js src/SoloLeveling.Api/wwwroot/ui.js
dotnet build
dotnet test
```
Expected: `node --check` 與後四個 grep／awk 全無輸出；`app.js` 約 620 行、`ui.js` 約 230 行（各 ≤ 700）；建置 0 警告；207 個測試、Failed 0。

- [ ] **Step 6: 重新部署**

Run: `docker compose up -d --build`
Expected: http://localhost:8080/ 可開。

- [ ] **Step 7: 手動檢查：引導**

無痕視窗註冊新帳號 `ui6@example.com`／`password123`／`六號玩家`。

1. 引導頁無導覽；頂部精簡狀態面板；視窗標題「先設定你的目標」，標題下方藍色「STEP 1 / 3」；四個類別為整列可點的選項框，勾選後框線變主色。沒勾就按「下一步」→ 紅色 toast「至少選一個目標」。
2. 勾「閱讀」「運動」→ 下一步 →「STEP 2 / 3」「回答幾個問題」，每個類別一個主色宋體小標與欄位；改一個欄位值 → 按「上一步」→ 兩個類別仍是勾選狀態 → 再「下一步」→ 剛改的值仍在。
3. 「預覽計畫」→「STEP 3 / 3」「你的計畫」列出起終點與階數；「基本任務」視窗中被目標取代的選項變淡且無法勾選。Network 節流 Slow 3G 後連點「開始」兩下 → 只有一筆 `POST /goals`；完成後系統訊息「計畫已建立。／系統將依計畫發布每日任務。」並進今日頁。關閉節流。

- [ ] **Step 8: 手動檢查：設定**

1. 「設定」頁依序有五個系統視窗：困難模式、目標、任務管理、66 天計畫、帳號。
2. 困難模式開關為方形軌道，開啟時滑塊移到右側並發主色光；切換後系統訊息「困難模式已開啟。達標門檻提高為 100%。」；回今日頁狀態面板出現「困難模式」紅點籤、門檻 100%。關閉後訊息「困難模式已關閉。」。
3. 目標視窗列出「閱讀」「運動」與各任務「第 1/N 階」；「＋ 新增目標」→ 單一類別引導（STEP 1 / 3、單選、有「取消」），導覽仍顯示且「設定」為作用中；按「取消」回設定頁。把剩下兩個類別都建立後再按「＋ 新增目標」→ 顯示「所有目標類別都已在進行中。」與「取消」。
4. 任務管理：「＋ 新增任務」展開表單（上方「NEW QUEST」），類型選「勾選」時單位、目標值、每次增減停用；新增後訊息「新任務「…」已登錄。」；「編輯」捲到表單（「EDIT QUEST」），儲存後「任務「…」已更新。」。
5. 「封存」按一下變「再按一次確認封存」黃框，3 秒不按還原；按兩下 → 「任務已封存。」並從清單消失。目標的「封存」同樣流程，訊息「目標已封存。」。
6. 「開新 66 天」兩段確認後訊息「新的 66 天計畫已開始。」，週期數 +1。
7. 帳號視窗顯示 Email 與「時區 …」；「登出」回登入頁。
8. Console 依序 `UI.setAccent('violet')`、`UI.setAccent('jade')` 檢視設定頁與引導頁，開關、選項框、按鈕、封存黃框都依主題著色，沒有任何元素停留在藍色。

- [ ] **Step 9: Commit**

```bash
git add src/SoloLeveling.Api/wwwroot/app.js src/SoloLeveling.Api/wwwroot/app.css
git commit -m "feat: 設定與引導畫面改為系統視窗並移除舊樣式" -m "1. 設定拆成五個系統視窗，封存與開新週期改用 UI.confirmButton，成功訊息走系統訊息
2. 引導步驟標示 STEP n / 3，回上一步保留已選類別與已填答案，送出期間停用按鈕避免重複建立
3. 所有畫面已改版，刪除 legacy 過渡樣式與 toast 過渡函式" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4"
```

---

### Task 7: 響應式、觸控、safe-area、減少動態與文件

**Files:**
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（檔尾新增寬窄螢幕與減少動態區段）
- Modify: `docs/ARCHITECTURE.md:102`、`:179-187`
- Modify: `CLAUDE.md:44`、`:82`

**Interfaces:**
- Consumes: 前面所有 Task 的類別與代號（`--gutter`、`--safe-*`、`.nav`、`.form-row`、`.win`、`.sysmsg`）。
- Produces: 無新名稱；文件描述 `window.UI`、`NAV_ITEMS`、`announce`、`seen:<userId>` 與 tokens 規則。

- [ ] **Step 1: 檔尾加入響應式與減少動態**

Edit `app.css`，old_string（Task 6 加入的最後一行）：
```css
.account { overflow-wrap: anywhere; }
```
new_string：
```css
.account { overflow-wrap: anywhere; }

/* ---- 寬螢幕：導覽格置中，不拉滿整列 ---- */
@media (min-width: 720px) {
  .nav { justify-content: center; grid-auto-columns: minmax(0, 160px); }
}

/* ---- 窄螢幕 ---- */
@media (max-width: 360px) {
  :root { --gutter: 12px; }
  .win { padding: 10px 12px; }
  .form-row { grid-template-columns: 1fr; }
}

/* ---- 減少動態：關閉光暈呼吸、日曆閃爍與訊息位移 ---- */
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation: none !important;
    transition: none !important;
    scroll-behavior: auto !important;
  }
  .sysmsg { transform: none; }
}
```

- [ ] **Step 2: 語法、色碼、建置與測試**

Run：
```bash
awk '/tokens:start/{t=1} /tokens:end/{t=0; next} !t && /#[0-9a-fA-F]{3,8}([^0-9a-zA-Z_-]|$)|rgba?\(|hsla?\(/ {print FNR": "$0}' src/SoloLeveling.Api/wwwroot/app.css
dotnet build
dotnet test
docker compose up -d --build
```
Expected: awk 無輸出；建置 0 警告；207 個測試、Failed 0；容器啟動。

- [ ] **Step 3: 手動檢查：Review Focus 5（375px 最長內容）與觸控尺寸**

DevTools 裝置模擬「iPhone SE」（375×667）。先在設定頁新增一般任務，名稱為 60 個無空白字元 `ＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡＡ`，類型「累計」、單位「分鐘」、目標值 120、每次增減 5。

在下列每個畫面狀態各執行一次 Console：
```js
[document.documentElement.scrollWidth <= window.innerWidth,
 [...document.querySelectorAll('button, a, select, textarea, input:not([type=checkbox]):not([type=radio]):not(.sr-only)')]
   .filter((e) => e.offsetParent && (e.getBoundingClientRect().width < 40 || e.getBoundingClientRect().height < 40))
   .map((e) => e.outerHTML.slice(0, 80))]
```
Expected: 每次都是 `[true, []]`。畫面狀態：
1. 登入頁、註冊頁。
2. 引導 STEP 1、STEP 2、STEP 3（用另一個新帳號走一次）。
3. 今日頁（含上述長名稱的累計任務：名稱換行、右側「− 0 / 120 ＋」不被擠出畫面）。
4. 進度頁（日曆一列 7 格、7 天表長名稱以「…」截斷）。
5. 設定頁：展開「＋ 新增任務」表單；以及把長名稱任務的「封存」按一下變成「再按一次確認封存」的當下。

再切到 320×568（DevTools 自訂裝置）重看今日、進度、設定：`scrollWidth <= innerWidth` 仍為 `true`，表單欄位變單欄。

- [ ] **Step 4: 手動檢查：safe-area、寬螢幕與減少動態**

1. **safe-area**：DevTools Elements 選 `<html>`，在 Styles 加 `--safe-top: 47px; --safe-bottom: 34px; --safe-left: 20px; --safe-right: 20px;`：狀態面板上方多 47px；底部導覽下方多 34px 內距且格子左右內縮；內容底部留白跟著加大，最後一個視窗不被導覽蓋住；觸發 `UI.sysMessage(['safe-area'])` 時訊息框在 47px 之下；觸發 `UI.toastError('safe-area')` 時 toast 在導覽之上。移除這些覆寫。
2. **寬螢幕**：視窗寬 1280px：內容欄最寬 640px 置中；底部導覽三格各 160px 置中、作用中格頂端發光線仍在該格中間。
3. **減少動態**：DevTools Rendering 面板「Emulate CSS media feature prefers-reduced-motion」選 `reduce`：今日頁 LEVEL 方塊不再呼吸；進度頁今日格不閃；系統訊息直接出現與消失、沒有上移位移，3.2 秒後仍會自動關閉並換下一則；設定頁「編輯」直接跳到表單不平滑捲動。改回 `No emulation`。
4. **觸控**：DevTools 裝置模擬下快速雙擊任務的勾選框或「＋」，頁面不會被雙擊放大。

- [ ] **Step 5: 手動檢查：三主題五畫面**

依序 `UI.setAccent('azure')`、`'violet'`、`'jade'`，每個主題各看一次今日、進度、設定、引導（用 `#onboarding` 只有新帳號可進，可用 Task 6 Step 8 的「新增目標」單一類別流程代替）、登入（登出後）五個畫面：背景霧光、視窗框、標題細線、按鈕、數據籤、日曆、勾選框、系統訊息都換成該主題色；錯誤 toast 一律紅色。

- [ ] **Step 6: Commit 樣式**

```bash
git add src/SoloLeveling.Api/wwwroot/app.css
git commit -m "feat: 補齊寬窄螢幕與減少動態設定" -m "1. 寬螢幕導覽格改為置中定寬，避免三格拉滿整列；360px 以下縮小邊距並讓表單改單欄
2. 系統偏好減少動態時關閉光暈呼吸、日曆閃爍與訊息位移" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4"
```

- [ ] **Step 7: 更新 `docs/ARCHITECTURE.md`**

Edit，old_string：
```markdown
| `wwwroot/` | `index.html`、`app.js`、`app.css` |
```
new_string：
```markdown
| `wwwroot/` | `index.html`、`app.css`、`ui.js`（共用元件）、`app.js`（路由與畫面） |
```

Edit，old_string：
```markdown
- 位置：`src/SoloLeveling.Api/wwwroot`（`index.html`、`app.js`、`app.css`），由 `UseDefaultFiles`＋`UseStaticFiles` 提供。純 HTML＋JS，無建置步驟。
```
new_string：
```markdown
- 位置：`src/SoloLeveling.Api/wwwroot`（`index.html`、`app.css`、`ui.js`、`app.js`），由 `UseDefaultFiles`＋`UseStaticFiles` 提供。純 HTML＋JS，無建置步驟；`index.html` 依序載入 `ui.js` 再 `app.js`。
- **分工**：`ui.js` 以 `window.UI` 匯出共用元件（`h`、`win`、`statusPanel`、`sysMessage`、`toastError`、`confirmButton`、`setAccent`、`ApiError`），不呼叫 API、不處理路由；`app.js` 負責路由、API 呼叫與各畫面。
- **樣式與主題**：色碼只能寫在 `app.css` 的 `tokens:start`～`tokens:end` 區塊，其他地方一律用設計代號；`<html data-accent="azure|violet|jade">` 切換三套色票，預設 `azure`（`UI.setAccent(key)`）。字體由 Google Fonts 載入（Noto Serif TC／Noto Sans TC／IBM Plex Mono／Chakra Petch），皆有系統後備字。
```

Edit，old_string：
```markdown
- **路由**：hash 路由 `#login`、`#register`、`#today`、`#progress`、`#settings`，監聽 `hashchange`；無 token 時一律顯示登入／註冊。
```
new_string：
```markdown
- **路由**：hash 路由 `#login`、`#register`、`#onboarding`、`#today`、`#progress`、`#settings`，監聽 `hashchange`；無 token 時一律顯示登入／註冊。底部導覽由 `app.js` 的 `NAV_ITEMS` 產生；導向用 `go(hash)`（hash 已相同時直接重跑路由）；快速連續切換時只有最後一次路由會寫入畫面。
```

Edit，old_string：
```markdown
- **資料流**：每次切換畫面先並行取 `GET /me` 與 `GET /today`；勾選或輸入進度時呼叫 `PUT /today/quests/{id}/progress`，以回應覆蓋今日資料，再重取 `/me` 更新等級與屬性。前端不計算 EXP、等級、達標率。
- **錯誤顯示**：讀取回應的 `error.message` 以 toast 顯示。
```
new_string：
```markdown
- **資料流**：每次切換畫面先並行取 `GET /me` 與 `GET /today`；勾選或輸入進度時呼叫 `PUT /today/quests/{id}/progress`，以回應覆蓋今日資料，再重取 `/me` 更新等級與屬性。同一時間只送一個進度請求，連點時忽略後續點擊。前端不計算 EXP、等級、達標率。
- **系統訊息**：成功與事件通知走 `UI.sysMessage`（排隊、3.2 秒或點擊關閉）。`announce(prevMe, prevToday, me, today)` 比對前後狀態宣告任務完成、今日達標、升級、目標升階；上一次看到的等級與各漸進任務階段存在 `localStorage` 的 `seen:<userId>`，跨次開啟也能宣告升級與升階。
- **錯誤顯示**：`api()` 把錯誤回應與網路失敗轉成 `UI.ApiError`，由 `showError` 以紅色 toast 顯示 `error.message`；其他例外照常往上丟。
```

- [ ] **Step 8: 更新 `CLAUDE.md`**

Edit，old_string：
```markdown
- **前端不計算 EXP／等級**，一律以 API 回傳值覆蓋畫面。
```
new_string：
```markdown
- **前端不計算 EXP／等級**，一律以 API 回傳值覆蓋畫面。
- **前端色碼只能寫在 `app.css` 的 `tokens:start`～`tokens:end` 區塊**，其他地方用設計代號；主題以 `<html data-accent>` 切換。共用元件走 `ui.js` 的 `window.UI`，分頁加在 `app.js` 的 `NAV_ITEMS`，系統訊息的觸發統一由 `announce()` 比對。
```

Edit，old_string：
```markdown
- 引導式目標與漸進任務功能完成，測試全綠（191 個：Domain 127 + Api 64）。
```
new_string：
```markdown
- 系統介面改版完成（純前端），測試全綠（207 個：Domain 142 + Api 65）。
```

- [ ] **Step 9: Commit 文件**

```bash
git add docs/ARCHITECTURE.md CLAUDE.md
git commit -m "docs: 更新前端架構說明與系統介面改版後的專案指引" -m "1. 前端拆成 ui.js 與 app.js、改用設計代號與系統訊息，架構文件同步描述分工、主題、路由與宣告機制
2. 專案指引加入色碼只寫在 tokens 區塊的規則，並更新目前測試數" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4"
```

- [ ] **Step 10: 回報**

停下來向使用者報告八顆 commit（Task 1～6 各一顆、Task 7 兩顆）各自涵蓋什麼，等待 review；不 push。

---

## Self-Review 紀錄

1. **Spec 覆蓋**：§1 設計代號與字體 → Task 1；§2 系統視窗 → Task 1（CSS）＋Task 2（`UI.win`）；狀態面板 → Task 2；任務列 → Task 4；系統訊息與錯誤 toast → Task 2；按鈕與二段式確認 → Task 1、Task 2；底部導覽 → Task 3；背景 → Task 1。§3 五個畫面 → Task 3（登入）、Task 4（今日）、Task 5（進度）、Task 6（設定、引導）；Header 改狀態面板（今日完整、其他精簡）→ Task 3、Task 4；日曆小方塊與點格看日期 → Task 5；系統訊息四種時機 → Task 3（`announce`）＋Task 4（觸發點）。§4 實作邊界、觸控、safe-area、減少動態 → Global Constraints、Task 7。§5 驗收：三主題五畫面 → Task 7 Step 5；四種訊息與紅色 toast → Task 4 Step 5；375px 與點格 → Task 5 Step 5、Task 7 Step 3；207 測試 → 每個 Task。
2. **Placeholder 掃描**：每個程式步驟都有完整程式碼；每個 Edit 都給出 old_string 或明確的起訖標記；沒有 TBD 或「同 Task N」。
3. **名稱一致性**：`renderHeader(compact = true)`、`renderProgress(isCurrent)`、`renderSettings(isCurrent)`、`announce(prevMe, prevToday, me, today, rewards)`、`readSeen`／`writeSeen`、`go`、`showError`、`setProgress`、`sameDayBase` 在定義與使用處一致；CSS 類別 `.ratio-row`、`.latin-label` 由 Task 4 定義、Task 5 沿用；`.win-step` 由 Task 6 定義，同 Task 的 `questForm` 與引導使用；`legacy` 段名在 Task 1 定義、Task 2～6 依序刪除。
4. **Review Focus**：五項各自對應到 Task 2／3／4／5／7 的手動檢查步驟。跨午夜的比對基準（`sameDayBase`）無法在本機手動重現，以程式註解說明並列入 Task 4 的 review 重點，不放進五項。
