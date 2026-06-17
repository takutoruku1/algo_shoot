# algo: Refrain of Light — UI 実装スクリプト（C# / Unity 想定）

このパッケージは、HTML で作成した UI デザイン（HUD・タイトル・難易度選択・STAGE START・ミナ強化ショップ・設定・会話システム）を
**Unity C# のデータ＋コントローラ雛形**に書き起こしたものです。Claude Code がこれを読み、実際の Unity 実装（uGUI / TextMeshPro 等）に展開する想定の足場です。

## 対応表（HTML → C#）
| HTML デザイン | C# |
|---|---|
| Refrain HUD A（HUD・3状態・ティッカー） | `HUD/HudController.cs` |
| Refrain Title（スタート画面） | `Title/TitleMenuController.cs` |
| Refrain Screens（STAGE START） | `Stage/StageIntroController.cs` |
| Refrain Screens（難易度選択） | `Difficulty/DifficultySelectController.cs` |
| Refrain Screens（ミナ強化ショップ） | `Shop/UpgradeShopController.cs` |
| Refrain Settings（設定） | `Settings/SettingsController.cs` |
| Refrain Dialogue Play（会話：タイプ/選択肢/AUTO/SKIP/ログ） | `Dialogue/DialogueSystem.cs` |
| 役割色・フォント・余白トークン（SnsTD 準拠） | `Theme/RefrainTheme.cs` |

## 役割色トークン（SnsTowerDefense デザインシステム準拠）
- 体力 HP = #e8769c / BOMB・ミナ = #9a72d9 / 浄化・光 = #6cbcd8 / ボス穢れ = #e072ac / SCORE・インプレ = #e8c45a
- 見出し/本文 = "Zen Kaku Gothic New" ／ 数値・ラベル = "JetBrains Mono"

## 注意
- 各 MonoBehaviour の `[SerializeField]` 参照（Image / TMP_Text 等）は Unity 側で割り当ててください。
- 立ち絵・アバターはプレースホルダー。実アセットに差し替え可能。
- これらは「振る舞いとデータの仕様」を写したものです。レイアウトの数値はコメントに HTML 由来値を残しています。
