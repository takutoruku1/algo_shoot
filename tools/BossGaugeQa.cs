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
//   ⑦ 塗りの色分け（2026-09-27 追加）：_phase／_phaseT を反射で Shielded→Break→Exposed→Reclose と切り替え、
//      BossGauge.LastFillKind が invuln／break／break／invuln、Enemy.GaugeVulnerable／GaugeWindowLeft／GaugeBreakFresh が
//      状態どおりか。--bg-shot では gauge_invuln／gauge_break と、ゲージ部分の3倍拡大 gauge_zoom_* も撮る。
//   使い方（実セーブを汚さないよう APPDATA=build/qa_story/boss_gauge_appdata を渡す）：
//     ヘッドレス : Godot --headless --path . res://tools/qa_boss_gauge.tscn
//     スクショ付き（窓あり。Shot はヘッドレスで固まる）:
//                  Godot --path . res://tools/qa_boss_gauge.tscn -- --bg-shot --bg-out <絶対パス>
public partial class BossGaugeQa : Node
{
    private const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, P)!.SetValue(o, v);
    // Enemy の private（_phase／_phaseT）は基底に宣言されているので、派生（CameoBoss）から上へたどって探す。
    private static FieldInfo BaseField(object o, string n)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
            if (t.GetField(n, P | BindingFlags.DeclaredOnly) is { } f) return f;
        throw new MissingFieldException(o.GetType().Name, n);
    }

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
        Write(player, "_locked", true);
        Write(player, "_lockArmed", true);
        Write(player, "_lockTarget", cameo);
        await Frames(80);
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

        // ── ⑦ 塗りの色分け：無敵＝灰＋斜線／BREAK＝金 ──
        await PhaseColors(hud, cameo);
        await RecoveryGauge(hud, cameo, "mid");
        await DamageTrail(hud, g1[0]);

        // ── ④ 2体目（本ボス相当）：自身の _Ready で ShowBossBar(..., this) ──
        _pin[cameo] = new Vector2(Field.CenterX - 60, 150);   // 1体目は脇へどける
        var old = g1.Length == 1 ? g1[0] : null;
        var second = new BossAkari { Name = "SecondBoss" };
        root.World.AddChild(second);
        Write(player, "_lockTarget", second);
        second.GlobalPosition = new Vector2(Field.CenterX + 40, 100);
        var caster = (AreaSpellCaster)BaseField(second, "_caster").GetValue(second)!;
        caster.SetProcess(false);
        caster.CancelPendingAttacks();
        await Frames(160);
        second.SetPhysicsProcess(false);
        hud.HideSpellCard();
        cameo.Hide();
        _pin[second] = new Vector2(Field.CenterX + 40, 100);
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
        await Zoom(g2[0], "main_zoom");
        await BandControl("main_4of6");
        await RecoveryGauge(hud, second, "main");
        if (_shot)
        {
            var phase = BaseField(second, "_phase");
            phase.SetValue(second, Enum.Parse(phase.FieldType, "Reclose"));
            BaseField(second, "_phaseT").SetValue(second, 0.675);
            second.GetNode<Node2D>("Shield").QueueRedraw();
            foreach (Vector2I size in new[] { new Vector2I(960, 540), new(1920, 1080), new(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(3);
                await Shot($"main_{size.X}x{size.Y}");
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(3);
            phase.SetValue(second, Enum.Parse(phase.FieldType, "Shielded"));
            second.GetNode<Node2D>("Shield").QueueRedraw();
        }

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

    private async Task PhaseColors(Hud hud, CameoBoss boss)
    {
        var g = Gauges(boss).FirstOrDefault();
        Check("⑦ 中ボスにゲージが付いている", g != null);
        if (g == null) return;
        var fPhase = BaseField(boss, "_phase");
        var fPhaseT = BaseField(boss, "_phaseT");
        var enumT = fPhase.FieldType;
        object Ph(string n) => Enum.Parse(enumT, n);
        hud.UpdateBossBar(1, 3, 0.7f);

        // 世界の CanvasModulate（夜の冷色 Tint）を SelfModulate の逆数で打ち消している（BubbleLayer と同じ）。
        var cm = boss.GetParent()?.GetParent()?.GetChildren().OfType<CanvasModulate>().FirstOrDefault();
        if (cm != null)
        {
            var back = g.SelfModulate * cm.Color;
            GD.Print($"[BG] info Tint={cm.Color} SelfModulate={g.SelfModulate}");
            Check($"Tint を打ち消している（SelfModulate×Tint={back}）", back.IsEqualApprox(Colors.White) || cm.Color.IsEqualApprox(Colors.White));
        }

        // 各状態に入れて 2 フレーム後に読む。Break の _phaseT 0 は BreakCueDur(0.45s) 内・Reclose の 0 は 1.35s 内なので自動遷移しない。
        async Task Enter(string phase, double t)
        {
            fPhase.SetValue(boss, Ph(phase)); fPhaseT.SetValue(boss, t);
            await Frames(2);
        }

        await Enter("Shielded", 0.0);
        Check($"Shielded：LastFillKind=invuln（{g.LastFillKind}）", g.LastFillKind == "invuln");
        Check($"Shielded：GaugeVulnerable=false（{boss.GaugeVulnerable}）", !boss.GaugeVulnerable);
        Check($"Shielded：GaugeWindowLeft=0（{boss.GaugeWindowLeft:0.00}）", boss.GaugeWindowLeft == 0f);
        await Shot("gauge_invuln");
        await Zoom(g, "gauge_zoom_invuln");

        await Enter("Break", 0.0);
        Check($"Break：LastFillKind=break（{g.LastFillKind}）", g.LastFillKind == "break");
        Check($"Break：GaugeVulnerable=true（{boss.GaugeVulnerable}）", boss.GaugeVulnerable);
        Check($"Break：GaugeWindowLeft=1（{boss.GaugeWindowLeft:0.00}）", boss.GaugeWindowLeft == 1f);
        Check($"Break 直後：GaugeBreakFresh が立っている（{boss.GaugeBreakFresh:0.00}）", boss.GaugeBreakFresh > 0.5f);

        await Enter("Exposed", 4.0 * 0.4);   // 無防備窓（VulnDur=4.0s）の残り 60%
        float left = boss.GaugeWindowLeft;
        Check($"Exposed：LastFillKind=break（{g.LastFillKind}）", g.LastFillKind == "break");
        Check($"Exposed：GaugeVulnerable=true（{boss.GaugeVulnerable}）", boss.GaugeVulnerable);
        Check($"Exposed：GaugeWindowLeft が 0..1 で約 0.6（{left:0.000}）", left >= 0f && left <= 1f && Mathf.Abs(left - 0.6f) < 0.05f);
        Check($"Exposed：GaugeBreakFresh は減衰済み（{boss.GaugeBreakFresh:0.00}）", boss.GaugeBreakFresh == 0f);
        fPhaseT.SetValue(boss, 1.6);   // 撮影前に 60% へ戻す（2 フレームぶん進んでいる）
        await Shot("gauge_break");
        await Zoom(g, "gauge_zoom_break");

        await Enter("Reclose", 0.0);
        Check($"Reclose：LastFillKind=invuln（{g.LastFillKind}）", g.LastFillKind == "invuln");
        Check($"Reclose：GaugeVulnerable=false（{boss.GaugeVulnerable}）", !boss.GaugeVulnerable);
        Check($"Reclose：GaugeWindowLeft=0（{boss.GaugeWindowLeft:0.00}）", boss.GaugeWindowLeft == 0f);

        await Enter("Shielded", 0.0);   // 以降の項目のために盾へ戻す
        Check($"盾へ戻すと invuln（{g.LastFillKind}）", g.LastFillKind == "invuln");
    }

    private async Task RecoveryGauge(Hud hud, Enemy boss, string prefix)
    {
        bool physics = boss.IsPhysicsProcessing();
        boss.SetPhysicsProcess(false);
        // 物理を止めた＝TickEntrance が回らないので登場フラグが立ちっぱなしになる。GaugeReforming は
        //   それを見て黙る（登場中はゲージを出さない）ので、_purified と同じく直に倒してから状態を作る。
        BaseField(boss, "_entering").SetValue(boss, false);
        var phase = BaseField(boss, "_phase");
        var time = BaseField(boss, "_phaseT");
        var gauge = Gauges(boss).Single();
        hud.HideSpellCard();
        async Task Enter(string name, double t)
        {
            phase.SetValue(boss, Enum.Parse(phase.FieldType, name));
            time.SetValue(boss, t);
            boss.QueueRedraw();
            boss.GetNode<Node2D>("Shield").QueueRedraw();
            await Frames(2);
        }

        await Enter("Break", 0);
        Check($"{prefix}: broken shield starts with a full attack window", boss.GaugeWindowLeft == 1f && !boss.GaugeReforming);
        hud.Bubbles!.ShieldBreak.Show(boss, GetNode<GameManager>("/root/Game").SelectedJob);
        await Wait(0.3);
        Check($"{prefix}: shield break callout is visible", hud.Bubbles.ShieldBreak.Active);
        await Shot($"{prefix}_recovery_break");
        await Zoom(gauge, $"{prefix}_recovery_break_zoom", recovery: true);
        if (_shot)
        {
            var game = GetNode<GameManager>("/root/Game");
            var previousJob = game.SelectedJob;
            foreach (Job job in Enum.GetValues<Job>())
            {
                game.SelectedJob = job;
                boss.QueueRedraw();
                await Shot($"{prefix}_lock_{job}");
            }
            game.SelectedJob = previousJob;
            boss.QueueRedraw();
        }
        foreach (double t in new[] { 1.6, 3.4, 4.0 })
        {
            await Enter("Exposed", t);
            Check($"{prefix}: window at {t}s is exact", Mathf.IsEqualApprox(boss.GaugeWindowLeft, 1f - (float)t / 4f));
            await Shot($"{prefix}_recovery_window_{t:0.0}");
        }
        foreach (double t in new[] { 0.0, 0.675, 1.35 })
        {
            await Enter("Reclose", t);
            Check($"{prefix}: reformation at {t}s is exact", boss.GaugeReforming && !boss.GaugeVulnerable
                && boss.GaugeWindowLeft == 0 && Mathf.IsEqualApprox(boss.GaugeReformProgress, (float)(t / 1.35)));
            await Shot($"{prefix}_recovery_reform_{t:0.000}");
            if (t == 0.675) await Zoom(gauge, $"{prefix}_recovery_reform_zoom", recovery: true);
        }
        BaseField(boss, "_purified").SetValue(boss, true);
        Check($"{prefix}: purified boss has no recovery indicator", !boss.GaugeVulnerable && !boss.GaugeReforming && boss.GaugeReformProgress == 0);
        await Shot($"{prefix}_recovery_purified");
        BaseField(boss, "_purified").SetValue(boss, false);
        await Enter("Shielded", 0);
        Check($"{prefix}: shielded boss has no recovery indicator", !boss.GaugeVulnerable && !boss.GaugeReforming && boss.GaugeReformProgress == 0);
        await Shot($"{prefix}_recovery_done");
        boss.SetPhysicsProcess(physics);
    }

    private async Task DamageTrail(Hud hud, BossGauge gauge)
    {
        float Trail() => (float)BaseField(gauge, "_trail").GetValue(gauge)!;
        hud.UpdateBossBar(1, 3, 1);
        await Frames(2);
        hud.UpdateBossBar(1, 3, 0.45f);
        await Frames(2);
        Check("damage trail holds the old health without delaying the actual health", Trail() > 0.9f && hud.GaugeState.Frac == 0.45f);
        await Shot("damage_trail");
        await Zoom(gauge, "damage_trail_zoom");
        await Wait(0.6);
        Check("damage trail settles to the current health", Mathf.IsEqualApprox(Trail(), 0.45f));
        hud.UpdateBossBar(0, 3, 0.8f);
        await Frames(2);
        Check("new health bar resets the damage trail", Mathf.IsEqualApprox(Trail(), 0.8f));
        for (int i = 0; i < 6; i++)
        {
            hud.UpdateBossBar(0, 3, 0.7f - i * 0.1f);
            await Wait(0.1);
        }
        Check("continuous hits do not keep the damage trail full", Trail() < 0.6f);
        foreach (float health in new[] { 0.0001f, 0f, 1f })
        {
            hud.UpdateBossBar(0, 1, health);
            await Frames(2);
            await Shot($"health_{health:0.0000}");
        }
    }

    private async Task Zoom(BossGauge g, string name, bool recovery = false)
    {
        if (!_shot || OwnerOf(g) is not Enemy owner) return;
        await Frames(1);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var vis = GetViewport().GetVisibleRect().Size;
        float sx = image.GetWidth() / vis.X, sy = image.GetHeight() / vis.Y;
        float w = owner.GaugeWidth, top = recovery ? owner.GaugeBottom : owner.GaugeTop;
        var xf = g.GetGlobalTransformWithCanvas();
        var a = xf * new Vector2(-w / 2f - (recovery ? 6f : 23f), top - (recovery ? 4f : 13f));
        var b = xf * new Vector2(w / 2f + 6f, top + (recovery ? 12f : 8f));
        int x0 = Mathf.Clamp((int)(Mathf.Min(a.X, b.X) * sx), 0, image.GetWidth() - 1);
        int y0 = Mathf.Clamp((int)(Mathf.Min(a.Y, b.Y) * sy), 0, image.GetHeight() - 1);
        int x1 = Mathf.Clamp((int)Mathf.Ceil(Mathf.Max(a.X, b.X) * sx), x0 + 1, image.GetWidth());
        int y1 = Mathf.Clamp((int)Mathf.Ceil(Mathf.Max(a.Y, b.Y) * sy), y0 + 1, image.GetHeight());
        using var crop = image.GetRegion(new Rect2I(x0, y0, x1 - x0, y1 - y0));
        crop.Resize(crop.GetWidth() * 3, crop.GetHeight() * 3, Image.Interpolation.Nearest);
        GD.Print($"[BG] info {name}: 切り抜き ({x0},{y0}) {x1 - x0}x{y1 - y0} px → {crop.GetWidth()}x{crop.GetHeight()}");
        Check($"screenshot {name}", crop.SavePng($"{_out}/{name}.png") == Error.Ok);
    }

    private static Enemy? OwnerOf(BossGauge g) => g.GetParent() as Enemy;

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
