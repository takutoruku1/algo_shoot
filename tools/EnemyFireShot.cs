using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// EnemyFireShot : 「敵弾がどこから出るか」を目で確かめるための比較スクショ（2026-09-28）。
//   作者報告「敵のたまが、アンチャーから少し離れた位置から予兆なくわいて攻撃している」に対し、
//   敵弾は必ず敵の中心（Enemy.ShotCenter）に湧き、そこから本来の発射点まで飛んでから
//   従来どおりの軌道へ移るようにした（Bullet.MakeLeadIn）。その前後を
//   「発射の瞬間から数フレームの連続コマ」として1枚に並べて保存する。
//     上段＝修正前：Bullet.CancelLeadIn で導入区間を捨てる＝発射点に直接湧いていた昔の挙動を再現
//     下段＝修正後：中心から飛び出して発射点へ入る
//   保存先 build/shots_enemyfire/<パターン>_compare.png（上下の間に1本の白線）。
//   ★撮影なので窓あり（--headless では Shot が固まる）で回す。
public partial class EnemyFireShot : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object o, string field) => (T)o.GetType().GetField(field, Private)!.GetValue(o)!;
    private static void Write(object o, string field, object value) => o.GetType().GetField(field, Private)!.SetValue(o, value);
    private static object? Call(object o, string method, params object[] args)
        => o.GetType().GetMethod(method, Private | BindingFlags.Public)!.Invoke(o, args);
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private Bullet[] Bullets() => Pool.GetChildren().OfType<Bullet>().Where(b => b.Active && b.IsEnemy).ToArray();

    // 撮影の枠。内部解像度 384×216 を窓 768×432＝2倍で描き、敵を中心に 140×140 を切り出す。
    private const int Zoom = 3;
    private const int CropW = 124, CropH = 124;   // 内部座標での切り出し（弾の広がりが全部入る大きさ）
    private const int Cols = 6;                   // 連続コマの枚数
    private const int StepFrames = 3;             // コマ間のフレーム数（6コマ×3＝発射から15フレーム＝0.25秒）
    private static readonly Vector2 Anchor = new Vector2(280f, 108f); // 敵を置く位置（盤面 120..384 の内側）

    public override async void _Ready()
    {
        try
        {
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(384 * Zoom, 216 * Zoom));
            await Frames(2);

            game.SelectedEntry = GameManager.StageEntry.Start;
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
            var world = root.GetNode<Node2D>("World");
            world.ProcessMode = ProcessModeEnum.Inherit;
            var player = world.GetNode<Player>("Player");
            player.SetPhysicsProcess(false);
            Write(player, "_invincible", true);
            Write(player, "_invincibleTimer", 999f);
            player.GlobalPosition = new Vector2(Field.Left + 30f, 170f);
            var hud = root.GetNode<Hud>("Hud");
            hud.HoldBubble = false;
            hud.HideBubble();
            Hud.BubblePaused = false;
            await Frames(3);
            // 見比べる物（体と弾）だけを残す：HUD は消し、背景は止めて前後の絵を揃える。
            hud.Visible = false;
            root.GetNode<StageBackground>("StageBackground").SetProcess(false);
            // 予告フラッシュ（FxLayer.AimFlash）は発射点で大きく光るので、弾そのものの位置が読めなくなる。
            // ここで見たいのは「弾がどこから出て、どこへ行ったか」だけなので、演出層は伏せる。
            if (FxLayer.Instance != null) FxLayer.Instance.Visible = false;
            await Frames(2);

            // 問題が出ていた種（体から離れた点に湧くもの）を全部撮る。
            // skip＝撮る前に捨てる斉射の数。荷物の人だけ、ずれが乗るのは2発目の斉射（偶奇で 10px 下がる）。
            foreach (var (theme, pattern, skip) in new[]
            {
                (StageTheme.Rei, AttackPattern.ReiClipper, 0),
                (StageTheme.Rei, AttackPattern.ReiMetrics, 0),
                (StageTheme.Akari, AttackPattern.AkariVacant, 0),
                (StageTheme.Koharu, AttackPattern.KoharuParcel, 1),
                (StageTheme.Mina, AttackPattern.MinaMemory, 0),
            })
            {
                var spec = EnemyTable.CharactersFor(theme).First(s => s.Pattern == pattern);
                using var before = await CaptureRow(world, spec, skip, leadIn: false);
                using var after = await CaptureRow(world, spec, skip, leadIn: true);
                Save(pattern.ToString(), before, after);
            }

            Audio.Instance?.StopMusic(0);
            await Frames(3);
            GD.Print("[EnemyFireShot] DONE");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[EnemyFireShot] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    // 1種ぶんの連続コマ（横1列）を作る。leadIn=false で修正前の挙動（発射点に直接湧く）を再現する。
    private async Task<Image> CaptureRow(Node2D world, EnemySpec spec, int skipSalvos, bool leadIn)
    {
        Pool.DespawnAll();
        var enemy = new MidEnemy();
        enemy.Configure(spec);
        world.AddChild(enemy);
        enemy.GlobalPosition = Anchor;
        enemy.SetPhysicsProcess(false);
        // 周回する盾（Panel）は体の真ん中を覆い隠す＝「どこから出たか」を見る邪魔になるので撮影中だけ伏せる。
        foreach (var panel in enemy.GetChildren().OfType<Panel>()) panel.Visible = false;
        // 「記憶の残響」は自分の足跡へ置きに行く種＝足跡を仕込まないと現在地へ落ちるだけになる。
        if (spec.Pattern == AttackPattern.MinaMemory)
        {
            var trail = Read<Vector2[]>(enemy, "_trail");
            for (int i = 0; i < trail.Length; i++)
                trail[i] = Anchor + new Vector2(-8f - i * 3.5f, 30f - i * 5f);
            Write(enemy, "_trailCount", trail.Length);
            Write(enemy, "_trailHead", 0);
        }
        await Frames(2);
        Call(enemy, "BeginCharacterAttack");
        for (int i = 0; i < skipSalvos; i++) { Call(enemy, "FireCharacterSalvo"); Pool.DespawnAll(); }
        Call(enemy, "FireCharacterSalvo");
        var bullets = Bullets();
        foreach (var b in bullets)
        {
            b.SetPhysicsProcess(false);       // コマ送りはこちらで握る（エンジンに二重に進められない）
            if (!leadIn) b.CancelLeadIn();    // 修正前＝導入区間なしで発射点に居る状態
        }
        GD.Print($"[EnemyFireShot] {spec.Pattern} {(leadIn ? "after " : "before")}: frame0 offsets from the body = "
            + string.Join(" / ", bullets.Select(b => $"{b.GlobalPosition.DistanceTo(enemy.ShotCenter):0.0}px")));

        var row = Image.CreateEmpty(CropW * Zoom * Cols, CropH * Zoom, false, Image.Format.Rgba8);
        var crop = new Rect2I((int)Anchor.X - CropW / 2, (int)Anchor.Y - CropH / 2, CropW, CropH);
        var screen = new Rect2I(crop.Position * Zoom, crop.Size * Zoom);
        for (int col = 0; col < Cols; col++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var frame = GetViewport().GetTexture().GetImage();
            frame.Convert(Image.Format.Rgba8);   // ビューポートは Rgb8 で返る＝並べる前に形式を合わせる
            row.BlitRect(frame, screen, new Vector2I(col * CropW * Zoom, 0));
            for (int step = 0; step < StepFrames; step++)
                foreach (var b in bullets) b._PhysicsProcess(1.0 / 60.0);
        }
        enemy.QueueFree();
        Pool.DespawnAll();
        await Frames(2);
        return row;
    }

    // 上段＝修正前／下段＝修正後 を白線1本で仕切って1枚にする。
    private static void Save(string name, Image before, Image after)
    {
        const int Rule = 2;
        int w = before.GetWidth(), h = before.GetHeight();
        var sheet = Image.CreateEmpty(w, h * 2 + Rule, false, Image.Format.Rgba8);
        sheet.Fill(new Color(1f, 1f, 1f, 1f));
        sheet.BlitRect(before, new Rect2I(0, 0, w, h), Vector2I.Zero);
        sheet.BlitRect(after, new Rect2I(0, 0, w, h), new Vector2I(0, h + Rule));
        string dir = ProjectSettings.GlobalizePath("res://build/shots_enemyfire");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        if (sheet.SavePng($"{dir}/{name}_compare.png") != Error.Ok)
            throw new Exception($"{name}: could not save the comparison sheet");
        if (before.SavePng($"{dir}/{name}_before.png") != Error.Ok
            || after.SavePng($"{dir}/{name}_after.png") != Error.Ok)
            throw new Exception($"{name}: could not save the single rows");
        GD.Print($"[EnemyFireShot] saved {name}_compare.png (top = before, bottom = after)");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
