# AI生成アセット開示（Steam AI Generated Content Disclosure 用）

作成日: 2026-07-02 ／ Steamリリースロードマップ Phase 0「AI生成アセットの棚卸し」の成果物。
Steam はストア提出フォームで AI 生成コンテンツの申告が必須（開示すればリリース可能）。本書は
①リポジトリ内の AI 生成アセットの棚卸し一覧と、②提出フォームにそのまま貼れる開示文案（英語＋日本語）をまとめる。

**要点**: 本作の AI 生成物はすべて開発時に生成した「事前生成（Pre-Generated）」であり、
ゲーム実行中に AI がコンテンツを生成する「ライブ生成（Live-Generated）」は存在しない。

---

## 1. 画像アセット（char/ 配下）— OpenAI gpt-image 生成

- **生成手段**: OpenAI Images API（モデル `gpt-image-2`、初期の一部は `gpt-image-1`）。
  生成スクリプトは `tools/gen_image.mjs`（新規生成）／`tools/gen_edit.mjs`（参照画像つき編集）。
  生成後に `tools/key_trim_scale.ps1` でクロマキー・トリム・縮小の人手加工を経てゲームに配置。
- **プロンプトの記録**: `char/raw/_prompt_*.txt`（char/raw/ は .gitignore 済み・ローカルのみ）。
- **人間の関与**: すべて開発者の指示（プロンプト設計・リテイク・採否判断）と手動加工を経ている。

### ファイル一覧（char/ 直下 65 点 + char/bg/ 11 点 = 76 点、全て gpt-image 生成）

| 分類 | ファイル |
|---|---|
| 自機・ミナ | mina_idle / mina_shoot / mina_dodge / mina_spin_00〜04 / mina_dress / title_mina / title_kv |
| ミナ表情差分 | mina_face / mina_smile / mina_tears / mina_doubt / mina_worried |
| 少年 | shonen_idle / shonen_face / shonen_fluster / shonen_gentle / shonen_point / shonen_proud |
| アルゴ | algo / algo_idle / algo_cutout |
| ボス・レイ | enemy_rei_pre / enemy_rei_post / enemy_rei_cry / enemy_rei_eye_pre / enemy_rei_eye_post / enemy_rei_drone_pre / enemy_rei_drone_post / rei_face / cutin_rei |
| ボス・あかり | enemy_akari_pre / enemy_akari_post / enemy_akari_cry / enemy_akari_desk_pre / enemy_akari_desk_post / enemy_akari_note_pre / enemy_akari_note_post / akari_face / cutin_akari |
| ボス・こはる | enemy_koharu_pre / enemy_koharu_post / enemy_koharu_cry / enemy_koharu_knife_pre / enemy_koharu_knife_post / enemy_koharu_pot_pre / enemy_koharu_pot_post / koharu_face / koharu_face_pale / cutin_koharu |
| ボス・ミナ(本体)・ヒカゲ | enemy_mina_pre / enemy_mina_post / cutin_mina / enemy_hikage_pre / enemy_hikage_post / enemy_hikage_cry / hikage_face_cry / hikage_face_happy / enemy_anti_pre / enemy_anti_post |
| パネル | panel_anti / panel_hikage |
| 背景 (char/bg/) | rei/board / rei/boss / rei/scroll / akari/classroom / akari/scroll / koharu/kitchen / koharu/scroll / w0/bg_w0_sky / bg_w0_far / bg_w0_mid / bg_w0_fore |

※フォント（assets/fonts/ の Zen Kaku Gothic New・JetBrains Mono・PixelMplus）は AI 生成物ではない
（人間のデザイナーによる OFL/M+ ライセンスのフォント。ライセンス全文を同梱済み）。

---

## 2. 音楽アセット（BGM/・audio/）— Google 生成AI 音楽

- **生成手段**: **Google の生成AI音楽サービス**。全 mp3 に Google LLC 署名の C2PA マニフェスト
  （`Google C2PA Media Services`、アクション `c2pa.created` = "Created by Google Generative AI"、
  digitalSourceType = trainedAlgorithmicMedia、SynthID 透かし付与）が埋め込まれていることを
  ファイル解析で確認済み（2026-07-02）。
  **具体的なサービス／モデル名（Gemini・Lyria・MusicFX 等のどれか）はリポジトリに記録がなく「要確認」**
  （ダウンロードした本人＝開発者の記憶・アカウント履歴で確定させること）。
- **audio/ の ogg は BGM/ の mp3 からの派生物**（ffmpeg でトリム・ゲイン調整・ループ加工。人手編集）。

### ファイル一覧と由来

| ゲームで使う音源 | 由来 | 用途 |
|---|---|---|
| BGM/The_Watcher_in_the_Hall.mp3 | Google 生成AI（C2PA 確認済） | ステージ1（レイ）道中 |
| BGM/Akari_s_Last_Corridor.mp3 | 同上 | ステージ2 あかりボス戦 |
| BGM/The_Leaking_Tap.mp3 | 同上 | ステージ3 こはるボス戦 |
| audio/bgm_menu_mina.ogg | BGM/Mina_s_Window (1).mp3（28.6秒）を編集 | タイトル/ハブ等メニュー |
| audio/bgm_stage_akari.ogg | BGM/Empty_Desks_at_Four.mp3（65秒）を編集 | ステージ2 道中 |
| audio/bgm_stage_koharu.ogg | BGM/The_Kettle_Stays_Warm.mp3（61秒）を編集 | ステージ3 道中 |
| audio/bgm_boss_rei.ogg | BGM/The_Frozen_Threshold.mp3（30.8秒）を編集（推定・要確認※） | ステージ1 レイボス戦 |
| audio/bgm_boss_mina.ogg | BGM/The_Weight_Of_Absolution.mp3（149秒）の 51.0〜84.5 秒を切り出し編集 | ラスボス（ミナ本体）戦 |

