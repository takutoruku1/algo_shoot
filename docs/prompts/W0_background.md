# フェーズ W0：背景「やすらぎの庭」— ChatGPT 生成プロンプト資料

> 横スクロールSTGの背景。**横方向にシームレス（ループ）／弾・敵が埋もれない低彩度・中明度／紫・赤は避ける／パララックス用にレイヤー分割**が要件。
> 大元: [../CHARACTER_ASSETS.md](../CHARACTER_ASSETS.md) ／ 世界観: [../GAME_DESIGN.md](../GAME_DESIGN.md) ／ ワールド色: 淡い緑＋光。

---

## ★ 背景用 共通スタイルプロンプト（接頭辞）— 全生成の先頭に毎回貼る

**日本語版:**
```
あなたは2DゲームのHD-2D寄りピクセルアート背景アーティストです。以下を厳守してください。

【スタイル】
- 手描きの柔らかいピクセルアート背景。HD-2D寄りで淡い発光・ソフトな陰影は可。3D・写実は禁止。黒のベタ塗り輪郭線は使わない。

【役割（最重要）】
- 横スクロール・シューティングゲームの背景。主役（自機・敵・弾）の視認性を最優先するため、背景は低彩度・中明度で控えめにする。
- 画面の中央〜やや上の「戦闘エリア」は特に簡素・低コントラストに保ち、装飾・明るい要素は上端／下端／遠景へ寄せる。

【カラー】
- 「やすらぎの庭」: 淡い緑・ミント・クリーム・やわらかな空色を基調。明るく優しく幻想的。
- シグネチャ紫 #8A6FD6 は使わない（自機と混同するため）。鮮やかな赤・オレンジも避ける（敵弾と混同するため）。

【構図】
- 横長（16:9のランドスケープ）。左右の端が滑らかに繋がる“横方向にシームレス（タイル可能）”な絵にする。左右に無限ループでスクロールさせる前提。
- 地平線/水平の主要ラインは画面の下寄りか上寄りに置き、中央の戦闘エリアを空けること。

【出力】
- 影・グラデの“ムラ”や唐突な濃淡を避け、つなぎ目が出ないように。
- （前景・中景など透過させたいレイヤーの場合）背景は純マゼンタ #FF00FF のベタ1色のみ。影・模様・市松を入れない。縁のにじみ禁止。
- 空（最遠景）レイヤーのみ不透明で全面塗りでよい。

以下のレイヤーを描いてください:
```

**英語版:**
```
You are an HD-2D-leaning pixel-art background artist for 2D games. Strictly follow this.
STYLE: Soft hand-crafted pixel-art background, HD-2D-leaning, subtle glow/soft shading allowed. No 3D, no photorealism, no solid black outlines.
ROLE (critical): Background for a horizontal side-scrolling shoot-em-up. Player/enemies/bullets readability comes first, so keep the background low-saturation, mid-brightness, understated. Keep the central/upper "combat area" especially simple and low-contrast; push decoration and bright elements to the top/bottom edges and far distance.
COLOR: "Garden of Serenity" — pale green, mint, cream, soft sky-blue. Bright, gentle, fantastical. Do NOT use signature purple #8A6FD6 (clashes with the player). Avoid vivid red/orange (clashes with enemy bullets).
COMPOSITION: Wide 16:9 landscape. Make it HORIZONTALLY SEAMLESS (tileable) so it can scroll left in an infinite loop; left and right edges must connect smoothly. Put the main horizon line low or high, keeping the central combat area clear.
OUTPUT: Avoid abrupt tonal blotches so seams don't show. For transparent layers (mid/foreground), background must be a single FLAT solid pure magenta #FF00FF only (no shadows/patterns/checkerboard, no edge fringing). Only the far sky layer may be fully opaque.
Now draw the following layer:
```

