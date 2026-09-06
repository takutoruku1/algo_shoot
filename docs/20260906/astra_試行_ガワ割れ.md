## 方針

強めるのは、破壊の勢いではなく、**「笑顔を維持していた面」と「その奥にいた人」が、別のものだったと分かる瞬間**です。

共通して、次を変更します。

- **中の人を最初からガワの背後に置く。** 部品が消えた後の画像交換ではなく、ガワが退くことで見えるようにする。
- 現在の「本体の1.7倍に広がるひび」は、**本体とほぼ同寸・拡大なし・短時間**へ。
- 足元とボスの位置は動かさない。中の人に呼吸、顔上げ、涙、光輪を追加しない。
- 全部が静止した時点から、**3.6秒**待つ。会話送りによって短縮させない。
- BGM停止以外の画面全体の変化は原則なし。停止中の弾も、この瞬間に追加で発光・消滅させない。

以下の数値は1280×720の設計座標です。高さ72pxの本体なので、**破片の移動は2〜7pxでも十分読めます。**

---

# 案1：笑顔の面が、左右にほどける

中央に細い割れ目が一本入り、ガワが二枚の薄い面として左右に離れる案です。

左右対称に大きく開くのではなく、**片側が0.1秒遅れる**。その小さな非対称によって、必殺技の解除ではなく、保持する力がなくなった印象にします。

## 1. 時系列

| 時刻 | 見え方 |
|---|---|
| 0.0s | 決定打の行。BGM停止。ガワをcry絵へ。枠・星・吹き出しの公転が止まる。 |
| 0.2s | 本体とほぼ同寸のひびが薄く見える。拡大しない。中の人はまだガワに隠れている。 |
| 0.4s | ひびの強調が消える。左側の面だけが動き始める。 |
| 0.6s | 右側も遅れて動く。中央に約1〜2pxの隙間。背景ではなく、中の人の色が見える。 |
| 0.8s | 隙間が約4pxになる。うつむいた輪郭の一部が読める。 |
| 1.0s | 隙間が約7px。ガワはまだ不透明で、「二人の顔のクロスフェード」にしない。 |
| 1.2s | 隙間が約10px。ガワの笑顔より、中央の動かない姿へ視線が移る。 |
| 1.4s | 隙間は最大約12px。左右の面が薄くなり始める。回転は合計でも1度未満。 |
| 1.6s | 中の人の全身が読める。外へ退いたガワだけが薄く残る。 |
| 1.8s | ガワの二枚が消える。中の人は高さ54px、足元は元と同じ。 |
| 2.0s | 周囲の枠に金色がごく薄く残る。吹き出しの中には最後まで何も出さない。 |
| 2.2s | 部品も消える。ここから3.6秒の静止保持を開始。 |
| 2.4s | 中の人だけ。発光しない。 |
| 2.6s | 同上。ミナも近寄らない。 |
| 2.8s | 同上。新しい効果音を鳴らさない。 |
| 3.0s | 同上。静止保持は5.8sまで続く。 |

**音を入れるなら**、0.4sに一度だけ、低い音量の乾いた短音。ガラスが飛び散る高域の音は使いません。無音でも成立する設計です。

## 2. 技法・素材

- **Polygon2Dによる本体テクスチャの二分割**
  - 中央の境界をわずかに折れた線にする。
  - 同じcryテクスチャを、UVを合わせた二枚のPolygon2Dに貼る。
  - 透明部分は元画像のアルファをそのまま利用する。
- **Sprite2D**
  - 中の人を背後に配置。
- **既存AddBurst**
  - ひびの一瞬の強調だけに使う。
- **既存BossParts**
  - 公転を止め、外へ最大6px程度移動しながら消す。
- シェーダ、GPUParticles2D、画面フラッシュは使わない。
- **新規画像：0枚。**

---

# 案2：配信の面が、角から剥がれる

こちらは中央で開かず、**ガワを表示していた薄い面が、下側、側面、顔のある上側の順に剥がれる**案です。

