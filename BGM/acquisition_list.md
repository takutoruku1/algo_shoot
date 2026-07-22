# BGM取得リスト — 商用利用可フリーBGMの調達仕様書

> 目的: ネット上の**商用利用可能なフリーBGM**を取得して各スロットへ配置するための一覧。
> **このファイルは「何を・どんな条件で・どこから拾うか」の仕様のみ**。入手・差し替え作業は別タスク。
> 作成: 2026-07-19（BGM棚卸し・配置統一後の状態がベース）。担当: composer（mitsuda skill）。

## 0. このプロジェクトの音源パイプライン（取得前に知っておくこと）

- 取得した原曲は `BGM/` に**マスター**として置く（export除外済み: `export_presets.cfg` の `BGM/*`）。
- ffmpeg で **-3dB 正規化・末尾フェードのトリム・頭40ms/尻120msの極小フェード**を掛けて
  `audio/bgm_<役割>.ogg` に書き出し、Godot import で `loop=true` を焼く（既存10曲と同レシピ）。
- つまり**「改変可」がライセンス上の必須条件**（音量加工・トリム・ループ加工・ogg再エンコードを必ず行う）。
- 再生側は `src/Audio.cs` の `Load*` が読む。ローダーは合成フォールバック付きなので、
  ファイルを差し替えるだけで鳴る（コード変更不要。新規スロットのみローダー追加が要る）。
- 全曲の主題は **M.I.N.A. モチーフ（ド・ミ・レ・ソ = C-E-D-G）の変奏**という設計。フリー素材で
  旋律一致は求められないため、**「調性・温度・楽器」の一致を優先**する（下記スペック参照）。

## 1. 全BGMスロット一覧（現状と差し替え要否）

| # | スロット（audio/） | 使用シーン | 再生フック（file:line） | ローダー（Audio.cs） | 現状 | 差し替え要否 |
|---|---|---|---|---|---|---|
| 1 | bgm_menu_mina.ogg | タイトル/ハブ/ショップ/設定/難易度/記録/ShopTutorial/Prologue/Epilogue | TitleMenu.cs:88, Hub.cs:74, Shop.cs:78, Settings.cs:35, DiffSelect.cs:55, Records.cs:35, ShopTutorial.cs:48, Prologue.cs:67, Epilogue.cs:87 | :992 | 既存曲あり（ユーザー制作 Mina_s_Window 系） | **不要**（§5判定済み: 商用OK） |
| 2 | **（新規）bgm_stage_w0.ogg** | W0チュートリアル道中 "tutorial" | Stage0Root.cs:24 → GameManager.cs:248 → StageBgm() :1134 | ローダー未作成（現在は合成 BgmStage が鳴る） | **合成のみ＝実音源ゼロ** | **取得必要（P1）** |
| 3 | bgm_stage_rei.ogg | STAGE1 レイ道中 | ReiRoot.cs:26 → SetStageMusic("rei") :1144 | :1007 | 既存曲あり（The_Watcher_in_the_Hall） | **不要**（§5判定済み: 商用OK） |
| 4 | bgm_boss_rei.ogg | レイ戦（HP20%で加速演出あり） | BossRei.cs:121 | :1045 | 既存曲あり | **不要**（§5判定済み: 商用OK） |
| 5 | bgm_stage_akari.ogg | STAGE2 あかり道中 | AkariRoot.cs:26 | :1020 | 既存曲あり（Empty_Desks_at_Four） | **不要**（§5判定済み: 商用OK） |
| 6 | bgm_boss_akari.ogg | あかり戦 | BossAkari.cs:111 | :1063 | 既存曲あり（Akari_s_Last_Corridor） | **不要**（§5判定済み: 商用OK） |
| 7 | bgm_stage_koharu.ogg | STAGE3 こはる道中 | KoharuRoot.cs:27 | :1031 | 既存曲あり（The_Kettle_Stays_Warm） | **不要**（§5判定済み: 商用OK） |
| 8 | bgm_boss_koharu.ogg | こはる戦 | BossKoharu.cs:104 | :1082 | 既存曲あり（The_Leaking_Tap） | **不要**（§5判定済み: 商用OK） |
| 9 | bgm_boss_hikage.ogg | W0中ボス ヒカゲ戦 | BossHikage.cs:58 | :1108 | 既存曲あり（The_Frozen_Threshold） | **不要**（§5判定済み: 商用OK） |
| 10 | bgm_boss_mina.ogg | FINAL ミナ戦 | BossMina.cs:88 | :1095 | 既存曲あり（The_Weight_Of_Absolution） | **不要**（§5判定済み: 商用OK） |
| 11 | bgm_final_resolve.ogg | Final 挿入歌枠（無音→一点投入） | Final.cs:198 → PlayFinalResolve() :406 | :1122 | インストplaceholder（Morning_Light_on_Glass） | **ボーカル入りが理想（P2・特別枠）** |
| 12 | （合成のまま維持） | Final冒頭の濁り曲=BgmBoss / 改心ジングルRedeem×4 / 濁りパッドMurkPad | Final.cs:69 ほか | — | コード合成 | **差し替え不要（設計意図）** |
| 13 | （新規・任意）Prologue/Epilogue 専用変奏 | 現在は BgmMenu 流用 | Prologue.cs:67 / Epilogue.cs:87 | ローダー未作成 | 流用で成立している | 任意（P4・後回し可） |

