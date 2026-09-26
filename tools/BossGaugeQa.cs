using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// 検証ハーネス（QA・2026-09-27）：ボス／中ボスの体力ゲージを画面上端のカード（Hud.DrawBossCard）から
//   ボス本体の頭上の簡略ゲージ（src/BossGauge.cs）へ移した件。
//   ① CameoBoss を StageAkari と同じ作り方で出す → _Ready の ShowBossBar(..., this) で子に BossGauge が付く
//   ② UpdateBossBar／FlashBossBarBreak／SetBossBarTint が Hud.GaugeState に出る
//   ③ 2体目の ShowBossBar で1体目のゲージが外れ、同時に2つ生きない
//   ④ HideBossBar：顔あり＝改心の見送り（Purify 0→1 → Fade 1→0 → 非表示）／顔なし＝即非表示
//   ⑤ ボスを消せばゲージも消える
//   ⑥ （--bg-shot のみ）スクショ＋画面上端に旧カードの暗い箱が出ていないことのピクセル確認
//   使い方（実セーブを汚さないよう APPDATA=build/qa_story/boss_gauge_appdata を渡す）：
//     ヘッドレス : Godot --headless --path . res://tools/qa_boss_gauge.tscn
//     スクショ付き（窓あり。Shot はヘッドレスで固まる）:
//                  Godot --path . res://tools/qa_boss_gauge.tscn -- --bg-shot --bg-out <絶対パス>
public partial class BossGaugeQa : Node
{
    private const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, P)!.SetValue(o, v);

    private int _fail;
    private bool _shot;
    private string _out = "build/shots_gauge_qa";
    private Hud? _hud;
    // 徘徊で盤面上端（旧カードの帯）まで上がられるとピクセル確認が濁るので、描画直前に定位置へ戻す。
    private readonly Dictionary<Enemy, Vector2> _pin = new();

    public override async void _Ready()
    {
        // ※ 全体の --shot（ShotTool 自動ロード＝撮って終了する）と衝突しないよう、専用の旗にしてある。
        var args = OS.GetCmdlineUserArgs();
        foreach (var a in args) if (a == "--bg-shot") _shot = true;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--bg-out") _out = args[i + 1];
        if (_shot)
        {
            DirAccess.MakeDirRecursiveAbsolute(_out);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            await Run();
            GD.Print(_fail == 0 ? "[BG] DONE ok" : $"[BG] DONE fail={_fail}");
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError($"[BG] FAIL {ex}"); GetTree().Quit(1); }
    }

    private async Task Run()
    {
        Check("セーブが隔離されている（APPDATA=build/qa_story/...）", OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"));
        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent(); game.AutoSaveEnabled = false;

        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        await Frames(2);
        root.Stage.SetProcess(false);
        var hud = root.Hud; _hud = hud;
        var player = root.Player;
        player.SetPhysicsProcess(false);   // 自弾が中ボスに当たって UpdateBossBar を上書きしないように
        Write(player, "_invincible", true); Write(player, "_invincibleTimer", 999f);
        player.GlobalPosition = new Vector2(Field.Left + 40, 170);
        hud.HoldBubble = false; hud.HideBubble();
        Write(hud, "_bannerTimer", 0d);
        await Frames(4);

        // ── ① 中ボス（CameoBoss）を StageAkari.Step_BossCameo と同じ作り方で出す ──
        var cameo = SpawnCameo(root, "AkariCameo", new Vector2(Field.CenterX + 40, 100));
        await Frames(6);
        var g1 = Gauges(cameo);
        Check($"中ボスの子に BossGauge が1つ（{g1.Length}）", g1.Length == 1);
        var s = hud.GaugeState;
        Check($"GaugeState.Visible（{s.Visible}）", s.Visible);
        Check($"Total/Index/Frac が中ボスの値と一致（{s.Total}/{s.Index}/{s.Frac:0.00} vs {cameo.TotalBars}/{cameo.CurrentBarIndex}/{cameo.CurrentBarFrac:0.00}）",
            s.Total == cameo.TotalBars && s.Index == cameo.CurrentBarIndex && Mathf.IsEqualApprox(s.Frac, cameo.CurrentBarFrac));
        if (g1.Length == 1)
            Check($"Z は絶対 11（ZIndex={g1[0].ZIndex} ZAsRelative={g1[0].ZAsRelative}）", g1[0].ZIndex == 11 && !g1[0].ZAsRelative);
        GD.Print($"[BG] info cameo GaugeTop={cameo.GaugeTop:0.0} GaugeWidth={cameo.GaugeWidth:0.0} (world px)");
        await Shot("mid_full");

        // ── ② UpdateBossBar／FlashBossBarBreak ──
        hud.UpdateBossBar(barIndex: 1, totalBars: 3, frac: 0.4f);
        s = hud.GaugeState;
        Check($"UpdateBossBar(1,3,0.4) が反映（{s.Index}/{s.Total}/{s.Frac:0.00}）", s.Index == 1 && s.Total == 3 && Mathf.IsEqualApprox(s.Frac, 0.4f));
        hud.FlashBossBarBreak();
        s = hud.GaugeState;
        Check($"FlashBossBarBreak 直後 Flash>0（{s.Flash:0.00}）", s.Flash > 0f);
        await Wait(0.4);
        s = hud.GaugeState;
        Check($"0.4 秒後 Flash は切れている（{s.Flash:0.000}）", s.Flash <= 0f);
        if (s.Flash < 0f) GD.Print($"[BG] info Flash が負で残る（{s.Flash:0.000}）＝ _bossBarFlash が 0 で止まらない。描画は >0 判定なので実害なし");

        // ── ③ SetBossBarTint ──
        var tint = new Color(0.2f, 0.8f, 0.6f);
        hud.SetBossBarTint(tint);
        Check($"SetBossBarTint が反映（{hud.GaugeState.Tint}）", hud.GaugeState.Tint.IsEqualApprox(tint));

        hud.UpdateBossBar(0, 1, 0.5f);
        await Shot("mid_half");

        // ── ④ 2体目（本ボス相当）：自身の _Ready で ShowBossBar(..., this) ──
        _pin[cameo] = new Vector2(Field.CenterX - 60, 150);   // 1体目は脇へどける
        var old = g1.Length == 1 ? g1[0] : null;
        var second = SpawnCameo(root, "SecondBoss", new Vector2(Field.CenterX + 40, 100));
        // 同フレーム内では QueueFree 済みでもまだ木に居る。次フレーム以降で無効になっていること。
        await Frames(3);
        Check("1体目のゲージは QueueFree されて無効", old == null || !IsInstanceValid(old));
        Check($"1体目の子に BossGauge が残っていない（{Gauges(cameo).Length}）", Gauges(cameo).Length == 0);
        var g2 = Gauges(second);
        Check($"2体目の子に BossGauge が1つ（{g2.Length}）", g2.Length == 1);
        Check($"盤面に生きている BossGauge は1つだけ（{AllGauges(root).Length}）", AllGauges(root).Length == 1);
        hud.UpdateBossBar(3, 6, 0.7f);
        s = hud.GaugeState;
        Check($"本ボス相当 6本中4本目（{s.Index + 1}/{s.Total} {s.Frac:0.00}）", s.Index == 3 && s.Total == 6 && Mathf.IsEqualApprox(s.Frac, 0.7f));
        await Shot("main_4of6");
        await BandControl("main_4of6");

        // ── ⑤ HideBossBar：顔あり（BossHandles.AkariBar）＝改心の見送り ──
        await HideWithFace(hud, second);

        // ── ⑤' HideBossBar：顔なし（ヒカゲと同じ Handles.Garble("@hikage_")）＝即非表示 ──
        hud.ShowBossBar("ヒカゲ", Handles.Garble("@hikage_"), second);
        await Frames(2);
        Check($"付け直しても2体目のゲージは1つ（{Gauges(second).Length}）", Gauges(second).Length == 1);
        Check("顔なしで表示中", hud.GaugeState.Visible);
        hud.HideBossBar();
        s = hud.GaugeState;
        Check($"顔なし：HideBossBar で即 Visible=false（Purify={s.Purify:0.000}）", !s.Visible && s.Purify == 0f);

        // ── ⑥ ボスを QueueFree → ゲージも消える ──
        var g3 = Gauges(second).FirstOrDefault();
        _pin.Remove(second);
        second.QueueFree();
        await Frames(3);
        Check("2体目を消したらゲージも無効", g3 != null && !IsInstanceValid(g3));
        _pin.Remove(cameo);
        cameo.QueueFree();
        await Frames(3);
        Check($"盤面の BossGauge は0（{AllGauges(root).Length}）", AllGauges(root).Length == 0);

        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        root.QueueFree(); await Frames(5);
    }

    private async Task HideWithFace(Hud hud, Enemy owner)
    {
        hud.HideBossBar();
        ulong t0 = Time.GetTicksMsec();
        float lastPurify = 0f, lastFade = 1f;
        bool purifyMono = true, fadeMono = true, fadeBeforeFull = false, shotTaken = false;
        double tPurifyFull = -1, tFadeStart = -1, tHidden = -1;
        while (true)
        {
            double t = (Time.GetTicksMsec() - t0) / 1000.0;
            var s = hud.GaugeState;
            if (!s.Visible) { tHidden = t; break; }
            if (s.Purify + 1e-4f < lastPurify) purifyMono = false;
            if (s.Fade > lastFade + 1e-4f) fadeMono = false;
            if (s.Fade < 1f && s.Purify < 1f) fadeBeforeFull = true;
            if (s.Purify >= 1f && tPurifyFull < 0) tPurifyFull = t;
            if (s.Fade < 1f && tFadeStart < 0) tFadeStart = t;
            lastPurify = s.Purify; lastFade = s.Fade;
            if (!shotTaken && t >= 0.5)
            {
                shotTaken = true;
                GD.Print($"[BG] info 見送り途中 t={t:0.00}s Purify={s.Purify:0.00} Fade={s.Fade:0.00}");
                Check($"ゲージは見送り中も2体目に付いている（{Gauges(owner).Length}）", Gauges(owner).Length == 1);
                await Shot("purify_mid");
                continue;
            }
            if (t > 4.0) break;
            await Frames(1);
        }
        GD.Print($"[BG] info 顔あり見送り：Purify=1 @{tPurifyFull:0.00}s／Fade開始 @{tFadeStart:0.00}s／非表示 @{tHidden:0.00}s");
        Check("顔あり：Purify は 0→1 へ単調に上がる", purifyMono && tPurifyFull > 0);
        Check("顔あり：Purify が上がりきってから Fade が下がる", !fadeBeforeFull && tFadeStart >= tPurifyFull && fadeMono);
        Check($"顔あり：約1.2s で Visible=false（{tHidden:0.00}s）", tHidden > 0.9 && tHidden < (_shot ? 2.2 : 1.7));
        var e = hud.GaugeState;
        Check($"見送り後は状態が戻る（Purify={e.Purify} Fade={e.Fade}）", e.Purify == 0f && e.Fade == 1f);
    }

    private CameoBoss SpawnCameo(AkariRoot root, string name, Vector2 pinAt)
    {
        var c = new CameoBoss
        {
            Name = name,
            Theme = new CameoTheme
            {
                DisplayName = "あかり", Handle = BossHandles.AkariBar,
                PreTex = "res://char/v3/akari_mid_v2.png",
                CryTex = "res://char/v3/akari_mid.png",
                PostTex = "res://char/v3/akari_mid.png",
                Face = "res://char/v3/akari_face_lit.png",
                SpellTint = new Color("6c9cd8"), SpellShape = BulletShape.Needle,
                Fire = CameoFireTheme.AkariGrief,
                Aura = FxLayer.BossAura.Akari,
                Bgm = null,   // 検証では曲を鳴らさない
                IntroLines = Array.Empty<(int, string, string)>(),
                TauntLines = Array.Empty<(int, string, string)>(),
                DefeatLines = Array.Empty<(int, string, string)>(),
            },
        };
        root.World.AddChild(c);
        c.GlobalPosition = new Vector2(300f, 70f);   // StageAkari.SpawnX と同じ出現点
        _pin[c] = pinAt;
        return c;
    }

    private static BossGauge[] Gauges(Node owner) =>
        owner.GetChildren().OfType<BossGauge>().Where(g => IsInstanceValid(g) && !g.IsQueuedForDeletion()).ToArray();

    private static BossGauge[] AllGauges(Node n)
    {
        var list = new List<BossGauge>();
        void Walk(Node x) { if (x is BossGauge g && !g.IsQueuedForDeletion()) list.Add(g); foreach (var c in x.GetChildren()) Walk(c); }
        Walk(n);
        return list.ToArray();
    }

    private void Check(string name, bool ok)
    {
        if (!ok) _fail++;
        GD.Print($"[BG] {(ok ? "OK " : "NG ")} {name}");
    }

    public override void _Process(double delta)
    {
        foreach (var (e, at) in _pin) if (IsInstanceValid(e)) e.GlobalPosition = at;
        // ステージ本体のイントロ会話が居座ると BubblePaused で時間が止まるので畳み続ける（LockModeQa と同じ）。
        if (_hud != null && IsInstanceValid(_hud) && Hud.BubblePaused) { _hud.HoldBubble = false; _hud.HideBubble(); }
    }

    private async Task Shot(string name)
    {
        if (!_shot) return;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();   // 弾でゲージが読みにくくならないように
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check($"screenshot {name}", image.SavePng($"{_out}/{name}.png") == Error.Ok);
        Check($"{name}: 上端に旧カードの暗い箱が無い", BandLooksEmpty(image, name));
    }

    // 旧カードの矩形（Hud.DrawBossCard：設計座標 x=DCenterX±280, y 24..68、塗り (18,12,22)×0.62）。
    //   カードがあれば帯の中だけ大きく暗く沈む。帯の輝度を上下の隣接帯と比べる（厳密でなくてよい判定）。
    private static bool BandLooksEmpty(Image img, string name)
    {
        float band = Lum(img, 30, 62), above = Lum(img, 2, 18), below = Lum(img, 76, 104);
        float refL = (above + below) / 2f;
        GD.Print($"[BG] info {name}: 帯の輝度 {band:0.000}／上 {above:0.000}／下 {below:0.000}");
        return band > refL * 0.75f;
    }

    private static float Lum(Image img, float dy0, float dy1)
    {
        float sx = img.GetWidth() / 1280f, sy = img.GetHeight() / 720f;
        float w = Mathf.Min(560f, Field.DWidth * 0.8f), x0 = Field.DCenterX - w / 2f + 12f, x1 = Field.DCenterX + w / 2f - 12f;
        double sum = 0; int n = 0;
        for (float y = dy0; y <= dy1; y += 2f)
            for (float x = x0; x <= x1; x += 6f)
            {
                var c = img.GetPixel((int)(x * sx), (int)(y * sy));
                sum += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B; n++;
            }
        return (float)(sum / Math.Max(1, n));
    }

    // 判定器の陽性対照：旧カードと同じ矩形・同じ塗りを Hud と同じ層にわざと描き、判定が「箱あり」を拾えることを確かめる。
    private async Task BandControl(string name)
    {
        if (!_shot || _hud == null) return;
        var layer = new CanvasLayer { Layer = _hud.Layer };
        layer.AddChild(new FakeCard());
        AddChild(layer);
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check($"{name}: 陽性対照（旧カード相当を描くと判定が拾う）", !BandLooksEmpty(image, name + "+fakecard"));
        layer.QueueFree();
        await Frames(2);
    }

    private partial class FakeCard : Node2D
    {
        public override void _Draw()
        {
            UiKit.BeginDesign(this);
            float w = Mathf.Min(560f, Field.DWidth * 0.8f);
            UiKit.Box(this, new Rect2(Field.DCenterX - w / 2f, 24f, w, 44f), new Color(18 / 255f, 12 / 255f, 22 / 255f, 0.62f), 16f,
                new Color(UiKit.Kegare, 0.4f), 1.2f);
            UiKit.EndDesign(this);
        }
    }

    private async Task Wait(double sec) => await ToSignal(GetTree().CreateTimer(sec), SceneTreeTimer.SignalName.Timeout);
    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