先に足元が現れるため、「別の人に変身した」よりも、**そこにずっと立っていた人が、最後に見える**という読みになります。

## 1. 時系列

| 時刻 | 見え方 |
|---|---|
| 0.0s | 決定打の行。BGM停止。cry絵へ。全部品の公転が止まる。 |
| 0.2s | ガワだけが約1割暗くなる。画面全体は暗くしない。ひびは短く、薄く。 |
| 0.4s | ガワの下側の面が剥がれ始める。まだ移動はほとんど見えない。 |
| 0.6s | 下側に1px程度のずれ。光の帯と空の吹き出しが目立たなくなる。 |
| 0.8s | 中の人の足元が先に見える。右側の面もずれる。 |
| 1.0s | 左側も剥がれ始める。破片は放射せず、少し下へ落ちる。 |
| 1.2s | 下側の面がほぼ消える。素の服装が読めるが、笑顔の面は上側に残っている。 |
| 1.4s | 顔のある上側の面が、約1pxだけ遅れてずれる。 |
| 1.6s | 上側も薄くなり始める。中の人の顔はうつむいたまま。 |
| 1.8s | 側面が消える。上側の薄い残りだけが、斜め下へ退く。 |
| 2.0s | ガワの笑顔がほぼ読めなくなる。中の人には何も起きない。 |
| 2.2s | 上側の面が最後に消えかける。新しいひびや光は足さない。 |
| 2.4s | ガワは消失済み。最後の金の枠だけが薄く残る。 |
| 2.6s | 部品も消える。ここから3.6秒の静止保持を開始。 |
| 2.8s | 中の人だけ。 |
| 3.0s | 同上。静止保持は6.2sまで続く。 |

こちらは**破断音もなくてよい**案です。配信の光が消えていく順序そのものを演出にします。

## 2. 技法・素材

- **Polygon2Dによる四面分割**
  - 矩形テクスチャを、少し中心を外した一点から四つに分ける。
  - 下、右、左、上の順で、移動と透明化の開始時刻をずらす。
  - 回転は最大約1度、移動は最大4px程度。
- **Sprite2D**
  - 中の人は最初から背後で静止。
- **既存AddBurst**
  - 初動のひびを案1より弱く使う。
- **既存BossParts**
  - 放射方向の速度を廃止。ほぼ下方向へ数pxだけずれて消える。
- シェーダ、GPUParticles2Dは使わない。
- **新規画像：0枚。**

---

# 3. Godot 4.6 C# 実装案

二案とも同じ基盤で実装できます。違うのは、**分割形状と各面の時刻表**だけです。

以下は既存ボスクラスへ追加する主要メソッドです。`_body`、`_texCry`、`_texPost`は、プロジェクトの実際のフィールド名へ読み替えてください。

### 前提

- `_body`は中央原点、均等スケール、`Offset == Vector2.Zero`のSprite2D。
- `_bodyH`は改心前の表示高72pxで固定する。
- 本体テクスチャの外枠下端を、現在の足元合わせの基準にしている。
  - 透明余白を除いた独自の足元アンカーがある場合は、後述の位置計算を既存処理に置き換える。
- 一般的なスプライトシートのRegionではなく、cry/postそれぞれのTexture2Dを渡す。
- 本体の揺れ・点滅・公転更新は、レイのRedeem中には走らせない。

## 共通フィールドと補助処理

