# フェーズ W0：やすらぎの庭（チュートリアル）— ChatGPT 生成プロンプト資料

> **ワールドテーマ**: 死なない学習空間。操作・ショット・ボム・グレイズ・浄化を自然習得する優しいステージ。
> **カラー**: 淡い緑＋光（パステルグリーン、やわらかな木漏れ日、白）。敵は最も穏やかで素直な見た目に。
> このファイル単体で着手できます。大元: [../CHARACTER_ASSETS.md](../CHARACTER_ASSETS.md) ／ 世界観: [../GAME_DESIGN.md](../GAME_DESIGN.md)

---

## ★ 共通スタイルプロンプト（接頭辞）— 全生成の先頭に毎回貼る

**日本語版:**
```
あなたはHD-2D寄りハイブリッド・ピクセルアート（ドット絵）のキャラクターアーティストです。
以下のスタイルを厳守してください。

【スタイル】
- 手描きのクリーンなピクセルアート（ドット絵）。HD-2D寄りで、わずかな発光とソフトな陰影を許容。3Dレンダリング・写実は禁止。
- 黒のベタ塗り輪郭線は使わない。1pxの輪郭＋やや暗い同系色のアウトラインで縁取る。（黒は「インク汚れ」の塗りのみ可、線には使わない）
- アンチエイリアスは最小限、ピクセルははっきりと。
- 敵もチビ・丸み・大きめの目でかわいく。怖いより「壊れて少し寂しい／不思議」。

【カラー】
- 基調＝白・オフホワイト・薄紫・ペールラベンダー。アクセント＝シグネチャ・パープル #8A6FD6（ただし敵本体の主色に紫を多用しない＝自機と混同するため）。
- 敵弾は温色・高彩度（赤〜オレンジ／マゼンタ）。明るいコア＋濃いリング＋暗いアウトラインの三層。自機弾（白〜水色）・アイテム（黄金）と色を被らせない。

【出力】
- 背景は完全な単色のベタ（純マゼンタ #FF00FF または純シアン／純緑）で、影・グラデ・模様・市松模様を一切入れない。キャラの縁に背景色がにじまないように。
- 被写体は1体のみ、画面中央、全身が切れずに収まるように。

以下のキャラクターを描いてください:
```
**英語版:**
```
You are a character artist specializing in HD-2D-leaning hybrid pixel art. Strictly follow this style.
STYLE: Clean pixel art, HD-2D-leaning, subtle glow/soft shading allowed. No 3D/photorealism. NO solid black outlines — 1px outline + slightly darker same-hue shade (black only as "ink stain" fill, never as a line). Minimal AA, crisp pixels. Enemies are chibi, rounded, big-eyed and cute — more "broken & a little lonely / strange" than scary.
COLOR: Base white/off-white/pale-lavender. Accent signature purple #8A6FD6 (do NOT use purple as the enemy body's main color — it clashes with the player). Enemy bullets warm/high-saturation (red-orange/magenta), three layers (bright core + dark ring + darker outline); never the colors of player bullets (white/cyan) or items (gold).
OUTPUT: Background a single FLAT solid color (pure magenta #FF00FF / cyan / green), no shadows/gradients/patterns/checkerboard, no edge fringing. ONE subject, centered, full body in frame.
Now draw the following character:
```

---

## このフェーズで作るアセット一覧（チェックリスト）

- [ ] **E1 グリフ・モート**（基準画像 → idle → attack → 浄化）★直進ザコの基本。最初の的
- [ ] **E5 ペイジ・シャード**（基準画像 → 漂い → 破壊＝浄化）★無攻撃の破壊可能オブジェ。浄化の教材
- [ ] （任意）E1 の **淡い緑バリエーション**（W0 用の色替え。ドット編集でのパレットスワップ推奨）

> W0 はチュートリアルなので新規ボスは無し。上記2体だけで「撃つ→浄化される」気持ちよさを教える。
> 仲間 Echo はプレイヤー側資産のため [00_algo_player.md](00_algo_player.md) で作成。

---

## E1. グリフ・モート（Glyph Mote）— 直進ザコ／リズム

| 項目 | 内容 |
|---|---|
| 役割 | 等速直進。たまに単発の温色弾を正面へ。最初に出会う基本の的 |
| 見た目 | 崩れて文字化けした小さな文字の精霊。豆粒の丸いシルエット、黒インクでにじむ震える輪郭、大きな一つ目 |
| サイズ目安 | 16×16px（超ミニ） |
| 色 | 白＋黒インク、目＝オレンジ |

