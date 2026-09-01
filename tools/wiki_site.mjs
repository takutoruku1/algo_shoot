#!/usr/bin/env node
// wiki/*.md から 1枚モノの Web サイト docs/wiki.html を生成する。
// 使い方: node tools/wiki_site.mjs
// - md はあくまで原稿。読者に見せるのはこの HTML（Artifact として公開）。
// - mermaid フェンスは <pre class="mermaid"> に変換（Artifact 側がネイティブ描画）。
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
  const kind = href.includes('build/shots') ? 'shot' : 'art';
  return `<figure class="fig fig-${kind}"><img src="${embedImage(abs, kind)}" alt="${alt}" loading="lazy"><figcaption>${alt}</figcaption></figure>`;
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
];

const GROUPS = [
  { label: '', ids: ['README', '01_作品概要', '02_世界観'] },
  { label: 'ストーリー', prefix: '03_ストーリー/' },
  { label: 'キャラクター', prefix: '04_キャラクター/' },
  { label: 'ゲーム仕様', prefix: '05_ゲーム仕様/' },
  { label: '', ids: ['06_用語集'] },
  { label: 'ギャラリー', prefix: '07_ギャラリー/' },
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
        fence = fm[1] === 'mermaid' ? 'mermaid' : 'code';
        fenceBuf = [];
      } else {
        if (fence === 'mermaid') out.push(`<pre class="mermaid">${escapeHtml(fenceBuf.join('\n'))}</pre>`);
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
`;

writeFileSync(resolve('docs/wiki.html'), html);
const kb = Math.round(Buffer.byteLength(html, 'utf8') / 1024);
console.log(`docs/wiki.html generated: ${articles.length} articles, ${kb} KB (画像 ${imgCount} 枚 / 縮小後合計 ${Math.round(imgBytes / 1024)} KB)`);
if (kb > 14 * 1024) console.warn('[warn] 14MB 超え — Artifact 上限 16MB に接近。画像の上限幅か品質を下げること');
