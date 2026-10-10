using Godot;
using System;
using System.Collections.Generic;

public partial class BossAnimalTechnique : Node2D
{
    private Enemy _boss = null!;
    private AreaSpellCaster _caster = null!;
    private BulletPool _pool = null!;
    private string _id = "";
    private Color _color;
    private Vector2 _origin, _target, _aim, _side;
    private double _time, _warning;
    private int _wave, _count;
    public bool Finished { get; private set; }
    public string SpellName { get; private set; } = "";
    private const int Waves = 3;
    private const double WaveInterval = 0.32;

    public static BossAnimalTechnique Begin(Enemy boss, string id, AreaSpellCaster caster)
    {
        var technique = new BossAnimalTechnique { _boss = boss, _id = id, _caster = caster };
        boss.GetParent().AddChild(technique);
        return technique;
    }

    public override void _Ready()
    {
        AddToGroup("boss_animal_techniques");
        ZIndex = -9;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        _pool = GetNode<BulletPool>("/root/Pool");
        var game = GetNode<GameManager>("/root/Game");
        _warning = game.Difficulty == GameManager.Diff.Easy ? 1.6 : 1.25;
        _count = game.ScaleBulletsOdd(_id == "rei" ? 11 : 5);
        _origin = _boss.ShotCenter;
        _target = GetTree().GetFirstNodeInGroup("player") is Node2D player ? player.GlobalPosition : Field.PlayerStart;
        _aim = _origin.DirectionTo(_target);
        if (_aim.IsZeroApprox()) _aim = Vector2.Left;
        _side = new Vector2(-_aim.Y, _aim.X);
        var (name, speaker, handle, tint) = _id switch
        {
            "akari" => ("狐火・三尾の便り", "あかり", BossHandles.AkariSpell, "a6d8ed"),
            "koharu" => ("枝角・結び目の檻", "こはる", BossHandles.KoharuMain, "b6efca"),
            _ => ("星翼・追い越す流星", "レイ", BossHandles.ReiMain, "e0b6ff"),
        };
        SpellName = name;
        _color = new Color(tint);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.AnnounceSpell(speaker, handle, name, _color);
        _boss.SetAnimalWindup(true);
    }

    private IEnumerable<(Vector2 Origin, Vector2 Direction)> Volley(int wave)
    {
        if (_id == "koharu")
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 origin = _origin + _side * side * (22 + wave * 4);
                Vector2 aim = origin.DirectionTo(_target);
                for (int i = -_count / 2; i <= _count / 2; i++)
                    yield return (origin, aim.Rotated(Mathf.DegToRad(i * 10f)));
            }
        }
        else if (_id == "rei" && wave != 1)
        {
            for (int i = 0; i < _count; i++)
                yield return (_origin, _aim.Rotated(Mathf.Tau * i / _count + (wave == 2 ? Mathf.Pi / _count : 0)));
        }
        else
        {
            int count = _id == "rei" ? 3 : _count;
            Vector2 origin = _origin + (_id == "akari" ? _side * (wave - 1) * 18f : Vector2.Zero);
            float sweep = _id == "akari" ? (wave - 1) * 24f : 0;
            for (int i = -count / 2; i <= count / 2; i++)
                yield return (origin, _aim.Rotated(Mathf.DegToRad(sweep + i * 7f)));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Finished) return;
        if (!IsInstanceValid(_boss) || _boss.IsQueuedForDeletion() || _boss.IsPurified || !_boss.IsVisibleInTree()
            || _caster.Suppressed || _caster.AoeActive)
        {
            Cancel();
            return;
        }
        if (Hud.BubblePaused || _boss.GaugeVulnerable || _boss.GaugeReforming) { QueueRedraw(); return; }
        _time += GameManager.EnemyDelta(delta);
        if (_wave < Waves && _time >= _warning + _wave * WaveInterval)
        {
            _boss.ShowAnimalAttack();
            foreach (var shot in Volley(_wave))
            {
                var bullet = _pool.SpawnBossShot(_boss.ShotCenter, shot.Direction, _boss.BodyShotJob!.Value, true);
                bullet.MakeLeadIn(shot.Origin);
            }
            _wave++;
        }
        if (_wave == Waves) { Cancel(); return; }
        QueueRedraw();
    }

    public void Cancel()
    {
        if (Finished) return;
        Finished = true;
        if (IsInstanceValid(_boss)) _boss.SetAnimalWindup(false);
        Hide();
        QueueFree();
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_boss)) _boss.SetAnimalWindup(false);
    }

    public override void _Draw()
    {
        if (Finished || Hud.BubblePaused || _wave >= Waves) return;
        var origins = new HashSet<Vector2>();
        for (int wave = _wave; wave < Waves; wave++)
            foreach (var shot in Volley(wave))
            {
                origins.Add(shot.Origin);
            }
        foreach (var origin in origins)
            DrawArc(origin, 6f + (float)(_time % 0.6) * 4f, 0, Mathf.Tau, 20, new Color(_color, 0.5f), 1f, true);
    }
}
