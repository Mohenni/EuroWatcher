#!/usr/bin/env node
/**
 * Rebuilds data/draws.js, the full EuroMillions draw history the app reads.
 *
 *   node tools/update-data.mjs
 *
 * Three public sources are read (nothing is sent to them):
 *   list    every draw: number, date, balls, UK jackpot figure
 *   prizes  every draw: balls again, plus the prize breakdown in euros
 *   latest  the official UK National Lottery record of the most recent draw
 *
 * The balls must agree between sources wherever more than one has a draw. If they
 * don't, or anything else looks wrong, the script stops and leaves the existing
 * file untouched.
 *
 * Offline / testing: --list-file, --prizes-file and --latest-file read a saved
 * copy instead of fetching.
 */
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const OUT = path.join(ROOT, 'data', 'draws.js');
const UA = 'Mozilla/5.0 (compatible; draw-history-refresh)';

const SOURCES = {
  list: {
    name: 'Merseyworld lottery archive',
    url: 'https://lottery.merseyworld.com/cgi-bin/lottery?days=20&Machine=Z&Ballset=0&order=1&show=1&year=0&display=CSV',
    role: 'Draw numbers, dates and balls for every draw',
  },
  prizes: {
    name: 'EuroMillions API (pedromealha.dev)',
    url: 'https://euromillions.api.pedromealha.dev/v1/draws',
    role: 'Second copy of the balls, plus winners and prizes in euros',
  },
  latest: {
    name: 'UK National Lottery results feed',
    url: 'https://www.national-lottery.co.uk/results/euromillions/draw-history/xml',
    role: 'Official record of the most recent draw',
  },
};

// Official prize order: [numbers matched, Lucky Stars matched].
const TIERS = [[5, 2], [5, 1], [5, 0], [4, 2], [4, 1], [3, 2], [4, 0], [2, 2], [3, 1], [3, 0], [1, 2], [2, 1], [2, 0]];
const MONTHS = { Jan: 1, Feb: 2, Mar: 3, Apr: 4, May: 5, Jun: 6, Jul: 7, Aug: 8, Sep: 9, Oct: 10, Nov: 11, Dec: 12 };
const TUESDAYS_SINCE = '2011-05-10'; // second weekly draw, 11 stars, 2+0 prize tier
const STAR_12_SINCE = '2016-09-27';

const asc = (a, b) => a - b;
const sleep = ms => new Promise(r => setTimeout(r, ms));
const log = (...a) => console.log(...a);
const comboKey = d => `${d.n.join(',')}|${d.s.join(',')}`;

function parseArgs(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i++) {
    const m = argv[i].match(/^--(list|prizes|latest)-file(?:=(.*))?$/);
    if (!m) throw new Error(`Unknown option: ${argv[i]}`);
    out[m[1]] = m[2] ?? argv[++i];
  }
  return out;
}

async function load(key, files) {
  if (files[key]) return readFile(files[key], 'utf8');
  let lastErr;
  for (const wait of [0, 5, 15, 30]) {
    if (wait) {
      log(`  ${key}: ${lastErr.message}; retrying in ${wait}s`);
      await sleep(wait * 1000);
    }
    try {
      const res = await fetch(SOURCES[key].url, {
        headers: { 'user-agent': UA, accept: '*/*' },
        signal: AbortSignal.timeout(90_000),
      });
      if (res.ok) return await res.text();
      lastErr = new Error(`HTTP ${res.status}`);
      if (res.status !== 429 && res.status < 500) break;
    } catch (err) {
      lastErr = err;
    }
  }
  throw lastErr;
}

/** "1997, Tue,10,Nov,2026, 15,18,31,37,41,01,05, 118812405,    0" */
function parseList(text) {
  const draws = new Map();
  const row = /^\s*(\d+),\s*\w{3},\s*(\d+),(\w{3}),(\d{4}),\s*(\d+),(\d+),(\d+),(\d+),(\d+),(\d+),(\d+),\s*(\d*),\s*(\d*)/;
  for (const line of text.split(/\r?\n/)) {
    const m = line.match(row);
    if (!m || !MONTHS[m[3]]) continue;
    const date = `${m[4]}-${String(MONTHS[m[3]]).padStart(2, '0')}-${m[2].padStart(2, '0')}`;
    draws.set(date, {
      no: +m[1],
      date,
      n: m.slice(5, 10).map(Number).sort(asc),
      s: m.slice(10, 12).map(Number).sort(asc),
      gbp: m[12] === '' ? null : +m[12],
      wins: m[13] === '' ? null : +m[13],
    });
  }
  return draws;
}

