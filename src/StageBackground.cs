using Godot;

// StageBackground : ステージ背景のデータ駆動コントローラ（旧 static 額背景 Sprite2D の置き換え）。
//
//   ・道中(mid)背景 ＝ 1枚絵を画面高(216)に合わせて横にタイルし、左へシームレスにループ。
//     自機は画面内にほぼ留まるので、背景が左へ流れる＝「左→右に前進している」移動感を作る。
//     （ScrollFx の near/far パララックス層・StageImagery・MurkVignette はこの上にそのまま重なる）
//   ・ボス(boss)背景 ＝ ボス戦突入(EnterBoss)で道中背景を隠し、専用背景へ差し替え。
//     軽い動きだけ付ける：ゆっくりした横ドリフト＋ごく弱い呼吸スケール＋光の明滅(modulate)。やり過ぎない。
//
//   ・層(layers)背景 ＝ LayerDefs に層定義の配列を入れると BgLayers（L1 -95 / L2 -92 / L3 -91 / L4 -88）へ委譲する。
//     このとき 1枚絵タイルは作らず、EnterBoss は「背景の差し替え」ではなく「暗転」を意味する。
//     LayerDefs が空なら以下の 1枚絵経路のまま＝既存ステージは何も変わらない。
//
//   ZIndex は旧額背景と同じ -90（ScrollFx -70..-55 / StageImagery -50 / MurkVignette -45 / 弾0.. の奥）。
//   背景パスは Root から MidBgPath / BossBgPath で注入する＝新アートが来たらパスを差すだけで反映できる。
//   BossBgPath を空にすると（中ボスのみ等）EnterBoss を呼ばない＝道中背景のまま。
//   テクスチャが読めなければ何も描かず HasMid=false（Root 側で単色フィルにフォールバック）。
public partial class StageBackground : Node2D
{
    private const float ScreenWidth = 384f;
    private const float ScreenHeight = 216f;

    // ───── データ駆動パラメータ（Root から代入） ─────
    public string MidBgPath = "";    // 道中の横スクロール背景（無ければフォールバック）
    public string BossBgPath = "";   // ボス専用背景（無ければボス突入でも切替なし）
    // 画像パスではなく生成テクスチャを直接ボス背景に使いたい場合（例：FINAL の暗いグラデ）。
    // BossBgPath より優先。ラスボのように画像アセットを持たないステージで使う。
    public Texture2D BossBgTexture = null!;
    public bool StartInBoss = false; // 道中の無いステージ（ラスボ）は最初からボス背景で軽く動かす。

    // 層システム（BgLayers）へ渡す層定義。空でなければこちらが使われ、MidBgPath/BossBgPath は無視する。
    // 空なら従来どおり 1枚絵タイルの経路に落ちる＝既存ステージは何も変わらない。
    public BgLayers.Layer[] LayerDefs = System.Array.Empty<BgLayers.Layer>();
    private BgLayers _layers = null!;

    // ───── tunable（控えめに） ─────
    public float MidScrollSpeed = 13f;     // px/s。道中背景が左へ流れる速さ＝前進感。控えめ（弾の視認を妨げない／画面酔い対策で半減）
    public float BossDriftSpeed = 4f;      // px/s。ボス背景の横ドリフト（ゆっくり／半減）
    public float BossBreathAmp = 0.012f;   // 呼吸スケール振幅（±1.2%。やり過ぎない）
    public float BossBreathHz = 0.18f;     // 呼吸の周期（Hz）
    public float BossPulseAmp = 0.06f;     // 光の明滅振幅（modulate ±6%）
    public float BossPulseHz = 0.5f;       // 明滅の周期（Hz）

    public bool HasMid { get; private set; }

    private enum Mode { Mid, Boss }
    private Mode _mode = Mode.Mid;
    private double _t;

    // 道中：横タイル群（左スクロール・シームレスループ）
    private Sprite2D[] _midTiles = System.Array.Empty<Sprite2D>();
    private float _midTileW;     // 1枚分の表示幅(px)
    private float _midX;         // ループ用オフセット

    // ボス：専用背景タイル群（横ドリフトでループ。呼吸/明滅は親 modulate で）
    private Sprite2D[] _bossTiles = System.Array.Empty<Sprite2D>();
    private float _bossTileW;
    private float _bossX;
    private Texture2D _bossTex = null!;     // 遅延ロード（EnterBoss で初めて読む）
    private bool _bossReady;

