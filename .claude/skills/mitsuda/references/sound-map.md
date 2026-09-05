# 音イベント ↔ 実装ファイル 対応表（sound-map）

「音の設計図」（`mitsuda-style.md`）の各原理を、どのコードのどこで鳴らすかの索引。
**設計・実装の前に、必ず該当ファイルの現在の演出（`FxLayer` 呼び出し・既存値）を読む**（推測で音を置かない）。
`/sakurai` の design-map、`/maeda` の scene-map と三位一体。

> ⚠️ 行番号は作成時点の目安。**実装前に必ず現在のファイルを開いて確認**する（コードは動く）。

---

## 0. 現状＝実装済み・実際に鳴る（2026-06-20 更新。旧記述「完全無音」は失効）
**全SE・全BGM がコード合成（`AudioStreamWav`）で実装され、フックも各所に配線済み。`dotnet build -c Debug` 0エラー。Rei ステージ＋DemoPilot で起動し audio エラー無しを確認。** 実音源（録音/挿入歌）が来たら差し替える前提のプレースホルダだが「鳴る」状態。
- **バス**: `default_bus_layout.tres` 実在。`Master ─ Music / SE / Voice / Amb / Alert`。Music に LowPass を1枚（汚染連動の濁し枠）。`project.godot:29` で指定済み。
- **オートロード**: `Audio="*res://src/Audio.cs"`（`project.godot:24`）。`src/Audio.cs` が起動時に全SE/BGMを合成し常駐。
- **音量スライダー**: `AudioConfig.cs` が `user://settings.json` を単一窓口で読み書きし、master→Master / bgm→Music / se→SE / voice→Voice / amb→Amb へ全配線（旧「Masterのみ」は解消）。起動時 `Audio._Ready`→`AudioConfig.ApplySaved()`。
- **SE（合成）**: ショット/グレイズ/被弾/浄化/ボム/全開/鎮まり/スペル宣言/パネル剥がし/UI5種/タイプライター3話者。`Audio.Play*()`。被弾は Alert バス（最優先）。
- **BGM（合成・8秒前後シームレスループ）**: `BgmMenu`(タイトル/ハブ/ショップ/設定/難易度) / `BgmStage`(道中) / `BgmBoss`(汎用ボス＝Mina/Hikage) / `BgmBossRei` / `BgmBossAkari` / `BgmBossKoharu`（ボス別）。全曲 M.I.N.A. モチーフ（C E D G）の変奏。
- **改心の解決音**: `RedeemRei/Akari/Koharu` ＝ `Audio.PlayRedeem(boss)`。`BossRei.cs:201` / `BossAkari.cs:221` 等の OnCryStart で発火（未完モチーフ→主音へ解決＝「届いた」）。
- **アダプティブ**: `Audio._Process` が `GameManager.Contamination/Warmth` で Music の LowPass を駆動＋ Amb の濁りパッド音量を抜き差し（連続値＝フィルタ／style §6）。ステージ戦闘曲のみ対象。
- **QA**: `--qa` で `Audio.Muted=true`（無音・高速を維持）。`--demo` は鳴る（demo-video に乗る）。

> 残課題（コード側で対応済みでないもの）は §7「未/要対応」を参照。

---

## 1. SE：自機イベント
| イベント | ファイル:行（目安） | メソッド | 同期する視覚フック | 優先度 |
|---|---|---|---|---|
| ショット発射 | `src/Player.cs` `Fire()`（335付近） | 発射処理 | `FxLayer.Muzzle()`（リング＋スパーク, FxLayer.cs:92） | **高** |
| グレイズ（かすり） | `src/Player.cs` `OnGrazeAreaEntered()`（423付近） | かすり判定 | `FxLayer.Graze()`（シアンのリング, FxLayer.cs:119） | 中 |
| 被弾 | `src/Player.cs` `OnAreaEntered()`/`TakeHit()`（401-420/513付近） | 被弾 | `FxLayer.PlayerHit()`＋`GameCamera.Shake`（FxLayer.cs:130-135） | **高（最優先で埋もれさせない）** |
| ボム/特殊 | `src/Player.cs` `TryBomb()`（433-466付近） | ボム | `FxLayer.Bomb()`（魔法陣＋光の波, FxLayer.cs:168） | **高** |
| ヒカゲ専用スキル | `src/Player.cs` `TryHikageSpecial()`（476-505付近） | 特殊 | （専用FX／要確認） | 中 |
| フォロワー救出 | `src/Player.cs` `AddFollower()`（81付近） | 仲間増加 | （`KindnessMote` 等） | 低 |
| やさしさ全開（Overload）発動 | `src/GameManager.cs:389-398` `AddKindness()` / `IsOverload`（:353）/ `JustOverloaded`（1フレーム, :358） | ゲージ満タン | （桜井提案の全開トースト）。連射 0.07s 化（Player.cs:284）に合わせ**ピッチを上げる**余地 | 中 |