```csharp
using Godot;
using System;
using System.Collections.Generic;

// 既存ボスクラス内へ追加
private enum ReiShellStyle
{
    OpenSeam,  // 案1
    PeelSheet  // 案2
}

private sealed class ShellFace
{
    public Polygon2D Node = null!;
    public Vector2 Drift;
    public float RotationRad;
    public float MoveFrom;
    public float MoveTo;
    public float FadeFrom;
    public float FadeTo;
}

private readonly List<ShellFace> _reiFaces = new();

private Node2D? _reiLayer;
private Vector2 _reiBodyOrigin;
private Vector2 _reiPostPosition;
private Vector2[] _reiPartStarts = Array.Empty<Vector2>();

private ReiShellStyle _reiStyle;
private float _reiTime;
private float _reiSettleAt;
private bool _reiStarted;
private bool _reiCommitted;

private const float ReiQuietHold = 3.6f;

private static float Ease(float t, float from, float to)
{
    float x = Mathf.Clamp(
        (t - from) / Mathf.Max(0.001f, to - from), 0f, 1f);

    return x * x * (3f - 2f * x);
}

private static Vector2[] NormalizedPoints(
    Vector2 displaySize, params Vector2[] points)
{
    var result = new Vector2[points.Length];

    for (int i = 0; i < points.Length; i++)
    {
        result[i] = new Vector2(
            points[i].X * displaySize.X,
            points[i].Y * displaySize.Y);
    }

    return result;
}

private void AddShellFace(
    Texture2D texture,
    Vector2 displaySize,
    Vector2[] polygon,
    Vector2 drift,
    float rotationDeg,
    float moveFrom,
    float moveTo,
    float fadeFrom,
    float fadeTo)
{
    var uv = new Vector2[polygon.Length];
    Vector2 texSize = texture.GetSize();

    // Polygon2D.Uv は、テクスチャ上のピクセル座標。
    for (int i = 0; i < polygon.Length; i++)
    {
        uv[i] = new Vector2(
            (polygon[i].X / displaySize.X + 0.5f) * texSize.X,
            (polygon[i].Y / displaySize.Y + 0.5f) * texSize.Y);
    }

    var node = new Polygon2D
    {
        Texture = texture,
        Polygon = polygon,
        Uv = uv,
        ZIndex = 1
    };

    _reiLayer!.AddChild(node);

    _reiFaces.Add(new ShellFace
    {
        Node = node,
        Drift = drift,
        RotationRad = Mathf.DegToRad(rotationDeg),
        MoveFrom = moveFrom,
        MoveTo = moveTo,
        FadeFrom = fadeFrom,
        FadeTo = fadeTo
    });
}
```

## 決定打の行で一度だけ開始する

既存の「cryへ交換」「部品消失後にpostへ交換」の二段処理を、ここへ集約します。

**会話側の`BreakCryBodyNow()`のレイ分岐から、このメソッドへ委譲してください。** レイについては、旧`OnRedeem()`の`EmitRedeemFx()`と外向き速度設定を併走させません。

```csharp
private void BeginReiRedeemAtLine(
    ReiShellStyle style,
    Action? done = null)
{
    if (_reiStarted)
        return;

    _reiStarted = true;
    _reiCommitted = false;
    _reiStyle = style;
    _reiTime = 0f;

    // 先にOnRedeemで登録済みの場合、nullで上書きしない。
    if (done != null)
        _onRedeemed = done;

    _redeemNotified = false;
    _st = St.Redeem;
    _stT = 0f;

    _reiSettleAt =
        style == ReiShellStyle.OpenSeam ? 2.2f : 2.6f;

    _reiBodyOrigin = _body.Position;

    _reiLayer = new Node2D
    {
        Name = "ReiShellLayer",
        Position = _reiBodyOrigin,
        Rotation = _body.Rotation,
        ZIndex = _body.ZIndex,
        ZAsRelative = _body.ZAsRelative
    };

    _body.GetParent().AddChild(_reiLayer);

    // 中の人を先に背後へ置く。
    float postH = _bodyH * 0.75f;
    Vector2 footOffset = new Vector2(
        0f, (_bodyH - postH) * 0.5f);

    var post = new Sprite2D
    {
        Texture = _texPost,
        Centered = true,
        Position = footOffset,
        Scale = Vector2.One * (postH / _texPost.GetHeight()),
        ZIndex = 0
    };

    _reiLayer.AddChild(post);

    // 最後に本体Sprite2Dへ戻す際の位置。
    _reiPostPosition =
        _reiBodyOrigin + footOffset.Rotated(_body.Rotation);

    Vector2 shellSize = new Vector2(
        _bodyH * _texCry.GetWidth() / _texCry.GetHeight(),
        _bodyH);

    _reiFaces.Clear();

    if (style == ReiShellStyle.OpenSeam)
        BuildOpenSeam(_texCry, shellSize);
    else
        BuildPeelSheet(_texCry, shellSize);

    _body.Visible = false;

    // 現在位置を固定する。Redeem中は通常の部品更新を呼ばない。
    _reiPartStarts = new Vector2[_parts.Count];

    for (int i = 0; i < _parts.Count; i++)
    {
        var p = _parts[i];

        p.FadeAtRedeem = p.Fade;
        _reiPartStarts[i] = p.Extra + BasePos(p, p.T);
        p.Vel = Vector2.Zero;
    }

    EmitReiQuietCrack(style);
}
```

