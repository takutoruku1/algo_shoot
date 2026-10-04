using Godot;

public partial class DemoPilot : Node
{
    private double _seconds = 80;
    private double _elapsed;
    private AutoplayInput? _input;

    public override void _Ready()
    {
        bool active = false, qa = false;
        var difficulty = GameManager.Diff.Easy;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--demo": active = true; break;
                case "--qa": qa = true; break;
                case "--normal": difficulty = GameManager.Diff.Normal; break;
                case "--hard": difficulty = GameManager.Diff.Hard; break;
                case "--easy": difficulty = GameManager.Diff.Easy; break;
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
        var game = GetNode<GameManager>("/root/Game");
        game.Difficulty = difficulty;
        _input = new AutoplayInput(GetTree(), game);
        GD.Print($"[DemoPilot] active. recording {_seconds:0}s of autoplay. difficulty={game.DiffName}");
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < _seconds) return;
        _input!.Release();
        GD.Print("[DemoPilot] done. quitting to finalize movie.");
        GetTree().Quit();
    }

    public override void _PhysicsProcess(double delta) => _input!.Update(delta);

    public override void _ExitTree() => _input?.Release();
}
