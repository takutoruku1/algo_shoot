# キャラ資産 ↔ 実装ファイル 対応表（art-map）＋表情マトリクス

「アートと演出の設計図」（`yoshida-style.md`）の各原理を、どのキャラ・どの絵・どのコードで動かすかの索引。
**方向づけ・実装の前に、必ず `char/` の現物と該当 `src/*.cs` を読む**（推測で絵・動きを置かない）。
`/maeda` の scene-map、`/sakurai` の design-map、`/mitsuda` の sound-map と四位一体。

> ⚠️ 行番号は作成時点の目安。**実装前に必ず現在のファイルを開いて確認**する（コードは動く）。

---

## 0. 現状の要点（最重要事実）
- ~~**ミナ＝主人公なのに表情1枚**~~ → **解消(2026-06-20)**: `mina_smile/worried/tears` を生成・配線済み（採用版 mina_face=滑らかアニメ塗りに画風を厳密に合わせた）。Prologue/3ステージの ShowLine をミナも行ごと差し替え可に開き、Epilogue クライマックスに tears 立ち絵を差した。※採用版 mina_face はドット絵 raw でなく **maid_smooth 系の滑らかアニメ塗り**である点に注意（gen-asset 既定のドット絵指定は使わない）。残: Final にも tears を差す余地／自分のボス戦(BossMina)は未タグ。
- **少年は4差分を正しく行ごとに使い分け**（`shonen_face`=不敵 / `shonen_proud`=得意げ / `shonen_gentle`=優しさ / `shonen_fluster`=動揺・照れ）＝手本。ただし「怖がり/自己嫌悪」差分は無い。
- **改心 cry 機構は実装済みだが3ボスで遊んでいる**。`enemy_hikage_cry.png` ＋ `hikage_face_cry/happy/smile` を持つヒカゲのみ三段（pre→cry→post）を正しく使用。rei/akari/koharu は cry スプライトが無く `CryTexPath=PostTexPath`（＝最初から改心後の顔）。
- **死蔵アセット**: `char/koharu_face_pale.png` が生成済みなのに**コードから一度も参照されていない**。
- **動きが空白**: スプライト本体に予備動作・squash・余韻がほぼ無い。`SwapBody` は**瞬間差し替え・補間なし**（`Enemy.cs:246-254`）。`FxLayer`（粒・光）は厚いが、キャラ本体は棒立ち。

---

## 1. 立ち絵・スプライト資産（char/ 現物。Glob `char/*.png`）
| キャラ | 立ち絵(face) | 自機/敵スプライト | 表情差分の状況 |
|---|---|---|---|
| **ミナ** | `mina_face` ＋ `mina_smile/worried/tears`（計4枚・滑らかアニメ塗り／474x720統一） | 自機 `mina_idle.png` ／ ボス `enemy_mina_pre/post.png` | ◎4表情を配線済（穴解消） |
| **少年** | `shonen_face / fluster / proud / gentle`（4枚） | `shonen_idle.png` | 良好。ただし afraid（怖がり）無し |
| **レイ** | `rei_face.png` | `enemy_rei_pre/post.png`（cry無し） | cry無し／face差分薄い |
| **あかり** | `akari_face.png` | `enemy_akari_pre/post.png`（cry無し） | cry無し（告白の感情に対し顔が一定） |
| **こはる** | `koharu_face.png` ＋ **`koharu_face_pale.png`（未使用）** | `enemy_koharu_pre/post.png`（cry無し） | pale が死蔵／cry無し |
| **ヒカゲ** | `hikage_face_cry/happy/smile`（3枚） | `enemy_hikage_pre/post/cry.png` | ◎三段＋3表情＝手本 |
| その他 | — | `enemy_anti_pre/post`, `algo_idle`, `algo(_cutout)`, panel_* | 背景: `char/bg/*` |

## 2. 表情マトリクス（キャラ×感情×使用箇所）— 穴を埋める索引
| キャラ | 必要な感情 | 現状 | 使用すべき場面（file:line 目安） |
|---|---|---|---|
| ミナ | 平常 | `mina_face` ◎ | 全般 |
| ミナ | **皮肉/軽口（smile）** | ✗無い | 「教養アピールですか」`Final.cs` ／ 軽口全般 |
| ミナ | **動揺/拒絶（worried）** | ✗無い | 「近づいては…穢れて、しまいます」`BossMina.cs:51` |
| ミナ | **落涙（tears）★** | ✗無い | Final 最後の軽口 `Final.cs` ／ Epilogue `Epilogue.cs:65-66,71` |
| 少年 | 不敵/得意/優しさ/動揺 | ◎4枚 | 各 Stage・Prologue（行ごと差し替え済み） |
| 少年 | **怖がり/自己嫌悪（afraid）** | ✗無い | 「自分が行くのが怖かった」`BossMina.cs` ／「ぼくの声じゃ、だめなんだ」`BossAkari.cs` |
| レイ/あかり/こはる | **改心の泣き（cry）** | ✗無い（=post流用） | 各 Boss の改心 Lines 中（`CryTexPath`） |
| こはる | **蒼白（pale）** | △生成済み未使用 | 「お兄ちゃんが、いなくなる」系の絶望行 `BossKoharu.cs` |

