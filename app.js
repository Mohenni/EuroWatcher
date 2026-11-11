(() => {
  'use strict';

  const DATA = window.EURO_DATA;
  const LANGS = window.EURO_I18N || {};
  const $ = (q, root = document) => root.querySelector(q);

  /* ---------- language ---------- */

  const LANG_KEY = 'eurowatcher.lang';
  const lang = (() => {
    let stored = null;
    try { stored = localStorage.getItem(LANG_KEY); } catch { /* storage unavailable */ }
    if (stored && LANGS[stored]) return stored;
    for (const tag of navigator.languages || [navigator.language || '']) {
      const code = String(tag).slice(0, 2).toLowerCase();
      if (LANGS[code]) return code;
    }
    return 'en';
  })();
  const M = LANGS[lang] || {};
  const EN = LANGS.en || {};
  const LOCALE = M.locale || 'en-GB';

  const intFmt = new Intl.NumberFormat(LOCALE);
  const int = x => intFmt.format(x);

  /** Looks up a message; {name} is replaced by p.name, and p.N is p.n formatted. */
  function t(key, p = {}) {
    const v = key in M ? M[key] : EN[key];
    if (v == null) return key;
    const params = typeof p.n === 'number' && p.N == null ? { ...p, N: int(p.n) } : p;
    if (typeof v === 'function') return v(params);
    return v.replace(/\{(\w+)\}/g, (_, k) => (params[k] ?? ''));
  }

  /** Like t(), but **bold** and `code` become elements. */
  function rich(key, p) {
    return t(key, p).split(/(\*\*[^*]+\*\*|`[^`]+`)/).filter(Boolean).map(part => {
      if (part.startsWith('**')) return h('strong', { text: part.slice(2, -2) });
      if (part.startsWith('`')) return h('code', { text: part.slice(1, -1) });
      return part;
    });
  }

  /* ---------- helpers ---------- */

  const asc = (a, b) => a - b;

  /** Tiny element builder. Everything goes in as text nodes, never as HTML. */
  function h(tag, attrs, ...kids) {
    const el = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v == null || v === false) continue;
      if (k === 'class') el.className = v;
      else if (k === 'text') el.textContent = v;
      else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
      else el.setAttribute(k, v === true ? '' : v);
    }
    for (const kid of kids.flat()) if (kid != null && kid !== false) el.append(kid);
    return el;
  }

  const utc = iso => new Date(`${iso}T00:00:00Z`);
  const EN_DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const EN_MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const EN_MONTHS_LONG = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
  const dtf = opts => new Intl.DateTimeFormat(LOCALE, { timeZone: 'UTC', ...opts });
  const fmtFull = dtf({ weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
  const fmtMedium = dtf({ day: 'numeric', month: 'short', year: 'numeric' });
  const fmtLong = dtf({ day: 'numeric', month: 'long', year: 'numeric' });
  const fmtDayMonth = dtf({ day: 'numeric', month: 'long' });
  // English is written by hand so every browser shows "Tue 10 Nov 2026" (some write "Sept").
  const en = lang === 'en';
  /** A date with its weekday: "Tue 10 Nov 2026". */
  const fmtT = d => (en ? `${EN_DAYS[d.getUTCDay()]} ${d.getUTCDate()} ${EN_MONTHS[d.getUTCMonth()]} ${d.getUTCFullYear()}` : fmtFull.format(d));
  const fmtDate = draw => fmtT(draw.t);
  /** "10 May 2011" */
  const fmtMed = d => (en ? `${d.getUTCDate()} ${EN_MONTHS[d.getUTCMonth()]} ${d.getUTCFullYear()}` : fmtMedium.format(d));
  /** "13 February 2004" */
  const fmtBig = d => (en ? `${d.getUTCDate()} ${EN_MONTHS_LONG[d.getUTCMonth()]} ${d.getUTCFullYear()}` : fmtLong.format(d));
  /** "13 November" */
  const fmtDay = d => (en ? `${d.getUTCDate()} ${EN_MONTHS_LONG[d.getUTCMonth()]}` : fmtDayMonth.format(d));

  const moneyFmts = {};
  function money(x, currency = 'EUR') {
    // English keeps the familiar "€118.8M" (the browser's compact style writes "€118.8m").
    if (en && x >= 1e6) return `${currency === 'GBP' ? '£' : '€'}${(x / 1e6).toFixed(1).replace(/\.0$/, '')}M`;
    const kind = x >= 1e6 ? 'compact' : x >= 10000 ? 'whole' : 'cents';
    const id = currency + kind;
    if (!moneyFmts[id]) {
      const opts = { style: 'currency', currency };
      if (kind === 'compact') Object.assign(opts, { notation: 'compact', maximumFractionDigits: 1 });
      else if (kind === 'whole') Object.assign(opts, { maximumFractionDigits: 0 });
      else Object.assign(opts, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
      moneyFmts[id] = new Intl.NumberFormat(LOCALE, opts);
    }
    return moneyFmts[id].format(x);
  }
  const times = n => t('times', { n });
  const drawsN = n => t('drawsN', { n });
  const listDates = rows => rows.map(r => fmtDate(r.d)).join(', ');

  /* ---------- static text ---------- */

  function applyStaticText() {
    document.documentElement.lang = lang;
    document.title = t('title');
    for (const el of document.querySelectorAll('[data-t]')) el.textContent = t(el.dataset.t);
    for (const el of document.querySelectorAll('[data-t-rich]')) el.replaceChildren(...rich(el.dataset.tRich));
    for (const el of document.querySelectorAll('[data-t-aria]')) el.setAttribute('aria-label', t(el.dataset.tAria));
    for (const el of document.querySelectorAll('[data-t-ph]')) el.setAttribute('placeholder', t(el.dataset.tPh));
    for (const el of document.querySelectorAll('[data-date]')) el.textContent = fmtMed(utc(el.dataset.date));
  }

  function setupLanguagePicker() {
    const select = $('#lang');
    for (const [code, dict] of Object.entries(LANGS)) select.append(h('option', { value: code, text: dict.name }));
    select.value = lang;
    select.addEventListener('change', () => {
      try { localStorage.setItem(LANG_KEY, select.value); } catch { /* not persisted */ }
      location.reload();
    });
  }

  /* ---------- donate ---------- */

  const BITCOIN = 'bc1qe53lfh6535w4h3py7pwfhcml32lpq9rty55l5p';

  function setupDonate() {
    const dialog = $('#donate');
    const address = $('#donate-address');
    const copy = $('#donate-copy');
    address.textContent = BITCOIN;
    let revert = 0;
    $('#donate-open').addEventListener('click', () => {
      copy.textContent = t('don.copy');
      if (dialog.showModal) dialog.showModal();
      else dialog.setAttribute('open', '');
      copy.focus();
    });
    copy.addEventListener('click', async () => {
      let ok = false;
      try {
        await navigator.clipboard.writeText(BITCOIN);
        ok = true;
      } catch {
        const range = document.createRange();
        range.selectNodeContents(address);
        const selection = getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
        try { ok = document.execCommand('copy'); } catch { ok = false; }
      }
      copy.textContent = t(ok ? 'don.copied' : 'don.copyFailed');
      clearTimeout(revert);
      revert = setTimeout(() => { copy.textContent = t('don.copy'); }, 2500);
    });
    dialog.addEventListener('click', e => { if (e.target === dialog) dialog.close(); });
  }

  applyStaticText();
  setupLanguagePicker();
  setupDonate();

  if (!DATA || !Array.isArray(DATA.draws) || !DATA.draws.length) {
    document.body.classList.add('no-data');
    $('#fatal').hidden = false;
    return;
  }

  /* ---------- data ---------- */

  const TIERS = DATA.tiers; // [numbers matched, stars matched], in official prize order
  const key = (m, s) => m * 3 + s;
  const TIER_AT = new Map(TIERS.map(([m, s], i) => [key(m, s), i]));
  // Every possible outcome, best first: the prize tiers, then the ones that pay nothing.
  const RANK = new Map([...TIERS, [1, 1], [0, 2], [1, 0], [0, 1], [0, 0]].map(([m, s], i) => [key(m, s), i]));
  const TWO_ZERO = TIER_AT.get(key(2, 0));
  const TWO_ZERO_PAYS_FROM = '2011-05-10';
  const PRICE_RISE = '2016-09-27';
  const STAR_JOINED = { 10: '2011-05-10', 11: '2011-05-10', 12: '2016-09-27' };

  const draws = DATA.draws.map(([no, date, n, s, prizes, winners, gbp]) => {
    const jw = winners ? winners[0] : null; // jackpot winners
    const each = prizes ? prizes[0] : null; // jackpot per winning ticket, or the unclaimed pot
    return {
      no, date, n, s, prizes, winners, gbp, jw, each,
      t: utc(date),
      year: +date.slice(0, 4),
      pot: each != null ? each * Math.max(1, jw || 1) : null,
      status: jw == null ? 'pending' : jw > 0 ? 'won' : 'rollover',
    };
  });
  // No 5+2 winner, yet the next pot collapsed: the jackpot was paid to the tier below.
  draws.forEach((d, i) => {
    const next = draws[i + 1];
    if (d.status === 'rollover' && next && d.pot && next.pot && next.pot < d.pot * 0.5) d.status = 'rolldown';
  });
  const first = draws[0];
  const latest = draws[draws.length - 1];

  function tierOf(d, m, s) {
    const i = TIER_AT.get(key(m, s));
    if (i === undefined || (i === TWO_ZERO && d.date < TWO_ZERO_PAYS_FROM)) return -1;
    return i;
  }

  /**
   * Draws held after the latest one on file whose results should be out by now.
   * A Tuesday or Friday draw counts from 07:00 the next morning, local time.
   */
  function missedDraws() {
    const out = [];
    const [y, m, d] = latest.date.split('-').map(Number);
    const day = new Date(y, m - 1, d + 1);
    const now = new Date();
    while (true) {
      const publishedAt = new Date(day.getFullYear(), day.getMonth(), day.getDate() + 1, 7);
      if (publishedAt > now) break;
      if (day.getDay() === 2 || day.getDay() === 5) out.push(new Date(day));
      day.setDate(day.getDate() + 1);
    }
    return out;
  }

  /* ---------- shared pieces ---------- */

  function lineEl(nums, stars, { picks, size } = {}) {
    const label = t('line.aria', { nums: nums.join(', '), stars: stars.join(', ') });
    const el = h('span', { class: ['line', size, picks && 'cmp'].filter(Boolean).join(' '), role: 'img', 'aria-label': label });
    for (const n of nums) el.append(h('span', { class: picks?.n.has(n) ? 'ball hit' : 'ball', text: n }));
    for (const s of stars) el.append(h('span', { class: picks?.s.has(s) ? 'star hit' : 'star', text: s }));
    return el;
  }

  function tierName(m, s) {
    return h('span', { class: 'tier-name' }, String(m), h('span', { class: 'plus', text: '+' }), String(s), h('span', { class: 'tier-star', text: '★' }));
  }

  function statusTag(d) {
    if (d.status === 'won') return h('span', { class: 'tag won', text: d.jw === 1 ? t('st.won') : t('st.wonBy', { n: d.jw }) });
    if (d.status === 'rolldown') return h('span', { class: 'tag down', text: t('st.rolldown') });
    if (d.status === 'rollover') return h('span', { class: 'tag', text: t('st.rollover') });
    return h('span', { class: 'tag', text: t('st.pending') });
  }

  function potText(d) {
    if (d.pot != null) return money(d.pot);
    if (d.gbp != null) return money(d.gbp, 'GBP');
    return t('na');
  }

  function jackpotStory(d) {
    const amount = d.pot != null ? money(d.pot)
      : d.gbp != null ? t('story.gbp', { amount: money(d.gbp, 'GBP') }) : '';
    const jp = t('story.jp', { amount });
    if (d.status === 'won') {
      return d.jw === 1 ? t('story.won1', { jp }) : t('story.wonN', { jp, n: d.jw, each: d.each != null ? money(d.each) : '' });
    }
    if (d.status === 'rolldown') return t('story.rolldown', { jp });
    if (d.status === 'rollover') return t('story.rollover', { jp });
    return t('story.pending', { jp });
  }

  function checkLine(d) {
    setLine(d.n, d.s);
    $('#check').scrollIntoView();
  }

  function drawDetail(d) {
    const facts = h('div', { class: 'detail-facts' },
      h('p', {}, h('strong', { text: t('d.head', { no: int(d.no), date: fmtDate(d) }) }), jackpotStory(d)),
      h('button', { class: 'btn btn-small', type: 'button', text: t('d.check'), onclick: () => checkLine(d) }),
    );
    if (!d.winners || !d.winners.some((w, i) => i > 0 && w != null)) {
      return h('div', { class: 'detail' }, facts, h('p', { class: 'fine', text: t('d.noBreakdown') }));
    }
    const prizeCell = i => {
      const p = d.prizes ? d.prizes[i] : null;
      if (p == null || (i === 0 && !d.winners[0])) return d.winners[i] ? t('notOnFile') : t('noWinner');
      return money(p);
    };
    const table = h('table', { class: 'tier-table' },
      h('thead', {}, h('tr', {}, h('th', { text: t('th.match') }), h('th', { class: 'num', text: t('th.winners') }), h('th', { class: 'num', text: t('th.each') }))),
      h('tbody', {}, TIERS.map(([m, s], i) => d.winners[i] == null ? null : h('tr', { class: d.winners[i] ? null : 'zero' },
        h('td', {}, tierName(m, s)),
        h('td', { class: 'num', text: int(d.winners[i]) }),
        h('td', { class: 'num', text: prizeCell(i) })))),
    );
    return h('div', { class: 'detail' }, facts, table);
  }

  function toggleDetail(host, btn, d) {
    const open = btn.getAttribute('aria-expanded') === 'true';
    btn.setAttribute('aria-expanded', String(!open));
    if (open) host.querySelector('.detail')?.remove();
    else host.append(drawDetail(d));
  }

  /* ---------- parsing a typed line ---------- */

  function parseLine(text) {
    const ints = s => (s.match(/\d+/g) || []).map(Number);
    // "+", "/", "|", ";" or a word for "stars" separates the numbers from the stars
    const parts = String(text).split(/[+/|;_★*]|stars?|étoiles?|estrellas?|estrelas?|sterne?n?|sterren?|stelle?/i);
    let n;
    let s;
    if (parts.length > 1) {
      n = ints(parts[0]);
      s = ints(parts.slice(1).join(' '));
    } else {
      const all = ints(parts[0]);
      n = all.slice(0, 5);
      s = all.slice(5);
    }
    const bad = (arr, max) => arr.some(x => x < 1 || x > max) || new Set(arr).size !== arr.length;
    if (n.length > 5) return { error: t('err.manyNumbers') };
    if (s.length > 2) return { error: t('err.manyStars') };
    if (bad(n, 50)) return { error: t('err.rangeNumbers') };
    if (bad(s, 12)) return { error: t('err.rangeStars') };
    return { n: n.sort(asc), s: s.sort(asc) };
  }
  const lineKey = (n, s) => `${n.join('-')}_${s.join('-')}`;
  const lineText = (n, s) => n.join(' ') + (s.length ? ` + ${s.join(' ')}` : '');

  /* ---------- state ---------- */

  const sel = { n: new Set(), s: new Set() };
  const view = { mode: 'tier', shown: 10 };
  const STORE = 'eurowatcher.plays';
  let plays = [];
  try {
    plays = JSON.parse(localStorage.getItem(STORE) || '[]')
      .map(k => parseLine(k))
      .filter(p => !p.error && p.n.length === 5 && p.s.length === 2)
      .map(p => lineKey(p.n, p.s));
  } catch { /* storage unavailable: plays just won't persist */ }

  function setLine(n, s) {
    sel.n = new Set(n);
    sel.s = new Set(s);
    view.shown = 10;
    changed();
  }

  function changed() {
    syncPicker();
    renderResults();
    renderSaved();
    // Keep the line in the address so a reload or a bookmark brings it back.
    const query = sel.n.size + sel.s.size ? `?line=${lineKey([...sel.n].sort(asc), [...sel.s].sort(asc))}` : '';
    if (location.search === query) return;
    try {
      history.replaceState(null, '', location.pathname + query + location.hash);
    } catch { /* some browsers refuse this on file:// pages */ }
  }

  /* ---------- picker ---------- */

  const numGrid = $('#num-grid');
  const starGrid = $('#star-grid');
  const quick = $('#quick');
  const quickError = $('#quick-error');

  for (let i = 1; i <= 50; i++) numGrid.append(h('button', { class: 'pick', type: 'button', 'aria-pressed': 'false', 'data-v': i, text: i }));
  for (let i = 1; i <= 12; i++) {
    starGrid.append(h('button', { class: 'pick-star', type: 'button', 'aria-pressed': 'false', 'aria-label': t('star', { n: i }), 'data-v': i }, h('span', { class: 'star-shape', text: i })));
  }

  function wireGrid(grid, set, max) {
    grid.addEventListener('click', e => {
      const btn = e.target.closest('button');
      if (!btn) return;
      const v = +btn.dataset.v;
      const picked = set();
      if (picked.has(v)) picked.delete(v);
      else if (picked.size < max) picked.add(v);
      else return;
      view.shown = 10;
      changed();
    });
  }
  wireGrid(numGrid, () => sel.n, 5);
  wireGrid(starGrid, () => sel.s, 2);

  function syncPicker() {
    const sync = (grid, picked, max, counter) => {
      for (const btn of grid.children) {
        const on = picked.has(+btn.dataset.v);
        btn.setAttribute('aria-pressed', String(on));
        btn.disabled = !on && picked.size >= max;
      }
      counter.textContent = `${picked.size} / ${max}`;
      counter.classList.toggle('full', picked.size === max);
    };
    sync(numGrid, sel.n, 5, $('#count-n'));
    sync(starGrid, sel.s, 2, $('#count-s'));
    if (document.activeElement !== quick) quick.value = lineText([...sel.n].sort(asc), [...sel.s].sort(asc));
    quickError.hidden = true;
  }

  $('#quick-form').addEventListener('submit', e => {
    e.preventDefault();
    const parsed = parseLine(quick.value);
    if (parsed.error) {
      quickError.textContent = parsed.error;
      quickError.hidden = false;
      return;
    }
    quick.blur();
    setLine(parsed.n, parsed.s);
  });

  function sample(count, max) {
    const out = new Set();
    while (out.size < count) out.add(1 + Math.floor(Math.random() * max));
    return [...out].sort(asc);
  }
  $('#random-line').addEventListener('click', () => setLine(sample(5, 50), sample(2, 12)));
  $('#clear-line').addEventListener('click', () => setLine([], []));
  $('#save-line').addEventListener('click', () => {
    const k = lineKey([...sel.n].sort(asc), [...sel.s].sort(asc));
    if (sel.n.size !== 5 || sel.s.size !== 2 || plays.includes(k)) return;
    plays.push(k);
    storePlays();
    renderSaved();
  });

  function storePlays() {
    try { localStorage.setItem(STORE, JSON.stringify(plays)); } catch { /* not persisted */ }
  }

  /* ---------- comparing a line with every draw ---------- */

  function score(picks, d) {
    let m = 0;
    let st = 0;
    for (const x of d.n) if (picks.n.has(x)) m++;
    for (const x of d.s) if (picks.s.has(x)) st++;
    return { m, st };
  }

  function evaluate() {
    const N = [...sel.n].sort(asc);
    const S = [...sel.s].sort(asc);
    const complete = N.length === 5 && S.length === 2;
    const rows = draws.map(d => {
      const { m, st } = score(sel, d);
      let gap = null; // how far the balls sit from the user's, position by position
      if (complete) {
        gap = 0;
        for (let k = 0; k < 5; k++) gap += Math.abs(d.n[k] - N[k]);
        for (let k = 0; k < 2; k++) gap += Math.abs(d.s[k] - S[k]);
      }
      return { d, m, st, gap, rank: RANK.get(key(m, st)), tier: tierOf(d, m, st) };
    });
    return { N, S, complete, rows };
  }

  const byTier = (a, b) => a.rank - b.rank || (a.gap ?? 0) - (b.gap ?? 0) || b.d.no - a.d.no;
  const byGap = (a, b) => a.gap - b.gap || a.rank - b.rank || b.d.no - a.d.no;

  /* ---------- results ---------- */

  const results = $('#results');

  function renderResults() {
    if (!sel.n.size && !sel.s.size) {
      results.replaceChildren(h('div', { class: 'empty' },
        h('strong', { text: t('empty.title') }),
        h('p', { text: t('empty.body', { count: int(draws.length) }) }),
        h('button', { class: 'btn', type: 'button', text: t('empty.try'), onclick: () => setLine(latest.n, latest.s) })));
      return;
    }
    const ev = evaluate();
    results.replaceChildren(...[verdictCard(ev), ev.complete && prizePanel(ev), nearestPanel(ev)].filter(Boolean));
  }

  function verdictCard(ev) {
    const { N, S, complete, rows } = ev;
    const body = h('div', { class: 'verdict-body' });
    let kind = 'partial';
    let title;

    if (!complete) {
      const together = rows.filter(r => r.m === N.length && r.st === S.length);
      const k = N.length + S.length;
      const lastSeen = together.length ? fmtDate(together[together.length - 1].d) : '';
      if (k === 1) {
        const name = N.length ? t('number', { n: N[0] }) : t('star', { n: S[0] });
        title = t('v.soloTitle', { times: times(together.length) });
        body.append(h('p', {}, ...(together.length
          ? rich('v.soloBody', { name, draws: drawsN(together.length), total: int(draws.length), date: lastSeen })
          : [t('v.soloNever', { name })])));
      } else if (together.length) {
        title = t('v.togetherTitle', { times: times(together.length) });
        body.append(h('p', {}, ...rich('v.togetherBody', { k, times: times(together.length), date: lastSeen })));
      } else {
        title = t('v.neverTogetherTitle');
        body.append(h('p', { text: t('v.neverTogetherBody', { k }) }));
      }
      body.append(h('p', { text: t('v.need', { n: 5 - N.length, s: 2 - S.length }) }));
    } else {
      const exact = rows.filter(r => r.m === 5 && r.st === 2);
      const five = rows.filter(r => r.m === 5 && r.st < 2);
      if (exact.length) {
        kind = 'drawn';
        title = exact.length === 1 ? t('v.drawnTitle') : t('v.drawnTitleN', { times: times(exact.length) });
        body.append(h('p', {}, ...rich('v.drawnBody', { dates: listDates(exact) })));
      } else {
        kind = 'novel';
        title = t('v.novelTitle');
        body.append(h('p', {}, ...rich('v.novelBody', { total: int(draws.length), date: fmtDate(first) })));
        const bestRank = Math.min(...rows.map(r => r.rank));
        const best = rows.filter(r => r.rank === bestRank);
        const last = best[best.length - 1];
        const match = t('match', { m: last.m, s: last.st });
        body.append(h('p', {}, ...(best.length === 1
          ? rich('v.closest', { match, date: fmtDate(last.d) })
          : rich('v.closestN', { match, times: times(best.length), date: fmtDate(last.d) }))));
      }
      if (five.length) body.append(h('p', {}, ...rich('v.five', { dates: listDates(five) })));
      else if (!exact.length) body.append(h('p', { text: t('v.fiveNever') }));
    }

    const late = S.filter(s => STAR_JOINED[s]);
    const [a, b] = late;
    const joined = s => fmtMed(utc(STAR_JOINED[s]));
    const lateText = !late.length ? null
      : late.length === 1 ? t('v.late1', { a, date: joined(a) })
      : STAR_JOINED[a] === STAR_JOINED[b] ? t('v.late2', { a, b, date: joined(a) })
      : t('v.late2diff', { a, da: joined(a), b, db: joined(b) });

    return h('article', { class: `card verdict ${kind}` },
      h('p', { class: 'verdict-kicker', text: t(complete ? 'v.kickerLine' : 'v.kickerPicks') }),
      h('p', { class: 'verdict-title', text: title }),
      h('div', { class: 'verdict-line' }, lineEl(N, S, { picks: sel, size: 'big' })),
      body,
      lateText && h('p', { class: 'verdict-note', text: lateText }));
  }

  function prizePanel(ev) {
    const stats = TIERS.map(() => ({ n: 0, sum: 0, unknown: 0, last: null }));
    let stake = 0;
    for (const r of ev.rows) {
      stake += r.d.date < PRICE_RISE ? 2 : 2.5;
      if (r.tier < 0) continue;
      const tier = stats[r.tier];
      const paid = r.d.prizes ? r.d.prizes[r.tier] : null;
      tier.n++;
      tier.last = r.d;
      if (paid == null) tier.unknown++;
      else tier.sum += paid;
    }
    const wins = stats.reduce((acc, x) => acc + x.n, 0);
    const total = stats.reduce((acc, x) => acc + x.sum, 0);
    const unknown = stats.reduce((acc, x) => acc + x.unknown, 0);

    const sub = h('p', { class: 'panel-sub' }, ...(wins
      ? rich('p.sub', {
        prizes: t('prizesN', { n: wins }),
        total: money(total),
        unknown: unknown ? t('p.unknown', { prizes: t('prizesN', { n: unknown }) }) : '',
        count: int(draws.length),
        stake: money(stake),
      })
      : [t('p.none', { count: int(draws.length) })]));

    const table = h('table', { class: 'tier-table' },
      h('thead', {}, h('tr', {}, h('th', { text: t('th.match') }), h('th', { class: 'num', text: t('th.times') }), h('th', { text: t('th.recent') }), h('th', { class: 'num', text: t('th.total') }))),
      h('tbody', {}, stats.map((x, i) => h('tr', { class: x.n ? null : 'zero' },
        h('td', {}, tierName(...TIERS[i])),
        h('td', { class: 'num', text: int(x.n) }),
        h('td', { text: x.last ? fmtDate(x.last) : t('never') }),
        h('td', { class: 'num', text: x.n ? (x.sum || !x.unknown ? money(x.sum) : t('notOnFile')) : '' })))),
    );

    return h('article', { class: 'card panel' },
      h('div', { class: 'panel-head' }, h('h3', { class: 'panel-title', text: t('p.title') })),
      sub, table,
      h('p', { class: 'verdict-note', text: t('p.note', { date: fmtMed(utc(PRICE_RISE)) }) }));
  }

  function nearestPanel(ev) {
    const gapMode = ev.complete && view.mode === 'gap';
    const ranked = [...ev.rows].sort(gapMode ? byGap : byTier);
    const head = h('div', { class: 'panel-head' }, h('h3', { class: 'panel-title', text: t(ev.complete ? 'n.titleFull' : 'n.titlePartial') }));
    if (ev.complete) {
      const seg = (mode, label) => h('button', { type: 'button', 'aria-pressed': String(view.mode === mode), text: label, onclick: () => { view.mode = mode; view.shown = 10; renderResults(); } });
      head.append(h('div', { class: 'seg', role: 'group', 'aria-label': t('n.rankBy') }, seg('tier', t('n.segTier')), seg('gap', t('n.segGap'))));
    }
    const sub = t(!ev.complete ? 'n.subPartial' : gapMode ? 'n.subGap' : 'n.subTier');

    const items = ranked.slice(0, view.shown).map((r, i) => {
      const li = h('li', { class: 'near' });
      const tags = h('span', { class: 'near-tags' });
      if (gapMode) tags.append(h('span', { class: 'tag', text: t('n.gap', { n: r.gap }) }));
      if (ev.complete) {
        const paid = r.tier >= 0 && r.d.prizes ? r.d.prizes[r.tier] : null;
        tags.append(r.tier < 0 ? h('span', { class: 'tag', text: t('n.noPrize') })
          : h('span', { class: 'tag prize', text: paid != null ? t('n.paid', { amount: money(paid) }) : t('n.prizeTier') }));
      }
      const side = h('span', { class: 'near-side' }, h('span', { class: 'near-score' }, `${r.m} + ${r.st}`, h('small', { text: t('n.matched') })), tags);
      const btn = h('button', { class: 'near-row', type: 'button', 'aria-expanded': 'false' },
        h('span', { class: 'near-rank', text: i + 1 }),
        h('span', { class: 'near-main' },
          h('span', { class: 'near-meta' }, h('span', { class: 'near-date', text: fmtDate(r.d) }), h('span', { class: 'near-no', text: t('n.drawNo', { no: int(r.d.no) }) }), statusTag(r.d)),
          lineEl(r.d.n, r.d.s, { picks: sel })),
        side);
      btn.addEventListener('click', () => toggleDetail(li, btn, r.d));
      li.append(btn);
      return li;
    });

    const more = view.shown < ranked.length && view.shown < 100
      ? h('button', { class: 'btn near-more', type: 'button', text: t('n.more'), onclick: () => { view.shown += 10; renderResults(); } })
      : null;

    return h('article', { class: 'card panel' }, head, h('p', { class: 'panel-sub', text: sub }), h('ol', { class: 'near-list' }, items), more);
  }

  /* ---------- saved plays ---------- */

  function renderSaved() {
    const current = lineKey([...sel.n].sort(asc), [...sel.s].sort(asc));
    const complete = sel.n.size === 5 && sel.s.size === 2;
    const saveBtn = $('#save-line');
    saveBtn.disabled = !complete || plays.includes(current);
    saveBtn.textContent = complete && plays.includes(current) ? t('saved') : t('save');

    $('#saved').hidden = !plays.length;
    $('#saved-list').replaceChildren(...plays.map(k => {
      const { n, s } = parseLine(k);
      const picks = { n: new Set(n), s: new Set(s) };
      let best = null;
      for (const d of draws) {
        const { m, st } = score(picks, d);
        const rank = RANK.get(key(m, st));
        if (!best || rank <= best.rank) best = { rank, m, st, d };
      }
      const now = score(picks, latest);
      return h('li', { class: k === current ? 'saved-item current' : 'saved-item' },
        h('button', { class: 'saved-load', type: 'button', title: t('plays.load'), onclick: () => setLine(n, s) },
          lineEl(n, s, { size: 'small' }),
          h('span', { class: 'saved-note', text: t('plays.note', { best: `${best.m} + ${best.st}`, date: fmtDate(best.d), now: `${now.m} + ${now.st}` }) })),
        h('button', { class: 'saved-remove', type: 'button', 'aria-label': t('plays.remove', { line: lineText(n, s) }), text: '×', onclick: () => { plays = plays.filter(p => p !== k); storePlays(); renderSaved(); } }));
    }));
  }

  /* ---------- hero ---------- */

  const missed = missedDraws();
  $('#hero-lede').replaceChildren(...rich('hero.lede', { count: int(draws.length), first: fmtBig(first.t) }));

  function renderLatest(desktopApp) {
    $('#latest').replaceChildren(...[
      h('div', { class: 'latest-top' }, h('span', { class: 'latest-label', text: t('latest.label') }), h('span', { class: 'latest-date', text: fmtDate(latest) })),
      lineEl(latest.n, latest.s, { size: 'big' }),
      h('div', { class: 'latest-foot' }, statusTag(latest), h('span', { text: t('latest.foot', { pot: potText(latest), no: int(latest.no) }) }),
        h('button', { class: 'btn btn-small', type: 'button', text: t('d.check'), onclick: () => checkLine(latest) })),
      missed.length && !desktopApp && h('p', { class: 'verdict-note' }, ...rich('latest.stale')),
    ].filter(Boolean));
  }

  /* ---------- history ---------- */

  const hist = { list: [], shown: 0 };
  const PAGE = 50;
  const drawList = $('#draws');
  const moreBtn = $('#draws-more');
  const fYear = $('#f-year');
  const fStatus = $('#f-status');
  const fSort = $('#f-sort');
  const fContains = $('#f-contains');

  fYear.append(h('option', { value: '', text: t('f.allYears') }));
  for (let y = latest.year; y >= first.year; y--) fYear.append(h('option', { value: y, text: y }));

  function drawItem(d) {
    const li = h('li', { class: 'draw' });
    const btn = h('button', { class: 'draw-row', type: 'button', 'aria-expanded': 'false' },
      h('span', { class: 'draw-no', text: `#${d.no}` }),
      h('span', { class: 'draw-date', text: fmtDate(d) }),
      lineEl(d.n, d.s),
      h('span', { class: d.pot == null ? 'draw-pot unknown' : 'draw-pot', text: potText(d) }),
      h('span', { class: 'draw-status' }, statusTag(d)),
      h('span', { class: 'chev' }));
    btn.addEventListener('click', () => toggleDetail(li, btn, d));
    li.append(btn);
    return li;
  }

  // Rows are added only when asked for, so the page has a fixed end and links below the list work.
  function addPage() {
    const next = hist.list.slice(hist.shown, hist.shown + PAGE);
    drawList.append(...next.map(drawItem));
    hist.shown += next.length;
    const left = hist.list.length - hist.shown;
    moreBtn.hidden = left <= 0;
    moreBtn.textContent = t('h.more', { n: Math.min(PAGE, left), left: int(left) });
  }
  moreBtn.addEventListener('click', addPage);

  function applyFilters() {
    const want = parseLine(fContains.value);
    const year = +fYear.value;
    const status = fStatus.value;
    let list = draws.filter(d =>
      (!year || d.year === year)
      && (!status || (status === 'won' ? d.status === 'won' : d.status === 'rollover' || d.status === 'rolldown'))
      && (want.error || (want.n.every(x => d.n.includes(x)) && want.s.every(x => d.s.includes(x)))));
    if (fSort.value === 'pot') list = list.filter(d => d.pot != null).sort((a, b) => b.pot - a.pot || b.no - a.no);
    else if (fSort.value === 'new') list.reverse();

    hist.list = list;
    hist.shown = 0;
    drawList.replaceChildren();
    if (!list.length) drawList.append(h('li', { class: 'draws-empty', text: t('h.noMatch') }));
    $('#f-count').textContent = want.error ? want.error
      : list.length === draws.length ? t('h.countAll', { count: int(draws.length) }) : t('h.countSome', { draws: drawsN(list.length), total: int(draws.length) });
    addPage();
  }

  for (const el of [fYear, fStatus, fSort]) el.addEventListener('change', applyFilters);
  fContains.addEventListener('input', applyFilters);

  /* ---------- same date in past years ---------- */

  const sdDate = $('#sd-date');
  const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

  /** Today if it is a Tuesday or Friday, otherwise the next one. */
  function nextDrawDay() {
    const d = new Date();
    while (d.getDay() !== 2 && d.getDay() !== 5) d.setDate(d.getDate() + 1);
    return iso(d);
  }

  function renderSameDay() {
    const list = $('#sd-list');
    const count = $('#sd-count');
    const m = sdDate.value.match(/^(\d{4})-(\d{2})-(\d{2})$/);
    if (!m) {
      list.replaceChildren(h('li', { class: 'draws-empty', text: t('sd.choose') }));
      count.textContent = '';
      return;
    }
    const picked = utc(sdDate.value);
    const day = fmtDay(picked);
    const drawDay = [2, 5].includes(picked.getUTCDay());
    const same = draws.filter(d => d.date.slice(5) === `${m[2]}-${m[3]}`).reverse();
    list.replaceChildren(...(same.length ? same.map(drawItem) : [h('li', { class: 'draws-empty', text: t('sd.none', { day }) })]));
    count.textContent = [
      same.length && t('sd.found', { day, draws: drawsN(same.length), years: same.map(d => d.year).reverse().join(', ') }),
      !drawDay && t('sd.notDrawDay', { date: fmtT(picked) }),
    ].filter(Boolean).map(s => s.charAt(0).toUpperCase() + s.slice(1)).join(' ');
  }

  sdDate.addEventListener('input', renderSameDay);
  $('#sd-next').addEventListener('click', () => { sdDate.value = nextDrawDay(); renderSameDay(); });
  sdDate.value = nextDrawDay();

  /* ---------- facts ---------- */

  function renderFacts() {
    const won = draws.filter(d => d.status === 'won');
    const maxPot = Math.max(...won.map(d => d.pot || 0));
    const biggest = won.filter(d => d.pot === maxPot);
    const seenFive = new Map();
    const seenFull = new Set();
    const repeatedFive = [];
    let repeatedFull = 0;
    for (const d of draws) {
      const five = d.n.join(' ');
      const full = `${five}|${d.s.join(' ')}`;
      if (seenFull.has(full)) repeatedFull++;
      seenFull.add(full);
      if (seenFive.has(five)) repeatedFive.push(t('fact.pair', { nums: d.n.join(', '), a: fmtDate(seenFive.get(five)), b: fmtDate(d) }));
      else seenFive.set(five, d);
    }
    const note = repeatedFive.length
      ? t('fact.repeat5', { times: times(repeatedFive.length), list: repeatedFive.join('; ') })
      : t('fact.repeat5none');
    const tiles = [
      [int(draws.length), t('fact.draws', { a: fmtDate(first), b: fmtDate(latest) })],
      [int(won.length), t('fact.won', { n: won.reduce((acc, d) => acc + d.jw, 0) })],
      [money(maxPot), biggest.length === 1 ? t('fact.biggest1', { date: fmtDate(biggest[0]) }) : t('fact.biggestN', { times: times(biggest.length), date: fmtDate(biggest[0]) })],
      [repeatedFull ? times(repeatedFull) : t('fact.never'), t('fact.repeat', { note })],
    ];
    $('#facts').replaceChildren(...tiles.map(([value, label]) => h('div', { class: 'fact' }, h('p', { class: 'fact-value', text: value }), h('p', { class: 'fact-label', text: label }))));
  }

  /* ---------- about ---------- */

  function renderAbout(desktopApp) {
    const checks = DATA.checks || {};
    const generated = new Date(DATA.generated);
    $('#about-checks').textContent = [
      t('about.checks', { count: int(draws.length), date: Number.isNaN(+generated) ? '?' : fmtMed(new Date(Date.UTC(generated.getFullYear(), generated.getMonth(), generated.getDate()))) }),
      checks.crossChecked && t('about.cross', { n: checks.crossChecked }),
      checks.officialLatest && t('about.official'),
    ].filter(Boolean).join(' ');
    $('#about-sources').replaceChildren(...(DATA.sources || []).map((src, i) => h('li', {},
      /^https:\/\//.test(src.url) ? h('a', { href: src.url, target: '_blank', rel: 'noopener noreferrer', text: src.name }) : h('strong', { text: src.name }),
      h('br'), `src${i}` in EN ? t(`src${i}`) : src.role)));
    $('#about-refresh').replaceChildren(...rich(desktopApp ? 'about.refreshApp' : 'about.refreshWeb'));
    $('#about-update').hidden = !desktopApp;
  }

  /* ---------- updates, when running inside the EuroWatcher desktop app ---------- */

  const DISMISS_KEY = 'eurowatcher.updateDismissed';

  async function runUpdate() {
    const status = $('#update-status');
    const buttons = [$('#update-now'), $('#update-manual')];
    const say = text => {
      status.textContent = text;
      $('#update-body').textContent = text;
    };
    buttons.forEach(b => { b.disabled = true; });
    say(t('u.working'));
    try {
      const res = await fetch('api/update', { method: 'POST', headers: { 'X-EuroWatcher': '1' }, cache: 'no-store' });
      const out = await res.json();
      if (!out.ok) throw new Error(out.message || `HTTP ${res.status}`);
      if (out.added > 0) {
        say(t('u.added', { n: out.added }));
        setTimeout(() => location.reload(), 1200);
        return;
      }
      say(t('u.current'));
    } catch (err) {
      say(t('u.failed', { message: err.message }));
    }
    buttons.forEach(b => { b.disabled = false; });
  }

  async function detectDesktopApp() {
    if (!/^https?:$/.test(location.protocol)) return false;
    try {
      const res = await fetch('api/status', { cache: 'no-store' });
      if (!res.ok) return false;
      const info = await res.json();
      return info && info.app === 'EuroWatcher';
    } catch {
      return false;
    }
  }

  function setupDesktopApp() {
    // Tells the app the page is still open (it closes itself a few minutes after the last check-in).
    setInterval(() => { fetch('api/status', { cache: 'no-store' }).catch(() => {}); }, 60000);
    renderLatest(true);
    renderAbout(true);
    $('#update-manual').addEventListener('click', runUpdate);
    $('#update-now').addEventListener('click', runUpdate);
    if (!missed.length) return;
    // Ask once for each new draw: "Later" hides the bar until the next draw is out.
    const newest = iso(missed[missed.length - 1]);
    let dismissed = null;
    try { dismissed = localStorage.getItem(DISMISS_KEY); } catch { /* storage unavailable */ }
    if (dismissed === newest) return;
    $('#update-body').textContent = t('u.body', { n: missed.length, date: fmtDate(latest) });
    $('#update-bar').hidden = false;
    $('#update-later').addEventListener('click', () => {
      try { localStorage.setItem(DISMISS_KEY, newest); } catch { /* not persisted */ }
      $('#update-bar').hidden = true;
    });
  }

  /* ---------- start ---------- */

  renderLatest(false);
  renderAbout(false);
  renderFacts();
  renderSameDay();
  applyFilters();
  const fromUrl = parseLine(new URLSearchParams(location.search).get('line') || '');
  if (!fromUrl.error && fromUrl.n.length + fromUrl.s.length) setLine(fromUrl.n, fromUrl.s);
  else changed();
  detectDesktopApp().then(yes => { if (yes) setupDesktopApp(); });
})();
