using Godot;

public partial class Records : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;
    private static readonly Color Surface = new("141719");
    private static readonly Color Raised = new("1c2224");
    private static readonly Color Ink = new("edf3ef");
    private static readonly Color Muted = new("a1afaa");
    private static readonly Color Line = new("303a38");
    private static readonly Color TimeInk = new("98dec6");
    private static readonly Color ScoreInk = new("f0c6a1");
    private static readonly Rect2 MemoryRect = new(48, 204, 448, 374);
    private static readonly Rect2 HomeRect = new(48, 662, 192, 40);
    private readonly Texture2D?[] _memories = new Texture2D?[4];
    private readonly Texture2D?[] _avatars = new Texture2D?[4];
    private int _sel;
    private double _t, _revealT;
    private float _tabX = 48;
    private bool _backHeld, _navHeld, _leaving, _autoplay;
    private int _hover = -1;

    private static readonly (string id, string label, Job job, string art)[] Stages =
    {
        ("akari", "STAGE 01", Job.Melee, "res://char/bg2/boss/akari_real_v1.png"),
        ("koharu", "STAGE 02", Job.Heal, "res://char/bg2/boss/koharu_real_v1.png"),
        ("rei", "STAGE 03", Job.Magic, "res://char/bg2/boss/rei_real_v1.png"),
        ("final", "FINAL", Job.Tank, "res://char/bg2/boss/mina_real_v1.png"),
    };
    private static readonly (GameManager.Diff diff, string label, Color color)[] Difficulties =
    {
        (GameManager.Diff.Easy, "EASY", new Color("98dec6")),
        (GameManager.Diff.Normal, "NORMAL", new Color("a6dcec")),
        (GameManager.Diff.Hard, "HARD", new Color("eeb99b")),
        (GameManager.Diff.Lunatic, "LUNATIC", new Color("e6a6c3")),
    };

    public override void _Ready()
    {
        _game = GetNode<GameManager>("/root/Game");
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        for (int i = 0; i < Stages.Length; i++)
        {
            if (Known(i)) _avatars[i] = GD.Load<Texture2D>(CompanionDialogue.AccountPortrait(Stages[i].job));
            // The final name is known before its room has been restored.
            if (Cleared(i)) _memories[i] = GD.Load<Texture2D>(Stages[i].art);
        }
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
    }

    private bool Cleared(int index) => index == 3 ? _game.IsFinalCleared : _game.IsStageCleared(Stages[index].id);
    private bool Known(int index) => index == 3 ? _game.AllStoryCleared || _game.IsFinalCleared : Cleared(index);
    private string StageName(int index) => Known(index) ? Jobs.Get(Stages[index].job).CharacterName : "???";
    private Color StageAccent(int index) => Known(index) ? CompanionDialogue.Accent(Stages[index].job) : Muted;
    private static Rect2 TabRect(int index) => new(48 + index * 296, 102, 296, 76);

    private int ClearCount()
    {
        int count = 0;
        for (int i = 0; i < Stages.Length; i++) if (Cleared(i)) count++;
        return count;
    }

    private int RecordCount(string id)
    {
        int count = 0;
        foreach (var d in Difficulties)
            if (_game.GetBestTime(id, d.diff).HasValue || _game.GetBestScore(id, d.diff).HasValue) count++;
        return count;
    }

    private (GameManager.Diff diff, long score)? BestScore(string id)
    {
        (GameManager.Diff diff, long score)? best = null;
        foreach (var d in Difficulties)
        {
            var score = _game.GetBestScore(id, d.diff);
            if (score.HasValue && (!best.HasValue || score.Value > best.Value.score)) best = (d.diff, score.Value);
        }
        return best;
    }

    private void SelectStage(int index)
    {
        if (_sel == index) return;
        _sel = index;
        _revealT = 0;
        Audio.Instance?.PlayUiMove();
        QueueRedraw();
    }

    private void GoHome()
    {
        if (_leaving) return;
        _leaving = true;
        Audio.Instance?.PlayUiCancel();
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _revealT += delta;
        _tabX = Mathf.Lerp(_tabX, TabRect(_sel).Position.X, 1f - Mathf.Exp(-18f * (float)delta));
        if (_leaving || _autoplay) { QueueRedraw(); return; }
        if (Pad.UiBlocked(this))
        {
            _backHeld = _navHeld = true;
            _hover = -1;
            QueueRedraw();
            return;
        }

        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < Stages.Length; i++) UiKit.Hotspot(TabRect(i), i);
        UiKit.Hotspot(HomeRect, 4);
        _hover = UiKit.HoveredId();
        int click = UiKit.ClickedId(Pad.MouseClick());
        if (click >= 0 && click < Stages.Length) SelectStage(click);

        bool prev = Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_up") || Pad.Pressed(JoyButton.LeftShoulder);
        bool next = Input.IsActionPressed("ui_right") || Input.IsActionPressed("ui_down") || Pad.Pressed(JoyButton.RightShoulder);
        if ((prev || next) && !_navHeld) SelectStage((_sel + (prev ? 3 : 1)) % Stages.Length);
        _navHeld = prev || next;

        // もどる＝X／T／Esc／パッドB（Esc は 2026-09-26 に「一つ前の画面へ」として復帰。メニューは M）。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.T) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld;
        _backHeld = back;
        if ((backEdge || click == 4 || Pad.MouseRightClick()) && _t > 0.2) GoHome();
        QueueRedraw();
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, W, H), Surface);
        DrawRect(new Rect2(0, 0, W, 88), Raised);
        DrawRect(new Rect2(0, 88, W, 1), Line);
        DrawRect(new Rect2(0, 648, W, H - 648), Raised);
        DrawRect(new Rect2(0, 648, W, 1), Line);
        DrawHeader();
        DrawTabs();
        DrawMemory();
        DrawBests();
        DrawTable();
        DrawFooter();
        UiKit.EndDesign(this);
    }

    private void DrawHeader()
    {
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(48, 22), "記録", 32, Ink);
        UiKit.Text(this, UiKit.Mono, new Vector2(130, 37), "RECORDS", 13, Muted);
        int clears = ClearCount();
        UiKit.Text(this, UiKit.Zen, new Vector2(950, 24), "クリアステージ", 13, Muted);
        UiKit.Text(this, UiKit.Mono, new Vector2(1088, 22), $"{clears:00} / 04", 26, clears == 4 ? TimeInk : Ink,
            HorizontalAlignment.Right, 144);
        for (int i = 0; i < Stages.Length; i++)
            DrawRect(new Rect2(950 + i * 72, 64, 66, 3), Cleared(i) ? StageAccent(i) : Line);
    }

    private void DrawTabs()
    {
        for (int i = 0; i < Stages.Length; i++)
        {
            var r = TabRect(i);
            bool active = _sel == i;
            if (active || _hover == i) DrawRect(r, active ? Raised : new Color("191d20"));
            UiKit.FaceAvatar(this, r.Position + new Vector2(35, 35), 22, _avatars[i], StageAccent(i), false, 0);
            UiKit.Text(this, UiKit.Mono, r.Position + new Vector2(75, 11), Stages[i].label, 13, Muted);
            UiKit.Text(this, UiKit.ZenBold, r.Position + new Vector2(75, 32), StageName(i), 20, active ? Ink : Muted);
            if (Cleared(i)) UiKit.Text(this, UiKit.Mono, r.Position + new Vector2(212, 29), "CLEAR", 13, TimeInk);
        }
        DrawLine(new Vector2(48, 178), new Vector2(1232, 178), Line, 1);
        DrawRect(new Rect2(_tabX, 176, 296, 3), StageAccent(_sel));
    }

    private void DrawMemory()
    {
        var art = _memories[_sel];
        float reveal = Mathf.Clamp((float)_revealT / 0.24f, 0, 1);
        if (art != null)
        {
            Vector2 sourceSize = art.GetSize();
            float scale = Mathf.Max(MemoryRect.Size.X / sourceSize.X, MemoryRect.Size.Y / sourceSize.Y);
            Vector2 crop = MemoryRect.Size / scale;
            DrawTextureRectRegion(art, MemoryRect, new Rect2((sourceSize - crop) / 2, crop), new Color(1, 1, 1, reveal));
        }
        else
        {
            DrawRect(MemoryRect, Raised);
            UiKit.Text(this, UiKit.Mono, new Vector2(48, 328), Stages[_sel].label, 18, Muted,
                HorizontalAlignment.Center, 448);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(48, 365), "未クリア", 28, Ink,
                HorizontalAlignment.Center, 448);
            DrawLine(new Vector2(236, 425), new Vector2(308, 425), Line, 2);
        }
        DrawRect(new Rect2(48, 578, 448, 3), art != null ? StageAccent(_sel) : Line);
        UiKit.Text(this, UiKit.Mono, new Vector2(48, 594), art != null ? "REAL REALM" : "NOT CLEARED", 13,
            art != null ? StageAccent(_sel) : Muted);
        UiKit.Text(this, UiKit.Zen, new Vector2(252, 592), art != null ? "取り戻した景色" : "記録なし", 14, Muted,
            HorizontalAlignment.Right, 244);
    }

    private void DrawBests()
    {
        var stage = Stages[_sel];
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(552, 199), StageName(_sel), 30, Ink);
        UiKit.Text(this, UiKit.Zen, new Vector2(1070, 213), "記録", 13, Muted);
        UiKit.Text(this, UiKit.Mono, new Vector2(1114, 212), $"{RecordCount(stage.id):00} / 04", 15, Muted,
            HorizontalAlignment.Right, 118);
        var bestTime = _game.BestAcrossDiffs(stage.id);
        var bestScore = BestScore(stage.id);
        UiKit.Text(this, UiKit.Zen, new Vector2(552, 264), "最速タイム", 14, Muted);
        UiKit.Text(this, UiKit.Zen, new Vector2(868, 264), "最高スコア", 14, Muted);
        if (bestTime.HasValue) UiKit.Text(this, UiKit.Mono, new Vector2(720, 265), bestTime.Value.diff.ToString().ToUpperInvariant(), 13,
            TimeInk, HorizontalAlignment.Right, 104);
        if (bestScore.HasValue) UiKit.Text(this, UiKit.Mono, new Vector2(1120, 265), bestScore.Value.diff.ToString().ToUpperInvariant(), 13,
            ScoreInk, HorizontalAlignment.Right, 112);
        DrawValue(bestTime.HasValue ? UiKit.FormatTime(bestTime.Value.sec) : "--", new Rect2(552, 289, 272, 48), 38,
            bestTime.HasValue ? TimeInk : Muted);
        DrawValue(bestScore.HasValue ? UiKit.FormatScore(bestScore.Value.score) : "--", new Rect2(868, 289, 364, 48), 38,
            bestScore.HasValue ? ScoreInk : Muted);
        DrawLine(new Vector2(844, 264), new Vector2(844, 340), Line, 1);
        DrawLine(new Vector2(552, 360), new Vector2(1232, 360), Line, 1);
    }

    private void DrawTable()
    {
        string id = Stages[_sel].id;
        UiKit.Text(this, UiKit.Zen, new Vector2(552, 381), "難易度", 13, Muted);
        UiKit.Text(this, UiKit.Mono, new Vector2(774, 382), "BEST TIME", 13, Muted, HorizontalAlignment.Right, 154);
        UiKit.Text(this, UiKit.Mono, new Vector2(994, 382), "BEST SCORE", 13, Muted, HorizontalAlignment.Right, 216);
        for (int i = 0; i < Difficulties.Length; i++)
        {
            var diff = Difficulties[i];
            float y = 416 + i * 54;
            var time = _game.GetBestTime(id, diff.diff);
            var score = _game.GetBestScore(id, diff.diff);
            bool hasRecord = time.HasValue || score.HasValue;
            if (hasRecord) DrawRect(new Rect2(552, y, 680, 49), Raised);
            DrawRect(new Rect2(552, y + 12, 3, 25), hasRecord ? diff.color : Line);
            UiKit.Text(this, UiKit.Mono, new Vector2(570, y + 13), diff.label, 16, hasRecord ? diff.color : Muted);
            DrawValue(time.HasValue ? UiKit.FormatTime(time.Value) : "--", new Rect2(746, y + 10, 182, 30), 22,
                time.HasValue ? Ink : Muted, true);
            DrawValue(score.HasValue ? UiKit.FormatScore(score.Value) : "--", new Rect2(994, y + 10, 216, 30), 22,
                score.HasValue ? Ink : Muted, true);
            DrawLine(new Vector2(552, y + 49), new Vector2(1232, y + 49), Line, 1);
        }
    }

    private int ValueSize(string text, float width, int preferred)
    {
        int size = preferred;
        while (size > 1 && UiKit.TextW(UiKit.Mono, text, size) > width) size--;
        return size;
    }

    private void DrawValue(string value, Rect2 rect, int size, Color color, bool right = false)
    {
        int fit = ValueSize(value, rect.Size.X, size);
        UiKit.Text(this, UiKit.Mono, rect.Position + new Vector2(0, (rect.Size.Y - UiKit.Mono.GetHeight(fit)) / 2), value, fit, color,
            right ? HorizontalAlignment.Right : HorizontalAlignment.Left, rect.Size.X);
    }

    private void DrawFooter()
    {
        if (_hover == 4) UiKit.Box(this, HomeRect, new Color("2a3335"), 6);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(64, 670), "ホームに戻る", 16, Ink);
        UiKit.Key(this, new Vector2(199, 670), Pad.CancelToken, Surface, Line, Muted);
    }
}