> `_parts`が参照型ではなく構造体のリストなら、変更後に`_parts[i] = p;`を書き戻してください。提示コードと同じく、ここでは参照型を想定しています。

## 案1の主要メソッド：二枚の面を作る

中央線は左右で同じ点列を使い、停止時には隙間ができないようにします。

```csharp
private void BuildOpenSeam(Texture2D texture, Vector2 size)
{
    // 座標は画像幅・高さに対する比率。
    // 中央の折れは小さく。72px表示で約1pxの変化になる。
    Vector2 s0 = new( 0.000f, -0.50f);
    Vector2 s1 = new(-0.025f, -0.20f);
    Vector2 s2 = new( 0.020f,  0.05f);
    Vector2 s3 = new(-0.015f,  0.28f);
    Vector2 s4 = new( 0.010f,  0.50f);

    Vector2[] left = NormalizedPoints(size,
        new(-0.5f, -0.5f),
        s0, s1, s2, s3, s4,
        new(-0.5f, 0.5f));

    Vector2[] right = NormalizedPoints(size,
        s0,
        new(0.5f, -0.5f),
        new(0.5f, 0.5f),
        s4, s3, s2, s1);

    AddShellFace(
        texture, size, left,
        drift: new Vector2(-5.5f, 0.6f),
        rotationDeg: -0.35f,
        moveFrom: 0.40f, moveTo: 1.40f,
        fadeFrom: 1.25f, fadeTo: 1.75f);

    AddShellFace(
        texture, size, right,
        drift: new Vector2(6.5f, 1.0f),
        rotationDeg: 0.45f,
        moveFrom: 0.50f, moveTo: 1.50f,
        fadeFrom: 1.35f, fadeTo: 1.80f);
}
```

## 案2の主要メソッド：剥がれる四面を作る

面を増やしすぎると粉砕に見えるため、四枚に留めます。分割点は可能なら、cry絵の既存のひびに合わせて調整します。

```csharp
private void BuildPeelSheet(Texture2D texture, Vector2 size)
{
    Vector2 tl = new(-0.5f, -0.5f);
    Vector2 tr = new( 0.5f, -0.5f);
    Vector2 br = new( 0.5f,  0.5f);
    Vector2 bl = new(-0.5f,  0.5f);

    // 上側の面が、ガワの顔を最後まで覆いやすい位置。
    Vector2 c = new(-0.06f, 0.08f);

    // 下
    AddShellFace(
        texture, size, NormalizedPoints(size, c, br, bl),
        drift: new Vector2(0.3f, 3.5f),
        rotationDeg: 0.4f,
        moveFrom: 0.40f, moveTo: 1.25f,
        fadeFrom: 0.65f, fadeTo: 1.35f);

    // 右
    AddShellFace(
        texture, size, NormalizedPoints(size, c, tr, br),
        drift: new Vector2(2.0f, 2.5f),
        rotationDeg: 0.8f,
        moveFrom: 0.65f, moveTo: 1.60f,
        fadeFrom: 0.95f, fadeTo: 1.80f);

    // 左
    AddShellFace(
        texture, size, NormalizedPoints(size, c, bl, tl),
        drift: new Vector2(-1.5f, 3.0f),
        rotationDeg: -0.7f,
        moveFrom: 0.85f, moveTo: 1.75f,
        fadeFrom: 1.10f, fadeTo: 1.95f);

    // 上：笑顔を載せた面が最後に退く。
    AddShellFace(
        texture, size, NormalizedPoints(size, c, tl, tr),
        drift: new Vector2(1.0f, 2.5f),
        rotationDeg: 1.0f,
        moveFrom: 1.20f, moveTo: 2.10f,
        fadeFrom: 1.45f, fadeTo: 2.30f);
}
```

