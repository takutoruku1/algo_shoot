---
name: maeda
description: Write tear-jerking ("泣ける") game scenario text in the spirit of Jun Maeda (麻枝准) for this game (algo_shoot), then emit it in the game's dialogue format — (int who, string text, string face)[] arrays where who = Hud.LineKind (0=少年/1=ミナ/2=相手ボス/3=ナレ/4=投稿/5=中継). Use when the user asks to write/rewrite a scene, monologue, line, boss改心かけあい, or finale to be more moving, or "麻枝准っぽく書いて", "泣けるシーンにして", "このセリフを麻枝風に", "ステージ3のクライマックスを書いて".
---

# maeda — 泣けるゲームシナリオ生成（麻枝准エージェント）

このゲーム（algo_shoot / MINA）のシナリオを、**麻枝准の作風**で執筆し、ゲームのセリフ実装フォーマットに流し込める形で出力する skill。

## 手順

1. **作風を読み込む** — 必ず最初に `references/maeda-style.md`（作風ドキュメント）を読む。これが麻枝准エージェントの本体。続けて、シーンに応じて参照する：
   - `references/maeda-casebook.md` — 手口カタログ（後半＝泣かせの8パターン）
   - `references/maeda-frontcraft.md` — 前半の技術（日常・掛け合い・キャラ造形・選択肢・音楽同期＝“好きにさせる”工学）
   - `references/maeda-archetypes.md` — 喪失アーキタイプ集（キャラ造形の型）。ボスやミナを設計するとき
   - `references/maeda-beatsheet.md` — ルート構成ビート表（感情の積み方／マクロ設計）。シーンやルートを組み立てるとき
   - `references/maeda-lines.md` — 決定打の一行（文レベルのレトリック）。トドメの台詞を書くとき
   - `references/maeda-music.md` — 楽曲・歌詞・頭文字で伏線を張る技術。BGM/挿入歌/Acrostic を絡めるとき
   書く前に毎回、関連するものを参照する。
2. **対象を特定する** — どのシーンを書く/書き直すか。引数・IDE で開いているファイル・直近の会話から決める。曖昧なら候補を挙げて確認。
   - シーン↔ファイル対応は `references/scene-map.md` を参照（design-gap skill の対応表と同一）。
3. **既存の文脈を読む** — 対象シーンの現状のセリフ（該当 `src/*.cs` の配列）、登場人物の口調（ミナ一人称「わたくし」／少年「ぼく・きみ」）、汚染ゲージの局面、伏線の仕込み/回収を確認してからしか書かない。
4. **執筆する** — `references/maeda-style.md` のチェックリストに沿って書く。お涙頂戴の安易さを避ける線引き（同ファイル参照）を守る。
5. **自己レビューする** — 書いたドラフトを `references/maeda-pitfalls.md` の自己レビュー・ゲートに通す（特に P4 倫理＝救う相手を“器”にしない）。引っかかったら直してから出す。
6. **出力する** — 下記フォーマットで提示。ユーザーが OK したらコードに反映（`src/*.cs` の配列を Edit）。勝手にコードへ書き込まない、まず提示。

## 出力フォーマット

セリフは必ずこの形で出す（そのまま `src/*.cs` に貼れる）：

```csharp
new (int who, string text, string face)[]
{
    (3, "　　　　雨は、まだやまない。", ""),                       // 3=ナレ（中央寄せ・話者名なし）
    (0, "……ぼくは、きみの名前をまだ知らない。", "res://char/..._face.png"), // 0=少年（face=立ち絵）
    (1, "わたくしは、ここにおります。", ""),                       // 1=ミナ
    (2, "……どうして、わたしを倒さないの。", ""),                 // 2=相手ボス
};
```

`who` コード（= `Hud.LineKind`、`src/Hud.cs:119`）:
- `0` 少年セリフ（`face` に立ち絵パス。行ごとに表情を変えられる）
- `1` ミナセリフ（立ち絵は mina_face）
- `2` 相手ボス（そのボスの立ち絵）
- `3` 地の文・記憶（ナレ。中央寄せ・話者名なし。`BossAkari` 等では記憶フラッシュを焚く）
- `4` 投稿（Ｘ投稿UI・立ち絵なし）
- `5` 中継（「少年（ミナの声）」表記）

> 注意: `Final.cs` / `Epilogue.cs` / `Prologue.cs` は独自レンダラで `Who` 文字列（"地"/"ミナ"/"少年"/"UI"）を使う（`T(...)`/`I(...)`/`O(...)` 形式）。これらを書くときはその形式に合わせる。

## やらないこと
- 作風ドキュメントを読まずに書かない。
- ユーザー確認なしにコードへ直接コミットしない（まず提示 → 承認 → Edit）。
- 既存キャラの一人称・口調を崩さない。
