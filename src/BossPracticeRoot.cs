using Godot;
using System.Linq;

public partial class BossPracticeRoot : Node2D
{
    public const string ScenePath = "res://BossPractice.tscn";
    private static readonly BgLayers.Layer[][] Backgrounds =
        { AkariRoot.BossLayers, KoharuRoot.BossLayers, ReiRoot.BossLayers, MinaRoot.BossLayers };
    private static readonly string[] Difficulties = { "EASY", "NORMAL", "HARD", "LUNATIC" };
    private static readonly Color Accent = new("82d8dc");
    private static readonly Rect2 PreviewRect = new(41, 404, Field.WorldRight - Field.Left, Field.Height);
    private readonly Texture2D[] _selectionAvatars = new Texture2D[4];
    private bool _hideFinal;
    private bool HiddenPreview => _bossIndex == 3 && _hideFinal;
    private GameManager _game = null!;
    private GameManager.MetaSnapshot _snapshot = null!;
    private GameManager.Diff _previousDifficulty;
    private float _previousContamination;
    private bool _previousAutoSave, _previousNoConsume, _previousTraining;
    private Node2D _battle = null!, _world = null!;
    private Control _overlay = null!;
    private BackBufferCopy _previewCopy = null!;
    private ColorRect _previewImage = null!;
    private double _previewTime;
    private Enemy _boss = null!;
    private Player _player = null!;
    private Hud _hud = null!;
    private BossAttackCycle.Slot[] _slots = System.Array.Empty<BossAttackCycle.Slot>();
    private (string Name, string Counterplay)[] _descriptions = System.Array.Empty<(string, string)>();
    private int _bossIndex, _phase, _difficulty, _technique, _focus;
    private bool _navHeld = true, _acceptHeld = true, _backHeld = true, _tabHeld = true, _leaving;
    private Vector2I _lastNav;
    private double _navDelay;
    private int _repetitions;
    private double _elapsed;
    private ulong _lastTechnique;
    public bool Practicing { get; private set; }
    internal Enemy PracticeBoss => _boss;
    internal Player PracticePlayer => _player;
    internal int Repetitions => _repetitions;
    private int StartId => 3 + _slots.Length;
    private int BackId => StartId + 1;
    private int PhaseCount => _bossIndex == 3 ? 5 : 2;

    public override void _Ready()
    {
        Active = this;
        ProcessMode = ProcessModeEnum.Always;
        ProcessPhysicsPriority = -1000;
        _previousTimeScale = Engine.TimeScale;
        AddToGroup("boss_practice");
        _game = GetNode<GameManager>("/root/Game");
        _hideFinal = !_game.FinalBossRevealed;
        for (int i = 0; i < _selectionAvatars.Length; i++)
            _selectionAvatars[i] = GD.Load<Texture2D>(CompanionDialogue.AccountPortrait(i switch { 0 => Job.Melee, 1 => Job.Heal, 2 => Job.Magic, _ => Job.Tank }));
        _snapshot = _game.SnapshotMeta();
        _previousDifficulty = _game.Difficulty;
        _previousContamination = _game.Contamination;
        _previousAutoSave = _game.AutoSaveEnabled;
        _previousNoConsume = _game.TutorialNoConsume;
        _previousTraining = _game.TrainingMode;
        _game.AutoSaveEnabled = false;
        _game.TutorialNoConsume = true;
        _game.TrainingMode = true;
        _difficulty = (int)_game.Difficulty;
        _overlay = new Control { Name = "PracticeUi", MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 90 };
        _overlay.Draw += DrawUi;
        var ui = new CanvasLayer { Layer = 25 };
        AddChild(ui);
        ui.AddChild(_overlay);
        // Copy the battle before the menu draws so pooled bullets and screen-space attack effects appear together.
        _previewCopy = new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport };
        ui.AddChild(_previewCopy);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/practice_preview.gdshader") };
        material.SetShaderParameter("field_left_uv", Field.Left / Field.WorldRight);
        _previewImage = new ColorRect { Material = material, Position = PreviewRect.Position * UiKit.Scale,
            Size = PreviewRect.Size * UiKit.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
        _overlay.AddChild(_previewImage);
        PrepareBattle(false);
    }

