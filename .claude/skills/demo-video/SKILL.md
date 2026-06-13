---
name: demo-video
description: Record an autoplay demo video (mp4) of this Godot/.NET game. The built-in DemoPilot autoplays a stage as a NO-DAMAGE SPEED-CLEAR — closed-loop bullet dodging + emergency bomb keep it from ever getting hit, while it holds the boss firing line for max DPS and blitzes dialogue at the fastest allowed rate. Runs on Easy by default (boss HP is difficulty-independent, so Easy clears just as fast but with sparser bullets = safer). Godot's Movie Maker records it, then ffmpeg transcodes to a small mp4. Use when the user asks to make/redo a demo/gameplay/promo video (e.g. "デモ動画を作って", "プレイ動画を録って", "デモ録画して", "STAGE1のデモ動画", "もっと長く録って"). NOTE: dialogue is skipped fast — if the user wants to SHOWCASE the STORY slowly, raise StoryPeriod in src/DemoPilot.cs.
---

# demo-video — 自動操縦のデモプレイ動画(mp4)を録る

このプロジェクト（Godot 4.6.3 mono / C#・.NET 8）を **自動操縦で勝手にプレイさせ**、
Godot の Movie Maker モードで録画 → ffmpeg で mp4 に圧縮する手順。手で操作する必要はない。

仕組み：オートロード `DemoPilot`（[src/DemoPilot.cs](../../../src/DemoPilot.cs)・`project.godot` に登録済み）が、
起動時のユーザ引数に `--demo` があるときだけ合成入力を流す。**目的は「ノーダメージで最速クリア」**。
被弾は絶対に避けつつ（死ぬと話が中断・リスタートする）、無駄な時間を一切作らないよう振る舞う：

- **閉ループ回避**：毎フレーム近傍の敵弾を速度ごと観測し、16方向＋静止を弾道シミュレーションして
  「最も弾から離れられる動き」を選ぶ（昔のサイン波ウィーブ＝弾を見ない開ループではない）。
- **攻撃定位置に張り付き**：脅威が無いときは左寄り＋ボスのYに合わせた定位置へ寄って撃ち続け、
  最大DPSでボスのパネルを剥がす。回避で離れたらすぐ撃ち位置へ戻る。
- **会話は最速スキップ**：各行の最短ゲート（0.25s/行）ぎりぎりで送る。会話中は弾も自機も止まるので安全。
- **緊急ボム**：どう動いても被弾が避けられない瞬間だけ、残っていればボム（画面弾消し＋無敵）を1発。
- **既定 Easy 難易度**（弾数55%・弾速72%・残機5・ボム5）。**ボスHPは難易度非依存で固定**（弾数だけが変わる）
  なので Easy でもクリア所要は変わらず、弾が薄いぶん回避に取られる時間が減る＝むしろ最速かつノーダメ向き。
  難しい弾幕を見せたいときだけ `--normal` / `--hard`。

指定秒で自分で `Quit()` して録画を確定する。ゲーム本体のプレイコードは一切書き換えていない
（入力をポーリングしている各シーンに、本物のキーと同じ経路で合成イベントを注入しているだけ）。

> ※「ストーリーをゆっくり読ませる動画」が欲しいときはこの限りでない。[src/DemoPilot.cs](../../../src/DemoPilot.cs) の
>   `StoryPeriod` を大きく（例 0.85）戻すと会話送りが遅くなる。

## 変数
- Godot 実行ファイル（mono・エディタ兼用）:
  `C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe`
  ※見つからなければ `Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "Godot_v*mono*win64.exe"`。
- ffmpeg（winget導入。手順1で実体パスを解決する）:
  `Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "ffmpeg.exe"` の先頭。
- プロジェクト: `d:\dev\algo_shoot`
- 録画するシーン（既定 STAGE1）: `res://Rei.tscn`。他に `res://Akari.tscn`（STAGE2）, `res://Koharu.tscn`（STAGE3）。
  ※タイトルから流したいなら `res://Prologue.tscn`（既定難易度でダイブする）。
- 尺（既定 80秒）: `--seconds N` で指定。**会話を最速スキップする**ので、STAGE1 でも戦闘は 5〜10秒で始まり、
  ノーダメで撃ち続ければ 1ステージは 60秒前後で片づく。全編（複数ステージ）を録るなら 120〜180秒推奨。
- 難易度（既定 Easy）: `--demo` の後ろに `--normal` / `--hard` / `--easy`。既定の Easy はノーダメ完走を
  狙った安全運転。難しい弾幕を見せたいときだけ `--normal`（被弾リスクは上がる）。
- 中間 AVI（無圧縮・巨大／使い捨て）: `build\demo.avi`
- 出力 mp4: `build\demo.mp4`

## 手順

1. **C# ビルド確認 ＆ ffmpeg を用意**:
   ```powershell
   dotnet build   # 0 Error(s) を確認（壊れたまま録画すると即落ちする）
   $ff = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "ffmpeg.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
   if (-not $ff) {
     winget install --id Gyan.FFmpeg -e --accept-source-agreements --accept-package-agreements --disable-interactivity | Out-Null
     $ff = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "ffmpeg.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
   }
   "ffmpeg: $ff"   # 空なら導入失敗 → ユーザーに相談
   ```
   ※winget 導入直後は PATH が未更新なので、`ffmpeg` という名前では呼ばず **実体パス `$ff`** で呼ぶこと。

