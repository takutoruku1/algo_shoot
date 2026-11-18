using Godot;

public partial class DemoPilot : Node
{
    private double _seconds = 80;
    private double _elapsed;
    private AutoplayInput? _input;
    private GameManager _game = null!;
    private string? _recordStage;
    private double _finishAt;
    private bool _cleared;
    private int _lastLives = -1;
    private double _nextReport;
    private readonly System.Collections.Generic.HashSet<ulong> _recordedTechniques = new();
    private bool _measureClearTime;
    private int _restoredLives;

    public override void _Ready()
    {
        bool active = false, qa = false;
        int? bossIndex = null;
        var difficulty = GameManager.Diff.Easy;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--demo": active = true; break;
                case "--measure-clear-time": _measureClearTime = true; break;
                case "--qa": qa = true; break;
                case "--normal": difficulty = GameManager.Diff.Normal; break;
                case "--hard": difficulty = GameManager.Diff.Hard; break;
                case "--lunatic": difficulty = GameManager.Diff.Lunatic; break;
                case "--easy": difficulty = GameManager.Diff.Easy; break;
                case "--record-stage":
                    if (i + 1 < args.Length) _recordStage = args[++i];
                    break;
                case "--boss-index":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var index) || index < 0 || index > 3)
                        throw new System.ArgumentException("--boss-index requires an index from 0 to 3.");
                    bossIndex = index;
                    break;
                case "--seed":
                    if (i + 1 < args.Length && ulong.TryParse(args[++i], out var seed)) GD.Seed(seed);
                    break;
                case "--seconds":
                    if (i + 1 < args.Length && double.TryParse(args[i + 1], out var seconds)) _seconds = seconds;
                    break;
            }
        }
        if (!active || qa)
        {
            SetProcess(false);
            SetPhysicsProcess(false);
            return;
        }
        _game = GetNode<GameManager>("/root/Game");
        _game.Difficulty = difficulty;
        if (bossIndex is int selectedBoss) _game.PendingBossIndex = selectedBoss;
        _input = new AutoplayInput(GetTree(), _game, _recordStage == null ? 0.12 : 1.8);
        GD.Print($"[DemoPilot] active. recording {_seconds:0}s of autoplay. difficulty={_game.DiffName} job={_game.JobDef.CharacterName}");
        if (_measureClearTime)
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
                throw new System.InvalidOperationException("Clear-time measurement requires isolated QA saves.");
            GD.Print("[DemoPilot] MEASUREMENT: replenishes low lives; normal hits, invulnerability windows and attack inputs remain active. This is not a survival-clear demonstration.");
        }
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_recordStage != null)
        {
            foreach (var node in GetTree().GetNodesInGroup("boss_character_techniques"))
                if (node is BossCharacterTechnique attack && attack.SpellName.Length > 0
                    && _recordedTechniques.Add(attack.GetInstanceId()))
                {
                    GD.Print($"[DemoPilot] TECHNIQUE START t={_elapsed:0.00} name={attack.SpellName} evolved={attack.IsEnhanced}");
                    attack.Ended += ended => GD.Print($"[DemoPilot] TECHNIQUE END t={_elapsed:0.00} name={ended.SpellName} elapsed={ended.Elapsed:0.00} shots={ended.ShotsCreated} completed={ended.Completed}");
                }
            var player = GetTree().GetFirstNodeInGroup("player") as Player;
            if (_measureClearTime && player?.Lives == 1)
            {
                player.AddLife(1);
                _restoredLives++;
            }
            if (player != null && (player.Lives != _lastLives || _elapsed >= _nextReport))
            {
                _lastLives = player.Lives;
                _nextReport = _elapsed + 30;
                var boss = GetTree().GetFirstNodeInGroup("enemies") as Enemy;
                GD.Print($"[DemoPilot] t={_elapsed:0.00} lives={player.Lives} bombs={_game.Bombs} score={_game.Score} enemies={GetTree().GetNodesInGroup("enemies").Count} boss_hp={boss?.HpRatio:0.0000} bar_hp={boss?.BarHp}");
            }
            if (_finishAt == 0 && _game.GetBestTime(_recordStage, _game.Difficulty) is float clearTime)
            {
                _cleared = true;
                _finishAt = _elapsed + 4;
                GD.Print($"[DemoPilot] STAGE CLEAR t={_elapsed:0.00} stage={_recordStage} time={clearTime:0.00} lives={player?.Lives} hits={_game.RunHitCount} restored_lives={_restoredLives} score={_game.Score}");
            }
            else if (_finishAt == 0 && player?.Lives == 0)
            {
                _finishAt = _elapsed + 3;
                GD.Print($"[DemoPilot] GAME OVER t={_elapsed:0.00}");
            }
        }
        if (_elapsed < _seconds && (_finishAt == 0 || _elapsed < _finishAt)) return;
        _input!.Release();
        GD.Print("[DemoPilot] done. quitting to finalize movie.");
        GetTree().Quit(_recordStage != null && !_cleared ? 1 : 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finishAt > 0) _input!.Release();
        else _input!.Update(delta);
    }

    public override void _ExitTree() => _input?.Release();
}
