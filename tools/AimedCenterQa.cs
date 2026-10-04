using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// 「自機を狙って撃つ攻撃は、棒立ちしていたら必ず当たる」を機械的に確かめる QA（2026-10-03 ユーザー指示）。
//   避ける動作を要求するのがゲーム性であって、動かなければ安全というのは逆＝その逆転を検出する。
//
// 見るもの:
//   ① 自機狙いの扇（あかり／こはるの FanDown・中ボスこはるの FanDown）
//      … 本数が偶数だと中心線（t=0）に弾が無く、棒立ちの自機の両脇を弾が通り抜ける。
//   ② 端からの斉射（BossEdgeVolley・あかり=上下／こはる=左右／レイ=左右上／ミナ=四方）
//      … 列の並びを場の内側へクランプしていると、自機が端に寄っているとどの列も自機の軸を通らない。
//   ③ 道中ザコの固有攻撃（AttackPattern 全23種・2026-10-03「雑魚敵も対応して」）
//      … 扇の中心だけでなく、置き弾の格子・壁の隙間・落下車線が「自機の居る行/列」を含むか。
//        敵の位置にだけ紐づいた格子は、ほとんど動かない個体の前で永久の安置を作っていた。
//
// 置き場所は中央・上下左右の端ぎわ・四隅の9点。②の穴は端でしか出ない。
// 難易度は4つ全部。①の穴は本数が偶数になる難易度でしか出ない（BulletCountMul 次第で変わる）。
// 判定は幾何：弾の発射点から Velocity 方向への半直線が、自機の当たり円（Player._hitR）＋弾半径に
//   触れるか。触れる＝そのまま動かなければ必ず当たる。実際に当たるまで回すより速く、取りこぼしが無い。
public partial class AimedCenterQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object o, string field) => (T)o.GetType().GetField(field, Private)!.GetValue(o)!;
    private static void Write(object o, string field, object value) => o.GetType().GetField(field, Private)!.SetValue(o, value);
    private static object? Call(object o, string method, params object[] args)
        => o.GetType().GetMethod(method, Private | BindingFlags.Public)!.Invoke(o, args);
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private Bullet[] Bullets() => Pool.GetChildren().OfType<Bullet>().Where(b => b.Active && b.IsEnemy).ToArray();
    private int _passes;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[AimedCenterQA] PASS {message}");
    }

    // 自機の置き場所（9点）。盤面の縁は自機の可動域そのもの（Player.MinX..MaxX = Field 矩形）。
    private static IEnumerable<(string name, Vector2 at)> Spots() => new[]
    {
        ("center", new Vector2(Field.CenterX, Field.CenterY)),
        ("left", new Vector2(Field.Left, Field.CenterY)),
        ("right", new Vector2(Field.Right, Field.CenterY)),
        ("top", new Vector2(Field.CenterX, Field.Top)),
        ("bottom", new Vector2(Field.CenterX, Field.Bottom)),
        ("topleft", new Vector2(Field.Left, Field.Top)),
        ("topright", new Vector2(Field.Right, Field.Top)),
        ("bottomleft", new Vector2(Field.Left, Field.Bottom)),
        ("bottomright", new Vector2(Field.Right, Field.Bottom)),
    };

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            PrintCounts(game);
            foreach (var (scene, key, theme) in new[]
            {
                ("Akari", "akari", StageTheme.Akari), ("Koharu", "koharu", StageTheme.Koharu),
                ("Rei", "rei", StageTheme.Rei), ("MinaBattle", "mina", StageTheme.Mina),
            })
            {
                game.SelectedEntry = GameManager.StageEntry.Start;
                game.Difficulty = GameManager.Diff.Normal;
                var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
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
                float hitR = Read<float>(player, "_hitR");
                await Frames(3);

                await CheckZako(game, world, player, hitR, theme, withDefault: scene == "Akari");
                if (scene == "Koharu") await CheckCameoFan(game, stage, player, hitR);

                Enemy boss = scene switch
                {
                    "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
                    "Rei" => new BossRei(), _ => new BossMina(),
                };
                world.AddChild(boss);
                boss.GlobalPosition = new Vector2(Field.Right - 70f, 90f);
                root.GetNode<StageBackground>("StageBackground").EnterBoss();
                await Frames(250);   // 登場演出を流し切る（シールド生成中は弾を撃たせない）
                boss.SetPhysicsProcess(false);
                boss.GlobalPosition = new Vector2(Field.Right - 70f, 90f);
                var caster = Read<Node>(boss, "_caster");
                caster.SetProcess(false);
                Call(caster, "CancelPendingAttacks");
                Hud.BubblePaused = false;
                await Frames(2);

                if (boss.GetType().GetMethod("FanDown", Private) != null)
                    await CheckBossFan(game, boss, player, hitR, scene);
                await CheckEdgeVolleys(game, boss, player, hitR, key);

                root.QueueFree();
                await Frames(5);
                Pool.DespawnAll();
                Hud.BubblePaused = false;
            }
            game.Difficulty = GameManager.Diff.Normal;
            Audio.Instance?.StopMusic(0);
            foreach (var node in GetNode<Audio>("/root/Audio").GetChildren())
                if (node is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print($"[AimedCenterQA] ALL PASS ({_passes} aimed patterns verified)");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[AimedCenterQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    // 難易度ごとの本数を一覧で出す（修正前後の比較と、BulletCountMul をいじった時の確認用）。
    private static void PrintCounts(GameManager game)
    {
        var keep = game.Difficulty;
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            GD.Print($"[AimedCenterQA] counts {diff}: mul={game.BulletCountMul:0.00} "
                + $"boss_fan(9) {game.ScaleBullets(9)}->{game.ScaleBulletsOdd(9)} "
                + $"cameo_fan(5) {game.ScaleBullets(5)}->{game.ScaleBulletsOdd(5)} "
                + $"char_fan(3) {game.ScaleBullets(3)}->{game.ScaleBulletsOdd(3)} "
                + $"char_fan(2) {game.ScaleBullets(2)}->{game.ScaleBulletsOdd(2)} "
                + $"char_fan(1) {game.ScaleBullets(1)}->{game.ScaleBulletsOdd(1)}");
        }
        game.Difficulty = keep;
    }

    // 弾の軌道（発射点から Velocity 方向への半直線）と自機の当たり円の隙間(px)。0以下＝必ず当たる。
    //   半直線なので「もう通り過ぎた弾」は距離が離れていく＝脅威に数えない。発射点が既に自機へ
    //   重なっているケース（端の門の真上に居る等）も、原点までの距離で正しく拾える。
    private static float Gap(Bullet b, Vector2 at, float hitR)
    {
        if (b.Velocity.LengthSquared() <= 0f) return 9999f;
        var dir = b.Velocity.Normalized();
        var rel = at - b.GlobalPosition;
        float along = rel.Dot(dir);
        float dist = along >= 0f ? Mathf.Abs(rel.Cross(dir)) : rel.Length();
        return dist - (hitR + b.Radius);
    }

    // 敵弾は「敵の中心に湧いて本来の発射点まで飛んでから」パターンへ移る（Bullet.MakeLeadIn）。
    // 軌道の幾何を見るので、導入区間を消化して本来の発射点に着かせてから判定する。
    private static Bullet[] Settle(Bullet[] bullets)
    {
        foreach (var b in bullets)
            for (int i = 0; i < 64 && b.LeadIn; i++) b._PhysicsProcess(1.0 / 60.0);
        return bullets;
    }

    private void Assert(string label, Vector2 at, float hitR)
    {
        var bullets = Settle(Bullets());
        float best = bullets.Length == 0 ? 9999f : bullets.Min(b => Gap(b, at, hitR));
        int hits = bullets.Count(b => Gap(b, at, hitR) <= 0f);
        Check(bullets.Length > 0 && hits > 0,
            $"{label}: {hits}/{bullets.Length} shots cross a motionless player (closest gap {best:0.00}px)");
        _passes++;
    }

    // 参考値のみ（その点へは幾何的に届きえない攻撃＝ここで当たることは要求しない。理由を必ず添える）。
    private void Report(string label, Vector2 at, float hitR, string why)
    {
        var bullets = Settle(Bullets());
        float best = bullets.Length == 0 ? 9999f : bullets.Min(b => Gap(b, at, hitR));
        GD.Print($"[AimedCenterQA] INFO {label}: {bullets.Length} shots, closest gap {best:0.00}px — {why}");
    }

    // ── ① ボスの扇（あかり／こはるの FanDown）──
    private async Task CheckBossFan(GameManager game, Enemy boss, Player player, float hitR, string scene)
    {
        var fan = boss.GetType().GetMethod("FanDown", Private)!;
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            foreach (var (name, at) in Spots())
            {
                Pool.DespawnAll();
                player.GlobalPosition = at;
                Call(boss, "ApplySpell");
                fan.Invoke(boss, new object[] { Pool });
                Assert($"{scene}/FanDown/{diff}/{name}", at, hitR);
            }
            await Frames(1);
        }
        Pool.DespawnAll();
        game.Difficulty = GameManager.Diff.Normal;
    }

    // ── ① 中ボス（こはる）の扇 ──
    private async Task CheckCameoFan(GameManager game, Node stage, Player player, float hitR)
    {
        // 中ボスの登場カットシーンは会話送りを待つ＝ルナティック経路（カットシーン無し）を借りて出す
        //（EnemyRosterQa.CheckCameoAttacks と同じ作法）。
        Write(stage, "_stepStarted", false);
        Write(stage, "_lunatic", true);
        Call(stage, "Step_BossCameo", 0d);
        Write(stage, "_lunatic", false);
        var cameo = Read<CameoBoss>(stage, "_cameo");
        // シールド生成が終わるまで FireBullet は湧いた弾をその場で捨てる（Enemy.FireBullet の
        //   GaugeReforming ガード）＝撃てる状態になるまで待つ。
        for (int i = 0; i < 24 && (cameo.GaugeReforming || cameo.GaugeVulnerable || cameo.IsPurified); i++)
            await Frames(30);
        Check(!cameo.GaugeReforming && !cameo.GaugeVulnerable && !cameo.IsPurified,
            "koharu cameo reaches a state where it actually fires");
        cameo.SetPhysicsProcess(false);
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            foreach (var (name, at) in Spots())
            {
                Pool.DespawnAll();
                player.GlobalPosition = at;
                Write(cameo, "_fireT", 0d);                                       // 1つ目（雨）は撃たせない
                Write(cameo, "_fireT2", 1.5d * game.DanmakuIntervalMul);          // 2つ目（扇）だけ撃たせる
                Call(cameo, "FirePattern", 0d);
                Assert($"KoharuCameo/FanDown/{diff}/{name}", at, hitR);
            }
            await Frames(1);
        }
        Pool.DespawnAll();
        game.Difficulty = GameManager.Diff.Normal;
        cameo.QueueFree();
        await Frames(2);
    }

    // ── ① 道中ザコの固有攻撃（AttackPattern 全23種）──
    //   2026-10-03 ユーザー指示「雑魚敵も対応して」。1種ずつ 9点×4難易度で
    //   「棒立ちの自機を通る弾が最低1発あるか」を見る。
    //
    //   自機を狙う種（単発・扇・ビーム）は狙いそのものが自機を通ること。
    //   置き弾・壁・落下弾は“狙い”という概念が無いが、「その場に居続ければ絶対に安全」も許さない＝
    //     格子/車線/隙間が自機の居る行・列を必ず含むかを見る（壁の隙間は自機の行に置かない）。
    //   どちらでもない種（全方位リング・撃たない盾・運び役）は Kind/Exempt に理由を書いて別扱いにする。
    //
    //   敵の置き場所は種ごとに変える：左へ流す壁・置き弾は自機より右から出さないと届かない、
    //   真下へ落とす種は自機より上に居ないと届かない。「届く位置に居る個体なら必ず当たる」を見る QA なので、
    //   届かない配置で測ってもその種の穴は見えない。Binding=false の点だけは理由付きの参考値にする。
    private enum Aim
    {
        Shots,   // 弾を出す：棒立ちの自機を通る弾が1発以上あること
        Beam,    // AreaStrike のビーム：被弾域が自機を含むこと
        Mute,    // 撃たない種：本当に1発も出ないこと
    }

    private readonly record struct ZakoCase(string Name, EnemySpec Spec, Aim Kind,
        Func<Vector2, Vector2> Place, Func<Vector2, bool> Binding, string Exempt, bool BothParities = false);

    private static bool Always(Vector2 at) => true;
    private static bool Never(Vector2 at) => false;
    private static Vector2 Camp(Vector2 at) => new(Field.Right - 60f, 100f);
    // 左へ流す壁・置き弾は自機より右から。自機が最右端に居るときだけ同じ列（＝頭上に湧く）になる。
    private static Vector2 RightOf(Vector2 at) => new(Mathf.Min(Field.Right, at.X + 90f), 100f);
    // 固定左向きのばらまきは「真横に並んだ個体」で測る（扇の中心＝真左が自機を通るか）。
    private static Vector2 LevelRight(Vector2 at) => new(Mathf.Min(Field.Right, at.X + 120f), at.Y);
    // 真下へ落とす種は自機の真上から。
    private static Vector2 Above(Vector2 at) => new(at.X, Mathf.Max(Field.Top, at.Y - 90f));
    // 上から降る雨は斜め上から。
    private static Vector2 AboveRight(Vector2 at)
        => new(Mathf.Min(Field.Right - 20f, at.X + 60f), Mathf.Max(Field.Top, at.Y - 70f));
    // 数字の雨が届く点＝発射列より下。発射列は MidEnemy と同じ式（本体の 24px 上／場の上端+16 以下にはしない）。
    private static bool BelowMetricsRow(Vector2 at)
        => at.Y > Mathf.Max(Field.Top + 16f, AboveRight(at).Y - 24f);

    private const string RingWhy =
        "omni ring: equal spokes around the body have no centre lane to aim, so 'the aimed shot must hit' does not apply";
    private const string RainWhy =
        "falling rain: bullets only travel downward, and a player pinned to the very top row (y=0) sits above every "
        + "emitter row the board allows — unreachable by construction, not a dodge-free safe spot";

    private System.Collections.Generic.List<ZakoCase> ZakoCases(StageTheme theme, bool withDefault)
    {
        var cases = new System.Collections.Generic.List<ZakoCase>();
        void Add(ZakoCase c) => cases.Add(c);
        var (shooter, drifter) = EnemyTable.For(theme);
        foreach (var spec in new[] { shooter, drifter })
            switch (spec.Pattern)
            {
                case AttackPattern.ReiLockBurst:
                    Add(new("ReiLockBurst", spec, Aim.Beam, Camp, Always, "")); break;
                case AttackPattern.ReiPulseRing:
                    Add(new("ReiPulseRing", spec, Aim.Shots, Camp, Never, RingWhy)); break;
                case AttackPattern.AkariScatter:
                    Add(new("AkariScatter", spec, Aim.Shots, LevelRight, Always, "")); break;
                case AttackPattern.AkariDrop:
                    Add(new("AkariDrop", spec, Aim.Shots, Above, Always, "")); break;
                case AttackPattern.KoharuSharp3:
                    Add(new("KoharuSharp3", spec, Aim.Shots, Camp, Always, "")); break;
                case AttackPattern.KoharuSimmer:
                    Add(new("KoharuSimmer", spec, Aim.Shots, Camp, Always, "")); break;
            }
        foreach (var spec in EnemyTable.CharactersFor(theme))
            switch (spec.Pattern)
            {
                case AttackPattern.AkariVacant:
                    Add(new("AkariVacant", spec, Aim.Shots, RightOf, Always, "", BothParities: true)); break;
                case AttackPattern.KoharuComparison:
                    Add(new("KoharuComparison", spec, Aim.Shots, Camp, Always, "", BothParities: true)); break;
                case AttackPattern.KoharuParcel:
                    Add(new("KoharuParcel", spec, Aim.Shots, RightOf, Always, "")); break;
                case AttackPattern.ReiMetrics:
                    Add(new("ReiMetrics", spec, Aim.Shots, AboveRight, BelowMetricsRow, RainWhy,
                        BothParities: true)); break;
                case AttackPattern.MinaMemory:
                    Add(new("MinaMemory", spec, Aim.Shots, RightOf, Always, "")); break;
                default:
                    Add(new(spec.Pattern.ToString(), spec, Aim.Shots, Camp, Always, "")); break;
            }
        Add(new("FlankAim", EnemyTable.Flanker(theme), Aim.Shots,
            _ => new Vector2(Field.Left + Field.Width * 0.40f, 64f), Always, ""));
        Add(new("BuzzWall", EnemyTable.BuzzWall(theme), Aim.Mute, Camp, Never,
            "shield species: fires nothing at all (BaseInterval 999). Its pressure is the body it parks in the lane, "
            + "which the player must shoot through or walk around — standing still never becomes safe"));
        if (theme == StageTheme.Koharu)
            Add(new("KoharuPrayerCarry", EnemyTable.PrayerCarrier(), Aim.Mute, Camp, Never,
                "bonus species: fires nothing (BaseInterval 999). The prayer bullets it carries hang off its body and "
                + "are the reward (erasable, AddPrayerCleared), not an attack aimed at the player"));
        if (withDefault)
        {
            var (anti, quiet) = EnemyTable.For(StageTheme.Default);
            Add(new("DefaultAim", anti, Aim.Shots, Camp, Always, ""));
            Add(new("None", quiet, Aim.Mute, Camp, Never,
                "AttackPattern.None: TickFire returns immediately, so this species has no attack to aim"));
        }
        return cases;
    }

    // 1回の攻撃を撃たせる。予告を待たずに即発射させる（予告の長さは別QAの担当）。
    private void FireZako(MidEnemy enemy, AttackPattern pattern, Vector2 at)
    {
        switch (pattern)
        {
            case AttackPattern.ReiLockBurst: Call(enemy, "BeginLockBurst"); return;
            case AttackPattern.ReiPulseRing: Call(enemy, "FirePulseRing"); return;
            case AttackPattern.AkariScatter: Call(enemy, "FireScatter"); return;
            case AttackPattern.AkariDrop: Call(enemy, "FireDrop"); return;
            case AttackPattern.KoharuSharp3: Call(enemy, "BeginSharp3"); Call(enemy, "FireSharp3"); return;
            case AttackPattern.KoharuSimmer: Call(enemy, "FireSimmer"); return;
            case AttackPattern.DefaultAim: Call(enemy, "FireDefaultAim"); return;
            case AttackPattern.FlankAim: Call(enemy, "FireFlank"); return;
        }
        // 「記憶の残響」は自分が通った道へ置く＝自機の居た点を通った足跡を1つ仕込んでから撃たせる
        //   （道が自機の位置を通っているなら、その痕は必ず自機に載るか、を見る）。
        if (pattern == AttackPattern.MinaMemory)
        {
            Read<Vector2[]>(enemy, "_trail")[0] = at;
            Write(enemy, "_trailHead", 1);
            Write(enemy, "_trailCount", 1);
        }
        Call(enemy, "BeginCharacterAttack");
        for (int i = 0; i < 8 && Read<int>(enemy, "_salvoRemaining") > 0; i++)
            Call(enemy, "FireCharacterSalvo");
    }

    // ロックオンビーム（AreaStrike）は弾ではないので被弾域で判定する。
    private void AssertBeam(string label, Vector2 at)
    {
        var strikes = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().ToArray();
        bool covered = strikes.Any(s => s.CoversPoint(at));
        // QueueFree は次フレームまで残る＝グループから即座に外して「今撃った1発」だけを見る。
        foreach (var s in strikes) { s.RemoveFromGroup("aoe"); s.QueueFree(); }
        Check(strikes.Length > 0 && covered,
            $"{label}: {strikes.Length} beam(s), danger zone covers a motionless player");
        _passes++;
    }

    private async Task CheckZako(GameManager game, Node2D world, Player player, float hitR,
        StageTheme theme, bool withDefault)
    {
        foreach (var zc in ZakoCases(theme, withDefault))
        {
            if (zc.Kind == Aim.Mute)
            {
                // 撃たない種：本当に1発も出ないことを確かめる（Pattern が撃つ側へ紛れ込んだら落ちる）。
                game.Difficulty = GameManager.Diff.Lunatic;
                Pool.DespawnAll();
                player.GlobalPosition = new Vector2(Field.CenterX, Field.CenterY);
                var mute = new MidEnemy();
                mute.Configure(zc.Spec);
                world.AddChild(mute);
                mute.GlobalPosition = Camp(player.GlobalPosition);
                mute.SetPhysicsProcess(false);
                for (int i = 0; i < 600; i++) Call(mute, "TickFire", 0.2);   // 120秒ぶん
                Check(Bullets().Length == 0, $"{zc.Name}: no shot in 120s — {zc.Exempt}");
                _passes++;
                mute.QueueFree();
                game.Difficulty = GameManager.Diff.Normal;
                await Frames(1);
                continue;
            }
            foreach (var diff in Enum.GetValues<GameManager.Diff>())
            {
                game.Difficulty = diff;
                foreach (var (name, at) in Spots())
                {
                    player.GlobalPosition = at;
                    var enemy = new MidEnemy();
                    enemy.Configure(zc.Spec);
                    world.AddChild(enemy);
                    enemy.GlobalPosition = zc.Place(at);
                    enemy.SetPhysicsProcess(false);
                    // 攻撃ごとに形を振る種（空席の隙間・比較の振り・数字の傾き）は両方の位相を見る。
                    for (int round = 0; round <= (zc.BothParities ? 1 : 0); round++)
                    {
                        Pool.DespawnAll();
                        FireZako(enemy, zc.Spec.Pattern, at);
                        string label = $"{zc.Name}/{diff}/{name}" + (zc.BothParities ? $"/{round}" : "");
                        if (zc.Kind == Aim.Beam) AssertBeam(label, at);
                        else if (zc.Binding(at)) Assert(label, at, hitR);
                        else Report(label, at, hitR, zc.Exempt);
                    }
                    enemy.QueueFree();
                }
                await Frames(1);
            }
        }
        Pool.DespawnAll();
        game.Difficulty = GameManager.Diff.Normal;
    }

    // ── ② 端からの斉射（BossEdgeVolley）──
    //   門の並びは「予兆が出た瞬間の自機位置」に合わせる仕様。予兆のあいだに動けば避けられるのは
    //   そのままで、見るのは「動かなかったら当たるか」だけ。自機が端へ寄っているケースが本番。
    private async Task CheckEdgeVolleys(GameManager game, Enemy boss, Player player, float hitR, string key)
    {
        int edges = key switch { "akari" => 2, "koharu" => 2, "rei" => 3, _ => 4 };
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            for (int sequence = 0; sequence < edges; sequence++)
                foreach (var (name, at) in Spots())
                {
                    Pool.DespawnAll();
                    player.GlobalPosition = at;
                    var volley = BossEdgeVolley.Begin(boss, key, sequence);
                    volley.SetPhysicsProcess(false);
                    var gates = Read<Vector2[]>(volley, "_gates");
                    Vector2 direction = Read<Vector2>(volley, "_direction");
                    Check(gates.All(p => p.X >= Field.Left && p.X <= Field.Right
                            && p.Y >= Field.Top && p.Y <= Field.Bottom),
                        $"edge/{key}/{volley.Side}/{diff}/{name}: emitters stay inside the playfield");
                    Check(gates.Length == new HashSet<Vector2>(gates).Count
                            && gates.Zip(gates.Skip(1), (a, b) => a.DistanceTo(b)).All(gap => gap >= 22f - 0.01f),
                        $"edge/{key}/{volley.Side}/{diff}/{name}: {gates.Length} lanes keep their 22px dodge gaps");
                    Check(gates.Any(p => Mathf.Abs((p - at).Cross(direction)) < 0.01f),
                        $"edge/{key}/{volley.Side}/{diff}/{name}: one lane sits exactly on the player's axis");
                    volley._PhysicsProcess(Read<double>(volley, "_warning") + 0.01);   // 1列目を出す
                    Assert($"edge/{key}/{volley.Side}/{diff}/{name}", at, hitR);
                    volley.Cancel();
                }
            await Frames(1);
        }
        Pool.DespawnAll();
        game.Difficulty = GameManager.Diff.Normal;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
