using Godot;

public partial class BossSelectRoot : Node2D
{
    public const string ScenePath = "res://BossSelect.tscn";
    internal static readonly string[] PortraitPaths = {
        "res://char/v3/boss_akari_body_idle_v3.png",
        "res://char/v3/boss_koharu_body_idle.png",
        "res://char/v3/boss_rei_body_idle.png",
        "res://char/v3/boss_mina_body_idle.png",
    };
    private static readonly Color[] Accents = { new("f0c969"), new("a6dac8"), new("de91b9"), new("87d7ed") };
    private readonly Texture2D[] _portraits = new Texture2D[4];
    private readonly float[] _cardLift = new float[4];
    private GameManager _game = null!;
    private int _selected;
    private bool _navHeld = true, _acceptHeld = true, _backHeld = true, _leaving;
    private double _elapsed;
    private const int ConfirmId = 4, BackId = 5, MenuId = 6;
    private static Rect2 BossRect(int index) => new(56 + index * 298, 168, 274, 390);
    private static Rect2 ConfirmRect => new(864, 588, 360, 54);
    private static (string token, string label)[] Hints() => UiKit.PhoneHints("←→", "決定", "もどる");

    public override void _Ready()
    {
        _game = GetNode<GameManager>("/root/Game");
        _selected = Mathf.Clamp(_game.PendingBossIndex, 0, 3);
        for (int i = 0; i < _portraits.Length; i++) _portraits[i] = i == 3 && !_game.FinalBossRevealed ? FinalAccount.CreateIcon() : GD.Load<Texture2D>(PortraitPaths[i]);
        Pad.ReleaseMouseFollow();
        Audio.Instance?.Music(Audio.Instance.BgmMenu);
    }

    private void ChooseBoss(int index)
    {
        if (_leaving) return;
        _leaving = true;
        _selected = index;
        _game.PendingBossIndex = index;
        _game.PendingStageScene = BossRushRoot.SingleScenePath;
        Audio.Instance?.PlayUiConfirm();
        GameManager.FadeToScene(this, "res://DiffSelect.tscn");
    }

    private void Back()
    {
        if (_leaving) return;
        _leaving = true;
        Audio.Instance?.PlayUiCancel();
        GameManager.FadeToScene(this, "res://Hub.tscn");
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        for (int i = 0; i < _cardLift.Length; i++)
            _cardLift[i] = Mathf.Lerp(_cardLift[i], i == _selected ? 6 : 0, 1 - Mathf.Exp(-(float)delta * 16));
        QueueRedraw();
        if (_leaving) return;
        if (Pad.UiBlocked(this))
        {
            _navHeld = _acceptHeld = _backHeld = true;
            return;
        }

        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < _portraits.Length; i++) UiKit.Hotspot(BossRect(i), i);
        UiKit.Hotspot(ConfirmRect, ConfirmId);
        UiKit.Hotspot(UiKit.HintItemRect(UiKit.HintAnchor, Hints(), "もどる"), BackId);
        UiKit.Hotspot(UiKit.HintItemRect(UiKit.HintAnchor, Hints(), "メニュー"), MenuId);
        int hovered = UiKit.HoveredId();
        if (Pad.UsingMouse && hovered >= 0 && hovered < _portraits.Length && hovered != _selected)
        {
            _selected = hovered;
            Audio.Instance?.PlayUiMove();
        }

