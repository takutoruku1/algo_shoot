using Godot;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class ScoreShardQa : Node
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Fields)!.GetValue(obj)!;
    private static IList Shards(ScoreShards drops) => Read<IList>(drops, "_shards");
    private static int Value(ScoreShards drops) => Shards(drops).Cast<object>().Sum(s => Read<int>(s, "Points"));
    // 欠片が運ぶショップ通貨の基礎額の総和（2026-09-17 経済改修）。スコア(Points)とは別系統。
    private static int ImpValue(ScoreShards drops) => Shards(drops).Cast<object>().Sum(s => Read<int>(s, "Imp"));
    // 基礎額 impBase が GainImpression の全倍率を通ったあとの実加算額（期待値計算用）。
    private static long Expected(GameManager game, int impBase) =>
        impBase <= 0 ? 0 : (long)Mathf.Round(impBase * game.TotalImpressionMul * game.ReplayMul * GameManager.MoneyGainMul);
    // 浄化1体が欠片に積む基礎額（コンボ加算後の値に取りこぼし補正 ShardImpressionMul を掛けたもの）。
    private static int PurifyImp(int comboAfter) =>
        Mathf.Max(1, Mathf.RoundToInt((2 + comboAfter) * GameManager.ShardImpressionMul));
    private static FxLayer.P[] Particles(FxLayer fx) => Read<IList>(fx, "_p").Cast<FxLayer.P>().ToArray();
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[ScoreShardQA] PASS {message}");
    }
    private static void Tick(ScoreShards drops, int frames)
    {
        for (int i = 0; i < frames; i++) drops._PhysicsProcess(1.0 / 60);
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--movie"))
            {
                await Movie(game);
                GetTree().Quit();
                return;
            }
            // --burst : 散り方と回収演出を目視するための連続スクショ（2026-09-22）。
            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--burst"))
            {
                await BurstShots(game);
                GetTree().Quit();
                return;
            }
            foreach (var job in Jobs.All)
            {
                game.SelectedJob = job.Id;
                var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                root.Stage.SetProcess(false);
                root.World.ProcessMode = ProcessModeEnum.Inherit;
                root.Player.SetPhysicsProcess(false);
                root.Player.GlobalPosition = new Vector2(Field.Left + 28, 150);
                root.Hud.HoldBubble = false;
                root.Hud.HideBubble();
                game.SetStageTarget(999);
                var fx = root.World.GetNode<FxLayer>("FxLayer");
                var drops = fx.ScoreDrops;
                drops.SetPhysicsProcess(false);
                await Frames(10);

                Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
                fx.PurifyBurst(new Vector2(302, 100));
                var original = Particles(fx).Where(p => p.Type is FxLayer.T.Petal or FxLayer.T.HeartP).ToArray();
                Check(Shards(drops).Count == 0, "non-defeat effects stay decorative and cannot award points");
                Read<IList>(fx, "_p").Clear();
                Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
                fx.PurifyBurst(new Vector2(302, 100), 80);
                var retained = Shards(drops).Cast<object>().Select(s => Read<FxLayer.P>(s, "Particle")).ToArray();
                Check(original.Length == retained.Length && original.Zip(retained, (a, b) =>
                    a.Type == b.Type && a.Col == b.Col && a.Size == b.Size && a.Rot == b.Rot && a.Spin == b.Spin
                    && a.Vx == b.Vx && a.Vy == b.Vy && a.Grav == b.Grav && a.Drag == b.Drag).All(same => same),
                    "pickups retain the original diamonds and hearts, palette, size, rotation and scatter velocity");
                Check(!Particles(fx).Any(p => p.Type is FxLayer.T.Petal or FxLayer.T.HeartP),
                    "defeat particles are transferred, never duplicated by another pickup burst");
                Check(retained.Any(p => p.Type == FxLayer.T.Petal) && retained.Any(p => p.Type == FxLayer.T.HeartP),
                    "both original particle shapes remain collectible");
                Shards(drops).Clear();
                Read<IList>(fx, "_p").Clear();

                var enemy = new PageShard { Position = new Vector2(302, 100) };
                root.World.AddChild(enemy);
                enemy.SetPhysicsProcess(false);
                var panels = enemy.GetChildren().OfType<Panel>().ToArray();
                long defeatScore = game.Score + 80 + panels.Length * 5;
                long impressionAtDefeat = game.Impression;
                foreach (var panel in panels) panel.Shatter();
                int dropCount = Shards(drops).Count;
                Check(enemy.IsPurified && dropCount >= 10 && dropCount <= 16,
                    $"{job.CharacterId}: actual enemy defeat keeps its original particle count");
                Check(game.Score == defeatScore && game.Combo == 1 && game.PurifiedCount == 1,
                    "original defeat score, combo and stage progress are unchanged");
                int bonus = Value(drops);
                Check(bonus == 8, "shards carry a small separate pickup bonus");
                // ★経済改修：撃破の瞬間にはインプレが1も入らない（通貨は拾って初めて入る）。
                long afterDefeat = game.Impression;
                Check(afterDefeat == impressionAtDefeat, "defeating an enemy alone never awards shop currency");
                int impBonus = ImpValue(drops);
                Check(impBonus == PurifyImp(1), $"shards carry the whole purify impression base ({impBonus})");
                enemy.Purify();
                Check(Shards(drops).Count == dropCount && game.Score == defeatScore, "repeat defeat cannot duplicate rewards");
                Tick(drops, 30);
                Check(Shards(drops).Count == dropCount && game.Score == defeatScore
                    && Shards(drops).Cast<object>().All(s => !Read<bool>(s, "Attracted")), "distant shards scatter without collecting");
                var positions = Shards(drops).Cast<object>().Select(s => Read<Vector2>(s, "Position")).ToArray();
                Check(positions.Distinct().Count() == dropCount && positions.Max(p => p.DistanceTo(enemy.Position)) > 12,
                    "visible outward burst has distinct trajectories");
                await Shot(job.CharacterId + "_scatter");

                root.Hud.HoldBubble = true;
                root.Hud.ShowMessage("QA");
                float age = Read<float>(Shards(drops)[0]!, "Age");
                Tick(drops, 360);
                Check(!drops.Visible && Shards(drops).Count == dropCount && Read<float>(Shards(drops)[0]!, "Age") == age,
                    "dialogue hides and freezes shards without spending their lifetime");
                root.Hud.HoldBubble = false;
                root.Hud.HideBubble();
                drops.SetPhysicsProcess(true);
                GetTree().Paused = true;
                await Frames(20);
                Check(Read<float>(Shards(drops)[0]!, "Age") == age, "pause menu freezes pickups");
                GetTree().Paused = false;
                drops.SetPhysicsProcess(false);

                long money = game.Impression;
                root.Player.GlobalPosition = new Vector2(302, 100);
                Tick(drops, 2);
                Check(Shards(drops).Cast<object>().All(s => Read<bool>(s, "Attracted")), "entering the radius starts attraction");
                root.Player.GlobalPosition = new Vector2(205, 160);
                await Shot(job.CharacterId + "_attract");
                Tick(drops, 150);
                Check(Shards(drops).Count == 0 && game.Score == defeatScore + bonus,
                    "attracted shards follow the moving player and award their points exactly once");
                // 拾った瞬間にショップ通貨が増える（説明文「浄化した心＝通貨」と実装が一致する）。
                // 小口はしきい値まで貯めてから1回で通すので、締めてから総額を比べる。
                game.FlushShardImpression();
                Check(game.Impression == money + Expected(game, impBonus),
                    $"collecting shards is what actually pays the shop currency (+{game.Impression - money})");
                Check(game.Combo == 1 && game.PurifiedCount == 1,
                    "collecting cannot farm combo or stage progress");
                money = game.Impression;
                Check(!Particles(fx).Any(p => p.Type == FxLayer.T.Dmg && p.Text.StartsWith("+")),
                    "pickup rewards update the HUD without floating plus-point labels");
                Tick(drops, 90);
                Check(game.Score == defeatScore + bonus && game.Impression == money,
                    "collected shards never score or pay twice");
                await Shot(job.CharacterId + "_collected");

                root.Player.GlobalPosition = new Vector2(Field.Left + 10, 190);
                fx.PurifyBurst(new Vector2(340, 65), 100);
                Tick(drops, 330);
                // 取りこぼしはスコアも通貨も入らない＝「拾いに行く」ことが賭けになる（リスクとリターン）。
                // ボス/中ボスは BeginRush、クリアは sweep で全回収が保証されるので、これは道中だけの賭け。
                game.FlushShardImpression();
                Check(Shards(drops).Count == 0 && game.Score == defeatScore + bonus && game.Impression == money,
                    "uncollected shards expire without awarding points or shop currency");
                fx.PurifyBurst(new Vector2(Field.Right + 30, -20), 100);
                Tick(drops, 1);
                Check(Shards(drops).Cast<object>().All(s => Field.Rect.HasPoint(Read<Vector2>(s, "Position"))),
                    "edge drops stay inside the playable field");
                Shards(drops).Clear();

                game.ResetRun();
                game.SetStageTarget(999);
                for (int i = 0; i < 4; i++)
                {
                    int previousCount = Shards(drops).Count;
                    var bombEnemy = new PageShard { Position = new Vector2(320, 80) };
                    root.World.AddChild(bombEnemy);
                    bombEnemy.SetPhysicsProcess(false);
                    bombEnemy.Purify();
                    if (i == 3) Check(Shards(drops).Count == previousCount, "bomb-capped effects remain non-collectible");
                }
                Check(Value(drops) == 24 && game.PurifiedCount == 4 && game.Combo == 3,
                    "bomb reward cap also caps shard drops without blocking stage progress");
                // キャップ超過の4体目は通貨も積まない＝ボム撃ちの無限ファームは通貨側でも塞がれている。
                Check(ImpValue(drops) == PurifyImp(1) + PurifyImp(2) + PurifyImp(3),
                    "bomb reward cap also caps the shop currency the shards carry");
                Shards(drops).Clear();

                // ── ボス/中ボス撃破の欠片量（2026-09-17 の爽快感強化）──
                // 粒の数を格で増やす。スコア総量は basePoints/10 に難易度倍率だけを掛ける。
                // 以下の期待値は Normal（倍率1.0）基準なので、難易度を明示的に固定してから測る。
                game.Difficulty = GameManager.Diff.Normal;
                Shards(drops).Clear();
                Read<IList>(fx, "_p").Clear();
                fx.PurifyBurst(new Vector2(350, 70), 700, FxLayer.PurifyTier.MidBoss);
                int midCount = Shards(drops).Count;
                Check(midCount >= 52 && midCount <= 64, $"mid-boss defeat scatters a mid-tier shard count ({midCount})");
                Check(Value(drops) == 70, "mid-boss shard total matches basePoints/10 exactly (economy unchanged)");
                Check(Read<float>(drops, "_rush") > 0, "mid-boss defeat forces the collection rush");
                Shards(drops).Clear();
                Read<IList>(fx, "_p").Clear();
                drops.GetType().GetField("_rush", Fields)!.SetValue(drops, 0f);

                fx.PurifyBurst(new Vector2(350, 70), 1500, true);
                int bossCount = Shards(drops).Count;
                Check(bossCount >= 104 && bossCount <= 128, $"boss defeat scatters the largest shard count ({bossCount})");
                Check(bossCount > midCount * 1.6f, "boss defeat is visibly denser than a mid-boss defeat");
                Check(Value(drops) == 150, "boss defeat preserves its larger pickup reward without adding particles");

                // ── 全方位に散る（2026-09-22 ユーザー要望「もっと全体にちって」）──
                //   ボス級だけ spread=π＝左（自機のいる側）にも散る。ザコは右上の扇のまま
                //   ＝倒した敵の残弾に破片をかぶせない視認性対策（2026-09-08）を維持する。
                Vector2[] Velocities() => Shards(drops).Cast<object>()
                    .Select(s => Read<FxLayer.P>(s, "Particle")).Select(p => new Vector2(p.Vx, p.Vy)).ToArray();
                var bossVel = Velocities();
                Check(bossVel.Any(v => v.X < -20f) && bossVel.Any(v => v.X > 20f)
                    && bossVel.Any(v => v.Y < -20f) && bossVel.Any(v => v.Y > 20f),
                    "boss shards scatter in every direction, left and down included");
                // 慣性で飛ぶ時間を伸ばしてあるか（ScatterTime 0.24 のままだと初速を上げても届かない）。
                Check(Shards(drops).Cast<object>().All(s => Read<float>(s, "Fly") > 0.24f),
                    "boss shards keep their momentum long enough to reach the field edges");
                Shards(drops).Clear();
                Read<IList>(fx, "_p").Clear();
                drops.GetType().GetField("_rush", Fields)!.SetValue(drops, 0f);
                fx.PurifyBurst(new Vector2(350, 70), 100, FxLayer.PurifyTier.Zako, 40);
                var zakoVel = Velocities();
                Check(zakoVel.All(v => v.X > -30f && v.Y < 40f),
                    "zako scatter stays in its upper-right fan (bullet readability unchanged)");
                Check(Shards(drops).Cast<object>().All(s => Read<float>(s, "Fly") == 0.24f),
                    "zako shards keep the original short scatter window");

                // ── 実際に飛ばして「盤面全体に散り、1粒も取り逃さない」を確かめる ──
                //   欠片の位置は毎フレーム Field.Rect へクランプされるので画面外へは出られないが、
                //   端に貼りついた粒まで BeginRush の尺で戻り切れるかは実測しないと判らない。
                Shards(drops).Clear();
                Read<IList>(fx, "_p").Clear();
                drops.GetType().GetField("_rush", Fields)!.SetValue(drops, 0f);
                game.FlushShardImpression();
                long beforeSpread = game.Score;
                root.Player.GlobalPosition = new Vector2(Field.Left + 20, 170);
                fx.PurifyBurst(new Vector2(260, 90), 1500, FxLayer.PurifyTier.Boss, 40);
                int spreadCount = Shards(drops).Count;
                int spreadBonus = Value(drops);
                Tick(drops, 48); // 0.8s＝散り切った瞬間の広がりを測る
                var spread = Shards(drops).Cast<object>().Select(s => Read<Vector2>(s, "Position"))
                    .Select(p => drops.ToGlobal(p)).ToArray();
                // 散っている（まだ吸引に入っていない）粒は1つ残らず盤面の中。初速を 1.55→2.6 倍に
                // 上げても画面外へ飛び去らないのは、_PhysicsProcess が毎フレーム Field.Rect へ
                // クランプするから＝端に貼りつくだけで「拾えないまま消える」粒は生まれない。
                var flying = Shards(drops).Cast<object>().Where(s => !Read<bool>(s, "Attracted"))
                    .Select(s => drops.ToGlobal(Read<Vector2>(s, "Position"))).ToArray();
                var outside = flying.Where(p => !Field.Rect.HasPoint(p)).ToArray();
                Check(outside.Length == 0,
                    $"scattering boss shards never leave the playable field ({outside.Length} outside)");
                // 盤面(264×216)を 4×4 に割って、少なくとも 12 マスに粒が居る＝「全体に散っている」。
                var cells = spread.Select(p => (
                        Mathf.Clamp((int)((p.X - Field.Left) / (Field.Width / 4)), 0, 3),
                        Mathf.Clamp((int)((p.Y - Field.Top) / (Field.Height / 4)), 0, 3)))
                    .Distinct().Count();
                Check(cells >= 12, $"boss shards cover the whole board, not one corner ({cells}/16 cells)");
                // 撒いた粒は最後の1粒まで拾える（端に貼りついた粒もラッシュ尺で戻り切る）。
                root.Player.GlobalPosition = new Vector2(260, 120);
                Tick(drops, 210);
                game.FlushShardImpression();
                Check(Shards(drops).Count == 0 && game.Score == beforeSpread + spreadBonus,
                    $"every one of the {spreadCount} boss shards is collected, none expires off-screen");
                Check(!Read<bool>(drops, "_harvest"), "the harvest showpiece closes once the last shard lands");
                drops.GetType().GetField("_rush", Fields)!.SetValue(drops, 0f);
                Shards(drops).Clear();
                Read<IList>(fx, "_p").Clear();

                // ── 難易度で欠片の量が変わる（Easy0.8 / Normal1.0 / Hard1.3 / Lunatic1.6）──
                //   粒数とスコア総量に同じ倍率が掛かり、階層（Zako<MidBoss<Boss）の段差は保たれる。
                //   ★通貨(impBase)にだけは DifficultyShardMul を掛けない。難易度の賭け金は経済側の
                //     DifficultyImpressionMul(0.7/1.0/1.6/3.0) 1本だけが担う＝二重適用の回帰検査。
                var shardDiffs = new[]
                {
                    (GameManager.Diff.Easy, 0.8f), (GameManager.Diff.Normal, 1.0f),
                    (GameManager.Diff.Hard, 1.3f), (GameManager.Diff.Lunatic, 1.6f),
                };
                // 難易度間は「実サンプル」でなく期待レンジの下限で比べる（Ri のゆらぎでレンジが
                // 一部重なるため、単発の実測値どうしの比較はフレーキーになる）。
                int previousBossFloor = 0;
                foreach (var (diff, mul) in shardDiffs)
                {
                    game.Difficulty = diff;
                    long impressionBefore = game.Impression;
                    foreach (var (tier, basePoints, lo, hi, rawPoints) in new[]
                    {
                        (FxLayer.PurifyTier.Zako, 100, 10, 16, 10),
                        (FxLayer.PurifyTier.MidBoss, 700, 52, 64, 70),
                        (FxLayer.PurifyTier.Boss, 1500, 104, 128, 150),
                    })
                    {
                        Shards(drops).Clear();
                        Read<IList>(fx, "_p").Clear();
                        // 通貨側の二重適用を検出するため、基礎額を明示して撒く（40＝分配の割り切れない値）。
                        const int ImpSeed = 40;
                        fx.PurifyBurst(new Vector2(350, 70), basePoints, tier, ImpSeed);
                        Check(ImpValue(drops) == ImpSeed,
                            $"{diff}/{tier}: shards carry the impression base unscaled by the shard multiplier ({ImpValue(drops)})");
                        int count = Shards(drops).Count;
                        int expectLo = Mathf.Max(1, Mathf.RoundToInt(lo * mul));
                        int expectHi = Mathf.Max(1, Mathf.RoundToInt(hi * mul));
                        Check(count >= expectLo && count <= expectHi,
                            $"{diff}/{tier}: shard count {count} scales with the difficulty multiplier ({expectLo}-{expectHi})");
                        Check(Value(drops) == Mathf.Max(1, Mathf.RoundToInt(rawPoints * mul)),
                            $"{diff}/{tier}: shard score total follows the same multiplier, distribution still sums exactly");
                        Check(Shards(drops).Count <= 640, $"{diff}/{tier}: shard count stays inside Capacity");
                        if (tier == FxLayer.PurifyTier.Boss)
                        {
                            Check(expectLo > previousBossFloor, $"{diff}: harder difficulty scatters more boss shards than the tier below");
                            previousBossFloor = expectLo;
                        }
                    }
                    // 撒いただけでは1も入らない（通貨は拾って初めて入る）。難易度倍率も撒く側には掛からない。
                    Check(game.Impression == impressionBefore, $"{diff}: scattering alone never pays shop currency");
                    drops.GetType().GetField("_rush", Fields)!.SetValue(drops, 0f);
                }

                // 同じ基礎額を難易度違いで実際に拾い、実入りが DifficultyImpressionMul(0.7/1.0/1.6/3.0) の
                // 1本だけで決まることを確かめる（欠片側 0.8/1.0/1.3/1.6 が混ざっていれば比が崩れて落ちる）。
                foreach (var diff in new[] { GameManager.Diff.Easy, GameManager.Diff.Normal, GameManager.Diff.Hard, GameManager.Diff.Lunatic })
                {
                    game.Difficulty = diff;
                    Shards(drops).Clear();
                    Read<IList>(fx, "_p").Clear();
                    game.FlushShardImpression();
                    long before = game.Impression;
                    const int ImpSeed = 40;
                    fx.PurifyBurst(new Vector2(350, 70), 100, FxLayer.PurifyTier.Zako, ImpSeed);
                    root.Player.GlobalPosition = new Vector2(350, 70);
                    Tick(drops, 2);
                    root.Player.GlobalPosition = new Vector2(350, 70);
                    Tick(drops, 120);
                    game.FlushShardImpression();
                    Check(Shards(drops).Count == 0 && game.Impression - before == Expected(game, ImpSeed),
                        $"{diff}: picked-up currency follows only the economy multiplier (+{game.Impression - before})");
                    drops.GetType().GetField("_rush", Fields)!.SetValue(drops, 0f);
                }
                // 以降の検査はすべて Normal（倍率1.0）の期待値で書かれているので Normal に戻して続ける。
                game.Difficulty = GameManager.Diff.Normal;
                Shards(drops).Clear();
                Read<IList>(fx, "_p").Clear();
                drops.GetType().GetField("_rush", Fields)!.SetValue(drops, 0f);

                fx.PurifyBurst(new Vector2(350, 70), 1500, FxLayer.PurifyTier.Boss, 40);
                int clearBonus = Value(drops);
                int clearImp = ImpValue(drops);
                long beforeClear = game.Score;
                game.FlushShardImpression();
                long beforeClearImp = game.Impression;
                game.SetStageTarget(game.PurifiedCount);
                root.Hud.HoldBubble = true;
                root.Hud.ShowMessage("QA");
                Tick(drops, 150);
                game.FlushShardImpression();
                // 掃引は通貨も含めて全部回収する＝ステージ終わりの取りこぼしは原理的に起きない。
                // これがあるので「拾い逃しのストレス」は道中に限定され、補正係数もそこだけを見ればよい。
                Check(Shards(drops).Count == 0 && game.Score == beforeClear + clearBonus
                    && game.Impression == beforeClearImp + Expected(game, clearImp),
                    "stage clear sweeps up all remaining shards, currency included, even during the defeat dialogue");
                root.Hud.HoldBubble = false;
                root.Hud.HideBubble();
                game.SetStageTarget(999);
                // 以降の「離れた欠片は拾えないまま」系の検査に撃破ラッシュの残りが効かないよう明示的に切る。
                drops.GetType().GetField("_rush", Fields)!.SetValue(drops, 0f);

                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540) })
                {
                    DisplayServer.WindowSetSize(size);
                    root.Player.GlobalPosition = new Vector2(148, 150);
                    fx.PurifyBurst(new Vector2(280, 100), 100);
                    int runtimeCount = Shards(drops).Count;
                    long beforePickup = game.Score;
                    int runtimeBonus = Value(drops);
                    drops.SetPhysicsProcess(true);
                    await PhysicsFrames(40);
                    Check(Shards(drops).Count == runtimeCount && game.Score == beforePickup,
                        $"{job.CharacterId} at {size}: actual physics keeps distant drops available");
                    await Shot($"{job.CharacterId}_{size.X}_runtime_scatter");
                    root.Player.GlobalPosition = new Vector2(280, 100);
                    await PhysicsFrames(2);
                    root.Player.GlobalPosition = new Vector2(208, 150);
                    await PhysicsFrames(12);
                    await Shot($"{job.CharacterId}_{size.X}_runtime_attract");
                    await PhysicsFrames(90);
                    Check(Shards(drops).Count == 0 && game.Score == beforePickup + runtimeBonus,
                        $"{job.CharacterId} at {size}: actual physics collects every shard once");
                    drops.SetPhysicsProcess(false);
                }
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));

                // 容量 256→640（ボス撃破 104〜128 粒 + 道中の取り残しを飲み込むため）。
                // 溢れた分は Add が false を返し、ただの装飾パーティクルへ落ちる＝上限は依然として有効。
                game.FlushShardImpression();
                long beforeOverflow = game.Impression;
                for (int i = 0; i < 120; i++) fx.PurifyBurst(new Vector2(310, 100), 100, FxLayer.PurifyTier.Zako, 40);
                Check(Shards(drops).Count == 640, "large chains have a bounded particle count");
                // 容量超過で欠片になれなかったぶんの通貨は、その場で入金して帳尻を合わせる
                // （演出の都合でお金が黙って消えるのは「気づけない損」＝いちばん質の悪い罰）。
                game.FlushShardImpression();
                Check(game.Impression > beforeOverflow + Expected(game, 40),
                    "currency that overflows the shard capacity is paid out instead of vanishing");
                typeof(Player).GetProperty("Lives")!.SetValue(root.Player, 0);
                long beforeDeath = game.Score, beforeDeathImp = game.Impression;
                Tick(drops, 60);
                game.FlushShardImpression();
                Check(Shards(drops).Count == 0 && game.Score == beforeDeath && game.Impression == beforeDeathImp,
                    "game over cannot collect lingering bonuses");
                fx.PurifyBurst(new Vector2(300, 100), 100);
                root.QueueFree();
                await Task.Delay(150);
                await Frames(10);
                Check(!IsInstanceValid(drops), "scene changes discard the previous stage's shards");
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[ScoreShardQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[ScoreShardQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Movie(GameManager game)
    {
        game.SelectedJob = Job.Magic;
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.Stage.SetProcess(false);
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        game.SetStageTarget(999);
        root.Player.GlobalPosition = new Vector2(156, 125);
        typeof(Player).GetField("_fireCooldown", Fields)!.SetValue(root.Player, 999f);
        var fx = root.World.GetNode<FxLayer>("FxLayer");
        Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
        Audio.Instance?.StopMusic(0);
        await Frames(45);

        for (int wave = 0; wave < 2; wave++)
        {
            var enemies = new PageShard[3];
            for (int i = 0; i < enemies.Length; i++)
            {
                var enemy = new PageShard { Position = new Vector2(278 + i * 12, 80 + i * 36) };
                root.World.AddChild(enemy);
                enemy.SetPhysicsProcess(false);
                enemies[i] = enemy;
            }
            await Frames(45);
            foreach (var enemy in enemies)
            {
                foreach (var panel in enemy.GetChildren().OfType<Panel>().ToArray()) panel.Shatter();
                enemy.SetPhysicsProcess(true);
                await Frames(16);
            }
            await Frames(40);
            long beforePickup = game.Score;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Right, Pressed = true });
            await Frames(100);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Right, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, Pressed = true });
            await Frames(35);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Down, Pressed = true });
            await Frames(70);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Down, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, Pressed = true });
            await Frames(35);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, Pressed = false });
            await Frames(25);
            Check(game.Score > beforePickup, "movie demonstrates proximity pickups");
            if (wave == 0)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Left, Pressed = true });
                await Frames(100);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Left, Pressed = false });
            }
        }
        await Frames(30);
        GD.Print("[ScoreShardQA] MOVIE COMPLETE");
    }

    // 撃破直後〜散り〜回収中〜拾い切りを、実物の物理とレンダで連続撮影する（--burst）。
    // FPS も同時に測る（ボス撃破の瞬間にフレームが落ちていないかの実測）。
    private async Task BurstShots(GameManager game)
    {
        game.SelectedJob = Job.Magic;
        game.Difficulty = GameManager.Diff.Lunatic; // 粒数が最大（×1.6）になる最悪ケースで測る
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.Stage.SetProcess(false);
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        game.SetStageTarget(999);
        var fx = root.World.GetNode<FxLayer>("FxLayer");
        Audio.Instance?.StopMusic(0);
        await Frames(45);
        // シーン/シェーダの暖機。最初の1秒は素で 5fps 台まで落ちるので、計測の前に必ず捨てる。
        await Frames(180);

        foreach (var (tier, label, basePoints) in new[]
        {
            (FxLayer.PurifyTier.Boss, "boss", 1500),
            (FxLayer.PurifyTier.MidBoss, "mid", 700),
            (FxLayer.PurifyTier.Zako, "zako", 100),
        })
        {
            root.Player.GlobalPosition = new Vector2(Field.Left + 40, 160);
            typeof(Player).GetField("_fireCooldown", Fields)!.SetValue(root.Player, 999f);
            await Frames(20);
            // 撒く直前の素のFPS（比較の基準）。ここが既に落ちていれば演出のせいではない。
            double idleMin = 999;
            for (int f = 0; f < 60; f++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (f >= 20) idleMin = Mathf.Min(idleMin, Engine.GetFramesPerSecond());
            }
            fx.PurifyBurst(new Vector2(268, 88), basePoints, tier, 60);
            GD.Print($"[burst] {label}: shards={Shards(fx.ScoreDrops).Count} idle-min-fps={idleMin:0}");
            // 0.1 / 0.3 / 0.6 / 0.9 / 1.4 / 2.0 / 2.8s の7枚（散り→吸引→拾い切り→締め）
            double fpsMin = 999; int prev = 0;
            foreach (var at in new[] { 6, 12, 18, 18, 30, 36, 48 })
            {
                for (int f = 0; f < at; f++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    fpsMin = Mathf.Min(fpsMin, Engine.GetFramesPerSecond());
                }
                await Shot($"burst_{label}_{prev:00}");
                prev++;
            }
            // ★min-fps には撮影(SavePng)の停止が混ざる。純粋な描画コストは撮影抜きで測ること
            //   （2026-09-22 実測: Lunatic・190粒で idle と burst-min が一致＝演出由来の低下は無し）。
            GD.Print($"[burst] {label}: min-fps={fpsMin:0}(incl. screenshot stalls) remaining={Shards(fx.ScoreDrops).Count}");
            Shards(fx.ScoreDrops).Clear();

            // 撮影を挟まない素の計測。撒いてから拾い切るまでを回し切って最小FPSを取る。
            root.Player.GlobalPosition = new Vector2(Field.Left + 40, 160);
            await Frames(40);
            double pureIdle = 999;
            for (int f = 0; f < 40; f++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); if (f >= 10) pureIdle = Mathf.Min(pureIdle, Engine.GetFramesPerSecond()); }
            fx.PurifyBurst(new Vector2(268, 88), basePoints, tier, 60);
            double pureMin = 999; int peak = 0;
            for (int f = 0; f < 180; f++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                pureMin = Mathf.Min(pureMin, Engine.GetFramesPerSecond());
                peak = Mathf.Max(peak, Shards(fx.ScoreDrops).Count);
            }
            GD.Print($"[burst] {label}: idle={pureIdle:0}fps burst-min={pureMin:0}fps peakShards={peak}");
            Shards(fx.ScoreDrops).Clear();
        }
        GD.Print("[ScoreShardQA] BURST COMPLETE");
    }

    private async Task PhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string dir = ProjectSettings.GlobalizePath("res://build/qa_story/score_shards");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"{dir}/{name}.png");
    }
}
