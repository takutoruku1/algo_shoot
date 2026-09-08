# BGM候補リスト（試聴用） — 2026-07-20 調査

> 使い方: スロットごとに上から試聴 → 採用曲を決めたら指揮官（Claude）に伝える。
> DL・正規化・ループ加工・配線はパイプラインで自動処理する。
> ライセンスは全て 2026-07-20 時点で規約原文を確認済み（詳細は acquisition_list.md §2/§5）。

## サイト別ライセンス要点（確認日 2026-07-20）

| サイト | 商用/改変/組込 | クレジット | 備考 |
|---|---|---|---|
| DOVA-SYNDROME | ○/○/○ | **不要** | 曲ごとの個別条件が優先（各候補欄に記載）。音源を容易に取り出せる同梱は不可 → ogg+pck格納でOK |
| 甘茶の音楽工房 | ○/○/○ | 任意 | 音楽単体の再配布・販売のみ禁止 |
| MusMus | ○/○/○ | **必須**「BGM:MusMus」 | 表記免除の有償プランあり |
| PeriTune | ○/○/○ | 2026-02以前の曲=**CC BY 4.0 必須** / 03以降=任意 | **03以降の新曲は有償販売時にpck暗号化が必須**。候補は全て旧曲(CC-BY)側 |
| 魔王魂 | ○/○(全改変可)/○ | **必須**「音楽：魔王魂」 | 歌もの豊富。音楽としての単体配信・販売は禁止 |
| incompetech | ○/○/○ (CC BY 4.0) | **必須**（有償で免除可） | 曲ページはDL時に原文再確認 |
| ~~FreePD~~ | — | — | **サービス終了確認済み（2026-07-20）→ 除外** |
| OtoLogic | ○/○/○ (CC BY 4.0) | 必須 | 機械アクセス403。使う場合はブラウザで規約再確認 |

## 試聴リスト（★=筆頭候補）

### ③ bgm_menu_mina（メニュー9画面・最重要ループ耐性）
- ★ **巡る思い出** — 蒲鉾さちこ/DOVA https://dova-s.jp/bgm/detail/18472 （1:34・ループ調整済み明記・表記不要。個別条件: 大幅な改変禁止・AI学習禁止）
- 静止した宇宙 — 甘茶 https://amachamusic.chagasi.com/music_seishishitauchu.html （2:37・オルゴール系・浮遊感やや強）
- Recollection — PeriTune https://peritune.com/recollection/ （公式ループ版あり・CC-BY・BPM128でやや速め）

### ① bgm_stage_w0（チュートリアル道中・唯一の実音源ゼロ枠）
- ★ **Roll Roll Roll** — もっぴーさうんど/DOVA https://dova-s.jp/bgm/play10827.html （2:16・ループ可明記・賑やかさ控えめチップチューン。テンポ感のみ要確認）
- Dreambyte — PeriTune https://peritune.com/dreambyte/ （公式ループ版・8bit・BPM170で速め・公開日要確認）
- ガーデン・シティ — のる/DOVA https://dova-s.jp/bgm/detail/23348 （2:33・トラック2がループ仕様・生楽器ポップでエレクトロニカではない）

### ④ bgm_stage_rei（ミニマル緊張・無機質）
- ★ **SO-001** — watson/MusMus 試聴 https://www.youtube.com/watch?v=gAWUdddNR7A （2:18・「機械的・無機質・冷たい」・要クレジット）
- R.E.C.Y.C.L.E — watson/MusMus 試聴 https://www.youtube.com/watch?v=j5vpNz05IJM （2:02・「無機質・緊張・STG」タグ）
- Logical Flow — watson/MusMus 試聴 https://www.youtube.com/watch?v=kyLxt1Qq7VA （2:46・反復・緊張は控えめ）

