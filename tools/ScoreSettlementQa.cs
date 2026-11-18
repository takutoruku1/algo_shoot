using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class ScoreSettlementQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameManager _game = null!;
    private int _checks;

    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
        GD.Print($"[ScoreSettlementQA] PASS {message}");
    }

    private void Start(GameManager.Diff difficulty = GameManager.Diff.Normal, string stage = "akari")
    {
        _game.ResetPersistent();
        typeof(GameManager).GetField("_lastRunStage", Private)!.SetValue(_game, "");
        _game.Difficulty = difficulty;
        _game.ResetRun();
        _game.BeginStageRun(stage);
        _game.TrainingSetImpression(1000);
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _game = GetNode<GameManager>("/root/Game");
            _game.AutoSaveEnabled = false;
            Start();
            _game.AddScoreShard(10000);
            _game.AddBossHit(100);
            _game.AddGraze();
            _game.AddDodgeGraze();
            _game.AddBulletCleared();
            Check(_game.Score == 10185 && _game.Impression == 1000, "all score sources leave the wallet unchanged during combat");
            Check(_game.PendingStageImpression == 204, "conversion rounds once from the final score");
            Check(_game.SaveToSlot(1) && _game.Impression == 1000, "mid-stage save does not settle");
            _game.SettleStageScore();
            Check(_game.Impression == 1204 && _game.SettleStageScore() == 0, "exit pays exactly once");
            Check(_game.LoadFromSlot(1) && _game.Impression == 1000 && _game.SettleStageScore() == 0,
                "loading a mid-stage save cannot recover abandoned score as money");

            Start();
            for (int i = 0; i < 10; i++)
            {
                _game.AddScoreShard(10000);
                _game.NotifyBossReached();
                Check(_game.PrepareBossRetry(120), "boss retry accepted");
                long retained = _game.Score / 2;
                _game.ResetRun();
                _game.BeginStageRun("akari");
                Check(_game.Score == retained && _game.Impression == 1000, "repeated boss retries halve score without paying money");
            }
            long expected = (long)Math.Round(_game.Score / 100d * 0.4 * 2, MidpointRounding.AwayFromZero);
            Check(_game.SettleStageScore() == expected && _game.Impression == 1000 + expected,
                "exit converts only the retained score with the existing repeat discount");
            _game.ResetRun();
            _game.BeginStageRun("akari");
            Check(_game.Score == 0 && _game.SettleStageScore() == 0, "restart from the beginning discards the previous score");

            foreach (var (difficulty, reward) in new[] {
                (GameManager.Diff.Easy, 140L), (GameManager.Diff.Normal, 200L),
                (GameManager.Diff.Hard, 320L), (GameManager.Diff.Lunatic, 600L) })
            {
                Start(difficulty);
                _game.AddScoreShard(10000);
                Check(_game.SettleStageScore() == reward, $"{difficulty}: 10000 score converts to {reward}");
            }
            Start();
            _game.AddFollowers(10000);
            _game.BurningThisRun = true;
            _game.AddScoreShard(10000);
            Check(_game.SettleStageScore() == 180, "existing follower and burning modifiers apply once");

            Start();
            _game.AddScoreShard(10000);
            _game.RegisterStageClear();
            Check(_game.Impression == 1200, "clear pays score only, without the former fixed bonus");
            Check(_game.SettleStageScore() == 0, "return after clear cannot pay twice");
            Start();
            _game.AddScoreShard(10000);
            Check(_game.SaveToSlot(2, settleStage: true) && _game.Impression == 1200,
                "save-and-exit persists the settled balance");
            Check(_game.LoadFromSlot(2) && _game.Impression == 1200 && _game.SettleStageScore() == 0,
                "settled save reloads without another payout");
            Start();
            _game.AddScoreShard(10000);
            _game.TrainingMode = true;
            Check(_game.PendingStageImpression == 0 && !_game.SaveToSlot(2, settleStage: true), "training cannot pay or save rewards");
            _game.TrainingMode = false;
            Check(_game.PendingStageImpression == 200 && _game.Impression == 1000, "rejected save leaves settlement pending");
            _game.ResetPersistent();
            Check(_game.SettleStageScore() == 0, "new game discards pending settlement");

            var menu = GetNode<PauseMenu>("/root/PauseMenu");
            Start();
            _game.AddScoreShard(10000);
            menu.QuitOverride = () => { };
            typeof(PauseMenu).GetField("_slotForSave", Private)!.SetValue(menu, true);
            typeof(PauseMenu).GetField("_afterSave", Private)!.SetValue(menu, PauseMenu.QuitKind.Exit);
            typeof(PauseMenu).GetMethod("RunSlot", Private)!.Invoke(menu, new object[] { 3 });
            Check(menu.QuitRequested && _game.Impression == 1200, "pause menu save-and-quit settles before saving");
            Check(_game.LoadFromSlot(3) && _game.Impression == 1200, "pause menu saved the settled amount");
            menu.QuitOverride = null;

            foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle", "Stage0", "BossRush", "BossSolo" })
            {
                Start(stage: "qa");
                var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                root.ProcessMode = ProcessModeEnum.Disabled;
                _game.AddScoreShard(10000);
                if (scene == "Akari")
                {
                    _game.NotifyBossReached();
                    _game.PrepareBossRetry(120);
                    GameManager.FadeToScene(root, "res://Akari.tscn");
                    await QaSceneTransition.Wait(this, 4);
                    root = (Node2D)GetTree().CurrentScene;
                    root.ProcessMode = ProcessModeEnum.Disabled;
                    Check(_game.Score == 5000 && _game.Impression == 1000, "actual boss retry reload preserves half score without paying");
                }
                long reward = _game.PendingStageImpression;
                if (scene == "Akari")
                {
                    var act = typeof(PauseMenu).GetField("_confirmAct", Private)!;
                    act.SetValue(menu, Enum.Parse(act.FieldType, "Leave"));
                    typeof(PauseMenu).GetField("_confirmYes", Private)!.SetValue(menu, true);
                    typeof(PauseMenu).GetMethod("RunConfirm", Private)!.Invoke(menu, null);
                }
                else
                    typeof(GameManager).GetMethod("ExitToHub", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, new object?[] { root, _game, root.GetNode<Hud>("Hud") });
                Check(_game.Impression == 1000 + reward && _game.SettleStageScore() == 0, $"{scene}: game-over exit settles once");
                await QaSceneTransition.Wait(this, 4);
                Check(GetTree().CurrentScene is Hub, $"{scene}: exit reaches the hub");
                var hub = GetTree().CurrentScene;
                GetTree().CurrentScene = this;
                hub.Free();
            }
            GD.Print($"[ScoreSettlementQA] ALL PASS ({_checks} checks)");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[ScoreSettlementQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }
}
