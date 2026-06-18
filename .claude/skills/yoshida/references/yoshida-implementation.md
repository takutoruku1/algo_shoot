# Godot/.NET 実装レシピ＋発注フロー（キャラを動かす／絵を足す）

`yoshida-style.md` の方向づけを、このプロジェクト（Godot 4 / .NET / C#）で**実際に動く**コードと、`gen-asset` への**発注仕様**に落とす手引き。
**コードは提示→承認→Edit**。ここはレシピであり、勝手にコミットしない。行番号は `art-map.md` 同様、実装前に現物で確認。

---

## A. 基本道具（コード駆動アニメ）
新規イラスト無しで生命感を出す主役は **`Tween` ＋ スプライトの Transform（Scale/Position/Rotation/SelfModulate）** と、既存の **`GameCamera.Hitstop/Shake`**・**`FxLayer`**。

- **squash & stretch**: `sprite.Scale` を一時的に潰す/伸ばして戻す。生命感の核。
  ```csharp
  // 被弾でビクッと縮む（0.85倍→1.0）
  var t = CreateTween();
  _sprite.Scale = new Vector2(0.85f, 1.1f);
  t.TweenProperty(_sprite, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
  ```
- **予備動作→本動作→余韻**: Tween をチェーンする（タメ→解放→揺り戻し）。ease は `Back`/`Elastic`/`Cubic` を使い分け（線形にしない）。
- **止め（ヒットストップ）**: 既存 `GameCamera.Instance?.Hitstop(0.08)` を決定打の瞬間に1発。`Engine.TimeScale` を落とすので、Tween は実時間で動かしたい場合 `t.SetProcessMode(Tween.TweenProcessMode.Physics)` 等で挙動を確認（演出は一緒に止まってよいことが多い）。
- **回転バンク**: `sprite.Rotation` を進行方向へ ±数°。`Player` の `dir` を使う。
- **注意**: 当たり判定は**スプライトと別**（自機の `HitRadius` 等）。見た目を動かしても判定座標は動かさない（`/sakurai` 視認性・整合）。

## B. 改心 pre→cry→post を「溶けるように」（style §3・§4）
現状 `SwapBody`（`Enemy.cs:246-254`）は即差し替え。これを**クロスフェード＋squash→pop**に。

- **クロスフェード**: 旧テクスチャを別スプライト（または `SelfModulate` α）で 0.12秒落としつつ、新テクスチャを上げる。`SwapBody` をラップする `SwapBodyFade(path, dur)` を1つ作る。
- **改心の山に演出**: 浄化確定（`Redeem` の `SwapBody(CryTexPath)` 付近）で：
  1. `GameCamera.Hitstop(0.08)` で一拍止める（予備動作の代わり）。
  2. pre を白へ飛ばしつつ縦に潰す（光に溶ける）→ cry へ差し替え → `Scale` を 1.15 へ pop → 0.25秒で 1.0（余韻）。
  3. 既存 `PurifyBurst`／改心フラッシュ（`Enemy.cs:317-325`）を**同フレーム**に重ねる。
- **cry→post の着地**（`EndCryNow` `Enemy.cs:269-276`）: post 差し替え直後に `Position.Y -= 4` → 戻す「肩の力が抜ける」動き。
- **cry 絵を繋ぐ**: rei/akari/koharu は `CryTexPath=PostTexPath` なので、`enemy_*_cry.png` を生成後（§F）、各 Boss の `CryTexPath` をその新パスに差し替えるだけ（`BossRei.cs`/`BossAkari.cs`/`BossKoharu.cs`）。

## C. 立ち絵の生命感（呼吸・まばたき・クロスフェード）（style §5）
`Hud.DrawDialog`（`Hud.cs:459-486`）は固定矩形に貼るだけ。

- **呼吸**: 描画矩形の Y/高さを `Sin(_t * 2π / 3.0)` で ±1〜2px（約3秒周期）。**話者の立ち絵だけ**揺らすと「誰の声か」も伝わる。自機 `_bobTime` と同手法。
- **まばたき**: まばたき差分（目を閉じた1枚, §F）があれば、数秒ごと 0.1秒だけ差し替え。無ければ**省略可**（呼吸だけでも生命感は出る）。
- **クロスフェード**: 表情/立ち絵の切替を瞬間でなく 0.1秒で。`DrawDialog` 側で旧 portrait をα落とししつつ新を上げる（切替時刻を保持）。
- **うなずき**: タイプ送り完了（`_dlgRevealed` 到達, `Hud.cs:84-90`）の瞬間に立ち絵を1px持ち上げて戻す。間（`/sakurai` §22）が出る。
- **ミナの表情を行ごとに**: `ShowLine` がミナで `mina_face` 固定の箇所を、少年と同じく `face` 引数で差し替えられるよう開く（§F でミナ差分を作ってから）。

