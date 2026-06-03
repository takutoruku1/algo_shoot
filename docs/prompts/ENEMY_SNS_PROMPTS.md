# 敵（SNS浄化コンセプト）— ChatGPT 画像生成プロンプト資料

> 新コンセプト「SNSの悪意で悪魔化した人々を algo が浄化する」の敵アセットを、ChatGPT（GPT-4o 画像生成）で作るための一冊完結資料です。
> このファイル単体で着手できます。実作業者はこれ1枚を ChatGPT に投げて敵画像を作り始められます。
>
> - 作画スタイル・技術仕様・仕上げ手順の大元: [../CHARACTER_ASSETS.md](../CHARACTER_ASSETS.md)
> - 新コンセプト本体（特に §3 三層方針 / §5 コアメカ / §6 敵デザイン）: [../CONCEPT_V2.md](../CONCEPT_V2.md)
> - 主人公の基準イラスト（絵柄合わせ用に参照添付する）: `../../char/algo.png`

---

## 0. この資料の使い方

### 0-1. 新コンセプトでの敵の構造（最重要・コードと一致させる）
旧版（CHARACTER_ASSETS.md）の「敵＝崩れた記号／撃つと消える」は廃止。新メカでは：

- 敵 ＝ **不滅の人間の本体（消えない）** ＋ その周囲を旋回する **黒い吹き出しパネル（暴言）数枚**。
- algo の光弾でパネルを1枚ずつ剥がす → 全部剥がすと本体が **改心して笑顔の味方に変わる**（QueueFree しない・画面に残る）。
- コード対応: `Enemy`（本体）＝ **(A)浄化前 / (B)浄化後** の2状態スプライト、`Panel`（旋回する吹き出し）＝ **(C)黒い吹き出しパネル**。

したがって **各敵につき最低3種**のアセットが要る：

| 種別 | 内容 | コード対応 |
|---|---|---|
| **(A) 浄化前の人** | 寒色・うつむき・とげとげしい（ただし怖すぎない・かわいい範囲） | `Enemy` 通常状態 |
| **(B) 浄化後の同一人物** | 暖色・顔を上げた笑顔・優しい（花/光のハート）。**同一人物と分かること** | `Enemy` 改心状態 |
| **(C) 黒い吹き出しパネル** | その敵が旋回させる暴言の吹き出し。黒インク／とげ／「・・・」「は？」程度の抽象表現 | `Panel`（弾源＋盾） |

### 0-2. 重いテーマの優しい扱い（三層方針・全プロンプト厳守）
CONCEPT_V2 §3 を厳守。各プロンプトに織り込み済みだが、改変時も必ず守ること：

1. **罵倒語そのものを出さない。** 暴言は黒い記号・形（黒ハート／黒い渦／ドクロ吹き出し／棘の輪郭／「・・・」）でのみ抽象表現。文字を入れるなら「は？」「で？」程度の軽い短文まで。**実在の中傷文は絶対に書かない。**
2. **やさしい言葉は浄化後 (B) だけ。**「ありがとう」「だいじょうぶ」「きみは悪くないよ」等。必ず前向きに締める。
3. **着地は"倒した"でなく"助けた"。** 敵は消えず、笑顔の味方／背景の花として残す。誰も死なない非致死。だから (A)→(B) は「破壊」でなく「同一人物が表情と色を取り戻す」演出にする。

### 0-3. 作業着手の順序（おすすめ）
1. 下の **★共通スタイル接頭辞** を理解（全生成の先頭に毎回貼る）。
2. 「キー背景の色ルール」を確認（人物と黒吹き出しで背景色を変える）。
3. 各敵で **(A)基準画像を確定 → 参照添付して (B)浄化後 → (C)パネル** の順に1枚ずつ生成。
4. 仕上げ・透過・縮小・整列・アトラス化・命名は [../CHARACTER_ASSETS.md 第8章](../CHARACTER_ASSETS.md) に従う。

---

## ★ 共通スタイルプロンプト（接頭辞）— 全生成の先頭に毎回貼る

> CHARACTER_ASSETS.md §2-2 準拠。今回は「人＋黒い吹き出し」用に最適化（チビ頭身の人間／黒吹き出しは"インク汚れの塗り"として黒を使うが、輪郭線は黒ベタ禁止）。
> **絵柄を algo と揃えるため、毎回 `../../char/algo.png` を参照添付**し「添付の絵柄・頭身・線の処理に合わせて」と書く。

