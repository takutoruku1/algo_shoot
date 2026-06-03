# algo: Refrain of Light — キャラクター画像アセット制作資料

> 対象作品: **algo: Refrain of Light**（2D横スクロール弾幕STG / PC・Steam）
> 内部解像度: **384×216（16:9）** ／ シグネチャ・パープル: **#8A6FD6**
> 基準アセット: `char/algo.png`（高解像度イラスト。本資料でドット絵化・アニメ分割する）
> 企画書: `docs/GAME_DESIGN.md`

この資料は、主人公 **algo（アルゴ）** と **敵キャラクター10体** の画像アセット（スプライト／スプライトシート）を、ChatGPT（GPT-4oの画像生成）を使って制作するための一冊完結の作業資料です。実作業者がこれ1枚で着手できる粒度で、ChatGPTにコピペできる日英プロンプト実物を省略せず収録しています。

---

## 0. この資料の使い方

### 0-1. 全体像
- **第1〜2章**＝全キャラ共通の土台（アートスタイル／技術仕様／ChatGPTワークフロー）。**最初に必ず読むこと**。
- **第3〜4章**＝主人公 algo の動き仕様とプロンプト。
- **第5〜7章**＝敵キャラのデザイン・プロンプト・シート仕様。
- **第8章**＝命名規則とファイル構成の全体まとめ（実装の最終リファレンス）。

### 0-2. 作業着手の順序（おすすめ）
1. 第1章でスタイル・カラー・解像度ルールを頭に入れる。
2. 第2章「共通ワークフロー」と「共通スタイルプロンプト（接頭辞）」を理解する。この接頭辞を**全生成の先頭に毎回貼る**のが品質の鍵。
3. algo → 雑魚敵 → 中ボス → ボス → ライバルの順で、1キャラずつ「基準画像確定 → ポーズ個別生成 → 手作業整列」を回す。
4. 第8章の命名・配置に従ってアトラス化し、エンジン（Godot想定）に取り込む。

---

## 1. 共通アートスタイル & 技術仕様（全キャラ厳守）

### 1-1. アートスタイル
- **スタイル**: HD-2D寄りハイブリッド・ピクセルアート（ドット絵）。わずかな発光とソフトな陰影は許容。3Dレンダリング・写実・過度なグラデは禁止。
- **輪郭線**: **黒のベタ塗り輪郭線は使わない。** 必ず「**1px輪郭＋やや暗い同系色のアウトライン**」で縁取る。
  - 例外: 黒は「インク汚れ（敵の汚染表現）」の**塗り**としてのみ使用可。**線として黒を使わない**。
- **アンチエイリアス**: 最小限。ピクセルははっきりと。
- **トーン**: 幻想的でかわいい。algoは「かわいく幻想的」、敵は「怖いより、壊れて少し寂しい／不思議」。algoと同じ世界観に収める。

### 1-2. カラー（基調とアクセント）
- **基調パレット**: 白・オフホワイト・薄紫・ペールラベンダー。
- **アクセント**: シグネチャ・パープル **#8A6FD6**（algoの紫十字・発光・王冠の宝石）。彩度は中程度、明るく清潔なトーン。
- **役割別カラー（絶対に混同させない／浄化テーマの根幹）**:

| 要素 | 色 | 備考 |
|---|---|---|
| 自機弾（algoの「光のインク」） | 白〜水色＋発光 | 浄化する側の光 |
| 敵弾（「黒インク」） | **温色・高彩度（赤〜オレンジ／マゼンタ）** | 明るいコア＋濃いリング＋暗いアウトラインの三層。白・水色・黄金は使わない |
| アイテム | 黄〜金 | 得点・回収物 |
| 敵本体 | 白系／グレー／薄紫 ＋ 黒インク汚れ | 背景に溶けない中明度。**紫 #8A6FD6 を主役で使いすぎない**（自機アクセントと競合するため、紫は「演算子の残滓」程度のワンポイント） |

### 1-3. 解像度・サイズ・向き
- **内部解像度**: 384×216（16:9）。表示は**整数倍スケールのみ（補間なし／ニアレストネイバー）**。
- **algo表示サイズ**: 約48〜64px高のチビ3頭身。
- **向き**: 横STGのため**基本「右向き（画面右へ進む）」を正面**とする。左向きはコード側で水平反転して作る。
- **チビ比率**: 約3頭身（頭が大きくかわいい）。全キャラで頭身を一定に保つ。

### 1-4. 世界観（敵デザインの根拠 ／ 企画書より）
- 浮遊図書館世界「ロギア」。魔法と数式が同一視される世界。
- 敵は**世界を記述する"正しい式・文字・記号"が黒インク／グリッチに侵食されて崩れた姿**。
- algoの光のインク弾を受けると、崩れが解け、一瞬"本来の正しい形"に戻ってから**光の花びら**になって消える（破壊ではなく**浄化**）。これが本作固有の余韻。

---

## 2. ChatGPT 共通ワークフロー & コツ

### 2-1. 大前提：なぜこの手順か
ChatGPTは**ピクセルパーフェクトな等間隔グリッドのスプライトシートを一発で正確に出すのが苦手**。そこで全キャラ共通で次の3段階を踏む。

1. **キャラ基準画像を1枚作る** — そのキャラの「正面・標準1ポーズ」を高品質で確定。
2. **基準画像を参照添付して、1ポーズずつ生成** — 出現／移動／発射／被弾／浄化…を個別に。
3. **手作業で切り出し・整列** — Aseprite等でセルpxに合わせてシート化（**整列はGPTに任せない**）。

### 2-2. 共通スタイルプロンプト（接頭辞）— 全生成の先頭に毎回貼る

このブロックを、algo・敵を問わず**毎回プロンプトの先頭に貼る**。チャットを跨ぐとスタイルが薄れるので省略しないこと。

**日本語版:**
```
あなたはHD-2D寄りハイブリッド・ピクセルアート（ドット絵）のキャラクターアーティストです。
以下のスタイルを厳守してください。

【スタイル】
- 手描きのクリーンなピクセルアート（ドット絵）。HD-2D寄りで、わずかな発光とソフトな陰影を許容。3Dレンダリング・写実は禁止。
- 黒のベタ塗り輪郭線は使わない。1pxの輪郭＋やや暗い同系色のアウトラインで縁取る。
- アンチエイリアスは最小限、ピクセルははっきりと。
- キャラはチビ3頭身（頭が大きくかわいい）。表示想定は小さいキャラ。

【カラー】
- 基調＝白・オフホワイト・薄紫・ペールラベンダー。
- アクセント＝シグネチャ・パープル #8A6FD6。
- 彩度は中程度、明るく清潔なトーン。被写体に彩度を集中。

【出力】
- 背景は完全な単色のベタ（純マゼンタ #FF00FF または純シアン／純緑）で、影・グラデ・模様・市松模様を一切入れない（後で透過処理するため）。キャラの縁に背景色がにじまないように。
- 被写体は1体のみ、画面中央、全身が切れずに収まるように。
- キャラの向きは右向き（画面の右を向く）。

以下のキャラクターを描いてください:
```

