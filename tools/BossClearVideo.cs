using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private bool _clearBattle, _clearDriving;
    private BossRushRoot _clearRoot = null!;
    private double _clearElapsed, _clearChargeWait, _clearPulseWait;
    private ulong _clearAttackId;

    private async Task RunClearBattle()
    {
        _clearBattle = true;
        int exit = 1;
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
                throw new InvalidOperationException("An isolated QA save directory is required.");
            var args = OS.GetCmdlineUserArgs();
            int previewSeconds = args.FirstOrDefault(a => a.StartsWith("--preview-seconds=")) is { } previewArg
                ? int.Parse(previewArg[18..]) : 0;
            _scene = args.First(a => a.StartsWith("--boss="))[7..];
            int index = Array.IndexOf(new[] { "Akari", "Koharu", "Rei", "MinaBattle" }, _scene);
            if (index < 0) throw new ArgumentException("Unknown boss.");
            _output = ProjectSettings.GlobalizePath(args.First(a => a.StartsWith("--output="))[9..]);
            Directory.CreateDirectory(_output);
            _game = GetNode<GameManager>("/root/Game");
            _game.Difficulty = GameManager.Diff.Hard;
            _game.PendingBossIndex = index;
            _game.AutoSaveEnabled = false;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            _pool = GetNode<BulletPool>("/root/Pool");
            _pilot = new AutoplayInput(GetTree(), _game, 1.5);
            _survival = true;
            GD.Seed(args.FirstOrDefault(a => a.StartsWith("--seed=")) is { } seed ? ulong.Parse(seed[7..]) : 4137);
            Engine.TimeScale = 1;
            Engine.PhysicsTicksPerSecond = 60;
            Input.UseAccumulatedInput = false;
            await Frames(1);
            _clearRoot = GD.Load<PackedScene>(BossRushRoot.SingleScenePath).Instantiate<BossRushRoot>();
            GetTree().Root.AddChild(_clearRoot);
            GetTree().CurrentScene = _clearRoot;
            _player = _clearRoot.Player;
            _hud = _clearRoot.Hud;
            _clearDriving = true;
            GD.Print($"[CLEAR] START boss={_scene} difficulty=HARD player={_game.JobDef.CharacterName} lives={_player.Lives} bombs={_game.Bombs} upgrades={string.Join(',', _game.ColumnOwnedIds())}");
            GD.Print("[CLEAR] CONDITIONS normal game rules; synthetic input with internal hazard forecasting; no HP changes, invincibility override, life refill, teleport, or attack skipping. Not a human readability test.");
            int lives = _player.Lives, hits = 0;
            double next = 0;
            var observations = new List<object>();
            while (_clearElapsed < (previewSeconds > 0 ? previewSeconds : 1800) && _player.Lives > 0 && !_clearRoot.Completed)
            {
                await Frames(1);
                if (_player.Lives != lives || _game.RunHitCount != hits || _clearElapsed >= next)
                {
                    lives = _player.Lives;
                    hits = _game.RunHitCount;
                    next = _clearElapsed + 15;
                    var boss = _clearRoot.Boss;
                    GD.Print($"[CLEAR] t={_clearElapsed:0.00} lives={lives} hits={hits} bombs={_game.Bombs} hp={boss?.HpRatio:0.0000} attack={_easyAttack?.MechanicKey} position={_player.Position}");
                    observations.Add(new { t = _clearElapsed, battle_seconds = Read<double>(_clearRoot, "_elapsed"), lives, hits,
                        bombs = _game.Bombs, hp = boss?.HpRatio, bar_hp = boss?.BarHp, bars = boss?.TotalBars,
                        attack = _easyAttack?.MechanicKey });
                }
            }
            _clearDriving = false;
            _pilot.Release();
            foreach (Key key in new[] { Key.Z, Key.Shift, Key.X, Key.C, Key.Space }) ClearKey(key, false);
            Input.FlushBufferedEvents();
            bool cleared = _clearRoot.Completed;
            bool previewed = previewSeconds > 0 && _clearElapsed >= previewSeconds && _player.Lives > 0 && !cleared;
            var result = new { boss = _scene, difficulty = "HARD", player = _game.JobDef.CharacterName,
                cleared, preview = previewed, seconds = _clearElapsed, battle_seconds = Read<double>(_clearRoot, "_elapsed"),
                lives = _player.Lives, hits = _game.RunHitCount,
                score = _game.Score, upgrades = _game.ColumnOwnedIds(),
                conditions = "Copied save upgrades; normal game rules; automatic input using internal hazard forecasts; no invincibility override, life refill, HP changes, teleport, or attack skipping.", observations };
            File.WriteAllText(Path.Combine(_output, "clear_result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[CLEAR] {(cleared ? "SUCCESS" : previewed ? "PREVIEW" : "FAILED")} boss={_scene} seconds={_clearElapsed:0.00} lives={_player.Lives} hits={_game.RunHitCount} score={_game.Score}");
            if (DisplayServer.GetName() != "headless")
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shot = GetViewport().GetTexture().GetImage();
                if (shot.SavePng(Path.Combine(_output, cleared ? "clear.png" : previewed ? "preview.png" : "failed.png")) != Error.Ok)
                    throw new IOException("Could not save the final battle screenshot.");
            }
            await ToSignal(GetTree().CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
            exit = cleared || previewed ? 0 : 1;
        }
        catch (Exception error) { GD.PushError($"[CLEAR] ERROR {error}"); }
        _clearDriving = false;
        _pilot?.Release();
        foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
        { audio.Stop(); audio.Stream = null; }
        if (IsInstanceValid(_clearRoot)) _clearRoot.QueueFree();
        _pool?.DespawnAll();
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GetTree().Quit(exit);
    }

    private void TickClearBattle(double delta)
    {
        if (!_clearDriving) return;
        _clearElapsed += delta;
        _clearChargeWait -= delta;
        _clearPulseWait -= delta;
        foreach (Key key in new[] { Key.X, Key.C, Key.Space }) ClearKey(key, false);
        if (Hud.BubblePaused || _hud.CinematicMode || _clearRoot.Boss == null || _player.Lives <= 0)
        {
            _pilot.Update(delta, bombs: false, dodge: false);
            return;
        }
        _boss = _clearRoot.Boss;
        _easyAttack = GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().FirstOrDefault(a => !a.Finished);
        if (_easyAttack != null && _clearAttackId != _easyAttack.GetInstanceId())
        {
            _clearAttackId = _easyAttack.GetInstanceId();
            _easyStart = _player.GlobalPosition;
            _bodyPilotGoal = _easyStart;
            _bodyPilotCue = -1;
            _bodyOrbitPlanned = false;
            _bodyOrbitDirection = 1;
            GD.Print($"[CLEAR] ATTACK t={_clearElapsed:0.00} key={_easyAttack.MechanicKey} name={_easyAttack.SpellName}");
        }
        bool airlock = _easyAttack?.MechanicKey == "mina.traveling-airlock";
        if (_easyAttack is MinaArchiveTechnique archive) DriveClearArchive(archive, delta);
        else if (airlock) DriveClearAirlock(delta);
        else if (_easyAttack != null) DriveEasyPilot(delta);
        else _pilot.Update(delta, shoot: false, bombs: false, dodge: false);
        if (_game.CurrentSpecial is { Character: Job.Heal, Elapsed: < PlayerSpecial.KoharuDuration } healing
            && (_easyAttack == null || _easyAttack.DangerDistance(healing.Origin, true) > 8)
            && !EasyBodyAt(healing.Origin))
            EasyGuideTo(healing.Origin, delta);
        ClearKey(Key.Shift, !_player.LockArmed ? !Input.IsKeyPressed(Key.Shift) : true);
        bool ready = _game.HasChargeTier2 ? _player.ChargeFull2 : _player.ChargeFull;
        if (ready) _clearChargeWait = .04;
        ClearKey(Key.Z, _player.LockedOn && _clearChargeWait <= 0 && !_player.Dodging);
        float gap = _easyAttack?.DangerDistance(_player.Position, false) ?? 999;
        foreach (var bullet in GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active && !b.LeadIn && b.CollisionLayer != 0))
        {
            Vector2 future = bullet.GlobalPosition + bullet.Velocity * .12f;
            gap = Math.Min(gap, Geometry2D.GetClosestPointToSegment(_player.Position, bullet.GlobalPosition, future).DistanceTo(_player.Position) - bullet.CollisionRadius - Player.HitRadius);
        }
        if (_clearPulseWait <= 0)
        {
            if (_game.SpecialReady && (_easyAttack == null && _player.Lives < _game.StartLives || gap < 12 && _player.Lives == _game.StartLives))
                ClearKey(Key.C, true);
            if (_scene == "MinaBattle" && gap < 4 && _game.Bombs > 0 && _player.InvincibleRemaining <= .05f && !_player.Dodging)
            {
                ClearKey(Key.X, true);
                _clearPulseWait = .2;
            }
            else if (!airlock && gap < 2 && _player.DodgeReady && _player.InvincibleRemaining <= 0)
            {
                ClearKey(Key.Z, false);
                ClearKey(Key.Space, true);
                _clearPulseWait = .12;
            }
            else if (gap < 0 && !_player.Dodging && _game.Bombs > 0 && !Read<bool>(_player, "_invincible"))
            {
                ClearKey(Key.X, true);
                _clearPulseWait = .2;
            }
        }
        Input.FlushBufferedEvents();
        Pad.ReleaseMouseFollow();
    }

    private void DriveClearAirlock(double delta)
    {
        var room = Read<System.Collections.IEnumerable>(_easyAttack!, "_easyMemoryCues").Cast<object>()
            .FirstOrDefault(c => Read<int>(c, "Mode") == 2);
        if (room == null) { EasyFollowRoute(null, delta); return; }
        var method = typeof(MinaBossTechnique).GetMethod("EasyAirlockDoors", BindingFlags.Static | BindingFlags.NonPublic)!;
        var doors = ((Rect2 Entry, Rect2 Exit, double OpensAt, double ClosesAt)[])method.Invoke(null, new[] { room })!;
        var current = doors.FirstOrDefault(d => _easyAttack!.Elapsed < d.ClosesAt + .4);
        Rect2 target = current.Entry.Size == Vector2.Zero ? doors[^1].Exit
            : _easyAttack!.Elapsed < current.OpensAt ? current.Entry : current.Exit;
        EasyGuideTo(target.GetCenter(), delta);
    }

    private void DriveClearArchive(MinaArchiveTechnique attack, double delta)
    {
        float y = _player.GlobalPosition.Y;
        float left = Field.Left + 4, right = Field.Right - 4;
        foreach (var hazard in attack.PracticeHazards)
        {
            if (hazard.B.Y < y - 4 || hazard.A.Y > y + 14) continue;
            if (Math.Abs(hazard.A.X - Field.Left) < .01f) left = Math.Max(left, hazard.B.X + 4);
            if (Math.Abs(hazard.B.X - Field.Right) < .01f) right = Math.Min(right, hazard.A.X - 4);
        }
        if (y > Field.Bottom - 15)
            foreach (var cue in attack.EntranceForecasts.Where(c => c.EntersAt - attack.Elapsed < .5))
            {
                left = Math.Max(left, cue.Left.End.X + 4);
                right = Math.Min(right, cue.Right.Position.X - 4);
            }
        EasyGuideTo(new Vector2((left + right) * .5f, y), delta);
    }

    private static void ClearKey(Key key, bool pressed)
    {
        if (Input.IsKeyPressed(key) != pressed)
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    }
}
