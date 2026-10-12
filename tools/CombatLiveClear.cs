using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CombatRevisionQa
{
    private bool _liveClear, _liveShift, _liveCharge, _liveDodge, _liveDone;
    private Player _livePlayer = null!;
    private BossAkari? _liveBoss;
    private Vector2 _liveDirection;
    private double _liveTime, _liveChargeDelay, _liveVictoryTime;
    private int _liveFrame, _liveWallDodges, _liveAttackFrame;
    private ulong _liveTechnique;
    private readonly Queue<string> _liveTrace = new();
    private int _livePlanDepth;

    private void BeginLiveClear()
    {
        GD.Seed(2070);
        _game.Difficulty = GameManager.Diff.Lunatic;
        _game.SelectedJob = Job.Tank;
        _game.TrainingSetUpgrade("n_dodge", true);
        _game.SelectedEntry = GameManager.StageEntry.Boss;
        ProcessPhysicsPriority = -1000;
        ProcessMode = ProcessModeEnum.Always;
        var stage = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(stage);
        GetTree().CurrentScene = stage;
        _livePlayer = stage.Player;
        _liveClear = true;
        GD.Print("[LiveClear] START LUNATIC Akari; Mina/base stats/dodge unlocked; bombs=OFF; dash=wall only; game physics unchanged");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_liveClear || _liveDone) return;
        _liveFrame++;
        _liveTime += delta;
        _liveBoss ??= GetTree().GetNodesInGroup("enemies").OfType<BossAkari>().FirstOrDefault();
        if (_game.RunHitCount > 0 || _livePlayer.Lives < _game.StartLives)
        {
            foreach (string entry in _liveTrace) GD.Print(entry);
            var near = Hostile().OrderBy(b => b.Position.DistanceSquaredTo(_livePlayer.Position)).Take(4);
            foreach (var bullet in near) GD.Print($"[LiveClear] NEAR p={bullet.Position} v={bullet.Velocity} age={Read<float>(bullet, "_age"):F2}");
            EndLiveClear($"HIT at {_liveTime:F2}s player={_livePlayer.Position} hits={_game.RunHitCount} boss_hp={_liveBoss?.HpRatio:F3}");
            return;
        }
        if (_liveTime > 240) { EndLiveClear("TIMEOUT"); return; }
        if (_liveVictoryTime > 0 || _liveBoss != null && IsInstanceValid(_liveBoss) && _liveBoss.IsPurified)
        {
            _liveVictoryTime += delta;
            if (_liveVictoryTime > 3) { EndLiveClear(""); return; }
        }
        if (_liveFrame % 300 == 0)
            GD.Print($"[LiveClear] t={_liveTime:F1} hp={_liveBoss?.HpRatio:F3} lives={_livePlayer.Lives} hits={_game.RunHitCount} wall_dodges={_liveWallDodges} p={_livePlayer.Position} speed={_livePlayer.MoveSpeed:F2} mouse={Pad.UsingMouse}");

        SetLiveKey(Key.Space, false, ref _liveDodge);
        if (Hud.BubblePaused)
        {
            AutoplayInput.Move(Vector2.Zero);
            SetLiveKey(Key.Shift, false, ref _liveShift);
            SetLiveKey(Key.Z, _liveFrame % 8 < 4, ref _liveCharge);
            Input.FlushBufferedEvents();
            return;
        }
        SetLiveKey(Key.Shift, true, ref _liveShift);
        float speed = _livePlayer.SlowestMoveSpeed;
        if (_livePlayer.Dodging || _livePlayer.ReturningToStart)
        {
            AutoplayInput.Move(Vector2.Zero);
            SetLiveKey(Key.Z, false, ref _liveCharge);
            Input.FlushBufferedEvents();
            return;
        }
        if (_liveBoss is { } boss && IsInstanceValid(boss) && !boss.IsPurified)
        {
            var wall = GetTree().GetNodesInGroup("boss_dodge_waves").OfType<BossDodgeWave>().FirstOrDefault(a => !a.Finished);
            var attack = boss.GetChildren().OfType<BossCrossfire>().FirstOrDefault(a => !a.Finished);
            Node? technique = (Node?)wall ?? attack;
            if (technique != null && technique.GetInstanceId() != _liveTechnique)
            {
                _liveTechnique = technique.GetInstanceId();
                _liveAttackFrame = _liveFrame;
                GD.Print($"[LiveClear] TECH t={_liveTime:F2} wall={wall != null} sweep={attack?.Sweep} weave={attack?.Weave} pincer={attack?.Pincer} diagonal={attack?.Diagonal}");
            }
            if (wall is { IsStriking: true } && _livePlayer.DodgeReady && wall.DistanceToFront(_livePlayer.Position) is >= -4 and <= 24)
            {
                AutoplayInput.Move(-wall.Direction);
                SetLiveKey(Key.Space, true, ref _liveDodge);
                _liveWallDodges++;
                GD.Print($"[LiveClear] WALL_DODGE t={_liveTime:F2} wave={wall.Wave}");
            }
            else
            {
                Vector2 goal = global::Field.PlayerStart;
                if (attack != null)
                {
                    int path = attack.Pincer && !attack.Diagonal ? 8 : attack.Diagonal && boss.HasEvolved ? 4
                        : attack.Sweep || attack.Weave || attack.Diagonal ? 3 : 5;
                    goal = BarrageRouteGoal(_livePlayer, attack, path, _liveFrame - _liveAttackFrame) ?? goal;
                }
                var areas = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().ToArray();
                if (areas.Length > 0)
                {
                    float best = float.MaxValue;
                    for (float x = global::Field.Left + 10; x < global::Field.Right - 10; x += 8)
                    for (float y = 10; y < global::Field.Bottom - 10; y += 8)
                    {
                        var point = new Vector2(x, y);
                        if (areas.Any(a => a.CoversPoint(point))) continue;
                        float cost = _livePlayer.Position.DistanceTo(point);
                        if (cost < best) { best = cost; goal = point; }
                    }
                }
                if (wall != null) _liveDirection = Vector2.Zero;
                else if (_liveFrame % 2 == 0)
                {
                    _liveDirection = PlanLiveMove(_livePlayer, boss, goal, speed);
                    _liveTrace.Enqueue($"[LiveClear] TRACE f={_liveFrame} p={_livePlayer.Position} move={_liveDirection} depth={_livePlanDepth} speed={_livePlayer.MoveSpeed:F2} gap={Clearance(0, _livePlayer.Position):F2}");
                    while (_liveTrace.Count > 20) _liveTrace.Dequeue();
                }
                AutoplayInput.Move(_liveDirection);
            }
        }
        _liveChargeDelay -= delta;
        if (_livePlayer.ChargeFull)
        {
            if (_liveCharge) _liveChargeDelay = 0.15;
            SetLiveKey(Key.Z, false, ref _liveCharge);
        }
        else if (_liveChargeDelay <= 0) SetLiveKey(Key.Z, true, ref _liveCharge);
        Input.FlushBufferedEvents();
    }

    private void SetLiveKey(Key key, bool down, ref bool held)
    {
        if (held == down) return;
        held = down;
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = down, ShiftPressed = key == Key.Shift ? down : _liveShift });
    }

    private async void EndLiveClear(string failure)
    {
        _liveDone = true;
        AutoplayInput.Move(Vector2.Zero);
        SetLiveKey(Key.Shift, false, ref _liveShift);
        SetLiveKey(Key.Z, false, ref _liveCharge);
        SetLiveKey(Key.Space, false, ref _liveDodge);
        Input.FlushBufferedEvents();
        GD.Print($"[LiveClear] {(failure.Length == 0 ? "PASS" : "FAIL " + failure)} frames={_liveFrame} dodges={_livePlayer.DodgeCount} wall_dodges={_liveWallDodges}");
        if (OS.GetCmdlineUserArgs().Contains("--live-shots"))
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            image.SavePng(ProjectSettings.GlobalizePath("res://build/qa_story/live_clear_last.png"));
        }
        GetTree().Quit(failure.Length == 0 ? 0 : 2);
    }
}
