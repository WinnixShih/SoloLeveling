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
            <span class="who-name">${h(me.user.displayName)}${pinnedThumb(p.pinnedCard)}</span>
            ${p.title ? `<span class="who-title">稱號　<em>${h(p.title)}</em></span>` : ''}
          </div>
          ${rank}
        </div>
        ${bar}
        <div class="chips">${chips.join('')}</div>
      </section>`;
  }

  /* ---------- 釘選卡小圖 ---------- */

  // 狀態面板名稱旁的釘選卡小圖；圖檔不存在時只剩稀有度色框
  function pinnedThumb(card) {
    if (!card) {
      return '';
    }
    return `<span class="pinned-thumb rarity-${h(card.rarity)}" title="${h(card.name)}"><img src="${h(card.image)}" alt="" onerror="this.remove()"></span>`;
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
