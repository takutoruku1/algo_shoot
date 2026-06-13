---
name: export-build
description: Build and package this Godot/.NET game as a distributable single Windows exe and a zip. Use when the user asks to export the exe, output/rebuild the release build, or make a zip to hand out (e.g. "exeに出力して", "exe出力しなおして", "zipにまとめて", "配布用ビルド").
---

# export-build — Windows exe + zip 書き出し

このプロジェクト（Godot 4.6.3 mono / C#・.NET 8）を、**インストール不要の単体 exe**（pck と .NET ランタイムを内蔵）として書き出し、配布用 zip にまとめる手順。

## 前提（初回のみ。通常は導入済み）
- エクスポートテンプレート `4.6.3.stable.mono` が `%APPDATA%\Godot\export_templates\4.6.3.stable.mono\` に入っていること。
  無ければ公式から取得：`https://github.com/godotengine/godot-builds/releases/download/4.6.3-stable/Godot_v4.6.3-stable_mono_export_templates.tpz` を DL → zip 展開 → `templates\*` を上記フォルダへ。
- `export_presets.cfg` にプリセット **"Windows Desktop"** があること（embed_pck=true / dotnet/embed_build_outputs=true ＝単体exe）。`.gitignore` 済みなので消さない。
- `build/` は `.gitignore` 済み（巨大な exe をコミットしない）。

## 変数
- Godot 実行ファイル（mono・エディタ兼用）:
  `C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe`
  ※見つからなければ `Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "Godot_v*mono*win64.exe"` で探す。
- プロジェクト: `d:\dev\algo_shoot`
- 出力 exe: `build\algo_shoot.exe`（約183MB）
- 出力 zip: `build\algo_shoot_windows.zip`（約74MB）

## 手順

1. **C# ビルドが通ることを確認**（任意だがおすすめ）:
   ```
   dotnet build   # 0 Error(s) を確認
   ```

2. **リソースをインポート → リリース書き出し**（PowerShell 1コール）:
   ```powershell
   $godot = "C:\Users\takut\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
   & $godot --path d:\dev\algo_shoot --headless --import 2>&1 | Select-Object -Last 1
   New-Item -ItemType Directory -Force -Path "d:\dev\algo_shoot\build" | Out-Null
   & $godot --headless --path d:\dev\algo_shoot --export-release "Windows Desktop" "d:\dev\algo_shoot\build\algo_shoot.exe" 2>&1 | Select-Object -Last 3
   "exit=$LASTEXITCODE"   # 0 なら成功（末尾に [ DONE ] savepack が出る）
   ```

3. **zip にまとめる**（操作説明 `はじめにお読みください.txt` を同梱）:
   - `build\はじめにお読みください.txt` が無ければ Write ツールで作る（操作一覧・難易度・起動方法。本文に `/` や `←→` を含めても OK だが、PowerShell の here-string ではなく **Write ツール**で書くこと。インラインの here-string はサンドボックスに弾かれることがある）。
   ```powershell
   $exe = "d:\dev\algo_shoot\build\algo_shoot.exe"
   $readme = "d:\dev\algo_shoot\build\はじめにお読みください.txt"
   $zip = "d:\dev\algo_shoot\build\algo_shoot_windows.zip"
   Compress-Archive -Path $exe,$readme -DestinationPath $zip -CompressionLevel Optimal -Force
   "exe MB: $([math]::Round((Get-Item $exe).Length/1MB,1))"
   "zip MB: $([math]::Round((Get-Item $zip).Length/1MB,1))"
   ```

4. **起動確認**（任意・軽く）:
   ```powershell
   $p = Start-Process "d:\dev\algo_shoot\build\algo_shoot.exe" -PassThru
   Start-Sleep -Seconds 6; "running: $(-not $p.HasExited)"; Stop-Process -Id $p.Id -Force
   ```

5. ユーザーへ報告：exe と zip のパス・サイズ、配布は zip を渡せば良い旨（インストール不要・Windows 64bit・コントローラ対応）。

## 注意・つまずき
- 失敗の典型：エクスポートテンプレ未導入（→ 前提を確認）。`--export-release` は **テンプレ名がバージョン一致**していること。
- `Remove-Item` を含む PowerShell がサンドボックスに弾かれることがある。zip 上書きは `Compress-Archive -Force` で行い、`Remove-Item` を避ける。
- 大きいファイルなので `build/` はコミットしない（`.gitignore` 済み）。
- `.NET 8 SDK` が必要（dotnet がこのプロジェクトをビルドできること）。
