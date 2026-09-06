using Godot;
using System.Collections.Generic;

// ShellPeelFx : レイの改心（決定打の行）だけで使う「ガワが左右にほどける」層。
//
//   狙いは破壊の勢いではなく、「笑顔を維持していた面」と「その奥にいた人」が別のものだった、と
//   分かる瞬間（docs/20260906/astra_試行_ガワ割れ.md 案1）。だから：
//     ・中の人は最初からガワの背後に置く＝画像の差し替えではなく、ガワが退くことで見えるようにする。
//     ・ガワは中央のわずかに折れた線で二分割した薄い面。左が先、右が 0.1 秒遅れて外へ。
//       非対称にするのは「必殺技の解除」ではなく「保持する力がなくなった」に見せるため。
//     ・飛ばす距離は最大 12px、回転は合計でも 1 度未満。72px の本体では、これ以上動かすと撃破爆発に見える。
//
//   作法は PanSlamFx / BossParts と同じ「自分で動いて自滅する層」：
//     ・当たり判定は一切持たない（純粋な Node2D＋子の Polygon2D/Sprite2D）。
//     ・自分では時間を進めない。会話中（Hud.BubblePaused）でも止めたくないので、
//       親（Enemy）が Tick(dt) を呼ぶ＝止める／進めるの判断は呼び出し側が握る。
//
//   親の座標系にぶら下がるので、本体（Enemy）が動けば一緒に動く。改心中は本体が止まっている
//   （Enemy._PhysicsProcess が _crying で早期 return する）ので、実際には足元も動かない。
public partial class ShellPeelFx : Node2D
{
    // ガワの1枚（左／右）。位置と濃さを、開始からの経過秒だけで決める（速度を積分しない＝暴れない）。
    private sealed class Face
    {
        public Polygon2D Node = null!;
        public Vector2 Drift;        // 動き切ったときの変位(px)
        public float RotationRad;    // 同じく回転(rad)。1度未満に留める
        public float MoveFrom, MoveTo;
        public float FadeFrom, FadeTo;
    }

    private readonly List<Face> _faces = new List<Face>();
    private float _t;
    private bool _done;

    // 案1の時刻表（docs の表）。左が先に動き、右が 0.1 秒遅れる。
    private const float LeftMoveFrom = 0.40f, LeftMoveTo = 1.40f;
    private const float LeftFadeFrom = 1.05f, LeftFadeTo = 1.70f;
    private const float RightMoveFrom = 0.50f, RightMoveTo = 1.50f;
    private const float RightFadeFrom = 1.15f, RightFadeTo = 1.80f;
    // 二枚が離れ切ったときの隙間(px)。docs は「1280×720・本体72px」で最大12px と書くが、
    // この game の設計解像度は 384×216＝本体72px が画面高の 1/3 を占める（docs の想定は 1/10）。
    // 12px のままだと画面上では docs の3倍以上に開き、実機では「静かな解除」ではなく撃破の破裂に見えた
    //（build/wiki_work/ingame_rei_peel_02.png で確認）。画面に対する見え方を docs に合わせて
    // 216/720＝0.3 倍に詰め、合計 3.6px（左1.65＋右1.95）にする。隙間から中の人は十分読める。
    private const float LeftDriftX = -1.65f, RightDriftX = 1.95f;
    private const float LeftDriftY = 0.18f, RightDriftY = 0.3f;
    // 回転も同じ理由で docs の値より浅く。合計 0.24 度＝「面が二枚ある」以上の意味を持たせない。
    private const float LeftRotDeg = -0.11f, RightRotDeg = 0.13f;

    // ガワの二枚が消え切る時刻＝この後は中の人だけが残る。
    public const float ShellGoneAt = 1.80f;
    // 部品（枠・吹き出し・星）も消え切って全部が静止する時刻。ここから静止保持が始まる。
    public const float SettleAt = 2.20f;

    public bool Finished => _done;
    public float Time => _t;