## 共通更新：部品も同じ時計で動かす

`BubblePaused`でも、この更新だけは進めます。ゲーム全体を再開するのではありません。

```csharp
private void TickReiRedeem(float dt)
{
    if (!_reiStarted || _redeemNotified)
        return;

    _reiTime += dt;
    float t = _reiTime;

    if (!_reiCommitted)
    {
        // 案2だけ、ガワの表示光をわずかに落とす。
        float brightness =
            _reiStyle == ReiShellStyle.PeelSheet
                ? Mathf.Lerp(1f, 0.88f, Ease(t, 0f, 0.25f))
                : 1f;

        foreach (var f in _reiFaces)
        {
            float move = Ease(t, f.MoveFrom, f.MoveTo);
            float alpha = 1f - Ease(t, f.FadeFrom, f.FadeTo);

            f.Node.Position = f.Drift * move;
            f.Node.Rotation = f.RotationRad * move;
            f.Node.Modulate =
                new Color(brightness, brightness, brightness, alpha);
        }

        TickReiParts(t);

        if (t >= _reiSettleAt)
            CommitReiPost();
    }

    if (t >= _reiSettleAt + ReiQuietHold)
    {
        _redeemNotified = true;
        _onRedeemed?.Invoke();
    }
}

private void TickReiParts(float t)
{
    for (int i = 0; i < _parts.Count; i++)
    {
        var p = _parts[i];
        int group = i % 3;

        Vector2 start = _reiPartStarts[i];
        Vector2 drift;
        float from;
        float to;

        if (_reiStyle == ReiShellStyle.OpenSeam)
        {
            Vector2 dir = start.LengthSquared() > 1f
                ? start.Normalized()
                : Vector2.Up;

            drift = dir * (4f + group);
            from = 0.20f + group * 0.12f;
            to = 1.65f + group * 0.275f; // 最後が2.2秒
        }
        else
        {
            drift = new Vector2((group - 1) * 0.8f, 3f + group);
            from = 0.12f + group * 0.16f;
            to = 2.00f + group * 0.30f;  // 最後が2.6秒
        }

        float k = Ease(t, from, to);

        // BasePosとの合成後の位置を、開始位置から制御。
        p.Extra = start + drift * k - BasePos(p, p.T);
        p.Fade = p.FadeAtRedeem * (1f - k);
    }
}

private void CommitReiPost()
{
    _reiCommitted = true;

    _body.Texture = _texPost;
    _body.Scale = Vector2.One *
        ((_bodyH * 0.75f) / _texPost.GetHeight());

    _body.Position = _reiPostPosition;
    _body.Visible = true;

    // 同一フレームに二重描画されないよう先に隠す。
    if (_reiLayer != null)
    {
        _reiLayer.Visible = false;
        _reiLayer.QueueFree();
        _reiLayer = null;
    }

    _reiFaces.Clear();
}
```

部品グループの`i % 3`は既存データ構造に依存しない仮実装です。実制作では、部品生成時に退場グループを持たせ、次の順にすると意図が通ります。

1. 光の帯・空の吹き出し
2. 小さな星
3. 金の枠

**最後に残すのは枠であって、涙や浄化光ではありません。**

## 既存AddBurstへの接続