> **横シームレスのコツ（依頼文に必ず添える）**: 日本語『左端と右端が継ぎ目なく繋がる“横方向タイル（seamless / wrap-around）”にしてください。中央に大きな主役を置かず、要素を横方向に均等に散らす。』 ／ EN: `Make it horizontally tileable (seamless wrap-around): the left and right edges must connect with no visible seam. Do not place one large focal element in the center; distribute elements evenly across the width.`

---

## このフェーズで作る背景レイヤー（パララックス4層）

奥→手前の順。スクロール速度は奥ほど遅く、手前ほど速くする（実装側で設定）。

- [ ] **L0 空（最遠景・不透明）** … 淡いグラデの空＋ふわっとした光の粒
- [ ] **L1 遠景** … 遠くに浮かぶ島・大きな半透明の本・かすかな五線/数式の光（ロギアの示唆）
- [ ] **L2 中景** … 近めの浮遊する花壇・低木・木漏れ日の光条
- [ ] **L3 前景（手前）** … 葉・花・つるのシルエット（半透明・速い・視界を塞がない隙間付き）

> まずは手軽に始めたい場合の **「1枚完結版（パララックス無し）」** プロンプトも末尾に用意。

---

## レイヤー別プロンプト
各依頼は「背景用 共通接頭辞 ＋ 横シームレスのコツ ＋ 下記レイヤー文」で投げる。

### L0 空（最遠景・不透明）
```
【日本語】「やすらぎの庭」の最遠景の空。上から下へ、淡いクリーム〜ミントグリーン〜やわらかな空色のごく緩やかなグラデーション。空全体にふんわりした光の粒・ぼかした光のにじみを“控えめに”散らす。雲は薄く柔らかく低彩度。横方向にシームレスで、戦闘エリア（中央）はほぼ無地に近く保つ。不透明で全面塗り。横長。
【English】The far sky of the "Garden of Serenity": a very gentle top-to-bottom gradient from pale cream to mint green to soft sky-blue. Sprinkle soft, blurred light particles/bloom subtly across the sky. Thin, soft, low-saturation clouds. Horizontally seamless; keep the central combat area almost plain. Fully opaque, full-bleed. Wide landscape.
```

### L1 遠景（透過 / マゼンタ背景）
```
【日本語】遠景レイヤー。地平線近くに、遠くに浮かぶ小さな島々（柔らかな草地と小花）、大きく半透明にかすむ“宙に浮かぶ本”、ごく淡く光る五線譜のラインや薄い数式の文字（ロギア＝魔法と数式の世界の示唆。読めなくてよい・うっすら）。すべて遠景なので低彩度・低コントラスト・霞んだ空気感。要素は横方向に均等に散らし、中央上の戦闘エリアは空ける。背景は純マゼンタ #FF00FF のベタ1色。横方向シームレス。
【English】A far layer: near the horizon, distant floating islets (soft grass and tiny flowers), large semi-transparent hazy "floating books", and very faint glowing music-staff lines / thin formula text (a hint of Logia — the world where magic equals math; unreadable, barely there). All distant: low saturation, low contrast, hazy atmosphere. Distribute elements evenly across the width; keep the upper-central combat area open. Background pure flat magenta #FF00FF. Horizontally seamless.
```

### L2 中景（透過 / マゼンタ背景）
```
【日本語】中景レイヤー。画面の下寄りに、近めに浮遊する花壇・丸い低木（トピアリー）・やわらかな草地の島、その間から差す木漏れ日の光条（淡く斜めに）。L1より少しだけ彩度・ディテール高めだが依然として優しい低彩度。明るい光条は下端・端側に寄せ、中央の戦闘エリアは抜けを保つ。背景は純マゼンタ #FF00FF のベタ1色。横方向シームレス。
【English】A mid layer: toward the lower part of the screen, closer floating flower-beds, round topiary shrubs, soft grassy islets, with gentle diagonal god-rays (komorebi) filtering between them. Slightly more saturation/detail than L1 but still soft and low-saturation. Keep bright god-rays toward the bottom/edges; keep the central combat area open. Background pure flat magenta #FF00FF. Horizontally seamless.
```