    // 組み立てる。
    //   shell     : ガワのテクスチャ（＝いま画面に出ている待機絵）
    //   shellSize : ガワの表示サイズ(px)。本体スプライトの Scale を掛けた実寸
    //   shellOffs : ガワの表示中心（親のローカル座標。本体スプライトの Position＋Offset×Scale）
    //   flipH     : 本体スプライトの左右反転。UV ではなく描画スケールで合わせる（絵の向きを保つ）
    //   inner     : 中の人のテクスチャ
    //   innerSize : 中の人の表示サイズ(px)
    //   innerOffs : 中の人の表示中心（足元がガワと揃う位置。呼び出し側が計算して渡す）
    public void Configure(Texture2D shell, Vector2 shellSize, Vector2 shellOffs, bool flipH,
                          Texture2D inner, Vector2 innerSize, Vector2 innerOffs)
    {
        // 中の人を先に（＝背後に）置く。ガワが退くことで見えるようにするので、ここでは何もしない
        // ＝呼吸も顔上げも涙の追加もない。ZIndex はガワより後ろ。
        AddChild(new Sprite2D
        {
            Name = "Inner",
            Texture = inner,
            Centered = true,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            ZIndex = -1,
            Position = innerOffs,
            Scale = new Vector2(innerSize.X / Mathf.Max(1, inner.GetWidth()),
                                innerSize.Y / Mathf.Max(1, inner.GetHeight())),
        });

        // 中央の割れ目。左右で同じ点列を使う＝止まっているうちは隙間ができない。
        // 折れ幅は表示幅の 2.5% までなので、72px の本体では 1px 前後にしかならない（縦一直線に見せない程度）。
        var seam = new[]
        {
            new Vector2( 0.000f, -0.50f),
            new Vector2(-0.025f, -0.20f),
            new Vector2( 0.020f,  0.05f),
            new Vector2(-0.015f,  0.28f),
            new Vector2( 0.010f,  0.50f),
        };

        var left = new List<Vector2> { new Vector2(-0.5f, -0.5f) };
        left.AddRange(seam);
        left.Add(new Vector2(-0.5f, 0.5f));

        var right = new List<Vector2> { seam[0], new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f) };
        for (int i = seam.Length - 1; i >= 1; i--) right.Add(seam[i]);

        AddFace(shell, shellSize, shellOffs, flipH, left.ToArray(),
                new Vector2(LeftDriftX, LeftDriftY), LeftRotDeg,
                LeftMoveFrom, LeftMoveTo, LeftFadeFrom, LeftFadeTo);
        AddFace(shell, shellSize, shellOffs, flipH, right.ToArray(),
                new Vector2(RightDriftX, RightDriftY), RightRotDeg,
                RightMoveFrom, RightMoveTo, RightFadeFrom, RightFadeTo);
    }

    // 1枚ぶんの面を作る。多角形は「表示サイズに対する比率」で受け取り、ここで実 px と UV(テクスチャ画素)へ直す。
    // 反転（flipH）は Polygon2D の Scale.x を負にして出す＝UV をいじらないので絵柄が左右で入れ替わらない。
    private void AddFace(Texture2D tex, Vector2 size, Vector2 offs, bool flipH, Vector2[] norm,
                         Vector2 drift, float rotDeg, float moveFrom, float moveTo, float fadeFrom, float fadeTo)
    {
        var poly = new Vector2[norm.Length];
        var uv = new Vector2[norm.Length];
        Vector2 texSize = tex.GetSize();
        for (int i = 0; i < norm.Length; i++)
        {
            poly[i] = new Vector2(norm[i].X * size.X, norm[i].Y * size.Y);
            uv[i] = new Vector2((norm[i].X + 0.5f) * texSize.X, (norm[i].Y + 0.5f) * texSize.Y);
        }

        // 親（ガワの表示中心）に置く器を挟む：器が反転と定位置を持ち、面はそこからの変位だけを動かす。
        // こうしないと反転時に Drift の符号まで裏返って「左が先」が崩れる。
        var pivot = new Node2D
        {
            Position = offs,
            Scale = new Vector2(flipH ? -1f : 1f, 1f),
        };
        AddChild(pivot);

        var node = new Polygon2D
        {
            Texture = tex,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Polygon = poly,
            UV = uv,
            ZIndex = 0,
        };
        pivot.AddChild(node);

        _faces.Add(new Face
        {
            Node = node,
            // 反転した器の中にいるので、外へ退く向きは器の中で左右をそのまま指定してよい
            //（器ごと裏返るため、画面上でも「顔の左側の面」が先に動く）。
            Drift = drift,
            RotationRad = Mathf.DegToRad(rotDeg),
            MoveFrom = moveFrom, MoveTo = moveTo,
            FadeFrom = fadeFrom, FadeTo = fadeTo,
        });
    }

    // 時間を進める。会話中も進めたいので、親（Enemy）が明示的に呼ぶ。
    public void Tick(float dt)
    {
        if (_done) return;
        _t += dt;
        foreach (var f in _faces)
        {
            float move = SmoothStep(_t, f.MoveFrom, f.MoveTo);
            float alpha = 1f - SmoothStep(_t, f.FadeFrom, f.FadeTo);
            f.Node.Position = f.Drift * move;
            f.Node.Rotation = f.RotationRad * move;
            // ガワは退き切るまで不透明のまま（クロスフェードにしない＝「変身」に見せない）。
            f.Node.Modulate = new Color(1f, 1f, 1f, alpha);
        }
        if (_t >= ShellGoneAt) _done = true;
    }

    // 中の人だけを残してこの層を畳む（本体スプライトへ引き渡した後に呼ぶ）。
    public void Dismiss()
    {
        _done = true;
        QueueFree();
    }

    // 3t²-2t³。加速も減速もある＝「押し開けた」ではなく「支えを失って離れた」に見える。
    private static float SmoothStep(float t, float from, float to)
    {
        float x = Mathf.Clamp((t - from) / Mathf.Max(0.001f, to - from), 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}