**英語版:**
```
You are a character artist specializing in HD-2D-leaning hybrid pixel art.
Strictly follow this style.

STYLE
- Hand-crafted clean pixel art, HD-2D-leaning, with subtle glow and soft shading allowed. No 3D rendering, no photorealism.
- NO solid black outlines. Use a 1px outline with a slightly darker shade of the same hue.
- Minimal anti-aliasing, crisp pixels.
- Chibi proportions, about 3 heads tall, cute, big head. Intended display size is small.

COLOR
- Base palette: white, off-white, pale lavender, pale purple.
- Accent: signature purple #8A6FD6.
- Medium saturation, bright and clean tone. Concentrate saturation on the subject.

OUTPUT
- Background must be a single FLAT solid color (pure magenta #FF00FF, or pure cyan/green), absolutely no shadows, gradients, patterns, or checkerboard (for later background removal). No background-color fringing on the character edges.
- Only ONE subject, centered, full body fully inside the frame.
- Character faces to the RIGHT (looking toward the right side of the screen).

Now draw the following character:
```

> **敵弾を一緒に描かせる時に必ず添える一文:**
> 日本語『敵弾は温色・高彩度（赤〜オレンジまたはマゼンタ）で、明るいコア＋濃いリング＋暗いアウトラインの三層。自機の弾（白〜水色）やアイテム（黄金）とは色を被らせない。』
> English: `Enemy bullets are warm, high-saturation (red-orange or magenta), three layers: bright core + dark ring + darker outline. Never use the colors of the player's bullets (white/cyan) or items (gold).`

### 2-3. 一貫性を保つコツ
- **基準画像を毎回必ず添付**し、「添付の絵柄・色・プロポーションを変えず、ポーズだけ変えて」と明示。
- 「キャラデザインを再解釈しない／髪型・王冠・紫十字・ローブ形状（敵なら固有モチーフ）を変えない」と毎回釘を刺す。
- **1回の依頼で変えるのはポーズ1点のみ**（ポーズ＋表情＋画角を同時に大きく変えると破綻しやすい）。
- **1スレッドで1キャラを通す**。崩れたら最新の良い1枚を改めて添付してリセット。
- 絶対サイズはGPTが苦手なので、相対比較（「algoより一回り小さく」「約24×24ピクセル相当の超ミニで」）も併用。

### 2-4. 背景透過の扱い
- ChatGPTは透過PNGや「市松模様なし」を安定して守れないことが多い。**純色ベタ背景（被写体に使わない補色：紫が多いキャラは緑/シアン、敵はマゼンタ等）で出力 → 後工程でキー抜き**するのが確実。
- 「影・グラデ・模様を背景に入れない」「縁のにじみ禁止」を毎回明記。

### 2-5. コンタクトシート（複数ポーズの並べ見本）の頼み方
- 一発で等間隔グリッドの完璧なシートは無理。**「ラフな並べ見本」までと割り切り、整列は手作業**（第5手順）でやる前提。
- 依頼例:
```
添付のキャラを使い、同じ絵柄・色・サイズで複数ポーズを1枚に並べてください。
2行×3列で各セルに1ポーズ。各ポーズのキャラの大きさと基準点（algoなら胸の紫十字）の高さを揃え、セル間に均等な余白を。背景は全面 純マゼンタ #FF00FF のベタ1色。
```

### 2-6. 失敗しやすい点と回避策（全キャラ共通）

| 失敗 | 回避策 |
|---|---|
| 等身が毎回変わる | 基準画像を添付し「3頭身チビ・頭の大きさ固定」を毎回明記。1ポーズずつ生成 |
| 固有モチーフが消える/変わる（algoの紫十字・王冠、敵の固有記号） | 「○○を必ず描く」を毎回書き、生成後に目視チェック |
| 黒い太線輪郭が付く | 「黒のベタ輪郭線禁止、1px＋暗い同系色アウトライン」を強調。出続けるなら良い1枚を添付し「この線の処理を守って」 |
| 3D・写実・グラデ過多 | 「フラットなドット絵、AA最小、HD-2D寄り、3Dレンダリング禁止」を追加 |
| 背景に影/模様が入り透過が汚れる | 純色ベタ・影模様禁止・縁のにじみ禁止を明記。市松も描かせない |
| 紫を敵本体に使いすぎる | 「紫 #8A6FD6 はワンポイントのみ。自機弾と混同するので体の主色に使わない」 |
| 弾が自機弾と紛らわしい | 「敵弾は温色（赤〜オレンジ／マゼンタ）固定、白・水色・黄金は使わない」を弾を描く時に毎回添付 |
| 解像度が高くドット感が出ない | 出力後に手作業で縮小＋減色（ニアレストネイバー）。生成段階で完璧なドットは狙わない |
| 左右の向きが揺れる | 「右向き」を毎回固定。左向きはコード反転で作る |
| 色味が微妙に変わる | パレットチップ画像も併せて添付し「このパレットの色のみ使用」 |
| グリッドシート一発生成がズレる | **1ポーズ=1生成**。シートの整列はGPTに任せず手作業 |
| 連番アニメのフレームが不連続 | 「直前フレームを添付して、ここから少しだけ動かした次のフレーム」と1枚ずつ送る（特に浄化の剥離→花びら） |

---

## 3. algo の動き スプレッドシート仕様

### 3-1. 必要なアニメーション一覧
横STG主人公として必要な全モーション。**1フレームの解像度は全アニメ共通で 64×64px セル**に統一（algo本体は48〜64px高で、セル内に余白を持たせ揺れ・エフェクトを収める）。fpsは内部60fps想定での「見た目の更新速度」目安。

| # | アニメ名 (key) | 用途 | フレーム数 | セル解像度 | ループ | 速度(fps目安) |
|---|---|---|---|---|---|---|
| 1 | `idle_float` | 常時の待機浮遊（基本表示） | 4〜6 | 64×64 | ○ ループ | 8 |
| 2 | `move_up` | 上移動バンク（上昇傾き） | 3 | 64×64 | △ 末尾保持 | 12 |
| 3 | `move_up_loop` | 上移動の継続（最大バンクのゆらぎ） | 2 | 64×64 | ○ ループ | 8 |
| 4 | `move_down` | 下移動バンク（下降傾き） | 3 | 64×64 | △ 末尾保持 | 12 |
| 5 | `move_down_loop` | 下移動の継続 | 2 | 64×64 | ○ ループ | 8 |
| 6 | `focus_idle` | 低速移動（集中）待機。判定縮小・表情キリッ | 4 | 64×64 | ○ ループ | 8 |
| 7 | `shot` | メインショット詠唱（フツー弾連射） | 3〜4 | 64×64 | ○ ループ | 12〜15 |
| 8 | `weave_shot` | 紡ぎ弾（遅い・塗り濃い）詠唱 | 4 | 64×64 | ○ ループ | 10 |
| 9 | `dash` | フロート・ダッシュ（短距離高速・無敵約8F） | 3 | 64×64 | × ワンショット | 20 |
| 10 | `graze` | グレイズ吸収反応（弾を魔力に変換した閃き） | 2 | 64×64 | × ワンショット | 15 |
| 11 | `hit` | 被弾（点滅＋白フラッシュ＋のけぞり） | 3〜4 | 64×64 | × ワンショット | 12 |
| 12 | `bomb` | ボム「魔法陣・解放」発動（全身エフェクト） | 6〜8 | **96×96** | × ワンショット | 12 |
| 13 | `overload` | リフレイン満タン超強化状態の発光ループ | 3 | 64×64 | ○ ループ | 8 |
| 14 | `appear` | 登場（光から実体化＋元気ポーズ） | 6 | 64×64 | × ワンショット | 12 |
| 15 | `win` | 勝利・リザルト（元気ポーズ＋喜び） | 4〜6 | 64×64 | ○ ループ | 8 |
| 16 | `death` | 死亡（残機ロスト。光の粒へ霧散） | 6 | 64×64 | × ワンショット | 10 |

