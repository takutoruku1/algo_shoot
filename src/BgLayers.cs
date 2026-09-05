using Godot;
using System.Collections.Generic;

// BgLayers : 背景の「層」をデータ駆動で敷くコントローラ（StageBackground の1枚絵タイル経路の一般化）。
//
//   1枚絵を横タイルするだけの旧経路に対し、こちらは奥行きの違う複数の層（遠景／中景／近景／光）を
//   別々のスクロール比・Z・色掛け・合成で重ねる。素材は 1280x720 前提で、内部解像度(384x216)へ
//   高さフィット（216/720 = 0.3）すると幅も 384 にちょうど収まる＝全画面層はタイル1枚で足りる。
//
//   Z の割り当て（ScrollFx -70..-55 / StageImagery -50 には触れない）:
//     L1 遠景 -95 / L2 中景 -92 / L3 近景 -91 / L4 光 -88
//
//   スクロール比 scrollMul は層ごとの固定値（L1 0.15 / L2 0.45 / L3 1.0 / L4 0.0）。
//   これに BgScroll の位置係数（自機が右にいるほど速い 0.65..1.45）を乗算して共存させる＝
//   StageBackground / ScrollFx と同じ「左へ流れる＝左→右に前進」の語彙。
//
//   loop=true の層だけ横に2枚並べて左へシームレスに流す。loop=false の層は1枚を置いたまま
//   （scrollMul>0 なら左へ動くが巻き戻さない＝近景の一枚物が通り過ぎる、という使い方は想定しない。
//    近景の一枚物は scrollMul をそのまま持たせつつ loop=false で置く＝視差だけ効く）。
//
//   加算層（additive=true）は CanvasItemMaterial{Add} を各 Sprite2D に載せる（WorldGrade / FxLayer と同じ作法）。
//   色掛け（tint）は層ごとの Modulate。WorldGrade の CanvasModulate グレーディングとは別系統で二重掛けにしない
//   （tint は Root が持ち、WorldGrade は従来どおり CanvasModulate 側に残す）。
public partial class BgLayers : Node2D
{
    private const float ScreenWidth = 384f;
    private const float ScreenHeight = 216f;

    // 層の定義（Root から配列で注入する）。
    //   Path      : res:// のテクスチャ。読めない層は黙って飛ばす（他の層は敷かれる＝事故らない）。
    //   ScrollMul : 左へ流れる速さの倍率。0 で静止（光の層）。
    //   Z         : 絶対 ZIndex（L1 -95 / L2 -92 / L3 -91 / L4 -88 を想定）。
    //   Tint      : Modulate。無彩色素材への色掛けにも使う。
    //   Additive  : true で加算合成（光の層）。
    //   Loop      : true のとき横に2枚並べてシームレスループ。全画面の一枚物は false。
    //   Offset    : 画面座標での配置位置（既定 0,0）。素材座標(1280x720基準)から置きたいときは
    //               Root 側で 0.3 倍してから渡す（BgLayers は素材座標を知らない）。
    public readonly struct Layer
    {
        public readonly string Path;
        public readonly float ScrollMul;
        public readonly int Z;
        public readonly Color Tint;
        public readonly bool Additive;
        public readonly bool Loop;
        public readonly Vector2 Offset;

        public Layer(string path, float scrollMul, int z, Color tint, bool additive = false, bool loop = false,
                     Vector2 offset = default)
        {
            Path = path; ScrollMul = scrollMul; Z = z; Tint = tint;
            Additive = additive; Loop = loop; Offset = offset;
        }
    }

    // Root から代入する層リスト（奥→手前の順に並べる。空なら何も敷かない）。
    public Layer[] Layers = System.Array.Empty<Layer>();

    // 基準スクロール速度（px/s）。StageBackground.MidScrollSpeed と同じ控えめな値に揃える。
    public float ScrollSpeed = 13f;

    // 敷けた層が1つでもあるか（Root 側のフォールバック判定用）。
    public bool HasAny => _live.Count > 0;

    private sealed class Live
    {
        public Sprite2D[] Tiles = System.Array.Empty<Sprite2D>();
        public float TileW;
        public float X;           // ループ用オフセット（0..TileW）
        public float ScrollMul;
        public bool Loop;
        public bool Additive;
        public Color BaseTint;    // Root が指定した元の色（暗転はこれに係数を掛ける）
        public Vector2 Offset;
    }

    private readonly List<Live> _live = new List<Live>();

    // ───── ボス突入の暗転 ─────
    // L4（加算＝光）は α を 0 へ、それ以外は Modulate を 0.55 倍へ、0.8 秒でフェードする。
    private const float BossDimDur = 0.8f;
    private const float BossDimMul = 0.55f;
    private bool _dimming;
    private float _dimT;
    private float _dimK;          // 0=通常 1=暗転しきり

    // 全層に追加で掛かる色（Root から SetTint で差し替え可能）。既定は白＝素通し。
    private Color _globalTint = Colors.White;

