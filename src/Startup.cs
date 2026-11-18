using Godot;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public partial class Startup : Node
{
    public const string ReleaseVersion = "3.015";
    private PlayerInstanceLock? _playerLock;
    public static string DataDirectory => OS.HasFeature("editor")
        || System.Environment.GetEnvironmentVariable("REFRAIN_QA_RUN") == "1"
        ? OS.GetUserDataDir()
        : Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "SaveData", "Progress");
    public static string DataPath(string file) => Path.Combine(DataDirectory, file).Replace('\\', '/');

    public override void _EnterTree()
    {
        // Steam Auto-Cloud already uses this relative folder; removing the launcher must not move saves.
        Error error = DirAccess.MakeDirRecursiveAbsolute(DataDirectory);
        if (error != Error.Ok)
        {
            GD.PushError($"Cannot create save directory: {DataDirectory} ({error})");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"[Startup] Refrain {ReleaseVersion}; saves: {DataDirectory}");
        if (PlayerInstanceLock.AllowsMultipleInstances(OS.HasFeature("editor"), OS.GetCmdlineUserArgs())) return;
        string profile = DataDirectory.Replace('\\', '/').ToUpperInvariant();
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
        _playerLock = new PlayerInstanceLock("Local\\Refrain.Player." + id);
        if (!_playerLock.Acquired) GetTree().Quit();
    }

    public override void _ExitTree()
    {
        _playerLock?.Dispose();
        _playerLock = null;
    }
}
