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
//   loop=true の層だけ横に2枚並べて左へシームレスに流す。loop=false の層は1枚を置いたままにし、
//   scrollMul は「流す速さ」ではなく「自機の左右位置に紐づく視差スウェイの大きさ」として効く
//   （近景の一枚物＝傘や看板が画面外へ流れ去って二度と戻らないのを防ぐ）。
//
//   加算層（additive=true）は CanvasItemMaterial{Add} を各 Sprite2D に載せる（WorldGrade / FxLayer と同じ作法）。
//   色掛け（tint）は層ごとの Modulate。WorldGrade の CanvasModulate グレーディングとは別系統で二重掛けにしない
//   （tint は Root が持ち、WorldGrade は従来どおり CanvasModulate 側に残す）。
//
//   層セットの入れ替え（CrossfadeTo）: 道中の途中で場所が変わる面（STAGE2 こはる＝部屋→教室）のために、
//   新しい層セットを α0 で敷いてから 0.8〜1.2 秒で入れ替え、旧セットを解放する。唐突に切らない
//   （StageBackground.CrossfadeBossTo と同じ作法）。
public partial class BgLayers : Node2D
{
    private const float ScreenWidth = 384f;
    private const float ScreenHeight = 216f;

    // 層の定義（Root から配列で注入する）。
    //   Path      : res:// のテクスチャ。読めない層は黙って飛ばす（他の層は敷かれる＝事故らない）。
    //   ScrollMul : loop=true なら左へ流れる速さの倍率、loop=false なら視差スウェイの大きさ。0 で静止（光の層）。
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

    // ボス突入で何が起きるか。面ごとに Root から選ぶ。
    //   Dim   : 既定。光(L4)のαを 0 へ、L1〜L3 を 0.55 倍へ落として世界を沈める（STAGE1 あかり/STAGE2 こはる）。
    //   Brighten : 逆に光を増やす（STAGE3 レイ）。L1〜L3 は 0.7 倍に留め、BossLayers の層セットへ
    //              クロスフェードして金の光を足す＝舞台が煌々と点く。
    public enum BossBehavior { Dim, Brighten }
    public BossBehavior OnBoss = BossBehavior.Dim;

    // OnBoss=Brighten のとき、ボス突入でこの層セットへクロスフェードする（空なら層は据え置きで係数だけ）。
    public Layer[] BossLayers = System.Array.Empty<Layer>();

    // 基準スクロール速度（px/s）。StageBackground.MidScrollSpeed と同じ控えめな値に揃える。
    public float ScrollSpeed = 13f;