`AddBurst`の定義自体は未提示なので、以下は**第5引数が初期アルファ、第6引数が寿命**の実装を想定しています。実際の定義が異なる場合は、このラッパーだけ引数位置を合わせてください。

```csharp
private void EmitReiQuietCrack(ReiShellStyle style)
{
    float alpha =
        style == ReiShellStyle.OpenSeam ? 0.28f : 0.18f;

    AddBurst(
        _texCrack,
        Vector2.Zero,
        0f,
        _bodyH * 1.03f,
        alpha,
        0.42f,
        additive: false,
        growK: 0f);
}
```

ここは二点、既存描画系の確認が必要です。

- **描画順**  
  親の`_Draw()`でBurstを描いている場合、同じZの子Polygon2Dより背面になります。改心ひびだけは、ガワより手前の専用`RedeemFrontFx`描画ノードへ振り分けてください。描画関数自体は既存Burst描画を再利用できます。
- **停止中の寿命更新**  
  Burstの寿命も`BubblePaused`中に進めます。そうしないと0.42秒のひびが会話中ずっと残ります。

ひび画像が強い白なら、Burstの描画色で薄い菫へ寄せます。**新しい白い線を走らせる必要はありません。**

## 更新経路の差し替え

既存`_Process`または演出更新メソッドの、`BubblePaused`判定より前へ置きます。

```csharp
// 通常の_stT加算・部品更新・BubblePaused判定より前。
if (_name == "rei" && _st == St.Redeem)
{
    TickReiRedeem((float)delta);
    return;
}
```

この分岐では、旧Redeemの次の処理を呼びません。

- 旧ひびBurstの追加
- 部品の通常公転・速度積分
- 「部品が消えたらpostへ交換」
- 旧タイマーによる完了通知

なお、`BubblePaused`が独自フラグではなく`GetTree().Paused`も使う構成なら、この更新は**専用演出ノードだけ**`ProcessMode = ProcessModeEnum.Always`にして実行します。ボス全体や敵全体を`Always`にするのは避けてください。

---

# 4. やり過ぎになる境界

## 案1で避けること

- **左右を15〜20px以上飛ばす**  
  72pxの本体では、静かな解除より撃破爆発に見え始めます。
- **回転を数度以上付ける**  
  顔や身体の切断として読まれやすくなります。二枚の「表示面」であることを保ちます。
- **隙間から白い柱・翼・強いブルームを出す**  
  中の人がミナによって聖化された印象になります。
- **ガワと中の人を全身クロスフェードする**  
  「奥にいた人」ではなく「変身」に寄ります。最初はガワを不透明のまま移動させます。
- **静止保持中に顔を上げさせる**  
  この場で回復しきったように見せない方が、対象読者には誠実です。

## 案2で避けること

- **細片を十枚、二十枚と増やす**  
  紙吹雪・浄化粒子・粉砕の見た目に寄ります。四枚で十分です。
- **高速な走査線、RGBずれ、ノイズを足す**  
  VTuber／AIの記号が強くなり、本人の疲弊から注意が逸れます。
- **面を大きく落下・バウンドさせる**  
  床に物体として残すと、壊された身体の印象が出ます。数pxで消します。
- **空の吹き出しに悪意あるコメントを最後に表示する**  
  決定打の台詞と中の人より、加害の言葉が記憶に残ります。
- **最後の面だけを長く残す**  
  笑顔と素顔の重なりが長いと不気味になるため、顔の退場は2.3秒までに完了させます。

また両案とも、決定打の台詞に合わせて「書いて、消した一行」の実物を画面へ再掲しません。**見ていたことは伝えるが、観客へ暴露はしない**という距離感を守ります。

---

**推奨は案1「笑顔の面が、左右にほどける」です。**  
72pxでも「割れ目の奥に、すでに人がいた」と読み取りやすく、静かなまま既存演出との差を出せます。  
案2はより内省的ですが、素顔が読めるまで時間がかかるため、まず案1で間と移動量を詰めるのが確実です。