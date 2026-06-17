# algo: Refrain of Light（MINA）— UI HTML（採用版）

HTML で作成した UI デザインの採用版一式です。各 `.dc.html` はブラウザで直接開けます（同梱の `support.js` と `hud/icons/` が必要なため、**フォルダ構成を保ったまま**開いてください）。ネット接続時は Google Fonts（Zen Kaku Gothic New / JetBrains Mono）を読み込みます。

## 収録ファイル
| ファイル | 内容 |
|---|---|
| `Refrain Title.dc.html` | スタート画面（メニュー／Xフィードティッカー） |
| `Refrain Screens.dc.html` | STAGE START・難易度選択・ミナ強化ショップ（インタラクティブ／射撃プレビュー） |
| `Refrain HUD A.dc.html` | ゲーム中HUD（通常／被弾／浄化100% ＋ 降ってくる言葉ティッカー） |
| `Refrain Settings.dc.html` | 設定（表示／サウンド／ゲームプレイ／操作／アクセシビリティ／X連携・インタラクティブ） |
| `Refrain Dialogue B.dc.html` | セリフ表示（シネマ下部バー：キャラ／ナレーター／名前なし） |
| `Refrain Dialogue Play.dc.html` | 会話システム動作版（タイプライター・選択肢・AUTO/SKIP/ログ） |
| `Refrain Danmaku v3.dc.html` | 弾幕パターン設計（レイ／あかり／こはる／ミナ）＋スペル宣言ツイート演出＋難易度切替 |

## デザイントークン（SnsTowerDefense デザインシステム準拠）
- 体力=`#e8769c` / BOMB・ミナ=`#9a72d9` / 浄化・光=`#6cbcd8` / ボス穢れ=`#e072ac` / SCORE・インプレ=`#e8c45a`
- 見出し・本文=Zen Kaku Gothic New ／ 数値・ラベル=JetBrains Mono
- 内部解像度 384×216 を整数倍した 16:9（1920×1080 相当）で設計

## 弾幕（v3）の要点
- キャラ別の弾色・弾形：レイ＝銀金菫ティール／あかり＝雨青と白／こはる＝琥珀と深紅／ミナ＝濁った全色
- 弾形7種：円弾・菱形・星・リング・針・粒弾・言葉弾
- 技名を X のスペル宣言ツイート＋通知として演出
- 難易度（EASY/NORMAL/HARD/LUNATIC）で弾数・速度・落下行数が変化（上部バーで切替）
- ミナ（FINAL）は全ステージの攻撃を組み合わせて使用

> C# 実装の土台は別フォルダ `RefrainScripts/` を参照（このZIPには未同梱の場合があります）。
