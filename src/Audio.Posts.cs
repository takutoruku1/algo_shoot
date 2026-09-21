using Godot;
using System;

public partial class Audio
{
    private AudioStream? _postTheme;
    private AudioEffectLowPassFilter? _postFilter;
    private int _postBus, _postDepthTarget;
    private float _postDepth;
    public int PostMusicDepth => _postDepthTarget;
    public bool PostMusicActive => _postTheme != null;

    public void StartPostMusic(string id, int depth, float fade = 1f)
    {
        if (Muted) return;
        AudioStream theme = id switch
        {
            "koharu" => BgmBossKoharu,
            "rei" => BgmBossRei,
            "mina" => BgmBossMina,
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        if (_postFilter == null)
        {
            _postBus = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName(_postBus, "PostTheme");
            AudioServer.SetBusSend(_postBus, "Music");
            _postFilter = new AudioEffectLowPassFilter();
            AudioServer.AddBusEffect(_postBus, _postFilter);
        }
        bool continuing = _postTheme == theme;
        Music(theme, fade);
        _postTheme = theme;
        _postDepthTarget = Mathf.Clamp(depth, 0, 5);
        if (!continuing) _postDepth = _postDepthTarget;
        (_useA ? _musicA : _musicB).Bus = "PostTheme";
        ApplyPostMix();
    }

    private void TickPostMusic(double delta)
    {
        if (_postTheme == null || GetTree().Paused || Hud.BubblePaused) return;
        _postDepth = Mathf.MoveToward(_postDepth, _postDepthTarget, (float)delta * 0.65f);
        ApplyPostMix();
    }

    private void ApplyPostMix()
    {
        float depth = _postDepth / 5f;
        // Keep each theme's own rhythm; progressively open its bandwidth and dynamics.
        _postFilter!.CutoffHz = Mathf.Lerp(2400, 18000, depth * depth);
        AudioServer.SetBusVolumeDb(_postBus, Mathf.Lerp(-4.5f, 0, depth));
    }
}