> SEは**必ず上記の `FxLayer` 呼び出しと同フレーム**で鳴らす（style §4）。新規SEは「どのフックに乗るか」を先に決める。

## 2. SE：敵・ボス・浄化
| イベント | ファイル:行（目安） | 同期する視覚フック | 優先度 |
|---|---|---|---|
| 敵弾発射 | `src/Enemy.cs` `SpawnPanels()`（117付近）/ `src/Panel.cs` `Fire()` | （発射FX） | 中（高密度では同時発音制限） |
| パネル剥がし（インク減） | `src/Panel.cs` `OnAreaEntered()`（107-117付近） | `FxLayer.Shatter`系/要確認 | 中 |
| パネル砕け | `src/Panel.cs` `Shatter()`（126-138付近） | `FxLayer.Shatter()`（破片＋KindnessMote, FxLayer.cs:103） | **高** |
| 敵浄化（改心） | `src/Enemy.cs` `Redeem()`（191-241付近） | `FxLayer.PurifyBurst()`（花びら＋ハート＋モート, FxLayer.cs:138） | **高** |
| やさしさの波紋（連鎖浄化） | `src/Ripple.cs`（15-52）/ `Enemy.cs:213-223` で親に AddChild | （Ripple拡大演出） | 中（連鎖は薄く重ねる） |
| 弾→花びら変換（ボム/会話消去） | `FxLayer.BulletToPetal()`（FxLayer.cs:156） | 視覚は実装済み | 中 |
| ボス段階移行 | `src/BossRei.cs` `OnHpChanged()`（169-183付近）他 Boss*.cs | スペル宣言UI（`Hud.cs` AnnounceSpell 162付近） | 中 |
| スペルカード発動/finale | `src/Boss*.cs` `ApplySpell()` → `GetHud()?.AnnounceSpell()` | UI宣言 | 中 |

## 3. BGM：シーン遷移
| 局面 | ファイル | 主題の扱い（style §1 変奏） |
|---|---|---|
| タイトル | `TitleMenu.tscn` / `src/TitleMenu.cs` | 主題を静かに（顔見せ） |
| Prologue（起動） | `src/Prologue.cs`（独自レンダラ。boot[]/Acrostic[] :32-38） | 電子・グリッチで主題の断片（無害に聞かせる＝伏線） |
| STAGE3 レイ | `Rei.tscn` / `src/StageRei.cs` `src/BossRei.cs` | 道中＝推進、ボス＝緊張変奏 |
| STAGE1 あかり | `Akari.tscn` / `src/StageAkari.cs` `src/BossAkari.cs` | 雨・教室の生楽器寄り。記憶フラッシュ（§4）と同期 |
| STAGE2 こはる | `Koharu.tscn` / `src/StageKoharu.cs` `src/BossKoharu.cs` | 台所の温もり→その後の冷感 |
| ミナ戦 | `src/StageMina.cs` `src/BossMina.cs` | 主題のフル（顔の本体） |
| ハブ | `Hub.tscn` / `src/Hub.cs` | まったり。短ループで可 |
| ショップ | `Shop.tscn` / `src/Shop.cs` | 取引のシンセ。短ループ |
| 難易度選択 | `DiffSelect.tscn` / `src/DiffSelect.cs` | テンション上げ |
| FINAL（汚染） | `src/Final.cs`（独自レンダラ。Screams[] :24-28, 汚染→1.0 :39付近） | 主題を濁す→決定打で挿入歌（一点投入） |
| EPILOGUE（名前） | `src/Epilogue.cs`（独自レンダラ。Acrostic[] :36-42, PwChoices） | 主題の回収（変奏で帰す） |

## 4. アダプティブ：連続値で曲を濁す/晴らす
| 連続値 | ファイル:行（目安） | 範囲 | 音響 |
|---|---|---|---|
| Warmth | `src/GameManager.cs:70` 付近（`StageProgress` 由来） | 0..1 | 浄化が進むほど LowPass を開く＝濁りが晴れる |
| Contamination（汚染） | `src/GameManager.cs:74-75` 付近（段階上昇、Final で 1.0） | 0..1 | 上がるほど主題にノイズ／ピッチ低下／拍の歪み |
| Player Tint（光の濁り） | `src/Player.cs:118-121`（`CleanTint→MurkTint`） | — | 視覚の濁りと**同じ曲線**で音も濁す（画と音の同期） |

