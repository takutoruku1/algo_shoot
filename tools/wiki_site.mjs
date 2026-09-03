#!/usr/bin/env node
// wiki/*.md から 1枚モノの Web サイト docs/wiki.html を生成する。
// 使い方: node tools/wiki_site.mjs
// - md はあくまで原稿。読者に見せるのはこの HTML（Artifact として公開）。
// - mermaid フェンスは <pre class="mermaid"> に変換（Artifact 側がネイティブ描画）。
// - sim フェンス（中身は JSON のスペル定義）は <canvas class="simbox" data-sim="..."> に変換し、
//   埋め込みの弾幕ランタイム（下の SIM スクリプト）がゲームと同じパラメータで弾を飛ばし続ける。
//   数値は src/Boss*.cs の Normal 実効値（弾数 Dn=round(n×0.7)・間隔 Di=×1.35・弾速 ×0.85）を転記する。
import { readFileSync, writeFileSync, existsSync, mkdirSync, statSync } from 'node:fs';
import { resolve, dirname, join, sep } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';

const ROOT = resolve('wiki');

// ── 画像の埋め込み ─────────────────────────────────────────────
// char/ の原画は数百KB×85枚=218MB あるので、ビルド時に ffmpeg で縮小した WebP を
// data URI として埋め込む（build/wiki_work/img/ にキャッシュ。元画像の mtime が変わると作り直し）。
// kind: 'art'=立ち絵・スプライト（幅320px上限） / 'shot'=画面スクショ（幅896px上限）。拡大はしない。
const IMG_CACHE = resolve('build/wiki_work/img');
mkdirSync(IMG_CACHE, { recursive: true });
let imgCount = 0, imgBytes = 0;
function embedImage(absPath, kind) {
  // SVG は縮小せずそのまま埋め込む（ベクターなので軽量・ffmpeg 不要）
  if (absPath.toLowerCase().endsWith('.svg')) {
    const svg = readFileSync(absPath);
    imgCount++; imgBytes += svg.length;
    return `data:image/svg+xml;base64,${svg.toString('base64')}`;
  }
  const st = statSync(absPath);
  const key = createHash('md5').update(absPath + '|' + st.mtimeMs + '|' + kind).digest('hex').slice(0, 16);
  const out = join(IMG_CACHE, key + '.webp');
  if (!existsSync(out)) {
    const maxW = kind === 'shot' ? 896 : 320;
    execFileSync('ffmpeg', ['-y', '-v', 'error', '-i', absPath,
      '-vf', `scale='if(gt(iw\\,${maxW})\\,${maxW}\\,iw)':-2`,
      '-quality', '85', out]);
  }
  const buf = readFileSync(out);
  imgCount++; imgBytes += buf.length;
  return `data:image/webp;base64,${buf.toString('base64')}`;
}
function figureHtml(alt, href, curDir) {
  const abs = resolve(ROOT, curDir || '.', href);
  if (!existsSync(abs)) {
    console.warn('  [warn] 画像が見つからない:', href);
    return `<span class="imgmissing">［画像未収録: ${alt}］</span>`;
  }
  const kind = (href.includes('build/shots') || href.toLowerCase().endsWith('.svg')) ? 'shot' : 'art';
  return `<figure class="fig fig-${kind}"><img src="${embedImage(abs, kind)}" alt="${alt}" loading="lazy"><figcaption>${alt}</figcaption></figure>`;
}

// ── 弾幕シミュレーション（```sim フェンス）───────────────────────
// フェンス内の JSON をそのまま data-sim 属性に埋め、共有ランタイムに駆動させる。
// "src"（数値の根拠）や "title"（キャプション）もそのまま持たせる（src は表示しない）。
let simCount = 0;
function simHtml(jsonText) {
  let spec;
  try { spec = JSON.parse(jsonText); }
  catch (e) { throw new Error(`sim フェンスの JSON が不正: ${e.message}\n--- ${jsonText.slice(0, 160)}`); }
  simCount++;
  const cap = spec.title ? `<figcaption>${escapeHtml(spec.title)}</figcaption>` : '';
  const data = escapeHtml(JSON.stringify(spec));
  return `<figure class="fig fig-shot fig-sim"><div class="simwrap">` +
    `<canvas class="simbox" width="768" height="432" data-sim="${data}"></canvas>` +
    `<button class="simbtn" type="button" aria-label="再生/停止">❚❚</button></div>${cap}</figure>`;
}

// 表示順＝README の目次順。id は wiki/ からの相対（拡張子なし・/ 区切り）。
const ORDER = [
  ['README', 'ホーム'],
  ['01_作品概要', null],
  ['02_世界観', null],
  ['03_ストーリー/00_時系列', null],
  ['03_ストーリー/01_プロローグ', null],
  ['03_ストーリー/02_チュートリアル', null],
  ['03_ストーリー/03_STAGE1_レイ', null],
  ['03_ストーリー/04_STAGE2_あかり', null],
  ['03_ストーリー/05_STAGE3_こはる', null],
  ['03_ストーリー/06_FINAL_ミナ', null],
  ['03_ストーリー/07_エピローグ', null],
  ['03_ストーリー/08_伏線と回収', null],
  ['04_キャラクター/00_相関図', null],
  ['04_キャラクター/01_少年', null],
  ['04_キャラクター/02_ミナ', null],
  ['04_キャラクター/03_レイ', null],
  ['04_キャラクター/04_あかり', null],
  ['04_キャラクター/05_こはる', null],
  ['04_キャラクター/06_その他', null],
  ['05_ゲーム仕様/01_操作', null],
  ['05_ゲーム仕様/02_自機とショット', null],
  ['05_ゲーム仕様/03_ゲージと経済', null],
  ['05_ゲーム仕様/04_ステージ構成', null],
  ['05_ゲーム仕様/05_敵', null],
  ['05_ゲーム仕様/06_ボス戦', null],
  ['05_ゲーム仕様/07_ショップと強化', null],
  ['05_ゲーム仕様/08_難易度', null],
  ['05_ゲーム仕様/09_セーブと周回', null],
  ['05_ゲーム仕様/10_画面一覧', null],
  ['05_ゲーム仕様/11_音楽と演出', null],
  ['06_用語集', null],
  ['07_ギャラリー/01_画面', null],
  ['07_ギャラリー/02_キャラクターアート', null],
  ['07_ギャラリー/03_敵の技', null],
  ['08_仮台本/01_声の劣化', null],
  ['08_仮台本/02_共感ポイント', null],
  ['08_仮台本/03_物語骨子の比較案', null],
  ['08_仮台本/04_冒頭台本_案C', null],
  ['08_仮台本/05_場面表_案C', null],
];