> - `bomb` のみ全身魔法陣エフェクトのため **96×96px の特大セル**を別シートで確保。
> - 表情差分（通常・集中・被弾・喜び）は各アニメ内に内包（別レイヤー管理可）。
> - **最優先実装（フェーズ1）**: `idle_float` / `move_up` / `move_down` / `shot` / `hit`。ボムと勝利は次点。

### 3-2. 主要アニメのフレーム内容
**全アニメ共通の原則**: 胸元の**紫十字マークの中心位置は常に画面上でブレさせない**（揺れても±1px以内）。これが当たり判定の基準点になる。

**`idle_float`（待機浮遊・4〜6F / ループ / 8fps）** — 上下に約2〜3pxサインカーブで漂う。
- F1（最下）: 体が一番下。髪・ローブ裾は自然落下。王冠は通常の薄い光。表情は通常。
- F2（上昇中）: 体が1〜2px上へ。後ろ髪が遅れて持ち上がる。ローブ裾が外に開き始める。
- F3（最上）: +3px。後ろ髪が最も浮く。裾が最も広がる。王冠の宝石が1フレームだけキラッ。
- F4（下降中）: 体が戻る。髪・裾が遅れて沈む（慣性）。6F版はF5/F6で減衰を細かく。

**`move_up`（上バンク・3F＋ループ2F / 末尾保持 / 12fps）**
- F1: 約8°上傾斜。髪が後ろ下へ流れる。
- F2: 約15°。裾が下後方へなびく。後ろ髪が大きく後流。表情やや集中。
- F3（最大・保持）: 約20°。上前方を向く。**後方に半透明トレイル1枚**。
- `move_up_loop`（F1/F2）: 最大バンクで髪・裾を小さくゆらす2Fループ。

**`move_down`（下バンク・3F＋ループ2F / 末尾保持 / 12fps）** — `move_up`の上下対称。
- F1: 約8°下傾斜、髪が上後方へ。F2: 約15°、裾が上後方へめくれる。F3（最大保持）: 約20°、後方に残像1枚。
- `move_down_loop`: 最大バンクで小ゆらぎ2Fループ。上下入力を離すと `idle_float` の最寄りへ補間。

**`shot`（メインショット・3〜4F / 連射中ループ / 12〜15fps）** — 前方（右）へ手をかざす。
- F1（構え）: 前の手を前へ。手先に白〜水色の小発光。表情やや集中（瞳に光点）。
- F2（発射）: 手先で発光フラッシュ＋**白〜水色の発光リング1枚**。反動で体が1px後ろへ。
- F3（戻り）: 発光減衰、手が基本位置へ。4F版はF2をコア閃光→拡散リングの2枚に分割。
- `weave_shot`（紡ぎ弾）: 同要領だが発光色を**紫寄り #8A6FD6**にし、手を弧を描く4Fに。10fpsでゆったり。

**`hit`（被弾・3〜4F / ワンショット / 12fps）** — フラッシュはalgo周辺のみ（背景は減光しない）。
- F1（白フラッシュ）: 全身が白〜ペールラベンダーのシルエットフラッシュ。
- F2（のけぞり）: 体が後方（左）へのけぞり、頭が下がる。髪・裾が衝撃で前方へ乱れる。表情は驚き・苦痛（瞳を細める／口を「っ」）。赤みのリムライトをわずかに。
- F3（点滅戻り）: 半透明点滅で基本姿勢へ復帰開始。F4（復帰）: 通常へ。以後の無敵点滅はコード側で実装（絵は通常フレーム使用）。

**`bomb`（ボム・6〜8F / ワンショット / 12fps / 96×96特大セル）** — 「魔法陣・解放」。王冠（管理者権限＝ルートの証）の発光が主役。
- F1（溜め）: 両手を胸（紫十字付近）へ。紫十字がジワッと発光、王冠の宝石が灯る。瞳に強い光。
- F2（チャージ）: 紫十字から放射光。足元に紫の魔法陣の輪郭。髪・裾が上向きに浮き上がる。
- F3（詠唱ピーク）: 王冠が眩しく輝き（白〜ゴールド）、頭上に小さな冠状の光。魔法陣完成・回転。手を左右に開く解放ポーズ。
- F4（解放・発光最大）: 全身から紫＆白の光が爆発拡散（紫十字モチーフの大きな光紋）。最も明るい1枚。
- F5〜F6（拡散）: 光紋が外周へ広がり薄れる。浄化された敵弾＝得点アイテムの花びら小粒を散らす。
- F7〜F8（収束）: 光が収まり `idle_float` 基本姿勢へ。残光がふわり消える。

### 3-3. グリッド・原点・当たり判定点
- **標準セル**: 64×64px（透明背景、余白込み）。**ボム専用セル**: 96×96px（別シート分離）。
- **ピボット（原点）**: 各セルの中心 **(32,32)** を基準にし、algo胴体中心をそこに合わせる。揺れはこの基準の周囲で動かす。
- **当たり判定点（紫十字）**: algo胸元の紫十字マーク中心を**ヒットボックス可視点**とし、**全フレームで座標を一定**（例: セル内 x=32, y=34 ±1px）に固定。これがゲーム側の被弾判定座標。制作時は「紫十字中心ガイド」を全フレームに重ねて整列。
- **向き**: 全フレーム右向き。左移動時はコード水平反転（反転後も紫十字中心が一致するよう左右対称配置を意識）。

### 3-4. 標準シートのレイアウト
- **行＝1アニメーション／列＝フレーム進行（左→右で時系列）**。フレーム数より右の余セルは透明。
- 最大フレーム数 `idle_float`(6) を基準に**標準シートは横6列**。`bomb`(8) のみ横8列の別シート。

| 行 | アニメ | 使用列数 |
|---|---|---|
| 0 | `idle_float` | 6 |
| 1 | `move_up`(F1-3) + `move_up_loop`(2) | 5 |
| 2 | `move_down`(F1-3) + `move_down_loop`(2) | 5 |
| 3 | `focus_idle` | 4 |
| 4 | `shot` | 4 |
| 5 | `weave_shot` | 4 |
| 6 | `dash`(3) + `graze`(2) | 5 |
| 7 | `hit` | 4 |
| 8 | `overload` | 3 |
| 9 | `appear` | 6 |
| 10 | `win` | 6 |
| 11 | `death` | 6 |

- **標準シート全体**: 6列 × 12行 × 64px = **384 × 768px**（横を内部解像度幅384に一致）。
- **ボムシート全体**: 8列 × 1行 × 96px = **768 × 96px**。

---

## 4. algo 生成プロンプト集

> 運用: **(B)で基準画像を1枚確定 → 毎回それを参照添付 → 第2章の共通接頭辞を先頭に付けて (C)の各ポーズを1枚ずつ生成**。

