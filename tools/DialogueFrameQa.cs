using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// 文字送りの「カクつき」を実時間で測る（窓あり専用。ヘッドレスでは描画が回らない）。
//   プロローグ／ハブ／本編ボス戦の会話を AUTO で流し、文字送り中の毎フレームについて
//   delta・shown の増分・画面のインク量（DialogueBox.VisibleInk）の増分・前フレームが描かれたか を記録する。
//   見た目が止まったフレーム（インクが増えない）は、止まった位置で原因を分ける：
//     authored＝台本の Lead/Beat（DialoguePacing）／implicit＝行頭・句読点の直後／other＝それ以外。
//   合格条件：止まるのは authored だけ／文字送り中のフレームは毎回描かれている。
//   delta の跳ね（>25ms）は機械の都合も混ざるので数字を出すだけ。
public partial class DialogueFrameQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class Probe
    {
        public string Name = "";
        public Func<string> Page = () => "";
        public Func<double> Shown = () => 0;
        public Func<DialoguePacing.Page?> Pacing = () => null;
        public CanvasItem Canvas = null!;
        public Func<bool> Active = () => true;
        public Action? Nudge;   // 選択肢など、会話の途中で止まる所を毎フレーム押し進める
        public Action? Skip;    // 送り途中で全文即表示（送りボタン1回目と同じ）にする
    }

    private sealed class Stats
    {
        public int Frames, Spikes, ZeroShown, Undrawn;
        public double MaxDelta, SumDelta, MaxProcess;
        public readonly Dictionary<string, int> Stalls = new() { ["authored"] = 0, ["implicit"] = 0, ["other"] = 0 };
        public readonly Dictionary<string, double> StallTime = new() { ["authored"] = 0, ["implicit"] = 0, ["other"] = 0 };
        public readonly List<string> Samples = new();
        public readonly List<string> SpikeSamples = new();
        // 送り音：文字が出たフレームで1回／出ないフレームで0回か。全文即表示のフレームで鳴らないか。
        public int Sounds, SoundMismatch, SkipChecked, SkipSounded;
        public string SoundSample = "";
    }

    private Probe? _probe;
    private Stats _stats = new();
    private string? _prevPage;
    private double _prevShown;
    private int _draws;
    private int _soundMark;
    private bool _skipPending, _skipDone;
    private GameManager _game = null!;
    private bool _cold;
    private int _warmedMark;
    private int _totalSpikes, _glyphSpikes;
    private ulong _pendingAt;
    private string _pendingScene = "";
    private readonly List<string> _failures = new();

    private static object? Get(object obj, string name)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Private);
            if (field != null) return field.GetValue(obj);
            var property = type.GetProperty(name, Private);
            if (property != null) return property.GetValue(obj);
        }
        throw new Exception($"{obj.GetType().Name}.{name} not found");
    }

    private static void Set(object obj, string name, object value)
        => obj.GetType().GetField(name, Private)!.SetValue(obj, value);

    // 二択は決めるまで AUTO でも止まる＝決定を押して先へ（会話の送りには押さない）。
    private static void PressChoice(object scene, string field)
    {
        if (Get(scene, field) is ChoiceOverlay { Decided: false } && !Input.IsActionPressed("ui_accept"))
            Input.ActionPress("ui_accept");
        else Input.ActionRelease("ui_accept");
    }

    public override async void _Ready()
    {
        ProcessPriority = 10000;   // 会話を進める側（優先度0）の後で読む
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            if (DisplayServer.GetName() == "headless") throw new Exception("run with a window: the probe needs real draws");
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/")) throw new Exception("not isolated saves");
            _game = GetNode<GameManager>("/root/Game");
            _game.AutoSaveEnabled = false;
            _game.MsgCharsPerSec = DialogueBox.DefaultCharsPerSec;
            _game.AutoAdvanceDialog = true;
            Hud.SkipLatched = false;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);

            // 字形の先作り（GlyphWarmer）。`-- cold` で切ると、温めない場合（修正前）の数字が取れる。
            _cold = OS.GetCmdlineUserArgs().Contains("cold");
            GlyphWarmer.Enabled = !_cold;
            GD.Print($"[DialogueFrame] glyph warmer {(_cold ? "OFF (cold)" : "ON")}, budget {GlyphWarmer.BudgetMs}ms/frame");
            // タイトルの裏：かな・英数・約物。
            GlyphWarmer.WarmBase(this);
            await DrainWarmer("base set (title)");

            // ── プロローグ（会話フェーズ3まで実時間で待つ）
            await Loading("res://Prologue.tscn");
            var prologue = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(prologue);
            GetTree().CurrentScene = prologue;
            for (int i = 0; i < 1200 && (int)Get(prologue, "_phase")! != 3; i++) await Frames(1);
            if ((int)Get(prologue, "_phase")! != 3) throw new Exception("prologue did not reach the talk phase");
            await Measure(new Probe
            {
                Name = "prologue", Canvas = prologue,
                Page = () => (string)Get(prologue, "CurPage")!,
                Shown = () => (double)Get(prologue, "_reveal")!,
                Pacing = () => (DialoguePacing.Page?)Get(prologue, "CurPacing"),
                Active = () => (bool)Get(prologue, "TalkBoxShown")!,
                Nudge = () => PressChoice(prologue, "_choice"),
                Skip = () => Set(prologue, "_reveal", (double)((string)Get(prologue, "CurPage")!).Length),
            }, 30);
            prologue.QueueFree();
            await Frames(3);

            // ── ハブ（雑談の会話を直接開く）
            await Loading("res://Hub.tscn");
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(10);
            var talks = ((string, string)[][])typeof(Hub).GetField("SmallTalks", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            typeof(Hub).GetMethod("StartDialogue", Private)!.Invoke(hub,
                new object?[] { talks.SelectMany(t => t).ToArray(), null, true,
                    Enum.Parse(typeof(Hub).GetNestedType("Mode", BindingFlags.NonPublic)!, "Cards"), null, null });
            await Measure(new Probe
            {
                Name = "hub", Canvas = hub,
                Page = () => (string)Get(hub, "DlgCurPage")!,
                Shown = () => (double)Get(hub, "_dlgReveal")!,
                Pacing = () => (DialoguePacing.Page?)Get(hub, "DlgPacing"),
                Active = () => Get(hub, "_mode")!.ToString() == "Dialogue",
                Skip = () => Set(hub, "_dlgReveal", (double)((string)Get(hub, "DlgCurPage")!).Length),
            }, 24);
            hub.QueueFree();
            await Frames(3);

            // ── 本編ボス戦（あかり戦のボス入口＝開幕の会話）
            await Loading("res://Akari.tscn");
            _game.SelectedJob = Job.Tank;
            _game.SelectedEntry = GameManager.StageEntry.Boss;
            _game.Difficulty = GameManager.Diff.Normal;
            var stage = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(stage);
            GetTree().CurrentScene = stage;
            var hud = stage.GetNode<Hud>("Hud");
            await Measure(new Probe
            {
                Name = "boss", Canvas = (CanvasItem)Get(hud, "_canvas")!,
                Page = () => (string)Get(hud, "CurPageText")!,
                Shown = () => (float)Get(hud, "_dlgRevealed")!,
                Pacing = () => (DialoguePacing.Page?)Get(hud, "CurPacing"),
                Active = () => (double)Get(hud, "_messageTimer")! > 0,
                Skip = () => hud.RevealDialogNow(),
            }, 24);
            stage.QueueFree();
            await Frames(3);

            // ── 最終決戦後の対話（Final の会話フェーズ1）とエピローグ（見上げる語り）
            await Loading("res://Final.tscn");
            var final = GD.Load<PackedScene>("res://Final.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(final);
            GetTree().CurrentScene = final;
            for (int i = 0; i < 900 && (int)Get(final, "_phase")! != 1; i++) await Frames(1);
            await Measure(new Probe
            {
                Name = "final", Canvas = final,
                Page = () => (string)Get(final, "CurPage")!,
                Shown = () => (double)Get(final, "_reveal")!,
                Pacing = () => (DialoguePacing.Page?)Get(final, "CurPacing"),
                Active = () => (int)Get(final, "_phase")! == 1 && Get(final, "_choice") == null,
                Nudge = () => PressChoice(final, "_choice"),
                Skip = () => Set(final, "_reveal", (double)((string)Get(final, "CurPage")!).Length),
            }, 20);
            final.QueueFree();
            await Frames(3);
            await Loading("res://Epilogue.tscn");
            var epilogue = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(epilogue);
            GetTree().CurrentScene = epilogue;
            await Measure(new Probe
            {
                Name = "epilogue", Canvas = epilogue,
                Page = () => (string)Get(epilogue, "CurPage")!,
                Shown = () => (double)Get(epilogue, "_reveal")!,
                Pacing = () => (DialoguePacing.Page?)Get(epilogue, "CurPacing"),
                Active = () => (bool)Get(epilogue, "TalkBoxShown")!,
                Skip = () => Set(epilogue, "_reveal", (double)((string)Get(epilogue, "CurPage")!).Length),
            }, 20);
            epilogue.QueueFree();
            await Frames(3);

            await CheckFilmSounds(new OpeningFilm(), 2);
            await CheckFilmSounds(new OpeningFilm(), 9);
            await CheckFilmSounds(new EndingFilm(), 2);

            await MeasurePageCost(talks.SelectMany(t => t).Select(l => l.Item2).Distinct().ToList());

            GD.Print($"[DialogueFrame] warmer total: {GlyphWarmer.Warmed} glyphs, {GlyphWarmer.SpentMs / 1000:F2}s of frame time, " +
                $"{GlyphWarmer.Warmed / Math.Max(1, GlyphWarmer.SpentMs) * 1000:F0} glyphs/s while working");
            GD.Print($"[DialogueFrame] reveal frames over 25ms in all dialogue scenes: {_totalSpikes} ({(_cold ? "cold" : "warmed")}), " +
                $"of which on a page with unwarmed glyphs or while the warmer worked: {_glyphSpikes}");
            // 字形が原因と言えるもの（その頁に温めていない字がある／そのフレームで温めが働いた）だけを縛る。
            //   それ以外（シーン開始直後の初回描画など）は数字を出して報告に回す。
            if (!_cold && _glyphSpikes > 0) _failures.Add($"{_glyphSpikes} reveal frames over 25ms are still caused by glyph generation");
            if (_failures.Count > 0) throw new Exception("FAIL\n  " + string.Join("\n  ", _failures));
            GD.Print("[DialogueFrame] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Measure(Probe probe, double seconds)
    {
        _stats = new Stats();
        _prevPage = null;
        _skipPending = _skipDone = false;
        _soundMark = DialogueBox.TypeSounds;
        probe.Canvas.Draw += CountDraw;
        _probe = probe;
        ulong start = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - start < seconds * 1000) await Frames(1);
        _probe = null;
        Input.ActionRelease("ui_accept");
        probe.Canvas.Draw -= CountDraw;
        var s = _stats;
        GD.Print($"[DialogueFrame] {probe.Name}: reveal frames={s.Frames} mean delta={(s.Frames > 0 ? s.SumDelta / s.Frames * 1000 : 0):F2}ms " +
            $"max delta={s.MaxDelta * 1000:F1}ms delta>25ms={s.Spikes} shown+0={s.ZeroShown} undrawn={s.Undrawn} max process={s.MaxProcess * 1000:F2}ms");
        GD.Print($"[DialogueFrame] {probe.Name}: stall frames authored={s.Stalls["authored"]} ({s.StallTime["authored"]:F2}s) " +
            $"implicit={s.Stalls["implicit"]} ({s.StallTime["implicit"]:F2}s) other={s.Stalls["other"]} ({s.StallTime["other"]:F2}s)");
        foreach (string sample in s.Samples.Take(12)) GD.Print($"[DialogueFrame]   {probe.Name} stall at {sample}");
        foreach (string sample in s.SpikeSamples.Take(12)) GD.Print($"[DialogueFrame]   {probe.Name} spike {sample}");
        GD.Print($"[DialogueFrame] {probe.Name}: type sounds={s.Sounds} mismatched frames={s.SoundMismatch}{s.SoundSample} " +
            $"skip frames={s.SkipChecked} sounded on skip={s.SkipSounded}");
        int before = _failures.Count;
        if (s.Sounds < 30 || s.SoundMismatch > 0)
            _failures.Add($"{probe.Name}: type sounds do not follow the characters ({s.Sounds} sounds, {s.SoundMismatch} frames off)");
        if (s.SkipChecked == 0 || s.SkipSounded > 0)
            _failures.Add($"{probe.Name}: reveal-now {(s.SkipChecked == 0 ? "was not exercised" : "played the type sound")}");
        if (s.Frames < 120) _failures.Add($"{probe.Name}: only {s.Frames} reveal frames were observed");
        if (s.Stalls["implicit"] + s.Stalls["other"] > 0)
            _failures.Add($"{probe.Name}: text stopped outside authored beats ({s.Stalls["implicit"]} implicit, {s.Stalls["other"]} other frames)");
        _totalSpikes += s.Spikes;
        if (s.Undrawn > 0) _failures.Add($"{probe.Name}: {s.Undrawn} reveal frames were not redrawn");
        if (_failures.Count == before)
            GD.Print($"[DialogueFrame] PASS {probe.Name}: reveal moves every frame except authored beats, and every frame is drawn");
    }

    private void CountDraw() => _draws++;

    // LoadingScreen.Open と同じく行き先の台本を温め始め、暗転が最短で明ける 1.15 秒だけ待つ（温め終わりは待たない）。
    private async Task Loading(string scene)
    {
        int before = GlyphWarmer.Warmed;
        GlyphWarmer.WarmScene(this, scene);
        int queued = GlyphWarmer.PendingCount;
        ulong start = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - start < 1150) await Frames(1);
        GD.Print($"[DialogueFrame] {System.IO.Path.GetFileNameWithoutExtension(scene)}: {queued} glyphs queued on loading, " +
            $"{GlyphWarmer.Warmed - before} made during the 1.15s loading screen, {GlyphWarmer.PendingCount} left when the scene opens");
        _pendingAt = GlyphWarmer.PendingCount > 0 ? Time.GetTicksMsec() : 0;
        _pendingScene = System.IO.Path.GetFileNameWithoutExtension(scene);
    }

    // 待ち行列が空になるまで回し、所要時間とその間のフレームの重さを出す。
    private async Task DrainWarmer(string label)
    {
        int before = GlyphWarmer.Warmed, frames = 0, heavy = 0;
        double spentBefore = GlyphWarmer.SpentMs, worst = 0;
        ulong start = Time.GetTicksMsec(), last = Time.GetTicksUsec();
        while (GlyphWarmer.PendingCount > 0 && !_cold && Time.GetTicksMsec() - start < 60_000)
        {
            await Frames(1);
            double frame = (Time.GetTicksUsec() - last) / 1000.0;
            last = Time.GetTicksUsec();
            frames++;
            worst = Math.Max(worst, frame);
            if (frame > 25) heavy++;
        }
        GD.Print($"[DialogueFrame] warm {label}: {GlyphWarmer.Warmed - before} glyphs in {(Time.GetTicksMsec() - start) / 1000.0:F2}s " +
            $"({frames} frames, warmer {(GlyphWarmer.SpentMs - spentBefore):F0}ms; worst frame {worst:F1}ms, frames>25ms {heavy})");
    }

    // 映画の字幕は時間で決まる＝ 1/60 秒ずつ手で送り、字幕に文字が出た回数と送り音の回数を突き合わせる。
    private async Task CheckFilmSounds(Node2D film, int shot)
    {
        GetTree().Root.AddChild(film);
        film.SetProcess(false);
        await Frames(2);
        var type = film.GetType();
        var cuts = (double[])type.GetField("Cuts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var pages = ((List<List<string>>)Get(film, "_captionPages")!)[shot];
        var pacing = ((List<DialoguePacing.Page[]>)Get(film, "_captionPacing")!)[shot];
        double lead = film is OpeningFilm
            ? (double)type.GetMethod("CaptionStart", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { shot })!
            : 0.25;
        type.GetProperty("Elapsed")!.SetValue(film, cuts[shot]);
        int expected = 0, before = DialogueBox.TypeSounds, mismatched = 0;
        var previous = DialogueBox.CaptionAt(pages, -1, pacing);
        for (double t = 0; t < cuts[shot + 1] - cuts[shot] - 0.05; t += 1.0 / 60)
        {
            int mark = DialogueBox.TypeSounds;
            film._Process(1.0 / 60);
            var now = DialogueBox.CaptionAt(pages, (double)type.GetProperty("Elapsed")!.GetValue(film)! - cuts[shot] - lead, pacing);
            int from = ReferenceEquals(previous.Page, now.Page) ? (int)previous.Shown : 0, to = (int)now.Shown;
            int want = to > from && !char.IsWhiteSpace(now.Page[to - 1]) ? 1 : 0;
            expected += want;
            if (DialogueBox.TypeSounds - mark != want) mismatched++;
            previous = now;
        }
        int sounds = DialogueBox.TypeSounds - before;
        GD.Print($"[DialogueFrame] {type.Name} cut {shot}: type sounds={sounds} expected={expected} mismatched frames={mismatched}");
        if (expected < 8 || mismatched > 0)
            _failures.Add($"{type.Name} cut {shot}: caption sounds do not follow the characters ({sounds} for {expected}, {mismatched} frames off)");
        else GD.Print($"[DialogueFrame] PASS {type.Name} cut {shot}: one type sound per character that appears");
        film.QueueFree();
        Audio.Instance?.StopMusic(0);
        await Frames(2);
    }

    // 新しいページの最初の描画で何が重いかを分ける：初出の字形（フォントキャッシュへのラスタライズ）か、
    //   ページの組版（TextLine のシェイプ）か。同じページを2巡目に描けば字形は温まっていて組版だけが残る。
    private async Task MeasurePageCost(List<string> lines)
    {
        var canvas = new DialogueCostQaCanvas();
        AddChild(canvas);
        await Frames(2);
        float width = DialogueBox.WrapWidth(DialogueBox.FullScreen);
        foreach (string pass in new[] { "first time", "again" })
        {
            double sum = 0, max = 0, shapeSum = 0;
            var fresh = new[] { "SnsIntro", "NotYetDialog", "AccountIntroAkari" }
                .SelectMany(n => ((string, string)[])typeof(Hub).GetField(n, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)
                .Select(l => l.Item2).ToList();
            foreach (string line in fresh)
            {
                if (pass == "first time")
                {
                    // 段落を一度シェイプするだけ（＝初出の字形の生成）にどれだけ掛かるかを先に分けて測る。
                    ulong s0 = Time.GetTicksUsec();
                    UiKit.TextW(DialogueBox.Body.Font, line.Replace("\n", ""), DialogueBox.Body.Size);
                    shapeSum += (Time.GetTicksUsec() - s0) / 1000.0;
                }
                ulong p0 = Time.GetTicksUsec();
                DialogueBox.Paginate(line, width);
                double ms = (Time.GetTicksUsec() - p0) / 1000.0;
                sum += ms; max = Math.Max(max, ms);
            }
            GD.Print($"[DialogueFrame] paginate cost ({pass}, {fresh.Count} lines): mean={sum / fresh.Count:F2}ms max={max:F2}ms; one shaping of the whole line beforehand mean={shapeSum / fresh.Count:F2}ms");
        }
        var pages = lines.SelectMany(l => DialogueBox.Paginate(l, width)).ToList();
        foreach (string pass in new[] { "shape only, first char", "all glyphs, first time", "all glyphs, warm" })
        {
            double drawSum = 0, drawMax = 0, frameMax = 0;
            int frameSpikes = 0;
            foreach (string page in pages)
            {
                canvas.Page = page;
                canvas.Shown = pass.StartsWith("shape") ? 1 : page.Length;
                canvas.QueueRedraw();
                ulong t0 = Time.GetTicksUsec();
                await Frames(1);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                double frame = (Time.GetTicksUsec() - t0) / 1000.0;
                drawSum += canvas.LastDrawMs;
                drawMax = Math.Max(drawMax, canvas.LastDrawMs);
                frameMax = Math.Max(frameMax, frame);
                if (frame > 25) frameSpikes++;
                canvas.Page = "";
                canvas.QueueRedraw();
                await Frames(1);
            }
            GD.Print($"[DialogueFrame] page cost ({pass}, {pages.Count} pages): DrawBody mean={drawSum / pages.Count:F2}ms max={drawMax:F2}ms; " +
                $"frame max={frameMax:F1}ms frames>25ms={frameSpikes}");
        }
        canvas.QueueFree();
        await Frames(2);
    }

    public override void _Process(double delta)
    {
        if (_pendingAt > 0 && GlyphWarmer.PendingCount == 0)
        {
            GD.Print($"[DialogueFrame] {_pendingScene}: the rest of its script finished warming {(Time.GetTicksMsec() - _pendingAt) / 1000.0:F2}s after the scene opened");
            _pendingAt = 0;
        }
        if (_probe == null) return;
        int warmed = GlyphWarmer.Warmed - _warmedMark;
        _warmedMark = GlyphWarmer.Warmed;
        int sounds = DialogueBox.TypeSounds - _soundMark;   // このフレームで会話側が鳴らした送り音
        _soundMark = DialogueBox.TypeSounds;
        _probe.Nudge?.Invoke();
        if (!_probe.Active()) { _prevPage = null; _draws = 0; return; }
        string page = _probe.Page();
        double shown = _probe.Shown();
        int draws = _draws;
        _draws = 0;
        if (_prevPage != null && _prevPage == page && _prevShown < page.Length && delta > 0.001)
        {
            var s = _stats;
            var pacing = _probe.Pacing();
            float speed = _game.MsgCharsPerSec;
            s.Frames++;
            s.SumDelta += delta;
            s.MaxDelta = Math.Max(s.MaxDelta, delta);
            s.MaxProcess = Math.Max(s.MaxProcess, Performance.GetMonitor(Performance.Monitor.TimeProcess));
            if (delta > 0.025)
            {
                s.Spikes++;
                // 原因の手がかり：このページで温めが済んでいない字の数／直前のフレームで温めが何字作ったか。
                int cold = page.Count(c => !char.IsWhiteSpace(c) && !GlyphWarmer.IsWarm(c));
                if (cold > 0 || warmed > 0) _glyphSpikes++;
                s.SpikeSamples.Add($"{delta * 1000:F1}ms at {_prevShown:F2}->{shown:F2}/{page.Length} unwarmed chars on page={cold} " +
                    $"warmer made {warmed} last frame process={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F2}ms \"{page.Replace('\n', '/')}\"");
            }
            if (shown <= _prevShown) s.ZeroShown++;
            if (draws == 0) s.Undrawn++;
            s.Sounds += sounds;
            if (_skipPending)
            {
                s.SkipChecked++;
                if (sounds > 0) s.SkipSounded++;
            }
            else
            {
                int from = (int)_prevShown, to = Math.Min((int)shown, page.Length);
                int want = to > from && !char.IsWhiteSpace(page[to - 1]) ? 1 : 0;
                if (sounds != want)
                {
                    s.SoundMismatch++;
                    if (s.SoundSample.Length == 0) s.SoundSample = $" (first at {from}->{to} of \"{page.Replace('\n', '/')}\": {sounds} for {want})";
                }
            }
            // 全文即表示の1フレームはインクが一気に増えるだけで止まりではないので、そのまま増分で判定してよい。
            double ink = DialogueBox.VisibleInk(page, shown, speed, pacing) - DialogueBox.VisibleInk(page, _prevShown, speed, pacing);
            if (ink < 1e-9 && shown < page.Length)
            {
                int index = (int)_prevShown;
                string cause = (pacing?.Before(index) ?? 0) > 0 || (pacing?.Before((int)shown) ?? 0) > 0 ? "authored"
                    : index == 0 || page[index - 1] is '、' or ',' or '。' or '！' or '？' or '!' or '?' or '…' or '‥' ? "implicit"
                    : "other";
                s.Stalls[cause]++;
                s.StallTime[cause] += delta;
                if (cause != "authored" && (s.Samples.Count == 0 || !s.Samples[^1].StartsWith(index + ":")))
                    s.Samples.Add($"{index}: {cause} \"{(index > 0 ? page[index - 1].ToString() : "^")}|{page[index]}\" in {page.Replace('\n', '/')}");
            }
        }
        _prevPage = page;
        _prevShown = shown;
        _skipPending = false;
        // 送りの途中で一度だけ全文即表示（送りボタン1回目）にし、次のフレームで送り音が鳴らないことを見る。
        if (_probe.Skip != null && !_skipDone && _stats.Frames > 150 && shown >= 2 && shown < page.Length - 4)
        {
            _probe.Skip();
            _skipDone = _skipPending = true;
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}

public partial class DialogueCostQaCanvas : Node2D
{
    public string Page = "";
    public double LastDrawMs, Shown = 1;

    public override void _Draw()
    {
        if (Page.Length == 0) return;
        UiKit.BeginDesign(this);
        ulong t0 = Time.GetTicksUsec();
        DialogueBox.DrawBody(this, DialogueBox.FullScreen, Page, Shown);
        LastDrawMs = (Time.GetTicksUsec() - t0) / 1000.0;
        UiKit.EndDesign(this);
    }
}
