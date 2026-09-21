using Godot;

public partial class Player
{
    public const int PowerLevelCap = 2;
    public const int KillsPerPowerDrop = 5;
    public int LinePower { get; private set; }
    public int SpeedPower { get; private set; }
    public int LifePower { get; private set; }
    public int ShieldPower { get; private set; }
    public int MaxLives => (_game?.StartLives ?? 3) + LifePower;
    public float PowerMoveMultiplier => 1f + SpeedPower * 0.25f;
    private int _powerKills;
    private int _nextPowerKind;

    public int PowerLevel(PowerKind kind) => kind switch
    {
        PowerKind.Line => LinePower,
        PowerKind.Speed => SpeedPower,
        PowerKind.Life => LifePower,
        _ => ShieldPower,
    };

    public bool ApplyPowerup(PowerKind kind)
    {
        if (_gameOver || Lives <= 0 || PowerLevel(kind) >= PowerLevelCap) return false;
        switch (kind)
        {
            case PowerKind.Line: LinePower++; break;
            case PowerKind.Speed: SpeedPower++; break;
            case PowerKind.Life:
                LifePower++;
                Lives++;
                (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetLives(Lives);
                break;
            case PowerKind.Shield: ShieldPower++; break;
        }
        QueueRedraw();
        return true;
    }

    public PowerKind? CountPowerupKill()
    {
        if (_gameOver || Hud.BubblePaused || _game == null || _game.TrainingMode || _game.TutorialNoConsume
            || _game.RedemptionActive || _game.StageCleared) return null;
        if (++_powerKills < KillsPerPowerDrop) return null;
        _powerKills = 0;
        if (FxLayer.Instance!.ScoreDrops.PowerCount >= 4) return null;
        for (int i = 0; i < 4; i++)
        {
            var kind = (PowerKind)((_nextPowerKind + i) % 4);
            if (PowerLevel(kind) >= PowerLevelCap) continue;
            _nextPowerKind = ((int)kind + 1) % 4;
            return kind;
        }
        return null;
    }

    private bool AbsorbPowerupHit()
    {
        if (ShieldPower == 0 || (_game?.TutorialNoConsume ?? false)) return false;
        ShieldPower--;
        StartInvincible(fromHit: true);
        FxLayer.Instance?.ScorePickup(GlobalPosition, PowerPickupArt.ColorFor(PowerKind.Shield));
        Audio.Instance?.PlayUiConfirm();
        return true;
    }

    private void LosePowerupsOnHit()
    {
        LinePower = 0;
        SpeedPower = 0;
        if (LifePower > 0) LifePower--;
    }
}
