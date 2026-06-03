# フェーズ0：プレイヤー（algo ＋ 仲間 Echo）— ChatGPT 生成プロンプト資料

> このファイル単体で着手できます。最優先で作るのは algo の **`idle_float` / `move_up` / `move_down` / `shot` / `hit`**。
> 大元の仕様: [../CHARACTER_ASSETS.md](../CHARACTER_ASSETS.md) ／ 世界観: [../GAME_DESIGN.md](../GAME_DESIGN.md)

---

## ★ 共通スタイルプロンプト（接頭辞）— 全生成の先頭に毎回貼る

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
You are a character artist specializing in HD-2D-leaning hybrid pixel art. Strictly follow this style.
STYLE: Hand-crafted clean pixel art, HD-2D-leaning, subtle glow and soft shading allowed. No 3D, no photorealism. NO solid black outlines — use a 1px outline with a slightly darker shade of the same hue. Minimal anti-aliasing, crisp pixels. Chibi proportions, about 3 heads tall, cute, big head.
COLOR: Base white/off-white/pale-lavender/pale-purple. Accent signature purple #8A6FD6. Medium saturation, bright and clean.
OUTPUT: Background a single FLAT solid color (pure magenta #FF00FF or cyan/green), no shadows/gradients/patterns/checkerboard, no edge fringing. ONE subject, centered, full body in frame. Character faces RIGHT.
Now draw the following character:
```

---

## このフェーズで作るアセット一覧（チェックリスト）

- [ ] **algo 基準画像**（標準立ち＋パレット）★最初に必ず確定
- [ ] algo `idle_float`（待機浮遊）★優先
- [ ] algo `move_up`（上バンク）★優先
- [ ] algo `move_down`（下バンク）★優先
- [ ] algo `shot`（メインショット）★優先
- [ ] algo `hit`（被弾）★優先
- [ ] algo `weave_shot`（紡ぎ弾）
- [ ] algo `focus_idle`（低速移動）/ `dash` / `graze` / `overload`
- [ ] algo `bomb`（魔法陣・解放）※96×96特大セル
- [ ] algo `appear`（登場）/ `win`（勝利）/ `death`（死亡）
- [ ] **仲間 Echo 基準画像**＋基本ポーズ（option機）

---

## A. algo キャラ定義ブロック（接頭辞の後ろに貼る）

**日本語版:**
```
【キャラ「algo（アルゴ）」】
- 白〜薄紫のふわっとした髪、頭に小さな王冠／角のような飾り。
- 白いローブ風ワンピース、胸元に黒い装飾と紫の十字（クロス）マーク。
- 大きな紫の瞳。かわいく幻想的な魔法使い／精霊。常時ふわふわ浮遊。
- アクセントのシグネチャ・パープル #8A6FD6 は、紫十字・発光・王冠の宝石に使う。
```
**英語版:**
```
CHARACTER "algo": Soft fluffy white-to-pale-lavender hair, a tiny crown / small horn-like ornament. White robe-like one-piece dress, a black chest ornament with a purple cross (plus) mark on the chest. Big purple eyes, a cute fantastical little mage/spirit, always gently floating. Apply signature purple #8A6FD6 on the purple cross, glows, and crown gem.
```

---

## B. algo 基準キャラ画像（★最初にこれを確定）

> 参照なしで生成し、最も設定に合う1枚を「基準画像」として保存（`char/refs/algo_ref_neutral.png`）。以後の全ポーズで参照添付する。

**日本語版:**
```
（共通接頭辞 ＋ A のalgoキャラ定義 を先頭に貼る）

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
TASK: CHARACTER REFERENCE SHEET. Draw algo in a clear neutral standing pose as the master reference. Pose: natural standing, slightly facing right, gently floating, arms relaxed. Expression: neutral, softly smiling. Lighting: flat and even, minimal shadow. Composition: large, centered, full body with margin. On the right side add a small COLOR PALETTE strip as square chips: hair (white-to-lavender), skin, robe white, robe shadow (pale purple), black chest ornament, purple cross #8A6FD6, eye purple, crown gold/gem. Background: pure flat magenta #FF00FF only.
```

---

## C. algo 主要ポーズ プロンプト
> 各依頼は「共通接頭辞 ＋ Bで確定した基準画像を添付 ＋ 下記ポーズ文」で投げる。**1回の依頼でポーズ1点だけ**変える。

**C-1 アイドル浮遊（idle_float）★**
```
【日本語】添付の基準画像のキャラ「algo」をそのままの絵柄・色で使い、待機の浮遊ポーズを描いてください。ふわっと宙に浮き、後ろ髪とローブ裾が下方向へ自然に垂れて軽く揺れる。手は体の横で力を抜く。表情は通常で穏やか。紫十字マークは胸の中央にはっきり見えるように。背景は純マゼンタ #FF00FF のベタ1色。右向き。
【English】Using the attached reference of "algo" with the exact same art style and colors, draw a calm idle floating pose. She hovers gently; back hair and robe hem drape downward and sway lightly; arms relaxed at her sides; neutral soft expression. Keep the purple cross clearly visible at the chest center. Background pure flat magenta #FF00FF. Facing right.
```
**C-2 上バンク（move_up）★**
```
【日本語】添付のalgoを同じ絵柄・色で、上方向へ上昇する移動ポーズに。体を上向きに約15〜20度傾け、髪とローブ裾が後ろ下方向へ風になびく。やや集中した表情。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, upward-moving banking pose: body tilted up ~15-20°, hair and robe hem streaming back-and-down as if in wind, slightly focused expression. Background pure flat magenta #FF00FF. Facing right.
```
**C-3 下バンク（move_down）★**
```
【日本語】添付のalgoを同じ絵柄・色で、下方向へ降下する移動ポーズに。体を下向きに約15〜20度傾け、髪とローブ裾が後ろ上方向へめくれてなびく。やや集中した表情。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, downward-moving banking pose: body tilted down ~15-20°, hair and robe hem flowing back-and-up, slightly focused expression. Background pure flat magenta #FF00FF. Facing right.
```
**C-4 ショット（shot）★**
```
【日本語】添付のalgoを同じ絵柄・色で、前方（右）へ魔法を放つショット詠唱ポーズに。前の手を右へ突き出し、手のひらの先に白〜水色の小さな発光リング。集中した表情、瞳に光点。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, casting a forward magic shot: front hand thrust right, a small white-to-cyan glowing ring at the palm, focused expression with a light glint in the eyes. Background pure flat magenta #FF00FF. Facing right.
```
**C-5 被弾（hit）★**
```
【日本語】添付のalgoを同じ絵柄・色で、敵弾に被弾してのけぞる瞬間に。体が後ろ（左）へのけぞり、髪とローブが衝撃で前方に乱れる。驚き・苦痛の表情（瞳を細め、口は「っ」）。algoのまわりだけ淡い白フラッシュのにじみ。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, the moment she is hit and recoils: body flinching backward (left), hair and robe disturbed forward by impact, startled/pained expression (squinted eyes, "!" mouth), a faint white flash bloom only around her. Background pure flat magenta #FF00FF. Facing right.
```
**C-6 紡ぎ弾（weave_shot）**
```
【日本語】添付のalgoを同じ絵柄・色で、ゆっくり弧を描いて紡ぎ弾を放つ詠唱ポーズに。手を弧を描くように動かし、手先に紫 #8A6FD6 寄りの光の軌跡。落ち着いた集中表情。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, casting the slow "weaving shot": hand tracing an arc, a purple #8A6FD6 light trail at the fingertips, calm focused expression. Background pure flat magenta #FF00FF. Facing right.
```
**C-7 ボム（bomb）※96×96特大セル**
```
【日本語】添付のalgoを同じ絵柄・色で、必殺技「魔法陣・解放」の発動ポーズに。両手を左右に開いた解放ポーズ。頭の小さな王冠が眩しく輝き（白〜金のハイライト）、胸の紫十字 #8A6FD6 から放射状の光が広がる。足元に紫の魔法陣の輪。全身を紫と白の光が包む。髪と裾はエネルギーで上向きに浮き上がる。背景は純マゼンタ #FF00FF（光は背景に溶け込ませない）。右向き、やや正面。
【English】Same algo, same style/colors, activating her bomb "Spell-Circle Release": arms spread open, the tiny crown glowing brightly (white-to-gold), radiant light bursting from the purple cross #8A6FD6, a purple magic circle ring at her feet, her whole body wrapped in purple-and-white glow, hair and hem lifted upward by the energy. Background pure flat magenta #FF00FF (do not blend the glow into the background). Facing right, slightly toward viewer.
```
**C-8 勝利（win）／登場（appear）**
```
【日本語】添付のalgoを同じ絵柄・色で、勝利の元気ポーズに。片手（または両手）を高く挙げ、満面の喜びの表情（瞳キラキラ、口を開けて「えいっ」と笑う）。髪と裾はふわっと上がる。王冠の宝石が小さくキラリ。背景は純マゼンタ #FF00FF。右向き、やや正面。
※登場（appear）が必要な場合：同じ元気ポーズで、足元から上へ淡い光の粒が立ち上り「実体化した直後」の雰囲気を足す。
【English】Same algo, same style/colors, a cheerful victory pose: one or both hands raised high, beaming joyful expression (sparkling eyes, open happy mouth), hair and hem fluffed upward, a tiny sparkle on the crown gem. Background pure flat magenta #FF00FF. Facing right, slightly toward viewer. (For "appear": same pose, add faint rising light particles from below.)
```
**C-9 死亡（death）**
```
【日本語】添付のalgoを同じ絵柄・色で、残機を失う瞬間に。力なく体が傾き、全身が淡い光の粒に解けて上方へ霧散し始める。表情は閉じた目で穏やか（痛々しくしすぎない）。背景は純マゼンタ #FF00FF。右向き。
【English】Same algo, same style/colors, the moment she loses a life: body tilting limply, dissolving into faint light particles drifting upward. Expression peaceful with closed eyes (not too painful). Background pure flat magenta #FF00FF. Facing right.
```

> `focus_idle`（低速時の集中待機・判定縮小演出）/ `dash`（短距離高速＋残像）/ `graze`（弾を魔力に変換する閃き）/ `overload`（リフレイン満タンの発光）は、C-1（アイドル）を基準に「表情をキリッと／後方に残像1枚／全身に薄い紫オーラ」など差分指示で生成する。

---

## D. 仲間 Echo（エコー）— option機 / ナビ妖精

過去の演算ログから生まれた小さな妖精。algoに付き従う **オプション機（サブウェポン）** 兼 物語の語り手。algoより一回り小さい。

**基準画像プロンプト:**
```
【日本語】（共通接頭辞を先頭に）algoの相棒の小さな妖精「Echo（エコー）」。algoより一回り小さい、手のひらサイズの光の妖精。半透明の白〜薄紫の体に、淡く光る小さな羽（または光の輪）。中心にシグネチャ・パープル #8A6FD6 の小さな菱形のコア（演算ログの結晶）。表情は丸い目の点でシンプルにかわいい。ふわふわ浮く。背景は純マゼンタ #FF00FF。右向き。
【English】(paste common prefix first) "Echo", algo's small companion fairy — a palm-sized light sprite, a bit smaller than algo. Translucent white-to-pale-lavender body with faintly glowing tiny wings (or a light ring), and a small diamond-shaped core in signature purple #8A6FD6 at its center (a crystal of computation logs). Simple cute dot eyes. Gently floating. Background pure flat magenta #FF00FF. Facing right.
【追加ポーズ】アイドル浮遊(2〜4Fループ) / 発射補助（algoと同じ向きに小さな白〜水色弾を撃つ）/ 喜び（イベント用にぴょこっと跳ねる）。
```

---

## E. このフェーズのスプレッドシート仕様（要点）
- **algo 標準セル 64×64px**（透明背景・余白込み）。**ボムのみ 96×96px の別シート**。
- ピボット＝セル中心 (32,32)。**当たり判定点＝胸の紫十字を全フレーム x=32,y=34 ±1px に固定**（これがゲームの被弾座標）。
- 標準シート = 6列×12行×64px = **384×768px**（行＝アニメ／列＝フレーム）。ボムシート = 8列×96px = **768×96px**。
- **Echo** は小型のため 32×32px セルでよい。
- 全フレーム右向き固定（左はコード水平反転）。
- 命名・アトラス化・仕上げ手順 → [../CHARACTER_ASSETS.md 第8章](../CHARACTER_ASSETS.md#8-命名規則--ファイル構成まとめ)。
  - 例: `algo_idle_float_00.png`, `algo_bomb_03.png`, `echo_idle_00.png`