    public override void _Ready()
    {
        ZIndex = -95;
        ZAsRelative = false;
        AddToGroup("bglayers");
        Build();
    }

    private void Build()
    {
        foreach (var def in Layers)
        {
            if (string.IsNullOrEmpty(def.Path) || !ResourceLoader.Exists(def.Path)) continue;
            var tex = ResourceLoader.Load<Texture2D>(def.Path);
            if (tex == null || tex.GetHeight() <= 0) continue;

            // 1280x720 の素材を内部解像度の高さに合わせる（216/720 = 0.3）。
            // 部分素材（L3 の一枚物など）も同じ 0.3 倍で置く＝素材どうしの大きさの関係が崩れない。
            float scale = ScreenHeight / 720f;
            float tileW = tex.GetWidth() * scale;

            // ループ層は画面幅を覆う枚数＋1（巻き戻し用に最低2枚）。非ループは1枚。
            int count = def.Loop ? Mathf.Max(2, Mathf.CeilToInt(ScreenWidth / Mathf.Max(1f, tileW)) + 1) : 1;

            var tiles = new Sprite2D[count];
            for (int i = 0; i < count; i++)
            {
                var spr = new Sprite2D
                {
                    Name = $"L{def.Z}_{i}",
                    Texture = tex,
                    Centered = false,
                    Scale = new Vector2(scale, scale),
                    Position = def.Offset + new Vector2(i * tileW, 0f),
                    ZIndex = def.Z,
                    ZAsRelative = false,
                    TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                    Modulate = def.Tint,
                };
                if (def.Additive)
                    spr.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
                AddChild(spr);
                tiles[i] = spr;
            }

            _live.Add(new Live
            {
                Tiles = tiles, TileW = tileW, ScrollMul = def.ScrollMul,
                Loop = def.Loop, Additive = def.Additive, BaseTint = def.Tint, Offset = def.Offset,
            });
        }
    }

    // ボス突入：光を落として世界を沈める（0.8秒）。二度呼んでも進行中の暗転を巻き戻さない。
    public void EnterBoss()
    {
        if (_dimming || _dimK >= 1f) return;
        _dimming = true;
        _dimT = 0f;
    }

    // 全層に掛かる色を差し替える（浄化で世界が暖まる等、Root 側の演出から呼ぶ）。
    public void SetTint(Color c)
    {
        _globalTint = c;
        ApplyTint();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_dimming)
        {
            _dimT += dt;
            float k = Mathf.Clamp(_dimT / BossDimDur, 0f, 1f);
            _dimK = k * k * (3f - 2f * k);   // smoothstep＝唐突に切らない（CrossfadeBossTo と同じ作法）
            if (k >= 1f) { _dimK = 1f; _dimming = false; }
            ApplyTint();
        }

        if (_live.Count == 0) return;

        // 位置係数：自機が右にいるほど背景が速く流れる（StageBackground / ScrollFx と同じ 0.65..1.45）。
        float posMul = 0.65f + 0.80f * BgScroll.PlayerNx(this);

        foreach (var l in _live)
        {
            if (l.ScrollMul <= 0f) continue;   // 光の層は動かさない
            l.X += ScrollSpeed * l.ScrollMul * posMul * dt;

            if (l.Loop && l.TileW > 0f)
            {
                // span を法に取って毎フレーム並べ直す（蓄積誤差なし。StageBackground と同じ）。
                float off = l.X % l.TileW;
                for (int i = 0; i < l.Tiles.Length; i++)
                {
                    var p = l.Tiles[i].Position;
                    p.X = l.Offset.X + i * l.TileW - off;
                    if (p.X <= l.Offset.X - l.TileW) p.X += l.Tiles.Length * l.TileW;
                    l.Tiles[i].Position = p;
                }
            }
            else
            {
                // 非ループ：視差だけ効かせて左へずらす（巻き戻さない）。
                foreach (var s in l.Tiles)
                {
                    var p = s.Position;
                    p.X = l.Offset.X - l.X;
                    s.Position = p;
                }
            }
        }
    }

    // 暗転係数と全体色を各層の Modulate へ反映する。
    private void ApplyTint()
    {
        foreach (var l in _live)
        {
            var b = l.BaseTint;
            // 加算層（光）はαを 0 へ、それ以外は明度を 0.55 倍へ。
            float rgbMul = l.Additive ? 1f : Mathf.Lerp(1f, BossDimMul, _dimK);
            float aMul = l.Additive ? Mathf.Lerp(1f, 0f, _dimK) : 1f;
            var c = new Color(
                b.R * _globalTint.R * rgbMul,
                b.G * _globalTint.G * rgbMul,
                b.B * _globalTint.B * rgbMul,
                b.A * _globalTint.A * aMul);
            foreach (var s in l.Tiles) s.Modulate = c;
        }
    }
}
