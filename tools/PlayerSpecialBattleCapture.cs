using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class PlayerSpecialQa
{
    private async Task CaptureBattles()
    {
        var args = OS.GetCmdlineUserArgs();
        string mode = args.First(a => a.StartsWith("--battle="))[9..];
        bool bossBattle = mode == "boss";
        bool exposed = args.Contains("--exposed");
        var chapters = new List<object>();
        _notes.Add("Normal battle rules and enemy AI; automatic input uses internal hazard forecasts. No invincibility, teleports, HP edits, life refills, or cooldown resets during the battle.");
        foreach (var job in Jobs.All)
        {
            if (args.FirstOrDefault(a => a.StartsWith("--job=")) is { } selected && selected[6..] != job.Id.ToString()) continue;
            await DisposeScene();
            _game.SelectedJob = job.Id;
            _game.TrainingMode = false;
            _game.TrainingSetAllUpgrades(false);
            _game.TrainingSetUpgrade("n_slow", true);
            _game.TrainingSetUpgrade("n_dodge", true);
            _game.MarkIdleDialogSeen(PlayerSpecialTutorial.SeenKey(job.Id));
            _game.MarkIdleDialogSeen(StageTutorial.ChoiceSeenKey);
            _game.MarkIdleDialogSeen(StageTutorial.SkipKey);
            _game.MarkIdleDialogSeen("once_midboss_shield");
            _game.PendingBossIndex = 0;
            _game.SelectedEntry = GameManager.StageEntry.Start;
            _game.AutoAdvanceDialog = false;
            GD.Seed(7832 + (ulong)job.Id);
            _scene = GD.Load<PackedScene>(bossBattle ? BossRushRoot.SingleScenePath : "res://Akari.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(_scene);
            GetTree().CurrentScene = _scene;
            _world = _scene.GetNode<Node2D>("World");
            _player = _world.GetNode<Player>("Player");
            _hud = _scene.GetNode<Hud>("Hud");
            AddBattleCaption(job.Id, bossBattle, exposed);
            var pilot = new AutoplayInput(GetTree(), _game);
            int activeFrames = 0, peakBullets = 0;
            bool fastForward = false, readyFound = false;
            Enemy? boss = null;
            List<Enemy> enemies = new();
            int incoming = 0;
            for (int frame = 0; frame < 9000; frame++)
            {
                bool talking = Hud.BubblePaused;
                if (talking != fastForward) { KeyEvent(Key.Ctrl, talking); fastForward = talking; }
                pilot.Update(1d / 60, bombs: false);
                await Frames(1);
                CheckBattleAlive(job.Id, mode);
                enemies = GetTree().GetNodesInGroup("enemies").OfType<Enemy>().Where(e => !e.IsPurified).ToList();
                incoming = GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Count(b => b.Active && !b.LeadIn);
                peakBullets = Math.Max(peakBullets, incoming);
                boss = (_scene as BossRushRoot)?.Boss;
                if (talking || _hud.CinematicMode || !_player.CanProcess() || enemies.Count == 0) continue;
                activeFrames++;
                bool ready = bossBattle
                    ? boss != null && activeFrames > 360 && !boss.Transforming && (exposed ? boss.GaugeBodyExposed : boss.UnfoldersActive && incoming >= 4)
                    : activeFrames > 300 && enemies.Count >= 2 && incoming >= 2;
                if (!ready && !bossBattle && activeFrames > 900 && enemies.Count > 0) ready = true;
                if (ready && _game.SpecialReady && !_player.Dodging && !_player.ReturningToStart)
                {
                    readyFound = true;
                    break;
                }
            }
            KeyEvent(Key.Ctrl, false);
            fastForward = false;
            Check(readyFound, $"{mode}/{job.Id}: battle reaches the requested attack phase");
            if (job.Id == Job.Melee || !bossBattle && job.Id is Job.Heal or Job.Magic)
            {
                float range = job.Id switch { Job.Heal => 130, Job.Magic => 95, _ => 148 };
                for (int frame = 0; frame < 100; frame++)
                {
                    var target = _player.LockTarget ?? enemies.Where(e => IsInstanceValid(e) && !e.IsPurified)
                        .OrderBy(e => e.Position.DistanceSquaredTo(_player.Position)).FirstOrDefault();
                    if (target == null || !IsInstanceValid(target) || _player.Position.DistanceTo(target.GlobalPosition) < range) break;
                    pilot.Update(1d / 60, bombs: false, dodge: false);
                    AutoplayInput.Move(_player.Position.DirectionTo(target.GlobalPosition));
                    Input.FlushBufferedEvents();
                    await Frames(1);
                    CheckBattleAlive(job.Id, mode);
                }
            }
            pilot.Release();
            KeyEvent(Key.Shift, true);
            await Frames(2);
            Check(_game.SpecialReady && !_player.IsGameOver, $"{mode}/{job.Id}: normal battle reaches the special activation");
            long cast = (long)Engine.GetProcessFrames();
            int livesBefore = _player.Lives, hitsBefore = _game.RunHitCount;
            int enemiesBefore = enemies.Count;
            float? hpBefore = boss?.HpRatio;
            int? shieldBefore = boss?.GetChildren().OfType<Panel>().Sum(p => p.Ink);
            string phaseBefore = boss == null ? "road" : boss.GaugeBodyExposed ? "body" : "shield";
            KeyEvent(Key.C, true);
            await Frames(2);
            KeyEvent(Key.C, false);
            var special = _game.CurrentSpecial;
            Check(special != null, $"{mode}/{job.Id}: C activates in live combat");
            int finishedAt = -1;
            for (int frame = 0; frame < 900; frame++)
            {
                bool talking = Hud.BubblePaused;
                if (talking != fastForward) { KeyEvent(Key.Ctrl, talking); fastForward = talking; }
                if (special!.CutinActive) pilot.Release();
                else
                {
                    pilot.Update(1d / 60, bombs: false, dodge: job.Id != Job.Tank || !special.Active);
                    if (job.Id == Job.Tank && special.Active)
                    {
                        AutoplayInput.Move(Vector2.Zero);
                        Input.FlushBufferedEvents();
                    }
                    if (job.Id == Job.Heal && special.Active && special.Elapsed is > 2.7f and < 4.1f)
                    {
                        AutoplayInput.Move((special.Origin - _player.Position).LimitLength(20) / 20);
                        Input.FlushBufferedEvents();
                    }
                }
                if (frame is 20 or 65 or 125 or 185 or 235 or 320 or 390) await Shot($"{mode}_{job.CharacterId}_{frame:000}");
                await Frames(1);
                CheckBattleAlive(job.Id, mode);
                incoming = GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Count(b => b.Active && !b.LeadIn);
                peakBullets = Math.Max(peakBullets, incoming);
                if (!special.Active && finishedAt < 0) finishedAt = frame;
                if (finishedAt >= 0 && frame >= finishedAt + 100) break;
            }
            pilot.Release();
            KeyEvent(Key.Ctrl, false);
            Check(!special!.Active && special.AttackStages > 0,
                $"{mode}/{job.Id}: special finishes with active enemies (elapsed={special.Elapsed:F2}, cutin={special.CutinActive}, dialogue={Hud.BubblePaused}, cinematic={_hud.CinematicMode})");
            if (special.HitCount == 0) _notes.Add($"{mode}/{job.Id}: the special missed; recapture before presenting.");
            var chapter = new
            {
                character = job.Id.ToString(), character_name = job.CharacterName, mode,
                start = cast - 110, cast, end = (long)Engine.GetProcessFrames(),
                lives_before = livesBefore, lives_after = _player.Lives, hits_before = hitsBefore, hits_after = _game.RunHitCount,
                enemies_before = enemiesBefore, peak_bullets = peakBullets, special_hits = special.HitCount,
                special_stages = special.AttackStages, absorbed = special.AbsorbedCount, healed = special.Healed,
                boss_phase_before = phaseBefore, boss_hp_before = hpBefore, boss_hp_after = boss?.HpRatio,
                shield_ink_before = shieldBefore, shield_ink_after = boss?.GetChildren().OfType<Panel>().Sum(p => p.Ink),
                health_changes_include_ordinary_shots = true,
            };
            chapters.Add(chapter);
            System.IO.File.WriteAllText(_output + "/chapters.json", System.Text.Json.JsonSerializer.Serialize(chapters));
            GD.Print("[BATTLE SPECIAL] " + System.Text.Json.JsonSerializer.Serialize(chapter));
        }
    }

    private void CheckBattleAlive(Job job, string mode)
    {
        if (_player.IsGameOver || _player.Lives <= 0)
            throw new Exception($"{mode}/{job}: player died before the complete special could be captured; frame={Engine.GetProcessFrames()} hits={_game.RunHitCount}");
    }

    private void AddBattleCaption(Job job, bool boss, bool exposed)
    {
        var layer = new CanvasLayer { Layer = 30 };
        _scene!.AddChild(layer);
        var plate = new ColorRect { Position = new Vector2(Field.Left, 195), Size = new Vector2(318, 21),
            Color = new Color("101628", .88f), MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(plate);
        string title = $"{(boss ? "BOSS戦・あかり" : "雑魚戦")}  /  {Jobs.Get(job).CharacterName}  /  NORMAL";
        if (exposed) title += "  / 本体への攻撃";
        foreach (var (text, y) in new[] { (title, 195f), ("自動操作（内部予測）／敵AI・被弾判定あり", 205f) })
        {
            var label = new Label { Text = text, Position = new Vector2(Field.Left + 5, y), MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontOverride("font", UiKit.Zen);
            label.AddThemeFontSizeOverride("font_size", 7);
            layer.AddChild(label);
        }
    }
}
