using Godot;

public partial class BossRushRoot : Node2D
{
    public const string ScenePath = "res://BossRush.tscn";
    public const string SingleScenePath = "res://BossSolo.tscn";
    public const string RecordId = "boss_rush";
    public static readonly string[] BossNames = { "あかり", "こはる", "レイ", "ミナ" };
    internal static string DisplayName(int index, GameManager game) => index == 3 && !game.FinalBossRevealed ? "???" : BossNames[index];
    internal static readonly string[] BossIds = { "akari", "koharu", "rei", "mina" };
    private static readonly BgLayers.Layer[][] Backgrounds =
        { AkariRoot.BossLayers, KoharuRoot.BossLayers, ReiRoot.BossLayers, MinaRoot.BossLayers };

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;
    public Enemy? Boss { get; private set; }
    public int Round { get; private set; }
    public bool Completed { get; private set; }
    [Export] public bool SingleBoss { get; set; }
    public int BossCount => SingleBoss ? 1 : BossNames.Length;
    public int BossIndex => SingleBoss ? _selectedBoss : Round;
    public string RunRecordId => SingleBoss ? $"boss_solo_{BossIds[_selectedBoss]}" : RecordId;
    private int _selectedBoss;
    private GameManager _game = null!;
    private StageBackground _background = null!;
    private Control _overlay = null!;
    private FxLayer _fx = null!;
    private readonly RetryHold _retry = new();
    private double _elapsed, _interval = 1.4;
    private bool _exitHeld, _leaving;

    public override void _Ready()
    {
        _game = GetNode<GameManager>("/root/Game");
        _selectedBoss = _game.PendingBossIndex;
        AddToGroup("boss_rush");
        _game.ResetRun();
        _game.SelectedEntry = GameManager.StageEntry.Start;
        _game.BeginStageRun(RunRecordId);
        _game.SetStageTarget(BossCount);
        _game.SetContamination(0);
        _background = new StageBackground
        {
            Name = "StageBackground", LayerDefs = Backgrounds[BossIndex], BossLayerDefs = Backgrounds[BossIndex],
            LayerBossBehavior = BgLayers.BossBehavior.Illustrated, StartInBoss = true,
        };
        AddChild(_background);
        World = new Node2D { Name = "World", ProcessMode = ProcessModeEnum.Disabled };
        AddChild(World);
        _fx = new FxLayer { Name = "FxLayer" };
        World.AddChild(_fx);
        AddChild(new GameCamera { Name = "GameCamera" });
        AddChild(new FuryDial { Name = "FuryDial" });
        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.SnapToStart();
        Player.SetCorruption(0);
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        AddChild(new BubbleLayer { Name = "BubbleLayer", Hud = Hud });
        Hud.SetLives(Player.Lives);
        _overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 50 };
        _overlay.Draw += DrawProgress;
        AddChild(_overlay);
        PlayerSpecialTutorial.Start(this, Hud);
    }

    public override void _Process(double delta)
    {
        if (_leaving || Pad.UiBlocked(this)) return;
        _overlay.QueueRedraw();
        bool gameOver = Player.Lives <= 0;
        if (!Completed && _retry.Update(delta, Input.IsKeyPressed(Key.R), instant: gameOver))
        {
            Leave(SingleBoss ? SingleScenePath : ScenePath);
            return;
        }
        Hud.SetRetryHold(_retry.Progress);
        if (gameOver)
        {
            GameManager.HandleGameOverExit(this, Hud, ref _exitHeld);
            return;
        }
        GameManager.ClearGameOverChoice(Hud);
        _exitHeld = false;
        if (Hud.BubblePaused) return;
        if (!Completed)
        {
            _elapsed += delta;
            Hud.SetElapsed((float)_elapsed);
        }
        if (Boss == null)
        {
            _interval -= delta;
            if (_interval > 0) return;
            if (Completed)
            {
                Leave(SingleBoss ? BossSelectRoot.ScenePath : "res://Hub.tscn");
            }
            else SpawnBoss();
            return;
        }
        bool finished = Boss switch
        {
            BossAkari boss => boss.Finished,
            BossKoharu boss => boss.Finished,
            BossRei boss => boss.Finished,
            BossMina boss => boss.Finished,
            _ => false,
        };
        if (finished) FinishRound();
    }

    private void SpawnBoss()
    {
        World.ProcessMode = ProcessModeEnum.Inherit;
        Player.SnapToStart();
        _game.NotifyBossReached();
        if (BossIndex == 3) _game.RevealFinalBoss();
        Boss = BossIndex switch
        {
            0 => new BossAkari { Name = "BossAkari" },
            1 => new BossKoharu { Name = "BossKoharu" },
            2 => new BossRei { Name = "BossRei" },
            _ => new BossMina { Name = "BossMina" },
        };
        Boss.Position = new Vector2(300, 70);
        World.AddChild(Boss);
    }

    private void FinishRound()
    {
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        foreach (Node child in World.GetChildren())
            if (child != Player && child != _fx) child.QueueFree();
        World.ProcessMode = ProcessModeEnum.Disabled;
        Boss = null;
        Hud.HideBossBar();
        Hud.HideSpellCard();
        if (!SingleBoss) SteamAchievements.RecordBossDefeat(BossIds[BossIndex], _game.Difficulty, _game.SelectedJob);
        Round++;
        _interval = 1.4;
        if (Round < BossCount)
        {
            _background.CrossfadeLayersToBoss(BgLayers.BossBehavior.Illustrated, Backgrounds[Round], 1f);
            return;
        }
        Completed = true;
        _interval = 5;
        var time = _game.RecordClearTime(RunRecordId, _game.Difficulty, (float)_elapsed);
        var score = _game.RecordScore(RunRecordId, _game.Difficulty, _game.Score);
        _game.SettleStageScore();
        _game.AutoSave();
        Hud.ShowClearBanner(SingleBoss ? "BOSS CLEAR" : "BOSS RUSH CLEAR", (float)_elapsed, time.isBest, time.prev,
            _game.Score, score.isBest, score.prev);
    }

    private void Leave(string scene)
    {
        _leaving = true;
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        GameManager.FadeToScene(this, scene);
    }

    private void DrawProgress()
    {
        if (Boss != null || Completed) return;
        UiKit.BeginDesign(_overlay);
        UiKit.Text(_overlay, UiKit.Mono, new Vector2(Field.DLeft, 260), SingleBoss ? "BOSS SOLO" : $"BOSS RUSH  {Round + 1} / {BossCount}", 22,
            UiKit.Info, HorizontalAlignment.Center, Field.DWidth);
        UiKit.Text(_overlay, UiKit.ZenBold, new Vector2(Field.DLeft, 300), DisplayName(BossIndex, _game), 38,
            UiKit.White, HorizontalAlignment.Center, Field.DWidth);
        UiKit.EndDesign(_overlay);
    }

    public override void _ExitTree()
    {
        GameManager.ClearGameOverChoice(null);
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
    }
}
