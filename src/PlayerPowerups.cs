using Godot;

public partial class Player
{
    public const int PowerLevelCap = 2;
    public const int KillsPerPowerDrop = 10;
    public const int KillsPerLifeDrop = 30;
    public int LinePower { get; private set; }
    public int SpeedPower { get; private set; }
    public int MaxLives => _game?.StartLives ?? 3;
    public float PowerMoveMultiplier => 1f + SpeedPower * 0.25f;
    private int _powerKills;
    private int _lifeKills;
    private int _nextPowerKind;

    public int PowerLevel(PowerKind kind) => kind switch
    {
        PowerKind.Line => LinePower,
        PowerKind.Speed => SpeedPower,
        _ => 0,
    };

    public bool ApplyPowerup(PowerKind kind)
    {
        if (_gameOver || Lives <= 0 || PowerLevel(kind) >= PowerLevelCap) return false;
        if (kind == PowerKind.Life) return AddLife();
        switch (kind)
        {
            case PowerKind.Line: LinePower++; break;
            case PowerKind.Speed: SpeedPower++; break;
        }
        QueueRedraw();
        return true;
    }

    public PowerKind? CountPowerupKill()
    {
        if (_gameOver || Hud.BubblePaused || _game == null || _game.TrainingMode || _game.TutorialNoConsume
            || _game.RedemptionActive || _game.StageCleared) return null;
        _powerKills++;
        if (++_lifeKills >= KillsPerLifeDrop)
        {
            _lifeKills = 0;
            _powerKills = 0;
            return PowerKind.Life;
        }
        if (_powerKills < KillsPerPowerDrop) return null;
        _powerKills = 0;
        if (FxLayer.Instance!.ScoreDrops.PowerCount >= 4) return null;
        for (int i = 0; i < 2; i++)
        {
            var kind = (PowerKind)((_nextPowerKind + i) % 2);
            if (PowerLevel(kind) >= PowerLevelCap) continue;
            _nextPowerKind = ((int)kind + 1) % 2;
            return kind;
        }
        return null;
    }

    private void LosePowerupsOnHit()
    {
        LinePower = 0;
        SpeedPower = 0;
    }
}
