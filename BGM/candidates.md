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