※ FINAL導入（StageMina.cs:55）は**意図的な無音**＝スロットではない。

## 2. ライセンス要件チェックリスト（DL時に毎回確認）

各曲のダウンロード前に、配布ページ・利用規約で以下を**全て**確認し、チェック結果を §6 の記録欄に残すこと。

- [ ] **商用利用可**（本作は配布・販売の可能性あり。「非商用のみ」は不可）
- [ ] **改変可** — **必須**。音量正規化（-3dB）・末尾トリム・フェード加工・ogg再エンコードを必ず行うため、
      「加工禁止」「そのままの形でのみ使用可」の素材は使えない
- [ ] **ゲームへの組み込み再配布可**（exe/pck に埋め込んで配布する。「ストリーミング配信のみ可」等は不可）
- [ ] **クレジット表記**の要否と**指定書式**（例:「音楽: ○○（サイト名）」。必要ならタイトル/クレジット画面に
      追加するタスクを起票。表記免除の有償ライセンスがあるかも確認）
- [ ] **登録・報告義務**の有無（使用報告フォーム、作品URL報告、ライセンス登録、YouTube Content ID 登録の有無）
- [ ] **著作権表示の削除禁止・二次配布禁止条件**（素材単体の再配布にならない使い方か＝ゲーム埋め込みはOKか）
- [ ] **規約のスナップショット保存**（DL日・規約ページのURL/文言を §6 に記録。規約は変わり得るため、
      DL時点の条件を証拠として残す）

> ⚠️ フリーBGMサイトの規約は**予告なく変わる**。このファイルの §4 の記述は 2026-07-19 時点の一般的傾向。
> **必ずDL前に各サイトの最新規約を確認**すること。

## 3. 曲ごとの要件スペック（優先度順）

書式: 情緒の狙い（mitsuda style §1/§3 の翻訳）→ 音楽仕様 → 尺・ループ → 検索キーワード。
共通仕様: **44.1kHz ステレオ / mp3 320kbps か wav / イントロが長すぎない（〜4秒目安）/ ループ前提の構造**
（完全シームレスでなくてよい。末尾フェードはパイプラインでトリムする。ただし「曲の途中で終わる」構造は不可）。

### P1-① bgm_stage_w0（W0チュートリアル道中）— 最優先（唯一の実音源ゼロ枠）
- **情緒**: 最初の一歩。SNS世界（X/タイムライン）の無機質さ＋「これから誰かを救いに行く」ほのかな前向き。
  怖がらせない（チュートリアル＝とっつき）。電子寄りだが冷たすぎない。
- **仕様**: BPM 100–120 / 長調 or 明るめのモーダル / シンセポップ・チップチューン寄りエレクトロニカ。
  ベル系やプラック系のリードだと主題（ガラスの音色=ミナ）と繋がる。過度に賑やかな四つ打ちEDMは避ける。
- **尺/ループ**: 30秒〜1分半。ループ運用。
- **検索語**: 「チュートリアル 明るい エレクトロニカ ループ」「シンセポップ かわいい ゲームBGM」
  「electronic chiptune loop cheerful」「synth pop game bgm tutorial upbeat light」
- 配置後タスク: `Audio.cs` に `LoadBgmStageW0()` を追加し `StageBgm()` :1134 の `"tutorial"` 分岐に接続（軽作業）。

### P2-② bgm_final_resolve（Final 挿入歌・一点投入枠）— 特別枠
- **情緒**: 作中唯一の「歌」の位置（無音→ppp で立ち上がる決定打。Final.cs:190 で無音→:198 で投入）。
  「返事は、ありませんでした。」の直後に流れる、喪失と赦しと朝の光。泣かせどころの本体。