### (A) ベース
全プロンプトの先頭に**第2章「2-2 共通スタイルプロンプト」**を貼ったうえで、algo固有の以下キャラ定義を続ける。

**日本語版（キャラ定義ブロック）:**
```
【キャラ「algo（アルゴ）」】
- 白〜薄紫のふわっとした髪、頭に小さな王冠／角のような飾り。
- 白いローブ風ワンピース、胸元に黒い装飾と紫の十字（クロス）マーク。
- 大きな紫の瞳。かわいく幻想的な魔法使い／精霊。常時ふわふわ浮遊。
- アクセントのシグネチャ・パープル #8A6FD6 は、紫十字・発光・王冠の宝石に使う。
```
**英語版:**
```
CHARACTER "algo"
- Soft fluffy white-to-pale-lavender hair, a tiny crown / small horn-like ornament on the head.
- White robe-like one-piece dress, a black chest ornament with a purple cross (plus) mark on the chest.
- Big purple eyes. A cute, fantastical little mage / spirit, always gently floating.
- Apply the signature purple #8A6FD6 on the purple cross, glows, and crown gem.
```

### (B) algo 基準キャラ画像（標準立ち＋カラーパレット）
> まず参照なしでこれを生成し、最も設定に合う1枚を「基準画像」として確定。標準立ちと（任意で）Tポーズの2枚を作っておくと以後の参照に便利。

**日本語版:**
```
（第2章の共通接頭辞 ＋ (A)のalgoキャラ定義 を先頭に貼る）

【今回の依頼：キャラ基準画像（リファレンスシート）】
algoの「標準ニュートラル立ち」を、後続アニメの基準にできる明快な1枚として描いてください。
- ポーズ：正面やや右向きの自然な立ち（ふわっと浮いた感じ。足は軽く揃える）。手は自然に下げる。
- 表情：通常（穏やかに微笑、口は小さく）。
- ライティング：フラットで均一（影を最小に）。後でドット化しやすいように。
- 構図：キャラを大きく中央に、全身が余白を持って収まるように。
- 同じ画像の右側に「カラーパレット見本」を小さな四角チップで並べてください：
  髪(白〜薄紫), 肌, ローブ白, ローブ影(薄紫), 胸の黒装飾, 紫十字 #8A6FD6, 瞳の紫, 王冠の金/宝石。
- 背景は純マゼンタ #FF00FF のベタ1色。
```
**英語版:**
```
(paste the common style prefix + the algo character block first)

TASK: CHARACTER REFERENCE SHEET
Draw algo in a clear, neutral standing pose to serve as the master reference for later animations.
- Pose: natural standing, slightly facing right, gently floating (feet lightly together), arms relaxed down.
- Expression: neutral, softly smiling, small mouth.
- Lighting: flat and even, minimal shadow, easy to pixelize later.
- Composition: character large and centered, full body with margin.
- On the right side of the same image, add a small COLOR PALETTE strip as square chips:
  hair (white-to-lavender), skin, robe white, robe shadow (pale purple), black chest ornament, purple cross #8A6FD6, eye purple, crown gold/gem.
- Background: pure flat magenta #FF00FF only.
```

### (C) 主要ポーズ個別プロンプト
> 各依頼は「共通接頭辞＋(B)で確定した基準画像を添付＋下記ポーズ文」で投げる。

**C-1 アイドル浮遊（idle_float）**
```
【日本語】添付の基準画像のキャラ「algo」をそのままの絵柄・色で使い、待機の浮遊ポーズを描いてください。ふわっと宙に浮き、後ろ髪とローブ裾が下方向へ自然に垂れて軽く揺れる。手は体の横で力を抜く。表情は通常で穏やか。紫十字マークは胸の中央にはっきり見えるように。背景は純マゼンタ #FF00FF のベタ1色。右向き。
【English】Using the attached reference of "algo" with the exact same art style and colors, draw a calm idle floating pose. She hovers gently; back hair and robe hem drape downward and sway lightly; arms relaxed at her sides; neutral soft expression. Keep the purple cross mark clearly visible at the center of the chest. Background pure flat magenta #FF00FF. Facing right.
```
**C-2 上バンク（move_up）**
```
【日本語】添付のalgoを同じ絵柄・色で、上方向へ上昇する移動ポーズに。体を上向きに約15〜20度傾け、髪とローブ裾が後ろ下方向へ風になびく。やや集中した表情。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, in an upward-moving banking pose: body tilted up ~15-20 degrees, hair and robe hem streaming back-and-down as if in wind, slightly focused expression. Background pure flat magenta #FF00FF. Facing right.
```
**C-3 下バンク（move_down）**
```
【日本語】添付のalgoを同じ絵柄・色で、下方向へ降下する移動ポーズに。体を下向きに約15〜20度傾け、髪とローブ裾が後ろ上方向へめくれてなびく。やや集中した表情。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, in a downward-moving banking pose: body tilted down ~15-20 degrees, hair and robe hem flowing back-and-up, slightly focused expression. Background pure flat magenta #FF00FF. Facing right.
```
**C-4 ショット（shot）**
```
【日本語】添付のalgoを同じ絵柄・色で、前方（右）へ魔法を放つショット詠唱ポーズに。前の手を右へ突き出し、手のひらの先に白〜水色の小さな発光リング。集中した表情、瞳に光点。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, casting a forward magic shot: front hand thrust to the right, a small white-to-cyan glowing ring at the palm/fingertips, focused expression with a light glint in the eyes. Background pure flat magenta #FF00FF. Facing right.
```
**C-5 被弾（hit）**
```
【日本語】添付のalgoを同じ絵柄・色で、敵弾に被弾してのけぞる瞬間に。体が後ろ（左）へのけぞり、髪とローブが衝撃で前方に乱れる。驚き・苦痛の表情（瞳を細め、口は「っ」）。algoのまわりだけ淡い白フラッシュのにじみ。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, the moment she is hit and recoils: body flinching backward (to the left), hair and robe disturbed forward by impact, a startled/pained expression (squinted eyes, small "!" mouth), a faint white flash bloom only around her. Background pure flat magenta #FF00FF. Facing right.
```
**C-6 ボム（bomb）**
```
【日本語】添付のalgoを同じ絵柄・色で、必殺技「魔法陣・解放」の発動ポーズに。両手を左右に開いた解放ポーズ。頭の小さな王冠が眩しく輝き（白〜金のハイライト）、胸の紫十字 #8A6FD6 から放射状の光が広がる。足元に紫の魔法陣の輪。全身を紫と白の光が包む。髪と裾はエネルギーで上向きに浮き上がる。背景は純マゼンタ #FF00FF（光は背景に溶け込ませない）。右向き、やや正面。
【English】Same algo, same style/colors, activating her bomb "Spell-Circle Release": arms spread open in a releasing pose, the tiny crown glowing brightly (white-to-gold highlights), radiant light bursting from the purple cross #8A6FD6 on the chest, a purple magic circle ring at her feet, her whole body wrapped in purple-and-white glow, hair and hem lifted upward by the energy. Background pure flat magenta #FF00FF (do not blend the glow into the background). Facing right, slightly toward viewer.
```
**C-7 勝利（win）／登場（appear）**
```
【日本語】添付のalgoを同じ絵柄・色で、勝利の元気ポーズに。片手（または両手）を高く挙げ、満面の喜びの表情（瞳キラキラ、口を開けて「えいっ」と笑う）。髪と裾はふわっと上がる。王冠の宝石が小さくキラリ。背景は純マゼンタ #FF00FF。右向き、やや正面。
※登場ポーズが必要な場合：同じ元気ポーズで、足元から上へ淡い光の粒が立ち上り「実体化した直後」の雰囲気を足す。
【English】Same algo, same style/colors, a cheerful victory pose: one (or both) hand raised high, a beaming joyful expression (sparkling eyes, open happy mouth), hair and hem fluffed upward, a tiny sparkle on the crown gem. Background pure flat magenta #FF00FF. Facing right, slightly toward viewer.
(For the "appear" pose: same cheerful pose, add faint rising light particles from below for a just-materialized feel.)
```