**日本語版:**
```
あなたはHD-2D寄りハイブリッド・ピクセルアート（ドット絵）のキャラクターアーティストです。
添付の参照画像（algo）と同じ作画スタイル・同じチビ3頭身・同じ線の処理に必ず揃えてください。
以下のスタイルを厳守してください。

【スタイル】
- 手描きのクリーンなピクセルアート（ドット絵）。HD-2D寄りで、わずかな発光とソフトな陰影を許容。3Dレンダリング・写実は禁止。
- 黒のベタ塗り輪郭線は使わない。1pxの輪郭＋やや暗い同系色のアウトラインで縁取る。
  （例外：黒は「暴言の吹き出しのインク汚れ＝塗り」としてのみ使用可。キャラの輪郭線として黒を使わない。）
- アンチエイリアスは最小限、ピクセルははっきりと。
- 人物はチビ3頭身（頭が大きくかわいい）。表示想定はとても小さいスプライト（内部解像度384×216の世界）。

【カラー（役割で混同させない）】
- 浄化前の人＝寒色（青・青緑・灰青）でうつむき気味、彩度低め。
- 浄化後の同一人物＝暖色（ピンク・橙・クリーム）で顔を上げた笑顔、彩度を少し上げる。
- 黒い吹き出しパネル＝黒インクの塗り＋温色（赤〜マゼンタ）の縁取り発光。白・水色・黄金は弾色に使わない。
- アクセントのシグネチャ・パープル #8A6FD6 は使いすぎない（自機色と競合）。ワンポイントのみ。

【出力（後で透過するため）】
- 背景は完全な単色のベタ1色で、影・グラデ・模様・市松模様を一切入れない。キャラ／吹き出しの縁に背景色がにじまないように。
- 被写体は1体（または吹き出し1枚）のみ、画面中央、全身が切れずに収まるように。
- 人物の向きは右向き（画面の右を向く）。

以下を描いてください:
```

**英語版:**
```
You are a character artist specializing in HD-2D-leaning hybrid pixel art. Match the attached reference (algo) exactly in art style, chibi 3-head-tall proportions, and line treatment. Strictly follow this style.
STYLE: Hand-crafted clean pixel art, HD-2D-leaning, subtle glow and soft shading allowed. No 3D, no photorealism. NO solid black outlines for the character — use a 1px outline with a slightly darker shade of the same hue. (Exception: black may be used only as the INK FILL of the hateful speech bubble, never as a character outline.) Minimal anti-aliasing, crisp pixels. People are chibi, about 3 heads tall, cute big head; intended display size is very small (a 384x216 world).
COLOR (never mix roles): pre-purification person = COOL colors (blue, teal, gray-blue), head down, low saturation. post-purification SAME person = WARM colors (pink, orange, cream), head up, smiling, slightly higher saturation. black speech-bubble panel = black ink fill + warm (red-to-magenta) glowing rim; never use white/cyan/gold for the panel. Use signature purple #8A6FD6 only as a tiny accent (it competes with the player color).
OUTPUT (for later keying): background must be a single FLAT solid color, no shadows/gradients/patterns/checkerboard, no edge fringing. ONE subject (or ONE bubble) only, centered, fully in frame. People face RIGHT.
Now draw:
```

### ★ キー背景の色ルール（被写体と背景を必ず分離）
ChatGPT は透過を安定して守れないため、**純色ベタ背景で出力 → 後工程でキー抜き**する。被写体の主要色と被らない補色を背景に使う。

| アセット | 主要色 | **指定するキー背景色** |
|---|---|---|
| (A) 浄化前の人（寒色：青/青緑） | 青・青緑 | **マゼンタ #FF00FF**（補色） |
| (B) 浄化後の人（暖色：ピンク/橙） | ピンク・橙 | **緑 #00FF00**（または青緑シアン #00FFFF） |
| (C) 黒い吹き出しパネル（黒インク） | 黒＋赤マゼンタ縁 | **緑 #00FF00**（黒が緑に映えて抜きやすい。マゼンタ縁とも被らない） |

> ボス等で暖色×緑が紛らわしい場合のみ、背景をマゼンタに切替（被写体にピンクが多い時は緑、緑が多い時はマゼンタ、を都度判断）。

---

## このフェーズで作るアセット一覧（チェックリスト）

各敵 (A)浄化前 / (B)浄化後 / (C)黒吹き出しパネル の3点が基本。ボスはフェーズ・追加吹き出しあり。

