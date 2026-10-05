/* SoloLeveling 第一階段前端：純 HTML + JS，hash 路由，所有數值都由 API 回傳，前端不自行計算 EXP／等級。 */
(() => {
  const API = '/api/v1';
  const STAT_NAMES = { STR: '力量', VIT: '體力', INT: '智力', WIL: '意志', SPI: '精神' };
  const STAT_ORDER = ['STR', 'VIT', 'INT', 'WIL', 'SPI'];
  const DIFFICULTIES = ['Easy', 'Normal', 'Hard'];
  const QUEST_TYPES = { Check: '勾選', Count: '累計', Limit: '上限' };

  const $header = document.getElementById('header');
  const $view = document.getElementById('view');
  const $nav = document.getElementById('nav');

  const state = { token: localStorage.getItem('token'), me: null, today: null };

  /* ---------- helpers ---------- */
  const { h } = UI;
  const num = (v) => (v === null || v === undefined ? '' : Number(v).toString());
  const addDays = (iso, n) => {
    const d = new Date(iso + 'T00:00:00Z');
    d.setUTCDate(d.getUTCDate() + n);
    return d.toISOString().slice(0, 10);
  };
  const diffDays = (a, b) => Math.round((new Date(b + 'T00:00:00Z') - new Date(a + 'T00:00:00Z')) / 86400000);

  // 成功訊息走系統訊息，錯誤走紅色 toast
  function toast(msg, ok = false) {
    if (ok) {
      UI.sysMessage([msg]);
    } else {
      UI.toastError(msg);
    }
  }

  async function api(method, path, body) {
    const headers = { 'Content-Type': 'application/json' };
    if (state.token) headers.Authorization = 'Bearer ' + state.token;
    const res = await fetch(API + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    if (res.status === 401 && state.token) {
      logout();
      throw new Error('登入已過期，請重新登入');
    }
    if (res.status === 204) return null;
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error?.message || `請求失敗（${res.status}）`);
    return data;
  }

  function logout() {
    state.token = null;
    state.me = null;
    state.today = null;
    localStorage.removeItem('token');
    location.hash = '#login';
  }

  /* ---------- header ---------- */
  function renderHeader() {
    const p = state.me?.player;
    const t = state.today;
    if (!p) {
      $header.classList.add('hidden');
      return;
    }
    const pct = Math.min(100, Math.round((p.xp / p.xpNeeded) * 100));
    const ratio = t ? Math.round(t.completionRatio * 100) : null;
    $header.classList.remove('hidden');
    $header.innerHTML = `
      <div class="row">
        <div><span class="level">Lv.${p.level}</span> <span class="rank">${h(p.rank)} 級</span> <span class="title">${h(p.title)}</span></div>
        <div class="title">${h(state.me.user.displayName)}</div>
      </div>
      <div class="xpbar"><i style="width:${pct}%"></i></div>
      <div class="meta">
        <span>EXP <b>${p.xp}</b> / ${p.xpNeeded}</span>
        <span>連續 <b>${p.displayStreak}</b> 天</span>
        <span>最佳 <b>${p.bestStreak}</b></span>
        ${ratio === null ? '' : `<span>今日 <b>${ratio}%</b>${t.isCleared ? ' ✓' : ''}</span>`}
        ${p.hardMode ? '<span class="badge">困難模式</span>' : ''}
      </div>`;
  }

  /* ---------- auth ---------- */
  function renderLogin(mode = 'login') {
    $header.classList.add('hidden');
    $nav.classList.add('hidden');
    const isRegister = mode === 'register';
    $view.innerHTML = `
      <div class="auth">
        <h1>SoloLeveling</h1>
        <p>每天完成任務，累積經驗，升級自己。</p>
        <div class="tabs">
          <button data-mode="login" class="${isRegister ? '' : 'active'}">登入</button>
          <button data-mode="register" class="${isRegister ? 'active' : ''}">註冊</button>
        </div>
        <form id="auth-form">
          <label class="field">Email<input name="email" type="email" required autocomplete="email"></label>
          <label class="field">密碼${isRegister ? '（至少 8 碼）' : ''}<input name="password" type="password" required minlength="${isRegister ? 8 : 1}" autocomplete="${isRegister ? 'new-password' : 'current-password'}"></label>
          ${isRegister ? '<label class="field">顯示名稱<input name="displayName" type="text" required maxlength="40"></label>' : ''}
          <button class="primary" type="submit" style="width:100%;margin-top:8px">${isRegister ? '建立帳號' : '登入'}</button>
        </form>
      </div>`;
    $view.querySelectorAll('.tabs button').forEach((b) => b.addEventListener('click', () => renderLogin(b.dataset.mode)));
    $view.querySelector('#auth-form').addEventListener('submit', async (e) => {
      e.preventDefault();
      const f = new FormData(e.target);
      const body = { email: f.get('email'), password: f.get('password') };
      if (isRegister) {
        body.displayName = f.get('displayName');
        body.timeZoneId = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Asia/Taipei';
      }
      try {
        const res = await api('POST', isRegister ? '/auth/register' : '/auth/login', body);
        state.token = res.token;
        localStorage.setItem('token', res.token);
        location.hash = '#today';
      } catch (err) {
        toast(err.message);
      }
    });
  }

  /* ---------- today ---------- */
  async function loadToday() {
    [state.me, state.today] = await Promise.all([api('GET', '/me'), api('GET', '/today')]);
  }

  function questControl(q) {
    if (q.questType === 'Check') {
      return `<input class="check" type="checkbox" data-id="${q.id}" ${q.isDone ? 'checked' : ''}>`;
    }
    if (q.questType === 'Count') {
      return `
        <button class="small" data-id="${q.id}" data-delta="-1">−</button>
        <span class="value">${num(q.value ?? 0)} / ${num(q.targetValue)} ${h(q.unit ?? '')}</span>
        <button class="small" data-id="${q.id}" data-delta="1">＋</button>`;
    }
    return `<input class="inline" type="number" min="0" step="${num(q.step ?? 0.5)}" data-id="${q.id}" value="${num(q.value)}" placeholder="—"> <span class="sub">≤ ${num(q.targetValue)} ${h(q.unit ?? '')}</span>`;
  }

  function renderToday() {
    renderHeader();
    const t = state.today;
    const groups = STAT_ORDER.map((s) => [s, t.quests.filter((q) => q.statType === s)]).filter(([, qs]) => qs.length);
    const ratio = Math.round(t.completionRatio * 100);
    $view.innerHTML = `
      <div class="card summary">
        <div><div class="section-title" style="margin:0">今日 ${h(t.date)}</div><div>門檻 ${Math.round(t.threshold * 100)}%${t.bonusGranted ? '，已領達標獎勵 +30' : ''}</div></div>
        <div class="ratio ${t.isCleared ? 'cleared' : ''}">${ratio}%</div>
      </div>
      ${groups.map(([s, qs]) => `
        <div class="section-title">${STAT_NAMES[s]} · ${s}</div>
        <div class="card">
          ${qs.map((q) => `
            <div class="quest ${q.isDone ? 'done' : ''}">
              <div class="name">${h(q.name)}${q.progression ? `<span class="stage">第 ${q.progression.stage}／${q.progression.stageCount} 階</span>` : ''}<span class="sub">${h(q.difficulty)} · +${q.xpReward} EXP · +${q.statReward} ${s}</span></div>
              <div class="ctl">${questControl(q)}</div>
            </div>`).join('')}
        </div>`).join('')}
      <div class="card">
        <h2>今日反思</h2>
        <textarea id="note" rows="3" maxlength="2000" placeholder="寫點什麼…">${h(t.note ?? '')}</textarea>
        <div class="actions"><button id="save-note" class="small">儲存</button></div>
      </div>`;

    const questById = Object.fromEntries(t.quests.map((q) => [q.id, q]));
    const setProgress = async (id, value) => {
      try {
        state.today = await api('PUT', `/today/quests/${id}/progress`, { value });
        state.me = await api('GET', '/me');
        renderToday();
      } catch (err) {
        toast(err.message);
        renderToday();
      }
    };
    $view.querySelectorAll('input.check').forEach((el) => el.addEventListener('change', () => setProgress(el.dataset.id, el.checked ? 1 : 0)));
    $view.querySelectorAll('button[data-delta]').forEach((el) => el.addEventListener('click', () => {
      const q = questById[el.dataset.id];
      const step = Number(q.step ?? 1);
      const next = Math.max(0, Number(q.value ?? 0) + Number(el.dataset.delta) * step);
      setProgress(q.id, Number(next.toFixed(2)));
    }));
    $view.querySelectorAll('input.inline').forEach((el) => el.addEventListener('change', () => {
      setProgress(el.dataset.id, el.value === '' ? null : Number(el.value));
    }));
    $view.querySelector('#save-note').addEventListener('click', async () => {
      try {
        await api('PUT', '/today/note', { note: $view.querySelector('#note').value });
        toast('已儲存', true);
      } catch (err) {
        toast(err.message);
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
  async function route() {
    const hash = (location.hash || '#today').slice(1);
    if (!state.token) {
      renderLogin(hash === 'register' ? 'register' : 'login');
      return;
    }
    if (hash === 'login' || hash === 'register') {
      location.hash = '#today';
      return;
    }
    window.scrollTo(0, 0);
    try {
      await loadToday();
      // 沒有任何任務就強制走引導；引導完成前不開放其他頁
      if (state.me.needsOnboarding && hash !== 'onboarding') {
        location.hash = '#onboarding';
        return;
      }
      // 已完成引導就不該再進引導頁（例如使用者手動改網址）
      if (!state.me.needsOnboarding && hash === 'onboarding') {
        location.hash = '#today';
        return;
      }
      if (hash === 'onboarding') {
        $nav.classList.add('hidden');
        const { goals } = await api('GET', '/goals');
        await renderOnboarding({ single: false, excluded: goals.map((g) => g.category) });
        return;
      }
      $nav.classList.remove('hidden');
      $nav.querySelectorAll('a').forEach((a) => a.classList.toggle('active', a.dataset.route === hash));
      if (hash === 'progress') await renderProgress();
      else if (hash === 'settings') await renderSettings();
      else renderToday();
    } catch (err) {
      if (state.token) toast(err.message);
    }
  }

  window.addEventListener('hashchange', route);
  route();
})();