---

## 5. 敵キャラ デザイン & ラインナップ

### 5-1. 統一コンセプト：「ノイズが具現化 → 撃つと正しい形に戻って浄化」
敵は世界を記述する正しい式・文字・記号が黒インク／グリッチに侵食された姿。光のインク弾を受けると崩れが解け、一瞬"本来の正しい形"に戻ってから光の花びらになって消える。

**見た目で表現する4つの言語:**

| 表現要素 | 具体 | 役割 |
|---|---|---|
| 崩れた文字・記号・データ片 | 反転した数字、欠けた括弧 `{ ]`、ノイズ化した楽譜・五線、化けた本のページ | 「世界＝式」を背景演出レベルで匂わせる（ベタな数式表示はしない） |
| 黒インク汚れ | 体表ににじむ黒、滴り、塗りつぶれ。輪郭が黒ににじんで震える | 「汚染」の象徴。敵弾＝黒インクの出所 |
| グリッチ表現 | 1〜2pxの水平ずれ、点滅、半透明のダブり、色収差（赤青のわずかなズレ） | 「壊れている」感。常時微振動 |
| 未完／欠落 | 体の一部が線画だけ・点滅して消える・ワイヤーフレーム化 | 「未完のままでいたい願い」テーマの伏線 |

**馴染ませる方針:** 黒ベタ線は線として使わない（黒は"インク汚れ"の塗りのみ）。敵は「怖い」より「壊れて少し寂しい／不思議」。丸み・ぷにっとしたシルエット・大きめの瞳。浄化後の"正しい形"はかわいくクリーン（白〜薄紫）。配色は第1章「1-2 役割別カラー」を厳守。

### 5-2. 敵ラインナップ（10体・ステージ進行順）

| # | 名前 | 役割 | 見た目 | 攻撃 | サイズ目安 | 主な色 |
|---|---|---|---|---|---|---|
| E1 | **グリフ・モート** (Glyph Mote) | 直進ザコ／リズム | 崩れた小さな文字が黒インクでにじんだ豆粒精霊。目が一つ | 等速直進。たまに単発の温色弾を正面へ | 16×16 | 白＋黒インク、目=オレンジ |
| E2 | **ベント・ノート** (Bent Note) | 曲射・誘導ザコ／避け練習 | 折れた音符♪。尾が黒インクで滴る。五線の破片を背負う | サイン波で蛇行しつつ緩い誘導弾(2〜3way) | 20×20 | オフホワイト＋マゼンタ滴り |
| E3 | **ブラケット・タレット** (Bracket Turret) | 設置型砲台／地形連動 | 地形固定の崩れた括弧 `[ }` の口。中に赤い光核 | 一定間隔で扇状3〜5way、または狙い撃ち単発 | 28×24 | 灰＋紫縁、核=赤 |
| E4 | **インクブロブ・ガード** (Inkblob Guard) | 耐久エリート／小休止 | ぷっくり太ったインク塊スライム。中に"正しい記号"が透ける。表面グリッチ | 低速接近、被弾で黒が剥がれ中の記号が露出。死亡時拡散弾 | 40×40 | 黒インク塊＋内部白記号、目=オレンジ |
| E5 | **ペイジ・シャード** (Page Shard) | 破壊可能オブジェクト／ルート分岐 | 破れて宙に浮く本のページ片。文字化け。攻撃しない | 無攻撃。破壊で花びら(得点)or通路開放 | 32×40 | セピア＋黒インク、エッジ点滅 |
| E6 | **ミラー・セイレーン**（双月のセイレーン） | 中ボス（鏡像ギミック） | 上下対称の水面妖精。下半身が鏡像反転で"二体に見える"。胸に割れた満月 | 本体＋鏡像が同時／交互に弾。反射弾。HP低め | 56×72 | 青〜白＋黒インク、月=ペールイエロー |
| E7 | **コグ・センチネル** (Cog Sentinel) | 後半エリート（機械敵） | 歯車仕掛けの守護機械。半分が錆び＆黒インクでガクつく。胸に欠けた歯車紋 | 回転しレーザー予兆→直線レーザー、設置弾 | 48×56 | ガンメタル灰＋紫錆、コア=オレンジ |
| E8 | **アーカイヴ・ワーム** (Archive Worm) | 中盤の大型誘導 | 本の背表紙が連なる芋虫状。節ごとに化けた書名。先頭に一つ目 | 端から這い、節から順次マゼンタ弾。先頭が誘導 | 各節24×24×6〜8節 | くすんだ赤茶＋黒、目=オレンジ |
| E9 | **レックス・コロナ**（壊れた管理者／月冠） | ラスボス（3フェーズ） | 王冠の巨大な旧守護プログラム。元はalgoと同じ管理者権限。全身が黒インク＆グリッチ侵食、王冠が割れ点滅。背後に壊れた式の輪 | フェーズ別（第6章） | 約120×140 | 白〜銀＋濃い黒、王冠=金、亀裂=赤、紫の残滓 |
| E10 | **Null**（ヌル） | ライバル／algoの鏡像 | algoと同シルエットのチビ3頭身だが黒基調・反転配色。髪=黒〜濃紫、瞳=赤、ローブ=黒〜濃灰、十字が裏返った×／空集合∅。輪郭グリッチ。クール | 高速ダッシュ＋algo技の反転弾幕。"全消去"の収束弾。和解差分あり | algo同等 約48〜64px高 | 黒・濃紫・濃灰、瞳=赤、∅=シアン発光 |

---

## 6. 敵キャラ 生成プロンプト集

> 全敵プロンプトは**第2章「2-2 共通スタイルプロンプト（接頭辞）」を先頭に貼る**こと（以下では `［共通接頭辞］` と略記）。弾を描く時は「2-2」末尾の弾配色ルールも添える。**E9ボス・E10 Null は `char/algo.png` を必ず参照添付**。

### E1. グリフ・モート
```
【JP・基準画像】［共通接頭辞］崩れて文字化けした小さな文字の精霊。本来は世界を記述する正しい一文字だったが、ノイズに侵食され形が崩れた姿。豆粒のような丸いシルエットに、黒インクがにじんで震える輪郭。大きな一つ目（オレンジ色）。体表に反転した記号やグリッチのスキャンラインが1〜2px走る。とても小さくかわいいが少し寂しげ。約16×16ピクセル相当の超ミニサイズで描く。
【EN】［style prefix］A tiny corrupted glyph spirit, originally a single correct letter that wrote the world, now distorted by noise. A small round bean-like silhouette with a black-ink-bleeding, trembling outline. One large eye (orange). Reversed symbols and 1-2px glitch scanlines across its body. Very small and cute but a little lonely. Draw it at roughly 16x16 pixel scale.
【追加ポーズ】同じキャラで、撃破され黒インクが解けて"正しい一文字（白く整った文字）"に戻りながら光の花びらに変わる瞬間 / the same character at the moment of purification: black ink dissolving, reverting to a clean white correct letter, turning into light petals.
```