- [ ] **1. アンチくん**：(A)pre / (B)post / (C)panel ＝3点
- [ ] **2. エンジョー（中ボス）**：(A)pre / (B)post / (C)panel ＋ 子分=同調アカ (A/B/C 簡易) ＝6点
- [ ] **3. スパムロボ（群れ）**：(A)pre / (B)post / (C)panel ＝3点
- [ ] **4. かげぐち（潜伏）**：(A)pre半透明 / (B)post / (C)panel ＝3点
- [ ] **5. ボス「バズの主」**：(A)pre P1 / (A)pre P2怒り / (B)post素の人＋花の樹 / (C)引用パネル大 / (C)引用パネル小 ＝5点

---

## 1. アンチくん（直進ザコ）

> CONCEPT_V2 §6：うつむいた人。黒い「↓低評価」吹き出しを3枚旋回。浄化後→にっこり「ありがとう」、低評価がピンクのいいね♡に反転。

### (A) 浄化前
```
【JP】［共通接頭辞］悪魔化した直進ザコ「アンチくん」の浄化前。ごく普通の小柄な人（チビ3頭身）が、SNSの悪意に当てられて少し悪魔化してしまった姿。うつむいて目を伏せ、肩を丸めて元気がない。配色は寒色（くすんだ青・青緑のパーカー、灰青の肌影）、彩度低め。怖いというより「疲れて少し寂しい」雰囲気。頭の上にごく小さな黒いもやがひとつ。手は持たない（吹き出しは別アセット）。右向き。背景は純マゼンタ #FF00FF のベタ1色。
【EN】［style prefix］"Anti-kun", a straight-moving small fry, BEFORE purification. An ordinary small person (chibi 3 heads), slightly devil-fied by SNS malice. Head down, eyes lowered, shoulders hunched, looking drained. Cool palette (dull blue / teal hoodie, gray-blue skin shadow), low saturation. Not scary, more "tired and a little lonely." One tiny black wisp of haze above the head. No held items. Facing right. Background pure flat magenta #FF00FF.
```

### (B) 浄化後（同一人物）
```
【JP】［共通接頭辞］「アンチくん」の浄化後。※添付の (A) 浄化前を参照し、同じ顔・同じ髪型・同じ服の形の同一人物だと分かるようにする。違いは色と表情だけ：顔を上げてにっこり笑い、頬に赤み。配色を暖色へ反転（あたたかいクリーム〜淡いピンクのパーカー、健康的な肌色）、彩度を少し上げる。頭上の黒いもやは消え、代わりに小さなピンクのハート♡が1つふわっと浮く。胸元に小さな白い吹き出しで「ありがとう」。右向き、やや正面。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Anti-kun" AFTER purification. (Attach (A) and keep the SAME face, hairstyle, and clothing shape — clearly the same person.) Only color and expression change: head lifted, smiling brightly, blush on cheeks. Palette flips to WARM (cream-to-pale-pink hoodie, healthy skin tone), slightly higher saturation. The black haze is gone, replaced by one small pink heart ♡ floating gently. A small white speech bubble at the chest reads "ありがとう" (thank you). Facing right, slightly toward viewer. Background pure flat green #00FF00.
```

### (C) 黒い吹き出しパネル
```
【JP】［共通接頭辞］アンチくんが旋回させる「黒い暴言の吹き出しパネル」を1枚だけ。角丸の吹き出しだが、ふちが棘でとげとげしく、全体が黒インクで塗りつぶされている。中央に低評価を表す白い下向き矢印「↓」を1つ（罵倒語は書かない）。ふちに赤〜マゼンタの細い発光リム。少しグリッチでふるえる。吹き出し1枚のみ、画面中央。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］A single black hateful-speech bubble panel that Anti-kun orbits. A rounded speech bubble but with a spiky, thorny rim, filled with black ink. In the center, one white downward arrow "↓" meaning a downvote (NO insult text). Thin red-to-magenta glowing rim. Slightly glitch-trembling. Only ONE bubble, centered. Background pure flat green #00FF00.
【浄化反転差分 / purified flip variant】同じ吹き出しの形で、色をピンクに反転し中央を白いハート♡に変えた「いいね」版（浄化エフェクト用）。Same bubble shape, flipped to pink with a white heart ♡ in the center — a "like" version for the purification effect.
```

**アセット仕様（要点）**
| 種別 | ファイル名案 | セルpx目安 | 必要枚数 |
|---|---|---|---|
| (A) 本体・浄化前 | `enemy_anti_pre.png` | 32×40 | 1（＋任意で移動2F） |
| (B) 本体・浄化後 | `enemy_anti_post.png` | 32×40 | 1 |
| (C) 旋回パネル | `panel_anti.png` | 20×20 | 1（コード側で3枚旋回。＋反転♡差分1） |

---

## 2. エンジョー（炎上型・中ボス）