### ⑥ bgm_stage_akari（雨・切ない生楽器）
- ★ **6月の雨傘** — 甘茶 https://amachamusic.chagasi.com/music_rokugatsunoamagasa.html （1:42・梅雨の儚げピアノ）
- 雨のプレリュード — 甘茶 https://amachamusic.chagasi.com/music_amenoprelude.html （2:35・雨×悲しいピアノ）
- 夏の霧 — 甘茶 https://amachamusic.chagasi.com/music_natsunokiri.html （2:31・湿度のある静けさ）

### ⑧ bgm_stage_koharu（温かいアコースティック）
- ★ **小さな足あと** — 甘茶 https://amachamusic.chagasi.com/music_chiisanaashiato.html （2:10・「家庭的で繊細」ストリングス）
- 秋うらら — 甘茶 https://amachamusic.chagasi.com/music_akiurara.html （2:42・フルート+ピアノ）
- 夢の跡の僕ら — watson/MusMus 試聴 https://www.youtube.com/watch?v=4kUxRhd5UXo （3:21・アコギ・翳りのある懐かしさ・要クレジット）

### ⑩ bgm_boss_hikage（氷系シンセ・持久曲）
- ★ **Frozen Forest** — PeriTune https://peritune.com/frozen_forest/ （BPM100でスペック一致・公式ループ版・CC-BY要表記）
- White snow chill days — 蒲鉾さちこ/DOVA https://dova-s.jp/bgm/detail/22951 （3:40・敵意のない冷たさ・緊張感ゼロ寄り。大幅改変禁止）
- Frosylva — PeriTune https://peritune.com/frosylva/ （BPM62で遅め・公開日要確認=新規約の可能性）

### ⑤ bgm_boss_rei（ピアノ疾走バトル・再生+15%演出ありテンポ一定必須）
- ★ **Falling with You** — のる/DOVA https://dova-s.jp/bgm/play21919.html （2:58・疾走ピアノ×切ない×電子ビート。女声ヴォカリーズ入り=「作中唯一の歌」設計との干渉を耳で判定。個別条件: 音楽主体動画禁止=ゲームは無関係）
- dear Dragon — MusMus https://musmus.main.jp/music_img5.html （3:31・激しめ×ピアノ主体・要クレジット）
- Red Sapphire — ISAo/DOVA https://dova-s.jp/_mobile/bgm/play3039.html （1:55・静かめ寄り。⑨と兼用候補）

### ⑦ bgm_boss_akari（エモーショナルバトル・弦+ピアノ）
- ★ **EpicBattle** — PeriTune https://peritune.com/blog/2020/08/28/epicbattle/ （BPM138でスペック帯・「切ない×熱い」・公式ループ版・CC-BY）
- Will you still cry? — まんぼう二等兵/DOVA https://dova-s.jp/bgm/detail/5060 （5:30・ループ点明記・悲壮バイオリン戦闘曲・BPM177で速め）
- EpicBattle_Deity — PeriTune https://peritune.com/blog/2022/05/09/epicbattle_deity/ （弦+ピアノ+コーラスだがBPM205で大幅超過）

### ⑨ bgm_boss_koharu（悲しいバトル・ミニマル反復）※完全一致が最も薄い枠
- ★ **Red Sapphire** — ISAo/DOVA https://dova-s.jp/_mobile/bgm/play3039.html （1:55・「悲しい・冷たい・緊張」ピアノ+弦）
- Volatile Reaction — Kevin MacLeod/incompetech https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400039 （2:45・反復オスティナートだが金管主体・7/4拍子）
- マーブルコーヒー — かずち/DOVA https://dova-s.jp/bgm/detail/4757 （不穏ピアノ・バトル強度不足の可能性）
- ※どれもしっくり来なければ「DOVAで 悲壮×戦闘 タグをもう1周」を発注可能

