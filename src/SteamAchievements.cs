using Godot;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

public partial class SteamAchievements : Node
{
    public const uint AppId = 5352870;
    public const string FirstUpgrade = "ACH_FIRST_UPGRADE";
    private static readonly Dictionary<Job, string> CharacterAchievements = new()
    {
        [Job.Melee] = "ACH_UNLOCK_AKARI", [Job.Heal] = "ACH_UNLOCK_KOHARU", [Job.Magic] = "ACH_UNLOCK_REI"
    };
    private static readonly Dictionary<(string boss, GameManager.Diff difficulty, Job job), string> BossAchievements = BuildBossAchievements();
    private static SteamAchievements? _instance;
    private readonly HashSet<string> _earned = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _storing = new(StringComparer.Ordinal);
    private Callback<UserStatsStored_t>? _stored;
    private bool _initialized;
    private ulong _account;
    private string _ledgerPath = "";
    private double _retry;

    public static bool AllowsSession(bool windows, bool editor, bool headless, string? steamAppId, string[] arguments)
        => windows && !editor && !headless && steamAppId == AppId.ToString() && arguments.Length == 0;

    private static Dictionary<(string, GameManager.Diff, Job), string> BuildBossAchievements()
    {
        var ids = new Dictionary<(string, GameManager.Diff, Job), string>();
        foreach (var diff in new[] { GameManager.Diff.Hard, GameManager.Diff.Lunatic })
        foreach (var player in Jobs.All)
        foreach (string boss in BossRushRoot.BossIds)
            ids.Add((boss, diff, player.Id), $"ACH_{diff}_{player.CharacterId}_VS_{boss}".ToUpperInvariant());
        return ids;
    }

    internal static string? ForBossDefeat(string bossId, GameManager.Diff difficulty, Job job)
        => BossAchievements.GetValueOrDefault((bossId, difficulty, job));

    internal static IEnumerable<string> ForCharacterUnlocks(GameManager game)
    {
        if (game.TrainingMode || GameManager.BossPracticeActive || game.JobForcedByCmdline) yield break;
        foreach (var (job, id) in CharacterAchievements)
            if (game.IsJobUnlocked(job)) yield return id;
    }

    public static void RecordCharacterUnlocks(GameManager game)
    {
        if (_instance == null) return;
        foreach (string id in ForCharacterUnlocks(game)) _instance.Earn(id);
    }

    public static IEnumerable<string> ForClear(string stageId, GameManager.Diff difficulty, Job job)
    {
        const string soloPrefix = "boss_solo_";
        string? boss = stageId switch
        {
            "akari" or "koharu" or "rei" => stageId,
            GameManager.FinalStageId => "mina",
            _ when stageId.StartsWith(soloPrefix, StringComparison.Ordinal) => stageId[soloPrefix.Length..],
            _ => null
        };
        if (boss != null && ForBossDefeat(boss, difficulty, job) is string matchup) yield return matchup;
        string? achievement = stageId switch
        {
            "akari" => "ACH_AKARI_CLEAR",
            "koharu" => "ACH_KOHARU_CLEAR",
            "rei" => "ACH_REI_CLEAR",
            GameManager.FinalStageId => "ACH_STORY_CLEAR",
            BossRushRoot.RecordId => "ACH_BOSS_RUSH_CLEAR",
            _ => null
        };
        if (achievement == null) yield break;
        yield return achievement;
        if (job != Job.Tank && stageId is "akari" or "koharu" or "rei")
            yield return "ACH_COMPANION_CLEAR";
        if (difficulty >= GameManager.Diff.Hard) yield return "ACH_HARD_CLEAR";
        if (difficulty == GameManager.Diff.Lunatic) yield return "ACH_LUNATIC_CLEAR";
    }

    public static void RecordClear(string stageId, GameManager.Diff difficulty, Job job)
    {
        if (_instance == null || GameManager.Instance?.TrainingMode != false) return;
        foreach (string id in ForClear(stageId, difficulty, job)) _instance.Earn(id);
    }

    public static void RecordBossDefeat(string bossId, GameManager.Diff difficulty, Job job)
    {
        if (_instance == null || GameManager.Instance?.TrainingMode != false) return;
        if (ForBossDefeat(bossId, difficulty, job) is string id) _instance.Earn(id);
    }

