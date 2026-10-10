using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// EnemyFacingQa : 「敵は常に自機の方を向く」の検査（2026-09-28 作者指示
//   「敵が通りすぎたとき操作してるキャラの方向に向くように修正してほしい」「うしろからでたときも同様」）。
//
// 以前は向きが出現時の EnemySpec.FlipH で固定だった＝自機を追い越しても、自機の背後（左）から湧いても
// 背中を向けたまま撃っていた。Enemy.TickFacing が毎フレーム「自機がどちら側か」で向きを引き直す。
//
// ここで確かめること:
//   (a) 敵の右に自機 → 左を向く／左に自機 → 右を向く／左右が入れ替わると向きも変わる
//   (b) 自機と x がほぼ同じでも毎フレーム反転しない（ヒステリシス＝Enemy.FacingDeadzone の帯）
//   (c) 反転の前後で「絵の中身」の水平中心と足元が動かない（UiKit.ContentRect 基準）
//   (d) 当たり判定（半径・レイヤー・マスク・本体位置）と弾の湧き位置・発射方向が向きに影響されない
//   (e) 浄化後は位置・向きを保ち、その場でフェードして消える
//   (f) 上記がザコ全種（人型12種＋道具6種＋アンチくん＋回り込み／バズ壁／祈り運び、
//       さらに別クラスの基本種 GlyphMote / PageShard）で成り立つ
//   (g) 中ボス（CameoBoss＝BossMover 側の向き）も自機と左右が入れ替われば向き直す
//
// 実行:
//   ヘッドレス（検査だけ）: Godot --headless --path . res://tools/qa_enemy_facing.tscn
//   窓あり（＋比較スクショ）: Godot --path . res://tools/qa_enemy_facing.tscn
//     → build/shots_facing/<名前>_compare.png（左＝自機が左／右＝自機が右）
public partial class EnemyFacingQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool Headless => DisplayServer.GetName() == "headless";

    // 敵を置く位置と、その左右に置く自機の位置（どちらも盤面 120..384 の内側）。
    private static readonly Vector2 Anchor = new Vector2(268f, 112f);
    private const float SideDx = 80f;      // 左右の検証で自機を置く距離（Deadzone より十分大きい）

    // 撮影の枠（窓ありのときだけ使う）。内部解像度 384×216 を Zoom 倍で描く。
    private const int Zoom = 3;
    private const int CropW = 208, CropH = 132;   // ザコ＋自機が収まる大きさ

    private float _deadzone;
    private int _checks;

    private static T Read<T>(object o, string name, Type? type = null)
        => (T)(type ?? o.GetType()).GetField(name, Private)!.GetValue(o)!;
    private static void Write(object o, string name, object value, Type? type = null)
        => (type ?? o.GetType()).GetField(name, Private)!.SetValue(o, value);
    private static object? Call(object o, string name, params object[] args)
        => o.GetType().GetMethod(name, Private)!.Invoke(o, args);
    private static object? CallBase(object o, Type type, string name, params object[] args)
        => type.GetMethod(name, Private)!.Invoke(o, args);
    private void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        _checks++;
        GD.Print($"[EnemyFacingQA] PASS {message}");
    }

    // 素材の基準向き（artFacesRight）と「どちらを向きたいか」から、期待する FlipH を出す。
    // 右向きに描かれた素材は左を向くのに反転が要り、左向き素材はその逆＝規則は1本。
    private static bool ExpectFlip(bool faceLeft, bool artFacesRight) => faceLeft == artFacesRight;

    // 向きを直接指定する（自機の位置を介さず Enemy.SetFacingLeft を呼ぶ）。
    private static void Face(Enemy e, bool faceLeft)
        => typeof(Enemy).GetMethod("SetFacingLeft", Private)!.Invoke(e, new object[] { faceLeft });

    // 本物の 1 フレームを手で進める。進入の移動だけ打ち消して「向きの変化」だけを見る
    //（位置を戻すのは検査の都合。向きは移動後の位置で決まるが、1フレームの移動は 1px 未満）。
    private static void Tick(Enemy e, Vector2 hold, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            e._PhysicsProcess(1.0 / 60.0);
            e.GlobalPosition = hold;
        }
    }

    // Sprite2D(Centered) が実際に描く「中身」の矩形（グローバル座標）。
    //   描画枠は Offset を中心に置かれ、FlipH はその枠の中で中身を折り返す
    //   （UiKit.DrawPortraitScaled と同じ規約）。ここで中身＝不透明部分の外接矩形。
    private static Rect2 ContentBox(Sprite2D s)
    {
        var c = UiKit.ContentRect(s.Texture);
        Vector2 half = s.Texture.GetSize() / 2f;
        float l = c.Position.X - half.X, r = c.Position.X + c.Size.X - half.X;
        if (s.FlipH) (l, r) = (-r, -l);
        float t = c.Position.Y - half.Y, b = c.Position.Y + c.Size.Y - half.Y;
        var a = s.ToGlobal(new Vector2(l, t) + s.Offset);
        var d = s.ToGlobal(new Vector2(r, b) + s.Offset);
        return new Rect2(a, d - a);
    }

    private Bullet[] EnemyBullets()
        => GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active).ToArray();

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _deadzone = (float)typeof(Enemy)
                .GetField("FacingDeadzone", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Check(_deadzone > 0f, $"hysteresis band is declared (±{_deadzone:0}px)");

            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Normal;
            game.SelectedEntry = GameManager.StageEntry.Start;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(384 * Zoom, 216 * Zoom));
            await Frames(1);

            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
            stage.SetProcess(false);
            var world = root.GetNode<Node2D>("World");
            world.ProcessMode = ProcessModeEnum.Inherit;
            var player = world.GetNode<Player>("Player");
            player.SetPhysicsProcess(false);
            Write(player, "_invincible", true);
            Write(player, "_invincibleTimer", 999f);
            var hud = root.GetNode<Hud>("Hud");
            hud.HoldBubble = false;
            hud.HideBubble();
            Hud.BubblePaused = false;
            Audio.Instance?.StopMusic(0);
            await Frames(3);

            var kinds = Roster();
            Check(kinds.Count(k => k.Spec.Humanoid) == 12, "twelve humanoid characters are covered");
            Check(kinds.Any(k => k.Spec.FlipH) && kinds.Any(k => !k.Spec.FlipH),
                "roster mixes right-drawn and left-drawn artwork");
            foreach (var (name, spec) in kinds) await CheckKind(world, player, name, spec);

            // 基本種（EnemySpec を持たない別クラス）も同じ基底の規則で自機を向く。
            await CheckBasic(world, player, new GlyphMote(), "GlyphMote");
            await CheckBasic(world, player, new PageShard(), "PageShard");

            var held = new GlyphMote();
            world.AddChild(held);
            held.SetPhysicsProcess(false);
            held.GlobalPosition = new Vector2(Field.Left - 28f, Anchor.Y);
            Write(held, "PurifiedExitHoldOverride", 3.6, typeof(Enemy));
            await CheckExit(held, player, "extended hold near left edge", 3.6);
            await Frames(2);

            await CheckCameo(stage, player);

            if (!Headless) await Shots(world, player, stage);

            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer p) { p.Stop(); p.Stream = null; }
            await Task.Delay(200);
            await Frames(3);
            GD.Print($"[EnemyFacingQA] ALL PASS ({_checks} checks)");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[EnemyFacingQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    // 道中に湧きうるザコ全種。人型12種＋道具6種＋アンチくん2種＋回り込み／バズ壁／祈り運び。
    private static List<(string Name, EnemySpec Spec)> Roster()
    {
        var list = new List<(string, EnemySpec)>();
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
            foreach (var spec in EnemyTable.CharactersFor(theme))
                list.Add(($"{theme}/{spec.Pattern}", spec));
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Default })
        {
            var (shooter, drifter) = EnemyTable.For(theme);
            list.Add(($"{theme}/shooter", shooter));
            list.Add(($"{theme}/drifter", drifter));
        }
        list.Add(("Akari/flanker", EnemyTable.Flanker(StageTheme.Akari)));
        list.Add(("Rei/buzzwall", EnemyTable.BuzzWall(StageTheme.Rei)));
        list.Add(("Koharu/prayer", EnemyTable.PrayerCarrier()));
        return list;
    }

    private async Task CheckKind(Node2D world, Player player, string name, EnemySpec spec)
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        pool.DespawnAll();
        var enemy = new MidEnemy();
        enemy.Configure(spec);
        world.AddChild(enemy);
        enemy.GlobalPosition = Anchor;
        enemy.SetPhysicsProcess(false);
        var body = enemy.GetNode<Sprite2D>("Body");
        bool artRight = spec.FlipH;   // 素材が右向きに描かれているか（＝自機側を向かせるのに反転が要る）
        Check(body.FlipH == artRight, $"{name}: spawns facing the player side");

        // ── (a)(f) 自機の側で向きが決まり、左右が入れ替わるたびに向き直す ──
        player.GlobalPosition = new Vector2(Anchor.X - SideDx, Anchor.Y);
        Tick(enemy, Anchor, 2);
        Check(body.FlipH == ExpectFlip(true, artRight), $"{name}: faces left while the player is on the left");
        player.GlobalPosition = new Vector2(Anchor.X + SideDx, Anchor.Y);
        Tick(enemy, Anchor, 2);
        Check(body.FlipH == ExpectFlip(false, artRight), $"{name}: turns around once the player passes it");
        player.GlobalPosition = new Vector2(Anchor.X - SideDx, Anchor.Y);
        Tick(enemy, Anchor, 2);
        Check(body.FlipH == ExpectFlip(true, artRight), $"{name}: turns back when the sides swap again");

        // ── (b) ヒステリシス：帯の中で微振動させても一度も反転しない ──
        int flips = 0;
        bool prev = body.FlipH;
        for (int i = 0; i < 180; i++)
        {
            player.GlobalPosition = new Vector2(Anchor.X + Mathf.Sin(i * 0.7f) * (_deadzone - 1f), Anchor.Y);
            Tick(enemy, Anchor, 1);
            if (body.FlipH != prev) { flips++; prev = body.FlipH; }
        }
        Check(flips == 0, $"{name}: jittering inside the ±{_deadzone:0}px band never flips the sprite");
        player.GlobalPosition = new Vector2(Anchor.X + _deadzone - 1f, Anchor.Y);
        Tick(enemy, Anchor, 2);
        Check(body.FlipH == ExpectFlip(true, artRight), $"{name}: stays put just inside the band");
        player.GlobalPosition = new Vector2(Anchor.X + _deadzone + 2f, Anchor.Y);
        Tick(enemy, Anchor, 2);
        Check(body.FlipH == ExpectFlip(false, artRight), $"{name}: turns as soon as the player clears the band");

        // ── (c) 反転しても「絵の中身」の水平中心と足元が動かない ──
        body.Rotation = 0f;   // 生命感モーションの傾きを外して、反転だけの差を見る
        Face(enemy, true);
        var left = ContentBox(body);
        Face(enemy, false);
        var right = ContentBox(body);
        Check(Mathf.Abs(left.GetCenter().X - right.GetCenter().X) < 0.01f
            && Mathf.Abs(left.End.Y - right.End.Y) < 0.01f
            && Mathf.Abs(left.Size.X - right.Size.X) < 0.01f,
            $"{name}: the drawn artwork keeps its centre and feet across the flip");

        // ── (d) 当たり判定と弾の出どころは向きに左右されない ──
        var shape = enemy.GetChildren().OfType<CollisionShape2D>().First();
        Face(enemy, true);
        var before = Snapshot(enemy, shape);
        Face(enemy, false);
        Check(Snapshot(enemy, shape) == before, $"{name}: collision and shot origin ignore the facing");
        if (spec.Humanoid)
        {
            player.GlobalPosition = new Vector2(Anchor.X - SideDx, Anchor.Y);
            var shotsLeft = FireOnce(enemy, true);
            var shotsRight = FireOnce(enemy, false);
            Check(shotsLeft.Length > 0 && shotsLeft.SequenceEqual(shotsRight),
                $"{name}: the volley keeps the same origins, speeds and directions");
        }

        player.GlobalPosition = new Vector2(Anchor.X + SideDx, Anchor.Y);
        Tick(enemy, Anchor, 2);
        await CheckExit(enemy, player, name);

        pool.DespawnAll();
        await Frames(2);
    }

    private async Task CheckExit(Enemy enemy, Player player, string name, double hold = 0.6)
    {
        Vector2 at = enemy.GlobalPosition;
        var body = enemy.GetNode<Sprite2D>("Body");
        bool flip = body.FlipH;
        if (enemy.HasHpBar) CallBase(enemy, typeof(Enemy), "Redeem");
        else enemy.Purify();
        Check(enemy.IsPurified && !enemy.IsInGroup("enemies"), $"{name}: defeat removes target eligibility");
        Check(body.FlipH == flip, $"{name}: defeat preserves the facing direction");
        if (Read<bool>(enemy, "_crying", typeof(Enemy)))
        {
            enemy._PhysicsProcess(0.2);
            Check(enemy.GlobalPosition.IsEqualApprox(at), $"{name}: defeat dialogue stays at the defeat position");
            CallBase(enemy, typeof(Enemy), "FinishCry");
        }
        await Frames(2);
        Check(!enemy.Monitorable && Read<CollisionShape2D>(enemy, "_bodyShape", typeof(Enemy)).Disabled,
            $"{name}: contact collision is disabled");

        player.GlobalPosition = new Vector2(at.X - SideDx, at.Y);
        bool stationary = true, sameFacing = true, monotonic = true, fading = false, holds = true;
        float lastAlpha = enemy.Modulate.A;
        double elapsed = 0;
        for (int i = 0; i < 110 && !enemy.IsQueuedForDeletion(); i++)
        {
            enemy._PhysicsProcess(0.05);
            elapsed += 0.05;
            float alpha = enemy.Modulate.A;
            stationary &= enemy.GlobalPosition.IsEqualApprox(at);
            sameFacing &= body.FlipH == flip;
            monotonic &= alpha <= lastAlpha + 0.0001f;
            fading |= alpha > 0 && alpha < 1;
            if (elapsed < hold - 0.01) holds &= Mathf.IsEqualApprox(alpha, 1);
            lastAlpha = alpha;
        }
        Check(stationary && sameFacing, $"{name}: no translation or turn throughout disappearance");
        Check(holds && monotonic && fading, $"{name}: existing hold and smooth fade are preserved");
        Check(enemy.IsQueuedForDeletion() && Mathf.Abs((float)(elapsed - hold - 0.9)) < 0.06f,
            $"{name}: despawns after the fade ({elapsed:0.00}s)");
    }

    // 当たり判定と弾の湧き位置のスナップショット（向きで動いてはいけない値だけ）。
    private static (float R, float H, uint Layer, uint Mask, Vector2 Pos, Vector2 Shot) Snapshot(Enemy e, CollisionShape2D s)
        => (s.Shape is CircleShape2D c ? c.Radius : ((CapsuleShape2D)s.Shape).Radius,
            s.Shape is CapsuleShape2D p ? p.Height : 0f,
            e.CollisionLayer, e.CollisionMask, e.GlobalPosition, e.ShotCenter);

    // 人型12種の斉射を1回ぶん撃たせ、弾の（湧き位置・速度・半径）を並べて返す。
    // 人型の斉射は乱数を使わない＝同じ条件なら完全に同じ結果になる（向きだけを変えて比べられる）。
    private (Vector2 Pos, Vector2 Vel, float R)[] FireOnce(MidEnemy e, bool faceLeft)
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        pool.DespawnAll();
        Face(e, faceLeft);
        Write(e, "_characterAttackIndex", 0);
        Write(e, "_salvoIndex", 0);
        Write(e, "_salvoRemaining", 0);
        Call(e, "BeginCharacterAttack");
        Call(e, "FireCharacterSalvo");
        var shots = EnemyBullets()
            .Select(b => (b.GlobalPosition, b.Velocity, b.Radius))
            .OrderBy(t => t.GlobalPosition.X).ThenBy(t => t.GlobalPosition.Y)
            .ThenBy(t => t.Velocity.X).ThenBy(t => t.Velocity.Y).ToArray();
        pool.DespawnAll();
        return shots;
    }

    // 基本種（GlyphMote/PageShard）。EnemySpec を持たず素材は右向きの「アンチくん」固定。
    private async Task CheckBasic(Node2D world, Player player, Enemy enemy, string name)
    {
        world.AddChild(enemy);
        enemy.GlobalPosition = Anchor;
        enemy.SetPhysicsProcess(false);
        var body = enemy.GetNode<Sprite2D>("Body");
        player.GlobalPosition = new Vector2(Anchor.X - SideDx, Anchor.Y);
        Tick(enemy, Anchor, 2);
        Check(body.FlipH, $"{name}: faces left while the player is on the left");
        player.GlobalPosition = new Vector2(Anchor.X + SideDx, Anchor.Y);
        Tick(enemy, Anchor, 2);
        Check(!body.FlipH, $"{name}: turns around once the player passes it");
        await CheckExit(enemy, player, name);
        await Frames(2);
    }

    // 中ボス（CameoBoss）。向きは BossMover が握る（自機が反対側へ 40px 以上・0.6 秒続いたら反転）。
    // 徘徊ゾーン x≈215..325 に対し自機は盤面 120..384 を動ける＝左右は実際に入れ替わる。
    private async Task CheckCameo(Node stage, Player player)
    {
        var cameo = await SpawnCameo(stage, new Vector2(300f, 92f));
        var body = cameo.GetNode<Sprite2D>("Body");

        // BossMover の保持は 0.6 秒＝36 物理フレーム。余裕を見て 60 フレームずつ進める。
        Vector2 hold = cameo.GlobalPosition;
        player.GlobalPosition = new Vector2(Field.Left + 16f, 150f);
        Tick(cameo, hold, 60);
        Check(Read<bool>(cameo, "FacePlayer", typeof(Enemy)) == false,
            "cameo hands its facing to BossMover instead of the shared tick");
        Check(body.FlipH, "cameo faces left while the player is on the left");
        Check(hold.X - player.GlobalPosition.X > 40f, "the cameo really is to the player's right");
        player.GlobalPosition = new Vector2(Field.Right - 14f, 150f);
        Tick(cameo, hold, 60);
        Check(!body.FlipH, "cameo turns around once the player gets behind it");
        player.GlobalPosition = new Vector2(Field.Left + 16f, 150f);
        Tick(cameo, hold, 60);
        Check(body.FlipH, "cameo turns back when the sides swap again");
        cameo.SetProcess(false);
        await CheckExit(cameo, player, "cameo");
        Write(stage, "_cameo", null!);
        await Frames(3);

        var entering = await SpawnCameo(stage, new Vector2(300f, 92f));
        entering.SetProcess(false);
        Write(entering, "_entering", true, typeof(Enemy));
        Write(entering, "_entInit", false, typeof(Enemy));
        await CheckExit(entering, player, "defeat during entrance");
        Write(stage, "_cameo", null!);
        await Frames(3);
    }

    // 中ボスを1体出し、登場演出だけ手で送り切って戦闘状態にする（qa_koharu_art と同じ作法）。
    private async Task<CameoBoss> SpawnCameo(Node stage, Vector2 at)
    {
        GetNode<GameManager>("/root/Game").MarkIdleDialogSeen("once_midboss_shield");
        Write(stage, "_stepStarted", false);
        Write(stage, "_lunatic", true);     // 登場カットシーンを挟まずその場で出す（qa_cameo_intro の担当）
        Call(stage, "Step_BossCameo", 0d);
        Write(stage, "_lunatic", false);
        var cameo = Read<CameoBoss>(stage, "_cameo");
        cameo.SetPhysicsProcess(false);
        cameo.GlobalPosition = at;
        CallBase(cameo, typeof(Enemy), "TickEntrance", 0d);
        CallBase(cameo, typeof(Enemy), "TickEntrance", 2d);
        await Frames(2);
        return cameo;
    }

    // ─── 比較スクショ（窓ありのときだけ）───
    // 左＝自機が敵の左／右＝自機が敵の右。1枚に並べて build/shots_facing/ へ。
    private async Task Shots(Node2D world, Player player, Node stage)
    {
        var root = (Node2D)GetTree().CurrentScene;
        root.GetNode<Hud>("Hud").Visible = false;
        root.GetNode<StageBackground>("StageBackground").SetProcess(false);
        if (FxLayer.Instance != null) FxLayer.Instance.Visible = false;
        await Frames(2);

        foreach (var (name, spec) in new[]
        {
            ("akari_deadline", EnemyTable.CharactersFor(StageTheme.Akari).First(s => s.Pattern == AttackPattern.AkariDeadline)),
            ("rei_anonymous", EnemyTable.CharactersFor(StageTheme.Rei).First(s => s.Pattern == AttackPattern.ReiAnonymous)),
            ("koharu_cheer", EnemyTable.CharactersFor(StageTheme.Koharu).First(s => s.Pattern == AttackPattern.KoharuCheer)),
            ("mina_memory", EnemyTable.CharactersFor(StageTheme.Mina).First(s => s.Pattern == AttackPattern.MinaMemory)),
            ("rei_icon", EnemyTable.For(StageTheme.Rei).shooter),
        })
        {
            var enemy = new MidEnemy();
            enemy.Configure(spec);
            world.AddChild(enemy);
            enemy.GlobalPosition = Anchor;
            enemy.SetPhysicsProcess(false);
            var crop = new Rect2I((int)Anchor.X - CropW / 2, (int)Anchor.Y - CropH / 2, CropW, CropH);
            using var left = await Panel(enemy, player, new Vector2(Anchor.X - SideDx, Anchor.Y), crop);
            using var right = await Panel(enemy, player, new Vector2(Anchor.X + SideDx, Anchor.Y), crop);
            Save(name, left, right);
            enemy.QueueFree();
            await Frames(2);
        }

        // 中ボスは盤面ごと撮る（立ち絵が大きく、自機との位置関係も一緒に見せたい）。
        var cameo = await SpawnCameo(stage, new Vector2(300f, 92f));
        var board = new Rect2I((int)Field.Left, (int)Field.Top, (int)Field.Width, (int)Field.Height);
        using (var l = await Panel(cameo, player, new Vector2(Field.Left + 16f, 150f), board, 60))
        using (var r = await Panel(cameo, player, new Vector2(Field.Right - 14f, 150f), board, 60))
            Save("cameo_akari", l, r);
        cameo.QueueFree();
        Write(stage, "_cameo", null!);
        await Frames(3);
    }

    // 自機を置いて向きが決まるまで進め、切り出した1枚を返す。
    // ticks＝手で進める物理フレーム数（中ボスは BossMover の保持 0.6 秒ぶん要る）。
    private async Task<Image> Panel(Enemy enemy, Player player, Vector2 playerAt, Rect2I crop, int ticks = 3)
    {
        player.GlobalPosition = playerAt;
        Tick(enemy, enemy.GlobalPosition, ticks);
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        frame.Convert(Image.Format.Rgba8);
        var shot = Image.CreateEmpty(crop.Size.X * Zoom, crop.Size.Y * Zoom, false, Image.Format.Rgba8);
        shot.BlitRect(frame, new Rect2I(crop.Position * Zoom, crop.Size * Zoom), Vector2I.Zero);
        return shot;
    }

    // 左＝自機が左／右＝自機が右 を白線1本で仕切って1枚にする。
    private static void Save(string name, Image left, Image right)
    {
        const int Rule = 2;
        int w = left.GetWidth(), h = left.GetHeight();
        var sheet = Image.CreateEmpty(w * 2 + Rule, h, false, Image.Format.Rgba8);
        sheet.Fill(new Color(1f, 1f, 1f, 1f));
        sheet.BlitRect(left, new Rect2I(0, 0, w, h), Vector2I.Zero);
        sheet.BlitRect(right, new Rect2I(0, 0, w, h), new Vector2I(w + Rule, 0));
        string dir = ProjectSettings.GlobalizePath("res://build/shots_facing");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        if (sheet.SavePng($"{dir}/{name}_compare.png") != Error.Ok)
            throw new Exception($"{name}: could not save the comparison sheet");
        GD.Print($"[EnemyFacingQA] saved {name}_compare.png (left = player on the left, right = player on the right)");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
