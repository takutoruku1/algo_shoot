using Godot;
using System;

public partial class Audio
{
    private AudioEffectLowPassFilter? _akariFilter;
    private int _akariBus;
    private float _akariDepth;
    private int _akariDepthTarget;
    private bool _akariMusicActive;
    private readonly AudioStreamWav?[] _akariBreakSounds = new AudioStreamWav?[5];
    public int AkariMusicDepth => _akariDepthTarget;

    public void StartAkariMusic(int depth, float fade = 1f)
    {
        if (Muted) return;
        if (_akariFilter == null)
        {
            _akariBus = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName(_akariBus, "AkariTheme");
            AudioServer.SetBusSend(_akariBus, "Music");
            _akariFilter = new AudioEffectLowPassFilter { CutoffHz = 15000f };
            AudioServer.AddBusEffect(_akariBus, _akariFilter);
        }
        Music(BgmBossAkari, fade);
        _akariDepthTarget = Mathf.Clamp(depth, 0, 5);
        var current = _useA ? _musicA : _musicB;
        current.Bus = "AkariTheme";
        if (!_akariMusicActive)
        {
            _akariDepth = _akariDepthTarget;
            _akariMusicActive = true;
        }
    }

    private void StopAkariMusicEffects() => _akariMusicActive = false;

    private void TickAkariMusic(double delta)
    {
        if (!_akariMusicActive) return;
        if (GetTree().Paused || Hud.BubblePaused) return;
        _akariDepth = Mathf.MoveToward(_akariDepth, _akariDepthTarget, (float)delta * 0.65f);
        float depth = _akariDepth / 5f;
        _akariFilter!.CutoffHz = Mathf.Lerp(15000f, 20000f, depth);
        AudioServer.SetBusVolumeDb(_akariBus, Mathf.Lerp(0f, 1.5f, depth));
    }

    public void PlayAkariPostBreak(int index)
    {
        index = Mathf.Clamp(index, 0, 4);
        _akariBreakSounds[index] ??= BuildAkariBreak(index);
        Se(_akariBreakSounds[index]!, volDb: -8f);
    }

    private static AudioStreamWav BuildAkariBreak(int index)
    {
        float duration = 0.65f + index * 0.24f;
        var samples = new float[(int)(Rate * duration)];
        var random = new Random(871 + index);
        float phase = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(random.NextDouble() * 2 - 1);
            phase += Mathf.Tau * (45 + 105 * Mathf.Exp(-t * 24)) / Rate;
            float low = Mathf.Sin(phase) * Mathf.Exp(-t * (9 - index)) * (0.10f + index * 0.035f);
            float glass = 0;
            for (int n = 0; n < 4 + index * 2; n++)
            {
                float delay = n * 0.026f;
                if (t < delay) continue;
                float s = t - delay;
                glass += Mathf.Sin(Mathf.Tau * (1240 + n * 317.3f) * s) * Mathf.Exp(-s * 15) * 0.025f;
            }
            samples[i] = low + glass + noise * Mathf.Exp(-t * 40) * (0.12f + index * 0.022f);
        }
        return MakeWav(samples);
    }
}
