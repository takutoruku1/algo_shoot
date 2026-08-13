# AUTODEV — 夜間クラウド routine の作業手順書

このファイルは `/schedule` で登録した routine が毎回読む手順書。routine のプロンプトは短く保ち、実際の手順はここを正本にする（手順を変えたいときは routine を作り直さず、このファイルを編集すればよい）。

## 前提

- 作業ブランチは `auto/dev`。`main` には直接コミットしない。
- 1回の実行 = 1タスク = 1コミット。欲張らない。
- ビルドが通らないものはコミットしない。

## 手順

### 1. 準備

```bash
git fetch origin
git checkout -B auto/dev origin/main   # main の最新から作り直す（PR がマージ済みでも未マージでも main 基準で始める）
```

> 既存の `auto/dev` に未マージPRがある場合は、`origin/auto/dev` を checkout して続きから積む。
> 判断: `gh pr list --head auto/dev --state open` が空なら main 基準、あれば auto/dev 基準。

### 2. タスクを1つ取る

`DEV_QUEUE.md` の `## TODO` 最上段の1件を読む。空なら「キューが空」と PROGRESS に記録して**何もせず終了**。

取ったタスクを `## WIP` へ移し、この時点で一度コミットする（多重着手防止）。

### 3. 「もう実装済みでないか」を先に確かめる

**キューは古びる。** 実装する前に、そのタスクが既に done でないか必ず確認すること（過去に #22/#27/#11 が実装済みのまま積まれていた実績あり）。

- `src/` を grep して該当機能の実体があるか見る（`docs/タスク管理.md` の状態欄は**信用しない**。実コードが正）
- `git log --oneline -S"<キーワード>"` で導入コミットを探す

実装済みだった場合は、**実装せず** `## DONE` へ「(完了 済) 根拠=ファイル:行 か コミットハッシュ」付きで移し、次のタスクへ進む。これは空振りではなく正しい仕事。

### 4. 実装する

タスク行の担当ワーカー（`engineer` / `qa` / `artist` / `scenario` / `composer` / `game-designer`）を Agent ツールの `subagent_type` に指定して振る。受入条件（`|` 以降）をそのままワーカーへの指示に含める。

- 仕様が曖昧で判断が割れる → 実装せず `## BLOCKED` へ理由付きで移し、次のタスクへ。
- 15分相当を超えそうな規模 → 同じく BLOCKED へ（人間に分割してもらう）。

### 5. 検証する

```bash
dotnet build algo_shoot.sln
```

通らなければ `git checkout -- .` で変更を捨て、そのタスクを BLOCKED（理由=ビルド失敗の内容）へ。

QA系タスクなら `qa-autoplay` skill も回す。

### 6. キューと進捗を更新する

- 完了タスクを `## WIP` から `## DONE` の先頭へ、`(完了 YYYY-MM-DD)` を付けて移動。
```bash
node tools/progress.mjs    # PROGRESS.md と docs/progress.json を再生成
node tools/dashboard.mjs   # docs/dashboard.html を再生成
```

### 7. コミットしてPRにする

```bash
git add -A
git commit -m "auto: <タスク名の要約>"
git push -u origin auto/dev
```

PR がまだ無ければ作る（あれば push だけでよい）:

```bash
gh pr list --head auto/dev --state open   # 空なら↓
gh pr create --base main --head auto/dev \
  --title "auto: 夜間自動開発の積み上げ" \
  --body "$(cat PROGRESS.md)"
```

PR が既にあるなら本文を更新: `gh pr edit --body "$(cat PROGRESS.md)"`

### 8. 報告

最後に3行で報告する: **何をやったか / ビルド結果 / 残りタスク数**。