> CONCEPT_V2 §6：頭から黒い炎、6枚の渦巻き吹き出しを高速旋回。剥がすほど鎮火 → 浄化で炎が桜の花びらに変わり舞いながら退場。子分（同調アカ）を従え、本体浄化で子分も連鎖浄化。

### (A) 浄化前
```
【JP】［共通接頭辞］炎上型の中ボス「エンジョー」の浄化前。怒りで悪魔化した中型の人（チビ頭身だが他のザコより一回り大きい）。頭の上から黒い炎がめらめら立ち上る（黒インクの塊が炎の形）。眉を吊り上げ口をへの字に、腕を組んで威圧的だが、よく見ると目元に疲れと寂しさ。配色は寒色＋黒（濃紺〜青黒の服、灰青の肌、黒い炎）。彩度低め、怖すぎず「燃え尽きそうな人」。右向き、やや正面。背景は純マゼンタ #FF00FF のベタ1色。
【EN】［style prefix］"Enjō", a flame-war mid-boss, BEFORE purification. A medium person (chibi but a size larger than small fry), devil-fied by anger. Black flames rise from the top of the head (a mass of black ink shaped like fire). Eyebrows raised, mouth in a frown, arms crossed, intimidating — but on closer look, tired and lonely around the eyes. Cool palette + black (navy-to-blue-black clothes, gray-blue skin, black flames). Low saturation, not too scary, a "burning-out person." Facing right, slightly toward viewer. Background pure flat magenta #FF00FF.
```

### (B) 浄化後（同一人物）
```
【JP】［共通接頭辞］「エンジョー」の浄化後。※添付の (A) を参照し同一人物（同じ顔・服の形）。頭の黒い炎が桜の花びらに変わってふわふわ舞う。眉が下がり、ほっとした穏やかな笑顔、頬に赤み。腕組みを解いて手を軽く広げる。配色を暖色へ（桜ピンク〜クリームの服、健康的な肌）、彩度アップ。胸元の白い吹き出しに「言い過ぎた、ごめんね」。全体に淡い光。右向き、やや正面。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Enjō" AFTER purification. (Attach (A); same person — same face and clothing shape.) The black flames have turned into cherry-blossom petals drifting gently. Eyebrows relaxed, a relieved calm smile, blush on cheeks. Arms uncrossed, opening slightly. Palette flips WARM (sakura-pink-to-cream clothes, healthy skin), higher saturation. A white speech bubble at the chest reads "言い過ぎた、ごめんね" (I went too far, sorry). Soft overall glow. Facing right, slightly toward viewer. Background pure flat green #00FF00.
```

### (C) 黒い吹き出しパネル
```
【JP】［共通接頭辞］エンジョーが高速旋回させる「黒い渦巻きの暴言吹き出し」を1枚。角丸の吹き出しの中身が黒い炎の渦（スパイラル）で塗りつぶされ、ふちは激しい棘。中央に小さく白い「は？」の文字（これ以上の罵倒は書かない）。ふちに赤〜オレンジの強い発光リム（炎上の熱）。強めのグリッチ。吹き出し1枚のみ中央。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］A single black swirling hate-bubble that Enjō spins fast. A rounded bubble filled with a black flame spiral, with a fiercely thorny rim. A small white "は？" (huh?) in the center (no further insults). A strong red-to-orange glowing rim (heat of a flame war). Heavy glitch. Only ONE bubble, centered. Background pure flat green #00FF00.
```

### 子分（同調アカ・ミニ）
```
【JP】［共通接頭辞］エンジョーの子分「同調アカ」。アンチくんをさらに小さく無個性にした、のっぺりした灰青の小さい人影（チビ）。顔は控えめ、黒いもや小1つ。浄化前=灰青/うつむき、浄化後=淡い暖色/にっこり、の2枚を同一人物として。旋回パネルは小さな黒い「・・・」吹き出し1枚。右向き。背景は (A)=マゼンタ #FF00FF / (B)=緑 #00FF00 / パネル=緑 #00FF00。
【EN】［style prefix］Enjō's minion "echo account": a smaller, featureless gray-blue little figure (chibi), like a plainer Anti-kun. Subdued face, one tiny black wisp. Provide pre (gray-blue, head down) and post (pale warm, smiling) as the SAME person. Its orbit panel is one small black "・・・" bubble. Facing right. Backgrounds: (A) magenta #FF00FF / (B) green #00FF00 / panel green #00FF00.
```

