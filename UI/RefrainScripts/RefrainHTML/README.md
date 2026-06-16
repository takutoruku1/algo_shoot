# algo: Refrain of Light — UI HTML（採用版）

HTML で作成した UI デザインの採用版一式です。Claude Code に C# 実装（`RefrainScripts/`）と合わせて読み込ませる想定の「見た目の正解」リファレンスになります。

## 収録ファイル
| ファイル | 内容 | 対応 C# |
|---|---|---|
| `Refrain Title.dc.html` | スタート画面 | `Title/TitleMenuController.cs` |
| `Refrain Screens.dc.html` | STAGE START・難易度選択・ミナ強化ショップ（インタラクティブ） | `Stage/` `Difficulty/` `Shop/` |
| `Refrain HUD A.dc.html` | ゲーム中HUD（通常／被弾／浄化100% ＋ 降ってくる言葉ティッカー） | `HUD/HudController.cs` |
| `Refrain Settings.dc.html` | 設定（6カテゴリ・インタラクティブ） | `Settings/SettingsController.cs` |
| `Refrain Dialogue B.dc.html` | セリフ表示（シネマ下部バー：キャラ／ナレーター／名前なし） | `Dialogue/DialogueSystem.cs` |
| `Refrain Dialogue Play.dc.html` | 会話システム動作版（タイプライター・選択肢・AUTO/SKIP/ログ） | `Dialogue/DialogueSystem.cs` |

## 開き方
- 各 `.dc.html` はブラウザで直接開けます（同梱の `support.js` と `hud/icons/` が必要なため、フォルダ構成を保ったまま開いてください）。
- ネット接続時は Google Fonts（Zen Kaku Gothic New / JetBrains Mono）を読み込みます。

## デザイントークン（SnsTowerDefense デザインシステム準拠）
- 体力=`#e8769c` / BOMB・ミナ=`#9a72d9` / 浄化・光=`#6cbcd8` / ボス穢れ=`#e072ac` / SCORE・インプレ=`#e8c45a`
- 見出し・本文=Zen Kaku Gothic New ／ 数値・ラベル=JetBrains Mono
- 内部解像度 384×216 を整数倍した 16:9（1920×1080 相当）で設計
