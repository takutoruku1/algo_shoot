#!/usr/bin/env node
// docs/progress.json から自動開発ダッシュボード(HTML)を生成する。
// 使い方: node tools/dashboard.mjs  → docs/dashboard.html
import { readFileSync, writeFileSync } from 'node:fs';

const ROOT = new URL('..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const d = JSON.parse(readFileSync(`${ROOT}/docs/progress.json`, 'utf8'));

const esc = (s) =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

// `バッククォート` を <code> に
const md = (s) => esc(s).replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

const task = (t, cls = '') => `
      <li class="task ${cls}">
        <div class="task-head">
          ${t.priority ? `<span class="chip chip-${t.priority.toLowerCase()}">${t.priority}</span>` : '<span class="chip chip-none">—</span>'}
          <span class="task-title">${md(t.title)}</span>
          ${t.worker ? `<span class="worker">${esc(t.worker)}</span>` : ''}
        </div>
        ${t.accept ? `<p class="accept">${md(t.accept)}</p>` : ''}
      </li>`;

const list = (arr, cls, empty) =>
  arr.length ? `<ol class="tasks">${arr.map((t) => task(t, cls)).join('')}</ol>` : `<p class="empty">${empty}</p>`;

const { todo, wip, blocked, done, commits, percent, counts, generated } = d;
// 進捗レールは20分割。ゲームのHPバーの語彙に合わせたセグメント表現
const SEG = 20;
const filled = Math.round((percent / 100) * SEG);
const rail = Array.from(
  { length: SEG },
  (_, i) => `<span class="seg${i < filled ? ' on' : ''}"></span>`
).join('');

const html = `<title>MINA Nightly Build</title>
<style>
  :root {
    --ground: #0B0E14;
    --surface: #141922;
    --surface-2: #1B2130;
    --line: #262E3D;
    --ink: #E4E9F2;
    --ink-dim: #8A93A6;
    --ink-faint: #5A6274;
    --accent: #5FD3C4;
    --warn: #F2B13C;
    --mute: #6A7386;
    --mono: ui-monospace, "SFMono-Regular", "Cascadia Mono", Menlo, Consolas, monospace;
    --sans: ui-sans-serif, system-ui, "Segoe UI", "Hiragino Sans", "Noto Sans JP", sans-serif;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0;
    background: var(--ground);
    color: var(--ink);
    font-family: var(--sans);
    line-height: 1.7;
    font-variant-numeric: tabular-nums;
  }
  .wrap { max-width: 980px; margin: 0 auto; padding: clamp(24px, 5vw, 64px) clamp(16px, 4vw, 32px) 80px; }

  .eyebrow {
    font-family: var(--mono);
    font-size: 11px;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: var(--ink-faint);
    margin: 0 0 8px;
  }

  /* ── 実行状況バンド: auto/dev を直接ポーリングするので main マージ前でも動きが出る ── */
  .live {
    display: flex; align-items: baseline; gap: 10px; flex-wrap: wrap;
    margin: 0 0 24px; padding: 10px 14px; border-radius: 6px;
    background: var(--surface); border: 1px solid var(--line);
    font-size: 13px;
  }
  .live-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--mute); flex: none; align-self: center; }
  .live-label { font-weight: 600; }
  .live-detail { color: var(--ink-dim); font-family: var(--mono); font-size: 12px; overflow-wrap: anywhere; }
  .live-running { border-color: var(--warn); background: rgba(242,177,60,0.07); }
  .live-running .live-dot { background: var(--warn); animation: pulse 1.6s ease-in-out infinite; }
  .live-running .live-label { color: var(--warn); }
  .live-pending { border-color: var(--accent); background: rgba(95,211,196,0.06); }
  .live-pending .live-dot { background: var(--accent); }
  .live-pending .live-label { color: var(--accent); }
  .live-idle .live-label { color: var(--ink-dim); }
  @keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.25; } }
  @media (prefers-reduced-motion: reduce) { .live-running .live-dot { animation: none; } }

  /* ── ヘッドライン: 消化率を一番大きく ── */
  header { border-bottom: 1px solid var(--line); padding-bottom: 32px; margin-bottom: 32px; }
  h1 { font-size: clamp(20px, 3vw, 26px); font-weight: 600; margin: 0 0 28px; letter-spacing: -0.01em; }
  .figure { display: flex; align-items: baseline; gap: 14px; flex-wrap: wrap; }
  .pct {
    font-family: var(--mono);
    font-size: clamp(56px, 12vw, 96px);
    font-weight: 600;
    line-height: 1;
    color: var(--accent);
    letter-spacing: -0.03em;
  }
  .pct-unit { font-size: 0.4em; color: var(--ink-dim); }
  .figure-note { color: var(--ink-dim); font-size: 14px; }
  .rail { display: flex; gap: 3px; margin-top: 20px; }
  .seg { flex: 1; height: 10px; border-radius: 1px; background: var(--surface-2); }
  .seg.on { background: var(--accent); box-shadow: 0 0 10px -2px var(--accent); }

  /* ── 状態4セル ── */
  .states { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 1px; background: var(--line); border: 1px solid var(--line); border-radius: 6px; overflow: hidden; margin-bottom: 40px; }
  .state { background: var(--surface); padding: 16px 18px; }
  .state-n { font-family: var(--mono); font-size: 30px; font-weight: 600; line-height: 1.1; }
  .state-l { font-size: 12px; color: var(--ink-dim); }
  .state.done .state-n { color: var(--accent); }
  .state.wip .state-n { color: var(--warn); }
  .state.todo .state-n { color: var(--ink); }
  .state.blocked .state-n { color: var(--mute); }

  section { margin-bottom: 40px; }
  h2 { font-size: 13px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase; color: var(--ink-dim); margin: 0 0 14px; padding-bottom: 8px; border-bottom: 1px solid var(--line); }

  .tasks { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
  .task { background: var(--surface); border-left: 2px solid var(--line); padding: 12px 16px; border-radius: 0 4px 4px 0; }
  .task-head { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
  .task-title { font-weight: 500; }
  .accept { margin: 6px 0 0; font-size: 13px; color: var(--ink-dim); line-height: 1.6; }
  .task.is-wip { border-left-color: var(--warn); background: var(--surface-2); }
  .task.is-done { border-left-color: var(--accent); }
  .task.is-blocked { border-left-color: var(--mute); opacity: 0.72; }

  .chip { font-family: var(--mono); font-size: 10px; font-weight: 600; letter-spacing: 0.06em; padding: 2px 7px; border-radius: 3px; background: var(--surface-2); color: var(--ink-dim); }
  .chip-p1 { background: rgba(242,177,60,0.14); color: var(--warn); }
  .chip-p2 { background: rgba(95,211,196,0.12); color: var(--accent); }
  .chip-p3 { background: var(--surface-2); color: var(--ink-faint); }
  .chip-none { color: var(--ink-faint); }
  .worker { font-family: var(--mono); font-size: 11px; color: var(--ink-faint); border: 1px solid var(--line); padding: 1px 7px; border-radius: 99px; }

  .empty { color: var(--ink-faint); font-style: italic; font-size: 14px; margin: 0; padding: 12px 16px; background: var(--surface); border-radius: 4px; }

  .log { list-style: none; margin: 0; padding: 0; font-family: var(--mono); font-size: 12.5px; display: flex; flex-direction: column; gap: 6px; }
  .log li { display: flex; gap: 12px; align-items: baseline; color: var(--ink-dim); }
  .log .h { color: var(--accent); }
  .log .s { color: var(--ink); flex: 1; min-width: 0; overflow-wrap: anywhere; }

  code { font-family: var(--mono); font-size: 0.9em; background: var(--surface-2); padding: 1px 5px; border-radius: 3px; color: var(--ink); }
  footer { margin-top: 56px; padding-top: 20px; border-top: 1px solid var(--line); font-size: 12px; color: var(--ink-faint); font-family: var(--mono); }
</style>

<div class="wrap">
  <header>
    <p class="eyebrow">algo_shoot / 夜間自動開発</p>
    <h1>MINA Nightly Build</h1>

    <!-- 実行状況: auto/dev ブランチを直接見にいくので、main へマージされる前でも動きが見える -->
    <div id="live" class="live live-idle">
      <span class="live-dot"></span>
      <span class="live-label">実行状況を確認しています…</span>
      <span class="live-detail"></span>
    </div>

    <div class="figure">
      <span class="pct">${percent}<span class="pct-unit">%</span></span>
      <span class="figure-note">完了 ${counts.done} / 対象 ${counts.done + counts.wip + counts.todo} タスク<br>保留 ${counts.blocked} 件は人間の判断待ちのため対象外</span>
    </div>
    <div class="rail" role="img" aria-label="進捗 ${percent}パーセント">${rail}</div>
  </header>

  <div class="states">
    <div class="state done"><div class="state-n">${counts.done}</div><div class="state-l">完了</div></div>
    <div class="state wip"><div class="state-n">${counts.wip}</div><div class="state-l">作業中</div></div>
    <div class="state todo"><div class="state-n">${counts.todo}</div><div class="state-l">残り</div></div>
    <div class="state blocked"><div class="state-n">${counts.blocked}</div><div class="state-l">保留</div></div>
  </div>

  <section>
    <h2>いま作業中</h2>
    <div id="live-wip">${list(wip, 'is-wip', 'アイドル — 次の実行を待っています')}</div>
  </section>

  <section>
    <h2>キュー（上から消化）</h2>
    ${list(todo, '', 'キューが空です。タスクを積んでください')}
  </section>

  <section>
    <h2>完了</h2>
    ${list(done, 'is-done', 'まだありません')}
  </section>

  <section>
    <h2>保留 — 自動では進められない</h2>
    ${list(blocked, 'is-blocked', 'なし')}
  </section>

  <section>
    <h2>直近のコミット</h2>
    <ul class="log">
      ${commits.map((c) => `<li><span class="h">${esc(c.hash)}</span><span>${esc(c.date)}</span><span class="s">${esc(c.subject)}</span></li>`).join('\n      ')}
    </ul>
  </section>

  <footer>生成 ${esc(generated)} — DEV_QUEUE.md より自動生成</footer>
</div>

<script>
// このページは main の DEV_QUEUE.md から生成される静的スナップショット。
// 夜間 routine は auto/dev で作業し朝までマージされないので、そのままだと
// 「動いている最中」が見えない。GitHub の公開APIで auto/dev を直接見て、
// 実行中かどうかと最後に何をしたかを出す（public repo なので認証は不要）。
(function () {
  var REPO = 'takutoruku1/algo_shoot';
  var RUNNING_WINDOW_MS = 15 * 60 * 1000; // 直近コミットがこれ以内なら「実行中」とみなす
  var el = document.getElementById('live');
  if (!el) return;
  var dot = el.querySelector('.live-dot');
  var label = el.querySelector('.live-label');
  var detail = el.querySelector('.live-detail');

  function setState(cls, text, sub) {
    el.className = 'live ' + cls;
    label.textContent = text;
    detail.textContent = sub || '';
  }

  // コミットメッセージの1行目。改行は正規表現で取る（生成側のテンプレートで
  // エスケープが食われないよう、文字列リテラルの \\n を避けている）
  function firstLine(msg) {
    return String(msg).split(/[\\r\\n]/)[0];
  }

  function ago(ms) {
    var m = Math.round(ms / 60000);
    if (m < 1) return 'たった今';
    if (m < 60) return m + '分前';
    var h = Math.floor(m / 60);
    if (h < 24) return h + '時間' + (m % 60) + '分前';
    return Math.floor(h / 24) + '日前';
  }

  function refresh() {
    fetch('https://api.github.com/repos/' + REPO + '/commits?sha=auto/dev&per_page=100', {
      cache: 'no-store',
    })
      .then(function (r) {
        // ブランチが無い = 夜間の成果は全てマージ済み（404）
        if (r.status === 404) return null;
        if (!r.ok) throw new Error('HTTP ' + r.status);
        return r.json();
      })
      .then(function (commits) {
        if (!commits || !commits.length) {
          setState('live-idle', 'アイドル', '未マージの作業はありません（次の実行は JST 3:00）');
          return;
        }
        var latest = commits[0];
        var when = new Date(latest.commit.committer.date);
        var age = Date.now() - when.getTime();
        // WIP コミットは着手の印。auto: で始まる完了コミットだけ数える
        var doneCount = commits.filter(function (c) {
          var s = firstLine(c.commit.message);
          return s.indexOf('auto:') === 0 && s.indexOf('auto: WIP') !== 0;
        }).length;
        var subject = firstLine(latest.commit.message).replace(/^auto:\\s*/, '');

        if (age < RUNNING_WINDOW_MS) {
          setState(
            'live-running',
            '実行中',
            doneCount + '件コミット済み · ' + ago(age) + ': ' + subject
          );
        } else {
          setState(
            'live-pending',
            'マージ待ち',
            doneCount + '件が auto/dev に未マージ · 最終 ' + ago(age) + ' · 朝 7:00 に自動マージ'
          );
        }
      })
      .catch(function () {
        setState('live-idle', '実行状況を取得できません', 'GitHub API に接続できませんでした');
      });
  }

  refresh();
  setInterval(refresh, 60000); // 1分ごとに更新
})();
</script>
`;

writeFileSync(`${ROOT}/docs/dashboard.html`, html);
console.log('docs/dashboard.html updated');