    // ───── ボス背景クロスフェード（FINAL：歴代ボス背景の追体験）─────
    // 既存のボスタイル群(_bossTiles)を「現行層」とし、切替のたびに別の Sprite2D 群を「新層」として
    // 生成→α0→1 でフェードイン、旧層はα1→0 で捨てる。WorldGrade と同じ作法＝唐突に切り替えない。
    // Dim は「歴代背景は道中用で明るい」ため必須：ボス弾幕の下では暗め・低コントラストに落として敷く。
    private Sprite2D[] _fadeTiles = System.Array.Empty<Sprite2D>();
    private float _fadeX, _fadeTileW;
    private float _fadeT, _fadeDur = 2.4f;
    private bool _fading;
    private Color _curDim = Colors.White, _newDim = Colors.White;

    // 指定した背景テクスチャへクロスフェードする（ボスモード限定）。
    //   path : res:// テクスチャ。読めなければ何もしない（現行背景を残す＝事故らない）。
    //   dim  : 敷き色。歴代の道中背景はここで暗く沈める（例 0.34,0.30,0.42）。
    //   dur  : フェード秒。
    public void CrossfadeBossTo(string path, Color dim, float dur = 2.4f)
    {
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) return;
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null) return;
        CrossfadeBossTo(tex, dim, dur);
    }

    public void CrossfadeBossTo(Texture2D tex, Color dim, float dur = 2.4f)
    {
        if (tex == null) return;
        if (_layers != null) return;    // 層モードは1枚絵のクロスフェードを持たない（層ごとの入替は別タスク）
        if (_fading) FinishFade();       // 連続要求：進行中のフェードを即着地させてから次へ（層が増えない）
        if (!_bossReady) SetupBoss();
        _mode = Mode.Boss;
        foreach (var s in _midTiles) s.Visible = false;

        float scale = ScreenHeight / tex.GetHeight();
        _fadeTileW = tex.GetWidth() * scale;
        int count = Mathf.Max(2, Mathf.CeilToInt(ScreenWidth / _fadeTileW) + 2);
        var tiles = new Sprite2D[count];
        for (int i = 0; i < count; i++)
        {
            var spr = new Sprite2D
            {
                Name = $"Fade{i}",
                Texture = tex, Centered = false,
                Scale = new Vector2(scale, scale),
                Position = new Vector2(i * _fadeTileW, 0f),
                ZIndex = -89, ZAsRelative = false,   // 現行層(-90)のすぐ手前に重ねる
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                Modulate = new Color(dim.R, dim.G, dim.B, 0f),
            };
            AddChild(spr);
            tiles[i] = spr;
        }
        _fadeTiles = tiles; _fadeX = _bossX; _newDim = dim;
        _fadeT = 0f; _fadeDur = Mathf.Max(0.05f, dur); _fading = true;
    }

    // 新層を現行層に昇格させ、旧層を破棄する。
    private void FinishFade()
    {
        if (!_fading) return;
        foreach (var s in _bossTiles) s.QueueFree();
        foreach (var s in _fadeTiles) { s.ZIndex = -90; s.Modulate = _newDim; }
        _bossTiles = _fadeTiles; _bossTileW = _fadeTileW; _bossX = _fadeX;
        _curDim = _newDim;
        _fadeTiles = System.Array.Empty<Sprite2D>();
        _fading = false;
    }

    public override void _Ready()
    {
        ZIndex = -90;
        ZAsRelative = false;
        AddToGroup("stagebg");

        // 層リストが与えられていれば BgLayers に委譲する（1枚絵タイルは作らない）。
        if (LayerDefs.Length > 0)
        {
            _layers = new BgLayers { Name = "BgLayers", Layers = LayerDefs, ScrollSpeed = MidScrollSpeed };
            AddChild(_layers);
            HasMid = _layers.HasAny;
            if (HasMid)
            {
                // 呼吸/明滅は 1枚絵ボス背景のための演出。層システムでは親を動かさない（層ごとの視差が壊れる）。
                if (StartInBoss) EnterBoss();
                return;
            }
            // 層が1枚も読めなかった＝旧経路へ落ちる（下へ抜ける）。
            _layers.QueueFree();
            _layers = null!;
        }

        SetupMid();
        // 道中の無いステージ（ラスボ）は最初からボス背景で軽く動かす。
        if (StartInBoss) EnterBoss();
    }

    // 道中背景を画面高に合わせ、横に画面幅+1枚ぶんタイルする（左ループ用に最低2枚）。
    private void SetupMid()
    {
        if (string.IsNullOrEmpty(MidBgPath) || !ResourceLoader.Exists(MidBgPath)) return;
        var tex = ResourceLoader.Load<Texture2D>(MidBgPath);
        if (tex == null) return;

        float scale = ScreenHeight / tex.GetHeight();
        _midTileW = tex.GetWidth() * scale;
        int count = Mathf.Max(2, Mathf.CeilToInt(ScreenWidth / _midTileW) + 1);

        var tiles = new Sprite2D[count];
        for (int i = 0; i < count; i++)
        {
            var spr = new Sprite2D
            {
                Name = $"Mid{i}",
                Texture = tex,
                Centered = false,
                Scale = new Vector2(scale, scale),
                Position = new Vector2(i * _midTileW, 0f),
                ZIndex = -90,
                ZAsRelative = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            };
            AddChild(spr);
            tiles[i] = spr;
        }
        _midTiles = tiles;
        HasMid = true;
    }

    // ボス戦突入：道中背景を隠し、専用背景へ切替（軽く動かす）。BossBgPath が無ければ何もしない（道中のまま）。
    public void EnterBoss()
    {
        // 層モード：暗転（L4 のα→0、L1〜L3 を 0.55 倍）だけを行い、背景の差し替えはしない。
        if (_layers != null) { _mode = Mode.Boss; _layers.EnterBoss(); return; }
        if (_mode == Mode.Boss) return;
        bool hasTex = BossBgTexture != null || (!string.IsNullOrEmpty(BossBgPath) && ResourceLoader.Exists(BossBgPath));
        if (!hasTex) return; // ボス背景未指定（例：中ボスのみ）＝切替しない
        if (!_bossReady) SetupBoss();
        if (_bossTiles.Length == 0) return; // 読込失敗時は切替しない（道中背景を残す）

        _mode = Mode.Boss;
        foreach (var s in _midTiles) s.Visible = false;
        foreach (var s in _bossTiles) s.Visible = true;
    }

    // ボス背景タイルを準備（横ドリフトでループ。ドリフト用に画面幅+余白ぶんタイル）。
    private void SetupBoss()
    {
        _bossReady = true;
        _bossTex = BossBgTexture ?? ResourceLoader.Load<Texture2D>(BossBgPath);
        if (_bossTex == null) return;

        float scale = ScreenHeight / _bossTex.GetHeight();
        _bossTileW = _bossTex.GetWidth() * scale;
        int count = Mathf.Max(2, Mathf.CeilToInt(ScreenWidth / _bossTileW) + 2);

        var tiles = new Sprite2D[count];
        for (int i = 0; i < count; i++)
        {
            var spr = new Sprite2D
            {
                Name = $"Boss{i}",
                Texture = _bossTex,
                Centered = false,
                Scale = new Vector2(scale, scale),
                Position = new Vector2(i * _bossTileW, 0f),
                ZIndex = -90,
                ZAsRelative = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                Visible = false,
            };
            AddChild(spr);
            tiles[i] = spr;
        }
        _bossTiles = tiles;
    }

    public override void _Process(double delta)
    {
        // 層モードはスクロールも暗転も BgLayers 側が回す（ここは何もしない）。
        if (_layers != null) return;

        _t += delta;
        float dt = (float)delta;

        if (_mode == Mode.Mid)
        {
            // 道中：左へシームレスループ。span を法に取り、各タイルを並べ直す（蓄積誤差なし）。
            if (_midTiles.Length > 0 && _midTileW > 0f)
            {
                // 位置係数 scrollMul：自機が右にいるほど背景が速く流れて前進感UP、左でゆっくり。
                //   scrollMul = 0.65 + 0.80*nx（左端0.65 / 中央1.05 / 右端1.45）。
                _midX += MidScrollSpeed * ScrollMul() * dt;
                float off = _midX % _midTileW;     // 0.._midTileW
                for (int i = 0; i < _midTiles.Length; i++)
                {
                    var p = _midTiles[i].Position;
                    p.X = i * _midTileW - off;
                    if (p.X <= -_midTileW) p.X += _midTiles.Length * _midTileW; // 左へ出た列を右端へ
                    _midTiles[i].Position = p;
                }
            }
        }
        else // Mode.Boss
        {
            if (_bossTiles.Length > 0 && _bossTileW > 0f)
            {
                // ① ゆっくり横ドリフト（左へ）＝専用背景が「微かに動いている」
                _bossX += BossDriftSpeed * dt;
                float off = _bossX % _bossTileW;
                for (int i = 0; i < _bossTiles.Length; i++)
                {
                    var p = _bossTiles[i].Position;
                    p.X = i * _bossTileW - off;
                    if (p.X <= -_bossTileW) p.X += _bossTiles.Length * _bossTileW;
                    _bossTiles[i].Position = p;
                }
            }

            // クロスフェード進行：新層をα0→1、旧層をα1→0。位置は現行層と同じ速度でドリフト。
            if (_fading)
            {
                _fadeT += dt;
                float k = Mathf.Clamp(_fadeT / _fadeDur, 0f, 1f);
                float s = k * k * (3f - 2f * k); // smoothstep＝立ち上がり/収束が柔らかい
                if (_fadeTiles.Length > 0 && _fadeTileW > 0f)
                {
                    _fadeX += BossDriftSpeed * dt;
                    float foff = _fadeX % _fadeTileW;
                    for (int i = 0; i < _fadeTiles.Length; i++)
                    {
                        var p = _fadeTiles[i].Position;
                        p.X = i * _fadeTileW - foff;
                        if (p.X <= -_fadeTileW) p.X += _fadeTiles.Length * _fadeTileW;
                        _fadeTiles[i].Position = p;
                        _fadeTiles[i].Modulate = new Color(_newDim.R, _newDim.G, _newDim.B, _newDim.A * s);
                    }
                }
                foreach (var b in _bossTiles) b.Modulate = new Color(_curDim.R, _curDim.G, _curDim.B, _curDim.A * (1f - s));
                if (k >= 1f) FinishFade();
            }

            // ② 呼吸スケール（ごく弱く、中心基準で拡大のみ）＋ ③ 光の明滅（modulate）。
            //    Node2D 全体に掛ける。背景は画面を覆う前提なので画面中心を基点にスケール。
            //    1.0 を下限に「拡大方向だけ」で揺らす＝縮小して画面端に隙間が出るのを防ぐ。
            float breath = 1f + BossBreathAmp * (0.5f + 0.5f * Mathf.Sin((float)(_t * Mathf.Tau * BossBreathHz)));
            var center = new Vector2(ScreenWidth * 0.5f, ScreenHeight * 0.5f);
            Scale = new Vector2(breath, breath);
            Position = center - center * breath; // 中心を固定したままスケール
            float pulse = 1f + BossPulseAmp * Mathf.Sin((float)(_t * Mathf.Tau * BossPulseHz));
            Modulate = new Color(pulse, pulse, pulse, 1f);
        }
    }

    // 位置係数：自機の正規化X(nx)から scrollMul = 0.65 + 0.80*nx を返す（左端0.65/中央1.05/右端1.45）。
    private float ScrollMul() => 0.65f + 0.80f * BgScroll.PlayerNx(this);
}

// BgScroll : 背景スクロールの位置係数まわりの共有ヘルパ（StageBackground / ScrollFx / WorldGrade から使う）。
//   nx（自機の正規化X, 0=左端/1=右端）の取得を1箇所に集約。engineer が公開した GameManager.PlayerNormX
//   （TickProgress で毎フレーム更新）を第一参照にし、GameManager が無い場面では Player を group から引いて
//   x/384 で算出するフォールバックを残す。参照名が変わってもここだけ直せばよい。
public static class BgScroll
{
    private const float ScreenW = 384f;

    public static float PlayerNx(Node self)
    {
        var g = self.GetNodeOrNull<GameManager>("/root/Game");
        if (g != null) return g.PlayerNormX;
        var p = self.GetTree().GetFirstNodeInGroup("player") as Node2D;
        if (p != null) return Mathf.Clamp(p.GlobalPosition.X / ScreenW, 0f, 1f);
        return 0.5f;
    }
}
