# PROGRESS — 自動開発の進捗

> `node tools/progress.mjs` が `DEV_QUEUE.md` から自動生成。手で編集しない。
> 生成: 2026-08-13 15:43 UTC

## 消化率

```
███░░░░░░░░░░░░░░░░░  16%   (完了 7 / 対象 44)
```

| 状態 | 件数 |
|---|---:|
| ✅ 完了 | 7 |
| 🔨 作業中 | 0 |
| 📋 残り | 37 |
| ⛔ 保留（人間の判断待ち） | 9 |

## 🔨 いま作業中

_なし_

## 📋 次にやること

- `P1` ボス戦が火力投資に反応しない
- `P1` 通常グレイズが通貨を生まない
- `P1` ミナが汚染を引き受ける選択の一行が無い
- `P1` Hubリプライがエピローグの二段落としを先に割る
- `P2` 汚染ゲージの説明が実装と食い違う

…ほか 32 件

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

- `P1` マウス未操作なのに選択が勝手に飛ぶ
- `P1` 「はじめから」の初期化漏れ2件
- `P1` SelectedEntry が消費後リセットされずリトライが壊れる
- `P1` #22 既読スキップ
- `P2` #27 ステージ選択のSNSタイムライン化
- `P2` #11 背景の文字をツイート風に
- `P3` レイ/あかりの cry ポートレート

## 直近のコミット

- `8eff3aa` 2026-08-14 Fix three defects that broke input, fresh saves, and retry
- `3ad5cbe` 2026-08-14 Let one nightly run work through several queue items
- `231a25f` 2026-08-13 Audit the game from four angles and queue what needs fixing
- `58700ca` 2026-08-13 Queue the small-talk work and park the pending story decisions
- `f506c6e` 2026-08-13 Reconcile the dev queue with what is already built
- `aa7cbc6` 2026-08-13 Give the epilogue a background per phase
- `bc2c32b` 2026-08-13 Add progress dashboard and fix queue worker parsing
- `3df5d8d` 2026-08-13 Add autonomous nightly dev pipeline
