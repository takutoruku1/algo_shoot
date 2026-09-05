---
name: qa-autoplay
description: Auto-play this Godot/.NET game headlessly and scan for bugs — weird/misplaced collision (当たり判定), progression deadlocks (進行不能), runtime exceptions, bullet leaks, and FPS drops. Use when the user asks to QA/playtest for bugs, auto-check for defects, or hunt collision/softlock issues (e.g. "オートプレイでバグチェックして", "当たり判定がおかしくないか調べて", "進行不能バグを探して", "不具合チェックして", "QAして").
---

# qa-autoplay — 自動プレイでバグ検出

`QaPilot` オートロード（`src/QaPilot.cs`）が、ゲーム本体を一切いじらずに合成入力で自動プレイしながら、毎フレーム シーンツリーを観測して異常をコンソールへ吐く。これを**ヘッドレスで複数パス走らせてログを解析**し、ユーザーへ所見を報告する skill。

`QaPilot` は起動引数（`--` の後ろ）に `--qa` が無ければ完全に無効（通常プレイ・デモ録画・配布ビルドには一切影響しない）。

## 何を検出するか（ログのタグ）
- `[QA-WARN] suspicious-hit` … 残機が減った瞬間、最寄りの敵弾も敵も自機表面から `>4px` 離れていた ＝ **変な所に当たり判定がある疑い**（直前フレームの距離で判定するので被弾時の弾消滅レースは回避済み）。
- `[QA-WARN] stuck` … シーンも浄化数もボスHPも会話の開閉も `40秒` 動かない ＝ **進行不能の疑い**。
- `[QA-ERROR] player-oob` … 自機がプレイ領域外 or 座標が NaN/Inf。
- `[QA-WARN] bullet-flood` … 敵弾が `1200` 超（弾リーク/パフォーマンス）。
- `[QA-WARN] low-fps` … FPS が `25` 未満を `3秒` 継続。
- `ERROR:` / `SCRIPT ERROR:` … Godot/C# 側の実行時エラー（ヌル参照・例外・物理状態の不正操作など）。これは QaPilot ではなくゲーム本体由来。
- `[QA] ...` … 1秒ごとのハートビート（scene/lives/bombs/purified/boss/弾数/fps/pos）。時系列を追う用。
- `[QA] hit (ok)` … 正常な被弾（距離が近い）。所見ではない。
- `[QA-SUMMARY] ...` … 走行終了時の警告件数まとめ。

## 変数
- Godot（mono・エディタ兼用）:
  `C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe`
  ※無ければ `Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "Godot_v*mono*win64.exe"`。
- プロジェクト: `d:\dev\algo_shoot`
- ログ出力先: `d:\dev\algo_shoot\build\qa\`（`build/` は gitignore 済み）
- 戦闘シーン: `res://Akari.tscn`（STAGE1）, `res://Koharu.tscn`（STAGE2）, `res://Rei.tscn`（STAGE3）
- シーンチェーン: Prologue → Akari → Koharu → Rei → Final → Epilogue（各面の間にハブを挟む）

## QaPilot の引数
- `--qa` … 有効化（必須）。
- `--assist` … `--god`＋`--aim`。自機周囲の敵弾を消して**死なず**、最寄りの敵へ自動DPSして**確実にボスを倒し最後まで進む**。進行不能の検出に使う。
- `--god` / `--aim` … 個別指定。
- `--easy` … 難易度を EASY 固定（クリアしやすくする）。
- `--seconds N` … 尺（省略時 240）。到達しなければその時点で Quit。
- `--quit` … Epilogue 到達（全シーン走破＝正常完走）で Quit。

> **重要**: `当たり判定`を見るパスでは `--assist`/`--god` を**付けない**（god は敵弾を消すので被弾しなくなり検出できない）。`進行不能`を見るパスでは `--assist` を付ける（ボットが下手で進めないのと、本物の進行不能を区別するため）。

## 手順

> **Linux コンテナ（夜間クラウド実行など）では `tools/qa-bootstrap.sh` を使う。** 下の手順は Windows のローカル環境前提。
> コンテナには Godot が無く `.godot/` も gitignore なので、素で起動すると**インポートが走り切らず無出力のまま固まる**。
> `dotnet` SDK が無い環境だと `--import` が signal 11 で即死して同じ無出力停止になるので、`qa-bootstrap.sh` は冒頭で `command -v dotnet` を確認し、無ければ `apt-get install -y dotnet-sdk-8.0` を自動で試みる（失敗時は理由を出して exit するので、その場合は手動導入が必要）。
> ```bash
> tools/qa-bootstrap.sh                        # Godot 取得＋初回 import まで（既にあれば再取得しない）
> tools/qa-bootstrap.sh Rei --hard --seconds 45   # res://Rei.tscn を QA 起動 → build/qa/Rei.log
> ```
> シーン名は拡張子なし（`Rei` / `Prologue` / `Stage0` …）。`--qa` と `--quit` は自動で付く。
> 手で叩く場合は**シーンパス → `--` 区切り → QaPilot 引数**の順を守ること（`--` を抜くと Godot 側が引数を食って無出力になる）。

