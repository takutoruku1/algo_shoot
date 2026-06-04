# W0 炎上ボス「ヒカゲ」— キャラクター設計 & 画像生成プロンプト

> 新規ボス。女の子。病み・泣き系／黒い炎のツインテール。
> 世界観・三層方針・キー背景ルール・共通スタイル接頭辞は [ENEMY_SNS_PROMPTS.md](ENEMY_SNS_PROMPTS.md) を厳守。
> 画風合わせのため、生成時は **`../../char/algo.png` を参照添付**（`gen_edit.mjs`）して「algoと同じ画風・線処理」を毎回指定する。

---

## 1. キャラクター設定

- **名前**：ヒカゲ（日陰）。仮。
- **一人称**：**うち**（二人称は「きみ」。素の自分が出るほど方言混じりの「うち」が出る。アルゴの「ボク」と対になる距離感）。
- **精神年齢**：**18歳ぐらい**。思春期の終わり〜大人の入口で、承認欲求と自意識がいちばん拗れる年頃。見た目はチビ中ボスでも、抱えているのは「見てほしい／でも見られるのがこわい」という年相応のひりついた心。アルゴの“生まれたての心”とは対照的に、**こじらせて大人になりかけている**。
- **モチーフ**：「日陰」にいた子が、光（＝注目）に焦がれて炎上の渦の中心に立ってしまった。
- **背景（なぜ渦に立ったか）**：大学に入ったのにリモート授業ばかりで、**同級生と一度も“ちゃんと会えない”まま友達ができなかった**女の子。さみしさと不安をまぎらわせるために**SNSにのめり込み**、画面の向こうの反応だけが心を落ち着かせる唯一のよりどころになっていく。けれど「いいね」は一瞬で消え、もっと強い反応＝**炎上**でしか自分の存在を確かめられなくなり、気づけば渦の中心に立っていた。＝**悪い子ではなく、つながりたかっただけの子**。だからアルゴの「ともだちに なろう」がいちばん刺さる相手。
- **系統**：病み・泣き系。**笑うのが下手な子**。注目がこわいのに、それでも見てほしくて、**無理やり作り笑い**で強がっている。怖いより**痛々しくて、ほっとけない**。
- **役割**：W0 ステージの climax 中ボス（画面の約1/4。チビだがザコより一回り大きい）。
- **コアメカ**：黒い炎の吹き出しを**リング状に旋回**（同調の渦＝ピンポン炎上）。algoの光で1枚ずつ剥がす＝**鎮火**。リングが細るほど作り笑いがこわばり、目に涙がたまる。全部剥がすと【浄化（改心）】。
- **浄化の3幕（このキャラの肝）**：
  1. **浄化前＝無理した作り笑い**。口角は上げているのに目が笑っていない、ぎこちなくて今にも泣きそう。
  2. **浄化の瞬間＝大泣き**。こらえていたものが決壊してくしゃっと号泣（黒い炎が桜の花びらに変わりかける遷移）。
  3. **改心後＝最高の笑顔**。algoと**友達**になり、生まれて初めての本物の自然な笑顔。「…うちも、わらえた」。改心後は味方フォロワーとして残る（非致死）。

### 状態とアセット（コード対応）
| 種別 | 内容 | コード対応 | ファイル名案 | セルpx目安 |
|---|---|---|---|---|
| (A) 浄化前 | 寒色・**無理した作り笑い**・黒い炎ツインテール | `Enemy` 通常 | `enemy_hikage_pre.png` | 64×88 |
| (B1) 大泣き | 浄化の瞬間・**決壊して号泣**・炎→花びら遷移 | `Redeem` 直後の一時表示(約1.2s) | `enemy_hikage_cry.png` | 64×88 |
| (B2) 最高の笑顔 | 暖色・**本物の自然な笑顔**・花びらツインテール | `Enemy` 改心（最終） | `enemy_hikage_post.png` | 64×88 |
| (C) 黒い炎の吹き出し | リング旋回する暴言パネル | `Panel` | `panel_hikage.png` | 24×24 |

---

## 2. 生成プロンプト（共通接頭辞を先頭に貼る → algo.png 参照添付）

