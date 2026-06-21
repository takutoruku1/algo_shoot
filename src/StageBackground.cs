using Godot;

// StageBackground : ステージ背景のデータ駆動コントローラ（旧 static 額背景 Sprite2D の置き換え）。
//
//   ・道中(mid)背景 ＝ 1枚絵を画面高(216)に合わせて横にタイルし、左へシームレスにループ。
//     自機は画面内にほぼ留まるので、背景が左へ流れる＝「左→右に前進している」移動感を作る。
//     （ScrollFx の near/far パララックス層・StageImagery・MurkVignette はこの上にそのまま重なる）
//   ・ボス(boss)背景 ＝ ボス戦突入(EnterBoss)で道中背景を隠し、専用背景へ差し替え。
//     軽い動きだけ付ける：ゆっくりした横ドリフト＋ごく弱い呼吸スケール＋光の明滅(modulate)。やり過ぎない。
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

    // ───── tunable（控えめに） ─────
    public float MidScrollSpeed = 26f;     // px/s。道中背景が左へ流れる速さ＝前進感。控えめ（弾の視認を妨げない）
    public float BossDriftSpeed = 8f;      // px/s。ボス背景の横ドリフト（ゆっくり）
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

    public override void _Ready()
    {
        ZIndex = -90;
        ZAsRelative = false;
        AddToGroup("stagebg");
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
        _t += delta;
        float dt = (float)delta;

        if (_mode == Mode.Mid)
        {
            // 道中：左へシームレスループ。span を法に取り、各タイルを並べ直す（蓄積誤差なし）。
            if (_midTiles.Length > 0 && _midTileW > 0f)
            {
                _midX += MidScrollSpeed * dt;
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
}
