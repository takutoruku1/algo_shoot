# デザインレバー ↔ 実装ファイル 対応表

「面白さの設計図」（`sakurai-style.md`）の各原理を、どのコードのどの値で動かすかの索引。
**批評・提案の前に、必ず該当ファイルの現在値を読む**（推測で語らない）。

| レバー | 主なファイル | 代表的な値・場所 |
|---|---|---|
| 自機の手触り（移動） | `src/Player.cs` | `NormalSpeed=75`（速度は1本。**低速移動は 2026-09-13 に廃止**）/ `LockMoveMul=0.8`（ロック中は足が重い）/ `MoveSpeedMul`（ショップ #9 で 1.5倍） |
| 自機の手触り（射撃） | `src/Player.cs` | **ショットはオート発射**（射撃ボタン無し）/ `FireInterval=0.13` / `ChargeNeed=0.6` `ChargeDamageMul=4`（溜め打ち）/ 弾筋数 `lines` |
| 当たり・かすり（リスクリターンの核） | `src/Player.cs` | `HitRadius=2`（ショップ #7 `HitRadiusMul=0.5` で 1.0）/ `GrazeRadius=11` / `InvincibleDuration=1.2` |
| 救済（残機・無敵） | `src/Player.cs` / `src/GameManager.cs` | `Lives`＝`StartLives`（`BaseLivesFor=3` 全難易度共通 ＋ ショップ♥×2段 ＋ ジョブ補正）/ 被弾後無敵は `JobDef.HitInvulSec`（基準1.2秒） |
| 仲間（救出の報酬曲線） | `src/Player.cs` | `MaxFollowers=4` / `SavedPerFollower=3`。**ただし `StageFollowersEnabled=false`（2026-09-06 以降OFF）＝現状は増えない** |
| ボム | `src/Player.cs` / `src/GameManager.cs` | `StartBombs`（`BaseBombsFor=1` 全難易度共通 ＋ ショップ #3）/ `BombPurifyRewardCap=3`（1発で報酬が付く体数） |
| 撃破・被弾の手応え（快感） | `src/Player.cs` / `src/fx/` / `src/Ripple.cs` / `src/Enemy.cs` | ヒットストップ / `FxLayer` ダメージ数字 / 発光 |
| 撃破の欠片（経済へ変換） | `src/ScoreShards.cs` / `src/fx/FxLayer.cs` / `src/GameManager.cs` | 欠片は Score とショップ通貨(`Imp`)を別々に運ぶ（2026-09-17 経済改修）/ `Capacity=640` / `MagnetRadius=48` / `Lifetime=5` / `BeginRush`（撃破直後の強制吸引）/ `DifficultyShardMulFor`(0.8/1/1.3/1.6＝粒数とScoreのみ) |
| 弾の見た目・視認性 | `src/Bullet.cs` | `BulletShape`（Orb/Diamond/Star/Ring/Needle/Rice）/ `Radius` / `HomingTurnRate=150`（既定。ホーミング系は `GameManager.HomingTurnRateOverride=200` で上書き） |
| 道中の密度・テンポ | `src/Spawner.cs` / `src/GameManager.cs` | `RampDur=28`（最大密度まで）/ `IntervalStart=2.0`→`IntervalEnd=0.8` / 同時上限は `MaxAliveEnemies`（難易度別 6/8/10/12。Spawner の `MaxAliveFallback=8` は取得失敗時の既定） |
| 敵の出現方向（読みの負荷） | `src/Spawner.cs` | `PickEdge`：Easy 右60/上20/下20（旧仕様据え置き）/ Normal 右65/上17.5/下17.5（**後ろを除く3方向**）/ Hard 左7%・Lunatic 左12%（**4方向**）。左湧きは予告付き（`LeftWarnX` / `LeftWarnLead=0.4`） |
| 前のめり進行（位置のリスクリターン） | `src/GameManager.cs` | `PosFactor`（左0.55→右1.60）/ `SpawnRateMul`（左0.60→右1.50）＝右へ攻めるほど進行も敵も増える |
| 敵・ボスの弾幕 | `src/Enemy.cs` / `src/BossRei.cs` 等 | `EnemyBulletSpeed=90`（ボスは `BossTuning` で個別 80〜95）/ `Dn()` `Di()`（難易度スケール）/ パネル発射 / スペルカード |
| ボスHP（**難易度依存**） | `src/Enemy.cs` / `src/GameManager.cs` | `BarHp=100` × `DiffBarBonus`（Easy2/Normal4/Hard5/Lunatic6、ラスボス格は +2）。**2026-09 に「難易度非依存」方針から変更済み** |
| 難易度カーブ | `src/GameManager.cs` | `BulletCountMul`(0.38/0.7/1.1/1.9) / `BulletSpeedMul`(0.62/0.85/1.05/1.18) / `DanmakuIntervalMul`(2.1/1.35/1.0/0.85) / `SpawnIntervalMul`(1.3/1.0/0.78/0.62) / `MaxAliveEnemies`(6/8/10/12) / `DiffBarBonus`。**♥・ボムの基礎値は難易度で変えない**（3/1 固定） |
| 難易度選択UI | `src/DiffSelect.cs` | `Tiers` / `IsLunaticUnlocked`（`LunaticFollowerReq=200` or 火力2倍所持） |
| 経済・リスクリターンの変換 | `src/Shop.cs` / `src/GameManager.cs` | **一本道13段**（`Upgrades` 配列が唯一の正典。価格 150→4,000 の単調増加・各段 `MaxLevel=1` 買い切り・`ParentId` が直前の段）/ `GetUpgradeCost` / `DifficultyImpressionMulFor`(0.7/1/1.6/3.0) / `MoneyGainMul=2` / クリア報酬400 |
| 撃ち方（ジョブが決める） | `src/GameManager.cs` / `src/Job.cs` | `ShotMode`（Rapid/Spread/Homing/Accel）は**ジョブ選択に従属**（切替操作は 2026-09-13 に廃止）/ `SpreadWays=5+ExtraLines*2` / `HomingShots=2+ExtraLines` |
| 進行・達成感 | `src/GameManager.cs` | `StageTarget`（既定24。各ステージ Root が `SetStageTarget` で上書き）/ `StageProgress`（撃破率＋前のめり先行）/ `Warmth` / ステージ解放 |
| 情報の提示（視認性） | `src/Hud.cs` | 残機・ボム・進行・HPバー・グレイズ表示 |
| 入力 | `src/Player.cs` / `src/Pad.cs` / `src/HowToPlay.cs` | 移動=矢印/WASD・L スティック / ロック送り=`F`・RB・左クリック短押し / **ロック解除=`G`・R3・右クリック（2026-09-17 追加）** / 溜め打ち=`C`・`Y`・左クリック長押し / 集中モード=`V`・L1・ホイール/サイドボタン / ボム=`X`・`X`・中クリック / 回避=`Alt`・L3・右クリック |