### E2. ベント・ノート
```
【JP】［共通接頭辞］折れ曲がった音符（♪）の妖精。本来は美しい旋律だったが侵食され、棒が折れ、尾からマゼンタの黒インクが滴る。背中に壊れた五線譜の破片を背負う。丸い体に小さな目。蛇行して飛ぶ軽やかさを感じるポーズ。オフホワイト＋マゼンタの汚れ。
【EN】［style prefix］A bent musical-note (♪) fairy, once a beautiful melody, now corrupted: its stem is broken and magenta black-ink drips from its tail. It carries broken music-staff shards on its back. Round body, small eyes, a light weaving-flight pose. Off-white with magenta stains.
【追加ポーズ】出現（ふわっと現れる）／誘導弾を放つ瞬間（口・尾からマゼンタの温色弾2〜3発）／浄化（整った音符に戻り花びら化）。
```

### E3. ブラケット・タレット
```
【JP】［共通接頭辞］地形に固定された設置型の砲台。崩れた括弧 [ と } を口のように開閉する形状。内部に赤く光る核。表面に紫の縁取りとグリッチのにじみ。機械でも生物でもない不思議な存在。横向きで右の画面外へ撃つ向き。
【EN】［style prefix］A stationary fixed turret. Shaped like a corrupted bracket [ and } that open and close like a mouth, with a red glowing core inside. Purple-tinted edges and glitch bleeding on its surface. Neither fully machine nor creature. Oriented to fire toward the right side of the screen.
【追加ポーズ】アイドル（口を閉じ核がほの光る）／チャージ（口を開け核が膨張、予兆光）／発射（扇状の赤い温色弾）／浄化（整った括弧 [ ] に戻る）。
```

### E4. インクブロブ・ガード
```
【JP】［共通接頭辞］ぷっくり太ったインクの塊のスライム型エリート敵。半透明の黒インクの内部に、本来の"正しい記号（白く光る等号や括弧）"が閉じ込められて透けて見える。表面はグリッチで小刻みに震え、色収差（赤と青のわずかなズレ）。大きめの一つ目（オレンジ）。重そうでかわいい。
【EN】［style prefix］A plump, chunky ink-blob slime elite enemy. Inside its translucent black ink, a trapped "correct symbol" (a glowing white equals-sign or bracket) is faintly visible. Its surface trembles with glitch and chromatic aberration (slight red/blue offset). A large single orange eye. Heavy-looking and cute.
【追加ポーズ】アイドル／被弾段階1・2（黒インクが剥がれ内部の白い記号が露出していく中間差分を2〜3枚）／撃破拡散（はじけて放射状の温色弾＋花びら）／浄化（中の記号が完全解放されクリーンに）。
```

### E5. ペイジ・シャード
```
【JP】［共通接頭辞］破れて宙に浮く一枚の本のページ片。表面に文字化けした行がびっしり並び、黒インクでところどころ塗りつぶれている。エッジがグリッチで1pxずつ点滅・ずれる。攻撃性はなく、静かに漂う。セピア＋黒インク。
【EN】［style prefix］A single torn page shard of a book, floating in the air. Its surface is filled with rows of garbled text, partially blotted with black ink. Its edges flicker and shift by 1px with glitch. Non-aggressive, drifting quietly. Sepia with black ink.
【追加ポーズ】アイドル漂い／破壊（文字が一瞬"読める正しい一行"になって光の花びらに散る＝浄化）。
```

### E6. ミラー・セイレーン（中ボス）
```
【JP・基準画像】［共通接頭辞］水面の鏡像をモチーフにした中ボスの妖精。上半身は美しい水の妖精、下半身は水面に映った鏡像として上下対称に反転し、まるで二体に見える。胸に割れた満月（ペールイエロー）。体に青〜白の波と黒インクの汚れ。鏡像が一瞬グリッチでずれる。優雅だが壊れて寂しげ。中ボスらしい中型サイズ。
【EN】［style prefix］A mid-boss fairy themed on water-surface reflection. Her upper body is a beautiful water fairy; her lower body is her mirror reflection, vertically symmetric, so she looks like two beings. A cracked full moon (pale yellow) on her chest. Blue-to-white waves and black-ink stains. Her reflection glitch-shifts for a moment. Elegant yet broken and lonely. Mid-boss medium size.
【追加ポーズ】出現（水面から立ち上がる）／アイドル（鏡像揺らぎ）／詠唱（両手で反射弾生成）／パターンA発射（本体＋鏡像が同時に拡散弾）／パターンB（反射弾の収束）／被弾／撃破＝浄化（鏡像と一つに戻り、整った満月の妖精として花びら化）。
```

### E7. コグ・センチネル
```
【JP】［共通接頭辞］歯車仕掛けの守護機械（後半エリート）。半身が錆び、関節に黒インクが固着して動きがガクついている。胸に歯が欠けた歯車の紋章。本来は世界を回す正しい機構だった名残。ガンメタル灰＋紫の錆、コアはオレンジ。重厚だが少し不憫。右を向く。
【EN】［style prefix］A gear-driven guardian machine (late-game elite). Half of it is rusted, with black ink congealed in its joints making its motion stutter. A chipped-gear emblem on its chest, a remnant of once being a correct mechanism that turned the world. Gunmetal gray with purple rust, orange core. Heavy yet a little pitiful. Facing right.
【追加ポーズ】アイドル（歯車ガクつき）／レーザー予兆（コア発光＋警告ライン）／レーザー発射（オレンジの直線ビーム）／設置弾展開／被弾／浄化（錆と黒が落ち、なめらかに回る正しい歯車に戻り花びら化）。
```

### E8. アーカイヴ・ワーム
```
【JP】［共通接頭辞］本の背表紙が連なって出来た芋虫状の長い敵。各節に化けた書名ラベルが貼られ、黒インクでにじんでいる。先頭の節に一つ目（オレンジ）。全体がうねうねと這うように波打つ。くすんだ赤茶＋黒。横長で右へ進む。
【EN】［style prefix］A long caterpillar-like enemy made of a row of connected book spines. Each segment has a garbled title label, bleeding with black ink. The head segment has a single orange eye. The whole body undulates as it crawls. Muddy red-brown with black. Horizontal, moving right.
【追加ポーズ】這い移動（節のうねり2〜4枚でループ）／各節発射（節からマゼンタ弾）／先頭誘導／被弾／浄化（背表紙が整った美しい本の列に戻り、ページがめくれて花びら化）。
```