### (A) 浄化前 ― 基準画像
```
【JP】［共通接頭辞］W0の炎上ボス「ヒカゲ」の浄化前・基準画像。SNSの悪意に当てられて悪魔化した女の子（チビだがザコより一回り大きい中ボスサイズ）。「日陰にいた子が、見てほしくて炎上の渦に立ってしまった」病み・泣き系。
左右の髪を高めのツインテールに結っているが、その毛束が【黒い炎】になってめらめら揺れている（黒インクの塊が炎の形、毛先に小さな黒い吹き出しの火の粉）。
表情は【無理して作った下手な笑顔】：口角は上げているのに目が笑っていない、ぎこちなくて今にも泣きそう。目の下にうっすらクマ、目尻に涙。よく見ると疲れて寂しい。服装は【病みかわ系】でかわいく：猫耳フードつきのぶかぶかパーカー＋胸元に大きなリボン、フリルのミニスカートにニーソ。小さなハートや星、安全ピン、ばんそうこう風の病みかわアクセ、涙ぼくろ。胸にスマホを抱え、肩を少し丸める。
配色は寒色（くすんだ紺〜青緑のパーカー、灰青の肌影、黒い炎）、彩度低め。怖いより痛々しい。黒はキャラの輪郭線に使わず、暴言（黒い炎）の塗りにのみ使う。
【中ボスらしく少し豪華に】※ただし algo と同じ画風・チビ頭身・1pxの線処理は厳守し、画風を大きく離さない。髪の黒い炎は大きめにめらめら立ち上げ、まわりに小さな黒い吹き出しの火の粉をいくつか漂わせる（旋回リングの示唆）。衣装の作り込み（フリル・リボン・アクセ）をザコより一段ていねいに。足元にごく淡い炎上の光。
右向き、やや正面でalgoと対峙。全身が収まる。背景は純マゼンタ #FF00FF のベタ1色。
【EN】［style prefix］W0 flame-war boss "Hikage" BEFORE purification, master reference. A girl devil-fied by SNS malice (chibi but a size larger than small fry — a mid-boss). Theme: "a girl from the shade who, craving to be seen, ended up at the center of a flame-war." Melancholic, teary type.
Her hair is tied in high TWIN-TAILS, but the strands are made of BLACK FLAMES flickering (black-ink masses shaped like fire, tiny black speech-bubble sparks at the tips).
Expression: a FORCED, AWKWARD SMILE of someone bad at smiling — the corners of her mouth are pulled up but her eyes are not smiling, stiff and on the verge of tears. Faint dark circles beneath, tears at the corners of her eyes; on a closer look she is tired and lonely. CUTE "yami-kawaii" (sick-cute) outfit: an oversized hoodie with a cat-ear hood, a big ribbon bow at the chest, a frilled mini-skirt with thigh-high socks; cute yami-kawaii accents (small hearts and stars, a safety pin, bandage-style charms, a teardrop beauty mark). She clutches a smartphone to her chest, shoulders slightly hunched.
Cool palette (dull navy-to-teal hoodie, gray-blue skin shadow, black flames), low saturation. More pitiful than scary. Never use black as the character outline — black only for the hateful black-flame fill.
[Slightly more elaborate, as a mid-boss] but STRICTLY keep algo's art style, chibi proportions, and 1px line treatment — do not drift from the style. The black hair-flames rise larger and fiercer; a few small black speech-bubble embers drift around her (hinting at the orbiting ring). The outfit (frills, ribbon, accessories) is a notch more detailed than small fry. A very faint flame-war glow at her feet.
Facing right, slightly toward viewer to confront algo. Full body in frame. Background pure flat magenta #FF00FF.
```