    private void PrepareBattle(bool practicing)
    {
        ResetPreviewClock();
        Practicing = practicing;
        _previewCopy.Visible = _previewImage.Visible = !practicing && !HiddenPreview;
        _previewTime = 0;
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        if (IsInstanceValid(_battle)) _battle.Free();
        _game.Difficulty = (GameManager.Diff)_difficulty;
        _game.ResetRun();
        _game.SetContamination(_bossIndex == 3 ? 1 : 0);
        _battle = new Node2D { Name = "PracticeBattle", ProcessMode = ProcessModeEnum.Pausable };
        AddChild(_battle);
        var layers = Backgrounds[_bossIndex == 3 && _phase is >= 1 and <= 3 ? _phase - 1 : _bossIndex];
        _battle.AddChild(new StageBackground {
            Name = "StageBackground", LayerDefs = layers, BossLayerDefs = layers,
            LayerBossBehavior = BgLayers.BossBehavior.Illustrated, StartInBoss = true,
        });
        _world = new Node2D { Name = "World" };
        _battle.AddChild(_world);
        _diagnostics = new Node2D { Name = "PracticeCollisionOverlay", ZIndex = 100, ProcessMode = ProcessModeEnum.Always };
        _diagnostics.Draw += DrawDiagnostics;
        _world.AddChild(_diagnostics);
        _world.AddChild(new FxLayer { Name = "FxLayer" });
        _battle.AddChild(new GameCamera { Name = "GameCamera" });
        _player = new Player { Name = "Player" };
        _world.AddChild(_player);
        if (!practicing) _player.SetAsPreviewTarget();
        _player.SnapToStart();
        _player.SetCorruption(0);
        if (practicing)
        {
            _battle.AddChild(new FuryDial { Name = "FuryDial" });
            _hud = new Hud { Name = "Hud" };
            _battle.AddChild(_hud);
            _battle.AddChild(new BubbleLayer { Name = "BubbleLayer", Hud = _hud });
        }
        _boss = _bossIndex switch { 0 => new BossAkari(), 1 => new BossKoharu(), 2 => new BossRei(), _ => new BossMina() };
        _boss.Name = "PracticeBoss";
        _boss.TechniquePractice = true;
        _boss.Position = new Vector2(300, 70);
        _world.AddChild(_boss);
        _boss.PrepareTechniquePractice(_phase);
        if (!practicing)
        {
            int segment = _bossIndex is 1 or 2 && _phase == 1 ? 2 : _phase;
            _slots = BossAttackCycle.Slots(_boss, segment).Distinct().ToArray();
            _descriptions = _slots.Select(slot => BossCharacterTechnique.Describe(_boss, slot.Kind, slot.Variant)).ToArray();
            _technique = Mathf.Clamp(_technique, 0, _slots.Length - 1);
            _focus = Mathf.Clamp(_focus, 0, BackId);
            _game.EndFury();
            Audio.Instance?.Music(Audio.Instance.BgmMenu);
        }
        _boss.PracticeSlots = new[] { _slots[_technique] };
        _repetitions = 0;
        _elapsed = 0;
        _lastTechnique = 0;
        _overlay.QueueRedraw();
    }

    public void ShowSelection()
    {
        PrepareBattle(false);
        _focus = 3 + _technique;
        Pad.ReleaseMouseFollow();
        _navHeld = _acceptHeld = _backHeld = _tabHeld = true;
        _navDelay = .32;
    }

    public void StartPractice()
    {
        if (_bossIndex == 3) _hideFinal = false;
        PrepareBattle(true);
        _player.SnapToStart();
        _hud.SetLives(_player.Lives);
        Engine.TimeScale = PlaybackRate;
        _game.BeginFury(_bossIndex switch { 0 => "akari", 1 => "koharu", 2 => "rei", _ => "mina" });
        _boss.GetNode<BossPostSequence>("PostSequence").ResumeMusic();
        Pad.ReleaseMouseFollow();
    }

    public void RestartPractice() => StartPractice();

    private void SelectTechnique(int index)
    {
        if (index == _technique) return;
        _technique = index;
        foreach (var attack in _boss.GetChildren().OfType<BossCharacterTechnique>())
            attack.Cancel();
        _boss.PracticeSlots = new[] { _slots[index] };
        _previewTime = 0;
        _player.SnapToStart();
    }