## 既存方針（壊さないこと）
- **難易度は「弾の量・速度・間隔・出現密度・出現方向」で調整**する。♥（3）とボム（1）の基礎値は全難易度共通（2026-09-15 ユーザー決定）。
  - ※ボスHP（バー本数）は 2026-09 に難易度依存へ変更済み（旧「剥がし回数は固定」方針は失効）。
- 浄化＝「倒す」ではなく「届ける／救う」。撃破演出はこの世界観に合わせる。
- 浄化ゲージは**撃破に紐づく**。撃破0では動かない（時間ぶんの先行は `LeadGate` で撃破率に比例させる＝ラベルと実装を一致させる不変条件）。
- 数値変更を提案するときは、変更前の値・変更後の値・狙う体験の変化を必ずセットで示す。

## 廃止済み（提案の前提にしないこと）
- **低速移動（Focus 移動）**（2026-09-13 廃止。`src/Player.cs:11-14`）
- **ショットモード切替操作**（2026-09-13 廃止。撃ち方はジョブが決める）
- **ヒカゲ専用スキル／`SpecialCdMax`**（配線ごと撤去。`C` は溜め打ちへ）
- **後方弾**（2026-09-15 に発射停止。`src/HowToPlay.cs:365`）
- **分岐する強化の木（70ノード・排他・振り直し）**（2026-09-13 に一本道13段へ畳んだ）
- **ラン中のフォロワー増加**（`StageFollowersEnabled=false`。SNS のフォロワー数とは別物）