### (B1) 浄化の瞬間 ― 大泣き（同一人物）
```
【JP】［共通接頭辞］「ヒカゲ」の浄化の瞬間＝大泣き。※添付の(A)を参照し、同じ顔・ツインテール・同じ病みかわ衣装の同一人物。ずっとこらえていたものが決壊して、くしゃっと顔を歪めて【号泣（大泣き）】：口を開けて泣き、涙がぼろぼろ大粒にこぼれ、頬は紅潮、眉は八の字。作り笑いが崩れた"素"の表情で、どこか安堵もある。
ツインテールの黒い炎が【ちょうど桜の花びらに変わりかけ】で、黒とピンクが混ざって舞い散る。配色は寒色→暖色への遷移中（くすんだ青と桜ピンクが混在）。抱えたスマホは少し下がる。
右向き、やや正面。全身が収まる。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Hikage" at the MOMENT of purification = crying hard. (Attach (A); same face, twin-tails, same yami-kawaii outfit — same person.) Everything she held in breaks loose: her face scrunches into a big, ugly, honest CRY — mouth open, large tears pouring, cheeks flushed, eyebrows in a troubled slope. The forced smile has shattered into a raw, real expression, with a hint of relief.
The black flames of her twin-tails are MID-TRANSFORMING into sakura petals — black and pink mixing and scattering. Palette transitioning cool→warm (dull blue and sakura pink coexisting). The smartphone she clutches lowers a little.
Facing right, slightly toward viewer. Full body in frame. Background pure flat green #00FF00.
```

### (B2) 浄化後 ― 最高の笑顔（同一人物）
```
【JP】［共通接頭辞］「ヒカゲ」の浄化後・最終。※添付の(A)浄化前を参照し、同じ顔・髪型（ツインテール）・同じ病みかわ衣装（猫耳フードパーカー＋大きいリボン＋フリルスカート＋ニーソ）の形の同一人物だと分かるように。違いは色・表情・炎→花びらだけ。
ひとしきり泣いたあと、顔を上げて【生まれて初めての最高の自然な笑顔】：涙のあとは残るが、目もちゃんと笑った、まぶしい本物の笑み。頬に赤み。肩の力が抜けて、抱えたスマホの画面に小さなハート。algoを"友達"として見つめる。
ツインテールの【黒い炎は桜の花びら】に変わり、毛束から花びらがふわふわ舞う。配色を暖色へ反転（桜ピンク〜クリームの病みかわ衣装、健康的な肌）、彩度を少し上げる。全体に淡くやわらかい光。胸元の小さな白い吹き出しに「ありがとう…ともだちに なってくれて」。
右向き、やや正面でalgoの方を見る。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Hikage" AFTER purification (final). (Attach (A) and keep the SAME face, twin-tail hairstyle, and yami-kawaii outfit shape (cat-ear hoodie + big ribbon + frilled skirt + thigh-high socks) — clearly the same person.) Only color, expression, and flames→petals change.
After crying it all out, she lifts her head with her FIRST EVER, BRIGHTEST, GENUINE smile — tear-streaks remain, but this time her eyes truly smile too; a real, radiant smile. Blush on cheeks. Her shoulders relax; the smartphone she clutches shows a small heart on its screen. She looks at algo as a FRIEND.
The black flames of her twin-tails have turned into CHERRY-BLOSSOM PETALS drifting gently from the strands. Palette flips WARM (sakura-pink-to-cream yami-kawaii outfit, healthy skin), slightly higher saturation, a soft gentle glow overall. A small white speech bubble at the chest reads "ありがとう…ともだちに なってくれて" (thank you for being my friend).
Facing right, slightly toward viewer, looking toward algo. Background pure flat green #00FF00.
```

### (C) 黒い炎の吹き出しパネル
```
【JP】［共通接頭辞］ヒカゲがリング状に旋回させる「黒い炎の暴言吹き出し」を1枚だけ。角丸の吹き出しだが、ふちが激しい棘でとげとげし、中身は黒い炎の渦（スパイラル）で塗りつぶされている。中央に白い小さな「は？」（罵倒語は書かない）。ふちに赤〜マゼンタの強い発光リム（炎上の熱）。毛先の火の粉のように小さく震えるグリッチ。吹き出し1枚のみ、画面中央。背景は純緑 #00FF00 のベタ1色。
【浄化反転差分】同じ吹き出しの形で、黒い炎を桜ピンクに反転し中央を白いハート♡に変えた「花びら」版（浄化エフェクト用）。
【EN】［style prefix］A single black flame hate-bubble that Hikage orbits in a ring. A rounded speech bubble with a fiercely thorny rim, filled with a black-flame spiral. A small white "は？" (huh?) in the center (no insults). A strong red-to-magenta glowing rim (heat of a flame war). A small trembling glitch like drifting embers. Only ONE bubble, centered. Background pure flat green #00FF00.
【purified flip variant】Same bubble shape, black flame flipped to sakura pink with a white heart ♡ in the center — a "petal" version for the purification effect.
```