    public static void RecordUpgrade()
    {
        if (GameManager.Instance?.TrainingMode == false) _instance?.Earn(FirstUpgrade);
    }

    public override void _Ready()
    {
        SetProcess(false);
        if (!AllowsSession(OperatingSystem.IsWindows(), OS.HasFeature("editor"),
                DisplayServer.GetName() == "headless", System.Environment.GetEnvironmentVariable("SteamAppId"), OS.GetCmdlineUserArgs()))
            return;
        try
        {
            // Godot embeds managed assemblies; Steam's native DLL lives beside the Steam game payload.
            string nativePath = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "steam_api64.dll");
            NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly,
                (name, assembly, searchPath) => name == "steam_api64" ? NativeLibrary.Load(nativePath) : IntPtr.Zero);
            if (!SteamAPI.Init())
            {
                GD.PushWarning("[SteamAchievements] Steam initialization failed; gameplay remains available.");
                return;
            }
            _initialized = true;
            if (SteamUtils.GetAppID().m_AppId != AppId)
            {
                SteamAPI.Shutdown();
                _initialized = false;
                return;
            }
            _account = SteamUser.GetSteamID().m_SteamID;
            _ledgerPath = Startup.DataPath($"steam_achievements_{_account}.json");
            LoadLedger();
            _stored = Callback<UserStatsStored_t>.Create(OnStored);
            _instance = this;
            ProcessMode = ProcessModeEnum.Always;
            SetProcess(true);
            GD.Print("[SteamAchievements] Initialized for Refrain.");
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            GD.PushWarning($"[SteamAchievements] Native SDK unavailable: {error.GetType().Name}");
        }
    }

    private void LoadLedger()
    {
        if (!File.Exists(_ledgerPath)) return;
        try
        {
            var saved = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(_ledgerPath));
            if (saved == null) return;
            foreach (string id in saved)
            {
                if (!IsKnown(id)) continue;
                _earned.Add(id);
                _pending.Add(id);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            GD.PushWarning("[SteamAchievements] Could not read the local achievement queue.");
        }
    }

    private static bool IsKnown(string id) => id is "ACH_AKARI_CLEAR" or "ACH_KOHARU_CLEAR" or "ACH_REI_CLEAR"
        or "ACH_STORY_CLEAR" or FirstUpgrade or "ACH_COMPANION_CLEAR" or "ACH_BOSS_RUSH_CLEAR"
        or "ACH_HARD_CLEAR" or "ACH_LUNATIC_CLEAR" || BossAchievements.ContainsValue(id) || CharacterAchievements.ContainsValue(id);

    private void Earn(string id)
    {
        if (!_initialized || SteamUser.GetSteamID().m_SteamID != _account || !_earned.Add(id)) return;
        _pending.Add(id);
        try
        {
            string temporary = _ledgerPath + ".tmp";
            File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(_earned));
            File.Move(temporary, _ledgerPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning("[SteamAchievements] Could not save the local achievement queue.");
        }
        if (_storing.Count == 0) _retry = 0;
    }

    public override void _Process(double delta)
    {
        SteamAPI.RunCallbacks();
        if (SteamUser.GetSteamID().m_SteamID != _account) return;
        _retry -= delta;
        if (_pending.Count == 0 || _storing.Count > 0 || _retry > 0) return;
        _retry = 30;
        _storing.Clear();
        foreach (string id in _pending)
            if (SteamUserStats.SetAchievement(id)) _storing.Add(id);
        if (_storing.Count > 0 && !SteamUserStats.StoreStats()) _storing.Clear();
    }

    private void OnStored(UserStatsStored_t result)
    {
        if (result.m_nGameID != AppId || SteamUser.GetSteamID().m_SteamID != _account) return;
        if (result.m_eResult == EResult.k_EResultOK) _pending.ExceptWith(_storing);
        _storing.Clear();
        _retry = result.m_eResult == EResult.k_EResultOK ? 0 : 30;
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
        _stored?.Dispose();
        if (_initialized) SteamAPI.Shutdown();
    }
}
