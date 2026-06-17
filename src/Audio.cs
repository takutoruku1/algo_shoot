using Godot;
using System.Collections.Generic;

// Audio : サウンド再生のオートロード (/root/Audio)。
//   FxLayer と同じ「静的 Instance 経由で各所から呼ぶ」流儀。シーンをまたいで常駐する。
//   SE は FxLayer の視覚フック（Muzzle/PlayerHit/PurifyBurst…）の隣で
//     Audio.Instance?.Se(stream) と鳴らし、画と音を同フレームで揃える（mitsuda style §4）。
//   バスは default_bus_layout.tres（Master/Music/SE/Voice/Amb/Alert）。
//   --qa 時は自己ミュート（QaPilot の無音・高速実行を妨げない / pitfalls P9）。
//
// ※この段階では「鳴らす土台」のみ。実音源（.ogg等）は別途調達して各所で差し込む。
public partial class Audio : Node
{
    public static Audio Instance = null!;

    public bool Muted;                 // QA等で全再生を止める

    private const int SePoolSize = 12;     // 同時発音（弾幕で飽和しても破綻しない）
    private const int AlertPoolSize = 3;   // 被弾など最優先音
    private const float SilentDb = -60f;   // フェード時の実質無音

    private readonly List<AudioStreamPlayer> _sePool = new();
    private readonly List<AudioStreamPlayer> _alertPool = new();
    private AudioStreamPlayer _musicA = null!, _musicB = null!;
    private bool _useA = true;
    private readonly RandomNumberGenerator _rng = new();

    // コアSE（コードで合成したプレースホルダ。実音源が来たら差し替える）。
    public AudioStreamWav SfxShot = null!, SfxGraze = null!, SfxHit = null!, SfxPurify = null!;

    // BGM（コード合成のループ。実音源が来たら差し替える）。
    public AudioStreamWav BgmStage = null!;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always; // ポーズ中も音は鳴らせる
        _rng.Randomize();

        // --qa は無音・高速で回す。autoload 初期化順に依存しないよう自分で判定する。
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--qa") { Muted = true; break; }

        for (int i = 0; i < SePoolSize; i++) _sePool.Add(MakePlayer("SE"));
        for (int i = 0; i < AlertPoolSize; i++) _alertPool.Add(MakePlayer("Alert"));
        _musicA = MakePlayer("Music");
        _musicB = MakePlayer("Music");

