/* 檔案 HUNTER 畫面與設定頁的主題區塊。依賴 ui.js 的 window.UI；資料一律來自 API，前端不計算金幣、進度或等級。 */
(() => {
  const RARITY_ORDER = ['S', 'A', 'C', 'E'];
  const CHEST_SOURCE = {
    LevelUp: '升級',
    RankUp: '晉階',
    Streak7: '連續 7 天',
    Streak30: '連續 30 天',
    GoalCompleted: '目標完成',
    ProgramCompleted: '66 天完成',
    Purchase: '商店',
  };
  const THEMES = [
    { key: 'azure', name: '藍光', price: 0 },
    { key: 'violet', name: '紫影', price: 500 },
    { key: 'jade', name: '翡翠', price: 500 },
  ];
  const SHOP = [
    { item: 'Shield', name: '連勝保險卡', price: 100, note: '最多持有 3 張；漏一天時自動生效。' },
    { item: 'EChest', name: 'E 級寶箱', price: 200, note: '開出 E 級卡片與金幣。' },
  ];
  const { h } = UI;
  // API 時間戳是 Unix 秒；這裡是前端唯一轉成 Date 顯示的地方
  const fromUnixSeconds = (seconds) => new Date(seconds * 1000);

  // 圖檔不存在時 onerror 移除 img，留下稀有度色塊與名稱當佔位卡
  function cardFace(card, { owned = true, large = false } = {}) {
    const size = large ? ' hcard-lg' : '';
    if (!owned) {
      return `<div class="hcard hcard-unknown rarity-${h(card.rarity)}${size}"><span class="hcard-q">？</span></div>`;
    }
    return `
      <div class="hcard rarity-${h(card.rarity)}${size}">
        <img src="${h(card.image)}" alt="" loading="lazy" onerror="this.remove()">
        <div class="hcard-frame"><span class="hcard-rarity">${h(card.rarity)}</span><span class="hcard-name">${h(card.name)}</span></div>
      </div>`;
  }

  // 全螢幕遮罩要掛在 body：.win 有 backdrop-filter，會讓 position: fixed 以該元素而非視窗為基準
  // onClose 在遮罩被任何方式關閉（點背景或 el.close()）時呼叫一次
  function overlay(html, onClose) {
    const el = document.createElement('div');
    el.className = 'hunter-modal';
    el.innerHTML = `<div class="hunter-modal-inner">${html}</div>`;
    el.close = () => {
      if (el.isConnected) {
        el.remove();
        if (onClose) {
          onClose();
        }
      }
    };
    el.addEventListener('click', (e) => {
      if (e.target === el) {
        el.close();
      }
    });
    document.body.appendChild(el);
    return el;
  }

  // 只顯示 API 與網路錯誤；其他例外屬程式錯誤，照常往上丟
  async function run(action) {
    try {
      await action();
    } catch (err) {
      if (!(err instanceof UI.ApiError)) {
        throw err;
      }
      UI.toastError(err.message);
    }
  }

  async function openChest(ctx, btn, chestId) {
    btn.disabled = true;
    let opened = false;
    try {
      await run(async () => {
        const result = await ctx.api('POST', `/rewards/chests/${chestId}/open`);
        opened = true;
        const el = overlay(`
          <div class="flip"><div class="flip-inner">
            <div class="flip-back"><span>${h(result.card.rarity)}</span></div>
            <div class="flip-front">${cardFace(result.card, { large: true })}</div>
          </div></div>
          <p class="flip-flavor">${h(result.card.flavor)}</p>
          <button type="button" class="btn btn-primary" id="flip-close">收下</button>`, () => ctx.reload());
        requestAnimationFrame(() => el.querySelector('.flip').classList.add('flipped'));
        UI.sysMessage([
          `獲得 ${result.card.rarity} 級卡片「${result.card.name}」。`,
          result.isDuplicate ? `重複卡片已轉換。獲得 ${result.coins} 金幣。` : `新卡片已收入圖鑑。獲得 ${result.coins} 金幣。`,
        ]);
        el.querySelector('#flip-close').addEventListener('click', () => el.close());
      });
    } finally {
      // 失敗時還原按鈕；成功後由「收下」重新載入畫面
      if (!opened) {
        btn.disabled = false;
      }
    }
  }

  function showCard(ctx, card, pinnedId) {
    const pinned = card.id === pinnedId;
    const el = overlay(`
      ${cardFace(card, { large: true })}
      <div class="card-detail">
        <div class="card-detail-name">${h(card.rarity)} 級・${h(card.name)}</div>
        <p>${h(card.flavor)}</p>
        <p class="sub">持有 ${h(card.count)} 張・首次取得 ${h(fromUnixSeconds(card.firstAcquiredAt).toLocaleDateString('zh-TW'))}</p>
        <button type="button" class="btn ${pinned ? 'btn-ghost' : 'btn-primary'}" id="pin">${pinned ? '取消釘選' : '釘選展示'}</button>
      </div>`);
    el.querySelector('#pin').addEventListener('click', () => run(async () => {
      await ctx.api('PUT', '/me/pinned-card', { cardId: pinned ? null : card.id });
      el.remove();
      await ctx.reload();
    }));
  }

  function openShop(ctx, rewards) {
    const el = overlay(UI.win({
      title: '商店',
      body: `
        <div class="shop-coins">金幣 <b>${h(rewards.coins)}</b>・保險卡 <b>${h(rewards.shieldCount)}</b> / 3</div>
        ${SHOP.map((s) => `
          <div class="shop-item">
            <div><div class="shop-name">${h(s.name)}</div><div class="sub">${h(s.note)}</div></div>
            <button type="button" class="btn btn-primary" data-item="${s.item}">${s.price} 金幣</button>
          </div>`).join('')}
        <div class="shop-item">
          <div><div class="shop-name">主題色</div><div class="sub">紫影、翡翠各 500 金幣。</div></div>
          <a class="btn btn-ghost" href="#settings">前往設定</a>
        </div>`,
    }));
    const itemButtons = el.querySelectorAll('[data-item]');
    itemButtons.forEach((b) => b.addEventListener('click', async () => {
      itemButtons.forEach((x) => { x.disabled = true; });
      let purchased = false;
      try {
        await run(async () => {
          const res = await ctx.api('POST', '/shop/purchase', { item: b.dataset.item });
          purchased = true;
          UI.sysMessage([
            b.dataset.item === 'Shield' ? `購買連勝保險卡。持有 ${res.shieldCount} 張。` : '購買 E 級寶箱，已放入待開寶箱。',
            `剩餘金幣 ${res.coins}。`,
          ]);
          el.remove();
          await ctx.reload();
        });
      } finally {
        // 購買失敗才還原；成功後整頁重畫
        if (!purchased) {
          itemButtons.forEach((x) => { x.disabled = false; });
        }
      }
    }));
    el.querySelector('a[href="#settings"]').addEventListener('click', () => el.remove());
  }

  /**
   * 畫檔案頁：稱號組合、展示卡、資源、待開寶箱、成就與圖鑑。
   * @param {{ api: Function, view: HTMLElement, me: object, today: object, reload: () => Promise<void>, isCurrent: () => boolean }} ctx 呼叫端提供的 api、容器、目前狀態、重新載入與「仍停留本頁」判斷
   * @returns {Promise<void>}
   */
  async function render(ctx) {
    const [rewards, cards] = await Promise.all([ctx.api('GET', '/rewards'), ctx.api('GET', '/cards')]);
    if (!ctx.isCurrent()) {
      return;
    }
    const prefixes = rewards.titleFragments.filter((f) => f.slot === 'Prefix');
    const suffixes = rewards.titleFragments.filter((f) => f.slot === 'Suffix');
    const options = (list, selected) => ['<option value="">（不選）</option>']
      .concat(list.map((f) => `<option value="${h(f.key)}"${f.key === selected ? ' selected' : ''}>${h(f.text)}</option>`))
      .join('');
    const sorted = [...cards.cards].sort((a, b) => RARITY_ORDER.indexOf(a.rarity) - RARITY_ORDER.indexOf(b.rarity));

    ctx.view.innerHTML = `
      ${rewards.pinnedCard ? UI.win({ title: '展示卡', body: `<div class="hunter-pinned">${cardFace(rewards.pinnedCard, { large: true })}</div>` }) : ''}
      ${UI.win({
        title: '稱號',
        body: `
          <div class="title-now">${h(rewards.title)}</div>
          <div class="title-picker">
            <label class="field">前綴<select id="title-prefix">${options(prefixes, rewards.titlePrefixKey)}</select></label>
            <label class="field">後綴<select id="title-suffix">${options(suffixes, rewards.titleSuffixKey)}</select></label>
          </div>`,
      })}
      ${UI.win({
        title: '資源',
        body: `
          <div class="hunter-wallet">
            <span class="chip">金幣 <b>${h(rewards.coins)}</b></span>
            <span class="chip">保險卡 <b>${h(rewards.shieldCount)}</b> / 3</span>
            <button type="button" class="btn btn-ghost" id="open-shop">商店</button>
          </div>`,
      })}
      ${UI.win({
        title: `待開寶箱（${rewards.unopenedChests.length}）`,
        body: rewards.unopenedChests.length
          ? `<div class="chest-list">${rewards.unopenedChests.map((c) => `
              <button type="button" class="chest rarity-${h(c.rarity)}" data-id="${h(c.id)}">
                <span class="chest-rank">${h(c.rarity)}</span><span class="chest-source">${h(CHEST_SOURCE[c.source] || c.source)}</span>
              </button>`).join('')}</div>`
          : '<p class="sub">目前沒有寶箱。升級、連續達標、完成目標都能獲得。</p>',
      })}
      ${UI.win({
        title: '成就',
        body: `<ul class="achievements">${rewards.achievements.map((a) => `
          <li class="${a.unlocked ? 'unlocked' : ''}">
            <div class="ach-name">${h(a.name)} <span class="chip">${a.slot === 'Prefix' ? '前綴' : '後綴'}「${h(a.titleText)}」</span></div>
            <div class="ach-cond">${h(a.condition)}</div>
            ${a.unlocked || !a.target ? '' : `<div class="ach-bar"><i style="width:${Math.min(100, Math.round((a.progress / a.target) * 100))}%"></i></div><div class="ach-progress">${h(a.progress)} / ${h(a.target)}</div>`}
          </li>`).join('')}</ul>`,
      })}
      ${UI.win({
        title: `圖鑑 ${h(cards.ownedKinds)} / ${h(cards.total)}`,
        body: `<div class="grid-cards">${sorted.map((c) => `
          <button type="button" class="card-cell" data-id="${h(c.id)}"${c.owned ? '' : ' disabled'} aria-label="${c.owned ? h(c.name) : '未取得的卡片'}">
            ${cardFace(c, { owned: c.owned })}
            ${c.owned && c.count > 1 ? `<span class="card-count">×${h(c.count)}</span>` : ''}
          </button>`).join('')}</div>`,
      })}`;

    const saveTitle = () => run(async () => {
      await ctx.api('PUT', '/me/title', {
        prefixKey: ctx.view.querySelector('#title-prefix').value || null,
        suffixKey: ctx.view.querySelector('#title-suffix').value || null,
      });
      await ctx.reload();
    });
    ctx.view.querySelector('#title-prefix').addEventListener('change', saveTitle);
    ctx.view.querySelector('#title-suffix').addEventListener('change', saveTitle);
    ctx.view.querySelector('#open-shop').addEventListener('click', () => openShop(ctx, rewards));
    ctx.view.querySelectorAll('.chest').forEach((b) => b.addEventListener('click', () => openChest(ctx, b, b.dataset.id)));
    ctx.view.querySelectorAll('.card-cell:not([disabled])').forEach((b) => b.addEventListener('click', () => {
      showCard(ctx, cards.cards.find((c) => c.id === b.dataset.id), rewards.pinnedCard?.id);
    }));
  }

  /**
   * 產生設定頁的主題視窗；呼叫端把 html 放進畫面後再用 bind 綁事件。
   * @param {{ api: Function, reload: () => Promise<void> }} ctx 呼叫端提供的 api 與重新載入
   * @returns {Promise<{ html: string, bind: (root: Element) => void }>} 視窗 HTML 與事件綁定函式
   */
  async function themeSection(ctx) {
    const rewards = await ctx.api('GET', '/rewards');
    const owned = new Set(rewards.ownedThemes);
    const html = UI.win({
      title: '主題',
      body: `<div class="theme-list">${THEMES.map((t) => `
        <div class="theme-card" data-theme-key="${t.key}">
          <span class="theme-swatch"></span><span class="theme-name">${h(t.name)}</span>
          ${rewards.themeKey === t.key
            ? '<span class="chip">使用中</span>'
            : owned.has(t.key)
              ? `<button type="button" class="btn btn-ghost" data-use="${t.key}">套用</button>`
              : `<button type="button" class="btn btn-primary" data-buy="${t.key}">${t.price} 金幣</button>`}
        </div>`).join('')}</div>
        <p class="sub">金幣 ${h(rewards.coins)}</p>`,
    });
    const bind = (root) => {
      root.querySelectorAll('[data-use]').forEach((b) => b.addEventListener('click', () => run(async () => {
        await ctx.api('PUT', '/me/theme', { themeKey: b.dataset.use });
        UI.setAccent(b.dataset.use);
        await ctx.reload();
      })));
      root.querySelectorAll('[data-buy]').forEach((b) => UI.confirmButton(b, b.textContent, async () => {
        const key = b.dataset.buy;
        await ctx.api('POST', '/shop/purchase', { item: 'Theme', themeKey: key });
        UI.sysMessage([`解鎖主題「${THEMES.find((t) => t.key === key).name}」。`, '按「套用」即可切換。']);
        await ctx.reload();
      }));
    };
    return { html, bind };
  }

  window.Hunter = { render, themeSection };
})();
