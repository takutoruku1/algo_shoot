---
name: screenshots
description: Capture per-screen PNG screenshots of this Godot/.NET game for UI review. The built-in Shot autoload (src/Shot.cs, --shot) saves the root viewport to PNG at given times, then quits — no manual play needed. Static UI screens (Hub/Shop/DiffSelect) are shot WITHOUT autoplay (they auto-navigate under --demo/--qa); gameplay & cutscenes are shot WITH --demo so the autopilot reaches battle/dialogue states. Use when the user asks to grab/refresh screenshots of the game's screens, capture the UI of each screen, or get stills for a UI redesign (e.g. "各画面のスクショを撮って", "UIのスクリーンショット取得して", "ハブ画面をキャプチャして", "全画面のスクショ撮り直して", "ステージのHUDのスクショ").
---

# screenshots — 各画面のスクリーンショット(PNG)を撮る

このプロジェクト（Godot 4.6.3 mono / C#・.NET 8）の **各画面を1枚ずつPNGで撮る**手順。
UI見直し用に主要画面の静止画を一括取得する。手で操作する必要はない。

仕組み：オートロード `Shot`（[src/Shot.cs](../../../src/Shot.cs)・`project.godot` に登録済み）が、
起動時のユーザ引数に `--shot` があるときだけ有効化し、**指定時刻にルートビューポートを PNG 保存**して、
撮り終えたら `Quit()` する。ゲーム本体・他オートロード（DemoPilot/QaPilot）には一切干渉しない
（`--shot` 無効時は即停止）。録画（demo-video）と違い実時間で動くので速い。

> Movie Maker（demo-video）ではなく、ビューポートを直接 PNG 保存する。`--headless` は付けない
> （真っ黒になる）。ウィンドウ表示で1〜2秒走らせて撮る。

## Shot の引数（`--` の後ろに渡す）
- `--shot` … 有効化（必須）
- `--shot-at "t1,t2,..."` … 撮影時刻（秒）のカンマ区切り。省略時 `1.0`。最後の撮影後に自動 Quit。
- `--shot-out <dir>` … 出力ディレクトリ（絶対パス推奨）。
- `--shot-name <prefix>` … ファイル接頭辞。出力は `<dir>/<prefix>_00.png`, `_01.png`, …

## 2つの撮り方（重要）
- **静的UI画面（Hub / Shop / DiffSelect）**：`--demo`/`--qa` を **付けない**。
  これらは autoplay 中だと即座に自動遷移してしまう（Shop→Hub、Hub→ダイブ、DiffSelect→ステージ）。
  初期フレームをそのまま撮ればよいので `--shot` 単体で。
- **ゲームプレイ / カットシーン（各ステージ・Prologue・Epilogue・Final）**：`--demo` を **併用**。
  DemoPilot が自動操縦で進める（会話送り Z・移動・射撃）ので、会話／戦闘／演出の状態を撮れる。
  Shot の最終撮影で DemoPilot より先に Quit する（`--seconds` 到達前に終わる）。

## 変数
- Godot 実行ファイル（mono・エディタ兼用）:
  `C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe`
  ※見つからなければ `Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "Godot_v*mono*win64.exe"`。
- プロジェクト: `d:\dev\algo_shoot`
- 出力先（既定）: `d:\dev\algo_shoot\build\shots`（`build/` は `.gitignore` 済み＝コミットされない）
- 撮れるシーンと推奨 `--shot-at`（既定の主要セット）:

  | シーン | name | --demo | --shot-at | 撮れる内容 |
  |---|---|---|---|---|
  | `res://Prologue.tscn` | prologue | あり | `2,5,9,13,17` | コードレイン→identity[MINA]→立ち絵会話→タイトル |
  | `res://Hub.tscn` | hub | **なし** | `1.5` | タイムライン（投稿カード・ヘッダ・汚染バー） |
  | `res://DiffSelect.tscn` | diffselect | **なし** | `1.5` | 難易度選択（報酬倍率・ロック表示） |
  | `res://Shop.tscn` | shop | **なし** | `1.5` | ミナ強化（レベルピップ・Imp価格） |
  | `res://Rei.tscn` | rei | あり | `2,6,12,20` | STAGE1 会話＋戦闘HUD |
  | `res://Akari.tscn` | akari | あり | `2,6,12,20` | STAGE2 教室背景の弾幕戦 |
  | `res://Koharu.tscn` | koharu | あり | `2,6,12,20` | STAGE3 台所背景の弾幕戦 |
  | `res://Epilogue.tscn` | epilogue | あり | `2,5,9,13` | ナレーション（黒画面＋テキストボックス） |
  | `res://Final.tscn` | final | あり | `3,8,15,22` | 内面ダイブ（汚染の言葉が漂う） |

