---
name: gen-asset
description: Generate a game art asset (character sprite, dialogue portrait + expression variants, enemy boss pre/post, panel, or stage background) with OpenAI gpt-image-2, then chroma-key, trim, downscale, place into char/, and reimport so Godot loads it. Use when the user asks to make/redo/adjust any in-game image (e.g. "立ち絵を作って", "敵を生成して", "背景を作り直して", "表情差分を追加して", "MINAの自機を直して", "もっとドット絵に").
---

# gen-asset — ドット絵アセット生成パイプライン

OpenAI `gpt-image-2` で生成 → 単色背景をキー抜き → トリム＆縮小 → `char/` に配置 → Godot 再インポート、までの一連。これまでの規約をすべて内包する。

## 鉄則（毎回守る）
- **APIキーは `.openai_key.txt`（gitignore済）から読むだけ。値を絶対に出力・コミットしない。**
- 生のraw・プロンプトは `char/raw/`（gitignore済）に置く。仕上げた最終PNGだけ `char/` に置く。
- **生成後は必ず `--headless --import`**。やらないと `.import` が無く `ResourceLoader.Load` が null → ゲームに出ない。
- 生成したら **Read ツールでrawを目視**し、狙いと違えばプロンプトを1点だけ直して再生成（顔・画風・色・向き）。

## 2つの生成ツール
- **キャラ・背景（参照画像で画風を固定）** → `tools/gen_edit.mjs`（editsエンドポイント）:
  ```
  node tools/gen_edit.mjs .openai_key.txt <refPng> <promptFile> <outPng> 1024x1536 high
  ```
  - **画風の基準は常に `char/algo.png` を参照**（同じ頭身・線・かわいさに揃う）。
  - **差分や改心後**は、確定した**そのキャラのraw**（例 `char/raw/rei_pre_raw.png`）を ref に渡すと同一人物を保てる。
- **物体（参照なし・吹き出しパネル等）** → `tools/gen_image.mjs`（generationsエンドポイント）:
  ```
  node tools/gen_image.mjs .openai_key.txt <promptFile> <outPng> 1024x1024 opaque high
  ```
  - **gpt-image-2 は transparent 非対応** → `opaque` で**ベタ背景**を出し、後でキー抜き。

## プロンプトの定型（promptFileに入れる）
冒頭に必ずこのブロック（ドット絵を強制）:
```
あなたはレトロな2Dゲームのドット絵（ピクセルアート）キャラクターアーティストです。
添付の参照画像（algo）と同じ作画スタイル・同じ可愛い頭身（約2.5〜3頭身・頭大きめ）・
同じ線の処理（黒ベタ輪郭線を使わず1px＋暗い同系色アウトライン）で描いてください。
【最重要：ドット絵で描く】
- はっきりした四角いドット（ピクセル）が見えるローレゾのドット絵。限定パレット（16〜32色）。
- なめらかなグラデ・アニメ塗りのぼかし・アンチエイリアスは禁止。ハードエッジ。
- 黒はキャラの輪郭線に使わない。黒は「穢れ/暴言の黒い炎・もや」の塗りにのみ可。
```
末尾に必ず出力指定:
```
【出力（後でキー抜きするため）】背景は完全な単色のベタ1色＝純マゼンタ #FF00FF（または純グリーン #00FF00）。
影・グラデ・模様を一切入れない。キャラの縁に背景色がにじまないように。
```
- **立ち絵**＝胸から上のバストアップ・正面やや斜め。**敵スプライト**＝全身・右向き（横スクロールで左右反転して使う）。
- 既存キャラの世界観：ミナ＝メイドAI（銀髪）／少年＝ダーク髪＋琥珀目／あかり＝アンバー髪＋ランプ髪飾り／レイ＝藍ポニテ＋メガネ＋「2位」。

## キー背景の選び方（重要）
| 被写体 | 指定する背景 |
|---|---|
| 寒色・浄化前・シアン/青系 | **マゼンタ #FF00FF** |
| 暖色・浄化後・ピンク/橙系・パネル・黒い炎 | **グリーン #00FF00** |
- 例外：被写体にマゼンタに近い紫が多い→緑へ。被写体にシアン/緑が多い→マゼンタへ。

## 仕上げ（キー抜き・トリム・縮小・配置）
```
powershell -ExecutionPolicy Bypass -File tools/key_trim_scale.ps1 -In <raw> -Out <final> -Key magenta|green|none -TargetH <H>
```
- 高さの目安：**立ち絵 240／敵スプライト 168／自機 80／パネル 64／背景 220（-Key none）**。
- 置き場所：キャラ＝`char/<name>.png`・`char/enemy_<name>_pre/post.png`・`char/<name>_face.png`／背景＝`char/bg/<stage>/<name>.png`。
- 仕上げ後：`& <godot> --path d:\dev\algo_shoot --headless --import`（必須）。

## 標準ワークフロー
1. `char/raw/_prompt_<name>.txt` を Write（定型＋キャラ説明＋キー背景）。
2. `gen_edit`（ref=algo.png か確定raw）/ `gen_image`（物体）で `char/raw/<name>_raw.png` 生成。
3. **Read で目視** → 必要なら1点直して再生成。
4. `key_trim_scale.ps1` で `char/...png` に仕上げ。
5. `--headless --import` で再インポート。
6. コードに配線（PreTexPath/PostTexPath、ShowDialogの立ち絵パス、背景ロード等）→ build。
7. （任意）play-game で確認。

## つまずき
- ゲームに出ない → 再インポート忘れ（`.import`欠落）。
- 別人化する → ref を `algo.png` ではなく**そのキャラの確定raw**にする＋「同じ顔・髪・服」と明記。
- 丸が滑らかで画像っぽい/にやけ等 → プロンプトで「ドット絵で描く」を強める／表情語を具体化して再生成。
- PowerShell でプロンプトを書かない（文字化けの恐れ）。**プロンプトは Write ツールで .txt に**。
- gen_image の transparent は不可（opaque＋ベタ背景）。
