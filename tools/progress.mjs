#!/usr/bin/env node
// DEV_QUEUE.md をパースして PROGRESS.md と docs/progress.json を生成する。
// 使い方: node tools/progress.mjs
import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';

const ROOT = new URL('..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const p = (f) => `${ROOT}/${f}`;

const queue = readFileSync(p('DEV_QUEUE.md'), 'utf8');

// "## SECTION" ごとに "- [ ] ..." 行を集める
function section(name) {
  // 末尾セクション(DONE)も拾えるように、次の "## " か文字列末尾で止める
  const m = queue.match(new RegExp(`^## ${name}\\s*$([\\s\\S]*?)(?=^## |$(?![\\s\\S]))`, 'm'));
  if (!m) return [];
  return m[1]
    .split('\n')
    .filter((l) => /^\s*- \[.\]/.test(l))
    .map((l) => l.replace(/^\s*- \[.\]\s*/, '').trim());
}

function parseTask(line) {
  const prio = (line.match(/^\((P\d)\)\s*/) || [])[1] || null;
  const body = line.replace(/^\(P\d\)\s*/, '');
  // 記法: <タイトル> | <担当> | <受入条件>
  const [title, worker, ...rest] = body.split('|').map((s) => s.trim());
  const known = /^(engineer|qa|artist|scenario|composer|game-designer)$/;
  return {
    priority: prio,
    title,
    worker: known.test(worker || '') ? worker : null,
    accept: rest.join(' | ').trim() || null,
  };
}

const todo = section('TODO').map(parseTask);
const wip = section('WIP').map(parseTask);
const blocked = section('BLOCKED').map(parseTask);
const done = section('DONE').map(parseTask);

// 消化率は「人間の判断待ち」である BLOCKED を分母から外す（routine の力では動かせないため）
const actionable = todo.length + wip.length + done.length;
const pct = actionable === 0 ? 100 : Math.round((done.length / actionable) * 100);

let commits = [];
try {
  commits = execFileSync(
    'git',
    ['log', '-8', '--pretty=format:%h\t%ad\t%s', '--date=short'],
    { cwd: ROOT, encoding: 'utf8' }
  )
    .trim()
    .split('\n')
    .filter(Boolean)
    .map((l) => {
      const [hash, date, ...s] = l.split('\t');
      return { hash, date, subject: s.join('\t') };
    });
} catch {}

const generated = new Date().toISOString().slice(0, 16).replace('T', ' ') + ' UTC';

const data = {
  generated,
  counts: { todo: todo.length, wip: wip.length, blocked: blocked.length, done: done.length },
  percent: pct,
  todo,
  wip,
  blocked,
  done,
  commits,
};

writeFileSync(p('docs/progress.json'), JSON.stringify(data, null, 2) + '\n');

const bar = (n) => '█'.repeat(Math.round(n / 5)) + '░'.repeat(20 - Math.round(n / 5));
const list = (arr, empty) =>
  arr.length ? arr.map((t) => `- ${t.priority ? `\`${t.priority}\` ` : ''}${t.title}`).join('\n') : `_${empty}_`;

const md = `# PROGRESS — 自動開発の進捗

> \`node tools/progress.mjs\` が \`DEV_QUEUE.md\` から自動生成。手で編集しない。
> 生成: ${generated}

## 消化率

\`\`\`
${bar(pct)}  ${pct}%   (完了 ${done.length} / 対象 ${actionable})
\`\`\`

| 状態 | 件数 |
|---|---:|
| ✅ 完了 | ${done.length} |
| 🔨 作業中 | ${wip.length} |
| 📋 残り | ${todo.length} |
| ⛔ 保留（人間の判断待ち） | ${blocked.length} |

## 🔨 いま作業中

${list(wip, 'なし')}

## 📋 次にやること

${list(todo.slice(0, 5), 'キューが空です')}
${todo.length > 5 ? `\n…ほか ${todo.length - 5} 件\n` : ''}
## ⛔ 保留（自動では進められない）

${list(blocked, 'なし')}

## ✅ 完了

${list(done, 'まだありません')}

## 直近のコミット

${commits.length ? commits.map((c) => `- \`${c.hash}\` ${c.date} ${c.subject}`).join('\n') : '_取得できませんでした_'}
`;

writeFileSync(p('PROGRESS.md'), md);
console.log(`PROGRESS.md updated: ${pct}% (done ${done.length} / actionable ${actionable}, blocked ${blocked.length})`);