## 手順

1. **C# ビルド確認**（壊れたまま起動すると即落ちする）:
   ```powershell
   dotnet build d:\dev\algo_shoot\algo_shoot.sln -nologo -clp:ErrorsOnly   # 0 Error(s) を確認
   ```

2. **撮影**（PowerShell。静的UIと autoplay 系を1ループで回す。各シーン数秒で終わる）:
   ```powershell
   $godot = "C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
   if (-not (Test-Path $godot)) { $godot = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "Godot_v*mono*win64.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
   $proj = "d:\dev\algo_shoot"; $out = "d:\dev\algo_shoot\build\shots"
   New-Item -ItemType Directory -Force -Path $out | Out-Null
   Get-ChildItem $out -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

   # scene, name, demo(自動操縦), at(撮影時刻)
   $jobs = @(
     @{ scene="res://Hub.tscn";        name="hub";        demo=$false; at="1.5" },
     @{ scene="res://DiffSelect.tscn"; name="diffselect"; demo=$false; at="1.5" },
     @{ scene="res://Shop.tscn";       name="shop";       demo=$false; at="1.5" },
     @{ scene="res://Prologue.tscn";   name="prologue";   demo=$true;  at="2,5,9,13,17" },
     @{ scene="res://Rei.tscn";        name="rei";        demo=$true;  at="2,6,12,20" },
     @{ scene="res://Akari.tscn";      name="akari";      demo=$true;  at="2,6,12,20" },
     @{ scene="res://Koharu.tscn";     name="koharu";     demo=$true;  at="2,6,12,20" },
     @{ scene="res://Epilogue.tscn";   name="epilogue";   demo=$true;  at="2,5,9,13" },
     @{ scene="res://Final.tscn";      name="final";      demo=$true;  at="3,8,15,22" }
   )
   foreach ($j in $jobs) {
     $a = @('--path',$proj,$j.scene,'--')
     if ($j.demo) { $a += '--demo' }
     $a += @('--shot','--shot-out',$out,'--shot-name',$j.name,'--shot-at',$j.at)
     $p = Start-Process $godot -ArgumentList $a -PassThru `
          -RedirectStandardOutput "$out\$($j.name).log" -RedirectStandardError "$out\$($j.name).err"
     if (-not $p.WaitForExit(90000)) { Stop-Process -Id $p.Id -Force; "TIMEOUT $($j.name)" }
     "done $($j.name)"
   }
   Get-ChildItem $out -Filter *.png | Sort-Object Name | Select-Object Name, Length
   ```
   - 全シーンの合計で実時間 数分。長いので **`run_in_background: true`** で投げて完了通知を待つとよい。
   - 1画面だけ撮り直すなら `$jobs` をその1件にする（例：ハブだけ `@{ scene="res://Hub.tscn"; name="hub"; demo=$false; at="1.5" }`）。

3. **確認**：撮れた PNG を Read ツールで開いて目視する（出力は 1152x216→実際は窓サイズの 1152x648 相当）。
   同サイズのフレームが連続するもの（prologue/epilogue 等）は進行が止まって同じ絵の可能性 → 別 name・別 `--shot-at` で撮り直す。
   OK ならユーザーへ出力フォルダ（`build\shots\`）と各 PNG のパスを報告。

## 注意・つまずき
- **`build/` は `.gitignore` 済み**。PNG はコミットされない。
- **Hub/Shop/DiffSelect は絶対に `--demo`/`--qa` を付けない**（autoplay で即遷移して撮れない）。
- **真っ黒になる典型**：`--headless` を付けた／ビルドエラーで即落ち（手順1で弾く）／`--shot` を `--` の前に置いてユーザ引数に渡っていない。
- **Hub/Shop は現在のセーブ状態を反映**（フォロワー・インプレ・解禁ステージ）。全クリア後の FINAL カードや汚染進行時のヘッダ等、別状態を撮りたいときはセーブを差し替えてから撮る（`GameManager` のセーブ）。
- カットシーン（Prologue/Epilogue/Final）は DemoPilot の Z 送りで進むが、←→選択待ち（Epilogue の PW 等）で止まることがある。その局面は同じ絵が続くので、撮れた範囲（導入フェーズ）で割り切るか別途手動で進める。
- `--shot-at` の各時刻は **その秒に到達したフレームで1枚**。会話を撮りたいなら早め（1〜3s）、戦闘HUDなら後ろ（10s〜）を混ぜる。
- 内部解像度 384x216 を窓 1152x648（3倍）に拡大表示しており、保存される PNG は窓ビューポートサイズ。UI 確認には十分な解像度。
