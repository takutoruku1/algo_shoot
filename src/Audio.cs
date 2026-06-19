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

    // 拡張SE（設計書 ③④⑥⑦⑧⑩）。同じくプレースホルダ。
    public AudioStreamWav SfxBomb = null!, SfxOverload = null!, SfxCalm = null!,
                          SfxSpell = null!, SfxStrip = null!;
    // UI操作音（カーソル/決定/キャンセル/購入成功/失敗）。全画面で共通＝一貫性。
    public AudioStreamWav SfxUiMove = null!, SfxUiConfirm = null!, SfxUiCancel = null!,
                          SfxUiBuy = null!, SfxUiDeny = null!;

    // BGM（コード合成のループ。実音源が来たら差し替える）。
    //   全曲で M.I.N.A. モチーフ（ド ミ レ ソ＝523/659/587/784Hz）を共有し「一つの主題の変奏」に。
    //   BgmMenu=温かく遅い薄編成 / BgmStage=道中 / BgmBoss=短調寄り・密度高め・緊張。
    public AudioStreamWav BgmMenu = null!, BgmStage = null!, BgmBoss = null!;

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
        SfxBomb     = SynthBomb();
        SfxOverload = SynthOverload();
        SfxCalm     = SynthCalm();
        SfxSpell    = SynthSpell();
        SfxStrip    = SynthStrip();
        SfxUiMove    = SynthUiMove();
        SfxUiConfirm = SynthUiConfirm();
        SfxUiCancel  = SynthUiCancel();
        SfxUiBuy     = SynthUiBuy();
        SfxUiDeny    = SynthUiDeny();
        BgmMenu   = BuildBgmMenu();
        BgmStage  = BuildBgm();
        BgmBoss   = BuildBgmBoss();

        // 保存済みの音量設定をバスへ適用（設定画面を開かなくても効く／再起動でも保たれる）。
        AudioConfig.ApplySaved();
        // 保存済みのコントローラ表記スタイル（Xbox/PS）を復元（HUD の操作ガイドへ即反映）。
        Pad.ApplySaved();
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

    // ───────── 拡張SE（③④⑥⑦⑧⑩）─────────
    // ③ボム：一拍の溜め→開放の二段。破壊でなく「鎮める／光が満ちる」。
    public void PlayBomb()
        => Se(SfxBomb, volDb: -10f);
    // ⑥全開発動：上昇音＋光が満ちるジングル。ピークの告知。
    public void PlayOverload()
        => Se(SfxOverload, volDb: -12f);
    // ⑦会話開始の弾消去：「鎮まる音」。沈黙を転換演出に変える。
    public void PlayCalm()
        => Se(SfxCalm, volDb: -16f);
    // ⑩スペル宣言・段階移行：短い宣言音。弾幕変化を耳で予告。被弾の下・グレイズの上＝Alert。
    public void PlaySpell()
        => AlertSe(SfxSpell, volDb: -14f);
    // ④パネル剥がし：軽い「コツッ」。浄化成立（PlayPurify）より一段軽い剥離音。
    public void PlayStrip()
        => Se(SfxStrip, volDb: -22f, pitch: _rng.RandfRange(0.97f, 1.04f));

    // ───────── UI操作音（⑧。全画面共通＝一貫性）─────────
    public void PlayUiMove()    => Se(SfxUiMove,    volDb: -20f, pitch: _rng.RandfRange(0.99f, 1.02f));
    public void PlayUiConfirm() => Se(SfxUiConfirm, volDb: -14f);
    public void PlayUiCancel()  => Se(SfxUiCancel,  volDb: -16f);
    public void PlayUiBuy()     => Se(SfxUiBuy,     volDb: -12f);
    public void PlayUiDeny()    => Se(SfxUiDeny,    volDb: -14f);

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

    // ③ボム：低い溜め（~0.18s スウェル）→ 開放（C-G-C の和音ブルーム＋やわらかい衝撃）。~0.9s。
    private AudioStreamWav SynthBomb()
    {
        float dur = 0.9f; int n = (int)(Rate * dur);
        var s = new float[n]; float lp = 0f;
        const float hit = 0.18f; // 溜め→開放の境
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            // 溜め：上昇するうなり（70→150Hz）が境で頂点。
            float ramp = Mathf.Min(1f, t / hit);
            float swell = Mathf.Sin(Mathf.Tau * Mathf.Lerp(70f, 150f, ramp) * t)
                        * ramp * ramp * (t < hit ? 1f : 0f) * 0.5f;
            // 開放：C5/G5/C6 の和音ブルーム（柔らかい立ち上がり＋長い減衰）。
            float bt = Mathf.Max(0f, t - hit);
            float env = (bt < 0.02f ? bt / 0.02f : 1f) * Mathf.Exp(-bt / 0.32f);
            float bloom = (Mathf.Sin(Mathf.Tau * 523.25f * bt)
                         + 0.7f * Mathf.Sin(Mathf.Tau * 783.99f * bt)
                         + 0.5f * Mathf.Sin(Mathf.Tau * 1046.5f * bt)) * env * 0.18f;
            // やわらかい光のノイズ（ローパスで刺さりを除去）。
            float white = _rng.Randf() * 2f - 1f;
            lp += (white - lp) * 0.05f;
            float air = lp * env * 0.12f;
            s[i] = swell + bloom + air;
        }
        FadeEnds(s, (int)(0.004f * Rate));
        return MakeWav(s);
    }

    // ⑥全開：C→E→G→C を駆け上がる上昇アルペジオ＋光が満ちるベル残響。~0.5s。
    private AudioStreamWav SynthOverload()
    {
        float dur = 0.5f; int n = (int)(Rate * dur);
        var s = new float[n];
        float[] steps = { 523.25f, 659.25f, 783.99f, 1046.5f }; // C5 E5 G5 C6
        const float step = 0.07f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            int k = Mathf.Min(steps.Length - 1, (int)(t / step));
            float lt = t - k * step;
            float env = (lt < 0.004f ? lt / 0.004f : 1f) * Mathf.Exp(-lt / 0.12f);
            float pluck = Mathf.Sin(Mathf.Tau * steps[k] * lt) * env * 0.22f;
            // 最後の音に重なる長い残響（光が満ちる）。
            float tailT = Mathf.Max(0f, t - 3 * step);
            float tail = Mathf.Sin(Mathf.Tau * 1046.5f * tailT)
                       * Mathf.Exp(-tailT / 0.2f) * 0.1f;
            s[i] = pluck + tail;
        }
        FadeEnds(s, (int)(0.004f * Rate));
        return MakeWav(s);
    }

    // ⑦鎮まる音：下降する柔らかいパッド（G5→C5）＋ふわっと消える。会話転換の「すっ」。~0.45s。
    private AudioStreamWav SynthCalm()
    {
        float dur = 0.45f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Sin(Mathf.Min(1f, t / dur) * Mathf.Pi) * 0.9f; // 山なり（両端0）
            float f = Mathf.Lerp(783.99f, 523.25f, t / dur); // 下降＝鎮まる
            float pad = (Mathf.Sin(Mathf.Tau * f * t)
                       + 0.5f * Mathf.Sin(Mathf.Tau * f * 1.5f * t)) * env * 0.14f;
            s[i] = pad;
        }
        FadeEnds(s, (int)(0.006f * Rate));
        return MakeWav(s);
    }

    // ⑩宣言：短い二音の警告（A5→E5）＋わずかな歪み。弾幕変化の予告。~0.22s。
    private AudioStreamWav SynthSpell()
    {
        float dur = 0.22f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = t < 0.09f ? 880f : 659.25f; // A5 → E5 の二音
            float lt = t < 0.09f ? t : t - 0.09f;
            float env = (lt < 0.003f ? lt / 0.003f : 1f) * Mathf.Exp(-lt / 0.06f);
            float v = Mathf.Sin(Mathf.Tau * f * t);
            float sq = v >= 0f ? 1f : -1f; // 矩形を少し混ぜて緊張感
            s[i] = (0.85f * v + 0.15f * sq) * env * 0.28f;
        }
        return MakeWav(s);
    }

    // ④パネル剥がし：高めの短い「コツッ」（2系統の減衰トーン）。浄化より軽い。~0.06s。
    private AudioStreamWav SynthStrip()
    {
        float dur = 0.06f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t / 0.016f);
            float a = Mathf.Sin(Mathf.Tau * 1400f * t);
            float b = Mathf.Sin(Mathf.Tau * 2100f * t) * 0.3f;
            s[i] = (a + b) * env * 0.3f;
        }
        return MakeWav(s);
    }

    // ───────── UI操作音（⑧）─────────
    // カーソル移動：ごく短い高い「ティッ」。
    private AudioStreamWav SynthUiMove()
    {
        float dur = 0.025f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t / 0.007f);
            s[i] = Mathf.Sin(Mathf.Tau * 1760f * t) * env * 0.28f;
        }
        return MakeWav(s);
    }

    // 決定：芯のある二音（E5→A5 上昇）。
    private AudioStreamWav SynthUiConfirm()
    {
        float dur = 0.16f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = t < 0.06f ? 659.25f : 880f; // E5 → A5
            float lt = t < 0.06f ? t : t - 0.06f;
            float env = (lt < 0.003f ? lt / 0.003f : 1f) * Mathf.Exp(-lt / 0.07f);
            s[i] = Mathf.Sin(Mathf.Tau * f * lt) * env * 0.3f;
        }
        return MakeWav(s);
    }

    // キャンセル：下降する二音（A4→E4）。
    private AudioStreamWav SynthUiCancel()
    {
        float dur = 0.16f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = t < 0.06f ? 440f : 329.63f; // A4 → E4
            float lt = t < 0.06f ? t : t - 0.06f;
            float env = (lt < 0.003f ? lt / 0.003f : 1f) * Mathf.Exp(-lt / 0.08f);
            s[i] = Mathf.Sin(Mathf.Tau * f * lt) * env * 0.28f;
        }
        return MakeWav(s);
    }

    // 購入成功：明るい三音の達成（C5-E5-G5）。
    private AudioStreamWav SynthUiBuy()
    {
        float dur = 0.34f; int n = (int)(Rate * dur);
        var s = new float[n];
        float[] notes = { 523.25f, 659.25f, 783.99f }; // C5 E5 G5
        const float step = 0.08f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            int k = Mathf.Min(notes.Length - 1, (int)(t / step));
            float lt = t - k * step;
            float env = (lt < 0.003f ? lt / 0.003f : 1f) * Mathf.Exp(-lt / 0.13f);
            float tailT = Mathf.Max(0f, t - 2 * step);
            float tail = Mathf.Sin(Mathf.Tau * 783.99f * tailT) * Mathf.Exp(-tailT / 0.18f) * 0.1f;
            s[i] = Mathf.Sin(Mathf.Tau * notes[k] * lt) * env * 0.24f + tail;
        }
        FadeEnds(s, (int)(0.004f * Rate));
        return MakeWav(s);
    }

    // 購入失敗・拒否：低い鈍い「ブッ」（矩形寄り、減衰速い）。
    private AudioStreamWav SynthUiDeny()
    {
        float dur = 0.14f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t / 0.05f);
            float v = Mathf.Sin(Mathf.Tau * 155f * t);
            float sq = v >= 0f ? 1f : -1f;
            s[i] = (0.5f * v + 0.5f * sq) * env * 0.26f;
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

    // ───────── BgmMenu（タイトル/ハブ/ショップ/設定/難易度選択）─────────
    // 同じ M.I.N.A. モチーフを、遅いテンポ・薄い編成・長3度寄りの温かい和声で。
    //   進行 C - Am - F - G（I-vi-IV-V＝穏やかなカノン系）。各小節3秒＝ゆったり12秒ループ。
    //   メロディは2小節に1音だけ置き、間（ま）を多く取って耳障りにしない（プレイヤーが長く居る）。
    private AudioStreamWav BuildBgmMenu()
    {
        const float bar = 3.0f; const int bars = 4;
        int barN = (int)(Rate * bar), n = barN * bars;
        var s = new float[n];

        // [bass, 三和音1, 三和音2, 三和音3]（Hz）。明るめ＝メジャー始まり。
        float[][] chords =
        {
            new[] { 130.81f, 261.63f, 329.63f, 392.00f }, // C
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
            new[] {  87.31f, 174.61f, 220.00f, 261.63f }, // F
            new[] {  98.00f, 196.00f, 246.94f, 293.66f }, // G
        };
        // モチーフ（ド ミ レ ソ）を1オクターブ下げ、偶数小節だけ鳴らす＝薄く、ぽつりと。
        float[] motif = { 261.63f, 329.63f, 293.66f, 392.00f };

        for (int b = 0; b < bars; b++)
        {
            float[] ch = chords[b];
            int baseI = b * barN;
            for (int i = 0; i < barN; i++)
            {
                float t = (float)i / Rate;
                // パッド：三和音の、ごく緩いスウェル（立ち上がり長め＝呼吸のよう）。
                float pad = (Mathf.Sin(Mathf.Tau * ch[1] * t) + Mathf.Sin(Mathf.Tau * ch[2] * t)
                           + Mathf.Sin(Mathf.Tau * ch[3] * t)) * Swell(t, bar, 0.6f, 0.8f) * 0.085f;
                // ベース：根音、やわらかく。
                float bass = Mathf.Sin(Mathf.Tau * ch[0] * t) * Swell(t, bar, 0.2f, 0.6f) * 0.13f;
                // メロディ：偶数小節のみ、モチーフ1音をフルートで（間を活かす）。
                float mel = (b % 2 == 0)
                    ? Mathf.Sin(Mathf.Tau * motif[b] * t) * Swell(t, bar, 0.3f, 1.2f) * 0.07f
                    : 0f;
                s[baseI + i] = (pad + bass + mel) * 0.38f;
            }
        }
        FadeEnds(s, (int)(0.008f * Rate));

        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // ───────── BgmBoss（ボス戦）─────────
    // 同じモチーフを「未完／不協和」に。短調寄り進行・密度高め・強いベース。各小節1.6秒＝速い6.4秒ループ。
    //   進行 Am - Dm - E - Am（i-iv-V-i＝和声的短調＝緊張と解決の反復）。
    //   モチーフは速い八分の刻みで反復し、半音上のテンション音を薄く重ねて濁す。
    private AudioStreamWav BuildBgmBoss()
    {
        const float bar = 1.6f; const int bars = 4;
        int barN = (int)(Rate * bar), n = barN * bars;
        var s = new float[n];

        float[][] chords =
        {
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
            new[] { 146.83f, 293.66f, 349.23f, 440.00f }, // Dm
            new[] { 164.81f, 329.63f, 415.30f, 493.88f }, // E (長3度 G#=415 で導音の緊張)
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
        };
        // モチーフ（ド ミ レ ソ）。短調文脈に置くことで同じ音形が「翳る」。
        float[] motif = { 523.25f, 659.25f, 587.33f, 783.99f };

        for (int b = 0; b < bars; b++)
        {
            float[] ch = chords[b];
            int baseI = b * barN;
            for (int i = 0; i < barN; i++)
            {
                float t = (float)i / Rate;
                // ベース：根音＋オクターブ下を強く（緊張の地盤）。八分で踏む脈動。
                float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Mathf.Tau * (1f / 0.2f) * t)); // 八分の刻み
                float bass = (Mathf.Sin(Mathf.Tau * ch[0] * t) + 0.5f * Mathf.Sin(Mathf.Tau * ch[0] * 0.5f * t))
                           * Swell(t, bar, 0.03f, 0.2f) * pulse * 0.2f;
                // パッド：三和音、やや硬め（密度を上げる）。
                float pad = (Mathf.Sin(Mathf.Tau * ch[1] * t) + Mathf.Sin(Mathf.Tau * ch[2] * t)
                           + Mathf.Sin(Mathf.Tau * ch[3] * t)) * Swell(t, bar, 0.1f, 0.3f) * 0.075f;
                // 刻みアルペジオ：八分で三和音を速く巡る（密度＝緊張）。
                float arp = ArpFast(t, ch) * 0.07f;
                // メロディ：モチーフ1音＋半音上のテンションを薄く重ね、不協和で「未完」に。
                float mel = (Mathf.Sin(Mathf.Tau * motif[b] * t)
                           + 0.22f * Mathf.Sin(Mathf.Tau * motif[b] * 1.0595f * t)) // 半音上＝濁り
                           * Swell(t, bar, 0.06f, 0.4f) * 0.07f;
                s[baseI + i] = (bass + pad + arp + mel) * 0.4f;
            }
        }
        FadeEnds(s, (int)(0.006f * Rate));

        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // 八分音符（0.2s）で三和音3音を速く巡る、やや硬めのプラック（ボスの密度用）。
    private static float ArpFast(float t, float[] ch)
    {
        const float step = 0.2f;
        int k = (int)(t / step);
        float lt = t - k * step;
        float f = ch[1 + (k % 3)];
        float env = Mathf.Exp(-lt / 0.08f) * (lt < 0.004f ? lt / 0.004f : 1f);
        float v = Mathf.Sin(Mathf.Tau * f * lt);
        float sq = v >= 0f ? 1f : -1f;
        return (0.8f * v + 0.2f * sq) * env; // 矩形を少し混ぜて硬く
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