## D. ボス登場・被弾・フォロワー（style §6・§7）
- **ボス登場**: 各 `Step_BossSpawn` の瞬間配置（`_boss.GlobalPosition = new Vector2(SpawnX,70f)`）の後に「登場待ち」ステップを1つ。画面外/上空から 0.6秒でスライドイン＋`Modulate` α 0→1＋`Scale` 1.2→1.0。着地で `GameCamera.Shake` 小＋グロー一発。スペル宣言はその**直後**（焦らし→開放）。
- **自機被弾**（`Player.TakeHit` `Player.cs:514`）: 既存の点滅に加え、進行と逆へ数px のけぞり＋回転、`Scale` 0.85→1.0（squash）。0.3秒で復帰。
- **移動バンク**（常時）: `_sprite.Rotation` を `dir` 方向へ ±5°。フォーカス（低速）時は抑える＝丁寧さが画で分かる。
- **発射反動**: `Fire`（`Player.cs:334-360`）で `_sprite.Position` を銃口と逆へ1〜2px キックバック→即戻し（0.05s、連射テンポを壊さない）。
- **フォロワー**: 登場（`Follower.cs:34-41` の `MoveToward`）を弾性補間＋到着ポップに。離脱（`Player.cs:532-537` の即 `QueueFree`）を 0.2秒散ってフェードしてから free に。

## E. ミックス＝視認性・テンポの検算（style §1・§10／`/sakurai`）
- 動き・差分を足すたび、**自機・当たり判定・重要弾が埋もれないか**を `screenshots`／`play-game` で確認。
- **動かしすぎ注意**: 常時アニメは1〜2要素まで。決定打の一拍は貴重に。テンポ（被弾後リスタート・会話送り）を遅くしない。
- 当たり判定座標は見た目と別（A）。視覚だけ動かす。

## F. 新規イラストの発注（gen-asset へ）
このエージェントは絵を描かない。**何を・どのキャラに・どの場面用に**を仕様化し、`gen-asset` に渡す。優先度は `yoshida-style.md` §12 の順。

発注テンプレ（例）:
```
gen-asset 依頼: ミナの表情差分
- 対象: mina（既存 char/mina_face.png と同一の画風・線・色温度・サイズ・向き・トリミング）
- 追加表情:
  1) mina_smile  … 皮肉・軽口。口角わずか、目は涼やか
  2) mina_worried… 動揺・拒絶。眉を寄せ、目を伏せ気味
  3) mina_tears  … 落涙。静かに泣く（嗚咽でなく一筋）。クライマックス用
- 制約: 一枚一感情（style §1-3）。既存立ち絵と差し替えて違和感が出ない同フレーム。
- 出力先: char/mina_smile.png 等（既存命名に揃える）
```
- **既存画風・サイズ・トリミングに厳密に揃える**（§1-5 世界観統一）。差し替えてズレない同一フレームが必須。
- cry 絵（rei/akari/koharu）は「pre と post の中間＝穢れが剥がれかけ・涙」。post と別物に。
- 生成後は `art-map.md` の表を更新し、`CryTexPath`/`ShowLine` の差し替え（§B・§C）を実装。

## G. 実装チェックリスト
- [ ] 当たり判定座標は動かさず、見た目だけ動かしている（A・E）
- [ ] 改心は cry を経てクロスフェード＋ヒットストップ＋余韻になっている（B）
- [ ] 立ち絵に呼吸（＋可能ならまばたき）＋切替クロスフェードを入れた（C）
- [ ] ボス登場に予備動作（スライド/フェード/スケール/着地）を入れた（D）
- [ ] 被弾でミナが反応（のけぞり/squash）、移動でバンク、発射で反動（D）
- [ ] フォロワーの登退場に手応え（ポップ/フェード）（D）
- [ ] 動かしすぎ・読みにくさが無いか screenshots/play-game で確認（E）
- [ ] 新規差分は既存画風・サイズに厳密に揃えて発注（F）
- [ ] 死蔵（koharu_face_pale）を先に接続した（art-map §0）
- [ ] 表情が物語ビートと一致（器化していない）（`/maeda` P4）