function parsePrizes(text) {
  const draws = new Map();
  for (const item of JSON.parse(text)) {
    const date = String(item.date).slice(0, 10);
    let prizes = null;
    let winners = null;
    if (Array.isArray(item.prizes) && item.prizes.length) {
      const find = ([m, s]) => item.prizes.find(p => p.matched_numbers === m && p.matched_stars === s);
      winners = TIERS.map(t => find(t)?.winners ?? null);
      // A tier nobody won has no per-winner amount, except the jackpot, where the figure is the pot.
      prizes = TIERS.map((t, i) => {
        const p = find(t)?.prize;
        if (p == null || (i > 0 && p === 0)) return null;
        return Math.round(p * 100) / 100;
      });
    }
    draws.set(date, {
      date,
      n: item.numbers.map(Number).sort(asc),
      s: item.stars.map(Number).sort(asc),
      prizes,
      winners,
    });
  }
  return draws;
}

function parseLatest(text) {
  const one = re => text.match(re)?.[1];
  const all = re => [...text.matchAll(re)].map(m => +m[1]);
  const date = one(/<draw-date>(\d{4}-\d{2}-\d{2})</);
  const n = all(/<ball number="\d+">(\d+)</g).sort(asc);
  const s = all(/<bonus-ball type="luckystar" number="\d+">(\d+)</g).sort(asc);
  if (!date || n.length !== 5 || s.length !== 2) throw new Error('unrecognised format');
  let winners = null;
  const block = text.match(/<prize-tiers country="All">([\s\S]*?)<\/prize-tiers>/)?.[1];
  if (block && one(/<confirmed>(\w)</) === 'Y') {
    const byLevel = new Map([...block.matchAll(/<prize-tier level="(\d+)"><number-of-winners>(\d+)</g)].map(m => [+m[1], +m[2]]));
    if (byLevel.size === TIERS.length) winners = TIERS.map((_, i) => byLevel.get(i + 1));
  }
  const gbp = one(/<jackpot-amount>([\d,]+)</);
  return { no: +one(/<draw-number>(\d+)</) || null, date, n, s, winners, gbp: gbp ? +gbp.replace(/,/g, '') : null };
}

async function loadExisting() {
  try {
    const text = await readFile(OUT, 'utf8');
    const json = text.match(/window\.EURO_DATA\s*=\s*(\{[\s\S]*\});?\s*$/)?.[1];
    const data = JSON.parse(json);
    const draws = new Map(data.draws.map(([no, date, n, s, prizes, winners, gbp]) => [date, { no, date, n, s, prizes, winners, gbp }]));
    draws.checks = data.checks || {};
    return draws;
  } catch {
    return new Map();
  }
}

/** On GitHub, also shows a message on the job's summary page. */
function note(level, message) {
  log(`  ${message}`);
  if (process.env.GITHUB_ACTIONS) console.log(`::${level}::${message.replace(/\r?\n/g, ' ')}`);
}

function validate(d) {
  const issues = [];
  const okSet = (arr, len, max) => arr.length === len && new Set(arr).size === len && arr.every(x => Number.isInteger(x) && x >= 1 && x <= max);
  const starMax = d.date < TUESDAYS_SINCE ? 9 : d.date < STAR_12_SINCE ? 11 : 12;
  if (!okSet(d.n, 5, 50)) issues.push('numbers out of range');
  if (!okSet(d.s, 2, starMax)) issues.push(`stars out of range for that date (max ${starMax})`);
  const weekday = new Date(`${d.date}T00:00:00Z`).getUTCDay();
  if (weekday !== 5 && !(weekday === 2 && d.date >= TUESDAYS_SINCE)) issues.push('not a draw day');
  return issues;
}

