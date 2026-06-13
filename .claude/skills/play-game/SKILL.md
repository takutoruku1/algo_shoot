---
name: play-game
description: Launch this Godot/.NET game from source for quick playtesting (not the distributable exe). Use when the user asks to run/start/play the game, or to test a specific stage/scene (e.g. "ゲームを起動して", "起動して", "プレイして", "実行して", "レイのステージを動かして", "このシーンを試して").
---

# play-game — ソースからゲームを起動

このプロジェクト（Godot 4.6.3 mono / C#・.NET 8）を、**ソースのまま**エディタ用 mono バイナリで起動して動作確認する手順。配布用の単体 exe を作るのは別 skill（`export-build`）。

## 変数
- Godot 実行ファイル（mono・エディタ兼用）:
  `C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe`
  ※見つからなければ `Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "Godot_v*mono*win64.exe"` で探す。
- プロジェクト: `d:\dev\algo_shoot`
- メインシーン: `res://Prologue.tscn`（`project.godot` の `run/main_scene`）
- 主なシーン: `Prologue.tscn`（タイトル/プロローグ）, `Rei.tscn`（STAGE1）, `Akari.tscn`, `Koharu.tscn`, `Final.tscn`, `Epilogue.tscn`

## 手順

1. **C# ビルドが通ることを確認**（壊れたコードのまま起動するとすぐ落ちるので先に弾く）:
   ```powershell
   dotnet build   # 0 Error(s) を確認
   ```

2. **起動**（PowerShell。`Start-Process` で投げて即座に返す＝ゲーム窓が出てもターン内で報告できる）:
   ```powershell
   $godot = "C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
   $p = Start-Process $godot -ArgumentList '--path','d:\dev\algo_shoot' -PassThru
   Start-Sleep -Seconds 5
   "running: $(-not $p.HasExited)  pid: $($p.Id)"   # running: True なら起動成功
   ```
   - **特定のシーンだけ試す**場合は引数末尾にシーンの res:// パスを足す（メインシーンを飛ばして直接そこから始まる）:
     ```powershell
     $p = Start-Process $godot -ArgumentList '--path','d:\dev\algo_shoot','res://Rei.tscn' -PassThru
     ```

3. ユーザーへ報告：起動できたか（running 判定と pid）、どのシーンを起動したか。操作は Z=ショット / X=ボム / Shift=低速 / 矢印=移動 / タイトルで←→=難易度・Z=ダイブ・R=最初から（ゲームパッド対応）。

## 確認だけして閉じたいとき（任意）
ヘッドレス不可（描画ありのゲーム確認のため）。数秒だけ起動して落とす:
```powershell
Start-Sleep -Seconds 6; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }; "stopped"
```
ただし基本は**ユーザーに触ってもらう**ので、勝手に閉じない。閉じるのはユーザーが頼んだときだけ。

## 注意・つまずき
- 起動直後に落ちる典型：C# のビルドエラー（手順1で先に確認）。`Start-Process` 直後は `HasExited` が False でも、ビルド失敗だと数秒で True に変わる → `Start-Sleep` 後に判定する。
- `--headless` は付けない（画面が出ないと意味がない）。動作の自動確認が要るなら別途 `verify` skill を検討。
- mono バイナリは初回起動時に C# を自動ビルドするが、手順1で先に通しておくと事故が減る。
- `.NET 8 SDK`（`dotnet`）が必要。
- パスにスペースは無いが、`Start-Process` の引数は `-ArgumentList` に**配列**で渡す（1本の文字列に詰めるとパス分割で失敗しやすい）。
