using Godot;
using System.Collections.Generic;

public partial class BossEdgeVolley : Node2D
{
    public enum Edge { Top, Bottom, Left, Right }
    private const float Spacing = 22f;
    private const double RowInterval = 0.28;
    private const int Rows = 3;
    private Enemy _boss = null!;
    private BulletPool _pool = null!;
    private Texture2D _art = null!;
    private Color _accent;
    private float _speed;
    private double _time, _warning;
    private int _rows;
    private Vector2[] _gates = null!;
    private Vector2 _direction;
    public Edge Side { get; private set; }
    public string SpellName { get; private set; } = "";
    public bool Finished { get; private set; }

    public static Edge DirectionFor(string key, int sequence) => key switch
    {
        "akari" => sequence % 2 == 0 ? Edge.Top : Edge.Bottom,
        "koharu" => sequence % 2 == 0 ? Edge.Right : Edge.Left,
        "rei" => (sequence % 3) switch { 0 => Edge.Left, 1 => Edge.Right, _ => Edge.Top },
        _ => (sequence % 4) switch { 0 => Edge.Top, 1 => Edge.Right, 2 => Edge.Bottom, _ => Edge.Left },
    };

    public static BossEdgeVolley Begin(Enemy boss, string key, int sequence)
    {
        var (name, art, accent, speed) = key switch
        {
            "akari" => ("降り積もる未送信", BulletArt.AkariEnvelope!, new Color("a6d8ed"), 82f),
            "koharu" => ("拍手の往復", BulletArt.KoharuTicket!, new Color("b6efca"), 88f),
            "rei" => ("流れる声の向こう", BulletArt.Get("rei_comment")!, new Color("e0b6ff"), 94f),
            _ => ("四方のリフレイン", BulletArt.Get("mina_butterfly")!, new Color("a4e7ff"), 98f),
        };
        var volley = new BossEdgeVolley
        {
            _boss = boss, Side = DirectionFor(key, sequence), SpellName = name,
            _art = art, _accent = accent, _speed = speed,
        };
        boss.GetParent().AddChild(volley);
        return volley;
    }