**アセット仕様（要点）**
| 種別 | ファイル名案 | セルpx目安 | 必要枚数 |
|---|---|---|---|
| (A) 本体・浄化前 | `enemy_enjo_pre.png` | 56×72 | 1（＋任意 idle/hit） |
| (B) 本体・浄化後 | `enemy_enjo_post.png` | 56×72 | 1（炎→花びらの差分推奨） |
| (C) 旋回パネル | `panel_enjo.png` | 24×24 | 1（コード側で6枚高速旋回） |
| 子分 pre/post/panel | `enemy_echoacc_pre/post.png`, `panel_echoacc.png` | 20×24 / 14×14 | 3 |

---

## 3. スパムロボ（連投型・群れ）

> CONCEPT_V2 §6：同じ黒吹き出しを機械的に量産。浄化で「コピペ」が花束の連投に。改心後は通知ドロップ（残機/ボム）を落とす得点源。
> 「ロボ」だが世界観は"悪魔化した人"。**機械の着ぐるみ／作業着の人**として、人間性を残す（中の人が透ける目）。

### (A) 浄化前
```
【JP】［共通接頭辞］連投型の群れ敵「スパムロボ」の浄化前。四角い段ボール／簡素なロボの着ぐるみを着た小さな人（チビ）。無表情・無機質で、同じ動作を繰り返す疲れた感じ。胸に「COPY」風の小さな表示パネル。配色は寒色（灰・青灰のボディ、冷たいシアンの単眼ランプ）。彩度低め。怖くなく「量産されて疲れた」雰囲気。中の人の目がうっすら見える。右向き。背景は純マゼンタ #FF00FF のベタ1色。
【EN】［style prefix］"Spam-robo", a swarm spammer, BEFORE purification. A small person (chibi) in a boxy cardboard / simple-robot costume. Expressionless, mechanical, tiredly repeating the same motion. A small "COPY"-like display panel on the chest. Cool palette (gray / blue-gray body, cold cyan single-eye lamp), low saturation. Not scary, a "mass-produced and tired" mood. The wearer's eyes faintly visible inside. Facing right. Background pure flat magenta #FF00FF.
```

### (B) 浄化後（同一人物）
```
【JP】［共通接頭辞］「スパムロボ」の浄化後。※添付の (A) を参照し同じ着ぐるみの形の同一の人。着ぐるみのバイザーが開き、中の人がにっこり顔を出す。胸のパネルが「COPY」から小さな花のアイコンに変わる。両手に小さな花束を抱える。配色を暖色へ（クリーム〜橙のボディ、あたたかい単眼ランプ）、彩度アップ。頭上に白い吹き出しで「みんな、げんき？」。右向き、やや正面。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Spam-robo" AFTER purification. (Attach (A); same costume shape, same person.) The costume visor opens and the smiling wearer peeks out. The chest panel changes from "COPY" to a small flower icon. It holds small bouquets in both hands. Palette flips WARM (cream-to-orange body, warm single-eye lamp), higher saturation. A white speech bubble above reads "みんな、げんき？" (everyone, doing okay?). Facing right, slightly toward viewer. Background pure flat green #00FF00.
```

### (C) 黒い吹き出しパネル
```
【JP】［共通接頭辞］スパムロボが機械的に量産する「黒い暴言吹き出し」を1枚。完全に同じ規格の角ばった黒インクの吹き出し（量産品らしくきっちり四角寄り）。ふちは細かい棘。中央に白い小さな「で？」（罵倒語は書かない）。右上に小さな複製マーク（重なった四角）で"連投"を示唆。ふちに赤〜マゼンタの発光リム。軽いグリッチ。1枚のみ中央。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］A single mass-produced black hate-bubble from Spam-robo. A perfectly uniform, squarish black-ink bubble (mass-produced look, rather rectangular). Fine thorny rim. A small white "で？" (so?) in the center (no insults). A small duplicate mark (overlapping squares) at the top-right hints at "repeated posting." Red-to-magenta glowing rim. Light glitch. Only ONE bubble, centered. Background pure flat green #00FF00.
【浄化差分 / purified variant】同じ吹き出し形で色を暖色（クリーム）にし中央を小さな花束に変えた「花束の連投」版。Same shape in warm cream with a tiny bouquet in the center — the "bouquet spam" purified version.
```

**アセット仕様（要点）**
| 種別 | ファイル名案 | セルpx目安 | 必要枚数 |
|---|---|---|---|
| (A) 本体・浄化前 | `enemy_spam_pre.png` | 28×32 | 1（群れ用、同一スプライト多数配置） |
| (B) 本体・浄化後 | `enemy_spam_post.png` | 28×32 | 1（通知ドロップ落下の合図用に手上げ差分推奨） |
| (C) 旋回パネル | `panel_spam.png` | 16×16 | 1（同一パネルを多数量産。＋花束差分1） |

---

## 4. かげぐち（潜伏型）

