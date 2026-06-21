using Godot;

// AreaSpellCaster : ボスに付ける「予測攻撃（テレグラフ）」のキャスター（RefrainArenaHTML 移植）。
//   一定間隔で技名を宣告（Xツイート風＝Hud.AnnounceSpell）→ 少し遅れて予測線/予測エリアを N 個出す。
//   キャラ別に形状・色・予兆時間・技セットが変わり、出る数は難易度でスケール（弱2/中4/強7/ルナ10）。
//   会話中(BubblePaused)は出さない。ボスの子に AddChild すれば、ボス撃破で一緒に消える。
//
// 使い方（各ボスの _Ready）：
//   var caster = new AreaSpellCaster();
//   caster.Configure("rei", GetParent());   // GetParent()=World（テレグラフの追加先）
//   AddChild(caster);
public partial class AreaSpellCaster : Node2D
{
    private const float W = 384f, H = 216f;

    private string _disp = "", _handle = "";
    private Color _tint = new(1f, 0.34f, 0.30f), _hot = new(1f, 0.92f, 0.7f);
    private double _warnMin = 1.0, _warnMax = 1.4, _interval = 6.0;
    private AreaStrike.Shape[] _shapes = { AreaStrike.Shape.Circle };
    private (string name, AreaStrike.Shape? shape)[] _spells = { ("range", null) };

    private readonly RandomNumberGenerator _rng = new();
    private Node _world = null!;

    private double _castT, _fireT;
    private bool _pending;
    private AreaStrike.Shape? _pendShape;
    private double _fireDelay = 0.7; // 技名宣告 → 予兆出現までの溜め

