using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class SteamGameplayCapture : Node
{
    private AutoplayInput? _pilot;
    private GameManager _game = null!;
    private Node _battle = null!;
    private double _elapsed, _nextReport;
    private bool _recording;

    public override async void _Ready()
    {
        int exit = 1;
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
                throw new InvalidOperationException("Capture requires an isolated QA save directory.");
            var args = OS.GetCmdlineUserArgs();
            string scene = args.FirstOrDefault(a => a.StartsWith("--capture-scene="))?[16..] ?? "BossSolo";
            int boss = int.Parse(args.FirstOrDefault(a => a.StartsWith("--capture-boss="))?[15..] ?? "0");
            double seconds = double.Parse(args.FirstOrDefault(a => a.StartsWith("--capture-seconds="))?[18..] ?? "40",
                System.Globalization.CultureInfo.InvariantCulture);
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = GameManager.Diff.Normal;
            _game.PendingBossIndex = boss;
            GD.Seed(5352870);
            _pilot = new AutoplayInput(GetTree(), _game, 1.8);
            Input.UseAccumulatedInput = false;
            await Frames(1);
            _battle = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate();
            GetTree().Root.AddChild(_battle);
            GetTree().CurrentScene = _battle;
            GD.Print($"[TRAILER] scene={scene} boss={boss} seconds={seconds} difficulty=NORMAL");
            GD.Print("[TRAILER] Normal game rules; synthetic input with internal hazard forecasts; no invincibility, life refill, HP changes, teleport, or attack skipping. Not a human playtest.");
            _recording = true;
            while (_elapsed < seconds) await Frames(1);
            _recording = false;
            _pilot.Release();
            GD.Print($"[TRAILER] COMPLETE seconds={_elapsed:0.000} hits={_game.RunHitCount} score={_game.Score}");
            exit = 0;
        }
        catch (Exception error) { GD.PushError($"[TRAILER] ERROR {error}"); }
        _recording = false;
        _pilot?.Release();
        if (IsInstanceValid(_battle)) _battle.QueueFree();
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
        { child.Stop(); child.Stream = null; }
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GetTree().Quit(exit);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_recording) return;
        _elapsed += delta;
        _pilot!.Update(delta);
        if (_elapsed < _nextReport) return;
        _nextReport = _elapsed + 5;
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        GD.Print($"[TRAILER] t={_elapsed:0.00} lives={player?.Lives} hits={_game.RunHitCount} bombs={_game.Bombs} score={_game.Score}");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