### E9. レックス・コロナ（ラスボス／3フェーズ）
> **必ず `char/algo.png` を参照添付**し、王冠＝管理者権限の共通モチーフを意識させる。
```
【JP・基準画像】［共通接頭辞］巨大なラスボス「壊れた管理者（旧守護プログラム）」。割れて点滅する王冠（金）をかぶる、元はalgoと同じ管理者権限を持つ気高い存在。全身が濃い黒インクとグリッチに深く侵食され、体の一部がワイヤーフレームや線画だけになって点滅・欠落している。背後に壊れた数式の輪（薄い文字が回る）。白〜銀の体に赤い亀裂、紫の演算子の残滓がワンポイント。荘厳で美しいが深く壊れて切ない。画面の1/3を占める大型。正面〜やや左向きでalgoと対峙。
【EN】［style prefix］A giant final boss, "the broken administrator (former guardian program)." It wears a cracked, flickering crown (gold); it was once a noble being with administrator privileges like algo. Its whole body is deeply corrupted by thick black ink and glitch; parts of its body have become wireframe or line-art only, flickering and missing. Behind it spins a broken ring of faint formulae. White-silver body with red cracks, a single touch of purple operator-residue. Majestic and beautiful but deeply broken and sorrowful. Large, occupying about a third of the screen, facing left to confront algo.
```
**フェーズ別 追加プロンプト（基準画像を参照添付して生成）:**
```
【フェーズ1（通常）JP】同じラスボスの第1形態。王冠は割れているが原型を保つ。落ち着いた佇まいで、直線的な赤い弾を整然と放つ姿。
【EN】Same boss, phase 1: crown cracked but mostly intact, composed posture, firing orderly straight red bullets.

【フェーズ2（怒り）JP】同じラスボスの第2形態。黒インクの侵食が拡大し王冠の亀裂が広がり赤く発光、表情と姿勢が激しくなり、拡散・誘導弾を放つ攻撃的な姿。グリッチ強め。
【EN】Same boss, phase 2 (enraged): ink corruption spreads, crown cracks widen and glow red, aggressive posture, firing spread and homing bullets, stronger glitch.

【フェーズ3（断末魔）JP】同じラスボスの第3形態（最終）。体の半分以上が崩壊し線画とワイヤーフレームになり、王冠が砕け散る寸前。背後の式の輪が暴走して回る。全画面を覆うような巨大な収束弾幕を放つ華のある最後の姿。美しくも消えゆく寂しさ。
【EN】Same boss, phase 3 (final/death-throes): over half its body collapsed into line-art and wireframe, crown about to shatter, the formula ring spinning out of control behind it, unleashing a screen-filling convergent barrage. A spectacular yet fading, sorrowful final form.

【浄化（撃破演出）JP】同じラスボスが浄化される瞬間。黒インクとグリッチが一気に剥がれ落ち、本来の"正しく美しい守護者（白〜薄紫のクリーンな姿）"に一瞬戻り、穏やかな表情で大量の光の花びらになって解けていく。
【EN】The same boss at purification: all black ink and glitch peel away at once, it briefly reverts to its true, correct, beautiful guardian form (clean white-lavender), and dissolves into a vast cloud of light petals with a peaceful expression.
```

### E10. Null（ライバル／algoの鏡像）
> **重要**: `char/algo.png` を必ず参照添付し、「同じシルエット・同じチビ3頭身で色とモチーフを反転」と指示。
```
【JP・基準画像】［共通接頭辞］主人公algoの鏡像であるライバル「Null（ヌル）」。algoと全く同じチビ3頭身のシルエット・体型・髪型・ローブの形だが、配色を完全に反転させる。髪は黒〜濃紫、瞳は赤、ローブは黒〜濃灰、頭の王冠/角の飾りは黒く尖る。algoの胸の紫十字マークに対し、Nullの胸は"消去・空"を表すマーク（裏返った十字や空集合∅、シアン寄りに発光）。輪郭が常にグリッチで1〜2px震える。表情はクールで挑戦的。「全部消してやり直す」を体現する、美しくも危うい鏡像。右向き（algoと対峙する時は左向き差分も）。添付したalgoの画像と同じ作画スタイル・同じ頭身・同じサイズに必ず揃える。
【EN】［style prefix］"Null", the rival and mirror image of the protagonist algo. Exactly the same chibi 3-head-tall silhouette, body, hairstyle, and robe shape as algo, but with a fully inverted palette. Hair black-to-dark-purple, eyes red, robe black-to-dark-gray, the crown/horn ornament black and sharp. Against algo's purple cross on the chest, Null has an "erasure/void" mark (an inverted cross or an empty-set ∅, glowing toward cyan). Its outline constantly trembles with 1-2px glitch. Cool, defiant expression. A beautiful yet dangerous mirror embodying "erase everything and start over." Facing right (also make a left-facing variant for confronting algo). Match the attached algo image exactly in art style, head-to-body ratio, and size.
【追加ポーズ】登場（腕を組む／algoの元気ポーズを反転した気だるげなポーズ）／アイドル浮遊／高速ダッシュ（残像＝黒インク）／弾幕発射（algoの技を反転した収束・消去弾、温色＋シアン縁）／被弾／共闘演出（algoに背を向けて並ぶ和解差分）。
※Nullは"倒して浄化"ではなく和解／離脱で退場するため、撃破＝花びら化は作らず「フェードして去る」差分を作る。
```

---

## 7. 敵キャラ スプレッドシート仕様

### 7-1. 共通ルール
- **背景透過PNG**。各セル内で被写体を中央＋基準ライン統一（地上敵は足元、浮遊敵は重心を揃える）。
- **レイアウト**: 1アニメ＝1行（横方向にフレーム並べ）。複数アニメは行を縦に積む。左→右が時間進行。
- **セルpx**: 各表参照。実寸より上下左右に余白を持たせた正方セル推奨（グリッチ揺れ・エフェクトのはみ出し用に+25%程度）。
- **アニメ語彙（全敵で統一）**: `spawn / idle / move / charge / attack / hit / purify / defeat`（ボスは `intro / phase_break / laser / place / attack_*` 等を追加）。

**撃破＝浄化フレームの作り方（全敵共通の型）:**
1. **崩れ状態**（最後のhitフレーム）
2. **黒インク剥離**: 黒の塗りを2〜3フレームかけて外側へ散らす（粒子化）
3. **正しい形が露出**: 1〜2フレーム、白〜薄紫のクリーンな"本来の姿"を一瞬見せる（カタルシスの山）
4. **花びら化**: 紫の花びら＋光に分解して上方へ散る（2〜3フレーム）
→ 全体4〜8フレーム。雑魚は4、ボスは8〜12でじっくり。

### 7-2. 雑魚（簡素: 3〜5アニメ）

| 敵 | セルpx | アニメ（フレーム数） | 行数 | 総セル目安 |
|---|---|---|---|---|
| E1 グリフ・モート | 24×24 | idle(2), attack(2), purify(4) | 3 | 8 |
| E2 ベント・ノート | 28×28 | spawn(2), move(4 loop), attack(2), purify(4) | 4 | 12 |
| E3 ブラケット・タレット | 36×32 | idle(2), charge(2), attack(3), purify(4) | 4 | 11 |
| E4 インクブロブ・ガード（エリート） | 56×56 | idle(2), hit(3=剥離段階), attack(2), purify(6) | 4 | 13 |
| E5 ペイジ・シャード（オブジェ） | 40×48 | idle(2 漂い), purify(4＝破壊浄化) | 2 | 6 |
| E8 アーカイヴ・ワーム | 各節32×32（先頭/胴/尾の3種パーツ）＋move(4) | crawl(4), seg_attack(2), purify(6) | パーツ分割推奨 | 〜18 |