---

## 2.5 会話用の立ち絵（表情差分・高解像度カットイン）

> algo の立ち絵（`char/algo_cutout.png`）と同じ用途。吹き出し横（画面左）に出す**高解像度のカットイン**。胸から上のバストアップ。
> 表情差分は **作り笑い／大泣き／最高の笑顔** の3種。差分は**ベース(作り笑い)を参照添付して表情だけ変える**と一貫性が出る。
> ※ 立ち絵は元の高解像度のまま保管（`char/raw/`）。ゲーム用カットアウトに整える。

### 立ち絵ベース（作り笑い）＝このキャラの基準画像
```
【JP】［共通接頭辞］炎上ボス「ヒカゲ」の会話用立ち絵（高解像度カットイン、胸から上のバストアップ）。病みかわ系のかわいい女の子。猫耳フードつきのぶかぶかパーカー＋胸元に大きなリボン、病みかわアクセ（ハート・星・安全ピン・ばんそうこう・涙ぼくろ）。髪は高めのツインテールで、毛束が黒い炎になってめらめら揺れる。
表情は【無理して作った下手な笑顔】：口角は上げているのに目が笑っていない、ぎこちなくて今にも泣きそう。目の下にうっすらクマ、目尻に涙。配色は寒色（くすんだ紺〜青緑）、彩度低め、痛々しくかわいい。algoと同じ画風・線処理だが、立ち絵なので塗りはていねい・解像度高め。
正面やや右向き、視線はこちら。背景は純マゼンタ #FF00FF のベタ1色。
【EN】［style prefix］Dialogue portrait (high-res cut-in, bust-up from chest) of the flame-war boss "Hikage". A cute yami-kawaii girl: oversized cat-ear hoodie + big chest ribbon, yami-kawaii accessories (hearts, stars, a safety pin, a bandage, a teardrop beauty mark). High twin-tails whose strands are black flames flickering.
Expression: a FORCED, AWKWARD SMILE of someone bad at smiling — mouth pulled up but eyes not smiling, stiff, on the verge of tears; faint dark circles, tears at the corners. Cool palette (dull navy-to-teal), low saturation, pitiful-cute. Same art style and line treatment as algo, but as a portrait the rendering is cleaner and higher-res.
Front, slightly turned right, looking at the viewer. Background pure flat magenta #FF00FF.
```

### 立ち絵 差分：大泣き
```
【JP】［共通接頭辞］「ヒカゲ」立ち絵の【大泣き】差分。※添付の立ち絵ベース（作り笑い）を参照し、同じ顔・髪型・衣装・画角の同一人物。表情と髪の炎だけ変える。
こらえていたものが決壊して【号泣】：口を開けて泣き、大粒の涙がぼろぼろ、頬は紅潮、眉は八の字。作り笑いが崩れた素の顔で、少し安堵もある。ツインテールの黒い炎が桜の花びらに変わりかけ（黒×ピンク混在）。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Hikage" portrait, CRYING-HARD variant. (Attach the base portrait (forced smile); same face, hair, outfit, framing — same person. Change only expression and hair-flames.)
A big, honest CRY: mouth open, large tears pouring, cheeks flushed, troubled eyebrows; the forced smile shattered into a raw face, with a hint of relief. The twin-tail black flames are mid-transforming into sakura petals (black & pink mixing). Background pure flat green #00FF00.
```