async function main() {
  const files = parseArgs(process.argv.slice(2));
  const previous = await loadExisting();

  log('Reading sources…');
  let list = null;
  try {
    list = parseList(await load('list', files));
    if (list.size < 1900) throw new Error(`only ${list.size} draws in it`);
    log(`  list:   ${list.size} draws`);
  } catch (err) {
    list = null;
    note('warning', `Draw list (${SOURCES.list.name}) unavailable: ${err.message}`);
  }

  let prizes = new Map();
  try {
    prizes = parsePrizes(await load('prizes', files));
    log(`  prizes: ${prizes.size} draws`);
  } catch (err) {
    note('warning', `Prize source (${SOURCES.prizes.name}) unavailable: ${err.message}`);
  }

  let latest = null;
  try {
    latest = parseLatest(await load('latest', files));
    log(`  latest: draw of ${latest.date}`);
  } catch (err) {
    note('warning', `Official feed (${SOURCES.latest.name}) unavailable: ${err.message}`);
  }

  if (!list) {
    if (!previous.size) throw new Error('The draw list is needed to build the history from scratch.');
    return addToExisting(previous, prizes, latest);
  }

  const dates = [...new Set([...list.keys(), ...prizes.keys(), ...(latest ? [latest.date] : [])])].sort();
  const problems = [];
  let crossChecked = 0;
  let officialLatest = false;

  const draws = dates.map((date, i) => {
    const L = list.get(date);
    const P = prizes.get(date);
    const O = latest?.date === date ? latest : null;
    const old = previous.get(date);
    const copies = [L, P, O].filter(Boolean);
    if (new Set(copies.map(comboKey)).size > 1) {
      problems.push(`${date}: sources disagree on the balls (${copies.map(comboKey).join('  vs  ')})`);
    } else if (copies.length > 1) {
      crossChecked++;
      if (O && i === dates.length - 1) officialLatest = true;
    }
    const d = { no: L?.no ?? O?.no ?? i + 1, date, n: copies[0].n, s: copies[0].s };
    if (d.no !== i + 1) problems.push(`${date}: draw number ${d.no} breaks the sequence (expected ${i + 1})`);
    for (const issue of validate(d)) problems.push(`${date}: ${issue}`);

    d.prizes = P?.prizes ?? old?.prizes ?? null;
    d.winners = P?.winners ?? O?.winners ?? old?.winners ?? null;
    if (!d.winners) {
      // Only the list knows this draw. Its "0 winners" is trusted once the next draw shows the pot kept growing.
      const next = list.get(dates[i + 1]);
      let jackpotWinners = L?.wins ?? null;
      if (jackpotWinners === 0 && !(next?.gbp != null && L.gbp != null && next.gbp >= L.gbp * 0.5)) jackpotWinners = null;
      if (jackpotWinners != null) d.winners = [jackpotWinners, ...Array(TIERS.length - 1).fill(null)];
    }
    d.gbp = L?.gbp ?? O?.gbp ?? old?.gbp ?? null;
    return d;
  });

  if (draws.length < previous.size) problems.push(`only ${draws.length} draws found but ${previous.size} are already on file`);
  await save(draws, previous, problems, crossChecked, officialLatest);
}

/**
 * Used when the draw list cannot be read (it refuses some data centres, GitHub's included).
 * Keeps the history on file and adds a new draw when the prize source and the official feed agree
 * on it ball for ball, or when the official feed alone reports it with the next draw number in the
 * sequence (it is the lottery operator's own record). Prize details missing then are filled in by a
 * later run. Anything else waits for a later run.
 */
