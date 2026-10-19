using Godot;

// カットシーン自身のフェードで戦闘背景が透けないよう、独立した不透明の板を背後に置く。
public partial class CutsceneBackdrop : ColorRect
{
    // 本体が消えた後だけ板を引き、フェード中の背景露出を防ぐ。
    private const double FallTime = 0.3;
    private double _t;
    private bool _leaving;

    // 本体の相対 Z=-1 の子画像より奥へ置く余白。
    private const int Depth = 8;

    public static CutsceneBackdrop Attach(Hud hud, int baseZ)
    {
        var pad = new CutsceneBackdrop
        {
            Name = "CutsceneBackdrop",
            Color = Colors.Black,   // 初フレームから背景を遮蔽する。
            Size = new Vector2(384, 216),
            ZIndex = baseZ - Depth,
            ZAsRelative = false,
            MouseFilter = MouseFilterEnum.Ignore,
            // カットシーンは World/Game を止めるが、板自身は自分でフェードを進める必要がある。
            ProcessMode = ProcessModeEnum.Always,
        };
        hud.AddChild(pad);
        return pad;
    }

    // カットシーン本体が消え終わってから呼ぶ（Restore() 相当のタイミング）。板は自分で引いて消える。
    public void Dismiss()
    {
        if (_leaving) return;
        _leaving = true;
        _t = 0;
    }

    public override void _Process(double delta)
    {
        if (!_leaving) return;
        _t += delta;
        float k = Mathf.Clamp((float)(_t / FallTime), 0, 1);
        Color = new Color(0, 0, 0, 1 - k);
        if (k >= 1) QueueFree();
    }
}