        bool previous = Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_up");
        bool next = Input.IsActionPressed("ui_right") || Input.IsActionPressed("ui_down");
        if ((previous || next) && !_navHeld)
        {
            _selected = (_selected + (previous ? -1 : 1) + _portraits.Length) % _portraits.Length;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = previous || next;

        bool accept = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool confirm = accept && !_acceptHeld;
        _acceptHeld = accept;
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool cancel = back && !_backHeld;
        _backHeld = back;
        int clicked = UiKit.ClickedId(Pad.MouseClick());
        if (_elapsed < .2) return;
        if (clicked == MenuId)
        {
            GetNode<PauseMenu>("/root/PauseMenu").Open();
            return;
        }
        if (cancel || clicked == BackId || Pad.MouseRightClick()) { Back(); return; }
        if (clicked >= 0 && clicked < _portraits.Length) ChooseBoss(clicked);
        else if (confirm || clicked == ConfirmId) ChooseBoss(_selected);
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        UiKit.ExhibitBackdrop(this, Accents[_selected], 136);
        UiKit.Text(this, UiKit.Mono, new Vector2(56, 20), "BOSS ENCOUNTER / SELECT", 13, Accents[_selected]);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(56, 40), "BOSS単体", 32, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(56, 94),
            "道中・中ボスはありません。選んだBOSSの第1形態から、すべての形態と戦います。", UiKit.FontBody, UiKit.Text2);

        for (int i = 0; i < _portraits.Length; i++)
        {
            Rect2 card = BossRect(i);
            float lift = _cardLift[i];
            card.Position -= new Vector2(0, lift);
            bool selected = i == _selected;
            Color accent = Accents[i];
            UiKit.ExhibitPanel(this, card, accent, selected, selected ? 12 : 6);
            UiKit.RadialGlow(this, new Vector2(card.GetCenter().X, 322 - lift), 156, accent, selected ? .22f : .09f);
            DrawColoredPolygon(new[] { card.Position + new Vector2(26, 1), card.Position + new Vector2(98, 1),
                new Vector2(card.End.X - 12, 446 - lift), new Vector2(card.End.X - 62, 446 - lift) }, new Color(accent, .045f));
            UiKit.Text(this, UiKit.Rajdhani, card.Position + new Vector2(16, 35), $"0{i + 1}", 94, new Color(accent, .065f));
            UiKit.Text(this, UiKit.Mono, card.Position + new Vector2(20, 16), $"{i + 1:00}", UiKit.FontLabel, accent);
            if (selected)
                UiKit.Text(this, UiKit.ZenBold, card.Position + new Vector2(20, 16), "選択中", UiKit.FontSmall, accent,
                    HorizontalAlignment.Right, card.Size.X - 40);
            UiKit.ExhibitPlinth(this, new Vector2(card.GetCenter().X, 440 - lift), new Vector2(114, 23), accent);
            Rect2 content = UiKit.ContentRect(_portraits[i]);
            float scale = Mathf.Min(236 / content.Size.X, 244 / content.Size.Y);
            UiKit.DrawPortraitScaled(this, _portraits[i], new Vector2(card.GetCenter().X, 431 - lift), scale);
            DrawLine(new Vector2(card.Position.X + 26, 518 - lift), new Vector2(card.End.X - 26, 518 - lift), new Color(accent, .22f), 1);
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(card.Position.X, 478 - lift), BossRushRoot.DisplayName(i, _game), UiKit.FontTitle,
                UiKit.White, HorizontalAlignment.Center, card.Size.X);
            UiKit.Text(this, UiKit.Zen, new Vector2(card.Position.X, 522 - lift), "全形態と対戦", UiKit.FontBody,
                selected ? accent : UiKit.Text3, HorizontalAlignment.Center, card.Size.X);
        }

        UiKit.Text(this, UiKit.Zen, new Vector2(56, 604), "難易度は次の画面で選べます。", UiKit.FontBody, UiKit.Text2);
        UiKit.ExhibitPanel(this, ConfirmRect, Accents[_selected], true, 6);
        UiKit.Text(this, UiKit.ZenBold, ConfirmRect.Position + new Vector2(0, 15),
            $"{BossRushRoot.DisplayName(_selected, _game)}で難易度を選ぶ", UiKit.FontHeading, UiKit.White,
            HorizontalAlignment.Center, ConfirmRect.Size.X);
        UiKit.HintBarPlate(this, UiKit.HintAnchor, Hints(), "もどる", "メニュー");
        UiKit.EndDesign(this);
    }
}
