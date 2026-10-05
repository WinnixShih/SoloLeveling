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

  /* ---------- onboarding ---------- */
  const CATEGORY_HINT = {
    Routine: '早睡早起，每 3 天提早幾分鐘',
    Exercise: '每天運動的分鐘數逐步增加',
    Reading: '每天閱讀的分鐘數逐步增加',
    ScreenTime: '每天手機使用上限逐步降低',
  };

  function questionInput(q) {
    if (q.type === 'Time') return `<input type="time" name="${q.key}" required>`;
    const step = q.type === 'Integer' ? 1 : 0.25;
    return `<input type="number" name="${q.key}" required min="${num(q.min)}" max="${num(q.max)}" step="${step}" value="${num(q.default)}">`;
  }

  // single=true 時只做一個類別（設定頁「新增目標」），不顯示基本任務；excluded 為已有進行中的類別
  async function renderOnboarding({ single, excluded = [] }) {
    renderHeader();
    const defs = await api('GET', '/goals/categories');
    const categories = defs.categories.filter((c) => !excluded.includes(c.category));
    let step = 1;
    let chosen = [];
    const answers = {};

    const renderStep1 = () => {
      $view.innerHTML = `
        <div class="card">
          <h2>${single ? '新增目標' : '先設定你的目標'}</h2>
          <p class="sub">選擇想改善的項目，系統會依你的現況安排每天的任務，並逐步逼近目標。</p>
          <div class="choices">${categories.map((c) => `
            <label class="choice"><input type="${single ? 'radio' : 'checkbox'}" name="cat" value="${c.category}"><div><b>${h(c.title)}</b><div class="sub">${h(CATEGORY_HINT[c.category] ?? '')}</div></div></label>`).join('')}</div>
          <div class="actions"><button id="next" class="small primary">下一步</button></div>
        </div>`;
      $view.querySelector('#next').addEventListener('click', () => {
        chosen = [...$view.querySelectorAll('input[name=cat]:checked')].map((el) => categories.find((c) => c.category === el.value));
        if (!chosen.length) { toast('至少選一個目標'); return; }
        step = 2;
        renderStep2();
      });
    };

    const renderStep2 = () => {
      $view.innerHTML = `
        <div class="card">
          <h2>回答幾個問題</h2>
          <form id="qa">${chosen.map((c) => `
            <div class="section-title">${h(c.title)}</div>
            ${c.questions.map((q) => `<label class="field">${h(q.label)}${questionInput(q).replace('name="', `name="${c.category}.`)}</label>`).join('')}`).join('')}
            <div class="actions"><button type="button" class="small back">上一步</button><button type="submit" class="small primary">預覽計畫</button></div>
          </form>
        </div>`;
      $view.querySelector('.back').addEventListener('click', () => { step = 1; renderStep1(); });
      $view.querySelector('#qa').addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target;
        chosen.forEach((c) => {
          answers[c.category] = Object.fromEntries(c.questions.map((q) => [q.key, form[`${c.category}.${q.key}`].value]));
        });
        try {
          const preview = await api('POST', '/goals/preview', { goals: goalsBody(), basicQuestIndexes: [] });
          step = 3;
          renderStep3(preview, defs.basicQuests);
        } catch (err) {
          toast(err.message);
        }
      });
    };

    const goalsBody = () => chosen.map((c) => ({ category: c.category, answers: answers[c.category] }));

    const renderStep3 = (preview, basics) => {
      const replaced = new Set(chosen.flatMap((c) => c.replacesBasicQuestIndexes));
      $view.innerHTML = `
        <div class="card">
          <h2>你的計畫</h2>
          ${preview.goals.map((g) => `
            <div class="section-title">${h(g.title)}</div>
            ${g.quests.map((q) => `
              <div class="quest"><div class="name">${h(q.name)}<span class="sub">${h(q.startLabel)} → ${h(q.endLabel)}，共 ${q.stageCount} 階，每 ${q.daysPerStep} 天達標就${h(q.stepLabel)}</span></div></div>`).join('')}`).join('')}
        </div>
        ${single ? '' : `
        <div class="card">
          <h2>基本任務</h2>
          <p class="sub">也可以一併加入這些日常任務，被目標取代的已排除。</p>
          ${basics.map((b) => `<label class="choice"><input type="checkbox" name="basic" value="${b.index}" ${replaced.has(b.index) ? 'disabled' : 'checked'}><div>${h(b.name)}<span class="badge">${b.statType}</span></div></label>`).join('')}
        </div>`}
        <div class="actions"><button class="small back">上一步</button><button id="confirm" class="small primary">開始</button></div>`;
      $view.querySelector('.back').addEventListener('click', () => { step = 2; renderStep2(); });
      $view.querySelector('#confirm').addEventListener('click', async () => {
        const basicQuestIndexes = single ? [] : [...$view.querySelectorAll('input[name=basic]:checked')].map((el) => Number(el.value));
        try {
          await api('POST', '/goals', { goals: goalsBody(), basicQuestIndexes });
          toast('計畫已建立', true);
          if (single) { await renderSettings(); } else { location.hash = '#today'; }
        } catch (err) {
          toast(err.message);
        }
      });
    };

    renderStep1();
  }

  /* ---------- progress ---------- */
  async function renderProgress() {
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
    const byDate = Object.fromEntries(history.map((d) => [d.date, d]));
    const cells = [];
    for (let i = 0; i < program.lengthDays; i++) {
      const date = addDays(program.startDate, i);
      const d = byDate[date];
      let cls = '';
      if (date > today) cls = 'future';
      else if (date === today) cls = 'today' + (d?.isCleared ? ' cleared' : '');
      else cls = d?.isCleared ? 'cleared' : 'missed';
      cells.push(`<i class="${cls}" title="${date}">${i + 1}</i>`);
    }
    const weekDates = Array.from({ length: 7 }, (_, i) => addDays(today, i - 6));
    const weekByDate = Object.fromEntries(week.map((d) => [d.date, new Set(d.doneQuestIds)]));
    $view.innerHTML = `
      <div class="card">
        <h2>66 天計畫 · 第 ${program.cycle} 週期 · 第 ${program.dayNumber} 天${program.isCompleted ? '（已完成）' : ''}</h2>
        <div class="grid66">${cells.join('')}</div>
      </div>
      <div class="card">
        <h2>屬性</h2>
        <div class="stats">${STAT_ORDER.map((s) => `<div><b>${player.stats[s.toLowerCase()]}</b><span>${STAT_NAMES[s]}</span></div>`).join('')}</div>
        <div class="section-title" style="margin-top:12px">累計完成 ${player.totalCompleted} 次 · 最佳連續 ${player.bestStreak} 天</div>
      </div>
      <div class="card">
        <h2>最近 7 天</h2>
        <table class="dots">
          <tr><th></th>${weekDates.map((d) => `<th>${d.slice(5).replace('-', '/')}</th>`).join('')}</tr>
          ${quests.map((q) => `<tr><td class="name">${h(q.name)}</td>${weekDates.map((d) => `<td><span class="dot ${weekByDate[d]?.has(q.id) ? 'on' : ''}"></span></td>`).join('')}</tr>`).join('')}
        </table>
      </div>`;
  }

  /* ---------- settings ---------- */
  function questForm(q) {
    const isCheck = (q?.questType ?? 'Check') === 'Check';
    return `
      <form class="quest-form" data-id="${q?.id ?? ''}">
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
          <label class="field">目標值<input name="targetValue" type="number" min="0.01" step="0.01" value="${num(q?.targetValue)}" ${isCheck ? 'disabled' : 'required'}></label>
          <label class="field">每次增減<input name="step" type="number" min="0" step="0.01" value="${num(q?.step)}" ${isCheck ? 'disabled' : ''}></label>
        </div>
        <div class="actions">
          <button type="button" class="small cancel">取消</button>
          <button type="submit" class="small primary">${q ? '儲存' : '新增'}</button>
        </div>
      </form>`;
  }

  async function renderSettings() {
    renderHeader();
    const [quests, goalsRes] = await Promise.all([api('GET', '/quests'), api('GET', '/goals')]);
    const goals = goalsRes.goals;
    const activeCategories = goals.map((g) => g.category);
    const { player, user, program } = state.me;
    $view.innerHTML = `
      <div class="card">
        <label class="switch"><span>困難模式<span class="sub" style="display:block;color:var(--muted);font-size:12px">門檻 100%，漏一天扣 EXP</span></span><input id="hard" type="checkbox" ${player.hardMode ? 'checked' : ''}></label>
      </div>
      <div class="card">
        <h2>目標</h2>
        ${goals.length ? goals.map((g) => `
          <div class="goal" data-id="${g.id}">
            <div class="name"><b>${h(g.title)}</b><span class="sub">自 ${h(g.startDate)} 起，${g.lengthDays} 天</span>
              ${g.quests.map((q) => `<div class="sub">${q.isArchived ? '（已封存）' : ''}${h(q.name)} · 第 ${q.stage}／${q.stageCount} 階</div>`).join('')}
            </div>
            <button class="small danger archive-goal">封存</button>
          </div>`).join('') : '<div class="sub">還沒有目標</div>'}
        <div class="actions"><button id="add-goal" class="small primary">＋ 新增目標</button></div>
      </div>
      <div class="card">
        <h2>任務</h2>
        <div id="quest-list">${quests.filter((q) => !q.goalId).map((q) => `
          <div class="qrow" data-id="${q.id}">
            <div class="name">${h(q.name)}<span class="badge">${q.statType}</span><span class="badge">${h(q.difficulty)}</span><span class="badge">${QUEST_TYPES[q.questType]}</span></div>
            <button class="small edit">編輯</button>
            <button class="small danger archive">封存</button>
          </div>`).join('')}</div>
        <div id="quest-editor"></div>
        <div class="actions"><button id="add-quest" class="small primary">＋ 新增任務</button></div>
      </div>
      <div class="card">
        <h2>66 天計畫</h2>
        <div>目前第 ${program.cycle} 週期，自 ${h(program.startDate)} 起，第 ${program.dayNumber} 天。</div>
        <div class="actions"><button id="restart" class="small danger">開新 66 天</button></div>
      </div>
      <div class="card">
        <h2>帳號</h2>
        <div>${h(user.email)} · 時區 ${h(user.timeZoneId)}</div>
        <div class="actions"><button id="logout" class="small">登出</button></div>
      </div>`;

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
        ['unit', 'targetValue', 'step'].forEach((n) => { form[n].disabled = isCheck; });
        form.targetValue.required = !isCheck;
      };
      form.questType.addEventListener('change', sync);
      form.querySelector('.cancel').addEventListener('click', () => { $editor.innerHTML = ''; });
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
        try {
          if (q) await api('PUT', `/quests/${q.id}`, body);
          else await api('POST', '/quests', body);
          toast('已儲存', true);
          await refresh();
        } catch (err) {
          toast(err.message);
        }
      });
    };

    $view.querySelector('#hard').addEventListener('change', async (e) => {
      try {
        state.me = await api('PATCH', '/me', { hardMode: e.target.checked });
        renderHeader();
        toast(e.target.checked ? '已開啟困難模式' : '已關閉困難模式', true);
      } catch (err) {
        toast(err.message);
        e.target.checked = !e.target.checked;
      }
    });
    $view.querySelector('#add-quest').addEventListener('click', () => bindForm(null));
    $view.querySelectorAll('.qrow .edit').forEach((b) => b.addEventListener('click', () => {
      bindForm(quests.find((q) => q.id === b.closest('.qrow').dataset.id));
      $editor.scrollIntoView({ behavior: 'smooth' });
    }));
    // 封存與開新週期不用瀏覽器對話框，改成「再按一次確認」
    const armConfirm = (btn, label, action) => {
      btn.addEventListener('click', async () => {
        if (btn.dataset.armed !== '1') {
          btn.dataset.armed = '1';
          btn.textContent = `再按一次確認${label}`;
          setTimeout(() => { btn.dataset.armed = ''; btn.textContent = label; }, 3000);
          return;
        }
        try {
          await action();
          await refresh();
        } catch (err) {
          toast(err.message);
        }
      });
    };
    $view.querySelectorAll('.qrow .archive').forEach((b) => armConfirm(b, '封存', () => api('DELETE', `/quests/${b.closest('.qrow').dataset.id}`)));
    $view.querySelectorAll('.goal .archive-goal').forEach((b) => armConfirm(b, '封存', () => api('DELETE', `/goals/${b.closest('.goal').dataset.id}`)));
    $view.querySelector('#add-goal').addEventListener('click', () => renderOnboarding({ single: true, excluded: activeCategories }));
    armConfirm($view.querySelector('#restart'), '開新 66 天', () => api('POST', '/program/restart'));
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
