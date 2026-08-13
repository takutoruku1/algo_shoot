# PROGRESS — 自動開発の進捗

> `node tools/progress.mjs` が `DEV_QUEUE.md` から自動生成。手で編集しない。
> 生成: 2026-08-13 18:27 UTC

## 消化率

```
████░░░░░░░░░░░░░░░░  18%   (完了 8 / 対象 44)
```

| 状態 | 件数 |
|---|---:|
| ✅ 完了 | 8 |
| 🔨 作業中 | 0 |
| 📋 残り | 36 |
| ⛔ 保留（人間の判断待ち） | 9 |

## 🔨 いま作業中

_なし_

## 📋 次にやること

- `P1` 通常グレイズが通貨を生まない
- `P1` ミナが汚染を引き受ける選択の一行が無い
- `P1` Hubリプライがエピローグの二段落としを先に割る
- `P2` 汚染ゲージの説明が実装と食い違う
- `P2` ステージ途中でハブへ戻れない

…ほか 31 件

## ⛔ 保留（自動では進められない）

- ショップ画面の毎フレーム再描画を実測してから判断
- ウィンドウリサイズ/フルスクリーン切替の表示確認
- 死亡系フロー（残機0・コンティニュー・チェックポイント再開）のQA
- 世界観の肉付け（SNSの雑音で語る）
- マルチエンディング化
- 挿入歌の調達
- BGM権利文言の再確認
- 旧Geminiマスター9本の削除
- 人力確認: R長押しリトライ / ESC の操作感

## ✅ 完了

- `P1` ボス戦が火力投資に反応しない
- `P1` マウス未操作なのに選択が勝手に飛ぶ
- `P1` 「はじめから」の初期化漏れ2件
- `P1` SelectedEntry が消費後リセットされずリトライが壊れる
- `P1` #22 既読スキップ
- `P2` #27 ステージ選択のSNSタイムライン化
- `P2` #11 背景の文字をツイート風に
- `P3` レイ/あかりの cry ポートレート

## 直近のコミット

- `defa32f` 2026-08-13 auto: WIP ボス戦が火力投資に反応しない
- `9360c42` 2026-08-14 Unify cutscene text boxes and color tokens
- `d123e2b` 2026-08-14 Let the player flip facing and shoot backwards
- `bf9ab6a` 2026-08-14 Make enemy AOE telegraphs and impacts read as one strike
- `26e6569` 2026-08-14 Refresh the progress snapshot
- `8eff3aa` 2026-08-14 Fix three defects that broke input, fresh saves, and retry
- `3ad5cbe` 2026-08-14 Let one nightly run work through several queue items
- `231a25f` 2026-08-13 Audit the game from four angles and queue what needs fixing