    public override void _Ready()
    {
        ZIndex = -10;
        ZAsRelative = false;
        TextureFilter = TextureFilterEnum.Linear;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        AddToGroup("boss_edge_volleys");
        _pool = GetNode<BulletPool>("/root/Pool");
        var difficulty = GetNode<GameManager>("/root/Game").Difficulty;
        int count = difficulty switch
        {
            GameManager.Diff.Easy => 3, GameManager.Diff.Hard => 5,
            GameManager.Diff.Lunatic => 6, _ => 4,
        };
        _warning = difficulty switch
        {
            GameManager.Diff.Easy => 1.65, GameManager.Diff.Hard => 1.25,
            GameManager.Diff.Lunatic => 1.15, _ => 1.45,
        };
        _direction = Side switch
        {
            Edge.Top => Vector2.Down, Edge.Bottom => Vector2.Up,
            Edge.Left => Vector2.Right, _ => Vector2.Left,
        };
        bool vertical = Side is Edge.Top or Edge.Bottom;
        var player = (Player)GetTree().GetFirstNodeInGroup("player");
        float low = vertical ? Field.Left + 20 : Field.Top + 40;
        float high = vertical ? Field.Right - 20 : Field.Bottom - 22;
        // 中心の1列は「予兆が出た瞬間の自機の軸」にぴったり乗せる。ここは盤面の縁までしか詰めない
        //   （自機の可動域＝盤面そのもの）＝どこに居ても必ず1列が自機を通る。
        //   2026-10-03 ユーザー指摘「上下左右から予兆してくる弾も当たるように配置して」。旧実装は
        //   列の並びごと Mathf.Clamp(low, high-…) で内側へスライドさせていたため、自機が場の端へ
        //   寄っているとどの列も自機の軸から外れ＝端で棒立ちしているのが最も安全だった。
        float axisTarget = Mathf.Clamp(vertical ? player.GlobalPosition.X : player.GlobalPosition.Y,
            vertical ? Field.Left : Field.Top, vertical ? Field.Right : Field.Bottom);
        // 残りの列は中心から外へ Spacing 刻みで交互に置き、門の絵が場からはみ出す側は飛ばして
        //   反対側へ回す（門の数も間隔も不変＝避け場の広さは変えない。詰める方向だけが変わる）。
        var axes = new List<float>(count) { axisTarget };
        for (int step = 1; axes.Count < count && step * Spacing <= high - low + Spacing; step++)
            foreach (int sign in new[] { -1, 1 })
            {
                float axis = axisTarget + sign * step * Spacing;
                if (axes.Count < count && axis >= low && axis <= high) axes.Add(axis);
            }
        axes.Sort();
        _gates = new Vector2[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            float axis = axes[i];
            _gates[i] = Side switch
            {
                Edge.Top => new Vector2(axis, Field.Top + 5),
                Edge.Bottom => new Vector2(axis, Field.Bottom - 5),
                Edge.Left => new Vector2(Field.Left + 5, axis),
                _ => new Vector2(Field.Right - 5, axis),
            };
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Finished) return;
        if (!IsInstanceValid(_boss) || _boss.IsQueuedForDeletion() || _boss.IsPurified || !_boss.Visible)
        {
            Cancel();
            return;
        }
        if (Hud.BubblePaused || _boss.GaugeVulnerable || _boss.GaugeReforming) { QueueRedraw(); return; }
        _time += GameManager.EnemyDelta(delta);
        if (_rows < Rows && _time >= _warning + _rows * RowInterval)
        {
            foreach (var gate in _gates)
            {
                var bullet = _pool.Spawn(gate, _direction * _speed, true, 3.4f, 1, BulletShape.Diamond, _accent);
                bullet.UseBossProjectile();
            }
            _rows++;
        }
        if (_rows == Rows) { Cancel(); return; }
        QueueRedraw();
    }

    public void Cancel()
    {
        if (Finished) return;
        Finished = true;
        Hide();
        QueueFree();
    }


    public override void _Draw()
    {
        if (Finished || Hud.BubblePaused) return;
        float charge = Mathf.Clamp((float)(_time / _warning), 0, 1);
        float fade = 1f - Mathf.Clamp((float)(_time - _warning - (Rows - 1) * RowInterval) / 0.4f, 0, 1);
        if (fade <= 0) return;
        float open = Mathf.SmoothStep(0, 0.35f, charge);
        var danger = new Color(AreaStrike.DangerEdge, (0.6f + charge * 0.4f) * fade);
        float angle = _direction.Angle();
        foreach (var gate in _gates)
        {
            DrawSetTransform(gate, angle);
            DrawRect(new Rect2(0, -7, 40, 14), new Color(AreaStrike.DangerEdge, 0.08f * open * fade));
            DrawLine(new Vector2(0, -8), new Vector2(0, 8), new Color(_accent, fade), 2.2f, true);
            DrawLine(new Vector2(0, -8), new Vector2(8, -8), danger, 0.8f, true);
            DrawLine(new Vector2(0, 8), new Vector2(8, 8), danger, 0.8f, true);
            DrawLine(new Vector2(2, -6), new Vector2(2, -6 + 12 * charge), new Color(Colors.White, fade), 1.1f);
            for (int i = 0; i < 3; i++)
            {
                float x = 15 + i * 8 + (float)(_time % 0.45 / 0.45) * 3;
                DrawPolyline(new[] { new Vector2(x - 3, -3), new Vector2(x, 0), new Vector2(x - 3, 3) },
                    new Color(danger, danger.A * (1f - i * 0.2f)), 1f, true);
            }
            DrawSetTransform(Vector2.Zero);
            var size = _art.GetSize() * (9f / Mathf.Max(_art.GetWidth(), _art.GetHeight()));
            DrawTextureRect(_art, new Rect2(gate + _direction * 7 - size * 0.5f, size), false,
                new Color(Colors.White, open * fade));
        }
    }
}