> ワーム(E8)は長さ可変のため、**セグメント単位のパーツ素材**（先頭・胴・尾）＋うねりmove4枚で構成し、エンジン側で連結する。1枚絵シート化しない。

### 7-3. 中ボス・エリート（中程度: 6〜7アニメ）

| 敵 | セルpx | アニメ（フレーム数） | 行数 |
|---|---|---|---|
| E6 ミラー・セイレーン | 80×96 | spawn(3), idle(4 loop), cast(3), attackA(2), attackB(2), hit(2), purify(8) | 7 |
| E7 コグ・センチネル | 64×72 | idle(4 歯車ガクつき loop), charge(3 レーザー予兆), laser(2), place(2), hit(2), purify(6) | 6 |

### 7-4. ラスボス（フェーズ別シート）
**E9 レックス・コロナ** — セルpx **160×176**（大型・エフェクト余白込み）。フェーズごとに別シート。

| シート | アニメ（フレーム数） |
|---|---|
| `boss_rex_p1` | intro(4), idle(6 loop), attack_straight(3), hit(2), phase_break(4 → P2移行) |
| `boss_rex_p2` | idle(6 loop), attack_spread(3), attack_homing(3), hit(2), phase_break(4 → P3移行) |
| `boss_rex_p3` | idle(6 loop 崩壊振動), attack_barrage(4 全画面寄り), hit(2), purify(12＝最終浄化) |

- **phase_break** は黒インク侵食が一段進む演出を4枚で。第3フェーズidleは体が線画/ワイヤーフレームに点滅する崩壊感をループに織り込む。
- **purify(12)** は浄化の型を最大尺で（剥離2 → 正しい守護者2 → 穏やかな表情1 → 花びら大量化4 → 消失3）。BGM無音・ヒットストップと合わせる見せ場。

### 7-5. ライバル Null
**E10 Null** — セルpx **64×64**（algoと同寸。algoのシート構成とミラー対応させると実装が楽）。

| アニメ（フレーム数） | 備考 |
|---|---|
| idle_float(4 loop) | 常時浮遊。輪郭グリッチ揺れ込み |
| dash(3) | 黒インク残像 |
| cast(3), attack(3) | algo技の反転 |
| hit(2) | |
| **leave(6 フェード退場)** | 浄化＝花びらは作らない。和解/離脱でフェードして去る固有差分 |
| (任意) coop(2) | algoと並ぶ共闘演出差分 |

### 7-6. 敵の量産・色替えのコツ
- まず**ニュートラルな白系ベース**で完成させ、ステージ色（W1パステル/W2青/W3灰紫…）は**画像生成ではなくドット編集（Aseprite）でパレットスワップ**する方が速く確実。
- ChatGPTに頼む場合は「同じ敵で、ステージ○○用に色相を青寄りに」と**1パラメータだけ変える**。
- パーツ替え（記号違い・ラベル違い）は「同じ体・同じポーズで、中の崩れた記号だけ別の文字に」と差分指定。

---

## 8. 命名規則 & ファイル構成まとめ

### 8-1. 共通の手作業仕上げ手順（ChatGPT出力 → ゲーム用アトラス）
1. **背景除去（キーイング）**: 純色背景を画像編集ソフト（Photoshop/Aseprite/GIMP/Krita）で色域選択して透過。縁の背景色にじみ（フリンジ）は Defringe で除去。
2. **ドット化・縮小・減色**: ニアレストネイバーで目標サイズへ縮小 → 共通パレットでインデックスカラー（16〜32色程度）へ減色（同一アニメは全フレーム同一パレット共有）→ 1px輪郭＋暗い同系色アウトラインに整える（黒ベタ線が出ていれば手修正）。
3. **フレーム整列（最重要）**: 各フレームを規定セル（algo標準64×64／ボム96×96／各敵は第7章）の透明キャンバスに配置。**基準点を全フレームで同一座標に**（algoは紫十字 x=32,y=34／敵は重心・足元）。Asepriteのオニオンスキンでブレ確認。
4. **連番書き出し**: 命名規則（下記）で各フレームを書き出し。
5. **アトラス化**: 行＝アニメ／列＝フレームのグリッドに整列配置してシートPNGを作成。TexturePacker / Aseprite シートエクスポート / Godot `SpriteFrames` でアトラスJSONを生成。
6. **実機チェック**: 384×216に整数倍表示し、(a)ループが滑らか (b)基準点が全アニメでブレない (c)弾幕背景上で視認性が保てる（白〜薄紫が埋もれない、敵弾が自機弾と混同しない）を確認。問題があれば減色・コントラスト調整か該当フレーム再生成。

### 8-2. 命名規則

**algo:**
- シート: `algo_sheet_main.png`（384×768）／ `algo_sheet_bomb.png`（768×96）／倍率派生 `algo_sheet_main@2x.png`
- 個別フレーム: `algo_{anim}_{frame:02d}.png`（例: `algo_idle_float_00.png`, `algo_shot_01.png`, `algo_bomb_03.png`）
- メタ: `algo_sheet_main.json`（各アニメの `frames` / `fps` / `loop` / `pivot` / `hitbox(x,y)` を記述）
- Godot `SpriteFrames` のアニメ名は `anim` key と一致（`idle_float`, `move_up`…）

**敵:**
- 個別フレーム: `enemy_<id>_<name>_<anim>_<NN>.png`（例: `enemy_e1_glyphmote_idle_01.png`）
- シート行ラベル: `<anim>`（`spawn / idle / move / charge / attack / hit / purify / defeat`）
- ボスはフェーズ別シート: `boss_rex_p1` / `boss_rex_p2` / `boss_rex_p3`
- `<id>` は E1〜E10（小文字 e1…e10）で統一。

### 8-3. 推奨ディレクトリ構成
```
algo_shoot/char/
  algo.png                      … 基準イラスト（既存）
  refs/                         … ChatGPTで確定した各キャラ基準画像
    algo_ref_neutral.png
    enemy_e1_ref.png … enemy_e10_ref.png
    boss_rex_ref.png / null_ref.png
  frames/                       … 手作業整列後の連番フレーム
    algo_{anim}_{frame}.png
    enemy_<id>_<anim>_<NN>.png
  sheets/                       … 最終アトラス
    algo_sheet_main.png / algo_sheet_bomb.png / algo_sheet_main.json
    enemy_<id>_sheet.png / boss_rex_p1.png … p3.png / null_sheet.png
```

### 8-4. 関連ファイルパス
- 基準イラスト: `char/algo.png`（全敵プロンプトに参照添付、特に E9 レックス・コロナ・E10 Null で必須）
- 企画書: `docs/GAME_DESIGN.md`（世界観・敵カテゴリ/ボス方針・アートディレクションの根拠）
- 想定生成物: 上記 `8-3 推奨ディレクトリ構成` を参照。

---

### 実装優先度サマリ
1. **algo フェーズ1**（`idle_float` / `move_up` / `move_down` / `shot` / `hit`）
2. **雑魚 E1〜E3**（基本弾幕の的）
3. **エリート E4・オブジェ E5**、**algo の `bomb` / `win`**
4. **中ボス E6・E7**、**E8 ワーム**
5. **ラスボス E9（3フェーズ）**、**ライバル E10 Null**
