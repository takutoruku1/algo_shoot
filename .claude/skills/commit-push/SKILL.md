---
name: commit-push
description: Stage all changes, commit with a clear message, and push to origin/main. Use when the user asks to commit and/or push the work (e.g. "コミットして", "プッシュして", "コミットプッシュして", "いったんコミット", "commit and push").
---

# commit-push — コミット＆プッシュ

作業内容をステージ → コミット → `origin/main` へプッシュする手順と規約。

## 手順

1. **差分を確認**（何を入れるか把握。秘密や巨大物が混じっていないか）:
   ```
   git status --short
   ```
   - `.openai_key.txt` / `char/raw/` / `build/` は `.gitignore` 済み。**もし出てきたら add しない**（誤って追跡されていたら指摘する）。

2. **ステージ**:
   ```
   git add -A
   ```

3. **コミット**（規約は下記）。Bash ツール（Git Bash）では PowerShell の here-string `@'...'@` は使えず先頭に `@` が残るので**使わない**。次のどちらかで：
   - 複数 `-m`（推奨・短い本文向き）:
     ```
     git commit -m "Subject in English, imperative mood" -m "- bullet of what changed
     - another bullet

     Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
     ```
   - 長い本文は heredoc を `-F -` に渡す:
     ```
     git commit -F - <<'EOF'
     Subject in English

     - bullet
     - bullet

     Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
     EOF
     ```

4. **プッシュ**（ユーザーがコミットだけを頼んだ場合は push しない）:
   ```
   git push origin main
   ```

5. ユーザーへ報告：コミットハッシュ、`origin/main` への反映、変更点の要約。

## コミットメッセージ規約（このリポジトリの慣習）
- **subject は英語・命令形・1行**（このリポの履歴に合わせる）。日本語本文は可だが subject は英語が揃っている。
- 本文は任意。中〜大きい変更は「何を・なぜ」を箇条書き。
- **必ず末尾に**: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- 1コミット＝1まとまり。関係ない変更を混ぜない。

## 注意
- `warning: LF will be replaced by CRLF` は**無害**（Windowsの改行。気にしない）。
- **main へ直接 push** してよいリポジトリ（ユーザー個人の `takutoruku1/algo_shoot`）。ただし push はユーザーが明示的に頼んだときだけ実行する。
- `git rebase -i` 等の対話的コマンドはこの環境で不可。`--no-verify` でフックを飛ばさない。
- コミットは新規作成を優先（むやみに `--amend` しない。直近メッセージの誤字修正など明確な時のみ）。
- 過去にやらかした例：bash で `@'...'@` を使い subject 先頭に `@` が入った → `--amend` で修正。最初から複数 `-m` か heredoc を使えば回避できる。