- **仕様**: 女性ボーカルの静かなバラード / ピアノ+弦の最小伴奏 / 長調圏（Epilogue の BgmMenu へ橋渡すため
  温かい響き必須）/ BPM 60–80。**歌もの・商用可・改変可**はフリー素材では希少＝見つからなければ
  ①現行インストplaceholder続投 ②ボーカリスト委託 の2択（この判断は指揮官マター）。
- **尺/ループ**: 1分〜（ループは緩くてよい。1周聴かせ切れれば足りる）。
- **検索語**: 「歌入り フリーBGM 商用可 バラード 切ない」「ボーカル入り フリー音楽素材 ゲーム」
  「royalty free vocal ballad emotional」「free music with vocals sad piano commercial use」
- **注意**: 歌詞の内容がシーン（喪失→帰還）と衝突しないか必ず試聴確認。歌詞が強すぎるならインスト維持が安全。

### P3 既存スロットの差し替え候補（§5 のライセンス確認が NG だった場合に発動）
確認NGのスロットだけ、以下のスペックで調達する。優先順は**プレイヤーが聴く時間の長い順**:
menu → stage_rei/boss_rei → stage_akari/boss_akari → stage_koharu/boss_koharu → boss_mina → boss_hikage。

#### ③ bgm_menu_mina（メニュー一族: 9画面で流れる最長試聴曲）
- **情緒**: ミナの部屋の窓辺。温かく、少し寂しい。長く居ても疲れない「間」の多い曲。
- **仕様**: BPM 70–90 / 長調（C系だと理想）/ ピアノ or ガラス系ベル+薄いパッド。音数少なめ必須。
- **尺/ループ**: 30秒〜1分。ループ耐性最重要（10分聴いて飽きないか）。
- **検索語**: 「優しい ピアノ ループ 日常 静か」「オルゴール 切ない フリーBGM」
  「gentle piano loop calm nostalgic」「music box soft loop bittersweet」

#### ④ bgm_stage_rei（レイ道中: 廊下・順位・監視）
- **情緒**: 学校の廊下を進む緊張。無機質な反復＝「見られている」感。推進力はあるが melodic すぎない。
- **仕様**: BPM 110–128 / 短調 or 無調寄り / 硬質ピアノ・プラック・ミニマルエレクトロニカ。持久型（平坦でよい）。
- **尺/ループ**: 30秒〜1分。
- **検索語**: 「ミニマル 緊張感 ピアノ ループ」「無機質 エレクトロニカ 追跡」
  「minimal tense piano ostinato loop」「dark electronic stealth loop」

#### ⑤ bgm_boss_rei（レイ戦: 孤高・あと一歩で一番になれない）
- **情緒**: 張り詰めた対峙。ライトモチーフ設計は「主音の直前で半音落ちる＝未完」。硬質ピアノが軸。
- **仕様**: BPM 130–150 / 短調 / ピアノ主体のバトル曲（ロックよりピアノ・ストリングス系）。※HP20%で
  再生速度+15%の演出（SetMusicSpeed）が掛かるため、テンポ揺れの大きい曲は不向き。
- **尺/ループ**: 1分〜2分。
- **検索語**: 「ピアノ バトル 疾走 短調」「緊迫 ボス戦 ストリングス」
  「intense piano battle loop」「dramatic strings boss fight minor key」

#### ⑥ bgm_stage_akari（あかり道中: 雨・放課後の教室）
- **情緒**: 雨の日の教室、言えなかった言葉。湿度のある静かな切なさ。生楽器寄り（世界の描き分け）。
- **仕様**: BPM 80–100 / 短調⇄長調を行き来する曖昧さ / ピアノ+木管 or アコギ。雨音入りも可。
- **尺/ループ**: 45秒〜1分半。
- **検索語**: 「雨 切ない ピアノ 教室」「放課後 ノスタルジック フリーBGM」
  「melancholic piano rain loop」「nostalgic school afternoon bgm emotional」

#### ⑦ bgm_boss_akari（あかり戦: 言いかけて切れる想い）
- **情緒**: 感情が溢れて戦いになる。設計は「フレーズが途中で切れる＝言えない好き」。激しさの中に切なさ。
- **仕様**: BPM 120–140 / 短調 / ストリングス+ピアノのエモーショナル系バトル。息のある木管が入ると理想。
- **尺/ループ**: 1分〜2分。静かな尾章があってもよい（トリム/橋渡しはパイプラインで処理）。
- **検索語**: 「切ない バトル ストリングス 感情的」「エモーショナル ボス戦 ピアノ」
  「emotional battle theme strings piano」「sad intense orchestral loop」