        SfxShot   = SynthShot();
        SfxGraze  = SynthGraze();
        SfxHit    = SynthHit();
        SfxPurify = SynthPurify();
        BgmStage  = BuildBgm();
    }

    private AudioStreamPlayer MakePlayer(string bus)
    {
        var p = new AudioStreamPlayer { Bus = bus, VolumeDb = 0f };
        AddChild(p);
        return p;
    }

    // ───────── SE（効果音）─────────
    // FxLayer の視覚フックと同フレームで鳴らす。プール枯渇時は最古を奪う。
    public void Se(AudioStream stream, float volDb = 0f, float pitch = 1f)
        => Play(_sePool, stream, volDb, pitch);

    // ───────── Alert（被弾・残機・段階移行など最優先音）─────────
    public void AlertSe(AudioStream stream, float volDb = 0f, float pitch = 1f)
        => Play(_alertPool, stream, volDb, pitch);

    private void Play(List<AudioStreamPlayer> pool, AudioStream stream, float volDb, float pitch)
    {
        if (Muted || stream == null || pool.Count == 0) return;
        AudioStreamPlayer? p = null;
        for (int i = 0; i < pool.Count; i++)
            if (!pool[i].Playing) { p = pool[i]; break; }
        p ??= pool[0]; // 空きが無ければ最古を奪う＝飽和しても破綻しない
        p.Stream = stream;
        p.VolumeDb = volDb;
        p.PitchScale = pitch;
        p.Play();
    }

    // ───────── BGM ─────────
    // クロスフェードでループ曲を差し替える。同じ曲なら何もしない。stream=null で停止。
    public void Music(AudioStream? stream, float fade = 1.0f)
    {
        if (Muted) return;
        var cur = _useA ? _musicA : _musicB;
        var nxt = _useA ? _musicB : _musicA;
        if (cur.Stream == stream && cur.Playing) return;

        if (stream != null)
        {
            nxt.Stream = stream;
            nxt.VolumeDb = SilentDb;
            nxt.Play();
        }
        var t = CreateTween().SetParallel();
        if (stream != null) t.TweenProperty(nxt, "volume_db", 0f, fade);
        if (cur.Playing)
        {
            t.TweenProperty(cur, "volume_db", SilentDb, fade);
            t.Chain().TweenCallback(Callable.From(() => { if (cur.Playing) cur.Stop(); }));
        }
        _useA = !_useA;
    }

    public void StopMusic(float fade = 0.5f) => Music(null, fade);

    // ───────── コアSE 再生（呼び出し側はこれだけ叩く）─────────
    // 発射：短く・減衰速く・ピッチ微ゆらぎ。全開中は高揚（ピッチ上げ）。
    public void PlayShot(bool overload)
        => Se(SfxShot, volDb: -24f, pitch: _rng.RandfRange(0.97f, 1.03f) + (overload ? 0.18f : 0f));
    // グレイズ：鋭く高い「チッ」。被弾と音域を分け、混同させない。
    public void PlayGraze()
        => Se(SfxGraze, volDb: -22f, pitch: _rng.RandfRange(0.98f, 1.05f));
    // 被弾：低いノイズバースト。最優先（Alertバス）で弾幕に埋もれさせない。
    public void PlayHit()
        => AlertSe(SfxHit, volDb: -8f);
    // 浄化：減衰の長いベル。「壊した」でなく「解けた・届いた」温かい余韻。
    public void PlayPurify()
        => Se(SfxPurify, volDb: -14f, pitch: _rng.RandfRange(0.99f, 1.02f));

    // ───────── 波形合成（16bit PCM の AudioStreamWav を生成）─────────
    private const int Rate = 44100;

    private static AudioStreamWav MakeWav(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = bytes,
        };
    }

    // 発射：820→440Hz に落ちる短い矩形寄りブリップ（~55ms）。
    private AudioStreamWav SynthShot()
    {
        float dur = 0.04f; int n = (int)(Rate * dur);
        var s = new float[n]; float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float freq = Mathf.Lerp(760f, 420f, t / dur);
            phase += freq / Rate;
            float v = Mathf.Sin(phase * Mathf.Tau);
            float sq = v >= 0f ? 1f : -1f;
            float env = Mathf.Exp(-t / 0.012f);
            // ほぼ正弦（刺さる倍音を最小化）＋短く＋小さく
            s[i] = (0.12f * sq + 0.88f * v) * env * 0.3f;
        }
        return MakeWav(s);
    }

    // グレイズ：2600Hz の鋭い高音＋倍音、極短（~38ms）。
    private AudioStreamWav SynthGraze()
    {
        float dur = 0.03f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t / 0.008f);
            float a = Mathf.Sin(Mathf.Tau * 2100f * t);       // 少し低く（刺さりを抑える）
            float b = Mathf.Sin(Mathf.Tau * 4200f * t) * 0.12f; // 倍音は控えめ
            s[i] = (a + b) * env * 0.32f;
        }
        return MakeWav(s);
    }

    // 被弾：低いトーン（160→90Hz）＋ノイズの鈍いバースト（~200ms）。
    private AudioStreamWav SynthHit()
    {
        float dur = 0.18f; int n = (int)(Rate * dur);
        var s = new float[n]; float phase = 0f; float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float freq = Mathf.Lerp(150f, 80f, t / dur);
            phase += freq / Rate;
            float tone = Mathf.Sin(phase * Mathf.Tau) * Mathf.Exp(-t / 0.08f);
            float white = _rng.Randf() * 2f - 1f;
            lp += (white - lp) * 0.12f; // 一極ローパス：高域を削り「耳に刺さる」成分を除去
            float noise = lp * Mathf.Exp(-t / 0.04f);
            s[i] = (0.7f * tone + 0.3f * noise) * 0.6f; // 低い「ドゥッ」。鋭さより重み
        }
        return MakeWav(s);
    }

    // 浄化：柔らかい立ち上がり＋長い減衰のベル（基音523Hz＋倍音、~600ms）。
    private AudioStreamWav SynthPurify()
    {
        float dur = 0.6f; int n = (int)(Rate * dur);
        var s = new float[n];
        float f0 = 523f; // C5
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float attack = t < 0.004f ? t / 0.004f : 1f;
            float env = attack * Mathf.Exp(-t / 0.2f);
            float bell = Mathf.Sin(Mathf.Tau * f0 * t)
                       + 0.4f * Mathf.Sin(Mathf.Tau * f0 * 2f * t)
                       + 0.18f * Mathf.Sin(Mathf.Tau * f0 * 3f * t);
            float third = 0.12f * Mathf.Sin(Mathf.Tau * f0 * 1.26f * t); // 長3度のきらめき（控えめ）
            s[i] = (bell + third) * env * 0.22f;
        }
        return MakeWav(s);
    }

    // ───────── BGM 合成（M.I.N.A. モチーフを核にした 8 秒シームレスループ）─────────
    // 進行 Am - F - C - G（vi-IV-I-V＝“泣ける”カノン系）。各小節2秒。
    // パッド/ベース/メロディは小節端で振幅0へ戻すので、ループ継ぎ目でクリックしない。
    private AudioStreamWav BuildBgm()
    {
        const float bar = 2.0f; const int bars = 4;
        int barN = (int)(Rate * bar), n = barN * bars;
        var s = new float[n];

        // [bass, 三和音1, 三和音2, 三和音3]（Hz）
        float[][] chords =
        {
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
            new[] {  87.31f, 174.61f, 220.00f, 261.63f }, // F
            new[] { 130.81f, 261.63f, 329.63f, 392.00f }, // C
            new[] {  98.00f, 196.00f, 246.94f, 293.66f }, // G
        };
        // M.I.N.A. モチーフ（ド ミ レ ソ）を1小節1音、上声で。
        float[] motif = { 523.25f, 659.25f, 587.33f, 783.99f };

        for (int b = 0; b < bars; b++)
        {
            float[] ch = chords[b];
            int baseI = b * barN;
            for (int i = 0; i < barN; i++)
            {
                float t = (float)i / Rate; // 小節内の経過秒
                // パッド：三和音の柔らかいスウェル（両端0）
                float pad = (Mathf.Sin(Mathf.Tau * ch[1] * t) + Mathf.Sin(Mathf.Tau * ch[2] * t)
                           + Mathf.Sin(Mathf.Tau * ch[3] * t)) * Swell(t, bar, 0.25f, 0.45f) * 0.09f;
                // ベース：根音
                float bass = Mathf.Sin(Mathf.Tau * ch[0] * t) * Swell(t, bar, 0.06f, 0.35f) * 0.16f;
                // アルペジオ：四分で三和音を巡る柔らかいプラック（動き）
                float arp = Arp(t, ch) * 0.06f;
                // メロディ：モチーフ1音（柔らかいフルート）
                float mel = Mathf.Sin(Mathf.Tau * motif[b] * t) * Swell(t, bar, 0.10f, 0.7f) * 0.08f;
                // マスターゲイン：SE（控えめ）に対して BGM が大きすぎないよう全体を下げる
                s[baseI + i] = (pad + bass + arp + mel) * 0.4f;
            }
        }
        FadeEnds(s, (int)(0.006f * Rate)); // 端を数msフェード＝継ぎ目を完全に無音化

        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // attack で 0→1、rel で 1→0（dur で終端0）。両端を0にしてループ/小節境界のクリックを防ぐ。
    private static float Swell(float t, float dur, float atk, float rel)
    {
        if (t < atk) return t / atk;
        if (t > dur - rel) return Mathf.Max(0f, (dur - t) / rel);
        return 1f;
    }

    // 四分音符（0.5s）で三和音3音を巡る柔らかいプラック。
    private static float Arp(float t, float[] ch)
    {
        const float step = 0.5f;
        int k = (int)(t / step);
        float lt = t - k * step;
        float f = ch[1 + (k % 3)];
        float env = Mathf.Exp(-lt / 0.18f) * (lt < 0.005f ? lt / 0.005f : 1f);
        return Mathf.Sin(Mathf.Tau * f * lt) * env;
    }

    private static void FadeEnds(float[] s, int k)
    {
        for (int i = 0; i < k && i < s.Length; i++)
        {
            float g = (float)i / k;
            s[i] *= g;
            s[s.Length - 1 - i] *= g;
        }
    }
}