    // 非ループ層の視差スウェイ幅（px）と追従速度（px/s）。自機が左端→右端で最大この幅だけ左へ寄る。
    // 一枚物が画面外へ流れ去らないよう、非ループ層は「流す」のではなく「自機位置に紐づけて揺らす」。
    private const float ParallaxSway = 22f;
    private const float ParallaxFollow = 26f;

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
        public float Fade = 1f;   // 層セットのクロスフェード用のα（旧セットは 1→0、新セットは 0→1）
    }

    private readonly List<Live> _live = new List<Live>();

    // ───── ボス突入の明暗 ─────
    // Dim（既定）: L4（加算＝光）は α を 0 へ、それ以外は Modulate を 0.45 倍へ、0.8 秒でフェードする。
    // 0.55 では浄化が進んだ面で WorldGrade の加算光（段階3の琥珀）と相殺し「空気が変わった」と分からなかった。
    // 係数を 0.45 まで下げ、併せて WorldGrade 側の加算光もボス中は半分に落とす（暗転を相殺させない）。
    // Brighten（STAGE3）: L4 はそのまま、L1〜L3 を 0.7 倍まで（暗転より浅く）落として本体を立たせる。
    private const float BossDimDur = 0.8f;
    private const float BossDimMul = 0.45f;
    private const float BossBrightMul = 0.70f;
    private bool _dimming;
    private float _dimT;
    private float _dimK;          // 0=通常 1=暗転（or 明転）しきり

    // 暗転のしきり（0=道中 1=ボス）を外へ公開する。WorldGrade がこれを読み、浄化が進んだ面で
    // 加算の光が暗転を打ち消さないよう自分の加算αを落とす（WorldGrade.BossAddMul）。
    public float BossDimK => _dimK;

    // ───── 層セットのクロスフェード（道中で場所が変わる面）─────
    // 新セットを α0 で敷いて _live に足し、旧セットの Live を _fadingOut に移す。
    // 進行中にもう一度呼ばれたら、走っている入れ替えを即着地させてから次を始める（層が無限に増えない）。
    private readonly List<Live> _fadingOut = new List<Live>();
    private readonly List<Live> _fadingIn = new List<Live>();
    private float _swapT, _swapDur;
    private bool _swapping;

    // 全層に追加で掛かる色（Root から SetTint で差し替え可能）。既定は白＝素通し。
    private Color _globalTint = Colors.White;

    public override void _Ready()
    {
        ZIndex = -95;
        ZAsRelative = false;
        AddToGroup("bglayers");
        Build(Layers, _live, 1f);
    }

    // 層定義の配列から Sprite2D を生やし、Live として into へ足す。fade は初期α（新セットは 0 で敷く）。
    private void Build(Layer[] defs, List<Live> into, float fade)
    {
        foreach (var def in defs)
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
                    Modulate = new Color(def.Tint.R, def.Tint.G, def.Tint.B, def.Tint.A * fade),
                };
                if (def.Additive)
                    spr.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
                AddChild(spr);
                tiles[i] = spr;
            }

            into.Add(new Live
            {
                Tiles = tiles, TileW = tileW, ScrollMul = def.ScrollMul,
                Loop = def.Loop, Additive = def.Additive, BaseTint = def.Tint, Offset = def.Offset,
                Fade = fade,
                // ループ層の X はループ用オフセット(0起点)、非ループ層の X は現在の画面X（初期位置＝配置位置）。
                X = def.Loop ? 0f : def.Offset.X,
            });
        }
    }

    // 層セットを丸ごと入れ替える（道中で場所が変わる面。例：こはるの部屋→教室）。
    // 新セットを α0 で敷いてから dur 秒で入れ替え、旧セットは着地時に解放する。唐突に切らない。
    // defs が1枚も読めなければ何もしない（現行の層が残る＝事故らない）。
    public void CrossfadeTo(Layer[] defs, float dur = 1.0f)
    {
        if (defs == null || defs.Length == 0) return;
        if (_swapping) FinishSwap();     // 連続要求：走っている入れ替えを着地させてから次へ

        int before = _live.Count;
        Build(defs, _live, 0f);
        if (_live.Count == before) return;   // 1枚も読めなかった＝現行のまま

        // 直前までの層を旧セットとして退避し、いま生やした層を新セットにする。
        for (int i = 0; i < before; i++) _fadingOut.Add(_live[i]);
        for (int i = before; i < _live.Count; i++) _fadingIn.Add(_live[i]);
        _swapT = 0f; _swapDur = Mathf.Max(0.05f, dur); _swapping = true;
        ApplyTint();
    }

    // 現行の層をすべて消す（新セット無し）。FINAL の巡回の終点＝三人の場所から離れ、
    // 背後のミナ自身の背景（生成グラデ）だけが残る＝旅が彼女に着地する。
    public void FadeOutAll(float dur = 1.0f)
    {
        if (_swapping) FinishSwap();
        if (_live.Count == 0) return;
        _fadingOut.AddRange(_live);
        _swapT = 0f; _swapDur = Mathf.Max(0.05f, dur); _swapping = true;
        ApplyTint();
    }

    // 入れ替えを着地させる：新セットを α1 に、旧セットを破棄して _live から外す。
    private void FinishSwap()
    {
        if (!_swapping) return;
        foreach (var l in _fadingOut)
        {
            foreach (var s in l.Tiles) s.QueueFree();
            _live.Remove(l);
        }
        foreach (var l in _fadingIn) l.Fade = 1f;
        _fadingOut.Clear(); _fadingIn.Clear();
        _swapping = false;
        ApplyTint();
    }

    // ボス突入：既定は光を落として世界を沈める（0.8秒）。OnBoss=Brighten の面（STAGE3 レイ）は
    // 逆に BossLayers（枠の全面版＋金の光）へ層セットをクロスフェードし、沈み方も浅くする。
    // 二度呼んでも進行中のフェードを巻き戻さない。
    public void EnterBoss()
    {
        if (_dimming || _dimK >= 1f) return;
        _dimming = true;
        _dimT = 0f;
        if (OnBoss == BossBehavior.Brighten && BossLayers.Length > 0) CrossfadeTo(BossLayers, BossDimDur);
    }

    // 層セットと「ボス中の見え方」を同時に差し替える（FINAL の巡回専用）。
    // FINAL は最初からボス中（_dimK=1）なので、巡る先ごとにその面のボス時の係数（Dim/Brighten）へ
    // 切り替えないと、レイの面だけ「煌々と点く」見え方が再現できない。層の入れ替えは CrossfadeTo に委ね、
    // ここは係数の差し替えだけを足す＝道中の面（Akari/Koharu/Rei Root）の挙動には一切触らない。
    public void CrossfadeToBoss(BossBehavior onBoss, Layer[] defs, float dur = 1.0f)
    {
        OnBoss = onBoss;
        CrossfadeTo(defs, dur);
        ApplyTint();   // 層が1枚も読めず CrossfadeTo が空振りしても、係数だけは現行層へ効かせる
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

        if (_swapping)
        {
            _swapT += dt;
            float k = Mathf.Clamp(_swapT / _swapDur, 0f, 1f);
            float s = k * k * (3f - 2f * k); // smoothstep
            foreach (var l in _fadingIn) l.Fade = s;
            foreach (var l in _fadingOut) l.Fade = 1f - s;
            ApplyTint();
            if (k >= 1f) FinishSwap();
        }

        if (_live.Count == 0) return;

        // 位置係数：自機が右にいるほど背景が速く流れる（StageBackground / ScrollFx と同じ 0.65..1.45）。
        float posMul = 0.65f + 0.80f * BgScroll.PlayerNx(this);

        float nx = BgScroll.PlayerNx(this);

        foreach (var l in _live)
        {
            if (l.ScrollMul <= 0f) continue;   // 光の層は動かさない

            if (l.Loop && l.TileW > 0f)
            {
                // ループ層：左へ流し続ける。span を法に取って毎フレーム並べ直す（蓄積誤差なし。StageBackground と同じ）。
                l.X += ScrollSpeed * l.ScrollMul * posMul * dt;
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
                // 非ループ層：流し続けると画面外へ出て二度と戻らないので、自機の左右位置に紐づけた
                // 有限の視差スウェイにする（nx 0→1 で右→左へ最大 ParallaxSway*scrollMul だけずれる）。
                // 置いた一枚物（近景の傘・看板など）が消えず、動きだけが手前らしく大きい。
                float target = l.Offset.X - ParallaxSway * l.ScrollMul * nx;
                l.X = Mathf.MoveToward(l.X, target, ParallaxFollow * dt);
                foreach (var s in l.Tiles) s.Position = new Vector2(l.X, s.Position.Y);
            }
        }
    }

    // 暗転（or 明転）係数・層セットのフェード・全体色を各層の Modulate へ反映する。
    private void ApplyTint()
    {
        bool brighten = OnBoss == BossBehavior.Brighten;
        foreach (var l in _live)
        {
            var b = l.BaseTint;
            // Dim: 加算層（光）はαを 0 へ、それ以外は明度を 0.45 倍へ。
            // Brighten: 光は消さず（レイのボスは煌々と点く）、L1〜L3 は 0.7 倍に留める。
            float dimTo = brighten ? BossBrightMul : BossDimMul;
            float rgbMul = l.Additive ? 1f : Mathf.Lerp(1f, dimTo, _dimK);
            float aMul = (!l.Additive || brighten) ? 1f : Mathf.Lerp(1f, 0f, _dimK);
            var c = new Color(
                b.R * _globalTint.R * rgbMul,
                b.G * _globalTint.G * rgbMul,
                b.B * _globalTint.B * rgbMul,
                b.A * _globalTint.A * aMul * l.Fade);
            foreach (var s in l.Tiles) s.Modulate = c;
        }
    }
}