### 立ち絵 差分：最高の笑顔（浄化後）
```
【JP】［共通接頭辞］「ヒカゲ」立ち絵の【最高の笑顔】差分（浄化後）。※添付の立ち絵ベースを参照し、同じ顔・髪型・衣装・画角の同一人物。表情・配色・炎→花びらだけ変える。
ひとしきり泣いたあとの【生まれて初めての本物の自然な笑顔】：涙のあとは残るが、今度は目もちゃんと笑った、まぶしい笑み。頬に赤み。ツインテールの黒い炎は桜の花びらに変わり、ふわふわ舞う。配色を暖色へ反転（桜ピンク〜クリームの病みかわ衣装、健康的な肌）、彩度を少し上げ、全体に淡くやわらかい光。背景は純緑 #00FF00 のベタ1色。
【EN】［style prefix］"Hikage" portrait, BEST-SMILE variant (after purification). (Attach the base portrait; same face, hair, outfit, framing — same person. Change only expression, palette, and flames→petals.)
After crying it all out, her FIRST EVER genuine, natural smile — tear-streaks remain but this time her eyes truly smile; a bright, radiant smile, blush on cheeks. The twin-tail black flames have become sakura petals drifting gently. Palette flips WARM (sakura-pink-to-cream yami-kawaii outfit, healthy skin), slightly higher saturation, soft gentle glow. Background pure flat green #00FF00.
```

### 立ち絵アセット仕様
| 表情 | 用途 | ファイル名案 | 背景キー |
|---|---|---|---|
| 作り笑い（基準） | 登場・煽り会話 | `hikage_face_smile.png` | magenta |
| 大泣き | 浄化の瞬間の会話 | `hikage_face_cry.png` | green |
| 最高の笑顔 | 友達になった後 | `hikage_face_happy.png` | green |

---

## 3. 生成手順（このフェーズ）

**まず「立ち絵ベース（作り笑い）」を基準画像として確定し、以降すべてそれを参照添付して一貫性を担保する。**

1. **立ち絵ベース（作り笑い）** を `gen_edit.mjs`（`char/algo.png` 参照添付）で生成 → 目視で「algo画風／病みかわ感／"作り笑い"の表情／黒は炎のみ」を確認。NGなら1点だけ直して再生成。**これが基準画像。**
2. 基準を参照添付して **立ち絵差分：大泣き → 最高の笑顔** を生成（同一人物性チェック必須）。
3. 基準を参照添付して **敵バトル(A)pre（全身・少し豪華）** を生成 → さらに参照添付して **(B1)大泣き → (B2)最高の笑顔** を生成。
4. **(C) 黒い炎パネル** を単体生成（人物に持たせない）。
5. 仕上げ（キー抜き・トリム・縮小・ふち処理）は `tools/key_trim_scale.ps1`。
   - 作り笑い系（寒色）：背景マゼンタ → magenta キー
   - 大泣き／最高の笑顔／パネル（暖色・緑映え）：背景グリーン → green キー
   - 立ち絵は高解像度のまま `char/raw/` に保管し、ゲーム用にカットアウト＆軽い縮小。
6. 配置・実装：
   - 立ち絵 → `char/hikage_face_smile/cry/happy.png`。Hud の会話で話者の立ち絵を差し替えられるよう、`ShowDialog(text, portrait)` に話者立ち絵を渡せるよう拡張。
   - 敵 → `char/enemy_hikage_pre/cry/post.png`, `char/panel_hikage.png`。`BossHikage : Enemy` を作成し `PreTexPath/PostTexPath/PanelTexPath` を指定。
   - **3段階演出**：`Redeem()` で本体スプライトを (A)→(B1)大泣き に差し替え、約1.2秒後に (B2)最高の笑顔 へ。大泣き中に立ち絵付きの専用かけあい（ヒカゲ「…うち、ずっと わらうの へただった」→ algo「そのままで いいよ。ともだちに なろう」→ ヒカゲ「…うん！」）。
   - フォロワー化（味方）はこの最高の笑顔の後。

## 4. 関連
- 共通スタイル・三層方針・キー背景ルール：[ENEMY_SNS_PROMPTS.md](ENEMY_SNS_PROMPTS.md)
- 仕上げ・命名：[../CHARACTER_ASSETS.md](../CHARACTER_ASSETS.md)
- 物語上の位置づけ：[../STORY.md](../STORY.md)（炎上＝個人を飲み込む渦。ラスボス＝システム/アルゴリズムの予兆）
