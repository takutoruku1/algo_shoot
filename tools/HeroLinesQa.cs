using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// HeroLinesQa : 2026-09-26「主人公の存在」17 行（docs/20260926/主人公の存在_診断と本文 §3）の目視ドライバ。
//   配列を読むだけでなく、実際の経路（StageAkari.Step_Clear → ClearBeforeFor／BossMina.OnCryStart → Lines／
//   Epilogue の見上げ）で新しい行が画面に流れるところまで Z 送りで進め、PNG に残す（build/shots_hero/）。
//   画面に載った本文（Hud._dlgText）が設計文書の本文と一字違わないことも検める。
//   静的な並びの検査は NewTextQa（5）。こちらは「実際に流れる」ことの確認と撮影が役目。
//   実行: Godot --path . res://tools/qa_hero_lines.tscn（ウィンドウ表示。セーブを書くので APPDATA を隔離すること）
public partial class HeroLinesQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private int _fail;
    private string _out = "";

    private void Check(string label, bool ok, string detail = "")
    {
        if (!ok) _fail++;
        GD.Print($"[heroqa] {(ok ? "OK  " : "NG  ")}{label}{(ok ? "" : $" — {detail}")}");
    }
    private static T Read<T>(object o, string f) => (T)o.GetType().GetField(f, Private)!.GetValue(o)!;
    private static void Write(object o, string f, object v) => o.GetType().GetField(f, Private)!.SetValue(o, v);
    private static object? Call(object o, string m, params object?[] a) => o.GetType().GetMethod(m, Private | BindingFlags.Public)!.Invoke(o, a);

    public override async void _Ready()
    {
        var game = GetNode<GameManager>("/root/Game");
        try
        {
            Check("隔離セーブ（build/qa_story/ 配下）", OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), OS.GetUserDataDir());
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath("res://build/shots_hero");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            game.AutoSaveEnabled = false;
            game.AutoAdvanceDialog = false;
            game.MsgCharsPerSec = 300;
            await Frames(2);
            await AkariClear(game, hesitated: true);
            await AkariClear(game, hesitated: false);
            await FinalRei(game);
            await EpilogueKoharu(game);
        }
        catch (Exception e)
        {
            _fail++;
            GD.PrintErr($"[heroqa] EXCEPTION {e}");
        }
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Frames(5);
        GD.Print(_fail == 0 ? "[heroqa] ALL OK" : $"[heroqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // ── S1 クリア後（あかり 2 行＋ミナ「たぶん」。迷い秒ゲート有り／無し）──
    //   Step_Clear（step 14）へ直接置く＝バナー → ClearBeforeFor(game) → RunLinesInPlace の実経路で流れる。
    private async Task AkariClear(GameManager game, bool hesitated)
    {
        string tag = hesitated ? "hesitated" : "quick";
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        game.SelectedEntry = GameManager.StageEntry.Boss;
        game.RecordChoice("p2", "おはよう", Array.Empty<string>(), 3f);
        game.RecordChoice("s1_4", "十二件、ぜんぶ", Array.Empty<string>(), hesitated ? 9f : 1f);
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        var stage = root.GetChildren().OfType<Node>().Single(n => n.GetType().Name == "StageAkari");
        var hud = root.GetNode<Hud>("Hud");
        Write(stage, "_startBannerShown", true);
        Write(stage, "_step", 14);          // Step_Clear
        Write(stage, "_stepStarted", false);
        Write(stage, "_bossActive", false);
        await Frames(6);
        var before = Read<(int who, string text, string face)[]>(stage, "_clearBefore");
        string[] head = { "「ほんと、バカなんだから。……あたしも、だけど。」", "……あったかい声が、した。……知らない声なのに。変なの。" };
        string[] added = hesitated
            ? new[] { "……ねえ。あのとき、すぐ決めなかったでしょ。……うん。それで、いい。", "……知らない声のくせに。……言い方だけ、どこかで、聞いたことある。", "……言い方は、わたくしのです。……たぶん。" }
            : new[] { "……知らない声のくせに。……言い方だけ、どこかで、聞いたことある。", "……言い方は、わたくしのです。……たぶん。" };
        Check($"S1 クリア({tag}) フィルム前の並び", before.Select(l => l.text).SequenceEqual(head.Concat(added)), string.Join(" / ", before.Select(l => l.text)));
        Check($"S1 クリア({tag}) 会話が立っている", Read<bool>(stage, "_stepStarted") && Hud.BubblePaused && Read<int>(stage, "_clearPhase") == 0,
            $"started={Read<bool>(stage, "_stepStarted")} paused={Hud.BubblePaused} phase={Read<int>(stage, "_clearPhase")}");
        for (int i = head.Length; i < before.Length; i++)
        {
            bool reached = await AdvanceTo(() => Read<int>(stage, "_introLine") == i && hud.DialogRevealed);
            string shown = Read<string>(hud, "_dlgText");
            Check($"S1 クリア({tag}) 行{i} が画面に載る: {before[i].text}", reached && shown == before[i].text && Read<int>(stage, "_clearPhase") == 0, $"shown='{shown}'");
            await Frames(4);
            await Shot($"s1_clear_{tag}_{i - head.Length + 1}");
        }
        await Drop(root);
    }

    // ── FINAL F3（レイ「……あんたの言い方。……この人に、そっくりよ。」）──
    //   BossPostsQa と同じ据え付けで BossMina を立て、回想は済んだことにして OnCryStart を直接呼ぶ
    //   ＝BossMina.Lines の会話ドライバ（_seq）が実際に立ち、Z 送りで 3 行目に届く。
    private async Task FinalRei(GameManager game)
    {
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        var root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        var stage = root.GetNode("StageMina");
        stage.SetProcess(false);
        Write(stage, "_step", -1);
        Write(stage, "_startBannerShown", true);
        Write(stage, "_titleThump", true);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        player.GlobalPosition = new Vector2(Field.Left + 32, 104);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        var boss = new BossMina { Name = "BossMina" };
        world.AddChild(boss);
        Write(stage, "_boss", boss);
        Write(stage, "_bossActive", true);
        boss.GlobalPosition = new Vector2(Field.Right - 64, 104);
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        await Frames(6);
        boss.SetPhysicsProcess(false);
        var caster = Read<Node>(boss, "_caster");
        caster.SetProcess(false);
        Call(caster, "CancelPendingAttacks");
        Write(boss, "_memoryPlayed", true);
        typeof(BossMina).GetMethod("OnCryStart", Private)!.Invoke(boss, null);
        await Frames(4);
        Check("F3 会話ドライバが立つ（_seq, _line=0）", Read<bool>(boss, "_seq") && Read<int>(boss, "_line") == 0,
            $"seq={Read<bool>(boss, "_seq")} line={Read<int>(boss, "_line")}");
        var lines = ((int who, string text, string face)[])typeof(BossMina).GetField("Lines", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        const string want = "……あんたの言い方。……この人に、そっくりよ。";
        int at = Array.FindIndex(lines, l => l.text == want);
        Check("F3 レイの行が Lines の index 2", at == 2, $"index={at}");
        bool reached = await AdvanceTo(() => Read<int>(boss, "_line") == at && hud.DialogRevealed);
        string shown = Read<string>(hud, "_dlgText");
        string speaker = Read<string>(hud, "_dlgSpeaker");
        Check("F3 レイの行が画面に載る", reached && shown == want && speaker == "レイ" && Read<bool>(boss, "_seq"), $"shown='{shown}' speaker='{speaker}'");
        await Frames(4);
        await Shot("final_rei_sokkuri");
        await Drop(root);
    }

    // ── EPILOGUE E5b（こはる「……ミナの後ろの人も、聞いてるんでしょ。——ありがと、知らない人。」）──
    private async Task EpilogueKoharu(GameManager game)
    {
        game.ResetPersistent();
        game.LastSentWord = "おかえり";
        var ep = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
        GetTree().Root.AddChild(ep);
        GetTree().CurrentScene = ep;
        await Frames(30);
        var gaze = Read<System.Collections.IList>(ep, "_gaze");   // List<DLine>（DLine は Epilogue の private struct）
        var texts = new System.Collections.Generic.List<(string who, string text)>();
        foreach (var d in gaze) texts.Add(((string)d!.GetType().GetField("Who")!.GetValue(d)!, (string)d.GetType().GetField("Text")!.GetValue(d)!));
        const string want = "……ミナの後ろの人も、聞いてるんでしょ。——ありがと、知らない人。";
        int idx = texts.FindIndex(t => t.text == want);
        Check("E5b こはるの行がレイ「胸張りなさいよ」の直後・曲停止「…………。」の直前", idx == 11 && texts[idx].who == "こはる"
            && texts[idx - 1].text == "一桁のほうが、顔が見えるの。……数えてるんでしょ。なら、胸張りなさいよ。" && texts[idx + 1].text == "…………。",
            string.Join(" / ", texts.Select(t => $"{t.who}:{t.text}")));
        int silence = (int)typeof(Epilogue).GetProperty("SilenceLine", Private)!.GetValue(ep)!;
        Check("E5b 曲停止 index は本文一致で追従（SilenceLine == idx+2）", silence == idx + 2, $"SilenceLine={silence}");
        bool reached = await AdvanceTo(() => Read<int>(ep, "_phase") == 0 && Read<int>(ep, "_line") == idx, holdFrames: 20);
        await Frames(40);   // 本文の描き出し（MsgCharsPerSec=300）を待ってから撮る
        Check("E5b こはるの行に届く（見上げフェーズのまま）", reached && Read<int>(ep, "_line") == idx && Read<int>(ep, "_phase") == 0,
            $"line={Read<int>(ep, "_line")} phase={Read<int>(ep, "_phase")}");
        await Shot("epilogue_koharu_shiranai_hito");
        await Drop(ep);
    }

    // Z を押して条件が立つまで送る。押す前に条件を見る＝行き過ぎない（1 押し＝全文表示 or 次行）。
    //   行送りの最短間隔（0.15〜0.25 秒）に届かない押しは無視されるだけなので、回数で吸収する。
    private async Task<bool> AdvanceTo(Func<bool> condition, int holdFrames = 14)
    {
        for (int i = 0; i < 60; i++)
        {
            if (condition()) return true;
            KeyDown(Key.Z, true);
            await Frames(2);
            KeyDown(Key.Z, false);
            await Frames(holdFrames);
        }
        return condition();
    }

    private static void KeyDown(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });

    private async Task Drop(Node root)
    {
        if (root.GetNodeOrNull<Hud>("Hud") is Hud hud) { hud.HoldBubble = false; hud.HideBubble(); }
        GetTree().Root.RemoveChild(root);
        root.QueueFree();
        await Frames(5);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Hud.BubblePaused = false;
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check($"screenshot {name}.png", image.SavePng($"{_out}/{name}.png") == Error.Ok);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