※ bgm_boss_rei.ogg の原曲名はコードコメントに「原曲30.8秒」とだけ記録。BGM/ 内で 30.8 秒の曲は
The_Watcher_in_the_Hall（レイ道中に使用済み）／Mina_s_Window／The_Frozen_Threshold の3つで、
曲名の意味（レイ＝凍結の少女）から The_Frozen_Threshold と推定。要確認。

### 未使用の原素材（配布ビルドから除外済み — export_presets.cfg の exclude_filter 参照）

BGM/Mina_s_Window.mp3・Morning_Light_on_Glass.mp3・The_Frozen_Threshold.mp3・
Empty_Desks_at_Four.mp3・The_Kettle_Stays_Warm.mp3・The_Weight_Of_Absolution.mp3・
Mina_s_Window (1).mp3（いずれも Google 生成AI。ogg 化済みの原素材、または未採用曲）。

### 効果音（SE）は AI 生成音源ではない

全 SE（ショット・被弾・浄化・ボム・UI 等）は `src/Audio.cs` 内でコードにより手続き合成した波形
（AudioStreamWav をプログラム生成）。外部音源ファイルを使っていないため、Steam 開示上は
「AI 生成オーディオアセット」に該当しない（合成コード自体は下記 3. の AI 支援で作成）。

---

## 3. テキスト・ソースコード — Anthropic Claude 支援

シナリオ・セリフ・ゲーム内テキストおよびソースコード（C#）は、Anthropic の Claude（Claude Code）
の支援を受けて、開発者の指示・監修・レビューの下で作成した。実行時に AI がテキストを生成する
機能は存在しない。

---

## 4. Steam 提出フォーム開示文案

ストア提出フォーム「AI Generated Content Disclosure」の **Pre-Generated** 欄にそのまま貼る。
Live-Generated（実行時生成）は「なし」で申告する。
（貼る前に、§2 の BGM サービス名「要確認」を確定させて文中の "Google's generative AI music tools" を
必要なら具体名に置き換えること。）

### 英語（提出用）

> This game contains pre-generated AI content. All AI-generated content was created during
> development under human direction and was reviewed and edited by the developer; the game does
> not generate any content at runtime (no live-generated content).
>
> 1. 2D art assets (character sprites, dialogue portraits, enemy/boss art, cut-in illustrations,
> and stage backgrounds) were generated with OpenAI's image generation models (gpt-image), then
> manually processed (chroma-keying, trimming, scaling) and curated by the developer.
>
> 2. Background music tracks were generated with Google's generative AI music tools (the files
> carry Google-signed C2PA provenance manifests and SynthID watermarks), then manually edited
> (trimming, gain adjustment, loop editing) for in-game use. Sound effects are procedurally
> synthesized by the game's own code and are not AI-generated audio assets.
>
> 3. The game's scenario text and source code were written with the assistance of Anthropic's
> Claude, under the developer's direction, supervision, and review.
>
> The developer has reviewed all AI-generated content and believes it does not infringe on any
> third-party rights and does not contain any illegal content.

### 日本語（控え・国内向け説明用）

> 本作には事前生成された AI コンテンツが含まれます。すべての AI 生成コンテンツは開発中に
> 人間（開発者）の指示のもとで生成し、開発者がレビュー・編集したものです。ゲーム実行中に
> AI がコンテンツを生成することはありません（ライブ生成なし）。
>
> 1. 2D アートアセット（自機・立ち絵・敵ボス・カットイン・ステージ背景）は OpenAI の画像生成
> モデル（gpt-image）で生成し、開発者がクロマキー・トリミング・縮小などの加工と取捨選択を
> 行いました。
>
> 2. BGM は Google の生成AI音楽ツールで生成し（各ファイルに Google 署名の C2PA 来歴情報と
> SynthID 透かしが付与されています）、トリミング・音量調整・ループ加工などの編集を行いました。
> 効果音はゲーム自身のコードによる手続き合成であり、AI 生成音源ではありません。
>
> 3. シナリオテキストおよびソースコードは、Anthropic の Claude の支援を受け、開発者の指示・
> 監修・レビューのもとで作成しました。
>
> 開発者はすべての AI 生成コンテンツをレビューし、第三者の権利を侵害するもの・違法なものが
> 含まれていないことを確認しています。

---

## 5. 残タスク（要確認）

- [ ] BGM 生成に使った Google のサービス名／モデル名を確定する（開発者のアカウント履歴で確認）。
  確定したら §2 と §4 の文案を具体名に更新。
- [ ] audio/bgm_boss_rei.ogg の原曲が The_Frozen_Threshold.mp3 で正しいか確認（§2 の※）。
- [ ] Phase 4 でストアアセット（カプセル画像等）を AI 生成した場合、本書と提出フォームに追記する。
