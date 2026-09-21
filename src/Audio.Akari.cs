using Godot;
using System;

public partial class Audio
{
    private AudioStreamPlayer[] _akariLayers = Array.Empty<AudioStreamPlayer>();
    private AudioEffectLowPassFilter? _akariFilter;
    private int _akariBus;
    private float _akariDepth;
    private int _akariDepthTarget;
    private bool _akariLayered;
    private readonly AudioStreamWav?[] _akariBreakSounds = new AudioStreamWav?[5];
    public int AkariMusicDepth => _akariDepthTarget;
    public bool AkariLayersPlaying => _akariLayered;

    public void StartAkariMusic(int depth, float fade = 1f)
    {
        if (Muted) return;
        if (_akariFilter == null)
        {
            _akariBus = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName(_akariBus, "AkariTheme");
            AudioServer.SetBusSend(_akariBus, "Music");
            _akariFilter = new AudioEffectLowPassFilter { CutoffHz = 1800f };
            AudioServer.AddBusEffect(_akariBus, _akariFilter);
            _akariLayers = new AudioStreamPlayer[2];
            for (int i = 0; i < _akariLayers.Length; i++)
            {
                _akariLayers[i] = MakePlayer("Music");
                _akariLayers[i].Name = $"AkariStem{i}";
                _akariLayers[i].Stream = BuildAkariStem(i);
                _akariLayers[i].VolumeDb = SilentDb;
            }
        }
        Music(BgmBossAkari, fade);
        _akariDepthTarget = Mathf.Clamp(depth, 0, 5);
        var current = _useA ? _musicA : _musicB;
        current.Bus = "AkariTheme";
        if (!_akariLayered)
        {
            _akariDepth = _akariDepthTarget;
            double start = current.GetPlaybackPosition() % _akariLayers[0].Stream.GetLength();
            foreach (var layer in _akariLayers) layer.Play((float)start);
            _akariLayered = true;
        }
    }

    private void StopAkariLayers()
    {
        _akariLayered = false;
        foreach (var layer in _akariLayers) { layer.Stop(); layer.VolumeDb = SilentDb; }
    }

    private void TickAkariMusic(double delta)
    {
        if (!_akariLayered) return;
        // Stems follow the transport of the licensed theme, including pause/resume.
        var current = _useA ? _musicA : _musicB;
        foreach (var layer in _akariLayers) layer.StreamPaused = current.StreamPaused;
        if (GetTree().Paused || Hud.BubblePaused) return;
        _akariDepth = Mathf.MoveToward(_akariDepth, _akariDepthTarget, (float)delta * 0.65f);
        float depth = _akariDepth / 5f;
        _akariFilter!.CutoffHz = Mathf.Lerp(1800f, 16500f, depth * depth);
        AudioServer.SetBusVolumeDb(_akariBus, Mathf.Lerp(-5f, 0f, depth));
        float blend = 1 - Mathf.Exp(-(float)delta * 2f);
        for (int i = 0; i < _akariLayers.Length; i++)
        {
            float strength = Mathf.Clamp((_akariDepth - (i == 0 ? 0.5f : 2f)) / 3f, 0, 1);
            float target = strength * (i == 0 ? 0.12f : 0.085f);
            _akariLayers[i].VolumeLinear = Mathf.Lerp(_akariLayers[i].VolumeLinear, target, blend);
        }
    }

    private static AudioStreamWav BuildAkariStem(int layer)
    {
        const double beat = 60.0 / 138.0;
        var samples = new float[(int)Math.Round(Rate * beat * 32)];
        var random = new Random(431 + layer);
        float previous = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)Rate;
            float noise = (float)(random.NextDouble() * 2 - 1);
            float hit = (float)(t % (layer == 0 ? beat : beat / 2));
            if (layer == 0)
            {
                float phase = Mathf.Tau * (48f * hit + 4.5f * (1 - Mathf.Exp(-hit * 30)));
                samples[i] = Mathf.Sin(phase) * Mathf.Exp(-hit * 13) * 0.68f
                    + noise * Mathf.Exp(-hit * 75) * 0.06f;
            }
            else
            {
                float high = noise - previous;
                float accent = ((int)(t / (beat / 2)) % 4 == 2) ? 0.22f : 0.09f;
                samples[i] = high * Mathf.Exp(-hit * 42) * accent;
            }
            previous = noise;
        }
        var wav = MakeWav(samples);
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        wav.LoopEnd = samples.Length;
        return wav;
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