### ⑪ bgm_boss_mina（ラスボス・荘厳オーケストラ+クワイア）
- ★ **Dramatic5** — PeriTune https://peritune.com/blog/2020/06/10/dramatic5/ （オルガン+金管+弦+ティンパニ+コーラス・公式ループ版・CC-BY。BPM175の疾走型荘厳）
- Final Battle of the Dark Wizards — Kevin MacLeod/incompetech https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1100657 （4:31・クワイア+オルガン+オケ・Dark/Epic/Somber）
- 覇道 — MusMus https://musmus.main.jp/music_img5.html （7:23・「ラスボス戦闘曲」明記・クワイアなし・要クレジット）

### ② bgm_final_resolve（挿入歌・女性Vo静バラード）※完全一致は発見できず
- Nostalgia — Mary(Vo)/魔王魂 https://maou.audio/09_nostalgia/ （女性Vo・喪失テーマで歌詞衝突なし・ただしBPM132で「静かなバラード」ではない）
- 月の河 — 森田交一/魔王魂 https://maou.audio/36_tsukinokawa/ （曲想は最有力の静バラードだが男性Vo）
- Blue Star — 龍崎一/DOVA https://dova-s.jp/bgm/detail/3635 （2:02・しっとりピアノロックバラード・Vo性別と歌詞は要試聴。個別条件: 「龍崎一」表記の希望あり）
- **推奨**: 試聴でピンと来なければこの枠は「該当なし」＝現行インスト続投 or ボーカリスト委託の判断へ

---

## 追記 2026-09-07 — エピローグ E5b オルゴール枠

### ⑬ bgm_epilogue_walk（エピローグ E5b「見上げる／歩く」・オルゴール）

> ## ✅ 採用: **夕べの星**（2026-09-08 ユーザー決定）
>
> 甘茶の音楽工房 https://amachamusic.chagasi.com/music_yuubenohoshi.html
> **調達・加工・実装まで完了（2026-09-08 composer）**。以下の候補記述は選定の経緯として全て残してある。
>
> - 配置: `audio/bgm_epilogue_walk.ogg`（マスターは `BGM/yuubenohoshi.mp3`）
> - 切り出し **0.00〜74.65 秒**（原曲 157.4s の「1周目の終わり＝A セクションが再開する直前」。
>   フェードアウトではなく曲想の切れ目で切ってある）／ゲイン **-3.7dB＝-17.6 LUFS**（`bgm_menu_mina` の
>   -17.7 LUFS に合わせた＝E6 で主題が戻るときに段差を出さない）
> - **`.import` は `loop=false`**（既存10曲と唯一違う。ループしない曲）
> - `Audio.MusicTargetDb()` で **0dB 扱いに除外**（`BgmMenu`/`BgmFinalResolve` と同じ扱い）
> - 実測: A メジャー・110 BPM。**B セクションの盛り上がり（39.7s〜）には到達しない**（停止が先に来る）
>   ＝結果として「泣かせにかからない」条件に有利
> - 詳細な根拠・ライセンス確認・実尺の実測値は `BGM/acquisition_list.md` §6.1
>
> **ユーザー決定（2026-09-07）: 既存曲（`bgm_menu_mina`＝巡る思い出）のオルゴール編曲は却下。新規に調達する。**
> 台本 `wiki/08_仮台本/08_粗い台本_案C_3_FINALと結末.md`「E5b 演出の指定」の音の項も、この決定に合わせて訂正済み。
>
> **条件**: オルゴール（music box）の音色 / 夜 → 明け方へ向かう静けさ / **泣かせにかからない**（悲愴・感傷を煽らない）
> / ループ不要（一度きりで終わってよい）/ **70〜90 秒あれば理想**、短ければループ可。
> ライセンスは既存 10 スロットと同条件（商用可・改変可・ゲーム組込可）。原曲が 70〜90 秒より長い場合は
> 既存パイプラインどおり**尺を切り出して使う**（＝改変可が必須）。
>
> **調査日 2026-09-07。以下は全て配布ページ原文を当日確認。ダウンロード・実装は未実施。**

