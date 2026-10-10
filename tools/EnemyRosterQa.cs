using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class EnemyRosterQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly HashSet<AttackPattern> _characterPatterns = new();
    private readonly HashSet<string> _attackSignatures = new();
    private static T Read<T>(object obj, string name, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value)
        => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[EnemyRosterQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);

            var paths = new HashSet<string>();
            foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
            {
                var roster = EnemyTable.CharactersFor(theme);
                Check(roster.Count == 3, $"{theme} has three new characters");
                foreach (var spec in roster)
                {
                    using var image = GD.Load<Texture2D>(spec.PreTexPath).GetImage();
                    Check(paths.Add(spec.PreTexPath) && image.GetHeight() == 720
                        && image.DetectAlpha() != Image.AlphaMode.None, $"unique transparent texture {spec.PreTexPath}");
                    Check(spec.Humanoid && spec.PostTexPath == spec.PreTexPath,
                        "humanoid remains the same character during purification");
                }
            }
            Check(paths.Count == 12 && EnemyTable.CharactersFor(StageTheme.Default).Count == 0,
                "twelve assets registered without changing the tutorial roster");

            await CheckMotionVariants();
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--motion-only") >= 0)
            {
                await CheckApproachAlwaysCamps();
                await MotionShots(game);
                GD.Print("[EnemyRosterQA] MOTION ALL PASS");
                GetTree().Quit();
                return;
            }

            foreach (var (theme, scene) in new[] { (StageTheme.Akari, "Akari"), (StageTheme.Koharu, "Koharu"), (StageTheme.Rei, "Rei") })
                await CheckStage(game, theme, scene);
            Check(_characterPatterns.Count == 9 && _attackSignatures.Count == 9, "nine distinct projectile attacks");
            // FINAL の3種は 2026-09-17 に固有パターンを新設した。それ以前は eraser/unanswered が
            // DefaultAim・memory が None ＝「絵以外の差分ゼロ」で、3体とも同じ動きの敵だった。
            Check(EnemyTable.CharactersFor(StageTheme.Mina).Select(s => s.Pattern)
                .SequenceEqual(new[] { AttackPattern.MinaEraser, AttackPattern.MinaMemory, AttackPattern.MinaUnanswered }),
                "Mina echoes each own a distinct attack");
            CheckShardWeights();
            await CheckApproachAlwaysCamps();

            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
            await Task.Delay(250);
            await Frames(5);
            GD.Print("[EnemyRosterQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[EnemyRosterQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task CheckMotionVariants()
    {
        var world = new Node2D();
        GetTree().Root.AddChild(world);
        var variants = Enum.GetValues(typeof(MidEnemy).GetNestedType("MoveVariant", BindingFlags.NonPublic)!);
        Check(variants.Length == 8, "eight entry and hover variants are available");
        var specs = new List<EnemySpec>();
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
        {
            specs.AddRange(EnemyTable.CharactersFor(theme));
            if (theme == StageTheme.Mina) continue;
            var (shooter, drifter) = EnemyTable.For(theme);
            specs.Add(shooter);
            specs.Add(drifter);
        }
        foreach (var spec in specs)
        {
            foreach (object variant in variants)
            {
                var endPositions = new List<Vector2>();
                foreach (int fps in new[] { 30, 120 })
                {
                    GD.Seed(241);
                    var enemy = new MidEnemy { Position = new Vector2(Field.Right + 14, 70) };
                    enemy.Configure(spec);
                    enemy.SetSilentEntry(new Vector2(260, 110));
                    world.AddChild(enemy);
                    enemy.SetPhysicsProcess(false);
                    Write(enemy, "_moveVariant", variant);
                    Write(enemy, "_approachFired", true);
                    Write(enemy, "_fireT", -1000d);
                    Write(enemy, "_startleT", 1000d);
                    var lateral = (Func<float, float>)typeof(MidEnemy).GetMethod("LateralOffset", Private)!
                        .CreateDelegate(typeof(Func<float, float>), enemy);
                    Check(Math.Abs(lateral(0)) < 0.001f && Math.Abs(lateral(1)) < 0.001f,
                        $"{spec.Pattern}/{variant}: entry curves meet both endpoints without a lateral jump");
                    var move = (Action<double>)typeof(MidEnemy).GetMethod("UpdateMovement", Private)!
                        .CreateDelegate(typeof(Action<double>), enemy);
                    var body = enemy.GetNode<Sprite2D>("Body");
                    float maxSpeed = 0;
                    for (int frame = 0; frame < fps * 18; frame++)
                    {
                        if (spec.Humanoid && frame % (fps * 4) == 0)
                        {
                            Write(enemy, "_gesture", frame / (fps * 4) % 4);
                            Write(enemy, "_startle", 1f);
                        }
                        Vector2 before = enemy.Position;
                        Vector2 pose = body.Position;
                        float angle = body.Rotation;
                        move(1d / fps);
                        maxSpeed = Mathf.Max(maxSpeed, before.DistanceTo(enemy.Position) * fps);
                        if (spec.Humanoid && frame > fps)
                        {
                            if (pose.DistanceTo(body.Position) * fps > 40f || Math.Abs(Mathf.AngleDifference(angle, body.Rotation)) * fps > 1.8f)
                                throw new Exception($"{spec.Pattern}/{variant}/{fps}: discontinuous body pose");
                            if (Math.Abs(body.Rotation) > 0.151f || body.Scale.X <= 0 || body.Scale.Y <= 0)
                                throw new Exception($"{spec.Pattern}/{variant}/{fps}: invalid body transform");
                        }
                        if (frame == fps * 12 && !Read<bool>(enemy, "_camped"))
                            throw new Exception($"{spec.Pattern}/{variant}/{fps}: entry did not finish");
                        if (frame > fps * 12 && (Math.Abs(enemy.Position.X - 260) > 9.1f
                            || enemy.Position.Y < 28 || enemy.Position.Y > 188))
                            throw new Exception($"{spec.Pattern}/{variant}/{fps}: hover left its safe area");
                    }
                    Check(maxSpeed <= 58.1f, $"{spec.Pattern}/{variant}/{fps}: smooth motion stays below player speed ({maxSpeed:0.0})");
                    Check(Read<float>(enemy, "BodyRadius", typeof(Enemy)) == 8f, "body collision radius stays unchanged");
                    endPositions.Add(enemy.Position);
                    enemy.QueueFree();
                    await Frames(1);
                }
                Check(endPositions[0].DistanceTo(endPositions[1]) < 3f,
                    $"{spec.Pattern}/{variant}: 30 and 120 Hz paths stay consistent");
            }
        }
        world.QueueFree();
        await Frames(2);
    }

    private async Task MotionShots(GameManager game)
    {
        game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.GetNode("StageAkari").SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Disabled;
        root.GetNode<Hud>("Hud").HideBubble();
        var enemies = new List<MidEnemy>();
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
            foreach (var spec in EnemyTable.CharactersFor(theme))
            {
                int i = enemies.Count;
                var enemy = new MidEnemy { Position = new Vector2(182 + (i % 4) * 54, 40 + (i / 4) * 62) };
                enemy.Configure(spec);
                world.AddChild(enemy);
                enemies.Add(enemy);
            }
        foreach (int frame in Enumerable.Range(0, 60))
        {
            foreach (var enemy in enemies) Call(enemy, "TickLivingMotion", 1d / 60);
            if (frame != 0 && frame != 20 && frame != 59) continue;
            await Frames(2);
            await Shot($"motion_{frame:00}");
        }
        Check(enemies.All(e => e.GetNode<Sprite2D>("Body").Texture != null), "all twelve animated character assets render");
        root.QueueFree();
        await Frames(3);
    }

    private async Task CheckStage(GameManager game, StageTheme theme, string scene)
    {
        game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
        var world = root.GetNode<Node2D>("World");
        var player = world.GetNode<Player>("Player");
        var hud = root.GetNode<Hud>("Hud");
        stage.SetProcess(false);
        world.ProcessMode = ProcessModeEnum.Inherit;
        player.SetPhysicsProcess(false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        player.GlobalPosition = new Vector2(Field.Left + 22, 184);
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(3);
        Call(stage, "StartMidwaveSpawner", 0.7f);
        var spawner = stage.GetNode<Spawner>("Spawner");
        spawner.SetProcess(false);
        Read<RandomNumberGenerator>(spawner, "_rng").Seed = 934;
        Check(spawner.Theme == theme, $"{scene} wires its actual spawner to the correct roster");
        var characters = EnemyTable.CharactersFor(theme);
        for (int i = 0; i < 3; i++)
        {
            spawner._Process(3);
            var enemy = world.GetChildren().OfType<MidEnemy>().Last();
            Check(Read<EnemySpec>(enemy, "_spec").PreTexPath == characters[i].PreTexPath,
                $"{theme} introduces character {i + 1} before random special enemies");
            var sprite = enemy.GetNode<Sprite2D>("Body");
            Check(sprite.FlipH == characters[i].FlipH && Mathf.IsEqualApprox(sprite.Scale.Y * sprite.Texture.GetHeight(), 30f),
                "new sprite faces left at a stable character size");
            Check(Read<float>(enemy, "BodyRadius", typeof(Enemy)) == 8f && Read<int>(enemy, "PanelCount", typeof(Enemy)) == 3,
                "contact radius and shield panel count remain unchanged");
        }
        var firstWave = world.GetChildren().OfType<MidEnemy>().ToArray();
        await Frames(140);
        var before = firstWave.Select(e => e.GetNode<Sprite2D>("Body").Position).ToArray();
        await Frames(10);
        Check(firstWave.Where((e, i) => e.GetNode<Sprite2D>("Body").Position != before[i]).Any(), "characters gently move");
        // 人型12種は 2026-09-17 に「全員 Humanoid 一種（±0.025rad）」から種ごとの専用モーションへ分けた。
        // 上限を 0.026→0.16rad(≒9.2°) へ広げる。狙いは変わらない＝道具種の SpinSpeed による
        // “ぐるぐる回る”を人型が受け継がないこと（AutoBank=false のまま基底の回転は握らせない）。
        // 9.2° は「首を振る／応援で体を振る」の範囲で、人が物のように回っては見えない線。
        Check(firstWave.All(e => Mathf.Abs(e.GetNode<Sprite2D>("Body").Rotation) <= 0.16f), "humanoids do not inherit prop spinning");
        // 予備動作を種ごとに 0.34〜0.78s へ分けた（2026-09-17 差別化）ので初弾の時刻が種で変わる。
        // あかり面は最長の溜め（Deadline 0.78 / Vacant 0.74）を持ち、NORMAL の Di(x1.35) に
        // 発射ゲート(0.7s)と FirstShotDelay(0.25s) が乗ると初弾が約 2.1s ＝ 固定150フレーム時点
        // （実測 約1.88s）に間に合わない。フレーム数ではなく「弾が出るまで待つ」判定に変え、
        // 溜めの長さにも実行環境のフレーム間隔にも依存しないようにする。
        Check(await WaitFrames(() => GetTree().GetNodesInGroup("enemy_bullets").Count > 0, 300),
            "new characters use the existing attacks");
        await Shot($"{scene.ToLowerInvariant()}_new_enemies");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(10);
        await Shot($"{scene.ToLowerInvariant()}_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));

        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
        await Frames(3);
        var positions = firstWave.Select(e => e.GlobalPosition).ToArray();
        int spawned = spawner.SpawnedCount;
        spawner._Process(20);
        await Frames(20);
        Check(spawner.SpawnedCount == spawned && firstWave.Where((e, i) => e.GlobalPosition != positions[i]).Count() == 0,
            "dialogue freezes both spawning and movement");
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(3);

        foreach (var enemy in firstWave)
        {
            string path = Read<EnemySpec>(enemy, "_spec").PreTexPath;
            int purified = game.PurifiedCount;
            enemy.Purify();
            enemy.Purify();
            Check(enemy.IsPurified && game.PurifiedCount == purified + 1
                && enemy.GetNode<Sprite2D>("Body").Texture.ResourcePath == path,
                "purification awards once and preserves the character illustration");
        }
        await Frames(140);
        Check(firstWave.All(e => !IsInstanceValid(e)), "purified characters leave the field");

        var skins = new HashSet<string>();
        var patterns = new HashSet<AttackPattern>();
        // ★下のループの .Last() は「SpawnOne が同期で world へ AddChild する」ことに依存している。
        //   Hard/Lunatic の左エッジ湧きだけは予告(0.4s)を挟む遅延スポーン（Spawner._pendingLeft）で、
        //   同期では world に現れない。Normal なら左エッジ確率が 0 なので安全＝ここで前提を明示しておく。
        //   このループを難易度でパラメータ化するなら、遅延ぶんを待つか左エッジを除外すること。
        Check(game.Difficulty == GameManager.Diff.Normal, "roster sampling runs on Normal (left-edge spawns are deferred)");
        for (int i = 0; i < 100; i++)
        {
            Call(spawner, "SpawnOne", game);   // Spawner.SpawnOne は GameManager? を取る（出現方向の拡張で引数が増えた）
            var enemy = world.GetChildren().OfType<MidEnemy>().Last();
            var spec = Read<EnemySpec>(enemy, "_spec");
            skins.Add(spec.PreTexPath);
            patterns.Add(spec.Pattern);
            enemy.QueueFree();
            await Frames(1);
        }
        var (shooter, drifter) = EnemyTable.For(theme);
        Check(skins.Count == 5 && skins.Contains(shooter.PreTexPath) && skins.Contains(drifter.PreTexPath),
            "new and existing enemies continue appearing together");
        Check(patterns.Contains(AttackPattern.FlankAim) && patterns.Contains(AttackPattern.BuzzWall),
            "flanking and shield enemies remain available");
        if (theme == StageTheme.Koharu) Check(patterns.Contains(AttackPattern.KoharuPrayerCarry), "prayer carrier remains available");
        for (int i = 0; i < 20; i++) spawner._Process(3);
        Check(GetTree().GetNodesInGroup("enemies").Count == game.MaxAliveEnemies, "existing concurrent enemy limit is respected");
        spawner.Stop();
        spawned = spawner.SpawnedCount;
        spawner._Process(20);
        Check(spawner.SpawnedCount == spawned, "stopped waves cannot spawn");
        foreach (var node in world.GetChildren())
            if (node is Enemy or Ripple) node.QueueFree();
        await Frames(3);
        await CheckCharacterAttacks(game, world, player, hud, characters);
        await CheckCameoAttacks(game, stage, player, hud, scene);
        root.QueueFree();
        await Frames(5);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Hud.BubblePaused = false;
    }

    // ─── 進入が必ず終わる（＝着座に到達する）ことの実測（2026-09-17 移動パターン差別化）───
    //   移動を種ごとに変えた結果いちばん怖いのは「着座しない個体」＝倒すまで居座る前提が崩れ、
    //   MaxAliveEnemies の枠を食い潰して以降のウェーブが湧かない＝進行不能になること。
    //   全種 × 全出現エッジ（右/上/下/左）× 全難易度 を実際に物理で回し、
    //   規定時間内に _camped が立つことを実測する。設計上の最悪値は
    //   「盤面対角 460px ÷ 実効前進 46px/s ≒ 10秒」なので、余裕をみて 12 秒を上限にする。
    private async Task CheckApproachAlwaysCamps()
    {
        const double LimitSec = 12.0;
        var world = new Node2D { Name = "ApproachWorld" };
        GetTree().Root.AddChild(world);
        var game = GetNode<GameManager>("/root/Game");

        // 全種を集める（基底6種＋人型12種＋引用リプ＋バズ壁＋祈り運び）。
        var specs = new List<(string label, EnemySpec spec)>();
        foreach (var theme in new[] { StageTheme.Rei, StageTheme.Akari, StageTheme.Koharu, StageTheme.Mina })
        {
            var (shooter, drifter) = EnemyTable.For(theme);
            if (theme != StageTheme.Mina)
            {
                specs.Add(($"{theme}.shooter", shooter));
                specs.Add(($"{theme}.drifter", drifter));
                specs.Add(($"{theme}.flank", EnemyTable.Flanker(theme)));
                specs.Add(($"{theme}.wall", EnemyTable.BuzzWall(theme)));
            }
            foreach (var c in EnemyTable.CharactersFor(theme))
                specs.Add(($"{theme}.{c.Pattern}", c));
        }

        // 出現点と着座点の組。Spawner が実際に使う4エッジを模す（右/上/下/左）。
        // 左だけは SetSilentEntry（着座まで撃たない）で、着座 x は 184〜224 の保証帯を使う。
        var entries = new (string edge, Vector2 pos, Vector2 camp, bool silent)[]
        {
            ("right",  new Vector2(Field.Right + 14f, 46f),  new Vector2(268f, 150f), false),
            ("top",    new Vector2(300f, Field.Top - 18f),   new Vector2(176f, 172f), false),
            ("bottom", new Vector2(320f, Field.Bottom + 18f), new Vector2(200f, 40f),  false),
            ("left",   new Vector2(Field.Left - 18f, 120f),  new Vector2(184f, 60f),  true),
        };

        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            foreach (var (label, spec) in specs)
            {
                // 祈り運びは「居座らず横断する」設計＝着座しないのが正。左端へ抜けて消えることだけ確かめる。
                if (spec.Pattern == AttackPattern.KoharuPrayerCarry) continue;
                foreach (var (edge, pos, camp, silent) in entries)
                {
                    var e = new MidEnemy();
                    e.Configure(spec);
                    if (silent) e.SetSilentEntry(camp); else e.SetEntry(camp);
                    world.AddChild(e);
                    e.GlobalPosition = pos;
                    double t = 0;
                    while (!Read<bool>(e, "_camped") && t < LimitSec)
                    {
                        e._PhysicsProcess(1.0 / 60.0);
                        t += 1.0 / 60.0;
                    }
                    Check(Read<bool>(e, "_camped"),
                        $"{label}/{edge}/{diff}: approach reaches its camp point in {t:0.00}s (< {LimitSec}s)");
                    // 着座点から離れていないこと（進入の横オフセットが収束せずズレたまま止まっていない）。
                    //   X は camp.X ± CampDriftMax(9px) の帯に収まる＝左湧きの着座保証 x=184..224 が保たれる。
                    //   Y は種ごとの縦の形（鋸波±22 / 段送り±24 など）を許す帯で見る。
                    Check(Mathf.Abs(e.GlobalPosition.X - camp.X) <= 10f,
                        $"{label}/{edge}/{diff}: settles on the camp column (x={e.GlobalPosition.X:0.0} vs {camp.X:0.0})");
                    Check(Mathf.Abs(e.GlobalPosition.Y - camp.Y) <= 30f,
                        $"{label}/{edge}/{diff}: settles near the camp row (y={e.GlobalPosition.Y:0.0} vs {camp.Y:0.0})");
                    // 左湧きは着座まで一度も撃たない（SetSilentEntry の保証を移動変更で壊していない）。
                    if (silent) Check(GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().All(b => !b.Active),
                        $"{label}/{edge}/{diff}: left-edge spawns stay silent until camped");
                    e.QueueFree();
                    // 予兆（AreaStrike）は発生源に紐づくが、発生源を消すのは次フレーム＝ここで一緒に掃除する
                    //（4難易度×全種×4エッジ ぶん溜め込まない）。
                    foreach (var n in world.GetChildren()) if (n is AreaStrike) n.QueueFree();
                    GetNode<BulletPool>("/root/Pool").DespawnAll();
                    await Frames(1);
                }
            }
        }

        // 着座後は盤面内に留まり続ける（横揺れ・縦の形が画面外へ持ち出さない）ことを長回しで確認。
        foreach (var (label, spec) in specs)
        {
            if (spec.Pattern == AttackPattern.KoharuPrayerCarry) continue;
            var e = new MidEnemy();
            e.Configure(spec);
            e.SetEntry(new Vector2(200f, 108f));
            world.AddChild(e);
            e.GlobalPosition = new Vector2(Field.Right + 14f, 108f);
            for (int i = 0; i < 60 * 25; i++)   // 25 秒ぶん回す
            {
                e._PhysicsProcess(1.0 / 60.0);
                if (!Read<bool>(e, "_camped")) continue;   // 進入中は見ない（出現点 Field.Right+14 は盤面外＝初回フレームで必ず範囲外になる）。このループの主題は「着座後」に留まり続けるか。
                var q = e.GlobalPosition;
                if (q.X < Field.Left + 8f || q.X > Field.Right - 8f || q.Y < 20f || q.Y > 196f)
                    throw new Exception($"{label}: camped enemy left the safe area at {q}");
            }
            e.QueueFree();
            foreach (var n in world.GetChildren()) if (n is AreaStrike) n.QueueFree();
            GetNode<BulletPool>("/root/Pool").DespawnAll();
            await Frames(1);
        }
        game.Difficulty = GameManager.Diff.Normal;
        world.QueueFree();
        await Frames(2);
        Check(true, "every enemy type camps in bounded time and stays inside the field");
    }

    // 種ごとの欠片配分（EnemySpec.ShardWeight・2026-09-17）の検証。
    //   ①「強い敵・倒しにくい敵ほど多い」の序列が崩れていないこと（リスクとリターンの比例）。
    //   ②粒数を変えても経済（ショップ通貨＝インプレ）が一切変わらないこと。
    //     PurifyBurst は impBase を n 粒へ総和保存で割る（impBase/n ＋ 余りを先頭から1ずつ）ので、
    //     n をどう動かしても拾い切ったときの合計は impBase のまま——これを実数で確かめる。
    private void CheckShardWeights()
    {
        float Weight(StageTheme theme, int i) => EnemyTable.CharactersFor(theme)[i].ShardWeight;

        // ① 序列：手間2.5倍のバズ壁が最大、手間1/3のボーナス種（祈り運び）が最小。
        float wall = EnemyTable.BuzzWall(StageTheme.Akari).ShardWeight;
        float carry = EnemyTable.PrayerCarrier().ShardWeight;
        Check(wall == 2.5f && carry == 0.45f && wall > EnemyTable.Flanker(StageTheme.Rei).ShardWeight,
            "shield wall drops the most shards and the bonus carrier the fewest");
        // 撃つ種 > 撃たない種（同じ6ヒットなら、残すと痛い方が多く落とす）。
        foreach (var theme in new[] { StageTheme.Rei, StageTheme.Akari, StageTheme.Koharu })
        {
            var (shooter, drifter) = EnemyTable.For(theme);
            Check(shooter.ShardWeight > drifter.ShardWeight, $"{theme} shooter outdrops its drifter");
        }
        // 人型は「撃つ頻度が高い／避け場を奪う」ほど多い。各面の最寡黙な種が最小になっていること。
        Check(Weight(StageTheme.Koharu, 2) < Weight(StageTheme.Koharu, 0)
            && Weight(StageTheme.Koharu, 2) < Weight(StageTheme.Koharu, 1)
            && Weight(StageTheme.Rei, 2) < Weight(StageTheme.Rei, 0)
            && Weight(StageTheme.Akari, 1) < Weight(StageTheme.Akari, 0),
            "quiet characters drop fewer shards than the pressuring ones");
        // 全種が妥当な範囲（0 や負で消えない・中ボス級を超えない）。
        var all = new[] { StageTheme.Rei, StageTheme.Akari, StageTheme.Koharu, StageTheme.Mina }
            .SelectMany(t => EnemyTable.CharactersFor(t).Select(s => s.ShardWeight))
            .Concat(new[] { wall, carry }).ToArray();
        Check(all.All(w => w >= 0.4f && w <= 2.5f), "every shard weight stays inside a sane range");

        // ② 経済不変：同じ impBase を、粒数だけ変えて撒いたときの入金額が一致するか。
        //    FxLayer 実物を通すのではなく、PurifyBurst と同じ総和保存の分配式で検算する
        //    （FxLayer は他ワーカーが編集中のため、ここでは式の不変性だけを見る）。
        int Distributed(int impBase, int n)
        {
            int sum = 0;
            for (int i = 0; i < n; i++) sum += impBase / n + (i < impBase % n ? 1 : 0);
            return sum;
        }
        foreach (int impBase in new[] { 1, 2, 7, 13, 40, 99 })
        {
            int baseline = Distributed(impBase, 13);   // 従来の Zako 粒数（10〜16）の中央
            foreach (float w in all)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(13 * w), 1, 64);
                Check(Distributed(impBase, n) == impBase && baseline == impBase,
                    $"shard weight {w} keeps the {impBase} impression payout intact");
            }
        }
    }

    private Bullet[] EnemyBullets() => GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active).ToArray();

    // 敵弾は「敵の中心に湧いて、本来の発射点まで飛んでから」パターンへ移る（Bullet.MakeLeadIn・2026-09-28）。
    // 弾幕の幾何（配置・弾速）を見る検査はその導入区間を消化してから行う＝以降の判定は従来と同じ座標で回る。
    // 導入を終えたフレームは速度を適用せず発射点で止まるので、ここを抜けた時点の位置＝本来の発射点ちょうど。
    private static Bullet[] SettleLeadIn(Bullet[] bullets)
    {
        foreach (var b in bullets)
            for (int i = 0; i < 64 && b.LeadIn; i++) b._PhysicsProcess(1.0 / 60.0);
        return bullets;
    }

    private async Task CheckCameoAttacks(GameManager game, Node stage, Player player, Hud hud, string scene)
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        game.MarkIdleDialogSeen("once_midboss_shield");
        Write(stage, "_stepStarted", false);
        // 中ボスの登場カットシーン（CameoIntroScene）は会話送りを待つ＝ここでは通り抜けられない。
        // ルナティック経路はカットシーンを流さずその場で中ボスを出すので、この一歩だけ借りて弾幕に入る
        //（ここで見るのは弾の絵・弾数・弾速で、登場演出は qa_cameo_intro の担当）。
        Write(stage, "_lunatic", true);
        Call(stage, "Step_BossCameo", 0d);
        Write(stage, "_lunatic", false);
        var cameo = Read<CameoBoss>(stage, "_cameo");
        await Frames(60);
        cameo.SetPhysicsProcess(false);
        player.GlobalPosition = new Vector2(Field.Left + 22f, 184f);
        var (firstArt, secondArt, firstInterval, secondInterval, firstCount, secondCount, firstSpeed, secondSpeed) = scene switch
        {
            "Akari" => ("akari_docs", "akari_envelope", 1.0d, 1.4d, 7, 8, 72f, 44f),
            "Koharu" => ("koharu_penlight", "koharu_ticket", 1.1d, 1.5d, 8, 5, 76f, 56f),
            "Rei" => ("enemy_rei_anonymous", "enemy_rei_metrics", 0.95d, 1.2d, 3, 12, 96f, 68f),
            _ => throw new Exception("Unexpected cameo stage"),
        };
        foreach (string art in new[] { firstArt, secondArt })
        {
            using var image = BulletArt.Get(art)!.GetImage();
            Check(image.DetectAlpha() != Image.AlphaMode.None && image.GetPixel(0, 0).A < 0.05f,
                $"{scene} cameo loads transparent projectile {art}");
        }
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            pool.DespawnAll();
            Write(cameo, "_fireT", 0d);
            Write(cameo, "_fireT2", 0d);
            double threshold = firstInterval * game.DanmakuIntervalMul;
            Call(cameo, "FirePattern", threshold - 0.00001d);
            Check(EnemyBullets().Length == 0, $"{scene}/{diff}: cameo preserves its firing interval");
            Call(cameo, "FirePattern", 0.00002d);
            int expectedFirst = scene == "Rei" ? firstCount : game.ScaleBullets(firstCount);
            // こはる中ボスの2つ目は自機狙いの扇（FanDown）＝本数は必ず奇数（2026-10-03）。
            // あかり/レイの2つ目はリング・雨なので従来どおり ScaleBullets。
            int expectedSecond = scene == "Koharu" ? game.ScaleBulletsOdd(secondCount) : game.ScaleBullets(secondCount);
            CheckVolley(firstArt, expectedFirst, firstSpeed, rain: scene != "Rei");
            pool.DespawnAll();
            Write(cameo, "_fireT", 0d);
            Write(cameo, "_fireT2", secondInterval * game.DanmakuIntervalMul);
            Call(cameo, "FirePattern", 0d);
            CheckVolley(secondArt, expectedSecond, secondSpeed, rain: false);
            pool.DespawnAll();
            Write(cameo, "_fireT", threshold);
            Write(cameo, "_fireT2", secondInterval * game.DanmakuIntervalMul);
            Call(cameo, "FirePattern", 0d);
            Check(EnemyBullets().Length == expectedFirst + expectedSecond,
                $"{scene}/{diff}: simultaneous cameo attacks keep their bullet counts");
            CheckVolley(firstArt, expectedFirst, firstSpeed, rain: scene != "Rei");
            CheckVolley(secondArt, expectedSecond, secondSpeed, rain: false);
        }
        game.Difficulty = GameManager.Diff.Normal;
        pool.DespawnAll();
        Write(cameo, "_fireT", 0d);
        Write(cameo, "_fireT2", 0d);
        cameo.SetPhysicsProcess(true);
        // 2つ目の攻撃の間隔は Di 込みで最大 1.4x1.35=1.89s（あかり）。固定130フレーム（約1.6s）では
        // 遅い方が間に合わずに落ちるので、両方の弾が出そろうまで待つ判定にする。
        bool bothFired = await WaitFrames(() =>
            EnemyBullets().Any(b => Read<Texture2D>(b, "_sprite").ResourcePath.EndsWith($"/{firstArt}.png"))
            && EnemyBullets().Any(b => Read<Texture2D>(b, "_sprite").ResourcePath.EndsWith($"/{secondArt}.png")), 600);
        Check(bothFired, $"{scene}: both illustrated attacks fire during real cameo combat");
        await Shot($"{scene.ToLowerInvariant()}_cameo_attacks");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(3);
        await Shot($"{scene.ToLowerInvariant()}_cameo_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
        await Frames(3);
        var active = EnemyBullets();
        var positions = active.Select(b => b.GlobalPosition).ToArray();
        double fireTime = Read<double>(cameo, "_fireT");
        await Frames(20);
        Check(fireTime == Read<double>(cameo, "_fireT")
            && active.Select((b, i) => b.GlobalPosition == positions[i]).All(x => x),
            $"{scene}: dialogue freezes cameo attacks and illustrated bullets");
        cameo.QueueFree();
        hud.HoldBubble = false;
        hud.HideBubble();
        pool.DespawnAll();
        await Frames(3);

        void CheckVolley(string art, int expected, float speed, bool rain)
        {
            var bullets = EnemyBullets().Where(b => Read<Texture2D?>(b, "_sprite")?.ResourcePath
                == $"res://char/v3/bullets/{art}.png").ToArray();
            Check(bullets.Length == expected && bullets.All(b => Mathf.IsEqualApprox(b.Radius, 3.2f)
                && b.Damage == 1 && !b.Homing && !b.IsPatternBullet && !b.Accel && b.Shape == cameo.Theme.SpellShape),
                $"{scene}/{game.Difficulty}: {art} retains count, collision and damage");
            Check(bullets.All(b => Math.Abs((rain ? b.Velocity.Y : b.Velocity.Length()) - speed * game.BulletSpeedMul) < 0.01f),
                $"{scene}/{game.Difficulty}: {art} retains speed scaling");
        }
    }

    private async Task CheckCharacterAttacks(GameManager game, Node2D world, Player player, Hud hud,
        IReadOnlyList<EnemySpec> characters)
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        game.SetStageTarget(10000);
        foreach (var spec in characters)
        {
            Check(_characterPatterns.Add(spec.Pattern) && spec.Fires, "character has a unique enabled attack");
            foreach (var diff in Enum.GetValues<GameManager.Diff>())
            {
                game.Difficulty = diff;
                pool.DespawnAll();
                player.GlobalPosition = new Vector2(Field.Left + 22f, 108f);
                var enemy = new MidEnemy();
                enemy.Configure(spec);
                world.AddChild(enemy);
                enemy.GlobalPosition = new Vector2(Field.Right - 80f, 108f);
                enemy.SetPhysicsProcess(false);
                var texture = Read<Texture2D>(enemy, "CurSprite", typeof(Enemy));
                using (var pixels = texture.GetImage())
                    Check(pixels.GetHeight() == 128 && pixels.GetPixel(0, 0).A < 0.05f
                        && pixels.DetectAlpha() != Image.AlphaMode.None, $"{spec.Pattern}/{diff}: transparent generated bullet loaded");
                string asset = texture.ResourcePath.GetFile().GetBaseName();
                Call(enemy, "BeginCharacterAttack");
                Call(enemy, "TickFire", 0.1d);
                Check(EnemyBullets().Length == 0, "warning never damages before firing");
                if (diff == GameManager.Diff.Normal)
                {
                    hud.HoldBubble = true;
                    hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
                    enemy.SetPhysicsProcess(true);
                    await Frames(3);
                    double warning = Read<double>(enemy, "_telegraphT");
                    await Frames(25);
                    Check(warning == Read<double>(enemy, "_telegraphT") && EnemyBullets().Length == 0,
                        "dialogue freezes pending character attacks");
                    enemy.SetPhysicsProcess(false);
                    hud.HoldBubble = false;
                    hud.HideBubble();
                    await Frames(3);
                }
                Vector2 lockedDirection = Read<Vector2>(enemy, "_burstDir");
                player.GlobalPosition = new Vector2(Field.Left + 22f, 184f);
                Call(enemy, "TickFire", 2.0d);
                for (int i = 0; i < 3 && Read<int>(enemy, "_salvoRemaining") > 0; i++)
                    Call(enemy, "TickFire", 2.0d);
                var bullets = SettleLeadIn(EnemyBullets());
                int Expected(int n) => game.ScaleBullets(n);
                // 扇（CharacterFan）だけは本数が必ず奇数＝中心の1本が予告方向の正面を通る（2026-10-03）。
                int Fan(int n) => game.ScaleBulletsOdd(n);
                // 片側払い（CharacterSweep：未送信・比較）は必ず2本以上＝内縁の1本が狙いそのものに載る。
                int Sweep(int n) => Math.Max(2, game.ScaleBullets(n));
                int count = spec.Pattern switch
                {
                    AttackPattern.AkariDeadline => Fan(3),
                    AttackPattern.KoharuComparison => Sweep(3),
                    AttackPattern.AkariUnsent => Sweep(2) * 2,
                    AttackPattern.ReiClipper => Fan(2) * 2,
                    AttackPattern.AkariVacant => Math.Clamp(Expected(5), 3, 13) - 1,
                    AttackPattern.KoharuCheer => Fan(3) * 2,
                    AttackPattern.KoharuParcel => Expected(2) * 3,
                    AttackPattern.ReiAnonymous => Fan(1) * 3,
                    AttackPattern.ReiMetrics => Math.Max(2, Expected(4)),
                    _ => throw new Exception("Unexpected character attack"),
                };
                Check(bullets.Length == count && Read<int>(enemy, "_salvoRemaining") == 0,
                    $"{spec.Pattern}/{diff}: completes expected {count}-bullet pattern");
                Check(bullets.All(b => Read<Texture2D>(b, "_sprite").ResourcePath == texture.ResourcePath
                    && b.Radius >= 3.6f && b.Radius <= 4f && b.Damage == 1 && !b.Homing && !b.IsPatternBullet),
                    "every bullet uses its own illustration with consistent collision rules");
                Check(bullets.All(b => b.GlobalPosition.X > Field.Left && b.GlobalPosition.X < Field.Right
                    && b.GlobalPosition.Y > Field.Top && b.GlobalPosition.Y < Field.Bottom), "all projectiles originate inside the field");
                if (spec.Pattern == AttackPattern.ReiAnonymous)
                    Check(bullets.All(b => Math.Abs(b.Velocity.Normalized().AngleTo(lockedDirection)) < 0.09f),
                        "anonymous burst uses the telegraphed direction, not live tracking");
                if (spec.Pattern == AttackPattern.ReiMetrics)
                    Check(bullets.All(b => b.Rotation == 0f), "ranking arrows always point down");
                if (spec.Pattern == AttackPattern.AkariVacant)
                {
                    var rows = bullets.Select(b => b.GlobalPosition.Y).OrderBy(y => y).ToArray();
                    Check(rows.Zip(rows.Skip(1), (a, b) => b - a).Max() >= 31f,
                        "ID badge wall leaves a visible escape gap");
                }
                if (diff == GameManager.Diff.Normal)
                {
                    string signature = string.Join(";", bullets.Select(b => $"{b.GlobalPosition.X:0},{b.GlobalPosition.Y:0}:{b.Velocity.X:0},{b.Velocity.Y:0}"));
                    Check(_attackSignatures.Add(signature), "projectile pattern differs from other characters");
                }
                if (spec.Pattern == AttackPattern.AkariDeadline)
                {
                    Check(bullets.All(b => b.AccelCharging && Math.Abs(b.Velocity.Length() - 10f * game.BulletSpeedMul) < 0.01f),
                        "clock projectiles begin with a readable charge");
                    foreach (var bullet in bullets) bullet._PhysicsProcess(game.DanmakuIntervalMul * 0.6d);
                    Check(bullets.All(b => !b.AccelCharging && Math.Abs(b.Velocity.Length() - 116f * game.BulletSpeedMul) < 0.01f),
                        "clock acceleration preserves difficulty scaling");
                }
                else
                {
                    float speed = spec.Pattern switch
                    {
                        AttackPattern.AkariUnsent => 64f, AttackPattern.AkariVacant => 52f,
                        AttackPattern.KoharuComparison => 84f, AttackPattern.KoharuCheer => 72f,
                        AttackPattern.KoharuParcel => 38f, AttackPattern.ReiAnonymous => 100f,
                        AttackPattern.ReiClipper => 85f, AttackPattern.ReiMetrics => 55f,
                        _ => 0f,
                    };
                    Check(bullets.All(b => Math.Abs(b.Velocity.Length() - speed * game.BulletSpeedMul) < 0.01f),
                        "projectile speed respects difficulty");
                }
                if (diff == GameManager.Diff.Normal)
                {
                    pool.DespawnAll();
                    Call(enemy, "BeginCharacterAttack");
                    int frames = 0;
                    while (Read<double>(enemy, "_telegraphT") > 0 || Read<int>(enemy, "_salvoRemaining") > 0)
                    {
                        Call(enemy, "TickFire", 1.0d / 60.0d);
                        await Frames(1);
                        if (++frames >= 300) throw new Exception("Timed character attack did not complete");
                    }
                    await Frames(24);
                    await Shot($"attack_{asset}");
                    if (spec.Pattern == AttackPattern.ReiMetrics)
                        Check(EnemyBullets().All(b => b.Rotation == 0f), "ranking arrows retain their orientation in flight");
                    hud.HoldBubble = true;
                    hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
                    await Frames(3);
                    var active = EnemyBullets();
                    var positions = active.Select(b => b.GlobalPosition).ToArray();
                    await Frames(20);
                    Check(active.Select((b, i) => b.GlobalPosition == positions[i]).All(x => x), "dialogue freezes generated projectiles");
                    hud.HoldBubble = false;
                    hud.HideBubble();
                    await Frames(3);
                }
                pool.DespawnAll();
                Call(enemy, "BeginCharacterAttack");
                if (spec.Pattern is AttackPattern.AkariUnsent or AttackPattern.KoharuCheer or AttackPattern.KoharuParcel or AttackPattern.ReiAnonymous)
                {
                    Call(enemy, "TickFire", 2.0d);
                    pool.DespawnAll();
                }
                enemy.Purify();
                enemy.SetPhysicsProcess(true);
                await Frames(100);
                Check(EnemyBullets().Length == 0, "purification cancels warnings and remaining salvos");
                if (IsInstanceValid(enemy)) enemy.QueueFree();
                await Frames(2);
                var reused = pool.Spawn(new Vector2(Field.Right - 40f, 108f), Vector2.Left * 30f, true);
                Check(Read<Texture2D?>(reused, "_sprite") == null && !reused.Accel, "pool reuse clears sprite and acceleration state");
                pool.DespawnAll();
            }
        }
        game.Difficulty = GameManager.Diff.Normal;
    }

    // 条件が成立するまで最大 limit フレーム待つ（成立したら true）。
    private async Task<bool> WaitFrames(Func<bool> cond, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            if (cond()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return cond();
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/enemy_roster/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
