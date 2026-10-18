using Godot;
using System;
using System.Collections.Generic;

public partial class TrainingRoot : Node2D
{
    public const int ScreenWidth = 384, ScreenHeight = 216;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;
    private const double DpsWindow = 3;
    private const float PanelW = 440, PanelX = W - PanelW - 16;
    private const float ListTop = 356, ListBottom = 652, RowH = 42;
    private static readonly Vector2 TargetPosition = new(224, 122);
    private static readonly Job[] TrialJobs = { Job.Melee, Job.Heal, Job.Magic, Job.Tank };

    private GameManager _game = null!;
    private BulletPool _pool = null!;
    private Node2D _world = null!;
    private Player _player = null!;
    private TrainingDummy _dummy = null!;
    private Control _uiLayer = null!;
    private GameManager.MetaSnapshot _snapshot = null!;
    private bool _restored, _prevAutoSave, _prevNoConsume, _prevTraining;
    private ProcessModeEnum _prevPoolMode;
    private readonly Queue<(double time, int damage)> _damageLog = new();
    private double _elapsed;
    private long _shotDamage, _bombDamage;
    private int _hits, _lastDamage;
    private int _linePower, _speedPower;
    private float _distance = 132;
    private bool _panelOpen, _backHeld;
    private float _scroll;
    private readonly HashSet<Key> _keys = new();
    private readonly List<(Rect2 rect, Action action)> _buttons = new();
    private static readonly string[] TrainEnter =
    { "練習に付き合っていただけるのですね。……はい、準備できております。", "的が一つきり。ずいぶん寂しい射撃場ですこと。", "整備の時間です。……こういう時間、嫌いではありません。" };

    private static readonly string[] TrainShoot =
    { "……いい音です。今のは、手応えがありました。", "数字が増えるのは、少し気持ちがいいですね。単純です、わたくし。", "この的、文句ひとつ言いませんね。……えらいです。", "肩慣らしのつもりが、つい本気に。……いけませんね。" };

    private static readonly string[] TrainIdle =
    { "……ご主人様? 手が、止まっておりますよ。", "お休みですか。どうぞ、ゆっくり。逃げませんので、わたくしも、的も。", "静かですね。……たまには、こういう日もいいものです。", "この時間、記録には残らないんですよ。もったいない話です。", "暇なので、的の数を数えていました。……一つでした。" };

    private string _talk = "";
    private double _talkTime;
    private double _idleTime;
    private bool _idleFired;

    public override void _Ready()
    {
        _game = GetNode<GameManager>("/root/Game");
        _pool = GetNode<BulletPool>("/root/Pool");
        _snapshot = _game.SnapshotMeta();
        _prevAutoSave = _game.AutoSaveEnabled;
        _prevNoConsume = _game.TutorialNoConsume;
        _prevTraining = _game.TrainingMode;
        _prevPoolMode = _pool.ProcessMode;
        _game.AutoSaveEnabled = false;
        _game.TutorialNoConsume = true;
        _game.TrainingMode = true;
        _game.SetContamination(0);
        Audio.Instance?.Music(Audio.Instance.BgmTraining);

        AddChild(new ColorRect
        {
            Color = new Color(0.08f, 0.09f, 0.14f), Size = new Vector2(ScreenWidth, ScreenHeight),
            ZIndex = -100, MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _world.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });
        _dummy = new TrainingDummy { Name = "TrainingDummy", Position = TargetPosition, OnDamaged = OnDummyDamaged };
        _world.AddChild(_dummy);
        ResetTrial();
        _uiLayer = new Control { Name = "UiLayer", MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 50 };
        _uiLayer.Draw += DrawUi;
        AddChild(_uiLayer);
        ShowTalk();
    }

    private void ShowTalk()
    {
        ShowTrainingTalk(CompanionDialogue.Menu.TrainEnter, TrainEnter);
    }

    private void ShowTrainingTalk(CompanionDialogue.Menu scene, string[] minaLines)
    {
        _talk = _game.SelectedJob == Job.Tank ? minaLines[GD.RandRange(0, minaLines.Length - 1)]
            : CompanionDialogue.MenuText(_game.SelectedJob, scene);
        _talkTime = 5;
    }