async function addToExisting(previous, prizes, latest) {
  log('  Draw list unavailable: adding to the history on file, from the other two sources.');
  const problems = [];
  let crossChecked = previous.checks?.crossChecked || 0;
  let officialLatest = previous.checks?.officialLatest ?? false;
  // Fill in prize details of draws already on file (recent ones are often incomplete), when the balls agree.
  const draws = [...previous.values()].map(d => {
    const P = prizes.get(d.date);
    if (!P) return d;
    if (comboKey(P) !== comboKey(d)) {
      problems.push(`${d.date}: the prize source disagrees with the balls on file (${comboKey(P)} vs ${comboKey(d)})`);
      return d;
    }
    return { ...d, prizes: P.prizes ?? d.prizes, winners: P.winners ?? d.winners };
  });
  const lastOnFile = draws[draws.length - 1];
  if (latest && latest.date === lastOnFile.date) {
    if (comboKey(latest) !== comboKey(lastOnFile)) problems.push(`${latest.date}: the official feed disagrees with the balls on file`);
    else officialLatest = true;
  }

  const newDates = [...new Set([...prizes.keys(), ...(latest ? [latest.date] : [])])].filter(date => date > lastOnFile.date).sort();
  for (const date of newDates) {
    const P = prizes.get(date);
    const O = latest?.date === date ? latest : null;
    const nextNo = draws[draws.length - 1].no + 1;
    if (!O || (!P && O.no !== nextNo)) {
      note('notice', `${date}: not confirmed yet; it will be added by a later run.`);
      break;
    }
    if (!P) note('notice', `${date}: added from the official feed alone (draw ${O.no}); prize details will follow.`);
    else if (comboKey(P) !== comboKey(O)) {
      problems.push(`${date}: sources disagree on the balls (${comboKey(P)}  vs  ${comboKey(O)})`);
      break;
    }
    const d = { no: nextNo, date, n: O.n, s: O.s, prizes: P?.prizes ?? null, winners: P?.winners ?? O.winners, gbp: O.gbp ?? null };
    if (O.no && O.no !== d.no) problems.push(`${date}: draw number ${O.no} breaks the sequence (expected ${d.no})`);
    for (const issue of validate(d)) problems.push(`${date}: ${issue}`);
    draws.push(d);
    crossChecked++;
    officialLatest = true;
  }
  await save(draws, previous, problems, crossChecked, officialLatest);
}

async function save(draws, previous, problems, crossChecked, officialLatest) {
  if (problems.length) {
    console.error(`\nStopped: ${problems.length} problem(s). ${path.relative(ROOT, OUT)} was not changed.`);
    for (const p of problems.slice(0, 25)) console.error(`  - ${p}`);
    if (process.env.GITHUB_ACTIONS) console.log(`::error::Draw data not updated: ${problems[0]}`);
    process.exit(1);
  }

  const checks = { crossChecked, officialLatest, withPrizes: draws.filter(d => d.prizes).length };
  const rows = draws.map(d => JSON.stringify([d.no, d.date, d.n, d.s, d.prizes, d.winners, d.gbp]));
  const body = [
    '{',
    ` "generated": ${JSON.stringify(new Date().toISOString())},`,
    ` "tiers": ${JSON.stringify(TIERS)},`,
    ` "sources": ${JSON.stringify(Object.values(SOURCES))},`,
    ` "checks": ${JSON.stringify(checks)},`,
    ' "draws": [',
    rows.join(',\n'),
    ' ]',
    '}',
  ].join('\n');
  JSON.parse(body); // must stay valid JSON
  await mkdir(path.dirname(OUT), { recursive: true });
  await writeFile(OUT, `// Generated by tools/update-data.mjs or the EuroWatcher app. Do not edit by hand.\nwindow.EURO_DATA = ${body};\n`);

  const added = draws.length - previous.size;
  const last = draws[draws.length - 1];
  log(`\nWrote ${path.relative(ROOT, OUT)}`);
  log(`  ${draws.length} draws, ${draws[0].date} to ${last.date}${previous.size ? ` (${added} new)` : ''}`);
  log(`  ${crossChecked} confirmed by a second source, ${checks.withPrizes} with a euro prize breakdown`);
  log(`  latest: #${last.no} ${last.date}  ${last.n.join(' ')} + ${last.s.join(' ')}${officialLatest ? '  (matches the official feed)' : ''}`);
  if (process.env.GITHUB_ACTIONS) console.log(`::notice::${draws.length} draws on file (${added} new), latest ${last.date}: ${last.n.join(' ')} + ${last.s.join(' ')}`);
}

main().catch(err => {
  console.error(`\nUpdate failed: ${err.message}`);
  process.exit(1);
});
