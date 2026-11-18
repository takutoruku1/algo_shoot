using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public partial class SteamAchievementsQa : Node
{
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
        GD.Print("[SteamAchievementsQA] PASS " + message);
    }

    public override void _Ready()
    {
        int exit = 0;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save directory");
            var stageAchievements = new Dictionary<string, string>
            {
                ["akari"] = "ACH_AKARI_CLEAR", ["koharu"] = "ACH_KOHARU_CLEAR", ["rei"] = "ACH_REI_CLEAR",
                ["final"] = "ACH_STORY_CLEAR", ["boss_rush"] = "ACH_BOSS_RUSH_CLEAR"
            };
            var players = new Dictionary<Job, string>
            {
                [Job.Tank] = "MINA", [Job.Melee] = "AKARI", [Job.Heal] = "KOHARU", [Job.Magic] = "REI"
            };
            var matchups = new HashSet<string>();
            var all = new HashSet<string> { SteamAchievements.FirstUpgrade };
            foreach (var stage in stageAchievements)
            foreach (GameManager.Diff difficulty in Enum.GetValues<GameManager.Diff>())
            foreach (Job job in Enum.GetValues<Job>())
            {
                var earned = SteamAchievements.ForClear(stage.Key, difficulty, job).ToHashSet();
                var expected = new HashSet<string> { stage.Value };
                if (job != Job.Tank && stage.Key is "akari" or "koharu" or "rei") expected.Add("ACH_COMPANION_CLEAR");
                if (difficulty >= GameManager.Diff.Hard) expected.Add("ACH_HARD_CLEAR");
                if (difficulty == GameManager.Diff.Lunatic) expected.Add("ACH_LUNATIC_CLEAR");
                if (stage.Key != "boss_rush")
                {
                    string boss = stage.Key == "final" ? "mina" : stage.Key;
                    string? matchup = difficulty switch
                    {
                        GameManager.Diff.Hard => $"ACH_HARD_{players[job]}_VS_{boss.ToUpperInvariant()}",
                        GameManager.Diff.Lunatic => $"ACH_LUNATIC_{players[job]}_VS_{boss.ToUpperInvariant()}",
                        _ => null
                    };
                    var solo = SteamAchievements.ForClear("boss_solo_" + boss, difficulty, job);
                    if (!solo.SequenceEqual(matchup == null ? Array.Empty<string>() : new[] { matchup })
                        || SteamAchievements.ForBossDefeat(boss, difficulty, job) != matchup)
                        throw new Exception($"Unexpected solo or rush award for {boss}/{difficulty}/{job}");
                    if (matchup != null)
                    {
                        if (!matchups.Add(matchup)) throw new Exception("Duplicate matchup " + matchup);
                        expected.Add(matchup);
                    }
                }
                if (!earned.SetEquals(expected))
                    throw new Exception($"Unexpected awards for {stage.Key}/{difficulty}/{job}");
                all.UnionWith(earned);
            }
            Check(matchups.Count == 32 && all.Count == 41, "32 distinct matchup achievements and all 9 original achievements");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            var unlocks = new HashSet<string>();
            Check(!SteamAchievements.ForCharacterUnlocks(game).Any(), "the starting character earns no unlock achievement");
            foreach (var (stage, id) in new[] { ("akari", "ACH_UNLOCK_AKARI"), ("koharu", "ACH_UNLOCK_KOHARU"), ("rei", "ACH_UNLOCK_REI") })
            {
                game.RecordClearTime("boss_solo_" + stage, GameManager.Diff.Hard, 30);
                Check(unlocks.SetEquals(SteamAchievements.ForCharacterUnlocks(game)), stage + " boss defeat alone cannot unlock a character");
                game.CompleteStage(stage);
                unlocks.Add(id);
                Check(unlocks.SetEquals(SteamAchievements.ForCharacterUnlocks(game)) && game.JustClearedStageWasNew, stage + " rescue unlocks exactly its character");
                game.CompleteStage(stage);
                Check(unlocks.SetEquals(SteamAchievements.ForCharacterUnlocks(game)) && !game.JustClearedStageWasNew, stage + " replay adds no unlock");
            }
            Check(game.SaveToSlot(1), "save unlocked characters");
            game.ResetPersistent();
            game.JobForcedByCmdline = true;
            Check(!SteamAchievements.ForCharacterUnlocks(game).Any(), "debug character selection earns no unlocks");
            game.JobForcedByCmdline = false;
            Check(game.LoadFromSlot(1) && unlocks.SetEquals(SteamAchievements.ForCharacterUnlocks(game)) && !game.JustClearedStageWasNew,
                "loading existing progress restores unlock awards without a new rescue");
            game.TrainingMode = true;
            Check(!SteamAchievements.ForCharacterUnlocks(game).Any(), "training earns no unlocks");
            game.TrainingMode = false;
            all.UnionWith(unlocks);
            foreach (string id in new[] { "boss_solo_unknown", "training", "boss_practice", "", "unknown" })
                Check(!SteamAchievements.ForClear(id, GameManager.Diff.Lunatic, Job.Melee).Any(), id + " does not award achievements");
            Check(SteamAchievements.AllowsSession(true, false, false, "5352870", Array.Empty<string>()), "normal Steam session is eligible");
            Check(!SteamAchievements.AllowsSession(false, false, false, "5352870", Array.Empty<string>()), "unsupported platform is disabled");
            Check(!SteamAchievements.AllowsSession(true, true, false, "5352870", Array.Empty<string>()), "editor cannot award Steam achievements");
            Check(!SteamAchievements.AllowsSession(true, false, true, "5352870", Array.Empty<string>()), "headless cannot award Steam achievements");
            Check(!SteamAchievements.AllowsSession(true, false, false, null, Array.Empty<string>()), "standalone does not contact Steam");
            Check(!SteamAchievements.AllowsSession(true, false, false, "480", Array.Empty<string>()), "another App ID is rejected");
            foreach (string argument in new[] { "--qa", "--autoplay", "--shot", "--boss", "--seed-records", "--job=melee", "--invincible" })
                Check(!SteamAchievements.AllowsSession(true, false, false, "5352870", new[] { argument }), argument + " cannot award achievements");

            var service = GetNode<SteamAchievements>("/root/SteamAchievements");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Check(!(bool)typeof(SteamAchievements).GetField("_initialized", flags)!.GetValue(service)!, "QA autoload has not initialized Steam");
            string ledger = ProjectSettings.GlobalizePath("user://achievement_qa.json");
            System.IO.File.WriteAllText(ledger, System.Text.Json.JsonSerializer.Serialize(all.Concat(new[]
                { "ACH_AKARI_CLEAR", "ACH_HARD_AKARI_VS_KOHARU", "ACH_NORMAL_AKARI_VS_KOHARU", "ACH_HARD_AKARI_VS_UNKNOWN", "INVALID" })));
            typeof(SteamAchievements).GetField("_ledgerPath", flags)!.SetValue(service, ledger);
            typeof(SteamAchievements).GetMethod("LoadLedger", flags)!.Invoke(service, null);
            var pending = (HashSet<string>)typeof(SteamAchievements).GetField("_pending", flags)!.GetValue(service)!;
            Check(all.Count == 44 && pending.SetEquals(all), "retry queue restores all 44 valid IDs and rejects unknown IDs");
            pending.Clear();
            game.RecordClearTime("final", GameManager.Diff.Lunatic, 30);
            game.CompleteStage("akari");
            SteamAchievements.RecordBossDefeat("koharu", GameManager.Diff.Hard, Job.Melee);
            Check(pending.Count == 0, "QA gameplay events cannot enter Steam queue");
            GD.Print("[SteamAchievementsQA] ALL PASS; no Steam achievements changed.");
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            exit = 1;
        }
        GetTree().Quit(exit);
    }
}
