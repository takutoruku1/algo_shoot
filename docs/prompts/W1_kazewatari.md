# フェーズ W1：風渡る花の回廊 — ChatGPT 生成プロンプト資料

> **ワールドテーマ**: 順スクロールの基本＋強制スクロール導入。死の概念を導入する最初の実戦（弾速はおそめ）。
> **カラー**: パステル（淡い桃・若葉・空色・白）。風になびく花と光の回廊。
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
- 基調＝白・オフホワイト・薄紫・ペールラベンダー。アクセント＝シグネチャ・パープル #8A6FD6（敵本体の主色に紫を多用しない＝自機と混同するため）。
- 敵弾は温色・高彩度（赤〜オレンジ／マゼンタ）。明るいコア＋濃いリング＋暗いアウトラインの三層。自機弾（白〜水色）・アイテム（黄金）と色を被らせない。

【出力】
- 背景は完全な単色のベタ（純マゼンタ #FF00FF または純シアン／純緑）で、影・グラデ・模様・市松模様を一切入れない。キャラの縁に背景色がにじまないように。
- 被写体は1体のみ、画面中央、全身が切れずに収まるように。

以下のキャラクターを描いてください:
```
**英語版:**
```
You are a character artist specializing in HD-2D-leaning hybrid pixel art. Strictly follow this style.
STYLE: Clean pixel art, HD-2D-leaning, subtle glow/soft shading allowed. No 3D/photorealism. NO solid black outlines — 1px outline + slightly darker same-hue shade (black only as "ink stain" fill, never as a line). Minimal AA, crisp pixels. Enemies chibi, rounded, big-eyed, cute — more "broken & lonely / strange" than scary.
COLOR: Base white/off-white/pale-lavender. Accent signature purple #8A6FD6 (not the enemy body's main color). Enemy bullets warm/high-saturation (red-orange/magenta), three layers (bright core + dark ring + darker outline); never player-bullet (white/cyan) or item (gold) colors.
OUTPUT: Background a single FLAT solid color (magenta #FF00FF / cyan / green), no shadows/gradients/patterns/checkerboard, no edge fringing. ONE subject, centered, full body in frame.
Now draw the following character:
```

---

## このフェーズで作るアセット一覧（チェックリスト）

- [ ] **E2 ベント・ノート**（基準 → spawn → move → attack → 浄化）曲射・誘導ザコ
- [ ] **E3 ブラケット・タレット**（基準 → idle → charge → attack → 浄化）設置型砲台
- [ ] **中ボス 風の番花（かぜのばんか）**（基準 → idle → cast → 誘導花弁 → 風弾 → hit → 浄化）
- [ ] **ステージボス 大輪のフローリア**（基準 → intro → P1〜P3 各攻撃 → phase_break → hit → 浄化）
- [ ] （任意）E1 グリフ・モートの **パステル色替え**（W0素材のパレットスワップで流用）

---

## E2. ベント・ノート（Bent Note）— 曲射・誘導ザコ／避け練習

| 項目 | 内容 |
|---|---|
| 役割 | サイン波で蛇行しつつ、緩い誘導弾(2〜3way)を放つ。避けの練習役 |
| 見た目 | 折れた音符♪。尾から黒インクが滴る。背に壊れた五線譜の破片。丸い体に小さな目 |
| サイズ目安 | 20×20px |
| 色 | オフホワイト＋マゼンタ滴り |

**基準画像プロンプト:**
```
【日本語】（共通接頭辞を先頭に）折れ曲がった音符（♪）の妖精「ベント・ノート」。本来は美しい旋律だったが侵食され、棒が折れ、尾からマゼンタの黒インクが滴る。背中に壊れた五線譜の破片を背負う。丸い体に小さな目。蛇行して飛ぶ軽やかさを感じるポーズ。オフホワイト＋マゼンタの汚れ。約20×20ピクセル相当のミニサイズ。背景は純マゼンタ #FF00FF。
【English】(common prefix first) "Bent Note", a bent musical-note (♪) fairy — once a beautiful melody, now corrupted: broken stem, magenta black-ink dripping from its tail, broken music-staff shards on its back. Round body, small eyes, a light weaving-flight pose. Off-white with magenta stains. ~20x20 pixel scale. Background pure flat magenta #FF00FF.
```
**追加ポーズ:**
```
【spawn】ふわっと現れる2F。/ appearing softly, 2 frames.
【move】蛇行飛行のループ2〜4F（体の傾きと尾の揺れ）。/ weaving-flight loop, body tilt + tail sway.
【attack】口・尾からマゼンタの温色弾を2〜3way放つ瞬間。/ firing 2-3 magenta warm bullets.
【浄化】整った音符に戻り、紫の光の花びらに散る。/ reverting to a clean note, scattering into purple light petals.
```

---

## E3. ブラケット・タレット（Bracket Turret）— 設置型砲台／地形連動

| 項目 | 内容 |
|---|---|
| 役割 | 地形固定。一定間隔で扇状3〜5way、または狙い撃ち単発 |
| 見た目 | 崩れた括弧 `[ }` の口を開閉。中に赤い光核。表面に紫の縁取りとグリッチのにじみ |
| サイズ目安 | 28×24px |
| 色 | 灰＋紫縁、核＝赤 |

**基準画像プロンプト:**
```
【日本語】（共通接頭辞を先頭に）地形に固定された設置型の砲台「ブラケット・タレット」。崩れた括弧 [ と } を口のように開閉する形状。内部に赤く光る核。表面に紫の縁取りとグリッチのにじみ。機械でも生物でもない不思議な存在。横向きで右の画面外へ撃つ向き。背景は純マゼンタ #FF00FF。
【English】(common prefix first) "Bracket Turret", a stationary fixed turret shaped like a corrupted bracket [ and } that opens/closes like a mouth, a red glowing core inside, purple-tinted edges and glitch bleeding. Neither fully machine nor creature. Oriented to fire toward the right. Background pure flat magenta #FF00FF.
```
**追加ポーズ:**
```
【idle】口を閉じ核がほの光る2F。/ mouth closed, core faintly glowing.
【charge】口を開け核が膨張、予兆光。/ mouth open, core swelling, telegraph glow.
【attack】扇状の赤い温色弾(3〜5way)を放つ。/ firing a red fan of 3-5 warm bullets.
【浄化】整った括弧 [ ] に戻り花びら化。/ reverting to a clean bracket [ ], scattering petals.
```

---

## 中ボス：風の番花（かぜのばんか / Warden Bloom）

| 項目 | 内容 |
|---|---|
| 役割 | W1中盤の中ボス（教材ボス）。風と誘導花弁で「避け＋接近」を教える。HP低め・パターン2種 |
| 設定 | 本来は回廊の風を整える正しい守護花（＝気流を司る関数）。ノイズに侵され花弁が破れインクが滴る |
| 見た目 | 大輪の花の精霊。中心に穏やかな顔、周囲に破れて黒インクの滴る花弁。茎に亀裂とグリッチ。中型サイズ |
| 攻撃 | ①誘導花弁（マゼンタの温色弾が緩く追尾）②風の薙ぎ払い（横方向の弾の帯） |
| 色 | パステル桃＋若葉、汚れ＝黒インク、弾＝マゼンタ |

**基準画像プロンプト:**
```
【日本語】（共通接頭辞を先頭に）W1の中ボス「風の番花（かぜのばんか）」。回廊の風を司っていた大輪の花の守護精霊が、ノイズに侵食された姿。中心に穏やかで少し寂しげな顔、周囲をぐるりと囲む花弁は何枚かが破れ、先から黒インクが滴る。茎には亀裂とグリッチのにじみ。本来の美しさが残るパステル桃＋若葉の配色に、黒インクの汚れがまだら。中型の中ボスサイズ（雑魚よりずっと大きい）。やや右を向く。背景は純マゼンタ #FF00FF。
【English】(common prefix first) W1 mid-boss "Warden Bloom" — a great flower guardian spirit that once governed the corridor's wind, now corrupted by noise. A calm, slightly lonely face at its center, surrounded by petals, several torn and dripping black ink. Cracks and glitch bleeding on its stem. Pastel pink + young-green palette with mottled black-ink stains, its original beauty still visible. Mid-boss size (much larger than the small enemies). Facing slightly right. Background pure flat magenta #FF00FF.
```
**追加ポーズ:**
```
【idle】花弁が呼吸するように開閉する4Fループ（黒インクが揺れる）。/ idle, petals breathing open/closed 4-frame loop, ink swaying.
【cast】中心の顔が集中し、花弁を持ち上げて詠唱。/ casting, center face focusing, petals raised.
【attack_petal】誘導花弁＝マゼンタの温色弾を数発、放射状にふわりと放つ瞬間。/ releasing several homing magenta warm "petal" bullets radially.
【attack_wind】花全体を横へなびかせ、風の薙ぎ払い（横一帯の弾の帯）を放つ。/ swaying sideways, releasing a horizontal sweep band of bullets.
【hit】被弾でのけぞり花弁が散りかける（algo周辺フラッシュ無し、本体に被弾フラッシュ）。/ recoiling, petals scattering slightly.
【浄化（purify）】黒インクが剥がれ、破れた花弁が修復されて満開の美しい花に戻り、穏やかな顔で大量の紫の光の花びらに解けていく（8フレーム尺）。/ at purification: ink peels away, torn petals heal into a full beautiful bloom, dissolving with a peaceful face into many purple light petals (8-frame sequence).
```

---

## ステージボス：大輪のフローリア（Floria）

| 項目 | 内容 |
|---|---|
| 役割 | W1の締めくくり。3段階フェーズ制（通常→怒り→断末魔）。弾速はW1らしく控えめ |
| 設定 | 花の回廊そのものを司っていた女王花。回廊全体を咲かせる「大きな式」の化身。深く侵食され、玉座のように咲き崩れている |
| 見た目 | 上半身は気高く優美な花の女王（人型寄り）、下半身は巨大な咲き崩れた花床。冠状の花、流れる花びらの裾。黒インクとグリッチに侵食。画面の存在感がある大型 |
| 攻撃 | P1: 整然とした花弁の拡散弾 / P2: 誘導花弁＋回転する弾の渦 / P3: 画面を覆う花吹雪状の収束弾（華のある見せ場） |
| 色 | 白〜パステル桃の気品＋若葉、亀裂＝赤、汚れ＝黒インク、弾＝マゼンタ〜赤 |

**基準画像プロンプト（必ず保存して各フェーズで参照添付）:**
```
【日本語】（共通接頭辞を先頭に）W1のステージボス「大輪のフローリア」。花の回廊を咲かせる「大きな式」の化身である女王花が、ノイズに深く侵食された姿。上半身は気高く優美な花の女王（人型寄り、頭に冠状の花、流れる花びらの裾）、下半身は巨大に咲き崩れた花床と化す。全体は白〜パステル桃＋若葉の気品ある配色だが、所々が黒インクとグリッチに侵食され、赤い亀裂が走る。美しいが深く壊れて切ない。画面に存在感のある大型ボス。やや左を向いてalgoと対峙。背景は純マゼンタ #FF00FF。
【English】(common prefix first) W1 stage boss "Floria" — the queen flower, an embodiment of the "great formula" that blooms the corridor, now deeply corrupted by noise. Her upper body is a noble, graceful flower queen (humanoid-ish, a crown of blossoms, a hem of flowing petals); her lower body becomes a giant collapsed flower-bed. Overall a refined white-to-pastel-pink + young-green palette, partly corrupted by black ink and glitch with red cracks. Beautiful yet deeply broken and sorrowful. A large boss with strong screen presence, facing slightly left to confront algo. Background pure flat magenta #FF00FF.
```
**フェーズ別 追加プロンプト（基準画像を参照添付して生成）:**
```
【intro】玉座の花床からゆっくり起き上がり咲き開く登場4F。/ rising and blooming open from her flower-bed throne, 4-frame intro.
【P1（通常）】落ち着いた佇まいで、整然とした花弁の拡散弾を放つ第1形態。冠の花は原型を保つ。/ Phase 1: composed, firing orderly spreading "petal" bullets, crown blossoms intact.
【P2（怒り）】黒インクの侵食が広がり冠の花が裂けて赤く発光、姿勢が激しくなり、誘導花弁＋回転する弾の渦を放つ攻撃的な第2形態。グリッチ強め。/ Phase 2 (enraged): ink spreading, crown blossoms split and glow red, aggressive, firing homing petals + a rotating bullet vortex, stronger glitch.
【P3（断末魔）】下半身の花床が崩れ、上半身も線画・ワイヤーフレーム化が始まり、背後に暴走した式の光輪。画面を覆う花吹雪状の巨大な収束弾幕を放つ華のある最終形態。/ Phase 3 (death-throes): flower-bed collapsing, upper body partly line-art/wireframe, a runaway ring of light behind her, unleashing a screen-filling "flower-storm" convergent barrage. Spectacular final form.
【phase_break】各フェーズ移行：黒インク侵食が一段進む演出4F。/ phase transition: ink corruption advancing one stage, 4 frames.
【hit】被弾フラッシュでのけぞる短い反応。/ short recoil with a hit flash.
【浄化（purify）】黒インクとグリッチが一気に剥がれ、本来の"正しく美しい女王花（白〜パステル桃のクリーンな満開）"に一瞬戻り、穏やかな表情で回廊いっぱいの紫の光の花びらに解けていく（12フレームの最大尺・無音＋ヒットストップの見せ場）。/ at purification: all ink and glitch peel away at once, she briefly reverts to her true, clean, full-bloom queen form (white-pastel-pink), dissolving with a peaceful face into a corridor-filling cloud of purple light petals (12-frame max sequence, silence + hitstop showcase).
```

---

## このフェーズのスプレッドシート仕様（要点）
共通ルール・浄化フレームの型は [../CHARACTER_ASSETS.md 第7章](../CHARACTER_ASSETS.md#7-敵キャラ-スプレッドシート仕様)。

| 敵 | セルpx | アニメ（フレーム数） | 行数 |
|---|---|---|---|
| E2 ベント・ノート | 28×28 | spawn(2), move(4 loop), attack(2), purify(4) | 4 |
| E3 ブラケット・タレット | 36×32 | idle(2), charge(2), attack(3), purify(4) | 4 |
| 風の番花（中ボス） | 80×88 | idle(4 loop), cast(3), attack_petal(2), attack_wind(2), hit(2), purify(8) | 6 |
| フローリア（ボス） | 160×176（フェーズ別シート） | `boss_floria_p1`: intro(4),idle(6),attack(3),hit(2),phase_break(4) / `_p2`: idle(6),attack_homing(3),attack_vortex(3),hit(2),phase_break(4) / `_p3`: idle(6 崩壊),attack_barrage(4),hit(2),purify(12) | フェーズ別 |

**命名例**: `enemy_e2_bentnote_move_01.png` / `enemy_e3_bracketturret_attack_02.png` / `midboss_warden_bloom_purify_05.png` / `boss_floria_p3_attack_barrage_02.png`
仕上げ・アトラス化・命名規則 → [../CHARACTER_ASSETS.md 第8章](../CHARACTER_ASSETS.md#8-命名規則--ファイル構成まとめ)