### L3 前景（手前・透過 / マゼンタ背景）
```
【日本語】前景（手前）レイヤー。画面の上端と下端に沿って、ぼかし気味の葉・花・つる・草のシルエットを配置。やや暗め・低彩度・半透明で、速くスクロールさせる前提。重要：画面中央の大部分は“何もない隙間”にして、プレイヤーや弾の視界を絶対に塞がないこと。上下の縁取りのように使う。背景は純マゼンタ #FF00FF のベタ1色。横方向シームレス。
【English】A foreground layer: along the top and bottom edges only, place softly-blurred silhouettes of leaves, flowers, vines, and grass. Slightly darker, low-saturation, semi-transparent, meant to scroll fast. Important: keep the large central area completely empty so it never blocks the player or bullets — use it like a top/bottom vignette frame. Background pure flat magenta #FF00FF. Horizontally seamless.
```

---

## 手軽版：1枚完結の背景（パララックス無し）
レイヤー分けが面倒な場合、まず1枚で雰囲気を出す用。
```
【日本語】（背景用 共通接頭辞 ＋ 横シームレスのコツ を先頭に）横スクロールSTG「やすらぎの庭」の背景を1枚で。淡いクリーム〜ミント〜空色の空を上半分に、下半分に遠くの浮遊する花壇・小島・大きく霞んだ浮遊する本・ごく淡い五線/数式の光をやさしく配置。全体に低彩度・中明度で、画面中央のやや上（戦闘エリア）は簡素・低コントラストに保つ。鮮やかな赤・オレンジと紫は使わない。横方向に継ぎ目なくループできる構図。横長・不透明。
【English】(paste the background style prefix + the seamless tip first) A single-image background for the horizontal shmup "Garden of Serenity": pale cream-to-mint-to-sky-blue sky in the upper half; in the lower half, distant floating flower-beds, islets, large hazy floating books, and very faint music-staff/formula glow placed gently. Overall low-saturation, mid-brightness; keep the upper-central combat area simple and low-contrast. No vivid red/orange, no purple. Composition tileable seamlessly left-to-right. Wide landscape, opaque.
```

---

## サイズ・書き出し・ゲーム取り込みの実務メモ

### ChatGPTの限界と進め方
- ChatGPTは「完全にシームレスなタイル」を一発で出すのは苦手。**まず雰囲気重視で生成 → 画像編集（Photoshop/Aseprite/GIMP/Krita）でつなぎ目を手修正**する前提で。
  - 継ぎ目消し: 画像を横半分ずらして（オフセット）中央に出た継ぎ目をスタンプ/ぼかしで修正 → 戻す、の定番手順。
- 透過レイヤー（L1〜L3）は純マゼンタ背景で出し、色域指定でキー抜き→PNG透過に。

### 推奨サイズ（内部解像度 384×216 基準）
- **L0 空**: 高さ 216px 以上（上下パララックスの遊び分で 260〜320px 推奨）、横は画面幅の2倍（768px〜）でループ。
- **L1/L2**: 横 768〜1152px（2〜3画面分）× 高さ 216〜300px、横タイル。
- **L3 前景**: 横 768px〜 × 高さ 216px、中央は空ける。
- ChatGPTでは大きめ（例 1536×864 など 16:9）で生成し、後で目標サイズへ縮小（ニアレストネイバー）＋必要なら減色。

### Godot 取り込み（このプロジェクト）
- `char/bg/w0/` に `bg_w0_sky.png` / `bg_w0_far.png` / `bg_w0_mid.png` / `bg_w0_fore.png` のように配置。
- `ParallaxBackground` ＋ 各層 `ParallaxLayer`（`motion_scale` を 0.1/0.3/0.6/1.0 目安）でループスクロール。現状 `Main.cs` の単色 ColorRect 背景を置き換える形で実装可（指示あれば配線します）。
- 命名・配置の大元は [../CHARACTER_ASSETS.md 第8章](../CHARACTER_ASSETS.md#8-命名規則--ファイル構成まとめ) に準拠。