#### ⑧ bgm_stage_koharu（こはる道中: 台所の温もりが冷えていく）
- **情緒**: 湯気と夕食の記憶→少しずつ冷える。温かい音色に翳りが差す構造だと理想。
- **仕様**: BPM 70–90 / 長調始まり（翳りは無くても可）/ アコースティック: 弦・木質パーカッション・オルゴール。
- **尺/ループ**: 45秒〜1分半。
- **検索語**: 「温かい アコースティック 日常 夕暮れ」「家族 思い出 オルゴール 切ない」
  「warm acoustic everyday life loop」「cozy kitchen nostalgic music bittersweet」

#### ⑨ bgm_boss_koharu（こはる戦: 冷えた祈り）
- **情緒**: 温かかったものが凍った悲しみの戦い。設計は「温かい旋律が冷えて減衰する」。水滴・時計のモチーフ可。
- **仕様**: BPM 100–130 / 短調 / ピアノ+弦、ミニマルな反復に不穏さ。金属質のパーカッション（水滴的）が合う。
- **尺/ループ**: 1分〜2分半。
- **検索語**: 「悲しい バトル ピアノ 弦楽」「不穏 ミニマル 水滴」
  「sorrowful battle loop piano strings」「cold minimal tension loop」

#### ⑩ bgm_boss_hikage（ヒカゲ戦: 凍った敷居・笑うのがへた）
- **情緒**: 輪に入れず立ち尽くす子の凍えた心。設計は「立ち上がりかけてオクターブ下へ沈む」。冷たいが敵意ではない。
- **仕様**: BPM 90–110 / 短調 / 氷・ガラス系シンセ+ピアノ。平坦な持久曲でよい（W0中ボス＝重すぎない）。
- **尺/ループ**: 30秒〜1分。
- **検索語**: 「氷 冷たい シンセ 緊張」「孤独 ボス戦 エレクトロ」
  「frozen icy synth tension loop」「lonely cold boss theme electronic」

#### ⑪ bgm_boss_mina（ミナ戦: 暴走した主題＝作品の顔の裏面）
- **情緒**: 主題の「顔」が穢れに沈んだ姿。荘厳で重い。悲鳴の中に、届けたい一本の旋律。
- **仕様**: BPM 70–100（重厚系）/ 短調 / オーケストラ+クワイア or 荘厳シンセ。ラスボスの格。
- **尺/ループ**: 1分半〜3分。フル強度区間が30秒以上連続してあること（現行は本体区間切り出しでループ化した）。
- **検索語**: 「ラスボス 荘厳 オーケストラ クワイア」「悲壮 最終決戦 合唱」
  「final boss orchestral choir epic tragic」「dark epic choral battle loop」

### P4-⑫ Prologue/Epilogue 専用変奏（任意・新規スロット）
- 現状 BgmMenu 流用で成立しているため**急がない**。取るなら: Prologue=③の電子グリッチ版（断片・不穏）、
  Epilogue=③の完全版（解決・朝）。同一曲のアレンジ違いが手に入るサイト（PeriTune 等はバリエーション配布が多い）だと理想。
- **検索語**: 「グリッチ アンビエント 断片」「朝 光 ピアノ 解決」 「glitch ambient fragments」「morning light piano resolution」

## 4. 推奨入手先（商用可フリーBGMサイト）

> ⚠️ 以下は一般的なライセンス傾向の要約（2026-07-19 時点の知識）。**規約は変わる。DL前に必ず最新規約を原文確認**し、§2 のチェックリストを通すこと。