    private void MovePreviewPlayer(double delta)
    {
        _previewTime += delta;
        float t = (float)_previewTime;
        var target = Field.PlayerStart + new Vector2(30 * Mathf.Sin(t * .7f), 52 * Mathf.Sin(t * .95f));
        var bounds = _player.RallyMovementBounds ?? Field.Rect;
        target = _player.GlobalPosition.MoveToward(target.Clamp(bounds.Position, bounds.End), _player.SlowestMoveSpeed * (float)delta);
        if (GetTree().GetFirstNodeInGroup("boss_character_techniques") is BossCharacterTechnique { Finished: false } attack)
            target = attack.ResolvePlayerMotion(_player.GlobalPosition, target);
        _player.GlobalPosition = target;
    }

    private void ChangeOption(int direction)
    {
        int value = _focus == 0 ? _bossIndex : _focus == 1 ? _difficulty : _phase;
        int count = _focus == 2 ? PhaseCount : 4;
        SelectOption(_focus, (value + direction + count) % count);
    }

    private void SelectOption(int group, int value)
    {
        _focus = group;
        if (group == 0 && value != _bossIndex) { _bossIndex = value; _phase = _technique = 0; }
        else if (group == 1 && value != _difficulty) _difficulty = value;
        else if (group == 2 && value != _phase) { _phase = value; _technique = 0; }
        else return;
        PrepareBattle(false);
        Audio.Instance?.PlayUiMove();
    }

    private void MoveFocus(Vector2I direction)
    {
        if (_focus < 3)
        {
            if (direction.X != 0) { ChangeOption(direction.X); return; }
            _focus = direction.Y < 0 ? Mathf.Max(0, _focus - 1) : _focus == 2 ? 3 + _technique : _focus + 1;
        }
        else if (_focus < StartId)
        {
            int index = _focus - 3;
            if (direction.Y < 0) _focus = index < 2 ? 2 : _focus - 2;
            else if (direction.Y > 0) _focus = index + 2 < _slots.Length ? _focus + 2 : StartId;
            else if (direction.X < 0) _focus = index % 2 == 0 ? 2 : _focus - 1;
            else if (index % 2 == 0 && index + 1 < _slots.Length) _focus++;
        }
        else if (direction.Y < 0) _focus = _focus == BackId ? 2 : 3 + _technique;
        else if (direction.X != 0) _focus = direction.X < 0 ? BackId : StartId;
        if (_focus >= 3 && _focus < StartId) SelectTechnique(_focus - 3);
        Audio.Instance?.PlayUiMove();
    }

    private void NextSection(int direction)
    {
        int section = _focus < 3 ? _focus : _focus < StartId ? 3 : _focus == StartId ? 4 : 5;
        section = (section + direction + 6) % 6;
        _focus = section < 3 ? section : section == 3 ? 3 + _technique : section == 4 ? StartId : BackId;
        Audio.Instance?.PlayUiMove();
    }

    private void BackToHome()
    {
        if (_leaving) return;
        _leaving = true;
        Audio.Instance?.PlayUiCancel();
        GameManager.FadeToScene(this, "res://Hub.tscn");
    }

    private void HandleSelectionClick(Vector2 at)
    {
        for (int group = 0; group < 3; group++)
        for (int option = 0; option < (group == 2 ? PhaseCount : 4); option++)
            if (ChoiceRect(group, option).HasPoint(at)) { SelectOption(group, option); return; }
        for (int i = 0; i < _slots.Length; i++)
            if (ButtonRect(i + 3).HasPoint(at))
            {
                _focus = i + 3;
                SelectTechnique(i);
                Audio.Instance?.PlayUiMove();
                return;
            }
        if (ButtonRect(StartId).HasPoint(at)) { StartPractice(); Audio.Instance?.PlayUiConfirm(); }
        else if (ButtonRect(BackId).HasPoint(at) || UiKit.HintItemRect(UiKit.HintAnchor, SelectionHints(), "もどる").HasPoint(at)) BackToHome();
        else if (UiKit.HintItemRect(UiKit.HintAnchor, SelectionHints(), "メニュー").HasPoint(at)) GetNode<PauseMenu>("/root/PauseMenu").Open();
    }

