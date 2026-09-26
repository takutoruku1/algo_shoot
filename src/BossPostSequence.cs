using Godot;
using System;

public partial class BossPostSequence : Node
{
    public int Count { get; private set; }
    public bool Active => _post != null || _draft;
    public float Floor => Count < _thresholds.Length ? _thresholds[Count] : 0;
    public bool Pending => Count < _thresholds.Length
        && _boss.HpRatio <= Mathf.RoundToInt(Floor * _boss.TotalBars * Enemy.BarHp)
            / (float)(_boss.TotalBars * Enemy.BarHp) + 0.00001f;
    private Enemy _boss = null!;
    private Node _caster = null!;
    private BossPostStory _story = null!;
    private BossRealmFx _realm = null!;
    private float[] _thresholds = null!;
    private Action _suspend = null!, _resume = null!;
    private BossPost? _post;
    private Vector2 _returnPosition;
    private bool _monitoring, _draft;
    private double _grace;

    public static BossPostSequence Attach(Enemy boss, string id, Node caster, Action suspend,
        Action resume, float[]? thresholds = null)
    {
        var sequence = new BossPostSequence
        {
            Name = "PostSequence", _boss = boss, _caster = caster, _story = BossPostStory.Get(id),
            _suspend = suspend, _resume = resume,
            _thresholds = thresholds ?? new[] { .8f, .6f, .4f, .2f, .01f },
        };
        // ルナティック（2026-09-26）：投稿の割り込み（ボスが隠れて札を撃たせる読みの間＋最後の下書きの一枚絵 BossDraftScene）は
        //   出さない＝ボス戦を止めない。しきい値を空にすると Pending／Floor が立たず、HP は素通しで 0 まで削れる
        //   （StoryFilmQa が同じ止め方をしている）。あかりは別実装（BossAkari._postsBroken）で同じことをする。
        if (GameManager.LunaticActive) sequence._thresholds = Array.Empty<float>();
        boss.AddChild(sequence);
        return sequence;
    }

    public override void _Ready()
    {
        _realm = new BossRealmFx { Name = "BossRealmFx", Story = _story };
        _boss.GetParent().GetParent().AddChild(_realm);
        ResumeMusic();
    }

    public void ResumeMusic() => Audio.Instance?.StartPostMusic(_story.Id, Count, 0.8f);

    public bool BombHit()
    {
        if (!Active && !Pending) return false;
        _post?.BombHit();
        return true;
    }

    public bool TryStart()
    {
        if (!Pending || Active || _boss.IsPurified || Hud.BubblePaused || Pad.UiBlocked(this)) return false;
        _returnPosition = _boss.GlobalPosition;
        _monitoring = _boss.Monitoring;
        _boss.GlobalPosition = new Vector2(Field.Right - 84, 104);
        _boss.Visible = false;
        _boss.Monitoring = false;
        _suspend();
        _boss.SetBodyContactEnabled(false);
        _boss.SetPanelsInvulnerable(true);
        _caster.SetProcess(false);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        foreach (Node hazard in GetTree().GetNodesInGroup("aoe"))
            if (hazard is AreaStrike) hazard.QueueFree();
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.HideSpellCard();
        _post = new BossPost
        {
            Story = _story, Boss = _boss, Index = Count, Position = _boss.GlobalPosition,
            Broken = BreakRealm, Completed = CompletePost,
        };
        _boss.GetParent().AddChild(_post);
        return true;
    }

    private void BreakRealm(int index, Vector2 at)
    {
        _realm.BreakPost(index, at);
        Audio.Instance?.StartPostMusic(_story.Id, index + 1, 0.2f);
        if (index == 4) (GetTree().GetFirstNodeInGroup("hud") as Hud)?.HideBossBar();
    }

    private void CompletePost()
    {
        _post = null;
        Count++;
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        if (Count == _thresholds.Length)
        {
            _draft = true;
            BossDraftScene.Play((Hud)GetTree().GetFirstNodeInGroup("hud"), _boss.GetParent(), _story, () =>
            {
                _draft = false;
                Restore();
                _boss.DealDirectDamage(Enemy.BarHp * _boss.TotalBars);
            });
        }
        else
        {
            Restore();
            _resume();
        }
    }

    private void Restore()
    {
        _boss.GlobalPosition = _returnPosition;
        _boss.Visible = true;
        _boss.Monitoring = _monitoring;
        _boss.SetPanelsInvulnerable(false);
        _grace = 0.9;
        _caster.SetProcess(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active || _grace <= 0 || _boss.IsPurified || Hud.BubblePaused || Pad.UiBlocked(this)) return;
        _grace -= delta;
        _boss.SetBodyContactEnabled(_grace <= 0);
    }
}