const GROUPS = [
  { label: '', ids: ['README', '01_作品概要', '02_世界観'] },
  { label: 'ストーリー', prefix: '03_ストーリー/' },
  { label: 'キャラクター', prefix: '04_キャラクター/' },
  { label: 'ゲーム仕様', prefix: '05_ゲーム仕様/' },
  { label: '', ids: ['06_用語集'] },
  { label: 'ギャラリー', prefix: '07_ギャラリー/' },
  { label: '仮台本（非正典）', prefix: '08_仮台本/' },
];

const escapeHtml = (s) =>
  s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

// ── インライン変換（HTML エスケープ後に適用）──
function inline(s, curDir) {
  let t = escapeHtml(s);
  // `code`
  t = t.replace(/`([^`]+)`/g, '<code>$1</code>');
  // **strong**
  t = t.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  // [text](href)
  t = t.replace(/\[([^\]]+)\]\(([^()\s]+)\)/g, (_, label, href) => {
    if (/^https?:/.test(href)) return `<a href="${href}" target="_blank" rel="noopener">${label}</a>`;
    // 内部リンク: 相対 .md（またはディレクトリ）→ ハッシュへ
    let target = href.replace(/\.md$/, '');
    // カレントディレクトリ基準で正規化
    const parts = (curDir ? curDir + '/' : '') + target;
    const stack = [];
    for (const p of parts.split('/')) {
      if (p === '..') stack.pop();
      else if (p !== '.' && p !== '') stack.push(p);
    }
    let id = stack.join('/');
    // ディレクトリリンク（03_ストーリー/ 等）→ 章の先頭記事へ
    if (!ORDER.some(([o]) => o === id)) {
      const first = ORDER.find(([o]) => o.startsWith(id + '/'));
      if (first) id = first[0];
    }
    return `<a href="#/${encodeURIComponent(id)}" data-nav="${escapeHtml(id)}">${label}</a>`;
  });
  return t;
}

// ── ブロック変換 ──
function mdToHtml(md, curDir) {
  // HTML コメント（出典行など）はサイトに含めない
  md = md.replace(/<!--[\s\S]*?-->/g, '');
  const lines = md.split(/\r?\n/);
  const out = [];
  let para = [];
  let listStack = 0; // 0=なし 1=ul 2=ul>ul
  let inTable = false;
  let tableRows = [];
  let fence = null; // null | 'mermaid' | 'code'
  let fenceBuf = [];
  let inQuote = false;

  const flushPara = () => {
    if (para.length) {
      out.push(`<p>${para.map((l) => inline(l, curDir)).join('\n')}</p>`);
      para = [];
    }
  };
  const closeLists = (to) => {
    while (listStack > to) {
      out.push('</ul>');
      listStack--;
    }
  };
  const flushTable = () => {
    if (!inTable) return;
    const rows = tableRows.filter((r) => !/^\s*\|[\s:|-]+\|\s*$/.test(r));
    const cells = (r) => r.replace(/^\s*\|/, '').replace(/\|\s*$/, '').split('|').map((c) => inline(c.trim(), curDir));
    let html = '<div class="tablewrap"><table>';
    rows.forEach((r, i) => {
      const tag = i === 0 ? 'th' : 'td';
      html += `<tr>${cells(r).map((c) => `<${tag}>${c}</${tag}>`).join('')}</tr>`;
    });
    html += '</table></div>';
    out.push(html);
    inTable = false;
    tableRows = [];
  };
  const closeQuote = () => {
    if (inQuote) {
      out.push('</blockquote>');
      inQuote = false;
    }
  };

  for (const raw of lines) {
    // フェンス
    const fm = raw.match(/^```\s*(\w*)/);
    if (fm) {
      if (fence === null) {
        flushPara(); closeLists(0); flushTable(); closeQuote();
        fence = fm[1] === 'mermaid' ? 'mermaid' : fm[1] === 'sim' ? 'sim' : 'code';
        fenceBuf = [];
      } else {
        if (fence === 'mermaid') out.push(`<pre class="mermaid">${escapeHtml(fenceBuf.join('\n'))}</pre>`);
        else if (fence === 'sim') out.push(simHtml(fenceBuf.join('\n')));
        else out.push(`<pre class="codeblock"><code>${escapeHtml(fenceBuf.join('\n'))}</code></pre>`);
        fence = null;
      }
      continue;
    }
    if (fence !== null) { fenceBuf.push(raw); continue; }

    const line = raw;

    if (/^\s*$/.test(line)) { flushPara(); closeLists(0); flushTable(); closeQuote(); continue; }

    // 画像行（行頭が ![ ）: 行内の全画像を figure にして横並びの1行にまとめる
    if (/^\s*!\[/.test(line)) {
      flushPara(); closeLists(0); flushTable(); closeQuote();
      const figs = [];
      for (const m of line.matchAll(/!\[([^\]]*)\]\(([^()\s]+)\)/g)) figs.push(figureHtml(escapeHtml(m[1]), m[2], curDir));
      if (figs.length) out.push(`<div class="figrow">${figs.join('')}</div>`);
      continue;
    }

    // 表
    if (/^\s*\|/.test(line)) {
      flushPara(); closeLists(0); closeQuote();
      inTable = true; tableRows.push(line); continue;
    }
    flushTable();

    // 引用（範囲宣言など）
    const qm = line.match(/^>\s?(.*)$/);
    if (qm) {
      flushPara(); closeLists(0);
      if (!inQuote) { out.push('<blockquote>'); inQuote = true; }
      out.push(`<p>${inline(qm[1], curDir)}</p>`);
      continue;
    }
    closeQuote();

    // 見出し
    const hm = line.match(/^(#{1,3})\s+(.*)$/);
    if (hm) {
      flushPara(); closeLists(0);
      const level = hm[1].length;
      out.push(`<h${level}>${inline(hm[2], curDir)}</h${level}>`);
      continue;
    }

    // 水平線
    if (/^-{3,}\s*$/.test(line)) { flushPara(); closeLists(0); out.push('<hr>'); continue; }

    // リスト（- ／ 数字. 、2スペースで1段ネスト）
    const lm = line.match(/^(\s*)(?:-|\d+\.)\s+(.*)$/);
    if (lm) {
      flushPara();
      const depth = lm[1].length >= 2 ? 2 : 1;
      while (listStack < depth) { out.push('<ul>'); listStack++; }
      closeLists(depth);
      out.push(`<li>${inline(lm[2], curDir)}</li>`);
      continue;
    }
    closeLists(0);

    para.push(line);
  }
  flushPara(); closeLists(0); flushTable(); closeQuote();
  return out.join('\n');
}