> 増やす前にこの表で「穴」と「死蔵」を確定する。足すなら必ず使用箇所（file:line）と物語ビート（`/maeda`）に紐づける。

## 3. 描画・差し替えの実装フック
| 対象 | ファイル:行（目安） | 仕組み | 改善余地 |
|---|---|---|---|
| 敵/ボス本体スプライト差し替え | `Enemy.cs:246-254` `SwapBody(path)` | テクスチャ即差し替え＋スケール確定（**補間なし**） | クロスフェード＋squash→pop（style §3・§4） |
| 改心の三段 | `Enemy.cs:269-298`（`EndCryNow`/`_crying`）/ `Redeem` 内 `SwapBody(CryTexPath)` | pre→(CryHoldDur)→post。`CryTexPath`/`PostTexPath`/`CryHoldDur` は各 Boss が設定 | cry絵を繋ぐ＋ヒットストップ＋余韻 |
| 各ボスの cry/post 設定 | `BossRei.cs` / `BossAkari.cs` / `BossKoharu.cs`（`CryTexPath=…post.png`）/ `BossHikage.cs`（cry正用） | 3ボスは cry=post＝泣きを飛ばす | `enemy_*_cry.png` を生成し `CryTexPath` を差し替え |
| 改心フラッシュ | `Enemy.cs:317-325` `_Draw` | 0.6α 拡大円（淡ピンク→淡紫） | スプライトの動きと同フレーム化 |
| 自機ミナ 描画/揺れ | `Player.cs`（`_sprite` / `_bobTime` のSin浮遊 321-328付近 / `dir` 226） | idle浮遊のみ。移動バンク無し | 移動バンク・発射反動（style §4・§7） |
| 自機 被弾リアクション | `Player.cs:514-544` `TakeHit`（FxLayer+Shake+Hitstop）/ `561-576` 無敵点滅 | 周囲は派手・本体は点滅のみ | のけぞり・縮み（squash）（style §7） |
| 会話立ち絵描画 | `Hud.cs:459-486` `DrawDialog`（`_dlgPortrait` を矩形に貼る）/ タイプ送り `84-90` | 完全静止・切替瞬間 | 呼吸・まばたき・うなずき・クロスフェード（style §5） |
| 表情切替（会話） | 各 `Stage*.cs ShowLine` / `Boss*.cs ShowDialog`（`face`/portrait 引数） | 少年=行ごと差し替え／ミナ=固定 | ミナも行ごと差し替え可に開く（style §2） |
| ボス登場 | 各 `Stage*.cs Step_BossSpawn`（`_boss.GlobalPosition = new Vector2(SpawnX,70f)` 瞬間配置） | 予備動作・フェード無し | スライドイン＋フェード＋スケール＋着地（style §6） |
| フォロワー登退場 | `Player.cs:81-90 AddFollower` / `Follower.cs:34-41 MoveToward` / 離脱 `Player.cs:532-537 QueueFree` | 直線寄せ／即消去 | オーバーシュート＋ポップ／散ってフェード（style §6・§7） |
| 記憶フラッシュ | `StageImagery.cs`（`TriggerMemoryFlash` 2.4s）/ `BossAkari.cs` で発火 | 雨の交差点等の心象 | クライマックスの画と連携（style §8） |
| クライマックス（人物=円） | `Final.cs:126-127` `DrawCircle` ほか / `Epilogue.cs`（portrait無し） | 人物が抽象円 | ミナ tears 差分を差す（style §8・最優先級） |

## 4. 他スキルとの分担
- **絵の生成**: `gen-asset`（gpt-image）。このエージェントは「何を・どのキャラに・どの場面用に」を発注仕様として渡す。
- **面白い/視認性/テンポの最終判断**: `/sakurai`。動き・エフェクトの足し引きは桜井原理で検算。
- **何を感じさせるか（物語ビート）**: `/maeda`。表情・改心・喪失の対応付け。pitfalls P4（器化）を共有。
- **音との同期**: `/mitsuda`（音が後回しの今は保留。再開時に改心/登場/フラッシュの音画同期を握る）。
- **動作確認**: `play-game` / `screenshots` で実際に見る（机上で画を語らない）。

---

## 既存方針（壊さないこと）
- **浄化＝倒すでなく届ける救う**。改心の絵は「壊した」でなく「ほどけた／届いた」。撃破演出をこの世界観に合わせる。
- **視認性・テンポ最優先**（`/sakurai`）。動き・差分を足して自機/当たり判定/重要弾を埋もれさせない。
- **キャラの一人称・造形・色語彙を崩さない**（ミナ＝わたくし／穢れ→浄化の色）。
- 死蔵（koharu_face_pale）や遊んでいる機構（cry）を**先に拾う**。新規生成はその後。
- 自分で絵は描かない。生成は `gen-asset`、方向づけ＋実装がこのエージェント。
