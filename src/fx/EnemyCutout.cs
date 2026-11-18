using Godot;

public partial class EnemyCutout : Node2D
{
    private Sprite2D _body = null!;
    private AnimationPlayer _idle = null!, _action = null!;
    private AnimationPlayer? _blink;
    private bool _coordinated;
    private float _sourceHeight;
    internal string ActionName => _action.CurrentAnimation;
    internal double ActionTime => _action.CurrentAnimationPosition;

    public void Bind(Sprite2D body, float phase)
    {
        _body = body;
        _sourceHeight = body.Texture.GetHeight();
        // 高さは既存のサイズ計算と改心クロスフェードが参照する。
        body.Texture = new GradientTexture2D
        {
            Width = 1, Height = (int)_sourceHeight,
            Gradient = new Gradient { Colors = new[] { Colors.Transparent, Colors.Transparent } },
        };
        _idle = GetNode<AnimationPlayer>("IdleAnimation");
        _action = GetNode<AnimationPlayer>("ActionAnimation");
        _blink = GetNodeOrNull<AnimationPlayer>("BlinkAnimation");
        _coordinated = HasNode("Torso/Action/Gesture");
        _idle.Play("idle");
        _idle.Seek(phase / Mathf.Tau * _idle.GetAnimation("idle").Length, true);
        _blink?.Play("blink");
        if (_blink != null) _blink.Seek(phase / Mathf.Tau * _blink.GetAnimation("blink").Length, true);
        SyncBody();
    }

    public void Advance(double delta)
    {
        if (_coordinated && _action.IsPlaying())
        {
            _action.Advance(delta);
            if (!_action.IsPlaying()) _idle.Play("idle");
        }
        else
        {
            _idle.Advance(delta);
            _action.Advance(delta);
        }
        _blink?.Advance(delta);
        SyncBody();
    }

    public void PlayAction(string name, float duration = 0f)
    {
        if (name == "spin") name = "recoil";
        if (_coordinated)
        {
            _idle.Stop(true);
            var joint = GetNode<Bone2D>("Torso/Action/Gesture");
            var actionJoint = joint.GetNode<Bone2D>("Action");
            actionJoint.Transform = joint.Rest.AffineInverse() * joint.Transform * actionJoint.Transform;
            joint.Transform = joint.Rest;
        }
        _action.Stop(_coordinated);
        _action.SpeedScale = duration > 0 ? _action.GetAnimation(name).Length / duration : 1;
        if (_coordinated) _action.PlayWithCapture(name, .1);
        else _action.Play(name);
        _action.Advance(0);
    }

    public void TransferToFade(Sprite2D fadingBody)
    {
        _idle.Stop(true);
        _action.Stop(true);
        _blink?.Stop(true);
        Reparent(fadingBody, false);
        _body = fadingBody;
        SyncBody();
    }

    public override void _Process(double delta) => SyncBody();

    private void SyncBody()
    {
        if (_body == null) return;
        Scale = new Vector2(_body.FlipH ? -1 : 1, 1) * (_sourceHeight / 720f);
        Modulate = _body.SelfModulate;
    }
}
