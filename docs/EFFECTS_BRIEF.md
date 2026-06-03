# 演出実装ブリーフ（弾・ボム等のエフェクト）— コーディング担当Claude向けプロンプト

このドキュメントを丸ごとコーディング担当に渡せば、**この既存プロジェクトにそのまま取り込めるエフェクトコード**を書けるように、言語・規約・フック先・要件をまとめています。

---

## 0. 使用言語・エンジン・取り込み前提（厳守）

- **エンジン**: Godot **4.6.3**（.NET / Mono 版）。
- **言語**: **C#（.NET 8）**。GDScriptではなくC#で書く。
- **コード規約**（既存に合わせる）:
  - 先頭 `using Godot;`、クラスは `public partial class X : <GodotType>`。
  - `_Ready()` / `_Process(double delta)` / `_PhysicsProcess(double delta)` を override。
  - ノード削除は `QueueFree()`。乱数は `GD.Randf()` / `RandomNumberGenerator`。
  - **新規ファイルは `src/fx/` 配下**（例: `src/fx/MuzzleFlash.cs`）。1ファイル1クラス。
  - 既存ファイルへの変更は**最小限**（フック呼び出し1〜数行）に留め、変更箇所を明記する。
- **ビルド/実行で必ず通ること**: `dotnet build algo_shoot.csproj` が0エラー、`godot --headless --path . --quit-after 120` で実行時エラー無し。
- **外部アセット/依存を増やさない**: 画像は使わず**手続き的描画（`_Draw` / `GpuParticles2D`＋ProcessMaterial）**で作る。NuGet追加禁止。

---

## 1. 画面・座標・配色（演出の前提）

- 内部解像度 **384×216**（16:9）、`stretch/mode="canvas_items"`、原点左上・+x右/+y下。横スクロールSTG。
- **配色ルール（役割で固定。混同させない）**:
  - 自機弾＝**白〜水色＋発光**（浄化の光）。
  - 敵弾＝**黒インク＋温色(赤〜マゼンタ)の縁**（暴言の吹き出し）。
  - 浄化/やさしさ＝**ピンク〜薄紫の花びら・光のハート**、シグネチャ紫 **#8A6FD6**。
  - 背景は淡色なので、**弾・エフェクトは縁取り/発光で視認性を確保**（背景に埋もれさせない）。
- 既存スプライトのフィルタ: 自機/敵=Linear。エフェクトは自由（発光は加算ブレンド推奨）。

---

## 2. 既存アーキテクチャ（フック先・呼び出し関係）

ファイルは全て `src/`。エフェクトは基本「**視覚専用ノード（当たり判定なし）をその場に生成→自走→QueueFree**」で実装し、下記の各地点から呼ぶ。

| 既存クラス(ファイル) | 役割 | エフェクトのフック先 |
|---|---|---|
| `BulletPool`(autoload `/root/Pool`) | 弾プール。`Spawn(pos,vel,isEnemy,radius,damage)` / `Despawn(b)` / `DespawnAll()` | （弾本体の見た目強化はBullet側） |
| `Bullet : Area2D` | 弾。`Velocity/IsEnemy/Active/Grazed`、`_Draw`で円描画、画面外で自動Despawn | **弾の見た目（発光/トレイル/吹き出し化）**、生成時/消滅時の小演出 |
| `Player : Area2D`(グループ"player") | 自機。`Fire()`=2way発射、`TryBomb()`=ボム、`OnGrazeAreaEntered()`=かすり、`StartInvincible()`、`_sprite` | **発射のマズルフラッシュ / グレイズ閃光 / ボム発動演出の起点** |
| `Panel : Area2D`(layer16) | 敵に旋回する黒い吹き出し。`Shatter()`で剥がれる | **パネル剥がし(砕け)演出＋光の粒"やさしさ"** |
| `Enemy : Area2D`(グループ"enemies") | 悪魔化した人。全パネル剥がしで `Redeem()`→改心、`Purify()`公開、`Ripple`生成、本体スプライトを浄化後に差し替え | **改心(浄化)バースト演出**（虹フラッシュは既にあり。花びら/ハート噴出を追加） |
| `Ripple : Area2D` | やさしさの波紋（連鎖浄化トリガー、`_Draw`でリング） | 波紋の見栄え強化（任意） |
| `Hud : CanvasLayer` | `Flash()`=全画面フラッシュ、`ShowBanner/ShowMessage`、スコア/コンボ表示 | ボムの全画面フラッシュは既存`Flash()`を活用/強化 |
| `GameManager`(autoload `/root/Game`) | `Score/Combo/Bombs`、`AddPurify/AddGraze/AddBulletCleared/UseBomb` | 演出トリガーの判断材料（必要なら参照） |
| `Main : Node2D`(ルート) | `World`(Node2D)に自機/敵をぶら下げ。**Camera2Dは未設置** | **画面シェイク用にCamera2Dを追加**するならここ |

