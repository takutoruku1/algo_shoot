# デザインレバー ↔ 実装ファイル 対応表

「面白さの設計図」（`sakurai-style.md`）の各原理を、どのコードのどの値で動かすかの索引。
**批評・提案の前に、必ず該当ファイルの現在値を読む**（推測で語らない）。

| レバー | 主なファイル | 代表的な値・場所 |
|---|---|---|
| 自機の手触り（移動） | `src/Player.cs` | `NormalSpeed=150` / `FocusSpeed=65`（低速）/ `MoveSpeedMul` |
| 自機の手触り（射撃） | `src/Player.cs` | `FireInterval=0.11` / `ShotDamageBonus` / 弾筋数 `lines` |
| 当たり・かすり（リスクリターンの核） | `src/Player.cs` | `HitRadius=2` / `GrazeRadius=11` / `InvincibleDuration=1.2` |
| 救済（残機・無敵） | `src/Player.cs` | `Lives=3` / 被弾後無敵 |
| 仲間（救出の報酬曲線） | `src/Player.cs` | `MaxFollowers=4` / `SavedPerFollower=3`（救うほど増える）|
| 特殊（ボム的リソース） | `src/Player.cs` | `SpecialCdMax=7` |
| 撃破・被弾の手応え（快感） | `src/Player.cs` / `src/fx/` / `src/Ripple.cs` / `src/Enemy.cs` | ヒットストップ / `FxLayer` ダメージ数字 / 発光 |
| 弾の見た目・視認性 | `src/Bullet.cs` | `BulletShape`（Orb/Diamond/Star/Ring/Needle/Rice）/ `Radius` / `HomingTurnRate=150`(deg/s) |
| 道中の密度・テンポ | `src/Spawner.cs` | `RampDur=60`（最大密度まで）/ `IntervalStart=2.0`→`IntervalEnd=0.9` / `MaxAlive=9` |
| 敵・ボスの弾幕 | `src/Enemy.cs` / `src/BossRei.cs` 等 | `EnemyBulletSpeed=90` / `SpinSpeed` / パネル発射 / スペルカード |
| ボスHP（バー本数は難易度で変動） | `src/GameManager.cs` | `DiffBarBonus`: 通常ボスEasy2/Normal4/Hard5/Lunatic6本、ラスボス格は+2本。1本=BarHp固定、**本数の調整は弾数調整とは別軸の「殴る回数」調整として意図的** |
| 難易度カーブ | `src/GameManager.cs` | `BulletCountMul` / `BulletSpeedMul` / `DanmakuIntervalMul` / `StartLives` / `StartBombs`（Diff別）|
| 難易度選択UI | `src/DiffSelect.cs` | Tiers / Lunatic 解放条件 |
| 経済・リスクリターンの変換 | `src/Shop.cs` / `src/GameManager.cs` | `GetUpgradeCost` / `ShotMode`（Rapid/Spread/Homing）/ `SpreadWays` / `HomingShots` |
| 進行・達成感 | `src/GameManager.cs` | `StageTarget=24` / `StageProgress` / `Warmth` / ステージ解放 |
| 情報の提示（視認性） | `src/Hud.cs` | 残機・ボム・進行・HPバー・グレイズ表示 |
| 入力 | `src/Pad.cs` | 操作マッピング・レスポンス |

## 既存方針（壊さないこと）
- **難易度は「弾の量・速度・間隔」で調整**するのが基本だが、ボスHPバー本数（`GameManager.DiffBarBonus`）も難易度で意図的に変動する（通常ボスEasy2〜Lunatic6本）。「1本あたりのHP(BarHp)は固定」という意味で、総HP・撃破に要する時間は難易度で変わる（`src/GameManager.cs:70-75` のコメント参照）。
- 浄化＝「倒す」ではなく「届ける／救う」。撃破演出はこの世界観に合わせる。
- 数値変更を提案するときは、変更前の値・変更後の値・狙う体験の変化を必ずセットで示す。