> CONCEPT_V2 §6：背景に紛れ半透明。光を当てると実体化。浄化で「だいじょうぶだよ」。

### (A) 浄化前（半透明・潜伏）
```
【JP】［共通接頭辞］潜伏型の敵「かげぐち」の浄化前。背景に紛れる半透明の人影（チビ）。フード／マントを目深にかぶり、口元だけ見えてこそこそ陰口を言う雰囲気。体の不透明度は約40〜60%で、輪郭が背景に溶けるように薄い（※キー抜き用に背景はベタ単色のままで、被写体のみ半透明に描く）。配色は寒色（青灰〜青緑、冷たい影）。怖いより「こっそりして寂しい」。口元に小さな黒いもや。光を当てると実体化する設定なので、別途「実体化版（不透明100%）」も同ポーズで用意。右向き。背景は純マゼンタ #FF00FF のベタ1色。
【EN】［style prefix］"Kage-guchi", a lurking enemy, BEFORE purification. A semi-transparent figure (chibi) blending into the background. A deep hood / cloak hiding the eyes, only the mouth visible, whispering behind-the-back gossip. Body opacity ~40–60%, outline faint as if dissolving into the background (NOTE: keep the background a flat solid color for keying; render only the SUBJECT as semi-transparent). Cool palette (blue-gray to teal, cold shadow). Not scary, more "sneaky and lonely." A small black wisp at the mouth. Since light makes it materialize, ALSO provide a "materialized version (100% opaque)" in the same pose. Facing right. Background pure flat magenta #FF00FF.
```

### (B) 浄化後（同一人物）
```
【JP】［共通接頭辞］「かげぐち」の浄化後。※添付の実体化版 (A) を参照し同一人物（同じ体型・マントの形）。フードを後ろに下ろして顔を出し、はにかんだ優しい笑顔、頬に赤み。完全に不透明（100%）になり実体化。配色を暖色へ（淡いピンク〜クリームのマント、健康的な肌）、彩度アップ。口元の黒いもやは小さな白い光に。胸元の白い吹き出しに「だいじょうぶだよ」。右向き、やや正面。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Kage-guchi" AFTER purification. (Attach the materialized (A); same person — same body and cloak shape.) The hood is pulled back to reveal the face, with a shy gentle smile and blush. Fully opaque (100%), materialized. Palette flips WARM (pale-pink-to-cream cloak, healthy skin), higher saturation. The black wisp at the mouth becomes a small white light. A white speech bubble at the chest reads "だいじょうぶだよ" (it's okay). Facing right, slightly toward viewer. Background pure flat green #00FF00.
```

### (C) 黒い吹き出しパネル
```
【JP】［共通接頭辞］かげぐちが旋回させる「黒い陰口の吹き出し」を1枚。半透明で輪郭がぼやけた黒インクの吹き出し（こそこそ感）。ふちは細かく不規則な棘。中央に白い小さな「・・・」（陰口を匂わせる省略。罵倒語は書かない）。ふちに赤〜マゼンタの控えめな発光リム。明滅して見え隠れする雰囲気。1枚のみ中央。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］A single black gossip-bubble that Kage-guchi orbits. A semi-transparent, blurry-edged black-ink bubble (sneaky feel). A fine, irregular thorny rim. A small white "・・・" in the center (ellipsis implying gossip; no insults). A subdued red-to-magenta glowing rim. A flickering, appearing-and-vanishing mood. Only ONE bubble, centered. Background pure flat green #00FF00.
```

**アセット仕様（要点）**
| 種別 | ファイル名案 | セルpx目安 | 必要枚数 |
|---|---|---|---|
| (A) 本体・潜伏（半透明） | `enemy_kage_pre.png` | 32×40 | 2（半透明版＋実体化版。実体化は光ヒット時に切替） |
| (B) 本体・浄化後 | `enemy_kage_post.png` | 32×40 | 1 |
| (C) 旋回パネル | `panel_kage.png` | 18×18 | 1（半透明） |

---

## 5. ボス「バズの主（ぬし）」（フェーズ制）

> CONCEPT_V2 §6：巨大な炎上の渦＋黒い引用吹き出しの王。元は注目されたかった寂しい人間。多層の吹き出しを順に剥がすフェーズ制。最終浄化で渦が大きな花の樹になり「ごめんね、言い過ぎた」とうなだれて素の人に戻り、静かにログアウトする余韻。タイムライン全体が虹色に。
> サイズは画面の約1/3を占める大型。多層の引用吹き出し（大小）を旋回させる。