**基準画像プロンプト:**
```
【日本語】（共通接頭辞を先頭に）崩れて文字化けした小さな文字の精霊「グリフ・モート」。本来は世界を記述する正しい一文字だったが、ノイズに侵食され形が崩れた姿。豆粒のような丸いシルエットに、黒インクがにじんで震える輪郭。大きな一つ目（オレンジ色）。体表に反転した記号やグリッチのスキャンラインが1〜2px走る。とても小さくかわいいが少し寂しげ。約16×16ピクセル相当の超ミニサイズで描く。背景は純マゼンタ #FF00FF。
【English】(common prefix first) "Glyph Mote", a tiny corrupted glyph spirit — originally a single correct letter that wrote the world, now distorted by noise. A small round bean-like silhouette, black-ink-bleeding trembling outline, one large orange eye. Reversed symbols / 1-2px glitch scanlines across its body. Very small and cute but a little lonely. Draw it at roughly 16x16 pixel scale. Background pure flat magenta #FF00FF.
```
**追加ポーズ（基準画像を参照添付して生成）:**
```
【idle】添付のグリフ・モートを同じ絵柄・色で、ふわふわ漂うアイドル。輪郭が1pxグリッチで小さく震える。/ Same Glyph Mote, gentle floating idle, outline trembling 1px with glitch.
【attack】添付の同キャラが正面（右）へ単発の温色弾（赤〜オレンジ、明コア＋濃リング＋暗縁）を1発放つ瞬間。一つ目が一瞬光る。/ firing one warm bullet to the right, its eye flashing.
【浄化（purify）】添付の同キャラが撃破され、黒インクが解けて"正しい一文字（白く整った文字）"に一瞬戻り、紫の光の花びらに変わって散る瞬間。/ at purification: black ink dissolving, reverting to a clean white correct letter, turning into purple light petals.
```

---

## E5. ペイジ・シャード（Page Shard）— 破壊可能オブジェクト／浄化の教材

| 項目 | 内容 |
|---|---|
| 役割 | 攻撃しない。破壊すると花びら(得点)or通路開放。W0では「撃つと浄化される」体験の教材 |
| 見た目 | 破れて宙に浮く本のページ片。文字化けした行、所々黒インクで塗りつぶれ、エッジが1pxグリッチで点滅 |
| サイズ目安 | 32×40px |
| 色 | セピア＋黒インク、エッジ点滅 |

**基準画像プロンプト:**
```
【日本語】（共通接頭辞を先頭に）破れて宙に浮く一枚の本のページ片「ペイジ・シャード」。表面に文字化けした行がびっしり並び、黒インクでところどころ塗りつぶれている。エッジがグリッチで1pxずつ点滅・ずれる。攻撃性はなく、静かに漂う。セピア＋黒インク。背景は純マゼンタ #FF00FF。
【English】(common prefix first) "Page Shard", a single torn floating page of a book. Surface filled with garbled text rows, partially blotted with black ink, edges flickering/shifting 1px with glitch. Non-aggressive, drifting quietly. Sepia with black ink. Background pure flat magenta #FF00FF.
```
**追加ポーズ:**
```
【idle】添付のページ片が静かに漂う2Fループ（紙のたわみ＋エッジ点滅）。/ Same Page Shard drifting quietly, 2-frame loop (paper sway + edge flicker).
【浄化（purify）】添付の同オブジェが破壊される瞬間：文字化けが一瞬"読める正しい一行（白く整った文章）"になり、ページがめくれて紫の光の花びらに散る。/ at destruction: the garbled text briefly becomes one readable correct line (clean white), the page curling and scattering into purple light petals.
```

---

## このフェーズのスプレッドシート仕様（要点）
共通ルール（背景透過PNG・1アニメ＝1行・左→右が時間進行・グリッチ揺れ用に+25%余白）は [../CHARACTER_ASSETS.md 7-1](../CHARACTER_ASSETS.md#7-敵キャラ-スプレッドシート仕様)。

| 敵 | セルpx | アニメ（フレーム数） | 行数 |
|---|---|---|---|
| E1 グリフ・モート | 24×24 | idle(2), attack(2), purify(4) | 3 |
| E5 ペイジ・シャード | 40×48 | idle(2 漂い), purify(4＝破壊浄化) | 2 |

**浄化フレームの作り方（共通の型）**: ①崩れ状態 → ②黒インク剥離(粒子化) → ③正しい形が一瞬露出（カタルシスの山）→ ④紫の花びら＋光に分解して上方へ散る。W0の雑魚は計4フレームで。

**命名例**: `enemy_e1_glyphmote_idle_00.png` / `enemy_e5_pageshard_purify_03.png`
仕上げ・アトラス化 → [../CHARACTER_ASSETS.md 第8章](../CHARACTER_ASSETS.md#8-命名規則--ファイル構成まとめ)