// ── 全記事を読み込み ──
const articles = ORDER.map(([id, alias]) => {
  const path = join(ROOT, id.split('/').join(sep) + '.md');
  const md = readFileSync(path, 'utf8');
  const h1 = (md.match(/^#\s+(.*)$/m) || [null, id])[1];
  const curDir = id.includes('/') ? id.slice(0, id.lastIndexOf('/')) : '';
  const html = mdToHtml(md.replace(/^#\s+.*$/m, ''), curDir); // h1 はヘッダで別描画
  // 検索用プレーンテキスト
  const text = html.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ');
  const short = alias || h1.replace(/^ALGO: Refrain of Light — /, '');
  return { id, title: h1, short, html, text };
});

const navGroups = GROUPS.map((g) => ({
  label: g.label,
  items: (g.ids || ORDER.filter(([o]) => o.startsWith(g.prefix)).map(([o]) => o)).map((id) => {
    const a = articles.find((x) => x.id === id);
    return { id, short: a.short };
  }),
}));

const payload = JSON.stringify({
  order: articles.map((a) => a.id),
  titles: Object.fromEntries(articles.map((a) => [a.id, a.title])),
  shorts: Object.fromEntries(articles.map((a) => [a.id, a.short])),
  texts: Object.fromEntries(articles.map((a) => [a.id, a.text])),
}).replace(/</g, '\\u003c');

const sections = articles
  .map((a) => `<article id="a-${escapeHtml(a.id)}" hidden><h1 class="pagetitle">${escapeHtml(a.title)}</h1>\n${a.html}</article>`)
  .join('\n');

const nav = navGroups
  .map((g) => {
    const items = g.items
      .map((it) => `<a class="navlink" data-id="${escapeHtml(it.id)}" href="#/${encodeURIComponent(it.id)}">${escapeHtml(it.short)}</a>`)
      .join('');
    return g.label ? `<div class="navgroup"><div class="navlabel">${g.label}</div>${items}</div>` : `<div class="navgroup">${items}</div>`;
  })
  .join('');

const html = `<title>ALGO: Refrain of Light Wiki</title>
<style>
  :root {
    --ground: #F5F6F8;
    --surface: #FFFFFF;
    --surface-2: #EDF0F4;
    --line: #DFE4EA;
    --ink: #1A2230;
    --ink-dim: #59647A;
    --ink-faint: #8A94A8;
    --accent: #0E9490;
    --accent-soft: #E0F2F1;
    --quote: #F0F4F3;
    --mono: ui-monospace, "Cascadia Mono", Consolas, monospace;
    --sans: ui-sans-serif, system-ui, "Segoe UI", "Hiragino Sans", "Noto Sans JP", Meiryo, sans-serif;
    color-scheme: light dark;
  }
  @media (prefers-color-scheme: dark) {
    :root:not([data-theme="light"]) {
      --ground: #0C111A; --surface: #121926; --surface-2: #1A2333; --line: #263043;
      --ink: #E4EAF4; --ink-dim: #9AA5B8; --ink-faint: #66718A;
      --accent: #5FD3C4; --accent-soft: #143433; --quote: #151E2C;
    }
  }
  :root[data-theme="dark"] {
    --ground: #0C111A; --surface: #121926; --surface-2: #1A2333; --line: #263043;
    --ink: #E4EAF4; --ink-dim: #9AA5B8; --ink-faint: #66718A;
    --accent: #5FD3C4; --accent-soft: #143433; --quote: #151E2C;
  }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--ground); color: var(--ink); font-family: var(--sans); line-height: 1.9; }

  /* ── トップバー ── */
  .topbar {
    position: sticky; top: 0; z-index: 20;
    display: flex; align-items: center; gap: 12px;
    padding: 10px 16px; background: var(--surface); border-bottom: 1px solid var(--line);
  }
  .menu-btn { display: none; border: 1px solid var(--line); background: var(--surface); color: var(--ink);
    border-radius: 6px; padding: 6px 10px; font-size: 16px; cursor: pointer; }
  .brand { font-weight: 700; letter-spacing: 0.02em; text-decoration: none; color: var(--ink); white-space: nowrap; }
  .brand b { color: var(--accent); font-weight: 700; }
  .search { margin-left: auto; position: relative; }
  .search input {
    width: min(280px, 46vw); padding: 7px 12px; border: 1px solid var(--line); border-radius: 8px;
    background: var(--ground); color: var(--ink); font-family: inherit; font-size: 13px;
  }
  .search input:focus { outline: 2px solid var(--accent); outline-offset: 1px; }

  /* ── レイアウト ── */
  .frame { display: flex; min-height: calc(100vh - 53px); }
  nav.side {
    width: 248px; flex: none; border-right: 1px solid var(--line); background: var(--surface);
    padding: 18px 0 40px; position: sticky; top: 53px; height: calc(100vh - 53px); overflow-y: auto;
  }
  .navgroup { padding: 4px 0 10px; }
  .navlabel {
    font-size: 11px; font-weight: 700; letter-spacing: 0.14em; color: var(--ink-faint);
    padding: 10px 20px 4px;
  }
  .navlink {
    display: block; padding: 5px 20px 5px 24px; font-size: 13.5px; color: var(--ink-dim);
    text-decoration: none; border-left: 2px solid transparent;
  }
  .navlink:hover { color: var(--ink); background: var(--surface-2); }
  .navlink.on { color: var(--accent); border-left-color: var(--accent); background: var(--accent-soft); font-weight: 600; }
  .navlink.dimmed { display: none; }
  .nohit { padding: 8px 20px; font-size: 12px; color: var(--ink-faint); display: none; }

  main { flex: 1; min-width: 0; }
  article { max-width: 780px; margin: 0 auto; padding: 36px 28px 90px; }
  .pagetitle { font-size: 27px; line-height: 1.4; letter-spacing: 0.01em; margin: 0 0 6px;
    padding-bottom: 14px; border-bottom: 2px solid var(--line); }
  h2 { font-size: 20px; margin: 44px 0 10px; padding-left: 12px; border-left: 4px solid var(--accent); line-height: 1.5; }
  h3 { font-size: 16px; margin: 30px 0 6px; color: var(--ink); }
  p { margin: 10px 0; }
  a { color: var(--accent); text-decoration: none; }
  article a:hover { text-decoration: underline; }
  blockquote {
    margin: 14px 0; padding: 10px 16px; background: var(--quote);
    border-left: 3px solid var(--accent); border-radius: 0 6px 6px 0; color: var(--ink-dim); font-size: 14px;
  }
  blockquote p { margin: 4px 0; }
  ul { margin: 10px 0; padding-left: 24px; }
  li { margin: 4px 0; }
  hr { border: 0; border-top: 1px solid var(--line); margin: 28px 0; }
  code { font-family: var(--mono); font-size: 0.88em; background: var(--surface-2); padding: 1px 6px; border-radius: 4px; }
  .codeblock { background: var(--surface-2); border: 1px solid var(--line); border-radius: 8px;
    padding: 12px 14px; overflow-x: auto; font-size: 13px; line-height: 1.7; }
  pre.mermaid { background: var(--surface); border: 1px solid var(--line); border-radius: 8px;
    padding: 12px; overflow-x: auto; }

  /* ── ギャラリー ── */
  .figrow { display: flex; flex-wrap: wrap; gap: 14px; margin: 16px 0; align-items: flex-start; }
  figure.fig { margin: 0; background: var(--surface); border: 1px solid var(--line); border-radius: 8px; padding: 8px; }
  .fig-art { width: 168px; }
  .fig-art img { width: 100%; height: auto; display: block; border-radius: 4px; background: #1A2029; }
  .fig-shot { width: 100%; max-width: 640px; }
  .fig-shot img { width: 100%; height: auto; display: block; border-radius: 4px; }
  figcaption { font-size: 12px; color: var(--ink-dim); line-height: 1.5; padding-top: 6px; }

  /* ── 弾幕シミュレーション ── */
  .fig-sim .simwrap { position: relative; }
  canvas.simbox { width: 100%; height: auto; display: block; border-radius: 4px; background: #0b101e; cursor: pointer; }
  .simbtn {
    position: absolute; right: 8px; bottom: 8px; width: 30px; height: 30px; border-radius: 15px;
    border: 1px solid rgba(255,255,255,0.25); background: rgba(10,16,30,0.55); color: #dfe6f2;
    font-size: 11px; line-height: 1; cursor: pointer; padding: 0;
  }
  .simbtn:hover { background: rgba(30,44,70,0.85); }
  .imgmissing { color: var(--ink-faint); font-size: 12px; }
  @media (max-width: 860px) { .fig-art { width: calc(50% - 7px); } }

  .tablewrap { overflow-x: auto; margin: 14px 0; border: 1px solid var(--line); border-radius: 8px; }
  table { border-collapse: collapse; width: 100%; font-size: 13.5px; line-height: 1.7; background: var(--surface); }
  th, td { padding: 8px 12px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; }
  th { background: var(--surface-2); font-size: 12.5px; letter-spacing: 0.04em; white-space: nowrap; }
  tr:last-child td { border-bottom: 0; }
  td { font-variant-numeric: tabular-nums; }

  /* ── 記事末尾の前後ナビ ── */
  .pnav { max-width: 780px; margin: 0 auto; padding: 0 28px 60px; display: flex; gap: 12px; }
  .pnav a {
    flex: 1; border: 1px solid var(--line); border-radius: 10px; padding: 12px 16px;
    background: var(--surface); text-decoration: none; color: var(--ink); min-width: 0;
  }
  .pnav a:hover { border-color: var(--accent); }
  .pnav .dir { font-size: 11px; color: var(--ink-faint); letter-spacing: 0.1em; }
  .pnav .nm { font-size: 14px; font-weight: 600; color: var(--accent);
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .pnav a.next { text-align: right; }

  /* ── モバイル ── */
  .scrim { display: none; }
  @media (max-width: 860px) {
    .menu-btn { display: block; }
    nav.side {
      position: fixed; left: 0; top: 53px; bottom: 0; height: auto; z-index: 30;
      transform: translateX(-100%); transition: transform 0.2s ease; box-shadow: 4px 0 24px rgba(0,0,0,0.25);
    }
    body.nav-open nav.side { transform: translateX(0); }
    body.nav-open .scrim { display: block; position: fixed; inset: 53px 0 0 0; z-index: 25; background: rgba(8,12,20,0.4); }
    article { padding: 24px 18px 70px; }
    .pnav { padding: 0 18px 50px; flex-direction: column; }
  }
  @media (prefers-reduced-motion: reduce) { nav.side { transition: none; } }
</style>

<header class="topbar">
  <button class="menu-btn" id="menuBtn" aria-label="目次を開く">☰</button>
  <a class="brand" href="#/README"><b>ALGO</b>: Refrain of Light — Wiki</a>
  <div class="search"><input id="q" type="search" placeholder="記事をさがす…" autocomplete="off"></div>
</header>
<div class="frame">
  <nav class="side" id="side">
    ${nav}
    <div class="nohit" id="nohit">見つかりません</div>
  </nav>
  <div class="scrim" id="scrim"></div>
  <main>
    ${sections}
    <div class="pnav" id="pnav"></div>
  </main>
</div>

<script>
var DATA = ${payload};
(function () {
  var order = DATA.order;
  var current = null;

  function idFromHash() {
    var h = location.hash;
    if (h.length > 2 && h.indexOf('#/') === 0) {
      try { var id = decodeURIComponent(h.slice(2)); if (order.indexOf(id) >= 0) return id; } catch (e) {}
    }
    return 'README';
  }

  function show(id) {
    if (current === id) return;
    current = id;
    var arts = document.querySelectorAll('article');
    for (var i = 0; i < arts.length; i++) arts[i].hidden = true;
    var el = document.getElementById('a-' + id);
    if (el) el.hidden = false;
    var links = document.querySelectorAll('.navlink');
    for (var j = 0; j < links.length; j++) {
      links[j].classList.toggle('on', links[j].getAttribute('data-id') === id);
    }
    document.title = (id === 'README' ? 'ALGO: Refrain of Light Wiki' : DATA.titles[id] + ' — ALGO Wiki');
    renderPnav(id);
    document.body.classList.remove('nav-open');
    window.scrollTo(0, 0);
  }

  function renderPnav(id) {
    var i = order.indexOf(id);
    var box = document.getElementById('pnav');
    var htmlStr = '';
    if (i > 0) {
      var p = order[i - 1];
      htmlStr += '<a href="#/' + encodeURIComponent(p) + '"><div class="dir">← まえ</div><div class="nm">' + DATA.shorts[p] + '</div></a>';
    }
    if (i >= 0 && i < order.length - 1) {
      var n = order[i + 1];
      htmlStr += '<a class="next" href="#/' + encodeURIComponent(n) + '"><div class="dir">つぎ →</div><div class="nm">' + DATA.shorts[n] + '</div></a>';
    }
    box.innerHTML = htmlStr;
  }

  window.addEventListener('hashchange', function () { show(idFromHash()); });
  show(idFromHash());

  // 検索: タイトル・本文の部分一致でサイドバーを絞り込む
  var q = document.getElementById('q');
  q.addEventListener('input', function () {
    var v = q.value.trim().toLowerCase();
    var links = document.querySelectorAll('.navlink');
    var hits = 0;
    for (var i = 0; i < links.length; i++) {
      var id = links[i].getAttribute('data-id');
      var hay = (DATA.shorts[id] + ' ' + DATA.titles[id] + ' ' + DATA.texts[id]).toLowerCase();
      var ok = v === '' || hay.indexOf(v) >= 0;
      links[i].classList.toggle('dimmed', !ok);
      if (ok) hits++;
    }
    document.getElementById('nohit').style.display = hits === 0 ? 'block' : 'none';
    if (v !== '') document.body.classList.add('nav-open');
  });

  document.getElementById('menuBtn').addEventListener('click', function () {
    document.body.classList.toggle('nav-open');
  });
  document.getElementById('scrim').addEventListener('click', function () {
    document.body.classList.remove('nav-open');
  });
})();
</script>

<script>
// ── 弾幕シミュレーション共有ランタイム ─────────────────────────────
// 各 <canvas class="simbox"> の data-sim(JSON) を読み、ゲームの Normal 実効値どおりに
// 「弾だけ」を飛ばし続ける小さなエンジン。384×216 の盤面を 2 倍で描く。
// ・ループ周期（spec.loop 秒）ごとに巻き戻す。乱数は種つき＝毎周同じ弾道。
// ・画面外の sim は IntersectionObserver で止める（可視のものだけ rAF 駆動）。
// ・URL に ?simt=秒 を付けると全 sim をその時刻まで進めて 1 フレーム静止描画（スクショ検証用）。
(function () {
  var FW = 384, FH = 216, DT = 1 / 60, TAU = Math.PI * 2;
  function rad(d) { return d * Math.PI / 180; }
  function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
  function dist(x1, y1, x2, y2) { var dx = x1 - x2, dy = y1 - y2; return Math.sqrt(dx * dx + dy * dy); }
  // 決定的乱数（mulberry32）
  function mulberry32(a) {
    return function () {
      var t = (a += 0x6D2B79F5) | 0;
      t = Math.imul(t ^ (t >>> 15), t | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  // ── 弾の描画（Bullet._Draw の簡略移植：グロー＋形状＋当たり芯ドット）──
  function drawBullet(c, b) {
    var ang = Math.atan2(b.vy, b.vx);
    c.save(); c.translate(b.x, b.y);
    c.globalAlpha = 0.14; c.fillStyle = b.color;
    c.beginPath(); c.arc(0, 0, b.r * 2.0, 0, TAU); c.fill();
    c.globalAlpha = 1;
    if (b.halo) { // 祈り弾＝暖白のハロ（受け止められる弾の記号）
      c.globalAlpha = 0.4; c.strokeStyle = '#ffe9c8'; c.lineWidth = 1.1;
      c.beginPath(); c.arc(0, 0, b.r + 2.2, 0, TAU); c.stroke(); c.globalAlpha = 1;
    }
    var s;
    switch (b.shape) {
      case 'diamond':
        s = b.r * 1.15;
        c.fillStyle = b.color;
        c.beginPath(); c.moveTo(0, -s); c.lineTo(s, 0); c.lineTo(0, s); c.lineTo(-s, 0); c.closePath(); c.fill();
        c.fillStyle = 'rgba(255,255,255,0.85)';
        c.beginPath(); c.moveTo(0, -s / 2); c.lineTo(s / 2, 0); c.lineTo(0, s / 2); c.lineTo(-s / 2, 0); c.closePath(); c.fill();
        break;
      case 'star':
        c.fillStyle = b.color; c.beginPath();
        for (var i = 0; i < 10; i++) {
          var rr = (i % 2 === 0 ? 1.35 : 0.55) * b.r, aa = -Math.PI / 2 + Math.PI * i / 5;
          if (i === 0) c.moveTo(Math.cos(aa) * rr, Math.sin(aa) * rr);
          else c.lineTo(Math.cos(aa) * rr, Math.sin(aa) * rr);
        }
        c.closePath(); c.fill();
        break;
      case 'ring':
        c.strokeStyle = b.color; c.lineWidth = Math.max(1.4, b.r * 0.42);
        c.beginPath(); c.arc(0, 0, b.r * 0.9, 0, TAU); c.stroke();
        break;
      case 'needle': {
        c.rotate(ang);
        var len = b.r * 2.8, w = b.r * 0.78;
        c.fillStyle = b.color;
        c.beginPath();
        c.arc(-len / 2, 0, w / 2, Math.PI / 2, -Math.PI / 2);
        c.arc(len / 2, 0, w / 2, -Math.PI / 2, Math.PI / 2);
        c.closePath(); c.fill();
        c.fillStyle = 'rgba(255,255,255,0.55)';
        c.fillRect(-len / 2, -w * 0.18, len, w * 0.36);
        break;
      }
      case 'rice':
        c.rotate(ang); c.scale(1.15, 0.5);
        c.fillStyle = b.color; c.beginPath(); c.arc(0, 0, b.r, 0, TAU); c.fill();
        c.fillStyle = 'rgba(255,255,255,0.7)';
        c.beginPath(); c.arc(-b.r * 0.25, -b.r * 0.25, b.r * 0.4, 0, TAU); c.fill();
        break;
      default: // orb: 白リング＋暗芯＋芯色
        c.fillStyle = 'rgba(255,255,255,0.95)'; c.beginPath(); c.arc(0, 0, b.r, 0, TAU); c.fill();
        c.fillStyle = '#160a12'; c.beginPath(); c.arc(0, 0, b.r * 0.7, 0, TAU); c.fill();
        c.fillStyle = b.color; c.beginPath(); c.arc(0, 0, b.r * 0.4, 0, TAU); c.fill();
    }
    c.restore();
    c.fillStyle = 'rgba(255,255,255,0.9)';
    c.beginPath(); c.arc(b.x, b.y, Math.min(1.5, b.r * 0.42), 0, TAU); c.fill();
  }

  // ── 全画面AOEの安置選び（AreaSpellCaster の移植）──
  function pickSafe(g, margin) {
    var p = g.player(), bs = g.boss();
    var best = [FW / 2, FH / 2], bestD = 1e9;
    for (var i = 0; i < 12; i++) {
      var x = margin + g.rng() * (FW - margin * 2), y = margin + g.rng() * (FH - margin * 2);
      if (dist(x, y, bs[0], bs[1]) < 60) continue; // ボスの居場所＝安置にしない
      var d = dist(x, y, p[0], p[1]);
      if (d >= 70 && d < bestD) { bestD = d; best = [x, y]; }
    }
    return best;
  }
  function chainHop(g, e, cast) {
    var margin = 30 + 14, prev = e.prevSafe, bs = g.boss(), safe = prev;
    for (var i = 0; i < 24; i++) {
      var a = g.rng() * TAU, dx = Math.cos(a), dy = Math.sin(a);
      if (e.prevDir && dx * e.prevDir[0] + dy * e.prevDir[1] >= 0.5) continue; // ジグザグ保証
      var L = cast.hopMin + g.rng() * (cast.hopMax - cast.hopMin);
      var x = clamp(prev[0] + dx * L, margin, FW - margin);
      var y = clamp(prev[1] + dy * L, margin, FH - margin);
      if (dist(x, y, prev[0], prev[1]) < cast.hopMin * 0.9) continue;
      if (dist(x, y, bs[0], bs[1]) < 60) continue;
      safe = [x, y]; break;
    }
    if (safe === prev) { // 保険：中央方向へ hopMin
      var nx = FW / 2 - prev[0], ny = FH / 2 - prev[1], n = Math.sqrt(nx * nx + ny * ny) || 1;
      safe = [clamp(prev[0] + nx / n * cast.hopMin, margin, FW - margin),
              clamp(prev[1] + ny / n * cast.hopMin, margin, FH - margin)];
    }
    var vx = safe[0] - prev[0], vy = safe[1] - prev[1], vl = Math.sqrt(vx * vx + vy * vy) || 1;
    e.prevDir = [vx / vl, vy / vl];
    return safe;
  }
  function fillOutside(c, x, y, r, style, alpha) {
    c.globalAlpha = alpha; c.fillStyle = style;
    c.beginPath(); c.rect(0, 0, FW, FH); c.arc(x, y, r, 0, TAU, true); c.fill();
    c.globalAlpha = 1;
  }

  // ── 発射部品 ──
  function makeEmitter(g, s) {
    var e = { s: s, acc: 0, off: 0, age: 0 };
    var volley;
    switch (s.kind) {
      case 'ring': // 発射ごとに step° 回るリング（speeds が複数なら各リング前に off が進む＝ミナの二重リング）
        volley = function () {
          var bs = g.boss();
          for (var j = 0; j < s.speeds.length; j++) {
            e.off += rad(s.step);
            for (var i = 0; i < s.n; i++) {
              var a = e.off + TAU * i / s.n;
              g.spawn(bs[0], bs[1], a, s.speeds[j], s, j === 0 ? s.r : (s.r2 || s.r));
            }
          }
        };
        break;
      case 'fan': // 下向きの扇（中心角 center°・全幅 spread°）
        volley = function () {
          var bs = g.boss();
          for (var i = 0; i < s.n; i++) {
            var a = rad(s.center) + (i / (s.n - 1) - 0.5) * rad(s.spread);
            g.spawn(bs[0], bs[1], a, s.speed, s, s.r);
          }
        };
        break;
      case 'nway': // 自機狙いの扇（±stepDeg° 刻み）
        volley = function () {
          var bs = g.boss(), p = g.player();
          var base = Math.atan2(p[1] - bs[1], p[0] - bs[0]), w = (s.ways - 1) / 2;
          for (var i = -w; i <= w; i++) g.spawn(bs[0], bs[1], base + i * rad(s.stepDeg), s.speed, s, s.r);
        };
        break;
      case 'spiral': // 発射ごとに step° 回る多腕スパイラル
        volley = function () {
          var bs = g.boss();
          e.off += rad(s.step);
          for (var arm = 0; arm < s.arms; arm++) g.spawn(bs[0], bs[1], e.off + TAU * arm / s.arms, s.speed, s, s.r);
        };
        break;
      case 'flower': // 二重速度の花型（発射ごとに半歩ずれる）
        volley = function () {
          var bs = g.boss();
          e.off += rad(180 / s.petals);
          for (var i = 0; i < s.petals; i++) {
            var a = e.off + TAU * i / s.petals;
            g.spawn(bs[0], bs[1], a, s.slow, s, s.rSlow);
            g.spawn(bs[0], bs[1], a, s.fast, s, s.rFast);
          }
        };
        break;
      case 'aoe': { // 全画面AOE（単発／安置リレー／絶域）
        e.state = 'delay'; e.timer = 0.7; e.ci = 0; e.hop = 0; e.prevDir = null;
        e.tick = function (g2, dt) {
          if (e.state === 'done') return;
          e.timer -= dt;
          if (e.timer > 0) return;
          var cast = s.casts[e.ci];
          if (e.state === 'delay' || e.state === 'rest') { // 予兆開始：安置を決める
            e.safe = e.hop === 0 ? pickSafe(g2, cast.safeR + 14) : chainHop(g2, e, cast);
            e.prevSafe = e.safe;
            e.warnDur = cast.warn; e.curR = cast.safeR;
            e.state = 'warn'; e.timer = cast.warn;
          } else if (e.state === 'warn') { e.state = 'impact'; e.timer = 0.2; }
          else if (e.state === 'impact') {
            e.hop++;
            if (e.hop < (cast.hops || 1)) { e.state = 'rest'; e.timer = 0.35; }
            else {
              e.ci++; e.hop = 0; e.prevDir = null;
              if (e.ci >= s.casts.length) e.state = 'done';
              else { e.state = 'delay'; e.timer = (cast.gap || 1.0) + 0.7; }
            }
          }
        };
        e.draw = function (g2, c) {
          if (e.state === 'warn') {
            var blink = 0.10 + 0.10 * Math.abs(Math.sin(g2.t * 9));
            fillOutside(c, e.safe[0], e.safe[1], e.curR, s.color, blink);
            var k = 1 - e.timer / e.warnDur; // 白フレーム収束
            c.globalAlpha = 0.35 * (1 - k); c.strokeStyle = '#ffffff'; c.lineWidth = 1;
            var inset = (1 - k) * 26;
            c.strokeRect(inset, inset, FW - inset * 2, FH - inset * 2);
            c.globalAlpha = 1;
            c.strokeStyle = '#6fe8a0'; c.lineWidth = 1.6; // 安置＝緑リング
            c.beginPath(); c.arc(e.safe[0], e.safe[1], e.curR, 0, TAU); c.stroke();
            c.globalAlpha = 0.10; c.fillStyle = '#6fe8a0';
            c.beginPath(); c.arc(e.safe[0], e.safe[1], e.curR, 0, TAU); c.fill();
            c.globalAlpha = 1;
          } else if (e.state === 'impact') {
            fillOutside(c, e.safe[0], e.safe[1], e.curR, s.hot || s.color, 0.55);
            c.strokeStyle = '#6fe8a0'; c.lineWidth = 1.6;
            c.beginPath(); c.arc(e.safe[0], e.safe[1], e.curR, 0, TAU); c.stroke();
          }
        };
        return e;
      }
      case 'gotoku': { // 五徳の十字火：自機の現在地に十字 → 0.8s後に同心の X
        e.tick = function (g2, dt) {
          e.age += dt;
          if (!e.c && e.age >= s.firstDelay) e.c = g2.player().slice();
        };
        function beam(c, cx, cy, deg, len, half, style, alpha, outline) {
          c.save(); c.translate(cx, cy); c.rotate(rad(deg));
          c.globalAlpha = alpha; c.fillStyle = style;
          c.fillRect(-len / 2, -half, len, half * 2);
          if (outline) { c.globalAlpha = Math.min(1, alpha * 3); c.strokeStyle = style; c.lineWidth = 0.8; c.strokeRect(-len / 2, -half, len, half * 2); }
          c.restore(); c.globalAlpha = 1;
        }
        e.draw = function (g2, c) {
          if (!e.c) return;
          var blink = 0.12 + 0.10 * Math.abs(Math.sin(g2.t * 10));
          var t1 = e.age - s.firstDelay;                  // 第一十字（軸の帯）
          if (t1 >= 0 && t1 < s.warn) {
            beam(c, FW / 2, e.c[1], 0, FW, s.halfH, s.color, blink, true);
            beam(c, e.c[0], FH / 2, 90, FH, s.halfH, s.color, blink, true);
          } else if (t1 >= s.warn && t1 < s.warn + 0.2) {
            beam(c, FW / 2, e.c[1], 0, FW, s.halfH, s.hot, 0.75);
            beam(c, e.c[0], FH / 2, 90, FH, s.halfH, s.hot, 0.75);
          }
          var t2 = e.age - s.firstDelay - s.secondDelay;  // 第二十字（±45° の X）
          if (t2 >= 0 && t2 < s.warn) {
            beam(c, e.c[0], e.c[1], 45, s.diagLen, s.halfX, s.xColor, blink, true);
            beam(c, e.c[0], e.c[1], -45, s.diagLen, s.halfX, s.xColor, blink, true);
          } else if (t2 >= s.warn && t2 < s.warn + 0.2) {
            beam(c, e.c[0], e.c[1], 45, s.diagLen, s.halfX, s.xHot, 0.75);
            beam(c, e.c[0], e.c[1], -45, s.diagLen, s.halfX, s.xHot, 0.75);
          }
        };
        return e;
      }
      case 'meal': { // お残し禁止：配膳 → 時間切れで残りが自機狙いニードルに変わる
        e.mealList = []; e.served = false; e.convAcc = 0;
        e.tick = function (g2, dt) {
          e.age += dt;
          if (!e.served && e.age >= s.serve) {
            e.served = true;
            for (var row = 0; row < s.rows; row++)
              for (var col = 0; col < s.cols; col++) {
                var b = { x: s.x0 + col * s.sx, y: s.y0 + row * s.sy, vx: 0, vy: s.fall,
                          r: s.rMeal, shape: 'orb', color: s.mealColor, halo: true };
                g2.bullets.push(b); e.mealList.push(b);
              }
          }
          if (e.served && e.age >= s.serve + s.window && e.mealList.length) {
            e.convAcc += dt;
            while (e.convAcc >= s.convStep && e.mealList.length) {
              e.convAcc -= s.convStep;
              var m = e.mealList.shift();
              var idx = g2.bullets.indexOf(m);
              if (idx >= 0) g2.bullets.splice(idx, 1);
              var p = g2.player(), a = Math.atan2(p[1] - m.y, p[0] - m.x);
              g2.bullets.push({ x: m.x, y: m.y, vx: Math.cos(a) * s.needleSpeed, vy: Math.sin(a) * s.needleSpeed,
                                r: s.rNeedle, shape: 'needle', color: s.needleColor });
            }
          }
        };
        return e;
      }
      case 'corridor': { // 雨の帰り道：壁カラムが左へ流れ、通路中央線が蛇行する
        var stride = 24, SS = 4;
        e.dStraight = 100 + s.scroll * (s.preview + 3);
        e.blend = s.scroll * 1.0;
        var n = Math.ceil((FW + stride + s.scroll * (s.preview + s.run) + 64) / SS) + 2;
        e.center = new Array(n);
        var maxSlope = s.maxVy / s.scroll, yM = s.gap * 0.8 + 10;
        var y = FH / 2, th = 0, wl = 90 + g.rng() * 80;
        for (var i2 = 0; i2 < n; i2++) {
          e.center[i2] = y;
          if (i2 * SS < e.dStraight) continue; // 学習区間＝直進
          y += maxSlope * Math.sin(th) * SS;
          y = clamp(y, yM, FH - yM);
          th += TAU / wl * SS;
          if (th >= TAU) { th -= TAU; wl = 90 + g.rng() * 80; }
        }
        e.centerAt = function (d) {
          var fi = clamp(d / SS, 0, e.center.length - 1.001), ii = Math.floor(fi);
          return e.center[ii] + (e.center[ii + 1] - e.center[ii]) * (fi - ii);
        };
        e.gapAt = function (d) {
          if (d < e.dStraight) return s.gap * 1.5;
          if (d < e.dStraight + e.blend) return s.gap * 1.5 + (s.gap - s.gap * 1.5) * ((d - e.dStraight) / e.blend);
          return s.gap;
        };
        e.scrollD = 0; e.active = true;
        e.guideY = function (x) { return e.centerAt(x + e.scrollD); };
        g.corr = e;
        e.tick = function (g2, dt) {
          e.age += dt;
          e.scrollD = Math.min(e.age, s.preview + s.run) * s.scroll;
          e.active = e.age < s.preview + s.run;
        };
        e.draw = function (g2, c) {
          var alpha = e.age < s.preview ? 0.22
                    : e.age < s.preview + s.run ? 0.5
                    : Math.max(0, 0.5 * (1 - (e.age - s.preview - s.run) / 0.5));
          if (alpha <= 0) return;
          var k0 = Math.floor(e.scrollD / stride) - 1, kn = k0 + Math.ceil(FW / stride) + 3;
          for (var k = k0; k <= kn; k++) {
            var d = k * stride, x = d - e.scrollD;
            if (x < -8 || x > FW + 8) continue;
            var cy = e.centerAt(d), gh = e.gapAt(d) / 2;
            c.globalAlpha = alpha; c.fillStyle = s.color;
            c.fillRect(x - 6, 0, 12, Math.max(0, cy - gh));
            c.fillRect(x - 6, cy + gh, 12, Math.max(0, FH - cy - gh));
            c.globalAlpha = Math.min(1, alpha * 1.8); c.fillStyle = s.hot; // 通路縁の危険ライン
            c.fillRect(x - 6, cy - gh - 1.2, 12, 1.2);
            c.fillRect(x - 6, cy + gh, 12, 1.2);
          }
          c.globalAlpha = 1;
        };
        return e;
      }
    }
    // 周期発射系（ring/fan/nway/spiral/flower）の共通タイマー
    e.tick = function (g2, dt) {
      e.acc += dt;
      while (e.acc >= s.interval) { e.acc -= s.interval; volley(); }
    };
    return e;
  }

  // ── エンジン ──
  function Engine(canvas) {
    this.cv = canvas;
    this.cx = canvas.getContext('2d');
    this.spec = JSON.parse(canvas.getAttribute('data-sim'));
    this.loop = this.spec.loop || 10;
    this.visible = false; this.userPaused = false; this.accReal = 0;
    this.reset();
  }
  Engine.prototype.reset = function () {
    this.t = 0;
    this.rng = mulberry32((this.spec.seed || 7) >>> 0);
    this.bullets = [];
    this.emitters = [];
    this.phaseIdx = -1;
    this.corr = null;
  };
  Engine.prototype.boss = function () { return this.spec.boss || [200, 70]; };
  Engine.prototype.player = function () {
    if (this.corr && this.corr.active) return [100, this.corr.guideY(100)]; // 通路中＝中央線を走る想定
    var p = this.spec.player || [88, 148];
    return [p[0] + 42 * Math.sin(TAU * this.t / 9), p[1]]; // ゆっくり左右に動く想定自機
  };
  Engine.prototype.spawn = function (x, y, a, spd, s, r) {
    if (this.bullets.length > 900) return; // 保険
    this.bullets.push({ x: x, y: y, vx: Math.cos(a) * spd, vy: Math.sin(a) * spd,
      r: r || s.r || 3, shape: s.shape || 'orb', color: s.color, halo: !!s.halo });
  };
  Engine.prototype.step = function () {
    var ph = this.spec.phases, idx = 0;
    for (var i = 0; i < ph.length; i++) if (this.t >= ph[i].t - 1e-9) idx = i;
    if (idx !== this.phaseIdx) {
      this.phaseIdx = idx;
      var g = this;
      this.emitters = (ph[idx].emit || []).map(function (s) { return makeEmitter(g, s); });
    }
    for (var j = 0; j < this.emitters.length; j++) this.emitters[j].tick(this, DT);
    var out = [];
    for (var k = 0; k < this.bullets.length; k++) {
      var b = this.bullets[k];
      b.x += b.vx * DT; b.y += b.vy * DT;
      if (b.x > -26 && b.x < FW + 26 && b.y > -26 && b.y < FH + 26) out.push(b);
    }
    this.bullets = out;
    this.t += DT;
    if (this.t >= this.loop) this.reset();
  };
  Engine.prototype.draw = function () {
    var c = this.cx;
    c.setTransform(2, 0, 0, 2, 0, 0);
    c.fillStyle = '#0b101e'; c.fillRect(0, 0, FW, FH); // 夜空の濃紺一色
    var bs = this.boss(); // ボス位置＝淡いマークだけ
    c.strokeStyle = 'rgba(255,255,255,0.20)'; c.lineWidth = 1;
    c.beginPath(); c.arc(bs[0], bs[1], 9, 0, TAU); c.stroke();
    c.fillStyle = 'rgba(255,255,255,0.25)';
    c.beginPath(); c.arc(bs[0], bs[1], 2, 0, TAU); c.fill();
    var p = this.player(); // 自機の想定位置＝淡い三角
    c.strokeStyle = 'rgba(120,220,200,0.45)'; c.lineWidth = 1;
    c.beginPath(); c.moveTo(p[0], p[1] - 4); c.lineTo(p[0] - 3.4, p[1] + 3); c.lineTo(p[0] + 3.4, p[1] + 3); c.closePath(); c.stroke();
    for (var i = 0; i < this.bullets.length; i++) drawBullet(c, this.bullets[i]);
    for (var j = 0; j < this.emitters.length; j++) if (this.emitters[j].draw) this.emitters[j].draw(this, c);
    var label = this.spec.phases[this.phaseIdx] && this.spec.phases[this.phaseIdx].label;
    if (label) {
      c.font = '9px ui-sans-serif, sans-serif'; c.fillStyle = 'rgba(226,236,255,0.55)';
      c.fillText(label, 8, 14);
    }
  };
  Engine.prototype.seekTo = function (sec) {
    var nsteps = Math.max(0, Math.round(sec / DT));
    for (var i = 0; i < nsteps; i++) this.step();
    this.draw();
  };

  var canvases = document.querySelectorAll('canvas.simbox');
  if (!canvases.length) return;
  var engines = [];
  for (var ci = 0; ci < canvases.length; ci++) {
    var eng = new Engine(canvases[ci]);
    canvases[ci]._eng = eng;
    engines.push(eng);
  }

  // スクショ検証用シーク：?simt=秒 → 全 sim をその時刻で静止描画（アニメは起動しない）
  var seek = location.search.match(/[?&]simt=([0-9]+(?:\.[0-9]+)?)/);
  if (seek) {
    var tt = parseFloat(seek[1]);
    for (var si = 0; si < engines.length; si++) engines[si].seekTo(tt);
    return;
  }

  // 再生/停止 UI（ボタンとキャンバス両方で切替）
  function wireToggle(cv) {
    var btn = cv.parentElement.querySelector('.simbtn');
    function toggle() {
      cv._eng.userPaused = !cv._eng.userPaused;
      if (btn) btn.textContent = cv._eng.userPaused ? '▶' : '❚❚';
    }
    if (btn) btn.addEventListener('click', toggle);
    cv.addEventListener('click', toggle);
  }
  for (var wi = 0; wi < canvases.length; wi++) wireToggle(canvases[wi]);

  // 動きを控える設定なら、代表フレーム（3秒時点）で静止させておく
  if (window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) {
    for (var ri = 0; ri < engines.length; ri++) { engines[ri].seekTo(3); engines[ri].userPaused = true; }
    for (var bi = 0; bi < canvases.length; bi++) {
      var b2 = canvases[bi].parentElement.querySelector('.simbtn');
      if (b2) b2.textContent = '▶';
    }
  }

  // 可視の sim だけ回す
  var io = new IntersectionObserver(function (entries) {
    for (var i = 0; i < entries.length; i++) entries[i].target._eng.visible = entries[i].isIntersecting;
  }, { threshold: 0.05 });
  for (var oi = 0; oi < canvases.length; oi++) io.observe(canvases[oi]);

  var last = 0;
  function frame(ts) {
    var dtReal = Math.min(0.1, (ts - last) / 1000); last = ts;
    for (var i = 0; i < engines.length; i++) {
      var g = engines[i];
      if (!g.visible || g.userPaused) continue;
      g.accReal += dtReal;
      var n = 0;
      while (g.accReal >= DT && n < 4) { g.step(); g.accReal -= DT; n++; }
      if (n) g.draw();
    }
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
})();
</script>
`;

// U+FFFD は仮台本の劣化表記として意図的に使うが、配信側が壊れた文字と誤検知するためエンティティ化
writeFileSync(resolve('docs/wiki.html'), html.replace(/�/g, '&#xFFFD;'));
const kb = Math.round(Buffer.byteLength(html, 'utf8') / 1024);
console.log(`docs/wiki.html generated: ${articles.length} articles, ${kb} KB (画像 ${imgCount} 枚 / 縮小後合計 ${Math.round(imgBytes / 1024)} KB / sim ${simCount} 面)`);
if (kb > 14 * 1024) console.warn('[warn] 14MB 超え — Artifact 上限 16MB に接近。画像の上限幅か品質を下げること');