| サイト | 傾向 | クレジット | 注意点 |
|---|---|---|---|
| **DOVA-SYNDROME**（国内最大手） | 商用可・加工可・ゲーム利用可 | 原則不要（任意） | 曲ごとに作曲者の追加条件がある場合あり。曲ページの利用条件を個別確認 |
| **魔王魂** | 商用可・加工可 | **必要**（「魔王魂」表記。書式指定あり） | 歌もの（ボーカル曲）も一部あり＝⑪挿入歌の候補になり得る |
| **甘茶の音楽工房** | 商用可・加工可 | 任意（推奨） | 優しい生楽器系が多い＝③⑥⑧向き |
| **MusMus** | 商用可・加工可 | **必要**（無償利用時。表記免除の有償プランあり） | ゲーム組込の規定を確認 |
| **PeriTune** | 商用可・CC-BY 4.0 中心（一部CC0） | CC-BY分は**必要** | 同曲のバリエーション配布が多い＝⑫（変奏）向き。RPG/ファンタジー系が充実 |
| **FreePD** | CC0（パブリックドメイン） | 不要 | 品質・曲調はばらつき大。⑨⑩の持久曲探しに |
| **incompetech**（Kevin MacLeod） | CC-BY 4.0（有償でクレジット免除） | CC-BY分は**必要** | 英語圏定番。オーケストラ系＝⑪候補 |
| **OtoLogic** | 商用可・CC-BY 4.0 | **必要** | ジングル・短尺も豊富 |

- クレジットが必要なサイトを使う場合: タイトル or Records 画面にクレジット表示を追加するタスクを**同時に起票**する
  （現状ゲーム内にクレジット画面が無い。表記義務を負ったら実装必須）。
- 複数サイト混在は可。ただし**曲ごとの出所と規約URL・DL日を §6 に必ず記録**する。

## 5. 既存 BGM/ マスター（mp3群）の扱い — **判定済み（2026-07-20）: 全10曲 差し替え不要**

**制作経路（ユーザー確認済み）**: 全曲 **Gemini アプリ（gemini.google.com チャットUI）で生成**（Lyria 3、2026-02-18 以降）。

**判定（Google 公式規約の一次調査 2026-07-20）**:
- 生成物の所有権はユーザー（Google ToS: "Google won't claim ownership over that content"）、商用禁止条項なし
  → **商用ゲームへの同梱・販売は規約上問題なし。P3 差し替えは不発動**
- 留意1: 「商用可」の積極的許諾文言は無く、Google の IP 補償も無し。明文許諾が欲しくなったら
  Gemini API 有料枠 / Vertex AI（Lyria 3 Pro・$0.08/曲）で再生成すれば "commercial purposes に使用可" の明文経路に載せ替え可能
- 留意2: 全曲に SynthID 透かし入り（配布の法的障害ではない。ストアの AI 利用開示は元々必要）
- 留意3: **Google ToS が 2026-07-30 に改定予定 → 改定後に文言を再確認すること**

下表はマスター→スロット対応の参照用（ライセンス確認は完了済み）:

| マスター | 使用スロット | 確認事項 |
|---|---|---|
| Mina_s_Window.mp3 / (1).mp3 | bgm_menu_mina（+未使用の別テイク） | 制作手段と商用権。**未使用分は権利と無関係に削除可** |
| The_Watcher_in_the_Hall.mp3 | bgm_stage_rei | 同上 |
| The_Frozen_Threshold.mp3 | bgm_boss_hikage | 同上 |
| Empty_Desks_at_Four.mp3 | bgm_stage_akari | 同上 |
| Akari_s_Last_Corridor.mp3 | bgm_boss_akari | 同上 |
| The_Kettle_Stays_Warm.mp3 | bgm_stage_koharu | 同上 |
| The_Leaking_Tap.mp3 | bgm_boss_koharu | 同上 |
| The_Weight_Of_Absolution.mp3 | bgm_boss_mina | 同上 |
| Morning_Light_on_Glass.mp3 | bgm_final_resolve | 同上（placeholder。P2で差し替え予定でもある） |
| （該当なし＝コード合成） | bgm_stage_w0 相当（BgmStage）/ BgmBoss / Redeem×4 | 合成は自前生成＝権利問題なし |

- ライセンス上は P3 不発動だが、**ユーザー判断（2026-07-20）: Gemini生成曲は品質・ループ長の面で不満のため全曲差し替える**。
  → **P3 を品質理由で発動**。取得対象は P1（W0）＋ P3 全スロット（§3 の優先順: menu → rei → akari → koharu → mina → hikage）＋ P2（挿入歌・任意）。
- 差し替え完了までは既存 Gemini 曲が placeholder として鳴り続ける（権利上は配布可能なので中間ビルドに支障なし）。

## 6. 取得記録（DL時にここへ追記する）

導入実施 2026-07-20（composer）。加工は全て既存レシピ（ピーク-3dB帯へゲイン・ogg q6・import loop=true。
非ループ原曲のみ末尾トリム＋頭40ms/尻120msフェード。**公式ループ版はゲインのみ＝トリム/端フェード無し**）。
マスターは `BGM/` に保存（export除外）。曲別の加工値は `src/Audio.cs` 各 `Load*()` コメントが正。