### (A) 浄化前・フェーズ1（君臨）
```
【JP・基準画像】［共通接頭辞］ステージボス「バズの主（ぬし）」の浄化前・第1形態。巨大な炎上の渦を背負って君臨する王のような人物（チビ頭身だが画面の約1/3を占める大型）。背後に黒インクの大きな渦（バズ＝拡散の渦）。安っぽい紙の王冠をかぶり、玉座のように渦の中心に座る。腕を広げて注目を集めるポーズだが、表情はどこか虚ろで寂しい（注目されたかっただけの人）。配色は寒色＋黒（濃紺〜青黒の衣、灰青の肌、黒い渦）。彩度低め。怖いより「孤独な王様」。周囲に多数の黒い引用吹き出しの気配。右向き、やや正面でalgoと対峙。背景は純マゼンタ #FF00FF のベタ1色。
【EN】［style prefix］Stage boss "Nushi of Buzz" BEFORE purification, phase 1. A king-like figure enthroned before a giant flame-war vortex (chibi proportions but large, ~1/3 of the screen). Behind it, a large black-ink vortex (the swirl of viral spread). It wears a cheap paper crown and sits at the vortex center like a throne. Arms spread to draw attention, but the expression is hollow and lonely (someone who only wanted to be noticed). Cool palette + black (navy-to-blue-black robe, gray-blue skin, black vortex). Low saturation. Not scary, a "lonely king." Many black quote-bubbles hinted around it. Facing right, slightly toward viewer to confront algo. Background pure flat magenta #FF00FF.
```

### (A) 浄化前・フェーズ2（暴走・怒り）
```
【JP】［共通接頭辞］「バズの主」第2形態（暴走）。※添付の第1形態を参照し同一人物。背後の黒い渦が巨大化して荒れ狂い、引用吹き出しが多層に渦巻く。紙の王冠が傾き、顔が必死さと焦りで歪む（注目を失う恐怖）。黒インクの侵食が体に広がる。配色はさらに寒く濃い黒、ふちに赤〜マゼンタの炎上発光。強いグリッチ。怖さより痛々しさ。右向き、やや正面。背景は純マゼンタ #FF00FF のベタ1色。
【EN】［style prefix］"Nushi of Buzz" phase 2 (rampage). (Attach phase 1; same person.) The black vortex behind it grows huge and turbulent, quote-bubbles swirling in multiple layers. The paper crown tilts; the face contorts with desperation (fear of losing attention). Black-ink corruption spreads over the body. Palette colder and darker black, with a red-to-magenta flame-war glow on the rim. Strong glitch. More pitiful than scary. Facing right, slightly toward viewer. Background pure flat magenta #FF00FF.
```

### (B) 浄化後（素の人＋花の樹）
```
【JP】［共通接頭辞］「バズの主」の浄化後（最終）。※添付の第1形態を参照し同一人物（同じ顔・体型）。背後の黒い渦が大きな花の樹（桜のような満開の樹）に変わり、花びらが降りそそぐ。紙の王冠を脱いで手に持ち、うなだれて静かに微笑む素の人間の姿。サイズは少し小さくなり等身大の人間味に。配色を暖色へ（クリーム〜桜ピンクの普段着、健康的な肌）、彩度アップ、全体に虹色の柔らかな光。胸元の白い吹き出しに「ごめんね、言い過ぎた」。右向き、やや正面でalgoの方を見る。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Nushi of Buzz" AFTER purification (final). (Attach phase 1; same person — same face and body.) The black vortex behind it has become a great flower tree (a fully-bloomed sakura-like tree) raining petals. It has taken off the paper crown and holds it, head bowed, quietly smiling as a plain human. Slightly smaller now, life-sized and human. Palette flips WARM (cream-to-sakura-pink everyday clothes, healthy skin), higher saturation, a soft rainbow glow overall. A white speech bubble at the chest reads "ごめんね、言い過ぎた" (sorry, I went too far). Facing right, slightly toward viewer, looking toward algo. Background pure flat green #00FF00.
```