    private void OnDummyDamaged(int damage, bool bomb)
    {
        if (_panelOpen) return;
        if (bomb) { _bombDamage += damage; return; }
        _damageLog.Enqueue((_elapsed, damage));
        _shotDamage += damage;
        _lastDamage = damage;
        _hits++;
        if (_talkTime <= 0 && GD.Randf() < 0.1f)
            ShowTrainingTalk(CompanionDialogue.Menu.TrainShoot, TrainShoot);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_panelOpen || _hits == 0) return;
        _elapsed += delta;
        while (_damageLog.Count > 0 && _damageLog.Peek().time < _elapsed - DpsWindow)
            _damageLog.Dequeue();
    }

    private float DummyDps()
    {
        if (_elapsed <= 0) return 0;
        long sum = 0;
        foreach (var sample in _damageLog) sum += sample.damage;
        return (float)(sum / Math.Min(_elapsed, DpsWindow));
    }

    private bool Press(Key key)
    {
        if (!Input.IsKeyPressed(key)) { _keys.Remove(key); return false; }
        return _keys.Add(key);
    }

    public override void _Process(double delta)
    {
        _talkTime -= delta;
        bool acting = _panelOpen || Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down") != Vector2.Zero
            || Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.C)
            || Input.IsKeyPressed(Key.Shift) || Pad.MouseDown() || Pad.UsingPad;
        if (acting) { _idleTime = 0; _idleFired = false; }
        else if ((_idleTime += delta) >= 7 && !_idleFired)
        {
            _idleFired = true;
            ShowTrainingTalk(CompanionDialogue.Menu.TrainIdle, TrainIdle);
        }
        _uiLayer.QueueRedraw();
        bool tab = Press(Key.Tab), reset = Press(Key.R), owned = Press(Key.Backslash);
        bool none = Press(Key.Bracketleft), all = Press(Key.Bracketright);
        bool up = Press(Key.Pageup), down = Press(Key.Pagedown);
        bool back = Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.Backspace) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld;
        _backHeld = back;
        if (Pad.UiBlocked(this)) return;
        if (backEdge)
        {
            if (_panelOpen) SetPanelOpen(false);
            else ExitToShop();
            return;
        }
        if (tab) SetPanelOpen(!_panelOpen);
        if (reset) ResetTrial();
        if (owned) ApplyPreset(0);
        if (none) ApplyPreset(1);
        if (all) ApplyPreset(2);
        if (_panelOpen)
        {
            float wheel = Pad.WheelDelta();
            if (Pad.MousePos().X >= PanelX || up || down)
                _scroll = Mathf.Clamp(_scroll - wheel * RowH + (down ? 4 * RowH : 0) - (up ? 4 * RowH : 0), 0, MaxScroll());
        }
        if (Pad.MouseClick())
            foreach (var (rect, action) in _buttons)
                if (rect.HasPoint(Pad.MousePos())) { action(); Audio.Instance?.PlayUiMove(); break; }
    }

    private void SetPanelOpen(bool open)
    {
        _panelOpen = open;
        _world.ProcessMode = open ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        _pool.ProcessMode = open ? ProcessModeEnum.Disabled : _prevPoolMode;
        Pad.ReleaseMouseFollow();
        if (!open) ResetTrial();
    }

    private void ApplyPreset(int preset)
    {
        if (preset == 0)
        {
            foreach (var def in GameManager.Upgrades)
                _game.TrainingSetUpgrade(def.Id, _snapshot.Upgrades.TryGetValue(def.Id, out int level) && level > 0);
        }
        else _game.TrainingSetAllUpgrades(preset == 2);
        ResetTrial();
    }

    private void SetUpgradeDepth(int count)
    {
        for (int i = 0; i < GameManager.Upgrades.Length; i++)
            _game.TrainingSetUpgrade(GameManager.Upgrades[i].Id, i < count);
        ResetTrial();
    }

    private void SetJob(Job job)
    {
        _game.SelectedJob = job;
        ResetTrial();
        ShowTalk();
    }

    private void SetPower(PowerKind kind, int level)
    {
        if (kind == PowerKind.Line) _linePower = level;
        else if (kind == PowerKind.Speed) _speedPower = level;
        ResetTrial();
    }

    private void SetDistance(float distance)
    {
        _distance = distance;
        ResetTrial();
    }

    private void ResetTrial()
    {
        _pool.DespawnPlayerBullets();
        if (_player != null)
        {
            _world.RemoveChild(_player);
            _player.QueueFree();
        }
        _game.ResetRun();
        _player = new Player { Name = "Player", StartPosition = TargetPosition - new Vector2(_distance, 0) };
        _world.AddChild(_player);
        _player.SnapToStart();
        _player.SetCorruption(0);
        for (int i = 0; i < _linePower; i++) _player.ApplyPowerup(PowerKind.Line);
        for (int i = 0; i < _speedPower; i++) _player.ApplyPowerup(PowerKind.Speed);
        _dummy.ResetMeasurement();
        _damageLog.Clear();
        _elapsed = 0;
        _shotDamage = _bombDamage = 0;
        _hits = _lastDamage = 0;
        Pad.ReleaseMouseFollow();
    }

    private void ExitToShop()
    {
        _world.ProcessMode = ProcessModeEnum.Disabled;
        SetProcess(false);
        SetPhysicsProcess(false);
        RestoreMeta();
        Audio.Instance?.PlayUiCancel();
        GameManager.FadeToScene(this, "res://Shop.tscn");
    }

    private void RestoreMeta()
    {
        if (_restored) return;
        _restored = true;
        _pool.DespawnAll();
        _pool.ProcessMode = _prevPoolMode;
        _game.RestoreMeta(_snapshot);
        _game.AutoSaveEnabled = _prevAutoSave;
        _game.TutorialNoConsume = _prevNoConsume;
        _game.TrainingMode = _prevTraining;
    }

    public override void _ExitTree() => RestoreMeta();

    private float MaxScroll() => Mathf.Max(0, GameManager.Upgrades.Length * RowH - (ListBottom - ListTop));

    private void DrawUi()
    {
        UiKit.BeginDesign(_uiLayer);
        _buttons.Clear();
        UiKit.Text(_uiLayer, UiKit.ZenBlack, new Vector2(40, 26), "トレーニング / DPSチェッカー", 26, UiKit.White);
        UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(40, 68), "購入済みの強化から試せます。試用したキャラ・強化は保存されません。", 14, UiKit.Text2);
        DrawMeter();
        DrawTalk();
        DrawControls();
        Button(new Rect2(40, H - 52, 180, 34), "ショップへ戻る  Esc", ExitToShop);
        Button(new Rect2(234, H - 52, 172, 34), "位置・計測リセット  R", ResetTrial);
        if (_panelOpen) DrawSettings();
        Button(new Rect2(1064, 26, 200, 34), _panelOpen ? "設定を閉じて計測  Tab" : "測定設定  Tab", () => SetPanelOpen(!_panelOpen));
        UiKit.EndDesign(_uiLayer);
    }

    private void DrawMeter()
    {
        var box = new Rect2(408, 108, 370, 208);
        UiKit.Box(_uiLayer, box, new Color(0.04f, 0.05f, 0.09f, 0.96f), 12, new Color(UiKit.Info, 0.45f));
        Text(426, 120, _panelOpen ? "設定中 / 計測停止" : _hits == 0 ? "初回命中から計測開始" : "射撃DPS / 直近3秒", 14, UiKit.Text2);
        UiKit.Text(_uiLayer, UiKit.Mono, new Vector2(426, 142), DummyDps().ToString("N1"), 40, UiKit.PurifyHi);
        float average = _elapsed > 0 ? (float)(_shotDamage / _elapsed) : 0;
        Text(426, 196, $"全体平均 {average:N1} / 計測 {_elapsed:0.0} 秒", 14, UiKit.Text2);
        Text(426, 220, $"射撃累計 {_shotDamage:N0} / {_hits:N0} HIT / 直近 {_lastDamage}", 13, UiKit.Text2);
        Text(426, 244, $"ボム累計 {_bombDamage:N0}（射撃DPSには含めません）", 12, UiKit.Text3);
        Text(426, 278, $"{_game.JobDef.CharacterName} / {_game.ShotModeName(_game.SelectedShotMode)} / 火力 ×{_player.ShotPowerPercent / 100f:0.00}", 14, UiKit.Info);
        Text(408, 328, "ボス本体への火力 / シールドなし・ダメージ総量の上限なし", 12, UiKit.Text2);
        float distance = _player.GlobalPosition.DistanceTo(_dummy.GlobalPosition);
        string bonus = distance <= Jobs.CloseRange && _game.JobDef.CritEnabled ? "近距離ボーナス"
            : distance > Jobs.FarRange && _game.JobDef.FarMult > 1 ? "遠距離ボーナス" : "距離ボーナスなし";
        Text(408, 530, $"距離 {distance:0} / {bonus}", 14, UiKit.Info);
        Text(408, 556, $"フォロワー補正 ×{_game.FollowerPowerMul:0.00}（本番と同じ）", 13, UiKit.Text2);
    }

    private void DrawTalk()
    {
        if (_talkTime <= 0) return;
        var box = new Rect2(40, 112, 340, 112);
        UiKit.Box(_uiLayer, box, new Color(0.04f, 0.05f, 0.09f, 0.9f), 12);
        Text(56, 126, _game.SelectedJob == Job.Tank ? "ミナ" : $"ミナ / {_game.JobDef.CharacterName}", 13, UiKit.Info);
        UiKit.Multi(_uiLayer, UiKit.Zen, new Vector2(56, 152), _talk, 14, UiKit.Text2, 308, 3);
    }

    private void DrawControls()
    {
        Text(40, 598, Pad.UsingPad ? "移動：Lスティック / ロック：RB / 溜め打ち：Y長押し / ボム：X / 回避：L3 / 集中：LB"
            : "移動：矢印 / ロック：Shift・左クリック / 溜め打ち：Z長押し / ボム：X / 回避：Space / 集中：C", 13, UiKit.Text2);
        Text(40, 624, "ショットはオート / 設定：Tab / 未強化：[ / 最大強化：] / 購入済み：\\ / リセット：R", 13, UiKit.Text3);
    }

    private void DrawSettings()
    {
        UiKit.Box(_uiLayer, new Rect2(PanelX, 76, PanelW, 604), new Color(0.04f, 0.05f, 0.09f), 12, new Color(UiKit.Info, 0.4f));
        Text(PanelX + 18, 88, "測定条件（無料・試用）", 18, UiKit.White);
        Text(PanelX + 18, 115, "閉じると初期位置から新しく計測します。", 12, UiKit.Text3);
        for (int i = 0; i < TrialJobs.Length; i++)
        {
            Job job = TrialJobs[i];
            Button(new Rect2(PanelX + 18 + i * 102, 140, 96, 30), Jobs.Get(job).CharacterName, () => SetJob(job), _game.SelectedJob == job);
        }
        string[] presets = { "購入済みに戻す", "未強化 [", "最大強化 ]" };
        for (int i = 0; i < presets.Length; i++)
        {
            int preset = i;
            Button(new Rect2(PanelX + 18 + i * 136, 180, 130, 30), presets[i], () => ApplyPreset(preset));
        }
        DrawPowerRow(PowerKind.Line, "火力アイテム", _linePower, 220);
        DrawPowerRow(PowerKind.Speed, "移動アイテム", _speedPower, 258);
        Text(PanelX + 18, 302, "開始距離", 13, UiKit.Text2);
        float[] ranges = { Jobs.CloseRange * 0.75f, (Jobs.CloseRange + Jobs.FarRange) * 0.5f, Jobs.FarRange + 12 };
        string[] names = { "近距離", "中距離", "遠距離" };
        for (int i = 0; i < ranges.Length; i++)
        {
            float range = ranges[i];
            Button(new Rect2(PanelX + 150 + i * 88, 296, 82, 30), names[i], () => SetDistance(range), Mathf.IsEqualApprox(_distance, range));
        }
        Text(PanelX + 18, 335, $"ショップ強化 / 全{GameManager.Upgrades.Length}段（クリックした段まで適用）", 13, UiKit.Info);
        float y = ListTop - _scroll;
        for (int i = 0; i < GameManager.Upgrades.Length; i++, y += RowH)
        {
            if (y < ListTop || y + RowH > ListBottom) continue;
            var def = GameManager.Upgrades[i];
            bool on = _game.Has(def.Id);
            int depth = i + 1;
            var rect = new Rect2(PanelX + 18, y, PanelW - 42, RowH - 3);
            UiKit.Box(_uiLayer, rect, new Color(UiKit.Purify, on ? 0.18f : 0.04f), 4,
                new Color(UiKit.Purify, on ? 0.6f : 0.2f));
            Text(rect.Position.X + 9, y + 2, $"{i + 1:00}  {def.Name}" + (on ? "  ON" : ""), 14, on ? UiKit.White : UiKit.Text2);
            Text(rect.Position.X + 34, y + 21, def.Desc, 12, UiKit.Text3);
            _buttons.Add((rect, () => SetUpgradeDepth(depth)));
        }
        float max = MaxScroll();
        if (max > 0)
        {
            float height = ListBottom - ListTop;
            float thumb = height * height / (height + max);
            UiKit.Box(_uiLayer, new Rect2(W - 27, ListTop + _scroll / max * (height - thumb), 4, thumb), UiKit.Info, 2);
        }
        Text(PanelX + 18, 657, "ホイール / PageUp・PageDown で下の強化を表示", 11, UiKit.Text3);
    }

    private void DrawPowerRow(PowerKind kind, string label, int current, float y)
    {
        Text(PanelX + 18, y + 6, label, 13, UiKit.Text2);
        for (int i = 0; i <= Player.PowerLevelCap; i++)
        {
            int level = i;
            Button(new Rect2(PanelX + 150 + i * 88, y, 82, 30), $"Lv.{i}", () => SetPower(kind, level), current == i);
        }
    }

    private void Text(float x, float y, string text, int size, Color color)
        => UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(x, y), text, size, color);

    private void Button(Rect2 rect, string label, Action action, bool selected = false)
    {
        bool hover = rect.HasPoint(Pad.MousePos());
        UiKit.Box(_uiLayer, rect, new Color(UiKit.Info, selected ? 0.32f : hover ? 0.22f : 0.1f), 6, new Color(UiKit.Info, selected ? 0.9f : 0.4f));
        float width = UiKit.TextW(UiKit.ZenBold, label, 12);
        UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(rect.GetCenter().X - width / 2, rect.Position.Y + 7), label, 12, UiKit.White);
        _buttons.Add((rect, action));
    }
}
