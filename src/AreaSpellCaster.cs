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
    // このキャスターを抱えるボス（＝攻撃の発生源）。浄化されたら予約中の宣告・出現済みの予兆を全てキャンセルする。
    // ＝「ボスが改心したのに、撃ち終わった範囲技だけが後から着弾する」残留攻撃を断つ。AddChild 先の親＝ボス。
    private Enemy? _owner;

    private double _castT, _fireT;
    private bool _pending;
    private AreaStrike.Shape? _pendShape;
    private double _fireDelay = 0.7; // 技名宣告 → 予兆出現までの溜め

    // ── 全画面AOE（ラスボスMina専用・通常ランダム枠とは独立した専用経路）──
    //   CastFullscreen で予約 → _aoeFireT 後に Fullscreen の AreaStrike を1枚出す。
    //   AoeActive は宣告〜予兆着弾まで true＝ボス側が通常弾(FirePattern)をこの間だけ止める(gate)のに使う。
    private bool _aoePending;            // 宣告済み・予兆出現待ち
    private double _aoeFireT;            // 予兆出現までの残り
    private bool _aoeWithSafe;           // 安置あり(true)／全面(false)
    private const float AoeWarn = 1.6f;  // 全画面AOEの予兆尺（WarnMul で難易度補正）
    private const float AoeFireDelay = 0.7f; // 宣告→予兆出現の溜め
    private const float AoeSafeR = 30f;  // 安置(セーフゾーン)半径（28-32px帯）
    private AreaStrike? _aoeStrike;      // 出現中の全画面予兆（生存＝AOE進行中の判定に使う）
    // 宣告〜着弾までの間 true（ボスが通常弾を止めるゲート）。
    public bool AoeActive => _aoePending || (_aoeStrike != null && IsInstanceValid(_aoeStrike));

    // ボス側（BossMina.OnHpChanged）から全画面AOEを1回予約する。withSafeZone=false で安置なしの全面型。
    public void CastFullscreen(bool withSafeZone)
    {
        if (AoeActive) return; // 多重予約しない（HP閾値の同フレーム多重発火対策）
        _aoeWithSafe = withSafeZone;
        _aoePending = true;
        _aoeFireT = AoeFireDelay;
        string name = withSafeZone ? "全画面浄化・安置" : "全画面浄化・絶域";
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.AnnounceSpell(_disp, _handle, name, _tint);
    }

    private void TickFullscreen(double delta)
    {
        if (!_aoePending) return;
        _aoeFireT -= delta;
        if (_aoeFireT > 0) return;
        _aoePending = false;

        // 安置位置：自機の現在地を避けて配置（即死事故防止）。画面端に寄せすぎない。
        Vector2 safe = Vector2.Zero;
        float r = _aoeWithSafe ? AoeSafeR : 0f;
        if (_aoeWithSafe)
        {
            Vector2 player = (GetTree().GetFirstNodeInGroup("player") as Node2D)?.GlobalPosition
                             ?? new Vector2(W / 2f, H / 2f);
            float margin = r + 14f; // 端から離す
            // 自機から十分離れた点を最大12回試行（避けられる安置にする）。
            safe = new Vector2(W / 2f, H / 2f);
            for (int i = 0; i < 12; i++)
            {
                var cand = new Vector2(_rng.RandfRange(margin, W - margin), _rng.RandfRange(margin, H - margin));
                if (cand.DistanceTo(player) >= 70f) { safe = cand; break; }
            }
        }

        var z = new AreaStrike();
        z.ConfigureFullscreen(safe, r, AoeWarn * WarnMul(), _tint, _hot);
        if (_owner != null) z.SetOwner(_owner); // 着弾前にボス浄化されたら予兆ごと消える
        _world.AddChild(z);
        z.GlobalPosition = Vector2.Zero; // 画面座標基準（Fullscreen は内部で安置座標を画面系で扱う）
        _aoeStrike = z;
    }

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
        // 発生源（ボス）を一度キャッシュ：AddChild 先の親がボス（Configure とは独立にここで拾う）。
        _owner ??= GetParent() as Enemy;
        // ボスが浄化（改心）されたら、以降は宣告も予兆出現もしない＝攻撃が終わった後に技が残らない。
        // 予約済み（宣告→出現待ち）の発火も破棄する。出現済みの予兆は AreaStrike 側が owner 浄化で自滅する。
        if (_owner != null && _owner.IsPurified)
        {
            _pending = false;
            _aoePending = false; // 予約中の全画面AOEも破棄（出現済みは AreaStrike が owner 浄化で自滅）
            return;
        }

        if (Hud.BubblePaused) return; // 会話中は出さない

        // 全画面AOEの予約を進める（専用経路）。AOE進行中は通常ランダム枠は止める（弾幕の過密回避）。
        TickFullscreen(delta);
        if (AoeActive) return;

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
                // 発生源を結びつけ、着弾前にボスが浄化されたら予兆ごと消えるようにする（残留着弾を断つ）。
                if (_owner != null) z.SetOwner(_owner);
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
