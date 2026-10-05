using Godot;

public sealed class BossDanmaku
{
    private readonly Enemy _boss;
    private readonly string _pattern;
    private double _timer;
    private int _wave;

    public BossDanmaku(Enemy boss)
    {
        _boss = boss;
        _pattern = boss switch
        {
            BossAkari => "akari", BossKoharu => "koharu", BossRei => "rei", BossHikage => "hikage",
            CameoBoss cameo => cameo.Theme.Fire switch
            {
                CameoFireTheme.AkariGrief => "akari", CameoFireTheme.KoharuFalling => "koharu", _ => "rei",
            },
            _ => "mina",
        };
    }

    public void Tick(double delta)
    {
        if (!_boss.IsVisibleInTree() || _boss.Transforming || !Field.Rect.HasPoint(_boss.ShotCenter)) return;
        if (_boss is BossMina { Transitioning: true }) return;
        _timer -= delta;
        if (_timer > 0) return;
        var game = GameManager.Instance!;
        bool charged = _boss.HasEvolved;
        bool reduced = _boss.GaugeVulnerable || _boss.GaugeReforming
            || _boss.GetTree().GetNodesInGroup("aoe").Count > 0;
        _timer = (charged ? 0.65 : 0.42) * game.DanmakuIntervalMul * (reduced ? 1.7 : 1) * (_boss is CameoBoss ? 1.3 : 1);
        var pool = _boss.GetNode<BulletPool>("/root/Pool");
        int arms = _pattern switch { "akari" => 3, "koharu" => 4, "rei" => 5, "hikage" => 6, _ => 8 };
        float rotation = _wave * (_pattern == "koharu" ? 0.17f : _pattern == "rei" ? -0.23f : 0.26f);
        int layers = reduced || _boss is CameoBoss || game.Difficulty == GameManager.Diff.Easy ? 1 : charged ? 3 : 2;
        for (int i = 0; i < arms; i++)
            for (int layer = 0; layer < layers; layer++)
            {
                float angle = Mathf.Tau * i / arms + rotation;
                if (_pattern == "mina") angle += Mathf.Sin(_wave * 0.4f) * (i % 2 == 0 ? 0.3f : -0.3f);
                float speed = (_pattern switch { "akari" => 62, "koharu" => 67, "rei" => 72, _ => 78 }) + layer * 18;
                Vector2 direction = Vector2.Right.Rotated(angle);
                if (_pattern == "koharu")
                    direction = new Vector2(layer == 0 ? -0.45f : 0.45f, -1).Rotated(Mathf.Tau * i / arms + rotation);
                else if (_pattern == "rei" && layer == 1)
                {
                    direction = direction.Rotated(Mathf.Pi / arms);
                    speed *= 0.55f;
                }
                else if (_pattern == "mina" && layer == 1)
                    direction = Vector2.Right.Rotated(Mathf.Tau * i / arms - rotation);
                Color tint = _pattern switch
                {
                    "akari" => new("6c9cd8"), "koharu" => new("e8945a"),
                    "rei" => new("967bd8"), _ => new("769cdb"),
                };
                var bullet = pool.Spawn(_boss.ShotCenter, direction * speed, true, 2.6f, 1, BulletShape.Dart, tint);
                bullet.UseBossProjectile();
                if (charged)
                {
                    Vector2 position = _boss.ShotCenter + direction.Normalized() * (18 + layer * 10);
                    bullet.MakeLeadIn(position);
                    bullet.MakeAccel(6f, speed * 1.3f * game.BulletSpeedMul, 0.85f + layer * 0.12f);
                }
            }
        _wave++;
    }
}
