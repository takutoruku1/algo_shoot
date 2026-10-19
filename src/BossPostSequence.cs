using Godot;
using System;

public partial class BossPostSequence : Node
{
    public int Count { get; private set; }
    public bool Active => _draft;
    public float Floor => _thresholds.Length > 0 && !_finished ? _thresholds[^1] : 0;
    public bool Pending => Count < _thresholds.Length && _boss.HpRatio <= Threshold(Count) + 0.00001f;
    private Enemy _boss = null!;
    private BossPostStory _story = null!;
    private BossRealmFx _realm = null!;
    private float[] _thresholds = null!;
    private Action _suspend = null!, _changed = null!;
    private BossPost? _post;
    private bool _draft, _finished;

    public static BossPostSequence Attach(Enemy boss, string id, Action suspend, Action changed, float[]? thresholds = null)
    {
        var sequence = new BossPostSequence
        {
            Name = "PostSequence", _boss = boss, _story = BossPostStory.Get(id),
            _suspend = suspend, _changed = changed,
            _thresholds = GameManager.SkipBossStory ? Array.Empty<float>()
                : thresholds ?? new[] { .8f, .6f, .4f, .2f, .01f },
        };
        boss.AddChild(sequence);
        return sequence;
    }

    private float Threshold(int index) => Mathf.RoundToInt(_thresholds[index] * (_boss.TotalBars * _boss.BarHp))
        / (float)(_boss.TotalBars * _boss.BarHp);

    public override void _Ready()
    {
        _realm = new BossRealmFx { Name = "BossRealmFx", Story = _story };
        _boss.GetParent().GetParent().AddChild(_realm);
        ResumeMusic();
    }

    public void ResumeMusic() => Music(_thresholds.Length == 0 ? 5 : Count, 0.8f);

    private void Music(int depth, float fade)
    {
        if (_story.Id == "akari") Audio.Instance?.StartAkariMusic(depth, fade);
        else Audio.Instance?.StartPostMusic(_story.Id, depth, fade);
    }

    public void UpdateDamage()
    {
        if (!Pending || Active || _post is { IsBroken: true } || _boss.IsPurified || Hud.BubblePaused || Pad.UiBlocked(this)) return;
        EnsurePost();
        Count++;
        _post!.Shatter();
        _changed();
    }

    private void EnsurePost()
    {
        if (_post != null) return;
        _post = new BossPost
        {
            Story = _story, Boss = _boss, Index = Count, Position = _boss.GlobalPosition,
            Broken = (index, at) =>
            {
                _realm.BreakPost(index, at);
                Music(index + 1, 0.2f);
            },
            Completed = CompletePost,
        };
        _boss.GetParent().AddChild(_post);
    }

    private void CompletePost()
    {
        _post = null;
        if (Count != _thresholds.Length || _boss.IsPurified) return;
        _draft = true;
        _suspend();
        BossDraftScene.Play((Hud)GetTree().GetFirstNodeInGroup("hud"), _boss.GetParent(), _story, () =>
        {
            _draft = false;
            _finished = true;
            _boss.DealDirectDamage(_boss.BarHp * _boss.TotalBars);
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active || _boss.IsPurified || Hud.BubblePaused || Pad.UiBlocked(this) || Count >= _thresholds.Length) return;
        if (_post is { IsBroken: true }) return;
        float start = Count == 0 ? 1 : Threshold(Count - 1);
        float progress = Mathf.Clamp((start - _boss.HpRatio) / (start - Threshold(Count)), 0, 1);
        if (_boss.GaugeVulnerable || progress > 0)
        {
            EnsurePost();
            _post!.SetDamageProgress(progress);
        }
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_post)) _post!.QueueFree();
        if (IsInstanceValid(_realm)) _realm.QueueFree();
    }
}
