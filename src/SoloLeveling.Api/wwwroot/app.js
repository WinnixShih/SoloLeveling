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
    { route: 'hunter', zh: '檔案', en: 'HUNTER' },
    { route: 'settings', zh: '設定', en: 'SYS' },
  ];

  const $header = document.getElementById('header');
  const $view = document.getElementById('view');
  const $nav = document.getElementById('nav');

  const state = { token: localStorage.getItem('token'), me: null, today: null, pendingRewards: [] };
  let routeSeq = 0;

  /* ---------- helpers ---------- */
  const { h } = UI;
  const num = (v) => (v === null || v === undefined ? '' : Number(v).toString());
  const addDays = (iso, n) => {
    const d = new Date(iso + 'T00:00:00Z');
    d.setUTCDate(d.getUTCDate() + n);
    return d.toISOString().slice(0, 10);
  };

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
    // 每個會改狀態的回應都帶 rewards；集中收集，由 announceRewards 依序顯示一次
    if (data && data.rewards) {
      state.pendingRewards.push(data.rewards);
    }
    return data;
  }

  function logout() {
    state.token = null;
    state.me = null;
    state.today = null;
    state.pendingRewards = [];
    UI.setAccent('azure');
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

  const CHEST_SOURCE_ZH = {
    LevelUp: '升級',
    RankUp: '晉階',
    Streak7: '連續 7 天',
    Streak30: '連續 30 天',
    GoalCompleted: '目標完成',
    Gate: '傳送門',
    Purchase: '商店',
  };

  // 取出並清空 api() 收集到的 rewards；每筆只會顯示一次
  function drainRewards() {
    const list = state.pendingRewards;
    state.pendingRewards = [];
    return list;
  }

  // 依序顯示：升級 → 晉階 → 傳送門（通關、失敗）→ 寶箱 → 成就 → 保險卡生效；傳送門的寶箱併在通關訊息裡
  function announceRewards(list, me) {
    if (!list || list.length === 0) {
      return;
    }
    const levels = list.reduce((sum, r) => sum + r.levelsGained, 0);
    const rankUps = list.flatMap((r) => r.rankUps);
    const shieldsGained = list.reduce((sum, r) => sum + r.shieldsGained, 0);
    const chests = list.flatMap((r) => r.newChests);
    const gateChests = chests.filter((c) => c.source === 'Gate');
    const otherChests = chests.filter((c) => c.source !== 'Gate');
    const gatesCleared = list.map((r) => r.gateCleared).filter(Boolean);
    const gatesFailed = list.map((r) => r.gateFailed).filter(Boolean);
    const achievements = list.flatMap((r) => r.newAchievements);
    const shieldUses = list.filter((r) => r.shieldsUsed.length > 0);
    if (levels > 0) {
      UI.sysMessage([`等級提升。目前 Lv.${me.player.level}。`]);
    }
    if (rankUps.length > 0) {
      UI.sysMessage(rankUps.map((rank) => `階級晉升：${rank} 級。`)
        .concat(shieldsGained > 0 ? [`獲得連勝保險卡 ${shieldsGained} 張。`] : []));
    }
    if (gatesCleared.length > 0) {
      UI.sysMessage(gatesCleared.map((rank) => `${rank} 級傳送門攻略完成。`)
        .concat(gateChests.map((c) => `獲得 ${c.rarity} 級寶箱。`))
        .concat(gateChests.length > 0 ? ['前往「檔案」開啟。'] : []));
    }
    gatesFailed.forEach((rank) => UI.sysMessage([`${rank} 級傳送門攻略失敗：漏的天數超過上限。`, '可以到「進度」重新挑戰。']));
    if (otherChests.length > 0) {
      UI.sysMessage(otherChests.map((c) => `獲得 ${c.rarity} 級寶箱（${CHEST_SOURCE_ZH[c.source] || c.source}）。`)
        .concat(['前往「檔案」開啟。']));
    }
    achievements.forEach((a) => UI.sysMessage([`成就解鎖「${a.name}」。`, `獲得稱號字塊「${a.titleText}」。`]));
    shieldUses.forEach((r) => UI.sysMessage([`連勝保險已生效，剩餘 ${r.shieldCount} 張。`]));
  }

  /**
   * 比對前後兩次 /me 與 /today，依序送出系統訊息：任務完成、今日達標、目標升階，最後接 rewards 的獎勵訊息。
   * 任務完成與達標只在兩份今日資料同一天、且前一份明確記錄為未完成（=== false）時宣告；
   * prevMe、prevToday 為 null（第一次看到這個帳號）時不宣告。rewards 預設取出 api() 累積的待顯示獎勵（升級、晉階、傳送門、寶箱、成就、保險卡）。
   */
  function announce(prevMe, prevToday, me, today, rewards = drainRewards()) {
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
    if (prevToday) {
      const stages = new Map(prevToday.quests.filter((q) => q.progression).map((q) => [q.id, q.progression.stage]));
      today.quests.forEach((q) => {
        const before = stages.get(q.id);
        if (q.progression && before !== undefined && q.progression.stage > before) {
          UI.sysMessage([`目標升階。「${q.name}」進入第 ${q.progression.stage}/${q.progression.stageCount} 階。`]);
        }
      });
    }
    announceRewards(rewards, me);
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
    UI.setAccent(me.player.themeKey || 'azure');
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
      // PUT 已生效，先更新今日資料；之後 GET /me 失敗也不會讓畫面停在過期狀態
      state.today = today;
      const me = await api('GET', '/me');
      announce(prevMe, sameDayBase(prevToday, today), me, today);
      writeSeen(me, today);
      state.me = me;
    } catch (err) {
      showError(err);
    } finally {
      progressBusy = false;
    }
    // 請求期間若已切到其他頁或被登出就不重繪，避免蓋掉新畫面
    if (state.today && $view.querySelector('#quest-list')) {
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
  async function renderOnboarding({ single, excluded = [], isCurrent = () => true }) {
    renderHeader();
    const defs = await api('GET', '/goals/categories');
    if (!isCurrent()) {
      return;
    }
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
            // 單一目標建立後直接重畫設定頁，不經 loadToday，獎勵在這裡顯示
            state.me = await api('GET', '/me');
            announceRewards(drainRewards(), state.me);
            if (isCurrent()) {
              await renderSettings();
            }
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

  // 進行中的門：等級、天數、已漏天數與門天數格的日曆；沒有進行中的門：列出六級門
  function gateWindow(gates, history, today) {
    const gate = gates.activeGate;
    if (!gate) {
      const rows = gates.tiers.map((t) => `
        <div class="gate-tier ${t.unlocked ? '' : 'locked'}">
          <div class="gate-main">
            <b>${h(t.rank)} 級門</b>
            <span class="sub">${h(t.lengthDays)} 天・最多漏 ${h(t.allowedMisses)} 天・已通關 ${h(t.clearCount)} 次</span>
            <span class="sub">通關獎勵 ${t.chests.map((r) => `${h(r)} 級寶箱`).join('、')}・首次通關解鎖「${h(t.achievementName)}」</span>
            ${t.unlocked ? '' : `<span class="sub">需先通關 ${h(t.requiredRank)} 級門</span>`}
          </div>
          ${t.unlocked
            ? `<button type="button" class="btn btn-primary open-gate" data-rank="${h(t.rank)}">開門</button>`
            : '<span class="gate-lock">LOCKED</span>'}
        </div>`).join('');
      return UI.win({ title: '傳送門', body: `<p class="sub">選一道已解鎖的門，開門當天為第 1 天。</p>${rows}` });
    }
    const byDate = Object.fromEntries(history.map((d) => [d.date, d]));
    const cells = Array.from({ length: gate.lengthDays }, (_, i) => {
      const date = addDays(gate.startDate, i);
      const { cls, label } = dayStatus(date, today, byDate[date]);
      return `<button type="button" class="day ${cls}" data-day="${i + 1}" data-date="${date}" data-status="${label}" aria-pressed="false" aria-label="第 ${i + 1} 天，${date}，${label}">${i + 1}</button>`;
    });
    return UI.win({ title: '傳送門', body: `
      <div class="ratio-row gate-head">
        <span>${h(gate.rank)} 級門 · 第 <b class="num">${h(gate.dayNumber)}</b> / ${h(gate.lengthDays)} 天</span>
        <span>已漏 <b class="num">${h(gate.misses)}</b> / ${h(gate.allowedMisses)} 天</span>
      </div>
      <div class="cal" id="cal">${cells.join('')}</div>
      <p class="cal-readout" id="cal-readout" aria-live="polite"></p>
      <div class="actions"><button id="abandon" type="button" class="btn btn-danger">放棄</button></div>` });
  }

  function bindCalendar() {
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

  // 開門單擊送出；放棄二段式確認。完成後重跑路由，由 loadToday 顯示回應帶回的獎勵
  function bindGateActions(gates) {
    const gate = gates.activeGate;
    if (gate) {
      bindCalendar();
      UI.confirmButton($view.querySelector('#abandon'), '放棄', async () => {
        const res = await api('POST', '/gates/active/abandon');
        // 門若在放棄前已判定通關或失敗，改由獎勵訊息說明，不顯示放棄
        if (!res.rewards.gateCleared && !res.rewards.gateFailed) {
          UI.sysMessage([`已放棄 ${gate.rank} 級傳送門。`, '可以重新選一道門挑戰。']);
        }
        await route();
      });
      return;
    }
    $view.querySelectorAll('.open-gate').forEach((btn) => btn.addEventListener('click', async () => {
      btn.disabled = true;
      try {
        const opened = await api('POST', '/gates', { rank: btn.dataset.rank });
        UI.sysMessage([`${opened.rank} 級傳送門已開啟。`, `${opened.lengthDays} 天內最多可漏 ${opened.allowedMisses} 天，今天是第 1 天。`]);
        await route();
      } catch (err) {
        showError(err);
      } finally {
        btn.disabled = false;
      }
    }));
  }

  async function renderProgress(isCurrent = () => true) {
    renderHeader();
    const { player } = state.me;
    const today = state.today.date;
    const gates = await api('GET', '/gates');
    if (!isCurrent()) {
      return;
    }
    // GET /gates 也會依讀取規則補判定通關與失敗，先顯示它帶回的獎勵
    announceRewards(drainRewards(), state.me);
    const gate = gates.activeGate;
    const gateEnd = gate ? addDays(gate.startDate, gate.lengthDays - 1) : null;
    const [history, week, quests] = await Promise.all([
      gate && gate.startDate <= today
        ? api('GET', `/history?from=${gate.startDate}&to=${gateEnd < today ? gateEnd : today}`)
        : Promise.resolve([]),
      api('GET', `/history?from=${addDays(today, -6)}&to=${today}`),
      api('GET', '/quests'),
    ]);
    if (!isCurrent()) {
      return;
    }
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
      ${gateWindow(gates, history, today)}
      ${UI.win({ title: '五維屬性', body: `
        <div class="stats">${STAT_ORDER.map((s) => `<div class="stat"><span class="latin-label">${s}</span><b class="num">${h(player.stats[s.toLowerCase()])}</b><span class="sub">${STAT_NAMES[s]}</span></div>`).join('')}</div>
        <p class="sub stats-foot">累計完成 ${player.totalCompleted} 次 · 最佳連續 ${player.bestStreak} 天</p>` })}
      ${UI.win({ title: '最近 7 天', body: `
        <table class="week">
          <thead><tr><th scope="col"><span class="sr-only">任務</span></th>${weekDates.map((d) => `<th scope="col" title="${d}"><span class="num">${d.slice(8)}</span><span class="sub">${weekdayOf(d)}</span></th>`).join('')}</tr></thead>
          <tbody>${weekRows}</tbody>
        </table>` })}`;
    bindGateActions(gates);
  }

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
    const theme = await Hunter.themeSection({ api, reload: route });
    if (!isCurrent()) {
      return;
    }
    const goals = goalsRes.goals;
    const activeCategories = goals.map((g) => g.category);
    const { player, user } = state.me;
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
      ${theme.html}
      ${UI.win({ title: '目標', body: `
        ${goalList}
        <div class="actions"><button id="add-goal" type="button" class="btn btn-primary">＋ 新增目標</button></div>` })}
      ${UI.win({ title: '任務管理', body: `
        <div id="quest-admin-list">${questList || '<p class="sub">沒有一般任務</p>'}</div>
        <div id="quest-editor" class="quest-editor"></div>
        <div class="actions"><button id="add-quest" type="button" class="btn btn-primary">＋ 新增任務</button></div>` })}
      ${UI.win({ title: '帳號', body: `
        <p class="account">${h(user.email)}<span class="sub">時區 ${h(user.timeZoneId)}</span></p>
        <div class="actions"><button id="logout" type="button" class="btn btn-ghost">登出</button></div>` })}`;

    theme.bind($view);
    const $editor = $view.querySelector('#quest-editor');
    // 非同步操作完成後，使用者可能已切到其他頁；只在仍停留設定頁時才重繪
    const onSettings = () => location.hash === '#settings';
    const refresh = async () => {
      state.me = await api('GET', '/me');
      announceRewards(drainRewards(), state.me);
      if (onSettings()) {
        await renderSettings();
      }
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
        announceRewards(drainRewards(), state.me);
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
    $view.querySelector('#add-goal').addEventListener('click', () => renderOnboarding({ single: true, excluded: activeCategories, isCurrent: onSettings }));
    $view.querySelector('#logout').addEventListener('click', logout);
  }

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
      } else if (current === 'hunter') {
        renderHeader(false);
        await Hunter.render({ api, view: $view, me: state.me, today: state.today, reload: route, isCurrent });
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