> 連続値＝フィルタ／離散イベント（段階移行）＝レイヤー・分岐（style §6）。実装は `mitsuda-implementation.md` §アダプティブ。

## 5. 会話・演出との同期
| 同期点 | ファイル:行（目安） | 音の入り/抜き |
|---|---|---|
| タイプライター送り | `src/Hud.cs:84-85`（`CharsPerSec=48`） | 微小なテキスト送り音（voiceバス）。`Settings msg`(:69)/`auto`(:70) を尊重 |
| 会話開始＝敵弾全消去・敵停止 | `src/Hud.cs:93-96`（`BubblePaused`, `ClearEnemyBullets`） | 消去を「鎮まる音」で物語化（桜井×麻枝のタッグ提案 B 参照）。会話中はSE抑制 |
| 記憶フラッシュ | `src/StageImagery.cs:27`（`_flashT=2.4`）/ `src/BossAkari.cs:257`（who=Narration で発火） | クラクション/環境音を 2.4s で立て、生楽器の主題を一瞬 |
| Acrostic 表示 | `src/Prologue.cs:189-195` / `src/Epilogue.cs:169-192` | 解錠の瞬間に主題の一音（回収の合図） |
| 改心の決定打送り | `src/Boss*.cs`（Zキー手動送り, 0.25s ゲート） | 決定打行の直前で無音→主題変奏（style §7） |

## 6. デモ/QA との関係
| 仕組み | ファイル:行（目安） | 音の扱い |
|---|---|---|
| DemoPilot | `src/DemoPilot.cs`（`--demo`, `StoryPeriod=0.30` で会話最速送り :61付近） | 無音前提で完走。`demo-video` 録画に音を乗せるなら、ミュートしない経路を別途用意 |
| QaPilot | `src/QaPilot.cs`（`--qa`, 1200発で警告 :37付近） | 異常検出のみ。SEは**ミュート可**にして高速実行を妨げない |

> 実装方針: 自動プレイ時はSE/BGMを既定ミュート、`demo-video` の収録時のみ鳴らす切替を持つ（`mitsuda-implementation.md` §デモ/QA）。

---

## 既存方針（壊さないこと）
- **浄化＝倒すでなく届ける救う**。撃破音を爽快な破壊音"だけ"にしない。砕けの後に温かい余韻（KindnessMote と同期）。
- **難易度は弾の量・速度・間隔で／HP固定**。音で難易度を変えない（緊張感の演出は可、長さは変えない）。
- SEは既存の `FxLayer` フックに同期させ、**画と音をズラさない**。
- 音源未調達のものは、暫定音/結線で先に体験を通し、差し替えポイントを明示（SKILL「できること」節）。

---

## 7. キャラ別ライトモチーフ計画（メイン楽器・未完→完）— 設計
> 光田 style §1「一つの主題、多くの編曲」/ §3「楽器で心を描き分ける」の MINA 翻訳。
> **共通主題 M.I.N.A.（ド ミ レ ソ＝C5 E5 D5 G5）を全曲が共有**し、ボスごとに「未完の仕方（翳り）」と「メイン楽器（音色）」を変える。
> 改心（`PlayRedeem`）で同じ音形を主音 C6 まで届かせて**解決＝「届いた」**。Epilogue で主題に溶ける布石。
> 現状は全てコード合成のプレースホルダ。下記の「実楽器」列は**実音源差し替え時の指針**（合成では音色を近似）。

| キャラ | 未完の仕方（戦闘テーマ） | メイン楽器（実音源指針／合成での近似） | 解決音(Redeem) | 実装 |
|---|---|---|---|---|
| **ミナ（主題本体）** | 汎用 `BgmBoss`（短調・半音上テンションで濁す＝未完） | 澄んだガラス/グロッケン＋フルート（合成=高域正弦＋非整数倍音） | （Final/Epilogue で主題フル＝回収） | `BuildBgmBoss` / TypMina |
| **レイ（順位・孤高）** | 主音の直前で**半音落ちる**＝あと一歩で一番になれない | 硬質ピアノ/プルックの単音（合成=グリッサンド正弦） | `RedeemRei`（澄んだ高め・C6 へ届く） | `BuildBgmBossRei` / `BuildRedeem(0)` |
| **あかり（言えない好き）** | フレーズが**途中で切れる**（"す——"／言いかけて沈黙） | 息のあるリード/木管（合成=中庸正弦を ~0.5s で断つ） | `RedeemAkari`（息のある中庸） | `BuildBgmBossAkari` / `BuildRedeem(1)` |
| **こはる（冷える祈り）** | 温かい旋律が**冷えて減衰**（台所の灯が消える） | 温かい弦/木質パッド（合成=倍音付き正弦＋速い減衰＋微ピッチ降下） | `RedeemKoharu`（温かい厚み） | `BuildBgmBossKoharu` / `BuildRedeem(2)` |
| **ヒカゲ（凍った敷居）** | モチーフが**影に沈む**（立ち上がりかけてオクターブ下へ引っ込む＝人前で笑えない） | 実音源 `audio/bgm_boss_hikage.ogg`（PeriTune「Frozen Forest」公式ループ版, CC BY 4.0）／合成= `BuildBgmBossHikage` | `RedeemHikage`（はにかんだ温もり warm=0.42） | `BossHikage.cs:58` Music / `:148` PlayRedeem(3) / `:161` OnCryEnd→ResumeStageMusic |

