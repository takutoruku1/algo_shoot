using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// 検証ハーネス（QA・2026-09-27）：作者指摘「ステージにもよるんだけど、自機や敵が暗い時がある／少なくとも
//   自機の当たり判定は分かるよう明るい方がいい」→ 追加指示「自キャラ／敵もどのステージでも明るくしてほしい」
//   への対応＝src/TintLift.cs による CanvasModulate（夜の冷色 Tint）の**完全打ち消し**を4ステージで確かめる。
//   ① 各ステージの Tint の実値を出す（あかり／こはる／レイ／FINAL）
//   ② 当たり判定の芯（PlayerHitDot.SelfModulate）× Tint が白＝各チャンネル 1.0（要件の >=0.85 を満たす）
//   ③ 自機の本体（Sprite.Modulate）× Tint、敵の本体（Body.Modulate）× Tint も白。敵はザコとボスの両方。
//      α には何も入れていないこと＝被弾点滅・退場フェード・改心差し替えのクロスフェードに干渉しない。
//   ④ 当たり判定そのものは不変：HitShape の半径／HitDot.Radius／衝突レイヤ・マスク／グレイズ円／
//      HitDot の Z・位置・スケール・回転。
//   ⑤ （--vis-shot のみ）4ステージ × 修正前後のスクショ。全体 stage_<stage>_<before|after>.png と、
//      自機の周囲を3倍拡大した hitdot_<stage>_<before|after>.png、シールドの泡つき hitdot_<stage>_shield.png。
//      さらに「自機の不透明画素だけの平均色」を素材（char/player/<id>/<id>_idle_v2.png）の同じ画素の平均と
//      突き合わせ、暗いステージでも昼の素材と同じ明るさで出ていることを数値でも押さえる。
//   使い方（実セーブを汚さないよう APPDATA=build/qa_story/visibility_appdata を渡す）:
//     ヘッドレス : Godot --headless --path . res://tools/qa_visibility.tscn
//     スクショ付き（窓あり。ヘッドレスでは画面が撮れない）:
//                  Godot --path . res://tools/qa_visibility.tscn -- --vis-shot --vis-out <絶対パス>
public partial class VisibilityQa : Node
{
    private const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, P)!.SetValue(o, v);
    private static T Read<T>(object o, string n) => (T)o.GetType().GetField(n, P)!.GetValue(o)!;

    // ミナの汚染ティント（Player._corruption を SelfModulate へ）。Tint とは**別系統**の意図的な演出で、
    //   このタスクでは触らない＝面ごとに自機が濁る。数値で押さえておくため Player の定数をここに写す
    //   （Player.CleanTint / Player.MurkTint は private。値がズレたらこの検証が落ちて気づける）。
    private static readonly Color Murk = new Color(0.42f, 0.40f, 0.52f);
    private static Color CorruptTint(float corruption) => Colors.White.Lerp(Murk, corruption);

    private int _fail;
    private bool _shot;
    private string _out = "build/shots_visibility";
    private Hud? _hud;
    // 徘徊・進入で動く敵は撮影のあいだ定位置へ固定する（BossGaugeQa と同じ作法）。
    private readonly Dictionary<Node2D, Vector2> _pin = new();
    // 自機の胴の平均色（--vis-shot のみ）。ステージ間で食い違わないことも最後に見る。
    private readonly List<(string Stage, Color Before, Color After, Color Source, int Samples)> _body = new();
    private Color _bodyBefore, _bodySource;
    private int _bodySamples;

    private readonly record struct StageCase(string Id, string Scene, StageTheme Theme, Func<Enemy> Boss);
    private static readonly StageCase[] Cases =
    {
        new("akari", "Akari", StageTheme.Akari, () => new BossAkari()),
        new("koharu", "Koharu", StageTheme.Koharu, () => new BossKoharu()),
        new("rei", "Rei", StageTheme.Rei, () => new BossRei()),
        new("mina", "MinaBattle", StageTheme.Mina, () => new BossMina()),
    };

    public override async void _Ready()
    {
        // 全体の --shot（ShotTool 自動ロード＝撮って終了する）と衝突しないよう専用の旗にしてある。
        var args = OS.GetCmdlineUserArgs();
        foreach (var a in args) if (a == "--vis-shot") _shot = true;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--vis-out") _out = args[i + 1];
        if (_shot)
        {
            DirAccess.MakeDirRecursiveAbsolute(_out);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        ProcessMode = ProcessModeEnum.Always;   // 撮影中はツリーを止めるので、この検証ノードだけは動かし続ける
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            await Run();
            GD.Print(_fail == 0 ? "[VIS] DONE ok" : $"[VIS] DONE fail={_fail}");
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError($"[VIS] FAIL {ex}"); GetTree().Quit(1); }
    }

    private async Task Run()
    {
        Check("セーブが隔離されている（APPDATA=build/qa_story/...）", OS.GetUserDataDir().Replace(Chr92, '/').Contains("/build/qa_story/"));
        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent(); game.AutoSaveEnabled = false; game.Difficulty = GameManager.Diff.Normal;
        foreach (var c in Cases) await RunStage(game, c);
        CompareStages();
    }

    private const char Chr92 = (char)92;   // Windows のパス区切り

    private async Task RunStage(GameManager game, StageCase c)
    {
        game.ResetRun();
        game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>($"res://{c.Scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);   // 道中の湧き・会話を止める
        var world = root.GetNode<Node2D>("World");
        // FINAL の StageMina._Ready は導入会話のあいだ World の処理を止める（World.ProcessMode=Disabled）。
        //   ステージ進行（Stage）を切ったこの検証では誰も戻さないので、ここで盤面を動く状態にしておく
        //   （EnemyProjectileQa と同じ手当て）。自機・芯・敵の _Process が止まると補正の追従も泡も見られない。
        world.ProcessMode = ProcessModeEnum.Inherit;
        var player = world.GetNode<Player>("Player");
        var hud = root.GetNode<Hud>("Hud"); _hud = hud;
        hud.HoldBubble = false; hud.HideBubble(); Hud.BubblePaused = false;
        Write(hud, "_bannerTimer", 0d);
        // 検証中に被弾で残機が減ったり絵が消えたりしないよう無敵にしておく。ただし無敵は 20Hz の点滅を伴う
        // （SetSpriteVisible が Visible と Modulate.α を落とす）ので、撮影・計測の直前に Pose() で必ず点灯側へ寄せる。
        Write(player, "_invincible", true); Write(player, "_invincibleTimer", 999f);
        // 定位置に留める（マウス追従・ノックバック等で流されると、切り抜きがボスの絵と重なって計測が濁る）。
        var playerAt = new Vector2(Field.Left + 62f, 128f);
        player.GlobalPosition = playerAt; _pin[player] = playerAt;
        await Frames(24);   // バンク（_lean）と Tint の初期化が落ち着くまで

        // ── ① この面の Tint ──
        var tintNode = root.GetChildren().OfType<CanvasModulate>().FirstOrDefault();
        Color tint = tintNode?.Color ?? Colors.White;
        GD.Print($"[VIS] info {c.Id}: Tint={Fmt(tint)}{(tintNode == null ? "（CanvasModulate 無し＝そもそも沈まない面）" : "")}");

        // ── 敵：ザコ（その面の撃つ種）とボス ──
        var (shooter, _) = EnemyTable.For(c.Theme);
        var zako = new MidEnemy { Name = "VisZako" };
        zako.Configure(shooter);
        world.AddChild(zako);
        var zakoAt = new Vector2(Field.Left + 150f, 78f);
        zako.GlobalPosition = zakoAt; _pin[zako] = zakoAt;
        Enemy boss = c.Boss();
        boss.Name = "VisBoss";
        world.AddChild(boss);
        var bossAt = new Vector2(Field.Right - 62f, 104f);
        boss.GlobalPosition = bossAt;
        await Frames(90);   // ボス登場演出（EntranceDur=1.15s）を通し切って立たせる
        _pin[boss] = bossAt;
        await Frames(8);
        if (_phaseScenes > 0) GD.Print($"[VIS] info {c.Id}: 段間カットシーンを {_phaseScenes} 回畳んだ（World の処理を戻すため）");
        _phaseScenes = 0;
        GD.Print($"[VIS] info {c.Id}: 自機 {player.GlobalPosition} ザコ {zako.GlobalPosition} ボス {boss.GlobalPosition}（内部座標）");
        Check($"{c.Id} 盤面の処理が生きている（World.ProcessMode={world.ProcessMode}）",
            world.ProcessMode is ProcessModeEnum.Inherit or ProcessModeEnum.Pausable);

        var dot = player.GetNode<PlayerHitDot>("HitDot");
        var body = player.GetNode<Sprite2D>("Sprite");
        var zakoBody = zako.GetNodeOrNull<Sprite2D>("Body");
        var bossBody = boss.GetNodeOrNull<Sprite2D>("Body");

        // ── ②③ 補正 × Tint が白（＝どのステージでも昼の素材そのままの明るさ）──
        CheckLift(c.Id, "当たり判定の芯 HitDot.SelfModulate", dot.SelfModulate, tint, TintLift.PlayerCore);
        CheckLift(c.Id, "自機の本体 Sprite.Modulate", body.Modulate, tint, TintLift.PlayerBody);
        Check($"{c.Id} ザコに本体スプライトがある", zakoBody != null);
        if (zakoBody != null)
            CheckLift(c.Id, $"ザコの本体 Body.Modulate（{shooter.PreTexPath.GetFile()}）", zakoBody.Modulate, tint, TintLift.EnemyBody);
        Check($"{c.Id} ボスに本体スプライトがある（{boss.GetType().Name}）", bossBody != null);
        if (bossBody != null)
            CheckLift(c.Id, $"ボスの本体 Body.Modulate（{boss.GetType().Name}）", bossBody.Modulate, tint, TintLift.EnemyBody);
        // ── 汚染ティント（Player._corruption → SelfModulate）は「濁らせるが暗くしない」か ──
        //   面ごとの濃さ：あかり 0.00→0.18／こはる 0.18→0.45／レイ 0.45→0.80／FINAL 0。
        //   TintLift.KeepBright が輝度だけ下限 0.85 へ正規化する＝色相・彩度（チャンネル比）は素の濁りのまま。
        float corr = Read<float>(player, "_corruption");
        CheckCorruption(c.Id, body, corr);
        var corrTint = body.SelfModulate;   // 実際に掛かっている濁り（下限つき）。以降の画素比較の期待値に使う
        if (zakoBody != null)
            Check($"{c.Id} 敵の SelfModulate はクロスフェード用のまま（{Fmt(zakoBody.SelfModulate)} α={zakoBody.SelfModulate.A:0.00}）",
                zakoBody.SelfModulate.IsEqualApprox(Colors.White));

        // ── 中ボス（CameoBoss）も同じ扱いか ──
        //   ザコ（MidEnemy）とボス（Boss*）と同じ Enemy 基底の経路を通るだけだが、「ボス・中ボスも同じ扱い」が
        //   指示なので現物で確かめる。撮影には出さない（ゲージが二重になるだけなので、見たら消す）。
        await CheckCameo(c.Id, world, tint);

        // ── ④ 当たり判定そのものが変わっていない ──
        CheckHitbox(c.Id, player, dot, zako, boss);

        // ── ⑤ スクショ（窓ありのみ）──
        await Shots(c.Id, player, dot, body, tint, corrTint, new[] { (Enemy)zako, boss });

        // ── ⑥ レイ面だけ：道中の終盤（汚染 0.80）でも自機が沈まないか ──
        if (c.Id == "rei") await CorruptShots(c.Id, root, player, dot, body, new[] { (Enemy)zako, boss });

        foreach (var k in _pin.Keys.ToArray()) _pin.Remove(k);
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        Audio.Instance?.StopMusic(0);
        root.QueueFree();
        await Frames(5);
        Hud.BubblePaused = false;
        _hud = null;
    }

    // 中ボス（CameoBoss）を1体だけ立てて本体スプライトの補正を見る（BossGaugeQa の作り方をそのまま使う）。
    private async Task CheckCameo(string id, Node2D world, Color tint)
    {
        var cameo = new CameoBoss
        {
            Name = "VisCameo",
            Theme = new CameoTheme
            {
                DisplayName = "検証中ボス", Handle = Handles.Garble("@qa_cameo_"),
                PreTex = "res://char/v3/akari_mid_v2.png",
                CryTex = "res://char/v3/akari_mid.png",
                PostTex = "res://char/v3/akari_mid.png",
                SpellTint = new Color("6c9cd8"), SpellShape = BulletShape.Needle,
                Fire = CameoFireTheme.AkariGrief, Aura = FxLayer.BossAura.Akari,
                Bgm = null,   // 検証では曲を鳴らさない
                IntroLines = Array.Empty<(int, string, string)>(),
                TauntLines = Array.Empty<(int, string, string)>(),
                DefeatLines = Array.Empty<(int, string, string)>(),
            },
        };
        world.AddChild(cameo);
        var at = new Vector2(Field.CenterX, 160f);
        cameo.GlobalPosition = at; _pin[cameo] = at;
        await Frames(6);
        var cameoBody = cameo.GetNodeOrNull<Sprite2D>("Body");
        Check($"{id} 中ボスに本体スプライトがある（CameoBoss）", cameoBody != null);
        if (cameoBody != null) CheckLift(id, "中ボスの本体 Body.Modulate（CameoBoss）", cameoBody.Modulate, tint, TintLift.EnemyBody);
        _pin.Remove(cameo);
        cameo.QueueFree();
        await Frames(3);
        if (_hud != null && IsInstanceValid(_hud)) _hud.HideBossBar();
    }

    // 汚染ティントの検証：濁り（色相・彩度）は素のまま／輝度は下限 CharacterMinLuma を割らない。
    private void CheckCorruption(string stage, Sprite2D body, float corruption)
    {
        var raw = CorruptTint(corruption);              // 明度の下限を入れる前の濁り（＝旧来の見え方）
        var want = TintLift.KeepBright(raw);            // 下限つき
        var now = body.SelfModulate;
        float yr = TintLift.Luma(raw), yn = TintLift.Luma(now);
        GD.Print($"[VIS] info {stage}: 汚染 _corruption={corruption:0.00} 素の濁り={Fmt(raw)}（輝度 {yr:0.000}）"
            + $" → 実際={Fmt(now)}（輝度 {yn:0.000}／下限 {TintLift.CharacterMinLuma:0.00}）");
        Check($"{stage} 汚染の濁りは残しつつ輝度が下限を割らない（{yn:0.000} >= {TintLift.CharacterMinLuma:0.00}）",
            yn >= TintLift.CharacterMinLuma - 0.002f && Near(now, want, 0.005f));
        // 色相・彩度＝チャンネル比が素の濁りと同じか（暗さだけを直し、色は動かしていない）。
        bool ratio = Mathf.Abs(now.R * raw.G - now.G * raw.R) < 0.002f
                     && Mathf.Abs(now.B * raw.G - now.G * raw.B) < 0.002f;
        Check($"{stage} 濁りの色味は変えていない（R:G:B の比が素の濁りと一致）", ratio);
    }

    // 補正 × Tint が狙いどおりか。強さ 1 なら「Tint を完全に打ち消して白」。
    private void CheckLift(string stage, string what, Color lift, Color tint, float strength)
    {
        var eff = new Color(lift.R * tint.R, lift.G * tint.G, lift.B * tint.B, 1f);
        var want = tint.Lerp(Colors.White, strength);
        bool bright = eff.R >= 0.85f && eff.G >= 0.85f && eff.B >= 0.85f;
        bool aimed = Mathf.Abs(eff.R - want.R) < 0.02f && Mathf.Abs(eff.G - want.G) < 0.02f && Mathf.Abs(eff.B - want.B) < 0.02f;
        bool alpha = Mathf.IsEqualApprox(lift.A, 1f);
        Check($"{stage} {what}：補正={Fmt(lift)} × Tint = {Fmt(eff)}（狙い {Fmt(want)}・各チャンネル>=0.85・α不介入）",
            bright && aimed && alpha);
    }

    private void CheckHitbox(string stage, Player player, PlayerHitDot dot, Enemy zako, Enemy boss)
    {
        var hit = (CircleShape2D)player.GetNode<CollisionShape2D>("HitShape").Shape;
        Check($"{stage} 当たり半径は 2px のまま（HitShape={hit.Radius:0.###} HitDot.Radius={dot.Radius:0.###}）",
            Mathf.IsEqualApprox(hit.Radius, 2f) && Mathf.IsEqualApprox(dot.Radius, hit.Radius));
        Check($"{stage} 自機の衝突は layer=1/mask=12 のまま（{player.CollisionLayer}/{player.CollisionMask}）",
            player.CollisionLayer == 1 && player.CollisionMask == 12 && player.Monitoring && player.Monitorable);
        var graze = player.GetNode<Area2D>("GrazeArea");
        var grazeShape = (CircleShape2D)graze.GetChildren().OfType<CollisionShape2D>().First().Shape;
        Check($"{stage} グレイズ円は 11px・layer=0/mask=8（{grazeShape.Radius:0.###} {graze.CollisionLayer}/{graze.CollisionMask}）",
            Mathf.IsEqualApprox(grazeShape.Radius, 11f) && graze.CollisionLayer == 0 && graze.CollisionMask == 8);
        Check($"{stage} 芯は当たり判定の中心に素のまま乗っている（Z={dot.ZIndex} rel={dot.ZAsRelative} scale={dot.Scale} rot={dot.Rotation:0.###}）",
            dot.ZIndex == 1 && dot.ZAsRelative && dot.Scale == Vector2.One && dot.Rotation == 0f
            && dot.GlobalPosition.IsEqualApprox(player.GlobalPosition));
        Check($"{stage} 敵の衝突は layer=4/mask=0 のまま（ザコ {zako.CollisionLayer}/{zako.CollisionMask}・ボス {boss.CollisionLayer}/{boss.CollisionMask}）",
            zako.CollisionLayer == 4 && zako.CollisionMask == 0 && boss.CollisionLayer == 4 && boss.CollisionMask == 0);
    }

    // ── スクショ：修正前（補正を白に戻した状態）と修正後を、同じポーズで撮り比べる ──
    private async Task Shots(string id, Player player, PlayerHitDot dot, Sprite2D body, Color tint, Color corrTint, Enemy[] enemies)
    {
        if (!_shot) return;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        var bodies = enemies.Select(e => e.GetNodeOrNull<Sprite2D>("Body")).Where(s => s != null).Select(s => s!).ToArray();
        // 盤面の時間ごと止める：背景スクロール・ビネット・オーラ・スペルの閃光が前後で動くと、
        //   同じ画素をくらべているつもりで「別の絵」をくらべることになる（FINAL は特に賑やか）。
        GetTree().Paused = true;
        Freeze(player, dot, enemies, true);   // 揺れ・呼吸・補正の再適用を止めて、前後で同じ絵にする
        Pose(player, body);

        void Apply(bool lifted)
        {
            var t = TintLift.Find(player);
            dot.SelfModulate = lifted ? TintLift.Of(t, TintLift.PlayerCore) : Colors.White;
            body.Modulate = lifted ? TintLift.Of(t, TintLift.PlayerBody) : Colors.White;
            foreach (var s in bodies) s.Modulate = lifted ? TintLift.Of(t, TintLift.EnemyBody) : Colors.White;
        }

        Apply(false);
        using (var img = await Grab())
        {
            Save(img, $"stage_{id}_before");
            Zoom(img, player, $"hitdot_{id}_before");
            var (screen, source, n) = Measure(img, body);
            _bodyBefore = screen; _bodySource = source; _bodySamples = n;
        }
        Apply(true);
        Pose(player, body);
        float ringPlain = 0f;
        using (var img = await Grab())
        {
            Save(img, $"stage_{id}_after");
            ringPlain = RingMean(img, player);   // シールド無しの輪の帯（後で泡ありと比べる）
            Zoom(img, player, $"hitdot_{id}_after");
            var (screen, source, n) = Measure(img, body);
            _body.Add((id, Div(_bodyBefore, corrTint), Div(screen, corrTint), source, Math.Min(n, _bodySamples)));
            var want = Mul(source, corrTint);   // 素材 × 汚染ティント＝Tint を完全に打ち消したときに出るべき色
            GD.Print($"[VIS] info {id}: 自機の胴（不透明 {n} 画素）素材={Fmt(source)} 汚染込みの狙い={Fmt(want)} 修正前={Fmt(_bodyBefore)} 修正後={Fmt(screen)}");
            GD.Print($"[VIS] info {id}: 素材に対する実効の明るさ 修正前={Fmt(Div(_bodyBefore, source))} → 修正後={Fmt(Div(screen, source))}"
                + $"（うち汚染ティントぶん {Fmt(corrTint)}）");
            Check($"{id} 修正後の自機は「素材×汚染ティント」の明るさで出ている（差 各チャンネル<0.06）", Near(screen, want, 0.06f));
            if (!tint.IsEqualApprox(Colors.White))
                Check($"{id} 修正前はそれより暗かった（Tint={Fmt(tint)} で沈んでいたことの確認）",
                    _bodyBefore.R < want.R - 0.02f || _bodyBefore.G < want.G - 0.02f || _bodyBefore.B < want.B - 0.02f);
        }

        // シールドの泡（ShieldPower>0 の輪）も同じ _Draw の中＝同じ補正で読めることを絵で残す。
        GetTree().Paused = false;
        Freeze(player, dot, enemies, false);
        player.ApplyPowerup(PowerKind.Shield);
        player.ApplyPowerup(PowerKind.Shield);   // 二重の輪（上限2）
        await Frames(6);   // 芯の _Process が泡を描き足すまで回す（ShieldPower>0 で毎フレーム QueueRedraw）
        Check($"{id} シールドを2枚張れた（ShieldPower={player.ShieldPower} tree.Paused={GetTree().Paused} dot.IsProcessing={dot.IsProcessing()}）",
            player.ShieldPower == 2);
        Check($"{id} シールド2枚を張っても芯の補正は変わらない（{Fmt(dot.SelfModulate)}）",
            dot.SelfModulate.IsEqualApprox(TintLift.Of(TintLift.Find(player), TintLift.PlayerCore)));
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().Paused = true;
        Freeze(player, dot, enemies, true);
        Pose(player, body);
        using (var img = await Grab())
        {
            Zoom(img, player, $"hitdot_{id}_shield");
            float ringShield = RingMean(img, player);
            GD.Print($"[VIS] info {id}: 輪の帯（半径 {ShieldRIn}..{ShieldROut}px）の明るさ 泡なし={ringPlain:0.000} → 泡あり={ringShield:0.000}");
            Check($"{id} シールドの泡が輪として読める（帯が {ringShield - ringPlain:+0.000;-0.000} 明るくなる）", ringShield > ringPlain + 0.02f);
        }
        GetTree().Paused = false;
        Freeze(player, dot, enemies, false);
    }

    // ── レイ面の終盤（汚染 0.80）の撮り比べ：濁りの色は同じまま、暗さだけが直っているか ──
    //   Root の _Process が毎フレーム汚染を書き戻す（StageProgress から算出）ので、入れる前に止める。
    private const float DeepCorruption = 0.80f;   // レイ面の終盤（設計書 4-b: 0.45→0.80）
    private async Task CorruptShots(string id, Node2D root, Player player, PlayerHitDot dot, Sprite2D body, Enemy[] enemies)
    {
        if (!_shot) return;
        // 直前のシールド検証で張った泡（半径 8.5px の白い円）は胴の計測窓に丸ごと重なるので外す。
        //   ShieldPower は private set の自動プロパティ＝反射でセッタを呼ぶ。芯は次フレームに描き直される。
        typeof(Player).GetProperty("ShieldPower")!.SetValue(player, 0);
        root.SetProcess(false);
        Write(player, "_corruption", DeepCorruption);
        await Frames(6);   // 泡が消え、自機の _PhysicsProcess が SelfModulate へ反映するまで
        Check($"{id} 計測前にシールドの泡を外した（ShieldPower={player.ShieldPower}）", player.ShieldPower == 0);
        CheckCorruption($"{id}/汚染{DeepCorruption:0.00}", body, DeepCorruption);
        var raw = CorruptTint(DeepCorruption);            // 明度の下限なし＝この追加対応の前の見え方
        var want = TintLift.KeepBright(raw);              // 下限あり＝今の見え方

        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().Paused = true;
        Freeze(player, dot, enemies, true);
        Pose(player, body);
        body.SelfModulate = raw;
        Color before;
        using (var img = await Grab())
        {
            Zoom(img, player, $"hitdot_{id}_corrupt_before");
            (before, _, _) = Measure(img, body);
        }
        body.SelfModulate = want;
        using (var img = await Grab())
        {
            Save(img, $"stage_{id}_corrupt_after");
            Zoom(img, player, $"hitdot_{id}_corrupt_after");
            var (screen, source, n) = Measure(img, body);
            float ys = TintLift.Luma(source), yb = TintLift.Luma(before), ya = TintLift.Luma(screen);
            GD.Print($"[VIS] info {id}/汚染{DeepCorruption:0.00}: 自機の胴（不透明 {n} 画素）素材={Fmt(source)}（輝度 {ys:0.000}）"
                + $" 修正前={Fmt(before)}（輝度 {yb:0.000}） 修正後={Fmt(screen)}（輝度 {ya:0.000}）");
            GD.Print($"[VIS] info {id}/汚染{DeepCorruption:0.00}: 素材比の輝度 修正前={yb / ys:0.000} → 修正後={ya / ys:0.000}");
            Check($"{id} 汚染 {DeepCorruption:0.00} でも自機は素材比 0.80 以上の輝度（{ya / ys:0.000}）", ya / ys >= 0.80f);
            Check($"{id} 汚染 {DeepCorruption:0.00} は下限なしより明るい（{yb / ys:0.000} → {ya / ys:0.000}）", ya > yb + 0.05f);
            Check($"{id} 汚染 {DeepCorruption:0.00} の見え方は「素材×下限つき濁り」と一致（差<0.06）", Near(screen, Mul(source, want), 0.06f));
        }
        GetTree().Paused = false;
        Freeze(player, dot, enemies, false);
        root.SetProcess(true);
    }

    // 無敵点滅の「消えている側」で撮らない／計測しない。物理を止めてから呼ぶので、以降は誰も書き戻さない。
    private static void Pose(Player player, Sprite2D body)
    {
        body.Visible = true;
        player.Modulate = Colors.White;
    }

    private static void Freeze(Player player, PlayerHitDot dot, Enemy[] enemies, bool frozen)
    {
        player.SetPhysicsProcess(!frozen);
        dot.SetProcess(!frozen);
        foreach (var e in enemies) if (IsInstanceValid(e)) e.SetPhysicsProcess(!frozen);
    }

    // ステージ間の食い違い（完全打ち消しなら、どの面でも自機は同じ明るさで出る）。
    private void CompareStages()
    {
        if (_body.Count < 2) return;
        // 汚染ティントぶんを割り戻した値でくらべる（面ごとの濁りは意図的なので、Tint の打ち消しだけを見る）。
        foreach (var (stage, before, after, source, n) in _body)
            GD.Print($"[VIS] info まとめ {stage}: 素材={Fmt(source)} 汚染を割り戻した 前={Fmt(before)} 後={Fmt(after)}（{n} 画素）");
        float spread = 0f, spreadBefore = 0f;
        for (int i = 0; i < _body.Count; i++)
            for (int j = i + 1; j < _body.Count; j++)
            {
                spread = Mathf.Max(spread, Diff(_body[i].After, _body[j].After));
                spreadBefore = Mathf.Max(spreadBefore, Diff(_body[i].Before, _body[j].Before));
            }
        GD.Print($"[VIS] info 自機の明るさのステージ間の最大差：修正前 {spreadBefore:0.000} → 修正後 {spread:0.000}");
        Check($"4ステージで自機の明るさがそろっている（最大差 {spread:0.000} < 0.05）", spread < 0.05f);
    }

    private static float Diff(Color a, Color b) =>
        Mathf.Max(Mathf.Abs(a.R - b.R), Mathf.Max(Mathf.Abs(a.G - b.G), Mathf.Abs(a.B - b.B)));
    private static bool Near(Color a, Color b, float tol) => Diff(a, b) < tol;
    private static Color Mul(Color a, Color b) => new Color(a.R * b.R, a.G * b.G, a.B * b.B);
    private static Color Div(Color a, Color b) => new Color(a.R / Mathf.Max(b.R, 0.01f), a.G / Mathf.Max(b.G, 0.01f), a.B / Mathf.Max(b.B, 0.01f));
    private static string Fmt(Color c) => $"({c.R:0.000}, {c.G:0.000}, {c.B:0.000})";

    // ── 画面の取得・保存 ──
    private async Task<Image> Grab()
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return GetViewport().GetTexture().GetImage();
    }

    private void Save(Image img, string name) => Check($"screenshot {name}", img.SavePng($"{_out}/{name}.png") == Error.Ok);

    // 自機の周囲（±ZoomHalf 内部px）を切り出して3倍（最近傍）で保存する。
    private const float ZoomHalf = 26f;
    private void Zoom(Image img, Node2D at, string name)
    {
        var vis = GetViewport().GetVisibleRect().Size;
        float sx = img.GetWidth() / vis.X, sy = img.GetHeight() / vis.Y;
        var c = at.GetGlobalTransformWithCanvas() * Vector2.Zero;
        int x0 = Mathf.Clamp((int)((c.X - ZoomHalf) * sx), 0, img.GetWidth() - 2);
        int y0 = Mathf.Clamp((int)((c.Y - ZoomHalf) * sy), 0, img.GetHeight() - 2);
        int x1 = Mathf.Clamp((int)Mathf.Ceil((c.X + ZoomHalf) * sx), x0 + 1, img.GetWidth());
        int y1 = Mathf.Clamp((int)Mathf.Ceil((c.Y + ZoomHalf) * sy), y0 + 1, img.GetHeight());
        using var crop = img.GetRegion(new Rect2I(x0, y0, x1 - x0, y1 - y0));
        crop.Resize(crop.GetWidth() * 3, crop.GetHeight() * 3, Image.Interpolation.Nearest);
        Check($"screenshot {name}（切り抜き {x1 - x0}x{y1 - y0} → {crop.GetWidth()}x{crop.GetHeight()}）",
            crop.SavePng($"{_out}/{name}.png") == Error.Ok);
    }

    // 自機の中心から半径 ShieldRIn..ShieldROut（内部px）の帯の平均輝度。シールドの泡（Radius+6.5 の円と
    //   Radius+2.6 の外輪）がこの帯に乗るので、泡の有無でここが明るくなるかを見る。
    private const float ShieldRIn = 6f, ShieldROut = 12f;
    private float RingMean(Image img, Node2D at)
    {
        var vis = GetViewport().GetVisibleRect().Size;
        float sx = img.GetWidth() / vis.X, sy = img.GetHeight() / vis.Y;
        var c = at.GetGlobalTransformWithCanvas() * Vector2.Zero;
        double sum = 0; int n = 0;
        for (float dy = -ShieldROut; dy <= ShieldROut; dy += 0.25f)
            for (float dx = -ShieldROut; dx <= ShieldROut; dx += 0.25f)
            {
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < ShieldRIn || r > ShieldROut) continue;
                int x = Mathf.Clamp((int)((c.X + dx) * sx), 0, img.GetWidth() - 1);
                int y = Mathf.Clamp((int)((c.Y + dy) * sy), 0, img.GetHeight() - 1);
                var p = img.GetPixel(x, y);
                sum += 0.299 * p.R + 0.587 * p.G + 0.114 * p.B; n++;
            }
        return n == 0 ? 0f : (float)(sum / n);
    }

    // 自機の「素材が不透明な画素」だけを画面から拾って平均する。背景が混ざらないので、
    //   素材（昼の色）の同じ画素の平均と直接くらべられる＝「Tint で沈んでいないか」を数値で言える。
    private (Color Screen, Color Source, int Samples) Measure(Image img, Sprite2D body)
    {
        using var src = body.Texture.GetImage();
        var size = body.Texture.GetSize();
        var vis = GetViewport().GetVisibleRect().Size;
        float sx = img.GetWidth() / vis.X, sy = img.GetHeight() / vis.Y;
        var xf = body.GetGlobalTransformWithCanvas();
        var inv = xf.AffineInverse();
        // 絵の中央 40%（胴＝髪・衣装の面積が大きいところ）だけを見る。輪郭の半透明を避ける。
        var a = xf * (-size * 0.2f); var b = xf * (size * 0.2f);
        int x0 = Mathf.Clamp((int)(Mathf.Min(a.X, b.X) * sx), 0, img.GetWidth() - 1);
        int y0 = Mathf.Clamp((int)(Mathf.Min(a.Y, b.Y) * sy), 0, img.GetHeight() - 1);
        int x1 = Mathf.Clamp((int)Mathf.Ceil(Mathf.Max(a.X, b.X) * sx), x0 + 1, img.GetWidth());
        int y1 = Mathf.Clamp((int)Mathf.Ceil(Mathf.Max(a.Y, b.Y) * sy), y0 + 1, img.GetHeight());
        double sr = 0, sg = 0, sb = 0;
        double tr = 0, tg = 0, tb = 0;
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var local = inv * new Vector2(x / sx, y / sy) + size * 0.5f;   // テクスチャ画素座標
                if (body.FlipH) local.X = size.X - local.X;
                var t = new Vector2I(Mathf.RoundToInt(local.X), Mathf.RoundToInt(local.Y));
                if (t.X < 1 || t.Y < 1 || t.X >= size.X - 1 || t.Y >= size.Y - 1) continue;
                var s = src.GetPixelv(t);
                if (s.A < 0.99f) continue;   // 輪郭の半透明＝背景が混ざる画素は数えない
                var c = img.GetPixel(x, y);
                sr += c.R; sg += c.G; sb += c.B;
                tr += s.R; tg += s.G; tb += s.B;
                n++;
            }
        if (n == 0) return (Colors.Black, Colors.Black, 0);
        return (new Color((float)(sr / n), (float)(sg / n), (float)(sb / n)),
                new Color((float)(tr / n), (float)(tg / n), (float)(tb / n)), n);
    }

    private void Check(string name, bool ok)
    {
        if (!ok) _fail++;
        GD.Print($"[VIS] {(ok ? "OK " : "NG ")} {name}");
    }

    public override void _Process(double delta)
    {
        foreach (var (n, at) in _pin) if (IsInstanceValid(n)) n.GlobalPosition = at;
        // ボスのイントロ会話が居座ると BubblePaused で時間が止まるので畳み続ける（BossGaugeQa と同じ）。
        if (_hud != null && IsInstanceValid(_hud) && Hud.BubblePaused) { _hud.HoldBubble = false; _hud.HideBubble(); }
        // FINAL の段間カットシーン（MinaPhaseScene）は World と GameManager の ProcessMode を Disabled に落とす＝
        //   自機・芯・敵の _Process が丸ごと止まり、シールドの泡もボスの登場も進まなくなる。検証したいのは
        //   戦闘画面そのものなので、出てきたら畳む（QueueFree → _ExitTree → Restore が ProcessMode を戻す）。
        foreach (var n in GetTree().GetNodesInGroup("mina_phase_scene"))
            if (IsInstanceValid(n) && !n.IsQueuedForDeletion()) { n.QueueFree(); _phaseScenes++; }
    }

    private int _phaseScenes;

    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