1. **ビルド**（壊れたコードだと即落ちる）:
   ```bash
   cd /d/dev/algo_shoot && dotnet build -clp:ErrorsOnly   # 0 Error(s)
   ```

2. **ログ出力先を用意**:
   ```bash
   mkdir -p /d/dev/algo_shoot/build/qa
   ```

3. **パスA：進行テスト（全シーン走破）** — `--assist` で死なず確実にクリアして最後まで進む。途中で 40 秒以上どこも進まなければ `stuck`＝進行不能。例外もここで広く拾う。Bash ツールで実行（exe のパスにスペースあり→クォート。stdout+stderr をログへ）:
   ```bash
   GODOT="/c/Users/takut/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.6.3-stable_mono_win64/Godot_v4.6.3-stable_mono_win64.exe"
   "$GODOT" --headless --path /d/dev/algo_shoot res://Prologue.tscn -- --qa --assist --easy --seconds 300 --quit > /d/dev/algo_shoot/build/qa/progress.log 2>&1
   ```

4. **パスB：当たり判定テスト（各戦闘シーンを個別に・god 無し）** — 本物の被弾を起こして `suspicious-hit` を見る。シーンごとに直接起動して 40 秒ずつ:
   ```bash
   for S in Rei Akari Koharu; do
     "$GODOT" --headless --path /d/dev/algo_shoot res://$S.tscn -- --qa --easy --seconds 40 --quit > /d/dev/algo_shoot/build/qa/collide_$S.log 2>&1
   done
   ```

5. **解析** — 各ログから所見を抽出（ハートビートの大量行は除外）:
   ```bash
   cd /d/dev/algo_shoot/build/qa
   echo "=== WARN/ERROR (QaPilot) ==="
   grep -hE "\[QA-WARN\]|\[QA-ERROR\]" *.log | sort | uniq -c | sort -rn
   echo "=== SUMMARY ==="
   grep -hE "\[QA-SUMMARY\]" *.log
   echo "=== engine errors (deduped) ==="
   grep -hE "SCRIPT ERROR:|ERROR:|Unhandled exception|NullReference|ObjectDisposed|IndexOutOfRange" *.log | sort | uniq -c | sort -rn | head -30
   echo "=== scene timeline (progress pass) ==="
   grep -E "\[QA\] scene ->|reached Epilogue|budget reached" progress.log
   ```
   - 進行テストのログ末尾に `reached Epilogue` があれば**全シーン走破＝進行は通った**。`budget reached` で止まっていたら、どのシーンで止まったか（最後の `scene ->`）と `stuck` 警告を確認＝進行不能の疑い。

6. **報告** — ユーザーへ日本語で:
   - `suspicious-hit` … どのシーン・座標で、敵弾/敵が何 px 離れて被弾したか（変な当たり判定の候補）。
   - `stuck` … どのシーンで何秒止まったか（進行不能）。`reached Epilogue` が出ていれば「全シーン走破OK」も明記。
   - `player-oob` / `bullet-flood` / `low-fps` … 該当があれば。
   - `ERROR:` / `SCRIPT ERROR:` … 代表メッセージと件数。多くは重複するので `uniq -c` の上位を提示。
   - 所見ゼロなら「全パス clean、Epilogue まで走破」と明言する。

## 解釈のコツ・既知事項
- **`ERROR: Function blocked during in/out signal. Use set_deferred(...)` は本物の所見**。弾やパネルが衝突シグナルの最中に `Monitoring`/`Monitorable` を直接代入していると出る（例: `Bullet.cs` の Despawn は直接代入。`Panel.cs` は `SetDeferred` を使っていて正しい）。高頻度で出るので `uniq -c` でまとめて報告し、必要なら `SetDeferred` 化を提案する。
- `suspicious-hit` が出ても、ボス本体は `BodyRadius` を安全側（14px 見積り）で見ているので、巨大な当たり判定のボスだと稀に誤検知しうる。座標とシーンを見て妥当性を判断する。
- 進行テストで `stuck` が出ても、`--assist` 中なら**ボットの腕の問題ではなく本物の進行不能**（god で死なず aim で削り続けているため）。会話が送れない・クリア条件が成立しない等を疑う。
- ヘッドレスで実行する（描画不要・速い・stdout を確実に拾える）。画面で挙動を目視したいときは `--headless` を外し、`play-game` skill 側で `-- --qa --assist` を付けて起動する。
- 個別シーン直起動（`res://Rei.tscn` 等）は `play-game` と同じ要領。Prologue から通すのはパスAだけでよい。
- 尺は調整可。広く回したいときはパスA の `--seconds` を伸ばす／難易度 `--easy` を外す。
- `dotnet`（.NET 8 SDK）必須。mono バイナリは初回 C# を自動ビルドするが、手順1で先に通す。

## セーブ保護（必須）
自動走行はオートセーブ枠と既読データを実プレイと同じ場所（`%APPDATA%\Godot\app_userdata\algo_shoot\`）に書き込む。**走行の前に `save_0.json` と `read.json` を同ディレクトリに `.bak` として退避し、全走行が終わったら復元すること**（手動スロット save_1〜3 には触れない）。復元まで含めて1タスク。