| 日付 | スロット | 曲名 | サイト/作者 | 規約URL | 商用/改変/組込 | クレジット義務 | 備考 |
|---|---|---|---|---|---|---|---|
| 2026-07-20 | bgm_menu_mina | 巡る思い出 | DOVA／蒲鉾さちこ | https://dova-s.jp/bgm/detail/18472 | ○/○/○ | 任意（記載済） | 個別条件: 大幅改変・AI学習禁止→音量+トリムのみで抵触せず。94.0→92.0s・+1.5dB |
| 2026-07-20 | bgm_stage_w0 | Roll Roll Roll | DOVA／もっぴーさうんど | https://dova-s.jp/bgm/play10827.html | ○/○/○ | 任意（記載済） | 公式ループトラック（トラック1）→ゲイン-4.8dBのみ・136.4s |
| 2026-07-20 | bgm_stage_rei | SO-001 | MusMus／watson | https://musmus.main.jp/（音源利用ライセンス） | ○/○/○ | **必須「BGM:MusMus」（記載済）** | 138.75→136.0s・-5.1dB |
| 2026-07-20 | bgm_boss_rei | Falling with You | DOVA／のる | https://dova-s.jp/bgm/play21919.html | ○/○/○ | 任意（記載済） | 個別条件: 音楽主体動画禁止→ゲームは非該当。178.8→169.0s・-1.5dB |
| 2026-07-20 | bgm_stage_akari | 6月の雨傘 | 甘茶の音楽工房 | https://amachamusic.chagasi.com/music_rokugatsunoamagasa.html | ○/○/○ | 任意（記載済） | 102.3→97.5s・-1.3dB |
| 2026-07-20 | bgm_boss_akari | EpicBattle | PeriTune | https://peritune.com/blog/2020/08/28/epicbattle/ | ○/○/○ | **必須 CC BY 4.0（記載済）** | 2020-08-28公開＝旧規約CC-BY側を確認。公式ループ版ogg→-4.1dBのみ・97.4s |
| 2026-07-20 | bgm_stage_koharu | 小さな足あと | 甘茶の音楽工房 | https://amachamusic.chagasi.com/music_chiisanaashiato.html | ○/○/○ | 任意（記載済） | 130.4→126.0s・-1.9dB |
| 2026-07-21 | bgm_boss_koharu | 切ない戦いが始まりそう | DOVA／シンシンワダ | https://dova-s.jp/bgm/play2254.html | ○/○/○ | 任意（記載済） | 初候補 Red Sapphire は曲削除確定（404）で断念→再探索候補を採用。2014-10-08公開・作曲者（creator/detail/91）個別条件なし確認。**配布トラック自体が公式ループ版**（132.1s）→ゲイン-5.0dBのみ・トリム/端フェード無し |
| 2026-07-20 | bgm_boss_hikage | Frozen Forest | PeriTune | https://peritune.com/frozen_forest/ | ○/○/○ | **必須 CC BY 4.0（記載済）** | 2021-11-24公開＝旧規約CC-BY側を確認。公式ループ版ogg→-3.0dBのみ・86.4s |
| 2026-07-20 | bgm_boss_mina | Dramatic5 | PeriTune | https://peritune.com/blog/2020/06/10/dramatic5/ | ○/○/○ | **必須 CC BY 4.0（記載済）** | 2020-06-10公開＝旧規約CC-BY側を確認。公式ループ版ogg→-4.2dBのみ・82.3s |
| — | bgm_final_resolve | （据え置き） | — | — | — | — | ユーザー決定: 現行インスト続投＝触らない |

- PeriTune の規約一本化告知（2026-03-01 https://peritune.com/blog/2026/03/01/terms-update/ ）の原文で
  「2026年2月以前の既存曲: 引き続き CC BY 4.0」を確認済み。3曲とも該当。
- クレジット表記は `config/credits.ini` [音楽] に記載済み（ゲーム内 Credits 画面に表示される）。
- 旧 Gemini 生成マスター（Mina_s_Window 他9本）は §5 のとおり権利上は配布可。品質理由の差し替えにより
  **全て未使用化**（2026-07-21 のこはる戦導入をもって全10スロットの商用ライセンス化が完了。
  旧マスターはフォールバック用途も無いため、ユーザー判断で削除可）。