    public override void _Process(double delta)
    {
        _overlay.QueueRedraw();
        _diagnostics.QueueRedraw();
        if (_leaving || GetNode<PauseMenu>("/root/PauseMenu").IsOpen || LoadingScreen.IsActive) return;
        if (Pad.UiBlocked(this)) { _navHeld = _acceptHeld = _backHeld = _tabHeld = true; _navDelay = .32; _lastNav = Vector2I.Zero; return; }
        if (CurveEditor != null) { CurveEditor.HandleClick(); return; }
        if (Practicing)
        {
            if (HandleToolClick()) return;
            if (_player.Lives <= 0)
            {
                if (_battle.ProcessMode != ProcessModeEnum.Disabled)
                {
                    _battle.ProcessMode = ProcessModeEnum.Disabled;
                    _game.EndFury();
                    GetNode<PauseMenu>("/root/PauseMenu").Open();
                }
                return;
            }
            _hud.SetElapsed((float)_elapsed);
            var attack = GetTree().GetFirstNodeInGroup("boss_character_techniques") as BossCharacterTechnique;
            if (attack is { Finished: false } && attack.GetInstanceId() != _lastTechnique)
            {
                _lastTechnique = attack.GetInstanceId();
                _repetitions++;
            }
            return;
        }
        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        bool accept = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool back = Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Backspace) || Pad.Pressed(JoyButton.B);
        var direction = new Vector2I(left ? -1 : right ? 1 : 0, up ? -1 : down ? 1 : 0);
        if (direction != Vector2I.Zero)
        {
            _navDelay -= delta;
            if (!_navHeld || _lastNav != Vector2I.Zero && direction != _lastNav)
            { MoveFocus(direction); _navDelay = .32; }
            else if (_navDelay <= 0 && _focus >= 3) { MoveFocus(direction); _navDelay = .09; }
        }
        _navHeld = direction != Vector2I.Zero;
        _lastNav = direction;
        bool tab = Input.IsKeyPressed(Key.Tab) || Pad.Pressed(JoyButton.RightShoulder) || Pad.Pressed(JoyButton.LeftShoulder);
        if (tab && !_tabHeld) NextSection(Input.IsKeyPressed(Key.Shift) || Pad.Pressed(JoyButton.LeftShoulder) ? -1 : 1);
        _tabHeld = tab;
        if (accept && !_acceptHeld)
        {
            if (_focus < 3) NextSection(1);
            else if (_focus <= StartId) { StartPractice(); Audio.Instance?.PlayUiConfirm(); }
            else BackToHome();
        }
        _acceptHeld = accept;
        if (back && !_backHeld || Pad.MouseRightClick()) BackToHome();
        _backHeld = back;
        if (!Practicing && !_leaving && Pad.MouseClick()) HandleSelectionClick(Pad.MousePos());
    }

    private Rect2 ButtonRect(int id)
    {
        if (id < StartId)
        {
            int rows = (_slots.Length + 1) / 2;
            int index = id - 3;
            float pitch = Mathf.Min(48, 340f / rows);
            return new Rect2(404 + index % 2 * 424, 156 + index / 2 * pitch, 412, pitch - 6);
        }
        return id == StartId ? new Rect2(952, 568, 288, 52) : new Rect2(40, 664, 180, 36);
    }
    private Rect2 ChoiceRect(int group, int option) => group switch
    {
        0 => new Rect2(40 + option % 2 * 164, 151 + option / 2 * 45, 156, 40),
        1 => new Rect2(40 + option * 82, 269, 74, 30),
        _ => new Rect2(40 + option * (328f / PhaseCount), 329, 328f / PhaseCount - 8, 30),
    };
    private string PhaseLabel => _bossIndex == 3 ? $"{_phase + 1} / {BossMina.PhaseName(_phase)}"
        : _phase == 0 ? "第1形態" : "第2形態";

    private (string token, string label)[] SelectionHints() => new[] {
        (Pad.ShowKeyboard ? "↑↓←→" : "十字キー", "えらぶ"),
        (Pad.ShowKeyboard ? "Tab" : "LB/RB", "項目切替"),
        (Pad.ShowKeyboard ? "Z" : "A", _focus >= 3 && _focus <= StartId ? "練習開始" : "決定"),
        (Pad.ShowKeyboard ? "Esc" : "B", "もどる"), (Pad.PauseToken, "メニュー"),
    };

    private void DrawChoice(int group, int option, string label, bool selected)
    {
        var rect = ChoiceRect(group, option);
        bool focus = Pad.UsingMouse ? rect.HasPoint(Pad.MousePos()) : _focus == group && selected;
        Color accent = group == 0 ? CompanionDialogue.Accent(option switch { 0 => Job.Melee, 1 => Job.Heal, 2 => Job.Magic, _ => Job.Tank })
            : group == 1 ? option switch { 0 => UiKit.Purify, 2 => new Color("e8ad76"), 3 => UiKit.Kegare, _ => Accent } : Accent;
        UiKit.ExhibitPanel(_overlay, rect, accent, selected, 3);
        if (focus) UiKit.Box(_overlay, rect.Grow(2), Colors.Transparent, 6, UiKit.White, 1);
        if (group == 0)
        {
            UiKit.FaceAvatar(_overlay, rect.Position + new Vector2(25, 20), 16,
                option == 3 && _hideFinal ? null : _selectionAvatars[option], accent, false, 0);
            UiKit.Text(_overlay, UiKit.ZenBold, rect.Position + new Vector2(50, 8), label, 17, selected ? UiKit.White : UiKit.Text2);
        }
        else UiKit.Text(_overlay, group == 1 ? UiKit.Mono : UiKit.ZenBold,
            rect.Position + new Vector2(0, 4), label, 15, selected ? UiKit.White : UiKit.Text2, HorizontalAlignment.Center, rect.Size.X);
        if (selected) _overlay.DrawRect(new Rect2(rect.Position.X + 10, rect.End.Y - 3, rect.Size.X - 20, 2), accent);
    }

    private void DrawButton(int id, string label, bool selected = false)
    {
        Rect2 rect = ButtonRect(id);
        bool focus = Pad.UsingMouse ? rect.HasPoint(Pad.MousePos()) : _focus == id;
        UiKit.ExhibitPanel(_overlay, rect, Accent, selected || focus, id >= StartId ? 4 : 2);
        if (id == StartId)
        {
            UiKit.Box(_overlay, rect.Grow(-2), new Color(focus ? "c4f5eb" : "98ded4"), 6);
            _overlay.DrawLine(rect.Position + new Vector2(10, 3), new Vector2(rect.End.X - 10, rect.Position.Y + 3), new Color("e4fff5"), 1);
        }
        if (focus) UiKit.Box(_overlay, rect.Grow(2), Colors.Transparent, 6, UiKit.White, 1);
        bool technique = id >= 3 && id < StartId;
        if (technique)
        {
            UiKit.Text(_overlay, UiKit.Rajdhani, rect.Position + new Vector2(10, (rect.Size.Y - UiKit.Rajdhani.GetHeight(17)) / 2), $"{id - 2:00}", 17, selected ? Accent : UiKit.Text4);
            _overlay.DrawLine(rect.Position + new Vector2(34, 8), rect.Position + new Vector2(34, rect.Size.Y - 8), new Color(Accent, .18f), 1);
        }
        int size = technique ? 16 : id == StartId ? 20 : 16;
        float textWidth = rect.Size.X - (technique ? 62 : 32);
        while (size > 13 && UiKit.TextW(UiKit.ZenBold, label, size) > textWidth) size--;
        UiKit.Text(_overlay, UiKit.ZenBold, rect.Position + new Vector2(technique ? 46 : 16, (rect.Size.Y - UiKit.ZenBold.GetHeight(size)) / 2), label,
            size, id == StartId ? new Color("102a32") : selected ? Accent : UiKit.White,
            technique ? HorizontalAlignment.Left : HorizontalAlignment.Center, textWidth);
    }

    private void DrawUi()
    {
        UiKit.BeginDesign(_overlay);
        if (CurveEditor != null) CurveEditor.DrawTools(_overlay);
        else if (Practicing)
        {
            DrawTools();
        }
        else
        {
            UiKit.ExhibitBackdrop(_overlay, Accent, 98);
            UiKit.ExhibitPanel(_overlay, new Rect2(24, 112, 352, 520), Accent, false, 5);
            UiKit.ExhibitPanel(_overlay, new Rect2(388, 112, 868, 398), Accent, false, 5);
            UiKit.ExhibitPanel(_overlay, new Rect2(388, 526, 868, 106), Accent, false, 5);
            UiKit.Text(_overlay, UiKit.Mono, new Vector2(40, 12), "REFRAIN / PRACTICE", 13, Accent);
            UiKit.Text(_overlay, UiKit.ZenBlack, new Vector2(40, 30), "プラクティス", 32, UiKit.White);
            UiKit.Text(_overlay, UiKit.Zen, new Vector2(40, 76), $"操作：{_game.JobDef.CharacterName}　／　現在の装備・強化で練習します", 15, UiKit.Text2);
            UiKit.Text(_overlay, UiKit.Zen, new Vector2(880, 60), "記録・ポイントは保存されません", 14, UiKit.Text2, HorizontalAlignment.Right, 360);
            UiKit.Text(_overlay, UiKit.ZenBold, new Vector2(40, 124), "01  BOSSと条件", 18, UiKit.White);
            for (int i = 0; i < 4; i++) DrawChoice(0, i, i == 3 && _hideFinal ? "???" : BossRushRoot.BossNames[i], i == _bossIndex);
            UiKit.Text(_overlay, UiKit.ZenBold, new Vector2(40, 245), "難易度", 16, UiKit.Text2);
            for (int i = 0; i < 4; i++) DrawChoice(1, i, Difficulties[i], i == _difficulty);
            UiKit.Text(_overlay, UiKit.ZenBold, new Vector2(40, 305), "形態 / フェーズ", 16, UiKit.Text2);
            for (int i = 0; i < PhaseCount; i++) DrawChoice(2, i, _bossIndex == 3 ? $"{i + 1:00}" : $"第{i + 1}形態", i == _phase);
            if (!HiddenPreview) UiKit.Text(_overlay, UiKit.Zen, new Vector2(162, 306), PhaseLabel, 14, Accent, HorizontalAlignment.Right, 198);
            _overlay.DrawLine(new Vector2(40, 370), new Vector2(360, 370), new Color(Accent, .18f), 1);
            UiKit.Text(_overlay, UiKit.ZenBold, new Vector2(40, 377), "技プレビュー", 18, UiKit.White);
            _overlay.DrawRect(PreviewRect.Grow(1), new Color(Accent, .5f));
            if (HiddenPreview) UiKit.Text(_overlay, UiKit.Mono, PreviewRect.Position + new Vector2(0, PreviewRect.Size.Y / 2 - 12),
                "???", 20, UiKit.Text3, HorizontalAlignment.Center, PreviewRect.Size.X);
            UiKit.Text(_overlay, UiKit.ZenBold, new Vector2(404, 124), "02  練習する技", 18, UiKit.White);
            UiKit.Text(_overlay, UiKit.Mono, new Vector2(1100, 126), $"{_technique + 1:00} / {_slots.Length:00}", 14, Accent, HorizontalAlignment.Right, 140);
            for (int i = 0; i < _slots.Length; i++) DrawButton(i + 3, HiddenPreview ? "???" : _descriptions[i].Name, i == _technique);
            if (!HiddenPreview)
            {
                UiKit.Text(_overlay, UiKit.ZenBold, new Vector2(410, 532), _descriptions[_technique].Name, 18, Accent, HorizontalAlignment.Left, 516);
                UiKit.Multi(_overlay, UiKit.Zen, new Vector2(410, 562), _descriptions[_technique].Counterplay, 15, UiKit.Text2, 516, 3);
            }
            UiKit.Text(_overlay, UiKit.Zen, new Vector2(952, 540), $"{(HiddenPreview ? "???" : BossRushRoot.BossNames[_bossIndex])}  /  {Difficulties[_difficulty]}", 14, UiKit.Text2, HorizontalAlignment.Center, 288);
            DrawButton(StartId, "この技で練習を始める", true);
            DrawButton(BackId, "← スマホへ戻る");
            UiKit.HintBarPlate(_overlay, UiKit.HintAnchor, SelectionHints(), "もどる", "メニュー");
        }
        UiKit.EndDesign(_overlay);
    }

    public override void _ExitTree()
    {
        ResetPreviewClock();
        Active = null;
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        _game.ResetRun();
        _game.EndFury();
        _game.RestoreMeta(_snapshot);
        _game.Difficulty = _previousDifficulty;
        _game.SetContamination(_previousContamination);
        _game.AutoSaveEnabled = _previousAutoSave;
        _game.TutorialNoConsume = _previousNoConsume;
        _game.TrainingMode = _previousTraining;
    }
}