    public void Configure(string key, Node world)
    {
        _world = world;
        _rng.Randomize();
        var H_ = AreaStrike.Shape.BeamH; var V = AreaStrike.Shape.BeamV;
        var C = AreaStrike.Shape.Circle; var R = AreaStrike.Shape.Rect;
        switch (key)
        {
            case "rei": // 順位掲示板・整然と裁く（予兆長め・金/菫）
                _disp = "レイ"; _handle = "@rei_compe";
                _tint = new Color("e8c45a"); _hot = new Color("ffe39a");
                _warnMin = 1.1; _warnMax = 1.6; _interval = 11.0;
                _shapes = new[] { H_, R };
                _spells = new (string, AreaStrike.Shape?)[] { ("ランキングレーザー", H_), ("表彰台圏", R), ("序列の楔", H_) };
                break;
            case "akari": // 雨の教室・降る前に予報（蒼）
                _disp = "あかり"; _handle = "@akari_rain";
                _tint = new Color("6c9cd8"); _hot = new Color("a9dcff");
                _warnMin = 1.0; _warnMax = 1.4; _interval = 9.0;
                _shapes = new[] { V, C };
                _spells = new (string, AreaStrike.Shape?)[] { ("豪雨予報", V), ("沈黙の波紋", C) };
                break;
            case "koharu": // 台所・熱してから一気に（予兆短め・琥珀/深紅）
                _disp = "こはる"; _handle = "@koharu_kitchen";
                _tint = new Color("e8945a"); _hot = new Color("ffc06a");
                _warnMin = 0.7; _warnMax = 1.0; _interval = 7.0;
                _shapes = new[] { C, R, H_ };
                _spells = new (string, AreaStrike.Shape?)[] { ("熱したフライパン", C), ("沸騰鍋", R) };
                break;
            default: // mina（暴走）：全テレグラフ同時・濁った全色
                _disp = "ミナ"; _handle = "@mina_ai_";
                _tint = new Color("e072ac"); _hot = new Color("ff8cc4");
                _warnMin = 0.8; _warnMax = 1.2; _interval = 6.0;
                _shapes = new[] { H_, V, C, R };
                _spells = new (string, AreaStrike.Shape?)[] { ("全テレグラフ同時", null), ("濁渦と雨", null) };
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (Hud.BubblePaused) return; // 会話中は出さない

        if (_pending)
        {
            _fireT -= delta;
            if (_fireT <= 0) { SpawnTelegraphs(); _pending = false; }
            return;
        }
        _castT += delta;
        if (_castT >= _interval) { _castT = 0; Cast(); }
    }

    // 技名を宣告（Xツイート風スペルカード）→ 溜めて予兆を出す。
    private void Cast()
    {
        var sp = _spells[_rng.RandiRange(0, _spells.Length - 1)];
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.AnnounceSpell(_disp, _handle, sp.name, _tint);
        _pendShape = sp.shape;
        _pending = true; _fireT = _fireDelay;
    }

    // 難易度で同時に出る数が増える（弾幕に上乗せするため控えめ：弱1/中2/強3/ルナ5）。
    private int DiffCount() => (GetNodeOrNull<GameManager>("/root/Game")?.Difficulty ?? GameManager.Diff.Normal) switch
    {
        GameManager.Diff.Easy => 1,
        GameManager.Diff.Hard => 3,
        GameManager.Diff.Lunatic => 5,
        _ => 2,
    };
    // 予兆時間の難易度補正（易しいほど長く＝避ける猶予が増える）。
    private float WarnMul() => (GetNodeOrNull<GameManager>("/root/Game")?.Difficulty ?? GameManager.Diff.Normal) switch
    {
        GameManager.Diff.Easy => 1.3f,
        GameManager.Diff.Hard => 0.85f,
        GameManager.Diff.Lunatic => 0.72f,
        _ => 1f,
    };

    // この一斉射ぶんの配置（重なり回避用）。
    private readonly System.Collections.Generic.List<(AreaStrike.Shape shape, Vector2 c, float hw, float hh)> _placed = new();

    private void SpawnTelegraphs()
    {
        _placed.Clear();
        int n = DiffCount();
        float wm = WarnMul();
        for (int i = 0; i < n; i++)
        {
            // 技の形状が決まっていなければ（ミナ）プロファイルの全形状から拾う＝“全テレグラフ同時”。
            var shape = _pendShape ?? _shapes[_rng.RandiRange(0, _shapes.Length - 1)];
            double warn = _rng.RandfRange((float)_warnMin, (float)_warnMax) * wm;

            // 重ならない配置を最大16回トライ。置けなければスキップ＝過密を自然に防ぐ。
            bool placed = false;
            for (int tryI = 0; tryI < 16 && !placed; tryI++)
            {
                var g = RandGeo(shape);
                if (OverlapsAny(shape, g.c, g.hw, g.hh)) continue;
                var z = new AreaStrike();
                z.Configure(shape, g.hw, g.hh, warn, _tint, _hot);
                _world.AddChild(z);
                z.GlobalPosition = g.c;
                _placed.Add((shape, g.c, g.hw, g.hh));
                placed = true;
            }
        }
    }

    // 形状ごとの候補配置（自機の起点＝左側はやや空け、中央〜右に寄せる＝フェアな逃げ場）。
    private (Vector2 c, float hw, float hh) RandGeo(AreaStrike.Shape shape)
    {
        switch (shape)
        {
            // サイズは 384×216 の実画面に合わせて小さめ（自機が避けられる大きさ）。
            case AreaStrike.Shape.BeamH: // 横ビーム（全幅・細め）
                return (new Vector2(W / 2f, _rng.RandfRange(22f, H - 22f)), W / 2f, _rng.RandfRange(5f, 8f));
            case AreaStrike.Shape.BeamV: // 縦カラム（全高・細め）
                return (new Vector2(_rng.RandfRange(W * 0.30f, W * 0.92f), H / 2f), _rng.RandfRange(5f, 8f), H / 2f);
            case AreaStrike.Shape.Circle:
            {
                float rad = _rng.RandfRange(15f, 24f);
                return (new Vector2(_rng.RandfRange(W * 0.28f, W * 0.9f), _rng.RandfRange(rad, H - rad)), rad, rad);
            }
            default: // Rect
            {
                float w = _rng.RandfRange(32f, 56f), h = _rng.RandfRange(28f, 50f);
                return (new Vector2(_rng.RandfRange(W * 0.30f, W * 0.88f), _rng.RandfRange(h / 2f, H - h / 2f)), w / 2f, h / 2f);
            }
        }
    }

    // 重なり判定：同方向の予測線（横×横／縦×縦）と、面どうし（円/矩形）は重ねない。
    // 横ビーム×縦カラムの交差や、線×面は“別物として読める”ので許可する（過剰に弾かない）。
    private bool OverlapsAny(AreaStrike.Shape s, Vector2 c, float hw, float hh)
    {
        const float gap = 8f;
        bool sH = s == AreaStrike.Shape.BeamH, sV = s == AreaStrike.Shape.BeamV;
        bool sArea = s == AreaStrike.Shape.Circle || s == AreaStrike.Shape.Rect;
        foreach (var p in _placed)
        {
            bool pH = p.shape == AreaStrike.Shape.BeamH, pV = p.shape == AreaStrike.Shape.BeamV;
            bool pArea = p.shape == AreaStrike.Shape.Circle || p.shape == AreaStrike.Shape.Rect;
            if (sH && pH) { if (Mathf.Abs(c.Y - p.c.Y) < hh + p.hh + gap) return true; }
            else if (sV && pV) { if (Mathf.Abs(c.X - p.c.X) < hw + p.hw + gap) return true; }
            else if (sArea && pArea)
            {
                if (Mathf.Abs(c.X - p.c.X) < hw + p.hw + gap && Mathf.Abs(c.Y - p.c.Y) < hh + p.hh + gap) return true;
            }
        }
        return false;
    }
}