#### サイト別ライセンス（今回あらためて原文確認したもの）

| サイト | 商用/改変/組込 | クレジット | 確認内容（2026-09-07） |
|---|---|---|---|
| 甘茶の音楽工房 | ○/○/○ | **任意**（サイト名 or 作曲者名 or URL のいずれか一つ） | `terms.html` 原文: 「商用利用、個人利用問わず利用できます」「ゲームなど、何かのBGMとして」「加工…テンポやキーの変更、音の付加、フェードイン・フェードアウトなど自由に加工できます」。禁止は**音楽単体の販売・2次配布**と直リンクのみ＝本作の用途は該当せず |
| PeriTune | ○/○/○ | **任意**（下記） | 2026-03-01 の規約一本化告知の原文を再確認。**2026年2月以前の既存曲は引き続き CC BY 4.0**、かつ「当サイトが独自に認めている**クレジット表記の任意化**・ファイル暗号化の容認等のルールを優先して運用して構いません」と明記。下記候補は全て 2016〜2017 年公開＝旧曲側 |

> ⚠️ **DOVA-SYNDROME は 2026-09-15 に「OpenTracks」へ名称変更**（サイト告知バナーで確認）。既存 5 曲の
> 規約URL（`dova-s.jp/...`）は将来リンク切れの可能性があるので、`acquisition_list.md` §6 の URL は改称後に要更新。
> 今回 DOVA 側は検索エンドポイントが改装中で機械アクセスできず、候補は 甘茶／PeriTune の 2 サイトから選定した。

#### 候補（★=筆頭）

- ★ **夕べの星** — 甘茶の音楽工房 https://amachamusic.chagasi.com/music_yuubenohoshi.html
  - 2:37 / 2017.09 公開 / タグ「癒し・オルゴール」/ **使用楽器: オルゴール／フルート／バスーン／シンセサイザー**
  - 商用○ 改変○ 組込○ / クレジット任意
  - **なぜこの場面に合うか**: タイトルがそのまま「夕べの星」＝**見上げる場面の情景と一致**。オルゴール主体に
    フルートとバスーンの薄い層が乗るので、**オルゴール単体より温度があり、四人が並んで立っている絵に耐える**。
    「癒し」タグ＝**泣かせにかからない**条件に最も素直に合う。2:37 のうち 70〜90 秒を切り出して使う。
    ミナのライトモチーフ（ガラス／グロッケンの澄んだ音色）と楽器のキャラクターが近い。

- ★ **月の小舟** — 甘茶の音楽工房 https://amachamusic.chagasi.com/music_tsukinokobune.html
  - 2:33 / 2010.04 公開 / タグ「幻想的・オルゴール」/ **使用楽器: オルゴールのみ**
  - 商用○ 改変○ 組込○ / クレジット任意
  - **なぜこの場面に合うか**: **オルゴール単体**＝ユーザー指示の「オルゴール」に最も忠実。伴奏が無いぶん
    台詞（18 行）を一切邪魔せず、台本指定の「…………。」での**完全停止 → 無音**が最も綺麗に決まる
    （層が無いので切っても不自然にならない）。「月」＝背景の満月と噛み合う。
    リスク: 単体ゆえ 70〜90 秒は淡白に感じる可能性。歩行パートの動きを音で支えない。

- **静止した宇宙** — 甘茶の音楽工房 https://amachamusic.chagasi.com/music_seishishitauchu.html
  - 2:37 / 2019.08 公開 / タグ「癒し・オルゴール」
  - 商用○ 改変○ 組込○ / クレジット任意
  - **なぜこの場面に合うか**: 「宇宙」＝夜空を見上げる 10 行にそのまま乗る。**浮遊感がやや強い**という指摘が
    §③（メニュー枠）の調査時にあり、メニューには不採用だったが、**一場面限りの E5b ではその浮遊感が有利**に働く。
    リスク: 浮遊が強すぎると「歩く」パートの推進力が出ない。

