# 音イベント ↔ 実装ファイル 対応表（sound-map）

「音の設計図」（`mitsuda-style.md`）の各原理を、どのコードのどこで鳴らすかの索引。
**設計・実装の前に、必ず該当ファイルの現在の演出（`FxLayer` 呼び出し・既存値）を読む**（推測で音を置かない）。
`/sakurai` の design-map、`/maeda` の scene-map と三位一体。

> ⚠️ 行番号は作成時点の目安。**実装前に必ず現在のファイルを開いて確認**する（コードは動く）。

---

## 0. 現状＝完全無音（最重要事実）
- 音声ファイル（.wav/.ogg/.mp3）が**リポジトリに0件**。
- `project.godot` に **AudioBusLayout（default_bus_layout.tres）の指定なし** → Godot 既定の **Master バスのみ**存在。
- `AudioStreamPlayer` / `AudioStreamPlayer2D` の使用箇所 **0件**。
- `Settings.cs` に音量スライダーが **5本** 定義済みだが、配線されているのは Master のみ：
  - 定義: `src/Settings.cs:58-62` — `master`(80) / `bgm`(70) / `se`(85) / `voice`(90) / `amb`(55「心象世界のノイズ」)
  - 配線: `src/Settings.cs:153-156` — `case "master"` が `AudioServer.GetBusIndex("Master")` を引いて音量反映。**bgm/se/voice/amb は受け皿のバスが無く、動かしても無音**。
  - 関連: `src/Settings.cs:69` `msg`（メッセージ速度 遅/中/速）, `:70` `auto`（オート会話送り）, `:67` `shake`（画面振動）, `:68` `flash`（被弾フラッシュ）, `:80` `reduceflash`（明滅を抑える）— 会話/被弾演出の同期・アクセシビリティに関わる。
- **第一の仕事**: バス（Music/SE/Voice/Amb）を作り、5スライダーを配線する（`mitsuda-implementation.md` §バス）。

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
| STAGE1 レイ | `Rei.tscn` / `src/StageRei.cs` `src/BossRei.cs` | 道中＝推進、ボス＝緊張変奏 |
| STAGE2 あかり | `Akari.tscn` / `src/StageAkari.cs` `src/BossAkari.cs` | 雨・教室の生楽器寄り。記憶フラッシュ（§4）と同期 |
| STAGE3 こはる | `Koharu.tscn` / `src/StageKoharu.cs` `src/BossKoharu.cs` | 台所の温もり→その後の冷感 |
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