### (C) 黒い引用吹き出しパネル（大・小）
```
【JP・大】［共通接頭辞］バズの主が旋回させる「黒い引用吹き出し（大）」を1枚。引用リツイートのような、外枠の大きな角丸吹き出しの中に小さな黒い吹き出しが入った"入れ子"の形。全体黒インク、ふちは威圧的な大きな棘。中央に白い小さな「は？」と引用記号「" "」（罵倒語は書かない）。ふちに赤〜マゼンタの強い発光。多層感を出すため半透明のダブり1枚。1枚のみ中央。背景は純緑 #00FF00 のベタ1色。
【EN・large】［style prefix］A single large black quote-bubble (like a quote-retweet) that the boss orbits: a nested shape — a small black bubble inside a large rounded outer bubble. All black ink, an intimidating large thorny rim. A small white "は？" and quote marks " " in the center (no insults). A strong red-to-magenta glow on the rim. One semi-transparent ghost copy for a multi-layer feel. Only ONE bubble, centered. Background pure flat green #00FF00.

【JP・小】［共通接頭辞］上の引用吹き出しを小型化・簡素化した「子吹き出し（小）」を1枚。同じ黒インク＋棘＋赤マゼンタ縁。中央は白い「・・・」のみ。多数を内周で旋回させる用。1枚のみ中央。背景は純緑 #00FF00 のベタ1色。
【EN・small】［style prefix］A single smaller, simpler child-bubble derived from the large quote-bubble. Same black ink + thorns + red-magenta rim. Center is just a white "・・・". For many to orbit on the inner ring. Only ONE bubble, centered. Background pure flat green #00FF00.
```

**アセット仕様（要点）**
| 種別 | ファイル名案 | セルpx目安 | 必要枚数 |
|---|---|---|---|
| (A) 本体・P1 | `boss_nushi_pre_p1.png` | 160×176 | 1（基準画像） |
| (A) 本体・P2怒り | `boss_nushi_pre_p2.png` | 160×176 | 1（P1参照で同一人物） |
| (B) 本体・浄化後 | `boss_nushi_post.png` | 160×176 | 1（渦→花の樹・脱冠うなだれ。退場フェード差分推奨） |
| (C) 引用パネル大 | `panel_nushi_l.png` | 40×40 | 1 |
| (C) 引用パネル小 | `panel_nushi_s.png` | 20×20 | 1 |

> ボスは「倒す」でなく「静かにログアウトする余韻」。撃破＝花びら化ではなく、(B) から**フェードして去る差分**を別途用意（CONCEPT_V2 §6 / CHARACTER_ASSETS.md §7-5 の Null と同じ非致死退場の考え方）。

---

## 6. ChatGPT で量産・一貫性を保つコツ（敵向け要点）

CHARACTER_ASSETS.md §2-3〜2-6 をベースに、今回の「人＋黒吹き出し」特有の注意：

- **同一人物性の担保（最重要）**：(B)浄化後は必ず **(A)を参照添付**し「同じ顔・髪型・服の形のまま、色と表情だけ変える」と明記。別人になりがちなので生成後に目視チェック。
- **絵柄合わせ**：毎回 `../../char/algo.png` を参照添付し「algo と同じ頭身・線の処理」と書く。チビ3頭身が崩れたら良い1枚を再添付してリセット。
- **キー抜き前提**：人物と吹き出しで背景色を変える（§★キー背景の色ルール）。被写体に背景色が混じると抜けないので「縁のにじみ禁止・影や模様を背景に入れない」を毎回明記。
- **黒の使い分け**：「黒はキャラの輪郭線に使わない（1px＋暗い同系色）。黒は暴言吹き出しのインク塗りにのみ使う」を毎回釘刺し。太い黒ベタ線が出たら良い例を添付して矯正。
- **テーマの安全装置**：罵倒語が出ていないか毎回確認。出てよいのは「は？」「で？」「・・・」程度まで。やさしい言葉は (B) のみ。
- **1依頼1変更**：ポーズ・表情・画角を同時に大きく変えない。(A)→(B) は「色＋表情＋（炎→花びら等の固有変換）」までに留め、1枚ずつ。
- **群れ・色替え**：スパムロボや子分は1枚を完成させ、量産・ステージ色替えは画像生成でなく **Aseprite のパレットスワップ**が速く確実。
- **吹き出しは単体生成**：パネルは人物と分けて1枚で生成（コード側で旋回・複製）。人物に持たせて描かせない。

---

## 7. 関連ファイル
- 大元の作画・技術・仕上げ・命名規則：[../CHARACTER_ASSETS.md](../CHARACTER_ASSETS.md)（特に §1 スタイル / §2 ワークフロー / §8 命名・ディレクトリ）
- 新コンセプト本体：[../CONCEPT_V2.md](../CONCEPT_V2.md)（§3 三層方針 / §5 コアメカ＝本体＋旋回パネル / §6 敵デザイン）
- 主人公基準イラスト（全生成で参照添付）：`../../char/algo.png`
- 想定生成物の配置：`char/refs/`（基準画像）→ `char/frames/`（整列後）→ `char/sheets/`（アトラス）。命名は本書のファイル名案に準拠（`enemy_<name>_pre/post.png` ／ `panel_<name>.png` ／ `boss_nushi_*`）。