**話者別タイプライター音（既実装）**: 少年=温かい木質(TypBoy 320Hz) / ミナ=澄んだガラス(TypMina 920Hz) / ボス=低くくぐもり(TypBoss 165Hz) / ナレ=無音。`Audio.PlayType(Hud.LineKind)`。

## 8. 未/要対応（残課題）— 2026-07-20 更新（商用フリーBGM導入済み）
**配置規約（2026-07-19 統一）**: 使用する実音源はすべて `audio/bgm_<役割>.ogg`（ピーク-3dB帯・import loop=true。
非ループ原曲は末尾トリム＋頭40ms/尻120ms 極小フェード／**公式ループ版はゲインのみ**）。`BGM/` は
**マスター置き場**（コードから直読みしない。export_presets.cfg の exclude_filter `BGM/*` で配布から一括除外）。
**2026-07-20: 商用ライセンス実音源へ全面差し替え**（ユーザー決定＝旧 Gemini 生成曲は品質理由で引退。
出所・規約記録は `BGM/acquisition_list.md` §6、クレジットは `config/credits.ini` [音楽]）：
- ✅ メニュー=「巡る思い出」(DOVA/蒲鉾さちこ) / **W0道中=「Roll Roll Roll」(DOVA/もっぴーさうんど)＝新規スロット
  `bgm_stage_w0.ogg`・`LoadBgmStageW0`・StageBgm("tutorial") 配線済み＝合成 BgmStage の実音源ゼロ枠が解消** /
  レイ道中=「SO-001」(MusMus/watson・表記必須) / レイ戦=「Falling with You」(DOVA/のる) /
  あかり道中=「6月の雨傘」(甘茶) / あかり戦=「EpicBattle」(PeriTune・CC BY 4.0) /
  こはる道中=「小さな足あと」(甘茶) / **こはる戦=「切ない戦いが始まりそう」(DOVA/シンシンワダ・2026-07-21 導入
  ＝公式ループ版・ゲインのみ。初候補 Red Sapphire は曲削除で断念)** / ミナ戦=「Dramatic5」(PeriTune・CC BY 4.0) /
  ヒカゲ戦=「Frozen Forest」(PeriTune・CC BY 4.0) / Final挿入歌=bgm_final_resolve.ogg（自前インスト続投＝据え置き）
  →**全10スロットの商用ライセンス化が完了**（挿入歌の自前インストのみ据え置き）。
- ✅ FINAL 導入の無音：`StageMina._Ready` に `StopMusic(fade:1.2)` を配線（設計コメント「無音に委ねる」の実装）。
- **合成のまま（意図的）**：`BgmBoss`（Final 冒頭の濁り曲＝「濁り切った未完」）/ `Redeem*` 4種（改心ジングル）/
  `BgmStage`（汎用道中＝現在は全ステージ実音源化により**フォールバック専用**）。
- **ボーカル入り挿入歌**は依然未調達。来たら `audio/bgm_final_resolve.ogg` を差し替えるだけ（結線・キュー・音量は完成済み）。
- ✅ 記憶フラッシュ専用音：`SynthMemoryFlash`（雨＋遠いクラクション二度鳴き＋E5が"す——"と切れる 2.4s）を
  `Audio.PlayMemoryFlash()`（-8dB, SEバス）で `StageImagery.TriggerMemoryFlash()` に同フレーム結線。
- Prologue/Epilogue の独自レンダラは `BgmMenu` 流用中（フェーズ別変奏は未着手・低優先）。
- ⚠️ ライトモチーフ設計（M.I.N.A. C-E-D-G）は**ライセンス曲の音源内容には及ばない**。設計は合成フォール
  バック（Build*）と Redeem* ジングル・タイプライター音に残存。実音源側は「調性・温度・楽器」の一致で選定。
