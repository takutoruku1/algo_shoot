using Godot;
using System;
using System.Linq;
using System.Reflection;

// 文字送り音「ピッ」（Audio.SynthTypeBeep）の数値検証。音は聴けないので波形を数値で見る。
//   ①尺／立ち上がり／減衰／基本周波数／ピーク／RMS
//   ②話者ごとの実効ピッチ・尺・音量（VoiceOf）と UI 決定音との実効レベル比較
//   ③抑揚（Prosody）：「？」で語尾が上がり、それ以外はほぼ一定か
//   ④32 文字/秒で連続再生したとき Voice プールが枯れないか（空き無しでの横取り回数）
public partial class TypeBeepQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Rate = 44100;
    private int _fail;

    private void Check(bool ok, string message)
    {
        if (!ok) _fail++;
        GD.Print("[TypeBeepQA] " + (ok ? "PASS " : "FAIL ") + message);
    }

    private static float[] Decode(AudioStreamWav w)
    {
        var d = w.Data; var s = new float[d.Length / 2];
        for (int i = 0; i < s.Length; i++) s[i] = (short)(d[i * 2] | (d[i * 2 + 1] << 8)) / 32768f;
        return s;
    }

    private static float Db(float v) => 20f * Mathf.Log(Mathf.Max(v, 1e-9f)) / Mathf.Log(10f);

    private static float Rms(float[] s, int from, int to)
    {
        double acc = 0; to = Math.Min(to, s.Length);
        for (int i = from; i < to; i++) acc += s[i] * s[i];
        return (float)Math.Sqrt(acc / Math.Max(1, to - from));
    }

    // 1ms 窓のピーク包絡
    private static float[] Envelope(float[] s)
    {
        int w = Rate / 1000; var e = new float[s.Length / w];
        for (int k = 0; k < e.Length; k++)
            for (int i = k * w; i < (k + 1) * w; i++) e[k] = Math.Max(e[k], Math.Abs(s[i]));
        return e;
    }

    // 自己相関で基本周波数
    private static float F0(float[] s)
    {
        int n = Math.Min(s.Length, Rate * 20 / 1000);
        int best = 0; double bestV = double.MinValue;
        for (int lag = Rate / 3000; lag < Rate / 100; lag++)
        {
            double acc = 0;
            for (int i = 0; i + lag < n; i++) acc += s[i] * s[i + lag];
            acc /= (n - lag);
            if (acc > bestV) { bestV = acc; best = lag; }
        }
        return (float)Rate / best;
    }

    private (float durMs, float atkMs, float decayMs, float f0, float peak, float rms) Measure(string name, AudioStreamWav w)
    {
        var s = Decode(w); var e = Envelope(s);
        float peak = s.Max(Math.Abs);
        int pk = Array.IndexOf(e, e.Max());
        int atk = Array.FindIndex(e, v => v >= peak * 0.9f);
        int dec = pk; while (dec < e.Length && e[dec] > peak * 0.1f) dec++;
        float durMs = s.Length * 1000f / Rate;
        float f0 = F0(s);
        float rms = Rms(s, 0, s.Length);
        GD.Print($"[TypeBeepQA] {name}: dur={durMs:F1}ms attack(->90%)={atk}ms peak@{pk}ms decay(peak->-20dB)={dec - pk}ms " +
                 $"f0={f0:F0}Hz peak={peak:F3} ({Db(peak):F1}dBFS) rms={rms:F3} ({Db(rms):F1}dBFS) end={s[^1]:F4}");
        return (durMs, atk, dec - pk, f0, peak, rms);
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            var audio = Audio.Instance!;
            audio.Muted = false;

            // ① 波形
            var beep = Measure("TypBoy(=Mina=Boss)", audio.TypBoy);
            var narr = Measure("TypNarr", audio.TypNarr);
            var ok = Measure("SfxUiConfirm", audio.SfxUiConfirm);
            var mv = Measure("SfxUiMove", audio.SfxUiMove);
            Check(ReferenceEquals(audio.TypBoy, audio.TypMina) && ReferenceEquals(audio.TypBoy, audio.TypBoss), "speakers share one beep");
            Check(beep.durMs >= 25 && beep.durMs <= 40, $"beep duration {beep.durMs:F1}ms in 25-40");
            Check(narr.durMs < beep.durMs, $"narration beep shorter ({narr.durMs:F1} < {beep.durMs:F1})");
            Check(beep.atkMs <= 2, "attack <= 2ms");
            Check(Math.Abs(beep.f0 - 880f) < 15f, $"f0 ~880Hz ({beep.f0:F0})");
            Check(Math.Abs(audio.TypBoy.Data[^1]) + Math.Abs(audio.TypBoy.Data[^2]) < 4, "ends at zero (no click)");

            // 倍音（25% パルス＝2,3倍音が立つ）を DFT で確認
            var bs = Decode(audio.TypBoy);
            foreach (int k in new[] { 1, 2, 3, 4, 5 })
            {
                double re = 0, im = 0; int n = Rate * 10 / 1000;
                for (int i = 0; i < n; i++) { double ph = Math.Tau * 880 * k * i / Rate; re += bs[i] * Math.Cos(ph); im += bs[i] * Math.Sin(ph); }
                GD.Print($"[TypeBeepQA] harmonic {k} ({880 * k}Hz): {Db((float)(Math.Sqrt(re * re + im * im) * 2 / n)):F1}dB");
            }

            // ② 話者ごとの実効値
            var voiceOf = typeof(Audio).GetMethod("VoiceOf", Private)!;
            float confirmLvl = Db(Rms(Decode(audio.SfxUiConfirm), 0, Rate * 60 / 1000)) - 14f;
            float moveLvl = Db(Rms(Decode(audio.SfxUiMove), 0, audio.SfxUiMove.Data.Length / 2)) - 20f;
            GD.Print($"[TypeBeepQA] UiConfirm level (rms first 60ms + -14dB) = {confirmLvl:F1}dB ; UiMove (rms + -20dB) = {moveLvl:F1}dB");
            foreach (Hud.LineKind kind in Enum.GetValues(typeof(Hud.LineKind)))
            {
                object tone = voiceOf.Invoke(audio, new object[] { kind })!;
                var st = (AudioStreamWav)tone.GetType().GetProperty("Stream")!.GetValue(tone)!;
                float db = (float)tone.GetType().GetProperty("Db")!.GetValue(tone)!;
                float pitch = (float)tone.GetType().GetProperty("Pitch")!.GetValue(tone)!;
                var ss = Decode(st);
                float lvl = Db(Rms(ss, 0, ss.Length)) + db;
                float ms = ss.Length * 1000f / Rate / pitch;
                GD.Print($"[TypeBeepQA] {kind,-10} vol={db:F0}dB pitch={pitch:F3} ({12 * Math.Log2(pitch):+0.0;-0.0}st) f0={880 * pitch:F0}Hz dur={ms:F1}ms level={lvl:F1}dB (vs UiConfirm {lvl - confirmLvl:+0.0;-0.0}dB)");
                Check(ms >= 20 && ms <= 40, $"{kind} played duration {ms:F1}ms");
            }

            // ③ 抑揚
            var prosody = typeof(Audio).GetMethod("Prosody", Private)!;
            foreach (var line in new[] { "それって、本当なの？", "わかった。もう行くね。", "……ごめん…", "ありがとう！" })
            {
                var st = new System.Text.StringBuilder();
                float min = 99, max = -99;
                for (int i = 0; i < line.Length; i++)
                {
                    var r = prosody.Invoke(audio, new object[] { line, i })!;
                    float semi = (float)r.GetType().GetField("Item1")!.GetValue(r)!;
                    st.Append($"{semi:+0.00;-0.00} ");
                    if (i < line.Length * 0.5f) { min = Math.Min(min, semi); max = Math.Max(max, semi); }
                }
                GD.Print($"[TypeBeepQA] prosody \"{line}\": {st}");
                Check(max - min <= 0.2f, $"first half of \"{line}\" is flat (spread {max - min:F2}st)");
            }
            var q = prosody.Invoke(audio, new object[] { "本当なの？", 4 })!;
            Check((float)q.GetType().GetField("Item1")!.GetValue(q)! > 2.0f, "question rises at the tail");

            // ④ 32 文字/秒の連打でプールが枯れないか（stride 1＝毎文字、stride 2）
            var pool = (System.Collections.Generic.List<AudioStreamPlayer>)typeof(Audio).GetField("_voicePool", Private)!.GetValue(audio)!;
            // まず再生が実際に終わる（Playing が落ちる）環境かを確かめる
            audio.PlayType(Hud.LineKind.Other, "テスト", 0);
            await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
            bool drains = pool.All(p => !p.Playing);
            Check(drains, "players stop after the beep ends (audio driver is mixing)");
            foreach (int stride in new[] { 1, 2 })
            {
                int calls = 0, steals = 0, maxBusy = 0;
                double t0 = Time.GetTicksMsec() / 1000.0, next = 0; int idx = 0;
                const string line = "ずっと同じ速さで文字が流れていくときに、送り音が団子にならないか確かめる行。";
                while (Time.GetTicksMsec() / 1000.0 - t0 < 3.0)
                {
                    double now = Time.GetTicksMsec() / 1000.0 - t0;
                    while (next <= now)
                    {
                        if (idx % stride == 0)
                        {
                            int busy = pool.Count(p => p.Playing);
                            maxBusy = Math.Max(maxBusy, busy);
                            if (busy >= pool.Count) steals++;
                            audio.PlayType(Hud.LineKind.Other, line, idx % line.Length);  // 最長（ボス 38ms）で検証
                            calls++;
                        }
                        idx++; next += 1.0 / 32.0;
                    }
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                GD.Print($"[TypeBeepQA] 32cps stride={stride}: calls={calls} maxBusyBeforeCall={maxBusy}/{pool.Count} steals={steals}");
                Check(steals == 0, $"32cps stride {stride}: no voice player stolen");
            }
            audio.StopVoice();
        }
        catch (Exception e) { _fail++; GD.PrintErr("[TypeBeepQA] EXCEPTION " + e); }
        GD.Print(_fail == 0 ? "[TypeBeepQA] ALL PASS" : $"[TypeBeepQA] {_fail} FAIL");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }
}