- **衝突レイヤー**: Player=1, PlayerBullet=2, Enemy=4, EnemyBullet=8, Panel=16。**エフェクトノードは衝突レイヤー/マスクを持たない（visual専用）**。
- **弾は `Pool`(=/root)直下**、敵/自機は `Main/World` 配下。エフェクトは原則 `Main/World` 配下か専用の`Node2D`レイヤーに追加（呼び出し元の `GetParent()` 等で取得）。
- **ZIndex目安**: 背景 -90〜-60、ワールド0、自機10、ボム全画面フラッシュ100(Hud内CanvasLayer)。弾・エフェクトは 1〜20 で重なり調整。

---

## 3. 作ってほしい演出（仕様）

### A. 弾の見た目強化
1. **自機弾（光のインク）**: 白いコア＋水色の外周グロー（加算）。短い**残像トレイル**（2〜4サンプル/フェード）。`Bullet._Draw` を IsEnemy=false 用に強化、またはトレイル用の軽量ノードを付与。
2. **敵弾（暴言の黒インク）**: 角丸の黒い小吹き出し＋温色(赤〜マゼンタ)の縁発光、ごく軽い揺れ/グリッチ。`Bullet._Draw` の IsEnemy=true 用。
3. **発射マズルフラッシュ**: `Player.Fire()` 時に銃口へ一瞬の小さな光リング（0.08s程度で消える）。
4. 性能上、弾本体は**数百発**出る前提。トレイル等は**超軽量**に（GPUParticlesを各弾に付けるのは避け、`_Draw`で数点 or 共有パーティクル）。

### B. 命中・剥がし・グレイズ
5. **パネル剥がし(砕け)**: `Panel.Shatter()` 時に、黒インクの破片が数個飛散→フェード＋**ピンクの光の粒「やさしさ」**が1つふわっと上昇。0.3〜0.5s。
6. **グレイズ閃光**: `Player.OnGrazeAreaEntered()` 時に自機周囲へ小さな水色リング＋微かなチャイム感（音は無し、視覚のみ）。
7. **被弾**: `Player` 被弾時に自機周囲の白フラッシュ＋小さな波紋（残機は減らさないW0仕様のまま、視覚のみ）。

### C. 浄化（改心）バースト
8. `Enemy.Redeem()` 時（既存: 虹フラッシュ＋`Ripple`＋スプライト差し替え）に加え、**花びら／光のハートが放射状に噴出**（8〜16粒、ピンク〜薄紫、上方向に舞ってフェード、0.6〜1.0s）。連鎖時に重なっても破綻しない軽さで。

### D. ボム「魔法陣・解放」（最重要・派手に）
`Player.TryBomb()`（現状: `Game.UseBomb()`→敵弾を`Pool.Despawn`で消去＋画面内敵を`Purify()`＋`Hud.Flash()`＋無敵）に、次の演出を統合:
9. **魔法陣**: algo足元〜全身に**回転する紫の魔法陣（紫十字モチーフ）**が一瞬展開（0.4s）。
10. **光の波（リング）**: algoから画面全体へ**拡大する光のリング**が走る。リングが通過した敵弾は**消去でなく“ピンクの花びら”に変換**して舞い散る（現状は即Despawn→花びら変換演出に置換）。
11. **全画面フラッシュ**: 既存 `Hud.Flash()` を使用/強化（白〜淡紫、すぐ減衰）。
12. **画面シェイク**: 短く弱め（発動0.15s）。**Camera2Dが無いので、`Main`に固定Camera2Dを追加してoffsetを揺らす**実装を提案（背景〜弾まで全部揺れる）。フレーム/タイムスケール非依存で。
13. **ヒットストップ**: ごく短い時間停止（約0.05s）。`Engine.TimeScale` を下げ、`GetTree().CreateTimer(t, processAlways:true, processInPhysics:false, ignoreTimeScale:true)` で実時間復帰させる方式。多重発動でも壊れないこと。

