using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// PlayerHomeQa : 「場が仕切り直される瞬間に自機も初期位置へ戻る」（2026-10-03 ユーザー指示）の自動検証。
//   仕組み（src/Player.cs の StartPosition / ReturnToStart / TickReturnHome）と、実装した**呼び出し箇所を全部**
//   機械的に通す。測るときは必ず自機をわざと初期位置から離した場所（右下の隅 Far）へ置いてから節目を起こす
//   ＝離さずに測ると「もともと初期位置に居た」のか「戻った」のか区別がつかない。
//
//   ■ 見るもの
//     (1) 節目で帰還が**予約**されること（その瞬間にワープしないこと＝演出や吹き出しの裏で位置が変わらない）
//     (2) 操作が戻った最初のフレームで滑り出し、初期位置へ**戻り切る**こと
//     (3) 滑走中〜着地直後に**被弾しないこと**（無敵が重なる。被弾由来ではない＝グレイズ経済に触らない）
//     (4) その無敵が**居座らない**こと（余韻が切れたら通常どおり被弾する）
//     (5) 対象外にした経路（ルナティックの道中の波の継ぎ目／FINAL の残響の波）では**戻さない**こと
//
//   ■ 決定論のために
//     ヘッドレスの process フレームは物理 tick と一致しないので、自機の _PhysicsProcess は切って
//     QA 側から dt=1/60 固定で手回しする（MinaPhaseQa と同じ流儀）。節目を起こしてから測り終えるまでの
//     あいだ await を一度も挟まない＝湧きや敵の射撃が混ざらない。
//     スクショは撮らない（Shot() はヘッドレスで固まる）。
//
//   起動:
//     APPDATA=<repo>/build/qa_story/player_home_appdata \
//       godot --headless --path <repo> res://tools/qa_player_home.tscn
public partial class PlayerHomeQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic;

    // private/protected は基底（Enemy）側にも居るので、宣言型まで遡って引く。
    private static FieldInfo FieldOf(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
            if (t.GetField(name, Private) is { } f) return f;
        throw new Exception($"field {name} not found on {type}");
    }
    private static MethodInfo MethodOf(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
            if (t.GetMethod(name, Private) is { } m) return m;
        throw new Exception($"method {name} not found on {type}");
    }
    private static T Read<T>(object o, string field) => (T)FieldOf(o.GetType(), field).GetValue(o)!;
    private static void Write(object o, string field, object? value) => FieldOf(o.GetType(), field).SetValue(o, value);
    private static object? Call(object o, string method, params object[] args)
        => MethodOf(o.GetType(), method).Invoke(o, args);
    private static T Prop<T>(object o, string name) => (T)o.GetType().GetProperty(name)!.GetValue(o)!;
    private static void Property(object o, string name, object value) => o.GetType().GetProperty(name)!.SetValue(o, value);

    private int _passed;
    private void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        _passed++;
        GD.Print($"[HOME] PASS {message}");
    }

    // 自機の1物理フレームぶんを手回しする（dt 固定＝距離と尺が毎回同じ）。
    private const double Dt = 1.0 / 60.0;
    private static void Tick(Player player, int frames)
    {
        for (int i = 0; i < frames; i++) player._PhysicsProcess(Dt);
    }

    // 測定用の「初期位置から離れた場所」＝盤面の右下の隅。初期位置(126,108)から約254px＝滑走は上限の 0.42 秒。
    private static readonly Vector2 Far = new Vector2(Field.Right - 20f, Field.Bottom - 20f);
    private const int GlideFrames = 40;  // 0.42 秒（26 フレーム）＋余白
    private const int TailFrames = 40;   // 余韻 0.35 秒（21 フレーム）＋余白

    private static bool AtStart(Player player) => player.GlobalPosition.DistanceTo(player.StartPosition) <= 0.01f;
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "隔離セーブで走っている");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            game.Difficulty = GameManager.Diff.Normal;
            game.SelectedJob = Job.Tank;
            await Frames(1);

            Check(Field.PlayerStart == new Vector2(Field.Left + 60f, 108f),
                "初期位置の定義元は Field.PlayerStart（値は従来どおり 盤面左端+60 / y=108）");

            await Mechanism(game);
            await Stage(game, "Akari", "akari");
            await Stage(game, "Koharu", "koharu");
            await Stage(game, "Rei", "rei");
            await Final(game);

            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            await Frames(3);
            GD.Print($"[HOME] ALL PASS ({_passed} checks)");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[HOME] FAIL {e}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    // ───────── 仕組み（あかり面の盤面を実験台に使う）─────────
    private async Task Mechanism(GameManager game)
    {
        var (root, stage, player, hud, world) = await Open(game, "Akari");
        Check(player.StartPosition == Field.PlayerStart, "仕組み: 自機の初期位置は Field.PlayerStart");
        Check(AtStart(player), "仕組み: ステージ開始の湧き位置＝初期位置（SnapToStart）");

        // 予約だけでは動かない／会話で止まっているあいだは滑り出さない。
        player.GlobalPosition = Far;
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "QA: 会話で止めている");
        await Frames(2);
        Check(Hud.BubblePaused, "仕組み: 会話で戦闘が止まっている状態を作れた");
        player.ReturnToStart();
        Check(player.ReturningToStart, "仕組み: 会話中でも帰還は予約される");
        Tick(player, 20);
        Check(player.GlobalPosition.IsEqualApprox(Far) && player.ReturningToStart,
            "仕組み: 会話中は滑り出さない（吹き出しの裏＝自機が文字の奥に潜っている間に位置を変えない）");
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(2);
        Check(!Hud.BubblePaused, "仕組み: 会話が明けた");

        // 明けた最初のフレームで滑り出し、戻り切る。瞬間移動ではない＝途中のフレームが観測できる。
        Write(player, "_invincible", false);
        Write(player, "_invincibleTimer", 0f);
        Tick(player, 1);
        float moved = player.GlobalPosition.DistanceTo(Far);
        Check(moved > 1f && !AtStart(player),
            $"仕組み: 操作が戻った最初のフレームで滑り出す（1フレームで {moved:0.0}px・瞬間移動ではない）");
        Check(Read<bool>(player, "_invincible") && !Read<bool>(player, "_hitInvincible"),
            "仕組み: 滑走中は無敵（被弾由来ではない＝グレイズ経済を止めない）");
        Tick(player, GlideFrames);
        Check(AtStart(player) && !player.ReturningToStart, "仕組み: 初期位置へきっちり戻り切る");

        // 無敵は余韻ぶんだけ。切れたら通常どおり被弾する（＝無敵が居座らない）。
        int lives = player.Lives;
        player.TakeHit();
        Check(player.Lives == lives, "仕組み: 着地直後は余韻の無敵で被弾しない");
        Tick(player, TailFrames);
        player.TakeHit();
        Check(player.Lives == lives - 1, "仕組み: 余韻が切れたら通常どおり被弾する（無敵は居座らない）");
        Property(player, "Lives", lives);
        Write(player, "_invincible", false);
        Write(player, "_invincibleTimer", 0f);

        // 既存の無敵（被弾・ゲームオーバーの 9999）を帰還の無敵で**縮めない**。
        Write(player, "_invincibleTimer", 9f);
        Write(player, "_invincible", true);
        player.GlobalPosition = Far;
        player.ReturnToStart();
        Tick(player, 2);
        Check(Read<float>(player, "_invincibleTimer") > 8.9f,
            "仕組み: 長い無敵は帰還の無敵で縮まない（Max で伸ばすだけ）");
        Tick(player, GlideFrames);
        Write(player, "_invincible", false);
        Write(player, "_invincibleTimer", 0f);

        // もう初期位置に居るなら動かさない＝入力を預からない・無敵も配らない
        //（ステージ開始直後の波0 がここに該当する）。
        player.GlobalPosition = player.StartPosition;
        player.ReturnToStart();
        Tick(player, 1);
        Check(!player.ReturningToStart && !Read<bool>(player, "_invincible"),
            "仕組み: すでに初期位置なら滑走も無敵も起こさない（無意味な入力ロックを作らない）");

        // マウス追従（カーソルへ毎フレーム引き戻す経路）に帰還が勝つ。さらに着いた後も、据え置きの
        // カーソルへ引き戻されない（Pad.ReleaseMouseFollow＝帰還が無かったことにならない）。
        typeof(Pad).GetField("_usingMouse", Statics)!.SetValue(null, true);
        player.GlobalPosition = Far;
        player.ReturnToStart();
        for (int i = 0; i < GlideFrames && player.ReturningToStart; i++) Tick(player, 1);
        Check(AtStart(player), "仕組み: マウス追従中でも帰還が勝って初期位置へ着く");
        Tick(player, 30);
        Check(AtStart(player) && !Pad.UsingMouse,
            "仕組み: 着地後に据え置きカーソルへ引き戻されない（マウス追従を一旦手放す）");
        typeof(Pad).GetField("_usingMouse", Statics)!.SetValue(null, false);

        // 滑走中の回避入力は食う（位置の駆動元を2つにしない）。押下は帰還明けにも繰り越さない。
        int dodges = player.DodgeCount;
        player.GlobalPosition = Far;
        player.ReturnToStart();
        KeyEvent(Key.Space, true);
        await Frames(2);
        Tick(player, 6);
        Check(player.DodgeCount == dodges, "仕組み: 滑走中は回避が出ない");
        Tick(player, GlideFrames);
        Check(AtStart(player) && player.DodgeCount == dodges,
            "仕組み: 滑走中に押された回避は明けてからも暴発しない（押下を食っている）");
        KeyEvent(Key.Space, false);
        await Frames(2);

        await Close(root);
    }

    // ───────── 呼び出し箇所（本編3面：道中の波の頭／中ボス戦の開始／ボス戦の開始／第二形態）─────────
    private async Task Stage(GameManager game, string scene, string id)
    {
        var (root, stage, player, hud, world) = await Open(game, scene);

        // (a) 道中の波の頭（StartMidwaveSpawner＝波0/A/B/C と、レイ面は引用の嵐もここを通る）
        Write(stage, "_spawner", null);
        Moment($"{id} 道中の波の頭", player, hud, world, () => Call(stage, "StartMidwaveSpawner", 0f));
        StopSpawner(stage);

        // (b) ルナティックの道中の波の頭＝対象外（会話の区切りを全部飛ばして戦闘を途切れさせない難易度なので、
        //     段ごとに入力を預かると流れを壊す。中ボス・ボス・形態変化は向こうでも戻す＝下の (c)(d)(e)）。
        Write(stage, "_lunatic", true);
        Write(stage, "_spawner", null);
        NoMoment($"{id} 道中の波の頭（ルナティック）", player, hud, () => Call(stage, "StartMidwaveSpawner", 0f));
        StopSpawner(stage);
        Write(stage, "_lunatic", false);

        // (c) 中ボス（カメオ）戦の開始。登場カットシーンは既読扱いにして飛ばし、湧きの一行だけを通す。
        Write(stage, "_cameoIntroDone", true);
        Write(stage, "_stepStarted", false);
        Moment($"{id} 中ボス戦の開始", player, hud, world, () => Call(stage, "Step_BossCameo", 0.0));
        var cameo = Read<CameoBoss>(stage, "_cameo");
        Check(IsInstanceValid(cameo), $"{id}: 中ボスが湧いている（測った節目が本物）");
        cameo.SetPhysicsProcess(false);
        cameo.Free();

        // (d) ボス戦の開始。_lunatic を立てて登場カットシーン（CameoIntroScene.PlayBoss）を経ずに
        //     SpawnBoss へ直に入る＝呼び出し行そのものを測る。
        Write(stage, "_lunatic", true);
        Write(stage, "_stepStarted", false);
        Moment($"{id} ボス戦の開始", player, hud, world, () => Call(stage, "Step_BossSpawn"));
        var boss = Read<Enemy>(stage, "_boss");
        Check(IsInstanceValid(boss), $"{id}: ボスが湧いている（測った節目が本物）");

        // (e) 第二形態（Enemy.AdvanceForm2）。変身演出のあいだは World ごと止まる＝絵の裏では滑らない。
        boss.SetPhysicsProcess(false);
        await Frames(4);
        Moment($"{id} 第二形態への切り替わり", player, hud, world,
            () => Check((bool)Call(boss, "AdvanceForm2")!, $"{id}: 第二形態が起きた（測った節目が本物）"),
            cinematic: "boss_transform");

        await Close(root);
    }

    // ───────── FINAL（ボス戦の開始／ミナの段替わり。残響の波は対象外）─────────
    private async Task Final(GameManager game)
    {
        var (root, stage, player, hud, world) = await Open(game, "MinaBattle");

        // 残響の波の継ぎ目は対象外＝FINAL の道中は意図して盤面を掃かず前の波と重ねる設計（StageMina.Step_Route）。
        // 生きた弾の中を自機を運ぶことになるので戻さない。
        NoMoment("FINAL 残響の波の継ぎ目", player, hud, () => Call(stage, "StartEchoWave"));
        var echo = Read<Spawner>(stage, "_echoSpawner");
        if (IsInstanceValid(echo)) { echo.Stop(); echo.Free(); }

        Write(stage, "_lunatic", true);
        Write(stage, "_stepStarted", false);
        Moment("FINAL ボス戦の開始", player, hud, world, () => Call(stage, "Step_BossSpawn"));
        var boss = Read<BossMina>(stage, "_boss");
        Check(IsInstanceValid(boss), "FINAL: ミナが湧いている（測った節目が本物）");
        boss.SetPhysicsProcess(false);
        await Frames(4);

        // 段（衣装）の切り替わり。従来難易度は段間カットシーン（MinaPhaseScene）が挟まる。
        Moment("FINAL ミナの段替わり", player, hud, world,
            () => Call(boss, "BeginPhaseTransition"), cinematic: "mina_phase_scene");
        Call(boss, "CompletePhaseTransition");

        // ルナティックの段替わりは段間カットシーンを出さない＝**弾を掃く経路が無い**。
        // ここが「掃いていない経路」の代表。帰還に重ねる無敵だけで被弾を断てていることを見る。
        game.Difficulty = GameManager.Diff.Lunatic;
        Check(GameManager.LunaticActive, "FINAL: ルナティックに切り替わった");
        Moment("FINAL ミナの段替わり（ルナティック＝弾を掃かない経路）", player, hud, world,
            () => Call(boss, "BeginPhaseTransition"));
        Check(GetTree().GetFirstNodeInGroup("mina_phase_scene") == null,
            "FINAL: ルナティックでは段間カットシーンが出ない（＝DespawnAll が走らない経路だった）");
        Check(boss.EncounterPhase == BossMina.DragonPhase,
            "FINAL: 龍形態＝ボス本体の位置ごと貼り直す段で自機も戻している");
        game.Difficulty = GameManager.Diff.Normal;

        await Close(root);
    }

    // ───────── 共通：節目を1つ測る ─────────
    //   自機をわざと Far（右下の隅）へ置いてから trigger で節目を起こし、
    //   予約 → 滑走 → 着地 → 無敵の余韻 → 余韻切れ までを1本で見る。await を挟まない＝途中に湧きや射撃が混ざらない。
    private void Moment(string label, Player player, Hud hud, Node2D world, Action trigger, string? cinematic = null)
    {
        hud.HoldBubble = false;
        hud.HideBubble();
        Check(!Hud.BubblePaused, $"{label}: 測定前は操作が戻っている");
        player.GlobalPosition = Far;
        Write(player, "_invincible", false);
        Write(player, "_invincibleTimer", 0f);
        Write(player, "_hitInvincible", false);
        int lives = player.Lives;

        trigger();

        Check(player.ReturningToStart, $"{label}: 帰還が予約される");
        Check(player.GlobalPosition.IsEqualApprox(Far), $"{label}: 予約した瞬間には動かない（裏でワープしない）");

        if (cinematic != null)
        {
            var scene = GetTree().GetFirstNodeInGroup(cinematic);
            Check(scene != null, $"{label}: 段の演出（{cinematic}）が立つ");
            // 「絵の裏で滑らない」を保証しているのは、演出が立った瞬間から同期的に効く Hud.BubblePaused
            //   （TickReturnHome のゲート）。World の ProcessMode 落としは演出によって効き方が違う
            //   （BossTransformation は物理コールバックの最中に呼ばれるので遅延させている＝この行の時点では
            //   まだ Inherit。src/fx/BossTransformation.cs の _Ready 参照）ので、await を挟まないこの関数は
            //   フラグではなく振る舞いを見る＝手回しで1フレーム送っても予約のまま動かないこと。
            Check(Hud.BubblePaused, $"{label}: 演出中は操作が預けられる");
            Tick(player, 1);
            Check(player.ReturningToStart && player.GlobalPosition.IsEqualApprox(Far),
                $"{label}: 演出のあいだ帰還は予約のまま待っている＝全画面の絵の裏で滑らない");
            scene!.Free();   // 読み終えた＝操作が戻る
            Check(!Hud.BubblePaused && world.ProcessMode != Node.ProcessModeEnum.Disabled,
                $"{label}: 演出明けで操作と World が戻る");
        }

        Tick(player, 1);
        Check(player.GlobalPosition.DistanceTo(Far) > 1f && !AtStart(player),
            $"{label}: 操作が戻った最初のフレームで滑り出す");
        Check(Read<bool>(player, "_invincible") && !Read<bool>(player, "_hitInvincible"),
            $"{label}: 滑走中は無敵（被弾由来ではない）");
        player.TakeHit();
        Check(player.Lives == lives, $"{label}: 滑走の最中に被弾しない");

        Tick(player, GlideFrames);
        Check(AtStart(player) && !player.ReturningToStart, $"{label}: 初期位置へ戻り切る");
        player.TakeHit();
        Check(player.Lives == lives, $"{label}: 着地直後も被弾しない");

        Tick(player, TailFrames);
        player.TakeHit();
        Check(player.Lives == lives - 1, $"{label}: 余韻が切れたら通常どおり被弾する（無敵は居座らない）");
        Property(player, "Lives", lives);
        Write(player, "_invincible", false);
        Write(player, "_invincibleTimer", 0f);
        Write(player, "_hitInvincible", false);
    }

    // 共通：対象外にした節目＝起こしても自機は動かないこと。
    private void NoMoment(string label, Player player, Hud hud, Action trigger)
    {
        hud.HoldBubble = false;
        hud.HideBubble();
        player.GlobalPosition = Far;
        trigger();
        Check(!player.ReturningToStart, $"{label}: 帰還を予約しない（対象外）");
        Tick(player, GlideFrames);
        Check(player.GlobalPosition.IsEqualApprox(Far), $"{label}: 自機はその場に留まる");
    }

    // ───────── 盤面の用意／片付け ─────────
    private async Task<(Node2D root, Node stage, Player player, Hud hud, Node2D world)> Open(GameManager game, string scene)
    {
        game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);              // ルートの色味・リトライ監視は止める
        var stage = Prop<Node>(root, "Stage");
        stage.SetProcess(false);             // step 機械は QA が手で進める
        var player = Prop<Player>(root, "Player");
        var hud = Prop<Hud>(root, "Hud");
        var world = Prop<Node2D>(root, "World");
        player.SetPhysicsProcess(false);     // 滑走は dt 固定で手回しする（ヘッドレスの可変フレームを排除）
        // FINAL は _Ready で World を止めて導入（Step_Lines）が起こす作法なので、step 機械を凍結した
        //   この QA では誰も起こさない＝段の演出が「止める前の値」として Disabled を覚えてしまう。
        //   戦闘が始まっている状態（本編で Step_Lines が起こした後）に揃えておく。
        world.ProcessMode = Node.ProcessModeEnum.Inherit;
        typeof(Pad).GetField("_usingMouse", Statics)!.SetValue(null, false);
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(2);
        Pool.DespawnAll();
        GD.Print($"[HOME] --- {scene} ---");
        return (root, stage, player, hud, world);
    }

    private async Task Close(Node2D root)
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemies")) n.QueueFree();
        root.QueueFree();
        await Frames(4);
        Pool.DespawnAll();
        await Frames(2);
    }

    private static void StopSpawner(Node stage)
    {
        var spawner = Read<Spawner>(stage, "_spawner");
        if (IsInstanceValid(spawner)) { spawner.Stop(); spawner.Free(); }
        Write(stage, "_spawner", null);
    }

    private static void KeyEvent(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