- **Music-box_Sad2** — PeriTune https://peritune.com/blog/2016/08/07/music-box_sad2/
  - 3:07 / 2016.08 公開（＝CC BY 4.0 側）/ BPM 83 / タグ「オルゴール・悲しい・切ない・穏やか・回想・静か」
  - **公式ループ版あり**（`https://peritune.com/loop/PerituneMaterial_Music-box_Sad2_loop.zip`）
    ＝既存の Frozen Forest / EpicBattle / Dramatic5 と同じ「ゲインのみで済む」加工ルートに乗る
  - 商用○ 改変○ 組込○ / クレジット任意（PeriTune 独自運用）
  - **なぜこの場面に合うか**: BPM 83 は指定帯（60–75）よりわずかに速いが、**歩行の足取りに合う速度**。
    公式ループ版があるので「短ければループ可」の逃げ道が最初から確保されている。
    リスク: **タグに「悲しい」**があり、**泣かせにかからない**という条件と正面から当たる。要試聴で判定。

- **Music-box_Gentle2** — PeriTune https://peritune.com/blog/2017/09/19/music-box_gentle2/
  - 約 2:02 / 2017.09 公開（＝CC BY 4.0 側）/ BPM 95 / タグ「オルゴール・切ない・優しい・穏やか・ヒーリング・回想・夢・ワルツ」
  - 商用○ 改変○ 組込○ / クレジット任意
  - **なぜこの場面に合うか**: 「優しい・穏やか・ヒーリング」が主タグで、**Sad2 より泣かせ成分が薄い**。
    ワルツ（3 拍子）なので**四人の足並みが揃わない**という E5b の演出意図（位相をずらした歩き）と相性が良い。
    リスク: BPM 95 でやや速い。ワルツが「軽い」方向に振れると場面の重さと合わない可能性。

#### 選定の指針（試聴時の判断軸）

1. **泣かせにかからないか** — これが最優先。感傷を煽る曲は E6 の頂点（最後の下書き選択）と食い合う。
   → この軸だけなら **夕べの星 > Gentle2 > 静止した宇宙 > 月の小舟 > Sad2**。
2. **「…………。」で止めて自然か** — 層が薄いほど切りやすい。→ **月の小舟 > 夕べの星 > Gentle2**。
3. **70〜90 秒を切り出せるか** — 全候補が 2 分以上あるので満たす。ループ版がある Sad2 のみ加工が最小。
4. **既存 `BgmMenu`（巡る思い出）と衝突しないか** — E5b の直後に E6 で `BgmMenu` が戻る設計なので、
   調性・温度が近すぎると「同じ曲に聞こえる」。逆に遠すぎると繋ぎ目が跳ねる。**試聴は E6 への繋ぎまで含めて判定する**。

#### 未確定・実装側の宿題（この枠を採用するとき）→ **全て解消済み（2026-09-08）**

- ~~E5b は**ループ不要＝1 周で終わって無音**という扱いが要るが、既存の `Audio.Music()` はループ前提の
  クロスフェード再生しか持たない~~ → `Audio.MusicOnce()` で解決（1周で無音に落ちる経路）。
  「『…………。』で完全停止」は `Audio.StopMusicOnce(0.9f)` を `Epilogue._Process` の PhGaze 行送りに
  同期させて実装済み（＝曲尾を待たず**台詞の進行で止める**）。実測で「停止 → 残り2行が無音」を確認。
- ~~ローダーを1本追加~~ → `LoadBgmEpilogueWalk()` 実装済み。**フォールバックは置かない**
  （曲が無ければ null＝無音のまま。「無い曲を代わりの音で埋めない」）。
- クレジットは任意だが `config/credits.ini` の `[音楽]` に記載済み（既存曲と同じ運用）。