---

## 4. 実装方針・制約

- **プール/使い回し**: 高頻度の小演出（命中・剥がし）は生成/破棄が多いので、`GpuParticles2D`の**OneShot＋再利用**か、軽量`_Draw`ノードを**自前プール**化。低頻度（ボム・改心）は都度生成→QueueFreeでよい。
- **GpuParticles2D**推奨（CPUParticlesでも可）。加算ブレンドのグローは `CanvasItemMaterial { BlendMode = Add }` を使用。
- **タイムスケール耐性**: ヒットストップ中もボム演出やUIが破綻しないよう、必要な箇所は実時間タイマーを使う。
- **既存挙動を壊さない**: 当たり判定・ゲームロジックは不変。エフェクトは視覚のみ。ボムの「敵弾消去」は**機能維持しつつ見た目を花びら変換**に。
- **設定可能に**: 色・粒数・寿命・強度は定数 or `[Export]` でまとめ、後から調整しやすく。
- **小規模・可読性優先**。1演出=1クラス。共通ユーティリティが要るなら `src/fx/Fx.cs`（static ヘルパ: 例 `Fx.Burst(parent, pos, ...)`）に集約可。

---

## 5. 成果物の形式（重要）

1. **新規ファイル**を `src/fx/` に作成（例）:
   - `src/fx/MuzzleFlash.cs` / `src/fx/HitSpark.cs` / `src/fx/KindnessMote.cs` / `src/fx/PurifyBurst.cs` / `src/fx/BombEffect.cs` / `src/fx/GameCamera.cs`(シェイク) / 必要なら `src/fx/Fx.cs`。
2. **既存ファイルへの変更点を、ファイル名＋挿入する数行で明示**（例: 「`Player.cs` の `Fire()` 末尾に `MuzzleFlash.Spawn(this, muzzle);` を追加」「`Panel.cs` の `Shatter()` で `HitSpark`/`KindnessMote` を生成」「`Enemy.cs` の `Redeem()` で `PurifyBurst` を生成」「`Player.cs` の `TryBomb()` をボム演出呼び出しに差し替え（敵弾は消去→花びら変換に）」「`Main.cs` に `GameCamera` を追加」）。
3. すべて**C#・Godot4.6.3でビルド＆起動できる**こと（`.tscn`は不要、コードからインスタンス化）。
4. 各エフェクトの**調整パラメータ（色/粒数/寿命/強度）の場所**を簡潔に記載。

---

## 6. 受け入れ基準（テスト観点）

- `dotnet build` 0エラー / `godot --headless --quit-after 120` 実行時エラー無し。
- 弾が**白〜水色グロー／黒インク吹き出し**で視認しやすい。
- パネル剥がしで**破片＋光の粒**、改心で**花びら/ハート噴出**が出る。
- **ボム**で 魔法陣→光の波→敵弾が花びら化→全画面フラッシュ→弱いシェイク→一瞬ヒットストップ、が一連で起こる。
- 多数の弾・連鎖浄化・ボム連打でも**極端なフレーム落ちが無い**（弾幕STG前提の軽さ）。

---

### 参考（既存コードの所在）
`src/` … `Main.cs / Player.cs / Bullet.cs / BulletPool.cs / Enemy.cs / Panel.cs / Ripple.cs / GameManager.cs / Hud.cs / HeartsBar.cs / StageW0.cs / Background.cs`。
世界観/演出トーンは [CONCEPT_V2.md](CONCEPT_V2.md)（やさしい・非致死・"倒す"でなく"浄化/助ける"）、技術前提は [DEV_W0.md](DEV_W0.md) を参照。