2. **録画**（PowerShell。Movie Maker は実時間より遅い＝80秒の動画でも収録に数十秒かかる。終わるまで待つ）:
   ```powershell
   $godot = "C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
   $scene = "res://Rei.tscn"   # 録りたいシーン
   $sec   = 80                  # 尺（秒）
   New-Item -ItemType Directory -Force -Path "d:\dev\algo_shoot\build" | Out-Null
   $avi = "d:\dev\algo_shoot\build\demo.avi"
   if (Test-Path $avi) { Remove-Item $avi -Force }
   $args = @('--path','d:\dev\algo_shoot','--write-movie',$avi,'--fixed-fps','60',$scene,'--','--demo','--seconds',"$sec")
   $p = Start-Process $godot -ArgumentList $args -PassThru `
        -RedirectStandardOutput "d:\dev\algo_shoot\build\demo.log" -RedirectStandardError "d:\dev\algo_shoot\build\demo.err"
   $p.WaitForExit(600000) | Out-Null   # 長尺なら余裕を持って待つ
   if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; "TIMEOUT" }
   "avi MB: $([math]::Round((Get-Item $avi).Length/1MB,1))"   # 出ていれば録画成功（DemoPilot のログは demo.log）
   ```
   - 引数の順序が肝：`--write-movie` `--fixed-fps` `<scene>` は **エンジン引数として `--` の前**、
     `--demo` `--seconds N` は **`--` の後ろ**（`OS.GetCmdlineUserArgs()` に渡る）。
   - `--headless` は付けない（Movie Maker は描画が要る。headless だと真っ黒になる）。

3. **mp4 に圧縮**（H.264 / yuv420p。AVI は無圧縮で巨大なので変換後は消す）:
   ```powershell
   $mp4 = "d:\dev\algo_shoot\build\demo.mp4"
   & $ff -y -i $avi -c:v libx264 -pix_fmt yuv420p -crf 20 -c:a aac -b:a 128k $mp4 *> $null
   if (Test-Path $mp4) { Remove-Item $avi -Force }   # 巨大な中間ファイルを掃除
   "mp4 MB: $([math]::Round((Get-Item $mp4).Length/1MB,2))"
   ```
   - X(Twitter)等で軽くしたい/長尺なら `-crf 23` や `-vf "scale=768:-2"` で更に縮む。
   - 無音で良ければ `-c:a aac -b:a 128k` を `-an` に。

4. **確認**（任意）：中盤フレームを抜いて、戦闘が映っているか目視する。
   ```powershell
   & $ff -y -ss 38 -i $mp4 -frames:v 1 "d:\dev\algo_shoot\build\demo_frame.png" *> $null
   ```
   抜いた png を Read ツールで開いて、自機のショットと敵弾が出ているか確認 → OK ならユーザーへ mp4 のパス・尺・サイズを報告。

## 注意・つまずき
- **`build/` は `.gitignore` 済み**。動画も AVI もコミットしない。中間 AVI は手順3で必ず消す（8秒で約38MB＝1分で約280MB）。
- Movie Maker は **実時間の約30%速度**で収録する（80秒の動画＝実時間で約4〜5分）。`WaitForExit` のタイムアウトは長めに。
- 1フレームも落とさず固定60fpsで録れる（Movie Maker は描画タイミングと録画を分離するため）。なので尺＝指定 `--seconds` どおりになる。
- 真っ黒な動画になる典型：`--headless` を付けてしまった／ビルドエラーで即落ちした（手順1で弾く）／`--demo` を `--` の **前**に置いてユーザ引数として渡っていない。
- 自機が動かない・撃たない typeのときは `DemoPilot` が無効化されている（autoload 登録漏れ or `--demo` 未到達）。`demo.log` に `[DemoPilot] active... difficulty=EASY` が出ているか見る。
- 移動は[src/DemoPilot.cs](../../../src/DemoPilot.cs)の統一スコアラ（16方向＋静止＋定位置寄せを弾道シミュレーションし `安全度＋ホーム接近` で1手選ぶ）。挙動の調整つまみ：`ComfortGap`（小さいほど弾の近くまで踏ん張って撃つ＝攻め／大きいほど早めに離れる＝安全）, `HomeBonus`（射線(ボスY)への張り付き強さ＝DPS）, `Horizon`（先読み秒数）, `PanicGap`（緊急ボムを切る閾値）。ノーダメが崩れるなら `ComfortGap` を上げる、DPSが足りず削り切れないなら `HomeBonus` を上げる。
- **録画が `--seconds` の途中で終わる（`demo.log` に `[DemoPilot] done.` が出ていない／AVIが妙に小さい）**：浄化の連鎖が物理シグナルのフラッシュ中にコリジョン状態を書き換える Godot のレース。根因の Ripple 生成は遅延化して直した（[src/Enemy.cs](../../../src/Enemy.cs) の `Redeem` で `CallDeferred(AddChild)`）が、ごく稀に再発しうる。**そのときは録り直せば通る**（ヘッドレス `--headless`（ただし真っ黒）／ウィンドウ実行では出ない＝ムービーモード固有）。
- セリフ送りの速さは `StoryPeriod`（既定 0.30s/行＝最速。各行に 0.25s の最短ゲートがあるのでこれ未満にしても速くならない）。ストーリーを読ませたいなら 0.85 など大きくする。会話中は弾も自機も止まるのでノーダメには影響しない。
- ffmpeg は winget の `Gyan.FFmpeg`。導入済みでも PATH に乗っていないことがあるので、毎回 `$ff`（実体パス）で呼ぶ。
