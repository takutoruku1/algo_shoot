using Godot;
using System;

public partial class AkariPost : Area2D
{
    public static readonly string[] Posts =
    {
        "雨 置き傘 会社に忘れた\n駅まで走る",
        "向かいの席、空いたまま。\n……三日目。",
        "0:40 通知\n開いて閉じて開いた\n既読 まだ",
        "いいねが、ひとつ。\n増えてないの、知ってるのに、\n今日だけで四回も、見にきちゃった。",
        "すき、すき、すき。\n……ひとつでいいから、\n本物になって。",
    };
    private static readonly string[] Replies =
    {
        "……一緒に帰れたら。それだけだったのに。",
        "席は空いたのに、仕事だけ、増えた。",
        "返事いらないって、書いたのは、あたし。",
        "増えないって知ってても、閉じられない。",
        "それだけは……消せなかった。",
    };
    public BossAkari Boss = null!;
    public int Index;
    public Action Completed = null!;
    private const int Strength = 24;
    private const double ReadHold = 3.2;
    private int _ink = Strength;
    private double _time, _hitTime, _breakTime;
    private bool _broken;
    private CollisionShape2D _shape = null!;
    private Texture2D _face = null!;
    private readonly string[] _fragments = new string[8];
    private static readonly Color Accent = new("a3d7eb");

    public override void _Ready()
    {
        AddToGroup("akari_post");
        ZIndex = 8;
        CollisionLayer = 16;
        CollisionMask = 2;
        _shape = new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(156, 96) } };
        AddChild(_shape);
        _face = GD.Load<Texture2D>(CompanionDialogue.AccountIcon("akari"));
        string body = Posts[Index].Replace("\n", "");
        for (int i = 0; i < _fragments.Length; i++)
            _fragments[i] = body[(i * body.Length / 8)..((i + 1) * body.Length / 8)];
        AreaEntered += Hit;
        Audio.Instance?.PlaySpell();
    }

    private void Hit(Area2D area)
    {
        if (_broken || Hud.BubblePaused || area is not Bullet { IsEnemy: false, Active: true } bullet) return;
        if (!bullet.RegisterChargeHit(this)) return;
        bullet.ChargeImpact(bullet.GlobalPosition);
        int damage = bullet.Damage;
        bullet.TryChain(Boss);
        if (bullet.Pierce > 0) bullet.Pierce--;
        else GetNode<BulletPool>("/root/Pool").Despawn(bullet);
        (GetTree().GetFirstNodeInGroup("player") as Player)?.NotifyShotHit(Boss);
        Damage(Mathf.Clamp(damage, 1, bullet.Charged ? 12 : 4));
    }

    public void BombHit()
    {
        if (!_broken && !Hud.BubblePaused) Damage(8);
    }

    private void Damage(int amount)
    {
        _ink = Mathf.Max(0, _ink - amount);
        if (_hitTime <= 0) Audio.Instance?.PlayStrip();
        _hitTime = 0.09;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Hud.BubblePaused || Pad.UiBlocked(this)) return;
        _time += delta;
        _hitTime = Math.Max(0, _hitTime - delta);
        if (!_broken && _ink == 0 && _time >= ReadHold)
        {
            _broken = true;
            SetDeferred(PropertyName.Monitoring, false);
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
            GetNode<BulletPool>("/root/Pool").DespawnPlayerBullets();
            FxLayer.Instance?.Shatter(GlobalPosition);
            Audio.Instance?.PlaySpell();
            GameCamera.Instance?.Shake(1.4f, 0.12f);
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.ShowBossLine("あかり", Replies[Index], Accent, 2.0);
            QueueRedraw();
            return;
        }
        if (_broken)
        {
            _breakTime += delta;
            if (_breakTime >= 2.1)
            {
                Completed();
                QueueFree();
                return;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float enter = Mathf.Clamp((float)(_time / 0.4), 0, 1);
        float split = Mathf.Clamp((float)(_breakTime / 0.65), 0, 1);
        float alpha = enter * (1 - split);
        UiKit.BeginDesign(this);
        var card = new Rect2(-260, -160, 520, 320);
        if (!_broken)
        {
            UiKit.Box(this, new Rect2(-252, -170, 504, 320), new Color("273237"), 6);
            DrawCard(card, alpha);
            float damage = 1f - _ink / (float)Strength;
            for (int i = 0; i < 4; i++)
            {
                if (damage <= i * 0.24f) continue;
                float x = -185 + i * 117;
                DrawPolyline(new[] { new Vector2(x, -160), new Vector2(x + 14, -126),
                    new Vector2(x - 10, -102), new Vector2(x + 28, -76) }, new Color(Accent, 0.8f), 2, true);
                DrawLine(new Vector2(x + 28, 113), new Vector2(x - 6, 159), new Color(Accent, 0.8f), 2, true);
            }
            DrawRect(new Rect2(-236, 148, 472 * damage, 2), Accent);
            if (_hitTime > 0) UiKit.Box(this, card, null, 6, new Color(1, 1, 1, 0.8f), 3);
            if (GetTree().GetFirstNodeInGroup("player") is Player player && player.LockTarget == Boss)
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    {
                        var corner = new Vector2(x * 268, y * 168);
                        DrawLine(corner, corner + new Vector2(-x * 18, 0), UiKit.Purify, 3);
                        DrawLine(corner, corner + new Vector2(0, -y * 18), UiKit.Purify, 3);
                    }
        }
        else
        {
            for (int i = 0; i < 8; i++)
            {
                float direction = i % 2 == 0 ? -1 : 1;
                var at = new Vector2(-230 + i * 62 + direction * split * 80, -80 + i % 3 * 64 + split * split * 140);
                float width = UiKit.TextW(UiKit.Zen, _fragments[i], 18) + 8;
                UiKit.Box(this, new Rect2(at, new Vector2(width, 26)), new Color(0.08f, 0.14f, 0.17f, alpha), 2,
                    new Color(Accent, alpha), 1);
                UiKit.Text(this, UiKit.Zen, at + new Vector2(4, 0), _fragments[i], 18, new Color(1, 1, 1, alpha));
            }
        }
        UiKit.EndDesign(this);
    }

    private void DrawCard(Rect2 card, float alpha)
    {
        UiKit.Box(this, card, new Color(0.065f, 0.095f, 0.11f, alpha), 6, new Color(Accent, alpha * 0.8f), 2);
        UiKit.FaceAvatar(this, new Vector2(-218, -119), 22, _face, new Color(Accent, alpha), false, topCrop: 0f, alpha: alpha);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(-180, -146), "あかり", 23, new Color(1, 1, 1, alpha));
        UiKit.Text(this, UiKit.Zen, new Vector2(-180, -116), "@akari.", 18, new Color(Accent, alpha));
        UiKit.Text(this, UiKit.Zen, new Vector2(112, -136), $"投稿 {Index + 1}/5", 19, new Color(Accent, alpha));
        DrawLine(new Vector2(-236, -78), new Vector2(236, -78), new Color(Accent, 0.2f * alpha));
        UiKit.Multi(this, UiKit.Zen, new Vector2(-232, -57), Posts[Index], 25, new Color(1, 1, 1, alpha), 466);
        UiKit.Text(this, UiKit.Zen, new Vector2(-232, 112), "返信 0", 17, new Color(Accent, alpha * 0.7f));
        UiKit.Text(this, UiKit.Zen, new Vector2(-122, 112), "いいね 1", 17, new Color(Accent, alpha * 0.7f));
    }
}
